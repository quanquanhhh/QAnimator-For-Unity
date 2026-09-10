using QAnimator.Encoder.UI;

namespace QAnimator.Encoder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
