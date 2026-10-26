using Microsoft.Win32;
using WallpaperApp.Native;

namespace WallpaperApp.Core;

/// <summary>One way of putting something on the desktop.</summary>
public interface IWallpaperMode
{
    void Apply(DesktopWallpaper wallpaper, AppConfig config, PortableStorage storage);
}

public sealed class ImageMode : IWallpaperMode
{
    public void Apply(DesktopWallpaper wallpaper, AppConfig config, PortableStorage storage)
    {
        if (config.Layout == MonitorLayout.PerMonitor)
        {
            // Check every file before changing anything, so a missing picture doesn't leave a half-applied desktop.
            var assignments = wallpaper.GetMonitors()
                .Select(m => (m.Id, Path: RequireImage(storage, config.ImageFor(m.Id, m.Index))))
                .ToList();
            wallpaper.Position = config.Fit.ToPosition();
            foreach (var (id, path) in assignments) wallpaper.SetWallpaper(id, path);
            return;
        }

        var image = RequireImage(storage, config.Image);
        wallpaper.Position = config.Layout == MonitorLayout.Span ? WallpaperPosition.Span : config.Fit.ToPosition();
        wallpaper.SetWallpaper(null, image);
    }

    static string RequireImage(PortableStorage storage, string? relative)
    {
        if (string.IsNullOrEmpty(relative)) throw new InvalidOperationException("Choose a picture first.");
        var path = storage.Resolve(relative);
        if (!File.Exists(path)) throw new FileNotFoundException($"This picture is missing: {path}", path);
        return path;
    }
}

public sealed class SlideshowMode : IWallpaperMode
{
    public void Apply(DesktopWallpaper wallpaper, AppConfig config, PortableStorage storage)
    {
        if (config.SlideshowFolder is null) throw new InvalidOperationException("Choose a slideshow folder first.");
        var folder = storage.Resolve(config.SlideshowFolder);
        if (PortableStorage.ListImages(folder).Count == 0)
            throw new InvalidOperationException($"The slideshow folder is missing or has no pictures: {folder}");

        wallpaper.SetSlideshowFolder(folder);
        wallpaper.SetSlideshowOptions(config.Shuffle, TimeSpan.FromMinutes(Math.Clamp(config.SlideshowMinutes, 1, 1440)));
        wallpaper.Position = config.Layout == MonitorLayout.Span ? WallpaperPosition.Span : config.Fit.ToPosition();
    }
}

/// <summary>Plays a video behind the desktop icons on every monitor, or once across all of them.</summary>
public sealed class VideoMode : IWallpaperMode
{
    public void Apply(DesktopWallpaper wallpaper, AppConfig config, PortableStorage storage)
    {
        if (string.IsNullOrEmpty(config.Video)) throw new InvalidOperationException("Choose a video first.");
        var path = storage.Resolve(config.Video);
        if (!File.Exists(path)) throw new FileNotFoundException($"This video is missing: {path}", path);

        var areas = wallpaper.GetMonitors().Select(m => m.Bounds).ToList();
        if (areas.Count == 0) throw new InvalidOperationException("No monitors found.");
        bool span = config.Layout == MonitorLayout.Span;
        if (span) areas = [areas.Aggregate(Rectangle.Union)];
        VideoWallpaper.Play(path, areas, span ? FitStyle.Fill : config.Fit);
    }
}

public static class WallpaperService
{
    public static void Apply(AppConfig config, PortableStorage storage)
    {
        using var wallpaper = DesktopWallpaper.Create();
        if (config.Mode != WallpaperMode.Video) VideoWallpaper.Stop();
        ModeFor(config.Mode).Apply(wallpaper, config, storage);
    }

    public static bool IsSlideshowRunning()
    {
        using var wallpaper = DesktopWallpaper.Create();
        return wallpaper.Status.HasFlag(SlideshowState.Slideshow);
    }

    public static void NextImage()
    {
        using var wallpaper = DesktopWallpaper.Create();
        wallpaper.AdvanceSlideshow();
    }

    /// <summary>
    /// Why Windows won't show our wallpaper even though it accepts the change, or null if nothing is in the way.
    /// Work and school PCs often force a wallpaper; reading these settings needs no admin rights.
    /// </summary>
    public static string? GetBlockReason()
    {
        const string systemPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        const string desktopPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            if (ReadValue(hive, systemPolicy, "Wallpaper") is string { Length: > 0 })
                return "Your organization sets the wallpaper through Group Policy, so Windows shows theirs instead.";
            if (ReadValue(hive, desktopPolicy, "NoChangingWallPaper") is 1)
                return "Your organization has turned off wallpaper changes through Group Policy.";
        }
        const string intune = @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
        if (ReadValue(Registry.LocalMachine, intune, "DesktopImageStatus") is 1
            || ReadValue(Registry.LocalMachine, intune, "DesktopImagePath") is string { Length: > 0 }
            || ReadValue(Registry.LocalMachine, intune, "DesktopImageUrl") is string { Length: > 0 })
            return "Your organization sets the wallpaper through device management (Intune), so Windows shows theirs instead.";

        try
        {
            using var wallpaper = DesktopWallpaper.Create();
            if (wallpaper.Status.HasFlag(SlideshowState.DisabledByRemoteSession))
                return "This is a Remote Desktop or virtual desktop session, where Windows turns off wallpapers.";
        }
        catch (System.Runtime.InteropServices.COMException) { }
        return null;
    }

    /// <summary>Reads the wallpaper back to check that Windows kept what was just applied.</summary>
    public static bool IsApplied(AppConfig config, PortableStorage storage)
    {
        if (config.Mode == WallpaperMode.Video) return VideoWallpaper.IsPlaying;
        using var wallpaper = DesktopWallpaper.Create();
        if (config.Mode == WallpaperMode.Slideshow) return wallpaper.Status.HasFlag(SlideshowState.Slideshow);

        foreach (var monitor in wallpaper.GetMonitors())
        {
            var expected = config.Layout == MonitorLayout.PerMonitor ? config.ImageFor(monitor.Id, monitor.Index) : config.Image;
            if (expected is null || !string.Equals(wallpaper.GetWallpaper(monitor.Id), storage.Resolve(expected), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>Read-only report of the wallpaper settings and policies, for troubleshooting without a command prompt.</summary>
    public static string GetDiagnostics()
    {
        var lines = new List<string> { $"Windows: {Environment.OSVersion.VersionString}" };
        (RegistryKey Hive, string Path, string Name)[] values =
        [
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "Wallpaper"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "WallpaperStyle"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "Wallpaper"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop", "NoChangingWallPaper"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop", "NoChangingWallPaper"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoThemesTab"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP", "DesktopImageStatus"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP", "DesktopImagePath"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP", "DesktopImageUrl"),
            (Registry.CurrentUser, @"Control Panel\Desktop", "Wallpaper"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers", "BackgroundType"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes", "CurrentTheme"),
        ];
        foreach (var (hive, path, name) in values)
            lines.Add($@"{(hive == Registry.CurrentUser ? "HKCU" : "HKLM")}\{path} [{name}] = {Describe(ReadValue(hive, path, name))}");

        try
        {
            using var wallpaper = DesktopWallpaper.Create();
            lines.Add($"Status: {wallpaper.Status}, position: {wallpaper.Position}");
            foreach (var monitor in wallpaper.GetMonitors())
                lines.Add($"Monitor {monitor.Index + 1} {monitor.Bounds.Width}x{monitor.Bounds.Height}: {wallpaper.GetWallpaper(monitor.Id)}");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            lines.Add($"Wallpaper API error: {ex.Message}");
        }
        lines.Add($"Video playing: {VideoWallpaper.IsPlaying}");
        lines.Add($"Block detected: {GetBlockReason() ?? "none"}");
        return string.Join(Environment.NewLine, lines);
    }

    static string Describe(object? value) => value switch
    {
        null => "(not set)",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString() ?? "",
    };

    static object? ReadValue(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Spotlight replaces any wallpaper we set. BackgroundType 3 is what Windows 11 writes when it's on.</summary>
    public static bool IsSpotlightActive()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
        return key?.GetValue("BackgroundType") is int type && type == 3;
    }

    public static WallpaperPosition ToPosition(this FitStyle fit) => fit switch
    {
        FitStyle.Fit => WallpaperPosition.Fit,
        FitStyle.Stretch => WallpaperPosition.Stretch,
        FitStyle.Center => WallpaperPosition.Center,
        FitStyle.Tile => WallpaperPosition.Tile,
        _ => WallpaperPosition.Fill,
    };

    static IWallpaperMode ModeFor(WallpaperMode mode) => mode switch
    {
        WallpaperMode.Slideshow => new SlideshowMode(),
        WallpaperMode.Video => new VideoMode(),
        _ => new ImageMode(),
    };
}
