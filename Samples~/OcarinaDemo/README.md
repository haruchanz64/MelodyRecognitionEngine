# Playable Ocarina Demo Sample

Attach `OcarinaDemoController` to an empty GameObject in any scene and press Play. On startup, the demo does four things:

1. Registers 6 sample songs: 4 unlocked, 1 locked for Guided/Learn Mode, and 1 `AlwaysDiscoverable` secret song.
2. Spawns the built-in uGUI `MusicalStaffHUD` with Hum, Inst, and Keys mode buttons.
3. Ducks any assigned `bgmAudioSource` while microphone listening is active to limit speaker bleed.
4. Exposes `StartLearnDawnChorale()` for Guided/Learn Mode alongside `StartRecordScarecrowSong()` and `SaveScarecrowSong()` for recording a custom song at runtime.
