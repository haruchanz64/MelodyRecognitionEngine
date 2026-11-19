using System.Collections.Generic;
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Input;
using MelodyRecognitionEngine.Matching;
using MelodyRecognitionEngine.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace MelodyRecognitionEngine.Tests
{
    public sealed class RepertoireAndIntegrationTests
    {
        private const int SampleRate = 44100;

        [Test]
        public void UnlockGating_BlocksLockedSongUntilUnlocked_AllowsAlwaysDiscoverableSecret()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("locked_song", "Locked", "E4 G4 B4 E5", unlockedByDefault: false, alwaysDiscoverable: false));
            rep.RegisterSong(SongDefinition.FromNotation("secret_song", "Secret", "C5 B4 G4 E4", unlockedByDefault: false, alwaysDiscoverable: true));

            var matcher = new MelodyMatcher(rep);
            var matched = new List<string>();
            matcher.OnSongMatched += r => matched.Add(r.SongId);

            var discrete = new DiscreteNoteInputSource();
            discrete.OnNoteDetected += matcher.FeedNote;

            // Play locked_song while locked -> must NOT trigger.
            int[] lockedNotes = { 64, 67, 71, 76 };
            for (int i = 0; i < lockedNotes.Length; i++) discrete.TriggerNoteTap(lockedNotes[i], i * 0.3);
            Assert.AreEqual(0, matched.Count, "Locked song must not trigger before unlocking.");

            // Play secret_song while locked -> MUST trigger because AlwaysDiscoverable = true.
            int[] secretNotes = { 72, 71, 67, 64 };
            for (int i = 0; i < secretNotes.Length; i++) discrete.TriggerNoteTap(secretNotes[i], 2.0 + i * 0.3);
            Assert.AreEqual(1, matched.Count);
            Assert.AreEqual("secret_song", matched[0]);

            // Unlock locked_song and replay -> MUST trigger.
            rep.UnlockSong("locked_song");
            for (int i = 0; i < lockedNotes.Length; i++) discrete.TriggerNoteTap(lockedNotes[i], 5.0 + i * 0.3);
            Assert.AreEqual(2, matched.Count);
            Assert.AreEqual("locked_song", matched[1]);
        }

        [Test]
        public void CustomSongRecorder_RecordsValidatesSerializesAndMatches()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("existing", "Existing", "B4 D5 A4 B4", unlockedByDefault: true));

            var recorder = new CustomSongRecorder();
            recorder.BeginRecording("scarecrow", "Scarecrow Song", minNotes: 4, maxNotes: 8);

            // Record a 5-note custom melody: D4 G4 A4 F4 D5.
            int[] customMidi = { 62, 67, 69, 65, 74 };
            for (int i = 0; i < customMidi.Length; i++)
            {
                recorder.FeedNote(new NoteEvent
                {
                    MidiNote = customMidi[i],
                    ExactMidiFloat = customMidi[i],
                    StartTimeSeconds = i * 0.35
                });
            }

            Assert.IsTrue(recorder.FinishRecording(out SongDefinition recorded, out string err, conflictCheckRepertoire: rep), err);
            Assert.IsTrue(recorded.IsCustomSong);
            Assert.AreEqual(5, recorded.NoteCount);

            rep.RegisterCustomSong(recorded, unlockImmediately: true);
            string json = rep.ExportStateJson();

            // Restore into a fresh repertoire and verify it recognizes the saved custom song!
            var restoredRep = new SongRepertoire();
            restoredRep.RegisterSong(SongDefinition.FromNotation("existing", "Existing", "B4 D5 A4 B4", unlockedByDefault: false));
            Assert.IsTrue(restoredRep.ImportStateJson(json));
            Assert.IsTrue(restoredRep.IsUnlocked("scarecrow"));

            var matcher = new MelodyMatcher(restoredRep);
            SongMatchResult? result = null;
            matcher.OnSongMatched += r => result = r;

            var discrete = new DiscreteNoteInputSource();
            discrete.OnNoteDetected += matcher.FeedNote;

            // Replay transposed down -3 semitones.
            for (int i = 0; i < customMidi.Length; i++)
            {
                discrete.TriggerNoteTap(customMidi[i] - 3, i * 0.3);
            }

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual("scarecrow", result.Value.SongId);
            Assert.IsTrue(result.Value.IsCustomSong);
        }

        [Test]
        public void EndToEnd_HumMode_TransposedVibratoHumRecognizesFullMelody()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("royal_lullaby", "Royal Lullaby", "B4 D5 A4 B4 D5 A4", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("meadow_call", "Meadow Call", "D5 B4 A4 D5 B4 A4", unlockedByDefault: true));

            DetectionProfileConfig humProfile = DetectionProfileConfig.CreateHumPreset();
            var matcher = new MelodyMatcher(rep);
            matcher.ApplyProfile(humProfile);

            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // Synthesize a human baritone humming Royal Lullaby an octave and a third lower (-15 semitones: G#3 B3 F#3 G#3 B3 F#3)
            // with vibrato, harmonics, and light room noise.
            int[] target = { 71, 74, 69, 71, 74, 69 };
            var specs = new List<SyntheticAudioGenerator.NoteSpec>();
            for (int i = 0; i < target.Length; i++)
            {
                specs.Add(new SyntheticAudioGenerator.NoteSpec(target[i] - 15, durationSeconds: 0.34f, gapAfterSeconds: 0.055f));
            }

            float[] pcm = SyntheticAudioGenerator.GenerateMelody(specs, SampleRate, SyntheticAudioGenerator.SynthStyle.HumanHum);
            var capture = new AudioClipCaptureSource(pcm, SampleRate, "BaritoneHum");

            using (var mic = new MicrophoneNoteInputSource(capture, humProfile))
            {
                mic.OnNoteDetected += matcher.FeedNote;
                mic.DrainAllSynchronous();
                matcher.Tick(mic.PitchTracker.CurrentStreamTimeSeconds + 1.0);
            }

            Assert.IsTrue(matched.HasValue, "Expected full end-to-end Burst DSP -> NoteSegmenter -> MelodyMatcher recognition for transposed baritone hum.");
            Assert.AreEqual("royal_lullaby", matched.Value.SongId);
            Assert.AreEqual(-15, matched.Value.TranspositionSemitones);
        }

        [Test]
        public void EndToEnd_InstrumentMode_OcarinaWithRepeatedNotesRecognizesFullMelody()
        {
            var rep = new SongRepertoire();
            // Song with consecutive repeated notes (A4 -> A4 -> D5 -> F5 -> D5 -> A4)
            rep.RegisterSong(SongDefinition.FromNotation("echo_chime", "Echo Chime", "A4 A4 D5 F5 D5 A4", unlockedByDefault: true));

            DetectionProfileConfig instProfile = DetectionProfileConfig.CreateInstrumentPreset();
            var matcher = new MelodyMatcher(rep);
            matcher.ApplyProfile(instProfile);

            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            int[] notes = { 69, 69, 74, 77, 74, 69 };
            var specs = new List<SyntheticAudioGenerator.NoteSpec>();
            for (int i = 0; i < notes.Length; i++)
            {
                specs.Add(new SyntheticAudioGenerator.NoteSpec(notes[i] + 5, durationSeconds: 0.26f, gapAfterSeconds: 0.055f));
            }

            float[] pcm = SyntheticAudioGenerator.GenerateMelody(specs, SampleRate, SyntheticAudioGenerator.SynthStyle.OcarinaInstrument);
            var capture = new AudioClipCaptureSource(pcm, SampleRate, "OcarinaPlay");

            using (var mic = new MicrophoneNoteInputSource(capture, instProfile))
            {
                mic.OnNoteDetected += matcher.FeedNote;
                mic.DrainAllSynchronous();
                matcher.Tick(mic.PitchTracker.CurrentStreamTimeSeconds + 1.0);
            }

            Assert.IsTrue(matched.HasValue, "Expected Instrument Mode to recognize repeated-note ocarina melody.");
            Assert.AreEqual("echo_chime", matched.Value.SongId);
            Assert.AreEqual(5, matched.Value.TranspositionSemitones);
        }

        [Test]
        public void MusicalNotation_ParseAndFormatRoundTrip()
        {
            const string notation = "B4:2 D5:1 F#5:1.5 Bb4:1";
            Assert.IsTrue(MusicalNote.TryParseNotationSequence(notation, out List<int> notes, out List<float> durs));
            Assert.AreEqual(4, notes.Count);
            Assert.AreEqual(71, notes[0]); // B4
            Assert.AreEqual(74, notes[1]); // D5
            Assert.AreEqual(78, notes[2]); // F#5
            Assert.AreEqual(70, notes[3]); // Bb4 == A#4
            Assert.AreEqual(1.5f, durs[2], 0.001f);

            string formatted = MusicalNote.FormatSequence(notes, durs);
            Assert.AreEqual("B4:2 D5:1 F#5:1.5 A#4:1", formatted);
        }

        [Test]
        public void Controller_SetDetectionProfileAndEquipInstrument_HotSwapsProfileTimbreAndSongs()
        {
            var go = new GameObject("TestMelodyController");
            var profile = ScriptableObject.CreateInstance<DetectionProfileAsset>();
            var songAsset = ScriptableObject.CreateInstance<SongAsset>();
            var library = ScriptableObject.CreateInstance<SongLibraryAsset>();
            var instrument = ScriptableObject.CreateInstance<InstrumentAsset>();

            try
            {
                var controller = go.AddComponent<MelodyRecognitionController>();
                var synth = go.AddComponent<ProceduralOcarinaSynth>();

                profile.ApplyPreset(DetectionPresetKind.Whistle);
                songAsset.SetDefinition(SongDefinition.FromNotation("harp_arpeggio", "Harp Arpeggio", "C4 E4 G4 B4 D5 F#5 A5 C6", unlockedByDefault: true));
                library.SetSongs(new[] { songAsset });

                var slots = new List<InstrumentNoteSlot>();
                for (int midi = 60; midi <= 71; midi++)
                {
                    slots.Add(new InstrumentNoteSlot(MusicalNote.MidiToName(midi), midi));
                }

                instrument.Configure(
                    "celestial_harp",
                    "Celestial Harp",
                    profile,
                    InstrumentTimbreConfig.CreatePreset(InstrumentWaveformPreset.HarpPluck),
                    slots,
                    library);

                controller.EnsureInitialized();
                controller.SetDetectionProfile(profile);
                Assert.AreEqual(profile.Config.MinFrequencyHz, controller.ActiveProfileConfig.MinFrequencyHz, 0.01f);

                InstrumentAsset equippedEventArg = null;
                controller.OnInstrumentEquippedEvent += inst => equippedEventArg = inst;

                controller.EquipInstrument(instrument, loadInstrumentSongs: true);

                Assert.AreSame(instrument, controller.EquippedInstrument);
                Assert.AreSame(instrument, equippedEventArg);
                Assert.AreEqual(12, controller.EquippedInstrument.NoteLayout.Count);
                Assert.AreEqual(InstrumentWaveformPreset.HarpPluck, synth.ActiveWaveform);
                Assert.IsTrue(controller.Repertoire.TryGetSong("harp_arpeggio", out _));

                // Verify chromatic 8-note song from the instrument's library matches via virtual input.
                controller.SetModality(InputModality.DiscreteButtons);
                controller.StartListening();
                SongMatchResult? matched = null;
                controller.OnSongMatchedEvent += r => matched = r;

                int[] harpNotes = { 60, 64, 67, 71, 74, 78, 81, 84 };
                for (int i = 0; i < harpNotes.Length; i++)
                {
                    controller.TriggerVirtualNoteTap(harpNotes[i]);
                }

                Assert.IsTrue(matched.HasValue, "Expected 8-note chromatic song from equipped InstrumentAsset library to match.");
                Assert.AreEqual("harp_arpeggio", matched.Value.SongId);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(songAsset);
                Object.DestroyImmediate(library);
                Object.DestroyImmediate(instrument);
            }
        }
    }
}
