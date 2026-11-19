using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MelodyRecognitionEngine.DSP
{
    /// <summary>
    /// Allocation-free manager for Burst-compiled pitch analysis.
    /// Maintains a persistent sliding PCM ring buffer and NativeArray scratch buffers,
    /// supporting both asynchronous Job scheduling at runtime and synchronous execution for tests/offline analysis.
    /// </summary>
    public sealed class BurstPitchTracker : IDisposable
    {
        private DetectionProfileConfig _config;
        private int _sampleRate;
        private float _ambientNoiseFloorRms;

        private float[] _ringBuffer;
        private int _ringCapacity;
        private int _ringWritePos;
        private int _ringFilledCount;
        private int _samplesSinceLastHop;
        private long _totalSamplesProcessed;

        private NativeArray<float> _jobInput;
        private NativeArray<float> _jobFiltered;
        private NativeArray<float> _jobCmnd;
        private NativeArray<PitchFrame> _jobOutput;

        private bool _hasPendingJob;
        private JobHandle _pendingHandle;
        private bool _disposed;

        public DetectionProfileConfig Config => _config;
        public int SampleRate => _sampleRate;
        public float AmbientNoiseFloorRms
        {
            get => _ambientNoiseFloorRms;
            set => _ambientNoiseFloorRms = Mathf.Max(0f, value);
        }

        public float EffectiveNoiseGateRms => Mathf.Max(_config.NoiseGateRms, _ambientNoiseFloorRms * _config.NoiseFloorMultiplier);
        public double CurrentStreamTimeSeconds => _sampleRate > 0 ? (double)_totalSamplesProcessed / _sampleRate : 0.0;
        public bool HasPendingJob => _hasPendingJob;

        public BurstPitchTracker(DetectionProfileConfig config, int sampleRate = 44100)
        {
            _sampleRate = Mathf.Max(8000, sampleRate);
            _config = config.Sanitized();
            AllocateBuffers();
        }

        public void Reconfigure(DetectionProfileConfig config, int sampleRate)
        {
            CompletePendingInternal();
            DetectionProfileConfig sanitized = config.Sanitized();
            int validRate = Mathf.Max(8000, sampleRate);
            bool needsRealloc = sanitized.FrameSize != _config.FrameSize || _ringBuffer == null;

            _config = sanitized;
            _sampleRate = validRate;

            if (needsRealloc)
            {
                DisposeNativeArrays();
                AllocateBuffers();
            }
        }

        public void Reset()
        {
            CompletePendingInternal();
            if (_ringBuffer != null)
            {
                Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
            }

            _ringWritePos = 0;
            _ringFilledCount = 0;
            _samplesSinceLastHop = 0;
            _totalSamplesProcessed = 0;
        }

        /// <summary>
        /// Pushes PCM samples into the ring buffer and runs Burst jobs synchronously for every completed hop.
        /// Ideal for EditMode tests, offline AudioClip scanning, and low-latency synchronous pumps.
        /// </summary>
        public int ProcessSamples(float[] samples, int count, List<PitchFrame> outFrames)
        {
            if (_disposed || samples == null || count <= 0)
            {
                return 0;
            }

            if (CompletePendingFrame(out PitchFrame pending))
            {
                outFrames?.Add(pending);
            }

            int emitted = 0;
            int clamped = Mathf.Min(count, samples.Length);
            int frameSize = _config.FrameSize;
            int hopSize = _config.HopSize;

            for (int i = 0; i < clamped; i++)
            {
                _ringBuffer[_ringWritePos] = samples[i];
                _ringWritePos = (_ringWritePos + 1) % _ringCapacity;
                if (_ringFilledCount < _ringCapacity)
                {
                    _ringFilledCount++;
                }

                _totalSamplesProcessed++;
                _samplesSinceLastHop++;

                if (_ringFilledCount >= frameSize && _samplesSinceLastHop >= hopSize)
                {
                    _samplesSinceLastHop -= hopSize;
                    PitchFrame frame = RunSingleFrameSync();
                    outFrames?.Add(frame);
                    emitted++;
                }
            }

            return emitted;
        }

        /// <summary>
        /// Pushes PCM samples into the ring buffer and schedules up to one async Burst job for the latest frame
        /// while synchronously draining any earlier hops if multiple hops arrived in one Update().
        /// </summary>
        public void PushSamplesAsync(float[] samples, int count, List<PitchFrame> completedFrames, bool useAsyncLatestFrame = true)
        {
            if (_disposed || samples == null || count <= 0)
            {
                return;
            }

            if (CompletePendingFrame(out PitchFrame prev))
            {
                completedFrames?.Add(prev);
            }

            int clamped = Mathf.Min(count, samples.Length);
            int frameSize = _config.FrameSize;
            int hopSize = _config.HopSize;

            // Count how many hops will trigger so only the final hop runs asynchronously across frames.
            int remainingHops = 0;
            int projectedFilled = _ringFilledCount;
            int projectedSinceHop = _samplesSinceLastHop;
            for (int i = 0; i < clamped; i++)
            {
                if (projectedFilled < _ringCapacity) projectedFilled++;
                projectedSinceHop++;
                if (projectedFilled >= frameSize && projectedSinceHop >= hopSize)
                {
                    projectedSinceHop -= hopSize;
                    remainingHops++;
                }
            }

            int hopIndex = 0;
            for (int i = 0; i < clamped; i++)
            {
                _ringBuffer[_ringWritePos] = samples[i];
                _ringWritePos = (_ringWritePos + 1) % _ringCapacity;
                if (_ringFilledCount < _ringCapacity)
                {
                    _ringFilledCount++;
                }

                _totalSamplesProcessed++;
                _samplesSinceLastHop++;

                if (_ringFilledCount >= frameSize && _samplesSinceLastHop >= hopSize)
                {
                    _samplesSinceLastHop -= hopSize;
                    hopIndex++;

                    if (useAsyncLatestFrame && hopIndex == remainingHops)
                    {
                        ScheduleSingleFrameAsync();
                    }
                    else
                    {
                        PitchFrame frame = RunSingleFrameSync();
                        completedFrames?.Add(frame);
                    }
                }
            }
        }

        /// <summary>
        /// Completes any previously scheduled async Burst job and returns its PitchFrame.
        /// </summary>
        public bool CompletePendingFrame(out PitchFrame frame)
        {
            if (!_hasPendingJob || _disposed)
            {
                frame = default;
                return false;
            }

            _pendingHandle.Complete();
            _hasPendingJob = false;
            frame = _jobOutput[0];
            return true;
        }

        /// <summary>
        /// Directly analyzes a single standalone frame buffer synchronously (length >= FrameSize).
        /// </summary>
        public PitchFrame AnalyzeFrameDirect(float[] frameSamples, double timestampSeconds = 0.0)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BurstPitchTracker));
            }

            CompletePendingInternal();
            int len = Mathf.Min(_config.FrameSize, frameSamples.Length);
            for (int i = 0; i < len; i++)
            {
                _jobInput[i] = frameSamples[i];
            }
            for (int i = len; i < _config.FrameSize; i++)
            {
                _jobInput[i] = 0f;
            }

            PitchDetectionJob job = BuildJob(timestampSeconds);
            job.Run();
            return _jobOutput[0];
        }

        private PitchFrame RunSingleFrameSync()
        {
            CopyLatestFrameToNative();
            PitchDetectionJob job = BuildJob(CurrentStreamTimeSeconds);
            job.Run();
            return _jobOutput[0];
        }

        private void ScheduleSingleFrameAsync()
        {
            CopyLatestFrameToNative();
            PitchDetectionJob job = BuildJob(CurrentStreamTimeSeconds);
            _pendingHandle = job.Schedule();
            _hasPendingJob = true;
        }

        private void CopyLatestFrameToNative()
        {
            int frameSize = _config.FrameSize;
            int start = (_ringWritePos - frameSize + _ringCapacity) % _ringCapacity;
            for (int i = 0; i < frameSize; i++)
            {
                _jobInput[i] = _ringBuffer[(start + i) % _ringCapacity];
            }
        }

        private PitchDetectionJob BuildJob(double timestampSeconds)
        {
            return new PitchDetectionJob
            {
                InputSamples = _jobInput,
                FilteredSamples = _jobFiltered,
                CmndBuffer = _jobCmnd,
                Output = _jobOutput,
                FrameSize = _config.FrameSize,
                SampleRate = _sampleRate,
                MinFrequencyHz = _config.MinFrequencyHz,
                MaxFrequencyHz = _config.MaxFrequencyHz,
                ReferenceA4Hz = _config.ReferenceA4Hz,
                EffectiveNoiseGateRms = EffectiveNoiseGateRms,
                YinThreshold = _config.YinThreshold,
                MinClarity = _config.MinClarity,
                MaxSpectralFlatness = _config.MaxSpectralFlatness,
                EnableOctaveCorrection = (byte)(_config.EnableOctaveCorrection ? 1 : 0),
                TimestampSeconds = timestampSeconds
            };
        }

        private void AllocateBuffers()
        {
            int frameSize = _config.FrameSize;
            _ringCapacity = frameSize * 2;
            _ringBuffer = new float[_ringCapacity];
            _ringWritePos = 0;
            _ringFilledCount = 0;
            _samplesSinceLastHop = 0;

            _jobInput = new NativeArray<float>(frameSize, Allocator.Persistent);
            _jobFiltered = new NativeArray<float>(frameSize, Allocator.Persistent);
            _jobCmnd = new NativeArray<float>((frameSize / 2) + 2, Allocator.Persistent);
            _jobOutput = new NativeArray<PitchFrame>(1, Allocator.Persistent);
        }

        private void CompletePendingInternal()
        {
            if (_hasPendingJob)
            {
                _pendingHandle.Complete();
                _hasPendingJob = false;
            }
        }

        private void DisposeNativeArrays()
        {
            if (_jobInput.IsCreated) _jobInput.Dispose();
            if (_jobFiltered.IsCreated) _jobFiltered.Dispose();
            if (_jobCmnd.IsCreated) _jobCmnd.Dispose();
            if (_jobOutput.IsCreated) _jobOutput.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            CompletePendingInternal();
            DisposeNativeArrays();
            _disposed = true;
        }
    }
}
