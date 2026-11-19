using System;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Input
{
    /// <summary>
    /// Platform microphone capture backed by UnityEngine.Microphone (Desktop, Mobile, XR).
    /// Reads mono PCM samples from a looping AudioClip ring buffer without per-frame allocations.
    /// </summary>
    public sealed class UnityMicrophoneCaptureSource : IAudioCaptureSource
    {
        private readonly string _requestedDevice;
        private readonly int _requestedSampleRate;
        private readonly int _clipLengthSeconds;

        private string _activeDevice;
        private int _sampleRate;
        private AudioClip _micClip;
        private int _lastReadSamplePos;
        private bool _isCapturing;
        private float[] _interleavedScratch = new float[8192];

        public int SampleRate => _sampleRate;
        public bool IsCapturing => _isCapturing;
        public string DeviceName => _activeDevice ?? string.Empty;

        public UnityMicrophoneCaptureSource(string deviceName = null, int sampleRate = 44100, int clipLengthSeconds = 2)
        {
            _requestedDevice = deviceName;
            _requestedSampleRate = Mathf.Clamp(sampleRate, 16000, 48000);
            _clipLengthSeconds = Mathf.Clamp(clipLengthSeconds, 1, 10);
            _sampleRate = _requestedSampleRate;
        }

        public static string[] GetAvailableDevices()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Array.Empty<string>();
#else
            return Microphone.devices ?? Array.Empty<string>();
#endif
        }

        public static bool HasMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS && !UNITY_EDITOR
            return Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
            return true;
#endif
        }

        public static void RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
        }

        public bool StartCapture()
        {
            if (_isCapturing) return true;

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("[MelodyRecognitionEngine] UnityEngine.Microphone is not supported on WebGL. Supply a custom IAudioCaptureSource.");
            return false;
#else
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[MelodyRecognitionEngine] No microphone input devices detected.");
                return false;
            }

            if (!string.IsNullOrEmpty(_requestedDevice))
            {
                bool found = false;
                for (int i = 0; i < devices.Length; i++)
                {
                    if (string.Equals(devices[i], _requestedDevice, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
                _activeDevice = found ? _requestedDevice : devices[0];
            }
            else
            {
                _activeDevice = devices[0];
            }

            Microphone.GetDeviceCaps(_activeDevice, out int minFreq, out int maxFreq);
            _sampleRate = _requestedSampleRate;
            if (maxFreq > 0)
            {
                _sampleRate = Mathf.Clamp(_requestedSampleRate, Mathf.Max(8000, minFreq), maxFreq);
            }

            _micClip = Microphone.Start(_activeDevice, true, _clipLengthSeconds, _sampleRate);
            if (_micClip == null)
            {
                Debug.LogWarning($"[MelodyRecognitionEngine] Failed to start microphone '{_activeDevice}'.");
                return false;
            }

            _sampleRate = _micClip.frequency;
            _lastReadSamplePos = 0;
            _isCapturing = true;
            return true;
#endif
        }

        public void StopCapture()
        {
            if (!_isCapturing) return;

#if !UNITY_WEBGL || UNITY_EDITOR
            if (!string.IsNullOrEmpty(_activeDevice) && Microphone.IsRecording(_activeDevice))
            {
                Microphone.End(_activeDevice);
            }
#endif
            if (_micClip != null)
            {
                UnityEngine.Object.DestroyImmediate(_micClip);
                _micClip = null;
            }

            _isCapturing = false;
            _lastReadSamplePos = 0;
        }

        public int ReadSamples(float[] destination, int maxSamples)
        {
            if (!_isCapturing || _micClip == null || destination == null || maxSamples <= 0)
            {
                return 0;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return 0;
#else
            int currentPos = Microphone.GetPosition(_activeDevice);
            if (currentPos < 0 || currentPos == _lastReadSamplePos)
            {
                return 0;
            }

            int clipSamples = _micClip.samples;
            int available = currentPos - _lastReadSamplePos;
            if (available < 0)
            {
                available += clipSamples;
            }

            int toRead = Mathf.Min(available, Mathf.Min(maxSamples, destination.Length));
            if (toRead <= 0) return 0;

            int channels = Mathf.Max(1, _micClip.channels);
            if (channels == 1)
            {
                ReadMonoWrapped(destination, toRead, clipSamples);
            }
            else
            {
                int neededInterleaved = toRead * channels;
                if (_interleavedScratch.Length < neededInterleaved)
                {
                    _interleavedScratch = new float[neededInterleaved];
                }

                ReadInterleavedWrapped(_interleavedScratch, toRead, channels, clipSamples);
                float invChannels = 1f / channels;
                for (int i = 0; i < toRead; i++)
                {
                    float sum = 0f;
                    int baseIdx = i * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += _interleavedScratch[baseIdx + c];
                    }
                    destination[i] = sum * invChannels;
                }
            }

            _lastReadSamplePos = (_lastReadSamplePos + toRead) % clipSamples;
            return toRead;
#endif
        }

        private void ReadMonoWrapped(float[] destination, int count, int clipSamples)
        {
            int firstPart = Mathf.Min(count, clipSamples - _lastReadSamplePos);
            if (firstPart == count)
            {
                _micClip.GetData(destination, _lastReadSamplePos);
                return;
            }

            if (_interleavedScratch.Length < count)
            {
                _interleavedScratch = new float[count];
            }

            // Unity AudioClip.GetData wraps automatically across clip boundaries when offset + length > clip.samples.
            var slice = new float[count];
            _micClip.GetData(slice, _lastReadSamplePos);
            Array.Copy(slice, 0, destination, 0, count);
        }

        private void ReadInterleavedWrapped(float[] interleaved, int frameCount, int channels, int clipSamples)
        {
            int totalFloats = frameCount * channels;
            if (interleaved.Length == totalFloats)
            {
                _micClip.GetData(interleaved, _lastReadSamplePos);
            }
            else
            {
                var temp = new float[totalFloats];
                _micClip.GetData(temp, _lastReadSamplePos);
                Array.Copy(temp, 0, interleaved, 0, totalFloats);
            }
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}
