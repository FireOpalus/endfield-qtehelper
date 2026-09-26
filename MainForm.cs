using System.Media;

namespace EndfieldQteHelper;

public sealed class MainForm : Form
{
    private readonly Settings settings = Settings.Load();
    private readonly OverlayForm overlay;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly StateFilter[] filters = Enumerable.Range(0, 4).Select(_ => new StateFilter()).ToArray();
    private readonly ComboBox monitors = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 };
    private readonly Label status = new() { AutoSize = false, Width = 650, Height = 48 };
    private readonly Label[] readings = new Label[4];
    private readonly TextBox[] names = new TextBox[4];
    private readonly Button start = new() { Text = "开始检测", AutoSize = true };
    private readonly Button unlock = new() { Text = "调整悬浮位置", AutoSize = true };
    private readonly PictureBox preview = new() { Width = 650, Height = 104, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(15, 20, 28) };
    private readonly NotifyIcon tray;
    private bool running, overlayVisible = true, calibrating;
    private bool initialized;
    private bool hotkeysOk = true;
    private Screen[] screens = [];
    private string observedLayout = "";
    private Screen SelectedScreen => screens.Length > 0 && monitors.SelectedIndex >= 0 ? screens[Math.Min(monitors.SelectedIndex, screens.Length - 1)] : Screen.PrimaryScreen!;

    public MainForm(bool previewOnly = false)
    {
        Text = $"终末地 · QTE 冷却助手 v{Program.Version}";
        Icon = Program.LoadIcon();
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(720, 730);
        MinimumSize = new Size(740, 770);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(242, 245, 249);
        overlay = new OverlayForm(settings);
        overlay.PositionChanged += () => { StorePosition(); Save(); };
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 18, 16, 16) };
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "QTE 冷却，抬眼可见", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) });
        root.Controls.Add(new Label { Text = "绿圆 = 冷却完成    灰圆扇区 = 冷却进度    黄圆 = 未识别\n浅灰从顶部顺时针填充，填满后等待白条确认；标记不透明度 50%。", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        root.Controls.Add(Row(new Label { Text = "游戏显示器", AutoSize = true, Width = 110 }, monitors));
        RefreshMonitors();
        monitors.SelectedIndexChanged += (_, _) =>
        {
            if (!initialized) return;
            settings.Monitor = SelectedScreen.DeviceName;
            settings.Regions = null;
            ResetStates(); overlay.Place(SelectedScreen.Bounds); Save();
        };
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            names[i] = new TextBox { Text = settings.Names[i], Width = 140, MaxLength = 12 };
            names[i].TextChanged += (_, _) => { settings.Names[slot] = names[slot].Text; overlay.Invalidate(); };
            readings[i] = new Label { Text = "未识别", AutoSize = true, Margin = new Padding(18, 7, 0, 0) };
            root.Controls.Add(Row(new Label { Text = $"{i + 1} 号角色", AutoSize = true, Margin = new Padding(0, 7, 18, 0) }, names[i], readings[i]));
        }
        var calibrate = Button("校准四条进度条", async () => await Calibrate());
        var restore = Button("恢复默认识别区域", () => { settings.Regions = null; ResetStates(); Save(); status.Text = "已恢复截图对应的默认 HUD 布局。"; });
        root.Controls.Add(Row(calibrate, restore));
        root.Controls.Add(new Label { Text = "实时采样（青框为血条校验区，黄框为 QTE 识别区）", AutoSize = true });
        root.Controls.Add(preview);
        var brightness = new NumericUpDown { Minimum = 140, Maximum = 250, Value = settings.Brightness, Width = 68 };
        brightness.ValueChanged += (_, _) => { settings.Brightness = (int)brightness.Value; ResetStates(); };
        var progressBrightness = new NumericUpDown { Minimum = 60, Maximum = 200, Value = settings.ProgressBrightness, Width = 68 };
        progressBrightness.ValueChanged += (_, _) => { settings.ProgressBrightness = (int)progressBrightness.Value; ResetStates(); };
        var scale = new NumericUpDown { Minimum = 60, Maximum = 180, Increment = 10, Value = settings.OverlayScale, Width = 68 };
        scale.ValueChanged += (_, _) => { settings.OverlayScale = (int)scale.Value; overlay.Place(SelectedScreen.Bounds); };
        root.Controls.Add(Row(new Label { Text = "就绪亮度", AutoSize = true }, brightness, new Label { Text = "  进度亮度", AutoSize = true }, progressBrightness, new Label { Text = "  大小 %", AutoSize = true }, scale));
        var sound = new CheckBox { Text = "从冷却变为就绪时播放提示音", Checked = settings.Sound, AutoSize = true };
        sound.CheckedChanged += (_, _) => settings.Sound = sound.Checked;
        root.Controls.Add(sound);
        start.Click += (_, _) => ToggleRunning();
        unlock.Click += (_, _) => ToggleEditing();
        root.Controls.Add(Row(start, unlock, Button("显示 / 隐藏标记", ToggleOverlay), Button("收到托盘", () => { Save(); Hide(); })));
        root.Controls.Add(status);
        root.Controls.Add(new Label { Text = "Ctrl+Alt+F8 调整 / 锁定位置    F9 暂停 / 继续    F10 显示 / 隐藏\n三个快捷键都需同时按 Ctrl+Alt。请使用无边框窗口或窗口模式。\n首次使用请核对采样区；窗口模式、HUD 缩放变化后请重新校准。", AutoSize = true, ForeColor = Color.FromArgb(85, 95, 112) });
        tray = new NotifyIcon { Icon = Icon, Text = $"终末地 QTE 助手 v{Program.Version}", Visible = !previewOnly };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("暂停 / 继续", null, (_, _) => ToggleRunning());
        menu.Items.Add("显示 / 隐藏标记", null, (_, _) => ToggleOverlay());
        menu.Items.Add("退出", null, (_, _) => Close());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowSettings();
        timer.Interval = settings.Interval;
        timer.Tick += (_, _) => TickCapture();
        initialized = true;
        Shown += (_, _) =>
        {
            if (previewOnly) return;
            for (int i = 0; i < 3; i++) hotkeysOk &= Native.RegisterHotKey(Handle, i + 1, 0x4003, (uint)Keys.F8 + (uint)i);
            overlay.Place(SelectedScreen.Bounds);
            overlay.Show();
            status.Text = hotkeysOk ? "选择游戏显示器，再点击“开始检测”。可先调整标记位置。" : "部分全局快捷键被占用；请使用设置窗口或托盘按钮。";
        };
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 5) };
        row.Controls.AddRange(controls);
        return row;
    }
    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(4, 3, 4, 3) };
        button.Click += (_, _) => action();
        return button;
    }
    private void RefreshMonitors()
    {
        screens = Screen.AllScreens;
        monitors.Items.Clear();
        foreach (var s in screens) monitors.Items.Add($"{s.DeviceName} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? " · 主屏" : "")}");
        int selected = Array.FindIndex(screens, s => s.DeviceName == settings.Monitor);
        monitors.SelectedIndex = Math.Max(0, selected);
        observedLayout = LayoutSignature();
    }
    private static string LayoutSignature() => string.Join(";", Screen.AllScreens.Select(s => $"{s.DeviceName}:{s.Bounds}"));
    private void Save()
    {
        try { settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "配置保存失败：" + e.Message; }
    }
    private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void StorePosition()
    {
        var b = SelectedScreen.Bounds;
        settings.OverlayX = Math.Clamp((overlay.Left + overlay.Width / 2.0 - b.Left) / b.Width, .05, .95);
        settings.OverlayY = Math.Clamp((overlay.Top + overlay.Height / 2.0 - b.Top) / b.Height, .05, .95);
    }
    private void ToggleEditing()
    {
        overlay.Editing = !overlay.Editing;
        unlock.Text = overlay.Editing ? "锁定位置（鼠标穿透）" : "调整悬浮位置";
        overlayVisible = true; overlay.Show();
        if (!overlay.Editing) { StorePosition(); overlay.Place(SelectedScreen.Bounds); Save(); }
    }
    private void ToggleOverlay() { overlayVisible = !overlayVisible; if (overlayVisible) overlay.Show(); else overlay.Hide(); }
    private void ToggleRunning()
    {
        if (calibrating) return;
        running = !running;
        start.Text = running ? "暂停检测" : "开始检测";
        ResetStates();
        if (running) { timer.Start(); Save(); TickCapture(); }
        else { timer.Stop(); status.Text = "已暂停，标记已清空。"; }
    }
    private void ResetStates()
    {
        foreach (var f in filters) f.Reset();
        overlay.SetStates(filters.Select(f => f.State).ToArray(), filters.Select(f => f.Progress).ToArray());
        foreach (var label in readings) if (label is not null) label.Text = "未识别";
    }
    private async Task Calibrate()
    {
        if (calibrating) return;
        calibrating = true;
        timer.Stop(); ResetStates();
        overlay.Hide(); Hide();
        try
        {
            await Task.Delay(400);
            var screen = SelectedScreen.Bounds;
            using var shot = EndfieldQteHelper.Capture.Screen(screen);
            using var dialog = new CalibrationForm(shot, screen);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                settings.Regions = dialog.Result.Select(r => EndfieldQteHelper.Region.From(r, screen.Size)).ToArray();
                Save();
            }
        }
        catch (Exception e) { status.Text = "校准截图失败：" + e.Message; }
        finally
        {
            calibrating = false;
            ShowSettings();
            if (overlayVisible) overlay.Show();
            if (running) timer.Start();
        }
    }
    private void TickCapture()
    {
        try
        {
            if (LayoutSignature() != observedLayout)
            {
                running = false; timer.Stop(); start.Text = "开始检测"; ResetStates();
                initialized = false; RefreshMonitors(); initialized = true;
                settings.Regions = null; overlay.Place(SelectedScreen.Bounds);
                status.Text = "显示器布局或分辨率已改变，请核对区域或重新校准后开始。";
                return;
            }
            var screen = SelectedScreen.Bounds;
            var regions = settings.GetRegions(screen.Size);
            var captureArea = regions.Select(r => Rectangle.Union(r, Detector.HealthRegion(r))).Aggregate(Rectangle.Union);
            if (!new Rectangle(Point.Empty, screen.Size).Contains(captureArea)) throw new InvalidOperationException("识别区域超出屏幕，请重新校准。");
            using var bitmap = EndfieldQteHelper.Capture.Screen(new Rectangle(captureArea.X + screen.X, captureArea.Y + screen.Y, captureArea.Width, captureArea.Height));
            var frame = new PixelFrame(bitmap);
            bool beep = false;
            int unknown = 0;
            for (int i = 0; i < 4; i++)
            {
                var region = regions[i]; region.Offset(-captureArea.X, -captureArea.Y);
                var reading = Detector.Read(frame, region, settings.Brightness, settings.ProgressBrightness);
                var previous = filters[i].State;
                var next = filters[i].Update(reading.State, reading.Progress);
                beep |= previous == QteState.Cooling && next == QteState.Ready;
                if (reading.State == QteState.Unknown) unknown++;
                readings[i].Text = next == QteState.Unknown ? "未识别" : $"{StateText(next)}   进度 {filters[i].Progress:P0}   白色 {reading.White:P0}";
                readings[i].ForeColor = next == QteState.Ready ? Color.SeaGreen : next == QteState.Cooling ? Color.DimGray : Color.DarkGoldenrod;
            }
            overlay.SetStates(filters.Select(f => f.State).ToArray(), filters.Select(f => f.Progress).ToArray());
            if (beep && settings.Sound) SystemSounds.Asterisk.Play();
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                var annotated = (Bitmap)bitmap.Clone();
                using (var g = Graphics.FromImage(annotated))
                    foreach (var r in regions)
                    {
                        var local = r; local.Offset(-captureArea.X, -captureArea.Y);
                        g.DrawRectangle(Pens.Yellow, local);
                        g.DrawRectangle(Pens.Cyan, Detector.HealthRegion(local));
                    }
                var old = preview.Image; preview.Image = annotated; old?.Dispose();
            }
            status.Text = unknown == 0 ? "正在检测 · 已启用连续帧确认 · QTE 冷却完成不代表触发条件已满足" : $"正在检测 · {unknown} 个位置未识别；请确认游戏 HUD 可见及采样框位置。";
            if (!hotkeysOk) status.Text += "\n部分快捷键被占用，请用窗口按钮。";
        }
        catch (Exception e)
        {
            ResetStates();
            status.Text = "无法读取画面：" + e.Message;
        }
    }
    internal static string StateText(QteState state) => state switch { QteState.Ready => "就绪", QteState.Cooling => "冷却中", _ => "未识别" };
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && !calibrating)
        {
            switch (m.WParam.ToInt32()) { case 1: ToggleEditing(); break; case 2: ToggleRunning(); break; case 3: ToggleOverlay(); break; }
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); timer.Dispose(); Save();
        for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(Handle, i);
        tray.Visible = false; tray.Dispose(); preview.Image?.Dispose(); overlay.Close(); overlay.Dispose(); Icon?.Dispose();
        base.OnFormClosed(e);
    }
}
