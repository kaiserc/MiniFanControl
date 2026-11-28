using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MiniFanControl
{
    public static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        // Public/internal forwarding facades preserved for unit tests and CLI/JSON callers
        public static string EscapeJson(string s)
        {
            return CliCommandRunner.EscapeJson(s);
        }

        public static string FormatStatusJson(FanStatus st, string mutexNamespace)
        {
            return CliCommandRunner.FormatStatusJson(st, mutexNamespace);
        }

        public static string FormatStatusJsonLine(FanStatus st, string mutexNamespace, DateTime utcNow)
        {
            return CliCommandRunner.FormatStatusJsonLine(st, mutexNamespace, utcNow);
        }

        public static int ClampCliFanDuty(int fanId, int requestedPct, bool force, out bool wasClamped, out int minFloor)
        {
            return CliCommandRunner.ClampCliFanDuty(fanId, requestedPct, force, out wasClamped, out minFloor);
        }

        internal static bool ShouldPreloadDriverForCommand(string cmd, bool demoMode)
        {
            return CliCommandRunner.ShouldPreloadDriverForCommand(cmd, demoMode);
        }

        internal static bool IsAnotherGuiInstanceRunning()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                Process[] procs = Process.GetProcessesByName(current.ProcessName);
                try
                {
                    for (int i = 0; i < procs.Length; i++)
                    {
                        if (procs[i].Id != current.Id)
                            return true;
                    }
                    return false;
                }
                finally
                {
                    for (int i = 0; i < procs.Length; i++)
                    {
                        try { procs[i].Dispose(); } catch { }
                    }
                }
            }
            catch
            {
                // Fail closed if process enumeration is denied
                return true;
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool demoMode = false;
            bool startMinimized = false;
            bool forceWrite = false;
            string cmd = null;
            string cmdArg1 = null;
            string cmdArg2 = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--demo" || a == "--simulate") demoMode = true;
                else if (a == "--minimized" || a == "--tray" || a == "-m") startMinimized = true;
                else if (a == "--force" || a == "-f") forceWrite = true;
                else if (cmd == null) cmd = a;
                else if (cmdArg1 == null) cmdArg1 = a;
                else if (cmdArg2 == null) cmdArg2 = a;
            }

            if (string.IsNullOrEmpty(cmd) && Process.GetCurrentProcess().ProcessName.IndexOf("helper", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cmd = "--help";
            }

            if (!string.IsNullOrEmpty(cmd))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                bool preloadedForCli = false;
                if (ShouldPreloadDriverForCommand(cmd, demoMode))
                {
                    NativeInpOut.PreloadDriverDll();
                    preloadedForCli = true;
                }

                try
                {
                    return CliCommandRunner.Run(cmd, cmdArg1, cmdArg2, args, demoMode, forceWrite);
                }
                finally
                {
                    if (preloadedForCli)
                    {
                        try { NativeInpOut.Shutdown(); } catch { }
                    }
                }
            }

            return RunGuiApplication(demoMode, startMinimized);
        }

        private static int RunGuiApplication(bool demoMode, bool startMinimized)
        {
#if NET5_0_OR_GREATER
            Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
            {
                try
                {
                    using (HardwareBackend hw = HardwareBackend.CreateAutoDetected()) { hw.SetAuto(); }
                }
                catch { }
                MessageBox.Show(
                    "An unexpected error occurred in MiniFanControl. Fans have been restored to BIOS Auto for hardware safety.\n\n" + e.Exception.Message,
                    "MiniFanControl - Safety Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    using (HardwareBackend hw = HardwareBackend.CreateAutoDetected()) { hw.SetAuto(); }
                }
                catch { }
            };

            // Single-Instance Guard: Prevent dual GUI instances from racing on hardware control loops
            const string SINGLE_INSTANCE_MUTEX_NAME = @"Global\MiniFanControl_SingleInstance_GUI";
            bool createdNew = false;
            Mutex singleInstanceMutex = null;
            try
            {
                singleInstanceMutex = new Mutex(true, SINGLE_INSTANCE_MUTEX_NAME, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
            catch
            {
                try
                {
                    singleInstanceMutex = new Mutex(true, @"Local\MiniFanControl_SingleInstance_GUI", out createdNew);
                }
                catch (Exception ex)
                {
                    WatchdogSupervisor.LogWatchdogEvent("Single-instance mutex fallback error: " + ex.Message);
                    createdNew = !IsAnotherGuiInstanceRunning();
                }
            }

            if (!createdNew)
            {
                SignalExistingInstanceToShow();
                return 0;
            }

            if (ShouldPreloadDriverForCommand(null, demoMode))
            {
                NativeInpOut.PreloadDriverDll();
            }

            try
            {
                Application.Run(new MainForm(demoMode, startMinimized));
                return 0;
            }
            finally
            {
                try { EtwLatencyMonitor.Shutdown(); } catch { }
                NativeInpOut.Shutdown();
                if (singleInstanceMutex != null)
                {
                    try { singleInstanceMutex.ReleaseMutex(); } catch { }
                    try { singleInstanceMutex.Close(); } catch { }
                }
            }
        }

        private static void SignalExistingInstanceToShow()
        {
            try
            {
                try
                {
                    using (EventWaitHandle showEvt = EventWaitHandle.OpenExisting(@"Global\MiniFanControl_ShowMainWindow"))
                        showEvt.Set();
                }
                catch
                {
                    try
                    {
                        using (EventWaitHandle showEvt = EventWaitHandle.OpenExisting(@"Local\MiniFanControl_ShowMainWindow"))
                            showEvt.Set();
                    }
                    catch { }
                }

                Process current = Process.GetCurrentProcess();
                Process[] procs = Process.GetProcessesByName(current.ProcessName);
                try
                {
                    bool activated = false;
                    foreach (Process p in procs)
                    {
                        if (!activated && p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(p.MainWindowHandle);
                            activated = true;
                        }
                    }
                }
                finally
                {
                    foreach (Process p in procs)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
