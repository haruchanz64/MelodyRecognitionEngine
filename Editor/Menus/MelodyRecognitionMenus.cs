using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Presentation;
using UnityEditor;
using UnityEngine;

namespace MelodyRecognitionEngine.Editor.Menus
{
    public static class MelodyRecognitionMenus
    {
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
