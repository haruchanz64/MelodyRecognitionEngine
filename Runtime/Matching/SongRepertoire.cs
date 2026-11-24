using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Data;
using UnityEngine;

namespace MelodyRecognitionEngine.Matching
{
    /// <summary>
    /// Runtime song registry managing unlock gating, AlwaysDiscoverable secret songs,
    /// player-recorded custom songs, and JSON save/load state serialization.
    /// </summary>
    public sealed class SongRepertoire
    {
        [Serializable]
        private sealed class SavePayload
        {
            public int Version = 1;
            public List<string> UnlockedSongIds = new List<string>();
            public List<SongDefinition> CustomSongs = new List<SongDefinition>();
        }

        private readonly Dictionary<string, SongDefinition> _songsById = new Dictionary<string, SongDefinition>(StringComparer.Ordinal);
        private readonly List<SongDefinition> _allSongs = new List<SongDefinition>();
        private readonly HashSet<string> _unlockedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SongDefinition> _activeScratch = new List<SongDefinition>();

        /// <summary>Invoked when a song is added or replaced via <see cref="RegisterSong"/>.</summary>
        public event Action<SongDefinition> OnSongRegistered;
        /// <summary>Invoked when a locked song transitions to unlocked.</summary>
        public event Action<string> OnSongUnlocked;
        /// <summary>Invoked when an unlocked song transitions to locked.</summary>
        public event Action<string> OnSongLocked;
        /// <summary>Invoked when a player-recorded custom song is saved via <see cref="RegisterCustomSong"/>.</summary>
        public event Action<SongDefinition> OnCustomSongSaved;
        /// <summary>Invoked when a song is removed by ID.</summary>
        public event Action<string> OnSongRemoved;

        /// <summary>All songs currently registered in the repertoire, regardless of lock state.</summary>
        public IReadOnlyList<SongDefinition> AllSongs => _allSongs;
        /// <summary>Total number of registered songs.</summary>
        public int Count => _allSongs.Count;

        /// <summary>
        /// Removes all registered songs and clears all unlock flags.
        /// </summary>
        public void Clear()
        {
            _songsById.Clear();
            _allSongs.Clear();
            _unlockedIds.Clear();
        }

        /// <summary>
        /// Registers every valid <see cref="SongAsset"/> in <paramref name="library"/>.
        /// </summary>
        /// <param name="library">Song library asset to read from (ignored if null).</param>
        /// <param name="clearExisting">When true, clears existing songs and unlocks before loading.</param>
        public void LoadFromLibrary(SongLibraryAsset library, bool clearExisting = false)
        {
            if (clearExisting) Clear();
            if (library == null || library.Songs == null) return;

            for (int i = 0; i < library.Songs.Count; i++)
            {
                SongAsset asset = library.Songs[i];
                if (asset != null && asset.Definition != null)
                {
                    RegisterSong(asset.Definition);
                }
            }
        }

        /// <summary>
        /// Clones and registers (or replaces by <see cref="SongDefinition.SongId"/>) a valid song definition.
        /// </summary>
        /// <param name="song">Song definition to register.</param>
        /// <param name="initialUnlockOverride">Optional override for <see cref="SongDefinition.UnlockedByDefault"/>.</param>
        /// <returns>True if <paramref name="song"/> was valid and registered.</returns>
        public bool RegisterSong(SongDefinition song, bool? initialUnlockOverride = null)
        {
            if (song == null || !song.IsValid)
            {
                return false;
            }

            SongDefinition copy = song.Clone();
            if (_songsById.ContainsKey(copy.SongId))
            {
                for (int i = 0; i < _allSongs.Count; i++)
                {
                    if (string.Equals(_allSongs[i].SongId, copy.SongId, StringComparison.Ordinal))
                    {
                        _allSongs[i] = copy;
                        break;
                    }
                }
                _songsById[copy.SongId] = copy;
            }
            else
            {
                _songsById.Add(copy.SongId, copy);
                _allSongs.Add(copy);
            }

            bool unlock = initialUnlockOverride ?? copy.UnlockedByDefault;
            if (unlock)
            {
                _unlockedIds.Add(copy.SongId);
            }

            OnSongRegistered?.Invoke(copy);
            return true;
        }

        /// <summary>
        /// Marks a song as a player-recorded custom song, registers it, and emits <see cref="OnCustomSongSaved"/>.
        /// </summary>
        /// <param name="customSong">Custom song definition to store.</param>
        /// <param name="unlockImmediately">When true, immediately unlocks the custom song for Free Play recognition.</param>
        /// <returns>True if <paramref name="customSong"/> was valid and saved.</returns>
        public bool RegisterCustomSong(SongDefinition customSong, bool unlockImmediately = true)
        {
            if (customSong == null || !customSong.IsValid)
            {
                return false;
            }

            SongDefinition copy = customSong.Clone();
            copy.IsCustomSong = true;
            copy.UnlockedByDefault = unlockImmediately;

            bool ok = RegisterSong(copy, unlockImmediately);
            if (ok)
            {
                OnCustomSongSaved?.Invoke(copy);
            }

            return ok;
        }

        /// <summary>
        /// Removes the song with the given ID and clears its unlock state.
        /// </summary>
        /// <returns>True if a matching song was found and removed.</returns>
        public bool RemoveSong(string songId)
        {
            if (string.IsNullOrWhiteSpace(songId) || !_songsById.Remove(songId))
            {
                return false;
            }

            _unlockedIds.Remove(songId);
            for (int i = _allSongs.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_allSongs[i].SongId, songId, StringComparison.Ordinal))
                {
                    _allSongs.RemoveAt(i);
                }
            }

            OnSongRemoved?.Invoke(songId);
            return true;
        }

        /// <summary>
        /// Looks up a registered song by its exact ordinal <paramref name="songId"/>.
        /// </summary>
        public bool TryGetSong(string songId, out SongDefinition song)
        {
            if (string.IsNullOrWhiteSpace(songId))
            {
                song = null;
                return false;
            }

            return _songsById.TryGetValue(songId, out song);
        }

        /// <summary>
        /// Returns true if <paramref name="songId"/> is currently unlocked.
        /// </summary>
        public bool IsUnlocked(string songId)
        {
            return !string.IsNullOrWhiteSpace(songId) && _unlockedIds.Contains(songId);
        }

        /// <summary>
        /// Returns true if <paramref name="songId"/> is registered and either unlocked or marked <see cref="SongDefinition.AlwaysDiscoverable"/>.
        /// </summary>
        public bool IsRecognizable(string songId)
        {
            if (!TryGetSong(songId, out SongDefinition song)) return false;
            return _unlockedIds.Contains(songId) || song.AlwaysDiscoverable;
        }

        /// <summary>
        /// Unlocks a registered song for Free Play recognition and fires <see cref="OnSongUnlocked"/> if it was previously locked.
        /// </summary>
        /// <returns>True if the song existed and transitioned from locked to unlocked.</returns>
        public bool UnlockSong(string songId)
        {
            if (string.IsNullOrWhiteSpace(songId) || !_songsById.ContainsKey(songId))
            {
                return false;
            }

            if (_unlockedIds.Add(songId))
            {
                OnSongUnlocked?.Invoke(songId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Locks a song and fires <see cref="OnSongLocked"/> if it was previously unlocked.
        /// </summary>
        /// <returns>True if the song transitioned from unlocked to locked.</returns>
        public bool LockSong(string songId)
        {
            if (string.IsNullOrWhiteSpace(songId)) return false;

            if (_unlockedIds.Remove(songId))
            {
                OnSongLocked?.Invoke(songId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Unlocks every song currently registered in the repertoire.
        /// </summary>
        public void UnlockAll()
        {
            for (int i = 0; i < _allSongs.Count; i++)
            {
                UnlockSong(_allSongs[i].SongId);
            }
        }

        /// <summary>
        /// Locks every currently unlocked song in the repertoire.
        /// </summary>
        public void LockAll()
        {
            var ids = new List<string>(_unlockedIds);
            for (int i = 0; i < ids.Count; i++)
            {
                LockSong(ids[i]);
            }
        }

        /// <summary>
        /// Returns all currently recognizable songs (either unlocked OR marked AlwaysDiscoverable).
        /// Reuses an internal buffer to avoid per-note allocations.
        /// </summary>
        public IReadOnlyList<SongDefinition> GetRecognizableSongs()
        {
            _activeScratch.Clear();
            for (int i = 0; i < _allSongs.Count; i++)
            {
                SongDefinition song = _allSongs[i];
                if (_unlockedIds.Contains(song.SongId) || song.AlwaysDiscoverable)
                {
                    _activeScratch.Add(song);
                }
            }

            return _activeScratch;
        }

        /// <summary>
        /// Serializes unlocked song IDs and player-recorded custom songs to a Unity <see cref="JsonUtility"/> JSON string.
        /// </summary>
        public string ExportStateJson(bool prettyPrint = true)
        {
            var payload = new SavePayload();
            foreach (string id in _unlockedIds)
            {
                payload.UnlockedSongIds.Add(id);
            }

            for (int i = 0; i < _allSongs.Count; i++)
            {
                if (_allSongs[i].IsCustomSong)
                {
                    payload.CustomSongs.Add(_allSongs[i].Clone());
                }
            }

            return JsonUtility.ToJson(payload, prettyPrint);
        }

        /// <summary>
        /// Restores unlocked song IDs and player-recorded custom songs from JSON created by <see cref="ExportStateJson"/>.
        /// Unknown song IDs in the unlock list are ignored until registered.
        /// </summary>
        /// <param name="json">Serialized repertoire state JSON.</param>
        /// <param name="clearExistingUnlocks">When true, clears current unlocks before applying saved unlock IDs.</param>
        /// <returns>True if the JSON payload parsed cleanly.</returns>
        public bool ImportStateJson(string json, bool clearExistingUnlocks = true)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            SavePayload payload;
            try
            {
                payload = JsonUtility.FromJson<SavePayload>(json);
            }
            catch
            {
                return false;
            }

            if (payload == null)
            {
                return false;
            }

            if (clearExistingUnlocks)
            {
                _unlockedIds.Clear();
            }

            if (payload.CustomSongs != null)
            {
                for (int i = 0; i < payload.CustomSongs.Count; i++)
                {
                    SongDefinition custom = payload.CustomSongs[i];
                    if (custom != null && custom.IsValid)
                    {
                        custom.IsCustomSong = true;
                        RegisterCustomSong(custom, unlockImmediately: false);
                    }
                }
            }

            if (payload.UnlockedSongIds != null)
            {
                for (int i = 0; i < payload.UnlockedSongIds.Count; i++)
                {
                    string id = payload.UnlockedSongIds[i];
                    if (_songsById.ContainsKey(id))
                    {
                        _unlockedIds.Add(id);
                    }
                }
            }

            return true;
        }
    }
}
