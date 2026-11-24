using System.Collections.Generic;
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;
using UnityEngine.UI;

namespace MelodyRecognitionEngine.Presentation
{
    /// <summary>
    /// Optional drop-in uGUI 5-line Musical Staff HUD driven exclusively by MelodyRecognitionController events.
    /// Displays live pitch needle, scrolling detected notes, ghosted Guided/Learn Mode cue notes,
    /// golden song-match banner, mic RMS meter, and Hum / Instrument / Button modality buttons.
    /// </summary>
    public sealed class MusicalStaffHUD : MonoBehaviour
    {
        [Header("Binding")]
        [SerializeField] private MelodyRecognitionController controller;
        [SerializeField] private bool autoBuildLayoutIfEmpty = true;
        [SerializeField] private int maxVisibleNotes = 8;

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0.08f, 0.10f, 0.16f, 0.88f);
        [SerializeField] private Color staffLineColor = new Color(0.75f, 0.80f, 0.90f, 0.55f);
        [SerializeField] private Color detectedNoteColor = new Color(0.32f, 0.82f, 1.0f, 1.0f);
        [SerializeField] private Color ghostGuideColor = new Color(0.95f, 0.95f, 0.98f, 0.32f);
        [SerializeField] private Color matchGoldColor = new Color(1.0f, 0.84f, 0.22f, 1.0f);
        [SerializeField] private Color pitchNeedleColor = new Color(0.40f, 1.0f, 0.65f, 1.0f);

        private RectTransform _rootPanel;
        private RectTransform _staffArea;
        private RectTransform _pitchNeedle;
        private Image _micMeterFill;
        private Text _statusLabel;
        private Text _bannerLabel;
        private Text _modalityLabel;

        private readonly List<RectTransform> _noteMarkers = new List<RectTransform>();
        private readonly List<RectTransform> _ghostMarkers = new List<RectTransform>();

        private float _staffLineSpacing = 18f;
        private float _bannerTimer;
        private bool _built;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindAnyObjectByType<MelodyRecognitionController>();
            }

            if (autoBuildLayoutIfEmpty && !_built)
            {
                BuildDefaultLayout();
            }
        }

        private void OnEnable()
        {
            BindController(controller);
        }

        private void OnDisable()
        {
            UnbindController(controller);
        }

        private void Update()
        {
            if (_bannerTimer > 0f)
            {
                _bannerTimer -= Time.unscaledDeltaTime;
                if (_bannerTimer <= 0f && _bannerLabel != null)
                {
                    _bannerLabel.text = string.Empty;
                }
            }
        }

        /// <summary>
        /// Subscribes this HUD to <paramref name="target"/> events, unbinding any previously bound controller first.
        /// </summary>
        public void BindController(MelodyRecognitionController target)
        {
            if (controller != null && controller != target)
            {
                UnbindController(controller);
            }

            controller = target;
            if (controller == null) return;

            controller.OnPitchFrame += HandlePitchFrame;
            controller.OnNoteDetectedEvent += HandleNoteDetected;
            controller.OnMatchProgressEvent += HandleMatchProgress;
            controller.OnSongMatchedEvent += HandleSongMatched;
            controller.OnGuidedStepEvent += HandleGuidedStep;
            controller.OnModalityChangedEvent += HandleModalityChanged;

            if (controller.Matcher != null)
            {
                controller.Matcher.OnHistoryCleared += ClearDetectedMarkers;
            }

            RefreshModalityLabel();
            RefreshGuidedGhosts();
        }

        /// <summary>
        /// Unsubscribes this HUD from all events on <paramref name="target"/>.
        /// </summary>
        public void UnbindController(MelodyRecognitionController target)
        {
            if (target == null) return;
            target.OnPitchFrame -= HandlePitchFrame;
            target.OnNoteDetectedEvent -= HandleNoteDetected;
            target.OnMatchProgressEvent -= HandleMatchProgress;
            target.OnSongMatchedEvent -= HandleSongMatched;
            target.OnGuidedStepEvent -= HandleGuidedStep;
            target.OnModalityChangedEvent -= HandleModalityChanged;

            if (target.Matcher != null)
            {
                target.Matcher.OnHistoryCleared -= ClearDetectedMarkers;
            }
        }

        /// <summary>
        /// Procedurally constructs the default ScreenSpaceOverlay uGUI staff panel, 5 treble lines, pitch needle, RMS meter, and modality toggle buttons.
        /// Safe to call repeatedly; subsequent calls are no-ops.
        /// </summary>
        public void BuildDefaultLayout()
        {
            if (_built) return;
            _built = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("MelodyStaffCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
            }

            // Bottom-centered translucent staff container.
            _rootPanel = CreatePanel("StaffPanel", canvas.transform, panelColor);
            _rootPanel.anchorMin = new Vector2(0.5f, 0f);
            _rootPanel.anchorMax = new Vector2(0.5f, 0f);
            _rootPanel.pivot = new Vector2(0.5f, 0f);
            _rootPanel.anchoredPosition = new Vector2(0f, 24f);
            _rootPanel.sizeDelta = new Vector2(760f, 185f);

            // Header bar with status, banner, and modality buttons.
            _modalityLabel = CreateText("ModalityLabel", _rootPanel, "Mode: Hum", 14, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f));
            _modalityLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _modalityLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
            _modalityLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _modalityLabel.rectTransform.anchoredPosition = new Vector2(14f, -8f);
            _modalityLabel.rectTransform.sizeDelta = new Vector2(180f, 24f);

            _bannerLabel = CreateText("MatchBanner", _rootPanel, "", 16, TextAnchor.MiddleCenter, matchGoldColor);
            _bannerLabel.fontStyle = FontStyle.Bold;
            _bannerLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            _bannerLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _bannerLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            _bannerLabel.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            _bannerLabel.rectTransform.sizeDelta = new Vector2(300f, 26f);

            CreateModeButton("Hum", new Vector2(-150f, -8f), () => controller?.SetModality(InputModality.MicrophoneHum));
            CreateModeButton("Inst", new Vector2(-84f, -8f), () => controller?.SetModality(InputModality.MicrophoneInstrument));
            CreateModeButton("Keys", new Vector2(-18f, -8f), () => controller?.SetModality(InputModality.DiscreteButtons));

            // 5-line musical staff area.
            _staffArea = CreatePanel("StaffLinesArea", _rootPanel, new Color(0f, 0f, 0f, 0.18f));
            _staffArea.anchorMin = new Vector2(0f, 0.5f);
            _staffArea.anchorMax = new Vector2(1f, 0.5f);
            _staffArea.pivot = new Vector2(0.5f, 0.5f);
            _staffArea.anchoredPosition = new Vector2(0f, -4f);
            _staffArea.sizeDelta = new Vector2(-36f, 108f);

            // 5 staff lines centered vertically (E4, G4, B4, D5, F5).
            for (int line = 0; line < 5; line++)
            {
                RectTransform lineRect = CreatePanel($"StaffLine_{line}", _staffArea, staffLineColor);
                lineRect.anchorMin = new Vector2(0f, 0.5f);
                lineRect.anchorMax = new Vector2(1f, 0.5f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                float y = (line - 2) * _staffLineSpacing;
                lineRect.anchoredPosition = new Vector2(0f, y);
                lineRect.sizeDelta = new Vector2(-16f, 2f);
            }

            // Live pitch needle on the left edge of the staff.
            _pitchNeedle = CreatePanel("LivePitchNeedle", _staffArea, pitchNeedleColor);
            _pitchNeedle.anchorMin = new Vector2(0f, 0.5f);
            _pitchNeedle.anchorMax = new Vector2(0f, 0.5f);
            _pitchNeedle.pivot = new Vector2(0.5f, 0.5f);
            _pitchNeedle.anchoredPosition = new Vector2(20f, 0f);
            _pitchNeedle.sizeDelta = new Vector2(12f, 12f);
            _pitchNeedle.gameObject.SetActive(false);

            // Bottom mic meter + live pitch status.
            RectTransform meterBg = CreatePanel("MicMeterBg", _rootPanel, new Color(1f, 1f, 1f, 0.12f));
            meterBg.anchorMin = new Vector2(0f, 0f);
            meterBg.anchorMax = new Vector2(0f, 0f);
            meterBg.pivot = new Vector2(0f, 0f);
            meterBg.anchoredPosition = new Vector2(18f, 10f);
            meterBg.sizeDelta = new Vector2(180f, 10f);

            RectTransform meterFillRect = CreatePanel("MicMeterFill", meterBg, new Color(0.28f, 0.90f, 0.55f, 0.95f));
            meterFillRect.anchorMin = Vector2.zero;
            meterFillRect.anchorMax = new Vector2(0f, 1f);
            meterFillRect.pivot = new Vector2(0f, 0.5f);
            meterFillRect.offsetMin = Vector2.zero;
            meterFillRect.offsetMax = Vector2.zero;
            _micMeterFill = meterFillRect.GetComponent<Image>();

            _statusLabel = CreateText("PitchStatus", _rootPanel, "Listening...", 12, TextAnchor.MiddleRight, new Color(0.85f, 0.88f, 0.95f));
            _statusLabel.rectTransform.anchorMin = new Vector2(1f, 0f);
            _statusLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            _statusLabel.rectTransform.pivot = new Vector2(1f, 0f);
            _statusLabel.rectTransform.anchoredPosition = new Vector2(-18f, 6f);
            _statusLabel.rectTransform.sizeDelta = new Vector2(360f, 20f);
        }

        /// <summary>
        /// Rebuilds the translucent ghost note markers representing the active Guided/Learn Mode target song.
        /// </summary>
        public void RefreshGuidedGhosts()
        {
            ClearGhostMarkers();
            if (_staffArea == null || controller == null || controller.Matcher == null) return;
            if (controller.Matcher.PlayMode != MatcherPlayMode.GuidedLearn) return;

            SongDefinition song = controller.Matcher.GuidedTargetSong;
            if (song == null || !song.IsValid) return;

            int count = Mathf.Min(song.NoteCount, maxVisibleNotes);
            for (int i = 0; i < count; i++)
            {
                RectTransform ghost = SpawnNoteMarker(song.Notes[i].MidiNote, i, count, ghostGuideColor, isGhost: true);
                _ghostMarkers.Add(ghost);
            }
        }

        /// <summary>Updates the mic RMS meter, live pitch needle position, and frequency status label from incoming pitch frames.</summary>
        private void HandlePitchFrame(PitchFrame frame)
        {
            if (_micMeterFill != null)
            {
                float normRms = Mathf.Clamp01(frame.Rms * 5f);
                _micMeterFill.rectTransform.anchorMax = new Vector2(normRms, 1f);
            }

            if (_pitchNeedle != null)
            {
                _pitchNeedle.gameObject.SetActive(frame.IsVoiced);
                if (frame.IsVoiced)
                {
                    float y = MidiToStaffY(frame.NearestMidiNote);
                    _pitchNeedle.anchoredPosition = new Vector2(20f, y);
                }
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = frame.IsVoiced
                    ? $"{MusicalNote.MidiToName(frame.NearestMidiNote)}  {frame.FrequencyHz:0.0} Hz  ({frame.CentsOffset:+0;-0;0}c)  Clarity {frame.Clarity:0.00}"
                    : "Silence / Waiting for tone...";
            }
        }

        /// <summary>Places a new note marker on the staff, clearing earlier markers once <see cref="maxVisibleNotes"/> is reached.</summary>
        private void HandleNoteDetected(NoteEvent note)
        {
            if (_staffArea == null) return;
            if (_noteMarkers.Count >= maxVisibleNotes)
            {
                ClearDetectedMarkers();
            }

            int idx = _noteMarkers.Count;
            RectTransform marker = SpawnNoteMarker(note.MidiNote, idx, maxVisibleNotes, detectedNoteColor, isGhost: false);
            _noteMarkers.Add(marker);
        }

        /// <summary>Displays the best candidate song and note count in the header banner when no match banner is active.</summary>
        private void HandleMatchProgress(SongMatchProgress progress)
        {
            if (_bannerLabel != null && _bannerTimer <= 0f)
            {
                _bannerLabel.color = new Color(0.85f, 0.92f, 1f);
                _bannerLabel.text = $"{progress.BestCandidateDisplayName} ({progress.MatchedNoteCount}/{progress.TargetNoteCount})";
            }
        }

        /// <summary>Tints active staff markers gold and displays the matched song banner for 2.4 seconds.</summary>
        private void HandleSongMatched(SongMatchResult result)
        {
            for (int i = 0; i < _noteMarkers.Count; i++)
            {
                Image img = _noteMarkers[i].GetComponent<Image>();
                if (img != null) img.color = matchGoldColor;
            }

            if (_bannerLabel != null)
            {
                _bannerLabel.color = matchGoldColor;
                _bannerLabel.text = $"♪ {result.DisplayName} ♪";
                _bannerTimer = 2.4f;
            }
        }

        /// <summary>Refreshes ghost markers and shows step pass/retry feedback during Guided/Learn Mode.</summary>
        private void HandleGuidedStep(GuidedStepResult step)
        {
            RefreshGuidedGhosts();
            if (_bannerLabel != null)
            {
                _bannerLabel.color = step.IsCorrect ? pitchNeedleColor : new Color(1f, 0.45f, 0.45f);
                _bannerLabel.text = step.IsSongCompleted
                    ? $"Learned: {step.SongId}!"
                    : (step.IsCorrect ? $"Step {step.StepIndex + 1}/{step.TotalSteps} OK" : $"Try {MusicalNote.MidiToName(step.ExpectedMidiNote)} again");
                _bannerTimer = 1.6f;
            }
        }

        private void HandleModalityChanged(InputModality modality)
        {
            RefreshModalityLabel();
        }

        private void RefreshModalityLabel()
        {
            if (_modalityLabel == null || controller == null) return;
            _modalityLabel.text = $"Mode: {controller.ActiveModality}";
        }

        /// <summary>Destroys all currently displayed player note markers on the staff.</summary>
        private void ClearDetectedMarkers()
        {
            for (int i = 0; i < _noteMarkers.Count; i++)
            {
                if (_noteMarkers[i] != null) Destroy(_noteMarkers[i].gameObject);
            }
            _noteMarkers.Clear();
        }

        /// <summary>Destroys all Guided/Learn Mode ghost note markers on the staff.</summary>
        private void ClearGhostMarkers()
        {
            for (int i = 0; i < _ghostMarkers.Count; i++)
            {
                if (_ghostMarkers[i] != null) Destroy(_ghostMarkers[i].gameObject);
            }
            _ghostMarkers.Clear();
        }

        /// <summary>Instantiates a labeled note pill positioned horizontally by slot index and vertically by treble staff step.</summary>
        private RectTransform SpawnNoteMarker(int midiNote, int slotIndex, int totalSlots, Color color, bool isGhost)
        {
            RectTransform marker = CreatePanel(isGhost ? $"Ghost_{slotIndex}" : $"Note_{slotIndex}", _staffArea, color);
            marker.anchorMin = new Vector2(0f, 0.5f);
            marker.anchorMax = new Vector2(0f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);

            float usableWidth = Mathf.Max(200f, _staffArea.rect.width > 10f ? _staffArea.rect.width - 90f : 620f);
            float spacing = usableWidth / Mathf.Max(1, totalSlots);
            float x = 60f + slotIndex * spacing;
            float y = MidiToStaffY(midiNote);

            marker.anchoredPosition = new Vector2(x, y);
            marker.sizeDelta = isGhost ? new Vector2(22f, 16f) : new Vector2(24f, 18f);

            string labelText = MusicalNote.MidiToName(midiNote);
            Text label = CreateText("Label", marker, labelText, 10, TextAnchor.MiddleCenter, isGhost ? new Color(1f, 1f, 1f, 0.65f) : Color.black);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return marker;
        }

        /// <summary>Converts a MIDI note to vertical staff pixel offset centered on B4, octave-wrapping extreme pitches into [-4, 12] diatonic steps.</summary>
        private float MidiToStaffY(int midiNote)
        {
            // Map treble staff steps (E4 = 0 .. F5 = 8, centered on B4 = 4) into vertical pixel offset.
            int step = MusicalNote.MidiToTrebleStaffStep(midiNote);
            // Wrap extreme octaves softly into visible staff range [-4, 12] so humming low octaves still renders cleanly.
            while (step < -4) step += 7;
            while (step > 12) step -= 7;
            return (step - 4) * (_staffLineSpacing * 0.5f);
        }

        /// <summary>Creates a header modality toggle button anchored relative to the top-right corner of the staff panel.</summary>
        private void CreateModeButton(string label, Vector2 anchoredTopRight, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform btnRect = CreatePanel($"Btn_{label}", _rootPanel, new Color(0.22f, 0.27f, 0.38f, 0.95f));
            btnRect.anchorMin = new Vector2(1f, 1f);
            btnRect.anchorMax = new Vector2(1f, 1f);
            btnRect.pivot = new Vector2(1f, 1f);
            btnRect.anchoredPosition = anchoredTopRight;
            btnRect.sizeDelta = new Vector2(58f, 22f);

            Button btn = btnRect.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnRect.GetComponent<Image>();
            btn.onClick.AddListener(onClick);

            Text txt = CreateText("Text", btnRect, label, 11, TextAnchor.MiddleCenter, Color.white);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero;
            txt.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>Creates a child uGUI <see cref="Image"/> panel with the specified color.</summary>
        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Creates a child uGUI <see cref="Text"/> label using Unity's built-in LegacyRuntime font.</summary>
        private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return txt;
        }
    }
}
