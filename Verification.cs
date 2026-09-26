using System.Drawing.Imaging;
using System.Text.Json;

namespace EndfieldQteHelper;

internal static class Verification
{
    internal static int Run(string[] args)
    {
        string report = args.Length > 0 ? args[0] : "verification.json";
        var results = new List<object>();
        int failed = 0;
        void Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail });
            if (!passed) failed++;
        }
        try
        {
            var bar = new Rectangle(5, 3, 108, 5);
            Bitmap Synthetic(int whiteWidth, bool health = true, Color? barColor = null)
            {
                var bmp = new Bitmap(125, 30, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.Black);
                using var brush = new SolidBrush(barColor ?? Color.FromArgb(51, 51, 51));
                g.FillRectangle(brush, bar);
                g.FillRectangle(Brushes.White, new Rectangle(bar.X, bar.Y, whiteWidth, bar.Height));
                if (health) g.FillRectangle(Brushes.DeepSkyBlue, Detector.HealthRegion(bar));
                return bmp;
            }
            foreach (var sample in new[] { ("white full", 108, true, QteState.Ready), ("empty gray", 0, true, QteState.Cooling),
                         ("half filled", 54, true, QteState.Cooling), ("almost full", 106, true, QteState.Cooling),
                         ("HUD absent", 108, false, QteState.Unknown), ("black frame", 0, false, QteState.Unknown) })
            {
                using var bmp = Synthetic(sample.Item2, sample.Item3);
                var reading = Detector.Read(new PixelFrame(bmp), bar);
                Check(sample.Item1, reading.State == sample.Item4, reading);
            }
            using (var bmp = Synthetic(0, true, Color.Red))
                Check("colored effect rejected", Detector.Read(new PixelFrame(bmp), bar).State == QteState.Unknown);
            using (var bmp = Synthetic(108))
                Check("out of bounds rejected", Detector.Read(new PixelFrame(bmp), new Rectangle(120, 0, 20, 5)).State == QteState.Unknown);
            foreach (int filled in new[] { 0, 27, 54, 81, 106, 108 })
            {
                using var bmp = Synthetic(0);
                using (var g = Graphics.FromImage(bmp))
                using (var gray = new SolidBrush(Color.FromArgb(160, 160, 160)))
                    g.FillRectangle(gray, new Rectangle(bar.X, bar.Y, filled, bar.Height));
                var reading = Detector.Read(new PixelFrame(bmp), bar);
                Check($"gray fill {filled}/108", reading.State == QteState.Cooling && Math.Abs(reading.Progress - filled / 108.0) < .001, reading);
            }
            using (var bmp = Synthetic(27))
            {
                using (var g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.White, bar.X + 75, bar.Y, 15, bar.Height);
                var reading = Detector.Read(new PixelFrame(bmp), bar);
                Check("detached bright effect does not extend progress", Math.Abs(reading.Progress - .25) < .001, reading);
            }
            using (var bmp = Synthetic(54))
            {
                using (var g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Black, bar.X + 12, bar.Y, 1, bar.Height);
                Check("one pixel internal gap tolerated", Math.Abs(Detector.Read(new PixelFrame(bmp), bar).Progress - .5) < .001);
            }
            using (var bmp = Synthetic(54, false))
                Check("missing HUD clears progress", Detector.Read(new PixelFrame(bmp), bar).Progress == 0);
            using (var bmp = Synthetic(0))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var gray = new SolidBrush(Color.FromArgb(100, 100, 100)))
                    g.FillRectangle(gray, bar.X, bar.Y, 54, bar.Height);
                var frame = new PixelFrame(bmp);
                Check("dim fill threshold adjustment", Detector.Read(frame, bar).Progress == 0 && Math.Abs(Detector.Read(frame, bar, 220, 90).Progress - .5) < .001);
            }
            var progressFilter = new StateFilter();
            progressFilter.Update(QteState.Cooling, .25);
            progressFilter.Update(QteState.Cooling, .25);
            Check("confirmed cooldown carries progress", progressFilter.State == QteState.Cooling && progressFilter.Progress == .25);
            progressFilter.Update(QteState.Cooling, .75);
            Check("progress updates without state change", progressFilter.Progress == .75);
            progressFilter.Update(QteState.Unknown, .75);
            Check("unknown clears filtered progress", progressFilter.Progress == 0);
            Check("old configuration receives progress default", JsonSerializer.Deserialize<Settings>("{\"Brightness\":220}")!.ProgressBrightness == 120);
            var f = new StateFilter();
            Check("ready debounce frame 1", f.Update(QteState.Ready) == QteState.Unknown);
            Check("ready debounce frame 2", f.Update(QteState.Ready) == QteState.Unknown);
            Check("ready debounce frame 3", f.Update(QteState.Ready) == QteState.Ready);
            Check("single dark frame ignored", f.Update(QteState.Cooling) == QteState.Ready);
            Check("cooldown confirmed", f.Update(QteState.Cooling) == QteState.Cooling);
            Check("capture loss clears stale state", f.Update(QteState.Unknown) == QteState.Unknown);
            f.Update(QteState.Ready); f.Update(QteState.Ready); f.Reset();
            Check("reset clears pending frames", f.Update(QteState.Ready) == QteState.Unknown);
            var settings = new Settings();
            var ultrawide = settings.GetRegions(new Size(3440, 1440));
            Check("ultrawide bottom-left anchor", ultrawide[0] == settings.GetRegions(new Size(2560, 1440))[0]);
            var normalized = Region.From(bar, new Size(125, 30));
            Check("calibration round trip", normalized.Pixels(new Size(125, 30)) == bar);
            var screen = Screen.PrimaryScreen!;
            using (var capture = Capture.Screen(new Rectangle(screen.Bounds.Location, new Size(32, 32))))
                Check("Windows desktop capture", new PixelFrame(capture).Size == new Size(32, 32));
            using (var marker = new OverlayForm(settings))
            {
                _ = marker.Handle;
                Check("overlay starts mouse-transparent", (Native.GetWindowLong(marker.Handle, -20) & 0x20) != 0);
                marker.Editing = true;
                Check("overlay can be dragged when unlocked", (Native.GetWindowLong(marker.Handle, -20) & 0x20) == 0);
                marker.Editing = false;
                Check("overlay restores mouse pass-through", (Native.GetWindowLong(marker.Handle, -20) & 0x20) != 0);
                marker.SetStates([QteState.Cooling, QteState.Cooling, QteState.Ready, QteState.Unknown], [0, .25, 1, 0]);
                using var pieImage = new Bitmap(marker.Width, marker.Height);
                marker.DrawToBitmap(pieImage, new Rectangle(Point.Empty, marker.Size));
                var light = Color.FromArgb(160, 173, 190).ToArgb();
                var dark = Color.FromArgb(75, 83, 96).ToArgb();
                Check("quarter pie fills top-right clockwise", pieImage.GetPixel(76, 12).ToArgb() == light && pieImage.GetPixel(56, 28).ToArgb() == dark);
                Check("empty cooldown stays dark", pieImage.GetPixel(32, 12).ToArgb() == dark);
                Check("ready stays solid green", pieImage.GetPixel(120, 12).ToArgb() == Color.FromArgb(115, 255, 185).ToArgb());
            }

            // Optional user-provided full-resolution reference images: all ready, then slot 1 cooling.
            for (int imageIndex = 1; imageIndex < args.Length; imageIndex++)
            {
                using var source = new Bitmap(args[imageIndex]);
                foreach (int width in new[] { 2560, 1920, 3840 })
                {
                    int height = width * source.Height / source.Width;
                    using var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(scaled)) g.DrawImage(source, new Rectangle(0, 0, width, height));
                    var frame = new PixelFrame(scaled);
                    var regions = settings.GetRegions(scaled.Size);
                    for (int slot = 0; slot < 4; slot++)
                    {
                        var reading = Detector.Read(frame, regions[slot]);
                        var expected = imageIndex == 2 && slot == 0 ? QteState.Cooling : QteState.Ready;
                        Check($"reference {imageIndex}, {width}px, slot {slot + 1}", reading.State == expected, reading);
                    }
                }
            }
        }
        catch (Exception e) { Check("unhandled exception", false, e.ToString()); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = failed == 0, total = results.Count, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }

    internal static int Render(string directory)
    {
        Directory.CreateDirectory(directory);
        using var main = new MainForm(previewOnly: true);
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-30000, -30000);
        main.Show();
        Application.DoEvents();
        using var mainImage = new Bitmap(main.Width, main.Height);
        main.DrawToBitmap(mainImage, new Rectangle(Point.Empty, main.Size));
        mainImage.Save(Path.Combine(directory, "settings-preview.png"));
        using var overlay = new OverlayForm(new Settings());
        overlay.SetStates([QteState.Ready, QteState.Cooling, QteState.Cooling, QteState.Unknown], [1, .25, .75, 0]);
        SaveOverlay(overlay, Path.Combine(directory, "overlay-preview.png"));
        overlay.SetStates([QteState.Cooling, QteState.Cooling, QteState.Cooling, QteState.Cooling], [0, .25, .5, .75]);
        SaveOverlay(overlay, Path.Combine(directory, "cooldown-progress-preview.png"));
        main.Close();
        return 0;
    }

    private static void SaveOverlay(OverlayForm overlay, string path)
    {
        using var image = new Bitmap(overlay.Width, overlay.Height);
        overlay.DrawToBitmap(image, new Rectangle(Point.Empty, overlay.Size));
        image.MakeTransparent(Color.Magenta);
        // DrawToBitmap does not include the native window's global opacity.
        using var translucent = new Bitmap(image.Width, image.Height);
        using (var g = Graphics.FromImage(translucent))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)overlay.Opacity });
            g.DrawImage(image, new Rectangle(Point.Empty, image.Size), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        translucent.Save(path);
    }
}
