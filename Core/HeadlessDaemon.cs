using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace MiniFanControl
{
    /// <summary>
    /// Zero-UI headless background fan control daemon for 24/7 servers, local LLM hosts,
    /// and low-overhead Scheduled Task / startup execution.
    /// Uses the exact same FanControllerService safety state machine (anti-stall floor clamping,
    /// 90°C thermal tripwire, sleep/resume protection, and optional anti-cheat safe pause).
    /// </summary>
    public static class HeadlessDaemon
    {
        private delegate bool ConsoleCtrlHandlerDelegate(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        private const uint CTRL_C_EVENT = 0;
        private const uint CTRL_BREAK_EVENT = 1;
        private const uint CTRL_CLOSE_EVENT = 2;
        private const uint CTRL_LOGOFF_EVENT = 5;
        private const uint CTRL_SHUTDOWN_EVENT = 6;

        /// <summary>
        /// Polls the anti-cheat state machine from WorkerIterationCompleted (which continues firing
        /// even while FanControllerService is paused, preventing pause/resume deadlocks).
        /// </summary>
        internal static AntiCheatAction PollAntiCheatOnWorkerTick(
            AntiCheatMonitor antiCheat,
            FanControllerService controller,
            bool isSuspended,
            Action pauseHandoff,
            Action<bool> resumeAction)
        {
            if (antiCheat == null || !antiCheat.Enabled || isSuspended) return AntiCheatAction.None;

            AntiCheatAction act = antiCheat.Poll();
            if (act == AntiCheatAction.PauseForAntiCheat)
            {
                if (controller != null) controller.PauseForSuspend();
                if (pauseHandoff != null) pauseHandoff();
            }
            else if (act == AntiCheatAction.ResumeAfterAntiCheat)
            {
                if (resumeAction != null) resumeAction(true);
                else if (controller != null) controller.ResumeFromSuspend(true);
            }
            return act;
        }
        public static FanCurveProfile[] CreateBuiltInProfiles()
        {
            return FanCurveProfile.CreateBuiltInProfiles();
        }

        public static FanCurveProfile ResolveProfileByName(string name, FanCurveProfile[] builtIns, List<FanCurveProfile> customs)
        {
            if (!string.IsNullOrEmpty(name))
            {
                if (builtIns != null)
                {
                    for (int i = 0; i < builtIns.Length; i++)
                    {
                        if (builtIns[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return builtIns[i];
                    }
                }
                if (customs != null)
                {
                    for (int i = 0; i < customs.Count; i++)
                    {
                        if (customs[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return customs[i];
                    }
                }
            }
            return (builtIns != null && builtIns.Length > 0) ? builtIns[0] : null;
        }

        public static int Run(bool demoMode, int intervalOverrideMs, int maxTicks, bool quiet)
        {
            return Run(demoMode, intervalOverrideMs, maxTicks, quiet, null, null);
        }

        public static int Run(
            bool demoMode,
            int intervalOverrideMs,
            int maxTicks,
            bool quiet,
            ManualResetEvent externalStopEvent,
            Action<Action, Action> registerServicePowerCallbacks)
        {
            SystemHardwareInfo sysInfo = SystemHardwareInfo.Detect();
            HardwareBackend hw = null;
            DemoBackend demo = new DemoBackend();

            if (!demoMode)
            {
                hw = HardwareBackend.CreateAutoDetected();
                if (hw == null)
                {
                    Console.Error.WriteLine("[DAEMON ERROR] Hardware initialization failed.");
                    return 1;
                }
            }

            AppSettings settings = new AppSettings();
            List<FanCurveProfile> customProfiles = FanCurveProfile.CreateProtectedExamples();
            settings.Load(sysInfo, hw, customProfiles);

            FanCurveProfile[] builtIns = CreateBuiltInProfiles();
            FanCurveProfile apuProf = ResolveProfileByName(settings.ApuModeName, builtIns, customProfiles);
            FanCurveProfile sysProf = ResolveProfileByName(settings.SysModeName, builtIns, customProfiles);

            int pollMs = (intervalOverrideMs >= 100 && intervalOverrideMs <= 10000)
                ? intervalOverrideMs
                : Math.Max(250, Math.Min(10000, settings.RefreshIntervalMs));

            AntiCheatMonitor antiCheat = new AntiCheatMonitor();
            antiCheat.Enabled = settings.AutoPauseForAntiCheat;

            WorkloadProfileSwitcher workloadSwitcher = new WorkloadProfileSwitcher();
            workloadSwitcher.Enabled = settings.AutoSwitchWorkloads;
            workloadSwitcher.Rules.AddRange(settings.WorkloadRules);

            bool ownsStopEvent = (externalStopEvent == null);
            ManualResetEvent stopEvent = externalStopEvent ?? new ManualResetEvent(false);
            int completedTicks = 0;
            int isSuspended = 0;
            int safeAutoRestored = 0;

            Action restoreAutoOnce = delegate
            {
                if (!settings.RestoreAutoOnExit) return;
                if (Interlocked.Exchange(ref safeAutoRestored, 1) != 0) return;
                if (demoMode)
                {
                    try { demo.SetAuto(); } catch { }
                }
                else if (hw != null)
                {
                    try { hw.SetAuto(); } catch { }
                }
            };

            using (FanControllerService controller = new FanControllerService(hw, demo, builtIns, demoMode))
            {
                controller.RefreshIntervalMs = pollMs;
                controller.HasDualFans = settings.HasDualFans;
                if (apuProf != null) controller.SetProfileTarget(apuProf, false);
                if (sysProf != null) controller.SetProfileTarget(sysProf, true);
                if (settings.BiosPowerMode >= 0 && settings.BiosPowerMode <= 2)
                {
                    controller.BiosPowerModeOverride = settings.BiosPowerMode;
                }

                ConsoleCancelEventHandler cancelHandler = delegate(object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    stopEvent.Set();
                };
                Console.CancelKeyPress += cancelHandler;

                ConsoleCtrlHandlerDelegate ctrlHandler = delegate(uint ctrlType)
                {
                    if (ctrlType == CTRL_CLOSE_EVENT || ctrlType == CTRL_LOGOFF_EVENT || ctrlType == CTRL_SHUTDOWN_EVENT)
                    {
                        try { controller.PauseForSuspend(); } catch { }
                        restoreAutoOnce();
                        try { stopEvent.Set(); } catch { }
                    }
                    else if (ctrlType == CTRL_C_EVENT || ctrlType == CTRL_BREAK_EVENT)
                    {
                        try { stopEvent.Set(); } catch { }
                        return true;
                    }
                    return false;
                };
                bool ctrlHandlerRegistered = false;
                try { ctrlHandlerRegistered = SetConsoleCtrlHandler(ctrlHandler, true); } catch { }

                EventHandler processExitHandler = delegate(object sender, EventArgs e)
                {
                    restoreAutoOnce();
                };
                AppDomain.CurrentDomain.ProcessExit += processExitHandler;

                Action handleSuspend = delegate
                {
                    Interlocked.Exchange(ref isSuspended, 1);
                    controller.PauseForSuspend();
                    if (!demoMode && hw != null)
                    {
                        try { hw.SetAuto(); } catch { }
                    }
                };
                Action handleResume = delegate
                {
                    Interlocked.Exchange(ref isSuspended, 0);
                    Interlocked.Exchange(ref safeAutoRestored, 0);
                    controller.ResumeFromSuspend(!demoMode);
                };

                if (registerServicePowerCallbacks != null)
                {
                    registerServicePowerCallbacks(handleSuspend, handleResume);
                }

                PowerModeChangedEventHandler powerHandler = delegate(object sender, PowerModeChangedEventArgs e)
                {
                    if (e.Mode == PowerModes.Suspend) handleSuspend();
                    else if (e.Mode == PowerModes.Resume) handleResume();
                };
                try { SystemEvents.PowerModeChanged += powerHandler; } catch { }

                controller.SampleReceived += delegate(FanStatus st)
                {
                    int tick = Interlocked.Increment(ref completedTicks);
                    if (!quiet && (tick == 1 || tick % 10 == 0 || maxTicks > 0))
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "[DAEMON #{0}] CPU={1}C iGPU={2}C APU={3}RPM({4}%) SYS={5}RPM({6}%) Mode={7}/{8}{9}",
                            tick, st.CpuTemp, st.GpuTemp, st.Fan1Rpm, st.Fan1Pct, st.Fan2Rpm, st.Fan2Pct,
                            apuProf != null ? apuProf.Name : "Balanced",
                            sysProf != null ? sysProf.Name : "Balanced",
                            antiCheat.IsPaused ? " [ANTI-CHEAT PAUSED]" : ""));
                    }

                    string wsMsg;
                    WorkloadSwitchAction wsAct = AutomationCoordinator.EvaluateAndApplyWorkloadSwitch(
                        workloadSwitcher,
                        controller,
                        st,
                        Volatile.Read(ref isSuspended) != 0,
                        antiCheat.IsPaused,
                        delegate(string profName) { return ResolveProfileByName(profName, builtIns, customProfiles); },
                        ref apuProf,
                        ref sysProf,
                        out wsMsg);
                    if (wsAct != WorkloadSwitchAction.None && !quiet && !string.IsNullOrEmpty(wsMsg))
                    {
                        Console.WriteLine("[DAEMON] " + wsMsg);
                    }

                    if (maxTicks > 0 && tick >= maxTicks)
                    {
                        stopEvent.Set();
                    }
                };

                // Poll anti-cheat in WorkerIterationCompleted so it continues monitoring even while
                // FanControllerService is paused, allowing automatic resume once the game exits.
                controller.WorkerIterationCompleted += delegate
                {
                    if (!demoMode && antiCheat.Enabled)
                    {
                        PollAntiCheatOnWorkerTick(
                            antiCheat,
                            controller,
                            Volatile.Read(ref isSuspended) != 0,
                            delegate
                            {
                                if (!quiet) Console.WriteLine("[DAEMON] Anti-cheat detected (" + antiCheat.ActiveAntiCheatName + ") — restoring BIOS Auto and unloading legacy driver.");
                                AntiCheatMonitor.ExecuteSafePauseHandoff(
                                    delegate { return hw != null && hw.SetAuto(true); },
                                    delegate { NativeInpOut.Shutdown(); },
                                    delegate(string svc) { return DriverServiceManager.StopDriverService(svc); },
                                    NativeInpOut.ActiveDriver);
                            },
                            delegate(bool reinit)
                            {
                                if (!quiet) Console.WriteLine("[DAEMON] Anti-cheat exited — resuming hardware fan control.");
                                controller.ResumeFromSuspend(reinit);
                            });
                    }
                };

                if (!quiet)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "MiniFanControl v{0} Headless Daemon started ({1}, poll={2}ms, APU='{3}', Sys='{4}', AntiCheatAutoPause={5}). Press Ctrl+C to stop.",
                        MainForm.APP_VERSION,
                        demoMode ? "DEMO" : (hw != null && hw.Profile != null ? hw.Profile.DisplayName : "Hardware"),
                        pollMs,
                        apuProf != null ? apuProf.Name : "Balanced",
                        sysProf != null ? sysProf.Name : "Balanced",
                        settings.AutoPauseForAntiCheat ? "ON" : "OFF"));
                }

                controller.Start();
                stopEvent.WaitOne();
                controller.Stop(1000);

                try { SystemEvents.PowerModeChanged -= powerHandler; } catch { }
                AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
                if (ctrlHandlerRegistered)
                {
                    try { SetConsoleCtrlHandler(ctrlHandler, false); } catch { }
                }
                GC.KeepAlive(ctrlHandler);
                Console.CancelKeyPress -= cancelHandler;

                restoreAutoOnce();
            }

            if (!demoMode && hw != null)
            {
                try { hw.Dispose(); } catch { }
            }
            if (ownsStopEvent)
            {
                try { stopEvent.Close(); } catch { }
            }
            return 0;
        }
    }
}
