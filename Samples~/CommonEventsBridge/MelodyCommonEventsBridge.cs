using System;
using System.Collections.Generic;
using CommonEventsEngine.Components;
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using UnityEngine;

namespace MelodyRecognitionEngine.Samples.CommonEventsBridge
{
    /// <summary>
    /// Optional bridge connecting MelodyRecognitionController song matches to CommonEventsEngine
    /// (triggering CommonEventAssets by ID, toggling Switches, or emitting Signals).
    /// Requires com.haruchanz64.common-events-engine in the host project.
    /// </summary>
    public sealed class MelodyCommonEventsBridge : MonoBehaviour
    {
        /// <summary>
        /// Maps a recognized song ID to optional CommonEventsEngine switch, signal, and event actions.
        /// </summary>
        [Serializable]
        public struct SongEventBinding
        {
            /// <summary>Exact <see cref="SongMatchResult.SongId"/> that activates this binding.</summary>
            public string SongId;
            /// <summary>Optional CommonEvent asset ID triggered via <c>TriggerEventById</c>.</summary>
            public string CommonEventId;
            /// <summary>Optional global switch name turned on via <c>SetSwitchOn</c>.</summary>
            public string SwitchToSetOn;
            /// <summary>Optional signal name broadcast via <c>EmitSignal</c>.</summary>
            public string SignalToEmit;
        }

        [SerializeField] private MelodyRecognitionController melodyController;
        [SerializeField] private CommonEventDirector eventDirector;
        [SerializeField] private List<SongEventBinding> bindings = new List<SongEventBinding>();

        private void OnEnable()
        {
            if (melodyController == null) melodyController = FindAnyObjectByType<MelodyRecognitionController>();
            if (eventDirector == null) eventDirector = CommonEventDirector.ActiveInstance ?? FindAnyObjectByType<CommonEventDirector>();

            if (melodyController != null)
            {
                melodyController.OnSongMatchedEvent += HandleSongMatched;
            }
        }

        private void OnDisable()
        {
            if (melodyController != null)
            {
                melodyController.OnSongMatchedEvent -= HandleSongMatched;
            }
        }

        /// <summary>
        /// Dispatches matching <see cref="SongEventBinding"/> entries to the active <see cref="CommonEventDirector"/>.
        /// </summary>
        private void HandleSongMatched(SongMatchResult match)
        {
            if (eventDirector == null) return;

            for (int i = 0; i < bindings.Count; i++)
            {
                SongEventBinding b = bindings[i];
                if (!string.Equals(b.SongId, match.SongId, StringComparison.Ordinal)) continue;

                if (!string.IsNullOrWhiteSpace(b.SwitchToSetOn))
                {
                    eventDirector.SetSwitchOn(b.SwitchToSetOn);
                }

                if (!string.IsNullOrWhiteSpace(b.SignalToEmit))
                {
                    eventDirector.EmitSignal(b.SignalToEmit);
                }

                if (!string.IsNullOrWhiteSpace(b.CommonEventId))
                {
                    eventDirector.TriggerEventById(b.CommonEventId);
                }
            }
        }
    }
}
