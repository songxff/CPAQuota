using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace CpaTaskbar
{
    /// <summary>
    /// Hidden window that receives commands from later launches of the EXE (open settings, open
    /// details, exit). Its caption is derived from the EXE path so each copy can be addressed.
    /// </summary>
    public sealed class CommandHost : NativeWindow, IDisposable
    {
        public static readonly int OpenMessage = Native.RegisterWindowMessage("CPAQuotaTaskbar.Open.Settings.v1");
        public static readonly int ExitMessage = Native.RegisterWindowMessage("CPAQuotaTaskbar.Exit.v1");
        public static readonly int DetailsMessage = Native.RegisterWindowMessage("CPAQuotaTaskbar.Open.Details.v1");

        readonly Action open, exit, details;

        public CommandHost(Action showSettings, Action close, Action showDetails)
        {
            open = showSettings;
            exit = close;
            details = showDetails;
            CreateHandle(new CreateParams { Caption = InstanceCaption, Style = Native.WS_POPUP, ExStyle = Native.WS_EX_TOOLWINDOW });
        }

        public static string InstanceCaption
        {
            get
            {
                return "CPAQuotaTaskbar." + J.Hash(Application.ExecutablePath.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Sends a command to the running copy of this EXE; when it cannot be found (another copy
        /// holds the single-instance mutex), broadcasts so that copy receives it.
        /// </summary>
        public static void Send(int message)
        {
            IntPtr host = Native.FindWindow(null, InstanceCaption);
            Native.PostMessage(host == IntPtr.Zero ? Native.HWND_BROADCAST : host, message, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Asks every running copy to exit.</summary>
        public static void BroadcastExit()
        {
            Native.PostMessage(Native.HWND_BROADCAST, ExitMessage, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Asks the running copy of this EXE to exit and waits for it, killing it after 8 seconds,
        /// so an uninstaller can remove the file without a files-in-use prompt.
        /// </summary>
        public static void ExitRunningCopy()
        {
            IntPtr host = Native.FindWindow(null, InstanceCaption);
            if (host == IntPtr.Zero)
                return;
            uint pid;
            Native.GetWindowThreadProcessId(host, out pid);
            Process running = null;
            try
            {
                if (pid != 0 && pid != (uint)Process.GetCurrentProcess().Id)
                    running = Process.GetProcessById((int)pid);
            }
            catch (ArgumentException)
            {
            }
            Native.PostMessage(host, ExitMessage, IntPtr.Zero, IntPtr.Zero);
            if (running == null)
                return;
            using (running)
            {
                if (running.WaitForExit(8000))
                    return;
                try
                {
                    running.Kill();
                    running.WaitForExit(2000);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == OpenMessage)
                open();
            else if (m.Msg == DetailsMessage)
                details();
            else if (m.Msg == ExitMessage)
                exit();
            else
                base.WndProc(ref m);
        }
    }
}
