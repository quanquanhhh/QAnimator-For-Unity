using System.Collections.Generic;
using QAnimator.Unity;
using UnityEditor;
using UnityEngine;

namespace QAnimator.Unity.Editor
{
    [CustomEditor(typeof(QEmoGraphic))]
    public sealed class QEmoGraphicEditor : UnityEditor.Editor
    {
        private SerializedProperty _source;
        private SerializedProperty _additionalAnimations;
        private SerializedProperty _animation;
        private SerializedProperty _autoPlay;
        private SerializedProperty _loop;
        private SerializedProperty _speed;
        private SerializedProperty _useUnscaledTime;
        private SerializedProperty _filterMode;
        private SerializedProperty _color;
        private SerializedProperty _material;
        private SerializedProperty _raycastTarget;
        private SerializedProperty _maskable;
        private double _lastEditorTime;

        private void OnEnable()
        {
            _source = serializedObject.FindProperty("_source");
            _additionalAnimations = serializedObject.FindProperty("_additionalAnimations");
            _animation = serializedObject.FindProperty("_animation");
            _autoPlay = serializedObject.FindProperty("_autoPlay");
            _loop = serializedObject.FindProperty("_loop");
            _speed = serializedObject.FindProperty("_speed");
            _useUnscaledTime = serializedObject.FindProperty("_useUnscaledTime");
            _filterMode = serializedObject.FindProperty("_filterMode");
            _color = serializedObject.FindProperty("m_Color");
            _material = serializedObject.FindProperty("m_Material");
            _raycastTarget = serializedObject.FindProperty("m_RaycastTarget");
            _maskable = serializedObject.FindProperty("m_Maskable");

            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdatePreview;
            Undo.undoRedoPerformed += ReloadAfterUndo;

            if (!Application.isPlaying)
                ((QEmoGraphic)target).ReloadEditorPreview();
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            Undo.undoRedoPerformed -= ReloadAfterUndo;
            if (!Application.isPlaying && target != null)
                ((QEmoGraphic)target).Pause();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Object oldSource = _source.objectReferenceValue;
            int oldFilterMode = _filterMode.enumValueIndex;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_source, new GUIContent("Animation Bytes"));
            EditorGUILayout.PropertyField(_additionalAnimations, new GUIContent("Additional Animations"), true);

            if (oldSource != _source.objectReferenceValue &&
                (string.IsNullOrEmpty(_animation.stringValue) ||
                 (oldSource != null && _animation.stringValue == oldSource.name)))
            {
                _animation.stringValue = _source.objectReferenceValue == null
                    ? string.Empty
                    : _source.objectReferenceValue.name;
            }
            DrawAnimationPopup();
            bool animationSelectionChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_autoPlay, new GUIContent("Auto Play"));
            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_speed);
            EditorGUILayout.PropertyField(_useUnscaledTime);
            EditorGUILayout.PropertyField(_filterMode);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            DrawIfPresent(_color);
            DrawIfPresent(_material);
            DrawIfPresent(_raycastTarget);
            DrawIfPresent(_maskable);

            serializedObject.ApplyModifiedProperties();

            var graphic = (QEmoGraphic)target;
            if (!Application.isPlaying &&
                (animationSelectionChanged || oldFilterMode != _filterMode.enumValueIndex))
            {
                graphic.ReloadEditorPreview();
                EditorUtility.SetDirty(graphic);
            }

            if (!string.IsNullOrEmpty(graphic.LastError))
                EditorGUILayout.HelpBox(graphic.LastError, MessageType.Error);

            using (new EditorGUI.DisabledScope(!graphic.IsLoaded))
            {
                if (GUILayout.Button("Set Native Size"))
                {
                    Undo.RecordObject(graphic.rectTransform, "Set QEmoGraphic Native Size");
                    graphic.SetNativeSize();
                }
            }

            DrawPreview(graphic);
        }

        private static void DrawIfPresent(SerializedProperty property)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property);
        }

        private void DrawAnimationPopup()
        {
            var names = new List<string>();
            AddAnimationName(names, _source.objectReferenceValue as TextAsset);
            for (int i = 0; i < _additionalAnimations.arraySize; i++)
                AddAnimationName(names, _additionalAnimations.GetArrayElementAtIndex(i).objectReferenceValue as TextAsset);

            if (names.Count == 0)
            {
                _animation.stringValue = string.Empty;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Animation", 0, new[] { "None" });
                return;
            }

            int selected = names.IndexOf(_animation.stringValue);
            if (selected < 0)
                selected = 0;
            int next = EditorGUILayout.Popup("Animation", selected, names.ToArray());
            _animation.stringValue = names[next];
        }

        private static void AddAnimationName(List<string> names, TextAsset asset)
        {
            if (asset != null && !names.Contains(asset.name))
                names.Add(asset.name);
        }

        private void ReloadAfterUndo()
        {
            if (Application.isPlaying || target == null)
                return;

            serializedObject.Update();
            ((QEmoGraphic)target).ReloadEditorPreview();
            Repaint();
        }

        private static void DrawPreview(QEmoGraphic graphic)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            float aspect = graphic.Width > 0 && graphic.Height > 0
                ? graphic.Width / (float)graphic.Height
                : 1f;
            Rect area = GUILayoutUtility.GetRect(64f, 260f, 64f, 260f);
            Rect imageRect = FitAspect(area, aspect);
            DrawCheckerboard(imageRect);
            if (graphic.Texture != null)
                EditorGUI.DrawPreviewTexture(imageRect, graphic.Texture, null, ScaleMode.ScaleToFit);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!graphic.IsLoaded))
            {
                if (GUILayout.Button("Play"))
                    graphic.Play();
                if (GUILayout.Button("Pause"))
                    graphic.Pause();
                if (GUILayout.Button("Stop"))
                    graphic.Stop();
            }

            int shownFrame = graphic.CurrentFrame < 0 ? 0 : graphic.CurrentFrame + 1;
            EditorGUILayout.LabelField(
                $"Frame: {shownFrame} / {graphic.FrameCount}    Time: {graphic.CurrentTime:0.00}s / {graphic.Duration:0.00}s");

            using (new EditorGUI.DisabledScope(!graphic.IsLoaded || graphic.Duration <= 0f))
            {
                float time = EditorGUILayout.Slider(graphic.CurrentTime, 0f, graphic.Duration);
                if (Mathf.Abs(time - graphic.CurrentTime) > 0.0001f)
                    graphic.Seek(time);
            }
        }

        private static Rect FitAspect(Rect area, float aspect)
        {
            if (aspect <= 0f)
                return area;

            float width = area.width;
            float height = width / aspect;
            if (height > area.height)
            {
                height = area.height;
                width = height * aspect;
            }

            return new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);
        }

        private static void DrawCheckerboard(Rect rect)
        {
            const float tileSize = 16f;
            Color dark = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f)
                : new Color(0.58f, 0.58f, 0.58f);
            Color light = EditorGUIUtility.isProSkin
                ? new Color(0.28f, 0.28f, 0.28f)
                : new Color(0.72f, 0.72f, 0.72f);

            int columns = Mathf.CeilToInt(rect.width / tileSize);
            int rows = Mathf.CeilToInt(rect.height / tileSize);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    var tile = new Rect(
                        rect.x + x * tileSize,
                        rect.y + y * tileSize,
                        Mathf.Min(tileSize, rect.xMax - (rect.x + x * tileSize)),
                        Mathf.Min(tileSize, rect.yMax - (rect.y + y * tileSize)));
                    EditorGUI.DrawRect(tile, ((x + y) & 1) == 0 ? dark : light);
                }
            }
        }

        private void UpdatePreview()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Min(0.1f, (float)(now - _lastEditorTime));
            _lastEditorTime = now;

            if (Application.isPlaying || target == null)
                return;

            var graphic = (QEmoGraphic)target;
            if (!graphic.IsPlaying)
                return;

            graphic.UpdateEditorPreview(delta);
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
