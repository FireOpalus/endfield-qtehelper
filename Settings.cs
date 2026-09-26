using System.Text.Json;

namespace EndfieldQteHelper;

public record Region(double X, double Y, double Width, double Height)
{
    public Rectangle Pixels(Size size) => new((int)Math.Round(X * size.Width), (int)Math.Round(Y * size.Height),
        Math.Max(1, (int)Math.Round(Width * size.Width)), Math.Max(1, (int)Math.Round(Height * size.Height)));
    public static Region From(Rectangle rect, Size size) => new((double)rect.X / size.Width, (double)rect.Y / size.Height,
        (double)rect.Width / size.Width, (double)rect.Height / size.Height);
    public bool Valid => double.IsFinite(X + Y + Width + Height) && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1 && Y + Height <= 1;
}

public sealed class Settings
{
    public string Monitor { get; set; } = "";
    public string[] Names { get; set; } = ["角色 1", "角色 2", "角色 3", "角色 4"];
    public Region[]? Regions { get; set; }
    public int Brightness { get; set; } = 220;
    public int ProgressBrightness { get; set; } = 120;
    public int Interval { get; set; } = 100;
    public int OverlayScale { get; set; } = 100;
    public double OverlayX { get; set; } = .5;
    public double OverlayY { get; set; } = .42;
    public bool Sound { get; set; }
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndfieldQteHelper", "settings.json");

    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            if (s.Names is null || s.Names.Length != 4) s.Names = new Settings().Names;
            s.Names = s.Names.Select((n, i) => string.IsNullOrWhiteSpace(n) ? $"角色 {i + 1}" : n[..Math.Min(n.Length, 12)]).ToArray();
            if (s.Regions is not { Length: 4 } || s.Regions.Any(r => r is null || !r.Valid)) s.Regions = null;
            s.Brightness = Math.Clamp(s.Brightness, 140, 250);
            s.ProgressBrightness = Math.Clamp(s.ProgressBrightness, 60, 200);
            s.Interval = Math.Clamp(s.Interval, 50, 500);
            s.OverlayScale = Math.Clamp(s.OverlayScale, 60, 180);
            if (!double.IsFinite(s.OverlayX)) s.OverlayX = .5;
            if (!double.IsFinite(s.OverlayY)) s.OverlayY = .42;
            s.OverlayX = Math.Clamp(s.OverlayX, .05, .95);
            s.OverlayY = Math.Clamp(s.OverlayY, .05, .95);
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }

    public Rectangle[] GetRegions(Size size)
    {
        if (Regions is { Length: 4 }) return Regions.Select(r => r.Pixels(size)).ToArray();
        // HUD is anchored to the bottom left; use height scaling for wider displays.
        double scale = size.Height / 1440.0;
        return new[] { 58, 213, 369, 525 }.Select(x => new Rectangle((int)Math.Round(x * scale),
            size.Height - (int)Math.Round(124 * scale), Math.Max(8, (int)Math.Round(108 * scale)),
            Math.Max(2, (int)Math.Round(5 * scale)))).ToArray();
    }
}
