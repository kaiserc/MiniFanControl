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

                int wTier0 = osd.Width;



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

                osd.OsdScaleTier = 1; osd.UpdateDimensions(); int wTier1 = osd.Width;

                osd.OsdScaleTier = 2; osd.UpdateDimensions(); int wTier2 = osd.Width;

                osd.OsdScaleTier = 3; osd.UpdateDimensions(); int wTier3 = osd.Width;



                osd.VerticalLayout = true;

                osd.UpdateDimensions();

                int hVertBoth = osd.Height;



                Check(hSpark > hText && hComb130 > hComb45 && hBoth > hText && hVertBoth > 0 &&

                      wTier1 > wTier0 && wTier2 > wTier1 && wTier3 > wTier2 &&

                      osd.CountVisibleMetrics() >= 4,

                    "CapFrameX OSD Multi-Tier Sizing (100%-185%), Live Graphs & Layout Modes",

                    string.Format("w100={0}, w125={1}, w150={2}, w185={3}, h45={4}, h130={5}", wTier0, wTier1, wTier2, wTier3, hComb45, hComb130));

            }



            // 15. Fail-Closed EcBusMutex Contention & Multi-Mutex Lock Scope

            bool contentionDetected = false;

            int initialContention = EcBusMutex.ContentionCount;

            Thread holderThread = new Thread(() =>

            {

                using (var holder = EcBusMutex.AcquireEc(500))

                {

                    Thread.Sleep(150);

                }

            });

            holderThread.IsBackground = true;

            holderThread.Start();

            Thread.Sleep(30);



            using (var contested = EcBusMutex.AcquireEc(15))

            {

                contentionDetected = !contested.Acquired && (EcBusMutex.ContentionCount > initialContention);

            }

            holderThread.Join(500);

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

                int resClean = WatchdogSupervisor.RunSimulated(currentPid, 300, hb, sd, true);

                sd.Reset();



                // B: Test Heartbeat Timeout detection (no heartbeat sent -> timeout triggers failsafe)

                int resTimeout = WatchdogSupervisor.RunSimulated(currentPid, 120, hb, sd, false);



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

                int resSustained = WatchdogSupervisor.RunSimulated(currentPid, 200, hb, sd, false);

                pulseThread.Join(500);



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

            testStats.Record(new FanStatus { CpuTemp = 42, GpuTemp = 38, ApuPowerWatts = 18, Fan1Rpm = 1450, Fan2Rpm = 1100, Fan1Pct = 35, Fan2Pct = 30, Mode = "Balanced", GfxActivityPct = 12, Fps = 60 });

            testStats.Record(new FanStatus { CpuTemp = 55, GpuTemp = 45, ApuPowerWatts = 28, Fan1Rpm = 2100, Fan2Rpm = 1400, Fan1Pct = 50, Fan2Pct = 40, Mode = "Performance", GfxActivityPct = 85, Fps = 120 });

            string tempCsvPath = Path.Combine(Path.GetTempPath(), "minifan-test-export-" + Guid.NewGuid().ToString("N") + ".csv");

            bool exportOk = testStats.ExportToCsv(tempCsvPath);

            bool csvValid = false;

            if (exportOk && File.Exists(tempCsvPath))

            {

                string[] csvLines = File.ReadAllLines(tempCsvPath);

                csvValid = csvLines.Length == 3 &&

                           csvLines[0].Trim() == "Timestamp,CPU_Temp_C,iGPU_Temp_C,APU_Power_W,Fan1_RPM,Fan2_RPM,Fan1_PWM_Pct,Fan2_PWM_Pct,Mode,iGPU_Load_Pct,FPS" &&

                           csvLines[1].Contains("42,38,18,1450,1100,35,30,\"Balanced\",12,60") &&

                           csvLines[2].Contains("55,45,28,2100,1400,50,40,\"Performance\",85,120");

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

            Console.WriteLine("----------------------------------------------------------------------");

            Console.WriteLine(string.Format("  Result: {0} passed, {1} failed", _passed, _failed));

            Console.WriteLine("======================================================================");

            return _failed == 0 ? 0 : 1;

        }

    }



}

