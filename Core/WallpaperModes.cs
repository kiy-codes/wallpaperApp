using Microsoft.Win32;
using WallpaperApp.Native;

namespace WallpaperApp.Core;

/// <summary>One way of putting something on the desktop. A video mode can be added here later.</summary>
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

public static class WallpaperService
{
    public static void Apply(AppConfig config, PortableStorage storage)
    {
        using var wallpaper = DesktopWallpaper.Create();
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
        _ => new ImageMode(),
    };
}
