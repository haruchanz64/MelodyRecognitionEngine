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

        public event Action<SongDefinition> OnSongRegistered;
        public event Action<string> OnSongUnlocked;
        public event Action<string> OnSongLocked;
        public event Action<SongDefinition> OnCustomSongSaved;
        public event Action<string> OnSongRemoved;

        public IReadOnlyList<SongDefinition> AllSongs => _allSongs;
        public int Count => _allSongs.Count;

        public void Clear()
        {
            _songsById.Clear();
            _allSongs.Clear();
            _unlockedIds.Clear();
        }

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

        public bool TryGetSong(string songId, out SongDefinition song)
        {
            if (string.IsNullOrWhiteSpace(songId))
            {
                song = null;
                return false;
            }

            return _songsById.TryGetValue(songId, out song);
        }

        public bool IsUnlocked(string songId)
        {
            return !string.IsNullOrWhiteSpace(songId) && _unlockedIds.Contains(songId);
        }

        public bool IsRecognizable(string songId)
        {
            if (!TryGetSong(songId, out SongDefinition song)) return false;
            return _unlockedIds.Contains(songId) || song.AlwaysDiscoverable;
        }

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

        public void UnlockAll()
        {
            for (int i = 0; i < _allSongs.Count; i++)
            {
                UnlockSong(_allSongs[i].SongId);
            }
        }

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
