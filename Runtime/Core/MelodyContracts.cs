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
        /// <summary>Estimated fundamental frequency in Hz, or 0 when unvoiced.</summary>
        public float FrequencyHz;
        /// <summary>Continuous fractional MIDI note number (e.g. 69.0 for A4), or -1 when unvoiced.</summary>
        public float MidiFloat;
        /// <summary>Nearest integer MIDI note number [0, 127], or -1 when unvoiced.</summary>
        public int NearestMidiNote;
        /// <summary>Pitch deviation from <see cref="NearestMidiNote"/> in cents (-50 to +50).</summary>
        public float CentsOffset;
        /// <summary>Root-mean-square signal amplitude after bandpass filtering.</summary>
        public float Rms;
        /// <summary>Pitch periodicity score in [0, 1], computed as <c>1 - CMND</c>.</summary>
        public float Clarity;
        /// <summary>Normalized broadband noise ratio in [0, 1]; lower values indicate cleaner tonal audio.</summary>
        public float SpectralFlatness;
        /// <summary>Stream timestamp of the analyzed frame in seconds.</summary>
        public double TimestampSeconds;
        /// <summary>Blittable voicing flag (1 when voiced, 0 when silent or rejected).</summary>
        public byte IsVoicedByte;

        /// <summary>Returns true when this frame passed noise, clarity, and frequency range checks.</summary>
        public bool IsVoiced => IsVoicedByte != 0;
    }

    /// <summary>
    /// Segmented musical note event produced by either the microphone DSP pipeline or discrete virtual input.
    /// </summary>
    [Serializable]
    public struct NoteEvent
    {
        /// <summary>Quantized integer MIDI note number [0, 127].</summary>
        public int MidiNote;
        /// <summary>Smoothed continuous MIDI pitch value before rounding.</summary>
        public float ExactMidiFloat;
        /// <summary>Fundamental frequency in Hz corresponding to <see cref="ExactMidiFloat"/>.</summary>
        public float FrequencyHz;
        /// <summary>Pitch deviation from <see cref="MidiNote"/> in cents.</summary>
        public float CentsOffset;
        /// <summary>Averaged pitch clarity in [0, 1] (always 1 for discrete button inputs).</summary>
        public float Clarity;
        /// <summary>Peak or current RMS amplitude in [0, 1].</summary>
        public float Rms;
        /// <summary>Onset timestamp of the note in seconds.</summary>
        public double StartTimeSeconds;
        /// <summary>Sustained duration of the note in seconds.</summary>
        public float DurationSeconds;
        /// <summary>True when this event was triggered by a repeated same-pitch amplitude re-articulation.</summary>
        public bool IsRearticulated;
        /// <summary>True when emitted from discrete virtual keys/buttons rather than acoustic microphone capture.</summary>
        public bool IsDiscreteInput;

        /// <summary>Formatted scientific pitch name (for example, "A4" or "F#5") for <see cref="MidiNote"/>.</summary>
        public string NoteName => MusicalNote.MidiToName(MidiNote);
    }

    /// <summary>
    /// Live partial-match progress reported as notes arrive during Free Play.
    /// </summary>
    public struct SongMatchProgress
    {
        /// <summary>Unique identifier of the best-matching candidate song.</summary>
        public string BestCandidateSongId;
        /// <summary>Human-readable display name of the best-matching candidate song.</summary>
        public string BestCandidateDisplayName;
        /// <summary>Number of consecutive prefix notes matched so far.</summary>
        public int MatchedNoteCount;
        /// <summary>Total number of notes required to complete the candidate song.</summary>
        public int TargetNoteCount;
        /// <summary>Estimated match confidence in [0, 1] for the matched prefix.</summary>
        public float Confidence;
        /// <summary>True while holding a completed shorter match to see if the player continues into a longer superset song.</summary>
        public bool IsAwaitingPrefixDisambiguation;

        /// <summary>Completion ratio in [0, 1] computed from <see cref="MatchedNoteCount"/> over <see cref="TargetNoteCount"/>.</summary>
        public float NormalizedProgress => TargetNoteCount > 0 ? (float)MatchedNoteCount / TargetNoteCount : 0f;
    }

    /// <summary>
    /// Result emitted when a song is successfully recognized.
    /// </summary>
    public struct SongMatchResult
    {
        /// <summary>Unique identifier of the recognized song.</summary>
        public string SongId;
        /// <summary>Human-readable display name of the recognized song.</summary>
        public string DisplayName;
        /// <summary>Overall match confidence in [0, 1], factoring in pitch accuracy and stray-note penalties.</summary>
        public float Confidence;
        /// <summary>Mean absolute pitch error across all matched notes in cents.</summary>
        public float AveragePitchErrorCents;
        /// <summary>Estimated transposition relative to the authored notation in whole semitones (0 in absolute mode).</summary>
        public int TranspositionSemitones;
        /// <summary>True when matched under strict concert-pitch validation rather than relative intervals.</summary>
        public bool MatchedViaAbsolutePitch;
        /// <summary>True when rhythm validation was satisfied or not required by the song.</summary>
        public bool MatchedRhythm;
        /// <summary>True when the recognized song is a player-recorded custom song.</summary>
        public bool IsCustomSong;
        /// <summary>Stream or session timestamp in seconds when the match completed.</summary>
        public double MatchedAtSeconds;
    }

    /// <summary>
    /// Step update emitted during Guided / Learn Mode.
    /// </summary>
    public struct GuidedStepResult
    {
        /// <summary>Unique identifier of the song being taught.</summary>
        public string SongId;
        /// <summary>Zero-based index of the step evaluated by this note.</summary>
        public int StepIndex;
        /// <summary>Total number of notes in the target song.</summary>
        public int TotalSteps;
        /// <summary>Nominal authored MIDI note expected for this step.</summary>
        public int ExpectedMidiNote;
        /// <summary>MIDI note played by the user.</summary>
        public int PlayedMidiNote;
        /// <summary>True when the played note fell within pitch tolerance for the current step.</summary>
        public bool IsCorrect;
        /// <summary>True when this step completed the final note of the guided song.</summary>
        public bool IsSongCompleted;
        /// <summary>Absolute pitch error in cents relative to the expected target pitch.</summary>
        public float PitchErrorCents;
    }

    /// <summary>
    /// Platform-agnostic PCM audio stream provider (microphone, AudioClip, synthetic stream, WebGL bridge, etc.).
    /// </summary>
    public interface IAudioCaptureSource : IDisposable
    {
        /// <summary>Active capture sample rate in Hz.</summary>
        int SampleRate { get; }
        /// <summary>True while the capture stream is actively producing samples.</summary>
        bool IsCapturing { get; }
        /// <summary>Human-readable capture device or stream label.</summary>
        string DeviceName { get; }

        /// <summary>
        /// Starts capturing audio samples from the underlying source.
        /// </summary>
        /// <returns>True if capture started or was already active; false if the source could not be opened.</returns>
        bool StartCapture();

        /// <summary>
        /// Stops capturing audio and releases active recording handles without necessarily disposing reusable state.
        /// </summary>
        void StopCapture();

        /// <summary>
        /// Copies newly available mono PCM samples [-1, 1] into destination and returns the sample count written.
        /// </summary>
        /// <param name="destination">Caller-owned buffer receiving mono PCM samples.</param>
        /// <param name="maxSamples">Maximum number of samples to write into <paramref name="destination"/>.</param>
        /// <returns>Number of mono samples written (0 when no new audio is available).</returns>
        int ReadSamples(float[] destination, int maxSamples);
    }

    /// <summary>
    /// Unified musical note event provider shared by microphone DSP pipelines and discrete controller/button inputs.
    /// </summary>
    public interface INoteInputSource
    {
        /// <summary>True while this input source is active and emitting note events.</summary>
        bool IsActive { get; }
        /// <summary>Invoked when a new musical note onset is confirmed.</summary>
        event Action<NoteEvent> OnNoteDetected;
        /// <summary>Invoked periodically while the current note remains sustained.</summary>
        event Action<NoteEvent> OnNoteUpdated;
        /// <summary>Invoked when the active note ends or is flushed.</summary>
        event Action<NoteEvent> OnNoteEnded;
        /// <summary>Invoked once after continuous silence exceeds the configured phrase timeout.</summary>
        event Action OnSilenceTimeout;
        /// <summary>Invoked for each raw pitch analysis frame or synthetic discrete pitch update.</summary>
        event Action<PitchFrame> OnPitchFrame;
    }
}
