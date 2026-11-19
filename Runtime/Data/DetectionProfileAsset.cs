using System;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Data
{
    /// <summary>
    /// Pure blittable/serializable DSP and note-segmentation configuration.
    /// Can be authored via DetectionProfileAsset or instantiated directly in code/tests.
    /// </summary>
    [Serializable]
    public struct DetectionProfileConfig
    {
        [Header("Frequency Band & Framing")]
        [Tooltip("Lowest fundamental frequency to detect (Hz). Hum ~80 Hz, Ocarina/Flute ~200 Hz.")]
        public float MinFrequencyHz;
        [Tooltip("Highest fundamental frequency to detect (Hz). Hum ~680 Hz, Instruments ~3600 Hz.")]
        public float MaxFrequencyHz;
        [Tooltip("FFT/YIN analysis frame size in samples (power of 2, e.g. 2048).")]
        public int FrameSize;
        [Tooltip("Hop stride between consecutive analysis frames in samples (e.g. 512).")]
        public int HopSize;
        [Tooltip("Concert pitch reference for A4 in Hz.")]
        public float ReferenceA4Hz;

        [Header("Noise Gate & Pitch Clarity")]
        [Tooltip("Absolute minimum RMS energy required to consider audio voiced.")]
        public float NoiseGateRms;
        [Tooltip("Multiplier above calibrated ambient noise floor.")]
        public float NoiseFloorMultiplier;
        [Tooltip("YIN CMND threshold for choosing the first fundamental valley (lower = stricter).")]
        [Range(0.05f, 0.35f)] public float YinThreshold;
        [Tooltip("Minimum clarity (1 - CMND) required to emit a voiced frame.")]
        [Range(0.4f, 0.98f)] public float MinClarity;
        [Tooltip("Maximum broadband spectral flatness allowed (rejects breath/hiss/claps).")]
        [Range(0.1f, 1f)] public float MaxSpectralFlatness;
        [Tooltip("Enable subharmonic/2nd-harmonic octave correction for rich instruments.")]
        public bool EnableOctaveCorrection;

        [Header("Note Segmentation & Onset")]
        [Tooltip("Number of recent pitch frames median-filtered to suppress glitches and vibrato.")]
        [Range(1, 9)] public int MedianWindowFrames;
        [Tooltip("Pitch change in semitones required to transition to a new note (hysteresis).")]
        [Range(0.35f, 0.95f)] public float NoteChangeHysteresisSemitones;
        [Tooltip("Minimum stable duration in seconds before a note is confirmed (filters stray blips).")]
        [Range(0.02f, 0.35f)] public float MinNoteDurationSeconds;
        [Tooltip("Unvoiced gap in seconds before closing the active note.")]
        [Range(0.015f, 0.25f)] public float NoteReleaseGapSeconds;
        [Tooltip("RMS dip ratio relative to recent peak that arms repeated same-pitch re-articulation.")]
        [Range(0.15f, 0.85f)] public float RearticulationDipRatio;
        [Tooltip("RMS rise multiplier over dipped floor required to fire a repeated same-pitch note.")]
        [Range(1.15f, 3.0f)] public float RearticulationRiseMultiplier;
        [Tooltip("Minimum seconds between repeated same-pitch onsets.")]
        [Range(0.05f, 0.4f)] public float MinRearticulationIntervalSeconds;
        [Tooltip("Seconds of continuous silence before emitting a phrase silence timeout.")]
        [Range(0.4f, 8.0f)] public float PhraseSilenceTimeoutSeconds;

        [Header("Matching Defaults")]
        [Tooltip("Default pitch tolerance in cents when matching melodies with this profile.")]
        [Range(15f, 95f)] public float DefaultPitchToleranceCents;
        [Tooltip("If true, overrides relative matching and requires exact concert pitch.")]
        public bool ForceStrictAbsolutePitch;

        public static DetectionProfileConfig CreateHumPreset() => new DetectionProfileConfig
        {
            MinFrequencyHz = 80f,
            MaxFrequencyHz = 680f,
            FrameSize = 2048,
            HopSize = 512,
            ReferenceA4Hz = 440f,
            NoiseGateRms = 0.012f,
            NoiseFloorMultiplier = 2.2f,
            YinThreshold = 0.20f,
            MinClarity = 0.72f,
            MaxSpectralFlatness = 0.55f,
            EnableOctaveCorrection = true,
            MedianWindowFrames = 5,
            NoteChangeHysteresisSemitones = 0.62f,
            MinNoteDurationSeconds = 0.085f,
            NoteReleaseGapSeconds = 0.055f,
            RearticulationDipRatio = 0.55f,
            RearticulationRiseMultiplier = 1.45f,
            MinRearticulationIntervalSeconds = 0.14f,
            PhraseSilenceTimeoutSeconds = 2.6f,
            DefaultPitchToleranceCents = 60f,
            ForceStrictAbsolutePitch = false
        };

        public static DetectionProfileConfig CreateInstrumentPreset() => new DetectionProfileConfig
        {
            MinFrequencyHz = 180f,
            MaxFrequencyHz = 3600f,
            FrameSize = 2048,
            HopSize = 512,
            ReferenceA4Hz = 440f,
            NoiseGateRms = 0.010f,
            NoiseFloorMultiplier = 2.0f,
            YinThreshold = 0.14f,
            MinClarity = 0.80f,
            MaxSpectralFlatness = 0.45f,
            EnableOctaveCorrection = true,
            MedianWindowFrames = 3,
            NoteChangeHysteresisSemitones = 0.52f,
            MinNoteDurationSeconds = 0.060f,
            NoteReleaseGapSeconds = 0.035f,
            RearticulationDipRatio = 0.50f,
            RearticulationRiseMultiplier = 1.50f,
            MinRearticulationIntervalSeconds = 0.10f,
            PhraseSilenceTimeoutSeconds = 2.4f,
            DefaultPitchToleranceCents = 40f,
            ForceStrictAbsolutePitch = false
        };

        public static DetectionProfileConfig CreateWhistlePreset() => new DetectionProfileConfig
        {
            MinFrequencyHz = 450f,
            MaxFrequencyHz = 3800f,
            FrameSize = 2048,
            HopSize = 512,
            ReferenceA4Hz = 440f,
            NoiseGateRms = 0.008f,
            NoiseFloorMultiplier = 2.0f,
            YinThreshold = 0.12f,
            MinClarity = 0.82f,
            MaxSpectralFlatness = 0.40f,
            EnableOctaveCorrection = false,
            MedianWindowFrames = 3,
            NoteChangeHysteresisSemitones = 0.55f,
            MinNoteDurationSeconds = 0.065f,
            NoteReleaseGapSeconds = 0.040f,
            RearticulationDipRatio = 0.52f,
            RearticulationRiseMultiplier = 1.45f,
            MinRearticulationIntervalSeconds = 0.11f,
            PhraseSilenceTimeoutSeconds = 2.4f,
            DefaultPitchToleranceCents = 48f,
            ForceStrictAbsolutePitch = false
        };

        public static DetectionProfileConfig CreatePreset(DetectionPresetKind kind)
        {
            switch (kind)
            {
                case DetectionPresetKind.Instrument: return CreateInstrumentPreset();
                case DetectionPresetKind.Whistle: return CreateWhistlePreset();
                default: return CreateHumPreset();
            }
        }

        public DetectionProfileConfig Sanitized()
        {
            DetectionProfileConfig c = this;
            c.MinFrequencyHz = Mathf.Clamp(c.MinFrequencyHz, 45f, 2000f);
            c.MaxFrequencyHz = Mathf.Clamp(c.MaxFrequencyHz, c.MinFrequencyHz + 50f, 6000f);
            c.FrameSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(512, c.FrameSize)), 512, 4096);
            c.HopSize = Mathf.Clamp(c.HopSize, 128, c.FrameSize);
            c.ReferenceA4Hz = Mathf.Clamp(c.ReferenceA4Hz <= 0f ? 440f : c.ReferenceA4Hz, 400f, 480f);
            c.NoiseGateRms = Mathf.Max(0.0001f, c.NoiseGateRms);
            c.NoiseFloorMultiplier = Mathf.Max(1f, c.NoiseFloorMultiplier);
            c.YinThreshold = Mathf.Clamp(c.YinThreshold, 0.05f, 0.4f);
            c.MinClarity = Mathf.Clamp(c.MinClarity, 0.3f, 0.99f);
            c.MaxSpectralFlatness = Mathf.Clamp(c.MaxSpectralFlatness, 0.1f, 1f);
            c.MedianWindowFrames = Mathf.Clamp(c.MedianWindowFrames, 1, 9);
            c.NoteChangeHysteresisSemitones = Mathf.Clamp(c.NoteChangeHysteresisSemitones, 0.3f, 1.0f);
            c.MinNoteDurationSeconds = Mathf.Clamp(c.MinNoteDurationSeconds, 0.015f, 0.5f);
            c.NoteReleaseGapSeconds = Mathf.Clamp(c.NoteReleaseGapSeconds, 0.01f, 0.5f);
            c.RearticulationDipRatio = Mathf.Clamp(c.RearticulationDipRatio, 0.1f, 0.9f);
            c.RearticulationRiseMultiplier = Mathf.Clamp(c.RearticulationRiseMultiplier, 1.1f, 4f);
            c.MinRearticulationIntervalSeconds = Mathf.Clamp(c.MinRearticulationIntervalSeconds, 0.04f, 0.6f);
            c.PhraseSilenceTimeoutSeconds = Mathf.Clamp(c.PhraseSilenceTimeoutSeconds, 0.3f, 15f);
            c.DefaultPitchToleranceCents = Mathf.Clamp(c.DefaultPitchToleranceCents, 10f, 120f);
            return c;
        }
    }

    /// <summary>
    /// ScriptableObject wrapper for swappable Hum, Instrument, Whistle, or Custom DSP profiles.
    /// </summary>
    [CreateAssetMenu(menuName = "MelodyRecognitionEngine/Detection Profile", fileName = "NewDetectionProfile")]
    public sealed class DetectionProfileAsset : ScriptableObject
    {
        [SerializeField] private DetectionPresetKind presetKind = DetectionPresetKind.Hum;
        [SerializeField] private DetectionProfileConfig config = DetectionProfileConfig.CreateHumPreset();

        public DetectionPresetKind PresetKind => presetKind;
        public DetectionProfileConfig Config => config.Sanitized();

        public void ApplyPreset(DetectionPresetKind kind)
        {
            presetKind = kind;
            config = DetectionProfileConfig.CreatePreset(kind);
        }

        private void OnValidate()
        {
            config = config.Sanitized();
        }
    }
}
