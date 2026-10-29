using System.Drawing;
using System.Drawing.Drawing2D;

namespace MeloongCore;

/// <summary>Isolates GDI+ operations from VB's .NET 10 internal-interface resolution.</summary>
public static class DrawingCompatibility
{
    public static Bitmap Clip(Image source, int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, source.PixelFormat);
        bitmap.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.TranslateTransform(-x, -y);
        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return bitmap;
    }
}
