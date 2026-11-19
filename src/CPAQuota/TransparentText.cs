using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Crisp text on surfaces with per-pixel alpha. Layered windows cannot use ClearType, and GDI+
    /// grayscale text at fractional pixel sizes and half-pixel positions looks soft. The text is
    /// rendered with GDI (same hinting, grid fitting and layout as TextRenderer on an opaque
    /// surface) as white on black, and that coverage becomes the alpha of the requested colour, so
    /// glyph stems stay snapped to whole pixels.
    /// </summary>
    public static class TransparentText
    {
        public static void Draw(Graphics g, string text, Font font, Color color, Rectangle area, TextFormatFlags flags)
        {
            if (area.Width <= 0 || area.Height <= 0 || String.IsNullOrEmpty(text))
                return;
            int width = area.Width, height = area.Height;
            var bounds = new Rectangle(0, 0, width, height);
            int[] pixels = RenderCoverage(text, font, bounds, flags);
            if (!Colorize(pixels, color))
                return;
            using (var glyphs = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var data = glyphs.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
                }
                finally
                {
                    glyphs.UnlockBits(data);
                }
                var mode = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImageUnscaled(glyphs, area.X, area.Y);
                g.CompositingMode = mode;
            }
        }

        /// <summary>White-on-black GDI rendering of the text, as 0xRRGGBB pixels.</summary>
        static int[] RenderCoverage(string text, Font font, Rectangle bounds, TextFormatFlags flags)
        {
            int[] pixels = new int[bounds.Width * bounds.Height];
            using (var mask = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, text, font, bounds, Color.White, Color.Black, flags);
                }
                var data = mask.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < bounds.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bounds.Width, bounds.Width);
                }
                finally
                {
                    mask.UnlockBits(data);
                }
            }
            return pixels;
        }

        /// <summary>Turns coverage pixels into ARGB pixels of the colour in place. Returns false when nothing is visible.</summary>
        static bool Colorize(int[] pixels, Color color)
        {
            int rgb = color.ToArgb() & 0xFFFFFF;
            bool any = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                int p = pixels[i];
                int r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
                // ClearType renders per-subpixel coverage; combine it into one grayscale coverage value.
                int coverage = (r * 30 + g * 59 + b * 11) / 100;
                int alpha = coverage * color.A / 255;
                pixels[i] = alpha == 0 ? 0 : (alpha << 24) | rgb;
                if (alpha != 0)
                    any = true;
            }
            return any;
        }
    }
}
