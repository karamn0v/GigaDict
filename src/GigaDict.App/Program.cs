namespace GigaDict.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test-input", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = InputSelfTest.Run();
            return;
        }
        if (args.Contains("--self-test-icons", StringComparer.OrdinalIgnoreCase))
        {
            using var appIcon = GigaDictApplicationContext.LoadAppIcon();
            using var icons = new TrayIconSet(appIcon);
            Environment.ExitCode = Enum.GetValues<TrayState>().All(state => icons.Get(state).Handle != 0) ? 0 : 9;
            return;
        }
        using var instance = new Mutex(true, @"Local\GigaDict.1.0", out var created);
        if (!created) return;
        Application.Run(new GigaDictApplicationContext());
    }
}
