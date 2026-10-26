using Microsoft.Win32;
using WallpaperApp.Core;
using WallpaperApp.Native;

namespace WallpaperApp.UI;

/// <summary>Owns the tray icon, the hotkey and the settings window for the life of the process.</summary>
internal sealed class TrayApplication : ApplicationContext
{
    const int StartupAttempts = 10;

    readonly PortableStorage _storage = new();
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _nextItem;
    readonly HotkeyWindow _hotkey = new();
    readonly Control _ui = new();
    readonly System.Windows.Forms.Timer _displayChangedTimer = new() { Interval = 2000 };
    readonly RegisteredWaitHandle _showWait;
    AppConfig _config;
    MainForm? _form;
    string? _hotkeyError;

    public TrayApplication(bool startup, WaitHandle showSignal)
    {
        _ = _ui.Handle; // gives background callbacks a window to marshal onto the UI thread
        _config = AppConfig.Load(_storage.ConfigPath);

        _nextItem = new ToolStripMenuItem("Next image", null, (_, _) => NextImage());
        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_nextItem, settingsItem, new ToolStripSeparator(), new ToolStripMenuItem("Exit", null, (_, _) => ExitThread())]);

        _tray = new NotifyIcon { Icon = AppIcon.Value, Text = "Wallpaper App", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkey.Pressed += NextImage;
        VideoWallpaper.Failed += Notify;
        VideoWallpaper.DesktopReset += ReapplyVideo;
        RegisterHotkey();
        UpdateMenu();

        try { StartupManager.RepairPath(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }

        _displayChangedTimer.Tick += (_, _) => { _displayChangedTimer.Stop(); OnDisplaysChanged(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            showSignal, (_, _) => _ui.BeginInvoke((Action)ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        if (startup) ApplyAtStartup();
        else ShowSettings();
    }

    /// <summary>Applies and saves the settings. Returns why the wallpaper isn't showing, or null if it worked.</summary>
    public string? Apply(AppConfig config, bool startWithWindows)
    {
        WallpaperService.Apply(config, _storage);
        config.Save(_storage.ConfigPath);
        _config = config;
        StartupManager.SetEnabled(startWithWindows);
        _storage.Cleanup(config);
        RegisterHotkey();
        UpdateMenu();

        // The video is our own window, so wallpaper policies and Spotlight don't hide it.
        if (config.Mode == WallpaperMode.Video) return null;
        if (WallpaperService.GetBlockReason() is { } reason) return reason;
        if (WallpaperService.IsSpotlightActive())
            return "Windows Spotlight is on and replaces this wallpaper. Turn it off in Settings > Personalization > Background.";
        if (!WallpaperService.IsApplied(config, _storage))
            return "Windows accepted the change but didn't keep it. Something else on this PC controls the wallpaper, usually a company policy or another wallpaper app (such as BgInfo or Lively).";
        CheckAgainLater(config);
        return null;
    }

    /// <summary>Management tools often put their wallpaper back a few seconds later, so look again.</summary>
    void CheckAgainLater(AppConfig config)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 10_000 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            if (!ReferenceEquals(config, _config)) return; // settings were applied again since
            try
            {
                if (!WallpaperService.IsApplied(config, _storage))
                    Notify("Your wallpaper was changed back a few seconds after applying. Something on this PC, usually a company policy or management tool, keeps resetting it.");
            }
            catch (System.Runtime.InteropServices.COMException) { }
        };
        timer.Start();
    }

    public IReadOnlyList<string> GetWarnings()
    {
        var warnings = new List<string>();
        bool video = _config.Mode == WallpaperMode.Video;
        if (video)
            warnings.Add("The video only plays while Wallpaper App is running in the tray.");
        else if (WallpaperService.GetBlockReason() is { } blockReason)
            warnings.Add(blockReason);
        if (_storage.UsingFallback)
            warnings.Add($"This folder can't be written to, so settings and pictures are kept in {_storage.DataDir}.");
        if (_storage.OnRemovableDrive)
            warnings.Add("Running from a removable drive. If it's unplugged or its drive letter changes, the wallpaper and \"Start with Windows\" can stop working.");
        if (!video && WallpaperService.IsSpotlightActive())
            warnings.Add("Windows Spotlight is on and will replace this wallpaper. Turn it off in Settings > Personalization > Background.");
        if (_hotkeyError is not null)
            warnings.Add(_hotkeyError);
        return warnings;
    }

    public void Notify(string message) => _tray.ShowBalloonTip(4000, "Wallpaper App", message, ToolTipIcon.Info);

    void ShowSettings()
    {
        if (_form is null || _form.IsDisposed) _form = new MainForm(this, _storage);
        if (!_form.Visible) _form.LoadConfig(_config);
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    void NextImage()
    {
        if (_config.Mode != WallpaperMode.Slideshow)
        {
            Notify("\"Next image\" works once a slideshow is applied.");
            return;
        }
        try { WallpaperService.NextImage(); }
        catch (Exception ex) { Notify($"Couldn't skip to the next picture: {ex.Message}"); }
    }

    /// <summary>
    /// Re-applies the saved wallpaper after sign-in. Explorer may not be ready yet, so this retries for ~30 seconds.
    /// A running slideshow is left alone so it doesn't restart from the first picture.
    /// </summary>
    void ApplyAtStartup()
    {
        if (!_config.HasWallpaper) return;

        int attempt = 0;
        var retry = new System.Windows.Forms.Timer { Interval = 3000 };
        void TryApply()
        {
            try
            {
                if (!(_config.Mode == WallpaperMode.Slideshow && WallpaperService.IsSlideshowRunning()))
                    WallpaperService.Apply(_config, _storage);
                retry.Dispose();
            }
            catch (Exception ex)
            {
                if (++attempt < StartupAttempts)
                {
                    retry.Start();
                    return;
                }
                retry.Dispose();
                Notify($"Couldn't apply the wallpaper: {ex.Message}");
            }
        }
        retry.Tick += (_, _) => { retry.Stop(); TryApply(); };
        TryApply();
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _ui.BeginInvoke(() => { _displayChangedTimer.Stop(); _displayChangedTimer.Start(); });

    /// <summary>Explorer restarted, which closes the video window.</summary>
    void ReapplyVideo()
    {
        if (_config is not { Mode: WallpaperMode.Video, HasWallpaper: true }) return;
        // Explorer needs a moment to rebuild the desktop before the video can go behind the icons again.
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            try { WallpaperService.Apply(_config, _storage); }
            catch (Exception ex) { Notify($"Couldn't restart the video after Explorer restarted: {ex.Message}"); }
        };
        timer.Start();
    }

    /// <summary>
    /// A monitor was plugged in, removed or rearranged: per-monitor pictures need to be re-matched,
    /// and video windows resized to the new monitors.
    /// </summary>
    void OnDisplaysChanged()
    {
        if (_config is { Mode: WallpaperMode.Image, Layout: MonitorLayout.PerMonitor, HasWallpaper: true }
            or { Mode: WallpaperMode.Video, HasWallpaper: true })
        {
            try { WallpaperService.Apply(_config, _storage); }
            catch (Exception ex) { Notify($"Couldn't update the wallpaper for the new monitor layout: {ex.Message}"); }
        }
        if (_form is { Visible: true }) _form.RefreshMonitors();
    }

    void RegisterHotkey()
    {
        _hotkeyError = _hotkey.Register(HotkeyWindow.Parse(_config.Hotkey))
            ? null
            : $"The hotkey {_config.Hotkey} is already used by another app. Pick a different one.";
    }

    void UpdateMenu() => _nextItem.Enabled = _config.Mode == WallpaperMode.Slideshow;

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        VideoWallpaper.Failed -= Notify;
        VideoWallpaper.DesktopReset -= ReapplyVideo;
        VideoWallpaper.Stop();
        _showWait.Unregister(null);
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _form?.Dispose();
        _displayChangedTimer.Dispose();
        _ui.Dispose();
        base.ExitThreadCore();
    }
}
