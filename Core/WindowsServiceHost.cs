using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniFanControl
{
    /// <summary>
    /// Zero-dependency native Win32 Service Control Manager (SCM) host for running MiniFanControl's
    /// HeadlessDaemon as a first-class Windows Service with automatic restart-on-failure,
    /// clean SERVICE_CONTROL_PRESHUTDOWN / SHUTDOWN BIOS Auto restoration, and native
    /// SERVICE_CONTROL_POWEREVENT sleep/resume handling.
    /// Compatible with both .NET Framework 4.8 (csc.exe) and .NET 10.0-windows without NuGet packages.
    /// </summary>
    public static class WindowsServiceHost
    {
        public const string SERVICE_NAME = "MiniFanControlService";
        public const string SERVICE_DISPLAY_NAME = "MiniFanControl Fan Control Service";
        public const string SERVICE_DESCRIPTION = "Hardware EC / Super I/O fan curve and thermal safety controller for AMD Ryzen Mini PCs.";

        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
        private const uint SERVICE_ALL_ACCESS = 0xF01FF;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const uint SERVICE_AUTO_START = 0x00000002;
        private const uint SERVICE_ERROR_NORMAL = 0x00000001;

        private const uint SERVICE_STOPPED = 0x00000001;
        private const uint SERVICE_START_PENDING = 0x00000002;
        private const uint SERVICE_STOP_PENDING = 0x00000003;
        private const uint SERVICE_RUNNING = 0x00000004;

        private const uint SERVICE_ACCEPT_STOP = 0x00000001;
        private const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;
        private const uint SERVICE_ACCEPT_POWEREVENT = 0x00000040;
        private const uint SERVICE_ACCEPT_PRESHUTDOWN = 0x00000100;

        private const uint SERVICE_CONTROL_STOP = 0x00000001;
        private const uint SERVICE_CONTROL_INTERROGATE = 0x00000004;
        private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;
        private const uint SERVICE_CONTROL_POWEREVENT = 0x0000000D;
        private const uint SERVICE_CONTROL_PRESHUTDOWN = 0x0000000F;

        private const uint PBT_APMSUSPEND = 0x0004;
        private const uint PBT_APMRESUMESUSPEND = 0x0007;
        private const uint PBT_APMRESUMEAUTOMATIC = 0x0012;

        private const uint SERVICE_CONFIG_DESCRIPTION = 1;
        private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
        private const uint SC_ACTION_RESTART = 1;

        private const uint NO_ERROR = 0;
        private const uint ERROR_CALL_NOT_IMPLEMENTED = 120;

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private delegate void ServiceMainDelegate(uint argc, IntPtr argv);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint ServiceCtrlHandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_TABLE_ENTRY
        {
            public string lpServiceName;
            public IntPtr lpServiceProc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_DESCRIPTION_STRUCT
        {
            public string lpDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SC_ACTION
        {
            public uint Type;
            public uint Delay;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_FAILURE_ACTIONS
        {
            public uint dwResetPeriod;
            public string lpRebootMsg;
            public string lpCommand;
            public uint cActions;
            public IntPtr lpsaActions;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartServiceCtrlDispatcherW(SERVICE_TABLE_ENTRY[] lpServiceStartTable);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr RegisterServiceCtrlHandlerExW(string lpServiceName, ServiceCtrlHandlerExDelegate lpHandlerProc, IntPtr lpContext);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManagerW(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateServiceW(
            IntPtr hSCManager,
            string lpServiceName,
            string lpDisplayName,
            uint dwDesiredAccess,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string lpDependencies,
            string lpServiceStartName,
            string lpPassword);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr hService);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig2W(IntPtr hService, uint dwInfoLevel, IntPtr lpInfo);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

        private static IntPtr _statusHandle = IntPtr.Zero;
        private static SERVICE_STATUS _status;
        private static ManualResetEvent _stopEvent;
        private static Action _onSuspend;
        private static Action _onResume;
        private static ServiceMainDelegate _pinnedMain;
        private static ServiceCtrlHandlerExDelegate _pinnedHandler;
        private static bool _serviceDemoMode = false;
        private static int _serviceIntervalMs = 0;
        private static string _serviceConfigPath = null;

        /// <summary>
        /// Builds a strictly quoted ImagePath for SCM service registration to prevent
        /// Windows unquoted service path vulnerabilities.
        /// </summary>
        public static string BuildServiceBinaryPath(string executablePath)
        {
            return BuildServiceBinaryPath(executablePath, null);
        }

        public static string BuildServiceBinaryPath(string executablePath, string configPath)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return "";
            string full = Path.GetFullPath(executablePath.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                string fullCfg = Path.GetFullPath(configPath.Trim().Trim('"'));
                return string.Format("\"{0}\" service --config \"{1}\"", full, fullCfg);
            }
            return string.Format("\"{0}\" service", full);
        }

        public static bool IsServiceInstalled()
        {
            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            try
            {
                IntPtr svc = OpenServiceW(scm, SERVICE_NAME, 0x0004); // SERVICE_QUERY_STATUS
                if (svc == IntPtr.Zero) return false;
                CloseServiceHandle(svc);
                return true;
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static bool IsServiceRunning()
        {
            return DriverServiceManager.IsDriverServiceRunning(SERVICE_NAME);
        }

        public static bool InstallService(string executablePath, out string message)
        {
            return InstallService(executablePath, null, out message);
        }

        public static bool InstallService(string executablePath, string configPath, out string message)
        {
            string binPath = BuildServiceBinaryPath(executablePath, configPath);
            if (string.IsNullOrEmpty(binPath))
            {
                message = "Executable path is empty.";
                return false;
            }

            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
            if (scm == IntPtr.Zero)
            {
                message = "Failed to open SCM (run as Administrator). Win32 error: " + Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                IntPtr svc = CreateServiceW(
                    scm,
                    SERVICE_NAME,
                    SERVICE_DISPLAY_NAME,
                    SERVICE_ALL_ACCESS,
                    SERVICE_WIN32_OWN_PROCESS,
                    SERVICE_AUTO_START,
                    SERVICE_ERROR_NORMAL,
                    binPath,
                    null,
                    IntPtr.Zero,
                    null,
                    null, // LocalSystem
                    null);

                if (svc == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    message = (err == 1073)
                        ? "Service '" + SERVICE_NAME + "' is already installed."
                        : "CreateService failed (Win32 error " + err + ").";
                    return false;
                }

                try
                {
                    // Set service description
                    SERVICE_DESCRIPTION_STRUCT desc = new SERVICE_DESCRIPTION_STRUCT { lpDescription = SERVICE_DESCRIPTION };
                    IntPtr pDesc = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SERVICE_DESCRIPTION_STRUCT)));
                    try
                    {
                        Marshal.StructureToPtr(desc, pDesc, false);
                        ChangeServiceConfig2W(svc, SERVICE_CONFIG_DESCRIPTION, pDesc);
                    }
                    finally
                    {
                        Marshal.DestroyStructure(pDesc, typeof(SERVICE_DESCRIPTION_STRUCT));
                        Marshal.FreeHGlobal(pDesc);
                    }

                    // Configure SCM auto-recovery: restart after 5s on first 3 failures, reset count after 1 day
                    SC_ACTION[] actions = new SC_ACTION[]
                    {
                        new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 5000 },
                        new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 5000 },
                        new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 15000 }
                    };
                    int actSize = Marshal.SizeOf(typeof(SC_ACTION));
                    IntPtr pActions = Marshal.AllocHGlobal(actSize * actions.Length);
                    IntPtr pFail = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SERVICE_FAILURE_ACTIONS)));
                    try
                    {
                        for (int i = 0; i < actions.Length; i++)
                        {
                            Marshal.StructureToPtr(actions[i], new IntPtr(pActions.ToInt64() + i * actSize), false);
                        }
                        SERVICE_FAILURE_ACTIONS fail = new SERVICE_FAILURE_ACTIONS
                        {
                            dwResetPeriod = 86400,
                            lpRebootMsg = null,
                            lpCommand = null,
                            cActions = (uint)actions.Length,
                            lpsaActions = pActions
                        };
                        Marshal.StructureToPtr(fail, pFail, false);
                        ChangeServiceConfig2W(svc, SERVICE_CONFIG_FAILURE_ACTIONS, pFail);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pFail);
                        Marshal.FreeHGlobal(pActions);
                    }

                    message = "Installed Windows Service '" + SERVICE_NAME + "' (" + binPath + "). Start with: sc start " + SERVICE_NAME;
                    return true;
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static bool UninstallService(out string message)
        {
            DriverServiceManager.StopDriverService(SERVICE_NAME);
            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                message = "Failed to open SCM (run as Administrator). Win32 error: " + Marshal.GetLastWin32Error();
                return false;
            }
            try
            {
                IntPtr svc = OpenServiceW(scm, SERVICE_NAME, SERVICE_ALL_ACCESS);
                if (svc == IntPtr.Zero)
                {
                    message = "Service '" + SERVICE_NAME + "' is not installed.";
                    return false;
                }
                try
                {
                    if (!DeleteService(svc))
                    {
                        message = "DeleteService failed (Win32 error " + Marshal.GetLastWin32Error() + ").";
                        return false;
                    }
                    message = "Uninstalled Windows Service '" + SERVICE_NAME + "'.";
                    return true;
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static bool StartService(out string message, int waitTimeoutMs = 5000)
        {
            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
            {
                message = "Failed to open SCM (run as Administrator). Win32 error: " + Marshal.GetLastWin32Error();
                return false;
            }
            try
            {
                IntPtr svc = OpenServiceW(scm, SERVICE_NAME, SERVICE_START | SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero)
                {
                    message = "Service '" + SERVICE_NAME + "' is not installed.";
                    return false;
                }
                try
                {
                    if (!StartServiceW(svc, 0, IntPtr.Zero))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == ERROR_SERVICE_ALREADY_RUNNING)
                        {
                            message = "Service '" + SERVICE_NAME + "' is already running.";
                            return true;
                        }
                        message = "StartService failed (Win32 error " + err + ").";
                        return false;
                    }
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < waitTimeoutMs)
            {
                if (IsServiceRunning())
                {
                    message = "Started Windows Service '" + SERVICE_NAME + "'.";
                    return true;
                }
                Thread.Sleep(100);
            }
            message = "Start requested for '" + SERVICE_NAME + "' (still starting).";
            return IsServiceRunning();
        }

        public static bool StopService(out string message, int waitTimeoutMs = 5000)
        {
            if (!IsServiceInstalled())
            {
                message = "Service '" + SERVICE_NAME + "' is not installed.";
                return false;
            }
            if (!IsServiceRunning())
            {
                message = "Service '" + SERVICE_NAME + "' is already stopped.";
                return true;
            }
            bool ok = DriverServiceManager.StopDriverService(SERVICE_NAME);
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < waitTimeoutMs && IsServiceRunning())
            {
                Thread.Sleep(100);
            }
            bool stopped = !IsServiceRunning();
            message = stopped
                ? "Stopped Windows Service '" + SERVICE_NAME + "'."
                : (ok ? "Stop requested for '" + SERVICE_NAME + "' (stop pending)." : "Failed to stop '" + SERVICE_NAME + "'.");
            return stopped;
        }

        public static int RunAsService(bool demoMode, int intervalOverrideMs)
        {
            return RunAsService(demoMode, intervalOverrideMs, null);
        }

        public static int RunAsService(bool demoMode, int intervalOverrideMs, string configPath)
        {
            _serviceDemoMode = demoMode;
            _serviceIntervalMs = intervalOverrideMs;
            _serviceConfigPath = configPath;
            _pinnedMain = new ServiceMainDelegate(ServiceMain);

            SERVICE_TABLE_ENTRY[] table = new SERVICE_TABLE_ENTRY[]
            {
                new SERVICE_TABLE_ENTRY
                {
                    lpServiceName = SERVICE_NAME,
                    lpServiceProc = Marshal.GetFunctionPointerForDelegate(_pinnedMain)
                },
                new SERVICE_TABLE_ENTRY
                {
                    lpServiceName = null,
                    lpServiceProc = IntPtr.Zero
                }
            };

            bool ok = StartServiceCtrlDispatcherW(table);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Console.Error.WriteLine("[SERVICE] StartServiceCtrlDispatcher failed (Win32 " + err + "). Use 'service-install' then 'sc start " + SERVICE_NAME + "', or use 'daemon' for console mode.");
                return 1;
            }
            return 0;
        }

        private static void ReportStatus(uint state, uint exitCode = 0, uint waitHint = 0)
        {
            if (_statusHandle == IntPtr.Zero) return;
            _status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
            _status.dwCurrentState = state;
            _status.dwWin32ExitCode = exitCode;
            _status.dwWaitHint = waitHint;
            _status.dwControlsAccepted = (state == SERVICE_RUNNING)
                ? (SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_PRESHUTDOWN | SERVICE_ACCEPT_POWEREVENT)
                : 0;
            SetServiceStatus(_statusHandle, ref _status);
        }

        private static void ServiceMain(uint argc, IntPtr argv)
        {
            _pinnedHandler = new ServiceCtrlHandlerExDelegate(ServiceCtrlHandler);
            _statusHandle = RegisterServiceCtrlHandlerExW(SERVICE_NAME, _pinnedHandler, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero) return;

            ReportStatus(SERVICE_START_PENDING, 0, 5000);
            _stopEvent = new ManualResetEvent(false);

            try
            {
                WatchdogSupervisor.LogWatchdogEvent("Windows Service '" + SERVICE_NAME + "' starting.");
                ReportStatus(SERVICE_RUNNING);

                HeadlessDaemon.Run(
                    _serviceDemoMode,
                    _serviceIntervalMs,
                    0,
                    true,
                    _stopEvent,
                    delegate(Action onSusp, Action onRes)
                    {
                        _onSuspend = onSusp;
                        _onResume = onRes;
                    },
                    _serviceConfigPath);
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("Windows Service fatal error: " + ex.ToString());
                ReportStatus(SERVICE_STOPPED, 1);
                return;
            }
            finally
            {
                if (_stopEvent != null)
                {
                    try { _stopEvent.Close(); } catch { }
                    _stopEvent = null;
                }
                _onSuspend = null;
                _onResume = null;
            }

            WatchdogSupervisor.LogWatchdogEvent("Windows Service '" + SERVICE_NAME + "' stopped cleanly.");
            ReportStatus(SERVICE_STOPPED, 0);
        }

        private static uint ServiceCtrlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            switch (control)
            {
                case SERVICE_CONTROL_STOP:
                case SERVICE_CONTROL_SHUTDOWN:
                case SERVICE_CONTROL_PRESHUTDOWN:
                    ReportStatus(SERVICE_STOP_PENDING, 0, 5000);
                    if (_stopEvent != null)
                    {
                        try { _stopEvent.Set(); } catch { }
                    }
                    return NO_ERROR;

                case SERVICE_CONTROL_POWEREVENT:
                    if (eventType == PBT_APMSUSPEND)
                    {
                        if (_onSuspend != null) { try { _onSuspend(); } catch { } }
                    }
                    else if (eventType == PBT_APMRESUMEAUTOMATIC || eventType == PBT_APMRESUMESUSPEND)
                    {
                        if (_onResume != null) { try { _onResume(); } catch { } }
                    }
                    return NO_ERROR;

                case SERVICE_CONTROL_INTERROGATE:
                    return NO_ERROR;

                default:
                    return ERROR_CALL_NOT_IMPLEMENTED;
            }
        }
    }
}
