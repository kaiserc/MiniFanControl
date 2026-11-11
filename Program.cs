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

    public static class Program

    {

        [DllImport("kernel32.dll")]

        private static extern bool AttachConsole(int dwProcessId);

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]

        public static int Main(string[] args)

        {

            NativeInpOut.PreloadDriverDll();

            bool demoMode = false;

            bool startMinimized = false;

            string cmd = null;

            string cmdArg1 = null;

            string cmdArg2 = null;

            for (int i = 0; i < args.Length; i++)

            {

                string a = args[i];

                if (a == "--demo" || a == "--simulate")

                {

                    demoMode = true;

                }

                else if (a == "--minimized" || a == "--tray" || a == "-m")

                {

                    startMinimized = true;

                }

                else if (cmd == null)

                {

                    cmd = a;

                }

                else if (cmdArg1 == null)

                {

                    cmdArg1 = a;

                }

                else if (cmdArg2 == null)

                {

                    cmdArg2 = a;

                }

            }

            if (!string.IsNullOrEmpty(cmd))

            {

                AttachConsole(ATTACH_PARENT_PROCESS);

                if (cmd == "--help" || cmd == "-h" || cmd == "/?" || cmd == "help")

                {

                    Console.WriteLine("MiniFanControl v1.0.0 CLI Commands:");

                    Console.WriteLine("  status [--demo]                       Query fan speeds, APU power, temperatures");

                    Console.WriteLine("  mode <0|1|2> [--demo]                 Set thermal mode (0=Balanced, 1=Performance, 2=Silent)");

                    Console.WriteLine("  fan <1|2> <0..100> [--demo]           Set fan 1 or 2 to PWM percentage (0=Auto)");

                    Console.WriteLine("  fan1 <0..100> [--demo]                Set fan 1 PWM percentage");

                    Console.WriteLine("  fan2 <0..100> [--demo]                Set fan 2 PWM percentage");

                    Console.WriteLine("  auto [--demo]                         Restore BIOS Automatic control for both fans");

                    Console.WriteLine("  probe / dump [--demo]                 Analyze hardware registers and EC access methods");

                    Console.WriteLine("  calibrate [passes] [--demo]           Execute multi-pass fan calibration curve analysis");

                    Console.WriteLine("  test                                  Run 36-point automated safety & regression suite");
                    Console.WriteLine("  hw-matrix                             Run multi-profile hardware validation matrix");

                    Console.WriteLine("  watchdog --pid <pid> [--timeout <ms>] Out-of-process safety watchdog supervisor");

                    Console.WriteLine("  gen-icon [1..3]                       Generate embedded Windows ICO and PNG assets");

                    return 0;

                }

                if (cmd == "test" || cmd == "--test")

                {

                    return SelfTestSuite.RunAll();

                }

                if (cmd == "hw-matrix" || cmd == "--hw-matrix")

                {

                    return SelfTestSuite.RunHardwareMatrix();

                }

                if (cmd == "watchdog")

                {

                    int pid = 0;

                    int timeoutMs = WatchdogSupervisor.DEFAULT_TIMEOUT_MS;

                    for (int i = 0; i < args.Length; i++)

                    {

                        if (args[i] == "--pid" && i + 1 < args.Length)

                        {

                            int.TryParse(args[i + 1], out pid);

                        }

                        else if (args[i] == "--timeout" && i + 1 < args.Length)

                        {

                            int.TryParse(args[i + 1], out timeoutMs);

                        }

                    }

                    if (pid <= 0)

                    {

                        Console.Error.WriteLine("Error: --pid <parentPid> is required for watchdog mode.");

                        return 1;

                    }

                    return WatchdogSupervisor.Run(pid, timeoutMs, demoMode);

                }

                if (cmd == "driver-status")

                {

                    bool isRunning = DriverServiceManager.IsDriverServiceRunning("inpoutx64");

                    Console.WriteLine("{{\"service\":\"inpoutx64\",\"running\":{0}}}", isRunning ? "true" : "false");

                    return 0;

                }

                else if (cmd == "driver-stop")

                {

                    bool stopped = DriverServiceManager.StopDriverService("inpoutx64");

                    Console.WriteLine("{{\"service\":\"inpoutx64\",\"action\":\"stop\",\"success\":{0}}}", stopped ? "true" : "false");

                    return 0;

                }

                if (cmd == "gen-icon" || cmd == "--gen-icon")

                {

                    int conceptId = 1;

                    int parsedConcept;

                    if (!string.IsNullOrEmpty(cmdArg1) && int.TryParse(cmdArg1, out parsedConcept) && parsedConcept >= 1 && parsedConcept <= 3)

                    {

                        conceptId = parsedConcept;

                    }

                    string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");

                    string logoPngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");

                    string showcasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo-concepts.png");

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

                try

                {

                    using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())

                    {

                        DemoBackend demo = new DemoBackend();

                        if (cmd == "status")

                        {

                            FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                            Console.WriteLine(
                                "{{\"cpu_temp\":{0},\"gpu_temp\":{1},\"apu_power_w\":{2},\"fan1_rpm\":{3},\"fan2_rpm\":{4}," +
                                "\"mode\":\"{5}\",\"mode_code\":{6},\"fan1_pct\":{7},\"fan2_pct\":{8}," +
                                "\"fan1_manual\":{9},\"fan2_manual\":{10},\"nvme_temp\":{11},\"nvme_health\":{12}," +
                                "\"ram_used_gb\":{13:0.0},\"ram_total_gb\":{14:0.0},\"ram_load_pct\":{15},\"radeon_features\":\"{16}\",\"mutex_namespace\":\"{17}\"}}",
                                st.CpuTemp, st.GpuTemp, st.ApuPowerWatts, st.Fan1Rpm, st.Fan2Rpm,
                                st.Mode, st.ModeCode, st.Fan1Pct, st.Fan2Pct,
                                st.Fan1Manual, st.Fan2Manual, st.NvmeTemp, st.NvmeHealthPct,
                                st.RamUsedGb, st.RamTotalGb, st.RamLoadPct, st.RadeonFeaturesSummary ?? "None", EcBusMutex.MutexNamespaceDiagnostic);
                            return 0;

                        }

                        else if (cmd == "mode")

                        {

                            int code = int.Parse(cmdArg1 ?? "-1");

                            bool ok = demoMode ? demo.SetMode(code) : hw.SetMode(code);

                            if (ok)

                            {

                                Console.WriteLine("{{\"ok\":true,\"mode_code\":{0}}}", code);

                                return 0;

                            }

                            else

                            {

                                Console.WriteLine("{{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"mode_code\":{0}}}", code);

                                return 1;

                            }

                        }

                        else if (cmd == "fan" || cmd == "fan1" || cmd == "fan2")

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

                            bool ok = demoMode ? demo.SetFan(fanId, pct) : hw.SetFan(fanId, pct);

                            if (ok)

                            {

                                if (pct == 0)

                                    Console.WriteLine("{{\"ok\":true,\"fan\":{0},\"mode\":\"auto\"}}", fanId);

                                else

                                    Console.WriteLine("{{\"ok\":true,\"fan\":{0},\"pct\":{1},\"mode\":\"manual\"}}", fanId, pct);

                                return 0;

                            }

                            else

                            {

                                Console.WriteLine("{{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"fan\":{0},\"pct\":{1}}}", fanId, pct);

                                return 1;

                            }

                        }

                        else if (cmd == "auto")

                        {

                            bool ok = demoMode ? demo.SetAuto() : hw.SetAuto();

                            if (ok)

                            {

                                string desc = demoMode ? "Demo Auto" : hw.GetAutoRestoreDescription();
                                Console.WriteLine("{{\"ok\":true,\"action\":\"auto\",\"mode\":\"Balanced\",\"status\":\"{0}\",\"fan1\":\"auto\",\"fan2\":\"auto\"}}", desc);

                                return 0;

                            }

                            else

                            {

                                Console.WriteLine("{\"ok\":false,\"error\":\"Hardware write verification failed or profile is read-only\",\"action\":\"auto\"}");

                                return 1;

                            }

                        }

                        else if (cmd == "dump" || cmd == "probe")

                        {

                            SystemHardwareInfo sysInfo = SystemHardwareInfo.Detect();

                            Console.WriteLine(sysInfo.BuildProbeReport(hw, demo, demoMode));

                            return 0;

                        }

                        else if (cmd == "calibrate")

                        {

                            int passes = 3;

                            if (!string.IsNullOrEmpty(cmdArg1)) int.TryParse(cmdArg1, out passes);

                            if (passes < 1 || passes > 5) passes = 3;

                            Console.WriteLine(string.Format("Running MiniFanControl {0}-pass real-time fan calibration...", passes));

                            FanCalibrationResult cal = FanCalibrationResult.RunCalibration(

                                hw, demo, demoMode,

                                demoMode ? 60 : 5000, // 5-second stabilization wait before fan measurement

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

                        else if (cmd == "watch" || cmd == "rgb-scan")

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

                                System.Threading.Thread.Sleep(150);

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

                            {

                                Console.WriteLine("No EC register changed during the watch window.");

                            }

                            return 0;

                        }

                        else

                        {

                            Console.Error.WriteLine(string.Format("Unknown command '{0}'. Run with --help for CLI usage.", cmd));

                            return 1;

                        }

                    }

                }

                catch (Exception ex)

                {

                    Console.Error.WriteLine("{{\"error\":\"{0}\"}}", ex.Message.Replace("\"", "\\\""));

                    return 1;

                }

            }

            Application.EnableVisualStyles();

            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>

            {

                try

                {

                    using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())

                    {

                        hw.SetAuto();

                    }

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

                    using (HardwareBackend hw = HardwareBackend.CreateAutoDetected())

                    {

                        hw.SetAuto();

                    }

                }

                catch { }

            };

            Application.Run(new MainForm(demoMode, startMinimized));

            return 0;

        }

    }

}

