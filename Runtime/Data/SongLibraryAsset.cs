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

        /// <summary>Songs registered into a repertoire when this library is loaded.</summary>
        public IReadOnlyList<SongAsset> Songs => songs;
        /// <summary>Optional Hum mode detection profile override.</summary>
        public DetectionProfileAsset HumProfile => humProfile;
        /// <summary>Optional Instrument mode detection profile override.</summary>
        public DetectionProfileAsset InstrumentProfile => instrumentProfile;
        /// <summary>Optional Whistle mode detection profile override.</summary>
        public DetectionProfileAsset WhistleProfile => whistleProfile;

        /// <summary>
        /// Returns the sanitized Hum DSP profile from <see cref="HumProfile"/> or the built-in Hum preset if unassigned.
        /// </summary>
        public DetectionProfileConfig GetHumConfig() =>
            humProfile != null ? humProfile.Config : DetectionProfileConfig.CreateHumPreset();

        /// <summary>
        /// Returns the sanitized Instrument DSP profile from <see cref="InstrumentProfile"/> or the built-in Instrument preset if unassigned.
        /// </summary>
        public DetectionProfileConfig GetInstrumentConfig() =>
            instrumentProfile != null ? instrumentProfile.Config : DetectionProfileConfig.CreateInstrumentPreset();

        /// <summary>
        /// Returns the sanitized Whistle DSP profile from <see cref="WhistleProfile"/> or the built-in Whistle preset if unassigned.
        /// </summary>
        public DetectionProfileConfig GetWhistleConfig() =>
            whistleProfile != null ? whistleProfile.Config : DetectionProfileConfig.CreateWhistlePreset();

        /// <summary>
        /// Replaces the library's song list with unique non-null assets from <paramref name="newSongs"/>.
        /// </summary>
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
