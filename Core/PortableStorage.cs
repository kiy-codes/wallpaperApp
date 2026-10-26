using System.Security.Cryptography;

namespace WallpaperApp.Core;

/// <summary>
/// Keeps settings and copies of the chosen pictures in a "WallpaperApp Data" folder beside the .exe,
/// so the wallpaper still works if the originals are moved or deleted.
/// Falls back to %APPDATA%\WallpaperApp when the .exe's folder is read-only.
/// </summary>
public sealed class PortableStorage
{
    public static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".wdp"];

    public static string ImageFilter =>
        "Pictures|" + string.Join(";", ImageExtensions.Select(e => "*" + e)) + "|All files|*.*";

    public PortableStorage() : this(AppContext.BaseDirectory) { }

    public PortableStorage(string appDir)
    {
        var portableDir = Path.GetFullPath(Path.Combine(appDir, "WallpaperApp Data"));
        if (CanWriteTo(portableDir))
        {
            DataDir = portableDir;
        }
        else
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperApp");
            Directory.CreateDirectory(DataDir);
            UsingFallback = true;
        }
        OnRemovableDrive = IsRemovable(appDir);
    }

    public string DataDir { get; }
    public bool UsingFallback { get; }
    public bool OnRemovableDrive { get; }
    public string ConfigPath => Path.Combine(DataDir, "config.json");

    string WallpapersDir => Path.Combine(DataDir, "wallpapers");
    string ImagesDir => Path.Combine(WallpapersDir, "images");
    string SlideshowsDir => Path.Combine(WallpapersDir, "slideshows");

    public string Resolve(string path) => Path.GetFullPath(Path.Combine(DataDir, path));

    /// <summary>Copies a picture into the data folder (reusing an identical copy) and returns its relative path.</summary>
    public string ImportImage(string source)
    {
        var full = Path.GetFullPath(source);
        if (IsInside(full, WallpapersDir)) return Relative(full);

        Directory.CreateDirectory(ImagesDir);
        var dest = UniqueDestination(ImagesDir, Path.GetFileName(full), full);
        if (!File.Exists(dest)) File.Copy(full, dest);
        return Relative(dest);
    }

    /// <summary>Copies the pictures in a folder (not subfolders) into the data folder and returns its relative path.</summary>
    public string ImportFolder(string source)
    {
        var full = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        if (IsInside(full, WallpapersDir)) return Relative(full);

        var images = ListImages(full);
        if (images.Count == 0)
            throw new InvalidOperationException("That folder has no pictures Windows can use (JPG, PNG, BMP, GIF or TIFF).");

        var name = Path.GetFileName(full);
        var dest = Path.Combine(SlideshowsDir, string.IsNullOrEmpty(name) ? "slideshow" : name);
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        Directory.CreateDirectory(dest);
        foreach (var image in images) File.Copy(image, Path.Combine(dest, Path.GetFileName(image)));
        return Relative(dest);
    }

    public static List<string> ListImages(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    /// <summary>Deletes copied pictures the config no longer uses. Only touches the app's own wallpapers folder.</summary>
    public void Cleanup(AppConfig config)
    {
        var keep = config.ReferencedPaths().Select(Resolve).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(ImagesDir))
                foreach (var file in Directory.EnumerateFiles(ImagesDir).Where(f => !keep.Contains(f)).ToList())
                    File.Delete(file);
            if (Directory.Exists(SlideshowsDir))
                foreach (var dir in Directory.EnumerateDirectories(SlideshowsDir).Where(d => !keep.Contains(d)).ToList())
                    Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file in use is left for the next cleanup.
        }
    }

    string Relative(string full) => Path.GetRelativePath(DataDir, full);

    static bool IsInside(string path, string dir) =>
        path.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    static string UniqueDestination(string dir, string fileName, string source)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, n == 1 ? fileName : $"{name} ({n}){ext}");
            if (!File.Exists(candidate) || SameContent(candidate, source)) return candidate;
        }
    }

    static bool SameContent(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }

    static bool CanWriteTo(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsRemovable(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).DriveType == DriveType.Removable; }
        catch (ArgumentException) { return false; } // network (UNC) paths
    }
}
