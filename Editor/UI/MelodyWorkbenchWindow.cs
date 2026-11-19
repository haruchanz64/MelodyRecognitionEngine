using System;
using System.Collections.Generic;
using System.Text;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Input;
using MelodyRecognitionEngine.Matching;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MelodyRecognitionEngine.Editor.UI
{
    /// <summary>
    /// UI Toolkit Workbench for live microphone DSP tuning, Mic/Piano SongAsset authoring,
    /// and deterministic dry-run melody recognition simulation.
    /// </summary>
    public sealed class MelodyWorkbenchWindow : EditorWindow
    {
        private enum WorkbenchTab
        {
            LiveTuner = 0,
            SongAuthoring = 1,
            DryRunSimulator = 2
        }

        private WorkbenchTab _activeTab = WorkbenchTab.LiveTuner;
        private VisualElement _tunerPane;
        private VisualElement _authorPane;
        private VisualElement _simPane;
        private Button _tabTunerBtn;
        private Button _tabAuthorBtn;
        private Button _tabSimBtn;

        // Live Tuner state.
        private DropdownField _deviceDropdown;
        private EnumField _presetField;
        private ObjectField _customProfileField;
        private Button _micToggleBtn;
        private Label _tunerNoteLabel;
        private Label _tunerDetailsLabel;
        private VisualElement _rmsFillBar;
        private VisualElement _liveNoteChips;
        private MicrophoneNoteInputSource _liveMicSource;
        private readonly List<string> _liveRecentNotes = new List<string>();
        private bool _isRecordingAuthorNotes;

        // Song Authoring state.
        private ObjectField _authorTargetSongField;
        private TextField _authorSongIdField;
        private TextField _authorDisplayNameField;
        private TextField _authorNotationField;
        private EnumField _authorPitchModeField;
        private Toggle _authorValidateRhythmToggle;
        private Toggle _authorAlwaysDiscoverableToggle;
        private Label _authorIntervalsLabel;
        private Button _recordMicAuthorBtn;
        private VisualElement _pianoRow;
        private Label _pianoOctaveLabel;
        private int _pianoBaseOctave = 4;

        // Dry-Run Simulator state.
        private ObjectField _simLibraryField;
        private ObjectField _simSongField;
        private ObjectField _simAudioClipField;
        private EnumField _simPresetField;
        private SliderInt _simTransposeSlider;
        private Slider _simNoiseSlider;
        private Label _simOutputLabel;

        [MenuItem("Tools/MelodyRecognitionEngine/Melody Workbench", false, 10)]
        public static void OpenWindow()
        {
            var window = GetWindow<MelodyWorkbenchWindow>();
            window.titleContent = new GUIContent("Melody Workbench");
            window.minSize = new Vector2(360f, 360f);
            window.Show();
        }

        public static void OpenForSong(SongAsset songAsset)
        {
            var window = GetWindow<MelodyWorkbenchWindow>();
            window.titleContent = new GUIContent("Melody Workbench");
            window.minSize = new Vector2(360f, 360f);
            window.Show();
            window.SelectTab(WorkbenchTab.SongAuthoring);
            if (window._authorTargetSongField != null)
            {
                window._authorTargetSongField.value = songAsset;
                window.LoadSongAssetIntoAuthoring(songAsset);
            }
        }

        private void OnDisable()
        {
            StopLiveMic();
        }

        private void Update()
        {
            if (_liveMicSource != null && _liveMicSource.IsActive)
            {
                _liveMicSource.Pump(useAsyncLatestJob: false);
                Repaint();
            }
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.AddToClassList("mre-root");

            string[] guids = AssetDatabase.FindAssets("MelodyWorkbenchStyles t:StyleSheet");
            if (guids.Length > 0)
            {
                var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (uss != null) root.styleSheets.Add(uss);
            }

            BuildHeader(root);
            BuildTabBar(root);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            root.Add(scroll);

            _tunerPane = BuildLiveTunerPane();
            _authorPane = BuildSongAuthoringPane();
            _simPane = BuildSimulatorPane();

            scroll.Add(_tunerPane);
            scroll.Add(_authorPane);
            scroll.Add(_simPane);

            SelectTab(_activeTab);
        }

        private void BuildHeader(VisualElement root)
        {
            var header = new VisualElement();
            header.AddToClassList("mre-header");

            var left = new VisualElement();
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;
            var title = new Label("MelodyRecognitionEngine Workbench");
            title.AddToClassList("mre-title");
            var sub = new Label("Burst YIN Pitch Tuner • Mic/Piano Song Authoring • Dry-Run Simulator");
            sub.AddToClassList("mre-subtitle");
            left.Add(title);
            left.Add(sub);
            header.Add(left);

            root.Add(header);
        }

        private void BuildTabBar(VisualElement root)
        {
            var tabs = new VisualElement();
            tabs.AddToClassList("mre-tabs");

            _tabTunerBtn = new Button(() => SelectTab(WorkbenchTab.LiveTuner)) { text = "Live Mic Tuner" };
            _tabTunerBtn.AddToClassList("mre-tab-btn");

            _tabAuthorBtn = new Button(() => SelectTab(WorkbenchTab.SongAuthoring)) { text = "Song Authoring" };
            _tabAuthorBtn.AddToClassList("mre-tab-btn");

            _tabSimBtn = new Button(() => SelectTab(WorkbenchTab.DryRunSimulator)) { text = "Dry-Run Simulator" };
            _tabSimBtn.AddToClassList("mre-tab-btn");

            tabs.Add(_tabTunerBtn);
            tabs.Add(_tabAuthorBtn);
            tabs.Add(_tabSimBtn);
            root.Add(tabs);
        }

        private void SelectTab(WorkbenchTab tab)
        {
            _activeTab = tab;
            if (_tunerPane != null) _tunerPane.style.display = tab == WorkbenchTab.LiveTuner ? DisplayStyle.Flex : DisplayStyle.None;
            if (_authorPane != null) _authorPane.style.display = tab == WorkbenchTab.SongAuthoring ? DisplayStyle.Flex : DisplayStyle.None;
            if (_simPane != null) _simPane.style.display = tab == WorkbenchTab.DryRunSimulator ? DisplayStyle.Flex : DisplayStyle.None;

            _tabTunerBtn?.EnableInClassList("mre-tab-btn--active", tab == WorkbenchTab.LiveTuner);
            _tabAuthorBtn?.EnableInClassList("mre-tab-btn--active", tab == WorkbenchTab.SongAuthoring);
            _tabSimBtn?.EnableInClassList("mre-tab-btn--active", tab == WorkbenchTab.DryRunSimulator);
        }

        private VisualElement BuildLiveTunerPane()
        {
            var container = new VisualElement();

            var cfgCard = new VisualElement();
            cfgCard.AddToClassList("mre-card");
            cfgCard.Add(CreateSectionTitle("Microphone & DSP Profile"));

            var devices = new List<string>(UnityMicrophoneCaptureSource.GetAvailableDevices());
            if (devices.Count == 0) devices.Add("(No microphone detected)");
            _deviceDropdown = new DropdownField("Input Device", devices, 0);
            cfgCard.Add(_deviceDropdown);

            _presetField = new EnumField("DSP Preset", DetectionPresetKind.Hum);
            _presetField.RegisterValueChangedCallback(_ => ReconfigureLiveMic());
            cfgCard.Add(_presetField);

            _customProfileField = new ObjectField("Profile Asset Override") { objectType = typeof(DetectionProfileAsset) };
            _customProfileField.RegisterValueChangedCallback(_ => ReconfigureLiveMic());
            cfgCard.Add(_customProfileField);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("mre-row");

            _micToggleBtn = new Button(ToggleLiveMic) { text = "Start Live Mic" };
            _micToggleBtn.style.flexGrow = 1;
            var calBtn = new Button(() => _liveMicSource?.BeginAmbientNoiseCalibration(0.8f)) { text = "Calibrate Noise Floor" };
            var clearBtn = new Button(ClearLiveChips) { text = "Clear Notes" };

            btnRow.Add(_micToggleBtn);
            btnRow.Add(calBtn);
            btnRow.Add(clearBtn);
            cfgCard.Add(btnRow);
            container.Add(cfgCard);

            var monitorCard = new VisualElement();
            monitorCard.AddToClassList("mre-card");
            monitorCard.Add(CreateSectionTitle("Real-Time Pitch Readout"));

            var readout = new VisualElement();
            readout.AddToClassList("mre-tuner-readout");
            _tunerNoteLabel = new Label("--");
            _tunerNoteLabel.AddToClassList("mre-note-big");
            _tunerDetailsLabel = new Label("Inactive — click Start Live Mic to test humming or an instrument.");
            _tunerDetailsLabel.style.color = new Color(0.78f, 0.84f, 0.94f);
            readout.Add(_tunerNoteLabel);
            readout.Add(_tunerDetailsLabel);
            monitorCard.Add(readout);

            var meterBg = new VisualElement();
            meterBg.AddToClassList("mre-meter-bg");
            _rmsFillBar = new VisualElement();
            _rmsFillBar.AddToClassList("mre-meter-fill");
            meterBg.Add(_rmsFillBar);
            monitorCard.Add(meterBg);

            monitorCard.Add(CreateSectionTitle("Detected Note Stream"));
            _liveNoteChips = new VisualElement();
            _liveNoteChips.AddToClassList("mre-chip-row");
            monitorCard.Add(_liveNoteChips);

            container.Add(monitorCard);
            return container;
        }

        private VisualElement BuildSongAuthoringPane()
        {
            var container = new VisualElement();

            var card = new VisualElement();
            card.AddToClassList("mre-card");
            card.Add(CreateSectionTitle("Author or Edit SongAsset"));

            _authorTargetSongField = new ObjectField("Existing SongAsset (Optional)") { objectType = typeof(SongAsset) };
            _authorTargetSongField.RegisterValueChangedCallback(evt => LoadSongAssetIntoAuthoring(evt.newValue as SongAsset));
            card.Add(_authorTargetSongField);

            _authorSongIdField = new TextField("Song ID") { value = "song_dawn_melody" };
            _authorDisplayNameField = new TextField("Display Name") { value = "Dawn Melody" };
            _authorNotationField = new TextField("Notation (e.g. B4 D5 A4)") { value = "B4 D5 A4 B4 D5 A4" };
            _authorNotationField.RegisterValueChangedCallback(_ => UpdateAuthorIntervalsPreview());

            _authorPitchModeField = new EnumField("Pitch Mode", PitchMatchingMode.RelativeIntervals);
            _authorValidateRhythmToggle = new Toggle("Validate Rhythm Ratios");
            _authorAlwaysDiscoverableToggle = new Toggle("Always Discoverable (Secret)");

            card.Add(_authorSongIdField);
            card.Add(_authorDisplayNameField);
            card.Add(_authorNotationField);
            card.Add(_authorPitchModeField);
            card.Add(_authorValidateRhythmToggle);
            card.Add(_authorAlwaysDiscoverableToggle);

            _authorIntervalsLabel = new Label();
            _authorIntervalsLabel.style.marginTop = 4;
            _authorIntervalsLabel.style.color = new Color(0.62f, 0.85f, 1f);
            card.Add(_authorIntervalsLabel);

            card.Add(CreateSectionTitle("Click Piano Keys to Append Notes"));

            var octaveRow = new VisualElement();
            octaveRow.AddToClassList("mre-row");

            var octDownBtn = new Button(() => ShiftPianoOctave(-1)) { text = "- Octave" };
            _pianoOctaveLabel = new Label();
            _pianoOctaveLabel.style.flexGrow = 1;
            _pianoOctaveLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pianoOctaveLabel.style.color = new Color(0.84f, 0.90f, 1f);
            var octUpBtn = new Button(() => ShiftPianoOctave(1)) { text = "+ Octave" };
            var backspaceBtn = new Button(RemoveLastNotationNote) { text = "Backspace" };

            octaveRow.Add(octDownBtn);
            octaveRow.Add(_pianoOctaveLabel);
            octaveRow.Add(octUpBtn);
            octaveRow.Add(backspaceBtn);
            card.Add(octaveRow);

            _pianoRow = new VisualElement();
            _pianoRow.AddToClassList("mre-piano-row");
            card.Add(_pianoRow);
            RebuildPianoKeys();

            var actionRow = new VisualElement();
            actionRow.AddToClassList("mre-row");

            _recordMicAuthorBtn = new Button(ToggleAuthorMicRecord) { text = "Record Notes From Mic" };
            var clearNotationBtn = new Button(() =>
            {
                _authorNotationField.value = string.Empty;
                UpdateAuthorIntervalsPreview();
            }) { text = "Clear Notes" };
            var saveAssetBtn = new Button(SaveAuthoredSongAsset) { text = "Save / Update SongAsset" };
            saveAssetBtn.style.flexGrow = 1;

            actionRow.Add(_recordMicAuthorBtn);
            actionRow.Add(clearNotationBtn);
            actionRow.Add(saveAssetBtn);
            card.Add(actionRow);

            container.Add(card);
            UpdateAuthorIntervalsPreview();
            return container;
        }

        private VisualElement BuildSimulatorPane()
        {
            var container = new VisualElement();

            var card = new VisualElement();
            card.AddToClassList("mre-card");
            card.Add(CreateSectionTitle("Deterministic Dry-Run Simulation"));

            _simLibraryField = new ObjectField("Song Library") { objectType = typeof(SongLibraryAsset) };
            _simSongField = new ObjectField("Target Song (For Synthetic Test)") { objectType = typeof(SongAsset) };
            _simAudioClipField = new ObjectField("Optional AudioClip Input") { objectType = typeof(AudioClip) };
            _simPresetField = new EnumField("DSP Profile Preset", DetectionPresetKind.Hum);
            _simTransposeSlider = new SliderInt("Synthetic Transposition (Semitones)", -12, 12) { value = 0 };
            _simNoiseSlider = new Slider("Synthetic Background Noise", 0f, 0.12f) { value = 0.015f };

            card.Add(_simLibraryField);
            card.Add(_simSongField);
            card.Add(_simAudioClipField);
            card.Add(_simPresetField);
            card.Add(_simTransposeSlider);
            card.Add(_simNoiseSlider);

            var runBtn = new Button(RunDryRunSimulation) { text = "Run Dry-Run Simulation (Burst DSP -> Matcher)" };
            runBtn.style.height = 28;
            runBtn.style.marginTop = 6;
            card.Add(runBtn);

            _simOutputLabel = new Label("Select a SongAsset or SongLibraryAsset (and optional AudioClip) and click Run.");
            _simOutputLabel.AddToClassList("mre-log-box");
            _simOutputLabel.style.marginTop = 8;
            card.Add(_simOutputLabel);

            container.Add(card);
            return container;
        }

        private void ToggleLiveMic()
        {
            if (_liveMicSource != null && _liveMicSource.IsActive)
            {
                StopLiveMic();
            }
            else
            {
                StartLiveMic();
            }
        }

        private void StartLiveMic()
        {
            StopLiveMic();
            string device = _deviceDropdown != null && _deviceDropdown.value != "(No microphone detected)"
                ? _deviceDropdown.value
                : null;

            DetectionProfileConfig cfg = ResolveWorkbenchProfileConfig();
            var capture = new UnityMicrophoneCaptureSource(device, 44100);
            _liveMicSource = new MicrophoneNoteInputSource(capture, cfg, ownsCaptureSource: true);
            _liveMicSource.OnPitchFrame += HandleLivePitchFrame;
            _liveMicSource.OnNoteDetected += HandleLiveNoteDetected;

            if (_liveMicSource.Start())
            {
                if (_micToggleBtn != null) _micToggleBtn.text = "Stop Live Mic";
            }
            else
            {
                StopLiveMic();
                if (_tunerDetailsLabel != null) _tunerDetailsLabel.text = "Could not start microphone capture.";
            }
        }

        private void StopLiveMic()
        {
            _isRecordingAuthorNotes = false;
            if (_recordMicAuthorBtn != null) _recordMicAuthorBtn.text = "Record Notes From Mic";

            if (_liveMicSource != null)
            {
                _liveMicSource.OnPitchFrame -= HandleLivePitchFrame;
                _liveMicSource.OnNoteDetected -= HandleLiveNoteDetected;
                _liveMicSource.Dispose();
                _liveMicSource = null;
            }

            if (_micToggleBtn != null) _micToggleBtn.text = "Start Live Mic";
            if (_tunerNoteLabel != null) _tunerNoteLabel.text = "--";
        }

        private void ReconfigureLiveMic()
        {
            if (_liveMicSource != null)
            {
                _liveMicSource.ApplyProfile(ResolveWorkbenchProfileConfig());
            }
        }

        private DetectionProfileConfig ResolveWorkbenchProfileConfig()
        {
            if (_customProfileField?.value is DetectionProfileAsset customAsset)
            {
                return customAsset.Config;
            }

            var kind = _presetField != null ? (DetectionPresetKind)_presetField.value : DetectionPresetKind.Hum;
            return DetectionProfileConfig.CreatePreset(kind);
        }

        private void HandleLivePitchFrame(PitchFrame frame)
        {
            if (_rmsFillBar != null)
            {
                float pct = Mathf.Clamp01(frame.Rms * 5f) * 100f;
                _rmsFillBar.style.width = Length.Percent(pct);
            }

            if (_tunerNoteLabel != null && _tunerDetailsLabel != null)
            {
                if (frame.IsVoiced)
                {
                    _tunerNoteLabel.text = MusicalNote.MidiToName(frame.NearestMidiNote);
                    _tunerDetailsLabel.text = $"{frame.FrequencyHz:0.0} Hz | {frame.CentsOffset:+0.0;-0.0;0.0} cents | Clarity {frame.Clarity:0.00} | RMS {frame.Rms:0.000}";
                }
                else
                {
                    _tunerNoteLabel.text = "--";
                    _tunerDetailsLabel.text = $"Silence / Below Gate | RMS {frame.Rms:0.000} (Gate {_liveMicSource?.EffectiveNoiseGateRms:0.000})";
                }
            }
        }

        private void HandleLiveNoteDetected(NoteEvent note)
        {
            string name = note.NoteName;
            _liveRecentNotes.Add(name);
            if (_liveRecentNotes.Count > 16) _liveRecentNotes.RemoveAt(0);
            RebuildLiveNoteChips();

            if (_isRecordingAuthorNotes)
            {
                AppendNotationNote(name);
            }
        }

        private void RebuildLiveNoteChips()
        {
            if (_liveNoteChips == null) return;
            _liveNoteChips.Clear();
            for (int i = 0; i < _liveRecentNotes.Count; i++)
            {
                var chip = new Label(_liveRecentNotes[i]);
                chip.AddToClassList("mre-chip");
                _liveNoteChips.Add(chip);
            }
        }

        private void ClearLiveChips()
        {
            _liveRecentNotes.Clear();
            RebuildLiveNoteChips();
        }

        private void ToggleAuthorMicRecord()
        {
            if (_isRecordingAuthorNotes)
            {
                _isRecordingAuthorNotes = false;
                if (_recordMicAuthorBtn != null) _recordMicAuthorBtn.text = "Record Notes From Mic";
            }
            else
            {
                if (_liveMicSource == null || !_liveMicSource.IsActive)
                {
                    StartLiveMic();
                }

                if (_liveMicSource != null && _liveMicSource.IsActive)
                {
                    _isRecordingAuthorNotes = true;
                    if (_recordMicAuthorBtn != null) _recordMicAuthorBtn.text = "Stop Recording From Mic";
                }
            }
        }

        private void ShiftPianoOctave(int delta)
        {
            _pianoBaseOctave = Mathf.Clamp(_pianoBaseOctave + delta, 1, 6);
            RebuildPianoKeys();
        }

        private void RebuildPianoKeys()
        {
            if (_pianoRow == null) return;
            _pianoRow.Clear();

            int startMidi = (_pianoBaseOctave + 1) * 12; // C of _pianoBaseOctave
            int endMidi = Mathf.Min(108, startMidi + 16); // C..E (+1 octave + major 3rd)

            if (_pianoOctaveLabel != null)
            {
                _pianoOctaveLabel.text = $"Range: {MusicalNote.MidiToName(startMidi)} - {MusicalNote.MidiToName(endMidi)}";
            }

            for (int midi = startMidi; midi <= endMidi; midi++)
            {
                int capturedMidi = midi;
                string noteName = MusicalNote.MidiToName(capturedMidi);
                var keyBtn = new Button(() => AppendNotationNote(noteName)) { text = noteName };
                keyBtn.AddToClassList("mre-piano-key");
                if (MusicalNote.IsAccidental(capturedMidi))
                {
                    keyBtn.AddToClassList("mre-piano-key--sharp");
                }
                _pianoRow.Add(keyBtn);
            }
        }

        private void RemoveLastNotationNote()
        {
            if (_authorNotationField == null) return;
            string current = _authorNotationField.value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(current)) return;

            int lastSpace = current.LastIndexOf(' ');
            _authorNotationField.value = lastSpace > 0 ? current.Substring(0, lastSpace).TrimEnd() : string.Empty;
            UpdateAuthorIntervalsPreview();
        }

        private void AppendNotationNote(string noteName)
        {
            if (_authorNotationField == null) return;
            string current = _authorNotationField.value?.Trim() ?? string.Empty;
            _authorNotationField.value = string.IsNullOrEmpty(current) ? noteName : $"{current} {noteName}";
            UpdateAuthorIntervalsPreview();
        }

        private void LoadSongAssetIntoAuthoring(SongAsset asset)
        {
            if (asset == null || asset.Definition == null) return;
            SongDefinition def = asset.Definition;
            _authorSongIdField.value = def.SongId;
            _authorDisplayNameField.value = def.DisplayName;
            _authorNotationField.value = def.ToNotationString();
            _authorPitchModeField.value = def.PitchMode;
            _authorValidateRhythmToggle.value = def.ValidateRhythm;
            _authorAlwaysDiscoverableToggle.value = def.AlwaysDiscoverable;
            UpdateAuthorIntervalsPreview();
        }

        private void UpdateAuthorIntervalsPreview()
        {
            if (_authorIntervalsLabel == null || _authorNotationField == null) return;
            if (!MusicalNote.TryParseNotationSequence(_authorNotationField.value, out List<int> notes, out _))
            {
                _authorIntervalsLabel.text = "Invalid notation — use space-separated notes like: B4 D5 A4 B4 D5 A4";
                return;
            }

            if (notes.Count < 2)
            {
                _authorIntervalsLabel.text = $"Parsed 1 note ({MusicalNote.MidiToName(notes[0])}). Add at least 2 notes.";
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"Notes: {notes.Count} | Relative Semitones from Root: [0");
            for (int i = 1; i < notes.Count; i++)
            {
                int diff = notes[i] - notes[0];
                sb.Append($", {(diff >= 0 ? "+" : "")}{diff}");
            }
            sb.Append("]");
            _authorIntervalsLabel.text = sb.ToString();
        }

        private void SaveAuthoredSongAsset()
        {
            var def = new SongDefinition
            {
                SongId = _authorSongIdField.value.Trim(),
                DisplayName = _authorDisplayNameField.value.Trim(),
                PitchMode = (PitchMatchingMode)_authorPitchModeField.value,
                ValidateRhythm = _authorValidateRhythmToggle.value,
                AlwaysDiscoverable = _authorAlwaysDiscoverableToggle.value,
                UnlockedByDefault = true
            };

            if (!def.SetNotesFromNotation(_authorNotationField.value) || !def.IsValid)
            {
                EditorUtility.DisplayDialog("Invalid Song", "Please provide a valid Song ID and at least 2 valid notes (e.g. B4 D5 A4).", "OK");
                return;
            }

            if (_authorTargetSongField.value is SongAsset existing)
            {
                Undo.RecordObject(existing, "Update SongAsset");
                existing.SetDefinition(def);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Saved", $"Updated '{existing.name}' successfully.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save SongAsset", $"{def.SongId}.asset", "asset", "Choose where to save the SongAsset.");
            if (string.IsNullOrEmpty(path)) return;

            var created = CreateInstance<SongAsset>();
            created.SetDefinition(def);
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            _authorTargetSongField.value = created;
            Selection.activeObject = created;
        }

        private void RunDryRunSimulation()
        {
            var library = _simLibraryField?.value as SongLibraryAsset;
            var targetSong = _simSongField?.value as SongAsset;
            var clip = _simAudioClipField?.value as AudioClip;

            var repertoire = new SongRepertoire();
            if (library != null) repertoire.LoadFromLibrary(library, clearExisting: true);
            if (targetSong != null && targetSong.Definition != null)
            {
                repertoire.RegisterSong(targetSong.Definition, initialUnlockOverride: true);
            }

            if (repertoire.Count == 0)
            {
                _simOutputLabel.text = "Assign a SongLibraryAsset or Target SongAsset first.";
                return;
            }

            repertoire.UnlockAll();

            var preset = (DetectionPresetKind)_simPresetField.value;
            DetectionProfileConfig profile = DetectionProfileConfig.CreatePreset(preset);
            var matcher = new MelodyMatcher(repertoire);
            matcher.ApplyProfile(profile);

            IAudioCaptureSource capture;
            string sourceSummary;
            if (clip != null)
            {
                capture = AudioClipCaptureSource.FromAudioClip(clip);
                sourceSummary = $"AudioClip '{clip.name}' ({clip.frequency} Hz, {clip.length:0.00}s)";
            }
            else
            {
                SongDefinition synthSong = targetSong != null ? targetSong.Definition : repertoire.AllSongs[0];
                int transpose = _simTransposeSlider.value;
                var specs = new List<SyntheticAudioGenerator.NoteSpec>();
                for (int i = 0; i < synthSong.Notes.Count; i++)
                {
                    float dur = Mathf.Clamp(0.28f * synthSong.Notes[i].RelativeDuration, 0.16f, 0.9f);
                    specs.Add(new SyntheticAudioGenerator.NoteSpec(synthSong.Notes[i].MidiNote + transpose, dur, 0.045f));
                }

                SyntheticAudioGenerator.SynthStyle style = preset == DetectionPresetKind.Instrument
                    ? SyntheticAudioGenerator.SynthStyle.OcarinaInstrument
                    : SyntheticAudioGenerator.SynthStyle.HumanHum;
                style.NoiseAmplitude = _simNoiseSlider.value;

                float[] pcm = SyntheticAudioGenerator.GenerateMelody(specs, 44100, style);
                capture = new AudioClipCaptureSource(pcm, 44100, "SyntheticMelody");
                sourceSummary = $"Synthetic {preset} of '{synthSong.DisplayName}' (transpose {transpose:+0;-0;0} st, noise {style.NoiseAmplitude:0.000})";
            }

            var detectedNotes = new List<NoteEvent>();
            var matches = new List<SongMatchResult>();

            using (var micSource = new MicrophoneNoteInputSource(capture, profile, ownsCaptureSource: true))
            {
                micSource.OnNoteDetected += note =>
                {
                    detectedNotes.Add(note);
                    matcher.FeedNote(note);
                };
                matcher.OnSongMatched += res => matches.Add(res);

                int frameCount = micSource.DrainAllSynchronous();
                matcher.Tick(micSource.PitchTracker.CurrentStreamTimeSeconds + 2.0);

                var sb = new StringBuilder();
                sb.AppendLine($"Source: {sourceSummary}");
                sb.AppendLine($"DSP Frames: {frameCount} | Detected Notes ({detectedNotes.Count}):");
                for (int i = 0; i < detectedNotes.Count; i++)
                {
                    sb.Append($"{detectedNotes[i].NoteName} ");
                }
                sb.AppendLine();
                sb.AppendLine($"Matches ({matches.Count}):");
                if (matches.Count == 0)
                {
                    sb.AppendLine("  (No song matched)");
                }
                else
                {
                    for (int i = 0; i < matches.Count; i++)
                    {
                        SongMatchResult m = matches[i];
                        sb.AppendLine($"  ✓ {m.DisplayName} [{m.SongId}] | Conf: {m.Confidence:P0} | AvgErr: {m.AveragePitchErrorCents:0.0}c | Shift: {m.TranspositionSemitones:+0;-0;0} st");
                    }
                }

                _simOutputLabel.text = sb.ToString();
            }
        }

        private static Label CreateSectionTitle(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("mre-section-title");
            return lbl;
        }
    }
}
