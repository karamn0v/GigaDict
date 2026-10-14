using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GigaDict.App;

internal enum TrayState { Idle, Recording, Working, Success, Warning, Error }

internal sealed class TrayIconSet : IDisposable
{
    internal TrayIconSet(Icon appIcon)
    {
        Idle = (Icon)appIcon.Clone();
        Recording = CreateBadged(appIcon, Color.FromArgb(220, 35, 45), BadgeGlyph.Record);
        Working = CreateBadged(appIcon, Color.FromArgb(245, 166, 35), BadgeGlyph.None);
        Success = CreateBadged(appIcon, Color.FromArgb(37, 170, 83), BadgeGlyph.Check);
        Warning = CreateBadged(appIcon, Color.FromArgb(245, 166, 35), BadgeGlyph.Exclamation);
        Error = CreateBadged(appIcon, Color.FromArgb(220, 35, 45), BadgeGlyph.Cross);
    }

    internal Icon Idle { get; }
    internal Icon Recording { get; }
    internal Icon Working { get; }
    internal Icon Success { get; }
    internal Icon Warning { get; }
    internal Icon Error { get; }

    internal Icon Get(TrayState state) => state switch
    {
        TrayState.Recording => Recording,
        TrayState.Working => Working,
        TrayState.Success => Success,
        TrayState.Warning => Warning,
        TrayState.Error => Error,
        _ => Idle
    };

    private static Icon CreateBadged(Icon source, Color color, BadgeGlyph glyph)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawIcon(source, new Rectangle(0, 0, 32, 32));

            graphics.FillEllipse(Brushes.White, 17, 17, 15, 15);
            using var badgeBrush = new SolidBrush(color);
            graphics.FillEllipse(badgeBrush, 19, 19, 11, 11);
            DrawGlyph(graphics, glyph);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    private static void DrawGlyph(Graphics graphics, BadgeGlyph glyph)
    {
        using var pen = new Pen(Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (glyph)
        {
            case BadgeGlyph.Record:
                graphics.FillEllipse(Brushes.White, 22, 22, 5, 5);
                break;
            case BadgeGlyph.Check:
                graphics.DrawLines(pen, [new PointF(21.5f, 24.5f), new PointF(24f, 27f), new PointF(28f, 22f)]);
                break;
            case BadgeGlyph.Exclamation:
                graphics.DrawLine(pen, 24.5f, 21.5f, 24.5f, 25.5f);
                graphics.FillEllipse(Brushes.White, 23.6f, 27f, 1.8f, 1.8f);
                break;
            case BadgeGlyph.Cross:
                graphics.DrawLine(pen, 22f, 22f, 28f, 28f);
                graphics.DrawLine(pen, 28f, 22f, 22f, 28f);
                break;
        }
    }

    public void Dispose()
    {
        Idle.Dispose();
        Recording.Dispose();
        Working.Dispose();
        Success.Dispose();
        Warning.Dispose();
        Error.Dispose();
    }

    private enum BadgeGlyph { None, Record, Check, Exclamation, Cross }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
