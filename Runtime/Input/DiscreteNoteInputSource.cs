using System;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Input
{
    /// <summary>
    /// Discrete note input provider for controller buttons, keyboard keys, on-screen virtual keys,
    /// or automated tests. Emits the exact same INoteInputSource events as microphone DSP capture.
    /// </summary>
    public sealed class DiscreteNoteInputSource : INoteInputSource
    {
        private bool _isActive = true;
        private bool _hasActiveNote;
        private NoteEvent _activeNote;
        private double _lastActivityTime = -100.0;
        private bool _silenceTimeoutFired = true;
        private float _silenceTimeoutSeconds = 2.6f;

        public event Action<NoteEvent> OnNoteDetected;
        public event Action<NoteEvent> OnNoteUpdated;
        public event Action<NoteEvent> OnNoteEnded;
        public event Action OnSilenceTimeout;
        public event Action<PitchFrame> OnPitchFrame;

        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        public float SilenceTimeoutSeconds
        {
            get => _silenceTimeoutSeconds;
            set => _silenceTimeoutSeconds = Mathf.Clamp(value, 0.3f, 15f);
        }

        public void PressNote(int midiNote, double timestampSeconds, float velocity = 0.85f, float referenceA4Hz = MusicalNote.DefaultReferenceA4Hz)
        {
            if (!_isActive) return;

            if (_hasActiveNote)
            {
                ReleaseActiveNote(timestampSeconds);
            }

            int clampedMidi = Mathf.Clamp(midiNote, 0, 127);
            float freq = MusicalNote.MidiToFrequency(clampedMidi, referenceA4Hz);

            _activeNote = new NoteEvent
            {
                MidiNote = clampedMidi,
                ExactMidiFloat = clampedMidi,
                FrequencyHz = freq,
                CentsOffset = 0f,
                Clarity = 1f,
                Rms = Mathf.Clamp01(velocity),
                StartTimeSeconds = timestampSeconds,
                DurationSeconds = 0.05f,
                IsRearticulated = false,
                IsDiscreteInput = true
            };

            _hasActiveNote = true;
            _lastActivityTime = timestampSeconds;
            _silenceTimeoutFired = false;

            OnPitchFrame?.Invoke(new PitchFrame
            {
                FrequencyHz = freq,
                MidiFloat = clampedMidi,
                NearestMidiNote = clampedMidi,
                CentsOffset = 0f,
                Rms = _activeNote.Rms,
                Clarity = 1f,
                SpectralFlatness = 0f,
                TimestampSeconds = timestampSeconds,
                IsVoicedByte = 1
            });

            OnNoteDetected?.Invoke(_activeNote);
        }

        public void ReleaseActiveNote(double timestampSeconds)
        {
            if (!_hasActiveNote) return;

            _activeNote.DurationSeconds = (float)Math.Max(0.05, timestampSeconds - _activeNote.StartTimeSeconds);
            NoteEvent completed = _activeNote;
            _hasActiveNote = false;
            _lastActivityTime = timestampSeconds;

            OnPitchFrame?.Invoke(new PitchFrame
            {
                TimestampSeconds = timestampSeconds,
                IsVoicedByte = 0
            });

            OnNoteEnded?.Invoke(completed);
        }

        /// <summary>
        /// Triggers an instant tap note (Press followed immediately by Release with nominal duration).
        /// </summary>
        public void TriggerNoteTap(int midiNote, double timestampSeconds, float nominalDurationSeconds = 0.25f)
        {
            PressNote(midiNote, timestampSeconds);
            if (_hasActiveNote)
            {
                _activeNote.DurationSeconds = Mathf.Max(0.05f, nominalDurationSeconds);
                NoteEvent completed = _activeNote;
                _hasActiveNote = false;
                _lastActivityTime = timestampSeconds;
                OnNoteEnded?.Invoke(completed);
            }
        }

        public void Tick(double currentTimeSeconds)
        {
            if (!_isActive) return;

            if (_hasActiveNote)
            {
                _activeNote.DurationSeconds = (float)Math.Max(0.05, currentTimeSeconds - _activeNote.StartTimeSeconds);
                OnNoteUpdated?.Invoke(_activeNote);
                return;
            }

            if (!_silenceTimeoutFired && (currentTimeSeconds - _lastActivityTime) >= _silenceTimeoutSeconds)
            {
                _silenceTimeoutFired = true;
                OnSilenceTimeout?.Invoke();
            }
        }

        public void Reset()
        {
            _hasActiveNote = false;
            _activeNote = default;
            _silenceTimeoutFired = true;
        }
    }
}
