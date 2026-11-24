using System;
using MelodyRecognitionEngine.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MelodyRecognitionEngine.DSP
{
    /// <summary>
    /// Burst-compiled monophonic pitch detection job implementing:
    /// 1) DC offset removal + cascaded 2nd-order Butterworth bandpass filter
    /// 2) Adaptive RMS noise gating and broadband-noise rejection
    /// 3) YIN Cumulative Mean Normalized Difference (CMND) with parabolic interpolation
    /// 4) Optional 2nd-harmonic octave correction for harmonically rich instruments
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct PitchDetectionJob : IJob
    {
        /// <summary>Raw mono PCM analysis window of length <see cref="FrameSize"/>.</summary>
        [ReadOnly] public NativeArray<float> InputSamples;
        /// <summary>Caller-owned scratch buffer storing DC-removed, bandpass-filtered samples.</summary>
        public NativeArray<float> FilteredSamples;
        /// <summary>Caller-owned scratch buffer storing YIN CMND values up to <c>FrameSize / 2</c>.</summary>
        public NativeArray<float> CmndBuffer;
        /// <summary>Single-element output array receiving the analyzed <see cref="PitchFrame"/>.</summary>
        [WriteOnly] public NativeArray<PitchFrame> Output;

        /// <summary>Analysis window size in samples (must be at least 64).</summary>
        public int FrameSize;
        /// <summary>Input audio sample rate in Hz.</summary>
        public int SampleRate;
        /// <summary>Lowest fundamental frequency to consider in Hz.</summary>
        public float MinFrequencyHz;
        /// <summary>Highest fundamental frequency to consider in Hz.</summary>
        public float MaxFrequencyHz;
        /// <summary>Concert pitch tuning frequency for A4 in Hz.</summary>
        public float ReferenceA4Hz;
        /// <summary>Minimum bandpass-filtered RMS required for a frame to be treated as voiced.</summary>
        public float EffectiveNoiseGateRms;
        /// <summary>YIN CMND threshold for choosing the first fundamental valley.</summary>
        public float YinThreshold;
        /// <summary>Minimum clarity (<c>1 - CMND</c>) required to emit a voiced frame.</summary>
        public float MinClarity;
        /// <summary>Maximum normalized broadband noise proxy allowed before rejecting the frame.</summary>
        public float MaxSpectralFlatness;
        /// <summary>Blittable toggle (non-zero enables 2nd-harmonic double-period verification).</summary>
        public byte EnableOctaveCorrection;
        /// <summary>Stream timestamp in seconds stamped onto the output <see cref="PitchFrame"/>.</summary>
        public double TimestampSeconds;

        /// <summary>
        /// Runs the complete bandpass, gating, YIN CMND, octave correction, and parabolic interpolation pipeline on <see cref="InputSamples"/>.
        /// </summary>
        public void Execute()
        {
            var result = new PitchFrame
            {
                FrequencyHz = 0f,
                MidiFloat = -1f,
                NearestMidiNote = -1,
                CentsOffset = 0f,
                Rms = 0f,
                Clarity = 0f,
                SpectralFlatness = 1f,
                TimestampSeconds = TimestampSeconds,
                IsVoicedByte = 0
            };

            if (FrameSize < 64 || SampleRate <= 0)
            {
                Output[0] = result;
                return;
            }

            // 1. Remove DC offset.
            float mean = 0f;
            for (int i = 0; i < FrameSize; i++)
            {
                mean += InputSamples[i];
            }
            mean /= FrameSize;

            for (int i = 0; i < FrameSize; i++)
            {
                FilteredSamples[i] = InputSamples[i] - mean;
            }

            // 2. Cascaded 2nd-order Butterworth high-pass & low-pass biquads.
            float hpCutoff = Mathf.Clamp(MinFrequencyHz * 0.78f, 35f, SampleRate * 0.25f);
            float lpCutoff = Mathf.Clamp(MaxFrequencyHz * 2.4f, hpCutoff + 100f, SampleRate * 0.45f);
            ApplyHighPassBiquad(FilteredSamples, FrameSize, SampleRate, hpCutoff);
            ApplyLowPassBiquad(FilteredSamples, FrameSize, SampleRate, lpCutoff);

            // 3. Compute RMS & broadband noise proxy (first-difference energy ratio).
            float energySum = 0f;
            float diffEnergySum = 0f;
            float prev = FilteredSamples[0];
            for (int i = 0; i < FrameSize; i++)
            {
                float s = FilteredSamples[i];
                energySum += s * s;
                float d = s - prev;
                diffEnergySum += d * d;
                prev = s;
            }

            float rms = Mathf.Sqrt(energySum / FrameSize);
            result.Rms = rms;

            // Normalized broadband noisiness proxy: white noise has diffEnergy / (2 * energy) ~ 1.0,
            // while periodic tonal audio in the vocal/instrument band is much lower.
            float noiseProxy = energySum > 1e-12f ? Mathf.Clamp01(diffEnergySum / (2f * energySum)) : 1f;
            result.SpectralFlatness = noiseProxy;

            if (rms < EffectiveNoiseGateRms)
            {
                Output[0] = result;
                return;
            }

            // 4. YIN difference function & cumulative mean normalized difference (CMND).
            int halfSize = FrameSize / 2;
            int minLag = Mathf.Max(2, (int)Mathf.Floor(SampleRate / MaxFrequencyHz));
            int maxLag = Mathf.Min(halfSize - 2, (int)Mathf.Ceil(SampleRate / MinFrequencyHz));
            if (minLag >= maxLag)
            {
                Output[0] = result;
                return;
            }

            CmndBuffer[0] = 1f;
            float runningSum = 0f;

            for (int tau = 1; tau <= maxLag; tau++)
            {
                float diffSum = 0f;
                for (int j = 0; j < halfSize; j++)
                {
                    float delta = FilteredSamples[j] - FilteredSamples[j + tau];
                    diffSum += delta * delta;
                }

                runningSum += diffSum;
                CmndBuffer[tau] = runningSum > 1e-12f ? (diffSum * tau) / runningSum : 1f;
            }

            // 5. Find first local minimum below YinThreshold (naturally favors fundamental over subharmonics).
            int bestTau = -1;
            float bestCmnd = 1f;

            for (int tau = minLag; tau <= maxLag; tau++)
            {
                if (CmndBuffer[tau] < YinThreshold)
                {
                    while (tau + 1 <= maxLag && CmndBuffer[tau + 1] < CmndBuffer[tau])
                    {
                        tau++;
                    }

                    bestTau = tau;
                    bestCmnd = CmndBuffer[tau];
                    break;
                }
            }

            // Fallback: pick global minimum inside [minLag, maxLag].
            if (bestTau < 0)
            {
                for (int tau = minLag; tau <= maxLag; tau++)
                {
                    if (CmndBuffer[tau] < bestCmnd)
                    {
                        bestCmnd = CmndBuffer[tau];
                        bestTau = tau;
                    }
                }
            }

            // 6. Optional 2nd-harmonic / octave correction:
            // If the first dip was caused by a very strong 2nd harmonic (shallow dip > 0.05)
            // and 2 * tau has a substantially cleaner period dip, lock onto the true fundamental.
            if (EnableOctaveCorrection != 0 && bestTau > 0 && bestCmnd > 0.045f)
            {
                int doubleTau = bestTau * 2;
                if (doubleTau + 1 <= maxLag)
                {
                    int searchStart = Mathf.Max(minLag, doubleTau - 2);
                    int searchEnd = Mathf.Min(maxLag, doubleTau + 2);
                    int refinedDoubleTau = doubleTau;
                    float refinedDoubleCmnd = CmndBuffer[doubleTau];

                    for (int t = searchStart; t <= searchEnd; t++)
                    {
                        if (CmndBuffer[t] < refinedDoubleCmnd)
                        {
                            refinedDoubleCmnd = CmndBuffer[t];
                            refinedDoubleTau = t;
                        }
                    }

                    if (refinedDoubleCmnd < bestCmnd * 0.65f)
                    {
                        bestTau = refinedDoubleTau;
                        bestCmnd = refinedDoubleCmnd;
                    }
                }
            }

            float clarity = Mathf.Clamp01(1f - bestCmnd);
            result.Clarity = clarity;

            // Reject unvoiced frames or broadband noise bursts (unless clarity is exceptionally high, e.g. high whistle).
            bool passesFlatness = noiseProxy <= MaxSpectralFlatness || clarity >= 0.92f;
            if (bestTau <= 0 || clarity < MinClarity || !passesFlatness)
            {
                Output[0] = result;
                return;
            }

            // 7. Parabolic interpolation around bestTau for sub-sample frequency precision.
            float refinedTau = bestTau;
            if (bestTau > 1 && bestTau < maxLag)
            {
                float s0 = CmndBuffer[bestTau - 1];
                float s1 = CmndBuffer[bestTau];
                float s2 = CmndBuffer[bestTau + 1];
                float denom = 2f * (s0 - 2f * s1 + s2);
                if (Mathf.Abs(denom) > 1e-9f)
                {
                    float shift = (s0 - s2) / denom;
                    if (shift > -1f && shift < 1f)
                    {
                        refinedTau = bestTau + shift;
                    }
                }
            }

            float frequencyHz = SampleRate / refinedTau;
            if (frequencyHz < MinFrequencyHz * 0.94f || frequencyHz > MaxFrequencyHz * 1.06f)
            {
                Output[0] = result;
                return;
            }

            float midiFloat = 69f + 12f * (Mathf.Log(frequencyHz / ReferenceA4Hz) / Mathf.Log(2f));
            int nearestMidi = Mathf.RoundToInt(midiFloat);
            float centsOffset = (midiFloat - nearestMidi) * 100f;

            result.FrequencyHz = frequencyHz;
            result.MidiFloat = midiFloat;
            result.NearestMidiNote = nearestMidi;
            result.CentsOffset = centsOffset;
            result.IsVoicedByte = 1;
            Output[0] = result;
        }

        /// <summary>
        /// Computes 2nd-order Butterworth high-pass biquad coefficients at <paramref name="cutoffHz"/> and filters <paramref name="samples"/> in place.
        /// </summary>
        private static void ApplyHighPassBiquad(NativeArray<float> samples, int length, int sampleRate, float cutoffHz)
        {
            float w0 = 2f * Mathf.PI * cutoffHz / sampleRate;
            float cosW0 = Mathf.Cos(w0);
            float sinW0 = Mathf.Sin(w0);
            float alpha = sinW0 / 1.41421356f; // Butterworth Q = 0.7071

            float b0 = (1f + cosW0) * 0.5f;
            float b1 = -(1f + cosW0);
            float b2 = (1f + cosW0) * 0.5f;
            float a0 = 1f + alpha;
            float a1 = -2f * cosW0;
            float a2 = 1f - alpha;

            ApplyBiquad(samples, length, b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }

        /// <summary>
        /// Computes 2nd-order Butterworth low-pass biquad coefficients at <paramref name="cutoffHz"/> and filters <paramref name="samples"/> in place.
        /// </summary>
        private static void ApplyLowPassBiquad(NativeArray<float> samples, int length, int sampleRate, float cutoffHz)
        {
            float w0 = 2f * Mathf.PI * cutoffHz / sampleRate;
            float cosW0 = Mathf.Cos(w0);
            float sinW0 = Mathf.Sin(w0);
            float alpha = sinW0 / 1.41421356f;

            float b0 = (1f - cosW0) * 0.5f;
            float b1 = 1f - cosW0;
            float b2 = (1f - cosW0) * 0.5f;
            float a0 = 1f + alpha;
            float a1 = -2f * cosW0;
            float a2 = 1f - alpha;

            ApplyBiquad(samples, length, b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }

        /// <summary>
        /// Applies a Direct Form I biquad difference equation in place over the first <paramref name="length"/> samples.
        /// </summary>
        private static void ApplyBiquad(NativeArray<float> samples, int length, float b0, float b1, float b2, float a1, float a2)
        {
            float x1 = 0f, x2 = 0f;
            float y1 = 0f, y2 = 0f;

            for (int i = 0; i < length; i++)
            {
                float x0 = samples[i];
                float y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                samples[i] = y0;
                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;
            }
        }
    }
}
