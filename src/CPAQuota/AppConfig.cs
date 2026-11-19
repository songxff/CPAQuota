using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CpaTaskbar
{
    /// <summary>
    /// User settings, persisted as JSON in data/desktop-settings.json. Field names are the JSON
    /// keys, so renaming a field breaks existing settings files.
    /// </summary>
    public class AppConfig
    {
        // Upstream quota endpoints check the client identity. These defaults can be updated in
        // data/desktop-settings.json when a provider stops accepting them, without rebuilding.
        public const string DefaultCodexUserAgent = "codex-tui/0.149.1 (Mac OS 26.5.2; arm64) iTerm.app/3.6.11 (codex-tui; 0.149.1)";
        public const string DefaultAntigravityUserAgent = "antigravity/hub/2.23.0 darwin/arm64";

        const string DefaultLowColor = "#50D39A", DefaultMediumColor = "#F1C553", DefaultHighColor = "#F17788";
        const string AutostartValueName = "CPAQuotaTaskbar";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Connection. Secrets are DPAPI ciphertext (see Protect).
        public string CpaUrl = "", CpaSecret = "";
        public bool CpampEnabled = false;
        public string CpampUrl = "", CpampSecret = "";
        public string CodexUserAgent = DefaultCodexUserAgent, AntigravityUserAgent = DefaultAntigravityUserAgent;

        // Accounts and metrics.
        public List<string> SelectedIds = new List<string>();
        public string FocusId = "", Model = "";
        /// <summary>single | average | sum</summary>
        public string QuotaDisplayMode = "single";
        /// <summary>off | time | countdown</summary>
        public string ResetTimeMode = "off";
        public bool ShowUsed = false, HistoryInBar = false, Rotate = false;
        public int RefreshSeconds = 120, HistorySeconds = 300, RotateSeconds = 12;

        // Appearance and startup.
        /// <summary>auto | dark | light</summary>
        public string Theme = "auto";
        public bool SingleLine = false, Autostart = false;
        public int Width = 152, Offset = 4, FontSize = 9;
        /// <summary>When false, Width and Offset are logical pixels scaled by the taskbar DPI (old settings).</summary>
        public bool PhysicalPixels = false;
        public bool TransparentBackground = true;
        /// <summary>thick | thin</summary>
        public string ProgressStyle = "thick";

        // Quota colours, chosen by used percentage.
        /// <summary>default | background | numbers | both</summary>
        public string ColorMode = "default";
        public int YellowUsedPercent = 50, RedUsedPercent = 90;
        public string LowUsageColor = DefaultLowColor, MediumUsageColor = DefaultMediumColor, HighUsageColor = DefaultHighColor;

        // ----- Storage -----

        /// <summary>Folder that holds data/ and logs/. Set by InitializeStorage.</summary>
        public static string Folder = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Set by Load when an unreadable settings file was moved aside; shown once at startup.</summary>
        public static string LoadWarning = "";

        public static string FileName
        {
            get
            {
                return Path.Combine(Folder, "data", "desktop-settings.json");
            }
        }

        /// <summary>
        /// Portable copies (portable.flag, or an existing data/desktop-settings.json next to the
        /// EXE from older versions) keep data beside the EXE; installed copies use the profile.
        /// </summary>
        public static string ResolveFolder(string executableFolder, string localDataFolder)
        {
            string folder = Path.GetFullPath(executableFolder);
            if (folder.Length > Path.GetPathRoot(folder).Length)
                folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool portable = File.Exists(Path.Combine(folder, "portable.flag"))
                || File.Exists(Path.Combine(folder, "data", "desktop-settings.json"));
            return portable ? folder : Path.Combine(localDataFolder, "CPAQuota");
        }

        public static void InitializeStorage()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Folder = ResolveFolder(AppDomain.CurrentDomain.BaseDirectory, profile);
        }

        /// <summary>
        /// Reads the settings file. A file that cannot be parsed is renamed to
        /// desktop-settings.json.corrupt-&lt;time&gt; and defaults are returned, with LoadWarning set.
        /// </summary>
        public static AppConfig Load()
        {
            LoadWarning = "";
            if (!File.Exists(FileName))
                return new AppConfig();
            try
            {
                var config = new JavaScriptSerializer().Deserialize<AppConfig>(File.ReadAllText(FileName, Encoding.UTF8));
                if (config != null)
                {
                    config.SelectedIds = config.SelectedIds ?? new List<string>();
                    if (String.IsNullOrWhiteSpace(config.CodexUserAgent))
                        config.CodexUserAgent = DefaultCodexUserAgent;
                    if (String.IsNullOrWhiteSpace(config.AntigravityUserAgent))
                        config.AntigravityUserAgent = DefaultAntigravityUserAgent;
                    config.NormalizeAppearance();
                    return config;
                }
            }
            catch (Exception error)
            {
                string backup = FileName + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    File.Move(FileName, backup);
                }
                catch (Exception moveError)
                {
                    throw new Exception("配置文件无法读取且无法备份：" + error.Message + "；" + moveError.Message);
                }
                LoadWarning = "配置文件无法读取（" + error.Message + "），已备份为 " + Path.GetFileName(backup) + "，将使用默认设置启动，请重新填写连接。";
            }
            return new AppConfig();
        }

        /// <summary>Writes atomically through a temporary file.</summary>
        public void Save()
        {
            NormalizeAppearance();
            Directory.CreateDirectory(Path.GetDirectoryName(FileName));
            string temp = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, J.Json(this), new UTF8Encoding(false));
            try
            {
                if (File.Exists(FileName))
                    File.Replace(temp, FileName, null);
                else
                    File.Move(temp, FileName);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public AppConfig Clone()
        {
            var copy = (AppConfig)MemberwiseClone();
            copy.SelectedIds = SelectedIds == null ? new List<string>() : SelectedIds.ToList();
            return copy;
        }

        /// <summary>Replaces unknown or inconsistent appearance values with defaults.</summary>
        public void NormalizeAppearance()
        {
            if (!new[] { "thick", "thin" }.Contains(ProgressStyle))
                ProgressStyle = "thick";
            if (!new[] { "single", "average", "sum" }.Contains(QuotaDisplayMode))
                QuotaDisplayMode = "single";
            if (!new[] { "off", "time", "countdown" }.Contains(ResetTimeMode))
                ResetTimeMode = "off";
            if (!new[] { "default", "background", "numbers", "both" }.Contains(ColorMode))
                ColorMode = "default";
            if (YellowUsedPercent < 1 || YellowUsedPercent >= RedUsedPercent || RedUsedPercent > 100)
            {
                YellowUsedPercent = 50;
                RedUsedPercent = 90;
            }
            if (!IsRgb(LowUsageColor))
                LowUsageColor = DefaultLowColor;
            if (!IsRgb(MediumUsageColor))
                MediumUsageColor = DefaultMediumColor;
            if (!IsRgb(HighUsageColor))
                HighUsageColor = DefaultHighColor;
        }

        static bool IsRgb(string value)
        {
            return value != null && value.Length == 7 && value[0] == '#'
                && value.Substring(1).All(ch => "0123456789ABCDEFabcdef".IndexOf(ch) >= 0);
        }

        // ----- Secrets -----

        public static string Protect(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }

        public static string Unprotect(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        public static bool CanUnprotect(string value)
        {
            try
            {
                Unprotect(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Unprotect with a user-facing message when the secret belongs to another user or machine.</summary>
        public static string Reveal(string value, string name)
        {
            try
            {
                return Unprotect(value);
            }
            catch (Exception error)
            {
                if (error is CryptographicException || error is FormatException)
                    throw new Exception("已保存的" + name + "无法用当前Windows账号解密（可能更换了用户或电脑），请重新输入。");
                throw;
            }
        }

        // ----- URLs -----

        /// <summary>Normalizes a CPA/CPAMP address to its base URL; rejects credentials and query strings.</summary>
        public static string Url(string value)
        {
            Uri uri;
            bool valid = Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri)
                && (uri.Scheme == "http" || uri.Scheme == "https")
                && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "";
            if (!valid)
                throw new Exception("服务地址须为HTTP/HTTPS地址，不能包含密钥或查询参数。");
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').Replace("/management.html", "").Replace("/v0/management", "");
        }

        /// <summary>True for plain-HTTP addresses outside loopback and private IPv4 ranges.</summary>
        public static bool IsPlainRemote(string url)
        {
            Uri uri;
            if (String.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "http")
                return false;
            if (uri.IsLoopback)
                return false;
            IPAddress ip;
            if (!IPAddress.TryParse(uri.Host, out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                return true;
            byte[] b = ip.GetAddressBytes();
            bool isPrivate = b[0] == 10
                || b[0] == 172 && b[1] >= 16 && b[1] <= 31
                || b[0] == 192 && b[1] == 168
                || b[0] == 169 && b[1] == 254;
            return !isPrivate;
        }

        // ----- Startup -----

        public void SetAutostart()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (Autostart)
                    key.SetValue(AutostartValueName, AutostartCommand(System.Windows.Forms.Application.ExecutablePath));
                else
                    key.DeleteValue(AutostartValueName, false);
            }
        }

        /// <summary>Removes the autostart entry only if it points at this executable.</summary>
        public static void RemoveAutostartFor(string executablePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key != null && String.Equals(key.GetValue(AutostartValueName) as string, AutostartCommand(executablePath), StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue(AutostartValueName, false);
            }
        }

        static string AutostartCommand(string executablePath)
        {
            return "\"" + executablePath + "\" --background";
        }
    }
}
