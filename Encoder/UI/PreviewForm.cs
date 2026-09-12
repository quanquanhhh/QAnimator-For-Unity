using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QAnimator.Unity;

namespace QAnimator.Encoder.UI;

// Uses the actual Unity decoder; no source video is needed for preview.
internal sealed class PreviewForm : Form
{
    private readonly QAnimatorDecoder _decoder;
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly TrackBar _seek = new() { Dock = DockStyle.Bottom, TickStyle = TickStyle.None };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.WhiteSmoke };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private readonly Bitmap _bitmap;
    private readonly byte[] _bgra;
    private double _offset;
    private bool _playing = true;

    public PreviewForm(string path)
    {
        _decoder = new QAnimatorDecoder(File.ReadAllBytes(path));
        _bitmap = new Bitmap(_decoder.Width, _decoder.Height, PixelFormat.Format32bppArgb);
        _bgra = new byte[_decoder.Width * _decoder.Height * 4];
        Text = $"QAnimator 预览 · {Path.GetFileName(path)}";
        Size = new Size(760, 720);
        BackColor = Color.FromArgb(24, 27, 34);
        StartPosition = FormStartPosition.CenterParent;
        _picture.BackgroundImage = Checkerboard();
        _picture.BackgroundImageLayout = ImageLayout.Tile;
        _picture.Image = _bitmap;
        _seek.Maximum = _decoder.FrameCount - 1;
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        var play = new Button { Text = "暂停 / 播放", AutoSize = true, ForeColor = Color.WhiteSmoke, BackColor = Color.FromArgb(60, 65, 78), FlatStyle = FlatStyle.Flat };
        play.Click += (_, _) =>
        {
            _offset = CurrentTime();
            _playing = !_playing;
            _clock.Restart();
        };
        controls.Controls.Add(play);
        controls.Controls.Add(_status);
        Controls.Add(_picture);
        Controls.Add(_seek);
        Controls.Add(controls);
        _seek.Scroll += (_, _) => { _offset = _seek.Value / (double)_decoder.Fps; _clock.Restart(); Render(_seek.Value); };
        _timer.Tick += (_, _) =>
        {
            if (!_playing) return;
            try { Render(Math.Min(_decoder.FrameCount - 1, (int)(CurrentTime() * _decoder.Fps))); }
            catch (Exception ex) { _timer.Stop(); _status.Text = ex.Message; }
        };
        try { Render(0); }
        catch { Dispose(); throw; }
        _clock.Start();
        _timer.Start();
    }

    private double CurrentTime() => (_offset + (_playing ? _clock.Elapsed.TotalSeconds : 0)) % _decoder.Duration;

    private void Render(int frame)
    {
        if (_decoder.DecodedFrame == frame) return;
        _decoder.DecodeFrame(frame);
        byte[] rgba = _decoder.RgbaBuffer;
        int rowBytes = _decoder.Width * 4;
        for (int y = 0; y < _decoder.Height; y++)
            for (int x = 0; x < rowBytes; x += 4)
            {
                int src = (_decoder.Height - 1 - y) * rowBytes + x;
                int dst = y * rowBytes + x;
                _bgra[dst] = rgba[src + 2]; _bgra[dst + 1] = rgba[src + 1];
                _bgra[dst + 2] = rgba[src]; _bgra[dst + 3] = rgba[src + 3];
            }
        var data = _bitmap.LockBits(new Rectangle(0, 0, _bitmap.Width, _bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < _bitmap.Height; y++)
                Marshal.Copy(_bgra, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
        }
        finally { _bitmap.UnlockBits(data); }
        _seek.Value = frame;
        _status.Text = $"{_decoder.Width} × {_decoder.Height} · {_decoder.Fps:0.##} FPS · {frame + 1}/{_decoder.FrameCount} · Alpha: {_decoder.HasAlpha}";
        _picture.Invalidate();
    }

    private static Bitmap Checkerboard()
    {
        var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(51, 55, 64));
        using var brush = new SolidBrush(Color.FromArgb(66, 70, 80));
        graphics.FillRectangle(brush, 0, 0, 16, 16);
        graphics.FillRectangle(brush, 16, 16, 16, 16);
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _picture.Image = null;
            _bitmap.Dispose();
            _picture.BackgroundImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
