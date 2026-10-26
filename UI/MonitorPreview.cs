using System.ComponentModel;
using System.Drawing.Drawing2D;
using WallpaperApp.Core;
using WallpaperApp.Native;

namespace WallpaperApp.UI;

/// <summary>Draws the monitors in their real arrangement with a preview of each one's picture.</summary>
internal sealed class MonitorPreview : Control
{
    static readonly Color Accent = Color.FromArgb(0, 120, 215);

    public MonitorPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.ControlLight;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<MonitorInfo> Monitors { get; set; } = [];

    /// <summary>Picture for each monitor when not spanning.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<MonitorInfo, Image?> ImageFor { get; set; } = _ => null;

    /// <summary>When set, one picture is stretched across the whole desktop.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? SpanImage { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FitStyle Fit { get; set; }

    /// <summary>Highlighted monitor, or -1 for none.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get; set; } = -1;

    public event Action<int>? MonitorClicked;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Monitors.Count == 0)
        {
            TextRenderer.DrawText(g, "No monitors found", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        var map = Mapping();
        var desktop = map(Monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union));

        for (int i = 0; i < Monitors.Count; i++)
        {
            var monitor = Monitors[i];
            var rect = map(monitor.Bounds);
            rect.Inflate(-2, -2);

            using (var screen = new SolidBrush(Color.FromArgb(30, 30, 36)))
                g.FillRectangle(screen, rect);

            g.SetClip(rect);
            if (SpanImage is not null) DrawPicture(g, SpanImage, desktop, FitStyle.Fill);
            else if (ImageFor(monitor) is { } image) DrawPicture(g, image, rect, Fit);
            g.ResetClip();

            bool selected = i == SelectedIndex;
            using (var border = new Pen(selected ? Accent : Color.FromArgb(90, 90, 90), selected ? 3 : 1))
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);

            var label = new Rectangle((int)rect.X + 4, (int)rect.Y + 4, 22, 20);
            using (var badge = new SolidBrush(selected ? Accent : Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(badge, label);
            TextRenderer.DrawText(g, (i + 1).ToString(), Font, label, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Monitors.Count == 0) return;
        var map = Mapping();
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (map(Monitors[i].Bounds).Contains(e.Location))
            {
                MonitorClicked?.Invoke(i);
                return;
            }
        }
    }

    /// <summary>Scales desktop coordinates down to fit the control, centred.</summary>
    Func<Rectangle, RectangleF> Mapping()
    {
        const float margin = 8;
        var all = Monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
        float scale = Math.Min((Width - 2 * margin) / all.Width, (Height - 2 * margin) / all.Height);
        float offsetX = (Width - all.Width * scale) / 2 - all.X * scale;
        float offsetY = (Height - all.Height * scale) / 2 - all.Y * scale;
        return r => new RectangleF(r.X * scale + offsetX, r.Y * scale + offsetY, r.Width * scale, r.Height * scale);
    }

    /// <summary>Approximates how Windows lays the picture out. Center and Tile are previewed as Fill.</summary>
    static void DrawPicture(Graphics g, Image image, RectangleF dest, FitStyle fit)
    {
        if (fit == FitStyle.Stretch)
        {
            g.DrawImage(image, dest);
            return;
        }
        float scaleX = dest.Width / image.Width, scaleY = dest.Height / image.Height;
        float scale = fit == FitStyle.Fit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
        float w = image.Width * scale, h = image.Height * scale;
        g.DrawImage(image, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
    }
}
