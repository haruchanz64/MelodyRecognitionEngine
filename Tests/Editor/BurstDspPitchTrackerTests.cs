using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.DSP;
using MelodyRecognitionEngine.Input;
using NUnit.Framework;
using UnityEngine;

namespace MelodyRecognitionEngine.Tests
{
    public sealed class BurstDspPitchTrackerTests
    {
        private const int SampleRate = 44100;

        [TestCase(110.0f, 45, Description = "Low vocal hum A2")]
        [TestCase(261.6256f, 60, Description = "Middle C4")]
        [TestCase(440.0f, 69, Description = "Concert A4")]
        [TestCase(880.0f, 81, Description = "Ocarina A5")]
        [TestCase(1760.0f, 93, Description = "High Whistle/Ocarina A6")]
        public void PureSine_DetectsAccurateFrequencyAndMidiNote(float freqHz, int expectedMidi)
        {
            DetectionProfileConfig cfg = freqHz < 200f
                ? DetectionProfileConfig.CreateHumPreset()
                : DetectionProfileConfig.CreateInstrumentPreset();

            float[] pcm = SyntheticAudioGenerator.GenerateTone(freqHz, 0.25f, SampleRate, SyntheticAudioGenerator.SynthStyle.PureSine);
            var frames = new List<PitchFrame>();

            using (var tracker = new BurstPitchTracker(cfg, SampleRate))
            {
                tracker.ProcessSamples(pcm, pcm.Length, frames);
            }

            // Pick a steady-state frame near the middle of the tone.
            PitchFrame mid = frames[frames.Count / 2];
            Assert.IsTrue(mid.IsVoiced, $"Expected voiced frame for {freqHz} Hz.");
            Assert.AreEqual(expectedMidi, mid.NearestMidiNote, $"Detected {mid.FrequencyHz:0.0} Hz instead of {freqHz:0.0} Hz.");
            Assert.Less(Mathf.Abs(mid.CentsOffset), 10f, $"Expected <10 cents error, got {mid.CentsOffset:0.0}c.");
            Assert.Greater(mid.Clarity, 0.90f);
        }

        [Test]
        public void RichHarmonicInstrument_LocksOntoFundamentalWithoutOctaveError()
        {
            // Strong 2nd & 3rd harmonics can trick naive detectors into jumping +1 octave.
            float fundamentalHz = MusicalNote.MidiToFrequency(62); // D4 (293.66 Hz)
            float[] pcm = SyntheticAudioGenerator.GenerateTone(fundamentalHz, 0.30f, SampleRate, SyntheticAudioGenerator.SynthStyle.RichHarmonicInstrument);
            var frames = new List<PitchFrame>();

            using (var tracker = new BurstPitchTracker(DetectionProfileConfig.CreateInstrumentPreset(), SampleRate))
            {
                tracker.ProcessSamples(pcm, pcm.Length, frames);
            }

            int correctFrames = 0;
            int voicedFrames = 0;
            foreach (PitchFrame f in frames)
            {
                if (!f.IsVoiced) continue;
                voicedFrames++;
                if (f.NearestMidiNote == 62) correctFrames++;
            }

            Assert.Greater(voicedFrames, 5, "Expected multiple voiced frames.");
            Assert.GreaterOrEqual((float)correctFrames / voicedFrames, 0.85f, "Expected fundamental D4 without octave jumping.");
        }

        [Test]
        public void WhiteNoise_IsRejectedAsUnvoiced()
        {
            float[] noise = SyntheticAudioGenerator.GenerateWhiteNoise(0.35f, SampleRate, amplitude: 0.25f);
            var frames = new List<PitchFrame>();

            using (var tracker = new BurstPitchTracker(DetectionProfileConfig.CreateHumPreset(), SampleRate))
            {
                tracker.ProcessSamples(noise, noise.Length, frames);
            }

            Assert.Greater(frames.Count, 5);
            int voicedCount = 0;
            foreach (PitchFrame f in frames)
            {
                if (f.IsVoiced) voicedCount++;
            }

            Assert.AreEqual(0, voicedCount, "Broadband white noise must not register as voiced pitches.");
        }

        [Test]
        public void HumanVibratoHum_StaysSingleNoteWithoutSplitting()
        {
            float[] hum = SyntheticAudioGenerator.GenerateTone(
                MusicalNote.MidiToFrequency(55), // G3 (196 Hz)
                0.75f,
                SampleRate,
                SyntheticAudioGenerator.SynthStyle.HumanHum);

            var notes = new List<NoteEvent>();
            var capture = new AudioClipCaptureSource(hum, SampleRate);
            using (var mic = new MicrophoneNoteInputSource(capture, DetectionProfileConfig.CreateHumPreset()))
            {
                mic.OnNoteDetected += n => notes.Add(n);
                mic.DrainAllSynchronous();
            }

            Assert.AreEqual(1, notes.Count, "A sustained vibrato hum should segment into exactly 1 note.");
            Assert.AreEqual(55, notes[0].MidiNote);
        }

        [Test]
        public void RepeatedSamePitchNotes_RearticulateIntoDistinctNotes()
        {
            // D4 -> D4 -> A4 with brief breath/amplitude dips between identical D4 notes.
            var specs = new List<SyntheticAudioGenerator.NoteSpec>
            {
                new SyntheticAudioGenerator.NoteSpec(62, 0.28f, 0.06f), // D4
                new SyntheticAudioGenerator.NoteSpec(62, 0.28f, 0.06f), // D4
                new SyntheticAudioGenerator.NoteSpec(69, 0.30f, 0.05f)  // A4
            };

            float[] pcm = SyntheticAudioGenerator.GenerateMelody(specs, SampleRate, SyntheticAudioGenerator.SynthStyle.OcarinaInstrument);
            var notes = new List<NoteEvent>();
            var capture = new AudioClipCaptureSource(pcm, SampleRate);

            using (var mic = new MicrophoneNoteInputSource(capture, DetectionProfileConfig.CreateInstrumentPreset()))
            {
                mic.OnNoteDetected += n => notes.Add(n);
                mic.DrainAllSynchronous();
            }

            Assert.AreEqual(3, notes.Count, "Expected D4, D4, A4 to segment into 3 distinct notes.");
            Assert.AreEqual(62, notes[0].MidiNote);
            Assert.AreEqual(62, notes[1].MidiNote);
            Assert.AreEqual(69, notes[2].MidiNote);
        }

        [Test]
        public void BriefStrayBlip_IsRejectedByNoteSegmenter()
        {
            // 25ms blip is well below MinNoteDurationSeconds (85ms in Hum preset).
            var specs = new List<SyntheticAudioGenerator.NoteSpec>
            {
                new SyntheticAudioGenerator.NoteSpec(64, 0.025f, 0.12f),
                new SyntheticAudioGenerator.NoteSpec(69, 0.35f, 0.05f)
            };

            float[] pcm = SyntheticAudioGenerator.GenerateMelody(specs, SampleRate, SyntheticAudioGenerator.SynthStyle.PureSine);
            var notes = new List<NoteEvent>();
            var capture = new AudioClipCaptureSource(pcm, SampleRate);

            using (var mic = new MicrophoneNoteInputSource(capture, DetectionProfileConfig.CreateHumPreset()))
            {
                mic.OnNoteDetected += n => notes.Add(n);
                mic.DrainAllSynchronous();
            }

            Assert.AreEqual(1, notes.Count, "25ms stray blip should be filtered out.");
            Assert.AreEqual(69, notes[0].MidiNote);
        }

        [Test]
        public void AmbientNoiseCalibration_RaisesEffectiveGate()
        {
            float[] roomHum = SyntheticAudioGenerator.GenerateWhiteNoise(0.6f, SampleRate, amplitude: 0.06f);
            var capture = new AudioClipCaptureSource(roomHum, SampleRate);
            float calibratedFloor = 0f;

            using (var mic = new MicrophoneNoteInputSource(capture, DetectionProfileConfig.CreateHumPreset()))
            {
                mic.OnAmbientCalibrationCompleted += floor => calibratedFloor = floor;
                mic.Start();
                mic.BeginAmbientNoiseCalibration(0.35f);
                mic.DrainAllSynchronous();

                Assert.Greater(calibratedFloor, 0.005f, "Calibration should measure non-zero ambient noise floor.");
                Assert.GreaterOrEqual(mic.EffectiveNoiseGateRms, calibratedFloor * mic.PitchTracker.Config.NoiseFloorMultiplier - 0.0001f);
            }
        }

        [Test]
        public void AsyncJobScheduling_MatchesSynchronousOutput()
        {
            float[] pcm = SyntheticAudioGenerator.GenerateTone(440f, 0.25f, SampleRate, SyntheticAudioGenerator.SynthStyle.PureSine);
            var syncFrames = new List<PitchFrame>();
            var asyncFrames = new List<PitchFrame>();
            DetectionProfileConfig cfg = DetectionProfileConfig.CreateInstrumentPreset();

            using (var syncTracker = new BurstPitchTracker(cfg, SampleRate))
            using (var asyncTracker = new BurstPitchTracker(cfg, SampleRate))
            {
                syncTracker.ProcessSamples(pcm, pcm.Length, syncFrames);

                int offset = 0;
                var chunk = new float[1024];
                while (offset < pcm.Length)
                {
                    int len = Mathf.Min(chunk.Length, pcm.Length - offset);
                    System.Array.Copy(pcm, offset, chunk, 0, len);
                    asyncTracker.PushSamplesAsync(chunk, len, asyncFrames, useAsyncLatestFrame: true);
                    offset += len;
                }

                if (asyncTracker.CompletePendingFrame(out PitchFrame last))
                {
                    asyncFrames.Add(last);
                }
            }

            Assert.AreEqual(syncFrames.Count, asyncFrames.Count);
            PitchFrame sMid = syncFrames[syncFrames.Count / 2];
            PitchFrame aMid = asyncFrames[asyncFrames.Count / 2];
            Assert.AreEqual(sMid.NearestMidiNote, aMid.NearestMidiNote);
            Assert.AreEqual(sMid.FrequencyHz, aMid.FrequencyHz, 0.01f);
        }
    }
}
