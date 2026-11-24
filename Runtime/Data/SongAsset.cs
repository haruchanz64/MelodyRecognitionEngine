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
        /// <summary>Reference MIDI note number (e.g. 60 = C4, 69 = A4, 71 = B4).</summary>
        [Tooltip("Reference MIDI note number (e.g. 60 = C4, 69 = A4, 71 = B4).")]
        [Range(24, 108)] public int MidiNote;

        /// <summary>Relative beat weight used when rhythm validation is enabled (clamped to at least 0.1).</summary>
        [Tooltip("Relative beat/duration weight used when ValidateRhythm is enabled (e.g. 1 = quarter, 2 = half).")]
        [Min(0.1f)] public float RelativeDuration;

        /// <summary>
        /// Creates a song note entry with clamped MIDI range [0, 127] and minimum duration weight of 0.1.
        /// </summary>
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
        /// <summary>Unique identifier used for repertoire lookup, unlocking, and save serialization.</summary>
        public string SongId = "song_untitled";
        /// <summary>Human-readable song title presented in HUDs and match events.</summary>
        public string DisplayName = "Untitled Song";
        /// <summary>Optional narrative or gameplay description.</summary>
        public string Description = "";

        /// <summary>Ordered note sequence defining this melody (must contain at least 2 notes to be valid).</summary>
        public List<SongNoteEntry> Notes = new List<SongNoteEntry>();

        /// <summary>Selects transposition-invariant relative interval matching or strict concert-pitch matching.</summary>
        [Header("Matching Rules")]
        public PitchMatchingMode PitchMode = PitchMatchingMode.RelativeIntervals;
        /// <summary>Per-song pitch tolerance in cents; values &lt;= 0 inherit the active profile's default tolerance.</summary>
        [Tooltip("Pitch tolerance in cents. Set <= 0 to inherit the active DetectionProfile default.")]
        public float PitchToleranceCents = 0f;
        /// <summary>Maximum interior accidental notes forgiven during matching (0 to 2; ignored for songs shorter than 4 notes).</summary>
        [Tooltip("Maximum accidental stray notes forgiven inside the phrase (0 = exact sequence).")]
        [Range(0, 2)] public int MaxStrayNotes = 0;

        /// <summary>When true, verifies tempo-invariant inter-onset duration ratios against <see cref="SongNoteEntry.RelativeDuration"/>.</summary>
        [Header("Optional Rhythm Validation")]
        public bool ValidateRhythm = false;
        /// <summary>Maximum fractional relative error allowed on normalized inter-onset intervals (e.g. 0.35 = +/-35%).</summary>
        [Tooltip("Maximum allowed relative error on inter-onset rhythm ratios (0.35 = +/-35%).")]
        [Range(0.1f, 0.75f)] public float RhythmTolerance = 0.35f;

        /// <summary>When true, the song starts unlocked when registered into a <see cref="Matching.SongRepertoire"/>.</summary>
        [Header("Progression & Discovery")]
        [Tooltip("If true, this song starts unlocked when added to a SongRepertoire.")]
        public bool UnlockedByDefault = false;
        /// <summary>When true, the song can be recognized even while locked (for secret or Easter-egg melodies).</summary>
        [Tooltip("If true, this song can be triggered even while locked (for secret/Easter-egg songs).")]
        public bool AlwaysDiscoverable = false;
        /// <summary>True for player-recorded runtime songs persisted in repertoire JSON state.</summary>
        [Tooltip("True for player-recorded runtime songs (e.g. Scarecrow's Song).")]
        public bool IsCustomSong = false;

        /// <summary>Accent color associated with this song for UI feedback.</summary>
        [Header("Presentation")]
        public Color HighlightColor = new Color(0.98f, 0.82f, 0.25f, 1f);
        /// <summary>Optional game-defined metadata tag.</summary>
        public string MetadataTag = "";

        /// <summary>Total number of notes in <see cref="Notes"/>.</summary>
        public int NoteCount => Notes != null ? Notes.Count : 0;
        /// <summary>True when <see cref="SongId"/> is non-empty and <see cref="Notes"/> contains at least 2 entries.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(SongId) && Notes != null && Notes.Count >= 2;

        /// <summary>
        /// Creates an empty <see cref="SongDefinition"/> with default matching settings.
        /// </summary>
        public SongDefinition() { }

        /// <summary>
        /// Creates a <see cref="SongDefinition"/> by parsing a musical notation string (e.g. "B4 D5 A4" or "B4:2 D5:1 A4:3").
        /// </summary>
        /// <param name="songId">Unique song identifier.</param>
        /// <param name="displayName">Human-readable title.</param>
        /// <param name="notation">Space- or comma-separated note sequence with optional <c>:duration</c> weights.</param>
        /// <param name="pitchMode">Relative interval or strict absolute pitch matching mode.</param>
        /// <param name="unlockedByDefault">Whether the song starts unlocked in a repertoire.</param>
        /// <param name="alwaysDiscoverable">Whether the song can trigger while locked.</param>
        /// <param name="validateRhythm">Whether to validate tempo-invariant inter-onset rhythm ratios.</param>
        /// <returns>The constructed song definition (check <see cref="IsValid"/> if notation may be malformed).</returns>
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

        /// <summary>
        /// Replaces <see cref="Notes"/> by parsing <paramref name="notation"/> via <see cref="MusicalNote.TryParseNotationSequence"/>.
        /// </summary>
        /// <returns>True if parsing succeeded and <see cref="Notes"/> was updated; false leaves existing notes unchanged.</returns>
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

        /// <summary>
        /// Formats <see cref="Notes"/> back into a notation string such as "B4 D5 A4" or "B4:2 D5:1 A4:3".
        /// </summary>
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

        /// <summary>
        /// Computes signed semitone offsets of notes 1..N-1 relative to the first note (index 0).
        /// </summary>
        /// <returns>Array of length <c>NoteCount - 1</c>, or an empty array when fewer than 2 notes exist.</returns>
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

        /// <summary>
        /// Creates a deep copy of this song definition and its <see cref="Notes"/> list.
        /// </summary>
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

        /// <summary>Underlying serializable song definition stored by this asset.</summary>
        public SongDefinition Definition => definition;
        /// <summary>Shortcut to <see cref="SongDefinition.SongId"/>.</summary>
        public string SongId => definition != null ? definition.SongId : string.Empty;
        /// <summary>Shortcut to <see cref="SongDefinition.DisplayName"/>, falling back to the asset name.</summary>
        public string DisplayName => definition != null ? definition.DisplayName : name;

        /// <summary>
        /// Replaces this asset's song definition with a deep clone of <paramref name="newDefinition"/>.
        /// </summary>
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
