using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using QAnimator.Unity;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public sealed class QAnimatorValidation : MonoBehaviour
{
    public QEmoGraphic Player;
    public TextAsset Opaque;
    public TextAsset Alpha;
    public Text Status;
    public string ReportDirectory = "QAnimatorTestResults";
    private readonly List<string> _passes = new List<string>();
    private string _failure;
    private int _completions;
    private int _originalFps;
    private int _originalVsync;
    private float _originalTimeScale;

    [Serializable] private sealed class Report
    {
        public string unityVersion;
        public string utc;
        public bool passed;
        public string failure;
        public string[] checks;
    }

    private IEnumerator Start()
    {
        Player.Load(Alpha);
        Player.Loop = true;
        Player.Play();
#if UNITY_EDITOR
        if (!UnityEditor.SessionState.GetBool("QAnimator.Validation.Run", false)) yield break;
#else
        yield break;
#endif
        _originalFps = Application.targetFrameRate;
        _originalVsync = QualitySettings.vSyncCount;
        _originalTimeScale = Time.timeScale;
        Directory.CreateDirectory(ReportDirectory);
        Player.OnComplete += Complete;
        Application.logMessageReceived += LogMessage;
        var tests = RunChecks();
        while (true)
        {
            object next;
            try
            {
                if (!tests.MoveNext()) break;
                next = tests.Current;
            }
            catch (Exception ex) { _failure = ex.ToString(); break; }
            yield return next;
            if (_failure != null) break;
        }
        Application.logMessageReceived -= LogMessage;
        Player.OnComplete -= Complete;
        Application.targetFrameRate = _originalFps;
        QualitySettings.vSyncCount = _originalVsync;
        Time.timeScale = _originalTimeScale;
        var report = new Report
        {
            unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"),
            passed = _failure == null, failure = _failure, checks = _passes.ToArray()
        };
        Status.text = _failure == null ? "ALL CHECKS PASSED  |  " + _passes.Count + " checks" : "TEST FAILED: " + _failure;
        File.WriteAllText(Path.Combine(ReportDirectory, "results.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[QAnimator Validation] " + Status.text);
        Player.Load(Alpha);
        Player.Speed = 1;
        Player.Loop = true;
        Player.Restart();
    }

    private void Complete() { _completions++; }
    private void LogMessage(string message, string trace, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && trace.Contains("QAnimator"))
            _failure = message + "\n" + trace;
    }
    private void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _passes.Add(message);
        Status.text = "PASS  " + message;
    }

    private IEnumerator RunChecks()
    {
        Player.Pause();
        Check(Player.Width == 512 && Player.Height == 512 && Player.FrameCount == 120 && Player.Fps == 30, "Header: 512x512 / 120 frames / 30 FPS");
        Check(Player.mainTexture == Player.Texture, "QEmoGraphic renders its playback texture directly");
        string[] expected = File.ReadAllLines(Path.Combine(ReportDirectory, "alpha-sha256.txt"));
        var texture = Player.Texture;
        using (var sha = SHA256.Create())
        {
            // The reference hashes come from independently decoding the WebM using FFmpeg offline.
            for (int frame = 0; frame < Player.FrameCount; frame++)
            {
                Player.Seek((frame + 0.1f) / 30f);
                string actual = BitConverter.ToString(sha.ComputeHash(Player.Texture.GetRawTextureData())).Replace("-", "").ToLowerInvariant();
                if (actual != expected[frame]) throw new Exception("GPU upload buffer mismatch at frame " + frame);
                if (Player.Texture != texture) throw new Exception("Playback texture was reallocated");
            }
        }
        Check(true, "All 120 uploaded RGBA frames match offline reference hashes; one Texture2D reused");
        Player.Seek(0);
        Check(Player.CurrentFrame == 0, "Backward seek reconstructs frame zero");
        var alphaPixels = Player.Texture.GetPixels32();
        bool transparent = false, visible = false, partial = false;
        foreach (var pixel in alphaPixels) { transparent |= pixel.a == 0; visible |= pixel.a == 255; partial |= pixel.a > 0 && pixel.a < 255; }
        Check(transparent && visible && partial, "Transparent, opaque and partial-alpha pixels preserved");
        yield return new WaitForEndOfFrame();
        Capture("alpha-closed.png");
        Player.Seek(2);
        yield return new WaitForEndOfFrame();
        Capture("alpha-open.png");
        Player.Play(Opaque.name);
        Player.Pause();
        bool allOpaque = true;
        foreach (var pixel in Player.Texture.GetPixels32()) allOpaque &= pixel.a == 255;
        Check(Player.AnimationName == Opaque.name && !Player.HasAlpha && allOpaque, "Play(name) switches assets and MP4 alpha values are opaque");
        Check(Player.mainTexture == Player.Texture, "QEmoGraphic keeps rendering after loading a new animation");
        Player.Seek(2);
        yield return new WaitForEndOfFrame();
        Capture("mp4-open.png");
        Player.Load(Alpha);
        Player.Play();
        yield return new WaitForSecondsRealtime(.25f);
        Check(Player.IsPlaying && Player.CurrentFrame > 0, "Play advances using actual Unity Update");
        Player.Pause();
        float paused = Player.CurrentTime;
        yield return new WaitForSecondsRealtime(.2f);
        Check(Player.CurrentTime == paused && !Player.IsPlaying, "Pause holds time and frame");
        Player.Resume();
        yield return new WaitForSecondsRealtime(.2f);
        Check(Player.CurrentTime > paused, "Resume continues playback");
        Player.Speed = 0;
        paused = Player.CurrentTime;
        yield return new WaitForSecondsRealtime(.15f);
        Check(Player.CurrentTime == paused, "Zero speed holds playback");
        Player.Speed = 2;
        Player.Restart();
        yield return new WaitForSecondsRealtime(.35f);
        Check(Player.CurrentTime > .6f && Player.CurrentTime < 1.4f, "2x speed advances approximately twice elapsed time");
        Player.Stop();
        Check(!Player.IsPlaying && Player.CurrentTime == 0 && Player.CurrentFrame == 0, "Stop resets time and first frame");
        Player.Speed = 1;
        Player.UseUnscaledTime = false;
        Time.timeScale = 0;
        Player.Play();
        yield return new WaitForSecondsRealtime(.2f);
        Check(Player.CurrentTime == 0, "Scaled playback respects Time.timeScale = 0");
        Player.UseUnscaledTime = true;
        yield return new WaitForSecondsRealtime(.2f);
        Check(Player.CurrentTime > .1f, "Unscaled playback continues when game time is paused");
        Time.timeScale = 1;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 10;
        Player.Restart();
        yield return new WaitForSecondsRealtime(.7f);
        Check(Player.CurrentFrame >= 18 && Player.CurrentFrame == Mathf.Min(119, Mathf.FloorToInt(Player.CurrentTime * 30)), "10 FPS game loop skips to correct 30 FPS animation frame");
        Application.targetFrameRate = 60;
        Player.Loop = false;
        Player.Seek(Player.Duration - .06f);
        Player.Play();
        yield return new WaitForSecondsRealtime(.25f);
        Check(!Player.IsPlaying && Player.CurrentFrame == 119 && _completions == 1, "Non-looping playback stops at final frame and completes once");
        yield return new WaitForSecondsRealtime(.1f);
        Check(_completions == 1, "Completion event is not repeated on later Updates");
        Player.Loop = true;
        Player.Seek(Player.Duration - .06f);
        Player.Play();
        yield return new WaitForSecondsRealtime(.25f);
        Check(Player.IsPlaying && Player.CurrentTime < 1 && _completions == 1, "Loop wraps time without non-loop completion events");
        Player.Restart();
        Check(Player.IsPlaying && Player.CurrentFrame == 0 && Player.CurrentTime == 0, "Restart immediately returns to first frame");
        Player.Pause();
        Player.enabled = false;
        Check(Player.Texture == null, "QEmoGraphic releases its texture while disabled");
        Player.enabled = true;
        Check(Player.Texture != null && Player.mainTexture == Player.Texture, "QEmoGraphic restores its first-frame preview when enabled");
        Player.Unload();
        Check(!Player.IsLoaded && Player.Texture == null, "Unload releases the QEmoGraphic playback texture");
        Player.Load(Alpha);
        Player.Seek(2);
        Check(Player.CurrentFrame == 60 && Player.Texture != null, "Reload after Unload reconstructs frame successfully");
        yield return null;
    }

    private void Capture(string name)
    {
        var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(ReportDirectory, name), screenshot.EncodeToPNG()); }
        finally { Destroy(screenshot); }
    }
}
