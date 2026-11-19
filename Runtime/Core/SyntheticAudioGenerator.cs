using System;
using System.Collections.Generic;
using UnityEngine;

namespace MelodyRecognitionEngine.Core
{
    /// <summary>
    /// Deterministic pure-C# audio synthesizer for humming, harmonic instruments, whistles,
    /// and noise-corrupted melodies. Used by both the Editor Simulator and automated EditMode tests.
    /// </summary>
    public static class SyntheticAudioGenerator
    {
        public struct NoteSpec
        {
            public float MidiNote;
            public float DurationSeconds;
            public float GapAfterSeconds;
            public float Amplitude;

            public NoteSpec(float midiNote, float durationSeconds = 0.35f, float gapAfterSeconds = 0.04f, float amplitude = 0.55f)
            {
                MidiNote = midiNote;
                DurationSeconds = durationSeconds;
                GapAfterSeconds = gapAfterSeconds;
                Amplitude = amplitude;
            }
        }

        public struct SynthStyle
        {
            public float VibratoRateHz;
            public float VibratoDepthCents;
            public float Harmonic2Weight;
            public float Harmonic3Weight;
            public float Harmonic4Weight;
            public float NoiseAmplitude;
            public float AttackSeconds;
            public float ReleaseSeconds;
            public int RandomSeed;

            public static SynthStyle PureSine => new SynthStyle
            {
                VibratoRateHz = 0f,
                VibratoDepthCents = 0f,
                Harmonic2Weight = 0f,
                Harmonic3Weight = 0f,
                Harmonic4Weight = 0f,
                NoiseAmplitude = 0f,
                AttackSeconds = 0.015f,
                ReleaseSeconds = 0.02f,
                RandomSeed = 1337
            };

            public static SynthStyle HumanHum => new SynthStyle
            {
                VibratoRateHz = 5.4f,
                VibratoDepthCents = 22f,
                Harmonic2Weight = 0.35f,
                Harmonic3Weight = 0.14f,
                Harmonic4Weight = 0.05f,
                NoiseAmplitude = 0.015f,
                AttackSeconds = 0.045f,
                ReleaseSeconds = 0.045f,
                RandomSeed = 4242
            };

            public static SynthStyle OcarinaInstrument => new SynthStyle
            {
                VibratoRateHz = 4.2f,
                VibratoDepthCents = 8f,
                Harmonic2Weight = 0.18f,
                Harmonic3Weight = 0.06f,
                Harmonic4Weight = 0.02f,
                NoiseAmplitude = 0.012f,
                AttackSeconds = 0.012f,
                ReleaseSeconds = 0.02f,
                RandomSeed = 6464
            };

            public static SynthStyle RichHarmonicInstrument => new SynthStyle
            {
                VibratoRateHz = 3.0f,
                VibratoDepthCents = 5f,
                Harmonic2Weight = 0.72f,
                Harmonic3Weight = 0.45f,
                Harmonic4Weight = 0.22f,
                NoiseAmplitude = 0.01f,
                AttackSeconds = 0.008f,
                ReleaseSeconds = 0.025f,
                RandomSeed = 9001
            };
        }

        public static float[] GenerateTone(float frequencyHz, float durationSeconds, int sampleRate, SynthStyle style, float amplitude = 0.55f)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(durationSeconds * sampleRate));
            var buffer = new float[count];
            var rng = new System.Random(style.RandomSeed);

            double phase = 0.0;
            float norm = 1f + style.Harmonic2Weight + style.Harmonic3Weight + style.Harmonic4Weight;
            if (norm <= 0f) norm = 1f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float env = ComputeEnvelope(t, durationSeconds, style.AttackSeconds, style.ReleaseSeconds);

                float instFreq = frequencyHz;
                if (style.VibratoDepthCents > 0f && style.VibratoRateHz > 0f)
                {
                    float centsMod = style.VibratoDepthCents * Mathf.Sin(2f * Mathf.PI * style.VibratoRateHz * t);
                    instFreq *= Mathf.Pow(2f, centsMod / 1200f);
                }

                phase += (2.0 * Math.PI * instFreq) / sampleRate;
                float sample = Mathf.Sin((float)phase);
                if (style.Harmonic2Weight > 0f) sample += style.Harmonic2Weight * Mathf.Sin((float)(2.0 * phase));
                if (style.Harmonic3Weight > 0f) sample += style.Harmonic3Weight * Mathf.Sin((float)(3.0 * phase));
                if (style.Harmonic4Weight > 0f) sample += style.Harmonic4Weight * Mathf.Sin((float)(4.0 * phase));

                sample = (sample / norm) * amplitude * env;

                if (style.NoiseAmplitude > 0f)
                {
                    sample += (float)(rng.NextDouble() * 2.0 - 1.0) * style.NoiseAmplitude;
                }

                buffer[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            return buffer;
        }

        public static float[] GenerateWhiteNoise(float durationSeconds, int sampleRate, float amplitude = 0.1f, int seed = 777)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(durationSeconds * sampleRate));
            var buffer = new float[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = Mathf.Clamp((float)(rng.NextDouble() * 2.0 - 1.0) * amplitude, -1f, 1f);
            }

            return buffer;
        }

        public static float[] GenerateMelody(IReadOnlyList<NoteSpec> notes, int sampleRate, SynthStyle style, float leadSilenceSeconds = 0.08f, float tailSilenceSeconds = 0.15f)
        {
            float totalSeconds = leadSilenceSeconds + tailSilenceSeconds;
            for (int i = 0; i < notes.Count; i++)
            {
                totalSeconds += notes[i].DurationSeconds + notes[i].GapAfterSeconds;
            }

            int totalSamples = Mathf.Max(1, Mathf.RoundToInt(totalSeconds * sampleRate));
            var output = new float[totalSamples];
            var rng = new System.Random(style.RandomSeed);

            if (style.NoiseAmplitude > 0f)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    output[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * style.NoiseAmplitude;
                }
            }

            int cursor = Mathf.RoundToInt(leadSilenceSeconds * sampleRate);
            float norm = 1f + style.Harmonic2Weight + style.Harmonic3Weight + style.Harmonic4Weight;
            if (norm <= 0f) norm = 1f;

            for (int n = 0; n < notes.Count; n++)
            {
                NoteSpec spec = notes[n];
                int noteSamples = Mathf.RoundToInt(spec.DurationSeconds * sampleRate);
                float baseFreq = MusicalNote.MidiToFrequency(spec.MidiNote);
                double phase = 0.0;

                for (int i = 0; i < noteSamples && (cursor + i) < totalSamples; i++)
                {
                    float t = (float)i / sampleRate;
                    float env = ComputeEnvelope(t, spec.DurationSeconds, style.AttackSeconds, style.ReleaseSeconds);

                    float instFreq = baseFreq;
                    if (style.VibratoDepthCents > 0f && style.VibratoRateHz > 0f)
                    {
                        float centsMod = style.VibratoDepthCents * Mathf.Sin(2f * Mathf.PI * style.VibratoRateHz * t);
                        instFreq *= Mathf.Pow(2f, centsMod / 1200f);
                    }

                    phase += (2.0 * Math.PI * instFreq) / sampleRate;
                    float sample = Mathf.Sin((float)phase);
                    if (style.Harmonic2Weight > 0f) sample += style.Harmonic2Weight * Mathf.Sin((float)(2.0 * phase));
                    if (style.Harmonic3Weight > 0f) sample += style.Harmonic3Weight * Mathf.Sin((float)(3.0 * phase));
                    if (style.Harmonic4Weight > 0f) sample += style.Harmonic4Weight * Mathf.Sin((float)(4.0 * phase));

                    output[cursor + i] = Mathf.Clamp(output[cursor + i] + (sample / norm) * spec.Amplitude * env, -1f, 1f);
                }

                cursor += noteSamples + Mathf.RoundToInt(spec.GapAfterSeconds * sampleRate);
            }

            return output;
        }

        private static float ComputeEnvelope(float t, float duration, float attack, float release)
        {
            float env = 1f;
            if (attack > 0f && t < attack)
            {
                env = Mathf.Clamp01(t / attack);
            }

            float remaining = duration - t;
            if (release > 0f && remaining < release)
            {
                env = Mathf.Min(env, Mathf.Clamp01(remaining / release));
            }

            return env;
        }
    }
}
