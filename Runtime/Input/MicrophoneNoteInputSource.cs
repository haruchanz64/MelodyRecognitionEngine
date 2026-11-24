using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.DSP;
using UnityEngine;

namespace MelodyRecognitionEngine.Input
{
    /// <summary>
    /// Bridges an IAudioCaptureSource through BurstPitchTracker and NoteSegmenter
    /// to expose a unified INoteInputSource stream with ambient noise-floor calibration.
    /// </summary>
    public sealed class MicrophoneNoteInputSource : INoteInputSource, IDisposable
    {
        private readonly IAudioCaptureSource _captureSource;
        private readonly bool _ownsCaptureSource;
        private readonly BurstPitchTracker _pitchTracker;
        private readonly NoteSegmenter _segmenter;

        private readonly float[] _readBuffer = new float[8192];
        private readonly List<PitchFrame> _frameScratch = new List<PitchFrame>(16);

        private bool _isActive;
        private bool _isCalibratingAmbient;
        private double _calibrationEndTime;
        private float _calibrationRmsSum;
        private int _calibrationFrameCount;
        private float _latestRms;
        private PitchFrame _latestPitchFrame;
        private bool _disposed;

        /// <inheritdoc/>
        public event Action<NoteEvent> OnNoteDetected;
        /// <inheritdoc/>
        public event Action<NoteEvent> OnNoteUpdated;
        /// <inheritdoc/>
        public event Action<NoteEvent> OnNoteEnded;
        /// <inheritdoc/>
        public event Action OnSilenceTimeout;
        /// <inheritdoc/>
        public event Action<PitchFrame> OnPitchFrame;
        /// <summary>Invoked when an ambient noise calibration pass finishes, passing the measured average RMS floor.</summary>
        public event Action<float> OnAmbientCalibrationCompleted;

        /// <inheritdoc/>
        public bool IsActive => _isActive;
        /// <summary>True while sampling ambient room noise instead of feeding frames to the note segmenter.</summary>
        public bool IsCalibratingAmbient => _isCalibratingAmbient;
        /// <summary>Current calibrated ambient noise floor RMS applied to the pitch tracker.</summary>
        public float AmbientNoiseFloorRms => _pitchTracker.AmbientNoiseFloorRms;
        /// <summary>Effective RMS noise gate threshold combining profile floor and scaled ambient noise.</summary>
        public float EffectiveNoiseGateRms => _pitchTracker.EffectiveNoiseGateRms;
        /// <summary>Most recent bandpass-filtered frame RMS amplitude.</summary>
        public float LatestRms => _latestRms;
        /// <summary>Most recent <see cref="PitchFrame"/> produced by the DSP pipeline.</summary>
        public PitchFrame LatestPitchFrame => _latestPitchFrame;
        /// <summary>Underlying Burst pitch tracker owned by this source.</summary>
        public BurstPitchTracker PitchTracker => _pitchTracker;
        /// <summary>Underlying note segmenter converting pitch frames into note events.</summary>
        public NoteSegmenter Segmenter => _segmenter;
        /// <summary>Underlying PCM audio capture source.</summary>
        public IAudioCaptureSource CaptureSource => _captureSource;

        /// <summary>
        /// Creates a unified note input pipeline around <paramref name="captureSource"/> using <paramref name="profile"/>.
        /// </summary>
        /// <param name="captureSource">PCM audio source supplying mono samples.</param>
        /// <param name="profile">Initial acoustic DSP and segmentation profile.</param>
        /// <param name="ownsCaptureSource">When true, <see cref="Dispose"/> also disposes <paramref name="captureSource"/>.</param>
        public MicrophoneNoteInputSource(IAudioCaptureSource captureSource, DetectionProfileConfig profile, bool ownsCaptureSource = true)
        {
            _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
            _ownsCaptureSource = ownsCaptureSource;

            DetectionProfileConfig clean = profile.Sanitized();
            _pitchTracker = new BurstPitchTracker(clean, Mathf.Max(8000, captureSource.SampleRate));
            _segmenter = new NoteSegmenter(clean);

            _segmenter.OnNoteStarted += HandleNoteStarted;
            _segmenter.OnNoteUpdated += HandleNoteUpdated;
            _segmenter.OnNoteEnded += HandleNoteEnded;
            _segmenter.OnSilenceTimeout += HandleSilenceTimeout;
        }

        /// <summary>
        /// Hot-swaps the DSP pitch tracker and note segmenter to a new <see cref="DetectionProfileConfig"/>.
        /// </summary>
        public void ApplyProfile(DetectionProfileConfig profile)
        {
            DetectionProfileConfig clean = profile.Sanitized();
            _pitchTracker.Reconfigure(clean, Mathf.Max(8000, _captureSource.SampleRate));
            _segmenter.Reconfigure(clean);
        }

        /// <summary>
        /// Starts the underlying capture source, synchronizes the tracker sample rate, and resets DSP state.
        /// </summary>
        /// <returns>True if capture started or was already active; false if disposed or capture failed.</returns>
        public bool Start()
        {
            if (_disposed) return false;
            if (_isActive) return true;

            if (!_captureSource.StartCapture())
            {
                return false;
            }

            _pitchTracker.Reconfigure(_pitchTracker.Config, _captureSource.SampleRate);
            _pitchTracker.Reset();
            _segmenter.Reset();
            _isActive = true;
            return true;
        }

        /// <summary>
        /// Flushes any active note, cancels ambient calibration, and stops the underlying audio capture source.
        /// </summary>
        public void Stop()
        {
            if (!_isActive) return;

            _isActive = false;
            _isCalibratingAmbient = false;
            _segmenter.Flush(_pitchTracker.CurrentStreamTimeSeconds);
            _captureSource.StopCapture();
        }

        /// <summary>
        /// Measures room noise floor over durationSeconds to dynamically raise the noise gate above background hum/fans.
        /// </summary>
        /// <param name="durationSeconds">Sampling window in seconds (clamped to [0.2, 5.0]).</param>
        public void BeginAmbientNoiseCalibration(float durationSeconds = 1.0f)
        {
            _isCalibratingAmbient = true;
            _calibrationEndTime = _pitchTracker.CurrentStreamTimeSeconds + Mathf.Clamp(durationSeconds, 0.2f, 5f);
            _calibrationRmsSum = 0f;
            _calibrationFrameCount = 0;
        }

        /// <summary>
        /// Manually sets the ambient noise floor RMS (clamped to non-negative values) without running live calibration.
        /// </summary>
        public void SetAmbientNoiseFloor(float rms)
        {
            _pitchTracker.AmbientNoiseFloorRms = Mathf.Max(0f, rms);
        }

        /// <summary>
        /// Pumps new audio samples from the capture source through the Burst pitch tracker and note segmenter.
        /// </summary>
        /// <param name="useAsyncLatestJob">When true, schedules the final hop asynchronously to overlap work with the next frame.</param>
        /// <param name="maxSamplesToRead">Upper bound on samples read per call (clamped to internal buffer size 8192).</param>
        /// <returns>Number of completed <see cref="PitchFrame"/> instances processed in this call.</returns>
        public int Pump(bool useAsyncLatestJob = true, int maxSamplesToRead = 8192)
        {
            if (_disposed || !_isActive) return 0;

            int samplesToRequest = Mathf.Clamp(maxSamplesToRead, 1, _readBuffer.Length);
            int readCount = _captureSource.ReadSamples(_readBuffer, samplesToRequest);

            _frameScratch.Clear();
            if (readCount > 0)
            {
                _pitchTracker.PushSamplesAsync(_readBuffer, readCount, _frameScratch, useAsyncLatestJob);
            }
            else if (_pitchTracker.CompletePendingFrame(out PitchFrame completed))
            {
                _frameScratch.Add(completed);
            }

            for (int i = 0; i < _frameScratch.Count; i++)
            {
                ProcessPitchFrame(_frameScratch[i]);
            }

            return _frameScratch.Count;
        }

        /// <summary>
        /// Synchronously drains an entire finite capture source (such as an AudioClipCaptureSource) in fixed chunks.
        /// Ideal for tests and Editor dry-run simulation.
        /// </summary>
        /// <param name="chunkSize">Number of samples read per iteration (clamped to [128, 8192]).</param>
        /// <returns>Total number of <see cref="PitchFrame"/> instances analyzed.</returns>
        public int DrainAllSynchronous(int chunkSize = 1024)
        {
            if (_disposed) return 0;
            if (!_isActive && !Start()) return 0;

            int totalFrames = 0;
            int safety = 0;
            int step = Mathf.Clamp(chunkSize, 128, _readBuffer.Length);

            while (_captureSource.IsCapturing && safety++ < 200000)
            {
                int read = _captureSource.ReadSamples(_readBuffer, step);
                if (read <= 0) break;

                _frameScratch.Clear();
                _pitchTracker.ProcessSamples(_readBuffer, read, _frameScratch);
                for (int i = 0; i < _frameScratch.Count; i++)
                {
                    ProcessPitchFrame(_frameScratch[i]);
                }

                totalFrames += _frameScratch.Count;
            }

            _segmenter.Flush(_pitchTracker.CurrentStreamTimeSeconds);
            return totalFrames;
        }

        /// <summary>
        /// Updates cached telemetry, emits <see cref="OnPitchFrame"/>, and either accumulates ambient calibration RMS or feeds <see cref="NoteSegmenter"/>.
        /// </summary>
        private void ProcessPitchFrame(PitchFrame frame)
        {
            _latestPitchFrame = frame;
            _latestRms = frame.Rms;
            OnPitchFrame?.Invoke(frame);

            if (_isCalibratingAmbient)
            {
                _calibrationRmsSum += frame.Rms;
                _calibrationFrameCount++;
                if (frame.TimestampSeconds >= _calibrationEndTime)
                {
                    _isCalibratingAmbient = false;
                    float avgAmbient = _calibrationFrameCount > 0 ? _calibrationRmsSum / _calibrationFrameCount : 0f;
                    _pitchTracker.AmbientNoiseFloorRms = avgAmbient;
                    OnAmbientCalibrationCompleted?.Invoke(avgAmbient);
                }
                return;
            }

            _segmenter.Feed(frame);
        }

        private void HandleNoteStarted(NoteEvent note) => OnNoteDetected?.Invoke(note);
        private void HandleNoteUpdated(NoteEvent note) => OnNoteUpdated?.Invoke(note);
        private void HandleNoteEnded(NoteEvent note) => OnNoteEnded?.Invoke(note);
        private void HandleSilenceTimeout() => OnSilenceTimeout?.Invoke();

        /// <summary>
        /// Stops capture, disposes the Burst pitch tracker's native buffers, and disposes the capture source if owned.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _pitchTracker.Dispose();
            if (_ownsCaptureSource)
            {
                _captureSource.Dispose();
            }
            _disposed = true;
        }
    }
}
