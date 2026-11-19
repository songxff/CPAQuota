using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Entry point. Command-line switches:
    /// --details (open details), --background (autostart), --exit (close all copies),
    /// --uninstall-cleanup (used by the MSI), --inspect-shell / --inspect-storage &lt;file&gt; (diagnostics).
    /// </summary>
    static class Program
    {
        public static string Version
        {
            get
            {
                return typeof(Program).Assembly.GetName().Version.ToString(3);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch
            {
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length > 1 && args[0] == "--inspect-shell")
                {
                    File.WriteAllText(args[1], J.Json(ShellDiagnostics.Capture()));
                    return 0;
                }
                if (args.Length > 1 && args[0] == "--inspect-storage")
                {
                    InspectStorage(args[1]);
                    return 0;
                }
                if (args.Contains("--uninstall-cleanup"))
                {
                    CommandHost.ExitRunningCopy();
                    AppConfig.RemoveAutostartFor(Application.ExecutablePath);
                    return 0;
                }
                if (args.Contains("--exit"))
                {
                    CommandHost.BroadcastExit();
                    return 0;
                }
                return Run(args.Contains("--details"));
            }
            catch (Exception error)
            {
                AppLog.Write("启动失败", error);
                MessageBox.Show("CPA状态栏启动失败：" + error.Message, "CPA 状态栏", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>Runs the app, or forwards the request to the copy that already runs for this user.</summary>
        static int Run(bool openDetails)
        {
            bool created;
            string mutexName = @"Local\CPAQuotaTaskbar." + WindowsIdentity.GetCurrent().User.Value;
            using (var mutex = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    CommandHost.Send(openDetails ? CommandHost.DetailsMessage : CommandHost.OpenMessage);
                    return 0;
                }
                Application.ThreadException += (s, e) => AppLog.Write("界面发生异常", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Write("未处理异常", e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    AppLog.Write("后台任务异常", e.Exception);
                    e.SetObserved();
                };

                AppConfig.InitializeStorage();
                var config = AppConfig.Load();
                if (AppConfig.LoadWarning != "")
                {
                    AppLog.Write(AppConfig.LoadWarning);
                    MessageBox.Show(AppConfig.LoadWarning, "CPA 状态栏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                Application.Run(new TaskbarApp(config, openDetails));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static void InspectStorage(string output)
        {
            AppConfig.InitializeStorage();
            string folder = Path.GetFullPath(AppConfig.Folder).TrimEnd(Path.DirectorySeparatorChar);
            string exeFolder = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            File.WriteAllText(output, J.Json(new
            {
                folder = AppConfig.Folder,
                portable = String.Equals(folder, exeFolder, StringComparison.OrdinalIgnoreCase),
            }));
        }
    }
}
