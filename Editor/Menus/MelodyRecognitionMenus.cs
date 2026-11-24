using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Presentation;
using UnityEditor;
using UnityEngine;

namespace MelodyRecognitionEngine.Editor.Menus
{
    /// <summary>
    /// Unity Editor menu commands for creating pre-wired MelodyRecognitionEngine scene rigs.
    /// </summary>
    public static class MelodyRecognitionMenus
    {
        /// <summary>
        /// Creates a Hierarchy rig containing <see cref="MelodyRecognitionController"/>, <see cref="ProceduralOcarinaSynth"/>, and a bound child <see cref="MusicalStaffHUD"/>.
        /// </summary>
        /// <param name="menuCommand">Context command supplying the optional parent GameObject.</param>
        [MenuItem("GameObject/MelodyRecognitionEngine/Create Melody Recognition Rig", false, 10)]
        public static void CreateMelodyRecognitionRig(MenuCommand menuCommand)
        {
            var root = new GameObject("MelodyRecognitionRig");
            GameObjectUtility.SetParentAndAlign(root, menuCommand.context as GameObject);

            var controller = root.AddComponent<MelodyRecognitionController>();
            root.AddComponent<ProceduralOcarinaSynth>();

            var hudGo = new GameObject("MusicalStaffHUD");
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<MusicalStaffHUD>();
            hud.BindController(controller);

            Undo.RegisterCreatedObjectUndo(root, "Create Melody Recognition Rig");
            Selection.activeGameObject = root;
        }
    }
}
