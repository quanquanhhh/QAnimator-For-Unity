using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace QAnimator.Unity.Editor
{
    public static class QAnimatorDemoMenu
    {
        [MenuItem("Tools/QAnimator/Create demo from selected .bytes")]
        private static void CreateDemo()
        {
            var asset = Selection.activeObject as TextAsset;
            if (asset == null || !AssetDatabase.GetAssetPath(asset).EndsWith(".bytes"))
            {
                EditorUtility.DisplayDialog("QAnimator", "Select a QAnimator .bytes asset in the Project window first.", "OK");
                return;
            }
            var root = new GameObject("QAnimator Demo", typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Create QAnimator demo");
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var screen = new GameObject("QEmoGraphic", typeof(RectTransform), typeof(QEmoGraphic));
            screen.transform.SetParent(root.transform, false);
            var rect = screen.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(512, 512);
            var graphic = screen.GetComponent<QEmoGraphic>();
            var serialized = new SerializedObject(graphic);
            serialized.FindProperty("_source").objectReferenceValue = asset;
            serialized.FindProperty("_autoPlay").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            graphic.ReloadEditorPreview();
            var controls = root.AddComponent<QAnimatorDemoControls>();
            controls.Player = graphic;
            Selection.activeGameObject = root;
        }
    }
}
