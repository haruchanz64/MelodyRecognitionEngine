# MelodyRecognitionEngine

Real-time Burst-compiled humming and acoustic instrument melody recognition for Unity 6, inspired by modern musical instrument and vocal-hum recognition mechanics.

Players can hum, whistle, play a physical instrument (ocarina, flute, guitar, piano) into their microphone, or fall back to accessible discrete button inputs. Every input source feeds a single unified note pipeline, unlockable song repertoire, Guided/Learn Mode, and Musical Staff HUD.

---

## Features

- Burst-Compiled YIN DSP Pitch Tracker (`PitchDetectionJob`, `BurstPitchTracker`)
  - Allocation-free `NativeArray<float>` ring and scratch buffers with asynchronous `IJob` scheduling (`Schedule()` at runtime, `.Run()` for deterministic tests).
  - Cascaded 2nd-order Butterworth bandpass filtering, adaptive ambient noise-floor gating, broadband noise rejection, YIN Cumulative Mean Normalized Difference (CMND) with parabolic sub-sample interpolation, and 2nd-harmonic octave correction.
- Acoustic Detection Profiles & Custom Instruments (`DetectionProfileAsset`, `InstrumentAsset`)
  - Built-in Hum, Instrument, and Whistle presets plus customizable ScriptableObject profiles hot-swappable at runtime via `SetDetectionProfile(...)`.
  - `InstrumentAsset` bundles a `DetectionProfileAsset`, an optional instrument-specific `SongLibraryAsset`, a customizable chromatic note layout of any size, and a procedural or pitch-shifted `AudioClip` playback timbre via `EquipInstrument(...)`.
- Note Segmentation (`NoteSegmenter`)
  - Rolling median filter, vibrato/glide hysteresis, stray-blip rejection, repeated same-pitch re-articulation onset detection, and phrase silence timeouts.
- Melody Matching (`MelodyMatcher`)
  - Relative semitone intervals by default (mean-centered, key- and octave-invariant), plus opt-in strict absolute concert pitch per song or profile.
  - Tempo-free pitch sequences by default, plus opt-in inter-onset rhythm ratio validation.
  - Full chromatic pitch range (`C1`–`C8`) and auto-expanding sliding-window history for songs of any length, with live `SongMatchProgress`, stray-note forgiveness, and a prefix disambiguation window when one song is a prefix of another.
  - Guided / Learn Mode for step-by-step teaching and automatic song unlocking.
- Repertoire & Player-Recorded Custom Songs (`SongRepertoire`, `CustomSongRecorder`)
  - Unlock gating (prevents brute-forcing unlearned songs) with per-song `AlwaysDiscoverable` secret overrides.
  - Runtime player-recorded custom songs (e.g., *Scarecrow's Song*) with conflict detection and JSON save/load serialization.
- Unified Input Abstraction (`INoteInputSource`, `IAudioCaptureSource`)
  - `UnityMicrophoneCaptureSource` (Desktop, Mobile, XR + ambient noise calibration), `AudioClipCaptureSource` (AudioClips & synthetic PCM streams), and `DiscreteNoteInputSource` (controller/keyboard fallback).
- Runtime Presentation & UI Toolkit Workbench
  - `MelodyRecognitionController` with BGM ducking hooks (`onListeningStateChanged`) and Inspector `UnityEvent`s.
  - Optional uGUI `MusicalStaffHUD` (5-line staff, live pitch needle, scrolling notes, ghosted Learn Mode guide notes, mic level meter, mode buttons).
  - Optional `ProceduralOcarinaSynth` (`OnAudioFilterRead` procedural voices for Ocarina, Flute, HarpPluck, BellChime, and SquareRetro, or pitch-shifted `AudioClip` samples, plus song-match fanfare).
  - UI Toolkit Melody Workbench (`Tools > HaruChanZ64 > Audio > MelodyRecognitionEngine > Melody Workbench`) with a Live Mic Tuner, Mic/Piano Song Authoring with octave-shift controls (`C1`–`C7`), and a deterministic Dry-Run Simulator.

---

## Installation (UPM Git URL)

1. Open `Window → Package Manager` in Unity 6 (`6000.0+`).
2. Click `+ → Add package from git URL...`
3. Enter:
   ```text
   https://github.com/haruchanz64/MelodyRecognitionEngine.git
   ```

Dependencies (`com.unity.burst`, `com.unity.collections`, `com.unity.ugui`) are declared in `package.json` and resolved automatically.

---

## Quick Start

1. In the Hierarchy, choose `GameObject → MelodyRecognitionEngine → Create Melody Recognition Rig` (creates `MelodyRecognitionController`, `ProceduralOcarinaSynth`, and `MusicalStaffHUD`).
2. Create songs via `Create → MelodyRecognitionEngine → Song Asset` or open `Tools → HaruChanZ64 → Audio → MelodyRecognitionEngine → Melody Workbench` to record directly from your microphone or click piano keys.
3. Group songs into a Song Library (`Create → MelodyRecognitionEngine → Song Library`) and assign it to `MelodyRecognitionController`.
4. Wire `onSongMatched` (and `onListeningStateChanged` to duck BGM while listening) and press Play.

```csharp
// Register a song via code and listen for matches
controller.Repertoire.RegisterSong(
    SongDefinition.FromNotation("song_lullaby", "Royal Lullaby", "B4 D5 A4 B4 D5 A4", unlockedByDefault: true)
);

controller.OnSongMatchedEvent += match =>
{
    Debug.Log($"Played {match.DisplayName} (confidence {match.Confidence:P0}, shift {match.TranspositionSemitones:+0;-0;0} st)");
};

controller.SetModality(InputModality.MicrophoneHum);
controller.StartListening();
```

---

## Included Samples (`Samples~`)

Import any sample directly from the Samples tab in Package Manager:

- Playable Ocarina Demo: 6-song demo with Hum/Instrument/Keys toggles, Guided/Learn Mode, BGM ducking, and a custom song slot.
- InputSystem Virtual Instrument Adapter: Maps a configurable list of `com.unity.inputsystem` actions or keyboard keys (with sharp/flat and octave modifiers, or synced from an `InstrumentAsset`) into `DiscreteNoteInputSource`.
- CommonEventsEngine Bridge: Triggers `CommonEventDirector` events, switches, and signals when melodies are recognized.

---

## License

MIT License. See [LICENSE.md](LICENSE.md).
