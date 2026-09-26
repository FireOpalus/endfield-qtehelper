using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EndfieldQteHelper;

public enum QteState { Unknown, Cooling, Ready }
public record Reading(QteState State, double White, double Hud, string Detail);

public sealed class PixelFrame
{
    private readonly byte[] bytes;
    private readonly int stride;
    public Size Size { get; }
    public PixelFrame(Bitmap bitmap)
    {
        Size = bitmap.Size;
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = Math.Abs(data.Stride);
            bytes = new byte[stride * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * stride, stride);
        }
        finally { bitmap.UnlockBits(data); }
    }
    public Color At(int x, int y)
    {
        int i = y * stride + x * 4;
        return Color.FromArgb(bytes[i + 2], bytes[i + 1], bytes[i]);
    }
}

public static class Detector
{
    public static Rectangle HealthRegion(Rectangle bar) => new(bar.X, bar.Bottom + Math.Max(1, (int)Math.Round(bar.Height * .6)),
        bar.Width, Math.Max(2, (int)Math.Round(bar.Height * 1.4)));

    public static Reading Read(PixelFrame frame, Rectangle bar, int brightness = 220)
    {
        var health = HealthRegion(bar);
        var bounds = new Rectangle(Point.Empty, frame.Size);
        if (bar.Width < 8 || bar.Height < 1 || !bounds.Contains(bar) || !bounds.Contains(health))
            return new(QteState.Unknown, 0, 0, "区域超出画面，请重新校准");
        int cyan = 0;
        for (int y = health.Top; y < health.Bottom; y++)
            for (int x = health.Left; x < health.Right; x++)
            {
                var c = frame.At(x, y);
                if (c.B > 135 && c.G > 95 && c.B - c.R > 65 && c.G - c.R > 45) cyan++;
            }
        double hud = (double)cyan / (health.Width * health.Height);
        int whiteColumns = 0, neutral = 0, tailWhite = 0;
        int tailStart = bar.Width * 9 / 10;
        for (int x = 0; x < bar.Width; x++)
        {
            int white = 0;
            for (int y = bar.Top; y < bar.Bottom; y++)
            {
                var c = frame.At(bar.X + x, y);
                int min = Math.Min(c.R, Math.Min(c.G, c.B)), max = Math.Max(c.R, Math.Max(c.G, c.B));
                if (max - min <= 42) neutral++;
                if (min >= brightness && max - min <= 42) white++;
            }
            if (white >= Math.Max(1, (int)Math.Ceiling(bar.Height * .6)))
            {
                whiteColumns++;
                if (x >= tailStart) tailWhite++;
            }
        }
        double ratio = (double)whiteColumns / bar.Width;
        if (hud < .08 || (double)neutral / (bar.Width * bar.Height) < .70)
            return new(QteState.Unknown, ratio, hud, "未检测到血条或进度条，等待 HUD");
        bool ready = ratio >= .985 && (double)tailWhite / (bar.Width - tailStart) >= .95;
        return new(ready ? QteState.Ready : QteState.Cooling, ratio, hud, ready ? "白条已填满" : "进度条未填满");
    }
}

public sealed class StateFilter
{
    public QteState State { get; private set; }
    private QteState candidate;
    private int count;
    public QteState Update(QteState next)
    {
        if (next == QteState.Unknown) { Reset(); return State; }
        if (candidate != next) { candidate = next; count = 1; } else count++;
        if (count >= (next == QteState.Ready ? 3 : 2)) State = next;
        return State;
    }
    public void Reset() { State = candidate = QteState.Unknown; count = 0; }
}

public static class Capture
{
    public static Bitmap Screen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
