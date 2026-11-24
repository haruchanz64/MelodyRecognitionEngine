using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEditor;
using UnityEngine;

namespace MelodyRecognitionEngine.Editor.Inspectors
{
    /// <summary>
    /// Custom Inspector for <see cref="DetectionProfileAsset"/> adding quick preset buttons for Hum, Instrument, and Whistle defaults.
    /// </summary>
    [CustomEditor(typeof(DetectionProfileAsset))]
    public sealed class DetectionProfileInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Renders the quick-preset toolbar with Undo support above the default serialized fields.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var profile = (DetectionProfileAsset)target;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Acoustic Presets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Hum Preset"))
            {
                Undo.RecordObject(profile, "Apply Hum Preset");
                profile.ApplyPreset(DetectionPresetKind.Hum);
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("Apply Instrument Preset"))
            {
                Undo.RecordObject(profile, "Apply Instrument Preset");
                profile.ApplyPreset(DetectionPresetKind.Instrument);
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("Apply Whistle Preset"))
            {
                Undo.RecordObject(profile, "Apply Whistle Preset");
                profile.ApplyPreset(DetectionPresetKind.Whistle);
                EditorUtility.SetDirty(profile);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);
            DrawDefaultInspector();
        }
    }
}
