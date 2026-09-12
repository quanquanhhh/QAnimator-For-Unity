using System;
using System.IO;
using QAnimator.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class QAnimatorValidationSetup
{
    private const string Command = "QAnimatorValidation.run";
    private const string ScenePath = "Assets/QAnimatorValidation/QAnimatorValidation.unity";
    private const string RunKey = "QAnimator.Validation.Run";
    static QAnimatorValidationSetup() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("QAnimatorValidation.demo") && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete("QAnimatorValidation.demo");
            SessionState.SetBool(RunKey, false);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            var gameType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameType != null) EditorWindow.GetWindow(gameType).maximized = true;
            EditorApplication.isPlaying = true;
        }
        if (File.Exists(Command) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Command);
            Run();
        }
        if (EditorApplication.isPlaying && SessionState.GetBool(RunKey, false) && File.Exists("QAnimatorTestResults/results.json"))
        {
            SessionState.SetBool(RunKey, false);
            EditorApplication.isPlaying = false;
        }
    }

    [MenuItem("Tools/QAnimator/Run Unity validation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            Directory.CreateDirectory("QAnimatorTestResults");
            if (File.Exists("QAnimatorTestResults/results.json")) File.Delete("QAnimatorTestResults/results.json");
            // A separate additive scene preserves unsaved work in the user's existing scene.
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (File.Exists(ScenePath)) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                else
                {
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    Build();
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
            }
            if (NeedsQEmoGraphicRebuild(scene))
            {
                EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                Build();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var label in root.GetComponentsInChildren<Text>()) FitLabel(label);
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(RunKey, true);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.isPlaying = true;
        }
        catch (Exception ex) { File.WriteAllText("QAnimatorTestResults/setup-error.txt", ex.ToString()); Debug.LogException(ex); }
    }

    private static bool NeedsQEmoGraphicRebuild(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var validation = root.GetComponent<QAnimatorValidation>();
            if (validation != null)
                return validation.Player == null || validation.Player.GetComponent<QEmoGraphic>() == null;
        }
        return true;
    }

    private static void Build()
    {
        var root = new GameObject("QAnimator Validation", typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000, 800);
        scaler.matchWidthOrHeight = .5f;
        var backdrop = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(root.transform, false);
        var backgroundRect = backdrop.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero; backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(.055f, .075f, .115f);
        var frame = new GameObject("Checkerboard", typeof(RectTransform), typeof(RawImage));
        frame.transform.SetParent(root.transform, false);
        frame.GetComponent<RectTransform>().sizeDelta = new Vector2(512, 512);
        var checker = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        var pixels = new Color32[1024];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) pixels[y * 32 + x] = (x / 16 + y / 16) % 2 == 0 ? new Color32(48, 60, 78, 255) : new Color32(73, 87, 105, 255);
        checker.SetPixels32(pixels); checker.Apply(); checker.filterMode = FilterMode.Point; checker.wrapMode = TextureWrapMode.Repeat;
        AssetDatabase.CreateAsset(checker, "Assets/QAnimatorValidation/Data/Checker.asset");
        frame.GetComponent<RawImage>().texture = checker;
        frame.GetComponent<RawImage>().uvRect = new Rect(0, 0, 16, 16);
        var view = new GameObject("Treasure Chest", typeof(RectTransform), typeof(QEmoGraphic));
        view.transform.SetParent(frame.transform, false);
        view.GetComponent<RectTransform>().sizeDelta = new Vector2(512, 512);
        var suite = root.AddComponent<QAnimatorValidation>();
        suite.Player = view.GetComponent<QEmoGraphic>();
        suite.Alpha = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/QAnimatorValidation/Data/treasure_chest_open_alpha.bytes");
        suite.Opaque = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/QAnimatorValidation/Data/treasure_chest_open.bytes");
        var graphic = view.GetComponent<QEmoGraphic>();
        var serialized = new SerializedObject(graphic);
        serialized.FindProperty("_source").objectReferenceValue = suite.Alpha;
        var additional = serialized.FindProperty("_additionalAnimations");
        additional.arraySize = 1;
        additional.GetArrayElementAtIndex(0).objectReferenceValue = suite.Opaque;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        graphic.ReloadEditorPreview();
        suite.Status = Label(root.transform, "QAnimator / Unity 6 · RGBA animation", new Vector2(0, 325), 26);
        Label(root.transform, "512 × 512   /   30 FPS   /   4 seconds", new Vector2(0, -310), 22);
        var controls = root.AddComponent<QAnimatorDemoControls>();
        controls.Player = suite.Player;
    }

    private static void FitLabel(Text text)
    {
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0, .5f);
        rect.anchorMax = new Vector2(1, .5f);
        rect.sizeDelta = new Vector2(-64, 90);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = text.fontSize;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    private static Text Label(Transform parent, string value, Vector2 position, int size)
    {
        var host = new GameObject("Label", typeof(RectTransform), typeof(Text));
        host.transform.SetParent(parent, false);
        var rect = host.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(960, 70); rect.anchoredPosition = position;
        var text = host.GetComponent<Text>(); text.text = value; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.color = Color.white;
        FitLabel(text);
        return text;
    }
}
