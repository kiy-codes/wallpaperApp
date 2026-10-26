using WallpaperApp.UI;

namespace WallpaperApp;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool startup = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);

        // One copy at a time; opening it again just brings the settings window forward.
        using var mutex = new Mutex(true, @"Local\WallpaperApp", out bool isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WallpaperApp.Show");
        if (!isFirstInstance)
        {
            if (!startup) showSignal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication(startup, showSignal));
    }
}
