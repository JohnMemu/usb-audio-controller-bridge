namespace UsbAudioController;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--startup", StringComparer.OrdinalIgnoreCase)));
    }
}
