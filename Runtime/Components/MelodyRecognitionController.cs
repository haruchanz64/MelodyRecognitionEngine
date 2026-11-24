using System;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Input;
using MelodyRecognitionEngine.Matching;
using MelodyRecognitionEngine.Presentation;
using UnityEngine;
using UnityEngine.Events;

namespace MelodyRecognitionEngine.Components
{
    /// <summary>Inspector-serializable UnityEvent carrying a boolean state (such as listening started/stopped).</summary>
    [Serializable] public sealed class BoolUnityEvent : UnityEvent<bool> { }
    /// <summary>Inspector-serializable UnityEvent carrying a segmented <see cref="NoteEvent"/>.</summary>
    [Serializable] public sealed class NoteUnityEvent : UnityEvent<NoteEvent> { }
    /// <summary>Inspector-serializable UnityEvent carrying a completed <see cref="SongMatchResult"/>.</summary>
    [Serializable] public sealed class SongMatchUnityEvent : UnityEvent<SongMatchResult> { }
    /// <summary>Inspector-serializable UnityEvent carrying live <see cref="SongMatchProgress"/> updates.</summary>
    [Serializable] public sealed class SongProgressUnityEvent : UnityEvent<SongMatchProgress> { }
    /// <summary>Inspector-serializable UnityEvent carrying a <see cref="GuidedStepResult"/> update.</summary>
    [Serializable] public sealed class GuidedStepUnityEvent : UnityEvent<GuidedStepResult> { }
    /// <summary>Inspector-serializable UnityEvent carrying a string identifier (such as an unlocked song ID).</summary>
    [Serializable] public sealed class StringUnityEvent : UnityEvent<string> { }
    /// <summary>Inspector-serializable UnityEvent carrying an equipped <see cref="InstrumentAsset"/> (may be null on unequip).</summary>
    [Serializable] public sealed class InstrumentUnityEvent : UnityEvent<InstrumentAsset> { }

    /// <summary>
    /// Drop-in scene controller wiring microphone/discrete input, Burst DSP pitch tracking,
    /// MelodyMatcher, SongRepertoire, CustomSongRecorder, optional tone synth, and BGM ducking hooks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MelodyRecognitionController : MonoBehaviour
    {
        [Header("Library, Instrument & Profiles")]
        [SerializeField] private SongLibraryAsset songLibrary;
        [SerializeField] private InstrumentAsset initialInstrument;
        [SerializeField] private DetectionProfileAsset customProfileOverride;
        [SerializeField] private InputModality initialModality = InputModality.MicrophoneHum;
        [SerializeField] private string microphoneDeviceName = "";
        [SerializeField] private int microphoneSampleRate = 44100;

        [Header("Session Behavior")]
        [SerializeField] private bool startListeningOnEnable = false;
        [SerializeField] private bool calibrateAmbientNoiseOnStart = false;
        [SerializeField] private float ambientCalibrationSeconds = 0.8f;
        [SerializeField] private bool useAsyncBurstJobs = true;
        [SerializeField] private float prefixDisambiguationSeconds = 0.65f;
        [SerializeField] private float matchCooldownSeconds = 0.35f;

        [Header("Optional Tone Feedback")]
        [SerializeField] private ProceduralOcarinaSynth toneSynth;
        [Tooltip("Play synthesized ocarina notes when using DiscreteButtons mode.")]
        [SerializeField] private bool playSynthInDiscreteMode = true;
        [Tooltip("Usually false to prevent speaker audio from feeding back into the microphone.")]
        [SerializeField] private bool playSynthInMicrophoneMode = false;
        [SerializeField] private bool playFanfareOnMatch = true;

        /// <summary>Invoked when listening starts (true) or stops (false); useful for ducking background music.</summary>
        [Header("Inspector Events (Wire BGM Ducking, Gameplay Triggers, UI)")]
        public BoolUnityEvent onListeningStateChanged = new BoolUnityEvent();
        /// <summary>Invoked in the Inspector when any musical note onset is confirmed.</summary>
        public NoteUnityEvent onNoteDetected = new NoteUnityEvent();
        /// <summary>Invoked in the Inspector when partial song-prefix progress updates during Free Play.</summary>
        public SongProgressUnityEvent onMatchProgress = new SongProgressUnityEvent();
        /// <summary>Invoked in the Inspector when a song is fully recognized.</summary>
        public SongMatchUnityEvent onSongMatched = new SongMatchUnityEvent();
        /// <summary>Invoked in the Inspector on each Guided/Learn Mode step evaluation.</summary>
        public GuidedStepUnityEvent onGuidedStep = new GuidedStepUnityEvent();
        /// <summary>Invoked in the Inspector when a song transitions to unlocked in the repertoire.</summary>
        public StringUnityEvent onSongUnlocked = new StringUnityEvent();
        /// <summary>Invoked in the Inspector when an <see cref="InstrumentAsset"/> is equipped or cleared.</summary>
        public InstrumentUnityEvent onInstrumentEquipped = new InstrumentUnityEvent();

        private SongRepertoire _repertoire;
        private MelodyMatcher _matcher;
        private CustomSongRecorder _customRecorder;
        private MicrophoneNoteInputSource _micInput;
        private DiscreteNoteInputSource _discreteInput;

        private InputModality _activeModality;
        private InstrumentAsset _equippedInstrument;
        private bool _hasActiveRuntimeProfileOverride;
        private DetectionProfileConfig _activeRuntimeProfileOverride;
        private bool _isListening;
        private bool _initialized;

        /// <summary>Runtime song repertoire managed by this controller (lazily initialized on first access).</summary>
        public SongRepertoire Repertoire { get { EnsureInitialized(); return _repertoire; } }
        /// <summary>Runtime melody matcher wired to <see cref="Repertoire"/> (lazily initialized on first access).</summary>
        public MelodyMatcher Matcher { get { EnsureInitialized(); return _matcher; } }
        /// <summary>Runtime recorder used for player-authored custom songs.</summary>
        public CustomSongRecorder CustomRecorder { get { EnsureInitialized(); return _customRecorder; } }
        /// <summary>Discrete note input source used for keyboard, gamepad, or UI button playback.</summary>
        public DiscreteNoteInputSource DiscreteInput { get { EnsureInitialized(); return _discreteInput; } }
        /// <summary>Active microphone DSP input source, or null until a microphone modality has started or a custom capture source is attached.</summary>
        public MicrophoneNoteInputSource MicrophoneInput => _micInput;
        /// <summary>Currently selected input modality.</summary>
        public InputModality ActiveModality => _activeModality;
        /// <summary>Currently equipped <see cref="InstrumentAsset"/>, or null if none is equipped.</summary>
        public InstrumentAsset EquippedInstrument => _equippedInstrument;
        /// <summary>Effective <see cref="DetectionProfileConfig"/> resolved for <see cref="ActiveModality"/>.</summary>
        public DetectionProfileConfig ActiveProfileConfig => ResolveProfileConfig(_activeModality);
        /// <summary>True while this controller is actively polling audio or discrete input.</summary>
        public bool IsListening => _isListening;
        /// <summary>Most recent <see cref="PitchFrame"/> from the microphone pipeline, or <c>default</c> when inactive or in discrete mode.</summary>
        public PitchFrame LatestPitchFrame => _micInput != null && IsMicrophoneModality(_activeModality) ? _micInput.LatestPitchFrame : default;
        /// <summary>Most recent bandpass-filtered microphone RMS level, or 0 when inactive or in discrete mode.</summary>
        public float LatestRms => _micInput != null && IsMicrophoneModality(_activeModality) ? _micInput.LatestRms : 0f;

        /// <summary>C# event raised on every raw <see cref="PitchFrame"/> from the active input source.</summary>
        public event Action<PitchFrame> OnPitchFrame;
        /// <summary>C# event raised when a new musical note onset is confirmed.</summary>
        public event Action<NoteEvent> OnNoteDetectedEvent;
        /// <summary>C# event raised when Free Play partial prefix progress changes.</summary>
        public event Action<SongMatchProgress> OnMatchProgressEvent;
        /// <summary>C# event raised when a song is fully recognized.</summary>
        public event Action<SongMatchResult> OnSongMatchedEvent;
        /// <summary>C# event raised on every Guided/Learn Mode step evaluation.</summary>
        public event Action<GuidedStepResult> OnGuidedStepEvent;
        /// <summary>C# event raised when <see cref="IsListening"/> transitions.</summary>
        public event Action<bool> OnListeningStateChangedEvent;
        /// <summary>C# event raised when <see cref="ActiveModality"/> changes.</summary>
        public event Action<InputModality> OnModalityChangedEvent;
        /// <summary>C# event raised when an <see cref="InstrumentAsset"/> is equipped or cleared.</summary>
        public event Action<InstrumentAsset> OnInstrumentEquippedEvent;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (startListeningOnEnable)
            {
                StartListening();
            }
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void OnDestroy()
        {
            DisposeMicrophoneInput();
        }

        private void Update()
        {
            if (!_initialized || !_isListening) return;

            double now = Time.unscaledTimeAsDouble;

            if (IsMicrophoneModality(_activeModality))
            {
                _micInput?.Pump(useAsyncBurstJobs);
            }
            else
            {
                _discreteInput.Tick(now);
            }

            _matcher.Tick(now);
        }

        /// <summary>
        /// Lazily initializes the repertoire, matcher, recorder, discrete input, synth reference, and initial instrument/profile.
        /// Safe to call multiple times or before <c>Awake</c> (for example in EditMode tests).
        /// </summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;

            _repertoire = new SongRepertoire();
            if (songLibrary != null)
            {
                _repertoire.LoadFromLibrary(songLibrary, clearExisting: true);
            }

            _repertoire.OnSongUnlocked += id => onSongUnlocked?.Invoke(id);

            _matcher = new MelodyMatcher(_repertoire)
            {
                PrefixDisambiguationSeconds = prefixDisambiguationSeconds,
                MatchCooldownSeconds = matchCooldownSeconds
            };

            _matcher.OnMatchProgress += progress =>
            {
                OnMatchProgressEvent?.Invoke(progress);
                onMatchProgress?.Invoke(progress);
            };

            _matcher.OnSongMatched += result =>
            {
                if (playFanfareOnMatch && toneSynth != null)
                {
                    toneSynth.PlayMatchFanfare();
                }

                OnSongMatchedEvent?.Invoke(result);
                onSongMatched?.Invoke(result);
            };

            _matcher.OnGuidedStep += step =>
            {
                OnGuidedStepEvent?.Invoke(step);
                onGuidedStep?.Invoke(step);
            };

            _customRecorder = new CustomSongRecorder();

            _discreteInput = new DiscreteNoteInputSource();
            WireNoteSource(_discreteInput);

            if (toneSynth == null)
            {
                toneSynth = GetComponentInChildren<ProceduralOcarinaSynth>();
            }

            _activeModality = initialModality;
            _initialized = true;

            if (initialInstrument != null)
            {
                EquipInstrument(initialInstrument, loadInstrumentSongs: initialInstrument.OptionalSongLibrary != null && songLibrary == null);
            }
            else
            {
                ApplyActiveProfileConfig();
            }
        }

        /// <summary>
        /// Begins listening on the active modality (starting microphone capture or enabling discrete input) and raises listening state events.
        /// </summary>
        /// <returns>True if listening started or was already active; false if microphone startup failed.</returns>
        public bool StartListening()
        {
            EnsureInitialized();
            if (_isListening) return true;

            if (IsMicrophoneModality(_activeModality))
            {
                EnsureMicrophoneInput();
                if (!_micInput.Start())
                {
                    return false;
                }

                if (calibrateAmbientNoiseOnStart)
                {
                    _micInput.BeginAmbientNoiseCalibration(ambientCalibrationSeconds);
                }
            }
            else
            {
                _discreteInput.Reset();
                _discreteInput.IsActive = true;
            }

            _isListening = true;
            OnListeningStateChangedEvent?.Invoke(true);
            onListeningStateChanged?.Invoke(true);
            return true;
        }

        /// <summary>
        /// Stops active microphone or discrete listening, silences any held tone synth note, and notifies listening-state listeners.
        /// </summary>
        public void StopListening()
        {
            if (!_isListening) return;

            _isListening = false;
            _micInput?.Stop();
            _discreteInput?.ReleaseActiveNote(Time.unscaledTimeAsDouble);
            toneSynth?.StopNote();

            OnListeningStateChangedEvent?.Invoke(false);
            onListeningStateChanged?.Invoke(false);
        }

        /// <summary>
        /// Switches between microphone presets and discrete button input, restarting listening automatically if it was already active.
        /// </summary>
        public void SetModality(InputModality modality)
        {
            EnsureInitialized();
            if (_activeModality == modality) return;

            bool wasListening = _isListening;
            if (wasListening)
            {
                StopListening();
            }

            _activeModality = modality;
            if (modality != InputModality.CustomProfile)
            {
                _hasActiveRuntimeProfileOverride = false;
            }

            ApplyActiveProfileConfig();
            OnModalityChangedEvent?.Invoke(_activeModality);

            if (wasListening)
            {
                StartListening();
            }
        }

        /// <summary>
        /// Hot-swaps the active acoustic DSP profile to a custom DetectionProfileAsset
        /// (e.g., flute, acoustic guitar, violin, soprano/baritone voice) without restarting capture.
        /// </summary>
        public void SetDetectionProfile(DetectionProfileAsset profileAsset)
        {
            EnsureInitialized();
            customProfileOverride = profileAsset;
            if (profileAsset != null)
            {
                SetDetectionProfile(profileAsset.Config);
            }
            else
            {
                _hasActiveRuntimeProfileOverride = false;
                ApplyActiveProfileConfig();
            }
        }

        /// <summary>
        /// Hot-swaps the active acoustic DSP profile to a custom DetectionProfileConfig struct.
        /// </summary>
        public void SetDetectionProfile(DetectionProfileConfig config)
        {
            EnsureInitialized();
            _activeRuntimeProfileOverride = config.Sanitized();
            _hasActiveRuntimeProfileOverride = true;

            if (IsMicrophoneModality(_activeModality) && _activeModality != InputModality.CustomProfile)
            {
                _activeModality = InputModality.CustomProfile;
                OnModalityChangedEvent?.Invoke(_activeModality);
            }

            ApplyActiveProfileConfig();
        }

        /// <summary>
        /// Equips a custom InstrumentAsset: applies its DetectionProfile, switches ProceduralOcarinaSynth
        /// to its timbre/AudioClip sample, and optionally loads its instrument-specific SongLibraryAsset.
        /// </summary>
        public void EquipInstrument(InstrumentAsset instrument, bool loadInstrumentSongs = false)
        {
            EnsureInitialized();
            _equippedInstrument = instrument;

            if (instrument == null)
            {
                _hasActiveRuntimeProfileOverride = false;
                ApplyActiveProfileConfig();
                OnInstrumentEquippedEvent?.Invoke(null);
                onInstrumentEquipped?.Invoke(null);
                return;
            }

            customProfileOverride = instrument.DetectionProfile;
            SetDetectionProfile(instrument.ResolveProfileConfig());

            if (toneSynth != null)
            {
                toneSynth.ApplyTimbre(instrument.Timbre);
            }

            if (loadInstrumentSongs && instrument.OptionalSongLibrary != null)
            {
                LoadSongLibrary(instrument.OptionalSongLibrary, clearExisting: true);
            }

            OnInstrumentEquippedEvent?.Invoke(instrument);
            onInstrumentEquipped?.Invoke(instrument);
        }

        /// <summary>
        /// Loads or replaces the active song repertoire from a SongLibraryAsset.
        /// </summary>
        public void LoadSongLibrary(SongLibraryAsset library, bool clearExisting = true)
        {
            EnsureInitialized();
            songLibrary = library;
            _repertoire.LoadFromLibrary(library, clearExisting);
            _matcher.ClearHistory();
        }

        /// <summary>
        /// Replaces the microphone capture backend with a custom <see cref="IAudioCaptureSource"/> (for example, an <see cref="AudioClipCaptureSource"/> or platform WebGL bridge).
        /// </summary>
        /// <param name="customSource">Custom PCM audio capture implementation.</param>
        /// <param name="ownsSource">When true, this controller disposes <paramref name="customSource"/> on replacement or destroy.</param>
        public void SetCustomCaptureSource(IAudioCaptureSource customSource, bool ownsSource = false)
        {
            EnsureInitialized();
            bool wasListening = _isListening;
            if (wasListening) StopListening();

            DisposeMicrophoneInput();
            DetectionProfileConfig cfg = ResolveProfileConfig(_activeModality);
            _micInput = new MicrophoneNoteInputSource(customSource, cfg, ownsSource);
            WireNoteSource(_micInput);

            if (wasListening && IsMicrophoneModality(_activeModality))
            {
                StartListening();
            }
        }

        /// <summary>
        /// Triggers an ambient room noise calibration pass when actively listening in a microphone modality.
        /// </summary>
        public void CalibrateAmbientNoise(float durationSeconds = 1.0f)
        {
            if (_micInput != null && _isListening && IsMicrophoneModality(_activeModality))
            {
                _micInput.BeginAmbientNoiseCalibration(durationSeconds);
            }
        }

        /// <summary>
        /// Presses and holds a discrete virtual note at <paramref name="midiNote"/>, starting listening automatically if idle.
        /// </summary>
        public void PressVirtualNote(int midiNote)
        {
            EnsureInitialized();
            if (!_isListening) StartListening();
            _discreteInput.PressNote(midiNote, Time.unscaledTimeAsDouble);
        }

        /// <summary>
        /// Releases any currently held virtual note on <see cref="DiscreteInput"/>.
        /// </summary>
        public void ReleaseVirtualNote()
        {
            EnsureInitialized();
            _discreteInput.ReleaseActiveNote(Time.unscaledTimeAsDouble);
        }

        /// <summary>
        /// Emits a one-shot virtual note tap at <paramref name="midiNote"/>, starting listening automatically if idle.
        /// </summary>
        public void TriggerVirtualNoteTap(int midiNote)
        {
            EnsureInitialized();
            if (!_isListening) StartListening();
            _discreteInput.TriggerNoteTap(midiNote, Time.unscaledTimeAsDouble);
        }

        /// <summary>
        /// Enters Guided/Learn Mode for a song registered in <see cref="Repertoire"/> by <paramref name="songId"/>.
        /// </summary>
        public void StartGuidedLearnSong(string songId, bool unlockOnComplete = true, bool resetOnMistake = false)
        {
            EnsureInitialized();
            if (_repertoire.TryGetSong(songId, out SongDefinition song))
            {
                _matcher.EnterGuidedLearnMode(song, unlockOnComplete, resetOnMistake);
            }
        }

        /// <summary>
        /// Returns the matcher to Free Play mode and clears its note history.
        /// </summary>
        public void EnterFreePlay()
        {
            EnsureInitialized();
            _matcher.EnterFreePlayMode();
        }

        /// <summary>
        /// Clears matcher history and starts routing incoming notes into <see cref="CustomRecorder"/>.
        /// </summary>
        public void BeginCustomSongRecording(string songId = "custom_song", string displayName = "Custom Song", int minNotes = 4, int maxNotes = 8)
        {
            EnsureInitialized();
            _matcher.ClearHistory();
            _customRecorder.BeginRecording(songId, displayName, minNotes, maxNotes);
        }

        /// <summary>
        /// Finalizes an active custom song recording, checks for interval conflicts, and registers and unlocks it in <see cref="Repertoire"/>.
        /// </summary>
        /// <param name="savedSong">Receives the saved custom <see cref="SongDefinition"/> on success.</param>
        /// <param name="error">Receives the validation failure message when returning false.</param>
        /// <returns>True if the custom melody was validated and saved.</returns>
        public bool FinishAndSaveCustomSong(out SongDefinition savedSong, out string error)
        {
            EnsureInitialized();
            if (_customRecorder.FinishRecording(out savedSong, out error, allowMonotone: false, conflictCheckRepertoire: _repertoire))
            {
                _repertoire.RegisterCustomSong(savedSong, unlockImmediately: true);
                return true;
            }

            return false;
        }

        /// <summary>Exports unlocked song IDs and custom songs from <see cref="Repertoire"/> as JSON.</summary>
        public string ExportRepertoireJson(bool prettyPrint = true) => Repertoire.ExportStateJson(prettyPrint);
        /// <summary>Imports unlocked song IDs and custom songs into <see cref="Repertoire"/> from JSON.</summary>
        public bool ImportRepertoireJson(string json) => Repertoire.ImportStateJson(json);

        /// <summary>
        /// Resolves the active <see cref="DetectionProfileConfig"/> for <paramref name="modality"/> considering runtime overrides, assigned assets, equipped instrument, and library defaults.
        /// </summary>
        public DetectionProfileConfig ResolveProfileConfig(InputModality modality)
        {
            switch (modality)
            {
                case InputModality.MicrophoneInstrument:
                    return songLibrary != null ? songLibrary.GetInstrumentConfig() : DetectionProfileConfig.CreateInstrumentPreset();
                case InputModality.MicrophoneWhistle:
                    return songLibrary != null ? songLibrary.GetWhistleConfig() : DetectionProfileConfig.CreateWhistlePreset();
                case InputModality.CustomProfile:
                    if (_hasActiveRuntimeProfileOverride) return _activeRuntimeProfileOverride;
                    if (customProfileOverride != null) return customProfileOverride.Config;
                    if (_equippedInstrument != null) return _equippedInstrument.ResolveProfileConfig();
                    return DetectionProfileConfig.CreateHumPreset();
                default:
                    if (_hasActiveRuntimeProfileOverride) return _activeRuntimeProfileOverride;
                    return songLibrary != null ? songLibrary.GetHumConfig() : DetectionProfileConfig.CreateHumPreset();
            }
        }

        /// <summary>Pushes the resolved profile configuration to the matcher, microphone pipeline, and discrete timeout.</summary>
        private void ApplyActiveProfileConfig()
        {
            DetectionProfileConfig cfg = ResolveProfileConfig(_activeModality);
            _matcher?.ApplyProfile(cfg);
            _micInput?.ApplyProfile(cfg);
            if (_discreteInput != null)
            {
                _discreteInput.SilenceTimeoutSeconds = cfg.PhraseSilenceTimeoutSeconds;
            }
        }

        /// <summary>Lazily instantiates and wires the default <see cref="UnityMicrophoneCaptureSource"/> pipeline.</summary>
        private void EnsureMicrophoneInput()
        {
            if (_micInput != null) return;
            var capture = new UnityMicrophoneCaptureSource(microphoneDeviceName, microphoneSampleRate);
            _micInput = new MicrophoneNoteInputSource(capture, ResolveProfileConfig(_activeModality), ownsCaptureSource: true);
            WireNoteSource(_micInput);
        }

        /// <summary>Unsubscribes from and disposes the current microphone note input source.</summary>
        private void DisposeMicrophoneInput()
        {
            if (_micInput == null) return;
            UnwireNoteSource(_micInput);
            _micInput.Dispose();
            _micInput = null;
        }

        /// <summary>Subscribes controller handlers to an <see cref="INoteInputSource"/>.</summary>
        private void WireNoteSource(INoteInputSource source)
        {
            source.OnNoteDetected += HandleNoteDetected;
            source.OnNoteEnded += HandleNoteEnded;
            source.OnSilenceTimeout += HandleSilenceTimeout;
            source.OnPitchFrame += HandlePitchFrame;
        }

        /// <summary>Unsubscribes controller handlers from an <see cref="INoteInputSource"/>.</summary>
        private void UnwireNoteSource(INoteInputSource source)
        {
            source.OnNoteDetected -= HandleNoteDetected;
            source.OnNoteEnded -= HandleNoteEnded;
            source.OnSilenceTimeout -= HandleSilenceTimeout;
            source.OnPitchFrame -= HandlePitchFrame;
        }

        /// <summary>Routes confirmed notes to optional synth feedback, custom song recording or melody matching, and outward events.</summary>
        private void HandleNoteDetected(NoteEvent note)
        {
            if (!_isListening) return;

            bool shouldSynth = note.IsDiscreteInput ? playSynthInDiscreteMode : playSynthInMicrophoneMode;
            if (shouldSynth && toneSynth != null)
            {
                toneSynth.PlayNote(note.FrequencyHz);
            }

            if (_customRecorder.IsRecording)
            {
                _customRecorder.FeedNote(note);
            }
            else
            {
                _matcher.FeedNote(note);
            }

            OnNoteDetectedEvent?.Invoke(note);
            onNoteDetected?.Invoke(note);
        }

        /// <summary>Stops tone synth playback when a note ends in a mode with synth feedback enabled.</summary>
        private void HandleNoteEnded(NoteEvent note)
        {
            if (toneSynth != null && ((note.IsDiscreteInput && playSynthInDiscreteMode) || (!note.IsDiscreteInput && playSynthInMicrophoneMode)))
            {
                toneSynth.StopNote();
            }
        }

        private void HandleSilenceTimeout()
        {
            if (!_isListening) return;
            _matcher.NotifySilenceTimeout();
        }

        private void HandlePitchFrame(PitchFrame frame)
        {
            OnPitchFrame?.Invoke(frame);
        }

        /// <summary>
        /// Returns true for all modalities that capture audio from a microphone or custom <see cref="IAudioCaptureSource"/> (all except <see cref="InputModality.DiscreteButtons"/>).
        /// </summary>
        public static bool IsMicrophoneModality(InputModality modality)
        {
            return modality != InputModality.DiscreteButtons;
        }
    }
}
