using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Matching;
using NUnit.Framework;

namespace MelodyRecognitionEngine.Tests
{
    public sealed class MelodyMatcherTests
    {
        private static NoteEvent MakeNote(float midiFloat, double time, float duration = 0.3f)
        {
            int nearest = UnityEngine.Mathf.RoundToInt(midiFloat);
            return new NoteEvent
            {
                MidiNote = nearest,
                ExactMidiFloat = midiFloat,
                FrequencyHz = MusicalNote.MidiToFrequency(midiFloat),
                CentsOffset = (midiFloat - nearest) * 100f,
                Clarity = 0.95f,
                Rms = 0.4f,
                StartTimeSeconds = time,
                DurationSeconds = duration
            };
        }

        [TestCase(0, Description = "Original key")]
        [TestCase(5, Description = "Transposed up a perfect fourth (+5 st)")]
        [TestCase(-7, Description = "Transposed down a perfect fifth (-7 st)")]
        [TestCase(-12, Description = "Shifted down one octave (-12 st)")]
        [TestCase(12, Description = "Shifted up one octave (+12 st)")]
        public void RelativeIntervalMatching_SucceedsAcrossTranspositionsAndOctaves(int transposeSemitones)
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("lullaby", "Royal Lullaby", "B4 D5 A4 B4 D5 A4", PitchMatchingMode.RelativeIntervals, unlockedByDefault: true));

            var matcher = new MelodyMatcher(rep);
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            int[] baseNotes = { 71, 74, 69, 71, 74, 69 };
            for (int i = 0; i < baseNotes.Length; i++)
            {
                matcher.FeedNote(MakeNote(baseNotes[i] + transposeSemitones, i * 0.4));
            }

            Assert.IsTrue(matched.HasValue, $"Expected match at transposition {transposeSemitones}.");
            Assert.AreEqual("lullaby", matched.Value.SongId);
            Assert.AreEqual(transposeSemitones, matched.Value.TranspositionSemitones);
            Assert.IsFalse(matched.Value.MatchedViaAbsolutePitch);
        }

        [Test]
        public void MeanCenteredRelativeMatching_AbsorbsFirstNoteFlatDrift()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("suns", "Dawn Call", "A4 F4 D5 A4 F4 D5", PitchMatchingMode.RelativeIntervals, unlockedByDefault: true));

            var matcher = new MelodyMatcher(rep) { DefaultPitchToleranceCents = 45f };
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // First note is 38 cents flat while remaining 5 notes are dead-on concert pitch.
            // A naive anchor-on-note-0 matcher would see +38c error on all 5 notes; mean-centering spreads it cleanly.
            float[] played = { 69f - 0.38f, 65f, 74f, 69f, 65f, 74f };
            for (int i = 0; i < played.Length; i++)
            {
                matcher.FeedNote(MakeNote(played[i], i * 0.35));
            }

            Assert.IsTrue(matched.HasValue);
            Assert.AreEqual("suns", matched.Value.SongId);
        }

        [Test]
        public void StrictAbsolutePitch_RejectsTransposedAndAcceptsExactConcertPitch()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("exact_chime", "Exact Chime", "C5 E5 G5 C6", PitchMatchingMode.AbsolutePitch, unlockedByDefault: true));

            var matcher = new MelodyMatcher(rep) { DefaultPitchToleranceCents = 35f };
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // Transposed up +2 semitones must NOT match.
            int[] transposed = { 74, 78, 81, 86 };
            for (int i = 0; i < transposed.Length; i++)
            {
                matcher.FeedNote(MakeNote(transposed[i], i * 0.3));
            }
            Assert.IsFalse(matched.HasValue, "Transposed melody should not match an AbsolutePitch song.");

            // Exact concert pitch C5 E5 G5 C6 MUST match.
            int[] exact = { 72, 76, 79, 84 };
            for (int i = 0; i < exact.Length; i++)
            {
                matcher.FeedNote(MakeNote(exact[i], 2.0 + i * 0.3));
            }
            Assert.IsTrue(matched.HasValue);
            Assert.AreEqual("exact_chime", matched.Value.SongId);
            Assert.IsTrue(matched.Value.MatchedViaAbsolutePitch);
        }

        [Test]
        public void SlidingWindow_IgnoresLeadingWarmupNotes()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("meadow", "Meadow Call", "D5 B4 A4 D5 B4 A4", unlockedByDefault: true));

            var matcher = new MelodyMatcher(rep);
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // 3 random warm-up notes followed immediately by the song.
            int[] stream = { 60, 63, 67, 74, 71, 69, 74, 71, 69 };
            for (int i = 0; i < stream.Length; i++)
            {
                matcher.FeedNote(MakeNote(stream[i], i * 0.35));
            }

            Assert.IsTrue(matched.HasValue);
            Assert.AreEqual("meadow", matched.Value.SongId);
        }

        [Test]
        public void StrayNoteForgiveness_MatchesWhenOneGlitchNoteOccursInsidePhrase()
        {
            var song = SongDefinition.FromNotation("forest", "Forest Dance", "F4 A4 B4 F4 A4 B4", unlockedByDefault: true);
            song.MaxStrayNotes = 1;

            var rep = new SongRepertoire();
            rep.RegisterSong(song);

            var matcher = new MelodyMatcher(rep);
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // Inject a stray C#5 (73) between note 3 and note 4.
            int[] withStray = { 65, 69, 71, 73, 65, 69, 71 };
            for (int i = 0; i < withStray.Length; i++)
            {
                matcher.FeedNote(MakeNote(withStray[i], i * 0.3));
            }

            Assert.IsTrue(matched.HasValue, "Expected song to match with MaxStrayNotes = 1.");
            Assert.AreEqual("forest", matched.Value.SongId);
        }

        [Test]
        public void OptionalRhythmValidation_DistinguishesRhythmRatios()
        {
            // Long-short-long-short pattern (3:1:3:1).
            var song = SongDefinition.FromNotation("rhythm_song", "Rhythm Song", "B4:3 D5:1 A4:3 B4:1 D5:1", validateRhythm: true, unlockedByDefault: true);
            song.RhythmTolerance = 0.25f;

            var rep = new SongRepertoire();
            rep.RegisterSong(song);

            var matcher = new MelodyMatcher(rep);
            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // Even timing (1:1:1:1) should be rejected.
            int[] notes = { 71, 74, 69, 71, 74 };
            double[] evenTimes = { 0.0, 0.3, 0.6, 0.9, 1.2 };
            for (int i = 0; i < notes.Length; i++)
            {
                matcher.FeedNote(MakeNote(notes[i], evenTimes[i]));
            }
            Assert.IsFalse(matched.HasValue, "Even rhythm must not match a 3:1:3:1 rhythm-validated song.");

            matcher.ClearHistory();

            // Accurate 3:1:3:1 inter-onset timing (at any tempo, here 0.6s / 0.2s / 0.6s / 0.2s) MUST match.
            double[] dottedTimes = { 2.0, 2.6, 2.8, 3.4, 3.6 };
            for (int i = 0; i < notes.Length; i++)
            {
                matcher.FeedNote(MakeNote(notes[i], dottedTimes[i]));
            }
            Assert.IsTrue(matched.HasValue, "Tempo-scaled 3:1:3:1 rhythm should match.");
            Assert.IsTrue(matched.Value.MatchedRhythm);
        }

        [Test]
        public void PrefixDisambiguation_AllowsLongerSongOrFiresShorterAfterTimeout()
        {
            var rep = new SongRepertoire();
            rep.RegisterSong(SongDefinition.FromNotation("short_song", "Short", "C4 D4 E4 G4", unlockedByDefault: true));
            rep.RegisterSong(SongDefinition.FromNotation("long_song", "Long", "C4 D4 E4 G4 A4 C5", unlockedByDefault: true));

            var matcher = new MelodyMatcher(rep) { PrefixDisambiguationSeconds = 0.6f };
            var matchedIds = new List<string>();
            matcher.OnSongMatched += r => matchedIds.Add(r.SongId);

            // Case 1: Play all 6 notes -> longer song must win without short_song firing prematurely.
            int[] sixNotes = { 60, 62, 64, 67, 69, 72 };
            for (int i = 0; i < sixNotes.Length; i++)
            {
                matcher.FeedNote(MakeNote(sixNotes[i], i * 0.25));
            }

            Assert.AreEqual(1, matchedIds.Count);
            Assert.AreEqual("long_song", matchedIds[0]);

            // Case 2: Play only 4 notes and pause past the 0.6s disambiguation window -> short_song must fire!
            matchedIds.Clear();
            double baseTime = 5.0;
            for (int i = 0; i < 4; i++)
            {
                matcher.FeedNote(MakeNote(sixNotes[i], baseTime + i * 0.25));
            }

            Assert.AreEqual(0, matchedIds.Count, "Short song should wait during prefix disambiguation window.");
            Assert.IsTrue(matcher.HasPendingPrefixDisambiguation);

            matcher.Tick(baseTime + 0.75 + 0.65);
            Assert.AreEqual(1, matchedIds.Count);
            Assert.AreEqual("short_song", matchedIds[0]);
        }

        [Test]
        public void GuidedLearnMode_ValidatesStepByStepAndUnlocksSongOnCompletion()
        {
            var song = SongDefinition.FromNotation("learn_me", "Locked Tune", "D4 F4 A4 D5", unlockedByDefault: false);
            var rep = new SongRepertoire();
            rep.RegisterSong(song);
            Assert.IsFalse(rep.IsUnlocked("learn_me"));

            var matcher = new MelodyMatcher(rep);
            var steps = new List<GuidedStepResult>();
            SongMatchResult? completed = null;
            matcher.OnGuidedStep += s => steps.Add(s);
            matcher.OnSongMatched += r => completed = r;

            matcher.EnterGuidedLearnMode(song, unlockOnComplete: true, resetOnMistake: false);

            // Start transposed up +3 semitones (F4 = 65); step 0 establishes anchor!
            matcher.FeedNote(MakeNote(65, 0.0)); // F4 (anchor) -> OK
            matcher.FeedNote(MakeNote(68, 0.4)); // G#4 (+3 st from F4, matching D4->F4) -> OK
            matcher.FeedNote(MakeNote(70, 0.8)); // Wrong note (A#4 instead of C5) -> Incorrect, stays at step 2
            matcher.FeedNote(MakeNote(72, 1.2)); // C5 (+7 st from F4, matching D4->A4) -> OK
            matcher.FeedNote(MakeNote(77, 1.6)); // F5 (+12 st from F4, matching D4->D5) -> Complete!

            Assert.AreEqual(5, steps.Count);
            Assert.IsTrue(steps[0].IsCorrect);
            Assert.IsTrue(steps[1].IsCorrect);
            Assert.IsFalse(steps[2].IsCorrect);
            Assert.IsTrue(steps[3].IsCorrect);
            Assert.IsTrue(steps[4].IsCorrect && steps[4].IsSongCompleted);

            Assert.IsTrue(completed.HasValue);
            Assert.AreEqual("learn_me", completed.Value.SongId);
            Assert.IsTrue(rep.IsUnlocked("learn_me"), "Completing Guided Learn Mode must unlock the song.");
        }

        [Test]
        public void AutoExpandingHistory_MatchesLong32NoteMelodyWithLeadingWarmupNotes()
        {
            var notes = new List<SongNoteEntry>();
            int[] motif = { 60, 62, 63, 65, 67, 68, 70, 72 }; // C minor scale (8 notes x 4 = 32 notes)
            for (int i = 0; i < 32; i++)
            {
                notes.Add(new SongNoteEntry(motif[i % motif.Length] + (i / 16) * 2));
            }

            var longSong = new SongDefinition
            {
                SongId = "epic_32",
                DisplayName = "Epic 32-Note Melody",
                Notes = notes,
                UnlockedByDefault = true
            };

            var rep = new SongRepertoire();
            rep.RegisterSong(longSong);

            var matcher = new MelodyMatcher(rep);
            Assert.GreaterOrEqual(matcher.EffectiveMaxHistoryLength, 40, "History capacity must auto-expand beyond default 24 for 32-note songs.");

            SongMatchResult? matched = null;
            matcher.OnSongMatched += r => matched = r;

            // Feed 6 warm-up notes followed by all 32 notes transposed +3 semitones.
            double t = 0.0;
            int[] warmup = { 55, 58, 61, 64, 59, 57 };
            for (int i = 0; i < warmup.Length; i++)
            {
                matcher.FeedNote(MakeNote(warmup[i], t));
                t += 0.25;
            }

            for (int i = 0; i < longSong.NoteCount; i++)
            {
                matcher.FeedNote(MakeNote(longSong.Notes[i].MidiNote + 3, t));
                t += 0.25;
            }

            Assert.IsTrue(matched.HasValue, "Expected 32-note melody to match via auto-expanded history window.");
            Assert.AreEqual("epic_32", matched.Value.SongId);
            Assert.AreEqual(3, matched.Value.TranspositionSemitones);
        }
    }
}
