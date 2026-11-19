using System;
using System.IO;

namespace CpaTaskbar
{
    /// <summary>
    /// Appends to logs/desktop.log with the full exception. The same message repeating within 60
    /// seconds is counted rather than re-written, and the log rotates at 1 MB, so a failure that
    /// repeats every second cannot fill the disk. Never throws.
    /// </summary>
    static class AppLog
    {
        const long RepeatWindowMs = 60000, MaxBytes = 1024 * 1024;
        static readonly object gate = new object();
        static string lastKey = "";
        static long lastAt;
        static int suppressed;

        public static void Write(string message, Exception error = null)
        {
            try
            {
                lock (gate)
                {
                    string key = message + "|" + (error == null ? "" : error.GetType().FullName + ":" + error.Message);
                    long now = J.Now;
                    if (key == lastKey && now - lastAt < RepeatWindowMs)
                    {
                        suppressed++;
                        return;
                    }

                    string folder = Path.Combine(AppConfig.Folder, "logs");
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "desktop.log");
                    Rotate(file);

                    string stamp = DateTime.Now.ToString("s");
                    string text = "";
                    if (suppressed > 0)
                        text += stamp + " 上一条重复 " + suppressed + " 次" + Environment.NewLine;
                    text += stamp + " " + message + (error == null ? "" : Environment.NewLine + error) + Environment.NewLine;
                    File.AppendAllText(file, text);

                    lastKey = key;
                    lastAt = now;
                    suppressed = 0;
                }
            }
            catch
            {
            }
        }

        static void Rotate(string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length <= MaxBytes)
                return;
            string old = file + ".1";
            if (File.Exists(old))
                File.Delete(old);
            File.Move(file, old);
        }
    }
}
