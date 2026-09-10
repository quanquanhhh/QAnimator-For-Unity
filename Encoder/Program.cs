using QAnimator.Encoder.Cli;
using QAnimator.Encoder.UI;

namespace QAnimator.Encoder;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            try
            {
                return await EncoderCli.RunAsync(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
