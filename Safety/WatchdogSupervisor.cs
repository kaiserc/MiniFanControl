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
    public enum WatchdogIpcRole
    {
        Signaler,
        Waiter
    }

    public static class WatchdogSupervisor
    {
        public const string HEARTBEAT_EVENT_PREFIX = "MiniFanControl_Heartbeat_";
        public const string SHUTDOWN_EVENT_PREFIX = "MiniFanControl_Shutdown_";
        public const string READY_EVENT_PREFIX = "MiniFanControl_Ready_";
        public const int DEFAULT_TIMEOUT_MS = 3500;
        public const int DEFAULT_POLL_INTERVAL_MS = 250;

        public static string GetHeartbeatEventName(int pid)
        {
            return HEARTBEAT_EVENT_PREFIX + pid;
        }

        public static string GetShutdownEventName(int pid)
        {
            return SHUTDOWN_EVENT_PREFIX + pid;
        }

        public static string GetReadyEventName(int pid)
        {
            return READY_EVENT_PREFIX + pid;
        }

        public static EventWaitHandle CreateOrOpenEvent(string baseName, EventResetMode mode, out bool createdNew)
        {
            return CreateOrOpenEvent(baseName, mode, out createdNew, WatchdogIpcRole.Signaler);
        }

        public static EventWaitHandle CreateOrOpenEvent(string baseName, EventResetMode mode, out bool createdNew, WatchdogIpcRole role)
        {
            createdNew = false;
            // Waiting on an AutoReset event performs an atomic reset upon wait satisfaction.
            // Under Windows NT security, this operation requires EVENT_MODIFY_STATE (EventWaitHandleRights.Modify).
            // A pure Waiter on a ManualReset event only requires Synchronize.
            EventWaitHandleRights rights = (role == WatchdogIpcRole.Waiter && mode != EventResetMode.AutoReset)
                ? EventWaitHandleRights.Synchronize
                : (EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize);

            // If role is strictly Waiter, attempt to open existing event with scoped rights
            if (role == WatchdogIpcRole.Waiter)
            {
                try
                {
#if NET8_0_OR_GREATER
                    return EventWaitHandleAcl.OpenExisting(@"Global\" + baseName, rights);
#else
                    return EventWaitHandle.OpenExisting(@"Global\" + baseName, rights);
#endif
                }
                catch
                {
                    try
                    {
#if NET8_0_OR_GREATER
                        return EventWaitHandleAcl.OpenExisting(@"Local\" + baseName, rights);
#else
                        return EventWaitHandle.OpenExisting(@"Local\" + baseName, rights);
#endif
                    }
                    catch { }
                }
            }

            try
            {
                EventWaitHandleSecurity sec = BuildEventSecurity();

#if NET8_0_OR_GREATER
                return EventWaitHandleAcl.Create(false, mode, @"Global\" + baseName, out createdNew, sec);
#else
                return new EventWaitHandle(false, mode, @"Global\" + baseName, out createdNew, sec);
#endif
            }
            catch
            {
                try
                {
                    EventWaitHandleSecurity secLocal = BuildEventSecurity();

#if NET8_0_OR_GREATER
                    return EventWaitHandleAcl.Create(false, mode, @"Local\" + baseName, out createdNew, secLocal);
#else
                    return new EventWaitHandle(false, mode, @"Local\" + baseName, out createdNew, secLocal);
#endif
                }
                catch
                {
                    // Fail-closed: Never fall back to unsecured event without explicit ACL!
                    return null;
                }
            }
        }

        private static EventWaitHandleSecurity BuildEventSecurity()
        {
            EventWaitHandleSecurity sec;
            try
            {
                using (EventWaitHandle temp = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\MFC_SecSeed_" + Guid.NewGuid().ToString("N")))
                {
                    sec = temp.GetAccessControl();
                }
            }
            catch
            {
                sec = new EventWaitHandleSecurity();
                SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    sec.AddAccessRule(new EventWaitHandleAccessRule(currentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                }
                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                sec.AddAccessRule(new EventWaitHandleAccessRule(systemSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            }

            SecurityIdentifier worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            SecurityIdentifier appPackagesSid = new SecurityIdentifier("S-1-15-2-1");
            SecurityIdentifier authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            sec.PurgeAccessRules(worldSid);
            sec.PurgeAccessRules(appPackagesSid);
            sec.PurgeAccessRules(authUsers);
            sec.SetAccessRuleProtection(true, false);

            SecurityIdentifier currentUserSid = WindowsIdentity.GetCurrent().User;
            if (currentUserSid != null)
            {
                sec.AddAccessRule(new EventWaitHandleAccessRule(currentUserSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            }
            SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            sec.AddAccessRule(new EventWaitHandleAccessRule(adminSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            SecurityIdentifier sysSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            sec.AddAccessRule(new EventWaitHandleAccessRule(sysSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));

            return sec;
        }

        public static void LogWatchdogEvent(string message)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDir = !string.IsNullOrEmpty(localApp) ? Path.Combine(localApp, "MiniFanControl") : AppDomain.CurrentDomain.BaseDirectory;
                try { Directory.CreateDirectory(logDir); } catch { }
                string logPath = Path.Combine(logDir, "watchdog.log");
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [PID {1}] {2}{3}",
                    DateTime.Now, Process.GetCurrentProcess().Id, message, Environment.NewLine);
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        public static bool RestoreAutoFailsafe(bool demoMode)
        {
            try
            {
                LogWatchdogEvent("Triggering emergency hardware failsafe -> restoring BIOS Auto fan control.");
                if (demoMode)
                {
                    Console.WriteLine("[WATCHDOG] Demo mode: Auto failsafe engaged.");
                    return true;
                }

                using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())
                {
                    bool ok = hw.SetAuto();
                    if (ok)
                    {
                        string desc = hw.GetAutoRestoreDescription();
                        Console.WriteLine("[WATCHDOG] Hardware fan control restored: " + desc + ".");
                        LogWatchdogEvent("Restore verified by register read-back: " + desc);
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("[WATCHDOG WARNING] SetAuto() verification did not confirm auto mode.");
                        LogWatchdogEvent("WARNING: SetAuto() verification did not confirm auto mode.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                string err = "[WATCHDOG ERROR] Failed to restore BIOS Auto: " + ex.Message;
                Console.Error.WriteLine(err);
                LogWatchdogEvent(err);
                return false;
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
            EventWaitHandle readyEvent = null;

            try
            {
                bool createdHb, createdSd;
                heartbeatEvent = CreateOrOpenEvent(GetHeartbeatEventName(parentPid), EventResetMode.AutoReset, out createdHb, WatchdogIpcRole.Waiter);
                bool createdReady;
                shutdownEvent = CreateOrOpenEvent(GetShutdownEventName(parentPid), EventResetMode.ManualReset, out createdSd, WatchdogIpcRole.Waiter);
                readyEvent = CreateOrOpenEvent(GetReadyEventName(parentPid), EventResetMode.ManualReset, out createdReady, WatchdogIpcRole.Signaler);

                if (heartbeatEvent == null || shutdownEvent == null)
                {
                    LogWatchdogEvent("FATAL: Failed to create or open named IPC events for parent PID " + parentPid + ". Watchdog supervisor aborting.");
                    Console.Error.WriteLine("[WATCHDOG ERROR] Failed to create or open named IPC events. Aborting.");
                    return 1;
                }

                // Explicit handshake: Signal the parent GUI that IPC channels are verified and monitor loop is starting
                if (readyEvent != null)
                {
                    try { readyEvent.Set(); } catch { }
                }

                try
                {
                    return ExecuteLoop(parentProcess, parentPid, timeoutMs, heartbeatEvent, shutdownEvent, demoMode, false);
                }
                catch (Exception ex)
                {
                    LogWatchdogEvent("FATAL: Unhandled exception in watchdog supervisor loop: " + ex.ToString());
                    Console.Error.WriteLine("[WATCHDOG ERROR] Supervisor loop crashed: " + ex.Message);
                    RestoreAutoFailsafe(demoMode);
                    return 1;
                }
            }
            finally
            {
                if (readyEvent != null) { try { readyEvent.Close(); } catch { } }
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

                // 3. Simultaneously wait on heartbeat or shutdown signal for instantaneous termination
                int pollWait = Math.Min(DEFAULT_POLL_INTERVAL_MS, Math.Max(20, timeoutMs / 3));
                WaitHandle[] waitHandles = new WaitHandle[] { heartbeatEvent, shutdownEvent };
                int waitIndex = WaitHandle.WaitTimeout;
                try
                {
                    waitIndex = WaitHandle.WaitAny(waitHandles, pollWait);
                }
                catch (Exception ex)
                {
                    LogWatchdogEvent("WaitAny exception: " + ex.Message);
                    Thread.Sleep(pollWait);
                }

                if (waitIndex == 1) // Clean shutdown signaled immediately!
                {
                    Console.WriteLine(string.Format("[WATCHDOG] Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    LogWatchdogEvent(string.Format("Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    return 0;
                }
                else if (waitIndex == 0) // Heartbeat pulse received
                {
                    lastHeartbeatUtc = DateTime.UtcNow;
                    if (triggeredFailsafe)
                    {
                        triggeredFailsafe = false;
                        LogWatchdogEvent(string.Format("Parent PID {0} heartbeat resumed after timeout. Supervisor re-armed.", parentPid));
                        Console.WriteLine(string.Format("[WATCHDOG] Parent PID {0} heartbeat resumed. Supervisor re-armed.", parentPid));
                    }
                }
                else // Timeout: waitIndex == WaitHandle.WaitTimeout
                {
                    double elapsedMs = (DateTime.UtcNow - lastHeartbeatUtc).TotalMilliseconds;
                    if (elapsedMs >= timeoutMs && !triggeredFailsafe)
                    {
                        Console.WriteLine(string.Format("[WATCHDOG TRIGGER] Parent PID {0} heartbeat missed for {1:0}ms (Timeout: {2}ms)! Main thread hung. Restoring BIOS fan control...", parentPid, elapsedMs, timeoutMs));
                        LogWatchdogEvent(string.Format("Parent PID {0} heartbeat missed for {1:0}ms! Restoring BIOS fan control...", parentPid, elapsedMs));
                        bool restored = RestoreAutoFailsafe(demoMode);
                        if (restored || testMode)
                        {
                            triggeredFailsafe = true;
                        }
                        else
                        {
                            LogWatchdogEvent("Failsafe restore will retry on next poll interval due to mutex contention.");
                        }
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
