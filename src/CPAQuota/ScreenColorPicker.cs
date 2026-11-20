using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Eyedropper: a frozen screenshot of all monitors with a magnifier at the cursor. Left click
    /// picks the pixel; right click or Esc cancels. Arrow keys move the cursor by one pixel.
    /// </summary>
    public sealed class ScreenColorPicker : Form
    {
        const int Zoom = 10, Cells = 11;

        readonly Bitmap capture;
        readonly Rectangle screen;
        Point cursor;

        public Color SelectedColor
        {
            get; private set;
        }

        ScreenColorPicker(Bitmap capture, Rectangle screen)
        {
            this.capture = capture;
            this.screen = screen;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Bounds = screen;
            Text = "取色器";
            AccessibleName = "屏幕取色器，单击选取颜色，Esc取消";
        }

        /// <summary>Lets the user pick a colour anywhere on screen; null when cancelled.</summary>
        public static Color? Pick(IWin32Window owner)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            using (var capture = new Bitmap(screen.Width, screen.Height))
            {
                using (var g = Graphics.FromImage(capture))
                    g.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);
                using (var picker = new ScreenColorPicker(capture, screen))
                {
                    if (picker.ShowDialog(owner) != DialogResult.OK)
                        return null;
                    return picker.SelectedColor;
                }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = screen;
            cursor = PointToClient(Control.MousePosition);
            Activate();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MoveTo(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                MoveTo(e.Location);
                Accept();
            }
            else if (e.Button == MouseButtons.Right)
                DialogResult = DialogResult.Cancel;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    DialogResult = DialogResult.Cancel;
                    return true;
                case Keys.Enter:
                case Keys.Space:
                    Accept();
                    return true;
                case Keys.Left:
                    Nudge(-1, 0);
                    return true;
                case Keys.Right:
                    Nudge(1, 0);
                    return true;
                case Keys.Up:
                    Nudge(0, -1);
                    return true;
                case Keys.Down:
                    Nudge(0, 1);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(capture, 0, 0);
            int size = Zoom * Cells;
            var lens = LensBounds(size);

            // Magnified pixels, crisp.
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var source = new Rectangle(cursor.X - Cells / 2, cursor.Y - Cells / 2, Cells, Cells);
            using (var back = new SolidBrush(Color.Black))
                g.FillRectangle(back, lens.X - 2, lens.Y - 2, size + 4, size + 30);
            g.DrawImage(capture, new Rectangle(lens.X, lens.Y, size, size), source, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            var center = new Rectangle(lens.X + Cells / 2 * Zoom, lens.Y + Cells / 2 * Zoom, Zoom - 1, Zoom - 1);
            g.DrawRectangle(Pens.White, center);
            g.DrawRectangle(Pens.Black, Rectangle.Inflate(center, 1, 1));

            Color color = ColorAt(cursor);
            var swatch = new Rectangle(lens.X + 2, lens.Y + size + 6, 16, 16);
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, swatch);
            g.DrawRectangle(Pens.White, swatch);
            using (var font = Look.Font(9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Hex(color), font, new Rectangle(swatch.Right + 6, swatch.Top - 2, size - 26, 20), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        public static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        Rectangle LensBounds(int size)
        {
            // Bottom-right of the cursor, flipped near the edges of the monitor.
            var area = Screen.FromPoint(PointToScreen(cursor)).Bounds;
            area.Offset(-screen.X, -screen.Y);
            int x = cursor.X + 20, y = cursor.Y + 20;
            if (x + size + 4 > area.Right)
                x = cursor.X - 20 - size;
            if (y + size + 30 > area.Bottom)
                y = cursor.Y - 20 - size - 28;
            return new Rectangle(x, y, size, size);
        }

        /// <summary>The magnifier with its frame and colour label.</summary>
        Rectangle PaintedLens()
        {
            int size = Zoom * Cells;
            var lens = LensBounds(size);
            return new Rectangle(lens.X - 4, lens.Y - 4, size + 8, size + 34);
        }

        Color ColorAt(Point p)
        {
            int x = Math.Max(0, Math.Min(capture.Width - 1, p.X)), y = Math.Max(0, Math.Min(capture.Height - 1, p.Y));
            Color c = capture.GetPixel(x, y);
            return Color.FromArgb(255, c.R, c.G, c.B);
        }

        void MoveTo(Point p)
        {
            if (p == cursor)
                return;
            // Repaint only the old and new magnifier, not the whole screenshot.
            Invalidate(PaintedLens());
            cursor = p;
            Invalidate(PaintedLens());
        }

        void Nudge(int dx, int dy)
        {
            System.Windows.Forms.Cursor.Position = PointToScreen(new Point(cursor.X + dx, cursor.Y + dy));
            MoveTo(new Point(cursor.X + dx, cursor.Y + dy));
        }

        void Accept()
        {
            SelectedColor = ColorAt(cursor);
            DialogResult = DialogResult.OK;
        }
    }
}
