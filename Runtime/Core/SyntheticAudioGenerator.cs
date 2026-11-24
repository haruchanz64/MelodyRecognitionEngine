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
        /// <summary>
        /// Describes a single synthesized note event for <see cref="GenerateMelody"/>.
        /// </summary>
        public struct NoteSpec
        {
            /// <summary>Target MIDI pitch (may be fractional to simulate detuning).</summary>
            public float MidiNote;
            /// <summary>Voiced duration of the note in seconds.</summary>
            public float DurationSeconds;
            /// <summary>Silent gap appended after this note in seconds.</summary>
            public float GapAfterSeconds;
            /// <summary>Peak linear amplitude in [0, 1].</summary>
            public float Amplitude;

            /// <summary>
            /// Creates a note specification with default timing and amplitude suitable for DSP testing.
            /// </summary>
            /// <param name="midiNote">MIDI pitch number (integer or fractional).</param>
            /// <param name="durationSeconds">Voiced note duration in seconds.</param>
            /// <param name="gapAfterSeconds">Silence duration following the note in seconds.</param>
            /// <param name="amplitude">Peak linear amplitude in [0, 1].</param>
            public NoteSpec(float midiNote, float durationSeconds = 0.35f, float gapAfterSeconds = 0.04f, float amplitude = 0.55f)
            {
                MidiNote = midiNote;
                DurationSeconds = durationSeconds;
                GapAfterSeconds = gapAfterSeconds;
                Amplitude = amplitude;
            }
        }

        /// <summary>
        /// Acoustic synthesis parameters controlling vibrato, harmonic overtones, noise floor, and envelope ramps.
        /// </summary>
        public struct SynthStyle
        {
            /// <summary>Vibrato modulation frequency in Hz.</summary>
            public float VibratoRateHz;
            /// <summary>Peak vibrato pitch excursion in cents.</summary>
            public float VibratoDepthCents;
            /// <summary>Relative weight of the 2nd harmonic partial.</summary>
            public float Harmonic2Weight;
            /// <summary>Relative weight of the 3rd harmonic partial.</summary>
            public float Harmonic3Weight;
            /// <summary>Relative weight of the 4th harmonic partial.</summary>
            public float Harmonic4Weight;
            /// <summary>Additive uniform white noise amplitude in [0, 1].</summary>
            public float NoiseAmplitude;
            /// <summary>Linear fade-in duration in seconds.</summary>
            public float AttackSeconds;
            /// <summary>Linear fade-out duration in seconds.</summary>
            public float ReleaseSeconds;
            /// <summary>Deterministic PRNG seed used when generating additive noise.</summary>
            public int RandomSeed;

            /// <summary>Clean single-frequency sine wave preset with no vibrato, overtones, or noise.</summary>
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

            /// <summary>Vocal humming preset with moderate vibrato, soft harmonics, and light room noise.</summary>
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

            /// <summary>Ocarina-like acoustic preset with gentle vibrato and restrained upper partials.</summary>
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

            /// <summary>Overtone-heavy instrument preset designed to stress-test fundamental vs. 2nd-harmonic octave locking.</summary>
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

        /// <summary>
        /// Synthesizes a single mono PCM tone at <paramref name="frequencyHz"/> using the specified <paramref name="style"/>.
        /// </summary>
        /// <param name="frequencyHz">Fundamental frequency in Hz.</param>
        /// <param name="durationSeconds">Total clip duration in seconds.</param>
        /// <param name="sampleRate">Output sample rate in Hz.</param>
        /// <param name="style">Timbre, vibrato, noise, and envelope parameters.</param>
        /// <param name="amplitude">Peak linear amplitude in [0, 1].</param>
        /// <returns>Mono PCM sample buffer clamped to [-1, 1].</returns>
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

        /// <summary>
        /// Generates deterministic uniform white noise for noise-gate and spectral-flatness rejection tests.
        /// </summary>
        /// <param name="durationSeconds">Noise clip duration in seconds.</param>
        /// <param name="sampleRate">Output sample rate in Hz.</param>
        /// <param name="amplitude">Peak linear noise amplitude in [0, 1].</param>
        /// <param name="seed">Deterministic PRNG seed.</param>
        /// <returns>Mono PCM buffer of uniform white noise clamped to [-1, 1].</returns>
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

        /// <summary>
        /// Synthesizes a multi-note mono PCM melody with optional leading/trailing silence and continuous background noise.
        /// </summary>
        /// <param name="notes">Ordered sequence of notes and inter-note gaps to render.</param>
        /// <param name="sampleRate">Output sample rate in Hz.</param>
        /// <param name="style">Timbre, vibrato, noise, and envelope parameters.</param>
        /// <param name="leadSilenceSeconds">Silence prepended before the first note in seconds.</param>
        /// <param name="tailSilenceSeconds">Silence appended after the final note in seconds.</param>
        /// <returns>Mono PCM sample buffer clamped to [-1, 1].</returns>
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

        /// <summary>
        /// Computes a linear trapezoidal attack/release envelope gain in [0, 1] at time <paramref name="t"/>.
        /// </summary>
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
