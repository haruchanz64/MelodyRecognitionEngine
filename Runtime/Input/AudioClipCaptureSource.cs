using System;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Input
{
    /// <summary>
    /// Feeds a Unity AudioClip or raw mono float[] PCM buffer through IAudioCaptureSource
    /// for offline dry-run simulation, Editor Workbench playback, and deterministic EditMode tests.
    /// </summary>
    public sealed class AudioClipCaptureSource : IAudioCaptureSource
    {
        private readonly float[] _monoSamples;
        private readonly int _sampleRate;
        private readonly string _deviceName;
        private readonly bool _loop;

        private int _readCursor;
        private bool _isCapturing;

        public int SampleRate => _sampleRate;
        public bool IsCapturing => _isCapturing;
        public string DeviceName => _deviceName;
        public int TotalSamples => _monoSamples.Length;
        public int ReadCursor => _readCursor;
        public bool IsFinished => !_loop && _readCursor >= _monoSamples.Length;

        public AudioClipCaptureSource(float[] monoSamples, int sampleRate = 44100, string label = "SyntheticStream", bool loop = false)
        {
            _monoSamples = monoSamples ?? Array.Empty<float>();
            _sampleRate = Mathf.Max(8000, sampleRate);
            _deviceName = label ?? "SyntheticStream";
            _loop = loop;
        }

        public static AudioClipCaptureSource FromAudioClip(AudioClip clip, bool loop = false)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            int channels = Mathf.Max(1, clip.channels);
            int sampleCount = clip.samples;
            var raw = new float[sampleCount * channels];
            clip.GetData(raw, 0);

            float[] mono;
            if (channels == 1)
            {
                mono = raw;
            }
            else
            {
                mono = new float[sampleCount];
                float inv = 1f / channels;
                for (int i = 0; i < sampleCount; i++)
                {
                    float sum = 0f;
                    int baseIdx = i * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += raw[baseIdx + c];
                    }
                    mono[i] = sum * inv;
                }
            }

            return new AudioClipCaptureSource(mono, clip.frequency, clip.name, loop);
        }

        public bool StartCapture()
        {
            _readCursor = 0;
            _isCapturing = _monoSamples.Length > 0;
            return _isCapturing;
        }

        public void StopCapture()
        {
            _isCapturing = false;
        }

        public void Seek(int sampleIndex)
        {
            _readCursor = Mathf.Clamp(sampleIndex, 0, _monoSamples.Length);
        }

        public int ReadSamples(float[] destination, int maxSamples)
        {
            if (!_isCapturing || destination == null || maxSamples <= 0 || _monoSamples.Length == 0)
            {
                return 0;
            }

            int requested = Mathf.Min(maxSamples, destination.Length);
            int written = 0;

            while (written < requested)
            {
                int remaining = _monoSamples.Length - _readCursor;
                if (remaining <= 0)
                {
                    if (_loop)
                    {
                        _readCursor = 0;
                        remaining = _monoSamples.Length;
                    }
                    else
                    {
                        _isCapturing = false;
                        break;
                    }
                }

                int chunk = Mathf.Min(requested - written, remaining);
                Array.Copy(_monoSamples, _readCursor, destination, written, chunk);
                _readCursor += chunk;
                written += chunk;
            }

            return written;
        }

        public void Dispose()
        {
            _isCapturing = false;
        }
    }
}
