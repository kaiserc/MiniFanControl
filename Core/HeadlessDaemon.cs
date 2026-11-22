using System;
using System.Collections.Generic;
using System.Globalization;
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
        public static FanCurveProfile[] CreateBuiltInProfiles()
        {
            return new FanCurveProfile[]
            {
                new FanCurveProfile("Balanced", true, 0,
                    new CurvePoint[] { new CurvePoint(40, 0),  new CurvePoint(48, 35), new CurvePoint(65, 55), new CurvePoint(78, 80), new CurvePoint(88, 100) },
                    new CurvePoint[] { new CurvePoint(40, 35), new CurvePoint(55, 42), new CurvePoint(70, 52), new CurvePoint(80, 65), new CurvePoint(90, 80) }),
                new FanCurveProfile("Performance", true, 1,
                    new CurvePoint[] { new CurvePoint(38, 40), new CurvePoint(50, 55), new CurvePoint(62, 75), new CurvePoint(74, 92), new CurvePoint(84, 100) },
                    new CurvePoint[] { new CurvePoint(38, 45), new CurvePoint(52, 58), new CurvePoint(66, 72), new CurvePoint(78, 85), new CurvePoint(88, 100) }),
                new FanCurveProfile("Silent", true, 2,
                    new CurvePoint[] { new CurvePoint(48, 0),  new CurvePoint(56, 30), new CurvePoint(70, 45), new CurvePoint(82, 70), new CurvePoint(92, 100) },
                    new CurvePoint[] { new CurvePoint(45, 30), new CurvePoint(60, 34), new CurvePoint(72, 42), new CurvePoint(84, 52), new CurvePoint(92, 65) }),
                new FanCurveProfile("AI Workload", true, 3,
                    new CurvePoint[] { new CurvePoint(38, 35), new CurvePoint(48, 50), new CurvePoint(60, 70), new CurvePoint(72, 88), new CurvePoint(82, 100) },
                    new CurvePoint[] { new CurvePoint(35, 45), new CurvePoint(46, 60), new CurvePoint(58, 75), new CurvePoint(70, 90), new CurvePoint(80, 100) })
            };
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

            ManualResetEvent stopEvent = new ManualResetEvent(false);
            int completedTicks = 0;

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

                PowerModeChangedEventHandler powerHandler = delegate(object sender, PowerModeChangedEventArgs e)
                {
                    if (e.Mode == PowerModes.Suspend)
                    {
                        controller.PauseForSuspend();
                        if (!demoMode && hw != null)
                        {
                            try { hw.SetAuto(); } catch { }
                        }
                    }
                    else if (e.Mode == PowerModes.Resume)
                    {
                        controller.ResumeFromSuspend(!demoMode);
                    }
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

                    if (!demoMode && antiCheat.Enabled)
                    {
                        AntiCheatAction act = antiCheat.Poll();
                        if (act == AntiCheatAction.PauseForAntiCheat)
                        {
                            if (!quiet) Console.WriteLine("[DAEMON] Anti-cheat detected (" + antiCheat.ActiveAntiCheatName + ") — restoring BIOS Auto and unloading legacy driver.");
                            controller.PauseForSuspend();
                            AntiCheatMonitor.ExecuteSafePauseHandoff(
                                delegate { return hw != null && hw.SetAuto(); },
                                delegate { NativeInpOut.Shutdown(); },
                                delegate(string svc) { return DriverServiceManager.StopDriverService(svc); },
                                NativeInpOut.ActiveDriver);
                        }
                        else if (act == AntiCheatAction.ResumeAfterAntiCheat)
                        {
                            if (!quiet) Console.WriteLine("[DAEMON] Anti-cheat exited — resuming hardware fan control.");
                            controller.ResumeFromSuspend(true);
                        }
                    }

                    if (maxTicks > 0 && tick >= maxTicks)
                    {
                        stopEvent.Set();
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
                Console.CancelKeyPress -= cancelHandler;

                if (settings.RestoreAutoOnExit)
                {
                    if (demoMode)
                    {
                        demo.SetAuto();
                    }
                    else if (hw != null)
                    {
                        try { hw.SetAuto(); } catch { }
                    }
                }
            }

            if (!demoMode && hw != null)
            {
                try { hw.Dispose(); } catch { }
            }
            stopEvent.Close();
            return 0;
        }
    }
}
