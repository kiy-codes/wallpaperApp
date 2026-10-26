using System.Windows.Forms.Integration;
using WallpaperApp.Native;
using Wpf = System.Windows.Controls;
using Stretch = System.Windows.Media.Stretch;

namespace WallpaperApp.Core;

/// <summary>
/// Plays a looping, muted video behind the desktop icons. Unlike pictures, it only shows while the app is running.
/// Uses WPF's MediaElement, which plays MP4 (H.264) with the decoders built into Windows.
/// </summary>
public static class VideoWallpaper
{
    static readonly List<PlayerWindow> Players = [];
    static ExplorerWatcher? _watcher;

    /// <summary>A video couldn't be played. Raised on the UI thread.</summary>
    public static event Action<string>? Failed;

    /// <summary>Explorer restarted and took the video with it, so it needs to be applied again.</summary>
    public static event Action? DesktopReset;

    public static bool IsPlaying => Players.Count > 0 && Players.All(p => p.IsHandleCreated && !p.IsDisposed);

    /// <summary>Plays <paramref name="path"/> in each of <paramref name="screenAreas"/>, replacing any video already playing.</summary>
    public static void Play(string path, IReadOnlyList<Rectangle> screenAreas, FitStyle fit)
    {
        Stop();
        _watcher ??= new ExplorerWatcher();
        try
        {
            foreach (var area in screenAreas)
            {
                var player = new PlayerWindow(path, area, ToStretch(fit));
                Players.Add(player);
                player.Start();
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public static void Stop()
    {
        foreach (var player in Players)
        {
            var parent = player.IsHandleCreated ? DesktopHost.ParentOf(player.Handle) : IntPtr.Zero;
            player.Dispose();
            DesktopHost.Repaint(parent);
        }
        Players.Clear();
    }

    static Stretch ToStretch(FitStyle fit) => fit switch
    {
        FitStyle.Fit => Stretch.Uniform,
        FitStyle.Stretch => Stretch.Fill,
        FitStyle.Center => Stretch.None,
        _ => Stretch.UniformToFill, // Fill, and Tile, which has no video equivalent
    };

    sealed class PlayerWindow : Form
    {
        readonly Rectangle _area;
        readonly Wpf.MediaElement _media;

        public PlayerWindow(string path, Rectangle area, Stretch stretch)
        {
            _area = area;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
            BackColor = Color.Black;

            _media = new Wpf.MediaElement
            {
                Source = new Uri(path),
                LoadedBehavior = Wpf.MediaState.Manual,
                UnloadedBehavior = Wpf.MediaState.Manual,
                IsMuted = true,
                Stretch = stretch,
            };
            _media.MediaEnded += (_, _) => { _media.Position = TimeSpan.Zero; _media.Play(); };
            _media.MediaFailed += (_, e) => Failed?.Invoke($"Couldn't play the video: {e.ErrorException?.Message}");

            var root = new Wpf.Grid { Background = System.Windows.Media.Brushes.Black };
            root.Children.Add(_media);
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = root });
        }

        protected override bool ShowWithoutActivation => true;

        public void Start()
        {
            DesktopHost.Attach(Handle, _area);
            Show();
            _media.Play();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _media.Close();
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
            if (m.Msg == TaskbarCreated && Players.Count > 0)
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
