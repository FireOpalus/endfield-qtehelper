using System.Drawing.Drawing2D;

namespace EndfieldQteHelper;

public sealed class OverlayForm : Form
{
    private readonly Settings settings;
    private QteState[] states = new QteState[4];
    private double[] progress = new double[4];
    private bool editing;
    private Point dragStart, windowStart;
    public event Action? PositionChanged;
    public bool Editing
    {
        get => editing;
        set { editing = value; if (IsHandleCreated) RecreateHandle(); Invalidate(); }
    }
    public OverlayForm(Settings settings)
    {
        this.settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = .5;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ResizeOverlay();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; // layered, tool window, no activate
            if (!editing) p.ExStyle |= 0x20; // mouse pass-through
            return p;
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowDisplayAffinity(Handle, 0x11); // avoid reading our own marker in desktop capture
    }
    public void ResizeOverlay() => Size = new Size(176 * settings.OverlayScale / 100, 40 * settings.OverlayScale / 100);
    public void Place(Rectangle monitor)
    {
        ResizeOverlay();
        Location = new Point(Math.Clamp(monitor.X + (int)(monitor.Width * settings.OverlayX) - Width / 2, monitor.Left, Math.Max(monitor.Left, monitor.Right - Width)),
            Math.Clamp(monitor.Y + (int)(monitor.Height * settings.OverlayY) - Height / 2, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - Height)));
    }
    public void SetStates(QteState[] value, double[]? completed = null)
    {
        if (value.Length != 4 || (completed is not null && completed.Length != 4)) throw new ArgumentException("Exactly four slots are required.");
        states = value.ToArray();
        progress = completed?.Select(p => double.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0).ToArray() ?? new double[4];
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(settings.OverlayScale / 100f, settings.OverlayScale / 100f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var number = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            var circle = new RectangleF(6 + i * 44, 4, 32, 32);
            var color = states[i] switch { QteState.Ready => Color.FromArgb(115, 255, 185), QteState.Cooling => Color.FromArgb(160, 173, 190), _ => Color.FromArgb(255, 198, 100) };
            using var accent = new SolidBrush(color);
            if (states[i] == QteState.Cooling)
            {
                using var remaining = new SolidBrush(Color.FromArgb(75, 83, 96));
                g.FillEllipse(remaining, circle);
                if (progress[i] >= 1) g.FillEllipse(accent, circle);
                else if (progress[i] > 0) g.FillPie(accent, circle.X, circle.Y, circle.Width, circle.Height, -90, (float)(360 * progress[i]));
                var shadow = circle; shadow.Offset(1, 1);
                g.DrawString((i + 1).ToString(), number, Brushes.Black, shadow, format);
                g.DrawString((i + 1).ToString(), number, Brushes.White, circle, format);
            }
            else
            {
                g.FillEllipse(accent, circle);
                g.DrawString((i + 1).ToString(), number, Brushes.Black, circle, format);
            }
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (editing && e.Button == MouseButtons.Left) { dragStart = Cursor.Position; windowStart = Location; Capture = true; }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (editing && Capture) Location = new Point(windowStart.X + Cursor.Position.X - dragStart.X, windowStart.Y + Cursor.Position.Y - dragStart.Y);
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (Capture) { Capture = false; PositionChanged?.Invoke(); }
        base.OnMouseUp(e);
    }
}
