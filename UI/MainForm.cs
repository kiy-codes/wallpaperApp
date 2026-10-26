using WallpaperApp.Core;
using WallpaperApp.Native;

namespace WallpaperApp.UI;

/// <summary>
/// Settings window. Edits a draft copy of the config; nothing changes on the desktop until Apply.
/// Closing it hides it to the tray.
/// </summary>
internal sealed class MainForm : Form
{
    // Same order as the MonitorLayout and FitStyle enums.
    static readonly string[] LayoutNames = ["Same picture on every monitor", "Different picture per monitor", "Stretch one picture across all monitors"];
    static readonly string[] FitNames = ["Fill", "Fit", "Stretch", "Center", "Tile"];

    readonly TrayApplication _app;
    readonly PortableStorage _storage;
    readonly Dictionary<string, Image?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    AppConfig _draft = new();
    List<MonitorInfo> _monitors = [];
    int _selectedMonitor;
    bool _loading;
    bool _trayHintShown;

    readonly RadioButton _imageMode = new() { Text = "Picture", AutoSize = true };
    readonly RadioButton _slideshowMode = new() { Text = "Slideshow", AutoSize = true };
    readonly ComboBox _layout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly ComboBox _fit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    readonly MonitorPreview _preview = new() { Size = new Size(540, 210), Margin = new Padding(3, 6, 3, 6) };
    readonly GroupBox _imageGroup = Group("Picture");
    readonly Button _chooseImage = new() { Text = "Choose picture…", AutoSize = true };
    readonly Label _imageLabel = InfoLabel();
    readonly GroupBox _slideshowGroup = Group("Slideshow");
    readonly Button _chooseFolder = new() { Text = "Choose folder…", AutoSize = true };
    readonly Label _folderLabel = InfoLabel();
    readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 1440, Width = 70 };
    readonly CheckBox _shuffle = new() { Text = "Shuffle", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    readonly TextBox _hotkey = new() { Width = 160, ReadOnly = true, BackColor = SystemColors.Window, ShortcutsEnabled = false };
    readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    readonly Label _warnings = new() { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = Color.FromArgb(150, 75, 0), Margin = new Padding(3, 8, 3, 3) };
    readonly Label _status = new() { AutoSize = true, Margin = new Padding(3, 9, 12, 3) };
    readonly Button _apply = new() { Text = "Apply", AutoSize = true };
    readonly Button _close = new() { Text = "Close", AutoSize = true };

    public MainForm(TrayApplication app, PortableStorage storage)
    {
        _app = app;
        _storage = storage;

        Text = "Wallpaper App";
        Icon = AppIcon.Value;
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _apply;

        _layout.Items.AddRange(LayoutNames);
        _fit.Items.AddRange(FitNames);
        BuildLayout();
        WireEvents();
    }

    public void LoadConfig(AppConfig config)
    {
        _draft = config.Clone();
        _monitors = LoadMonitors();
        _selectedMonitor = 0;
        Silently(() =>
        {
            _imageMode.Checked = _draft.Mode == WallpaperMode.Image;
            _slideshowMode.Checked = _draft.Mode == WallpaperMode.Slideshow;
            _layout.SelectedIndex = (int)_draft.Layout;
            _fit.SelectedIndex = (int)_draft.Fit;
            _interval.Value = Math.Clamp(_draft.SlideshowMinutes, 1, 1440);
            _shuffle.Checked = _draft.Shuffle;
            _hotkey.Text = _draft.Hotkey;
            _startWithWindows.Checked = StartupManager.IsEnabled;
        });
        _status.Text = "";
        RefreshView();
    }

    public void RefreshMonitors()
    {
        _monitors = LoadMonitors();
        RefreshView();
    }

    void BuildLayout()
    {
        var imageRow = Row(_chooseImage, _imageLabel);
        imageRow.Dock = DockStyle.Fill;
        _imageGroup.Controls.Add(imageRow);

        var slideshowPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        slideshowPanel.Controls.Add(Row(_chooseFolder, _folderLabel));
        slideshowPanel.Controls.Add(Row(Caption("Change picture every"), _interval, Caption("minutes"), _shuffle));
        _slideshowGroup.Controls.Add(slideshowPanel);

        var clearHotkey = new Button { Text = "Clear", AutoSize = true };
        clearHotkey.Click += (_, _) => { _hotkey.Text = ""; _draft.Hotkey = ""; };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Fill,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.AddRange([_close, _apply, _status]);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12),
        };
        root.Controls.Add(Row(Caption("Show"), _imageMode, _slideshowMode));
        root.Controls.Add(Row(Caption("Monitors"), _layout, Caption("Fit"), _fit));
        root.Controls.Add(_preview);
        root.Controls.Add(_imageGroup);
        root.Controls.Add(_slideshowGroup);
        root.Controls.Add(Row(Caption("\"Next image\" hotkey"), _hotkey, clearHotkey));
        root.Controls.Add(_startWithWindows);
        root.Controls.Add(_warnings);
        root.Controls.Add(buttons);
        Controls.Add(root);
    }

    void WireEvents()
    {
        _imageMode.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _draft.Mode = _imageMode.Checked ? WallpaperMode.Image : WallpaperMode.Slideshow;
            if (_draft.Mode == WallpaperMode.Slideshow && _draft.Layout == MonitorLayout.PerMonitor)
            {
                _draft.Layout = MonitorLayout.SameOnAll;
                Silently(() => _layout.SelectedIndex = (int)_draft.Layout);
            }
            RefreshView();
        };
        _layout.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            var layout = (MonitorLayout)_layout.SelectedIndex;
            if (layout == MonitorLayout.PerMonitor && _draft.Mode == WallpaperMode.Slideshow)
            {
                _status.Text = "Slideshows use one folder; Windows shows a different picture on each monitor.";
                Silently(() => _layout.SelectedIndex = (int)_draft.Layout);
                return;
            }
            _draft.Layout = layout;
            RefreshView();
        };
        _fit.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _draft.Fit = (FitStyle)_fit.SelectedIndex;
            RefreshView();
        };
        _interval.ValueChanged += (_, _) => { if (!_loading) _draft.SlideshowMinutes = (int)_interval.Value; };
        _shuffle.CheckedChanged += (_, _) => { if (!_loading) _draft.Shuffle = _shuffle.Checked; };
        _preview.MonitorClicked += index => { _selectedMonitor = index; RefreshView(); };
        _chooseImage.Click += (_, _) => ChooseImage();
        _chooseFolder.Click += (_, _) => ChooseFolder();
        _hotkey.KeyDown += OnHotkeyKeyDown;
        _apply.Click += (_, _) => ApplyDraft();
        _close.Click += (_, _) => Close();
        FormClosing += OnFormClosing;
    }

    void RefreshView()
    {
        bool slideshow = _draft.Mode == WallpaperMode.Slideshow;
        bool perMonitor = !slideshow && _draft.Layout == MonitorLayout.PerMonitor;
        bool span = _draft.Layout == MonitorLayout.Span;
        if (_selectedMonitor >= _monitors.Count) _selectedMonitor = 0;

        _imageGroup.Visible = !slideshow;
        _slideshowGroup.Visible = slideshow;
        _fit.Enabled = !span;

        var slideshowFolder = _draft.SlideshowFolder is null ? null : _storage.Resolve(_draft.SlideshowFolder);
        List<string> slideshowImages = slideshowFolder is null ? [] : PortableStorage.ListImages(slideshowFolder);
        var shared = Thumbnail(slideshow ? slideshowImages.FirstOrDefault() : _draft.Image);

        _preview.Monitors = _monitors;
        _preview.Fit = _draft.Fit;
        _preview.SpanImage = span ? shared : null;
        _preview.ImageFor = perMonitor ? m => Thumbnail(_draft.ImageFor(m.Id, m.Index)) : _ => shared;
        _preview.SelectedIndex = perMonitor ? _selectedMonitor : -1;
        _preview.Cursor = perMonitor ? Cursors.Hand : Cursors.Default;
        _preview.Invalidate();

        if (perMonitor && _monitors.Count > 0)
        {
            var monitor = _monitors[_selectedMonitor];
            _imageGroup.Text = $"Picture for monitor {_selectedMonitor + 1} (click a monitor above to switch)";
            _imageLabel.Text = FileLabel(_draft.ImageFor(monitor.Id, monitor.Index));
        }
        else
        {
            _imageGroup.Text = "Picture";
            _imageLabel.Text = FileLabel(_draft.Image);
        }

        _folderLabel.Text = slideshowFolder is null
            ? "No folder chosen"
            : $"{Path.GetFileName(slideshowFolder)} ({slideshowImages.Count} pictures)";

        UpdateWarnings();
    }

    void ChooseImage()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a picture", Filter = PortableStorage.ImageFilter };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string image;
        try { image = _storage.ImportImage(dialog.FileName); }
        catch (Exception ex) { ShowError("Couldn't copy the picture.", ex); return; }

        if (_draft.Layout == MonitorLayout.PerMonitor && _monitors.Count > 0)
        {
            var monitor = _monitors[_selectedMonitor];
            _draft.SetMonitorImage(monitor.Id, monitor.Index, image);
        }
        else
        {
            _draft.Image = image;
        }
        _status.Text = "";
        RefreshView();
    }

    void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder of pictures", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        UseWaitCursor = true;
        try { _draft.SlideshowFolder = _storage.ImportFolder(dialog.SelectedPath); }
        catch (Exception ex) { ShowError("Couldn't use that folder.", ex); }
        finally { UseWaitCursor = false; }
        _status.Text = "";
        RefreshView();
    }

    void ApplyDraft()
    {
        UseWaitCursor = true;
        try
        {
            _app.Apply(_draft.Clone(), _startWithWindows.Checked);
            _status.Text = "Applied.";
        }
        catch (Exception ex)
        {
            ShowError("Couldn't apply the wallpaper.", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
        UpdateWarnings();
    }

    void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;

        if (e.Modifiers == Keys.None && e.KeyCode is Keys.Back or Keys.Delete or Keys.Escape)
        {
            _hotkey.Text = _draft.Hotkey = "";
            return;
        }
        if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0)
        {
            _status.Text = "Hotkeys need Ctrl or Alt.";
            return;
        }
        _hotkey.Text = _draft.Hotkey = HotkeyWindow.Format(e.KeyData);
        _status.Text = "";
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _app.Notify("Still running in the tray. Right-click the icon to exit.");
        }
    }

    void UpdateWarnings()
    {
        var warnings = _app.GetWarnings();
        _warnings.Text = string.Join(Environment.NewLine, warnings.Select(w => "⚠ " + w));
        _warnings.Visible = warnings.Count > 0;
    }

    Image? Thumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var full = _storage.Resolve(path);
        if (!_thumbnails.TryGetValue(full, out var thumbnail))
            _thumbnails[full] = thumbnail = LoadThumbnail(full);
        return thumbnail;
    }

    /// <summary>Small copy for the preview; reading through a stream avoids locking the file.</summary>
    static Image? LoadThumbnail(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream);
            float scale = Math.Min(1f, 480f / Math.Max(source.Width, source.Height));
            return new Bitmap(source, Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    static List<MonitorInfo> LoadMonitors()
    {
        try
        {
            using var wallpaper = DesktopWallpaper.Create();
            return wallpaper.GetMonitors();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return [];
        }
    }

    void Silently(Action change)
    {
        _loading = true;
        try { change(); }
        finally { _loading = false; }
    }

    void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, $"{title}{Environment.NewLine}{Environment.NewLine}{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    static string FileLabel(string? path) => path is null ? "No picture chosen" : Path.GetFileName(path);

    static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        row.Controls.AddRange(controls);
        return row;
    }

    static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    static Label InfoLabel() => new() { AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(6, 8, 3, 3) };

    static GroupBox Group(string text) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(546, 0), Padding = new Padding(6),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var thumbnail in _thumbnails.Values) thumbnail?.Dispose();
        base.Dispose(disposing);
    }
}
