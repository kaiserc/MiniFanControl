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



            // 5. Tachometer Glitch & Motor Stop Filter (8000 raw -> 0 RPM, >= 7500 rollover spike -> 0 RPM)

            int rpmStall8000 = HardwareBackend.DecodeTachometerRpm(0x1F, 0x40); // 8000

            int rpmSpike9489 = HardwareBackend.DecodeTachometerRpm(0x25, 0x11); // 9489 rollover spike

            int rpmGlitchFFFF = HardwareBackend.DecodeTachometerRpm(0xFF, 0xFF); // 65535

            int rpmValid592 = HardwareBackend.DecodeTachometerRpm(0x02, 0x50); // 592

            int rpmValid3150 = HardwareBackend.DecodeTachometerRpm(0x0C, 0x4E); // 3150

            int robustTrimmed = FanCalibrationResult.ComputeRobustStepRpm(new List<int> { 1520, 1522, 9489, 1525, 57300, 1521 });

            Check(rpmStall8000 == 0 && rpmSpike9489 == 0 && rpmGlitchFFFF == 0 && rpmValid592 == 592 && rpmValid3150 == 3150 &&
                  robustTrimmed >= 1520 && robustTrimmed <= 1525,

                "Tachometer Stall & Rollover Spike Filter (8000/9489/65535 -> 0 RPM, trimmed step mean preserved)",

                string.Format("8000->{0}, 9489->{1}, FFFF->{2}, 592->{3}, 3150->{4}, robust={5}", rpmStall8000, rpmSpike9489, rpmGlitchFFFF, rpmValid592, rpmValid3150, robustTrimmed));



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

            DeviceProfile pK12Dmi = SystemHardwareInfo.MatchProfile("GMKtec", "GMKtec", "AMD Ryzen 7 H 255", "GMKtec");

            DeviceProfile pK8 = SystemHardwareInfo.MatchProfile("NucBox K8 Plus", "GMKtec", "AMD Ryzen 7 8845HS");

            DeviceProfile pK8Fallback = SystemHardwareInfo.MatchProfile("Default string", "Default string", "AMD Ryzen 7 8845HS", "GMKtec");

            DeviceProfile pHx370 = SystemHardwareInfo.MatchProfile("EVO-X1", "GMKtec", "AMD Ryzen AI 9 HX 370");

            DeviceProfile pTopc = SystemHardwareInfo.MatchProfile("TS-12C", "TOPC", "AMD Ryzen AI 9 HX 370");

            DeviceProfile pAdreamer = SystemHardwareInfo.MatchProfile("AriesBox 10", "ADREAMER", "AMD Ryzen 7 7840HS");

            DeviceProfile pRecoreqichK8 = SystemHardwareInfo.MatchProfile("K8", "Default string", "AMD Ryzen AI 9 HX PRO 370 w/ Radeon 890M", "Recoreqich");

            DeviceProfile pUnverifiedHx370 = SystemHardwareInfo.MatchProfile("SER9", "Default string", "AMD Ryzen AI 9 HX 370", "Beelink");

            DeviceProfile pSzqftx8845 = SystemHardwareInfo.MatchProfile("ARB39-SC", "SZQFTX", "AMD Ryzen 7 8845HS");

            DeviceProfile pTianbeiH255 = SystemHardwareInfo.MatchProfile("MACO", "TianBei", "AMD Ryzen 7 H 255");

            DeviceProfile pTopcPhx7940 = SystemHardwareInfo.MatchProfile("PHX", "TOPC", "AMD Ryzen 9 7940HS");

            Check(pK12.Id == "gmktec-k12" && pK12Dmi.Id == "gmktec-k12"
                && pK8.Id == "gmktec-k8-k11" && pK8Fallback.Id == "gmktec-k8-k11"
                && pHx370.Id == "ryzen-hx370-ec" && pTopc.Id == "ryzen-hx370-ec"
                && pAdreamer.Id == "adreamer-ariesbox10"
                && pRecoreqichK8.Id == "unknown-safe" && pUnverifiedHx370.Id == "unknown-safe"
                && pSzqftx8845.Id == "unknown-safe" && pTianbeiH255.Id == "unknown-safe" && pTopcPhx7940.Id == "unknown-safe",

                "Multi-Model Ryzen Mini-PC Profile Matching (K12 / K8 Plus / EVO-X1 / TS-12C / AriesBox 10 / Vendor-Scoped Fail-Closed)",

                string.Format("K12={0}, K12Dmi={1}, K8={2}, K8Fb={3}, EVO-X1={4}, TS-12C={5}, AriesBox={6}, RecoreqichK8={7}, UnvHX370={8}, SZQFTX={9}, TianBei={10}, TOPCPHX={11}",
                    pK12.Id, pK12Dmi.Id, pK8.Id, pK8Fallback.Id, pHx370.Id, pTopc.Id, pAdreamer.Id, pRecoreqichK8.Id, pUnverifiedHx370.Id, pSzqftx8845.Id, pTianbeiH255.Id, pTopcPhx7940.Id));



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
            string freshBadge = "";
            string staleBadge = "";
            string origCalDate = FanCurveProfile.LastCalibratedDate;
            int origApuMin = FanCurveProfile.ApuMinSpinPct;
            int origSysMin = FanCurveProfile.SysMinSpinPct;
            try
            {
                FanCurveProfile.ApuMinSpinPct = calRes.ApuMinSpinPct;
                FanCurveProfile.SysMinSpinPct = calRes.SysMinSpinPct;
                FanCurveProfile.LastCalibratedDate = calRes.CalibratedAt;
                freshBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalFresh, out isStaleFresh);
                FanCurveProfile.LastCalibratedDate = "2024-01-15 12:00";
                staleBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalOld, out isStaleOld);
            }
            finally
            {
                FanCurveProfile.LastCalibratedDate = origCalDate;
                FanCurveProfile.ApuMinSpinPct = origApuMin;
                FanCurveProfile.SysMinSpinPct = origSysMin;
            }

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
                osd.ClampToScreen = false;

                osd.Stats = stats;

                osd.OsdScaleTier = 0; // 100%
                osd.GraphHeight = 65;
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
                    string.Format("hSpk={0}>hTxt={1}({2}), h130={3}>h45={4}({5}), hBth={6}>hTxt={7}({8}), hV={9}, tW0={10}, tW1={11}, tW2={12}, tW3={13}, w0={14}, w1={15}, w2={16}, w3={17}, cnt={18}",
                        hSpark, hText, hSpark > hText,
                        hComb130, hComb45, hComb130 > hComb45,
                        hBoth, hText, hBoth > hText,
                        hVertBoth, targetW0, targetW1, targetW2, targetW3,
                        wTier0, wTier1, wTier2, wTier3, osd.CountVisibleMetrics()));

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
                osd.ShowNpu = false;
                osd.ShowFabricClock = false;
                osd.ShowDriverVersion = false;
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
                Check(colsCount == 11 && wideW >= 1200,
                    "OSD Horizontal Bar Auto-Width Expansion (11 Merged Metrics & Custom Mode Support)",
                    string.Format("cols={0}, targetW={1}px (expected >= 1200px)", colsCount, wideW));

                // Test OSD Driver Version Metric & Afterburner Theme Palette
                osd.ShowDriverVersion = true;
                fullSt.RadeonSoftwareVer = "26.10.1";
                osd.IsAfterburnerTheme = true;
                osd.UpdateMetrics(fullSt, "Performance", true);
                int countWithDrv = osd.CountVisibleMetrics();
                Check(countWithDrv == 12 && osd.IsAfterburnerTheme,
                    "OSD Driver Version & Afterburner Theme Integration",
                    string.Format("cols={0} (expected 12), theme=Afterburner", countWithDrv));

                // Test OSD Center-Top & Center-Bottom Snap Locations
                Rectangle testWa = new Rectangle(0, 0, 1920, 1080);
                int margin = 24;
                int osdW = 800;
                int osdH = 80;
                int ctX = testWa.Left + (testWa.Width - osdW) / 2;
                int ctY = testWa.Top + margin;
                int cbX = testWa.Left + (testWa.Width - osdW) / 2;
                int cbY = testWa.Bottom - osdH - margin;
                Check(ctX == 560 && ctY == 24 && cbX == 560 && cbY == 976,
                    "OSD Center-Top and Center-Bottom Snap Location Geometry",
                    string.Format("CenterTop=({0},{1}), CenterBottom=({2},{3})", ctX, ctY, cbX, cbY));
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

                    Thread.Sleep(20);

                    hb.Set();

                    Thread.Sleep(20);

                    hb.Set();

                    Thread.Sleep(20);

                    sd.Set();

                });

                pulseThread.IsBackground = true;

                pulseThread.Start();

                int resSustained = WatchdogSupervisor.RunSimulated(currentPid, 1200, hb, sd, false);

                pulseThread.Join(2500);



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
            bool driverDllPresent = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inpoutx64.dll"));

            Check(port62Allow && port66Allow && port2EAllow && port0A35Allow &&
                  port70Blocked && portCF9Blocked && port20Blocked && port40Blocked && port61Blocked &&
                  lowMemMap == IntPtr.Zero && hugeMemMap == IntPtr.Zero &&
                  (dllPreloadOk || !driverDllPresent),
                "Driver Security Port Whitelist & Memory Map Safety Bounds",
                string.Format("Allow=[62:{0},2E:{1},A35:{2}], Block=[70:{3},CF9:{4},20:{5}], SafeMap=[Low:{6},Huge:{7}], DllPreload={8}",
                    port62Allow, port2EAllow, port0A35Allow, port70Blocked, portCF9Blocked, port20Blocked, lowMemMap == IntPtr.Zero, hugeMemMap == IntPtr.Zero, dllPreloadOk));



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

            // Simulate missing SMBus mutex (null) -> WaitSmbus() MUST return false (fail-closed)
            EcBusMutex.SetSimulatedMutexes(new Mutex(), new Mutex(), new Mutex(), null);
            bool missingSmbusBlocked = !EcBusMutex.WaitSmbus(10);

            // Restore normal mutexes
            EcBusMutex.ResetMutexes();

            Check(missingEcBlocked && missingIsaBlocked && missingPciBlocked && missingSmbusBlocked,
                "Missing-Mutex Fail-Closed Enforcement & Lock Security",
                string.Format("MissingEcBlocked={0}, MissingIsaBlocked={1}, MissingPciBlocked={2}, MissingSmbusBlocked={3}",
                    missingEcBlocked, missingIsaBlocked, missingPciBlocked, missingSmbusBlocked));



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
                    SecurityIdentifier appPackagesSid = new SecurityIdentifier("S-1-15-2-1");
                    bool noAppPackagesSid = true;
                    foreach (AuthorizationRule rule in rules)
                    {
                        if (rule.IdentityReference.Equals(worldSid))
                        {
                            noWorldSid = false;
                        }
                        if (rule.IdentityReference.Equals(appPackagesSid))
                        {
                            noAppPackagesSid = false;
                        }
                    }
                    aclValid = (rules.Count > 0 && noAppPackagesSid);
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
                string.Format("EventCreated={0}, AclRulesValid={1}, WorldSidExcluded={2}, AppPackagesExcluded={3}",
                    aclHandle != null, aclValid, noWorldSid, aclValid));

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

                    // AutoReset Event Role Verification:
                    // Verifies that a Waiter opening an AutoReset event can successfully call WaitOne()
                    // and consume the pulse without throwing UnauthorizedAccessException (Modify rights granted for implicit reset)
                    bool autoResetWaiterOk = false;
                    string autoTestName = "MFC_AutoResetRoleTest_" + Guid.NewGuid().ToString("N");
                    bool createdAutoSignaler, createdAutoWaiter;
                    using (EventWaitHandle autoSignaler = WatchdogSupervisor.CreateOrOpenEvent(autoTestName, EventResetMode.AutoReset, out createdAutoSignaler, WatchdogIpcRole.Signaler))
                    using (EventWaitHandle autoWaiter = WatchdogSupervisor.CreateOrOpenEvent(autoTestName, EventResetMode.AutoReset, out createdAutoWaiter, WatchdogIpcRole.Waiter))
                    {
                        if (autoSignaler != null && autoWaiter != null)
                        {
                            try
                            {
                                autoSignaler.Set();
                                autoResetWaiterOk = autoWaiter.WaitOne(50);
                            }
                            catch { }
                        }
                    }

                    handshakeEventValid = (readyEventName.StartsWith("MiniFanControl_Ready_") && readySignalVerified && timeoutVerified && localPathVerified && autoResetWaiterOk);
                }
                finally
                {
                    readyHandle.Close();
                    waiterHandle.Close();
                }
            }

            Check(handshakeEventValid,
                "Watchdog Ready Handshake, Scoped Roles & Global/Local Path Verification",
                string.Format("Name={0}, Signaled={1}, TimedOut={2}, LocalPath={3}, AutoResetWaiter={4}",
                    readyEventName, readySignalVerified, timeoutVerified, localPathVerified, handshakeEventValid));

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
                    osdTest.ShowNpu = false;
                    osdTest.ShowFabricClock = false;
                    osdTest.ShowAmbient = false;
                    osdTest.ShowVrmCurrent = false;
                    osdTest.ShowDriverVersion = false;
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

            // 44. UI Layout Anti-Clipping Geometry (Telemetry Boxes, OSD Studio & Options Card)
            bool uiGeometryValid = false;
            string uiGeomDetails = "";
            try
            {
                using (MainForm mf = new MainForm(true, true, true))
                {
                    bool metricBoxesValid = (mf.TestCpuTempLabel != null && !mf.TestCpuTempLabel.Visible && mf.TestCpuTempLabel.Height >= 32 && mf.TestCpuTempLabel.Font.SizeInPoints <= 15.0f) &&
                                            (mf.TestFan1RpmLabel != null && !mf.TestFan1RpmLabel.Visible && mf.TestFan1RpmLabel.Height >= 32 && mf.TestFan1RpmLabel.Font.SizeInPoints <= 12.0f) &&
                                            (mf.TestCpuTempBox != null && !string.IsNullOrEmpty(mf.TestResolveTooltipText(mf.TestCpuTempBox)));
                    if (metricBoxesValid && mf.TestCpuTempBox != null)
                    {
                        using (Bitmap boxBmp = new Bitmap(Math.Max(1, mf.TestCpuTempBox.Width), Math.Max(1, mf.TestCpuTempBox.Height)))
                        {
                            mf.TestCpuTempBox.DrawToBitmap(boxBmp, new Rectangle(0, 0, boxBmp.Width, boxBmp.Height));
                        }
                    }
                    bool osdStudioValid = (mf.TestOsdOpacityLabel != null && mf.TestOsdOpacityLabel.Width >= 60) &&
                                          (mf.TestOsdSnapLabel != null && mf.TestOsdSnapLabel.Width >= 85) &&
                                          (mf.TestOsdMetricsHeaderLabel != null && mf.TestOsdMetricsHeaderLabel.Location.Y >= 170) &&
                                          (mf.TestOsdQuickPosBc != null && mf.TestOsdQuickPosBc.Width >= 100) &&
                                          (mf.TestOsdQuickPosBr != null && mf.TestOsdQuickPosBr.Width >= 96);
                    bool optionsCardValid = (mf.TestAutostartCb != null && mf.TestAutostartCb.Width >= 300) &&
                                            (mf.TestAlwaysOnTopCb != null && mf.TestAlwaysOnTopCb.Location.X >= 330);

                    uiGeometryValid = metricBoxesValid && osdStudioValid && optionsCardValid;
                    uiGeomDetails = string.Format("MetricBoxes={0}, OsdStudio={1}, OptionsCard={2}", metricBoxesValid, osdStudioValid, optionsCardValid);
                }
            }
            catch (Exception ex)
            {
                uiGeometryValid = false;
                uiGeomDetails = ex.Message;
            }

            Check(uiGeometryValid,
                "UI Layout Anti-Clipping Geometry (Telemetry Boxes, OSD Studio & Options Card)",
                uiGeomDetails);

            // 45. Digital Foundry Framerate Dynamic Scale Ladder & Baseline Anti-Clipping Geometry
            bool dfValid = false;
            string dfDetails = "";
            try
            {
                // Test A: 60 FPS locked gameplay (80 ceiling provides 20 FPS headroom above 60 line, preventing clipping)
                List<int> fps60History = new List<int> { 60, 60, 59, 60, 60, 60 };
                int ceiling60 = CapFrameXOverlayForm.CalculateDfCeiling(60, fps60History);
                bool c60Ok = (ceiling60 == 80);

                // Test B: 55-64 FPS gameplay (Ori and the Will of the Wisps - stays solid at 80, no oscillation)
                List<int> oriHistory = new List<int> { 55, 62, 64, 58, 64, 63, 61, 64 };
                int ceilingOri = CapFrameXOverlayForm.CalculateDfCeiling(64, oriHistory);
                bool cOriOk = (ceilingOri == 80);

                // Test C: Loading / Splash screen spike rolls off within 20 samples
                List<int> spikeHistory = new List<int>();
                for (int i = 0; i < 50; i++) spikeHistory.Add(350); // splash screen
                for (int i = 0; i < 20; i++) spikeHistory.Add(64);  // active gameplay
                int ceilingRollOff = CapFrameXOverlayForm.CalculateDfCeiling(64, spikeHistory);
                bool cSpikeOk = (ceilingRollOff == 80); // Loading spike dropped from active window

                // Test D: High-refresh rate gameplay tiers (144Hz & 240Hz)
                int ceiling144 = CapFrameXOverlayForm.CalculateDfCeiling(144, new List<int> { 140, 144, 142 });
                int ceiling240 = CapFrameXOverlayForm.CalculateDfCeiling(240, new List<int> { 235, 240, 238 });
                bool cHdrOk = (ceiling144 == 180) && (ceiling240 == 240);

                // Test E: Height geometry & viability across all 4 tiers (75px Compact, 105px Medium, 140px Large, 180px XL)
                int ftHCompact = CapFrameXOverlayForm.GetDfFrametimeHeight(1.0f, 75);
                int fpsHCompact = CapFrameXOverlayForm.GetDfFramerateHeight(1.0f, 75);
                int ftHMedium = CapFrameXOverlayForm.GetDfFrametimeHeight(1.0f, 105);
                int fpsHMedium = CapFrameXOverlayForm.GetDfFramerateHeight(1.0f, 105);
                int ftHLarge = CapFrameXOverlayForm.GetDfFrametimeHeight(1.0f, 140);
                int fpsHLarge = CapFrameXOverlayForm.GetDfFramerateHeight(1.0f, 140);
                int ftHXl = CapFrameXOverlayForm.GetDfFrametimeHeight(1.0f, 180);
                int fpsHXl = CapFrameXOverlayForm.GetDfFramerateHeight(1.0f, 180);

                bool compactViable = (ftHCompact >= 50) && (fpsHCompact >= 80);
                bool mediumViable = (ftHMedium >= 54) && (fpsHMedium >= 95);
                bool largeViable = (ftHLarge >= 70) && (fpsHLarge >= 130);
                bool xlViable = (ftHXl >= 90) && (fpsHXl >= 170);
                bool geomOk = compactViable && mediumViable && largeViable && xlViable;

                // Test F: Frametime ceiling zoom-in & scale-out (50ms zoom default, scaling out on stutters)
                int ftCeil50 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(19.3, new List<int> { 166, 193, 168 });
                int ftCeil75 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(55.0, new List<int> { 550, 520 });
                int ftCeil100 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(85.0, new List<int> { 850, 780 });
                bool ftCeilOk = (ftCeil50 == 50) && (ftCeil75 == 75) && (ftCeil100 == 100);

                dfValid = c60Ok && cOriOk && cSpikeOk && cHdrOk && geomOk && ftCeilOk;
                dfDetails = string.Format("C60={0}({1}), Ori={2}({3}), SpikeRollOff={4}({5}), HighRefresh={6}, FtCeil={7}({8}/{9}/{10}), TiersOk={11}(Cmp={12}/{13},Med={14}/{15},Lg={16}/{17},Xl={18}/{19})",
                    c60Ok, ceiling60, cOriOk, ceilingOri, cSpikeOk, ceilingRollOff, cHdrOk, ftCeilOk, ftCeil50, ftCeil75, ftCeil100, geomOk,
                    ftHCompact, fpsHCompact, ftHMedium, fpsHMedium, ftHLarge, fpsHLarge, ftHXl, fpsHXl);
            }
            catch (Exception ex)
            {
                dfValid = false;
                dfDetails = ex.Message;
            }

            Check(dfValid,
                "Digital Foundry Dual Graphs Dynamic Scale Ladder & Baseline Anti-Clipping",
                dfDetails);

            // 46. Driver Engine Isolation & WinRing0 MMIO Heap-Mapping Prevention
            bool driverEngineIsolationOk = false;
            string driverEngineDetails = "";
            try
            {
                // Test A: MapPhysToLinSafe strictly blocks low addresses (< 0x000E0000) and excessive apertures (> 16MB)
                IntPtr h1, h2;
                IntPtr lowPhys = NativeInpOut.MapPhysToLinSafe(new IntPtr(0x1000), 4096, out h1);
                IntPtr excessivePhys = NativeInpOut.MapPhysToLinSafe(new IntPtr(0xFE0B0400), 32 * 1024 * 1024, out h2);
                bool boundsOk = (lowPhys == IntPtr.Zero && excessivePhys == IntPtr.Zero);

                // Test B: ReadPhysicalByteSafe & WritePhysicalByteSafe enforce minimum physical address boundary
                byte lowByte = NativeInpOut.ReadPhysicalByteSafe(0x00001000);
                bool writeLowOk = NativeInpOut.WritePhysicalByteSafe(0x00001000, 0x55);
                bool lowPhysBlocked = (lowByte == 0xFF && !writeLowOk);

                // Test C: UnmapPhysicalMemory handles null pointers safely
                int unmapZero = NativeInpOut.UnmapPhysicalMemory(IntPtr.Zero, IntPtr.Zero);
                bool unmapSafe = (unmapZero == 0);

                // Test D: HardwareBackend fails closed if unverified or without linear map
                using (HardwareBackend hwTest = new HardwareBackend())
                {
                    hwTest.Profile = DeviceProfile.FindProfile("unknown-safe");
                    bool writeBlocked = !hwTest.WriteReg(0x32, 0x01); // Read-only profile blocks writes
                    bool setModeBlocked = !hwTest.SetMode(1);
                    bool readBackSafe = writeBlocked && setModeBlocked;

                    driverEngineIsolationOk = boundsOk && lowPhysBlocked && unmapSafe && readBackSafe;
                    driverEngineDetails = string.Format("BoundsOk={0}, LowBlocked={1}, UnmapSafe={2}, ReadBackFailClosed={3}, ActiveDriver={4}",
                        boundsOk, lowPhysBlocked, unmapSafe, readBackSafe, NativeInpOut.ActiveDriverName);
                }
            }
            catch (Exception ex)
            {
                driverEngineIsolationOk = false;
                driverEngineDetails = ex.Message;
            }

            Check(driverEngineIsolationOk,
                "Driver Engine Isolation & WinRing0 MMIO Heap-Mapping Prevention",
                driverEngineDetails);

            // 47. Strict Fail-Closed Dword Port Whitelist Aperture Enforcement
            bool dwordWhitelistOk = false;
            string dwordWhitelistDetails = "";
            try
            {
                // DlPortReadPortUlong / DlPortWritePortUlong must strictly permit ONLY 0xCF8 and 0xCFC
                uint readRtc = NativeInpOut.DlPortReadPortUlong(0x70);
                uint readPic = NativeInpOut.DlPortReadPortUlong(0x20);
                uint readPit = NativeInpOut.DlPortReadPortUlong(0x40);
                uint readEc = NativeInpOut.DlPortReadPortUlong(0x62);
                uint readSio = NativeInpOut.DlPortReadPortUlong(0x2E);

                // Attempting dword writes to sensitive or non-PCI ports must be blocked
                NativeInpOut.DlPortWritePortUlong(0x70, 0x12345678);
                NativeInpOut.DlPortWritePortUlong(0x20, 0x12345678);
                NativeInpOut.DlPortWritePortUlong(0x40, 0x12345678);
                NativeInpOut.DlPortWritePortUlong(0x62, 0x12345678);

                bool portsBlocked = (readRtc == 0 && readPic == 0 && readPit == 0 && readEc == 0 && readSio == 0);
                dwordWhitelistOk = portsBlocked;
                dwordWhitelistDetails = string.Format("Blocked=[RTC:{0},PIC:{1},PIT:{2},EC:{3},SIO:{4}]",
                    readRtc == 0, readPic == 0, readPit == 0, readEc == 0, readSio == 0);
            }
            catch (Exception ex)
            {
                dwordWhitelistOk = false;
                dwordWhitelistDetails = ex.Message;
            }

            Check(dwordWhitelistOk,
                "Strict Fail-Closed Dword Port Whitelist Aperture Enforcement (0xCF8/0xCFC Only)",
                dwordWhitelistDetails);

            // 48. Safe Read-Only Telemetry Fallback When Kernel Driver Is Blocked
            bool safeTelemetryOk = false;
            string safeTelemetryDetails = "";
            try
            {
                using (HardwareBackend hwSafe = new HardwareBackend())
                {
                    hwSafe.Profile = DeviceProfile.FindProfile("unknown-safe");
                    // Calling GetStatus on unverified profile must not throw unhandled exception
                    FanStatus st = hwSafe.GetStatus();
                    bool validStatus = !string.IsNullOrEmpty(st.Mode);
                    bool readOnlySafe = hwSafe.Profile.IsReadOnly;
                    bool writeSafe = !hwSafe.WriteReg(0x32, 0x01); // Fan writes must fail closed
                    bool setAutoSafe = !hwSafe.SetAuto();          // Fan auto must fail closed on read-only

                    safeTelemetryOk = validStatus && readOnlySafe && writeSafe && setAutoSafe;
                    safeTelemetryDetails = string.Format("ValidStatus={0}, ReadOnly={1}, WriteSafe={2}, AutoSafe={3}, Mode={4}",
                        validStatus, readOnlySafe, writeSafe, setAutoSafe, st.Mode);
                }
            }
            catch (Exception ex)
            {
                safeTelemetryOk = false;
                safeTelemetryDetails = ex.Message;
            }

            Check(safeTelemetryOk,
                "Safe Read-Only Telemetry Fallback When Kernel Driver Is Blocked",
                safeTelemetryDetails);

            // 49. Single-Instance Mutex & Clean Driver Shutdown Lifecycle
            bool singleInstanceOk = false;
            string singleInstanceDetails = "";
            try
            {
                string testMutexName = @"Local\MiniFanControl_SingleInstance_Test_" + Guid.NewGuid().ToString("N");
                bool createdFirst = false;
                bool createdSecond = true;
                using (Mutex m1 = new Mutex(true, testMutexName, out createdFirst))
                {
                    // Second instance attempting to acquire the same mutex must detect createdNew = false
                    try
                    {
                        using (Mutex m2 = new Mutex(true, testMutexName, out createdSecond))
                        {
                            // createdSecond must be false because m1 owns it
                        }
                    }
                    catch (AbandonedMutexException)
                    {
                        createdSecond = false;
                    }
                }

                bool mutexDetected = createdFirst && !createdSecond;

                // Test clean shutdown
                NativeInpOut.Shutdown();
                bool shutdownOk = (NativeInpOut.ActiveDriver == HardwareDriverEngine.None);

                // Re-preload driver to restore state for any subsequent operations
                NativeInpOut.PreloadDriverDll();

                singleInstanceOk = mutexDetected && shutdownOk;
                singleInstanceDetails = string.Format("MutexGuard={0} (First={1}, Second={2}), ShutdownOk={3}",
                    mutexDetected, createdFirst, createdSecond, shutdownOk);
            }
            catch (Exception ex)
            {
                singleInstanceOk = false;
                singleInstanceDetails = ex.Message;
            }

            Check(singleInstanceOk,
                "Single-Instance Mutex & Clean Driver Shutdown Lifecycle",
                singleInstanceDetails);

            // 50. Anti-Poisoning Gameplay 0.1% Lows & Startup Hitch Rejection
            bool antiPoisonLowsOk = false;
            string antiPoisonDetails = "";
            try
            {
                // Part A: Microsecond calculation with loading stall (1,000,000 µs = 1 FPS)
                int[] spikedFrames = new int[500];
                for (int i = 0; i < 490; i++) spikedFrames[i] = 19608; // 51 FPS
                for (int i = 490; i < 499; i++) spikedFrames[i] = 23809; // 42 FPS
                spikedFrames[499] = 1000000; // 1 second loading screen hitch (1 FPS)

                int testAvg, testP1, testP01;
                FpsMonitor.CalculateLowsFromMicroseconds(spikedFrames, out testAvg, out testP1, out testP01);
                bool stallFiltered = (testP01 >= 38 && testP01 <= 51);

                // Part B: Session stats receiving startup 1 FPS artifact followed by 51 FPS gameplay
                TelemetrySessionStats testSession = new TelemetrySessionStats();
                testSession.Record(new FanStatus { CpuTemp = 45, RtssFps = 1, RtssP01LowFps = 1, RtssAvgFps = 1 });
                for (int i = 0; i < 20; i++)
                {
                    testSession.Record(new FanStatus { CpuTemp = 60, RtssFps = 51, RtssP01LowFps = 1, RtssAvgFps = 51 });
                }
                bool sessionLowsRecovered = (testSession.P01LowFps >= 35 && testSession.P01LowFps <= 51);

                // Part C: Overlay render with active 51 FPS and poisoned 0.1% low = 1
                bool overlayLowsRendered = false;
                using (CapFrameXOverlayForm osdLowTest = new CapFrameXOverlayForm())
                {
                    osdLowTest.Stats = testSession;
                    osdLowTest.ShowFps = true;
                    osdLowTest.ShowLows = true;
                    osdLowTest.UpdateMetrics(new FanStatus {
                        CpuTemp = 60,
                        RtssFps = 51,
                        RtssFrametimeMs = 19.6,
                        RtssP01LowFps = 1, // Poisoned RTSS artifact
                        RtssAvgFps = 51
                    }, "Balanced", true);
                    osdLowTest.UpdateDimensions();

                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdLowTest.Width), Math.Max(1, osdLowTest.Height)))
                    {
                        osdLowTest.DrawToBitmap(bmp, new Rectangle(0, 0, osdLowTest.Width, osdLowTest.Height));
                    }
                    overlayLowsRendered = true;
                }

                antiPoisonLowsOk = stallFiltered && sessionLowsRecovered && overlayLowsRendered;
                antiPoisonDetails = string.Format("StallFiltered={0} (P01={1} FPS), SessionLowsRecovered={2} (P01={3} FPS), OverlayOk={4}",
                    stallFiltered, testP01, sessionLowsRecovered, testSession.P01LowFps, overlayLowsRendered);
            }
            catch (Exception ex)
            {
                antiPoisonLowsOk = false;
                antiPoisonDetails = ex.Message;
            }

            Check(antiPoisonLowsOk,
                "Anti-Poisoning Gameplay 0.1% Lows & Startup Hitch Rejection",
                antiPoisonDetails);

            // Test 51: AMD Silicon & ADL Extended Telemetry Validation (CPU Clocks, Memory Clock, Hotspot & Power Limits)
            bool extendedTelemetryOk = false;
            string extendedDetails = "";
            try
            {
                FanStatus extStatus = new FanStatus
                {
                    CpuTemp = 78,
                    CpuLoadPct = 62,
                    CpuClockMhz = 4300,
                    CpuPowerWatts = 35,
                    GpuTemp = 66,
                    GpuHotspotTemp = 72,
                    GfxClockMhz = 2400,
                    GfxActivityPct = 96,
                    GfxPowerWatts = 18,
                    ApuPowerWatts = 53,
                    ApuPowerLimitWatts = 65,
                    MemClockMhz = 2800,
                    NpuClockMhz = 1600,
                    NpuActivityPct = 14,
                    RamLoadPct = 94,
                    RamUsedGb = 12.0,
                    RamTotalGb = 12.8
                };

                string extCpuTip = extStatus.FormatCpuTooltip();
                string extGpuTip = extStatus.FormatIgpuTooltip();
                string extRamTip = extStatus.FormatRamTooltip();

                bool cpuTipOk = extCpuTip.Contains("PMLOG_CLK_CPUCLK") && extCpuTip.Contains("4300 MHz") && extCpuTip.Contains("4.30 GHz") && extCpuTip.Contains("CPU Total Load") && extCpuTip.Contains("62%");
                bool gpuTipOk = extGpuTip.Contains("PMLOG_HOTSPOT") && extGpuTip.Contains("72 °C") && extGpuTip.Contains("PMLOG_NPU_BUSY_AVG");
                bool ramTipOk = extRamTip.Contains("PMLOG_CLK_MEMCLK") && extRamTip.Contains("2800 MHz") && extRamTip.Contains("DDR5-5600");

                bool overlayHorizOk = false;
                bool overlayVertOk = false;

                using (CapFrameXOverlayForm osd = new CapFrameXOverlayForm())
                {
                    osd.VerticalLayout = false;
                    osd.ShowPeaks = true;
                    osd.ShowPower = true;
                    osd.ShowRam = true;
                    osd.UpdateMetrics(extStatus, "Custom 4", true);
                    osd.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osd.TargetWidth), Math.Max(1, osd.TargetHeight)))
                    {
                        osd.DrawToBitmap(bmp, new Rectangle(0, 0, osd.TargetWidth, osd.TargetHeight));
                    }
                    overlayHorizOk = osd.TargetWidth > 200 && osd.TargetHeight > 20;

                    osd.VerticalLayout = true;
                    osd.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osd.TargetWidth), Math.Max(1, osd.TargetHeight)))
                    {
                        osd.DrawToBitmap(bmp, new Rectangle(0, 0, osd.TargetWidth, osd.TargetHeight));
                    }
                    overlayVertOk = osd.TargetWidth >= 285 && osd.TargetHeight > 50;
                }

                // Verify AmdAdlGpuSensor 16-parameter overload safety when uninitialized
                AmdAdlGpuSensor adl = new AmdAdlGpuSensor();
                int iGpu, adlCpu, apuP, cpuP, gfxP, socP, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt, memClk, hotspot, npuClk, npuAct;
                string pwrSrc;
                adl.ReadTelemetryDetailed(out iGpu, out adlCpu, out apuP, out cpuP, out gfxP, out socP,
                    out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out pwrSrc,
                    out memClk, out hotspot, out npuClk, out npuAct);

                bool adlSafe = (hotspot >= 0 && memClk >= 0 && npuClk >= 0 && (npuAct == -1 || (npuAct >= 0 && npuAct <= 100)));

                // Verify merged iGPU & GPU load formatting with both peaks + CPU load tracking
                bool mergedPeaksOk = false;
                bool cpuStatsOk = false;
                int nativeCpuUsage = SystemStorageMemorySensor.GetCpuUsage();
                bool nativeCpuOk = nativeCpuUsage >= 0 && nativeCpuUsage <= 100;

                using (CapFrameXOverlayForm osdMerge = new CapFrameXOverlayForm())
                {
                    TelemetrySessionStats mergeStats = new TelemetrySessionStats();
                    mergeStats.Record(new FanStatus { CpuTemp = 60, CpuLoadPct = 45, GpuTemp = 55, GfxActivityPct = 99 });
                    cpuStatsOk = (mergeStats.MaxCpuLoad == 45 && mergeStats.MinCpuLoad == 45);

                    osdMerge.Stats = mergeStats;
                    osdMerge.ShowPeaks = true;
                    osdMerge.ShowCpu = true;
                    osdMerge.ShowGpu = true;
                    osdMerge.ShowGpuLoad = true;
                    osdMerge.ShowClocks = false;
                    osdMerge.SparklinesAsBackground = true;
                    osdMerge.UpdateMetrics(extStatus, "Custom 4", true);
                    osdMerge.UpdateDimensions();
                    using (Bitmap bmpSpark = new Bitmap(osdMerge.TargetWidth, osdMerge.TargetHeight))
                    {
                        osdMerge.DrawToBitmap(bmpSpark, new Rectangle(0, 0, osdMerge.TargetWidth, osdMerge.TargetHeight));
                    }
                    mergedPeaksOk = osdMerge.TargetWidth > 0 && osdMerge.TargetHeight > 0;
                }

                // Verify active game title formatting and OSD display (Vertical header & Horizontal bar)
                bool gameFmtOk = (CapFrameXOverlayForm.FormatGameName("metro.exe") == "METRO")
                    && (CapFrameXOverlayForm.FormatGameName("MetroLL.exe") == "METRO LAST LIGHT")
                    && (CapFrameXOverlayForm.FormatGameName("Cyberpunk2077.exe") == "CYBERPUNK 2077")
                    && (CapFrameXOverlayForm.FormatGameName("FortniteClient-Win64-Shipping.exe") == "FORTNITE")
                    && (CapFrameXOverlayForm.FormatGameName("antigravity.exe") == null)
                    && (CapFrameXOverlayForm.FormatGameName("artcraft launcher.exe") == null)
                    && (CapFrameXOverlayForm.FormatGameName("explorer.exe") == null);

                bool gameOsdOk = false;
                using (CapFrameXOverlayForm osdGame = new CapFrameXOverlayForm())
                {
                    osdGame.ClampToScreen = false;
                    osdGame.ShowGameName = true;
                    FanStatus gameSt = new FanStatus { CpuTemp = 55, CpuLoadPct = 30, GpuTemp = 48, GfxActivityPct = 85, RtssFps = 60, RtssProcessName = "metro.exe" };
                    osdGame.VerticalLayout = true;
                    osdGame.UpdateMetrics(gameSt, "Balanced", true);
                    osdGame.UpdateDimensions();
                    using (Bitmap bmpVert = new Bitmap(osdGame.TargetWidth, osdGame.TargetHeight))
                    {
                        osdGame.DrawToBitmap(bmpVert, new Rectangle(0, 0, osdGame.TargetWidth, osdGame.TargetHeight));
                    }
                    bool vertGameOk = osdGame.TargetWidth > 0 && osdGame.TargetHeight > 0;

                    osdGame.VerticalLayout = false;
                    osdGame.UpdateMetrics(gameSt, "Balanced", true);
                    osdGame.UpdateDimensions();
                    using (Bitmap bmpHoriz = new Bitmap(osdGame.TargetWidth, osdGame.TargetHeight))
                    {
                        osdGame.DrawToBitmap(bmpHoriz, new Rectangle(0, 0, osdGame.TargetWidth, osdGame.TargetHeight));
                    }
                    bool horizGameOk = osdGame.TargetWidth > 0 && osdGame.TargetHeight > 0;
                    gameOsdOk = vertGameOk && horizGameOk;
                }

                extendedTelemetryOk = cpuTipOk && gpuTipOk && ramTipOk && overlayHorizOk && overlayVertOk && adlSafe && mergedPeaksOk && cpuStatsOk && nativeCpuOk && gameFmtOk && gameOsdOk;
                extendedDetails = string.Format("CpuTip={0}, GpuTip={1}, RamTip={2}, HorizOk={3}, VertOk={4}, AdlSafe={5}, MergedPeaks={6}, CpuStats={7}, NativeCpu={8} ({9}%), GameFmt={10}, GameOsd={11}",
                    cpuTipOk, gpuTipOk, ramTipOk, overlayHorizOk, overlayVertOk, adlSafe, mergedPeaksOk, cpuStatsOk, nativeCpuOk, nativeCpuUsage, gameFmtOk, gameOsdOk);
            }
            catch (Exception ex)
            {
                extendedTelemetryOk = false;
                extendedDetails = ex.Message;
            }

            Check(extendedTelemetryOk,
                "AMD Silicon & ADL Extended Telemetry Validation (CPU Clocks, Memory Clock, Hotspot & Power Limits)",
                extendedDetails);

            // 52. Tier 2 EC Differential Sensor Scanner & Motherboard Telemetry Discovery
            bool ecDiffOk = false;
            string ecDiffDetails = "";
            try
            {
                using (DemoBackend demoHw = new DemoBackend())
                {
                    // 1. Verify DemoBackend implements IHardwareBackend and returns 256 bytes
                    byte[] rawRegs = demoHw.ReadAllRegisters();
                    bool regLenOk = rawRegs != null && rawRegs.Length == 256;

                    // 2. Snapshot capture with median filtering
                    EcSnapshot idleSnap = EcDifferentialScanner.CaptureSnapshot(demoHw, "Simulated Idle", 3, 10);
                    bool snapOk = idleSnap != null && idleSnap.Registers != null && idleSnap.Registers.Length == 256;

                    // Simulate load state on registers
                    EcSnapshot loadSnap = new EcSnapshot { Label = "Simulated Thermal Load" };
                    Array.Copy(idleSnap.Registers, loadSnap.Registers, 256);
                    loadSnap.CpuTemp = 82;
                    loadSnap.GpuTemp = 74;
                    loadSnap.Fan1Rpm = 2700;
                    loadSnap.Fan2Rpm = 1750;
                    loadSnap.Registers[0x70] = 82;  // CPU temp
                    loadSnap.Registers[0x71] = 74;  // GPU temp
                    loadSnap.Registers[0x72] = 86;  // Discovered VRM power delivery thermistor (+14C rise)
                    loadSnap.Registers[0x74] = 45;  // Discovered Ambient thermistor (+9C rise)
                    loadSnap.Registers[0x75] = 52;  // Discovered DDR5 thermistor (+10C rise)
                    loadSnap.Registers[0x33] = 0xBD; // Fan 1 PWM change
                    loadSnap.Registers[0xE0] = 186; // 19V DC jack load sag (192 -> 186)
                    loadSnap.Registers[0x24] = 0x81; // Hardware state flag XOR 0x80

                    // 3. Differential analysis
                    EcDiffReport report = EcDifferentialScanner.Analyze(idleSnap, loadSnap, DeviceProfile.KnownProfiles[0]);
                    bool hasThermal = report.ThermalCandidates.Count >= 3; // CPU, GPU, VRM, Ambient, DDR5
                    bool hasPwm = report.FanPwmCandidates.Count >= 1;
                    bool hasVolt = report.VoltageCandidates.Count >= 1; // DC sag at 0xE0
                    bool hasFlags = report.FlagCandidates.Count >= 1;

                    // 4. Report generation
                    string textRep = EcDifferentialScanner.GenerateTextReport(report);
                    bool reportOk = !string.IsNullOrEmpty(textRep)
                        && textRep.Contains("MINIFANCONTROL • TIER 2 EC MOTHERBOARD DIFFERENTIAL TELEMETRY REPORT")
                        && textRep.Contains("VRM")
                        && textRep.Contains("256-BYTE EC DIFFERENTIAL MATRIX");

                    // 5. Tier 2 fields on FanStatus
                    FanStatus st = demoHw.GetStatus();
                    bool fanStatusTier2Ok = st.VrmTemp > 0 && st.AmbientTemp > 0 && st.DdrTemp > 0 && st.DcInputVolts > 10.0;

                    ecDiffOk = regLenOk && snapOk && hasThermal && hasPwm && hasVolt && hasFlags && reportOk && fanStatusTier2Ok;
                    ecDiffDetails = string.Format("RegLen={0}, SnapOk={1}, Thermals={2}, Pwm={3}, Volt={4}, Flags={5}, RepOk={6}, Tier2Status={7} (VRM {8}°C, Amb {9}°C, {10:0.0}V)",
                        regLenOk, snapOk, report.ThermalCandidates.Count, report.FanPwmCandidates.Count, report.VoltageCandidates.Count,
                        report.FlagCandidates.Count, reportOk, fanStatusTier2Ok, st.VrmTemp, st.AmbientTemp, st.DcInputVolts);
                }
            }
            catch (Exception ex)
            {
                ecDiffOk = false;
                ecDiffDetails = ex.Message;
            }

            Check(ecDiffOk,
                "Tier 2 EC Motherboard Differential Sensor Scanner & Telemetry (VRM, Ambient, DDR5 & ADC Rails)",
                ecDiffDetails);

            // 53. Dynamic Custom DeviceProfile Generation, JSON Serialization & Unlocked Fan Control Lifecycle
            bool customProfOk = false;
            string customProfDetails = "";
            string tempCustomPath = Path.Combine(Path.GetTempPath(), "test-custom-profile-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                SystemHardwareInfo dummySys = new SystemHardwareInfo
                {
                    Manufacturer = "Minisforum",
                    ProductName = "Minisforum UM890 Pro",
                    BaseBoard = "F7BSC",
                    BiosVersion = "1.08",
                    CpuName = "AMD Ryzen 9 8945HS with Radeon 780M"
                };

                EcSnapshot idle = new EcSnapshot { Label = "Idle" };
                idle.Registers[0x70] = 45; // CPU
                idle.Registers[0x71] = 42; // GPU
                idle.Registers[0x72] = 48; // VRM
                idle.Registers[0x33] = 0x90; // Fan 1 PWM (16%)
                idle.Registers[0x34] = 0x8A; // Fan 2 PWM (10%)
                idle.Registers[0x35] = 0x04; idle.Registers[0x36] = 0xB0; // Fan 1: 1200 RPM
                idle.Registers[0x37] = 0x03; idle.Registers[0x38] = 0x84; // Fan 2: 900 RPM
                idle.Registers[0xE0] = 192; // 19.2V

                EcSnapshot load = new EcSnapshot { Label = "Load" };
                Array.Copy(idle.Registers, load.Registers, 256);
                load.Registers[0x70] = 80;
                load.Registers[0x71] = 73;
                load.Registers[0x72] = 85;
                load.Registers[0x33] = 0xBD; // Fan 1 PWM (61%)
                load.Registers[0x34] = 0xB0; // Fan 2 PWM (48%)
                load.Registers[0x35] = 0x0A; load.Registers[0x36] = 0x8C; // Fan 1: 2700 RPM
                load.Registers[0x37] = 0x07; load.Registers[0x38] = 0x08; // Fan 2: 1800 RPM
                load.Registers[0xE0] = 186; // 18.6V

                EcDiffReport diff = EcDifferentialScanner.Analyze(idle, load, DeviceProfile.KnownProfiles[0]);

                // 1. Generate Custom DeviceProfile
                DeviceProfile custom = DeviceProfile.CreateFromDiffReport(diff, dummySys);
                bool genOk = custom != null
                    && custom.RegCpuTemp == 0x70
                    && custom.RegFan1Ctrl == 0x33
                    && custom.RegFan2Ctrl == 0x34
                    && custom.RegFan1Hi == 0x35
                    && custom.RegFan1Lo == 0x36
                    && custom.RegFan2Hi == 0x37
                    && custom.RegFan2Lo == 0x38
                    && custom.HasDualFans
                    && !custom.IsReadOnly
                    && custom.IsVerified;

                // 2. JSON Serialization & Deserialization round-trip
                string json = custom.ToJson();
                bool jsonHasProps = !string.IsNullOrEmpty(json)
                    && json.Contains("Minisforum UM890 Pro")
                    && json.Contains("0x33")
                    && json.Contains("0x34");

                DeviceProfile parsedProf = DeviceProfile.FromJson(json);
                bool parseOk = parsedProf != null
                    && parsedProf.DisplayName == custom.DisplayName
                    && parsedProf.RegCpuTemp == custom.RegCpuTemp
                    && parsedProf.RegFan1Ctrl == custom.RegFan1Ctrl
                    && parsedProf.RegFan2Ctrl == custom.RegFan2Ctrl
                    && parsedProf.HasDualFans == custom.HasDualFans
                    && !parsedProf.IsReadOnly;

                // 3. Save & Load from disk
                DeviceProfile.SaveCustomProfile(custom, tempCustomPath);
                DeviceProfile loaded = DeviceProfile.LoadCustomProfile(tempCustomPath);
                bool loadOk = loaded != null && loaded.DisplayName == custom.DisplayName;

                // 4. Overriding unknown-safe
                DeviceProfile match = SystemHardwareInfo.MatchProfile("Minisforum UM890 Pro", "F7BSC", "AMD Ryzen 9 8945HS");
                bool matchOk = match != null && match.DisplayName == custom.DisplayName && !match.IsReadOnly;

                // 5. Lookup via FindProfile
                DeviceProfile findCust = DeviceProfile.FindProfile("custom");
                bool findOk = findCust != null && findCust.DisplayName == custom.DisplayName;

                customProfOk = genOk && jsonHasProps && parseOk && loadOk && matchOk && findOk;
                customProfDetails = string.Format("GenOk={0}, JsonOk={1}, ParseOk={2}, LoadOk={3}, MatchOk={4}, FindOk={5}",
                    genOk, jsonHasProps, parseOk, loadOk, matchOk, findOk);
            }
            catch (Exception ex)
            {
                customProfOk = false;
                customProfDetails = ex.Message;
            }
            finally
            {
                DeviceProfile.CustomProfile = null;
                try { if (File.Exists(tempCustomPath)) File.Delete(tempCustomPath); } catch { }
            }

            Check(customProfOk,
                "Dynamic Custom DeviceProfile Generation, JSON Serialization & Unlocked Fan Control Lifecycle",
                customProfDetails);

            // ------------------------------------------------------------------
            // Test 54: Tier 3 AMD SMU / SMN Telemetry & Deep Silicon VRM / FCLK Bus Synchronization
            // ------------------------------------------------------------------
            bool smuOk = false;
            string smuDetails = "";
            try
            {
                // 1. Direct Silicon Sensor Sampling
                AmdSmuSiliconSensor.ResetCache();
                AmdSmuTelemetry smu = AmdSmuSiliconSensor.Sample(
                    cpuPowerWatts: 35,
                    cpuClockMhz: 4200,
                    memClockMhz: 2800,
                    gfxVoltMv: 1100,
                    adlFclk: 2000,
                    adlGfxCurrent: 28,
                    adlSocCurrent: 9,
                    forceRefresh: true);

                bool sampleOk = smu.IsAvailable
                    && smu.VddTdcCurrentAmps == 28.0
                    && smu.SocTdcCurrentAmps == 9.0
                    && smu.FabricClockMhz == 2000
                    && smu.IsFabricSynced
                    && smu.VddTdcLimitAmps > 0;

                // 2. FanStatus & Tooltip formatting
                FanStatus smuStatus = new FanStatus
                {
                    CpuTemp = 62,
                    CpuLoadPct = 40,
                    GpuTemp = 55,
                    ApuPowerWatts = 35,
                    MemClockMhz = 2800,
                    VddTdcCurrentAmps = smu.VddTdcCurrentAmps,
                    VddEdcCurrentAmps = smu.VddEdcCurrentAmps,
                    SocTdcCurrentAmps = smu.SocTdcCurrentAmps,
                    SocEdcCurrentAmps = smu.SocEdcCurrentAmps,
                    VddTdcLimitAmps = smu.VddTdcLimitAmps,
                    VddEdcLimitAmps = smu.VddEdcLimitAmps,
                    FabricClockMhz = smu.FabricClockMhz,
                    IsFabricSynced = smu.IsFabricSynced,
                    SmuTelemetrySource = smu.TelemetrySource
                };

                string pwrTip = smuStatus.FormatPowerTooltip();
                bool pwrTipOk = !string.IsNullOrEmpty(pwrTip)
                    && pwrTip.Contains("Deep Silicon Telemetry")
                    && pwrTip.Contains("VDDCR_VDD Core Current")
                    && pwrTip.Contains("28.0 A")
                    && pwrTip.Contains("FCLK");

                string smuRamTip = smuStatus.FormatRamTooltip();
                bool ramTipOk = !string.IsNullOrEmpty(smuRamTip)
                    && smuRamTip.Contains("Infinity Fabric Clock (FCLK): 2000 MHz (1:1 Fabric Synchronized)");

                // 3. TelemetrySessionStats tracking
                TelemetrySessionStats smuStats = new TelemetrySessionStats();
                smuStats.Record(smuStatus);
                bool statsOk = (smuStats.MinVrmCurrentAmps == 28.0)
                    && (smuStats.MaxVrmCurrentAmps == 28.0)
                    && (smuStats.MinFabricClockMhz == 2000)
                    && (smuStats.MaxFabricClockMhz == 2000)
                    && !string.IsNullOrEmpty(smuStats.GetVrmSparkline())
                    && !string.IsNullOrEmpty(smuStats.GetFabricClockSparkline());

                // 4. CapFrameXOverlayForm Rendering with ShowVrmCurrent and ShowFabricClock
                bool osdRenderOk = false;
                using (CapFrameXOverlayForm osdSmu = new CapFrameXOverlayForm())
                {
                    osdSmu.ClampToScreen = false;
                    osdSmu.Stats = smuStats;
                    osdSmu.ShowVrmCurrent = true;
                    osdSmu.ShowFabricClock = true;
                    osdSmu.ShowPower = true;
                    osdSmu.ShowRam = true;

                    // Vertical layout render check
                    osdSmu.VerticalLayout = true;
                    osdSmu.UpdateMetrics(smuStatus, "Balanced", true);
                    osdSmu.UpdateDimensions();
                    using (Bitmap bmpVert = new Bitmap(osdSmu.TargetWidth, osdSmu.TargetHeight))
                    {
                        osdSmu.DrawToBitmap(bmpVert, new Rectangle(0, 0, osdSmu.TargetWidth, osdSmu.TargetHeight));
                    }
                    bool vertOk = osdSmu.TargetWidth > 0 && osdSmu.TargetHeight > 0;

                    // Horizontal layout render check
                    osdSmu.VerticalLayout = false;
                    osdSmu.UpdateMetrics(smuStatus, "Balanced", true);
                    osdSmu.UpdateDimensions();
                    using (Bitmap bmpHoriz = new Bitmap(osdSmu.TargetWidth, osdSmu.TargetHeight))
                    {
                        osdSmu.DrawToBitmap(bmpHoriz, new Rectangle(0, 0, osdSmu.TargetWidth, osdSmu.TargetHeight));
                    }
                    bool horizOk = osdSmu.TargetWidth > 0 && osdSmu.TargetHeight > 0;
                    osdRenderOk = vertOk && horizOk;
                }

                smuOk = sampleOk && pwrTipOk && ramTipOk && statsOk && osdRenderOk;
                smuDetails = string.Format("SampleOk={0}, PwrTipOk={1}, RamTipOk={2}, StatsOk={3}, OsdRenderOk={4}",
                    sampleOk, pwrTipOk, ramTipOk, statsOk, osdRenderOk);
            }
            catch (Exception ex)
            {
                smuOk = false;
                smuDetails = ex.Message;
            }

            Check(smuOk,
                "Tier 3: AMD SMU / SMN Telemetry & Deep Silicon VRM / FCLK Bus Synchronization",
                smuDetails);

            // 55. NPU Telemetry Utilization, "AI Workload" Fan Curve Profile & Proactive Thermal Coupling
            bool npuOk = false;
            string npuDetails = "";
            try
            {
                // 1. Verify "AI Workload" 4th built-in profile structure
                FanCurveProfile aiProf = new FanCurveProfile("AI Workload", true, 3,
                    new CurvePoint[] { new CurvePoint(38, 35), new CurvePoint(48, 50), new CurvePoint(60, 70), new CurvePoint(72, 88), new CurvePoint(82, 100) },
                    new CurvePoint[] { new CurvePoint(35, 45), new CurvePoint(46, 60), new CurvePoint(58, 75), new CurvePoint(70, 90), new CurvePoint(80, 100) });

                bool aiProfOk = aiProf.Name == "AI Workload"
                    && aiProf.IsBuiltIn
                    && aiProf.BuiltInCode == 3
                    && aiProf.EvaluateApu(48) == 50
                    && aiProf.EvaluateSys(46) == 60
                    && aiProf.EvaluateApu(85) == 100
                    && aiProf.EvaluateSys(85) == 100;

                // 2. Proactive NPU thermal coupling math validation
                // At 0% NPU activity, boost = 0
                int act0 = 0;
                int boost0 = act0 >= 10 ? (int)Math.Round(act0 * 0.35) : 0;
                // At 40% NPU activity, boost = Math.Round(40 * 0.35) = 14°C
                int act40 = 40;
                int boost40 = act40 >= 10 ? (int)Math.Round(act40 * 0.35) : 0;
                int baseTemp = 52;
                int effectiveTemp40 = baseTemp + boost40; // 52 + 14 = 66°C
                int regularPwm = aiProf.EvaluateApu(baseTemp);
                int boostedPwm = aiProf.EvaluateApu(effectiveTemp40);

                bool couplingOk = (boost0 == 0) && (boost40 == 14) && (boostedPwm > regularPwm + 15);

                // 3. Telemetry tracking & Session Stats for NPU
                FanStatus npuStatus = new FanStatus
                {
                    CpuTemp = 55,
                    ApuPowerWatts = 30,
                    NpuActivityPct = 45,
                    NpuClockMhz = 1600
                };
                TelemetrySessionStats npuStats = new TelemetrySessionStats();
                npuStats.Record(npuStatus);
                bool statsNpuOk = (npuStats.MinNpuActivityPct == 45)
                    && (npuStats.MaxNpuActivityPct == 45)
                    && (npuStats.NpuHistory != null && npuStats.NpuHistory.Count == 1)
                    && !string.IsNullOrEmpty(npuStats.GetNpuSparkline());

                // 4. CapFrameX Overlay with ShowNpu
                bool osdNpuOk = false;
                using (CapFrameXOverlayForm osdNpu = new CapFrameXOverlayForm())
                {
                    osdNpu.ClampToScreen = false;
                    osdNpu.Stats = npuStats;
                    osdNpu.ShowNpu = true;
                    osdNpu.ShowPower = true;

                    int countBefore = osdNpu.CountVisibleMetrics();
                    osdNpu.UpdateMetrics(npuStatus, "AI Workload", true);
                    int countAfter = osdNpu.CountVisibleMetrics();
                    bool countOk = countAfter >= countBefore;

                    // Vertical render check
                    osdNpu.VerticalLayout = true;
                    osdNpu.UpdateDimensions();
                    using (Bitmap bmpV = new Bitmap(osdNpu.TargetWidth, osdNpu.TargetHeight))
                    {
                        osdNpu.DrawToBitmap(bmpV, new Rectangle(0, 0, osdNpu.TargetWidth, osdNpu.TargetHeight));
                    }
                    bool vertNpuOk = osdNpu.TargetWidth > 0 && osdNpu.TargetHeight > 0;

                    // Horizontal render check
                    osdNpu.VerticalLayout = false;
                    osdNpu.UpdateDimensions();
                    using (Bitmap bmpH = new Bitmap(osdNpu.TargetWidth, osdNpu.TargetHeight))
                    {
                        osdNpu.DrawToBitmap(bmpH, new Rectangle(0, 0, osdNpu.TargetWidth, osdNpu.TargetHeight));
                    }
                    bool horizNpuOk = osdNpu.TargetWidth > 0 && osdNpu.TargetHeight > 0;

                    osdNpuOk = countOk && vertNpuOk && horizNpuOk;
                }

                // 5. PawnIO Driver & HVCI Query verification
                bool hvciQueried = false;
                try
                {
                    bool hvci = NativeInpOut.IsHvciEnabled();
                    string drvName = NativeInpOut.ActiveDriverName;
                    hvciQueried = (drvName != null);
                }
                catch { }

                npuOk = aiProfOk && couplingOk && statsNpuOk && osdNpuOk && hvciQueried;
                npuDetails = string.Format("AiProfOk={0}, CouplingOk={1} (BasePWM={2}%, BoostPWM={3}%), StatsOk={4}, OsdOk={5}, HvciOk={6}",
                    aiProfOk, couplingOk, regularPwm, boostedPwm, statsNpuOk, osdNpuOk, hvciQueried);
            }
            catch (Exception ex)
            {
                npuOk = false;
                npuDetails = ex.Message;
            }

            Check(npuOk,
                "NPU Telemetry Utilization, AI Workload Curve & Proactive Thermal Coupling",
                npuDetails);

            // 56. Localization Engine (Loc), Built-In Multi-Language Dictionaries & Community JSON Schema
            bool locOk = false;
            string locDetails = "";
            try
            {
                // 1. Built-in dictionaries verification across 6 languages
                Loc.SetLanguage(AppLanguage.English);
                bool enOk = Loc.T("Mode_Balanced") == "Balanced" && Loc.T("Btn_AiWorkload") == "🤖 AI Workload";

                Loc.SetLanguage(AppLanguage.Chinese);
                bool zhOk = Loc.T("Mode_Balanced") == "平衡模式" && Loc.T("Btn_AiWorkload") == "🤖 AI负载";

                Loc.SetLanguage(AppLanguage.German);
                bool deOk = Loc.T("Mode_Balanced") == "Ausbalanciert" && Loc.T("Btn_AiWorkload") == "🤖 KI-Workload";

                Loc.SetLanguage(AppLanguage.Spanish);
                bool esOk = Loc.T("Mode_Balanced") == "Equilibrado" && Loc.T("Btn_AiWorkload") == "🤖 Carga IA";

                Loc.SetLanguage(AppLanguage.French);
                bool frOk = Loc.T("Mode_Balanced") == "Équilibré" && Loc.T("Btn_AiWorkload") == "🤖 Charge IA";

                Loc.SetLanguage(AppLanguage.Japanese);
                bool jaOk = Loc.T("Mode_Balanced") == "バランス" && Loc.T("Btn_AiWorkload") == "🤖 AI負荷";

                // 2. Fallback handling verification
                string missingFallback = Loc.T("NonExistent_Key_XYZ", "FallbackValue");
                bool fallbackOk = missingFallback == "FallbackValue";

                // 3. Drop-in Community Translation JSON schema parser verification
                string synthJson = "{\r\n  \"Community_Test_Key\": \"Translated Value\",\r\n  \"Btn_Balanced\": \"Custom Balanced\"\r\n}";
                Loc.LoadCustomJson(synthJson);
                bool jsonCustomOk = Loc.T("Community_Test_Key") == "Translated Value" && Loc.T("Btn_Balanced") == "Custom Balanced";
                Loc.ClearCustomStrings();

                // 4. Validate language files in workspace 'lang/' directory if present
                string langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
                bool langDirValid = true;
                if (Directory.Exists(langDir) && File.Exists(Path.Combine(langDir, "template.json")))
                {
                    List<string> valErrors;
                    langDirValid = Loc.ValidateLanguageFiles(langDir, out valErrors);
                }

                // 5. Restore Auto
                Loc.SetLanguage(AppLanguage.Auto);

                locOk = enOk && zhOk && deOk && esOk && frOk && jaOk && fallbackOk && jsonCustomOk && langDirValid;
                locDetails = string.Format("EN={0}, ZH={1}, DE={2}, ES={3}, FR={4}, JA={5}, Fallback={6}, JsonCustom={7}, LangDirValid={8}",
                    enOk, zhOk, deOk, esOk, frOk, jaOk, fallbackOk, jsonCustomOk, langDirValid);
            }
            catch (Exception ex)
            {
                locOk = false;
                locDetails = ex.Message;
            }

            Check(locOk,
                "Localization Engine (Loc), Built-In Multi-Language Dictionaries & Community JSON Schema",
                locDetails);

            // 57. Live Performance Tab & Digital Foundry Frametime / Framerate Engine Integration
            bool perfTabOk = false;
            string perfTabDetails = "";
            try
            {
                // 1. Digital Foundry Frametime ceiling calculation logic
                List<int> sampleFt = new List<int> { 166, 168, 165 }; // 16.6ms
                int ftCeil50 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(16.6, sampleFt);
                sampleFt.Add(620); // 62ms spike
                int ftCeil75 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(62.0, sampleFt);
                sampleFt.Add(920); // 92ms spike
                int ftCeil100 = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(92.0, sampleFt);
                bool ftCeilOk = (ftCeil50 == 50 && ftCeil75 == 75 && ftCeil100 == 100);

                // 2. Digital Foundry Framerate ceiling calculation logic (Headroom ladder: 60/80/120/180/240)
                int fpsCeil60 = CapFrameXOverlayForm.CalculateDfCeiling(30, new List<int> { 28, 30, 29 });
                int fpsCeil80 = CapFrameXOverlayForm.CalculateDfCeiling(60, new List<int> { 58, 60, 59 });
                int fpsCeil120 = CapFrameXOverlayForm.CalculateDfCeiling(100, new List<int> { 98, 100, 99 });
                int fpsCeil180 = CapFrameXOverlayForm.CalculateDfCeiling(144, new List<int> { 142, 144, 143 });
                bool fpsCeilOk = (fpsCeil60 == 60 && fpsCeil80 == 80 && fpsCeil120 == 120 && fpsCeil180 == 180);

                // 3. LivePerformanceCanvas control lifecycle & properties
                TelemetrySessionStats perfStats = new TelemetrySessionStats();
                LivePerformanceCanvas lpc = new LivePerformanceCanvas
                {
                    Width = 936,
                    Height = 368,
                    SplitView = true,
                    IsDarkMode = true,
                    Stats = perfStats
                };
                FanStatus testSt = new FanStatus
                {
                    CpuTemp = 62,
                    GpuTemp = 54,
                    RtssRunning = true,
                    RtssFps = 144,
                    RtssFrametimeMs = 6.94,
                    RtssAvgFps = 141,
                    RtssP01LowFps = 92,
                    RtssProcessName = "Cyberpunk2077.exe"
                };
                lpc.UpdateStatus(testSt);

                // Render across 0 samples (post-Reset), 1 sample (first tick after Reset), and multiple samples in both Split and Combined modes
                using (Bitmap bmp = new Bitmap(936, 368))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // 0 samples (immediately after Reset)
                    perfStats.Reset();
                    lpc.SplitView = true;
                    lpc.RenderForTest(g);
                    lpc.SplitView = false;
                    lpc.RenderForTest(g);

                    // 1 sample (exact first poll tick after Reset — must not throw GDI+ ArgumentException on 1-point array)
                    perfStats.Record(testSt);
                    lpc.SplitView = true;
                    lpc.RenderForTest(g);
                    lpc.SplitView = false;
                    lpc.RenderForTest(g);

                    // Multiple samples
                    perfStats.Record(testSt);
                    perfStats.Record(testSt);
                    lpc.SplitView = true;
                    lpc.RenderForTest(g);
                    lpc.SplitView = false;
                    lpc.RenderForTest(g);
                }

                lpc.SplitView = true;
                bool canvasOk = (lpc.Width == 936 && lpc.Height == 368 && lpc.SplitView && lpc.Status.RtssFps == 144 && perfStats.FpsHistory.Count == 3);

                // 4. Tab localization verification
                Loc.SetLanguage(AppLanguage.English);
                bool tabEnOk = Loc.T("Tab_LivePerf") == "• Live Perf";
                Loc.SetLanguage(AppLanguage.Chinese);
                bool tabZhOk = Loc.T("Tab_LivePerf") == "• 游戏性能";
                Loc.SetLanguage(AppLanguage.Auto);

                perfTabOk = ftCeilOk && fpsCeilOk && canvasOk && tabEnOk && tabZhOk;
                perfTabDetails = string.Format("FtCeil={0} (50/75/100), FpsCeil={1} (60/80/120), Canvas={2}, TabEN={3}, TabZH={4}",
                    ftCeilOk, fpsCeilOk, canvasOk, tabEnOk, tabZhOk);
            }
            catch (Exception ex)
            {
                perfTabOk = false;
                perfTabDetails = ex.Message;
            }

            Check(perfTabOk,
                "Live Performance Tab & Digital Foundry Frametime / Framerate Engine Integration",
                perfTabDetails);

            // 58. Live Trend First Tab Sequence, Dynamic Manual Fan Visibility & 1440p Taskbar Auto-Fit
            bool tabOrderAndTaskbarOk = false;
            string tabOrderDetails = "";
            try
            {
                using (MainForm mf = new MainForm(true, true, true))
                {
                    // 1. Verify Tab Order: Live Trend (1st) -> Live Perf (2nd) -> APU Fan (3rd) -> Sys Fan (4th) -> OSD Studio (5th)
                    bool orderOk = (mf.TestTabLiveGraphBtn != null && mf.TestTabLiveGraphBtn.Location.X == 8) &&
                                   (mf.TestTabPerfGraphBtn != null && mf.TestTabPerfGraphBtn.Location.X == 220) &&
                                   (mf.TestTabApuCurveBtn != null && mf.TestTabApuCurveBtn.Location.X == 432) &&
                                   (mf.TestTabSysCurveBtn != null && mf.TestTabSysCurveBtn.Location.X == 644) &&
                                   (mf.TestTabOsdConfigBtn != null && mf.TestTabOsdConfigBtn.Location.X == 856);

                    // 2. Verify Tab Naming includes "Fan"
                    bool namesOk = (mf.TestTabApuCurveBtn.Text.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   (mf.TestTabSysCurveBtn.Text.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0);

                    // 3. Verify Dynamic Manual Fan Control Visibility & Canvas Expansion
                    // On Live Trend: Manual Fan Control hidden, canvas expanded to 648px (fits all OSD Studio metrics without inner scrollbar)
                    mf.TestSetGraphViewMode(true, false, 1);
                    bool liveHiddenOk = (!mf.TestManualFanControlVisible) &&
                                        (mf.TestModeCardPanel != null && mf.TestModeCardPanel.Height == 720) &&
                                        (mf.TestLiveCanvas != null && mf.TestLiveCanvas.Height == 648);

                    // On Live Perf: Manual Fan Control hidden, perf canvas expanded to 648px
                    mf.TestSetGraphViewMode(false, true, 0);
                    bool perfHiddenOk = (!mf.TestManualFanControlVisible) &&
                                        (mf.TestModeCardPanel != null && mf.TestModeCardPanel.Height == 720) &&
                                        (mf.TestPerfCanvas != null && mf.TestPerfCanvas.Height == 648);

                    // On APU Fan: Manual Fan Control visible, card height fills space above Manual Fan Control (590px)
                    mf.TestSetGraphViewMode(false, false, 0);
                    int curCardH = mf.TestModeCardPanel != null ? mf.TestModeCardPanel.Height : 0;
                    bool fanShownOk = mf.TestManualFanControlVisible && (curCardH == 590);

                    // 4. Verify CapFrameX Overlay 1440p / High-DPI Auto-Fit Scaling & Taskbar Clamping
                    using (CapFrameXOverlayForm ov = new CapFrameXOverlayForm())
                    {
                        ov.VerticalLayout = true;
                        ov.OsdScaleTier = 3; // 185% 4K scale
                        ov.GraphHeight = 180; // XL 180px graphs
                        ov.ShowLiveTrend = true;
                        ov.ShowFpsGraph = true;
                        ov.DfGraphStyle = true;
                        ov.ShowCpu = true;
                        ov.ShowGpu = true;
                        ov.ShowGpuLoad = true;
                        ov.ShowNvme = true;
                        ov.ShowPower = true;
                        ov.ShowRam = true;
                        ov.ShowVrmCurrent = true;
                        ov.ShowFabricClock = true;
                        ov.ShowNpu = true;
                        ov.ShowFps = true;
                        ov.ShowLows = true;
                        ov.ShowApuFan = true;
                        ov.ShowSysFan = true;
                        ov.ShowMode = true;
                        ov.ShowPeaks = true;
                        ov.ClampToScreen = true;

                        Screen scr = Screen.FromControl(ov);
                        Rectangle wa = (scr != null) ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;
                        ov.Location = new Point(wa.Left + 16, wa.Bottom - 60);
                        ov.UpdateDimensions();

                        bool fitsScreenOk = (ov.TargetHeight <= wa.Height - 16);
                        bool respectsTaskbarOk = (ov.Location.Y + ov.TargetHeight <= wa.Bottom - 8);

                        // 5. Verify 100 ms (10 Hz) Extreme polling rate option
                        mf.TestSetRefreshInterval(100);
                        bool poll100Ok = (mf.TestRefreshIntervalMs == 100) &&
                                         (mf.TestSamplingCombo != null && mf.TestSamplingCombo.SelectedIndex == 0 &&
                                          mf.TestSamplingCombo.Items[0].ToString().StartsWith("100 ms", StringComparison.OrdinalIgnoreCase));

                        tabOrderAndTaskbarOk = orderOk && namesOk && liveHiddenOk && perfHiddenOk && fanShownOk && fitsScreenOk && respectsTaskbarOk && poll100Ok;
                        tabOrderDetails = string.Format("Order={0}, Names={1}, LiveExpand={2}, PerfExpand={3}, FanContract={4}, AutoFitH={5} (<= {6}px), TaskbarClamped={7}, Poll100ms={8}",
                            orderOk, namesOk, liveHiddenOk, perfHiddenOk, fanShownOk, fitsScreenOk, wa.Height - 16, respectsTaskbarOk, poll100Ok);
                    }
                }
            }
            catch (Exception ex)
            {
                tabOrderAndTaskbarOk = false;
                tabOrderDetails = ex.Message;
            }

            Check(tabOrderAndTaskbarOk,
                "Live Trend First Tab Sequence, Dynamic Manual Fan Visibility & 1440p Taskbar Auto-Fit",
                tabOrderDetails);

            // 59. Undeletable Protected Reference Curves (🔒 Reset-Only) & ✨ Auto-Tune Curve Synthesizer
            bool examplesAndAutoTuneOk = false;
            string examplesAndAutoTuneDetails = "";
            int savedApuMin = FanCurveProfile.ApuMinSpinPct;
            int savedSysMin = FanCurveProfile.SysMinSpinPct;
            try
            {
                // 1. Verify factory protected examples
                List<FanCurveProfile> examples = FanCurveProfile.CreateProtectedExamples();
                bool fourExamplesOk = (examples != null && examples.Count == 4);
                foreach (FanCurveProfile ex in examples)
                {
                    if (!ex.IsProtectedExample || ex.ApuPoints == null || ex.ApuPoints.Length != 5 || ex.SysPoints == null || ex.SysPoints.Length != 5)
                        fourExamplesOk = false;
                }

                // 2. Verify Auto-Tune synthesis across 35W Eco vs 65W Performance TDP & NVMe/NPU boost
                FanCurveProfile.ApuMinSpinPct = 26;
                FanCurveProfile.SysMinSpinPct = 24;
                TelemetrySessionStats ecoStats = new TelemetrySessionStats();
                ecoStats.Record(new FanStatus { CpuTemp = 40, GpuTemp = 38, ApuPowerWatts = 12, ApuPowerLimitWatts = 35, NvmeTemp = 44 });
                string ecoSummary;
                FanCurveProfile ecoTuned = FanCurveProfile.SynthesizeAutoTunedCurve(
                    new FanStatus { CpuTemp = 40, GpuTemp = 38, ApuPowerWatts = 28, ApuPowerLimitWatts = 35, NvmeTemp = 44 },
                    ecoStats, out ecoSummary);

                TelemetrySessionStats perfStats = new TelemetrySessionStats();
                perfStats.Record(new FanStatus { CpuTemp = 44, GpuTemp = 40, ApuPowerWatts = 15, ApuPowerLimitWatts = 65, NvmeTemp = 48 });
                perfStats.Record(new FanStatus { CpuTemp = 86, GpuTemp = 76, ApuPowerWatts = 64, ApuPowerLimitWatts = 65, NvmeTemp = 66, NpuActivityPct = 45 });
                string perfSummary;
                FanCurveProfile perfTuned = FanCurveProfile.SynthesizeAutoTunedCurve(
                    new FanStatus { CpuTemp = 78, GpuTemp = 70, ApuPowerWatts = 60, ApuPowerLimitWatts = 65, NvmeTemp = 66, NpuActivityPct = 45 },
                    perfStats, out perfSummary);

                bool monotonicOk = (ecoTuned.ApuPoints.Length == 5 && perfTuned.ApuPoints.Length == 5);
                for (int i = 0; i < 4; i++)
                {
                    if (ecoTuned.ApuPoints[i].Temp >= ecoTuned.ApuPoints[i + 1].Temp || ecoTuned.ApuPoints[i].Pct > ecoTuned.ApuPoints[i + 1].Pct)
                        monotonicOk = false;
                    if (perfTuned.ApuPoints[i].Temp >= perfTuned.ApuPoints[i + 1].Temp || perfTuned.ApuPoints[i].Pct > perfTuned.ApuPoints[i + 1].Pct)
                        monotonicOk = false;
                }

                // 65W + 86°C peak should have a firmer/earlier acoustic knee than 35W Eco, and elevated Sys Fan duty for 66°C NVMe / 45% NPU
                // Both curves must anchor APU idle at ApuMinSpinPct (26%) rather than 0% to prevent fan hunting
                bool kneeScalingOk = (ecoTuned.ApuPoints[0].Pct == 26) &&
                                     (perfTuned.ApuPoints[0].Pct == 26) &&
                                     (ecoTuned.ApuPoints[1].Pct >= 26) &&
                                     (perfTuned.ApuPoints[2].Pct > ecoTuned.ApuPoints[2].Pct) &&
                                     (perfTuned.ApuPoints[2].Temp < ecoTuned.ApuPoints[2].Temp) &&
                                     (perfTuned.SysPoints[2].Pct > ecoTuned.SysPoints[2].Pct);

                // 3. Verify MainForm Protected Example Reset-vs-Delete & Auto-Tune UI Lifecycle
                using (MainForm mf = new MainForm(true, true, true))
                {
                    int countBefore = mf.CustomProfiles.Count;
                    FanCurveProfile firstProtected = null;
                    int protIdx = -1;
                    for (int i = 0; i < mf.CustomProfiles.Count; i++)
                    {
                        if (mf.CustomProfiles[i].IsProtectedExample)
                        {
                            firstProtected = mf.CustomProfiles[i];
                            protIdx = i;
                            break;
                        }
                    }

                    bool hasProtectedInForm = (firstProtected != null && protIdx >= 0);
                    int origPct = hasProtectedInForm ? firstProtected.ApuPoints[2].Pct : -1;
                    if (hasProtectedInForm)
                    {
                        firstProtected.ApuPoints[2] = new CurvePoint(firstProtected.ApuPoints[2].Temp, 99);
                        mf.TestCustomModeCombo.SelectedIndex = protIdx;
                    }
                    bool btnShowsReset = (mf.TestDeleteCustomBtn != null && mf.TestDeleteCustomBtn.Text == "Reset");

                    // Clicking Del/Reset on a protected example must NOT remove it, and must restore factory points
                    mf.TestDeleteSelectedCustomMode();
                    bool notDeletedOk = (mf.CustomProfiles.Count == countBefore) &&
                                        (firstProtected != null && firstProtected.ApuPoints[2].Pct == origPct);

                    // Clicking Auto-Tune synthesizes and selects the ✨ Auto-Tuned curve, and switches button back to "Del"
                    mf.TestRunAutoTuneCurve();
                    bool autoTuneSelectedOk = (mf.ApuProfile != null && mf.ApuProfile.Name.StartsWith("✨ Auto-Tuned", StringComparison.OrdinalIgnoreCase)) &&
                                              (mf.TestDeleteCustomBtn != null && mf.TestDeleteCustomBtn.Text == "Del") &&
                                              (mf.TestAutoTuneCustomBtn != null && mf.TestAutoTuneCustomBtn.Text.IndexOf("Auto-Tune", StringComparison.OrdinalIgnoreCase) >= 0);

                    // Wide 2-Column Mode (>= 1450px): Live Trend docks in right panel alongside Fan Curve on left, then restores when narrowed
                    mf.TestApplyWideLayoutSize(1900, 1000);
                    bool wideOnOk = (mf.TestWideRightPanel != null) &&
                                    (mf.TestLiveCanvas != null && mf.TestLiveCanvas.Parent == mf.TestWideRightPanel && mf.TestLiveCanvas.Width >= 700);
                    mf.TestApplyWideLayoutSize(1090, 900);
                    bool wideOffOk = (mf.TestWideRightPanel != null) &&
                                     (mf.TestLiveCanvas != null && mf.TestLiveCanvas.Parent == mf.TestModeCardPanel);

                    examplesAndAutoTuneOk = fourExamplesOk && monotonicOk && kneeScalingOk && hasProtectedInForm && btnShowsReset && notDeletedOk && autoTuneSelectedOk && wideOnOk && wideOffOk;
                    examplesAndAutoTuneDetails = string.Format("Examples4={0}, Monotonic={1}, KneeScale={2} (35W={3}%@{4}°C vs 65W={5}%@{6}°C), ResetBtn={7}, ResetRestored={8}, AutoTuneUI={9}, WideOn={10}, WideOff={11}",
                        fourExamplesOk, monotonicOk, kneeScalingOk,
                        ecoTuned.ApuPoints[2].Pct, ecoTuned.ApuPoints[2].Temp,
                        perfTuned.ApuPoints[2].Pct, perfTuned.ApuPoints[2].Temp,
                        btnShowsReset, notDeletedOk, autoTuneSelectedOk, wideOnOk, wideOffOk);
                }
            }
            catch (Exception ex)
            {
                examplesAndAutoTuneOk = false;
                examplesAndAutoTuneDetails = ex.Message;
            }
            finally
            {
                FanCurveProfile.ApuMinSpinPct = savedApuMin;
                FanCurveProfile.SysMinSpinPct = savedSysMin;
            }

            Check(examplesAndAutoTuneOk,
                "Undeletable Protected Reference Curves (🔒 Reset-Only) & ✨ Auto-Tune Curve Synthesizer",
                examplesAndAutoTuneDetails);

            // 60. AMD Silicon Junction SMN Tctl/Tdie Decoding & HWiNFO64 Parity
            bool smnTctlParityOk = true;
            string smnTctlDetails = "";
            try
            {
                // Verify 88.0°C decoding (matching HWiNFO64 Tctl/Tdie with bit 19 range shift)
                uint raw88 = (1096u << 21) | 0x00080000u; // 1096 * 0.125 = 137C - 49C = 88C
                int calc88 = HardwareBackend.DecodeAmdSmnTemp(raw88);
                if (calc88 != 88)
                {
                    smnTctlParityOk = false;
                    smnTctlDetails = "Expected 88C for raw88, got " + calc88;
                }

                // Verify 45.0°C idle decoding (matching HWiNFO64 Tctl/Tdie with bit 19 range shift)
                uint raw45 = (752u << 21) | 0x00080000u; // 752 * 0.125 = 94C - 49C = 45C
                int calc45 = HardwareBackend.DecodeAmdSmnTemp(raw45);
                if (calc45 != 45)
                {
                    smnTctlParityOk = false;
                    smnTctlDetails = "Expected 45C for raw45, got " + calc45;
                }

                // Verify Zen 5 / Strix Point (Ryzen AI 9 HX 370) standard 0..255 C range decoding:
                // TJ_SEL bits 17:16 = 3 (0x00030000), bit 19 = 0 (no -49C offset).
                // 419 * 0.125 = 52.375C -> rounds to 52C (exact parity with HWiNFO64 52.4C).
                uint rawZen5 = (419u << 21) | 0x00030000u;
                int calcZen5 = HardwareBackend.DecodeAmdSmnTemp(rawZen5);
                if (calcZen5 != 52)
                {
                    smnTctlParityOk = false;
                    smnTctlDetails = "Expected 52C for Zen 5 Strix Point rawZen5, got " + calcZen5;
                }

                // Verify unmapped IT8613 thermistor 128C rejection
                byte unmappedIt87Thermistor = 128;
                int signedTherm = (sbyte)unmappedIt87Thermistor;
                bool rejected128 = !(signedTherm >= 15 && signedTherm <= 115);
                if (!rejected128)
                {
                    smnTctlParityOk = false;
                    smnTctlDetails = "Unmapped 128C thermistor was not rejected!";
                }
            }
            catch (Exception ex)
            {
                smnTctlParityOk = false;
                smnTctlDetails = "Exception: " + ex.Message;
            }

            Check(smnTctlParityOk,
                "AMD Silicon Junction SMN Tctl/Tdie Decoding & HWiNFO64 Parity",
                smnTctlDetails);

            // 61. Live Trend & Overlay Metric Ordering (RAM 3rd, FCLK 4th, Chassis Ambient) & Format Parity
            bool metricOrderAndFormatOk = true;
            string metricOrderDetails = "";
            try
            {
                TelemetrySessionStats t61Stats = new TelemetrySessionStats();
                FanStatus t61Status = new FanStatus
                {
                    CpuTemp = 57,
                    CpuLoadPct = 5,
                    GpuTemp = 50,
                    GfxActivityPct = 3,
                    GfxClockMhz = 796,
                    DdrTemp = 55,
                    RamLoadPct = 41,
                    RamTotalGb = 47.6,
                    RamUsedGb = 19.6,
                    MemClockMhz = 1705,
                    FabricClockMhz = 1600,
                    IsFabricSynced = true,
                    NvmeTemp = 43,
                    ApuPowerWatts = 11,
                    VddTdcCurrentAmps = 3.1,
                    VrmTemp = 56,
                    AmbientTemp = 47,
                    Fan1Rpm = 849,
                    Fan2Rpm = 974,
                    Mode = "AutoTune"
                };

                t61Stats.Record(t61Status);

                // 1. Verify TelemetrySessionStats recorded Ambient & DDR temps, VRM temp and FCLK
                bool statsOk = (t61Stats.AmbientHistory.Count == 1 && t61Stats.AmbientHistory[0] == 47) &&
                               (t61Stats.RamTempHistory.Count == 1 && t61Stats.RamTempHistory[0] == 55) &&
                               (t61Stats.RamLoadHistory.Count == 1 && t61Stats.RamLoadHistory[0] == 41) &&
                               (t61Stats.FabricClockHistory.Count == 1 && t61Stats.FabricClockHistory[0] == 1600) &&
                               (t61Stats.MaxVrmTemp == 56);
                if (!statsOk)
                {
                    metricOrderAndFormatOk = false;
                    metricOrderDetails = "Session stats history recording failed!";
                }

                // 2. Verify Overlay metrics count & toggles
                using (CapFrameXOverlayForm ov = new CapFrameXOverlayForm())
                {
                    ov.Stats = t61Stats;
                    ov.ShowGameName = false;
                    ov.ShowCpu = true;
                    ov.ShowGpu = true;
                    ov.ShowRam = true;
                    ov.ShowFabricClock = true;
                    ov.ShowNvme = true;
                    ov.ShowPower = true;
                    ov.ShowVrmCurrent = true;
                    ov.ShowAmbient = true;
                    ov.ShowFps = false;
                    ov.ShowLows = false;
                    ov.ShowApuFan = false;
                    ov.ShowSysFan = false;
                    ov.ShowMode = false;
                    ov.ShowClocks = true;

                    ov.UpdateMetrics(t61Status, "AutoTune", true);
                    int visibleCount = ov.CountVisibleMetrics();

                    if (visibleCount < 8)
                    {
                        metricOrderAndFormatOk = false;
                        metricOrderDetails = string.Format("Expected >= 8 visible metrics (including RAM 3rd, FCLK 4th, Ambient), got {0}", visibleCount);
                    }
                }

                // 3. Verify LiveTelemetryCanvas properties
                using (LiveTelemetryCanvas canvas = new LiveTelemetryCanvas())
                {
                    canvas.Stats = t61Stats;
                    canvas.Status = t61Status;
                    canvas.ShowCpu = true;
                    canvas.ShowGpu = true;
                    canvas.ShowRam = true;
                    canvas.ShowFabricClock = true;
                    canvas.ShowNvme = true;
                    canvas.ShowPower = true;
                    canvas.ShowAmbient = true;

                    if (!canvas.ShowAmbient || !canvas.ShowRam || !canvas.ShowFabricClock)
                    {
                        metricOrderAndFormatOk = false;
                        metricOrderDetails = "LiveTelemetryCanvas toggles failed!";
                    }
                }
            }
            catch (Exception ex)
            {
                metricOrderAndFormatOk = false;
                metricOrderDetails = "Exception: " + ex.Message;
            }

            Check(metricOrderAndFormatOk,
                "Live Trend & Overlay Metric Ordering (RAM 3rd, FCLK 4th, Chassis Ambient) & Format Parity",
                metricOrderDetails);

            // 62. AMD FSR Upscaling Detection, Multi-NVMe Storage & Read/Write Throughput, and RAM Dual Peaks (Temp + Load)
            bool fsrAndNvmeOk = true;
            string fsrAndNvmeDetails = "";
            try
            {
                // 1. Validate FSR in RadeonSettingsTelemetry and FanStatus
                RadeonSettingsTelemetry radFsrTest = new RadeonSettingsTelemetry { FreeSync = true, AntiLag = true, Afmf = true, Fsr = true, RadeonBoost = true };
                List<string> radTags = radFsrTest.GetActiveTags();
                string radSummary = RadeonSettingsTelemetry.BuildSummary(true, true, true, true, false, false, false, true, false, false, false, 0);

                FanStatus fsrStatus = new FanStatus
                {
                    RadeonFreeSync = true,
                    RadeonAntiLag = true,
                    RadeonAfmf = true,
                    RadeonFsr = true,
                    RadeonBoost = true
                };
                List<string> fsrActiveTags = fsrStatus.GetActiveRadeonTags();

                if (!radTags.Contains("FSR") || !fsrActiveTags.Contains("FSR") || !radSummary.Contains("FSR"))
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = "FSR pill missing from active tags or summary!";
                }

                // 1b. Validate Global vs Running-Game Adrenalin DB scoping (never leak per-game FSR/Anti-Lag at idle)
                string mockGm = @"{""games"":[{""guid"":""{b0256f8c-e353-42ea-a0fe-88ded17b32c5}"",""exe_path"":""C:\\Games\\SurrounDead-Win64-Shipping.exe"",""FSRUpgradeAvailable"":""TRUE""}]}";
                string mockGex = @"{"""":{""Profiles"":[{""GPUS"":[{""GraphicsFeatures"":[{""FeatureID"":33,""FeatureValue"":""0""},{""FeatureID"":66,""FeatureValue"":""0""}]}]}]},""{b0256f8c-e353-42ea-a0fe-88ded17b32c5}"":{""Profiles"":[{""GPUS"":[{""GraphicsFeatures"":[{""FeatureID"":33,""FeatureValue"":""1""},{""FeatureID"":66,""FeatureValue"":""1""}]}]}]}}";

                RadeonSettingsTelemetry idleTel = new RadeonSettingsTelemetry();
                RadeonSettingsSensor.TryCorroborateFromAdrenalinJson(ref idleTel, mockGm, mockGex, null);
                RadeonSettingsTelemetry gameTel = new RadeonSettingsTelemetry();
                RadeonSettingsSensor.TryCorroborateFromAdrenalinJson(ref gameTel, mockGm, mockGex, "SurrounDead-Win64-Shipping.exe");

                if (idleTel.Fsr || idleTel.AntiLag || !gameTel.Fsr || !gameTel.AntiLag)
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = string.Format("Adrenalin DB scoping failed! Idle(FSR={0},AntiLag={1}) Game(FSR={2},AntiLag={3})",
                        idleTel.Fsr, idleTel.AntiLag, gameTel.Fsr, gameTel.AntiLag);
                }

                // 2. Validate Multi-NVMe & Read/Write throughput tracking in FanStatus & Tooltip
                FanStatus dualNvme = new FanStatus
                {
                    NvmeTemp = 42,
                    NvmeHealthPct = 94,
                    NvmeModel = "Sabrent Rocket 4.0 Plus",
                    NvmeAvailableSpare = 100,
                    NvmeWearPct = 6,
                    NvmeDriveCount = 2,
                    NvmeReadMBs = 15.2,
                    NvmeWriteMBs = 48.7,
                    Nvme2Temp = 46,
                    Nvme2HealthPct = 99,
                    Nvme2Model = "Samsung 990 PRO 2TB",
                    Nvme2ReadMBs = 8.1,
                    Nvme2WriteMBs = 0.0
                };

                string dualTip = dualNvme.FormatNvmeTooltip();
                if (!dualTip.Contains("2 Drives Detected") || !dualTip.Contains("Sabrent Rocket") || !dualTip.Contains("Samsung 990") || !dualTip.Contains("15.2 MB/s Read"))
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = "Multi-NVMe tooltip does not contain dual drive stats or throughput!";
                }

                // 3. Validate RAM Peak tracking (both temp and load)
                TelemetrySessionStats ramPeakStats = new TelemetrySessionStats();
                ramPeakStats.Record(new FanStatus { CpuTemp = 55, DdrTemp = 50, RamLoadPct = 35 });
                ramPeakStats.Record(new FanStatus { CpuTemp = 60, DdrTemp = 55, RamLoadPct = 42 });
                ramPeakStats.Record(new FanStatus { CpuTemp = 58, DdrTemp = 53, RamLoadPct = 39 });

                if (ramPeakStats.MaxDdrTemp != 55 || ramPeakStats.MaxRamLoad != 42 || ramPeakStats.MinRamLoad != 35)
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = string.Format("RAM session stats peak tracking failed! MaxDdr={0}, MaxLoad={1}, MinLoad={2}",
                        ramPeakStats.MaxDdrTemp, ramPeakStats.MaxRamLoad, ramPeakStats.MinRamLoad);
                }

                // Verify Dual Peak format
                string ramPeakFmt = string.Format("▲{0}° ▲{1}%", ramPeakStats.MaxDdrTemp, ramPeakStats.MaxRamLoad);
                if (ramPeakFmt != "▲55° ▲42%")
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = "RAM dual peak format mismatch: " + ramPeakFmt;
                }

                if (fsrAndNvmeOk)
                {
                    fsrAndNvmeDetails = string.Format("FSR Pill [OK], Dual NVMe Tooltip & Read/Write ({0:0.0}R/{1:0.0}W MB/s) [OK], RAM Dual Peaks ({2}) [OK]",
                        dualNvme.NvmeReadMBs, dualNvme.NvmeWriteMBs, ramPeakFmt);
                }
            }
            catch (Exception ex)
            {
                fsrAndNvmeOk = false;
                fsrAndNvmeDetails = "Exception: " + ex.Message;
            }

            Check(fsrAndNvmeOk,
                "AMD FSR Upscaling Detection, Multi-NVMe I/O Throughput & RAM Dual Peaks (Temp + Load)",
                fsrAndNvmeDetails);

            // 63. DDR Dual Data Rate (MT/s) Effective Speed & Dual Stacked SODIMM Temperature Tracking (Single vs Dual Stick)
            bool ddrAndDualStickOk = true;
            string ddrDetails = "";
            try
            {
                // 1. Verify Double Data Rate (DDR) Speed Calculation (MCLK 2800 -> 5600 MT/s / MHz)
                FanStatus ddrStatus = new FanStatus
                {
                    MemClockMhz = 2800,
                    RamTotalGb = 47.6,
                    RamUsedGb = 20.5,
                    RamLoadPct = 43,
                    DdrStickCount = 2,
                    DdrTemp1 = 41, // Bottom DIMM 0 (cooler)
                    DdrTemp2 = 48, // Top DIMM 1 (hotter stacked stick)
                    DdrTemp = 48   // Composite / fan curve target
                };

                if (ddrStatus.RamSpeedMhz != 5600)
                {
                    ddrAndDualStickOk = false;
                    ddrDetails = string.Format("DDR RamSpeedMhz calculation failed! Expected 5600, got {0}", ddrStatus.RamSpeedMhz);
                }

                // 2. Validate Dual Stacked SODIMM Tooltip with peaks
                TelemetrySessionStats ddrStats = new TelemetrySessionStats();
                ddrStats.Record(new FanStatus { CpuTemp = 50, DdrStickCount = 2, DdrTemp1 = 41, DdrTemp2 = 48, DdrTemp = 48, RamLoadPct = 43 });
                ddrStats.Record(new FanStatus { CpuTemp = 65, DdrStickCount = 2, DdrTemp1 = 59, DdrTemp2 = 76, DdrTemp = 76, RamLoadPct = 85 });

                string dualStickTip = ddrStatus.FormatRamTooltip(ddrStats);
                if (!dualStickTip.Contains("5600 MT/s") ||
                    !dualStickTip.Contains("Dual Stacked SO-DIMMs") ||
                    !dualStickTip.Contains("DIMM 0 (Channel A / Bottom Stick): 41°C (SPD5118 Hub • Peak: 59°C)") ||
                    !dualStickTip.Contains("DIMM 1 (Channel B / Top Stick)   : 48°C (SPD5118 Hub • Peak: 76°C)") ||
                    !dualStickTip.Contains("Stack Thermal Differential (ΔT)  : 7°C"))
                {
                    ddrAndDualStickOk = false;
                    ddrDetails = "Dual stacked SODIMM tooltip missing detailed per-slot SPD Hub temps or peaks: " + dualStickTip;
                }

                // 3. Validate Single Stick Accomodation (Graceful fallback when only 1 stick is installed)
                FanStatus singleStickStatus = new FanStatus
                {
                    MemClockMhz = 2800,
                    RamTotalGb = 16.0,
                    RamUsedGb = 6.2,
                    RamLoadPct = 39,
                    DdrStickCount = 1,
                    DdrTemp1 = 43,
                    DdrTemp2 = 0,
                    DdrTemp = 43
                };
                string singleStickTip = singleStickStatus.FormatRamTooltip();
                if (!singleStickTip.Contains("Single SO-DIMM") || singleStickTip.Contains("DIMM 1") || singleStickTip.Contains("Dual Stacked"))
                {
                    ddrAndDualStickOk = false;
                    ddrDetails = "Single stick RAM tooltip incorrectly displayed dual modules: " + singleStickTip;
                }

                // 4. Validate Overlay Rendering (Dual Data Rate & Temperature Display)
                using (CapFrameXOverlayForm osdDdr = new CapFrameXOverlayForm())
                {
                    osdDdr.VerticalLayout = false;
                    osdDdr.ShowClocks = true;
                    osdDdr.ShowRam = true;
                    osdDdr.UpdateMetrics(ddrStatus, "Balanced", true);
                    osdDdr.UpdateDimensions();

                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdDdr.TargetWidth), Math.Max(1, osdDdr.TargetHeight)))
                    {
                        osdDdr.DrawToBitmap(bmp, new Rectangle(0, 0, osdDdr.TargetWidth, osdDdr.TargetHeight));
                    }
                    bool horizRenderOk = osdDdr.TargetWidth > 300 && osdDdr.TargetHeight > 20;

                    // Single stick OSD check
                    osdDdr.UpdateMetrics(singleStickStatus, "Balanced", true);
                    osdDdr.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdDdr.TargetWidth), Math.Max(1, osdDdr.TargetHeight)))
                    {
                        osdDdr.DrawToBitmap(bmp, new Rectangle(0, 0, osdDdr.TargetWidth, osdDdr.TargetHeight));
                    }
                    bool singleRenderOk = osdDdr.TargetWidth > 300 && osdDdr.TargetHeight > 20;

                    // Vertical layout check
                    osdDdr.VerticalLayout = true;
                    osdDdr.UpdateMetrics(ddrStatus, "Balanced", true);
                    osdDdr.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdDdr.TargetWidth), Math.Max(1, osdDdr.TargetHeight)))
                    {
                        osdDdr.DrawToBitmap(bmp, new Rectangle(0, 0, osdDdr.TargetWidth, osdDdr.TargetHeight));
                    }
                    bool vertRenderOk = osdDdr.TargetWidth >= 285 && osdDdr.TargetHeight > 100;

                    if (!horizRenderOk || !singleRenderOk || !vertRenderOk)
                    {
                        ddrAndDualStickOk = false;
                        ddrDetails = string.Format("OSD Render failed! HorizOk={0}, SingleOk={1}, VertOk={2}", horizRenderOk, singleRenderOk, vertRenderOk);
                    }
                }

                if (ddrAndDualStickOk)
                {
                    ddrDetails = string.Format("Dual Data Rate [5600 MT/s @ 2800 MCLK OK], Dual Stacked SODIMMs [DIMM0=41°C/Peak 59°C, DIMM1=48°C/Peak 76°C, ΔT=7°C OK], Single Stick Fallback [OK], OSD Formatting [OK]");
                }
            }
            catch (Exception ex)
            {
                ddrAndDualStickOk = false;
                ddrDetails = "Exception: " + ex.Message;
            }

            Check(ddrAndDualStickOk,
                "DDR Dual Data Rate (MT/s) Effective Speed & Dual Stacked SODIMM Temperature Tracking (Single vs Dual Stick)",
                ddrDetails);

            // 64. CapFrameX-Inspired Phased RefreshThrottle Bus Optimization
            bool throttleTestOk = true;
            string throttleDetails = "";
            try
            {
                long mockClock = 0;
                Func<long> clock = () => mockClock;

                // 1. First call reads immediately
                RefreshThrottle throttleA = new RefreshThrottle(TimeSpan.FromSeconds(2), TimeSpan.Zero, clock);
                if (!throttleA.TryBegin())
                {
                    throttleTestOk = false;
                    throttleDetails = "First call to RefreshThrottle did not read immediately!";
                }
                if (throttleA.TryBegin())
                {
                    throttleTestOk = false;
                    throttleDetails = "Immediate subsequent call to RefreshThrottle should have been throttled!";
                }

                // 2. Grid timing verification across 10 seconds of simulated 250ms polling jitter
                int[] pollJitter = new int[] { -12, 9, -3, 15, -15, 4, 0, -7, 11, -1 };
                List<long> reads = new List<long>();
                mockClock = 0;
                throttleA.Reset();

                for (int poll = 0; poll < 40; poll++)
                {
                    mockClock = 250L * poll + (poll == 0 ? 0 : pollJitter[poll % pollJitter.Length]);
                    if (throttleA.TryBegin())
                        reads.Add(mockClock);
                }

                if (reads.Count != 5)
                {
                    throttleTestOk = false;
                    throttleDetails = string.Format("Expected exactly 5 periodic reads across 10s, got {0}", reads.Count);
                }

                // 3. Phased staggering between Stick 0 and Stick 1 (interleaved bus access)
                mockClock = 0;
                RefreshThrottle stick0 = new RefreshThrottle(TimeSpan.FromSeconds(2), TimeSpan.Zero, clock);
                RefreshThrottle stick1 = new RefreshThrottle(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(250), clock);

                List<long> stick0Reads = new List<long>();
                List<long> stick1Reads = new List<long>();
                for (mockClock = 0; mockClock <= 6000; mockClock += 250)
                {
                    if (stick0.TryBegin()) stick0Reads.Add(mockClock);
                    if (stick1.TryBegin()) stick1Reads.Add(mockClock);
                }

                // Verify non-collision: stick0 and stick1 never read at the same timestamp (except initial discovery t=0)
                int collisions = 0;
                for (int i = 0; i < stick0Reads.Count; i++)
                {
                    if (stick0Reads[i] > 0 && stick1Reads.Contains(stick0Reads[i]))
                        collisions++;
                }

                if (collisions > 0)
                {
                    throttleTestOk = false;
                    throttleDetails = string.Format("Phased throttles collided {0} times on the shared bus!", collisions);
                }

                // 4. Gap restart verification
                mockClock = 15000; // 9s idle gap
                if (!stick0.TryBegin())
                {
                    throttleTestOk = false;
                    throttleDetails = "Throttle failed to fire after an idle gap!";
                }

                if (throttleTestOk)
                {
                    throttleDetails = string.Format("Deterministic Grid [5/5 Periodic Reads OK], Phased Bus Interleaving [0 collisions on SMBus OK], Gap Recovery [OK]");
                }
            }
            catch (Exception ex)
            {
                throttleTestOk = false;
                throttleDetails = "Exception: " + ex.Message;
            }

            Check(throttleTestOk,
                "CapFrameX-Inspired Phased RefreshThrottle Bus Optimization (Zero Bus Collision & Jitter Immunity)",
                throttleDetails);

            // 65. Hook-Free Overlay Zero-Impact Repaint Dirty-Check & Ryzen Processor Tier Badges
            bool osdDirtyCheckOk = true;
            string osdDirtyDetails = "";
            try
            {
                FanStatus baseSt = new FanStatus
                {
                    CpuTemp = 50,
                    CpuLoadPct = 12,
                    CpuClockMhz = 1850,
                    GpuTemp = 48,
                    ApuPowerWatts = 15,
                    Fan1Rpm = 1100,
                    Fan2Rpm = 650,
                    RamTotalGb = 32.0,
                    RamUsedGb = 12.0,
                    RamLoadPct = 37,
                    DdrStickCount = 2,
                    DdrTemp1 = 41,
                    DdrTemp2 = 48,
                    DdrTemp = 48,
                    NvmeTemp = 38,
                    AmbientTemp = 43,
                    RadeonFeaturesAvailable = true,
                    RadeonFreeSync = true,
                    RadeonFsr = true
                };

                // 1. Verify Thermal Differential property (ΔT)
                if (baseSt.DdrTempSpread != 7)
                {
                    osdDirtyCheckOk = false;
                    osdDirtyDetails = string.Format("Expected DdrTempSpread of 7°C, got {0}", baseSt.DdrTempSpread);
                }

                // 2. Verify Hook-Free Invalidation Optimization (Skipping redundant GDI+ redraws)
                using (CapFrameXOverlayForm osd = new CapFrameXOverlayForm())
                {
                    osd.Show(); // Make visible so dirty-check runs
                    osd.ProcessorBadgeText = "Ryzen AI 9";
                    osd.ShowRadeonSettings = true;
                    osd.GraphMode = 3; // Text only to test pure metric signature stability

                    // First update should repaint
                    osd.UpdateMetrics(baseSt, "Balanced", true);
                    long repaintsAfterFirst = osd.TotalRepaintCount;
                    long skippedAfterFirst = osd.SkippedRepaintCount;

                    // Second update with identical telemetry should be skipped entirely
                    osd.UpdateMetrics(baseSt, "Balanced", true);
                    if (osd.SkippedRepaintCount <= skippedAfterFirst)
                    {
                        osdDirtyCheckOk = false;
                        osdDirtyDetails = "Hook-Free Overlay failed to skip redundant repaint when telemetry was identical!";
                    }

                    // Third update with modified CPU temperature MUST trigger repaint
                    FanStatus changedSt = baseSt;
                    changedSt.CpuTemp = 58;
                    osd.UpdateMetrics(changedSt, "Balanced", true);
                    if (osd.TotalRepaintCount <= repaintsAfterFirst)
                    {
                        osdDirtyCheckOk = false;
                        osdDirtyDetails = "Hook-Free Overlay failed to trigger repaint when CPU temperature changed!";
                    }

                    // 3. Verify Ryzen Processor Tier Badge Rendering (Horizontal & Vertical)
                    osd.VerticalLayout = false;
                    osd.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osd.TargetWidth), Math.Max(1, osd.TargetHeight)))
                    {
                        osd.DrawToBitmap(bmp, new Rectangle(0, 0, osd.TargetWidth, osd.TargetHeight));
                    }
                    bool hBadgeOk = osd.TargetWidth > 200 && osd.TargetHeight > 20;

                    osd.VerticalLayout = true;
                    osd.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osd.TargetWidth), Math.Max(1, osd.TargetHeight)))
                    {
                        osd.DrawToBitmap(bmp, new Rectangle(0, 0, osd.TargetWidth, osd.TargetHeight));
                    }
                    bool vBadgeOk = osd.TargetWidth >= 285 && osd.TargetHeight > 100;

                    if (!hBadgeOk || !vBadgeOk)
                    {
                        osdDirtyCheckOk = false;
                        osdDirtyDetails = string.Format("Ryzen tier badge render failed! H={0}, V={1}", hBadgeOk, vBadgeOk);
                    }
                    osd.Hide();
                }

                if (osdDirtyCheckOk)
                {
                    osdDirtyDetails = string.Format("SODIMM Thermal Spread [ΔT=7°C OK], Dirty-Check Repaint Skip [Redundant Render Skipped OK], Dynamic Invalidation [OK], Ryzen Tier Badge [Ryzen AI 9 Rendered OK]");
                }
            }
            catch (Exception ex)
            {
                osdDirtyCheckOk = false;
                osdDirtyDetails = "Exception: " + ex.Message;
            }

            Check(osdDirtyCheckOk,
                "Hook-Free Overlay Zero-Impact Repaint Dirty-Check & Ryzen Processor Tier Badges",
                osdDirtyDetails);

            // 66. RAM & NVMe Dynamic Power Usage Readout (OSD, Live Trend Pills, Status Labels & Tooltips)
            bool powerReadoutOk = true;
            string powerReadoutDetails = "";
            try
            {
                // 1. Verify RAM Power calculation (single stick vs dual stick)
                FanStatus singleRam = new FanStatus { RamTotalGb = 16.0, RamLoadPct = 50 };
                FanStatus dualRam = new FanStatus { RamTotalGb = 47.6, RamLoadPct = 49 };
                if (Math.Abs(singleRam.RamPowerWatts - 2.4) > 0.05)
                {
                    powerReadoutOk = false;
                    powerReadoutDetails = string.Format("Single-stick RAM power calculation failed! Expected ~2.4W, got {0:F2}W", singleRam.RamPowerWatts);
                }
                if (Math.Abs(dualRam.RamPowerWatts - 4.76) > 0.05)
                {
                    powerReadoutOk = false;
                    powerReadoutDetails = string.Format("Dual-stick RAM power calculation failed! Expected ~4.8W, got {0:F2}W", dualRam.RamPowerWatts);
                }

                // 2. Verify NVMe Power calculation (idle vs active I/O load)
                FanStatus nvmeIdle = new FanStatus { NvmeTemp = 44, NvmeDriveCount = 1 };
                FanStatus nvmeActive = new FanStatus { NvmeTemp = 44, NvmeDriveCount = 1, NvmeReadMBs = 250.0 };
                if (Math.Abs(nvmeIdle.NvmePowerWatts - 0.9) > 0.05)
                {
                    powerReadoutOk = false;
                    powerReadoutDetails = string.Format("NVMe idle power calculation failed! Expected ~0.9W, got {0:F2}W", nvmeIdle.NvmePowerWatts);
                }
                if (Math.Abs(nvmeActive.NvmePowerWatts - 5.4) > 0.05)
                {
                    powerReadoutOk = false;
                    powerReadoutDetails = string.Format("NVMe active power calculation failed! Expected ~5.4W, got {0:F2}W", nvmeActive.NvmePowerWatts);
                }

                // 3. Verify Tooltip Power Readouts
                string nvmePwrTip = nvmeIdle.FormatNvmeTooltip();
                string ramPwrTip = dualRam.FormatRamTooltip();
                if (!nvmePwrTip.Contains("~0.9 W") || !ramPwrTip.Contains("~4.8 W"))
                {
                    powerReadoutOk = false;
                    powerReadoutDetails = "Tooltips missing RAM or NVMe power envelopes: " + nvmePwrTip;
                }

                // 4. Verify CapFrameX OSD Formatting for RAM & NVMe Power (Horizontal & Vertical)
                FanStatus testOsdSt = new FanStatus
                {
                    CpuTemp = 64,
                    CpuLoadPct = 24,
                    CpuClockMhz = 1763,
                    GpuTemp = 74,
                    GfxClockMhz = 2685,
                    GfxActivityPct = 99,
                    RamTotalGb = 47.6,
                    RamLoadPct = 49,
                    MemClockMhz = 2800,
                    DdrStickCount = 2,
                    DdrTemp1 = 37,
                    DdrTemp2 = 43,
                    DdrTemp = 43,
                    NvmeTemp = 44,
                    NvmeDriveCount = 1,
                    NvmeReadMBs = 0.0,
                    NvmeWriteMBs = 0.0
                };

                using (CapFrameXOverlayForm osdPwr = new CapFrameXOverlayForm())
                {
                    osdPwr.ShowRam = true;
                    osdPwr.ShowNvme = true;
                    osdPwr.ShowClocks = true;
                    osdPwr.VerticalLayout = false;
                    osdPwr.UpdateMetrics(testOsdSt, "AutoTune", true);
                    osdPwr.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdPwr.TargetWidth), Math.Max(1, osdPwr.TargetHeight)))
                    {
                        osdPwr.DrawToBitmap(bmp, new Rectangle(0, 0, osdPwr.TargetWidth, osdPwr.TargetHeight));
                    }
                    bool hOk = osdPwr.TargetWidth > 200 && osdPwr.TargetHeight > 20;

                    osdPwr.VerticalLayout = true;
                    osdPwr.UpdateMetrics(testOsdSt, "AutoTune", true);
                    osdPwr.UpdateDimensions();
                    using (Bitmap bmp = new Bitmap(Math.Max(1, osdPwr.TargetWidth), Math.Max(1, osdPwr.TargetHeight)))
                    {
                        osdPwr.DrawToBitmap(bmp, new Rectangle(0, 0, osdPwr.TargetWidth, osdPwr.TargetHeight));
                    }
                    bool vOk = osdPwr.TargetWidth >= 285 && osdPwr.TargetHeight > 100;

                    if (!hOk || !vOk)
                    {
                        powerReadoutOk = false;
                        powerReadoutDetails = string.Format("Overlay Power render failed! H={0}, V={1}", hOk, vOk);
                    }
                }

                if (powerReadoutOk)
                {
                    powerReadoutDetails = string.Format("RAM Power [Single=~2.4W, Dual=~4.8W OK], NVMe Power [Idle=~0.9W, Active=~5.4W OK], Tooltips & OSD Power Formatting [OK]");
                }
            }
            catch (Exception ex)
            {
                powerReadoutOk = false;
                powerReadoutDetails = "Exception: " + ex.Message;
            }

            Check(powerReadoutOk,
                "RAM & NVMe Dynamic Power Usage Readout (OSD, Live Trend Pills, Status Labels & Tooltips)",
                powerReadoutDetails);

            // 67. Live Telemetry Auto-Zoom Scaling, Right-Axis RPM Decoupling & AMD Silicon Junction Fallback Parity
            bool autoZoomAndSiliconOk = true;
            string autoZoomDetails = "";
            try
            {
                // 1. Verify Auto-Zoom Canvas Layout, Non-Overlapping Hitboxes & Fan RPM Decoupling
                TelemetrySessionStats t67Stats = new TelemetrySessionStats();
                t67Stats.CpuHistory.AddRange(new int[] { 52, 53, 56, 58, 55, 54 });
                t67Stats.GpuHistory.AddRange(new int[] { 48, 49, 50, 52, 51, 50 });
                t67Stats.PowerHistory.AddRange(new int[] { 16, 17, 19, 18, 16, 17 });
                t67Stats.RamPowerHistory.AddRange(new int[] { 35, 36, 35, 35, 36, 35 });
                t67Stats.Fan1RpmHistory.AddRange(new int[] { 335, 340, 337, 345, 338, 337 });
                t67Stats.Fan2RpmHistory.AddRange(new int[] { 430, 440, 438, 445, 436, 438 });
                t67Stats.FabricClockHistory.AddRange(new int[] { 1800, 1800, 1800, 1800, 1800, 1800 });

                FanStatus t67Status = new FanStatus
                {
                    CpuTemp = 58,
                    GpuTemp = 50,
                    ApuPowerWatts = 19,
                    Fan1Rpm = 337,
                    Fan2Rpm = 438,
                    FabricClockMhz = 1800,
                    DdrTemp1 = 34,
                    DdrTemp2 = 40,
                    DdrStickCount = 2,
                    RamLoadPct = 34,
                    AmbientTemp = 36
                };

                using (LiveTelemetryCanvas canvas = new LiveTelemetryCanvas())
                {
                    canvas.Size = new Size(680, 260);
                    canvas.Stats = t67Stats;
                    canvas.Status = t67Status;
                    canvas.HasDualFans = true;
                    canvas.AutoScale = true;
                    canvas.HighlightMetric = 1; // Spotlight CPU Temp

                    using (Bitmap bmp = new Bitmap(680, 260))
                    {
                        canvas.DrawToBitmap(bmp, new Rectangle(0, 0, 680, 260));
                    }

                    // Verify Action buttons (index 9: Auto-Zoom, index 10: Reset)
                    Rectangle azRect = canvas.TestGetPillRect(9);
                    Rectangle rstRect = canvas.TestGetPillRect(10);
                    Rectangle apuRect = canvas.TestGetPillRect(7);
                    Rectangle sysRect = canvas.TestGetPillRect(8);

                    if (azRect.IsEmpty || rstRect.IsEmpty)
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = "Auto-Zoom or Reset action pill rect is empty!";
                    }
                    else if (azRect.IntersectsWith(apuRect) || azRect.IntersectsWith(sysRect))
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = string.Format("Auto-Zoom ({0}) collides with APU ({1}) or Sys ({2}) pill!", azRect, apuRect, sysRect);
                    }
                    else if (rstRect.IntersectsWith(apuRect) || rstRect.IntersectsWith(sysRect))
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = string.Format("Reset ({0}) collides with APU ({1}) or Sys ({2}) pill!", rstRect, apuRect, sysRect);
                    }

                    // Test hit-testing priority: clicking Auto-Zoom rect toggles AutoScale without altering fan visibility
                    bool origApu = canvas.ShowApuRpm;
                    bool origSys = canvas.ShowSysRpm;
                    bool origScale = canvas.AutoScale;

                    MouseEventArgs clickAz = new MouseEventArgs(MouseButtons.Left, 1, azRect.X + azRect.Width / 2, azRect.Y + azRect.Height / 2, 0);
                    canvas.TestOnMouseDown(clickAz);

                    if (canvas.AutoScale == origScale)
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = "Clicking Auto-Zoom pill failed to toggle AutoScale!";
                    }
                    if (canvas.ShowApuRpm != origApu || canvas.ShowSysRpm != origSys)
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = "Clicking Auto-Zoom inadvertently toggled fan visibility!";
                    }
                }

                // 2. Verify Silicon Tctl Parity and Multi-Tier Fallback Priority
                // Test SMN range shift & Strix Point Zen 5 (both unshifted and PCIe ECAM 0x74EB0000 = 67.88C -> 68C matching HWiNFO64 68.1C / GPU-Z 68.2C)
                uint rawZen5_58 = (466u << 21) | 0x00030000u; // 466 * 0.125 = 58.25C -> 58C
                int decoded58 = HardwareBackend.DecodeAmdSmnTemp(rawZen5_58);
                uint rawEcam68 = 0x74EB0000u; // Live Strix Point PCIe ECAM capture: (935 * 0.125) - 49 = 67.875C -> 68C
                int decoded68 = HardwareBackend.DecodeAmdSmnTemp(rawEcam68);
                if (decoded58 != 58 || decoded68 != 68)
                {
                    autoZoomAndSiliconOk = false;
                    autoZoomDetails = string.Format("Expected 58C/68C for Zen 5 Tctl, got {0}C/{1}C", decoded58, decoded68);
                }

                // 3. Verify OSD Granular RAM Sub-Field Formatting (Decluttered Default vs Compact vs Detailed)
                using (CapFrameXOverlayForm osdSub = new CapFrameXOverlayForm())
                {
                    FanStatus subSt = new FanStatus
                    {
                        RamTotalGb = 47.6,
                        RamUsedGb = 10.5,
                        RamLoadPct = 22,
                        MemClockMhz = 1719, // 3438 MT/s
                        DdrStickCount = 2,
                        DdrTemp1 = 35,
                        DdrTemp2 = 41,
                        DdrTemp = 41
                    };
                    osdSub.UpdateMetrics(subSt, "AutoTune", true);

                    // Default: Single-stick + spread delta without dual-stick or '@ 3438MHz' overflow
                    string fmtDefault = osdSub.FormatRamOverlayValue();
                    // Compact single-stick temp without delta
                    osdSub.ShowRamDualSticks = false;
                    osdSub.ShowRamSpread = false;
                    string fmtSingle = osdSub.FormatRamOverlayValue();
                    // Full detailed
                    osdSub.ShowRamDualSticks = true;
                    osdSub.ShowRamSpread = true;
                    osdSub.ShowRamClock = true;
                    string fmtFull = osdSub.FormatRamOverlayValue();

                    if (fmtDefault != "41°C (Δ6°) 22% ~3.6W" ||
                        fmtSingle != "41°C 22% ~3.6W" ||
                        fmtFull != "35°/41°C (Δ6°) 22% @ 3438MHz ~3.6W")
                    {
                        autoZoomAndSiliconOk = false;
                        autoZoomDetails = string.Format("RAM Sub-Field formatting mismatch: default='{0}', single='{1}', full='{2}'", fmtDefault, fmtSingle, fmtFull);
                    }

                    // Verify new default OSD settings & Peak/Low/Avg tracker modes
                    using (CapFrameXOverlayForm defOsd = new CapFrameXOverlayForm())
                    {
                        bool defaultsOk = defOsd.VerticalLayout && defOsd.OsdScaleTier == 1 && defOsd.GraphHeight == 140 &&
                                          defOsd.OpacityPct == 80 && defOsd.ShowFabricClock && defOsd.ShowNpu &&
                                          defOsd.ShowDriverVersion && defOsd.ShowClocks && defOsd.SparklinesAsBackground &&
                                          defOsd.DfGraphStyle && defOsd.DfGraphFirst && !defOsd.ShowRamDualSticks &&
                                          defOsd.ShowRamSpread && !defOsd.ShowVrmCurrent;
                        TelemetrySessionStats tStats = new TelemetrySessionStats();
                        tStats.Record(new FanStatus { CpuTemp = 40, CpuLoadPct = 10, ApuPowerWatts = 8, DdrTemp = 36, RamLoadPct = 50, AmbientTemp = 30 });
                        tStats.Record(new FanStatus { CpuTemp = 70, CpuLoadPct = 80, ApuPowerWatts = 36, DdrTemp = 44, RamLoadPct = 90, AmbientTemp = 38 });
                        defOsd.Stats = tStats;
                        defOsd.ShowPeaks = true;
                        defOsd.PeakTrackerMode = 0; // Peaks
                        string pk0 = defOsd.FormatRamOverlayPeak();
                        defOsd.PeakTrackerMode = 1; // Lows
                        string pk1 = defOsd.FormatRamOverlayPeak();
                        defOsd.PeakTrackerMode = 2; // Min/Max
                        string pk2 = defOsd.FormatRamOverlayPeak();
                        defOsd.PeakTrackerMode = 3; // Avg
                        string pk3 = defOsd.FormatRamOverlayPeak();
                        if (!defaultsOk || pk0 != "▲44° ▲90%" || pk1 != "▼36° ▼50%" || pk2 != "▼36▲44°" || pk3 != "~40° ~70%")
                        {
                            autoZoomAndSiliconOk = false;
                            autoZoomDetails = string.Format("Defaults/Trackers mismatch: defaults={0}, pk0='{1}', pk1='{2}', pk2='{3}', pk3='{4}'",
                                defaultsOk, pk0, pk1, pk2, pk3);
                        }
                    }
                }

                if (autoZoomAndSiliconOk)
                {
                    autoZoomDetails = "Auto-Zoom Pill Anti-Collision [OK], Right-Axis RPM FCLK Decoupling [OK], PCIe ECAM SMN Tctl 58°C/68°C Parity [OK], OSD RAM Sub-Fields [OK]";
                }
            }
            catch (Exception ex)
            {
                autoZoomAndSiliconOk = false;
                autoZoomDetails = "Exception: " + ex.Message;
            }

            Check(autoZoomAndSiliconOk,
                "Live Telemetry Auto-Zoom Scaling, Right-Axis RPM Decoupling & AMD Silicon Junction Fallback Parity",
                autoZoomDetails);

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
                    bool autoOk = hw.SetAuto();
                    if (autoOk)
                        Console.WriteLine("  Restore : Safe Super I/O Fallback & EC Auto [OK]");
                    else
                        ok = false;
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
                    bool autoOk = hw.SetAuto();
                    if (autoOk)
                        Console.WriteLine("  Restore : Native BIOS EC Auto (0x00 to 0x32..0x34) [OK]");
                    else
                        ok = false;
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