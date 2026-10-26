using System.Drawing.Drawing2D;

namespace WallpaperApp.UI;

/// <summary>A small landscape picture drawn at runtime, used for the tray and window icon.</summary>
static class AppIcon
{
    static Icon? s_icon;

    public static Icon Value => s_icon ??= Create();

    static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(45, 45, 55));
            g.FillRectangle(frame, 1, 4, 30, 24);
            using var sky = new LinearGradientBrush(new Rectangle(3, 6, 26, 20), Color.FromArgb(70, 140, 230), Color.FromArgb(170, 210, 250), 90f);
            g.FillRectangle(sky, 3, 6, 26, 20);
            using var sun = new SolidBrush(Color.FromArgb(255, 210, 70));
            g.FillEllipse(sun, 20, 8, 6, 6);
            using var hills = new SolidBrush(Color.FromArgb(60, 160, 90));
            g.FillPolygon(hills, [new Point(3, 26), new Point(12, 14), new Point(18, 20), new Point(22, 17), new Point(29, 26)]);
        }
        // The handle lives for the whole process, so it is never destroyed.
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
