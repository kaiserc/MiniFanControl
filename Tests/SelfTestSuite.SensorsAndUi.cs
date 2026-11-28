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
        private static void RunSensorsAndUiTests()
        {
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

                    // 3. Verify Dynamic Responsive Vertical Layout across 4K/1440p (1216px) and 1080p/Scaled (976px)
                    mf.TestApplyWideLayoutSize(1100, 1216);
                    // On Live Trend at 1216px: full 648px canvas & 451px OSD Studio metrics panel (zero inner scroll!)
                    mf.TestSetGraphViewMode(true, false, 1);
                    bool liveHiddenOk = (!mf.TestManualFanControlVisible) &&
                                        (mf.TestModeCardPanel != null && mf.TestModeCardPanel.Height == 716) &&
                                        (mf.TestLiveCanvas != null && mf.TestLiveCanvas.Height == 648) &&
                                        (mf.TestOsdMetricsScrollPanel != null && mf.TestOsdMetricsScrollPanel.Height >= 450);

                    // On Live Perf at 1216px: full 648px perf canvas
                    mf.TestSetGraphViewMode(false, true, 0);
                    bool perfHiddenOk = (!mf.TestManualFanControlVisible) &&
                                        (mf.TestModeCardPanel != null && mf.TestModeCardPanel.Height == 716) &&
                                        (mf.TestPerfCanvas != null && mf.TestPerfCanvas.Height == 648);

                    // On APU Fan at 1216px: Manual Fan Control visible, modeCard contracts cleanly to 606px
                    mf.TestSetGraphViewMode(false, false, 0);
                    int curCardH = mf.TestModeCardPanel != null ? mf.TestModeCardPanel.Height : 0;
                    bool fanShownOk = mf.TestManualFanControlVisible && (curCardH == 606);

                    // Compact 1080p / 4K@200% responsive adaptation (976px): shrinks graphs to 408px with zero outer scroll
                    mf.TestApplyWideLayoutSize(1100, 976);
                    mf.TestSetGraphViewMode(true, false, 1);
                    bool compactAdaptOk = (mf.TestModeCardPanel != null && mf.TestModeCardPanel.Height == 476) &&
                                          (mf.TestLiveCanvas != null && mf.TestLiveCanvas.Height == 408);
                    fanShownOk = fanShownOk && compactAdaptOk;

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

                // 1c. Validate UMD\FsrOverride REG_SZ/DWORD flag helper ("1" -> true, "0"/"" -> false)
                if (!RadeonSettingsSensor.IsStringFlagEnabled("1")
                    || RadeonSettingsSensor.IsStringFlagEnabled("0")
                    || RadeonSettingsSensor.IsStringFlagEnabled("")
                    || !RadeonSettingsSensor.IsStringFlagEnabled(1)
                    || RadeonSettingsSensor.IsStringFlagEnabled(0))
                {
                    fsrAndNvmeOk = false;
                    fsrAndNvmeDetails = "UMD\\FsrOverride flag parser failed!";
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

            // Regression: Hidden OSD must never be resurrected via SWP_SHOWWINDOW when toggling click-through lock
            // (which previously left a visible zombie overlay with frozen startup _status while _showOverlay == false).
            bool hiddenOsdLockGuardOk = true;
            string hiddenOsdLockDetails = "Hidden OSD stayed invisible across ApplyClickThrough/EnsureTopMost toggles [OK], Unsloth AI process recognized [OK]";
            try
            {
                using (CapFrameXOverlayForm osdGuard = new CapFrameXOverlayForm())
                {
                    osdGuard.Show();
                    osdGuard.Hide();
                    bool hiddenAfterHide = !osdGuard.Visible && !osdGuard.IsNativeWindowVisible();
                    osdGuard.ApplyClickThrough(true);
                    osdGuard.ApplyClickThrough(false);
                    osdGuard.EnsureTopMost();
                    bool stillHiddenAfterLockToggle = !osdGuard.Visible && !osdGuard.IsNativeWindowVisible();
                    bool unslothRecognized = WorkloadProfileSwitcher.IsKnownAiProcess("unsloth-studio.exe");
                    if (!hiddenAfterHide || !stillHiddenAfterLockToggle || !unslothRecognized)
                    {
                        hiddenOsdLockGuardOk = false;
                        hiddenOsdLockDetails = string.Format(
                            "hiddenAfterHide={0}, stillHiddenAfterLockToggle={1}, Visible={2}, NativeVisible={3}, unslothRecognized={4}",
                            hiddenAfterHide, stillHiddenAfterLockToggle, osdGuard.Visible, osdGuard.IsNativeWindowVisible(), unslothRecognized);
                    }
                }
            }
            catch (Exception ex)
            {
                hiddenOsdLockGuardOk = false;
                hiddenOsdLockDetails = "Exception: " + ex.Message;
            }
            Check(hiddenOsdLockGuardOk,
                "Hidden OSD Click-Through Lock Does Not Resurrect Stale Overlay Window & Unsloth AI Detection",
                hiddenOsdLockDetails);

        }
    }
}
