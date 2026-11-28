using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MiniFanControl
{
    /// <summary>
    /// Handles all CLI subcommands for minifan-helper.exe / MiniFanControl.exe
    /// (status/stream JSONL, fan/mode/auto control, diagnostics, calibration, daemon, Windows Service,
    /// driver inspection, validators, and self-tests), keeping Program.cs focused on process bootstrap.
    /// </summary>
    public static class CliCommandRunner
    {
        public static string EscapeJson(string s)
        {
            return SimpleJson.Escape(s);
        }

        public static string FormatStatusJson(FanStatus st, string mutexNamespace)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{{\"cpu_temp\":{0},\"gpu_temp\":{1},\"apu_power_w\":{2},\"fan1_rpm\":{3},\"fan2_rpm\":{4}," +
                "\"mode\":\"{5}\",\"mode_code\":{6},\"fan1_pct\":{7},\"fan2_pct\":{8}," +
                "\"fan1_manual\":{9},\"fan2_manual\":{10},\"nvme_temp\":{11},\"nvme_health\":{12}," +
                "\"ram_used_gb\":{13:0.0},\"ram_total_gb\":{14:0.0},\"ram_load_pct\":{15},\"ddr_sticks\":{16},\"ddr_temp1\":{17},\"ddr_temp2\":{18},\"ddr_spread\":{19},\"radeon_features\":\"{20}\",\"mutex_namespace\":\"{21}\",\"display_latency_ms\":{22:0.0},\"render_latency_ms\":{23:0.0},\"flip_mode\":\"{24}\",\"etw_active\":{25}}}",
                st.CpuTemp, st.GpuTemp, st.ApuPowerWatts, st.Fan1Rpm, st.Fan2Rpm,
                EscapeJson(st.Mode), st.ModeCode, st.Fan1Pct, st.Fan2Pct,
                st.Fan1Manual, st.Fan2Manual, st.NvmeTemp, st.NvmeHealthPct,
                st.RamUsedGb, st.RamTotalGb, st.RamLoadPct, st.DdrStickCount, st.DdrTemp1, st.DdrTemp2, st.DdrTempSpread,
                EscapeJson(st.RadeonFeaturesSummary ?? "None"), EscapeJson(mutexNamespace ?? ""),
                st.DisplayLatencyMs, st.RenderLatencyMs, EscapeJson(st.PresentFlipMode ?? ""), st.EtwLatencyActive ? "true" : "false");
        }

        public static string FormatStatusJsonLine(FanStatus st, string mutexNamespace, DateTime utcNow)
        {
            string baseJson = FormatStatusJson(st, mutexNamespace);
            string ts = utcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(baseJson) && baseJson.StartsWith("{"))
            {
                return "{\"ts_utc\":\"" + ts + "\"," + baseJson.Substring(1);
            }
            return baseJson;
        }

        public static int ClampCliFanDuty(int fanId, int requestedPct, bool force, out bool wasClamped, out int minFloor)
        {
            return FanControllerService.ClampManualFanDuty(fanId, requestedPct, force, out wasClamped, out minFloor);
        }

        internal static bool ShouldPreloadDriverForCommand(string cmd, bool demoMode)
        {
            if (demoMode) return false;
            if (string.IsNullOrEmpty(cmd)) return true; // GUI mode (after single-instance check)
            string c = cmd.Trim().ToLowerInvariant();
            if (c == "--help" || c == "-h" || c == "/?" || c == "help" ||
                c == "test" || c == "--test" ||
                c == "hw-matrix" || c == "--hw-matrix" ||
                c == "export-profiles" || c == "--export-profiles" ||
                c == "driver-stop" ||
                c == "validate-lang" || c == "--validate-lang" ||
                c == "validate-profiles" || c == "--validate-profiles" ||
                c == "service-install" || c == "service-uninstall" || c == "service-status" ||
                c == "service-start" || c == "service-stop" ||
                c == "gen-icon" || c == "--gen-icon" ||
                c == "latency" || c == "--latency")
            {
                return false;
            }
            return true;
        }

        public static int Run(string cmd, string cmdArg1, string cmdArg2, string[] args, bool demoMode, bool forceWrite)
        {
            if (cmd == "--help" || cmd == "-h" || cmd == "/?" || cmd == "help")
                return PrintHelp();

            if (cmd == "test" || cmd == "--test")
            {
#if INCLUDE_SELF_TESTS
                return SelfTestSuite.RunAll();
#else
                Console.Error.WriteLine("Self-test suite is built into minifan-helper.exe (run: minifan-helper.exe test).");
                return 1;
#endif
            }

            if (cmd == "hw-matrix" || cmd == "--hw-matrix")
            {
#if INCLUDE_SELF_TESTS
                return SelfTestSuite.RunHardwareMatrix();
#else
                Console.Error.WriteLine("Hardware matrix test is built into minifan-helper.exe (run: minifan-helper.exe hw-matrix).");
                return 1;
#endif
            }

            if (cmd == "export-profiles" || cmd == "--export-profiles")
            {
                bool asJson = HasFlag(args, "--json");
                Console.Write(asJson
                    ? DeviceProfile.ExportKnownProfilesJson(MainForm.APP_VERSION)
                    : DeviceProfile.ExportKnownProfilesTable());
                return 0;
            }

            if (cmd == "watchdog") return RunWatchdog(args, demoMode);
            if (cmd == "latency" || cmd == "--latency") return RunLatency(demoMode);
            if (cmd == "daemon" || cmd == "--daemon" || cmd == "headless" || cmd == "--headless") return RunDaemon(args, demoMode);
            if (cmd == "service" || cmd == "--service" ||
                cmd == "service-install" || cmd == "service-start" ||
                cmd == "service-stop" || cmd == "service-uninstall" || cmd == "service-status")
            {
                return RunServiceCommand(cmd, args, demoMode);
            }
            if (cmd == "submit-report" || cmd == "--submit-report") return RunSubmitReport(demoMode);
            if (cmd == "driver-status") return RunDriverStatus();
            if (cmd == "driver-stop") return RunDriverStop();
            if (cmd == "validate-profiles" || cmd == "--validate-profiles") return RunValidateProfiles();
            if (cmd == "validate-lang" || cmd == "--validate-lang") return RunValidateLang();
            if (cmd == "gen-icon" || cmd == "--gen-icon") return RunGenIcon(cmdArg1);

            return RunHardwareCommand(cmd, cmdArg1, cmdArg2, args, demoMode, forceWrite);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int PrintHelp()
        {
            Console.WriteLine("MiniFanControl v" + MainForm.APP_VERSION + " CLI Commands:");
            Console.WriteLine("  status [--stream] [--interval ms] [--count n] [--demo]");
            Console.WriteLine("                                        Query status JSON or stream live JSONL telemetry");
            Console.WriteLine("  latency [--demo]                      Inspect native Windows DXGI/DxgKrnl ETW display latency & flip mode");
            Console.WriteLine("  mode <0|1|2> [--demo]                 Set thermal mode (0=Balanced, 1=Performance, 2=Silent)");
            Console.WriteLine("  fan <1|2> <0..100> [--force] [--demo] Set fan 1 or 2 PWM (0=Auto; clamped to spin floor unless --force)");
            Console.WriteLine("  fan1 <0..100> [--force] [--demo]      Set fan 1 PWM percentage (clamped to spin floor unless --force)");
            Console.WriteLine("  fan2 <0..100> [--force] [--demo]      Set fan 2 PWM percentage (clamped to spin floor unless --force)");
            Console.WriteLine("  auto [--demo]                         Restore BIOS Automatic control for both fans");
            Console.WriteLine("  probe / dump [--json] [--demo]        Analyze hardware registers and EC access methods");
            Console.WriteLine("  submit-report [--demo]                Generate sanitized hardware profile JSON & GitHub Issue URL");
            Console.WriteLine("  daemon [--interval ms] [--demo]       Run zero-UI headless background fan control daemon");
            Console.WriteLine("  service / service-install / -uninstall Run or manage MiniFanControl as a native Windows Service");
            Console.WriteLine("  driver-status / driver-stop           Inspect PawnIO/HVCI/driver integrity or stop legacy drivers");
            Console.WriteLine("  diff-scan / ec-scan [--demo]          Run EC differential sensor scanner (Tier 2 discovery)");
            Console.WriteLine("  calibrate [passes] [--demo]           Execute multi-pass fan calibration curve analysis");
            Console.WriteLine("  test                                  Run 128-point automated safety & regression suite");
            Console.WriteLine("  hw-matrix                             Run multi-profile hardware validation matrix");
            Console.WriteLine("  export-profiles [--json]              Export supported hardware profiles & Super I/O IDs");
            Console.WriteLine("  validate-profiles                     Validate external JSON hardware profiles in profiles/");
            Console.WriteLine("  watchdog --pid <pid> [--timeout <ms>] Out-of-process safety watchdog supervisor");
            Console.WriteLine("  gen-icon [1..3]                       Generate embedded Windows ICO and PNG assets");
            return 0;
        }

        private static int RunWatchdog(string[] args, bool demoMode)
        {
            int pid = 0;
            int timeoutMs = WatchdogSupervisor.DEFAULT_TIMEOUT_MS;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pid" && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out pid);
                else if (args[i] == "--timeout" && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out timeoutMs);
            }
            if (pid <= 0)
            {
                Console.Error.WriteLine("Error: --pid <parentPid> is required for watchdog mode.");
                return 1;
            }
            try
            {
                return WatchdogSupervisor.Run(pid, timeoutMs, demoMode);
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("Watchdog CLI fatal exception: " + ex.ToString());
                Console.Error.WriteLine("{{\"error\":\"{0}\"}}", ex.Message.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "));
                return 1;
            }
        }

        private static int RunLatency(bool demoMode)
        {
            if (demoMode)
            {
                Console.WriteLine("{\"etw_active\":true,\"has_live_frames\":true,\"display_latency_ms\":11.4,\"render_latency_ms\":6.8,\"present_api_ms\":0.24,\"flip_mode\":\"MPO Flip\",\"fps\":98,\"frametime_ms\":10.2,\"process\":\"ES2-Win64-Shipping.exe\",\"diagnostic\":\"Demo Mode\"}");
                return 0;
            }
            try
            {
                EtwLatencyMonitor.EnsureStarted();
                Thread.Sleep(350);
                EtwLatencyTelemetry etw = EtwLatencyMonitor.GetTelemetry(false);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"etw_active\":{0},\"has_live_frames\":{1},\"display_latency_ms\":{2:0.0},\"render_latency_ms\":{3:0.0},\"present_api_ms\":{4:0.00},\"flip_mode\":\"{5}\",\"fps\":{6},\"frametime_ms\":{7:0.00},\"process\":\"{8}\",\"diagnostic\":\"{9}\"}}",
                    etw.IsEtwActive ? "true" : "false",
                    etw.HasLiveFrames ? "true" : "false",
                    etw.DisplayLatencyMs,
                    etw.RenderLatencyMs,
                    etw.PresentApiMs,
                    EscapeJson(etw.FlipMode ?? ""),
                    etw.Fps,
                    etw.FrametimeMs,
                    EscapeJson(etw.ProcessName ?? ""),
                    EscapeJson(EtwLatencyMonitor.LastDiagnostic ?? "")));
                return 0;
            }
            finally
            {
                try { EtwLatencyMonitor.Shutdown(); } catch { }
            }
        }

        private static int RunDaemon(string[] args, bool demoMode)
        {
            int intervalMs = 0;
            int maxTicks = 0;
            bool quiet = false;
            string cfgPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--interval" || args[i] == "-i") && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out intervalMs);
                else if (args[i] == "--max-ticks" && i + 1 < args.Length)
                    int.TryParse(args[i + 1], out maxTicks);
                else if (args[i] == "--config" && i + 1 < args.Length)
                    cfgPath = args[i + 1];
                else if (args[i] == "--once")
                    maxTicks = 1;
                else if (args[i] == "--quiet" || args[i] == "-q")
                    quiet = true;
            }
            return HeadlessDaemon.Run(demoMode, intervalMs, maxTicks, quiet, null, null, cfgPath);
        }

        private static int RunServiceCommand(string cmd, string[] args, bool demoMode)
        {
            if (cmd == "service" || cmd == "--service")
            {
                int intervalMs = 0;
                string cfgPath = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if ((args[i] == "--interval" || args[i] == "-i") && i + 1 < args.Length)
                        int.TryParse(args[i + 1], out intervalMs);
                    else if (args[i] == "--config" && i + 1 < args.Length)
                        cfgPath = args[i + 1];
                }
                return WindowsServiceHost.RunAsService(demoMode, intervalMs, cfgPath);
            }

            if (cmd == "service-install")
            {
                string exePath = Process.GetCurrentProcess().MainModule != null
                    ? Process.GetCurrentProcess().MainModule.FileName
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "minifan-helper.exe");
                string cfgPath = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--config" && i + 1 < args.Length)
                        cfgPath = args[i + 1];
                }
                if (string.IsNullOrEmpty(cfgPath))
                {
                    AppSettings.SyncConfigToServicePath(AppSettings.GetSettingsPath());
                    cfgPath = AppSettings.ResolveServiceConfigPath(true);
                }
                string msg;
                bool ok = WindowsServiceHost.InstallService(exePath, cfgPath, out msg);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":{0},\"service\":\"{1}\",\"config\":\"{2}\",\"message\":\"{3}\"}}",
                    ok ? "true" : "false", EscapeJson(WindowsServiceHost.SERVICE_NAME), EscapeJson(cfgPath ?? ""), EscapeJson(msg)));
                return ok ? 0 : 1;
            }

            if (cmd == "service-start")
            {
                string msg;
                bool ok = WindowsServiceHost.StartService(out msg);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":{0},\"service\":\"{1}\",\"message\":\"{2}\"}}",
                    ok ? "true" : "false", EscapeJson(WindowsServiceHost.SERVICE_NAME), EscapeJson(msg)));
                return ok ? 0 : 1;
            }

            if (cmd == "service-stop")
            {
                string msg;
                bool ok = WindowsServiceHost.StopService(out msg);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":{0},\"service\":\"{1}\",\"message\":\"{2}\"}}",
                    ok ? "true" : "false", EscapeJson(WindowsServiceHost.SERVICE_NAME), EscapeJson(msg)));
                return ok ? 0 : 1;
            }

            if (cmd == "service-uninstall")
            {
                string msg;
                bool ok = WindowsServiceHost.UninstallService(out msg);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":{0},\"service\":\"{1}\",\"message\":\"{2}\"}}",
                    ok ? "true" : "false", EscapeJson(WindowsServiceHost.SERVICE_NAME), EscapeJson(msg)));
                return ok ? 0 : 1;
            }

            bool installed = WindowsServiceHost.IsServiceInstalled();
            bool running = WindowsServiceHost.IsServiceRunning();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"service\":\"{0}\",\"installed\":{1},\"running\":{2}}}",
                EscapeJson(WindowsServiceHost.SERVICE_NAME),
                installed ? "true" : "false",
                running ? "true" : "false"));
            return 0;
        }

        private static int RunSubmitReport(bool demoMode)
        {
            SystemHardwareInfo subSys = SystemHardwareInfo.Detect();
            HardwareBackend subHw = demoMode ? null : HardwareBackend.CreateAutoDetected();
            try
            {
                string json = HardwareProfileSubmission.BuildSanitizedJson(subSys, subHw, MainForm.APP_VERSION);
                string issueUrl = HardwareProfileSubmission.BuildGitHubIssueUrl(subSys, subHw, MainForm.APP_VERSION);
                Console.WriteLine(json);
                Console.WriteLine();
                Console.WriteLine("GitHub Issue URL: " + issueUrl);
                return 0;
            }
            finally
            {
                if (subHw != null) try { subHw.Dispose(); } catch { }
            }
        }

        private static int RunDriverStatus()
        {
            bool pawnRunning = DriverServiceManager.IsDriverServiceRunning("PawnIO");
            bool inpoutRunning = DriverServiceManager.IsDriverServiceRunning("inpoutx64");
            bool winringRunning = DriverServiceManager.IsDriverServiceRunning("WinRing0_1_2_0");
            bool hvciEnabled = NativeInpOut.IsHvciEnabled();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"active_driver\":\"{0}\",\"hash_verified\":{1},\"sha256\":\"{2}\",\"integrity_error\":\"{3}\",\"hvci_enabled\":{4},\"pawnio_service\":{5},\"pawnio_available\":{6},\"pawnio_modules\":\"{7}\",\"winring0_service\":{8},\"inpout_service\":{9}}}",
                EscapeJson(NativeInpOut.ActiveDriverName),
                NativeInpOut.IsDriverHashVerified ? "true" : "false",
                EscapeJson(NativeInpOut.ActiveDriverSha256 ?? ""),
                EscapeJson(NativeInpOut.LastDriverIntegrityError ?? ""),
                hvciEnabled ? "true" : "false",
                pawnRunning ? "true" : "false",
                PawnIoDriver.IsAvailable ? "true" : "false",
                EscapeJson(PawnIoDriver.LoadedModulesSummary),
                winringRunning ? "true" : "false",
                inpoutRunning ? "true" : "false"));
            return 0;
        }

        private static int RunDriverStop()
        {
            bool s1 = DriverServiceManager.StopDriverService("WinRing0_1_2_0");
            bool s2 = DriverServiceManager.StopDriverService("inpoutx64");
            Console.WriteLine("{{\"winring0_stopped\":{0},\"inpout_stopped\":{1}}}",
                s1 ? "true" : "false", s2 ? "true" : "false");
            return 0;
        }

        private static int RunValidateProfiles()
        {
            string profDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExternalProfileRegistry.PROFILES_DIR_NAME);
            if (!Directory.Exists(profDir))
            {
                Console.WriteLine("[INFO] No 'profiles/' directory found. Nothing to validate.");
                return 0;
            }
            List<string> profErrors;
            int validCount;
            bool profOk = ExternalProfileRegistry.ValidateDirectory(profDir, out profErrors, out validCount);
            if (profOk)
            {
                Console.WriteLine(string.Format("[PASS] All external hardware profiles in 'profiles/' are valid ({0} profile(s) loaded).", validCount));
                return 0;
            }
            Console.Error.WriteLine("[FAIL] External hardware profile validation errors found:");
            foreach (string err in profErrors)
                Console.Error.WriteLine("  • " + err);
            return 1;
        }

        private static int RunValidateLang()
        {
            string langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
            if (!Directory.Exists(langDir))
            {
                Console.WriteLine("[INFO] No 'lang/' directory found. Nothing to validate.");
                return 0;
            }
            List<string> errors;
            bool ok = Loc.ValidateLanguageFiles(langDir, out errors);
            if (ok)
            {
                Console.WriteLine("[PASS] All community language files in 'lang/' are strictly valid!");
                return 0;
            }
            Console.Error.WriteLine("[FAIL] Community language file validation errors found:");
            foreach (string err in errors)
                Console.Error.WriteLine("  • " + err);
            return 1;
        }

        private static int RunGenIcon(string cmdArg1)
        {
            int conceptId = 1;
            int parsedConcept;
            if (!string.IsNullOrEmpty(cmdArg1) && int.TryParse(cmdArg1, out parsedConcept) && parsedConcept >= 1 && parsedConcept <= 3)
                conceptId = parsedConcept;

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            string logoPngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            string showcasePath = Directory.Exists(assetsDir)
                ? Path.Combine(assetsDir, "logo-concepts.png")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo-concepts.png");

            AppLogoRenderer.WriteMultiResIco(icoPath, conceptId);
            using (Bitmap hiResLogo = AppLogoRenderer.RenderConcept(conceptId, 512))
            {
                hiResLogo.Save(logoPngPath, ImageFormat.Png);
            }
            AppLogoRenderer.WriteConceptShowcasePng(showcasePath);

            Console.WriteLine(string.Format("[OK] Generated multi-res icon (Option #{0}): {1}", conceptId, icoPath));
            Console.WriteLine(string.Format("[OK] Generated 512x512 logo PNG (Option #{0}): {1}", conceptId, logoPngPath));
            Console.WriteLine(string.Format("[OK] Generated 3-concept logo sheet: {0}", showcasePath));
            return 0;
        }

        private static int RunHardwareCommand(string cmd, string cmdArg1, string cmdArg2, string[] args, bool demoMode, bool forceWrite)
        {
            try
            {
                using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())
                {
                    // One-shot CLI commands must never auto-start a persistent kernel ETW trace session
                    hw.AllowEtwAutoStart = false;
                    DemoBackend demo = new DemoBackend();

                    if (cmd == "status" || cmd == "stream" || cmd == "monitor")
                        return RunStatusOrStream(cmd, args, hw, demo, demoMode);
                    if (cmd == "mode")
                        return RunModeCommand(cmdArg1, hw, demo, demoMode);
                    if (cmd == "fan" || cmd == "fan1" || cmd == "fan2")
                        return RunFanCommand(cmd, cmdArg1, cmdArg2, hw, demo, demoMode, forceWrite);
                    if (cmd == "auto")
                        return RunAutoCommand(hw, demo, demoMode);
                    if (cmd == "dump" || cmd == "probe")
                    {
                        bool asJson = HasFlag(args, "--json");
                        SystemHardwareInfo sysInfo = SystemHardwareInfo.Detect();
                        Console.Write(asJson
                            ? sysInfo.BuildProbeReportJson(hw, demo, demoMode)
                            : sysInfo.BuildProbeReport(hw, demo, demoMode) + Environment.NewLine);
                        return 0;
                    }
                    if (cmd == "calibrate")
                        return RunCalibrateCommand(cmdArg1, hw, demo, demoMode);
                    if (cmd == "watch" || cmd == "rgb-scan")
                        return RunWatchOrRgbScan(cmd, hw);
                    if (cmd == "diff-scan" || cmd == "ec-scan")
                        return RunDiffScan(hw, demo, demoMode);

                    Console.Error.WriteLine(string.Format("Unknown command '{0}'. Run with --help for CLI usage.", cmd));
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("{{\"error\":\"{0}\"}}", ex.Message.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "));
                return 1;
            }
        }

        private static int RunStatusOrStream(string cmd, string[] args, HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            bool streamMode = (cmd == "stream" || cmd == "monitor");
            int streamIntervalMs = 1000;
            int streamCount = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("--stream", StringComparison.OrdinalIgnoreCase) || a.Equals("--monitor", StringComparison.OrdinalIgnoreCase))
                    streamMode = true;
                else if ((a.Equals("--interval", StringComparison.OrdinalIgnoreCase) || a.Equals("-i", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                    int.TryParse(args[++i], out streamIntervalMs);
                else if ((a.Equals("--count", StringComparison.OrdinalIgnoreCase) || a.Equals("-n", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                    int.TryParse(args[++i], out streamCount);
            }

            if (!streamMode)
            {
                FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                Console.WriteLine(FormatStatusJson(st, EcBusMutex.MutexNamespaceDiagnostic));
                return 0;
            }

            streamIntervalMs = Math.Max(250, Math.Min(10000, streamIntervalMs));
            bool stopStream = false;
            ConsoleCancelEventHandler cancelHandler = delegate(object s, ConsoleCancelEventArgs ce)
            {
                ce.Cancel = true;
                stopStream = true;
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                int emitted = 0;
                while (!stopStream && (streamCount <= 0 || emitted < streamCount))
                {
                    FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                    Console.WriteLine(FormatStatusJsonLine(st, EcBusMutex.MutexNamespaceDiagnostic, DateTime.UtcNow));
                    Console.Out.Flush();
                    emitted++;
                    if (streamCount > 0 && emitted >= streamCount) break;
                    int slept = 0;
                    while (!stopStream && slept < streamIntervalMs)
                    {
                        int step = Math.Min(100, streamIntervalMs - slept);
                        Thread.Sleep(step);
                        slept += step;
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
            return 0;
        }

        private static int RunModeCommand(string cmdArg1, HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            int code;
            if (!int.TryParse(cmdArg1, out code))
            {
                Console.Error.WriteLine("{\"error\":\"Invalid mode code: must be an integer (0..2)\"}");
                return 1;
            }
            bool ok = demoMode ? demo.SetMode(code) : hw.SetMode(code);
            if (ok)
            {
                Console.WriteLine("{{\"ok\":true,\"mode_code\":{0}}}", code);
                return 0;
            }
            Console.WriteLine("{{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"mode_code\":{0}}}", code);
            return 1;
        }

        private static int RunFanCommand(string cmd, string cmdArg1, string cmdArg2, HardwareBackend hw, DemoBackend demo, bool demoMode, bool forceWrite)
        {
            int fanId = 1;
            int pct = -1;
            if (cmd == "fan")
            {
                int.TryParse(cmdArg1 ?? "-1", out fanId);
                int.TryParse(cmdArg2 ?? "-1", out pct);
            }
            else
            {
                fanId = (cmd == "fan1") ? 1 : 2;
                int.TryParse(cmdArg1 ?? "-1", out pct);
            }

            if (fanId < 1 || fanId > 2 || pct < 0 || pct > 100)
            {
                Console.Error.WriteLine("{\"ok\":false,\"error\":\"Invalid fan parameters. Fan ID must be 1 or 2, duty percentage 0..100%\"}");
                return 1;
            }

            // Load calibrated anti-stall floor from saved settings so CLI respects hardware calibration
            try
            {
                AppSettings cliSettings = new AppSettings();
                cliSettings.Load(null, null, new List<FanCurveProfile>());
            }
            catch { }

            int requestedPct = pct;
            bool wasClamped;
            int minFloor;
            int appliedPct = ClampCliFanDuty(fanId, requestedPct, forceWrite, out wasClamped, out minFloor);
            bool ok = demoMode ? demo.SetFan(fanId, appliedPct) : hw.SetFan(fanId, appliedPct);
            if (ok)
            {
                if (appliedPct == 0)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{{\"ok\":true,\"fan\":{0},\"mode\":\"auto\"}}", fanId));
                else if (wasClamped)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{{\"ok\":true,\"fan\":{0},\"pct\":{1},\"requested_pct\":{2},\"clamped\":true,\"min_spin_pct\":{3},\"mode\":\"manual\"}}",
                        fanId, appliedPct, requestedPct, minFloor));
                else
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{{\"ok\":true,\"fan\":{0},\"pct\":{1},\"forced\":{2},\"mode\":\"manual\"}}",
                        fanId, appliedPct, forceWrite ? "true" : "false"));
                return 0;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"fan\":{0},\"pct\":{1}}}", fanId, appliedPct));
            return 1;
        }

        private static int RunAutoCommand(HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            bool ok = demoMode ? demo.SetAuto() : hw.SetAuto();
            if (ok)
            {
                string desc = demoMode ? "Demo Auto" : hw.GetAutoRestoreDescription();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":true,\"action\":\"auto\",\"mode\":\"BIOS Auto\",\"status\":\"{0}\",\"fan1\":\"auto\",\"fan2\":\"auto\"}}",
                    EscapeJson(desc)));
                return 0;
            }
            Console.WriteLine("{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"action\":\"auto\"}");
            return 1;
        }

        private static int RunCalibrateCommand(string cmdArg1, HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            int passes = 3;
            if (!string.IsNullOrEmpty(cmdArg1)) int.TryParse(cmdArg1, out passes);
            if (passes < 1 || passes > 5) passes = 3;

            Console.WriteLine(string.Format("Running MiniFanControl {0}-pass real-time fan calibration...", passes));
            FanCalibrationResult cal = FanCalibrationResult.RunCalibration(
                hw, demo, demoMode,
                demoMode ? 60 : 5000,
                passes,
                prog =>
                {
                    if (!string.IsNullOrEmpty(prog.LogLine))
                        Console.WriteLine(prog.LogLine);
                },
                null);
            Console.WriteLine();
            Console.WriteLine(cal.SummaryReport);
            return 0;
        }

        private static int RunWatchOrRgbScan(string cmd, HardwareBackend hw)
        {
            byte[] baseline = hw.ReadAllRegisters();
            if (cmd == "rgb-scan")
            {
                Console.WriteLine("Initial lighting registers: KBLT(0x09)=0x{0:X2}, KBF(0x39)=0x{1:X2}, KBFL(0x3A)=0x{2:X2}, KBCH(0x3C)=0x{3:X2}, KBCL(0x3D)=0x{4:X2}",
                    baseline[0x09], baseline[0x39], baseline[0x3A], baseline[0x3C], baseline[0x3D]);
                Console.WriteLine("Watching all 255 EC registers for 12 seconds... Press the physical RGB button on the K12 now!");
            }
            else
            {
                Console.WriteLine("Watching all 255 EC registers for 15 seconds (change BIOS mode, fan speed, or load to identify registers)...");
            }

            DateTime until = DateTime.UtcNow.AddSeconds(cmd == "rgb-scan" ? 12 : 15);
            bool anyChanged = false;
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(150);
                byte[] cur = hw.ReadAllRegisters();
                for (int r = 0; r < cur.Length; r++)
                {
                    if (cmd == "rgb-scan")
                    {
                        if (r >= 0x35 && r <= 0x38) continue;
                        if (r >= 0x70 && r <= 0x73) continue;
                    }
                    if (cur[r] != baseline[r])
                    {
                        Console.WriteLine("  [CHANGE] Register 0x{0:X2}: 0x{1:X2} -> 0x{2:X2}", r, baseline[r], cur[r]);
                        baseline[r] = cur[r];
                        anyChanged = true;
                    }
                }
            }
            if (!anyChanged)
                Console.WriteLine("No EC register changed during the watch window.");
            return 0;
        }

        private static int RunDiffScan(HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            IHardwareBackend backend = demoMode ? (IHardwareBackend)demo : (IHardwareBackend)hw;
            SystemHardwareInfo sysInfo = SystemHardwareInfo.Detect();
            Console.WriteLine("======================================================================");
            Console.WriteLine("  EC Motherboard Telemetry & Differential Scanner (Tier 2 Discovery)");
            Console.WriteLine("======================================================================");
            Console.WriteLine("Capturing EC Differential Baseline (Idle: 10 samples)...");
            EcSnapshot idle = EcDifferentialScanner.CaptureSnapshot(backend, "Idle Baseline", 10, 80);
            Console.WriteLine(string.Format("Baseline: CPU {0}°C, GPU {1}°C, Fan1 {2} RPM, Fan2 {3} RPM, ~{4}W",
                idle.CpuTemp, idle.GpuTemp, idle.Fan1Rpm, idle.Fan2Rpm, idle.ApuPowerWatts));
            Console.WriteLine();
            Console.WriteLine("Capturing EC Differential Thermal Load (10 samples)...");
            Console.WriteLine("(Tip: In live testing, put CPU/GPU under load or run stress test)");
            EcSnapshot load = EcDifferentialScanner.CaptureSnapshot(backend, "Load Snapshot", 10, 80);
            Console.WriteLine(string.Format("Load: CPU {0}°C, GPU {1}°C, Fan1 {2} RPM, Fan2 {3} RPM, ~{4}W",
                load.CpuTemp, load.GpuTemp, load.Fan1Rpm, load.Fan2Rpm, load.ApuPowerWatts));
            Console.WriteLine();
            Console.WriteLine("Analyzing register differential deltas...");
            EcDiffReport report = EcDifferentialScanner.Analyze(idle, load, sysInfo.MatchedProfile);
            Console.WriteLine(EcDifferentialScanner.GenerateTextReport(report));
            return 0;
        }
    }
}
