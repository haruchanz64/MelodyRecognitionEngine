using System;

namespace MelodyRecognitionEngine.Core
{
    /// <summary>
    /// Built-in acoustic tuning presets for the DSP pitch tracker and note segmenter.
    /// </summary>
    public enum DetectionPresetKind
    {
        Hum = 0,
        Instrument = 1,
        Whistle = 2,
        Custom = 3
    }

    /// <summary>
    /// Active input modality for a melody recognition session.
    /// </summary>
    public enum InputModality
    {
        MicrophoneHum = 0,
        MicrophoneInstrument = 1,
        MicrophoneWhistle = 2,
        DiscreteButtons = 3,
        CustomProfile = 4
    }

    /// <summary>
    /// Controls whether a song requires concert pitch or transposition-invariant relative intervals.
    /// </summary>
    public enum PitchMatchingMode
    {
        RelativeIntervals = 0,
        AbsolutePitch = 1
    }

    /// <summary>
    /// Active operational mode of the MelodyMatcher.
    /// </summary>
    public enum MatcherPlayMode
    {
        FreePlay = 0,
        GuidedLearn = 1
    }

    /// <summary>
    /// Single pitch-analysis frame emitted by the Burst DSP pitch tracker.
    /// Blittable so it can be produced directly inside a Burst-compiled job.
    /// </summary>
    public struct PitchFrame
    {
        public float FrequencyHz;
        public float MidiFloat;
        public int NearestMidiNote;
        public float CentsOffset;
        public float Rms;
        public float Clarity;
        public float SpectralFlatness;
        public double TimestampSeconds;
        public byte IsVoicedByte;

        public bool IsVoiced => IsVoicedByte != 0;
    }

    /// <summary>
    /// Segmented musical note event produced by either the microphone DSP pipeline or discrete virtual input.
    /// </summary>
    [Serializable]
    public struct NoteEvent
    {
        public int MidiNote;
        public float ExactMidiFloat;
        public float FrequencyHz;
        public float CentsOffset;
        public float Clarity;
        public float Rms;
        public double StartTimeSeconds;
        public float DurationSeconds;
        public bool IsRearticulated;
        public bool IsDiscreteInput;

        public string NoteName => MusicalNote.MidiToName(MidiNote);
    }

    /// <summary>
    /// Live partial-match progress reported as notes arrive during Free Play.
    /// </summary>
    public struct SongMatchProgress
    {
        public string BestCandidateSongId;
        public string BestCandidateDisplayName;
        public int MatchedNoteCount;
        public int TargetNoteCount;
        public float Confidence;
        public bool IsAwaitingPrefixDisambiguation;

        public float NormalizedProgress => TargetNoteCount > 0 ? (float)MatchedNoteCount / TargetNoteCount : 0f;
    }

    /// <summary>
    /// Result emitted when a song is successfully recognized.
    /// </summary>
    public struct SongMatchResult
    {
        public string SongId;
        public string DisplayName;
        public float Confidence;
        public float AveragePitchErrorCents;
        public int TranspositionSemitones;
        public bool MatchedViaAbsolutePitch;
        public bool MatchedRhythm;
        public bool IsCustomSong;
        public double MatchedAtSeconds;
    }

    /// <summary>
    /// Step update emitted during Guided / Learn Mode.
    /// </summary>
    public struct GuidedStepResult
    {
        public string SongId;
        public int StepIndex;
        public int TotalSteps;
        public int ExpectedMidiNote;
        public int PlayedMidiNote;
        public bool IsCorrect;
        public bool IsSongCompleted;
        public float PitchErrorCents;
    }

    /// <summary>
    /// Platform-agnostic PCM audio stream provider (microphone, AudioClip, synthetic stream, WebGL bridge, etc.).
    /// </summary>
    public interface IAudioCaptureSource : IDisposable
    {
        int SampleRate { get; }
        bool IsCapturing { get; }
        string DeviceName { get; }

        bool StartCapture();
        void StopCapture();

        /// <summary>
        /// Copies newly available mono PCM samples [-1, 1] into destination and returns the sample count written.
        /// </summary>
        int ReadSamples(float[] destination, int maxSamples);
    }

    /// <summary>
    /// Unified musical note event provider shared by microphone DSP pipelines and discrete controller/button inputs.
    /// </summary>
    public interface INoteInputSource
    {
        bool IsActive { get; }
        event Action<NoteEvent> OnNoteDetected;
        event Action<NoteEvent> OnNoteUpdated;
        event Action<NoteEvent> OnNoteEnded;
        event Action OnSilenceTimeout;
        event Action<PitchFrame> OnPitchFrame;
    }
}
