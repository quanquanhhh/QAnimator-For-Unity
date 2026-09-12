using System.Reflection;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var assembly = Assembly.Load("QAnimatorEncoder");
        var type = assembly.GetType("QAnimator.Encoder.UI.MainForm")!;
        using var form = (Form)Activator.CreateInstance(type)!;
        form.Opacity = 0;
        form.ShowInTaskbar = false;
        int result = 0;
        T Field<T>(string name) => (T)type.GetField(name, Private)!.GetValue(form)!;
        object? Invoke(string name, params object?[] values) => type.GetMethod(name, Private)!.Invoke(form, values);
        form.Shown += async (_, _) =>
        {
            try
            {
                string input = Path.GetFullPath(args[0]);
                string output = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(output);
                Field<TextBox>("_outputFolder").Text = output;
                Field<NumericUpDown>("_width").Value = 256;
                await (Task)Invoke("AddPathsAsync", (object)new string[] { input })!;
                Invoke("StartAll", null, EventArgs.Empty);
                // The queue must safely reject additions while processing.
                await (Task)Invoke("AddPathsAsync", (object)new string[] { input })!;
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (type.GetField("_cts", Private)!.GetValue(form) != null)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("GUI conversion did not complete");
                    await Task.Delay(50);
                }
                var grid = Field<DataGridView>("_grid");
                if ((string?)grid.Rows[0].Cells[6].Value != "完成")
                    throw new Exception(Field<TextBox>("_log").Text);
                using (var screenshot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                    screenshot.Save(Path.Combine(output, "encoder-ui.png"));
                }
                string bytes = Directory.GetFiles(output, "*.bytes")[0];
                var previewType = assembly.GetType("QAnimator.Encoder.UI.PreviewForm")!;
                using var preview = (Form)Activator.CreateInstance(previewType, bytes)!;
                preview.Opacity = 0;
                preview.ShowInTaskbar = false;
                preview.Show(form);
                await Task.Delay(300);
                previewType.GetMethod("Render", Private)!.Invoke(preview, new object[] { Math.Min(60, ((TrackBar)previewType.GetField("_seek", Private)!.GetValue(preview)!).Maximum) });
                using (var screenshot = new Bitmap(preview.Width, preview.Height))
                {
                    preview.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                    screenshot.Save(Path.Combine(output, "preview-ui.png"));
                }
                Console.WriteLine("PASS desktop: queue conversion, additions during processing, 256-width resize, real decoder preview and seek.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
        return result;
    }
}
