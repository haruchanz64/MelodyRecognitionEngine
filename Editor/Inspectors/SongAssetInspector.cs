using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using MelodyRecognitionEngine.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace MelodyRecognitionEngine.Editor.Inspectors
{
    [CustomEditor(typeof(SongAsset))]
    public sealed class SongAssetInspector : UnityEditor.Editor
    {
        private string _quickNotation;
        private SongAsset _lastTarget;

        public override void OnInspectorGUI()
        {
            var asset = (SongAsset)target;
            if (_lastTarget != asset)
            {
                _lastTarget = asset;
                _quickNotation = asset.Definition != null ? asset.Definition.ToNotationString() : string.Empty;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Notation Editor", EditorStyles.boldLabel);
            _quickNotation = EditorGUILayout.TextField("Notes (e.g. B4 D5 A4)", _quickNotation);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Notation"))
            {
                Undo.RecordObject(asset, "Apply Song Notation");
                if (asset.Definition.SetNotesFromNotation(_quickNotation))
                {
                    EditorUtility.SetDirty(asset);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Notation", "Use space-separated note names like 'B4 D5 A4' or with durations 'B4:2 D5:1 A4:3'.", "OK");
                }
            }

            if (GUILayout.Button("Open in Workbench"))
            {
                MelodyWorkbenchWindow.OpenForSong(asset);
            }
            EditorGUILayout.EndHorizontal();

            if (asset.Definition != null && asset.Definition.NoteCount >= 2)
            {
                int[] intervals = asset.Definition.GetRelativeIntervals();
                string[] parts = new string[intervals.Length + 1];
                parts[0] = "0";
                for (int i = 0; i < intervals.Length; i++)
                {
                    parts[i + 1] = intervals[i] >= 0 ? $"+{intervals[i]}" : intervals[i].ToString();
                }

                EditorGUILayout.HelpBox(
                    $"Notes: {asset.Definition.ToNotationString()}\nRelative Semitones: [{string.Join(", ", parts)}]",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);
            DrawDefaultInspector();
        }
    }
}
