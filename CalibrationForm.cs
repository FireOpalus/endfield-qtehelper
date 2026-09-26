using System.Drawing.Drawing2D;

namespace EndfieldQteHelper;

public sealed class CalibrationForm : Form
{
    private readonly Bitmap screenshot;
    private readonly List<Rectangle> selected = [];
    private Point start, pointer;
    private bool drawing;
    public Rectangle[] Result => selected.ToArray();

    public CalibrationForm(Bitmap screenshot, Rectangle bounds)
    {
        this.screenshot = screenshot;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
    }
    private Rectangle Selection => Rectangle.FromLTRB(Math.Min(start.X, pointer.X), Math.Min(start.Y, pointer.Y),
        Math.Max(start.X, pointer.X), Math.Max(start.Y, pointer.Y));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(screenshot, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(225, 20, 25, 35));
        g.FillRectangle(shade, 20, 20, Math.Min(880, Width - 40), 118);
        using var font = new Font("Microsoft YaHei UI", 13);
        g.DrawString($"校准 {Math.Min(selected.Count + 1, 4)} / 4：从左到右，框选蓝色血条正上方的细进度条", font, Brushes.White, 36, 33);
        g.DrawString("只框进度条内部，包含完整宽度；不要包含头像、边框或蓝色血条。", font, Brushes.White, 36, 68);
        g.DrawString(selected.Count == 4 ? "已选完：Enter 保存 · Backspace 重选最后一条 · Esc 取消" : "右上角放大镜辅助定位 · Backspace 撤销 · Esc 取消", font, Brushes.LightGreen, 36, 103);
        using var pen = new Pen(Color.Lime, 1);
        for (int i = 0; i < selected.Count; i++)
        {
            g.DrawRectangle(pen, selected[i]);
            g.DrawString((i + 1).ToString(), font, Brushes.Lime, selected[i].X, selected[i].Y - 28);
        }
        if (drawing) g.DrawRectangle(Pens.Yellow, Selection);
        if (pointer != Point.Empty)
        {
            var source = new Rectangle(Math.Clamp(pointer.X - 35, 0, Math.Max(0, Width - 70)), Math.Clamp(pointer.Y - 18, 0, Math.Max(0, Height - 36)), 70, 36);
            var dest = new Rectangle(Width - 460, 160, 420, 216);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(screenshot, dest, source, GraphicsUnit.Pixel);
            float cx = dest.X + (pointer.X - source.X) * 6, cy = dest.Y + (pointer.Y - source.Y) * 6;
            g.DrawLine(Pens.Red, cx, dest.Top, cx, dest.Bottom);
            g.DrawLine(Pens.Red, dest.Left, cy, dest.Right, cy);
            g.DrawRectangle(Pens.White, dest);
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && selected.Count < 4) { drawing = true; start = pointer = e.Location; Capture = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e) { pointer = new Point(Math.Clamp(e.X, 0, Width - 1), Math.Clamp(e.Y, 0, Height - 1)); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!drawing) return;
        drawing = false;
        Capture = false;
        if (Selection.Width >= 8 && Selection.Height >= 1) selected.Add(Selection);
        Invalidate();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        if (e.KeyCode == Keys.Back && selected.Count > 0) { selected.RemoveAt(selected.Count - 1); Invalidate(); }
        if (e.KeyCode == Keys.Enter && selected.Count == 4) { DialogResult = DialogResult.OK; Close(); }
    }
}
