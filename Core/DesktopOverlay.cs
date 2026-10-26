using System.Windows.Forms.Integration;
using WallpaperApp.Native;
using Wpf = System.Windows.Controls;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace WallpaperApp.Core;

/// <summary>
/// Draws the wallpaper in our own windows behind the desktop icons, over the Windows wallpaper.
/// The Windows wallpaper setting is never changed, so it comes back when the app exits.
/// Pictures use WPF's Image; videos use MediaElement, which plays MP4 (H.264) with the decoders built into Windows.
/// </summary>
public static class DesktopOverlay
{
    static readonly List<OverlayWindow> Windows = [];
    static readonly System.Windows.Forms.Timer SlideshowTimer = new();
    static ExplorerWatcher? _watcher;
    static string[] _slides = [];
    static int _slide;
    static bool _shuffle;
    static FitStyle _slideFit;

    static DesktopOverlay() => SlideshowTimer.Tick += (_, _) =>
    {
        try { NextSlide(); }
        catch (Exception ex) { Failed?.Invoke($"Couldn't show the next picture: {ex.Message}"); }
    };

    /// <summary>Something couldn't be shown after it was applied. Raised on the UI thread.</summary>
    public static event Action<string>? Failed;

    /// <summary>Explorer restarted and took the overlay with it, so the wallpaper needs to be applied again.</summary>
    public static event Action? DesktopReset;

    public static bool IsShowing => Windows.Count > 0 && Windows.All(w => w.IsHandleCreated && !w.IsDisposed);

    /// <summary>Shows <paramref name="paths"/>[i] in <paramref name="areas"/>[i], replacing whatever is showing.</summary>
    public static void ShowPictures(IReadOnlyList<Rectangle> areas, IReadOnlyList<string> paths, FitStyle fit) =>
        Open(areas, (window, i) => window.ShowPicture(paths[i], fit));

    public static void ShowVideo(IReadOnlyList<Rectangle> areas, string path, FitStyle fit) =>
        Open(areas, (window, _) => window.ShowVideo(path, fit));

    /// <summary>Rotates through <paramref name="paths"/>, showing the same picture in every area.</summary>
    public static void ShowSlideshow(IReadOnlyList<Rectangle> areas, IReadOnlyList<string> paths, bool shuffle, TimeSpan interval, FitStyle fit)
    {
        var slides = paths.ToArray();
        if (shuffle) Random.Shared.Shuffle(slides);
        Open(areas, (window, _) => window.ShowPicture(slides[0], fit));

        (_slides, _slide, _shuffle, _slideFit) = (slides, 0, shuffle, fit);
        SlideshowTimer.Interval = (int)interval.TotalMilliseconds;
        SlideshowTimer.Start();
    }

    /// <summary>Skips to the next slideshow picture and restarts the interval. Reshuffles after each pass.</summary>
    public static void NextSlide()
    {
        if (_slides.Length == 0) return;
        if (++_slide >= _slides.Length)
        {
            _slide = 0;
            if (_shuffle) Random.Shared.Shuffle(_slides);
        }
        SlideshowTimer.Stop();
        SlideshowTimer.Start();
        foreach (var window in Windows) window.ShowPicture(_slides[_slide], _slideFit);
    }

    public static void Stop()
    {
        SlideshowTimer.Stop();
        _slides = [];
        foreach (var window in Windows)
        {
            var parent = window.IsHandleCreated ? DesktopHost.ParentOf(window.Handle) : IntPtr.Zero;
            window.Dispose();
            DesktopHost.Repaint(parent);
        }
        Windows.Clear();
    }

    static void Open(IReadOnlyList<Rectangle> areas, Action<OverlayWindow, int> fill)
    {
        Stop();
        _watcher ??= new ExplorerWatcher();
        try
        {
            for (int i = 0; i < areas.Count; i++)
            {
                var window = new OverlayWindow(areas[i]);
                Windows.Add(window);
                fill(window, i);
                window.Attach();
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    static Media.Stretch ToStretch(FitStyle fit) => fit switch
    {
        FitStyle.Fit => Media.Stretch.Uniform,
        FitStyle.Stretch => Media.Stretch.Fill,
        FitStyle.Center => Media.Stretch.None,
        _ => Media.Stretch.UniformToFill,
    };

    sealed class OverlayWindow : Form
    {
        readonly Rectangle _area;
        readonly ElementHost _host = new() { Dock = DockStyle.Fill };
        Wpf.MediaElement? _media;

        public OverlayWindow(Rectangle area)
        {
            _area = area;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
            BackColor = Color.Black;
            Controls.Add(_host);
        }

        protected override bool ShowWithoutActivation => true;

        public void Attach()
        {
            DesktopHost.Attach(Handle, _area);
            Show();
            _media?.Play();
        }

        public void ShowPicture(string path, FitStyle fit)
        {
            var bitmap = new Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = Imaging.BitmapCacheOption.OnLoad; // read it now so the file isn't kept locked
            bitmap.EndInit();
            bitmap.Freeze();

            // Center and Tile show the picture at its pixel size, like Windows does.
            double scale = DeviceDpi / 96.0;
            var size = new System.Windows.Size(bitmap.PixelWidth / scale, bitmap.PixelHeight / scale);
            SetContent(fit switch
            {
                FitStyle.Tile => new Wpf.Border
                {
                    Background = new Media.ImageBrush(bitmap)
                    {
                        TileMode = Media.TileMode.Tile,
                        Stretch = Media.Stretch.Fill,
                        ViewportUnits = Media.BrushMappingMode.Absolute,
                        Viewport = new System.Windows.Rect(size),
                    },
                },
                FitStyle.Center => new Wpf.Image { Source = bitmap, Stretch = Media.Stretch.Fill, Width = size.Width, Height = size.Height },
                _ => new Wpf.Image { Source = bitmap, Stretch = ToStretch(fit) },
            });
        }

        public void ShowVideo(string path, FitStyle fit)
        {
            var media = new Wpf.MediaElement
            {
                Source = new Uri(path),
                LoadedBehavior = Wpf.MediaState.Manual,
                UnloadedBehavior = Wpf.MediaState.Manual,
                IsMuted = true,
                Stretch = ToStretch(fit),
            };
            media.MediaEnded += (_, _) => { media.Position = TimeSpan.Zero; media.Play(); };
            media.MediaFailed += (_, e) => Failed?.Invoke($"Couldn't play the video: {e.ErrorException?.Message}");
            SetContent(media);
            _media = media;
        }

        void SetContent(System.Windows.UIElement content)
        {
            _media?.Close();
            _media = null;
            var root = new Wpf.Grid { Background = Media.Brushes.Black };
            root.Children.Add(content);
            _host.Child = root;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _media?.Close();
            base.Dispose(disposing);
        }
    }

    /// <summary>A hidden top-level window that hears Explorer's "TaskbarCreated" broadcast after it restarts.</summary>
    sealed class ExplorerWatcher : NativeWindow
    {
        static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        public ExplorerWatcher() => CreateHandle(new CreateParams());

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TaskbarCreated && Windows.Count > 0)
            {
                Stop();
                DesktopReset?.Invoke();
            }
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegisterWindowMessage(string message);
    }
}
