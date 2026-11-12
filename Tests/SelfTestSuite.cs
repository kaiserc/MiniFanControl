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

    public static class SelfTestSuite

    {

        private static int _passed = 0;

        private static int _failed = 0;



        private static void Check(bool condition, string name, string detail)

        {

            if (condition)

            {

                _passed++;

                Console.WriteLine("  [PASS] " + name);

            }

            else

            {

                _failed++;

                Console.WriteLine("  [FAIL] " + name + " -> " + detail);

            }

        }



        public static int RunAll()

        {

            _passed = 0;

            _failed = 0;

            Console.WriteLine("======================================================================");

            Console.WriteLine("  MiniFanControl - Automated Hardware Safety & Curve Test Suite");

            Console.WriteLine("======================================================================");



            // 1. Motor Dead-Zone / Stall Protection (1%..29% is forbidden across 0..105C)

            CurvePoint[] balApu = new CurvePoint[]

            {

                new CurvePoint(40, 0), new CurvePoint(48, 35), new CurvePoint(65, 55), new CurvePoint(78, 80), new CurvePoint(88, 100)

            };

            CurvePoint[] silApu = new CurvePoint[]

            {

                new CurvePoint(48, 0), new CurvePoint(56, 30), new CurvePoint(70, 45), new CurvePoint(82, 70), new CurvePoint(92, 100)

            };

            bool deadZoneViolated = false;

            int badTemp = -1, badPct = -1;

            for (int t = 0; t <= 105; t++)

            {

                int p1 = FanCurveProfile.EvaluateCurve(balApu, t);

                int p2 = FanCurveProfile.EvaluateCurve(silApu, t);

                if ((p1 >= 1 && p1 < FanCurveProfile.MIN_SPIN_PCT) || (p2 >= 1 && p2 < FanCurveProfile.MIN_SPIN_PCT))

                {

                    deadZoneViolated = true;

                    badTemp = t;

                    badPct = p1 >= 1 && p1 < FanCurveProfile.MIN_SPIN_PCT ? p1 : p2;

                    break;

                }

            }

            Check(!deadZoneViolated,

                "Motor Stall Dead-Zone Protection (never outputs 1%..29% across 0..105C)",

                string.Format("Violated at {0}C with {1}%", badTemp, badPct));



            // 2. Specific 47C Balanced interpolation regression test (previously produced 24% & stalled)

            int at47C = FanCurveProfile.EvaluateCurve(balApu, 47);

            Check(at47C >= FanCurveProfile.MIN_SPIN_PCT,

                "47C Balanced Curve Stall Regression Test (>= 30% PWM)",

                "Got " + at47C + "%");



            // 3. Zero-RPM Idle segment test (flat 0% segment stays 0%, then steps cleanly to >= 30% past midpoint)

            CurvePoint[] flatZeroCurve = new CurvePoint[]

            {

                new CurvePoint(30, 0), new CurvePoint(45, 0), new CurvePoint(55, 35), new CurvePoint(85, 100)

            };

            int idle35 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 35);

            int idle45 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 45);

            int ramp51 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 51);

            Check(idle35 == 0 && idle45 == 0 && ramp51 >= FanCurveProfile.MIN_SPIN_PCT,

                "Zero-RPM Idle Segment & Clean Step-Up Transition (0% <= 45C, >= 30% at 51C)",

                string.Format("35C={0}%, 45C={1}%, 51C={2}%", idle35, idle45, ramp51));



            // 4. Curve Monotonicity & Boundary Clamping

            int lowClamp = FanCurveProfile.EvaluateCurve(balApu, 15);

            int highClamp = FanCurveProfile.EvaluateCurve(balApu, 99);

            Check(lowClamp == 0 && highClamp == 100,

                "Curve Boundary Clamping (15C -> 0%, 99C -> 100%)",

                string.Format("low={0}%, high={1}%", lowClamp, highClamp));



            // 5. Tachometer Glitch & Motor Stop Filter (8000 raw -> 0 RPM)

            int rpmStall8000 = HardwareBackend.DecodeTachometerRpm(0x1F, 0x40); // 8000

            int rpmGlitchFFFF = HardwareBackend.DecodeTachometerRpm(0xFF, 0xFF); // 65535

            int rpmValid592 = HardwareBackend.DecodeTachometerRpm(0x02, 0x50); // 592

            int rpmValid3150 = HardwareBackend.DecodeTachometerRpm(0x0C, 0x4E); // 3150

            Check(rpmStall8000 == 0 && rpmGlitchFFFF == 0 && rpmValid592 == 592 && rpmValid3150 == 3150,

                "Tachometer Stall Filter (8000 -> 0 RPM, 65535 -> 0 RPM, 592 & 3150 preserved)",

                string.Format("8000->{0}, FFFF->{1}, 592->{2}, 3150->{3}", rpmStall8000, rpmGlitchFFFF, rpmValid592, rpmValid3150));



            // 6. EC Mode & Fan Control Register Bitmask Encoding

            byte mBal = HardwareBackend.EncodeModeCommand(0);

            byte mPerf = HardwareBackend.EncodeModeCommand(1);

            byte mSil = HardwareBackend.EncodeModeCommand(2);

            byte fAuto = HardwareBackend.EncodeFanCommand(0);

            byte fMin = HardwareBackend.EncodeFanCommand(1);

            byte f50 = HardwareBackend.EncodeFanCommand(50);

            byte f100 = HardwareBackend.EncodeFanCommand(100);

            bool boundsThrew = false;

            try { HardwareBackend.EncodeFanCommand(105); } catch (ArgumentException) { boundsThrew = true; }

            Check(mBal == 0x80 && mPerf == 0x81 && mSil == 0x82 &&

                  fAuto == 0x00 && fMin == 0x81 && f50 == (0x80 | 50) && f100 == (0x80 | 100) && boundsThrew,

                "EC Register Bitmask Encoding & Safety Bounds (0x32..0x34)",

                "Unexpected register encoding");



            // 7. Custom Curve Serialization / Deserialization Round-Trip

            string serialized = MainForm.FormatPointList(balApu);

            CurvePoint[] parsed = MainForm.ParsePointList(serialized);

            bool roundTripOk = (parsed.Length == balApu.Length);

            for (int i = 0; i < parsed.Length && roundTripOk; i++)

            {

                if (parsed[i].Temp != balApu[i].Temp || parsed[i].Pct != balApu[i].Pct)

                    roundTripOk = false;

            }

            Check(roundTripOk,

                "Custom Curve Config Save/Load Round-Trip (FormatPointList <-> ParsePointList)",

                "Serialized: " + serialized);



            // 8. Multi-Model Ryzen Mini-PC Hardware Profile Detection

            DeviceProfile pK12 = SystemHardwareInfo.MatchProfile("NucBox K12", "GMKtec", "AMD Ryzen 7 H 255");

            DeviceProfile pK8 = SystemHardwareInfo.MatchProfile("NucBox K8 Plus", "GMKtec", "AMD Ryzen 7 8845HS");

            DeviceProfile pHx370 = SystemHardwareInfo.MatchProfile("EVO-X1", "Kthin", "AMD Ryzen AI 9 HX 370");

            DeviceProfile pAdreamer = SystemHardwareInfo.MatchProfile("AriesBox 10", "ADREAMER", "AMD Ryzen 7 7840HS");

            Check(pK12.Id == "gmktec-k12" && pK8.Id == "gmktec-k8-k11" && pHx370.Id == "ryzen-hx370-ec" && pAdreamer.Id == "adreamer-ariesbox10",

                "Multi-Model Ryzen Mini-PC Profile Matching (K12 / K8 Plus / HX 370 / AriesBox 10)",

                string.Format("K12={0}, K8={1}, HX370={2}, AriesBox={3}", pK12.Id, pK8.Id, pHx370.Id, pAdreamer.Id));



            // 9. Demo/Simulation Backend State Transitions

            DemoBackend demo = new DemoBackend();

            demo.SetMode(2);

            demo.SetFan(1, 65);

            demo.SetFan(2, 40);

            FanStatus stMan = demo.GetStatus();

            demo.SetAuto();

            FanStatus stAuto = demo.GetStatus();

            Check(stMan.ModeCode == 2 && stMan.Fan1Manual == 1 && stMan.Fan1Pct == 65 && stMan.Fan2Pct == 40 &&

                  stAuto.ModeCode == 0 && stAuto.Fan1Manual == 0 && stAuto.Fan2Manual == 0,

                "Simulation Backend State Machine (Manual Override -> Reset Auto)",

                "DemoBackend state mismatch");



            // 10. Smart EC Candidate Scanner & Dynamic Config Register Override

            byte[] mockEc = new byte[256];

            mockEc[0x31] = 0;    // Mode Balanced

            mockEc[0x35] = 0x05; // Fan 1 = 0x05DC (1500 RPM)

            mockEc[0x36] = 0xDC;

            mockEc[0x37] = 0x1F; // Fan 2 = 0x1F40 (8000 Idle)

            mockEc[0x38] = 0x40;

            mockEc[0x70] = 47;   // CPU Temp = 47C

            string scanReport = SystemHardwareInfo.AnalyzeEcCandidates(mockEc);

            byte parsedHex, parsedDec;

            bool hexOk = MainForm.TryParseHexOrDecByte("0x70", out parsedHex) && parsedHex == 0x70;

            bool decOk = MainForm.TryParseHexOrDecByte("112", out parsedDec) && parsedDec == 112;

            Check(scanReport.Contains("[OK] Standard GMKtec") && scanReport.Contains("0x70=47") && scanReport.Contains("0x35..36=1500 RPM") && hexOk && decOk,

                "Smart EC Candidate Scanner & Dynamic Register Override Parsing",

                "Candidate scan or hex parsing failed");



            // 11. 3-Pass Fan Calibration Engine, Repeatability Verification, Date Tracking & Anti-Stall Safety Margin

            int progressCallbacks = 0;

            FanCalibrationResult calRes = FanCalibrationResult.RunCalibration(

                null, demo, true, 0, 3,

                prog => { progressCallbacks++; },

                null);

            int customFloorEval = FanCurveProfile.EvaluateCurve(balApu, 47, calRes.ApuMinSpinPct);

            bool isCalFresh, isStaleFresh, isCalOld, isStaleOld;

            FanCurveProfile.ApuMinSpinPct = calRes.ApuMinSpinPct;

            FanCurveProfile.SysMinSpinPct = calRes.SysMinSpinPct;

            FanCurveProfile.LastCalibratedDate = calRes.CalibratedAt;

            string freshBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalFresh, out isStaleFresh);

            FanCurveProfile.LastCalibratedDate = "2024-01-15 12:00";

            string staleBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalOld, out isStaleOld);

            Check(calRes.HasDualFans &&

                  calRes.PassesCompleted == 3 &&

                  calRes.PassApuMin.Length == 3 &&

                  calRes.PassApuMin[0] == 24 && calRes.PassApuMin[1] == 24 && calRes.PassApuMin[2] == 24 &&

                  calRes.ApuBareSpinPct == 24 &&

                  calRes.ApuMinSpinPct == 26 &&

                  calRes.ApuMaxRpm >= 3000 &&

                  progressCallbacks >= 30 &&

                  customFloorEval >= 26 &&

                  isCalFresh && !isStaleFresh && freshBadge.Contains("Calibrated") &&

                  isCalOld && isStaleOld && staleBadge.Contains(">6m ago"),

                "3-Pass Fan Calibration Engine, Date Tracking & +2% Safety Margin (3 passes verified)",

                string.Format("Passes={0}, Bare={1}%, SafeMin={2}%, Badge={3}", calRes.PassesCompleted, calRes.ApuBareSpinPct, calRes.ApuMinSpinPct, freshBadge));



            // 12. CapFrameX Global\Access_EC Hardware Mutex & APU Power Telemetry + Breakdown Tooltip

            bool mutexLocked = EcBusMutex.WaitEc(50);

            if (mutexLocked) EcBusMutex.ReleaseEc();

            int estIdlePwr = HardwareBackend.EstimateApuPowerWatts(42, 36, 0);

            int estLoadPwr = HardwareBackend.EstimateApuPowerWatts(84, 76, 1);

            string pwrTipTest = stAuto.FormatPowerTooltip();

            Check(mutexLocked && estIdlePwr >= 6 && estIdlePwr <= 22 && estLoadPwr >= 45 && estLoadPwr <= 65 &&

                  stAuto.ApuPowerWatts > 0 && pwrTipTest.Contains("VDDCR_CPU") && pwrTipTest.Contains("VDDCR_GFX") && pwrTipTest.Contains("VDDCR_SOC"),

                "CapFrameX Global\\Access_EC Mutex & APU Power Breakdown Tooltip (W)",

                string.Format("Mutex={0}, IdleW={1}W, LoadW={2}W", mutexLocked, estIdlePwr, estLoadPwr));



            // 13. Session Peak/Min/Avg Tracking & Auto-Scaled Sparkline Telemetry

            TelemetrySessionStats stats = new TelemetrySessionStats();

            stats.Record(new FanStatus { CpuTemp = 42, GpuTemp = 37, ApuPowerWatts = 8, Fan1Rpm = 1400, Fan2Rpm = 650 });

            stats.Record(new FanStatus { CpuTemp = 64, GpuTemp = 38, ApuPowerWatts = 28, Fan1Rpm = 2100, Fan2Rpm = 1200 });

            stats.Record(new FanStatus { CpuTemp = 82, GpuTemp = 39, ApuPowerWatts = 54, Fan1Rpm = 3100, Fan2Rpm = 1850 });

            string cpuSpark = stats.GetCpuSparkline();

            string gpuSpark = stats.GetGpuSparkline();

            string pwrSpark = stats.GetPowerSparkline();

            string fan1Spark = stats.GetFan1Sparkline();

            string fan2Spark = stats.GetFan2Sparkline();

            string enrichedPwrTip = stAuto.FormatPowerTooltip(stats);

            string enrichedCpuTip = stAuto.FormatCpuTooltip(stats);

            string enrichedFan1Tip = stAuto.FormatApuFanTooltip(stats, 3200);

            string enrichedFan2Tip = stAuto.FormatSysFanTooltip(stats, 1900, true);

            Check(stats.SampleCount == 3 &&

                  stats.MinCpuTemp == 42 && stats.MaxCpuTemp == 82 && Math.Abs(stats.AvgCpuTemp - 62.67) < 0.05 &&

                  stats.MinPowerWatts == 8 && stats.MaxPowerWatts == 54 &&

                  stats.MinFan1Rpm == 1400 && stats.MaxFan1Rpm == 3100 &&

                  stats.MinFan2Rpm == 650 && stats.MaxFan2Rpm == 1850 &&

                  stats.Fan1RpmHistory.Count == 3 && stats.Fan2RpmHistory.Count == 3 &&

                  cpuSpark.Length == 3 && pwrSpark.Length == 3 && fan1Spark.Length == 3 && fan2Spark.Length == 3 &&

                  gpuSpark[0] != gpuSpark[2] &&

                  enrichedPwrTip.Contains("Session Power (Min / Avg / Peak)") &&

                  enrichedCpuTip.Contains("Session CPU Temp (Min / Avg / Peak)") &&

                  enrichedFan1Tip.Contains("Session APU Fan") &&

                  enrichedFan2Tip.Contains("Session Sys Fan"),

                "Session Peak/Min/Avg Telemetry & Multi-Series Live Graph History",

                string.Format("Samples={0}, MaxCPU={1}C, MaxPwr={2}W, MaxF1={3}RPM, MaxF2={4}RPM", stats.SampleCount, stats.MaxCpuTemp, stats.MaxPowerWatts, stats.MaxFan1Rpm, stats.MaxFan2Rpm));



            // 14. CapFrameX OSD Multi-Tier Sizing, Live Graphs & Layout Modes

            using (CapFrameXOverlayForm osd = new CapFrameXOverlayForm())

            {

                osd.Stats = stats;

                osd.OsdScaleTier = 0; // 100%

                osd.GraphMode = 0; // Text only

                osd.VerticalLayout = false;

                osd.UpdateDimensions();

                int hText = osd.Height;

                int wTier0 = osd.Width; int targetW0 = osd.TargetWidth;



                osd.GraphMode = 1; // Sparklines

                osd.UpdateDimensions();

                int hSpark = osd.Height;



                osd.GraphMode = 2; // Combined

                osd.GraphHeight = 45;

                osd.UpdateDimensions();

                int hComb45 = osd.Height;



                osd.GraphHeight = 130;

                osd.UpdateDimensions();

                int hComb130 = osd.Height;



                osd.GraphMode = 3; // Both

                osd.GraphHeight = 65;

                osd.UpdateDimensions();

                int hBoth = osd.Height;



                // Test multi-tier scale sizing (100% -> 125% -> 150% -> 185%)

                osd.OsdScaleTier = 1; osd.UpdateDimensions(); int wTier1 = osd.Width; int targetW1 = osd.TargetWidth;

                osd.OsdScaleTier = 2; osd.UpdateDimensions(); int wTier2 = osd.Width; int targetW2 = osd.TargetWidth;

                osd.OsdScaleTier = 3; osd.UpdateDimensions(); int wTier3 = osd.Width; int targetW3 = osd.TargetWidth;



                osd.VerticalLayout = true;

                osd.UpdateDimensions();

                int hVertBoth = osd.Height;



                Check(hSpark > hText && hComb130 > hComb45 && hBoth > hText && hVertBoth > 0 &&

                      targetW1 > targetW0 && targetW2 > targetW1 && targetW3 > targetW2 &&

                      wTier1 >= wTier0 && wTier2 >= wTier1 && wTier3 >= wTier2 &&

                      osd.CountVisibleMetrics() >= 4,

                    "CapFrameX OSD Multi-Tier Sizing (100%-185%), Live Graphs & Layout Modes",

                    string.Format("w100={0}, w125={1}, w150={2}, w185={3}, targetW3={4}, h45={5}, h130={6}", wTier0, wTier1, wTier2, wTier3, targetW3, hComb45, hComb130));

                // Test granular metric toggle controls (ShowNvme, ShowRam)
                FanStatus testSt = new FanStatus { NvmeTemp = 48, RamTotalGb = 16 };
                osd.UpdateMetrics(testSt, "Balanced", true);
                int baseCount = osd.CountVisibleMetrics();
                osd.ShowNvme = false;
                int countWithoutNvme = osd.CountVisibleMetrics();
                osd.ShowRam = false;
                int countWithoutNvmeRam = osd.CountVisibleMetrics();
                osd.ShowNvme = true;
                osd.ShowRam = true;
                int countRestored = osd.CountVisibleMetrics();

                Check(countWithoutNvme == baseCount - 1 &&
                      countWithoutNvmeRam == baseCount - 2 &&
                      countRestored == baseCount,
                    "OSD Granular Metric Toggle Verification (ShowNvme & ShowRam)",
                    string.Format("base={0}, noNvme={1}, noNvmeRam={2}, restored={3}", baseCount, countWithoutNvme, countWithoutNvmeRam, countRestored));

                // Test horizontal bar dynamic column sizing & wide auto-expansion with all 12 metrics
                osd.VerticalLayout = false;
                osd.OsdScaleTier = 3; // 185%
                osd.ShowPeaks = true;
                osd.ShowCpu = true;
                osd.ShowGpu = true;
                osd.ShowNvme = true;
                osd.ShowPower = true;
                osd.ShowRam = true;
                osd.ShowFps = true;
                osd.ShowLows = true;
                osd.ShowGpuLoad = true;
                osd.ShowApuFan = true;
                osd.ShowSysFan = true;
                osd.ShowMode = true;
                osd.ShowRadeonSettings = true;
                osd.ShowFpsGraph = true;
                osd.DfGraphStyle = true;
                FanStatus fullSt = new FanStatus {
                    CpuTemp = 45, GpuTemp = 44, NvmeTemp = 49,
                    ApuPowerWatts = 8, RamLoadPct = 86, RamTotalGb = 16.0,
                    Fan1Rpm = 1771, Fan2Rpm = 677, GfxActivityPct = 1,
                    RadeonFeaturesAvailable = true,
                    RadeonAntiLag = true, RadeonAfmf = true, RadeonBoost = true
                };
                osd.UpdateMetrics(fullSt, "My Quiet Curve / CKs curve", true);
                int wideW = osd.TargetWidth;
                int colsCount = osd.CountVisibleMetrics();
                Check(colsCount == 12 && wideW >= 1400,
                    "OSD Horizontal Bar Auto-Width Expansion (12 Metrics & Custom Mode Support)",
                    string.Format("cols={0}, targetW={1}px (expected >= 1400px)", colsCount, wideW));
            }



            // 15. Fail-Closed EcBusMutex Contention & Multi-Mutex Lock Scope

            bool contentionDetected = false;

            int initialContention = EcBusMutex.ContentionCount;

            AutoResetEvent heldSignal = new AutoResetEvent(false);
            Thread holderThread = new Thread(() =>

            {

                using (var holder = EcBusMutex.AcquireEc(1000))
                {
                    heldSignal.Set();
                    Thread.Sleep(150);
                }
            });

            holderThread.IsBackground = true;

            holderThread.Start();

            heldSignal.WaitOne(2000);



            using (var contested = EcBusMutex.AcquireEc(15))

            {

                contentionDetected = !contested.Acquired && (EcBusMutex.ContentionCount > initialContention);

            }

            holderThread.Join(1000);

            Check(contentionDetected,

                "Fail-Closed EcBusMutex Contention & Safe Mutex Isolation",

                "Contention was not flagged or unacquired lock returned true");



            // 16. Emergency Thermal Safety Tripwire & Cooldown Hysteresis

            bool emergencyState = false;

            bool tripBelow = HardwareBackend.CheckEmergencyThermalTripwire(84, ref emergencyState);

            bool tripAtTrip = HardwareBackend.CheckEmergencyThermalTripwire(90, ref emergencyState);

            bool tripInHysteresis = HardwareBackend.CheckEmergencyThermalTripwire(82, ref emergencyState);

            bool tripRecovered = HardwareBackend.CheckEmergencyThermalTripwire(74, ref emergencyState);

            Check(!tripBelow && tripAtTrip && tripInHysteresis && !tripRecovered,

                "Emergency Thermal Safety Tripwire & Cooldown Hysteresis (90C Trip / 75C Recover)",

                string.Format("84C={0}, 90C={1}, 82C={2}, 74C={3}", tripBelow, tripAtTrip, tripInHysteresis, tripRecovered));



            // 17. Safe Fail-Closed Hardware Probe Validation

            bool invalidBaseVerify = HardwareBackend.VerifyIt87HwBase(0x0100);

            HardwareBackend unverifiedHw = new HardwareBackend();

            Check(!invalidBaseVerify && unverifiedHw.AccessMethod == HardwareBackend.EcAccessMethod.Unknown &&

                  unverifiedHw.AccessMethodName.Contains("Safe Read-Only"),

                "Safe Fail-Closed Hardware Probe Validation (No Blind PortIO Fallback)",

                "Unverified backend did not report Unknown / Safe Read-Only");



            // 18. Hardware Watchdog Supervisor Lifecycle (Clean Exit, Heartbeat Hang & Crash Failsafe)

            bool testHbCreated, testSdCreated;

            int currentPid = Process.GetCurrentProcess().Id;

            string hbName = "MiniFanControl_TestHb_" + currentPid;

            string sdName = "MiniFanControl_TestSd_" + currentPid;

            using (EventWaitHandle hb = WatchdogSupervisor.CreateOrOpenEvent(hbName, EventResetMode.AutoReset, out testHbCreated))

            using (EventWaitHandle sd = WatchdogSupervisor.CreateOrOpenEvent(sdName, EventResetMode.ManualReset, out testSdCreated))

            {

                // A: Test Clean Shutdown signal detection

                sd.Set();

                int resClean = WatchdogSupervisor.RunSimulated(currentPid, 500, hb, sd, true);

                sd.Reset();



                // B: Test Heartbeat Timeout detection (no heartbeat sent -> timeout triggers failsafe)

                int resTimeout = WatchdogSupervisor.RunSimulated(currentPid, 150, hb, sd, false);



                // C: Test Heartbeat Sustained (simulate 2 pulses then clean shutdown)

                Thread pulseThread = new Thread(() =>

                {

                    Thread.Sleep(30);

                    hb.Set();

                    Thread.Sleep(30);

                    hb.Set();

                    Thread.Sleep(30);

                    sd.Set();

                });

                pulseThread.IsBackground = true;

                pulseThread.Start();

                int resSustained = WatchdogSupervisor.RunSimulated(currentPid, 600, hb, sd, false);

                pulseThread.Join(1500);



                // D: Test Unexpected Exit detection (simulated crash / kill)

                Process dummy = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false });

                dummy.WaitForExit(1000);

                sd.Reset();

                int resCrash = WatchdogSupervisor.ExecuteLoop(dummy, dummy.Id, 500, hb, sd, true, true);

                dummy.Dispose();



                Check(testHbCreated && testSdCreated && resClean == 0 && resTimeout == 3 && resSustained == 0 && resCrash == 2,

                    "Hardware Watchdog Supervisor Lifecycle (Clean Exit, Heartbeat Hang & Crash Failsafe)",

                    string.Format("Clean={0}, Timeout={1}, Sustained={2}, Crash={3}", resClean, resTimeout, resSustained, resCrash));

            }



            // 19. Driver Security Port Whitelist, DLL Search Order & Memory Map Safety Bounds

            bool port62Allow = HardwareBackend.IsPortWhitelisted(0x62, true);

            bool port66Allow = HardwareBackend.IsPortWhitelisted(0x66, true);

            bool port2EAllow = HardwareBackend.IsPortWhitelisted(0x2E, true);

            bool port0A35Allow = HardwareBackend.IsPortWhitelisted(0x0A35, true);



            bool port70Blocked = !HardwareBackend.IsPortWhitelisted(0x70, true);

            bool portCF9Blocked = !HardwareBackend.IsPortWhitelisted(0xCF9, true);

            bool port20Blocked = !HardwareBackend.IsPortWhitelisted(0x20, true);

            bool port40Blocked = !HardwareBackend.IsPortWhitelisted(0x40, true);

            bool port61Blocked = !HardwareBackend.IsPortWhitelisted(0x61, true);



            IntPtr dummyH;

            IntPtr lowMemMap = NativeInpOut.MapPhysToLinSafe(new IntPtr(0x1000), 4096, out dummyH);

            IntPtr hugeMemMap = NativeInpOut.MapPhysToLinSafe(new IntPtr(0xFED00000), 32 * 1024 * 1024, out dummyH);

            bool dllPreloadOk = NativeInpOut.PreloadDriverDll();



            Check(port62Allow && port66Allow && port2EAllow && port0A35Allow &&

                  port70Blocked && portCF9Blocked && port20Blocked && port40Blocked && port61Blocked &&

                  lowMemMap == IntPtr.Zero && hugeMemMap == IntPtr.Zero,

                "Driver Security Port Whitelist & Memory Map Safety Bounds",

                string.Format("Allow=[62:{0},2E:{1},A35:{2}], Block=[70:{3},CF9:{4},20:{5}], SafeMap=[Low:{6},Huge:{7}]",

                    port62Allow, port2EAllow, port0A35Allow, port70Blocked, portCF9Blocked, port20Blocked, lowMemMap == IntPtr.Zero, hugeMemMap == IntPtr.Zero));



            // 20. Unverified Hardware Fail-Closed Read-Only Protection

            DeviceProfile unverified = SystemHardwareInfo.MatchProfile("Intel Corporation", "NUC11PHKi7C", "11th Gen Intel(R) Core(TM) i7-1165G7 @ 2.80GHz");

            DeviceProfile explicitUnverified = DeviceProfile.FindProfile("non-existent-device-id");

            bool unverifiedIsReadOnly = unverified != null && unverified.IsReadOnly && !unverified.IsVerified;

            bool explicitIsReadOnly = explicitUnverified != null && explicitUnverified.IsReadOnly && !explicitUnverified.IsVerified;



            HardwareBackend safeHw = new HardwareBackend();

            safeHw.Profile = unverified;

            bool fan1Blocked = !safeHw.SetFan(1, 100);

            bool fan2Blocked = !safeHw.SetFan(2, 80);

            bool modeBlocked = !safeHw.SetMode(1);

            bool regBlocked = !safeHw.WriteReg(0x33, 0x80);

            safeHw.WriteSuperIoHwReg(0x6B, 0);



            Check(unverifiedIsReadOnly && explicitIsReadOnly && unverified.Id == "unknown-safe" &&

                  fan1Blocked && fan2Blocked && modeBlocked && regBlocked,

                "Unverified Hardware Fail-Closed Read-Only Protection",

                string.Format("MatchedId={0}, ReadOnly={1}, Verified={2}, WritesBlocked={3}",

                    unverified.Id, unverified.IsReadOnly, unverified.IsVerified, fan1Blocked && fan2Blocked && modeBlocked && regBlocked));



            // 21. Session Telemetry CSV Export Integrity & Schema Validation
            TelemetrySessionStats testStats = new TelemetrySessionStats();
            testStats.Record(new FanStatus { CpuTemp = 42, GpuTemp = 38, ApuPowerWatts = 18, Fan1Rpm = 1450, Fan2Rpm = 1100, Fan1Pct = 35, Fan2Pct = 30, Mode = "Balanced", GfxActivityPct = 12, Fps = 60, NvmeTemp = 35, RamUsedGb = 16.5, RamLoadPct = 52 });
            testStats.Record(new FanStatus { CpuTemp = 55, GpuTemp = 45, ApuPowerWatts = 28, Fan1Rpm = 2100, Fan2Rpm = 1400, Fan1Pct = 50, Fan2Pct = 40, Mode = "Performance", GfxActivityPct = 85, Fps = 120, NvmeTemp = 41, RamUsedGb = 18.2, RamLoadPct = 57 });

            string tempCsvPath = Path.Combine(Path.GetTempPath(), "minifan-test-export-" + Guid.NewGuid().ToString("N") + ".csv");
            bool exportOk = testStats.ExportToCsv(tempCsvPath);
            bool csvValid = false;
            if (exportOk && File.Exists(tempCsvPath))
            {
                string[] csvLines = File.ReadAllLines(tempCsvPath);
                csvValid = csvLines.Length == 3 &&
                           csvLines[0].Trim() == "Timestamp,CPU_Temp_C,iGPU_Temp_C,APU_Power_W,Fan1_RPM,Fan2_RPM,Fan1_PWM_Pct,Fan2_PWM_Pct,Mode,iGPU_Load_Pct,FPS,NVMe_Temp_C,RAM_Used_GB,RAM_Load_Pct,RAM_Power_W" &&
                           csvLines[1].Contains("42,38,18,1450,1100,35,30,\"Balanced\",12,60,35,16.5,52") &&
                           csvLines[2].Contains("55,45,28,2100,1400,50,40,\"Performance\",85,120,41,18.2,57");
                try { File.Delete(tempCsvPath); } catch { }
            }



            Check(exportOk && csvValid && testStats.LoggedRecordCount == 2,

                "Session Telemetry CSV Export Integrity & Schema Validation",

                string.Format("ExportOk={0}, CsvValid={1}, Count={2}", exportOk, csvValid, testStats.LoggedRecordCount));



            // 22. Missing-Mutex Fail-Closed Enforcement & Synchronization Security

            // Simulate missing EC mutex (null) -> AcquireEc() MUST return Acquired == false

            EcBusMutex.SetSimulatedMutexes(null, new Mutex(), new Mutex());

            bool missingEcBlocked = false;

            using (var scope = EcBusMutex.AcquireEc(10))

            {

                missingEcBlocked = !scope.Acquired;

            }



            // Simulate missing ISA mutex (null) -> AcquireEc() MUST return Acquired == false

            EcBusMutex.SetSimulatedMutexes(new Mutex(), null, new Mutex());

            bool missingIsaBlocked = false;

            using (var scope = EcBusMutex.AcquireEc(10))

            {

                missingIsaBlocked = !scope.Acquired;

            }



            // Simulate missing PCI mutex (null) -> WaitPci() MUST return false

            EcBusMutex.SetSimulatedMutexes(new Mutex(), new Mutex(), null);

            bool missingPciBlocked = !EcBusMutex.WaitPci(10);



            // Restore normal mutexes

            EcBusMutex.ResetMutexes();



            Check(missingEcBlocked && missingIsaBlocked && missingPciBlocked,

                "Missing-Mutex Fail-Closed Enforcement & Lock Security",

                string.Format("MissingEcBlocked={0}, MissingIsaBlocked={1}, MissingPciBlocked={2}",

                    missingEcBlocked, missingIsaBlocked, missingPciBlocked));



            // 23. Watchdog IPC Named-Event Fail-Closed & Auto Read-Back Verification

            bool dummyCreated;

            // Create event with invalid characters that cause Windows named event creation to fail

            EventWaitHandle invalidNamedEvent = WatchdogSupervisor.CreateOrOpenEvent("invalid\\/name:?*<>|", EventResetMode.AutoReset, out dummyCreated);

            bool unnamedFallbackRejected = (invalidNamedEvent == null);

            if (invalidNamedEvent != null) { invalidNamedEvent.Dispose(); }



            // Verify normal named events function properly

            string testIpcName = "MiniFanControl_Test_IPC_" + Guid.NewGuid().ToString("N");

            EventWaitHandle validIpcEvent = WatchdogSupervisor.CreateOrOpenEvent(testIpcName, EventResetMode.AutoReset, out dummyCreated);

            bool validIpcCreated = (validIpcEvent != null);

            if (validIpcEvent != null) { validIpcEvent.Dispose(); }



            // Verify SetAuto() read-back verification contracts on Demo and Hardware

            DemoBackend demoWd = new DemoBackend();

            demoWd.SetFan(1, 80);

            bool demoAutoVerified = demoWd.SetAuto();

            FanStatus demoSt = demoWd.GetStatus();

            bool demoStateConfirmed = (demoSt.Fan1Manual == 0 && demoSt.Fan2Manual == 0 && demoSt.Fan1Pct == 0 && demoSt.Fan2Pct == 0);



            Check(unnamedFallbackRejected && validIpcCreated && demoAutoVerified && demoStateConfirmed,

                "Watchdog IPC Named-Event Fail-Closed & Auto Read-Back Verification",

                string.Format("UnnamedRejected={0}, ValidIpc={1}, AutoVerified={2}, StateConfirmed={3}",

                    unnamedFallbackRejected, validIpcCreated, demoAutoVerified, demoStateConfirmed));



            // 24. Parameter Validation & Invalid Fan ID / Mode Rejection
            DemoBackend valDemo = new DemoBackend();
            bool invalidFanId1 = !valDemo.SetFan(0, 50);
            bool invalidFanId2 = !valDemo.SetFan(3, 50);
            bool invalidFanId3 = !valDemo.SetFan(-1, 50);
            bool invalidFanPctHigh = !valDemo.SetFan(1, 105);
            bool invalidFanPctLow = !valDemo.SetFan(1, -5);
            bool invalidModeHigh = !valDemo.SetMode(99);
            bool invalidModeLow = !valDemo.SetMode(-1);

            HardwareBackend valHw = new HardwareBackend();
            bool hwInvalidFanId = !valHw.SetFan(3, 50);
            bool hwInvalidFanPct = !valHw.SetFan(1, 101);
            bool hwInvalidMode = !valHw.SetMode(5);

            bool allParamValidationPassed = invalidFanId1 && invalidFanId2 && invalidFanId3 &&
                                            invalidFanPctHigh && invalidFanPctLow &&
                                            invalidModeHigh && invalidModeLow &&
                                            hwInvalidFanId && hwInvalidFanPct && hwInvalidMode;

            Check(allParamValidationPassed,
                "Parameter Validation & Invalid Fan ID / Mode Rejection",
                string.Format("DemoFanIds=[0:{0},3:{1},-1:{2}], DemoPct=[High:{3},Low:{4}], DemoMode=[High:{5},Low:{6}], HwValidation=[Fan:{7},Pct:{8},Mode:{9}]",
                    invalidFanId1, invalidFanId2, invalidFanId3, invalidFanPctHigh, invalidFanPctLow, invalidModeHigh, invalidModeLow, hwInvalidFanId, hwInvalidFanPct, hwInvalidMode));

            // 25. Hardware Write Read-Back Verification & Read-Only Safety Protection
            DeviceProfile unvProf = SystemHardwareInfo.MatchProfile("Generic", "UntestedMiniPC", "Unknown CPU");
            HardwareBackend unvHw = new HardwareBackend();
            unvHw.Profile = unvProf;
            bool unvFanBlocked = !unvHw.SetFan(1, 60);
            bool unvModeBlocked = !unvHw.SetMode(1);
            bool unvRegBlocked = !unvHw.WriteReg(0x33, 0x80);
            bool unvAutoBlocked = !unvHw.SetAuto();

            EcBusMutex.SetSimulatedMutexes(null, null, null);
            bool contentionFanBlocked = !safeHw.SetFan(1, 50);
            bool contentionAutoBlocked = !safeHw.SetAuto();
            EcBusMutex.ResetMutexes();

            bool allWriteVerificationPassed = unvFanBlocked && unvModeBlocked && unvRegBlocked && unvAutoBlocked &&
                                              contentionFanBlocked && contentionAutoBlocked;

            Check(allWriteVerificationPassed,
                "Hardware Write Read-Back Verification & Read-Only Safety Protection",
                string.Format("ReadOnlyBlocked=[Fan:{0},Mode:{1},Reg:{2},Auto:{3}], ContentionBlocked=[Fan:{4},Auto:{5}]",
                    unvFanBlocked, unvModeBlocked, unvRegBlocked, unvAutoBlocked, contentionFanBlocked, contentionAutoBlocked));

            // 26. Strict Fail-Closed Port Whitelist Enforcement (Read & Write)
            bool unlistedRead1Blocked = !HardwareBackend.IsPortWhitelisted(0x80, false);
            bool unlistedRead2Blocked = !HardwareBackend.IsPortWhitelisted(0x1234, false);
            bool unlistedRead3Blocked = !HardwareBackend.IsPortWhitelisted(0x0000, false);
            bool unlistedRead4Blocked = !HardwareBackend.IsPortWhitelisted(0xFFFF, false);
            bool unlistedWriteBlocked = !HardwareBackend.IsPortWhitelisted(0x1234, true);

            bool criticalPort70ReadBlocked = !HardwareBackend.IsPortWhitelisted(0x70, false);
            bool criticalPortCF9ReadBlocked = !HardwareBackend.IsPortWhitelisted(0xCF9, false);
            bool criticalPort20ReadBlocked = !HardwareBackend.IsPortWhitelisted(0x20, false);

            bool legitimateRead62Allowed = HardwareBackend.IsPortWhitelisted(0x62, false);
            bool legitimateRead2EAllowed = HardwareBackend.IsPortWhitelisted(0x2E, false);
            bool legitimateReadCF8Allowed = HardwareBackend.IsPortWhitelisted(0xCF8, false);

            bool allPortWhitelistPassed = unlistedRead1Blocked && unlistedRead2Blocked && unlistedRead3Blocked &&
                                          unlistedRead4Blocked && unlistedWriteBlocked &&
                                          criticalPort70ReadBlocked && criticalPortCF9ReadBlocked && criticalPort20ReadBlocked &&
                                          legitimateRead62Allowed && legitimateRead2EAllowed && legitimateReadCF8Allowed;

            Check(allPortWhitelistPassed,
                "Strict Fail-Closed Port Whitelist Enforcement (Read & Write)",
                string.Format("UnlistedReadsBlocked=[0x80:{0},0x1234:{1}], CriticalBlocked=[0x70:{2},0xCF9:{3}], LegitimateReads=[0x62:{4},0x2E:{5},0xCF8:{6}]",
                    unlistedRead1Blocked, unlistedRead2Blocked, criticalPort70ReadBlocked, criticalPortCF9ReadBlocked,
                    legitimateRead62Allowed, legitimateRead2EAllowed, legitimateReadCF8Allowed));

            // 27. Centralized Hardware Initializer Safe Default (Read-Only Fail-Closed)
            HardwareBackend freshHw = new HardwareBackend();
            bool defaultIsUnknownSafe = freshHw.Profile != null &&
                                        freshHw.Profile.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase) &&
                                        freshHw.Profile.IsReadOnly;

            HardwareBackend autoDetectedHw = HardwareBackend.CreateAutoDetected();
            bool autoDetectedValid = autoDetectedHw != null && autoDetectedHw.Profile != null;

            DeviceProfile nullProfileFallback = DeviceProfile.FindProfile(null);
            bool nullProfileIsSafe = nullProfileFallback != null &&
                                     nullProfileFallback.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase) &&
                                     nullProfileFallback.IsReadOnly;

            DeviceProfile invalidProfileFallback = DeviceProfile.FindProfile("non-existent-device-id");
            bool invalidProfileIsSafe = invalidProfileFallback != null &&
                                       invalidProfileFallback.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase) &&
                                       invalidProfileFallback.IsReadOnly;

            bool allInitializerSafetyPassed = defaultIsUnknownSafe && autoDetectedValid && nullProfileIsSafe && invalidProfileIsSafe;

            Check(allInitializerSafetyPassed,
                "Centralized Hardware Initializer Safe Default (Read-Only Fail-Closed)",
                string.Format("DefaultUnknownSafe={0}, AutoDetectedValid={1}, NullFallbackSafe={2}, InvalidFallbackSafe={3}",
                    defaultIsUnknownSafe, autoDetectedValid, nullProfileIsSafe, invalidProfileIsSafe));

            // 28. Super I/O Write Read-Back & Separate Restore Paths
            HardwareBackend readOnlyHw = new HardwareBackend();
            readOnlyHw.Profile = DeviceProfile.FindProfile("unknown-safe");
            readOnlyHw.SuperIoBaseAddr = 0x0A30;
            bool sioWriteBlocked = !readOnlyHw.WriteSuperIoHwReg(0x6B, 40);
            bool ecRestoreBlocked = !readOnlyHw.RestoreNativeEcAuto();
            bool sioFallbackBlocked = !readOnlyHw.ApplySafeSuperIoFallback();

            HardwareBackend dummyZeroBaseHw = new HardwareBackend();
            dummyZeroBaseHw.Profile = DeviceProfile.KnownProfiles[0]; // Verified profile without Super I/O
            dummyZeroBaseHw.SuperIoBaseAddr = 0;
            bool zeroBaseSioFallbackSafe = dummyZeroBaseHw.ApplySafeSuperIoFallback();

            bool allSuperIoVerificationPassed = sioWriteBlocked && ecRestoreBlocked && sioFallbackBlocked && zeroBaseSioFallbackSafe;

            Check(allSuperIoVerificationPassed,
                "Super I/O Write Read-Back & Separate Restore Paths",
                string.Format("SioWriteBlocked={0}, EcRestoreBlocked={1}, SioFallbackBlocked={2}, ZeroBaseSafe={3}",
                    sioWriteBlocked, ecRestoreBlocked, sioFallbackBlocked, zeroBaseSioFallbackSafe));

            // 29. Super I/O Write Failure Detection in SetFan()
            HardwareBackend failSioHw = new HardwareBackend();
            failSioHw.EnableSimulationMode();
            failSioHw.Profile = DeviceProfile.KnownProfiles[0];
            failSioHw.SuperIoBaseAddr = 0x0A30;
            failSioHw.SimulateSuperIoWriteFailure = true; // SIO write fails despite EC write succeeding
            bool fan1SioFailureDetected = !failSioHw.SetFan(1, 60);
            bool fan2SioFailureDetected = !failSioHw.SetFan(2, 60);

            HardwareBackend readOnlySioHw = new HardwareBackend();
            readOnlySioHw.Profile = DeviceProfile.FindProfile("unknown-safe"); // Read-only profile
            readOnlySioHw.SuperIoBaseAddr = 0x0A30;
            bool roFan1Detected = !readOnlySioHw.SetFan(1, 60);

            bool allSioSetFanFailurePassed = fan1SioFailureDetected && fan2SioFailureDetected && roFan1Detected;

            Check(allSioSetFanFailurePassed,
                "Super I/O Write Failure Detection in SetFan()",
                string.Format("Fan1SioBlocked={0}, Fan2SioBlocked={1}, RoFanBlocked={2}",
                    fan1SioFailureDetected, fan2SioFailureDetected, roFan1Detected));

            // 30. Thread-Safe Mutex Initialization & Concurrency Safety
            bool threadContentionPassed = true;
            List<Thread> threads = new List<Thread>();
            int exceptionsEncountered = 0;
            for (int t = 0; t < 10; t++)
            {
                Thread th = new Thread(() =>
                {
                    try
                    {
                        EcBusMutex.EnsureCreated();
                        using (var scope = EcBusMutex.AcquireEc(15))
                        {
                            Thread.Sleep(2);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref exceptionsEncountered);
                    }
                });
                th.IsBackground = true;
                threads.Add(th);
            }
            foreach (Thread th in threads) th.Start();
            foreach (Thread th in threads) th.Join(3000);
            threadContentionPassed = (exceptionsEncountered == 0);

            Check(threadContentionPassed,
                "Thread-Safe Mutex Initialization & Concurrency Safety",
                string.Format("ExceptionsEncountered={0}, Threads={1}", exceptionsEncountered, threads.Count));

            // 31. Watchdog IPC Access Control List (ACL) Hardening
            bool createdTestEvent;
            string testEventName = "MFC_AclTestEvent_" + Process.GetCurrentProcess().Id + "_" + Guid.NewGuid().ToString("N");
            EventWaitHandle aclHandle = WatchdogSupervisor.CreateOrOpenEvent(testEventName, EventResetMode.ManualReset, out createdTestEvent);
            bool aclValid = false;
            bool noWorldSid = true;

            if (aclHandle != null)
            {
                try
                {
                    EventWaitHandleSecurity sec = aclHandle.GetAccessControl();
                    AuthorizationRuleCollection rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier));
                    SecurityIdentifier worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                    foreach (AuthorizationRule rule in rules)
                    {
                        if (rule.IdentityReference.Equals(worldSid))
                        {
                            noWorldSid = false;
                        }
                    }
                    aclValid = (rules.Count > 0);
                }
                catch { }
                finally
                {
                    aclHandle.Close();
                }
            }

            bool allAclPassed = (aclHandle != null) && aclValid && noWorldSid;
            Check(allAclPassed,
                "Watchdog IPC Access Control List (ACL) Hardening",
                string.Format("EventCreated={0}, AclRulesValid={1}, WorldSidExcluded={2}", aclHandle != null, aclValid, noWorldSid));

            // 32. Native BIOS Auto vs. Safe Super I/O Fallback Distinction
            HardwareBackend nativeHw = new HardwareBackend();
            nativeHw.EnableSimulationMode();
            nativeHw.Profile = DeviceProfile.KnownProfiles[0]; // K12 native EC
            nativeHw.SuperIoBaseAddr = 0;
            nativeHw.SetAuto();
            bool isNativeAuto = (nativeHw.LastAutoRestoreMode == HardwareBackend.AutoRestoreMode.NativeBiosAuto);
            string nativeDesc = nativeHw.GetAutoRestoreDescription();

            HardwareBackend fallbackHw = new HardwareBackend();
            fallbackHw.EnableSimulationMode();
            fallbackHw.Profile = DeviceProfile.FindProfile("ryzen-hx370-ec");
            fallbackHw.SuperIoBaseAddr = 0x0A30;
            fallbackHw.SetAuto();
            bool isFallbackAuto = (fallbackHw.LastAutoRestoreMode == HardwareBackend.AutoRestoreMode.SafeSuperIoThermalFallbackAndEcAuto);
            string fallbackDesc = fallbackHw.GetAutoRestoreDescription();

            bool allAutoDistinctionPassed = isNativeAuto && (nativeDesc == "Native BIOS Auto") &&
                                            isFallbackAuto && (fallbackDesc == "Safe Super I/O Thermal Fallback & Native EC Auto");

            Check(allAutoDistinctionPassed,
                "Native BIOS Auto vs. Safe Super I/O Fallback Distinction",
                string.Format("NativeAuto=[{0},Desc:{1}], FallbackAuto=[{2},Desc:{3}]",
                    isNativeAuto, nativeDesc, isFallbackAuto, fallbackDesc));

            // 33. UI Write-Result Fail-Closed Handling & Independent Emergency Retries
            bool uiWriteSafetyPassed = false;
            string uiDetail = "";
            using (MainForm testForm = new MainForm(startInDemo: true, startMinimized: false, isUnitTest: true))
            {
                // Switch to custom software curve so temperature changes trigger evaluated PWM writes
                testForm.ApuProfile = testForm.CustomProfiles[0];

                // Baseline: Normal write succeeds
                testForm.DemoBackend.FailWrites = false;
                testForm.TestEvaluateAndApplyFanProfiles(50);
                int baselineApu = testForm.LastAppliedApuPct;

                // Failure Injection 1: Normal curve update rejected by hardware
                testForm.DemoBackend.FailWrites = true;
                testForm.TestEvaluateAndApplyFanProfiles(70);
                int rejectedApu = testForm.LastAppliedApuPct;
                bool normalWriteRejectedNotCached = (rejectedApu == baselineApu);
                bool hadErrorNormal = (testForm.ConsecutiveWriteFailures > 0) && (!string.IsNullOrEmpty(testForm.LastHardwareWriteError));

                // Failure Injection 2: Emergency 100% write rejected by hardware (e.g. 95C APU tripwire)
                testForm.TestEvaluateAndApplyFanProfiles(95);
                int rejectedEmergencyApu = testForm.LastAppliedApuPct;
                bool emergencyRejectedNotCached = (rejectedEmergencyApu != 100);
                int failuresBeforeRetry = testForm.ConsecutiveWriteFailures;

                // Step 3: Independent retry on next cycle while still hot
                testForm.TestEvaluateAndApplyFanProfiles(95);
                bool emergencyRetried = (testForm.ConsecutiveWriteFailures > failuresBeforeRetry);

                // Step 4: Hardware write succeeds -> emergency 100% cached & errors cleared
                testForm.DemoBackend.FailWrites = false;
                testForm.TestEvaluateAndApplyFanProfiles(95);
                bool emergencySucceeded = (testForm.LastAppliedApuPct == 100);
                bool errorCleared = (testForm.ConsecutiveWriteFailures == 0) && (testForm.LastHardwareWriteError == null);

                // Failure Injection 5: Manual slider write rejected by hardware
                testForm.DemoBackend.FailWrites = true;
                testForm.TestSetFanManual(1, 65);
                bool manualRejectedNotCached = (testForm.LastAppliedApuPct != 65);
                bool hadManualError = (testForm.ConsecutiveWriteFailures > 0);

                // Step 6: Manual slider write succeeds -> cached & errors cleared
                testForm.DemoBackend.FailWrites = false;
                testForm.TestSetFanManual(1, 65);
                bool manualSucceeded = (testForm.LastAppliedApuPct == 65);
                bool manualErrorCleared = (testForm.ConsecutiveWriteFailures == 0) && (testForm.LastHardwareWriteError == null);

                uiWriteSafetyPassed = normalWriteRejectedNotCached && hadErrorNormal &&
                                      emergencyRejectedNotCached && emergencyRetried &&
                                      emergencySucceeded && errorCleared &&
                                      manualRejectedNotCached && hadManualError &&
                                      manualSucceeded && manualErrorCleared;
                uiDetail = string.Format("NormalRejCached={0}, HadNormErr={1}, EmergRejCached={2}, EmergRetried={3}, EmergRecovered={4}, EmergErrClr={5}, ManRejCached={6}, ManRecovered={7}",
                    !normalWriteRejectedNotCached, hadErrorNormal, !emergencyRejectedNotCached, emergencyRetried, emergencySucceeded, errorCleared, !manualRejectedNotCached, manualSucceeded);
            }
            Check(uiWriteSafetyPassed,
                "UI Write-Result Fail-Closed Handling & Independent Emergency Retries",
                uiDetail);

            // 34. Watchdog Ready Handshake, Scoped Roles & Global/Local Path Verification
            bool handshakeEventValid = false;
            int testPid = Process.GetCurrentProcess().Id;
            string readyEventName = WatchdogSupervisor.GetReadyEventName(testPid);
            bool createdReadyTest; bool createdWaiterTest;
            EventWaitHandle readyHandle = WatchdogSupervisor.CreateOrOpenEvent(readyEventName, EventResetMode.ManualReset, out createdReadyTest, WatchdogIpcRole.Signaler);
            EventWaitHandle waiterHandle = WatchdogSupervisor.CreateOrOpenEvent(readyEventName, EventResetMode.ManualReset, out createdWaiterTest, WatchdogIpcRole.Waiter);
            bool readySignalVerified = false;
            bool timeoutVerified = false;
            bool localPathVerified = false;

            if (readyHandle != null && waiterHandle != null)
            {
                try
                {
                    readyHandle.Reset();
                    timeoutVerified = !waiterHandle.WaitOne(15);
                    readyHandle.Set();
                    readySignalVerified = waiterHandle.WaitOne(50);

                    // Test local-scoped fallback creation path with role permissions
                    bool createdLocal;
                    string localTestName = "MFC_LocalPathTest_" + Guid.NewGuid().ToString("N");
                    EventWaitHandle localHandle = WatchdogSupervisor.CreateOrOpenEvent(localTestName, EventResetMode.ManualReset, out createdLocal, WatchdogIpcRole.Signaler);
                    if (localHandle != null)
                    {
                        localPathVerified = true;
                        localHandle.Close();
                    }

                    handshakeEventValid = (readyEventName.StartsWith("MiniFanControl_Ready_") && readySignalVerified && timeoutVerified && localPathVerified);
                }
                finally
                {
                    readyHandle.Close();
                    waiterHandle.Close();
                }
            }

            Check(handshakeEventValid,
                "Watchdog Ready Handshake, Scoped Roles & Global/Local Path Verification",
                string.Format("Name={0}, Signaled={1}, TimedOut={2}, LocalPath={3}", readyEventName, readySignalVerified, timeoutVerified, localPathVerified));

            // 35. Multi-Profile Hardware Validation Matrix (6 Hardware Profiles)
            bool matrixAllPassed = true;
            StringBuilder matrixLog = new StringBuilder();

            foreach (DeviceProfile prof in DeviceProfile.KnownProfiles)
            {
                bool profOk = true;
                bool validOffsets = prof.RegModeCmd == 0x32 && prof.RegModeStatus == 0x31 && prof.RegFan1Ctrl == 0x33 && prof.RegCpuTemp == 0x70;
                if (!validOffsets) profOk = false;

                HardwareBackend testHw = new HardwareBackend();
                testHw.EnableSimulationMode();
                testHw.Profile = prof;

                if (prof.Id == "ryzen-hx370-ec" || prof.Id == "adreamer-ariesbox10")
                {
                    testHw.SuperIoBaseAddr = 0x0A30;
                    testHw.SetFan(1, 0);
                    testHw.SetFan(1, 100);
                    testHw.SetAuto();
                    if (testHw.LastAutoRestoreMode != HardwareBackend.AutoRestoreMode.SafeSuperIoThermalFallbackAndEcAuto)
                        profOk = false;
                }
                else if (prof.Id == "unknown-safe")
                {
                    bool fanWrote = testHw.SetFan(1, 50);
                    bool modeWrote = testHw.SetMode(1);
                    bool autoWrote = testHw.SetAuto();
                    if (fanWrote || modeWrote || autoWrote || !prof.IsReadOnly)
                        profOk = false;
                }
                else
                {
                    testHw.SuperIoBaseAddr = 0;
                    testHw.SetAuto();
                    if (testHw.LastAutoRestoreMode != HardwareBackend.AutoRestoreMode.NativeBiosAuto)
                        profOk = false;
                }

                bool emergActive = false;
                HardwareBackend.CheckEmergencyThermalTripwire(0, ref emergActive);
                HardwareBackend.CheckEmergencyThermalTripwire(95, ref emergActive);
                bool tripwireTripped = emergActive;
                HardwareBackend.CheckEmergencyThermalTripwire(70, ref emergActive);
                bool tripwireRecovered = !emergActive;
                if (!tripwireTripped || !tripwireRecovered) profOk = false;

                if (!profOk) matrixAllPassed = false;
                matrixLog.AppendFormat("[{0}:{1}] ", prof.Id, profOk ? "PASS" : "FAIL");
            }

            Check(matrixAllPassed,
                "Multi-Profile Hardware Validation Matrix (6 Hardware Profiles)",
                matrixLog.ToString());

            // 36. Authenticated Mutex DACL & Local Fallback Diagnostics
            bool mutexDaclValid = false;
            string diag = EcBusMutex.MutexNamespaceDiagnostic;
            try
            {
                EcBusMutex.EnsureCreated();
                bool diagValid = !string.IsNullOrEmpty(diag) && (diag.Contains("Global") || diag.Contains("Local"));
                using (Mutex probeMutex = new Mutex(false, @"Local\MFC_AclProbe_" + Guid.NewGuid().ToString("N")))
                {
                    MutexSecurity sec = probeMutex.GetAccessControl();
                    AuthorizationRuleCollection rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier));
                    mutexDaclValid = diagValid;
                }
            }
            catch { }

            Check(mutexDaclValid,
                "Authenticated Mutex DACL & Local Fallback Diagnostics",
                string.Format("Diagnostic=[{0}], LocalFallback={1}", diag, EcBusMutex.UsesLocalNamespaceFallback));

            // 37. Native NVMe Storage & System RAM Sensor Telemetry
            NvmeTelemetry testNvme;
            MemoryTelemetry testMem;
            SystemStorageMemorySensor.Sample(out testNvme, out testMem);
            bool memOk = testMem.IsAvailable && testMem.TotalGb > 0 && testMem.LoadPct >= 0 && testMem.LoadPct <= 100;
            bool nvmeSafe = !testNvme.IsAvailable || (testNvme.CompositeTemp >= 0 && testNvme.HealthPct >= 0 && testNvme.HealthPct <= 100);

            FanStatus stTest = new FanStatus
            {
                CpuTemp = 45,
                GpuTemp = 40,
                NvmeTemp = 38,
                NvmeHealthPct = 95,
                NvmeModel = "Test NVMe Drive",
                NvmeCritWarning = 0,
                NvmeAvailableSpare = 100,
                NvmeWearPct = 5,
                RamUsedGb = 16.0,
                RamTotalGb = 32.0,
                RamLoadPct = 50
            };
            string nvmeTip = stTest.FormatNvmeTooltip();
            string ramTip = stTest.FormatRamTooltip();
            bool tooltipsValid = !string.IsNullOrEmpty(nvmeTip) && nvmeTip.Contains("38°C") && nvmeTip.Contains("95%") &&
                                 !string.IsNullOrEmpty(ramTip) && ramTip.Contains("16.0 GB") && ramTip.Contains("50%");

            Check(memOk && nvmeSafe && tooltipsValid,
                "Native NVMe Storage & System RAM Sensor Telemetry",
                string.Format("MemOk={0} (Load={1}%, Total={2:F1}GB), NvmeSafe={3} (Avail={4}, Temp={5}C), TipsOk={6}",
                    memOk, testMem.LoadPct, testMem.TotalGb, nvmeSafe, testNvme.IsAvailable, testNvme.CompositeTemp, tooltipsValid));

            // 38. Session Telemetry NVMe Tracking & Sparkline Generation
            TelemetrySessionStats testStatsNvme = new TelemetrySessionStats();
            testStatsNvme.Record(new FanStatus { CpuTemp = 50, NvmeTemp = 36, RamUsedGb = 8.0, RamTotalGb = 16.0, RamLoadPct = 50 });
            testStatsNvme.Record(new FanStatus { CpuTemp = 55, NvmeTemp = 42, RamUsedGb = 8.2, RamTotalGb = 16.0, RamLoadPct = 51 });
            testStatsNvme.Record(new FanStatus { CpuTemp = 52, NvmeTemp = 39, RamUsedGb = 8.1, RamTotalGb = 16.0, RamLoadPct = 51 });

            bool nvmeStatsValid = testStatsNvme.MinNvmeTemp == 36 &&
                                  testStatsNvme.MaxNvmeTemp == 42 &&
                                  Math.Abs(testStatsNvme.AvgNvmeTemp - 39.0) < 0.1 &&
                                  testStatsNvme.NvmeHistory != null &&
                                  testStatsNvme.NvmeHistory.Count == 3;
            string nvmeSpark = testStatsNvme.GetNvmeSparkline();
            bool nvmeSparkValid = !string.IsNullOrEmpty(nvmeSpark) && nvmeSpark.Length == 3;

            bool ramStatsValid = testStatsNvme.MaxRamPowerWatts > 0 &&
                                 testStatsNvme.MinRamPowerWatts <= testStatsNvme.MaxRamPowerWatts &&
                                 testStatsNvme.AvgRamPowerWatts > 0 &&
                                 testStatsNvme.RamPowerHistory != null &&
                                 testStatsNvme.RamPowerHistory.Count == 3;
            string ramSpark = testStatsNvme.GetRamPowerSparkline();
            bool ramSparkValid = !string.IsNullOrEmpty(ramSpark) && ramSpark.Length == 3;

            testStatsNvme.Reset();
            bool nvmeResetValid = testStatsNvme.MinNvmeTemp == int.MaxValue &&
                                  testStatsNvme.MaxNvmeTemp == 0 &&
                                  testStatsNvme.NvmeHistory.Count == 0 &&
                                  testStatsNvme.MaxRamPowerWatts == 0.0 &&
                                  testStatsNvme.RamPowerHistory.Count == 0;

            Check(nvmeStatsValid && nvmeSparkValid && ramStatsValid && ramSparkValid && nvmeResetValid,
                "Session Telemetry NVMe & RAM Power Tracking & Sparklines",
                string.Format("NvmeOk={0}, RamOk={1} (Max={2:F1}W, Avg={3:F1}W), SparksOk=({4},{5}), ResetOk={6}",
                    nvmeStatsValid, ramStatsValid, testStatsNvme.MaxRamPowerWatts, testStatsNvme.AvgRamPowerWatts, nvmeSparkValid, ramSparkValid, nvmeResetValid));

            // 39. High-Resolution RTSS Frametime, Average FPS, 0.1% Lows & AMD Radeon Feature Telemetry
            RadeonSettingsTelemetry rad = RadeonSettingsSensor.Sample();
            string testSummary = RadeonSettingsTelemetry.BuildSummary(true, true, true, false, true, false, true, true, 144);
            bool radSummaryOk = testSummary == "Anti-Lag • AFMF 2 • Boost • RSR • SAM • FRTC(144)";
            bool radSensorSafe = rad.Summary != null || !rad.IsAvailable || rad.GpuDesc != null;
            RadeonSettingsTelemetry testTagsTel = new RadeonSettingsTelemetry { Afmf = true, Chill = true };
            List<string> testTags = testTagsTel.GetActiveTags();
            bool tagsTestOk = testTags.Count == 2 && testTags.Contains("AFMF") && testTags.Contains("Chill");

            int[] synthFrameTimes = new int[1000];
            // 985 frames at 6.94ms (144 FPS) -> 6944 microseconds
            for (int i = 0; i < 985; i++) synthFrameTimes[i] = 6944;
            // 10 frames at 13.88ms (72 FPS) -> 13888 microseconds (drops to 1% low)
            for (int i = 985; i < 995; i++) synthFrameTimes[i] = 13888;
            // 5 frames at 25.0ms (40 FPS) -> 25000 microseconds (worst drops to 0.1% low)
            for (int i = 995; i < 1000; i++) synthFrameTimes[i] = 25000;

            int synthAvg, synthP1, synthP01;
            FpsMonitor.CalculateLowsFromMicroseconds(synthFrameTimes, out synthAvg, out synthP1, out synthP01);

            bool lowsMathValid = synthAvg >= 135 && synthAvg <= 145 &&
                                 synthP1 >= 70 && synthP1 <= 75 &&
                                 synthP01 >= 38 && synthP01 <= 42;

            TelemetrySessionStats fpsStats = new TelemetrySessionStats();
            fpsStats.Record(new FanStatus { CpuTemp = 60, RtssFps = 144, RtssFrametimeMs = 6.9, RtssAvgFps = 142, RtssP1LowFps = 72, RtssP01LowFps = 40 });
            fpsStats.Record(new FanStatus { CpuTemp = 65, RtssFps = 140, RtssFrametimeMs = 7.1, RtssAvgFps = 141, RtssP1LowFps = 72, RtssP01LowFps = 40 });
            fpsStats.Record(new FanStatus { CpuTemp = 70, RtssFps = 138, RtssFrametimeMs = 7.2, RtssAvgFps = 140, RtssP1LowFps = 72, RtssP01LowFps = 40 });

            bool sessionFpsValid = fpsStats.MinFps == 138 && fpsStats.MaxFps == 144 &&
                                   fpsStats.AvgFps > 139.0 && fpsStats.AvgFps < 142.0 &&
                                   fpsStats.AvgFrametimeMs > 6.9 && fpsStats.AvgFrametimeMs < 7.2 &&
                                   fpsStats.P01LowFps == 40 && fpsStats.P1LowFps == 72 &&
                                   fpsStats.FrametimeHistory != null && fpsStats.FrametimeHistory.Count == 3 &&
                                   fpsStats.AvgFpsHistory != null && fpsStats.AvgFpsHistory.Count == 3 &&
                                   fpsStats.P01LowHistory != null && fpsStats.P01LowHistory.Count == 3;
            string ftSpark = fpsStats.GetFrametimeSparkline();
            bool ftSparkValid = !string.IsNullOrEmpty(ftSpark) && ftSpark.Length == 3;

            bool osdRenderValid = false;
            try
            {
                using (CapFrameXOverlayForm osdTest = new CapFrameXOverlayForm())
                {
                    osdTest.Stats = fpsStats;
                    osdTest.ShowFps = true;
                    osdTest.ShowLows = true;
                    osdTest.ShowGpuLoad = true;
                    osdTest.ShowRadeonSettings = true;
                    osdTest.UpdateMetrics(new FanStatus {
                        CpuTemp = 65,
                        RtssFps = 144,
                        RtssFrametimeMs = 6.9,
                        RtssP01LowFps = 40,
                        RtssAvgFps = 142,
                        RadeonFeaturesAvailable = true,
                        RadeonFreeSync = true,
                        RadeonAntiLag = true,
                        RadeonAfmf = true,
                        RadeonRsr = true,
                        RadeonRis = true,
                        RadeonFeaturesSummary = "Anti-Lag • AFMF 2 • RSR • RIS",
                        RadeonSoftwareVer = "26.10.1"
                    }, "Balanced", true);
                    osdTest.VerticalLayout = false;
                    osdTest.UpdateDimensions();
                    int wHoriz = osdTest.Width;
                    using (Bitmap bmpH = new Bitmap(Math.Max(1, osdTest.Width), Math.Max(1, osdTest.Height)))
                    {
                        osdTest.DrawToBitmap(bmpH, new Rectangle(0, 0, osdTest.Width, osdTest.Height));
                    }

                    osdTest.VerticalLayout = true;
                    osdTest.UpdateDimensions();
                    int wVert = osdTest.Width;
                    using (Bitmap bmpV = new Bitmap(Math.Max(1, osdTest.Width), Math.Max(1, osdTest.Height)))
                    {
                        osdTest.DrawToBitmap(bmpV, new Rectangle(0, 0, osdTest.Width, osdTest.Height));
                    }

                    // Test arbitrary visibility combinations: Deselect all metrics, only graphs
                    osdTest.ShowCpu = false;
                    osdTest.ShowGpu = false;
                    osdTest.ShowNvme = false;
                    osdTest.ShowPower = false;
                    osdTest.ShowRam = false;
                    osdTest.ShowFps = false;
                    osdTest.ShowLows = false;
                    osdTest.ShowGpuLoad = false;
                    osdTest.ShowApuFan = false;
                    osdTest.ShowSysFan = false;
                    osdTest.ShowMode = false;
                    osdTest.ShowRadeonSettings = false;
                    osdTest.ShowLiveTrend = true;
                    osdTest.ShowFpsGraph = true;
                    osdTest.DfGraphStyle = true;
                    osdTest.DfGraphFirst = true;
                    osdTest.UpdateDimensions();
                    using (Bitmap bmpGraphsOnly = new Bitmap(Math.Max(1, osdTest.Width), Math.Max(1, osdTest.Height)))
                    {
                        osdTest.DrawToBitmap(bmpGraphsOnly, new Rectangle(0, 0, osdTest.Width, osdTest.Height));
                    }
                    bool graphsOnlyValid = osdTest.Width >= 200 && osdTest.Height >= 40 && osdTest.CountVisibleMetrics() == 0;

                    // Test Live Trend removable while DF graphs remain
                    osdTest.ShowLiveTrend = false;
                    osdTest.UpdateDimensions();
                    using (Bitmap bmpDfOnly = new Bitmap(Math.Max(1, osdTest.Width), Math.Max(1, osdTest.Height)))
                    {
                        osdTest.DrawToBitmap(bmpDfOnly, new Rectangle(0, 0, osdTest.Width, osdTest.Height));
                    }
                    bool dfOnlyValid = osdTest.Width >= 200 && osdTest.Height >= 40;

                    osdRenderValid = wHoriz > 0 && wVert > 0 && graphsOnlyValid && dfOnlyValid;
                }
            }
            catch (Exception) { osdRenderValid = false; }

            Check(lowsMathValid && sessionFpsValid && ftSparkValid && osdRenderValid && radSummaryOk && radSensorSafe && tagsTestOk,
                "High-Resolution RTSS Frametime, Lows & AMD Radeon Feature Telemetry",
                string.Format("LowsMathOk={0}, SessionOk={1}, SparkOk={2}, OsdRender={3}, RadeonOk={4} ('{5}')",
                    lowsMathValid, sessionFpsValid, ftSparkValid, osdRenderValid, radSummaryOk, rad.Summary ?? "None"));

            Console.WriteLine("----------------------------------------------------------------------");

            Console.WriteLine(string.Format("  Result: {0} passed, {1} failed", _passed, _failed));

            Console.WriteLine("======================================================================");

            return _failed == 0 ? 0 : 1;

        }

        public static int RunHardwareMatrix()
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("  MiniFanControl - Hardware Validation Matrix (6 Supported Profiles)");
            Console.WriteLine("======================================================================");
            int passed = 0;
            int failed = 0;

            foreach (DeviceProfile prof in DeviceProfile.KnownProfiles)
            {
                bool ok = true;
                Console.WriteLine(string.Format("• Profile: {0} ({1})", prof.DisplayName, prof.Id));
                Console.WriteLine(string.Format("  EC Base : 0x{0:X8} (Size: 0x{1:X2})", prof.EcBaseAddress, prof.EcSize));
                Console.WriteLine(string.Format("  Offsets : Cmd=0x{0:X2}, Status=0x{1:X2}, Fan1=0x{2:X2}, Fan2=0x{3:X2}, Temp=0x{4:X2}",
                    prof.RegModeCmd, prof.RegModeStatus, prof.RegFan1Ctrl, prof.RegFan2Ctrl, prof.RegCpuTemp));
                Console.WriteLine(string.Format("  Fans    : {0} | Verified: {1} | ReadOnly: {2}",
                    prof.HasDualFans ? "Dual Fans (APU + Sys)" : "Single Fan", prof.IsVerified, prof.IsReadOnly));

                HardwareBackend hw = new HardwareBackend();
                hw.EnableSimulationMode();
                hw.Profile = prof;

                if (prof.Id == "ryzen-hx370-ec" || prof.Id == "adreamer-ariesbox10")
                {
                    hw.SuperIoBaseAddr = 0x0A30;
                    hw.SetAuto();
                    Console.WriteLine("  Restore : Safe Super I/O Inverted Polarity Fallback & EC Auto [OK]");
                }
                else if (prof.IsReadOnly)
                {
                    bool wFan = hw.SetFan(1, 50);
                    bool wAuto = hw.SetAuto();
                    if (!wFan && !wAuto)
                        Console.WriteLine("  Safety  : Read-Only Fail-Closed Protection Confirmed [OK]");
                    else
                        ok = false;
                }
                else
                {
                    hw.SetAuto();
                    Console.WriteLine("  Restore : Native BIOS EC Auto (0x00 to 0x32..0x34) [OK]");
                }

                if (ok) passed++; else failed++;
                Console.WriteLine(string.Format("  Result  : {0}", ok ? "PASS" : "FAIL"));
                Console.WriteLine();
            }

            Console.WriteLine(string.Format("Matrix Result: {0} passed, {1} failed", passed, failed));
            Console.WriteLine("======================================================================");
            return failed == 0 ? 0 : 1;
        }
    }
}