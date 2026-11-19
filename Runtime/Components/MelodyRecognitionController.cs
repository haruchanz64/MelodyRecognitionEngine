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
    [Serializable] public sealed class BoolUnityEvent : UnityEvent<bool> { }
    [Serializable] public sealed class NoteUnityEvent : UnityEvent<NoteEvent> { }
    [Serializable] public sealed class SongMatchUnityEvent : UnityEvent<SongMatchResult> { }
    [Serializable] public sealed class SongProgressUnityEvent : UnityEvent<SongMatchProgress> { }
    [Serializable] public sealed class GuidedStepUnityEvent : UnityEvent<GuidedStepResult> { }
    [Serializable] public sealed class StringUnityEvent : UnityEvent<string> { }
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

        [Header("Inspector Events (Wire BGM Ducking, Gameplay Triggers, UI)")]
        public BoolUnityEvent onListeningStateChanged = new BoolUnityEvent();
        public NoteUnityEvent onNoteDetected = new NoteUnityEvent();
        public SongProgressUnityEvent onMatchProgress = new SongProgressUnityEvent();
        public SongMatchUnityEvent onSongMatched = new SongMatchUnityEvent();
        public GuidedStepUnityEvent onGuidedStep = new GuidedStepUnityEvent();
        public StringUnityEvent onSongUnlocked = new StringUnityEvent();
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

        public SongRepertoire Repertoire { get { EnsureInitialized(); return _repertoire; } }
        public MelodyMatcher Matcher { get { EnsureInitialized(); return _matcher; } }
        public CustomSongRecorder CustomRecorder { get { EnsureInitialized(); return _customRecorder; } }
        public DiscreteNoteInputSource DiscreteInput { get { EnsureInitialized(); return _discreteInput; } }
        public MicrophoneNoteInputSource MicrophoneInput => _micInput;
        public InputModality ActiveModality => _activeModality;
        public InstrumentAsset EquippedInstrument => _equippedInstrument;
        public DetectionProfileConfig ActiveProfileConfig => ResolveProfileConfig(_activeModality);
        public bool IsListening => _isListening;
        public PitchFrame LatestPitchFrame => _micInput != null && IsMicrophoneModality(_activeModality) ? _micInput.LatestPitchFrame : default;
        public float LatestRms => _micInput != null && IsMicrophoneModality(_activeModality) ? _micInput.LatestRms : 0f;

        public event Action<PitchFrame> OnPitchFrame;
        public event Action<NoteEvent> OnNoteDetectedEvent;
        public event Action<SongMatchProgress> OnMatchProgressEvent;
        public event Action<SongMatchResult> OnSongMatchedEvent;
        public event Action<GuidedStepResult> OnGuidedStepEvent;
        public event Action<bool> OnListeningStateChangedEvent;
        public event Action<InputModality> OnModalityChangedEvent;
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

        public void CalibrateAmbientNoise(float durationSeconds = 1.0f)
        {
            if (_micInput != null && _isListening && IsMicrophoneModality(_activeModality))
            {
                _micInput.BeginAmbientNoiseCalibration(durationSeconds);
            }
        }

        public void PressVirtualNote(int midiNote)
        {
            EnsureInitialized();
            if (!_isListening) StartListening();
            _discreteInput.PressNote(midiNote, Time.unscaledTimeAsDouble);
        }

        public void ReleaseVirtualNote()
        {
            EnsureInitialized();
            _discreteInput.ReleaseActiveNote(Time.unscaledTimeAsDouble);
        }

        public void TriggerVirtualNoteTap(int midiNote)
        {
            EnsureInitialized();
            if (!_isListening) StartListening();
            _discreteInput.TriggerNoteTap(midiNote, Time.unscaledTimeAsDouble);
        }

        public void StartGuidedLearnSong(string songId, bool unlockOnComplete = true, bool resetOnMistake = false)
        {
            EnsureInitialized();
            if (_repertoire.TryGetSong(songId, out SongDefinition song))
            {
                _matcher.EnterGuidedLearnMode(song, unlockOnComplete, resetOnMistake);
            }
        }

        public void EnterFreePlay()
        {
            EnsureInitialized();
            _matcher.EnterFreePlayMode();
        }

        public void BeginCustomSongRecording(string songId = "custom_song", string displayName = "Custom Song", int minNotes = 4, int maxNotes = 8)
        {
            EnsureInitialized();
            _matcher.ClearHistory();
            _customRecorder.BeginRecording(songId, displayName, minNotes, maxNotes);
        }

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

        public string ExportRepertoireJson(bool prettyPrint = true) => Repertoire.ExportStateJson(prettyPrint);
        public bool ImportRepertoireJson(string json) => Repertoire.ImportStateJson(json);

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

        private void EnsureMicrophoneInput()
        {
            if (_micInput != null) return;
            var capture = new UnityMicrophoneCaptureSource(microphoneDeviceName, microphoneSampleRate);
            _micInput = new MicrophoneNoteInputSource(capture, ResolveProfileConfig(_activeModality), ownsCaptureSource: true);
            WireNoteSource(_micInput);
        }

        private void DisposeMicrophoneInput()
        {
            if (_micInput == null) return;
            UnwireNoteSource(_micInput);
            _micInput.Dispose();
            _micInput = null;
        }

        private void WireNoteSource(INoteInputSource source)
        {
            source.OnNoteDetected += HandleNoteDetected;
            source.OnNoteEnded += HandleNoteEnded;
            source.OnSilenceTimeout += HandleSilenceTimeout;
            source.OnPitchFrame += HandlePitchFrame;
        }

        private void UnwireNoteSource(INoteInputSource source)
        {
            source.OnNoteDetected -= HandleNoteDetected;
            source.OnNoteEnded -= HandleNoteEnded;
            source.OnSilenceTimeout -= HandleSilenceTimeout;
            source.OnPitchFrame -= HandlePitchFrame;
        }

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

        public static bool IsMicrophoneModality(InputModality modality)
        {
            return modality != InputModality.DiscreteButtons;
        }
    }
}
