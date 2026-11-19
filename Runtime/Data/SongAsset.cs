using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Data
{
    /// <summary>
    /// Single note step inside a song definition.
    /// </summary>
    [Serializable]
    public struct SongNoteEntry
    {
        [Tooltip("Reference MIDI note number (e.g. 60 = C4, 69 = A4, 71 = B4).")]
        [Range(24, 108)] public int MidiNote;

        [Tooltip("Relative beat/duration weight used when ValidateRhythm is enabled (e.g. 1 = quarter, 2 = half).")]
        [Min(0.1f)] public float RelativeDuration;

        public SongNoteEntry(int midiNote, float relativeDuration = 1f)
        {
            MidiNote = Mathf.Clamp(midiNote, 0, 127);
            RelativeDuration = Mathf.Max(0.1f, relativeDuration);
        }
    }

    /// <summary>
    /// Serializable pure-data song model usable both inside SongAsset ScriptableObjects
    /// and at runtime for player-recorded custom songs (JSON save/load).
    /// </summary>
    [Serializable]
    public sealed class SongDefinition
    {
        public string SongId = "song_untitled";
        public string DisplayName = "Untitled Song";
        public string Description = "";

        public List<SongNoteEntry> Notes = new List<SongNoteEntry>();

        [Header("Matching Rules")]
        public PitchMatchingMode PitchMode = PitchMatchingMode.RelativeIntervals;
        [Tooltip("Pitch tolerance in cents. Set <= 0 to inherit the active DetectionProfile default.")]
        public float PitchToleranceCents = 0f;
        [Tooltip("Maximum accidental stray notes forgiven inside the phrase (0 = exact sequence).")]
        [Range(0, 2)] public int MaxStrayNotes = 0;

        [Header("Optional Rhythm Validation")]
        public bool ValidateRhythm = false;
        [Tooltip("Maximum allowed relative error on inter-onset rhythm ratios (0.35 = +/-35%).")]
        [Range(0.1f, 0.75f)] public float RhythmTolerance = 0.35f;

        [Header("Progression & Discovery")]
        [Tooltip("If true, this song starts unlocked when added to a SongRepertoire.")]
        public bool UnlockedByDefault = false;
        [Tooltip("If true, this song can be triggered even while locked (for secret/Easter-egg songs).")]
        public bool AlwaysDiscoverable = false;
        [Tooltip("True for player-recorded runtime songs (e.g. Scarecrow's Song).")]
        public bool IsCustomSong = false;

        [Header("Presentation")]
        public Color HighlightColor = new Color(0.98f, 0.82f, 0.25f, 1f);
        public string MetadataTag = "";

        public int NoteCount => Notes != null ? Notes.Count : 0;
        public bool IsValid => !string.IsNullOrWhiteSpace(SongId) && Notes != null && Notes.Count >= 2;

        public SongDefinition() { }

        public static SongDefinition FromNotation(
            string songId,
            string displayName,
            string notation,
            PitchMatchingMode pitchMode = PitchMatchingMode.RelativeIntervals,
            bool unlockedByDefault = true,
            bool alwaysDiscoverable = false,
            bool validateRhythm = false)
        {
            var song = new SongDefinition
            {
                SongId = songId,
                DisplayName = displayName,
                PitchMode = pitchMode,
                UnlockedByDefault = unlockedByDefault,
                AlwaysDiscoverable = alwaysDiscoverable,
                ValidateRhythm = validateRhythm
            };

            song.SetNotesFromNotation(notation);
            return song;
        }

        public bool SetNotesFromNotation(string notation)
        {
            if (!MusicalNote.TryParseNotationSequence(notation, out List<int> midiNotes, out List<float> durations))
            {
                return false;
            }

            if (Notes == null) Notes = new List<SongNoteEntry>();
            Notes.Clear();

            for (int i = 0; i < midiNotes.Count; i++)
            {
                Notes.Add(new SongNoteEntry(midiNotes[i], durations[i]));
            }

            return true;
        }

        public string ToNotationString()
        {
            if (Notes == null || Notes.Count == 0) return string.Empty;
            var midis = new int[Notes.Count];
            var durs = new float[Notes.Count];
            for (int i = 0; i < Notes.Count; i++)
            {
                midis[i] = Notes[i].MidiNote;
                durs[i] = Notes[i].RelativeDuration;
            }

            return MusicalNote.FormatSequence(midis, durs);
        }

        public int[] GetRelativeIntervals()
        {
            if (Notes == null || Notes.Count < 2) return Array.Empty<int>();
            var intervals = new int[Notes.Count - 1];
            int root = Notes[0].MidiNote;
            for (int i = 1; i < Notes.Count; i++)
            {
                intervals[i - 1] = Notes[i].MidiNote - root;
            }
            return intervals;
        }

        public SongDefinition Clone()
        {
            var copy = new SongDefinition
            {
                SongId = SongId,
                DisplayName = DisplayName,
                Description = Description,
                PitchMode = PitchMode,
                PitchToleranceCents = PitchToleranceCents,
                MaxStrayNotes = MaxStrayNotes,
                ValidateRhythm = ValidateRhythm,
                RhythmTolerance = RhythmTolerance,
                UnlockedByDefault = UnlockedByDefault,
                AlwaysDiscoverable = AlwaysDiscoverable,
                IsCustomSong = IsCustomSong,
                HighlightColor = HighlightColor,
                MetadataTag = MetadataTag,
                Notes = new List<SongNoteEntry>(Notes != null ? Notes.Count : 0)
            };

            if (Notes != null)
            {
                for (int i = 0; i < Notes.Count; i++)
                {
                    copy.Notes.Add(Notes[i]);
                }
            }

            return copy;
        }
    }

    /// <summary>
    /// ScriptableObject song asset authored in the Unity Inspector or Melody Workbench.
    /// </summary>
    [CreateAssetMenu(menuName = "MelodyRecognitionEngine/Song Asset", fileName = "NewSongAsset")]
    public sealed class SongAsset : ScriptableObject
    {
        [SerializeField] private SongDefinition definition = new SongDefinition
        {
            SongId = "song_new",
            DisplayName = "New Song",
            Notes = new List<SongNoteEntry>
            {
                new SongNoteEntry(71, 2f), // B4
                new SongNoteEntry(74, 1f), // D5
                new SongNoteEntry(69, 3f), // A4
                new SongNoteEntry(71, 2f), // B4
                new SongNoteEntry(74, 1f), // D5
                new SongNoteEntry(69, 3f)  // A4
            }
        };

        public SongDefinition Definition => definition;
        public string SongId => definition != null ? definition.SongId : string.Empty;
        public string DisplayName => definition != null ? definition.DisplayName : name;

        public void SetDefinition(SongDefinition newDefinition)
        {
            definition = newDefinition != null ? newDefinition.Clone() : new SongDefinition();
        }

        private void OnValidate()
        {
            if (definition == null)
            {
                definition = new SongDefinition();
            }

            if (string.IsNullOrWhiteSpace(definition.SongId))
            {
                definition.SongId = name.ToLowerInvariant().Replace(' ', '_');
            }

            if (definition.Notes != null)
            {
                for (int i = 0; i < definition.Notes.Count; i++)
                {
                    SongNoteEntry entry = definition.Notes[i];
                    entry.MidiNote = Mathf.Clamp(entry.MidiNote, 24, 108);
                    entry.RelativeDuration = Mathf.Max(0.1f, entry.RelativeDuration);
                    definition.Notes[i] = entry;
                }
            }
        }
    }
}
