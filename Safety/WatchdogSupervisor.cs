using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public static class WatchdogSupervisor
    {
        public const string HEARTBEAT_EVENT_PREFIX = "Global\\MiniFanControl_Heartbeat_";
        public const string SHUTDOWN_EVENT_PREFIX = "Global\\MiniFanControl_Shutdown_";
        public const int DEFAULT_TIMEOUT_MS = 3500;
        public const int DEFAULT_POLL_INTERVAL_MS = 250;

        public static string GetHeartbeatEventName(int pid)
        {
            return "MiniFanControl_Heartbeat_" + pid;
        }

        public static string GetShutdownEventName(int pid)
        {
            return "MiniFanControl_Shutdown_" + pid;
        }

        public static EventWaitHandle CreateOrOpenEvent(string baseName, EventResetMode mode, out bool createdNew)
        {
            createdNew = false;
            try
            {
                EventWaitHandleSecurity sec = new EventWaitHandleSecurity();
                sec.SetAccessRuleProtection(true, false); // Protect from inheritance and strip inherited Everyone (WorldSid) rules
                // Secure least-privilege ACL: Grant FullControl ONLY to current Windows user, Local Administrators, and SYSTEM
                // Explicitly avoiding WorldSid (Everyone) to prevent unauthorized disarming or spoofing from untrusted local processes
                SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    sec.AddAccessRule(new EventWaitHandleAccessRule(currentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                }
                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(adminSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(systemSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                return new EventWaitHandle(false, mode, @"Global\" + baseName, out createdNew, sec);
            }
            catch
            {
                try
                {
                    EventWaitHandleSecurity secLocal = new EventWaitHandleSecurity();
                    secLocal.SetAccessRuleProtection(true, false);
                    SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                    {
                        secLocal.AddAccessRule(new EventWaitHandleAccessRule(currentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                    }
                    SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                    secLocal.AddAccessRule(new EventWaitHandleAccessRule(adminSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                    return new EventWaitHandle(false, mode, @"Local\" + baseName, out createdNew, secLocal);
                }
                catch
                {
                    // Fail-closed: do NOT return an unnamed event that cannot be signaled across processes
                    return null;
                }
            }
        }

        public static void LogWatchdogEvent(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watchdog.log");
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [PID {1}] {2}{3}",
                    DateTime.Now, Process.GetCurrentProcess().Id, message, Environment.NewLine);
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        public static void RestoreAutoFailsafe(bool demoMode)
        {
            try
            {
                LogWatchdogEvent("Triggering emergency hardware failsafe -> restoring BIOS Auto fan control.");
                if (demoMode)
                {
                    Console.WriteLine("[WATCHDOG] Demo mode: Auto failsafe engaged.");
                    return;
                }

                using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())
                {
                    bool ok = hw.SetAuto();
                    if (ok)
                    {
                        string desc = hw.GetAutoRestoreDescription();
                        Console.WriteLine("[WATCHDOG] Hardware fan control restored: " + desc + ".");
                        LogWatchdogEvent("Restore verified by register read-back: " + desc);
                    }
                    else
                    {
                        Console.WriteLine("[WATCHDOG WARNING] SetAuto() verification did not confirm auto mode.");
                        LogWatchdogEvent("WARNING: SetAuto() verification did not confirm auto mode.");
                    }
                }
            }
            catch (Exception ex)
            {
                string err = "[WATCHDOG ERROR] Failed to restore BIOS Auto: " + ex.Message;
                Console.Error.WriteLine(err);
                LogWatchdogEvent(err);
            }
        }

        public static int Run(int parentPid, int timeoutMs = DEFAULT_TIMEOUT_MS, bool demoMode = false)
        {
            LogWatchdogEvent(string.Format("Supervisor started for parent PID {0} with timeout {1}ms", parentPid, timeoutMs));
            Console.WriteLine(string.Format("[WATCHDOG] Initializing supervisor for PID {0} (Timeout: {1}ms)...", parentPid, timeoutMs));

            Process parentProcess = null;
            try
            {
                parentProcess = Process.GetProcessById(parentPid);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[WATCHDOG] Warning: Parent PID {0} not found or exited: {1}", parentPid, ex.Message));
                if (!demoMode)
                {
                    RestoreAutoFailsafe(demoMode);
                }
                return 1;
            }

            EventWaitHandle heartbeatEvent = null;
            EventWaitHandle shutdownEvent = null;

            try
            {
                bool createdHb, createdSd;
                heartbeatEvent = CreateOrOpenEvent(GetHeartbeatEventName(parentPid), EventResetMode.AutoReset, out createdHb);
                shutdownEvent = CreateOrOpenEvent(GetShutdownEventName(parentPid), EventResetMode.ManualReset, out createdSd);

                if (heartbeatEvent == null || shutdownEvent == null)
                {
                    LogWatchdogEvent("FATAL: Failed to create or open named IPC events for parent PID " + parentPid + ". Watchdog supervisor aborting.");
                    Console.Error.WriteLine("[WATCHDOG ERROR] Failed to create or open named IPC events. Aborting.");
                    return 1;
                }

                return ExecuteLoop(parentProcess, parentPid, timeoutMs, heartbeatEvent, shutdownEvent, demoMode, false);
            }
            finally
            {
                if (heartbeatEvent != null) { try { heartbeatEvent.Close(); } catch { } }
                if (shutdownEvent != null) { try { shutdownEvent.Close(); } catch { } }
                if (parentProcess != null) { try { parentProcess.Dispose(); } catch { } }
            }
        }

        public static int RunSimulated(int parentPid, int timeoutMs, EventWaitHandle heartbeatEvent, EventWaitHandle shutdownEvent, bool initialShutdownSet)
        {
            Process currentProc = Process.GetCurrentProcess();
            if (initialShutdownSet)
            {
                shutdownEvent.Set();
            }
            return ExecuteLoop(currentProc, parentPid, timeoutMs, heartbeatEvent, shutdownEvent, true, true);
        }

        public static int ExecuteLoop(
            Process parentProcess,
            int parentPid,
            int timeoutMs,
            EventWaitHandle heartbeatEvent,
            EventWaitHandle shutdownEvent,
            bool demoMode,
            bool testMode)
        {
            if (heartbeatEvent == null || shutdownEvent == null)
            {
                return 1;
            }
            DateTime lastHeartbeatUtc = DateTime.UtcNow;
            bool triggeredFailsafe = false;

            while (true)
            {
                // 1. Check if parent process exited
                bool parentExited = false;
                try
                {
                    parentExited = parentProcess.HasExited;
                }
                catch
                {
                    parentExited = true;
                }

                if (parentExited)
                {
                    bool cleanShutdown = shutdownEvent.WaitOne(50);
                    if (cleanShutdown)
                    {
                        Console.WriteLine(string.Format("[WATCHDOG] Parent PID {0} exited gracefully. Terminating watchdog.", parentPid));
                        LogWatchdogEvent(string.Format("Parent PID {0} exited gracefully. Terminating watchdog.", parentPid));
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine(string.Format("[WATCHDOG TRIGGER] Parent PID {0} terminated unexpectedly! Restoring BIOS fan control...", parentPid));
                        LogWatchdogEvent(string.Format("Parent PID {0} terminated unexpectedly (crash/killed)! Restoring BIOS fan control...", parentPid));
                        RestoreAutoFailsafe(demoMode);
                        return 2;
                    }
                }

                // 2. Check for clean shutdown signal while process is still alive
                if (shutdownEvent.WaitOne(0))
                {
                    Console.WriteLine(string.Format("[WATCHDOG] Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    LogWatchdogEvent(string.Format("Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    return 0;
                }

                // 3. Wait on heartbeat with poll interval
                int pollWait = Math.Min(DEFAULT_POLL_INTERVAL_MS, Math.Max(20, timeoutMs / 3));
                if (heartbeatEvent.WaitOne(pollWait))
                {
                    lastHeartbeatUtc = DateTime.UtcNow;
                }
                else
                {
                    double elapsedMs = (DateTime.UtcNow - lastHeartbeatUtc).TotalMilliseconds;
                    if (elapsedMs >= timeoutMs && !triggeredFailsafe)
                    {
                        Console.WriteLine(string.Format("[WATCHDOG TRIGGER] Parent PID {0} heartbeat missed for {1:0}ms (Timeout: {2}ms)! Main thread hung. Restoring BIOS fan control...", parentPid, elapsedMs, timeoutMs));
                        LogWatchdogEvent(string.Format("Parent PID {0} heartbeat missed for {1:0}ms! Restoring BIOS fan control...", parentPid, elapsedMs));
                        RestoreAutoFailsafe(demoMode);
                        triggeredFailsafe = true;
                        if (testMode) return 3;
                    }
                }

                if (testMode && triggeredFailsafe)
                {
                    return 3;
                }
            }
        }
    }

}
