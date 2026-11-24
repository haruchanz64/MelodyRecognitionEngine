using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Data
{
    /// <summary>
    /// Built-in procedural synthesis voices for discrete/virtual instrument playback.
    /// </summary>
    public enum InstrumentWaveformPreset
    {
        Ocarina = 0,
        Flute = 1,
        HarpPluck = 2,
        BellChime = 3,
        SquareRetro = 4,
        AudioClipSample = 5
    }

    /// <summary>
    /// Describes a single playable note slot on a custom virtual instrument layout.
    /// </summary>
    [Serializable]
    public struct InstrumentNoteSlot
    {
        /// <summary>Short UI or key label displayed for this slot (e.g. "D4").</summary>
        public string Label;
        /// <summary>MIDI note number clamped to [24, 108].</summary>
        [Range(24, 108)] public int MidiNote;

        /// <summary>
        /// Creates a playable note slot with <paramref name="midiNote"/> clamped to [24, 108].
        /// </summary>
        public InstrumentNoteSlot(string label, int midiNote)
        {
            Label = label;
            MidiNote = Mathf.Clamp(midiNote, 24, 108);
        }
    }

    /// <summary>
    /// Playback timbre configuration for ProceduralOcarinaSynth (either procedural synthesis
    /// or a pitch-shifted AudioClip sample).
    /// </summary>
    [Serializable]
    public struct InstrumentTimbreConfig
    {
        /// <summary>Oscillator waveform preset or <see cref="InstrumentWaveformPreset.AudioClipSample"/> playback mode.</summary>
        public InstrumentWaveformPreset Waveform;
        /// <summary>Linear output gain in [0, 1].</summary>
        [Range(0f, 1f)] public float MasterVolume;
        /// <summary>Relative blend of the 2nd harmonic partial in [0, 0.8].</summary>
        [Range(0f, 0.8f)] public float SecondHarmonicMix;
        /// <summary>Relative blend of the 3rd harmonic partial in [0, 0.6].</summary>
        [Range(0f, 0.6f)] public float ThirdHarmonicMix;
        /// <summary>Vibrato LFO frequency in Hz.</summary>
        [Range(0f, 10f)] public float VibratoRateHz;
        /// <summary>Vibrato pitch modulation depth in cents.</summary>
        [Range(0f, 30f)] public float VibratoDepthCents;
        /// <summary>Envelope attack time in seconds.</summary>
        [Range(0.003f, 0.2f)] public float AttackSeconds;
        /// <summary>Envelope release time in seconds after note-off.</summary>
        [Range(0.02f, 1.2f)] public float ReleaseSeconds;
        /// <summary>When true, notes automatically decay after the attack peak instead of sustaining indefinitely.</summary>
        [Tooltip("If true, note decays automatically after attack (harp/bell pluck style).")]
        public bool AutoDecayPluck;
        /// <summary>Decay duration in seconds when <see cref="AutoDecayPluck"/> is enabled.</summary>
        [Range(0.08f, 2.5f)] public float PluckDecaySeconds;

        /// <summary>Optional root audio sample pitch-shifted when <see cref="Waveform"/> is <see cref="InstrumentWaveformPreset.AudioClipSample"/>.</summary>
        [Header("Optional AudioClip Sample (Used when Waveform = AudioClipSample)")]
        public AudioClip SampleClip;
        /// <summary>MIDI note number at which <see cref="SampleClip"/> was recorded (default 69 = A4).</summary>
        [Range(24, 108)] public int SampleRootMidiNote;

        /// <summary>
        /// Creates a default timbre configuration for the requested procedural <paramref name="preset"/>.
        /// </summary>
        public static InstrumentTimbreConfig CreatePreset(InstrumentWaveformPreset preset)
        {
            switch (preset)
            {
                case InstrumentWaveformPreset.Flute:
                    return new InstrumentTimbreConfig
                    {
                        Waveform = preset,
                        MasterVolume = 0.26f,
                        SecondHarmonicMix = 0.32f,
                        ThirdHarmonicMix = 0.14f,
                        VibratoRateHz = 5.6f,
                        VibratoDepthCents = 11f,
                        AttackSeconds = 0.045f,
                        ReleaseSeconds = 0.14f,
                        AutoDecayPluck = false,
                        PluckDecaySeconds = 0.6f,
                        SampleRootMidiNote = 69
                    };
                case InstrumentWaveformPreset.HarpPluck:
                    return new InstrumentTimbreConfig
                    {
                        Waveform = preset,
                        MasterVolume = 0.30f,
                        SecondHarmonicMix = 0.40f,
                        ThirdHarmonicMix = 0.18f,
                        VibratoRateHz = 0f,
                        VibratoDepthCents = 0f,
                        AttackSeconds = 0.005f,
                        ReleaseSeconds = 0.35f,
                        AutoDecayPluck = true,
                        PluckDecaySeconds = 0.75f,
                        SampleRootMidiNote = 69
                    };
                case InstrumentWaveformPreset.BellChime:
                    return new InstrumentTimbreConfig
                    {
                        Waveform = preset,
                        MasterVolume = 0.26f,
                        SecondHarmonicMix = 0.50f,
                        ThirdHarmonicMix = 0.30f,
                        VibratoRateHz = 0f,
                        VibratoDepthCents = 0f,
                        AttackSeconds = 0.004f,
                        ReleaseSeconds = 0.65f,
                        AutoDecayPluck = true,
                        PluckDecaySeconds = 1.1f,
                        SampleRootMidiNote = 69
                    };
                case InstrumentWaveformPreset.SquareRetro:
                    return new InstrumentTimbreConfig
                    {
                        Waveform = preset,
                        MasterVolume = 0.20f,
                        SecondHarmonicMix = 0f,
                        ThirdHarmonicMix = 0.33f,
                        VibratoRateHz = 4.5f,
                        VibratoDepthCents = 6f,
                        AttackSeconds = 0.008f,
                        ReleaseSeconds = 0.06f,
                        AutoDecayPluck = false,
                        PluckDecaySeconds = 0.5f,
                        SampleRootMidiNote = 69
                    };
                default:
                    return new InstrumentTimbreConfig
                    {
                        Waveform = InstrumentWaveformPreset.Ocarina,
                        MasterVolume = 0.28f,
                        SecondHarmonicMix = 0.16f,
                        ThirdHarmonicMix = 0.03f,
                        VibratoRateHz = 5.0f,
                        VibratoDepthCents = 8f,
                        AttackSeconds = 0.025f,
                        ReleaseSeconds = 0.12f,
                        AutoDecayPluck = false,
                        PluckDecaySeconds = 0.6f,
                        SampleRootMidiNote = 69
                    };
            }
        }
    }

    /// <summary>
    /// Bundles a custom instrument's acoustic DetectionProfileAsset, virtual note layout
    /// (any count of chromatic/diatonic notes), optional instrument-specific SongLibraryAsset,
    /// and AudioClip or procedural playback timbre.
    /// </summary>
    [CreateAssetMenu(menuName = "MelodyRecognitionEngine/Instrument Asset", fileName = "NewInstrumentAsset")]
    public sealed class InstrumentAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string instrumentId = "instrument_custom";
        [SerializeField] private string displayName = "Custom Instrument";

        [Header("Acoustic Recognition")]
        [Tooltip("Optional DetectionProfileAsset tuned for this instrument. Falls back to FallbackPreset if null.")]
        [SerializeField] private DetectionProfileAsset detectionProfile;
        [SerializeField] private DetectionPresetKind fallbackPreset = DetectionPresetKind.Instrument;

        [Header("Optional Instrument-Specific Song List")]
        [SerializeField] private SongLibraryAsset optionalSongLibrary;

        [Header("Playable Note Layout (Add any number of notes/chromatics)")]
        [SerializeField] private List<InstrumentNoteSlot> noteLayout = new List<InstrumentNoteSlot>
        {
            new InstrumentNoteSlot("D4", 62),
            new InstrumentNoteSlot("E4", 64),
            new InstrumentNoteSlot("F4", 65),
            new InstrumentNoteSlot("G4", 67),
            new InstrumentNoteSlot("A4", 69),
            new InstrumentNoteSlot("B4", 71),
            new InstrumentNoteSlot("C5", 72),
            new InstrumentNoteSlot("D5", 74)
        };

        [Header("Playback Timbre (Procedural Synth or Pitch-Shifted AudioClip)")]
        [SerializeField] private InstrumentTimbreConfig timbre = InstrumentTimbreConfig.CreatePreset(InstrumentWaveformPreset.Ocarina);

        /// <summary>Unique identifier for this instrument.</summary>
        public string InstrumentId => instrumentId;
        /// <summary>Human-readable instrument display name.</summary>
        public string DisplayName => displayName;
        /// <summary>Optional dedicated acoustic detection profile; may be null.</summary>
        public DetectionProfileAsset DetectionProfile => detectionProfile;
        /// <summary>Built-in preset used when <see cref="DetectionProfile"/> is unassigned.</summary>
        public DetectionPresetKind FallbackPreset => fallbackPreset;
        /// <summary>Optional song library loaded when equipping this instrument with song loading enabled.</summary>
        public SongLibraryAsset OptionalSongLibrary => optionalSongLibrary;
        /// <summary>Ordered list of virtual note slots exposed for discrete button or keyboard adapters.</summary>
        public IReadOnlyList<InstrumentNoteSlot> NoteLayout => noteLayout;
        /// <summary>Audio synthesis or sample playback configuration for this instrument.</summary>
        public InstrumentTimbreConfig Timbre => timbre;

        /// <summary>
        /// Resolves the effective <see cref="DetectionProfileConfig"/> from <see cref="DetectionProfile"/> or <see cref="FallbackPreset"/>.
        /// </summary>
        public DetectionProfileConfig ResolveProfileConfig()
        {
            return detectionProfile != null
                ? detectionProfile.Config
                : DetectionProfileConfig.CreatePreset(fallbackPreset);
        }

        /// <summary>
        /// Populates this instrument asset programmatically (useful for runtime-created instruments and EditMode tests).
        /// </summary>
        /// <param name="id">Unique instrument identifier (defaults to "instrument_custom" if blank).</param>
        /// <param name="nameDisplay">Display name (defaults to <paramref name="id"/> if blank).</param>
        /// <param name="profile">Optional acoustic profile override.</param>
        /// <param name="timbreConfig">Playback timbre configuration.</param>
        /// <param name="slots">Optional note layout replacing the existing slots when non-null.</param>
        /// <param name="songLibrary">Optional instrument-specific song library.</param>
        public void Configure(
            string id,
            string nameDisplay,
            DetectionProfileAsset profile,
            InstrumentTimbreConfig timbreConfig,
            IEnumerable<InstrumentNoteSlot> slots = null,
            SongLibraryAsset songLibrary = null)
        {
            instrumentId = string.IsNullOrWhiteSpace(id) ? "instrument_custom" : id;
            displayName = string.IsNullOrWhiteSpace(nameDisplay) ? instrumentId : nameDisplay;
            detectionProfile = profile;
            timbre = timbreConfig;
            optionalSongLibrary = songLibrary;

            if (slots != null)
            {
                noteLayout.Clear();
                foreach (InstrumentNoteSlot slot in slots)
                {
                    noteLayout.Add(slot);
                }
            }
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(instrumentId))
            {
                instrumentId = name.ToLowerInvariant().Replace(' ', '_');
            }

            for (int i = 0; i < noteLayout.Count; i++)
            {
                InstrumentNoteSlot s = noteLayout[i];
                s.MidiNote = Mathf.Clamp(s.MidiNote, 24, 108);
                if (string.IsNullOrWhiteSpace(s.Label))
                {
                    s.Label = MusicalNote.MidiToName(s.MidiNote);
                }
                noteLayout[i] = s;
            }
        }
    }
}
