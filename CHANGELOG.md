# Changelog

All notable changes to MelodyRecognitionEngine are documented in this file.

## [1.0.0] - 2026-11-19

### Added
- Burst-Compiled DSP Pitch Tracker (`PitchDetectionJob`, `BurstPitchTracker`): Allocation-free `NativeArray<float>` ring buffer and `[BurstCompile]` `IJob` implementing cascaded biquad bandpass filtering, RMS & ambient noise-floor gating, YIN cumulative mean normalized difference (CMND) with parabolic interpolation, spectral flatness rejection, and harmonic/octave disambiguation.
- Note Segmenter (`NoteSegmenter`): Converts pitch frames into clean `NoteEvent` emissions with median/hysteresis smoothing, vibrato/glide tolerance, stray-blip rejection, amplitude/flux dip onset detection for repeated same-pitch notes, and inter-note silence timeouts.
- Detection Profiles & Custom Instruments (`DetectionProfileAsset`, `DetectionProfileConfig`, `InstrumentAsset`): Built-in Hum, Instrument, and Whistle presets, runtime `SetDetectionProfile(...)` and `EquipInstrument(...)` hot-swapping, customizable chromatic note layouts, optional instrument-specific song libraries, and procedural or pitch-shifted `AudioClip` timbres.
- Melody Matcher (`MelodyMatcher`): Auto-expanding sliding-window Free Play recognition for melodies of any length (relative semitone intervals by default with opt-in strict absolute pitch, optional rhythm ratio matching, stray-note forgiveness, prefix disambiguation window, live partial-match progress) and Guided / Learn Mode with step-by-step validation.
- Song Repertoire & Custom Songs (`SongRepertoire`, `CustomSongRecorder`): Unlock gating with per-song `AlwaysDiscoverable` secret overrides, plus runtime player-recorded custom songs with JSON serialization.
- Unified Input Sources (`INoteInputSource`, `IAudioCaptureSource`): `UnityMicrophoneCaptureSource` (Desktop/Mobile/XR + ambient calibration), `AudioClipCaptureSource`, `MicrophoneNoteInputSource`, and `DiscreteNoteInputSource` (virtual/button fallback).
- Runtime Controller & Presentation (`MelodyRecognitionController`, `MusicalStaffHUD`, `ProceduralOcarinaSynth`): Scene controller with BGM ducking hooks and UnityEvents, optional drop-in uGUI 5-line Musical Staff HUD, and lightweight procedural tone feedback synth.
- UI Toolkit Melody Workbench (`MelodyWorkbenchWindow`): Live mic DSP tuner/monitor, record-from-mic & octave-shifting piano-click song authoring into `SongAsset`s, and a synthetic/AudioClip dry-run simulator.
- Samples~ & EditMode Test Suite: Playable Ocarina Demo, data-driven InputSystem Virtual Instrument adapter, CommonEventsEngine bridge, and automated NUnit EditMode tests.
