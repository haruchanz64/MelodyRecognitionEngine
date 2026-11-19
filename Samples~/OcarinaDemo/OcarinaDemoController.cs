using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Presentation;
using UnityEngine;

namespace MelodyRecognitionEngine.Samples.OcarinaDemo
{
    /// <summary>
    /// Ready-to-run 6-song demo controller showcasing Hum Mode, Instrument Mode, Whistle Mode,
    /// Discrete Button fallback, Guided/Learn Mode, BGM ducking, and a Scarecrow-style custom song slot.
    /// </summary>
    public sealed class OcarinaDemoController : MonoBehaviour
    {
        [SerializeField] private MelodyRecognitionController controller;
        [SerializeField] private MusicalStaffHUD staffHud;
        [SerializeField] private AudioSource bgmAudioSource;
        [SerializeField, Range(0f, 1f)] private float bgmDuckedVolume = 0.12f;

        private float _bgmOriginalVolume = 0.7f;

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<MelodyRecognitionController>() ?? gameObject.AddComponent<MelodyRecognitionController>();
            }

            if (GetComponent<ProceduralOcarinaSynth>() == null)
            {
                gameObject.AddComponent<ProceduralOcarinaSynth>();
            }

            if (staffHud == null)
            {
                staffHud = FindAnyObjectByType<MusicalStaffHUD>() ?? gameObject.AddComponent<MusicalStaffHUD>();
            }

            if (bgmAudioSource != null)
            {
                _bgmOriginalVolume = bgmAudioSource.volume;
            }

            RegisterDemoSongs();
            staffHud.BindController(controller);

            controller.OnListeningStateChangedEvent += HandleListeningStateChanged;
            controller.OnSongMatchedEvent += HandleSongMatched;
            controller.OnGuidedStepEvent += HandleGuidedStep;
        }

        private void Start()
        {
            controller.StartListening();
        }

        private void RegisterDemoSongs()
        {
            var rep = controller.Repertoire;
            rep.RegisterSong(SongDefinition.FromNotation("song_royal_lullaby", "Royal Lullaby", "B4:2 D5:1 A4:3 B4:2 D5:1 A4:3", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("song_meadow_call", "Meadow Call", "D5 B4 A4 D5 B4 A4", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("song_forest_dance", "Forest Dance", "F4 A4 B4 F4 A4 B4", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("song_dawn_chorale", "Dawn Chorale", "A4 F4 D5 A4 F4 D5", unlockedByDefault: false));
            rep.RegisterSong(SongDefinition.FromNotation("song_storm_waltz", "Storm Waltz", "D4 F4 D5 D4 F4 D5", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("song_secret_chime", "Secret Sanctuary Chime", "E5 D5 B4 A4 G4 E4", unlockedByDefault: false, alwaysDiscoverable: true));
        }

        public void StartLearnDawnChorale()
        {
            controller.StartGuidedLearnSong("song_dawn_chorale", unlockOnComplete: true, resetOnMistake: false);
            staffHud.RefreshGuidedGhosts();
        }

        public void StartRecordScarecrowSong()
        {
            controller.BeginCustomSongRecording("song_scarecrow_custom", "Scarecrow Custom Song", minNotes: 4, maxNotes: 8);
        }

        public void SaveScarecrowSong()
        {
            if (controller.FinishAndSaveCustomSong(out SongDefinition saved, out string error))
            {
                Debug.Log($"[OcarinaDemo] Saved custom song: {saved.DisplayName} ({saved.ToNotationString()})");
            }
            else
            {
                Debug.LogWarning($"[OcarinaDemo] Could not save custom song: {error}");
            }
        }

        private void HandleListeningStateChanged(bool isListening)
        {
            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = isListening ? bgmDuckedVolume : _bgmOriginalVolume;
            }
        }

        private void HandleSongMatched(SongMatchResult result)
        {
            Debug.Log($"[OcarinaDemo] Matched '{result.DisplayName}' (id={result.SongId}, conf={result.Confidence:P0}, err={result.AveragePitchErrorCents:0.0}c, shift={result.TranspositionSemitones:+0;-0;0} st)");
        }

        private void HandleGuidedStep(GuidedStepResult step)
        {
            Debug.Log($"[OcarinaDemo] Learn Step {step.StepIndex + 1}/{step.TotalSteps}: {(step.IsCorrect ? "OK" : "Retry")} (played {MusicalNote.MidiToName(step.PlayedMidiNote)})");
        }
    }
}
