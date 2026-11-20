using System;
using System.Drawing;
using System.Reflection;

namespace CpaTaskbar
{
    public static class TextColorTests
    {
        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        public static void Run(Action<string, Action> test)
        {
            test("文字颜色配置非法值恢复默认，自定义色号保留", () =>
            {
                var c = new AppConfig { TextColorMode = "rainbow", TextColor = "red" };
                c.NormalizeAppearance();
                Check(c.TextColorMode == "theme" && c.TextColor == AppConfig.DefaultTextColor, "invalid text colour reset");
                c = new AppConfig { TextColorMode = "custom", TextColor = "#1a2B3c" };
                c.NormalizeAppearance();
                Check(c.TextColorMode == "custom" && c.TextColor == "#1a2B3c", "custom text colour kept");
            });
            test("自定义文字颜色同时用于数值和标签，跟随主题保持原配色", () =>
            {
                Color foreground, muted;
                Look.TextColors(new AppConfig { TextColorMode = "custom", TextColor = "#102030" }, true, out foreground, out muted);
                Check(foreground.ToArgb() == Color.FromArgb(16, 32, 48).ToArgb() && muted.ToArgb() == foreground.ToArgb(), "custom colours");
                Look.TextColors(new AppConfig(), false, out foreground, out muted);
                Check(foreground.ToArgb() == Color.FromArgb(20, 23, 27).ToArgb() && muted.ToArgb() == Color.FromArgb(52, 55, 60).ToArgb(), "light theme colours");
            });
            test("背景采样取亮度中位数，少量文字像素不影响深浅判断", () =>
            {
                var pixels = new int[100];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = i < 20 ? 0xFFFFFF : 0xE6E1C8;
                var median = TaskbarBackground.MedianOf(pixels).Value;
                Check(median.R == 0xE6 && median.G == 0xE1 && median.B == 0xC8, "median pixel");
                Check(!TaskbarBackground.IsDark(median) && TaskbarBackground.IsDark(Color.FromArgb(32, 32, 32)), "dark threshold");
                Check(TaskbarBackground.MedianOf(new int[0]) == null, "empty sample");
            });
            test("自适应模式按采样背景决定深浅，非透明背景仍跟随主题", () =>
            {
                var field = typeof(TaskbarBackground).GetField("color", BindingFlags.Static | BindingFlags.NonPublic);
                object saved = field.GetValue(null);
                try
                {
                    field.SetValue(null, (Color?)Color.FromArgb(230, 225, 200));
                    var c = new AppConfig { Theme = "dark", TextColorMode = "adaptive", TransparentBackground = true };
                    Check(!Look.StripDark(c), "light taskbar gives dark text");
                    c.TransparentBackground = false;
                    Check(Look.StripDark(c), "opaque panel follows theme");
                    c.TransparentBackground = true;
                    c.TextColorMode = "theme";
                    Check(Look.StripDark(c), "theme mode ignores the sample");
                }
                finally
                {
                    field.SetValue(null, saved);
                }
            });
        }
    }
}
