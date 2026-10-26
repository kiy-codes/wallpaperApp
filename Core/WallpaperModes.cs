using Microsoft.Win32;
using WallpaperApp.Native;

namespace WallpaperApp.Core;

/// <summary>One way of putting something on the desktop. Every mode draws through <see cref="DesktopOverlay"/>.</summary>
public interface IWallpaperMode
{
    void Apply(List<MonitorInfo> monitors, AppConfig config, PortableStorage storage);
}

public sealed class ImageMode : IWallpaperMode
{
    public void Apply(List<MonitorInfo> monitors, AppConfig config, PortableStorage storage)
    {
        if (config.Layout == MonitorLayout.PerMonitor)
        {
            // Check every file before changing anything, so a missing picture doesn't leave a half-applied desktop.
            var paths = monitors.Select(m => RequireImage(storage, config.ImageFor(m.Id, m.Index))).ToList();
            DesktopOverlay.ShowPictures(monitors.Select(m => m.Bounds).ToList(), paths, config.Fit);
            return;
        }

        var image = RequireImage(storage, config.Image);
        var areas = WallpaperService.AreasFor(monitors, config.Layout);
        DesktopOverlay.ShowPictures(areas, areas.Select(_ => image).ToList(), WallpaperService.FitFor(config));
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
    public void Apply(List<MonitorInfo> monitors, AppConfig config, PortableStorage storage)
    {
        if (config.SlideshowFolder is null) throw new InvalidOperationException("Choose a slideshow folder first.");
        var folder = storage.Resolve(config.SlideshowFolder);
        var images = PortableStorage.ListImages(folder);
        if (images.Count == 0)
            throw new InvalidOperationException($"The slideshow folder is missing or has no pictures: {folder}");

        DesktopOverlay.ShowSlideshow(WallpaperService.AreasFor(monitors, config.Layout), images, config.Shuffle,
            TimeSpan.FromMinutes(Math.Clamp(config.SlideshowMinutes, 1, 1440)), WallpaperService.FitFor(config));
    }
}

/// <summary>Plays a video on every monitor, or once across all of them.</summary>
public sealed class VideoMode : IWallpaperMode
{
    public void Apply(List<MonitorInfo> monitors, AppConfig config, PortableStorage storage)
    {
        if (string.IsNullOrEmpty(config.Video)) throw new InvalidOperationException("Choose a video first.");
        var path = storage.Resolve(config.Video);
        if (!File.Exists(path)) throw new FileNotFoundException($"This video is missing: {path}", path);

        DesktopOverlay.ShowVideo(WallpaperService.AreasFor(monitors, config.Layout), path, WallpaperService.FitFor(config));
    }
}

public static class WallpaperService
{
    public static void Apply(AppConfig config, PortableStorage storage)
    {
        List<MonitorInfo> monitors;
        using (var wallpaper = DesktopWallpaper.Create()) monitors = wallpaper.GetMonitors();
        if (monitors.Count == 0) throw new InvalidOperationException("No monitors found.");
        ModeFor(config.Mode).Apply(monitors, config, storage);
    }

    public static void NextImage() => DesktopOverlay.NextSlide();

    /// <summary>One area per monitor, or a single area covering all of them when spanning.</summary>
    internal static List<Rectangle> AreasFor(List<MonitorInfo> monitors, MonitorLayout layout) =>
        layout == MonitorLayout.Span
            ? [monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union)]
            : monitors.Select(m => m.Bounds).ToList();

    /// <summary>Spanning ignores the fit setting and fills the combined area, like Windows does.</summary>
    internal static FitStyle FitFor(AppConfig config) => config.Layout == MonitorLayout.Span ? FitStyle.Fill : config.Fit;

    /// <summary>
    /// Why Windows won't show its own wallpaper, or null if nothing is in the way. Our overlay draws over it either way,
    /// so this is only reported in the diagnostics. Reading these settings needs no admin rights.
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
        lines.Add($"Overlay showing: {DesktopOverlay.IsShowing}");
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

    static IWallpaperMode ModeFor(WallpaperMode mode) => mode switch
    {
        WallpaperMode.Slideshow => new SlideshowMode(),
        WallpaperMode.Video => new VideoMode(),
        _ => new ImageMode(),
    };
}
