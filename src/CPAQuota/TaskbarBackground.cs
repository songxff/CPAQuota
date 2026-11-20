using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CpaTaskbar
{
    /// <summary>
    /// The colour of the taskbar behind the panel, sampled from the screen for adaptive text
    /// colours. Windows 11 taskbars (Mica/acrylic, third-party skins) do not always match the
    /// registry theme, so the actual pixels decide whether the text should be dark or light.
    /// </summary>
    public static class TaskbarBackground
    {
        const int SampleEveryMs = 3000;

        static Color? color;
        static long sampledAt = long.MinValue;

        /// <summary>The last sampled background colour, or null before the first successful sample.</summary>
        public static Color? Current
        {
            get
            {
                return color;
            }
        }

        /// <summary>True for a dark background, null when nothing has been sampled yet.</summary>
        public static bool? Dark
        {
            get
            {
                return color.HasValue ? (bool?)IsDark(color.Value) : null;
            }
        }

        public static bool IsDark(Color c)
        {
            // Below ~0.18 white text has more contrast than black.
            return QuotaColors.Luminance(c) < .18;
        }

        /// <summary>
        /// Samples the screen area at most every 3 seconds (always when forced). Returns true when
        /// the dark/light decision changed. BitBlt without CAPTUREBLT leaves out layered windows, and
        /// the median pixel ignores the panel's own glyphs if they are captured.
        /// </summary>
        public static bool Sample(Rectangle screenArea, bool force = false)
        {
            long now = J.Now;
            if (!force && now - sampledAt < SampleEveryMs)
                return false;
            sampledAt = now;
            if (screenArea.Width <= 0 || screenArea.Height <= 0)
                return false;
            bool? before = Dark;
            try
            {
                Color? sampled = Median(screenArea);
                if (sampled.HasValue)
                    color = sampled;
            }
            catch
            {
                // Locked workstation or secure desktop: keep the previous sample.
            }
            return before != Dark;
        }

        static Color? Median(Rectangle area)
        {
            using (var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                var bounds = new Rectangle(0, 0, area.Width, area.Height);
                int[] pixels = new int[area.Width * area.Height];
                var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < area.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * area.Width, area.Width);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return MedianOf(pixels);
            }
        }

        /// <summary>The pixel with the median brightness (exposed for tests).</summary>
        public static Color? MedianOf(int[] pixels)
        {
            if (pixels == null || pixels.Length == 0)
                return null;
            var keys = new long[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                int p = pixels[i] & 0xFFFFFF;
                int brightness = ((p >> 16) & 255) * 299 + ((p >> 8) & 255) * 587 + (p & 255) * 114;
                keys[i] = ((long)brightness << 24) | (uint)p;
            }
            Array.Sort(keys);
            int rgb = (int)(keys[keys.Length / 2] & 0xFFFFFF);
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }
    }
}
