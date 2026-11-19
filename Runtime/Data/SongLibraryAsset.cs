using System.Collections.Generic;
using UnityEngine;

namespace MelodyRecognitionEngine.Data
{
    /// <summary>
    /// Collection of SongAssets and default Hum/Instrument/Whistle DetectionProfileAssets.
    /// </summary>
    [CreateAssetMenu(menuName = "MelodyRecognitionEngine/Song Library", fileName = "NewSongLibrary")]
    public sealed class SongLibraryAsset : ScriptableObject
    {
        [Header("Songs")]
        [SerializeField] private List<SongAsset> songs = new List<SongAsset>();

        [Header("Optional Default Profiles (Falls back to built-in presets if null)")]
        [SerializeField] private DetectionProfileAsset humProfile;
        [SerializeField] private DetectionProfileAsset instrumentProfile;
        [SerializeField] private DetectionProfileAsset whistleProfile;

        public IReadOnlyList<SongAsset> Songs => songs;
        public DetectionProfileAsset HumProfile => humProfile;
        public DetectionProfileAsset InstrumentProfile => instrumentProfile;
        public DetectionProfileAsset WhistleProfile => whistleProfile;

        public DetectionProfileConfig GetHumConfig() =>
            humProfile != null ? humProfile.Config : DetectionProfileConfig.CreateHumPreset();

        public DetectionProfileConfig GetInstrumentConfig() =>
            instrumentProfile != null ? instrumentProfile.Config : DetectionProfileConfig.CreateInstrumentPreset();

        public DetectionProfileConfig GetWhistleConfig() =>
            whistleProfile != null ? whistleProfile.Config : DetectionProfileConfig.CreateWhistlePreset();

        public void SetSongs(IEnumerable<SongAsset> newSongs)
        {
            songs.Clear();
            if (newSongs == null) return;
            foreach (SongAsset asset in newSongs)
            {
                if (asset != null && !songs.Contains(asset))
                {
                    songs.Add(asset);
                }
            }
        }
    }
}
