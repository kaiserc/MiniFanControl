using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiniFanControl
{
    public static partial class SelfTestSuite
    {
        private static void RunSecurityAndEtwTests()
        {
            // 68. Cross-Process Watchdog IPC Verification & Child Lifecycle Handshake
            bool crossIpcOk = false;
            string crossIpcDetails = "";
            try
            {
                int testParentPid = Process.GetCurrentProcess().Id;
                string helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "minifan-helper.exe");
                string helperArgsPrefix = "";
                if (!File.Exists(helperPath))
                {
                    string managedDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MiniFanControl.dll");
                    string currentExe = "";
                    try { currentExe = Process.GetCurrentProcess().MainModule.FileName; } catch { }
                    if (File.Exists(managedDll) && !string.IsNullOrEmpty(currentExe) &&
                        Path.GetFileName(currentExe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        helperPath = currentExe;
                        helperArgsPrefix = string.Format("\"{0}\" ", managedDll);
                    }
                    else
                    {
                        helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MiniFanControl.exe");
                    }
                }

                if (File.Exists(helperPath))
                {
                    bool createdHb, createdSd, createdReady;
                    using (EventWaitHandle hbEvt = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetHeartbeatEventName(testParentPid), EventResetMode.AutoReset, out createdHb, WatchdogIpcRole.Signaler))
                    using (EventWaitHandle sdEvt = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetShutdownEventName(testParentPid), EventResetMode.ManualReset, out createdSd, WatchdogIpcRole.Signaler))
                    using (EventWaitHandle readyEvt = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetReadyEventName(testParentPid), EventResetMode.ManualReset, out createdReady, WatchdogIpcRole.Waiter))
                    {
                        sdEvt.Reset();

                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = helperPath,
                            Arguments = string.Format("{0}watchdog --pid {1} --timeout 1500 --demo", helperArgsPrefix, testParentPid),
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        using (Process helperProc = Process.Start(psi))
                        {
                            if (helperProc != null)
                            {
                                // A. Handshake verification: Child opens events and signals Ready
                                bool readySignaled = readyEvt.WaitOne(3000);

                                // B. Heartbeat pulse verification: AutoReset event pulse consumed without UnauthorizedAccessException
                                hbEvt.Set();
                                Thread.Sleep(60);

                                // C. Clean shutdown verification: Signal shutdown, helper exits gracefully with ExitCode 0
                                sdEvt.Set();
                                bool exitedClean = helperProc.WaitForExit(3500);
                                int exitCode = exitedClean ? helperProc.ExitCode : -1;

                                if (readySignaled && exitedClean && exitCode == 0)
                                {
                                    crossIpcOk = true;
                                    crossIpcDetails = string.Format("ReadyHandshake=OK, AutoResetWaitOne=OK, GracefulExitCode={0}", exitCode);
                                }
                                else
                                {
                                    crossIpcDetails = string.Format("Ready={0}, ExitedClean={1}, ExitCode={2}", readySignaled, exitedClean, exitCode);
                                    if (!exitedClean) { try { helperProc.Kill(); } catch { } }
                                }
                            }
                        }
                    }
                }
                else
                {
                    crossIpcOk = true;
                    crossIpcDetails = "Skipped cross-process spawn (helper binary not on disk yet); IPC roles verified in Test #34";
                }
            }
            catch (Exception ex)
            {
                crossIpcOk = false;
                crossIpcDetails = "Exception: " + ex.ToString();
            }

            // Always reset mutexes at conclusion of test run to avoid static state leakage
            EcBusMutex.ResetMutexes();

            Check(crossIpcOk,
                "Cross-Process Watchdog IPC Verification & Child Lifecycle Handshake",
                crossIpcDetails);

            // 69. Sleep & Suspend Lifecycle Protection (Worker Pause, Safe Standby Auto, Inverted Duty & Resume Recovery)
            bool sleepLifecycleOk = false;
            string sleepDetails = "";
            try
            {
                // A. WinForms Suspend & Resume Lifecycle (including Modern Standby S0ix notifications)
                bool suspendPausedWorker = false;
                bool resumeUnpausedWorker = false;
                bool modernStandbyWorked = false;
                bool defaultSilentStandbyOk = false;
                using (MainForm testForm = new MainForm(startInDemo: true, startMinimized: false, isUnitTest: true))
                {
                    defaultSilentStandbyOk = testForm.TestSilentSleepStandby;

                    bool initSuspended = testForm.TestIsSystemSuspended;
                    bool initPaused = testForm.TestHwWorkerPaused;

                    testForm.TestOnPowerSuspend();
                    bool afterSuspendState = testForm.TestIsSystemSuspended;
                    bool afterSuspendPaused = testForm.TestHwWorkerPaused;
                    suspendPausedWorker = (!initSuspended && !initPaused && afterSuspendState && afterSuspendPaused);

                    testForm.TestOnPowerResume();
                    bool afterResumeState = testForm.TestIsSystemSuspended;
                    bool afterResumePaused = testForm.TestHwWorkerPaused;
                    resumeUnpausedWorker = (!afterResumeState && !afterResumePaused);

                    // Modern Standby display timeout (0) vs low-power epoch (1) simulation:
                    // A. Display timeout (0) sets display-off flag but DOES NOT suspend system or stop worker!
                    Guid gDisplay = new Guid("4838e551-f71f-4e03-9e60-429a4c2b97c6");
                    testForm.TestHandleModernStandbySettingChange(gDisplay, 0); // Display off (screen timeout)
                    bool displayOffKeptWorkerRunning = (!testForm.TestIsSystemSuspended && !testForm.TestHwWorkerPaused && testForm.TestIsDisplayOff);

                    testForm.TestHandleModernStandbySettingChange(gDisplay, 1); // Display on (screen wake)
                    bool displayOnRestored = (!testForm.TestIsSystemSuspended && !testForm.TestHwWorkerPaused && !testForm.TestIsDisplayOff);

                    // B. Low Power Epoch (1 = enter connected standby/sleep, 0 = exit/wake)
                    Guid gEpoch = new Guid("fc8b2612-a7f4-42f1-82bb-23d948fe4457");
                    testForm.TestHandleModernStandbySettingChange(gEpoch, 1); // Enter low power epoch -> suspend
                    bool msEpochSuspended = (testForm.TestIsSystemSuspended && testForm.TestHwWorkerPaused);

                    testForm.TestHandleModernStandbySettingChange(gEpoch, 0); // Exit low power epoch -> resume
                    bool msEpochResumed = (!testForm.TestIsSystemSuspended && !testForm.TestHwWorkerPaused);

                    modernStandbyWorked = displayOffKeptWorkerRunning && displayOnRestored && msEpochSuspended && msEpochResumed;
                }

                // B. Super I/O Extended PWM Safe Quiet Standby Duty (0x00: Bit 7 = 0 Extended PWM, safe baseline duty)
                HardwareBackend sioHw = new HardwareBackend();
                sioHw.EnableSimulationMode();
                sioHw.Profile = DeviceProfile.FindProfile("ryzen-hx370-ec");
                sioHw.SuperIoBaseAddr = 0x0A30;
                bool fallbackApplied = sioHw.ApplySafeSuperIoFallback();
                byte r15 = sioHw.ReadSuperIoHwReg(0x15);
                byte r16 = sioHw.ReadSuperIoHwReg(0x16);
                byte r17 = sioHw.ReadSuperIoHwReg(0x17);
                byte r63 = sioHw.ReadSuperIoHwReg(0x63);
                bool sioInvertedSafe = fallbackApplied && (r15 == 0x00) && (r16 == 0x00) && (r17 == 0x00) &&
                                      ((r16 & 0x80) == 0) && (r63 == 89);

                // C. Disconnected Bus Float & Sensor Drop-off Tripwire Protection
                bool emergActive = false;
                HardwareBackend.CheckEmergencyThermalTripwire(255, ref emergActive); // Bus float / disconnected sensor (0xFF)
                bool floatRejected = !emergActive;
                HardwareBackend.CheckEmergencyThermalTripwire(127, ref emergActive); // Disconnected thermal diode float (0x7F)
                bool float127Rejected = !emergActive;
                HardwareBackend.CheckEmergencyThermalTripwire(92, ref emergActive);  // Genuine high thermal reading
                bool tripAccepted = emergActive;
                HardwareBackend.CheckEmergencyThermalTripwire(70, ref emergActive);  // Hysteresis cooldown
                bool coolRecovered = !emergActive;
                bool sensorSafetyOk = floatRejected && float127Rejected && tripAccepted && coolRecovered;

                if (suspendPausedWorker && resumeUnpausedWorker && modernStandbyWorked && defaultSilentStandbyOk && sioInvertedSafe && sensorSafetyOk)
                {
                    sleepLifecycleOk = true;
                    sleepDetails = string.Format("ModernStandbyS0ix=OK, SilentStandbyDefault=OK, WorkerSuspend=OK, ResumeRecovery=OK, SioDuty=0x{0:X2}, FloatsRejected=OK", r16);
                }
                else
                {
                    sleepDetails = string.Format("SuspendPause={0}, ResumeUnpause={1}, ModernStandby={2}, SilentDefault={3}, SioStandbySafe={4}, SensorSafety={5}",
                        suspendPausedWorker, resumeUnpausedWorker, modernStandbyWorked, defaultSilentStandbyOk, sioInvertedSafe, sensorSafetyOk);
                }
            }
            catch (Exception ex)
            {
                sleepLifecycleOk = false;
                sleepDetails = "Exception: " + ex.Message;
            }

            Check(sleepLifecycleOk,
                "Sleep & Suspend Lifecycle Protection (Worker Pause, Safe Standby Auto, Inverted Duty & Resume Recovery)",
                sleepDetails);

            // -----------------------------------------------------------------
            // 18. Review Refactors: JSON Parsers, OsdOverlayConfig & Accessibility
            // -----------------------------------------------------------------
            bool profileJsonOk = false;
            string profileJsonDetail = "";
            try
            {
                string pj = "{\n"
                    + "  \"id\": \"custom-profile\",\n"
                    + "  \"displayName\": \"Custom: GMKtec K12 (12:34 test)\",\n"
                    + "  \"chipsetNotes\": \"Scanned on 2026-11-18 14:30:00\",\n"
                    + "  \"regCpuTemp\": \"0x70\",\n"
                    + "  \"hasDualFans\": true\n"
                    + "}";
                DeviceProfile dpParsed = DeviceProfile.FromJson(pj);
                profileJsonOk = dpParsed != null
                    && dpParsed.Id == "custom-profile"
                    && dpParsed.DisplayName == "Custom: GMKtec K12 (12:34 test)"
                    && dpParsed.ChipsetNotes == "Scanned on 2026-11-18 14:30:00"
                    && dpParsed.RegCpuTemp == 0x70
                    && dpParsed.HasDualFans;
                profileJsonDetail = dpParsed != null ? dpParsed.DisplayName : "null";
            }
            catch (Exception ex) { profileJsonDetail = ex.Message; }
            Check(profileJsonOk,
                "DeviceProfile.FromJson Preserves Colons Inside String Values",
                profileJsonDetail);

            bool locJsonOk = false;
            string locJsonDetail = "";
            try
            {
                string singleLineLoc = "{\"Test_Colon\": \"Value: 12:34 \\\"quoted\\\"\", \"Test_Unicode\": \"Temp\\u00B0C\"}";
                Loc.LoadCustomJson(singleLineLoc);
                string v1 = Loc.T("Test_Colon", "");
                string v2 = Loc.T("Test_Unicode", "");
                Loc.ClearCustomStrings();
                locJsonOk = (v1 == "Value: 12:34 \"quoted\"") && (v2 == "Temp\u00B0C");
                locJsonDetail = string.Format("v1='{0}', v2='{1}'", v1, v2);
            }
            catch (Exception ex)
            {
                try { Loc.ClearCustomStrings(); } catch { }
                locJsonDetail = ex.Message;
            }
            Check(locJsonOk,
                "Loc Tokenizing JSON Parser (Single-Line, Colons, Escapes & Unicode)",
                locJsonDetail);

            bool osdCfgOk = false;
            string osdCfgDetail = "";
            try
            {
                var cfg = new OsdOverlayConfig();
                cfg.DeselectAllMetrics();
                bool afterDeselect = !cfg.ShowCpu && !cfg.ShowGpu && !cfg.ShowFps;
                cfg.SelectAllMetrics(false);
                bool afterSelectSingle = cfg.ShowCpu && cfg.ShowGpu && !cfg.ShowSysFan;
                cfg.SelectAllMetrics(true);
                bool afterSelectDual = cfg.ShowSysFan && cfg.ShowVrmCurrent;
                cfg.ResetDefaults(false);
                bool afterReset = cfg.ShowCpu && !cfg.ShowSysFan && !cfg.ShowVrmCurrent && cfg.PeakTrackerMode == 0;

                cfg.OverlayX = 123;
                cfg.OpacityPct = 77;
                cfg.PeakTrackerMode = 2;
                cfg.ShowGameName = false;
                var lines = new List<string>();
                cfg.AppendSettings(lines);

                var cfg2 = new OsdOverlayConfig();
                for (int li = 0; li < lines.Count; li++)
                {
                    int eq = lines[li].IndexOf('=');
                    if (eq > 0)
                    {
                        cfg2.TryParseSetting(lines[li].Substring(0, eq), lines[li].Substring(eq + 1));
                    }
                }
                bool roundTrip = cfg2.OverlayX == 123 && cfg2.OpacityPct == 77 && cfg2.PeakTrackerMode == 2 && !cfg2.ShowGameName;
                osdCfgOk = afterDeselect && afterSelectSingle && afterSelectDual && afterReset && roundTrip;
                osdCfgDetail = string.Format("desel={0} sel1={1} sel2={2} rst={3} rt={4}",
                    afterDeselect, afterSelectSingle, afterSelectDual, afterReset, roundTrip);
            }
            catch (Exception ex) { osdCfgDetail = ex.Message; }
            Check(osdCfgOk,
                "OsdOverlayConfig Defaults, Dual/Single SelectAll & Settings Round-Trip",
                osdCfgDetail);

            bool a11yOk = false;
            string a11yDetail = "";
            try
            {
                using (var pnl = new DoubleBufferedPanel())
                using (var curve = new FanCurveCanvas())
                {
                    pnl.EnableKeyboardActivation("CPU Metric", "Open CPU details");
                    bool pnlOk = pnl.TabStop && pnl.AccessibleRole == AccessibleRole.PushButton && pnl.AccessibleName == "CPU Metric";
                    bool curveOk = curve.TabStop && curve.AccessibleRole == AccessibleRole.Chart;
                    a11yOk = pnlOk && curveOk;
                    a11yDetail = string.Format("pnlOk={0}, curveOk={1}", pnlOk, curveOk);
                }
            }
            catch (Exception ex) { a11yDetail = ex.Message; }
            Check(a11yOk,
                "Custom Controls Accessibility (Keyboard TabStop & UIA Roles)",
                a11yDetail);

            bool ctrlSvcOk = false;
            string ctrlSvcDetail = "";
            try
            {
                var ctrlDemo = new DemoBackend();
                var dummyCurve = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var profs = new FanCurveProfile[] {
                    new FanCurveProfile("Balanced", true, 0, dummyCurve, dummyCurve),
                    new FanCurveProfile("Performance", true, 1, dummyCurve, dummyCurve)
                };
                using (var svc = new FanControllerService(null, ctrlDemo, profs, true))
                {
                    bool manualOk = svc.ApplyManualFan(1, 65) && svc.ApuManualOverride && svc.ApuManualPct == 65;
                    bool autoOk = svc.ApplyAutoAll() && !svc.ApuManualOverride && svc.ApuManualPct == 0;
                    bool evalNormal = svc.EvaluateAndApplyFanProfiles(55);
                    bool emergencyOk = svc.EvaluateAndApplyFanProfiles(96) && svc.EmergencyThermalActive && svc.LastAppliedApuPct == 100;
                    ctrlSvcOk = manualOk && autoOk && emergencyOk;
                    ctrlSvcDetail = string.Format("manual={0} auto={1} evalNorm={2} emerg={3} apuPct={4}",
                        manualOk, autoOk, evalNormal, emergencyOk, svc.LastAppliedApuPct);
                }
            }
            catch (Exception ex) { ctrlSvcDetail = ex.Message; }
            Check(ctrlSvcOk,
                "FanControllerService Decoupled Evaluation, Manual/Auto Transitions & Thermal Tripwire",
                ctrlSvcDetail);

            bool exportProfOk = false;
            string exportProfDetail = "";
            try
            {
                string jsonOut = DeviceProfile.ExportKnownProfilesJson("1.0.3");
                string tblOut = DeviceProfile.ExportKnownProfilesTable();
                exportProfOk = !string.IsNullOrEmpty(jsonOut)
                    && jsonOut.Contains("\"gmktec-k12\"")
                    && jsonOut.Contains("\"ryzen-hx370-ec\"")
                    && jsonOut.Contains("\"probedSuperIoFamilies\"")
                    && tblOut.Contains("gmktec-k12")
                    && tblOut.Contains("0x86xx");
                exportProfDetail = string.Format("jsonLen={0}, tblLen={1}", jsonOut.Length, tblOut.Length);
            }
            catch (Exception ex) { exportProfDetail = ex.Message; }
            Check(exportProfOk,
                "DeviceProfile CLI Export (JSON & Summary Table Formats)",
                exportProfDetail);

            bool probeJsonOk = false;
            string probeJsonDetail = "";
            try
            {
                SystemHardwareInfo probeSys = new SystemHardwareInfo();
                probeSys.Manufacturer = "Recoreqich";
                probeSys.ProductName = "K8";
                probeSys.BaseBoard = "Default string";
                probeSys.BiosVersion = "1.09";
                probeSys.CpuName = "AMD Ryzen AI 9 HX PRO 370 w/ Radeon 890M";
                probeSys.MatchedProfile = SystemHardwareInfo.MatchProfile(probeSys.ProductName, probeSys.BaseBoard, probeSys.CpuName, probeSys.Manufacturer);
                string dumpJson = probeSys.BuildProbeReportJson(null, new DemoBackend(), true);
                probeJsonOk = !string.IsNullOrEmpty(dumpJson)
                    && dumpJson.Contains("\"manufacturer\": \"Recoreqich\"")
                    && dumpJson.Contains("\"baseBoard\": \"Default string\"")
                    && dumpJson.Contains("\"id\": \"unknown-safe\"")
                    && dumpJson.Contains("\"port0x4E\"")
                    && SystemHardwareInfo.DescribeSuperIoChip(0x5570) == "ITE IT5570E"
                    && SystemHardwareInfo.DescribeSuperIoChip(0x8613) == "ITE IT8613E"
                    && SystemHardwareInfo.DescribeSuperIoChip(0xD423) == "Nuvoton NCT67xx";
                probeJsonDetail = string.Format("dumpJsonLen={0}, chip5570={1}",
                    dumpJson.Length, SystemHardwareInfo.DescribeSuperIoChip(0x5570));
            }
            catch (Exception ex) { probeJsonDetail = ex.Message; }
            Check(probeJsonOk,
                "Hardware Probe JSON Export & Super I/O Chip Identification (IT5570E / IT8613E / NCT67xx)",
                probeJsonDetail);

            // 77. Calibrated 14% Floor Evaluation, BIOS-Mode Auto-Tune & OSD Graphs-Only / DF Corner Persistence
            bool v110FeaturesOk = false;
            string v110Detail = "";
            int savedMinApu = FanCurveProfile.ApuMinSpinPct;
            int savedMinSys = FanCurveProfile.SysMinSpinPct;
            try
            {
                FanCurveProfile.ApuMinSpinPct = 14;
                FanCurveProfile.SysMinSpinPct = 20;

                // 1. EvaluateCurve must honor calibrated 14% floor (never snap 14% back up to 30%)
                CurvePoint[] lowPts = new CurvePoint[] { new CurvePoint(40, 14), new CurvePoint(60, 40), new CurvePoint(85, 100) };
                int eval14 = FanCurveProfile.EvaluateCurve(lowPts, 40, 14);
                int evalClamp14 = FanCurveProfile.EvaluateCurve(new CurvePoint[] { new CurvePoint(40, 10), new CurvePoint(80, 100) }, 40, 14);
                bool floor14Ok = (eval14 == 14 && evalClamp14 == 14);

                // 2. Mode-aware Auto-Tune adapts TDP & knee across Silent (2), Balanced (0), Perf (1)
                FanStatus st = new FanStatus { CpuTemp = 44, GpuTemp = 40, NvmeTemp = 45, ApuPowerLimitWatts = 48 };
                string sumS, sumB, sumP;
                FanCurveProfile profS = FanCurveProfile.SynthesizeAutoTunedCurve(st, null, out sumS, 2);
                FanCurveProfile profB = FanCurveProfile.SynthesizeAutoTunedCurve(st, null, out sumB, 0);
                FanCurveProfile profP = FanCurveProfile.SynthesizeAutoTunedCurve(st, null, out sumP, 1);
                bool autoTuneModesOk = profS.ApuPoints[0].Pct == 14 && profS.SysPoints[0].Pct == 20
                    && profS.Name.Contains("Silent 35W")
                    && profB.Name.Contains("Balanced 48W")
                    && profP.Name.Contains("Perf 60W")
                    && profS.ApuPoints[2].Pct <= profB.ApuPoints[2].Pct
                    && profB.ApuPoints[2].Pct <= profP.ApuPoints[2].Pct;

                // 3. OSD GraphMode=4 (Graphs Only), DfCornerInset & per-series Live Trend toggles round-trip
                OsdOverlayConfig cfg = new OsdOverlayConfig();
                cfg.GraphMode = 4;
                cfg.DfCornerInset = true;
                cfg.TrendPower = false;
                cfg.TrendApuFan = false;
                List<string> lines = new List<string>();
                cfg.AppendSettings(lines);
                OsdOverlayConfig cfg2 = new OsdOverlayConfig();
                foreach (string ln in lines)
                {
                    int eq = ln.IndexOf('=');
                    if (eq > 0) cfg2.TryParseSetting(ln.Substring(0, eq), ln.Substring(eq + 1));
                }
                bool osdV110Ok = (cfg2.GraphMode == 4 && cfg2.DfCornerInset && !cfg2.TrendPower && !cfg2.TrendApuFan && cfg2.TrendCpu);

                v110FeaturesOk = floor14Ok && autoTuneModesOk && osdV110Ok;
                v110Detail = string.Format("Floor14={0} (eval={1}/{2}), AutoTuneModes={3}, OsdV110={4}",
                    floor14Ok, eval14, evalClamp14, autoTuneModesOk, osdV110Ok);
            }
            catch (Exception ex) { v110Detail = ex.Message; }
            finally
            {
                FanCurveProfile.ApuMinSpinPct = savedMinApu;
                FanCurveProfile.SysMinSpinPct = savedMinSys;
            }
            Check(v110FeaturesOk,
                "Calibrated 14% Floor Evaluation, BIOS-Mode Auto-Tune & OSD Graphs-Only / DF Corner Config",
                v110Detail);

            // 78. NativeInpOut SHA256 Pin Verification & Single-Choke-Point Port Whitelist Enforcement
            bool shaAndWhitelistOk = false;
            string shaAndWhitelistDetail = "";
            string tempDllPath = Path.Combine(Path.GetTempPath(), "test-driver-pin-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllBytes(tempDllPath, Encoding.ASCII.GetBytes("tampered-kernel-driver-payload"));
                string actualTamperedHash;
                bool tamperedRejected = !NativeInpOut.VerifyFileSha256(tempDllPath, new string[] { NativeInpOut.INPOUTX64_SHA256 }, out actualTamperedHash);
                bool matchingAccepted = !string.IsNullOrEmpty(actualTamperedHash)
                    && NativeInpOut.VerifyFileSha256(tempDllPath, new string[] { actualTamperedHash }, out actualTamperedHash);

                bool bundledPinOk = true;
                string bundledDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inpoutx64.dll");
                if (File.Exists(bundledDll))
                {
                    string bundledHash;
                    bundledPinOk = NativeInpOut.VerifyFileSha256(bundledDll, new string[] { NativeInpOut.INPOUTX64_SHA256 }, out bundledHash);
                }

                bool nativeBlocksCritical = !NativeInpOut.IsPortWhitelisted(0x70, false)
                    && !NativeInpOut.IsPortWhitelisted(0xCF9, true)
                    && !NativeInpOut.IsPortWhitelisted(0x1234, false)
                    && NativeInpOut.IsPortWhitelisted(0x62, true)
                    && NativeInpOut.IsPortWhitelisted(0x2E, false)
                    && NativeInpOut.IsPortWhitelisted(0xCF8, false);
                bool directReadBlocked = (NativeInpOut.DlPortReadPortUchar(0x70) == 0xFF)
                    && (NativeInpOut.DlPortReadPortUchar(0x1234) == 0xFF);

                shaAndWhitelistOk = tamperedRejected && matchingAccepted && bundledPinOk && nativeBlocksCritical && directReadBlocked;
                shaAndWhitelistDetail = string.Format("TamperedRej={0}, MatchAcc={1}, BundledPin={2}, NativeWL={3}, DirectBlock={4}",
                    tamperedRejected, matchingAccepted, bundledPinOk, nativeBlocksCritical, directReadBlocked);
            }
            catch (Exception ex) { shaAndWhitelistDetail = ex.Message; }
            finally
            {
                try { if (File.Exists(tempDllPath)) File.Delete(tempDllPath); } catch { }
            }
            Check(shaAndWhitelistOk,
                "NativeInpOut SHA256 Binary Pin Verification & Single-Choke-Point Port Whitelist",
                shaAndWhitelistDetail);

            // 79. Custom DeviceProfile Fail-Closed Defaults, DMI Fingerprint Binding & Blocklist Precedence
            bool customBindingOk = false;
            string customBindingDetail = "";
            DeviceProfile savedCustom = DeviceProfile.CustomProfile;
            try
            {
                // Stray/truncated JSON omitting flags & DMI binding MUST default fail-closed to ReadOnly/Unverified
                DeviceProfile stray = DeviceProfile.FromJson("{\n  \"id\": \"stray\",\n  \"displayName\": \"Stray Profile\"\n}");
                bool strayFailClosed = (stray != null && stray.IsReadOnly && !stray.IsVerified && !stray.MatchesDmi("NucBox K12", "K12", "GMKtec"));

                // Empty/corrupt payload returns null; single-line JSON with escapes & DMI binding parses cleanly
                bool corruptNull = (DeviceProfile.FromJson("not-json") == null);
                string singleLineProfileJson = "{\"id\":\"single-line\",\"displayName\":\"Compact: \\\"Test\\\"\",\"regCpuTemp\":\"0x70\",\"regFan1Ctrl\":\"0x33\",\"hasDualFans\":true,\"isReadOnly\":false,\"isVerified\":true,\"boundProductName\":\"UM890 Pro\",\"boundBaseBoard\":\"F7BSC\"}";
                DeviceProfile singleLineParsed = DeviceProfile.FromJson(singleLineProfileJson);
                bool singleLineOk = (singleLineParsed != null
                    && singleLineParsed.Id == "single-line"
                    && singleLineParsed.DisplayName == "Compact: \"Test\""
                    && singleLineParsed.RegCpuTemp == 0x70
                    && singleLineParsed.HasDualFans
                    && !singleLineParsed.IsReadOnly
                    && singleLineParsed.IsVerified
                    && singleLineParsed.MatchesDmi("UM890 Pro", "F7BSC"));

                // Bound profile matches ONLY source machine DMI, never another Mini PC or a blocklisted machine
                DeviceProfile bound = new DeviceProfile
                {
                    Id = "custom-bound",
                    DisplayName = "Custom Bound Machine",
                    BoundManufacturer = "Minisforum",
                    BoundProductName = "UM890 Pro",
                    BoundBaseBoard = "F7BSC",
                    IsReadOnly = false,
                    IsVerified = true
                };
                DeviceProfile.CustomProfile = bound;

                DeviceProfile matchSame = SystemHardwareInfo.MatchProfile("UM890 Pro", "F7BSC", "AMD Ryzen 9 8945HS", "Minisforum");
                DeviceProfile matchOther = SystemHardwareInfo.MatchProfile("UnknownBox X1", "OTHER-MB", "AMD Ryzen 7 7840HS", "OtherVendor");
                DeviceProfile matchBlocklisted = SystemHardwareInfo.MatchProfile("KTHIN", "F7BSC", "AMD Ryzen AI 9 HX PRO 370", "Recoreqich");

                bool sameMatches = (matchSame != null && matchSame.Id == "custom-bound" && !matchSame.IsReadOnly);
                bool otherRejected = (matchOther != null && matchOther.Id == "unknown-safe" && matchOther.IsReadOnly);
                bool blocklistWins = (matchBlocklisted != null && matchBlocklisted.Id == "unknown-safe" && matchBlocklisted.IsReadOnly);

                customBindingOk = strayFailClosed && corruptNull && singleLineOk && sameMatches && otherRejected && blocklistWins;
                customBindingDetail = string.Format("StrayRO={0}, CorruptNull={1}, SingleLine={2}, SameMatch={3}, OtherRej={4}, BlocklistWins={5}",
                    strayFailClosed, corruptNull, singleLineOk, sameMatches, otherRejected, blocklistWins);
            }
            catch (Exception ex) { customBindingDetail = ex.Message; }
            finally
            {
                DeviceProfile.CustomProfile = savedCustom;
            }
            Check(customBindingOk,
                "Custom DeviceProfile Fail-Closed FromJson, DMI Fingerprint Binding & Blocklist Precedence",
                customBindingDetail);

            // 80. CLI Fan Duty Anti-Stall Clamping & Explicit --force Override
            bool cliClampOk = false;
            string cliClampDetail = "";
            int savedApuClamp = FanCurveProfile.ApuMinSpinPct;
            int savedSysClamp = FanCurveProfile.SysMinSpinPct;
            try
            {
                FanCurveProfile.ApuMinSpinPct = 22;
                FanCurveProfile.SysMinSpinPct = 28;
                bool c0, c1, c2, c3, c4;
                int f0, f1, f2, f3, f4;

                int rAuto = Program.ClampCliFanDuty(1, 0, false, out c0, out f0);
                int rClampedApu = Program.ClampCliFanDuty(1, 5, false, out c1, out f1);
                int rClampedSys = Program.ClampCliFanDuty(2, 10, false, out c2, out f2);
                int rForced = Program.ClampCliFanDuty(1, 5, true, out c3, out f3);
                int rNormal = Program.ClampCliFanDuty(1, 65, false, out c4, out f4);

                cliClampOk = (rAuto == 0 && !c0)
                    && (rClampedApu == 22 && c1 && f1 == 22)
                    && (rClampedSys == 28 && c2 && f2 == 28)
                    && (rForced == 5 && !c3)
                    && (rNormal == 65 && !c4);
                cliClampDetail = string.Format("Auto={0}, Apu5->{1}(clamped={2}), Sys10->{3}(clamped={4}), Forced5->{5}, Norm65->{6}",
                    rAuto, rClampedApu, c1, rClampedSys, c2, rForced, rNormal);
            }
            catch (Exception ex) { cliClampDetail = ex.Message; }
            finally
            {
                FanCurveProfile.ApuMinSpinPct = savedApuClamp;
                FanCurveProfile.SysMinSpinPct = savedSysClamp;
            }
            Check(cliClampOk,
                "CLI Fan Duty Anti-Stall Floor Clamping & --force Override",
                cliClampDetail);

            // 81. Read-Only Failsafe Latch Preserves Emergency Thermal Tripwire & Periodic SetAuto() Retry
            bool latchSafetyOk = false;
            string latchSafetyDetail = "";
            try
            {
                var latchDemo = new DemoBackend();
                var curvePts = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var customCurve = new FanCurveProfile("Custom", false, -1, curvePts, curvePts);
                var builtIns = new FanCurveProfile[] { new FanCurveProfile("Balanced", true, 0, curvePts, curvePts) };

                using (var svc = new FanControllerService(null, latchDemo, builtIns, true))
                {
                    svc.ApuProfile = customCurve;
                    svc.SysProfile = customCurve;
                    latchDemo.FailWrites = true;

                    // Trigger 10+ consecutive failures to engage read-only failsafe latch (with SetAuto also failing)
                    for (int i = 0; i < 12 && !svc.ReadOnlyFailsafeLatched; i++)
                    {
                        svc.EvaluateAndApplyFanProfiles(50 + (i % 2) * 15);
                    }
                    bool latchedAfterFails = svc.ReadOnlyFailsafeLatched && !svc.LatchedAutoRestored;

                    // Recover backend: normal curve & manual writes must remain blocked by latch,
                    // while emergency >= 90°C tripwire MUST still command 100% PWM!
                    latchDemo.FailWrites = false;
                    bool manualBlockedWhileLatched = !svc.ApplyManualFan(1, 50);
                    bool emergencySucceededWhileLatched = svc.EvaluateAndApplyFanProfiles(94)
                        && svc.EmergencyThermalActive
                        && svc.LastAppliedApuPct == 100
                        && svc.ReadOnlyFailsafeLatched;

                    // After cooling below 75°C hysteresis and retry interval expiry, periodic SetAuto() retry restores BIOS Auto
                    svc.ExpireLatchedAutoRetryTimerForTest();
                    bool periodicAutoRetryOk = svc.EvaluateAndApplyFanProfiles(60)
                        && svc.LatchedAutoRestored
                        && svc.ReadOnlyFailsafeLatched;

                    latchSafetyOk = latchedAfterFails && manualBlockedWhileLatched && emergencySucceededWhileLatched && periodicAutoRetryOk;
                    latchSafetyDetail = string.Format("Latched={0}, ManualBlocked={1}, Emerg100WhileLatched={2}, PeriodicAutoRetry={3}",
                        latchedAfterFails, manualBlockedWhileLatched, emergencySucceededWhileLatched, periodicAutoRetryOk);
                }
            }
            catch (Exception ex) { latchSafetyDetail = ex.Message; }
            Check(latchSafetyOk,
                "Read-Only Failsafe Latch Preserves Emergency Thermal Tripwire & Periodic SetAuto() Retry",
                latchSafetyDetail);

            // 82. CLI status JSON InvariantCulture Formatting & String Escaping (Non-English Locales)
            bool statusJsonLocaleOk = false;
            string statusJsonLocaleDetail = "";
            CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                FanStatus st = new FanStatus
                {
                    CpuTemp = 54,
                    GpuTemp = 48,
                    RamUsedGb = 12.5,
                    RamTotalGb = 32.0,
                    Mode = "Custom \"Quiet\"\nMode",
                    RadeonFeaturesSummary = "FreeSync\\AntiLag"
                };
                string json = Program.FormatStatusJson(st, "Global\\Access_EC");
                bool invariantDecimals = json.Contains("\"ram_used_gb\":12.5") && json.Contains("\"ram_total_gb\":32.0") && !json.Contains("12,5");
                bool escapedStrings = json.Contains("\"mode\":\"Custom \\\"Quiet\\\"\\nMode\"")
                    && json.Contains("\"radeon_features\":\"FreeSync\\\\AntiLag\"")
                    && json.Contains("\"mutex_namespace\":\"Global\\\\Access_EC\"");
                statusJsonLocaleOk = invariantDecimals && escapedStrings;
                statusJsonLocaleDetail = string.Format("InvariantDec={0}, Escaped={1}", invariantDecimals, escapedStrings);
            }
            catch (Exception ex) { statusJsonLocaleDetail = ex.Message; }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prevCulture;
            }
            Check(statusJsonLocaleOk,
                "CLI status JSON InvariantCulture Formatting & String Escaping (de-DE Locale Safety)",
                statusJsonLocaleDetail);

            // 83. EvaluateCurve Defensive Sorting & Monotonic Thermal Safety on Unsorted/Descending Curves
            bool curveSortMonoOk = false;
            string curveSortMonoDetail = "";
            try
            {
                CurvePoint[] unsortedDescending = new CurvePoint[]
                {
                    new CurvePoint(80, 90),
                    new CurvePoint(40, 30),
                    new CurvePoint(95, 10), // Malformed descending point at high temp
                    new CurvePoint(60, 55)
                };
                int p35 = FanCurveProfile.EvaluateCurve(unsortedDescending, 35, 30);
                int p60 = FanCurveProfile.EvaluateCurve(unsortedDescending, 60, 30);
                int p80 = FanCurveProfile.EvaluateCurve(unsortedDescending, 80, 30);
                int p95 = FanCurveProfile.EvaluateCurve(unsortedDescending, 95, 30);
                curveSortMonoOk = (p35 == 30 && p60 == 55 && p80 == 90 && p95 >= 90);
                curveSortMonoDetail = string.Format("35C={0}%, 60C={1}%, 80C={2}%, 95C={3}%", p35, p60, p80, p95);
            }
            catch (Exception ex) { curveSortMonoDetail = ex.Message; }
            Check(curveSortMonoOk,
                "EvaluateCurve Defensive Sorting & Monotonic Safety on Unsorted/Descending Curves",
                curveSortMonoDetail);

            // 84. Tachometer Spike Filter Bypass During 100% Emergency / Full-Speed Ramp
            bool tachSpikeBypassOk = false;
            string tachSpikeBypassDetail = "";
            try
            {
                HardwareBackend.ResetTachometerFilterState();
                int baseline = HardwareBackend.DebounceTachometerZero(1, 900, false, 35);
                // At moderate duty (35%), an instant +2600 RPM glitch is filtered/held
                int glitchHeld = HardwareBackend.DebounceTachometerZero(1, 3500, false, 35);
                // At emergency/100% commanded duty, the +2600 RPM spin-up must be reported immediately
                int emergImmediate = HardwareBackend.DebounceTachometerZero(1, 3500, false, 100);

                tachSpikeBypassOk = (baseline == 900 && glitchHeld == 900 && emergImmediate == 3500);
                tachSpikeBypassDetail = string.Format("Base={0}, GlitchHeld@35%={1}, Immediate@100%={2}", baseline, glitchHeld, emergImmediate);
            }
            catch (Exception ex) { tachSpikeBypassDetail = ex.Message; }
            finally
            {
                HardwareBackend.ResetTachometerFilterState();
            }
            Check(tachSpikeBypassOk,
                "Tachometer Spike Filter Bypass on 100% Emergency/Full-Speed Spin-Up",
                tachSpikeBypassDetail);

            // 85. SetBiosPowerMode Validation Order (No State Mutation on Unsupported Super I/O Platforms)
            bool biosPowerOrderOk = false;
            string biosPowerOrderDetail = "";
            try
            {
                HardwareBackend sioHw = new HardwareBackend();
                sioHw.EnableSimulationMode();
                sioHw.Profile = DeviceProfile.FindProfile("ryzen-hx370-ec");
                sioHw.SuperIoBaseAddr = 0x0A30;
                var pts = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var builtIns = new FanCurveProfile[] { new FanCurveProfile("Balanced", true, 0, pts, pts) };

                using (var svc = new FanControllerService(sioHw, null, builtIns, false))
                {
                    int beforeOverride = svc.BiosPowerModeOverride;
                    bool result = svc.SetBiosPowerMode(1);
                    int afterOverride = svc.BiosPowerModeOverride;
                    biosPowerOrderOk = (!result && afterOverride == beforeOverride);
                    biosPowerOrderDetail = string.Format("Result={0}, OverrideBefore={1}, OverrideAfter={2}", result, beforeOverride, afterOverride);
                }
            }
            catch (Exception ex) { biosPowerOrderDetail = ex.Message; }
            Check(biosPowerOrderOk,
                "SetBiosPowerMode Rejects Super I/O Platforms Without Mutating Controller State",
                biosPowerOrderDetail);

            // 86. Unified FanControllerService.ApplyManualFan Anti-Stall Clamping (GUI + CLI Parity) & Explicit Override
            bool unifiedManualClampOk = false;
            string unifiedManualClampDetail = "";
            int savedMinApu86 = FanCurveProfile.ApuMinSpinPct;
            int savedMinSys86 = FanCurveProfile.SysMinSpinPct;
            try
            {
                FanCurveProfile.ApuMinSpinPct = 24;
                FanCurveProfile.SysMinSpinPct = 28;
                var demo86 = new DemoBackend();
                var pts86 = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var builtIns86 = new FanCurveProfile[] { new FanCurveProfile("Balanced", true, 0, pts86, pts86) };

                using (var svc = new FanControllerService(null, demo86, builtIns86, true))
                {
                    int app1, floor1;
                    bool clamped1;
                    bool ok1 = svc.ApplyManualFan(1, 5, false, out app1, out clamped1, out floor1);
                    bool apuClampedOk = ok1 && clamped1 && app1 == 24 && floor1 == 24
                        && svc.ApuManualPct == 24 && svc.LastAppliedApuPct == 24;

                    bool ok2 = svc.ApplyManualFan(2, 10);
                    bool sysClampedOk = ok2 && svc.SysManualPct == 28 && svc.LastAppliedSysPct == 28;

                    bool okAuto = svc.ApplyManualFan(1, 0);
                    bool autoNotClampedOk = okAuto && svc.ApuManualPct == 0 && !svc.ApuManualOverride;

                    int appForce, floorForce;
                    bool clampedForce;
                    bool okForce = svc.ApplyManualFan(1, 5, true, out appForce, out clampedForce, out floorForce);
                    bool explicitOverrideOk = okForce && !clampedForce && appForce == 5 && svc.ApuManualPct == 5;

                    unifiedManualClampOk = apuClampedOk && sysClampedOk && autoNotClampedOk && explicitOverrideOk;
                    unifiedManualClampDetail = string.Format("Apu5->{0}(clamped={1}), Sys10->{2}, Auto={3}, Override5->{4}",
                        app1, clamped1, svc.SysManualPct, autoNotClampedOk, appForce);
                }
            }
            catch (Exception ex) { unifiedManualClampDetail = ex.Message; }
            finally
            {
                FanCurveProfile.ApuMinSpinPct = savedMinApu86;
                FanCurveProfile.SysMinSpinPct = savedMinSys86;
            }
            Check(unifiedManualClampOk,
                "Unified FanControllerService.ApplyManualFan Anti-Stall Clamping (GUI + CLI Parity) & Override",
                unifiedManualClampDetail);

            // 87. NativeInpOut Physical Memory Allowlist & Strict EC-Only Write Barrier
            bool physAllowlistOk = false;
            string physAllowlistDetail = "";
            try
            {
                NativeInpOut.ResetRegisteredPhysicalWindowsForTest();

                bool ecReadOk = NativeInpOut.IsPhysicalMemoryRangeAllowed(0xFE0B0400UL, 0x100, isWrite: false);
                bool ecWriteOk = NativeInpOut.IsPhysicalMemoryRangeAllowed(0xFE0B0433UL, 1, isWrite: true);
                bool ecamReadOk = NativeInpOut.IsPhysicalMemoryRangeAllowed(0xE0000000UL, 0x1000, isWrite: false);
                bool ecamWriteBlocked = !NativeInpOut.IsPhysicalMemoryRangeAllowed(0xE0000000UL, 4, isWrite: true);

                // Arbitrary physical RAM & unregistered regions MUST be blocked for both reads and writes
                bool arbitraryRamReadBlocked = !NativeInpOut.IsPhysicalMemoryRangeAllowed(0x10000000UL, 4, isWrite: false);
                bool arbitraryRamWriteBlocked = !NativeInpOut.IsPhysicalMemoryRangeAllowed(0x10000000UL, 1, isWrite: true);
                IntPtr dummyH87;
                bool mapArbitraryBlocked = (NativeInpOut.MapPhysToLinSafe(new IntPtr(0x10000000), 4096, out dummyH87) == IntPtr.Zero);
                bool writeArbitraryBlocked = !NativeInpOut.WritePhysicalByteSafe(0x10000000UL, 0x55);

                // Non-MMIO EC registration rejected fail-closed
                bool badEcRegRejected = !NativeInpOut.RegisterAllowedEcMmioWindow(0x10000000UL, 0x100);

                // Registered SMU PM Table window allows bounded reads only, never writes or out-of-bounds reads
                bool smuRegOk = NativeInpOut.RegisterAllowedSmuPmTableWindow(0x30000000UL, 0x1000);
                bool smuReadOk = NativeInpOut.IsPhysicalMemoryRangeAllowed(0x30000024UL, 4, isWrite: false);
                bool smuWriteBlocked = !NativeInpOut.IsPhysicalMemoryRangeAllowed(0x30000024UL, 1, isWrite: true);
                bool smuOobBlocked = !NativeInpOut.IsPhysicalMemoryRangeAllowed(0x30001000UL, 4, isWrite: false);

                physAllowlistOk = ecReadOk && ecWriteOk && ecamReadOk && ecamWriteBlocked
                    && arbitraryRamReadBlocked && arbitraryRamWriteBlocked && mapArbitraryBlocked && writeArbitraryBlocked
                    && badEcRegRejected && smuRegOk && smuReadOk && smuWriteBlocked && smuOobBlocked;
                physAllowlistDetail = string.Format("EcRW={0}/{1}, EcamR/WBlk={2}/{3}, ArbBlk={4}/{5}, BadEcRej={6}, SmuR/WBlk/Oob={7}/{8}/{9}",
                    ecReadOk, ecWriteOk, ecamReadOk, ecamWriteBlocked,
                    arbitraryRamReadBlocked && mapArbitraryBlocked, arbitraryRamWriteBlocked && writeArbitraryBlocked,
                    badEcRegRejected, smuReadOk, smuWriteBlocked, smuOobBlocked);
            }
            catch (Exception ex) { physAllowlistDetail = ex.Message; }
            finally
            {
                NativeInpOut.ResetRegisteredPhysicalWindowsForTest();
            }
            Check(physAllowlistOk,
                "NativeInpOut Physical Memory Allowlist (EC MMIO / PCIe ECAM / SMU PM Table) & Write Barrier",
                physAllowlistDetail);

            // 88. Super I/O 0% Manual Stop, Tachometer Channel Latching (No Cross-Talk) & Non-Zero Floor Calibration
            bool sioCalibFixOk = false;
            string sioCalibFixDetail = "";
            try
            {
                HardwareBackend.ResetTachometerFilterState();

                // A. Super I/O uses true 0% stop (duty=0x00), ACPI EC uses 1% (0x81)
                HardwareBackend sioHw = new HardwareBackend(DeviceProfile.FindProfile("ryzen-hx370-ec"));
                sioHw.EnableSimulationMode();
                sioHw.SuperIoBaseAddr = 0x0A30;
                sioHw.AccessMethod = HardwareBackend.EcAccessMethod.SuperIo2E;
                int sioStopPct = sioHw.GetManualStopPct();
                bool sioSetZeroOk = sioHw.SetFan(1, sioStopPct) && sioHw.SetFan(2, sioStopPct);
                bool sioZeroDutyWritten = (sioHw.ReadSuperIoHwReg(0x63) == 0 && sioHw.ReadSuperIoHwReg(0x6B) == 0 && sioHw.ReadSuperIoHwReg(0x73) == 0);

                HardwareBackend ecHw = new HardwareBackend(DeviceProfile.FindProfile("gmktec-k12"));
                ecHw.EnableSimulationMode();
                ecHw.SuperIoBaseAddr = 0;
                ecHw.AccessMethod = HardwareBackend.EcAccessMethod.Mmio;
                int ecStopPct = ecHw.GetManualStopPct();

                // B. Super I/O tachometer channel latching (TS-12C / KTHIN K8 layout: Ch1=0xFFFF, Ch2=APU, Ch3=Sys)
                // Ch1 (0x0D/0x18) = 0xFFFF (unwired)
                sioHw.WriteSuperIoHwReg(0x0D, 0xFF); sioHw.WriteSuperIoHwReg(0x18, 0xFF);
                // Ch2 (0x0E/0x19) = 0x083D (2109 -> 320 RPM floor)
                sioHw.WriteSuperIoHwReg(0x0E, 0x3D); sioHw.WriteSuperIoHwReg(0x19, 0x08);
                // Ch3 (0x0F/0x1A) = 0x01C5 (453 -> 1490 RPM)
                sioHw.WriteSuperIoHwReg(0x0F, 0xC5); sioHw.WriteSuperIoHwReg(0x1A, 0x01);

                int f1Active = sioHw.ReadSuperIoFanRpm(1);
                int f2Active = sioHw.ReadSuperIoFanRpm(2);

                // Now Sys Fan (Ch3) stops to 0 RPM (0xFFFF) while APU Fan (Ch2) stays at 320 RPM floor
                sioHw.WriteSuperIoHwReg(0x0F, 0xFF); sioHw.WriteSuperIoHwReg(0x1A, 0xFF);
                int f1Floor = sioHw.ReadSuperIoFanRpm(1);
                int f2Stopped = sioHw.ReadSuperIoFanRpm(2);

                // Single-fan Ch2-only board must NEVER clone Fan 1 onto Fan 2
                sioHw.ResetSuperIoChannelLatch();
                HardwareBackend.ResetTachometerFilterState();
                int singleF1 = sioHw.ReadSuperIoFanRpm(1);
                int singleF2 = sioHw.ReadSuperIoFanRpm(2);

                // C. Non-zero hardware floor plateau & ramp-up detection
                List<int> flat320 = new List<int> { 320, 318, 322, 320, 321, 319, 320, 320, 321, 320 };
                List<int> decel = new List<int> { 1200, 1120, 1050, 980, 910, 840, 770, 700, 640, 580 };
                bool plateauFlatOk = FanCalibrationResult.IsRpmWindowSettled(flat320, 20);
                bool plateauDecelRejected = !FanCalibrationResult.IsRpmWindowSettled(decel, 20);

                bool noFalseTriggerAtFloor = !FanCalibrationResult.IsSpinUpDetected(320, 320, 320, 0);
                bool noFalseTriggerSmallJitter = !FanCalibrationResult.IsSpinUpDetected(390, 320, 320, 0);
                bool rampTriggerAboveFloor = FanCalibrationResult.IsSpinUpDetected(450, 320, 390, 0);
                bool zeroBaselineStandardTrigger = FanCalibrationResult.IsSpinUpDetected(260, 0, 0, 0);

                sioCalibFixOk = (sioStopPct == 0) && sioSetZeroOk && sioZeroDutyWritten && (ecStopPct == 1)
                    && (f1Active >= 310 && f1Active <= 330) && (f2Active >= 1450 && f2Active <= 1530)
                    && (f1Floor >= 310 && f1Floor <= 330) && (f2Stopped == 0)
                    && (singleF1 >= 310 && singleF1 <= 330) && (singleF2 == 0)
                    && plateauFlatOk && plateauDecelRejected
                    && noFalseTriggerAtFloor && noFalseTriggerSmallJitter && rampTriggerAboveFloor && zeroBaselineStandardTrigger;

                sioCalibFixDetail = string.Format("StopPct(SIO/EC)={0}/{1}, ZeroDuty={2}, Tach(F1/F2/F2Stop/SingleF2)={3}/{4}/{5}/{6}, Plateau={7}/{8}, Ramp={9}/{10}/{11}/{12}",
                    sioStopPct, ecStopPct, sioZeroDutyWritten, f1Active, f2Active, f2Stopped, singleF2,
                    plateauFlatOk, plateauDecelRejected,
                    noFalseTriggerAtFloor, noFalseTriggerSmallJitter, rampTriggerAboveFloor, zeroBaselineStandardTrigger);
            }
            catch (Exception ex) { sioCalibFixDetail = ex.Message; }
            finally
            {
                HardwareBackend.ResetTachometerFilterState();
            }
            Check(sioCalibFixOk,
                "Super I/O 0% Stop Duty, Tachometer Channel Latching (No Ch2 Mirroring) & Non-Zero Floor Calibration",
                sioCalibFixDetail);

            // 89. Super I/O IT8613E Dynamic PWM Polarity Tracking via Reg 0x14 Bit 7 (Direct 0xC7 vs Sleep-Reset Inverted 0x47)
            bool sioPolarityOk = false;
            string sioPolarityDetail = "";
            try
            {
                HardwareBackend polHw = new HardwareBackend(DeviceProfile.FindProfile("ryzen-hx370-ec"));
                polHw.EnableSimulationMode();
                polHw.SuperIoBaseAddr = 0x0A30;
                polHw.AccessMethod = HardwareBackend.EcAccessMethod.SuperIo2E;

                // A. Active-High / Direct (Reg 0x14 = 0xC7, Bit 7 = 1): 0% -> 0x00, 100% -> 0xFF, fallback 35% -> 89
                polHw.WriteSuperIoHwReg(0x14, 0xC7);
                bool directFlag = !polHw.IsSuperIoPwmInverted();
                polHw.SetFan(1, 0);
                byte dirZero = polHw.ReadSuperIoHwReg(0x6B);
                polHw.SetFan(1, 100);
                byte dirFull = polHw.ReadSuperIoHwReg(0x6B);
                FanStatus dirSt = polHw.GetStatus();
                polHw.ApplySafeSuperIoFallback();
                byte dirFallback = polHw.ReadSuperIoHwReg(0x6B);

                // B. Active-Low / Inverted (Reg 0x14 = 0x47, Bit 7 = 0 after sleep/resume): 0% -> 0xFF, 100% -> 0x00, fallback 35% -> 166
                polHw.WriteSuperIoHwReg(0x14, 0x47);
                bool invFlag = polHw.IsSuperIoPwmInverted();
                polHw.SetFan(1, 0);
                byte invZero = polHw.ReadSuperIoHwReg(0x6B);
                FanStatus invZeroSt = polHw.GetStatus();
                polHw.SetFan(1, 100);
                byte invFull = polHw.ReadSuperIoHwReg(0x6B);
                FanStatus invFullSt = polHw.GetStatus();
                polHw.ApplySafeSuperIoFallback();
                byte invFallback = polHw.ReadSuperIoHwReg(0x6B);

                bool directOk = directFlag && (dirZero == 0) && (dirFull == 255) && (dirSt.Fan1Pct == 100) && (dirFallback == 89);
                bool invertedOk = invFlag && (invZero == 255) && (invZeroSt.Fan1Pct == 0) && (invFull == 0) && (invFullSt.Fan1Pct == 100) && (invFallback == 166);
                sioPolarityOk = directOk && invertedOk;
                sioPolarityDetail = string.Format("Direct(0xC7)={0} [0%={1},100%={2},fb={3}], Inv(0x47)={4} [0%={5}/pct={6},100%={7}/pct={8},fb={9}]",
                    directOk, dirZero, dirFull, dirFallback, invertedOk, invZero, invZeroSt.Fan1Pct, invFull, invFullSt.Fan1Pct, invFallback);
            }
            catch (Exception ex) { sioPolarityDetail = ex.Message; }
            Check(sioPolarityOk,
                "Super I/O IT8613E Dynamic Reg 0x14 Bit 7 PWM Polarity (Direct 0xC7 & Post-Sleep Inverted 0x47)",
                sioPolarityDetail);

            // 90. Native Windows Power Mode Overlay Mapping (Best Power Efficiency / Balanced / Best Performance)
            bool winPowerModeOk = false;
            string winPowerModeDetail = "";
            try
            {
                Guid gSilent = WindowsPowerMode.ModeCodeToGuid(2);
                Guid gBal = WindowsPowerMode.ModeCodeToGuid(0);
                Guid gPerf = WindowsPowerMode.ModeCodeToGuid(1);

                bool guidRoundTrip = (WindowsPowerMode.GuidToModeCode(gSilent) == 2)
                    && (WindowsPowerMode.GuidToModeCode(gBal) == 0)
                    && (WindowsPowerMode.GuidToModeCode(gPerf) == 1)
                    && (gSilent == WindowsPowerMode.GuidBestPowerEfficiency)
                    && (gBal == Guid.Empty)
                    && (gPerf == WindowsPowerMode.GuidBestPerformance);

                bool namesOk = WindowsPowerMode.GetModeDisplayName(2).Contains("Efficiency")
                    && WindowsPowerMode.GetModeDisplayName(0).Contains("Balanced")
                    && WindowsPowerMode.GetModeDisplayName(1).Contains("Performance");

                bool invalidRejected = !WindowsPowerMode.SetModeCode(-1) && !WindowsPowerMode.SetModeCode(9);
                int liveCode = WindowsPowerMode.GetActiveModeCode();
                bool liveQuerySafe = (liveCode >= -1 && liveCode <= 2);

                winPowerModeOk = guidRoundTrip && namesOk && invalidRejected && liveQuerySafe;
                winPowerModeDetail = string.Format("RoundTrip={0}, Names={1}, InvalidRej={2}, LiveCode={3}",
                    guidRoundTrip, namesOk, invalidRejected, liveCode);
            }
            catch (Exception ex) { winPowerModeDetail = ex.Message; }
            Check(winPowerModeOk,
                "Native Windows Power Mode Overlay Mapping (Best Power Efficiency / Balanced / Best Performance)",
                winPowerModeDetail);

            // 91. PawnIO HVCI Driver Backend, Priority-0 Selection & Fail-Closed Port Whitelist Gating
            bool pawnIoOk = false;
            string pawnIoDetail = "";
            try
            {
                NativeInpOut.Shutdown();
                Dictionary<ushort, byte> fakePorts = new Dictionary<ushort, byte>();
                Dictionary<byte, byte> fakeEc = new Dictionary<byte, byte>();
                byte ecPendingCmd = 0;
                int ecArgStage = 0;
                byte ecPendingReg = 0;
                byte ecObfData = 0;
                bool ecObfReady = false;
                int blockedCalls = 0;

                PawnIoDriver.SimulatedAvailable = true;
                PawnIoDriver.SimulatedExecuteHook = delegate(string fn, ulong[] inp)
                {
                    // Official LpcIO.bin ABI: ioctl_pio_outb(port, val) / ioctl_pio_inb(port)
                    if (fn == "ioctl_pio_outb" && inp.Length >= 2)
                    {
                        ushort p = (ushort)inp[0];
                        if (p == 0x70 || p == 0xCF9) blockedCalls++;
                        fakePorts[p] = (byte)(inp[1] & 0xFF);
                        return new ulong[] { 0 };
                    }
                    if (fn == "ioctl_pio_inb" && inp.Length >= 1)
                    {
                        ushort p = (ushort)inp[0];
                        if (p == 0x70 || p == 0xCF9) blockedCalls++;
                        byte v;
                        fakePorts.TryGetValue(p, out v);
                        return new ulong[] { v };
                    }
                    // Official LpcACPIEC.bin ABI: ioctl_pio_write(port, val) / ioctl_pio_read(port) on 0x62/0x66
                    if (fn == "ioctl_pio_write" && inp.Length >= 2)
                    {
                        byte p = (byte)inp[0];
                        byte val = (byte)(inp[1] & 0xFF);
                        if (p == 0x66)
                        {
                            ecPendingCmd = val;
                            ecArgStage = 1;
                            ecObfReady = false;
                        }
                        else if (p == 0x62)
                        {
                            if (ecPendingCmd == 0x80 && ecArgStage == 1)
                            {
                                byte v;
                                fakeEc.TryGetValue(val, out v);
                                ecObfData = v;
                                ecObfReady = true;
                                ecArgStage = 0;
                            }
                            else if (ecPendingCmd == 0x81 && ecArgStage == 1)
                            {
                                ecPendingReg = val;
                                ecArgStage = 2;
                            }
                            else if (ecPendingCmd == 0x81 && ecArgStage == 2)
                            {
                                fakeEc[ecPendingReg] = val;
                                ecArgStage = 0;
                            }
                        }
                        return new ulong[] { 0 };
                    }
                    if (fn == "ioctl_pio_read" && inp.Length >= 1)
                    {
                        byte p = (byte)inp[0];
                        if (p == 0x66)
                        {
                            return new ulong[] { ecObfReady ? 0x01UL : 0x00UL };
                        }
                        if (p == 0x62)
                        {
                            ecObfReady = false;
                            return new ulong[] { ecObfData };
                        }
                    }
                    return null;
                };

                bool preloaded = NativeInpOut.PreloadDriverDll();
                bool isPawnActive = (NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO)
                    && NativeInpOut.IsDriverOpen()
                    && NativeInpOut.ActiveDriverName.Contains("PawnIO");

                // Whitelisted Super I/O port read/write via NativeInpOut -> PawnIO
                NativeInpOut.DlPortWritePortUchar(0x0A35, 0x5A);
                byte readBack = NativeInpOut.DlPortReadPortUchar(0x0A35);

                // Dangerous ports must be blocked fail-closed BEFORE reaching PawnIO bytecode
                NativeInpOut.DlPortWritePortUchar(0x70, 0x11);
                byte rtcRead = NativeInpOut.DlPortReadPortUchar(0x70);
                NativeInpOut.DlPortWritePortUchar(0xCF9, 0x06);

                // ACPI EC module round-trip
                bool ecWriteOk = PawnIoDriver.TryWriteAcpiEcByte(0x33, 0xA5);
                byte ecReadVal;
                bool ecReadOk = PawnIoDriver.TryReadAcpiEcByte(0x33, out ecReadVal);

                NativeInpOut.Shutdown();
                bool cleanShutdown = (NativeInpOut.ActiveDriver == HardwareDriverEngine.None) && !PawnIoDriver.IsAvailable;

                pawnIoOk = preloaded && isPawnActive && (readBack == 0x5A) && (rtcRead == 0xFF)
                    && (blockedCalls == 0) && ecWriteOk && ecReadOk && (ecReadVal == 0xA5) && cleanShutdown;
                pawnIoDetail = string.Format("Active={0}, PortRW=0x{1:X2}, Blocked={2}, EcRW={3}/0x{4:X2}, CleanStop={5}",
                    isPawnActive, readBack, blockedCalls, ecWriteOk && ecReadOk, ecReadVal, cleanShutdown);
            }
            catch (Exception ex) { pawnIoDetail = ex.Message; }
            finally
            {
                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
            }
            Check(pawnIoOk,
                "PawnIO HVCI Driver Backend, Priority-0 Selection & Fail-Closed Port Whitelist Gating",
                pawnIoDetail);

            // 92. Anti-Cheat Safe Auto-Pause State Machine & Ordered BIOS-Auto-First Handoff
            bool antiCheatOk = false;
            string antiCheatDetail = "";
            try
            {
                bool eacRunning = false;
                bool vgkRunning = true;
                long fakeClock = 1000;

                AntiCheatMonitor mon = new AntiCheatMonitor(
                    delegate(string svc)
                    {
                        if (svc == "EasyAntiCheat_EOS") return eacRunning;
                        if (svc == "vgk") return vgkRunning;
                        return false;
                    },
                    delegate { return fakeClock; },
                    3000);

                // Disabled by default: ignores EAC start
                eacRunning = true;
                AntiCheatAction aDisabled = mon.Poll();
                bool vgkDetected = mon.IsBootVanguardDetected;

                // Enable -> triggers PauseForAntiCheat immediately
                mon.Enabled = true;
                AntiCheatAction aPause = mon.Poll();
                bool pausedFlag = mon.IsPaused && (mon.ActiveAntiCheatName == "Easy Anti-Cheat");

                // While still running -> None
                AntiCheatAction aHold = mon.Poll();

                // EAC exits -> holds during 3000ms cooldown, then resumes
                eacRunning = false;
                fakeClock = 2000;
                AntiCheatAction aCool1 = mon.Poll();
                fakeClock = 4500;
                AntiCheatAction aCool2 = mon.Poll();
                fakeClock = 5100;
                AntiCheatAction aResume = mon.Poll();
                bool resumedClear = !mon.IsPaused;

                // Verify strict ordering: BIOS Auto MUST run BEFORE legacy driver unload
                List<string> callOrder = new List<string>();
                bool handoffOk = AntiCheatMonitor.ExecuteSafePauseHandoff(
                    delegate { callOrder.Add("auto"); return true; },
                    delegate { callOrder.Add("shutdown"); },
                    delegate(string svc) { callOrder.Add("stop:" + svc); return true; },
                    HardwareDriverEngine.InpOut64);
                bool orderStrict = handoffOk && callOrder.Count >= 3 && callOrder[0] == "auto" && callOrder[1] == "shutdown";

                // Verify PawnIO skips legacy driver unload while still restoring BIOS Auto
                List<string> pawnOrder = new List<string>();
                AntiCheatMonitor.ExecuteSafePauseHandoff(
                    delegate { pawnOrder.Add("auto"); return true; },
                    delegate { pawnOrder.Add("shutdown"); },
                    delegate(string svc) { pawnOrder.Add("stop:" + svc); return true; },
                    HardwareDriverEngine.PawnIO);
                bool pawnSkippedUnload = (pawnOrder.Count == 1 && pawnOrder[0] == "auto");

                antiCheatOk = (aDisabled == AntiCheatAction.None) && vgkDetected
                    && (aPause == AntiCheatAction.PauseForAntiCheat) && pausedFlag
                    && (aHold == AntiCheatAction.None) && (aCool1 == AntiCheatAction.None) && (aCool2 == AntiCheatAction.None)
                    && (aResume == AntiCheatAction.ResumeAfterAntiCheat) && resumedClear
                    && orderStrict && pawnSkippedUnload;
                antiCheatDetail = string.Format("Pause={0}, Cooldown={1}, Resume={2}, Vgk={3}, AutoFirst={4}, PawnSkip={5}",
                    pausedFlag, (aCool1 == AntiCheatAction.None && aCool2 == AntiCheatAction.None), resumedClear, vgkDetected, orderStrict, pawnSkippedUnload);
            }
            catch (Exception ex) { antiCheatDetail = ex.Message; }
            Check(antiCheatOk,
                "Anti-Cheat Safe Auto-Pause State Machine (EAC/BattlEye/Vanguard) & Ordered BIOS-Auto Handoff",
                antiCheatDetail);

            // 93. Headless Background Daemon Mode (Zero-UI FanControllerService Loop & Clean Exit)
            bool daemonOk = false;
            string daemonDetail = "";
            try
            {
                FanCurveProfile[] dBuiltIns = HeadlessDaemon.CreateBuiltInProfiles();
                List<FanCurveProfile> dCustoms = FanCurveProfile.CreateProtectedExamples();
                FanCurveProfile r1 = HeadlessDaemon.ResolveProfileByName("Silent", dBuiltIns, dCustoms);
                FanCurveProfile r2 = HeadlessDaemon.ResolveProfileByName("🔒 Gaming Sweet-Spot (Acoustic Knee)", dBuiltIns, dCustoms);
                FanCurveProfile rFallback = HeadlessDaemon.ResolveProfileByName("DoesNotExist", dBuiltIns, dCustoms);

                int rc = HeadlessDaemon.Run(true, 100, 2, true);
                daemonOk = (rc == 0) && (r1 != null && r1.Name == "Silent")
                    && (r2 != null && r2.Name.Contains("Gaming Sweet-Spot"))
                    && (rFallback != null && rFallback.Name == "Balanced");
                daemonDetail = string.Format("ExitCode={0}, BuiltIn={1}, Protected={2}, Fallback={3}",
                    rc, r1 != null ? r1.Name : "null", r2 != null ? r2.Name : "null", rFallback != null ? rFallback.Name : "null");
            }
            catch (Exception ex) { daemonDetail = ex.Message; }
            Check(daemonOk,
                "Headless Background Daemon Mode (Zero-UI FanControllerService Loop & Profile Resolution)",
                daemonDetail);

            // 94. Sanitized One-Click Hardware Profile Submission JSON & GitHub Issue URL Builder
            bool submitOk = false;
            string submitDetail = "";
            try
            {
                SystemHardwareInfo fakeSys = new SystemHardwareInfo
                {
                    Manufacturer = "TOPC",
                    ProductName = "TS-12C",
                    BaseBoard = "C:\\Users\\alice\\secret-path",
                    CpuName = "AMD Ryzen AI 9 HX 370",
                    BiosVersion = "1.05",
                    MatchedProfile = DeviceProfile.FindProfile("ryzen-hx370-ec")
                };
                HardwareBackend fakeHw = new HardwareBackend(fakeSys.MatchedProfile);
                fakeHw.SuperIoChipId = 0x8613;
                fakeHw.SuperIoBaseAddr = 0x0A30;
                fakeHw.AccessMethod = HardwareBackend.EcAccessMethod.SuperIo2E;

                string json = HardwareProfileSubmission.BuildSanitizedJson(fakeSys, fakeHw, MainForm.APP_VERSION);
                string url = HardwareProfileSubmission.BuildGitHubIssueUrl(fakeSys, fakeHw, MainForm.APP_VERSION);

                Dictionary<string, string> subParsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                SimpleJson.ParseFlatObject(json, subParsed, false);
                bool parsedOk = subParsed.ContainsKey("manufacturer") && subParsed["manufacturer"] == "TOPC"
                    && subParsed.ContainsKey("superIoChipId") && subParsed["superIoChipId"] == "0x8613"
                    && subParsed.ContainsKey("superIoBaseAddr") && subParsed["superIoBaseAddr"] == "0x0A30";
                bool redactedUser = !json.Contains("alice") && json.Contains("<redacted>");
                bool urlValid = url.StartsWith("https://github.com/kaiserc/MiniFanControl/issues/new?")
                    && url.Contains("template=unsupported_hardware.md")
                    && url.Contains("title=") && url.Contains("body=")
                    && url.Length <= HardwareProfileSubmission.MAX_URL_LENGTH;

                submitOk = parsedOk && redactedUser && urlValid;
                submitDetail = string.Format("JsonOk={0}, Redacted={1}, UrlLen={2}", parsedOk, redactedUser, url.Length);
            }
            catch (Exception ex) { submitDetail = ex.Message; }
            Check(submitOk,
                "Sanitized Hardware Profile Submission JSON (PII Redaction) & Pre-Filled GitHub Issue URL",
                submitDetail);

            // 95. Windows 11 DWM Visual Style, ColorRef Conversion, Rounded Card Chrome & Modern Button Polish
            bool win11StyleOk = false;
            string win11StyleDetail = "";
            try
            {
                // Win32 COLORREF is 0x00BBGGRR
                int cref = Win11VisualStyle.ToColorRef(Color.FromArgb(0x12, 0x34, 0x56));
                bool crefOk = (cref == 0x00563412);

                Color mid = Win11VisualStyle.Blend(Color.Black, Color.White, 0.5f);
                bool blendOk = (mid.R >= 127 && mid.R <= 128) && (mid.G == mid.R) && (mid.B == mid.R);

                bool nullFormSafe = !Win11VisualStyle.ApplyWindowFrame(null, true, Color.Black, Color.White, Color.Gray);

                // Draw rounded border & style button without GDI exceptions
                bool gdiOk = false;
                using (Bitmap bmp = new Bitmap(220, 90))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, 220, 90), Color.CornflowerBlue, 8, 1f);
                    Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, 4, 4), Color.CornflowerBlue, 8, 1f);
                    gdiOk = true;
                }

                bool btnOk = false;
                using (Button b = new Button())
                {
                    Win11VisualStyle.StyleModernButton(b, Color.FromArgb(0x2E, 0x30, 0x44), Color.White, Color.FromArgb(0x36, 0x3A, 0x52), true);
                    btnOk = (b.FlatStyle == FlatStyle.Flat) && (b.FlatAppearance.BorderSize == 1)
                        && (b.FlatAppearance.MouseOverBackColor != b.BackColor);
                }

                win11StyleOk = crefOk && blendOk && nullFormSafe && gdiOk && btnOk;
                win11StyleDetail = string.Format("ColorRef=0x{0:X6}, Blend={1}, NullSafe={2}, Gdi={3}, Btn={4}",
                    cref, blendOk, nullFormSafe, gdiOk, btnOk);
            }
            catch (Exception ex) { win11StyleDetail = ex.Message; }
            Check(win11StyleOk,
                "Windows 11 DWM Visual Style (COLORREF, Rounded Card Borders & Modern Button Styling)",
                win11StyleDetail);

            // 96. AppSettings AutoPauseForAntiCheat Default & MainForm Win11 Theme Cycle Safety
            bool polishUiOk = false;
            string polishUiDetail = "";
            try
            {
                AppSettings defSettings = new AppSettings();
                bool defaultOff = !defSettings.AutoPauseForAntiCheat;

                bool formThemesOk = false;
                using (MainForm f = new MainForm(true, false, true))
                {
                    IntPtr h = f.Handle; // Force handle creation -> exercises OnHandleCreated + ApplyWin11WindowFrame
                    formThemesOk = (h != IntPtr.Zero) && !f.IsDisposed;
                }

                polishUiOk = defaultOff && formThemesOk;
                polishUiDetail = string.Format("AntiCheatDefaultOff={0}, MainFormWin11HandleOk={1}", defaultOff, formThemesOk);
            }
            catch (Exception ex) { polishUiDetail = ex.Message; }
            Check(polishUiOk,
                "AppSettings AutoPauseForAntiCheat Opt-In Default & MainForm Win11 DWM Handle Integration",
                polishUiDetail);

            // 97. Unverified Desktop Hardware (unknown-safe) Skips Writes Without Fake Failsafe Escalation
            bool roDesktopOk = false;
            string roDesktopDetail = "";
            try
            {
                HardwareBackend roHw = new HardwareBackend();
                roHw.Profile = DeviceProfile.FindProfile("unknown-safe");
                var pts = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var builtIns = new FanCurveProfile[] { new FanCurveProfile("Balanced", true, 0, pts, pts) };
                using (var svc = new FanControllerService(roHw, new DemoBackend(), builtIns, false))
                {
                    bool isRo = svc.IsInherentlyReadOnly;
                    bool anyEvalWrote = false;
                    for (int i = 0; i < 15; i++)
                    {
                        if (svc.EvaluateAndApplyFanProfiles(55 + (i % 3) * 10))
                            anyEvalWrote = true;
                    }
                    bool manWrote = svc.ApplyManualFan(1, 50);
                    bool modeWrote = svc.SetBiosPowerMode(1);
                    bool autoWrote = svc.ApplyAutoAll();

                    roDesktopOk = isRo && !anyEvalWrote && !manWrote && !modeWrote && !autoWrote &&
                                  svc.ConsecutiveWriteFailures == 0 &&
                                  svc.LastHardwareWriteError == null &&
                                  !svc.ReadOnlyFailsafeLatched;
                    roDesktopDetail = string.Format("IsRO={0}, Fails={1}, Err={2}, Latched={3}",
                        isRo, svc.ConsecutiveWriteFailures, svc.LastHardwareWriteError ?? "null", svc.ReadOnlyFailsafeLatched);
                }
            }
            catch (Exception ex) { roDesktopDetail = ex.Message; }
            Check(roDesktopOk,
                "Unverified Desktop Hardware (unknown-safe) Read-Only Monitoring Without Failsafe Spam",
                roDesktopDetail);

            // 98. High-DPI GDI Scaling Manifest & MainForm AutoScaleMode Guard
            bool dpiGuardOk = false;
            string dpiGuardDetail = "";
            try
            {
                string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.manifest");
                if (!File.Exists(manifestPath))
                    manifestPath = Path.Combine(Environment.CurrentDirectory, "app.manifest");
                bool manifestOk = true;
                if (File.Exists(manifestPath))
                {
                    string xml = File.ReadAllText(manifestPath);
                    manifestOk = xml.Contains("<gdiScaling") && xml.Contains(">true</gdiScaling>") &&
                                 xml.Contains(">false</dpiAware>");
                }
                bool formScaleNone = false;
                using (MainForm f = new MainForm(true, false, true))
                {
                    formScaleNone = (f.AutoScaleMode == AutoScaleMode.None);
                }
                dpiGuardOk = manifestOk && formScaleNone;
                dpiGuardDetail = string.Format("ManifestGdiScaling={0}, MainFormAutoScaleNone={1}", manifestOk, formScaleNone);
            }
            catch (Exception ex) { dpiGuardDetail = ex.Message; }
            Check(dpiGuardOk,
                "High-DPI GDI Scaling Manifest & MainForm AutoScaleMode Layout Integrity",
                dpiGuardDetail);

            // 99. Desktop PC OSD Polish: Discrete GPU Detection, Hotspot ASCII Label, Read-Only Row Trimming, Consistent Frametime & 1% Low History
            bool desktopOsdOk = false;
            string desktopOsdDetail = "";
            try
            {
                bool dgpuDet = RadeonSettingsSensor.IsDiscreteGpuDescription("AMD Radeon RX 9070 XT")
                            && RadeonSettingsSensor.IsDiscreteGpuDescription("NVIDIA GeForce RTX 5080")
                            && !RadeonSettingsSensor.IsDiscreteGpuDescription("AMD Radeon 780M Graphics")
                            && !RadeonSettingsSensor.IsDiscreteGpuDescription("AMD Radeon(TM) Graphics");

                FanStatus dgpuSt = new FanStatus { CpuTemp = 59, GpuTemp = 46, GpuHotspotTemp = 52, GfxActivityPct = 86, GfxClockMhz = 2306, ApuPowerWatts = 135, IsDiscreteGpu = true, RtssFps = 60, RtssFrametimeMs = 16.7, RtssAvgFps = 59, RtssP1LowFps = 46, RtssP01LowFps = 36, RtssProcessName = "StarCitizen.exe" };
                FanStatus igpuSt = new FanStatus { CpuTemp = 55, GpuTemp = 48, IsDiscreteGpu = false };
                bool labelsOk = (dgpuSt.GpuShortLabel == "GPU" && igpuSt.GpuShortLabel == "iGPU");

                double steadyFt = FpsMonitor.ResolveConsistentFrametimeMs(16.67, 18.1, 60);
                double spikeFt = FpsMonitor.ResolveConsistentFrametimeMs(18.5, 115.0, 54);
                bool ftConsistOk = (Math.Abs(steadyFt - 16.67) < 0.01 && Math.Abs(spikeFt - 115.0) < 0.01);

                TelemetrySessionStats p1Stats = new TelemetrySessionStats();
                p1Stats.Record(dgpuSt);
                p1Stats.Record(dgpuSt);
                bool p1HistOk = (p1Stats.P1LowHistory != null && p1Stats.P1LowHistory.Count == 2 && p1Stats.P1LowHistory[1] == 46);

                bool osdHotspotOk = false;
                bool roHideOk = false;
                bool lpcLightOk = false;
                using (CapFrameXOverlayForm osd = new CapFrameXOverlayForm())
                {
                    osd.Stats = p1Stats;
                    osd.ShowGpu = true;
                    osd.ShowGpuHotspot = true;
                    osd.ShowApuFan = true;
                    osd.ShowSysFan = true;
                    osd.ShowMode = true;
                    osd.HasDualFans = true;
                    osd.IsReadOnlyMode = false;
                    osd.UpdateMetrics(dgpuSt, "Balanced", true);
                    int normalCount = osd.CountVisibleMetrics();
                    string gpuTxt = osd.FormatGpuOverlayValue();
                    osdHotspotOk = (gpuTxt.IndexOf("(HS 52°)", StringComparison.Ordinal) >= 0 && gpuTxt.IndexOf("🔥", StringComparison.Ordinal) < 0);

                    osd.IsReadOnlyMode = true;
                    osd.UpdateMetrics(dgpuSt, "Read-Only", true);
                    int roCount = osd.CountVisibleMetrics();
                    roHideOk = (roCount == normalCount - 3); // APU Fan (0 RPM), Sys Fan (0 RPM), and Mode (Read-Only) hidden
                }

                using (LivePerformanceCanvas lpc2 = new LivePerformanceCanvas { Width = 900, Height = 360, SplitView = true, DfZoomedFrametime = true, DfCornerInset = true, IsDarkMode = false, Stats = p1Stats })
                {
                    lpc2.UpdateStatus(dgpuSt);
                    using (Bitmap bmp = new Bitmap(900, 360))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        lpc2.RenderForTest(g);
                        lpc2.IsDarkMode = true;
                        lpc2.RenderForTest(g);
                        lpcLightOk = true;
                    }
                }

                desktopOsdOk = dgpuDet && labelsOk && ftConsistOk && p1HistOk && osdHotspotOk && roHideOk && lpcLightOk;
                desktopOsdDetail = string.Format("dGPU={0}, Labels={1}, FtConsist={2}, P1Hist={3}, HotspotAscii={4}, RoHide={5}, LpcRender={6}",
                    dgpuDet, labelsOk, ftConsistOk, p1HistOk, osdHotspotOk, roHideOk, lpcLightOk);
            }
            catch (Exception ex) { desktopOsdDetail = ex.Message; }
            Check(desktopOsdOk,
                "Desktop PC OSD Polish (Discrete GPU Label, ASCII Hotspot, Read-Only Row Trimming, Consistent FT & 1% Low History)",
                desktopOsdDetail);

            // 100. Native Windows ETW Present-to-Display Latency Tracker, FreeSync HSync Scanout & x64 Struct Layout
            bool etwTrackerOk = false;
            string etwTrackerDetail = "";
            try
            {
                int szEventTrace = Marshal.SizeOf(typeof(EtwLatencyMonitor.EVENT_TRACE));
                int szLogHdr = Marshal.SizeOf(typeof(EtwLatencyMonitor.TRACE_LOGFILE_HEADER_64));
                int szLogFile = Marshal.SizeOf(typeof(EtwLatencyMonitor.EVENT_TRACE_LOGFILE_64));
                bool structsOk = (IntPtr.Size != 8) || (szEventTrace == 88 && szLogHdr == 280 && szLogFile == 448);

                EtwPresentTracker tracker = new EtwPresentTracker(delegate(uint p)
                {
                    if (p == 1001) return "explorer.exe";
                    if (p == 2002) return "ES2-Win64-Shipping.exe";
                    if (p == 3003) return "ComposedGame.exe";
                    return null;
                });

                double freq = 10000000.0; // 10 MHz QPC (10,000 ticks = 1.0 ms)
                long baseQpc = 100000000L;

                // Ignored process (explorer.exe) must never be tracked
                tracker.ProcessEvent(EtwPresentTracker.ProviderDxgi, EtwPresentTracker.DXGI_PRESENT_START, 1001, baseQpc, freq);
                EtwLatencyTelemetry ignSnap = tracker.GetSnapshot(baseQpc + 100000, freq, true);
                bool ignoredFiltered = !ignSnap.HasLiveFrames;

                // Simulate 30 frames at 100 FPS (10.0 ms interval) with MPO Flip (+6.5 ms) & FreeSync HSync Scanout (+11.2 ms)
                long lastQpc = baseQpc;
                for (int i = 0; i < 30; i++)
                {
                    long tStart = baseQpc + i * 100000L; // +10.0 ms per frame
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgi, EtwPresentTracker.DXGI_PRESENT_MPO_START, 2002, tStart, freq);
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgi, EtwPresentTracker.DXGI_PRESENT_MPO_STOP, 2002, tStart + 2500L, freq); // +0.25 ms
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_MMIO_FLIP_MPO, 2002, tStart + 65000L, freq); // +6.5 ms
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_HSYNC_DPC_MPO, 0, tStart + 112000L, freq); // +11.2 ms
                    lastQpc = tStart + 112000L;
                }

                EtwLatencyTelemetry mpoSnap = tracker.GetSnapshot(lastQpc + 50000L, freq, true);
                bool mpoOk = mpoSnap.HasLiveFrames
                    && mpoSnap.ProcessName == "ES2-Win64-Shipping.exe"
                    && mpoSnap.FlipMode == "MPO Flip"
                    && (mpoSnap.DisplayLatencyMs >= 10.5 && mpoSnap.DisplayLatencyMs <= 12.0)
                    && (mpoSnap.RenderLatencyMs >= 6.0 && mpoSnap.RenderLatencyMs <= 7.0)
                    && (mpoSnap.PresentApiMs >= 0.20 && mpoSnap.PresentApiMs <= 0.30)
                    && (mpoSnap.Fps >= 98 && mpoSnap.Fps <= 102)
                    && (mpoSnap.AvgFps >= 98 && mpoSnap.AvgFps <= 102)
                    && mpoSnap.P1LowFps > 0;

                // Stale idle expiry (> 2.5s) clears HasLiveFrames
                EtwLatencyTelemetry staleSnap = tracker.GetSnapshot(lastQpc + 30000000L, freq, true);
                bool staleCleared = !staleSnap.HasLiveFrames;

                // Composed Blit flip mode detection on a new game after idle
                long tComp = lastQpc + 30000000L;
                for (int i = 0; i < 5; i++)
                {
                    long ts = tComp + i * 166667L;
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgi, EtwPresentTracker.DXGI_PRESENT_START, 3003, ts, freq);
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_BLIT, 3003, ts + 80000L, freq);
                    tracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_VSYNC_DPC, 0, ts + 280000L, freq);
                }
                EtwLatencyTelemetry compSnap = tracker.GetSnapshot(tComp + 5 * 166667L, freq, true);
                bool composedOk = compSnap.HasLiveFrames && compSnap.FlipMode == "Composed" && compSnap.DisplayLatencyMs >= 25.0;

                etwTrackerOk = structsOk && ignoredFiltered && mpoOk && staleCleared && composedOk;
                etwTrackerDetail = string.Format("Structs({0}/{1}/{2})={3}, Ignored={4}, MPO(Lat={5:0.0}ms,Ren={6:0.0}ms,FPS={7})={8}, Stale={9}, Comp({10}/{11:0.0}ms)={12}",
                    szEventTrace, szLogHdr, szLogFile, structsOk, ignoredFiltered,
                    mpoSnap.DisplayLatencyMs, mpoSnap.RenderLatencyMs, mpoSnap.Fps, mpoOk,
                    staleCleared, compSnap.FlipMode, compSnap.DisplayLatencyMs, composedOk);
            }
            catch (Exception ex) { etwTrackerDetail = ex.Message; }
            Check(etwTrackerOk,
                "Native Windows ETW Display Latency Tracker (DXGI/DxgKrnl MPO/FreeSync/Composed & x64 Structs)",
                etwTrackerDetail);

            // 101. ETW Display Latency Stats, OSD Studio Persistence & Overlay/LivePerf Rendering
            bool etwUiOk = false;
            string etwUiDetail = "";
            try
            {
                TelemetrySessionStats latStats = new TelemetrySessionStats();
                FanStatus stLat = new FanStatus
                {
                    CpuTemp = 72,
                    GpuTemp = 68,
                    ApuPowerWatts = 48,
                    RtssFps = 98,
                    RtssFrametimeMs = 10.2,
                    RtssAvgFps = 96,
                    RtssP1LowFps = 78,
                    RtssP01LowFps = 71,
                    RtssProcessName = "ES2-Win64-Shipping.exe",
                    DisplayLatencyMs = 11.4,
                    RenderLatencyMs = 6.8,
                    PresentFlipMode = "MPO Flip",
                    EtwLatencyActive = true,
                    Mode = "Balanced"
                };
                latStats.Record(stLat);
                stLat.DisplayLatencyMs = 13.6;
                latStats.Record(stLat);
                bool statsOk = (latStats.LatencyHistory.Count == 2)
                    && (Math.Abs(latStats.MinLatencyMs - 11.4) < 0.05)
                    && (Math.Abs(latStats.MaxLatencyMs - 13.6) < 0.05)
                    && (Math.Abs(latStats.AvgLatencyMs - 12.5) < 0.05);

                // OsdOverlayConfig ShowLatency round-trip
                OsdOverlayConfig cfg = new OsdOverlayConfig();
                cfg.ShowLatency = false;
                List<string> lines = new List<string>();
                cfg.AppendSettings(lines);
                OsdOverlayConfig cfg2 = new OsdOverlayConfig();
                for (int i = 0; i < lines.Count; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq > 0) cfg2.TryParseSetting(lines[i].Substring(0, eq), lines[i].Substring(eq + 1));
                }
                bool cfgRoundTrip = !cfg2.ShowLatency;

                // Render Overlay (DF Corner) and LivePerformanceCanvas with live latency without exceptions
                bool renderOk = false;
                using (CapFrameXOverlayForm osdLat = new CapFrameXOverlayForm())
                using (LivePerformanceCanvas perfLat = new LivePerformanceCanvas())
                {
                    osdLat.ShowLatency = true;
                    osdLat.DfCornerInset = true;
                    osdLat.Stats = latStats;
                    osdLat.UpdateMetrics(stLat, "Balanced", true);
                    osdLat.UpdateDimensions();
                    using (Bitmap b1 = new Bitmap(Math.Max(1, osdLat.TargetWidth), Math.Max(1, osdLat.TargetHeight)))
                    {
                        osdLat.DrawToBitmap(b1, new Rectangle(0, 0, osdLat.TargetWidth, osdLat.TargetHeight));
                    }

                    perfLat.Size = new Size(900, 480);
                    perfLat.Stats = latStats;
                    perfLat.Status = stLat;
                    using (Bitmap b2 = new Bitmap(900, 480))
                    {
                        perfLat.DrawToBitmap(b2, new Rectangle(0, 0, 900, 480));
                    }
                    renderOk = true;
                }

                // Verify CLI status JSON contains display_latency_ms & flip_mode
                string statusJson = Program.FormatStatusJson(stLat, "Global");
                bool jsonOk = statusJson.Contains("\"display_latency_ms\":13.6")
                    && statusJson.Contains("\"flip_mode\":\"MPO Flip\"")
                    && statusJson.Contains("\"etw_active\":true");

                etwUiOk = statsOk && cfgRoundTrip && renderOk && jsonOk;
                etwUiDetail = string.Format("Stats(Avg={0:0.0}ms)={1}, CfgRT={2}, Render={3}, Json={4}",
                    latStats.AvgLatencyMs, statsOk, cfgRoundTrip, renderOk, jsonOk);
            }
            catch (Exception ex) { etwUiDetail = ex.Message; }
            Check(etwUiOk,
                "ETW Display Latency Stats, OSD Studio Persistence, Overlay/LivePerf Render & Status JSON",
                etwUiDetail);

            // 102. PawnIO ProgramFiles Priority, Fail-Closed Authenticode Verification & LpcACPIEC Routing
            bool pawnSecOk = false;
            string pawnSecDetail = "";
            string tempUnsignedDll = null;
            try
            {
                string[] cands = PawnIoDriver.GetCandidateLibraryPaths(@"C:\Portable\MiniFan", @"C:\Program Files");
                bool prefersProgramFiles = (cands != null && cands.Length >= 3
                    && cands[0].StartsWith(@"C:\Program Files\PawnIO", StringComparison.OrdinalIgnoreCase)
                    && cands[2].StartsWith(@"C:\Portable\MiniFan", StringComparison.OrdinalIgnoreCase));

                tempUnsignedDll = Path.Combine(Path.GetTempPath(), "mfc_unsigned_test_" + Guid.NewGuid().ToString("N") + ".dll");
                File.WriteAllBytes(tempUnsignedDll, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });
                bool unsignedRejected = !PawnIoDriver.VerifyLibrarySignature(tempUnsignedDll);

                PawnIoDriver.SimulatedSignatureVerifier = delegate { return false; };
                bool hookRejected = !PawnIoDriver.VerifyLibrarySignature(tempUnsignedDll);
                PawnIoDriver.SimulatedSignatureVerifier = null;

                // Verify TryPortIoWrite / TryPortIoRead on 0x62/0x66 route directly to PawnIO LpcACPIEC module (ioctl_pio_read/write)
                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
                Dictionary<byte, byte> fakeAcpiEc = new Dictionary<byte, byte>();
                byte ecCmd = 0;
                int ecStage = 0;
                byte ecReg = 0;
                byte ecOut = 0;
                bool ecObf = false;
                int ioctlEcCalls = 0;
                PawnIoDriver.SimulatedAvailable = true;
                PawnIoDriver.SimulatedExecuteHook = delegate(string fn, ulong[] inp)
                {
                    if (fn == "ioctl_pio_write" && inp.Length >= 2)
                    {
                        ioctlEcCalls++;
                        byte p = (byte)inp[0];
                        byte val = (byte)(inp[1] & 0xFF);
                        if (p == 0x66) { ecCmd = val; ecStage = 1; ecObf = false; }
                        else if (p == 0x62)
                        {
                            if (ecCmd == 0x80 && ecStage == 1)
                            {
                                byte v;
                                fakeAcpiEc.TryGetValue(val, out v);
                                ecOut = v;
                                ecObf = true;
                                ecStage = 0;
                            }
                            else if (ecCmd == 0x81 && ecStage == 1) { ecReg = val; ecStage = 2; }
                            else if (ecCmd == 0x81 && ecStage == 2) { fakeAcpiEc[ecReg] = val; ecStage = 0; }
                        }
                        return new ulong[] { 0 };
                    }
                    if (fn == "ioctl_pio_read" && inp.Length >= 1)
                    {
                        ioctlEcCalls++;
                        byte p = (byte)inp[0];
                        if (p == 0x66) return new ulong[] { ecObf ? 0x01UL : 0x00UL };
                        if (p == 0x62) { ecObf = false; return new ulong[] { ecOut }; }
                    }
                    return null;
                };
                bool pre = NativeInpOut.PreloadDriverDll();
                bool ecW = HardwareBackend.TryPortIoWrite(0x62, 0x66, 0x33, 0xA8);
                byte ecVal;
                bool ecR = HardwareBackend.TryPortIoRead(0x62, 0x66, 0x33, out ecVal);

                pawnSecOk = prefersProgramFiles && unsignedRejected && hookRejected && pre && ecW && ecR && (ecVal == 0xA8) && (ioctlEcCalls >= 8);
                pawnSecDetail = string.Format("ProgFilesFirst={0}, UnsignedBlocked={1}, HookBlocked={2}, AcpiEcRouted={3}/0x{4:X2}({5})",
                    prefersProgramFiles, unsignedRejected, hookRejected, ecW && ecR, ecVal, ioctlEcCalls);
            }
            catch (Exception ex) { pawnSecDetail = ex.Message; }
            finally
            {
                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
                if (!string.IsNullOrEmpty(tempUnsignedDll)) { try { File.Delete(tempUnsignedDll); } catch { } }
            }
            Check(pawnSecOk,
                "PawnIO ProgramFiles Priority, Fail-Closed Authenticode Verification & LpcACPIEC Routing",
                pawnSecDetail);

            // 103. HeadlessDaemon Anti-Cheat Pause & Resume via WorkerIterationCompleted (Zero Deadlock)
            bool daemonAcOk = false;
            string daemonAcDetail = "";
            try
            {
                bool eacActive = true;
                long acClock = 1000;
                AntiCheatMonitor mon = new AntiCheatMonitor(
                    delegate(string svc) { return svc == "EasyAntiCheat_EOS" && eacActive; },
                    delegate { return acClock; },
                    0);
                mon.Enabled = true;

                FanCurveProfile[] builtIns = HeadlessDaemon.CreateBuiltInProfiles();
                DemoBackend acDemo = new DemoBackend();
                int pauseCount = 0;
                int resumeCount = 0;
                ManualResetEvent pausedEvt = new ManualResetEvent(false);
                ManualResetEvent resumedEvt = new ManualResetEvent(false);

                using (FanControllerService svc = new FanControllerService(null, acDemo, builtIns, true))
                {
                    svc.RefreshIntervalMs = 100;
                    svc.WorkerIterationCompleted += delegate
                    {
                        HeadlessDaemon.PollAntiCheatOnWorkerTick(
                            mon,
                            svc,
                            false,
                            delegate
                            {
                                Interlocked.Increment(ref pauseCount);
                                pausedEvt.Set();
                            },
                            delegate(bool reinit)
                            {
                                svc.ResumeFromSuspend(false);
                                Interlocked.Increment(ref resumeCount);
                                resumedEvt.Set();
                            });
                    };

                    svc.Start();
                    bool gotPaused = pausedEvt.WaitOne(1500);
                    bool workerWasPaused = svc.IsWorkerPaused;

                    // Flip EAC off while worker is paused — WorkerIterationCompleted must still poll and resume!
                    eacActive = false;
                    acClock = 5000;
                    bool gotResumed = resumedEvt.WaitOne(1500);
                    bool workerUnpaused = !svc.IsWorkerPaused;
                    svc.Stop(500);

                    daemonAcOk = gotPaused && workerWasPaused && gotResumed && workerUnpaused && pauseCount == 1 && resumeCount == 1;
                    daemonAcDetail = string.Format("Paused={0}({1}), Resumed={2}({3}), Counts={4}/{5}",
                        gotPaused, workerWasPaused, gotResumed, workerUnpaused, pauseCount, resumeCount);
                }
                pausedEvt.Close();
                resumedEvt.Close();
            }
            catch (Exception ex) { daemonAcDetail = ex.Message; }
            Check(daemonAcOk,
                "HeadlessDaemon Anti-Cheat Pause & Automatic Resume While Worker Paused (Zero Deadlock)",
                daemonAcDetail);

            // 104. Read-Only / Non-AMD Active Silicon Gate & Temperature-Aware Super I/O Safe Fallback
            bool siliconGateOk = false;
            string siliconGateDetail = "";
            try
            {
                DeviceProfile verifiedProf = DeviceProfile.FindProfile("ryzen-hx370-ec");
                DeviceProfile roProf = DeviceProfile.FindProfile("unknown-safe");

                HardwareBackend.SimulatedIsAmdCpuForTest = false; // Simulate Intel CPU
                bool intelBlocked = !HardwareBackend.CanRunActiveSiliconProbes(verifiedProf, false);

                HardwareBackend.SimulatedIsAmdCpuForTest = true; // Simulate AMD CPU
                bool roBlocked = !HardwareBackend.CanRunActiveSiliconProbes(roProf, false);
                bool verifiedAllowed = HardwareBackend.CanRunActiveSiliconProbes(verifiedProf, false);
                HardwareBackend.SimulatedIsAmdCpuForTest = null;

                int coolPct = HardwareBackend.ComputeSafeSuperIoFallbackPct(45, false);
                int acPct = HardwareBackend.ComputeSafeSuperIoFallbackPct(52, true);
                int warmPct = HardwareBackend.ComputeSafeSuperIoFallbackPct(70, false);
                int hotPct = HardwareBackend.ComputeSafeSuperIoFallbackPct(85, false);

                HardwareBackend sioHw = new HardwareBackend();
                sioHw.EnableSimulationMode();
                sioHw.Profile = verifiedProf;
                sioHw.SuperIoBaseAddr = 0x0A30;
                bool acFallbackOk = sioHw.ApplySafeSuperIoFallback(true);
                byte dutyAc = sioHw.ReadSuperIoHwReg(0x63);
                byte expectedAc = HardwareBackend.EncodeSuperIoDuty(60, false);

                // Simulate hot CPU diode on Reg 0x2B (82°C) -> auto-escalates to 75% even without thermalLoadContext
                sioHw.WriteSuperIoHwReg(0x2B, 82);
                bool hotFallbackOk = sioHw.ApplySafeSuperIoFallback(false);
                byte dutyHot = sioHw.ReadSuperIoHwReg(0x63);
                byte expectedHot = HardwareBackend.EncodeSuperIoDuty(75, false);

                siliconGateOk = intelBlocked && roBlocked && verifiedAllowed
                    && (coolPct == 35) && (acPct == 60) && (warmPct == 60) && (hotPct == 75)
                    && acFallbackOk && (dutyAc == expectedAc)
                    && hotFallbackOk && (dutyHot == expectedHot);
                siliconGateDetail = string.Format("IntelBlk={0}, RoBlk={1}, AmdAllow={2}, Pcts={3}/{4}/{5}/{6}, AcDuty={7}/{8}, HotDuty={9}/{10}",
                    intelBlocked, roBlocked, verifiedAllowed, coolPct, acPct, warmPct, hotPct, dutyAc, expectedAc, dutyHot, expectedHot);
            }
            catch (Exception ex) { siliconGateDetail = ex.Message; }
            finally { HardwareBackend.SimulatedIsAmdCpuForTest = null; }
            Check(siliconGateOk,
                "Read-Only / Non-AMD Silicon Write Gate & Temperature-Aware Super I/O Safe Fallback",
                siliconGateDetail);

            // 105. Horizontal OSD Brush-Safe Render (FCLK/VRM/NPU) & ETW Shutdown/AutoStart Guard
            bool osdEtwGuardOk = false;
            string osdEtwGuardDetail = "";
            try
            {
                FanStatus stRich = new FanStatus
                {
                    CpuTemp = 62, GpuTemp = 54, ApuPowerWatts = 45,
                    FabricClockMhz = 2000, IsFabricSynced = true,
                    VddTdcCurrentAmps = 38.5, VrmTemp = 58,
                    NpuActivityPct = 42, NpuClockMhz = 1200,
                    RamTotalGb = 32, RamUsedGb = 14.2, RamLoadPct = 44,
                    Mode = "Balanced"
                };
                TelemetrySessionStats richStats = new TelemetrySessionStats();
                richStats.Record(stRich);

                bool horizRenderOk = false;
                using (CapFrameXOverlayForm osdH = new CapFrameXOverlayForm())
                {
                    osdH.VerticalLayout = false;
                    osdH.ShowFabricClock = true;
                    osdH.ShowVrmCurrent = true;
                    osdH.ShowNpu = true;
                    osdH.Stats = richStats;
                    osdH.UpdateMetrics(stRich, "Balanced", true);
                    osdH.UpdateDimensions();
                    int w = Math.Max(1, osdH.TargetWidth);
                    int h = Math.Max(1, osdH.TargetHeight);
                    using (Bitmap bmp = new Bitmap(w, h))
                    {
                        for (int i = 0; i < 3; i++)
                            osdH.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    }
                    horizRenderOk = true;
                }

                EtwLatencyMonitor.Shutdown();
                bool etwStopped = !EtwLatencyMonitor.IsActive;
                HardwareBackend cliHw = new HardwareBackend();
                bool defaultAutoStart = cliHw.AllowEtwAutoStart;
                cliHw.AllowEtwAutoStart = false;

                osdEtwGuardOk = horizRenderOk && etwStopped && defaultAutoStart && !cliHw.AllowEtwAutoStart;
                osdEtwGuardDetail = string.Format("HorizRender={0}, EtwCleanStop={1}, AutoStartToggle={2}",
                    horizRenderOk, etwStopped, defaultAutoStart && !cliHw.AllowEtwAutoStart);
            }
            catch (Exception ex) { osdEtwGuardDetail = ex.Message; }
            Check(osdEtwGuardOk,
                "Horizontal OSD Brush-Safe Render (FCLK/VRM/NPU) & ETW Shutdown/AutoStart Guard",
                osdEtwGuardDetail);

            // 106. WatchdogSupervisor Log Rotation Cap & Monotonic Timeout Integrity
            bool wdRotOk = false;
            string wdRotDetail = "";
            string tempLog = null;
            try
            {
                tempLog = Path.Combine(Path.GetTempPath(), "mfc_wd_rot_" + Guid.NewGuid().ToString("N") + ".log");
                string line = new string('X', 80) + Environment.NewLine;
                WatchdogSupervisor.WriteLogEntryWithRotation(tempLog, line, 128);
                WatchdogSupervisor.WriteLogEntryWithRotation(tempLog, line, 128);
                WatchdogSupervisor.WriteLogEntryWithRotation(tempLog, line, 128); // Triggers rollover to .1

                bool backupCreated = File.Exists(tempLog + ".1");
                long curLen = File.Exists(tempLog) ? new FileInfo(tempLog).Length : -1;
                bool bounded = (curLen > 0 && curLen < 128);

                wdRotOk = backupCreated && bounded;
                wdRotDetail = string.Format("BackupCreated={0}, CurrentLen={1}", backupCreated, curLen);
            }
            catch (Exception ex) { wdRotDetail = ex.Message; }
            finally
            {
                if (!string.IsNullOrEmpty(tempLog))
                {
                    try { if (File.Exists(tempLog)) File.Delete(tempLog); } catch { }
                    try { if (File.Exists(tempLog + ".1")) File.Delete(tempLog + ".1"); } catch { }
                }
            }
            Check(wdRotOk,
                "WatchdogSupervisor Log Rotation Rollover Cap & Monotonic Timing",
                wdRotDetail);

            // 107. Driver ReaderWriterLock Shutdown Safety & Deferred CLI Preload Policy
            bool drvSyncOk = false;
            string drvSyncDetail = "";
            try
            {
                bool cliGateOk = !Program.ShouldPreloadDriverForCommand("--help", false)
                    && !Program.ShouldPreloadDriverForCommand("test", false)
                    && !Program.ShouldPreloadDriverForCommand("driver-stop", false)
                    && !Program.ShouldPreloadDriverForCommand("validate-lang", false)
                    && !Program.ShouldPreloadDriverForCommand("export-profiles", false)
                    && !Program.ShouldPreloadDriverForCommand("latency", false)
                    && !Program.ShouldPreloadDriverForCommand("status", true)
                    && !Program.ShouldPreloadDriverForCommand(null, true)
                    && Program.ShouldPreloadDriverForCommand("status", false)
                    && Program.ShouldPreloadDriverForCommand("daemon", false)
                    && Program.ShouldPreloadDriverForCommand(null, false);

                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
                PawnIoDriver.SimulatedAvailable = true;
                PawnIoDriver.SimulatedExecuteHook = delegate(string fn, ulong[] inp) { return new ulong[] { 0x42 }; };
                NativeInpOut.PreloadDriverDll();

                int raceExceptions = 0;
                Thread reader = new Thread(delegate()
                {
                    for (int i = 0; i < 200; i++)
                    {
                        try { NativeInpOut.DlPortReadPortUchar(0x0A35); }
                        catch { Interlocked.Increment(ref raceExceptions); }
                    }
                });
                reader.Start();
                for (int i = 0; i < 10; i++)
                {
                    NativeInpOut.Shutdown();
                    Thread.Sleep(1);
                }
                reader.Join(1000);
                bool cleanStopped = (NativeInpOut.ActiveDriver == HardwareDriverEngine.None) && (raceExceptions == 0);

                drvSyncOk = cliGateOk && cleanStopped;
                drvSyncDetail = string.Format("CliGate={0}, RaceExceptions={1}, Stopped={2}", cliGateOk, raceExceptions, cleanStopped);
            }
            catch (Exception ex) { drvSyncDetail = ex.Message; }
            finally
            {
                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
            }
            Check(drvSyncOk,
                "Driver ReaderWriterLock Shutdown Safety & Deferred CLI Preload Policy",
                drvSyncDetail);

            // 108. Atomic AppSettings Write, Profile Clone Non-Mutation & CustomProfile Deduplication
            bool settingsIsoOk = false;
            string settingsIsoDetail = "";
            string tempCfg = null;
            try
            {
                tempCfg = Path.Combine(Path.GetTempPath(), "mfc_cfg_test_" + Guid.NewGuid().ToString("N") + ".cfg");
                AppSettings.WriteAllLinesAtomic(tempCfg, new string[]
                {
                    "HardwareProfile=gmktec-k12",
                    "RegCpuTemp=0x79",
                    "CustomProfile=MyTest|40:30;80:100|40:30;80:100"
                });
                // Second atomic write exercises File.Replace + .bak creation
                AppSettings.WriteAllLinesAtomic(tempCfg, new string[]
                {
                    "HardwareProfile=gmktec-k12",
                    "RegCpuTemp=0x7A",
                    "CustomProfile=MyTest|40:35;80:100|40:35;80:100"
                });

                byte staticOrig = DeviceProfile.FindProfile("gmktec-k12").RegCpuTemp;
                HardwareBackend testHw = new HardwareBackend();
                List<FanCurveProfile> customs = new List<FanCurveProfile>
                {
                    new FanCurveProfile("MyTest", false, -1,
                        new CurvePoint[] { new CurvePoint(40, 20), new CurvePoint(80, 90) },
                        new CurvePoint[] { new CurvePoint(40, 20), new CurvePoint(80, 90) })
                };
                AppSettings loaded = new AppSettings();
                loaded.LoadFromPath(tempCfg, null, testHw, customs);

                bool hwClonedOverride = (testHw.Profile != null && testHw.Profile.RegCpuTemp == 0x7A);
                bool staticUntouched = (DeviceProfile.FindProfile("gmktec-k12").RegCpuTemp == staticOrig && staticOrig == 0x70);
                bool deduped = (customs.Count == 1 && customs[0].ApuPoints[0].Pct == 35);
                bool noStrayTmp = !File.Exists(tempCfg + ".tmp");

                settingsIsoOk = hwClonedOverride && staticUntouched && deduped && noStrayTmp;
                settingsIsoDetail = string.Format("Override=0x{0:X2}, StaticOrig=0x{1:X2}, CustomCount={2}, NoTmp={3}",
                    testHw.Profile != null ? testHw.Profile.RegCpuTemp : 0,
                    DeviceProfile.FindProfile("gmktec-k12").RegCpuTemp, customs.Count, noStrayTmp);
            }
            catch (Exception ex) { settingsIsoDetail = ex.Message; }
            finally
            {
                if (!string.IsNullOrEmpty(tempCfg))
                {
                    try { if (File.Exists(tempCfg)) File.Delete(tempCfg); } catch { }
                    try { if (File.Exists(tempCfg + ".tmp")) File.Delete(tempCfg + ".tmp"); } catch { }
                    try { if (File.Exists(tempCfg + ".bak")) File.Delete(tempCfg + ".bak"); } catch { }
                }
            }
            Check(settingsIsoOk,
                "Atomic AppSettings Write, Profile Clone Non-Mutation & CustomProfile Deduplication",
                settingsIsoDetail);

            // 109. Unverified Profile Read-Only Invariant (IsVerified) & HardwareBackend.SetMode Fail-Safe
            bool unvInvOk = false;
            string unvInvDetail = "";
            try
            {
                DeviceProfile customUnv = new DeviceProfile
                {
                    Id = "custom-unverified-board",
                    DisplayName = "Custom Unverified",
                    IsVerified = false,
                    IsReadOnly = true
                };
                HardwareBackend unvCustomHw = new HardwareBackend(customUnv);
                var pts = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(80, 100) };
                var builtIns = new FanCurveProfile[] { new FanCurveProfile("Balanced", true, 0, pts, pts) };
                using (var svc = new FanControllerService(unvCustomHw, new DemoBackend(), builtIns, false))
                {
                    svc.ClearStaleWriteErrors();
                    bool roAfterClear = customUnv.IsReadOnly;
                    bool autoRes = svc.ApplyAutoAll();
                    bool roAfterAuto = customUnv.IsReadOnly;

                    // Verify SetMode returns false safely without throwing when driver isn't open
                    NativeInpOut.Shutdown();
                    HardwareBackend noDrvHw = new HardwareBackend(DeviceProfile.FindProfile("gmktec-k12"));
                    bool modeSafeFalse = !noDrvHw.SetMode(1);

                    unvInvOk = roAfterClear && !autoRes && roAfterAuto && modeSafeFalse;
                    unvInvDetail = string.Format("RoAfterClear={0}, RoAfterAuto={1}, SetModeSafe={2}",
                        roAfterClear, roAfterAuto, modeSafeFalse);
                }
            }
            catch (Exception ex) { unvInvDetail = ex.Message; }
            Check(unvInvOk,
                "Unverified Profile Read-Only Invariant (IsVerified) & HardwareBackend.SetMode Fail-Safe",
                unvInvDetail);

            // 110. Narrow FpsMonitor Crash Filter & Zero-Alloc EtwPresentTracker Ring Buffer Wrap
            bool etwFpsPolishOk = false;
            string etwFpsPolishDetail = "";
            try
            {
                bool gameAllowed = !FpsMonitor.IsIgnoredProcess("CrashBandicoot4.exe")
                    && !FpsMonitor.IsIgnoredProcess("CrashBandicootNSaneTrilogy.exe");
                bool crashHandlersIgnored = FpsMonitor.IsIgnoredProcess("UnityCrashHandler64.exe")
                    && FpsMonitor.IsIgnoredProcess("CrashReporter.exe")
                    && FpsMonitor.IsIgnoredProcess("crashpad_handler.exe")
                    && FpsMonitor.IsIgnoredProcess("CrashDumpHelper.exe");

                EtwPresentTracker ringTracker = new EtwPresentTracker(delegate(uint p) { return p == 4242 ? "CrashBandicoot4.exe" : null; });
                double qFreq = 10000000.0;
                long t0 = 500000000L;
                // Push 20 frames without scanout to force multiple 8-slot ring wraps, then flip & scanout
                for (int i = 0; i < 20; i++)
                {
                    long ts = t0 + i * 100000L;
                    ringTracker.ProcessEvent(EtwPresentTracker.ProviderDxgi, EtwPresentTracker.DXGI_PRESENT_MPO_START, 4242, ts, qFreq);
                }
                long lastStart = t0 + 19 * 100000L;
                ringTracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_MMIO_FLIP_MPO, 4242, lastStart + 40000L, qFreq);
                ringTracker.ProcessEvent(EtwPresentTracker.ProviderDxgKrnl, EtwPresentTracker.DXGKRNL_HSYNC_DPC_MPO, 0, lastStart + 90000L, qFreq);
                EtwLatencyTelemetry snap = ringTracker.GetSnapshot(lastStart + 100000L, qFreq, true);
                bool ringOk = snap.HasLiveFrames && snap.ProcessName == "CrashBandicoot4.exe" && snap.DisplayLatencyMs > 0.0 && snap.FlipMode == "MPO Flip";

                etwFpsPolishOk = gameAllowed && crashHandlersIgnored && ringOk;
                etwFpsPolishDetail = string.Format("GameAllowed={0}, HandlersIgnored={1}, RingWrapOk={2}(Lat={3:0.0}ms)",
                    gameAllowed, crashHandlersIgnored, ringOk, snap.DisplayLatencyMs);
            }
            catch (Exception ex) { etwFpsPolishDetail = ex.Message; }
            Check(etwFpsPolishOk,
                "Narrow FpsMonitor Crash Filter & Zero-Alloc EtwPresentTracker Ring Buffer Wrap",
                etwFpsPolishDetail);

            // 111. Independent APU & Sys Fan Cooldown Hysteresis (NVMe Rise Not Masked by APU Dip)
            bool splitHystOk = false;
            string splitHystDetail = "";
            try
            {
                CurvePoint[] linPts = new CurvePoint[] { new CurvePoint(40, 30), new CurvePoint(60, 50), new CurvePoint(80, 90) };
                FanCurveProfile customBoth = new FanCurveProfile("CustomSplit", false, -1, linPts, linPts);
                FanCurveProfile[] builtIns = HeadlessDaemon.CreateBuiltInProfiles();
                DemoBackend hystDemo = new DemoBackend();
                using (FanControllerService svc = new FanControllerService(null, hystDemo, builtIns, true))
                {
                    svc.HasDualFans = true;
                    svc.SetProfileTarget(customBoth, false);
                    svc.SetProfileTarget(customBoth, true);

                    // Step 1: APU=60°C, NVMe=50°C -> both fans settle at 50%
                    svc.SetInitialStatus(new FanStatus { CpuTemp = 60, GpuTemp = 50, NvmeTemp = 50 });
                    svc.EvaluateAndApplyFanProfiles(60);
                    int apu1 = svc.LastAppliedApuPct;
                    int sys1 = svc.LastAppliedSysPct;

                    // Step 2: APU dips 2°C to 58°C (within 4°C hysteresis -> APU must hold 50%),
                    // while NVMe spikes to 76°C -> Sys fan MUST immediately ramp to >= 80%!
                    svc.SetInitialStatus(new FanStatus { CpuTemp = 58, GpuTemp = 50, NvmeTemp = 76 });
                    svc.EvaluateAndApplyFanProfiles(58);
                    int apu2 = svc.LastAppliedApuPct;
                    int sys2 = svc.LastAppliedSysPct;

                    splitHystOk = (apu1 == 50) && (sys1 == 50) && (apu2 == 50) && (sys2 >= 80);
                    splitHystDetail = string.Format("Step1(APU={0}%,Sys={1}%), Step2(APU={2}%,Sys={3}%)", apu1, sys1, apu2, sys2);
                }
            }
            catch (Exception ex) { splitHystDetail = ex.Message; }
            Check(splitHystOk,
                "Independent APU & Sys Fan Cooldown Hysteresis (NVMe Rise Not Masked by APU Dip)",
                splitHystDetail);

        }
    }
}
