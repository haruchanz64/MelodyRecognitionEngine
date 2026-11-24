using System;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;

namespace MelodyRecognitionEngine.DSP
{
    /// <summary>
    /// Converts a continuous stream of PitchFrames into discrete NoteEvents.
    /// Handles median smoothing, vibrato/glide hysteresis, stray-blip rejection,
    /// repeated same-pitch onset re-articulation, and inter-note silence timeouts.
    /// </summary>
    public sealed class NoteSegmenter
    {
        private DetectionProfileConfig _config;

        // Rolling median buffer (max 9 frames, allocation-free).
        private readonly float[] _medianHistory = new float[9];
        private readonly float[] _medianScratch = new float[9];
        private int _medianCount;
        private int _medianWriteIndex;

        // Candidate note stability tracking (for stray-blip rejection).
        private bool _hasCandidate;
        private int _candidateMidi;
        private double _candidateStartTime;
        private float _candidateMidiAccum;
        private float _candidateClarityAccum;
        private float _candidateRmsPeak;
        private int _candidateFrameCount;

        // Active confirmed note state.
        private bool _hasActiveNote;
        private NoteEvent _activeNote;
        private float _activeNotePeakRms;
        private float _activeNoteDipRms;
        private bool _rearticulationArmed;
        private int _rearticulationConfirmFrames;
        private double _lastOnsetTime;

        // Silence / dropout tracking.
        private double _unvoicedStartTime;
        private bool _isInUnvoicedGap;
        private bool _silenceTimeoutFired;
        private double _lastVoicedTime;

        /// <summary>Invoked once a candidate pitch stays stable for at least <see cref="DetectionProfileConfig.MinNoteDurationSeconds"/> or re-articulates.</summary>
        public event Action<NoteEvent> OnNoteStarted;
        /// <summary>Invoked on voiced frames while the current note stays within the pitch hysteresis band.</summary>
        public event Action<NoteEvent> OnNoteUpdated;
        /// <summary>Invoked when the active note closes due to silence, pitch transition, re-articulation, or <see cref="Flush"/>.</summary>
        public event Action<NoteEvent> OnNoteEnded;
        /// <summary>Invoked once after continuous unvoiced audio exceeds <see cref="DetectionProfileConfig.PhraseSilenceTimeoutSeconds"/>.</summary>
        public event Action OnSilenceTimeout;

        /// <summary>True while a confirmed note is currently sustained.</summary>
        public bool HasActiveNote => _hasActiveNote;
        /// <summary>Snapshot of the currently active note (valid when <see cref="HasActiveNote"/> is true).</summary>
        public NoteEvent ActiveNote => _activeNote;
        /// <summary>Active sanitized segmentation parameters.</summary>
        public DetectionProfileConfig Config => _config;

        /// <summary>
        /// Creates a note segmenter configured with sanitized thresholds from <paramref name="config"/>.
        /// </summary>
        public NoteSegmenter(DetectionProfileConfig config)
        {
            _config = config.Sanitized();
        }

        /// <summary>
        /// Applies a new <see cref="DetectionProfileConfig"/> and resets all smoothing and active-note state.
        /// </summary>
        public void Reconfigure(DetectionProfileConfig config)
        {
            _config = config.Sanitized();
            Reset();
        }

        /// <summary>
        /// Clears rolling median history, candidate tracking, active note state, and silence timers.
        /// </summary>
        public void Reset()
        {
            _medianCount = 0;
            _medianWriteIndex = 0;
            _hasCandidate = false;
            _candidateFrameCount = 0;
            _hasActiveNote = false;
            _activeNote = default;
            _activeNotePeakRms = 0f;
            _activeNoteDipRms = 0f;
            _rearticulationArmed = false;
            _rearticulationConfirmFrames = 0;
            _lastOnsetTime = -100.0;
            _isInUnvoicedGap = false;
            _unvoicedStartTime = 0.0;
            _silenceTimeoutFired = true; // Don't fire timeout before any note has ever occurred.
            _lastVoicedTime = 0.0;
        }

        /// <summary>
        /// Ingests the next chronological <see cref="PitchFrame"/> and emits note lifecycle or silence-timeout events as appropriate.
        /// </summary>
        public void Feed(PitchFrame frame)
        {
            double now = frame.TimestampSeconds;

            if (!frame.IsVoiced)
            {
                HandleUnvoicedFrame(now);
                return;
            }

            _isInUnvoicedGap = false;
            _lastVoicedTime = now;

            // Smooth incoming MIDI float via rolling median to suppress single-frame octave glitches & vibrato jitter.
            float smoothedMidi = PushAndComputeMedian(frame.MidiFloat);
            int nearestMidi = Mathf.RoundToInt(smoothedMidi);

            if (_hasActiveNote)
            {
                float semitoneDelta = Mathf.Abs(smoothedMidi - _activeNote.ExactMidiFloat);
                float rawSemitoneDelta = Mathf.Abs(frame.MidiFloat - _activeNote.ExactMidiFloat);

                // If raw pitch has already jumped away to a new note, immediately disarm same-pitch re-articulation
                // so a lagging median filter cannot emit a phantom repeat of the previous note.
                if (rawSemitoneDelta >= _config.NoteChangeHysteresisSemitones)
                {
                    _rearticulationArmed = false;
                    _rearticulationConfirmFrames = 0;
                }

                // Stay locked onto the active note while within hysteresis band (absorbs vocal vibrato!).
                if (semitoneDelta < _config.NoteChangeHysteresisSemitones)
                {
                    _hasCandidate = false;

                    // Update active note duration and gently track pitch center.
                    _activeNote.DurationSeconds = (float)Math.Max(0.0, now - _activeNote.StartTimeSeconds);
                    if (rawSemitoneDelta < _config.NoteChangeHysteresisSemitones)
                    {
                        _activeNote.ExactMidiFloat = Mathf.Lerp(_activeNote.ExactMidiFloat, smoothedMidi, 0.12f);
                        _activeNote.CentsOffset = (_activeNote.ExactMidiFloat - _activeNote.MidiNote) * 100f;
                        _activeNote.FrequencyHz = MusicalNote.MidiToFrequency(_activeNote.ExactMidiFloat, _config.ReferenceA4Hz);
                    }
                    _activeNote.Clarity = Mathf.Lerp(_activeNote.Clarity, frame.Clarity, 0.25f);
                    _activeNote.Rms = frame.Rms;

                    // Check for repeated same-pitch re-articulation only when raw pitch also agrees with active note.
                    if (rawSemitoneDelta < 0.48f && CheckSamePitchRearticulation(frame.Rms, now))
                    {
                        int repeatMidi = _activeNote.MidiNote;
                        CloseActiveNote(now);
                        EmitNewNote(
                            repeatMidi,
                            smoothedMidi,
                            frame.Clarity,
                            frame.Rms,
                            now,
                            isRearticulated: true);
                        return;
                    }

                    OnNoteUpdated?.Invoke(_activeNote);
                    return;
                }
            }

            // Either no active note, or pitch has moved outside hysteresis band -> accumulate candidate note.
            if (!_hasCandidate || Mathf.Abs(nearestMidi - _candidateMidi) > 0)
            {
                _hasCandidate = true;
                _candidateMidi = nearestMidi;
                _candidateStartTime = now;
                _candidateMidiAccum = smoothedMidi;
                _candidateClarityAccum = frame.Clarity;
                _candidateRmsPeak = frame.Rms;
                _candidateFrameCount = 1;
            }
            else
            {
                _candidateMidiAccum += smoothedMidi;
                _candidateClarityAccum += frame.Clarity;
                if (frame.Rms > _candidateRmsPeak) _candidateRmsPeak = frame.Rms;
                _candidateFrameCount++;
            }

            double candidateElapsed = now - _candidateStartTime;
            if (candidateElapsed >= _config.MinNoteDurationSeconds)
            {
                float avgMidi = _candidateMidiAccum / _candidateFrameCount;
                float avgClarity = _candidateClarityAccum / _candidateFrameCount;
                int confirmedMidi = Mathf.RoundToInt(avgMidi);

                if (_hasActiveNote)
                {
                    CloseActiveNote(_candidateStartTime);
                }

                EmitNewNote(
                    confirmedMidi,
                    avgMidi,
                    avgClarity,
                    _candidateRmsPeak,
                    _candidateStartTime,
                    isRearticulated: false);
                _hasCandidate = false;
            }
        }

        /// <summary>
        /// Forces closure of any currently sustained note (e.g. when stopping microphone capture).
        /// </summary>
        public void Flush(double timestampSeconds)
        {
            if (_hasActiveNote)
            {
                CloseActiveNote(timestampSeconds);
            }

            _hasCandidate = false;
            _medianCount = 0;
        }

        /// <summary>
        /// Advances unvoiced gap timers to cancel stray candidates, release the active note, or fire <see cref="OnSilenceTimeout"/>.
        /// </summary>
        private void HandleUnvoicedFrame(double now)
        {
            if (!_isInUnvoicedGap)
            {
                _isInUnvoicedGap = true;
                _unvoicedStartTime = now;
            }

            double gapDuration = now - _unvoicedStartTime;

            // Cancel unstable blip candidate as soon as sound drops out.
            if (gapDuration >= _config.NoteReleaseGapSeconds * 0.5f)
            {
                _hasCandidate = false;
            }

            // Close active note once release gap expires.
            if (_hasActiveNote && gapDuration >= _config.NoteReleaseGapSeconds)
            {
                CloseActiveNote(_unvoicedStartTime);
                _medianCount = 0;
            }

            // Emit phrase silence timeout once after last voiced audio.
            if (!_hasActiveNote && !_silenceTimeoutFired && (now - _lastVoicedTime) >= _config.PhraseSilenceTimeoutSeconds)
            {
                _silenceTimeoutFired = true;
                OnSilenceTimeout?.Invoke();
            }
        }

        /// <summary>
        /// Detects an RMS dip-then-rise envelope signature while pitch stays constant to split repeated same-pitch notes.
        /// </summary>
        private bool CheckSamePitchRearticulation(float currentRms, double now)
        {
            if (currentRms > _activeNotePeakRms)
            {
                _activeNotePeakRms = currentRms;
            }

            if (!_rearticulationArmed)
            {
                if (_activeNotePeakRms > 0f && currentRms <= _activeNotePeakRms * _config.RearticulationDipRatio)
                {
                    _rearticulationArmed = true;
                    _activeNoteDipRms = currentRms;
                    _rearticulationConfirmFrames = 0;
                }
            }
            else
            {
                if (currentRms < _activeNoteDipRms)
                {
                    _activeNoteDipRms = currentRms;
                    _rearticulationConfirmFrames = 0;
                }
                else if ((now - _lastOnsetTime) >= _config.MinRearticulationIntervalSeconds &&
                         currentRms >= _activeNoteDipRms * _config.RearticulationRiseMultiplier)
                {
                    _rearticulationConfirmFrames++;
                    if (_rearticulationConfirmFrames >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Initializes active note tracking state and invokes <see cref="OnNoteStarted"/>.
        /// </summary>
        private void EmitNewNote(int midiNote, float exactMidiFloat, float clarity, float rms, double startTime, bool isRearticulated)
        {
            _activeNote = new NoteEvent
            {
                MidiNote = midiNote,
                ExactMidiFloat = exactMidiFloat,
                FrequencyHz = MusicalNote.MidiToFrequency(exactMidiFloat, _config.ReferenceA4Hz),
                CentsOffset = (exactMidiFloat - midiNote) * 100f,
                Clarity = clarity,
                Rms = rms,
                StartTimeSeconds = startTime,
                DurationSeconds = _config.MinNoteDurationSeconds,
                IsRearticulated = isRearticulated,
                IsDiscreteInput = false
            };

            _hasActiveNote = true;
            _activeNotePeakRms = rms;
            _activeNoteDipRms = rms;
            _rearticulationArmed = false;
            _rearticulationConfirmFrames = 0;
            _lastOnsetTime = startTime;
            _silenceTimeoutFired = false;

            OnNoteStarted?.Invoke(_activeNote);
        }

        /// <summary>
        /// Finalizes the active note's duration, clears active state, and invokes <see cref="OnNoteEnded"/>.
        /// </summary>
        private void CloseActiveNote(double endTime)
        {
            if (!_hasActiveNote) return;

            _activeNote.DurationSeconds = (float)Math.Max(_activeNote.DurationSeconds, endTime - _activeNote.StartTimeSeconds);
            NoteEvent completed = _activeNote;
            _hasActiveNote = false;
            _rearticulationArmed = false;
            _rearticulationConfirmFrames = 0;
            OnNoteEnded?.Invoke(completed);
        }

        /// <summary>
        /// Appends <paramref name="value"/> to the circular median window and returns the median without heap allocations.
        /// </summary>
        private float PushAndComputeMedian(float value)
        {
            int window = Mathf.Clamp(_config.MedianWindowFrames, 1, _medianHistory.Length);
            _medianHistory[_medianWriteIndex] = value;
            _medianWriteIndex = (_medianWriteIndex + 1) % window;
            if (_medianCount < window) _medianCount++;

            if (_medianCount == 1) return value;

            for (int i = 0; i < _medianCount; i++)
            {
                _medianScratch[i] = _medianHistory[i];
            }

            // Insertion sort on <= 9 elements (zero allocations).
            for (int i = 1; i < _medianCount; i++)
            {
                float key = _medianScratch[i];
                int j = i - 1;
                while (j >= 0 && _medianScratch[j] > key)
                {
                    _medianScratch[j + 1] = _medianScratch[j];
                    j--;
                }
                _medianScratch[j + 1] = key;
            }

            return _medianScratch[_medianCount / 2];
        }
    }
}
