using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperApp.Core;

public enum WallpaperMode { Image, Slideshow, Video }

public enum MonitorLayout { SameOnAll, PerMonitor, Span }

public enum FitStyle { Fill, Fit, Stretch, Center, Tile }

public sealed class MonitorImage
{
    public string MonitorId { get; set; } = "";
    public int Index { get; set; }
    public string Image { get; set; } = "";
}

/// <summary>
/// Saved settings. Picture paths are relative to the app's data folder so the whole folder can be moved.
/// </summary>
public sealed class AppConfig
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public WallpaperMode Mode { get; set; } = WallpaperMode.Image;
    public MonitorLayout Layout { get; set; } = MonitorLayout.SameOnAll;
    public FitStyle Fit { get; set; } = FitStyle.Fill;
    public string? Image { get; set; }
    public List<MonitorImage> MonitorImages { get; set; } = [];
    public string? SlideshowFolder { get; set; }
    public int SlideshowMinutes { get; set; } = 30;
    public bool Shuffle { get; set; } = true;
    public string? Video { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+N";

    [JsonIgnore]
    public bool HasWallpaper => Mode switch
    {
        WallpaperMode.Slideshow => SlideshowFolder is not null,
        WallpaperMode.Video => Video is not null,
        _ => Image is not null || MonitorImages.Count > 0,
    };

    /// <summary>
    /// Picture for one monitor: matched by device path, then by position, then the shared image.
    /// The fallbacks keep something on screen after monitors are swapped or reordered.
    /// </summary>
    public string? ImageFor(string monitorId, int index) =>
        MonitorImages.FirstOrDefault(m => m.MonitorId == monitorId)?.Image
        ?? MonitorImages.FirstOrDefault(m => m.Index == index)?.Image
        ?? Image
        ?? MonitorImages.FirstOrDefault()?.Image;

    public void SetMonitorImage(string monitorId, int index, string image)
    {
        MonitorImages.RemoveAll(m => m.MonitorId == monitorId);
        MonitorImages.Add(new MonitorImage { MonitorId = monitorId, Index = index, Image = image });
    }

    public IEnumerable<string> ReferencedPaths()
    {
        if (Image is not null) yield return Image;
        foreach (var m in MonitorImages) yield return m.Image;
        if (SlideshowFolder is not null) yield return SlideshowFolder;
        if (Video is not null) yield return Video;
    }

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static AppConfig Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions) ?? new AppConfig()
                : new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
