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

        /// <summary>Active sanitized detection profile driving framing, gating, and YIN thresholds.</summary>
        public DetectionProfileConfig Config => _config;
        /// <summary>Active audio stream sample rate in Hz.</summary>
        public int SampleRate => _sampleRate;
        /// <summary>Calibrated ambient room noise floor RMS (clamped to non-negative values).</summary>
        public float AmbientNoiseFloorRms
        {
            get => _ambientNoiseFloorRms;
            set => _ambientNoiseFloorRms = Mathf.Max(0f, value);
        }

        /// <summary>Effective RMS threshold applied by <see cref="PitchDetectionJob"/>, taking the greater of the profile floor and scaled ambient noise.</summary>
        public float EffectiveNoiseGateRms => Mathf.Max(_config.NoiseGateRms, _ambientNoiseFloorRms * _config.NoiseFloorMultiplier);
        /// <summary>Elapsed stream time in seconds derived from total ingested samples and <see cref="SampleRate"/>.</summary>
        public double CurrentStreamTimeSeconds => _sampleRate > 0 ? (double)_totalSamplesProcessed / _sampleRate : 0.0;
        /// <summary>True when an asynchronous <see cref="PitchDetectionJob"/> is currently scheduled and not yet completed.</summary>
        public bool HasPendingJob => _hasPendingJob;

        /// <summary>
        /// Allocates persistent ring and native scratch buffers for pitch analysis at the given sample rate.
        /// </summary>
        /// <param name="config">DSP configuration (sanitized before buffer allocation).</param>
        /// <param name="sampleRate">Audio stream sample rate in Hz (clamped to at least 8000).</param>
        public BurstPitchTracker(DetectionProfileConfig config, int sampleRate = 44100)
        {
            _sampleRate = Mathf.Max(8000, sampleRate);
            _config = config.Sanitized();
            AllocateBuffers();
        }

        /// <summary>
        /// Updates DSP parameters and sample rate, completing any pending job and reallocating native buffers only if <see cref="DetectionProfileConfig.FrameSize"/> changed.
        /// </summary>
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

        /// <summary>
        /// Completes any pending job and clears the PCM ring buffer and sample counters without freeing native memory.
        /// </summary>
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
        /// <param name="samples">Input mono PCM samples in [-1, 1].</param>
        /// <param name="count">Number of valid samples to ingest from <paramref name="samples"/>.</param>
        /// <param name="outFrames">Optional list receiving emitted <see cref="PitchFrame"/> results.</param>
        /// <returns>Number of new frames analyzed synchronously during this call.</returns>
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
        /// <param name="samples">Input mono PCM samples in [-1, 1].</param>
        /// <param name="count">Number of valid samples to ingest from <paramref name="samples"/>.</param>
        /// <param name="completedFrames">Optional list receiving the previously pending frame and any synchronously drained hops.</param>
        /// <param name="useAsyncLatestFrame">When true, schedules the final hop via <see cref="IJobExtensions.Schedule{T}"/> instead of running inline.</param>
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
        /// <param name="frame">Receives the completed pitch frame, or <c>default</c> when no job was pending.</param>
        /// <returns>True if a pending job was completed and returned.</returns>
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
        /// Directly analyzes a single standalone frame buffer synchronously (zero-padded if shorter than FrameSize).
        /// </summary>
        /// <param name="frameSamples">Mono PCM frame samples (expected length at least <see cref="DetectionProfileConfig.FrameSize"/>).</param>
        /// <param name="timestampSeconds">Timestamp assigned to the resulting <see cref="PitchFrame"/>.</param>
        /// <returns>Analyzed pitch frame.</returns>
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

        /// <summary>Copies the most recent window from the ring buffer and executes <see cref="PitchDetectionJob"/> synchronously.</summary>
        private PitchFrame RunSingleFrameSync()
        {
            CopyLatestFrameToNative();
            PitchDetectionJob job = BuildJob(CurrentStreamTimeSeconds);
            job.Run();
            return _jobOutput[0];
        }

        /// <summary>Copies the most recent window from the ring buffer and schedules <see cref="PitchDetectionJob"/> asynchronously.</summary>
        private void ScheduleSingleFrameAsync()
        {
            CopyLatestFrameToNative();
            PitchDetectionJob job = BuildJob(CurrentStreamTimeSeconds);
            _pendingHandle = job.Schedule();
            _hasPendingJob = true;
        }

        /// <summary>Unwraps the latest <see cref="DetectionProfileConfig.FrameSize"/> samples from the circular buffer into <see cref="_jobInput"/>.</summary>
        private void CopyLatestFrameToNative()
        {
            int frameSize = _config.FrameSize;
            int start = (_ringWritePos - frameSize + _ringCapacity) % _ringCapacity;
            for (int i = 0; i < frameSize; i++)
            {
                _jobInput[i] = _ringBuffer[(start + i) % _ringCapacity];
            }
        }

        /// <summary>Populates a <see cref="PitchDetectionJob"/> struct bound to this tracker's persistent native buffers.</summary>
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

        /// <summary>Allocates the managed circular buffer and persistent native arrays sized for the current frame length.</summary>
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

        /// <summary>Blocks until any in-flight Burst job finishes so native buffers can be safely reused or disposed.</summary>
        private void CompletePendingInternal()
        {
            if (_hasPendingJob)
            {
                _pendingHandle.Complete();
                _hasPendingJob = false;
            }
        }

        /// <summary>Disposes all allocated persistent <see cref="NativeArray{T}"/> scratch buffers.</summary>
        private void DisposeNativeArrays()
        {
            if (_jobInput.IsCreated) _jobInput.Dispose();
            if (_jobFiltered.IsCreated) _jobFiltered.Dispose();
            if (_jobCmnd.IsCreated) _jobCmnd.Dispose();
            if (_jobOutput.IsCreated) _jobOutput.Dispose();
        }

        /// <summary>
        /// Completes any pending Burst job and frees all persistent native buffers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            CompletePendingInternal();
            DisposeNativeArrays();
            _disposed = true;
        }
    }
}
