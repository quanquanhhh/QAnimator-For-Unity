using System.IO.Compression;
using QAnimator.Encoder.Encoding;
using QAnimator.Encoder.Ffmpeg;

namespace QAnimator.Encoder.UI;

internal sealed class MainForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _outputFolder = new();
    private readonly NumericUpDown _fps = new();
    private readonly NumericUpDown _width = new();
    private readonly NumericUpDown _height = new();
    private readonly NumericUpDown _keyInterval = new();
    private readonly NumericUpDown _blockSize = new();
    private readonly ComboBox _compression = new();
    private readonly CheckBox _preserveAlpha = new();
    private readonly ProgressBar _overallProgress = new();
    private readonly Label _overallLabel = new();
    private readonly TextBox _log = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly List<EncodeJob> _jobs = new();
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "QAnimator Encoder v0.1";
        Width = 1120;
        Height = 900;
        MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += (_, _) => _cts?.Cancel();

        BuildUi();
        ApplyDarkTheme(this);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildTaskGroup(), 0, 0);
        root.Controls.Add(BuildProgressPanel(), 0, 1);
        root.Controls.Add(BuildActionPanel(), 0, 2);
        root.Controls.Add(BuildSettingsGroup(), 0, 3);
        root.Controls.Add(BuildLogGroup(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
    }

    private Control BuildTaskGroup()
    {
        var group = Group("任务列表");
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add("Id", "ID");
        _grid.Columns.Add("Input", "输入文件");
        _grid.Columns.Add("Resolution", "分辨率");
        _grid.Columns.Add("Fps", "FPS");
        _grid.Columns.Add("Alpha", "Alpha");
        _grid.Columns.Add("Output", "输出文件");
        _grid.Columns.Add("Status", "状态");
        _grid.Columns.Add("Progress", "进度");
        _grid.Columns[0].FillWeight = 30;
        _grid.Columns[1].FillWeight = 145;
        _grid.Columns[2].FillWeight = 70;
        _grid.Columns[3].FillWeight = 42;
        _grid.Columns[4].FillWeight = 42;
        _grid.Columns[5].FillWeight = 140;
        _grid.Columns[6].FillWeight = 60;
        _grid.Columns[7].FillWeight = 55;
        group.Controls.Add(_grid);
        return group;
    }

    private Control BuildProgressPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _overallProgress.Dock = DockStyle.Fill;
        _overallProgress.Height = 24;
        _overallLabel.Text = "0/0 任务完成 (0%)";
        _overallLabel.AutoSize = true;
        _overallLabel.Padding = new Padding(10, 4, 0, 0);
        panel.Controls.Add(_overallProgress, 0, 0);
        panel.Controls.Add(_overallLabel, 1, 0);
        return panel;
    }

    private Control BuildActionPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 8) };
        panel.Controls.Add(Button("添加文件", AddFiles));
        panel.Controls.Add(Button("移除选中任务", RemoveSelected));
        panel.Controls.Add(Button("清除完成", ClearFinished));
        panel.Controls.Add(Button("清除全部", (_, _) =>
        {
            if (_cts != null) return;
            _jobs.Clear();
            RefreshGrid();
        }));
        panel.Controls.Add(Button("打开输出目录", (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(_outputFolder.Text);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _outputFolder.Text,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppendLog($"无法打开输出目录: {ex.Message}");
            }
        }));

        _startButton.Text = "开始所有任务";
        _startButton.AutoSize = true;
        _startButton.Padding = new Padding(8, 2, 8, 2);
        _startButton.BackColor = Color.FromArgb(58, 174, 82);
        _startButton.ForeColor = Color.White;
        _startButton.FlatStyle = FlatStyle.Flat;
        _startButton.Click += StartAll;
        panel.Controls.Add(_startButton);

        _stopButton.Text = "停止所有任务";
        _stopButton.AutoSize = true;
        _stopButton.Padding = new Padding(8, 2, 8, 2);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => _cts?.Cancel();
        panel.Controls.Add(_stopButton);
        return panel;
    }

    private Control BuildSettingsGroup()
    {
        var group = Group("转码设置（Unity）");
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 5,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _outputFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        _outputFolder.Dock = DockStyle.Fill;
        AddPair(grid, 0, "输出文件夹", _outputFolder, "", BrowseOutputButton());

        ConfigureNumber(_fps, 0, 240, 0); _fps.Width = 140;
        ConfigureNumber(_width, 0, 8192, 0); _width.Increment = 16;
        ConfigureNumber(_height, 0, 8192, 0); _height.Increment = 16;
        ConfigureNumber(_keyInterval, 1, 300, 15);
        ConfigureNumber(_blockSize, 4, 128, 16);

        _compression.DropDownStyle = ComboBoxStyle.DropDownList;
        _compression.Items.AddRange(new object[] { "Fastest", "Optimal", "SmallestSize" });
        _compression.SelectedIndex = 1;
        _preserveAlpha.Text = "保留透明通道 (RGBA)";
        _preserveAlpha.Checked = true;
        _preserveAlpha.AutoSize = true;

        AddPair(grid, 1, "输出 FPS (0=源)", _fps, "宽度 (0=源)", _width);
        AddPair(grid, 2, "高度 (0=源)", _height, "关键帧间隔", _keyInterval);
        AddPair(grid, 3, "Delta Block", _blockSize, "压缩级别", _compression);
        grid.Controls.Add(new Label { Text = "Alpha", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        grid.Controls.Add(_preserveAlpha, 1, 4);
        grid.Controls.Add(new Label { Text = "输出格式", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 4);
        grid.Controls.Add(new Label { Text = ".bytes (QANM v1)", AutoSize = true, Anchor = AnchorStyles.Left }, 3, 4);

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildLogGroup()
    {
        var group = Group("日志");
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        group.Controls.Add(_log);
        return group;
    }

    private Control BuildFooter() => new Label
    {
        AutoSize = true,
        Text = "就绪 · MP4/WebM → QAnimator .bytes → Unity Texture2D（Runtime 不使用 VideoPlayer / FFmpeg）",
        Padding = new Padding(0, 5, 0, 0),
    };

    private async void AddFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.webm;*.mp4|WebM|*.webm|MP4|*.mp4",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await AddPathsAsync(dialog.FileNames);
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths.Where(IsSupportedInput))
        {
            if (_jobs.Any(j => string.Equals(j.InputPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var job = new EncodeJob { InputPath = path, Status = "分析中" };
            _jobs.Add(job);
            RefreshGrid();
            try
            {
                VideoInfo info = await FfmpegTools.ProbeAsync(path, CancellationToken.None);
                job.SourceInfo = info;
                job.Status = "等待";
                AppendLog($"已添加: {Path.GetFileName(path)} · {info.Width}x{info.Height} · {info.Fps:0.##} FPS · {info.CodecName} · alpha={info.HasAlpha}");
            }
            catch (Exception ex)
            {
                job.Status = "分析失败";
                job.Error = ex.Message;
                AppendLog($"分析失败 {path}: {ex.Message}");
            }
            RefreshGrid();
        }
    }

    private async void StartAll(object? sender, EventArgs e)
    {
        if (_cts != null || _jobs.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            MessageBox.Show(this, "请选择输出文件夹。", "QAnimator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Directory.CreateDirectory(_outputFolder.Text);
        _cts = new CancellationTokenSource();
        _startButton.Enabled = false;
        _stopButton.Enabled = true;

        try
        {
            foreach (EncodeJob job in _jobs.Where(j => j.Status is "等待" or "失败" or "已停止"))
            {
                if (_cts.IsCancellationRequested)
                    break;
                await RunJobAsync(job, _cts.Token);
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            UpdateOverallProgress();
        }
    }

    private async Task RunJobAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        if (job.SourceInfo == null)
            return;

        VideoInfo source = job.SourceInfo;
        int width = (int)_width.Value > 0 ? (int)_width.Value : source.Width;
        int height = (int)_height.Value > 0 ? (int)_height.Value : source.Height;
        int fps = (int)_fps.Value > 0 ? (int)_fps.Value : Math.Max(1, (int)Math.Round(source.Fps));
        int keyInterval = (int)_keyInterval.Value;
        int blockSize = (int)_blockSize.Value;
        bool hasAlpha = _preserveAlpha.Checked && source.HasAlpha;
        string output = Path.Combine(_outputFolder.Text, Path.GetFileNameWithoutExtension(job.InputPath) + ".bytes");
        job.OutputPath = output;
        job.Status = "处理中";
        job.Progress = 0;
        job.Error = null;
        RefreshGrid();
        AppendLog($"开始: {job.InputPath} -> {output}");

        using var ffmpeg = FfmpegTools.StartRgbaDecode(job.InputPath, width, height, fps, source.CodecName, hasAlpha);
        Task ffmpegCompletion = FfmpegTools.EnsureSuccessAsync(ffmpeg, cancellationToken);
        try
        {
            double estimatedFrames = source.Duration > 0 ? Math.Max(1, source.Duration * fps) : 1;
            var progress = new Progress<double>(frameNumber =>
            {
                job.Progress = Math.Clamp(frameNumber / estimatedFrames, 0, 0.99);
                RefreshGrid();
                UpdateOverallProgress();
            });

            var settings = new EncodeSettings(
                width,
                height,
                fps,
                keyInterval,
                blockSize,
                SelectedCompressionLevel(),
                hasAlpha);

            var encoder = new QAnimatorEncoder();
            await encoder.EncodeAsync(ffmpeg.StandardOutput.BaseStream, output, settings, progress, cancellationToken);
            await ffmpegCompletion;

            job.Status = "完成";
            job.Progress = 1;
            AppendLog($"完成: {Path.GetFileName(output)} ({new FileInfo(output).Length / 1024.0 / 1024.0:0.00} MB)");
        }
        catch (OperationCanceledException)
        {
            FfmpegTools.TryKill(ffmpeg);
            try { await ffmpegCompletion; } catch { }
            job.Status = "已停止";
            TryDelete(job.OutputPath);
            AppendLog($"已停止: {Path.GetFileName(job.InputPath)}");
        }
        catch (Exception ex)
        {
            FfmpegTools.TryKill(ffmpeg);
            try { await ffmpegCompletion; } catch { }
            job.Status = "失败";
            job.Error = ex.Message;
            TryDelete(job.OutputPath);
            AppendLog($"失败: {Path.GetFileName(job.InputPath)} · {ex.Message}");
        }
        finally
        {
            RefreshGrid();
            UpdateOverallProgress();
        }
    }

    private void RemoveSelected(object? sender, EventArgs e)
    {
        if (_cts != null) return;
        var indexes = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).OrderByDescending(i => i).ToArray();
        foreach (int index in indexes)
            if (index >= 0 && index < _jobs.Count) _jobs.RemoveAt(index);
        RefreshGrid();
    }

    private void ClearFinished(object? sender, EventArgs e)
    {
        if (_cts != null) return;
        _jobs.RemoveAll(j => j.Status == "完成");
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        if (InvokeRequired) { BeginInvoke(RefreshGrid); return; }
        _grid.Rows.Clear();
        for (int i = 0; i < _jobs.Count; i++)
        {
            EncodeJob j = _jobs[i];
            string resolution = j.SourceInfo == null ? "-" : $"{j.SourceInfo.Width}x{j.SourceInfo.Height}";
            string fps = j.SourceInfo == null ? "-" : j.SourceInfo.Fps.ToString("0.##");
            string alpha = j.SourceInfo == null ? "-" : (j.SourceInfo.HasAlpha ? "Yes" : "No");
            _grid.Rows.Add(i + 1, Path.GetFileName(j.InputPath), resolution, fps, alpha,
                string.IsNullOrEmpty(j.OutputPath) ? "-" : Path.GetFileName(j.OutputPath),
                j.Status, $"{j.Progress * 100:0}%");
        }
        UpdateOverallProgress();
    }

    private void UpdateOverallProgress()
    {
        if (InvokeRequired) { BeginInvoke(UpdateOverallProgress); return; }
        int completed = _jobs.Count(j => j.Status == "完成");
        int percent = _jobs.Count == 0 ? 0 : (int)Math.Round(_jobs.Average(j => j.Progress) * 100);
        _overallProgress.Value = Math.Clamp(percent, 0, 100);
        _overallLabel.Text = $"{completed}/{_jobs.Count} 任务完成 ({percent}%)";
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private Button BrowseOutputButton() => Button("浏览...", (_, _) =>
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _outputFolder.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputFolder.Text = dialog.SelectedPath;
    });

    private static GroupBox Group(string text) => new() { Text = text, Dock = DockStyle.Fill, Padding = new Padding(8) };

    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        button.Click += handler;
        return button;
    }

    private static void ConfigureNumber(NumericUpDown control, decimal min, decimal max, decimal value)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Value = value;
        control.Anchor = AnchorStyles.Left;
    }

    private static void AddPair(TableLayoutPanel grid, int row, string label1, Control control1, string label2, Control control2)
    {
        grid.Controls.Add(new Label { Text = label1, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(control1, 1, row);
        grid.Controls.Add(new Label { Text = label2, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
        grid.Controls.Add(control2, 3, row);
    }

    private CompressionLevel SelectedCompressionLevel() => _compression.SelectedItem?.ToString() switch
    {
        "Fastest" => CompressionLevel.Fastest,
        "SmallestSize" => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,
    };

    private static bool IsSupportedInput(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            await AddPathsAsync(files);
    }

    private static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void ApplyDarkTheme(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TextBoxBase)
            {
                c.BackColor = Color.FromArgb(52, 52, 52);
                c.ForeColor = Color.WhiteSmoke;
            }
            else if (c is DataGridView dgv)
            {
                dgv.BackgroundColor = Color.FromArgb(48, 48, 48);
                dgv.GridColor = Color.FromArgb(75, 75, 75);
                dgv.DefaultCellStyle.BackColor = Color.FromArgb(52, 52, 52);
                dgv.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 105, 150);
                dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(65, 65, 65);
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
                dgv.EnableHeadersVisualStyles = false;
            }
            else if (c is Button b && b.BackColor == SystemColors.Control)
            {
                b.BackColor = Color.FromArgb(72, 72, 72);
                b.ForeColor = Color.WhiteSmoke;
                b.FlatStyle = FlatStyle.Flat;
            }
            else
            {
                c.ForeColor = Color.WhiteSmoke;
                if (c is Panel or GroupBox or TableLayoutPanel or FlowLayoutPanel)
                    c.BackColor = Color.FromArgb(40, 40, 40);
            }
            ApplyDarkTheme(c);
        }
    }

    private sealed class EncodeJob
    {
        public required string InputPath { get; init; }
        public string? OutputPath { get; set; }
        public VideoInfo? SourceInfo { get; set; }
        public string Status { get; set; } = "等待";
        public double Progress { get; set; }
        public string? Error { get; set; }
    }
}
