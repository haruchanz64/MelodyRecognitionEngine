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
        [Serializable]
        public struct SongEventBinding
        {
            public string SongId;
            public string CommonEventId;
            public string SwitchToSetOn;
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
