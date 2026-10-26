using System.Drawing;
using System.Runtime.InteropServices;

namespace WallpaperApp.Native;

public enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

[Flags]
public enum SlideshowOptions { None = 0, ShuffleImages = 1 }

public enum SlideshowDirection { Forward = 0, Backward = 1 }

[Flags]
public enum SlideshowState { None = 0, Enabled = 1, Slideshow = 2, DisabledByRemoteSession = 4 }

public sealed record MonitorInfo(int Index, string Id, Rectangle Bounds);

/// <summary>
/// Wraps the IDesktopWallpaper COM object (Windows 8+), the same API the Personalize page uses.
/// Everything it changes is per-user, so no administrator rights are needed.
/// </summary>
public sealed class DesktopWallpaper : IDisposable
{
    static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    const string IidShellItem = "43826D1E-E718-42EE-BC55-A1E261C37BFE";
    const string IidShellItemArray = "B63EA76D-1F85-456F-A19C-48159EFA858B";

    readonly IDesktopWallpaper _com;

    DesktopWallpaper(IDesktopWallpaper com) => _com = com;

    public static DesktopWallpaper Create()
    {
        var type = Type.GetTypeFromCLSID(ClsidDesktopWallpaper, throwOnError: true)!;
        return new DesktopWallpaper((IDesktopWallpaper)Activator.CreateInstance(type)!);
    }

    public WallpaperPosition Position
    {
        get => _com.GetPosition();
        set => _com.SetPosition(value);
    }

    public SlideshowState Status => _com.GetStatus();

    /// <summary>Sets the wallpaper on one monitor, or on all of them when <paramref name="monitorId"/> is null.</summary>
    public void SetWallpaper(string? monitorId, string path) => _com.SetWallpaper(monitorId, path);

    public string GetWallpaper(string? monitorId) => _com.GetWallpaper(monitorId);

    /// <summary>Active monitors only; disconnected ones report an empty rectangle and are skipped.</summary>
    public List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        uint count = _com.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            try
            {
                string id = _com.GetMonitorDevicePathAt(i);
                var rect = _com.GetMonitorRECT(id);
                if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                    monitors.Add(new MonitorInfo(monitors.Count, id, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)));
            }
            catch (COMException)
            {
                // Monitor went away between calls.
            }
        }
        return monitors;
    }

    /// <summary>Starts the built-in Windows slideshow from a folder. Windows keeps rotating it after this app exits.</summary>
    public void SetSlideshowFolder(string folder)
    {
        var iidItem = new Guid(IidShellItem);
        var iidArray = new Guid(IidShellItemArray);
        IntPtr item = IntPtr.Zero, array = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iidItem, out item);
            SHCreateShellItemArrayFromShellItem(item, ref iidArray, out array);
            _com.SetSlideshow(array);
        }
        finally
        {
            if (array != IntPtr.Zero) Marshal.Release(array);
            if (item != IntPtr.Zero) Marshal.Release(item);
        }
    }

    public void SetSlideshowOptions(bool shuffle, TimeSpan interval) =>
        _com.SetSlideshowOptions(shuffle ? SlideshowOptions.ShuffleImages : SlideshowOptions.None, (uint)interval.TotalMilliseconds);

    public void AdvanceSlideshow() => _com.AdvanceSlideshow(null, SlideshowDirection.Forward);

    public void Dispose() => Marshal.ReleaseComObject(_com);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IntPtr item);

    [DllImport("shell32.dll", PreserveSig = false)]
    static extern void SHCreateShellItemArrayFromShellItem(IntPtr item, ref Guid riid, out IntPtr array);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    // Method order must match the vtable in shobjidl_core.h.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        NativeRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(WallpaperPosition position);
        WallpaperPosition GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(SlideshowOptions options, uint slideshowTick);
        void GetSlideshowOptions(out SlideshowOptions options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, SlideshowDirection direction);
        SlideshowState GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
