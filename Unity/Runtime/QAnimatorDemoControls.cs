using UnityEngine;
using UnityEngine.UI;

namespace QAnimator.Unity
{
    // Optional demo controls. Remove this component for production UI.
    public sealed class QAnimatorDemoControls : MonoBehaviour
    {
        public QEmoGraphic Player;
        private int _completions;

        private void Start()
        {
            if (Player == null) return;
            Player.OnComplete += Completed;
            var aspect = Player.GetComponent<AspectRatioFitter>();
            if (aspect != null && Player.Height > 0)
            {
                aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                aspect.aspectRatio = Player.Width / (float)Player.Height;
            }
        }

        private void Completed() { _completions++; }
        private void OnDestroy() { if (Player != null) Player.OnComplete -= Completed; }

        private void OnGUI()
        {
            if (Player == null || !Player.IsLoaded) return;
            GUILayout.BeginArea(new Rect(16, 16, 360, 210), GUI.skin.box);
            GUILayout.Label($"QAnimator | {Player.Width} x {Player.Height} | {Player.Fps:0.##} FPS");
            GUILayout.Label($"Frame {Player.CurrentFrame + 1}/{Player.FrameCount} | {Player.CurrentTime:0.00}/{Player.Duration:0.00}s");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play")) Player.Play();
            if (GUILayout.Button("Pause")) Player.Pause();
            if (GUILayout.Button("Stop")) Player.Stop();
            if (GUILayout.Button("Restart")) Player.Restart();
            GUILayout.EndHorizontal();
            Player.Loop = GUILayout.Toggle(Player.Loop, "Loop");
            GUILayout.Label($"Speed: {Player.Speed:0.00}x | Completions: {_completions}");
            Player.Speed = GUILayout.HorizontalSlider(Player.Speed, 0, 3);
            float time = GUILayout.HorizontalSlider(Player.CurrentTime, 0, Player.Duration);
            if (Mathf.Abs(time - Player.CurrentTime) > 0.001f) Player.Seek(time);
            GUILayout.EndArea();
        }
    }
}
