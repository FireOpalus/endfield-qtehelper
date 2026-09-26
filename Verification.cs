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
        overlay.SetStates([QteState.Ready, QteState.Cooling, QteState.Ready, QteState.Unknown]);
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
        translucent.Save(Path.Combine(directory, "overlay-preview.png"));
        main.Close();
        return 0;
    }
}
