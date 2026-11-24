using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;

namespace MelodyRecognitionEngine.Matching
{
    /// <summary>
    /// Records live NoteEvents into a validated custom SongDefinition (e.g. Scarecrow's Song)
    /// that can be saved into SongRepertoire and serialized to JSON.
    /// </summary>
    public sealed class CustomSongRecorder
    {
        private readonly List<NoteEvent> _capturedNotes = new List<NoteEvent>();
        private bool _isRecording;
        private int _minNotes = 4;
        private int _maxNotes = 8;
        private string _targetSongId = "custom_song";
        private string _targetDisplayName = "Custom Song";

        /// <summary>Invoked each time a note is appended during recording, passing the note and updated total count.</summary>
        public event Action<NoteEvent, int> OnNoteCaptured;
        /// <summary>Invoked when recording finishes and produces a validated <see cref="SongDefinition"/>.</summary>
        public event Action<SongDefinition> OnRecordingCompleted;
        /// <summary>Invoked when <see cref="FinishRecording"/> rejects a recording, passing a human-readable reason.</summary>
        public event Action<string> OnRecordingFailed;

        /// <summary>True while actively capturing incoming <see cref="NoteEvent"/> instances.</summary>
        public bool IsRecording => _isRecording;
        /// <summary>Number of notes captured so far in the current recording session.</summary>
        public int CapturedCount => _capturedNotes.Count;
        /// <summary>Minimum number of notes required to finalize a custom song.</summary>
        public int MinNotes => _minNotes;
        /// <summary>Maximum number of notes captured before <see cref="FeedNote"/> automatically finishes recording.</summary>
        public int MaxNotes => _maxNotes;
        /// <summary>Read-only view of the notes captured so far.</summary>
        public IReadOnlyList<NoteEvent> CapturedNotes => _capturedNotes;

        /// <summary>
        /// Starts a new custom song recording session and clears any previously captured notes.
        /// </summary>
        /// <param name="songId">Song identifier assigned to the finished <see cref="SongDefinition"/>.</param>
        /// <param name="displayName">Human-readable title assigned to the finished song.</param>
        /// <param name="minNotes">Minimum required note count (clamped to at least 2).</param>
        /// <param name="maxNotes">Maximum note count before auto-finishing (clamped to at least <paramref name="minNotes"/>).</param>
        public void BeginRecording(string songId = "custom_song", string displayName = "Custom Song", int minNotes = 4, int maxNotes = 8)
        {
            _targetSongId = string.IsNullOrWhiteSpace(songId) ? "custom_song" : songId;
            _targetDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Custom Song" : displayName;
            _minNotes = Mathf.Max(2, minNotes);
            _maxNotes = Mathf.Max(_minNotes, maxNotes);
            _capturedNotes.Clear();
            _isRecording = true;
        }

        /// <summary>
        /// Aborts the current recording session and discards all captured notes without emitting failure events.
        /// </summary>
        public void CancelRecording()
        {
            _isRecording = false;
            _capturedNotes.Clear();
        }

        /// <summary>
        /// Appends a live note while recording and automatically calls <see cref="FinishRecording"/> once <see cref="MaxNotes"/> is reached.
        /// </summary>
        public void FeedNote(NoteEvent note)
        {
            if (!_isRecording) return;

            _capturedNotes.Add(note);
            OnNoteCaptured?.Invoke(note, _capturedNotes.Count);

            if (_capturedNotes.Count >= _maxNotes)
            {
                FinishRecording(out _, out _);
            }
        }

        /// <summary>
        /// Stops recording and builds a SongDefinition if at least MinNotes were captured
        /// and the phrase isn't strictly monotone (unless allowMonotone is true).
        /// </summary>
        /// <param name="song">Receives the constructed custom <see cref="SongDefinition"/> on success, or null on failure.</param>
        /// <param name="error">Receives a validation error message on failure, or null on success.</param>
        /// <param name="allowMonotone">When false, requires at least two distinct MIDI pitches.</param>
        /// <param name="conflictCheckRepertoire">Optional repertoire checked to reject exact relative-interval duplicates of other songs.</param>
        /// <returns>True if validation succeeded and <paramref name="song"/> was produced.</returns>
        public bool FinishRecording(out SongDefinition song, out string error, bool allowMonotone = false, SongRepertoire conflictCheckRepertoire = null)
        {
            song = null;
            error = null;
            _isRecording = false;

            if (_capturedNotes.Count < _minNotes)
            {
                error = $"Need at least {_minNotes} notes (captured {_capturedNotes.Count}).";
                OnRecordingFailed?.Invoke(error);
                return false;
            }

            if (!allowMonotone)
            {
                int firstMidi = _capturedNotes[0].MidiNote;
                bool hasPitchVariety = false;
                for (int i = 1; i < _capturedNotes.Count; i++)
                {
                    if (_capturedNotes[i].MidiNote != firstMidi)
                    {
                        hasPitchVariety = true;
                        break;
                    }
                }

                if (!hasPitchVariety)
                {
                    error = "Custom melody must contain at least two distinct pitches.";
                    OnRecordingFailed?.Invoke(error);
                    return false;
                }
            }

            var built = new SongDefinition
            {
                SongId = _targetSongId,
                DisplayName = _targetDisplayName,
                Description = "Player-recorded custom melody.",
                PitchMode = PitchMatchingMode.RelativeIntervals,
                UnlockedByDefault = true,
                AlwaysDiscoverable = false,
                IsCustomSong = true,
                Notes = new List<SongNoteEntry>(_capturedNotes.Count)
            };

            // Compute normalized relative beat durations from inter-onset intervals.
            float baselineDur = 0.35f;
            if (_capturedNotes.Count >= 2)
            {
                float sumIoi = 0f;
                int countIoi = 0;
                for (int i = 0; i < _capturedNotes.Count - 1; i++)
                {
                    float ioi = (float)(_capturedNotes[i + 1].StartTimeSeconds - _capturedNotes[i].StartTimeSeconds);
                    if (ioi > 0.05f && ioi < 4f)
                    {
                        sumIoi += ioi;
                        countIoi++;
                    }
                }

                if (countIoi > 0)
                {
                    baselineDur = Mathf.Max(0.1f, sumIoi / countIoi);
                }
            }

            for (int i = 0; i < _capturedNotes.Count; i++)
            {
                float relDur = 1f;
                if (i < _capturedNotes.Count - 1)
                {
                    float ioi = (float)(_capturedNotes[i + 1].StartTimeSeconds - _capturedNotes[i].StartTimeSeconds);
                    if (ioi > 0.05f && ioi < 4f)
                    {
                        relDur = Mathf.Clamp(ioi / baselineDur, 0.25f, 4f);
                    }
                }

                built.Notes.Add(new SongNoteEntry(_capturedNotes[i].MidiNote, relDur));
            }

            if (conflictCheckRepertoire != null && HasExactIntervalConflict(built, conflictCheckRepertoire))
            {
                error = "Custom melody conflicts with an existing song's interval sequence.";
                OnRecordingFailed?.Invoke(error);
                return false;
            }

            song = built;
            OnRecordingCompleted?.Invoke(built);
            return true;
        }

        /// <summary>
        /// Returns true if any other song in <paramref name="repertoire"/> has the same length and identical relative semitone intervals.
        /// </summary>
        private static bool HasExactIntervalConflict(SongDefinition candidate, SongRepertoire repertoire)
        {
            int[] candIntervals = candidate.GetRelativeIntervals();
            IReadOnlyList<SongDefinition> existing = repertoire.AllSongs;

            for (int s = 0; s < existing.Count; s++)
            {
                SongDefinition other = existing[s];
                if (string.Equals(other.SongId, candidate.SongId, StringComparison.Ordinal)) continue;
                if (other.NoteCount != candidate.NoteCount) continue;

                int[] otherIntervals = other.GetRelativeIntervals();
                bool identical = true;
                for (int i = 0; i < candIntervals.Length; i++)
                {
                    if (candIntervals[i] != otherIntervals[i])
                    {
                        identical = false;
                        break;
                    }
                }

                if (identical) return true;
            }

            return false;
        }
    }
}
