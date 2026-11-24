using System;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;

namespace MelodyRecognitionEngine.Presentation
{
    /// <summary>
    /// Lightweight procedural and sample-backed instrument synthesizer.
    /// Supports Ocarina, Flute, HarpPluck, BellChime, SquareRetro procedural voices via OnAudioFilterRead,
    /// plus pitch-shifted AudioClip sample playback when configured via InstrumentTimbreConfig.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralOcarinaSynth : MonoBehaviour
    {
        [Header("Tone Shaping")]
        [SerializeField] private InstrumentWaveformPreset waveformPreset = InstrumentWaveformPreset.Ocarina;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.28f;
        [SerializeField, Range(0f, 0.8f)] private float secondHarmonicMix = 0.16f;
        [SerializeField, Range(0f, 0.6f)] private float thirdHarmonicMix = 0.03f;
        [SerializeField, Range(0f, 10f)] private float vibratoRateHz = 5.0f;
        [SerializeField, Range(0f, 30f)] private float vibratoDepthCents = 8.0f;
        [SerializeField, Range(0.003f, 0.2f)] private float attackSeconds = 0.025f;
        [SerializeField, Range(0.02f, 1.2f)] private float releaseSeconds = 0.12f;
        [SerializeField] private bool autoDecayPluck = false;
        [SerializeField, Range(0.08f, 2.5f)] private float pluckDecaySeconds = 0.65f;

        [Header("Optional AudioClip Sample Playback")]
        [SerializeField] private AudioClip sampleClip;
        [SerializeField, Range(24, 108)] private int sampleRootMidiNote = 69;

        private AudioSource _audioSource;
        private AudioSource _sampleVoiceSource;
        private double _sampleRate = 44100.0;
        private double _phase;
        private double _vibratoPhase;

        private volatile bool _noteHeld;
        private volatile bool _pluckTriggered;
        private float _targetFrequencyHz = 440f;
        private float _currentFrequencyHz = 440f;
        private float _envelope;

        // Short 4-note celebratory arpeggio chime played on song match.
        private static readonly float[] FanfareFrequencies = { 587.33f, 739.99f, 880.00f, 1174.66f }; // D5 F#5 A5 D6
        private volatile bool _fanfareActive;
        private int _fanfareStepIndex;
        private int _fanfareSamplesRemaining;
        private int _fanfareStepSamples;

        /// <summary>Currently active waveform preset or sample playback mode.</summary>
        public InstrumentWaveformPreset ActiveWaveform => waveformPreset;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 0f;
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            _fanfareStepSamples = Mathf.Max(1, Mathf.RoundToInt((float)(_sampleRate * 0.095)));

            // Provide a tiny silent clip so Unity invokes OnAudioFilterRead reliably across platforms.
            if (_audioSource.clip == null)
            {
                _audioSource.clip = AudioClip.Create("OcarinaSynthCarrier", 4096, 1, (int)_sampleRate, false);
            }
        }

        private void OnEnable()
        {
            if (_audioSource != null && !_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }

        private void OnDisable()
        {
            _noteHeld = false;
            _pluckTriggered = false;
            _fanfareActive = false;
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (_sampleVoiceSource != null && _sampleVoiceSource.isPlaying)
            {
                _sampleVoiceSource.Stop();
            }
        }

        /// <summary>
        /// Applies an <see cref="InstrumentTimbreConfig"/> to this synthesizer, clamping all synthesis and sample parameters to safe ranges.
        /// </summary>
        public void ApplyTimbre(InstrumentTimbreConfig timbre)
        {
            waveformPreset = timbre.Waveform;
            masterVolume = Mathf.Clamp01(timbre.MasterVolume);
            secondHarmonicMix = Mathf.Clamp(timbre.SecondHarmonicMix, 0f, 0.8f);
            thirdHarmonicMix = Mathf.Clamp(timbre.ThirdHarmonicMix, 0f, 0.6f);
            vibratoRateHz = Mathf.Clamp(timbre.VibratoRateHz, 0f, 10f);
            vibratoDepthCents = Mathf.Clamp(timbre.VibratoDepthCents, 0f, 30f);
            attackSeconds = Mathf.Clamp(timbre.AttackSeconds, 0.003f, 0.2f);
            releaseSeconds = Mathf.Clamp(timbre.ReleaseSeconds, 0.02f, 1.2f);
            autoDecayPluck = timbre.AutoDecayPluck;
            pluckDecaySeconds = Mathf.Clamp(timbre.PluckDecaySeconds, 0.08f, 2.5f);
            sampleClip = timbre.SampleClip;
            sampleRootMidiNote = Mathf.Clamp(timbre.SampleRootMidiNote <= 0 ? 69 : timbre.SampleRootMidiNote, 24, 108);
        }

        /// <summary>
        /// Starts playing a note at the concert frequency corresponding to <paramref name="midiNote"/>.
        /// </summary>
        public void PlayMidiNote(int midiNote)
        {
            PlayNote(MusicalNote.MidiToFrequency(midiNote));
        }

        /// <summary>
        /// Starts playing a note at <paramref name="frequencyHz"/> via either the pitch-shifted sample voice or procedural oscillator.
        /// </summary>
        public void PlayNote(float frequencyHz)
        {
            if (frequencyHz <= 20f) return;

            if (waveformPreset == InstrumentWaveformPreset.AudioClipSample && sampleClip != null)
            {
                PlaySampleVoice(frequencyHz);
                return;
            }

            _targetFrequencyHz = Mathf.Clamp(frequencyHz, 60f, 4200f);
            _fanfareActive = false;
            _noteHeld = true;
            if (autoDecayPluck)
            {
                _pluckTriggered = true;
            }

            if (_audioSource != null && !_audioSource.isPlaying && isActiveAndEnabled)
            {
                _audioSource.Play();
            }
        }

        /// <summary>
        /// Releases the currently sustained note (allowing procedural voices to ramp down over release time).
        /// </summary>
        public void StopNote()
        {
            _noteHeld = false;
            if (_sampleVoiceSource != null && _sampleVoiceSource.isPlaying && !autoDecayPluck)
            {
                _sampleVoiceSource.Stop();
            }
        }

        /// <summary>
        /// Triggers the built-in 4-note D major arpeggio fanfare (D5, F#5, A5, D6) on the procedural audio thread.
        /// </summary>
        public void PlayMatchFanfare()
        {
            _fanfareStepIndex = 0;
            _fanfareSamplesRemaining = _fanfareStepSamples;
            _targetFrequencyHz = FanfareFrequencies[0];
            _currentFrequencyHz = _targetFrequencyHz;
            _noteHeld = false;
            _pluckTriggered = false;
            _fanfareActive = true;

            if (_audioSource != null && !_audioSource.isPlaying && isActiveAndEnabled)
            {
                _audioSource.Play();
            }
        }

        /// <summary>
        /// Lazily creates a child <see cref="AudioSource"/> and plays <see cref="sampleClip"/> pitch-shifted to <paramref name="targetFrequencyHz"/>.
        /// </summary>
        private void PlaySampleVoice(float targetFrequencyHz)
        {
            if (_sampleVoiceSource == null)
            {
                var voiceGo = new GameObject("InstrumentSampleVoice");
                voiceGo.transform.SetParent(transform, false);
                _sampleVoiceSource = voiceGo.AddComponent<AudioSource>();
                _sampleVoiceSource.playOnAwake = false;
                _sampleVoiceSource.spatialBlend = 0f;
            }

            float rootFreq = MusicalNote.MidiToFrequency(sampleRootMidiNote);
            float pitchRatio = rootFreq > 0f ? Mathf.Clamp(targetFrequencyHz / rootFreq, 0.25f, 4f) : 1f;
            _sampleVoiceSource.pitch = pitchRatio;
            _sampleVoiceSource.volume = masterVolume;
            _sampleVoiceSource.loop = !autoDecayPluck;
            _sampleVoiceSource.clip = sampleClip;
            _sampleVoiceSource.Play();
        }

        /// <summary>
        /// Audio-thread callback synthesizing the active procedural waveform, vibrato, attack/release or pluck envelope, and match fanfare.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || channels <= 0) return;

            double sr = _sampleRate > 0 ? _sampleRate : 44100.0;
            float attackStep = (float)(1.0 / Math.Max(1.0, attackSeconds * sr));
            float releaseStep = (float)(1.0 / Math.Max(1.0, releaseSeconds * sr));
            float pluckDecayStep = (float)(1.0 / Math.Max(1.0, pluckDecaySeconds * sr));
            double twoPi = 2.0 * Math.PI;
            float norm = Math.Max(1f, 1f + secondHarmonicMix + thirdHarmonicMix);

            for (int i = 0; i < data.Length; i += channels)
            {
                bool active = _noteHeld;

                if (_fanfareActive)
                {
                    active = true;
                    _fanfareSamplesRemaining--;
                    if (_fanfareSamplesRemaining <= 0)
                    {
                        _fanfareStepIndex++;
                        if (_fanfareStepIndex >= FanfareFrequencies.Length)
                        {
                            _fanfareActive = false;
                            active = _noteHeld;
                        }
                        else
                        {
                            _targetFrequencyHz = FanfareFrequencies[_fanfareStepIndex];
                            _fanfareSamplesRemaining = _fanfareStepIndex == FanfareFrequencies.Length - 1
                                ? _fanfareStepSamples * 2
                                : _fanfareStepSamples;
                        }
                    }
                }

                if (!_fanfareActive && autoDecayPluck)
                {
                    if (_pluckTriggered)
                    {
                        _envelope = Math.Min(1f, _envelope + attackStep);
                        if (_envelope >= 0.99f)
                        {
                            _pluckTriggered = false;
                        }
                    }
                    else
                    {
                        _envelope = Math.Max(0f, _envelope - pluckDecayStep);
                    }
                }
                else if (active)
                {
                    _envelope = Math.Min(1f, _envelope + attackStep);
                }
                else
                {
                    _envelope = Math.Max(0f, _envelope - releaseStep);
                }

                if (_envelope <= 0.0001f)
                {
                    continue;
                }

                // Smooth frequency glide to prevent clicks between notes.
                _currentFrequencyHz += (_targetFrequencyHz - _currentFrequencyHz) * 0.025f;

                double freq = _currentFrequencyHz;
                if (vibratoDepthCents > 0f && vibratoRateHz > 0f)
                {
                    _vibratoPhase += (twoPi * vibratoRateHz) / sr;
                    if (_vibratoPhase > twoPi) _vibratoPhase -= twoPi;
                    double cents = vibratoDepthCents * Math.Sin(_vibratoPhase);
                    freq *= Math.Pow(2.0, cents / 1200.0);
                }

                _phase += (twoPi * freq) / sr;
                if (_phase > twoPi) _phase -= twoPi;

                float sample;
                if (!_fanfareActive && waveformPreset == InstrumentWaveformPreset.SquareRetro)
                {
                    sample = _phase < Math.PI ? 0.8f : -0.8f;
                }
                else if (!_fanfareActive && waveformPreset == InstrumentWaveformPreset.BellChime)
                {
                    // Bell overtone ratios (1 : 2.0 : 2.756) for bright chime timbre.
                    sample = (float)(Math.Sin(_phase) + secondHarmonicMix * Math.Sin(2.0 * _phase) + thirdHarmonicMix * Math.Sin(2.756 * _phase)) / norm;
                }
                else
                {
                    sample = (float)(Math.Sin(_phase) + secondHarmonicMix * Math.Sin(2.0 * _phase) + thirdHarmonicMix * Math.Sin(3.0 * _phase)) / norm;
                }

                float outVal = sample * _envelope * masterVolume;

                for (int c = 0; c < channels; c++)
                {
                    data[i + c] += outVal;
                }
            }
        }
    }
}
