# MelodyRecognitionEngine

Real-time Burst-compiled humming and acoustic instrument melody recognition engine for Unity 6. Players can hum, whistle, play an acoustic instrument into a microphone, or use accessible discrete button inputs; every input modality feeds the same note segmentation, repertoire gating, melody matching, Guided/Learn Mode, and Musical Staff HUD pipeline.

## Features

- **Burst-compiled YIN pitch tracker (`PitchDetectionJob`, `BurstPitchTracker`):**
  - Allocation-free managed ring buffer and persistent `NativeArray<float>` scratch buffers supporting asynchronous `IJob` scheduling at runtime (`PushSamplesAsync`) and synchronous frame draining for offline analysis and tests (`ProcessSamples`, `DrainAllSynchronous`).
  - DC offset removal, cascaded 2nd-order Butterworth high-pass and low-pass biquad filtering, adaptive RMS noise gating, first-difference broadband noise rejection, YIN Cumulative Mean Normalized Difference (CMND) with parabolic sub-sample interpolation, and optional 2nd-harmonic octave correction.
- **Acoustic detection profiles and instruments (`DetectionProfileAsset`, `InstrumentAsset`):**
  - Built-in Hum, Instrument, and Whistle presets (`DetectionProfileConfig`) plus ScriptableObject overrides hot-swappable at runtime via `SetDetectionProfile(...)`.
  - `InstrumentAsset` bundles a `DetectionProfileAsset`, an optional instrument-specific `SongLibraryAsset`, a customizable chromatic note layout of any size, and a procedural or pitch-shifted `AudioClip` playback timbre via `EquipInstrument(...)`.
- **Note segmentation (`NoteSegmenter`):**
  - Allocation-free rolling median filter (up to 9 frames), semitone hysteresis to absorb vocal vibrato, minimum note duration filtering for stray blips, RMS dip-and-rise re-articulation detection for repeated same-pitch notes, and inter-phrase silence timeouts.
- **Melody matching (`MelodyMatcher`):**
  - Mean-centered relative semitone interval matching by default (key- and octave-invariant), plus opt-in strict concert-pitch validation per song (`PitchMatchingMode.AbsolutePitch`) or per profile (`ForceStrictAbsolutePitch`).
  - Tempo-free pitch sequence matching by default, plus opt-in tempo-invariant inter-onset rhythm ratio validation (`ValidateRhythm`, `RhythmTolerance`).
  - Auto-expanding sliding-window history for songs of any length, interior stray-note forgiveness (`MaxStrayNotes` up to 2 on songs with 4+ notes), live `SongMatchProgress` updates, and a configurable prefix disambiguation window when a shorter song is a prefix of a longer song.
  - Guided/Learn Mode (`MatcherPlayMode.GuidedLearn`) for step-by-step teaching with optional automatic unlocking on completion.
- **Repertoire gating and custom songs (`SongRepertoire`, `CustomSongRecorder`):**
  - Unlock gating keeps unlearned songs from firing during Free Play, with per-song `AlwaysDiscoverable` overrides for secret melodies.
  - Runtime player-recorded custom songs with minimum/maximum note bounds, monotone validation, interval conflict detection, and `JsonUtility` state serialization (`ExportStateJson`, `ImportStateJson`).
- **Unified input abstraction (`IAudioCaptureSource`, `INoteInputSource`):**
  - Ships with `UnityMicrophoneCaptureSource` (Desktop, Mobile, XR), `AudioClipCaptureSource` (Unity `AudioClip` and raw mono PCM streams), `MicrophoneNoteInputSource` (DSP pipeline wrapper with ambient room-noise calibration), and `DiscreteNoteInputSource` (keyboard, gamepad, or virtual UI button input).
- **Runtime presentation and UI Toolkit workbench:**
  - `MelodyRecognitionController` drop-in scene component with Inspector `UnityEvent`s and BGM ducking hooks (`onListeningStateChanged` / `OnListeningStateChangedEvent`).
  - Optional uGUI `MusicalStaffHUD` (5-line treble staff, live pitch needle, detected note markers, Guided/Learn Mode ghost cues, mic RMS meter, and Hum/Inst/Keys mode buttons).
  - Optional `ProceduralOcarinaSynth` (`OnAudioFilterRead` voices for `Ocarina`, `Flute`, `HarpPluck`, `BellChime`, and `SquareRetro`, pitch-shifted `AudioClip` sample playback, and a 4-note match fanfare).
  - UI Toolkit `MelodyWorkbenchWindow` (`Tools > HaruChanZ64 > Audio > MelodyRecognitionEngine > Melody Workbench`) with a Live Mic Tuner, Mic/Piano Song Authoring (`C1` to `C7` range), and a deterministic Dry-Run Simulator, plus custom Inspectors for `DetectionProfileAsset` and `SongAsset`.

## Requirements

- Unity `6000.0` or newer
- Required UPM dependencies (declared in `package.json` and installed automatically):
  - `com.unity.burst` (`1.8.14+`)
  - `com.unity.collections` (`2.4.3+`)
  - `com.unity.ugui` (`2.0.0+`)
- Optional dependencies for specific samples:
  - `com.unity.inputsystem` for the **InputSystem Virtual Instrument Adapter** sample (`ENABLE_INPUT_SYSTEM`)
  - `com.haruchanz64.common-events-engine` for the **CommonEventsEngine Bridge** sample

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager** in Unity 6 (`6000.0+`).
2. Click **+** (top-left) and choose **Install package from git URL...**.
3. Enter:

```text
https://github.com/haruchanz64/MelodyRecognitionEngine.git
```

### Local package

Add the package path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.haruchanz64.melody-recognition-engine": "file:../Plugins/MelodyRecognitionEngine"
  }
}
```

## Quick start

1. Create a pre-wired scene rig from **GameObject > MelodyRecognitionEngine > Create Melody Recognition Rig**. This spawns a `MelodyRecognitionRig` containing `MelodyRecognitionController`, `ProceduralOcarinaSynth`, and a bound child `MusicalStaffHUD`.
2. Author songs via **Assets > Create > MelodyRecognitionEngine > Song Asset** or open **Tools > HaruChanZ64 > Audio > MelodyRecognitionEngine > Melody Workbench** to record notes from your microphone or click the on-screen piano keys.
3. Group songs into a library via **Assets > Create > MelodyRecognitionEngine > Song Library** and assign it to `Song Library` on `MelodyRecognitionController`.
4. *(Optional)* Create custom DSP profiles (**Assets > Create > MelodyRecognitionEngine > Detection Profile**) or bundled instruments (**Assets > Create > MelodyRecognitionEngine > Instrument Asset**) and assign them to the library or controller.
5. Wire `onSongMatched` for gameplay reactions and `onListeningStateChanged` to duck background music while listening, enable `Start Listening On Enable` (or call `StartListening()` from code), and enter Play Mode.

## Core concepts

### Audio-to-melody pipeline

Every live microphone session runs through four decoupled stages:

1. **Audio capture (`IAudioCaptureSource`):** `UnityMicrophoneCaptureSource` reads newly recorded mono PCM samples from a looping microphone `AudioClip` (downmixing multi-channel devices to mono). `AudioClipCaptureSource` implements the same contract for offline simulation and tests.
2. **Burst pitch tracking (`BurstPitchTracker` + `PitchDetectionJob`):** Incoming PCM samples feed a circular ring buffer. Each completed hop (`HopSize`, default `512` samples over a `2048`-sample window) runs a Burst-compiled `IJob` that removes DC offset, applies a 2nd-order Butterworth bandpass filter, checks RMS against `EffectiveNoiseGateRms` and broadband flatness, computes YIN CMND with optional 2nd-harmonic octave correction, and refines frequency via parabolic interpolation into a `PitchFrame`.
3. **Note segmentation (`NoteSegmenter`):** Smooths continuous `PitchFrame.MidiFloat` values through a rolling median window, holds the active note inside `NoteChangeHysteresisSemitones`, filters brief blips shorter than `MinNoteDurationSeconds`, splits repeated same-pitch notes on RMS dip-and-rise articulation, and emits `NoteEvent`s plus `OnSilenceTimeout`. `DiscreteNoteInputSource` bypasses stages 1 through 3 and emits identical `NoteEvent`s from buttons or keys.
4. **Melody matching (`MelodyMatcher` + `SongRepertoire`):** Appends each `NoteEvent` to an auto-expanding sliding window and scores all recognizable songs (unlocked or `AlwaysDiscoverable`). Relative mode computes the optimal mean transposition across the window to remove first-note anchor bias; optional rhythm validation compares normalized inter-onset intervals against `SongNoteEntry.RelativeDuration`.

### Prefix disambiguation and Guided/Learn Mode

- **Prefix disambiguation:** If a completed shorter song is a prefix of a longer recognizable song, `MelodyMatcher` buffers the shorter match for `PrefixDisambiguationSeconds` (default `0.65` s). If the player completes the longer song, the longer song wins; if the player diverges, pauses past the deadline, or triggers a silence timeout, the buffered shorter song fires.
- **Guided/Learn Mode:** Calling `StartGuidedLearnSong(songId, unlockOnComplete, resetOnMistake)` switches `MelodyMatcher` into step-by-step evaluation. In relative mode, the first played note anchors the player's vocal or instrument key, and completing the final step automatically unlocks the song in `SongRepertoire`.

## Code examples

### 1. Register songs, duck BGM, and listen for matches

```csharp
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;

public sealed class ShrineMelodyListener : MonoBehaviour
{
    [SerializeField] private MelodyRecognitionController controller;
    [SerializeField] private AudioSource bgmSource;
    private float _defaultBgmVolume = 0.7f;

    private void Start()
    {
        if (bgmSource != null) _defaultBgmVolume = bgmSource.volume;

        controller.Repertoire.RegisterSong(
            SongDefinition.FromNotation(
                "song_lullaby",
                "Royal Lullaby",
                "B4:2 D5:1 A4:3 B4:2 D5:1 A4:3",
                PitchMatchingMode.RelativeIntervals,
                unlockedByDefault: true));

        controller.OnListeningStateChangedEvent += HandleListeningChanged;
        controller.OnSongMatchedEvent += HandleSongMatched;

        controller.SetModality(InputModality.MicrophoneHum);
        controller.StartListening();
    }

    private void OnDestroy()
    {
        if (controller == null) return;
        controller.OnListeningStateChangedEvent -= HandleListeningChanged;
        controller.OnSongMatchedEvent -= HandleSongMatched;
    }

    private void HandleListeningChanged(bool isListening)
    {
        if (bgmSource != null)
        {
            bgmSource.volume = isListening ? 0.15f : _defaultBgmVolume;
        }
    }

    private void HandleSongMatched(SongMatchResult match)
    {
        Debug.Log($"Matched {match.DisplayName} (confidence {match.Confidence:P0}, shift {match.TranspositionSemitones:+0;-0;0} st)");
    }
}
```

### 2. Equip instruments, run Guided/Learn Mode, and save custom songs

```csharp
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Data;
using UnityEngine;

public sealed class SongProgressionManager : MonoBehaviour
{
    [SerializeField] private MelodyRecognitionController controller;
    [SerializeField] private InstrumentAsset fluteInstrument;
    private string _savedRepertoireJson;

    public void EquipFlute()
    {
        // Applies the instrument's DetectionProfile, synth timbre, and optional song library.
        controller.EquipInstrument(fluteInstrument, loadInstrumentSongs: true);
    }

    public void TeachSong(string songId)
    {
        controller.StartGuidedLearnSong(songId, unlockOnComplete: true, resetOnMistake: false);
    }

    public void BeginCustomRecording()
    {
        controller.BeginCustomSongRecording("song_player_custom", "Traveler's Tune", minNotes: 4, maxNotes: 8);
    }

    public void SaveCustomRecording()
    {
        if (controller.FinishAndSaveCustomSong(out SongDefinition song, out string error))
        {
            _savedRepertoireJson = controller.ExportRepertoireJson();
            Debug.Log($"Saved custom song: {song.ToNotationString()}");
        }
        else
        {
            Debug.LogWarning($"Recording rejected: {error}");
        }
    }

    public void RestoreSavedRepertoire()
    {
        if (!string.IsNullOrEmpty(_savedRepertoireJson))
        {
            controller.ImportRepertoireJson(_savedRepertoireJson);
        }
    }
}
```

### 3. Supply a custom `IAudioCaptureSource`

Implement `IAudioCaptureSource` to feed audio from a WebGL JavaScript bridge, native plugin, or custom audio buffer into `MelodyRecognitionController.SetCustomCaptureSource(...)`:

```csharp
using System;
using MelodyRecognitionEngine.Core;
using UnityEngine;

public sealed class RingBufferAudioCaptureSource : IAudioCaptureSource
{
    private readonly float[] _buffer;
    private int _writePos;
    private int _readPos;

    public int SampleRate { get; }
    public bool IsCapturing { get; private set; }
    public string DeviceName => "CustomRingBuffer";

    public RingBufferAudioCaptureSource(int sampleRate = 44100, int capacitySamples = 16384)
    {
        SampleRate = Mathf.Max(8000, sampleRate);
        _buffer = new float[Mathf.Max(2048, capacitySamples)];
    }

    public void PushSamples(float[] samples, int count)
    {
        if (!IsCapturing || samples == null) return;
        for (int i = 0; i < count; i++)
        {
            _buffer[_writePos] = samples[i];
            _writePos = (_writePos + 1) % _buffer.Length;
        }
    }

    public bool StartCapture()
    {
        _readPos = _writePos;
        IsCapturing = true;
        return true;
    }

    public void StopCapture() => IsCapturing = false;

    public int ReadSamples(float[] destination, int maxSamples)
    {
        if (!IsCapturing || destination == null || maxSamples <= 0) return 0;
        int limit = Mathf.Min(maxSamples, destination.Length);
        int written = 0;
        while (written < limit && _readPos != _writePos)
        {
            destination[written++] = _buffer[_readPos];
            _readPos = (_readPos + 1) % _buffer.Length;
        }
        return written;
    }

    public void Dispose() => StopCapture();
}
```

## Extension contracts and core API

| Type | Namespace | Purpose | Built-in implementation |
| :--- | :--- | :--- | :--- |
| `IAudioCaptureSource` | `MelodyRecognitionEngine.Core` | Platform-agnostic mono PCM audio stream provider | `UnityMicrophoneCaptureSource`, `AudioClipCaptureSource` |
| `INoteInputSource` | `MelodyRecognitionEngine.Core` | Unified note onset, pitch frame, and silence-timeout event stream | `MicrophoneNoteInputSource`, `DiscreteNoteInputSource` |
| `BurstPitchTracker` | `MelodyRecognitionEngine.DSP` | Persistent ring buffer and async/sync scheduler for `PitchDetectionJob` | `BurstPitchTracker` |
| `NoteSegmenter` | `MelodyRecognitionEngine.DSP` | Median smoothing, vibrato hysteresis, re-articulation, and blip filtering | `NoteSegmenter` |
| `MelodyMatcher` | `MelodyRecognitionEngine.Matching` | Sliding-window Free Play matching, prefix disambiguation, and Guided/Learn Mode | `MelodyMatcher` |
| `SongRepertoire` | `MelodyRecognitionEngine.Matching` | Runtime song registry, unlock gating, custom songs, and JSON save/load | `SongRepertoire` |
| `CustomSongRecorder` | `MelodyRecognitionEngine.Matching` | Captures live `NoteEvent`s into validated custom `SongDefinition`s | `CustomSongRecorder` |
| `DetectionProfileAsset` | `MelodyRecognitionEngine.Data` | ScriptableObject container for `DetectionProfileConfig` DSP/segmentation presets | `CreateHumPreset`, `CreateInstrumentPreset`, `CreateWhistlePreset` |
| `InstrumentAsset` | `MelodyRecognitionEngine.Data` | Bundles detection profile, virtual note layout, playback timbre, and optional song library | `InstrumentAsset` |
| `SongAsset` / `SongLibraryAsset` | `MelodyRecognitionEngine.Data` | Authored song definitions and grouped song/profile collections | `SongAsset`, `SongLibraryAsset` |
| `MelodyRecognitionController` | `MelodyRecognitionEngine.Components` | Scene controller wiring input sources, matcher, recorder, synth, and Inspector events | `MelodyRecognitionController` |
| `MusicalStaffHUD` / `ProceduralOcarinaSynth` | `MelodyRecognitionEngine.Presentation` | Optional uGUI 5-line musical staff HUD and procedural/sample feedback synth | `MusicalStaffHUD`, `ProceduralOcarinaSynth` |

## Included samples

Import any sample directly from the Package Manager **Samples** tab (each sample folder also includes its own local `README.md`):

- **Playable Ocarina Demo** (`Samples~/OcarinaDemo`): `OcarinaDemoController` registers 6 songs (4 unlocked, 1 locked `song_dawn_chorale` for Guided/Learn Mode, and 1 `AlwaysDiscoverable` secret `song_secret_chime`), spawns `MusicalStaffHUD` with Hum/Inst/Keys mode toggles, ducks any assigned `bgmAudioSource` while listening, and exposes `StartLearnDawnChorale()`, `StartRecordScarecrowSong()`, and `SaveScarecrowSong()`.
- **InputSystem Virtual Instrument Adapter** (`Samples~/InputSystemAdapter`): `InputSystemVirtualInstrument` maps a configurable list of `com.unity.inputsystem` `InputActionReference`s or keyboard keys (`Z`, `X`, `C`, `V`, `B`, `N`, `M`, `,` mapped to `D4` through `D5` by default) with `Left Shift` (+1 semitone), `Left Ctrl` (-1 semitone), and `[` / `]` octave shift modifiers into `DiscreteNoteInputSource`, and can sync its layout automatically from an equipped `InstrumentAsset`.
- **CommonEventsEngine Bridge** (`Samples~/CommonEventsBridge`): `MelodyCommonEventsBridge` maps recognized `SongId`s to `CommonEventDirector` event IDs (`TriggerEventById`), global switches (`SetSwitchOn`), or signals (`EmitSignal`). Requires `com.haruchanz64.common-events-engine`.

## Testing

The package includes EditMode NUnit tests under `Tests/Editor/` runnable from **Window > General > Test Runner** (EditMode tab):

- `Tests/Editor/BurstDspPitchTrackerTests.cs`: verifies pure sine frequency accuracy (`A2` through `A6`), rich-harmonic fundamental locking without octave jumping, broadband white-noise rejection, sustained vocal vibrato stability, repeated same-pitch re-articulation, 25 ms stray-blip rejection, ambient noise-floor calibration, and async vs. synchronous Burst job parity.
- `Tests/Editor/MelodyMatcherTests.cs`: verifies relative interval matching across transpositions and octaves (`0`, `+5`, `-7`, `-12`, `+12` semitones), mean-centered first-note pitch-drift absorption, strict absolute concert-pitch matching, sliding-window warm-up ignoring, stray-note forgiveness (`MaxStrayNotes = 1`), tempo-invariant inter-onset rhythm ratio validation, prefix disambiguation timeouts and longer-song overrides, Guided/Learn Mode step-by-step unlocking, and auto-expanding history for 32-note melodies.
- `Tests/Editor/RepertoireAndIntegrationTests.cs`: verifies unlock gating and `AlwaysDiscoverable` secrets, `CustomSongRecorder` validation and JSON export/import round-trip, end-to-end Hum Mode transposed vibrato recognition, end-to-end Instrument Mode repeated-note recognition, notation parse/format round-trip, and `MelodyRecognitionController` runtime `SetDetectionProfile` and `EquipInstrument` hot-swapping.

## Troubleshooting and limitations

- **WebGL microphone capture is not supported by `UnityEngine.Microphone`:** On WebGL builds (`UNITY_WEBGL && !UNITY_EDITOR`), `UnityMicrophoneCaptureSource.StartCapture()` logs a warning and returns `false`. Provide a custom `IAudioCaptureSource` via `controller.SetCustomCaptureSource(...)` (for example, bridging the browser Web Audio API) or fall back to `InputModality.DiscreteButtons`.
- **Mobile microphone permissions (Android / iOS):** Call `UnityMicrophoneCaptureSource.HasMicrophonePermission()` and `UnityMicrophoneCaptureSource.RequestMicrophonePermission()` before invoking `StartListening()` on Android and iOS builds.
- **Speaker bleed and audio feedback loops:** `playSynthInMicrophoneMode` defaults to `false` on `MelodyRecognitionController` so synthesized tones do not loop back into the microphone. Wire `onListeningStateChanged` (or `OnListeningStateChangedEvent`) to duck BGM volume during active microphone listening, and call `CalibrateAmbientNoise(...)` in noisy environments.
- **Locked songs do not trigger in Free Play:** Songs with `UnlockedByDefault = false` and `AlwaysDiscoverable = false` are excluded from `SongRepertoire.GetRecognizableSongs()` until unlocked with `Repertoire.UnlockSong(songId)` or completed in Guided/Learn Mode.
- **Stray-note forgiveness constraints:** `MelodyMatcher` only evaluates interior stray-note skips (`MaxStrayNotes` up to 2) on songs with at least 4 notes, and never skips the first or final note of a candidate match window.
- **Short prefix songs pause briefly before firing:** When a shorter song is a prefix of a longer recognizable song, `MelodyMatcher` waits up to `PrefixDisambiguationSeconds` (default `0.65` s) before committing the shorter song. Set `PrefixDisambiguationSeconds` to `0` if your repertoire has no overlapping song prefixes.
- **Monophonic pitch tracking:** `PitchDetectionJob` tracks a single dominant fundamental frequency per analysis window; simultaneous polyphonic chords or loud un-ducked background music can drop `Clarity` below `MinClarity`.

## License

[MIT](LICENSE.md)
