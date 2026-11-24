using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace MiniFanControl
{
    public partial class CapFrameXOverlayForm
    {
        public int CountVisibleMetrics()
        {
            int c = 0;
            if (!VerticalLayout && ShowGameName && !string.IsNullOrEmpty(FormatGameName(_status.RtssProcessName))) c++;
            if (ShowCpu) c++;
            if (ShowGpu || ShowGpuLoad) c++;
            if (ShowNvme && _status.NvmeTemp > 0) c++;
            if (ShowPower) c++;
            if (ShowRam && _status.RamTotalGb > 0) c++;
            if (ShowVrmCurrent && _status.VddTdcCurrentAmps > 0) c++;
            if (ShowAmbient && _status.AmbientTemp > 0) c++;
            if (ShowFabricClock && _status.FabricClockMhz > 0) c++;
            if (ShowNpu && _status.NpuActivityPct >= 0) c++;
            if (ShowFps) c++;
            if (ShowLows) c++;
            if (ShouldShowApuFanRow()) c++;
            if (ShouldShowSysFanRow()) c++;
            if (ShouldShowModeRow()) c++;
            if (ShowDriverVersion && !string.IsNullOrEmpty(_status.RadeonSoftwareVer)) c++;
            if (ShowRadeonSettings)
            {
                List<string> tags = (_status.RadeonFeaturesAvailable && _status.GetActiveRadeonTags(ShowRadeonExtendedTags) != null) ? _status.GetActiveRadeonTags(ShowRadeonExtendedTags) : null;
                if ((tags != null && tags.Count > 0) || !string.IsNullOrEmpty(ProcessorBadgeText)) c++;
            }
            return c;
        }

        public int TargetWidth { get; private set; }
        public int TargetHeight { get; private set; }

        private class HorizColumn
        {
            public string Key;
            public string Label;
            public string Value;
            public string Peak;
            public Brush LabelBrush;
            public Brush ValueBrush;
            public Brush PeakBrush;
            public List<int> SparkSamples;
            public Color SparkColor;
            public List<string> RadeonTags;
            public float MinWidth;
            public float AllocatedWidth;
            public float StartX;
        }

        public string FormatRamOverlayValue()
        {
            List<string> parts = new List<string>();

            if (ShowRamTemp)
            {
                string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
                if (ShowRamDualSticks && _status.DdrStickCount == 2 && _status.DdrTemp1 > 0 && _status.DdrTemp2 > 0)
                {
                    int t1 = TelemetrySessionStats.ToDisplayTemp(_status.DdrTemp1, UseFahrenheit);
                    int t2 = TelemetrySessionStats.ToDisplayTemp(_status.DdrTemp2, UseFahrenheit);
                    int spr = TelemetrySessionStats.ToDisplayTempDelta(_status.DdrTempSpread, UseFahrenheit);
                    if (ShowRamSpread && _status.DdrTempSpread >= 1)
                        parts.Add(string.Format("{0}°/{1}{2} (Δ{3}°)", t1, t2, tUnit, spr));
                    else if (ShowRamSpread)
                        parts.Add(string.Format("{0}°/{1}{2} (Δ0°)", t1, t2, tUnit));
                    else
                        parts.Add(string.Format("{0}/{1}{2}", t1, t2, tUnit));
                }
                else if (_status.DdrTemp > 0)
                {
                    int t = TelemetrySessionStats.ToDisplayTemp(_status.DdrTemp, UseFahrenheit);
                    int spr = TelemetrySessionStats.ToDisplayTempDelta(_status.DdrTempSpread, UseFahrenheit);
                    if (ShowRamSpread && _status.DdrStickCount == 2 && _status.DdrTempSpread >= 1)
                        parts.Add(string.Format("{0}{1} (Δ{2}°)", t, tUnit, spr));
                    else
                        parts.Add(string.Format("{0}{1}", t, tUnit));
                }
            }

            if (ShowRamLoad)
            {
                parts.Add(string.Format("{0}%", _status.RamLoadPct));
            }

            int ddrSpeed = _status.RamSpeedMhz;
            if (ShowRamClock && ddrSpeed > 0)
            {
                parts.Add(string.Format("@ {0}MHz", ddrSpeed));
            }

            if (ShowRamPower && _status.RamPowerWatts > 0)
            {
                parts.Add(string.Format("~{0:0.0}W", _status.RamPowerWatts));
            }

            if (parts.Count == 0)
            {
                return string.Format("{0}%", _status.RamLoadPct);
            }

            return string.Join(" ", parts.ToArray());
        }

        private string FormatSingleTracker(int minVal, int maxVal, double avgVal, string unit, bool allowZero = false)
        {
            if (!ShowPeaks || Stats == null) return null;
            bool hasMax = allowZero ? maxVal >= 0 : maxVal > 0;
            bool hasMin = minVal < int.MaxValue && (allowZero ? minVal >= 0 : minVal > 0);
            bool isTemp = (unit == "°");
            int dispMin = (isTemp && hasMin) ? TelemetrySessionStats.ToDisplayTemp(minVal, UseFahrenheit) : minVal;
            int dispMax = (isTemp && hasMax) ? TelemetrySessionStats.ToDisplayTemp(maxVal, UseFahrenheit) : maxVal;
            double dispAvg = (isTemp && avgVal > 0) ? TelemetrySessionStats.ToDisplayTemp(avgVal, UseFahrenheit) : avgVal;
            if (PeakTrackerMode == 1)
                return hasMin ? string.Format("▼{0}{1}", dispMin, unit) : null;
            if (PeakTrackerMode == 2)
            {
                if (hasMin && hasMax) return string.Format("▼{0}▲{1}{2}", dispMin, dispMax, unit);
                if (hasMax) return string.Format("▲{0}{1}", dispMax, unit);
                if (hasMin) return string.Format("▼{0}{1}", dispMin, unit);
                return null;
            }
            if (PeakTrackerMode == 3)
                return dispAvg > 0 ? string.Format("~{0}{1}", (int)Math.Round(dispAvg), unit) : null;
            return hasMax ? string.Format("▲{0}{1}", dispMax, unit) : null;
        }

        private string FormatPairTracker(
            bool showFirst, int min1, int max1, double avg1, string unit1,
            bool showSecond, int min2, int max2, double avg2, string unit2,
            bool allowZeroSecond = true)
        {
            if (!ShowPeaks || Stats == null) return null;
            if (PeakTrackerMode == 2)
            {
                string t1 = showFirst ? FormatSingleTracker(min1, max1, avg1, unit1) : null;
                if (t1 != null) return t1;
                return showSecond ? FormatSingleTracker(min2, max2, avg2, unit2, allowZeroSecond) : null;
            }
            string p1 = showFirst ? FormatSingleTracker(min1, max1, avg1, unit1) : null;
            string p2 = showSecond ? FormatSingleTracker(min2, max2, avg2, unit2, allowZeroSecond) : null;
            if (p1 != null && p2 != null) return string.Format("{0} {1}", p1, p2);
            return p1 ?? p2;
        }

        private string FormatDoubleTracker(double minVal, double maxVal, double avgVal, string unit)
        {
            if (!ShowPeaks || Stats == null) return null;
            bool hasMax = maxVal > 0;
            bool hasMin = minVal < double.MaxValue && minVal > 0;
            if (PeakTrackerMode == 1)
                return hasMin ? string.Format("▼{0:0.0}{1}", minVal, unit) : null;
            if (PeakTrackerMode == 2)
            {
                if (hasMin && hasMax) return string.Format("▼{0:0.0}▲{1:0.0}{2}", minVal, maxVal, unit);
                if (hasMax) return string.Format("▲{0:0.0}{1}", maxVal, unit);
                if (hasMin) return string.Format("▼{0:0.0}{1}", minVal, unit);
                return null;
            }
            if (PeakTrackerMode == 3)
                return avgVal > 0 ? string.Format("~{0:0.0}{1}", avgVal, unit) : null;
            return hasMax ? string.Format("▲{0:0.0}{1}", maxVal, unit) : null;
        }

        private string FormatCpuOverlayValue()
        {
            int cpuT = TelemetrySessionStats.ToDisplayTemp(_status.CpuTemp, UseFahrenheit);
            string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
            bool hasLoad = ShowCpuLoad && _status.CpuLoadPct >= 0;
            bool hasClock = ShowClocks && _status.CpuClockMhz > 0;
            if (hasClock)
            {
                return hasLoad
                    ? string.Format("{0,2}{1} {2,3}% {3,4}MHz", cpuT, tUnit, _status.CpuLoadPct, _status.CpuClockMhz)
                    : string.Format("{0,2}{1} {2,4}MHz", cpuT, tUnit, _status.CpuClockMhz);
            }
            return hasLoad
                ? string.Format("{0,2}{1} {2,3}%", cpuT, tUnit, _status.CpuLoadPct)
                : string.Format("{0,2}{1}", cpuT, tUnit);
        }

        internal string FormatGpuOverlayValue()
        {
            bool hasGpuTemp = ShowGpu && _status.GpuTemp > 0;
            bool hasGpuLoad = ShowGpuLoad && (_status.GfxActivityPct >= 0);
            bool hasGpuClock = ShowGpuLoad && (_status.GfxClockMhz > 0);
            string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
            int gpuT = TelemetrySessionStats.ToDisplayTemp(_status.GpuTemp, UseFahrenheit);
            int hotT = TelemetrySessionStats.ToDisplayTemp(_status.GpuHotspotTemp, UseFahrenheit);

            string gpuTempPart = hasGpuTemp
                ? ((ShowGpuHotspot && _status.GpuHotspotTemp > 0) ? string.Format("{0,2}{1} (HS {2}°)", gpuT, tUnit, hotT) : string.Format("{0,2}{1}", gpuT, tUnit))
                : null;

            string gpuLoadPart = null;
            if (hasGpuLoad && ShowClocks && hasGpuClock)
                gpuLoadPart = string.Format("{0,3}% {1,4}MHz", _status.GfxActivityPct, _status.GfxClockMhz);
            else if (hasGpuLoad)
                gpuLoadPart = string.Format("{0,3}%", _status.GfxActivityPct);
            else if (ShowClocks && hasGpuClock)
                gpuLoadPart = string.Format("{0,4}MHz", _status.GfxClockMhz);

            if (gpuTempPart != null && gpuLoadPart != null)
                return string.Format("{0} {1}", gpuTempPart, gpuLoadPart);
            if (gpuTempPart != null)
                return gpuTempPart;
            if (gpuLoadPart != null)
                return gpuLoadPart;
            return "N/A";
        }

        private string FormatPowerOverlayValue()
        {
            if (_status.ApuPowerWatts <= 0) return "--W";
            return (ShowPowerLimit && _status.ApuPowerLimitWatts > 0)
                ? string.Format("{0}W/{1}W", _status.ApuPowerWatts, _status.ApuPowerLimitWatts)
                : string.Format("{0}W", _status.ApuPowerWatts);
        }

        private string FormatCpuOverlayPeak()
        {
            return FormatPairTracker(
                true, Stats != null ? Stats.MinCpuTemp : int.MaxValue, Stats != null ? Stats.MaxCpuTemp : 0, Stats != null ? Stats.AvgCpuTemp : 0.0, "°",
                ShowCpuLoad, Stats != null ? Stats.MinCpuLoad : int.MaxValue, Stats != null ? Stats.MaxCpuLoad : 0, Stats != null ? Stats.AvgCpuLoad : 0.0, "%", true);
        }

        private string FormatGpuOverlayPeak()
        {
            return FormatPairTracker(
                ShowGpu, Stats != null ? Stats.MinGpuTemp : int.MaxValue, Stats != null ? Stats.MaxGpuTemp : 0, Stats != null ? Stats.AvgGpuTemp : 0.0, "°",
                ShowGpuLoad, Stats != null ? Stats.MinGpuLoad : int.MaxValue, Stats != null ? Stats.MaxGpuLoad : 0, Stats != null ? Stats.AvgGpuLoad : 0.0, "%", true);
        }

        public string FormatRamOverlayPeak()
        {
            if (!ShowPeaks || Stats == null) return null;
            string pair = FormatPairTracker(
                ShowRamTemp, Stats.MinDdrTemp, Stats.MaxDdrTemp, Stats.AvgDdrTemp, "°",
                ShowRamLoad, Stats.MinRamLoad, Stats.MaxRamLoad, Stats.AvgRamLoad, "%", true);
            if (pair != null) return pair;
            if (ShowRamPower)
                return FormatDoubleTracker(Stats.MinRamPowerWatts, Stats.MaxRamPowerWatts, Stats.AvgRamPowerWatts, "W");
            return null;
        }

        private List<HorizColumn> BuildHorizontalColumns(
            Graphics g, float sc, bool showSpark,
            Brush bLabel, Brush bCpu, Brush bGpu, Brush bNvme, Brush bPwr, Brush bRam,
            Brush bFps, Brush bLow, Brush bLoad, Brush bApu, Brush bSys, Brush bWhite, Brush bPeak,
            Brush bFclk, Brush bVrm, Brush bNpu,
            Color cpuCol, Color gpuCol, Color pwrCol, Color loadCol, Color apuCol, Color sysCol)
        {
            List<HorizColumn> list = new List<HorizColumn>();

            string cpuVal = FormatCpuOverlayValue();
            bool showGpuTemp = ShowGpu;
            string gpuVal = FormatGpuOverlayValue();

            string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
            int nvmeT = TelemetrySessionStats.ToDisplayTemp(_status.NvmeTemp, UseFahrenheit);
            string nvmePwrPart = (ShowNvmePower && _status.NvmePowerWatts > 0) ? string.Format(" ~{0:0.0}W", _status.NvmePowerWatts) : "";
            string nvmeVal = string.Format("{0}{1}{2}", nvmeT, tUnit, nvmePwrPart);
            if (ShowNvmeIo && (_status.NvmeReadMBs > 0.1 || _status.NvmeWriteMBs > 0.1))
            {
                if (_status.NvmeReadMBs > 0.1 && _status.NvmeWriteMBs > 0.1)
                    nvmeVal = string.Format("{0}{1}{2} {3:0.0}R/{4:0.0}W", nvmeT, tUnit, nvmePwrPart, _status.NvmeReadMBs, _status.NvmeWriteMBs);
                else if (_status.NvmeReadMBs > 0.1)
                    nvmeVal = string.Format("{0}{1}{2} {3:0.0}R", nvmeT, tUnit, nvmePwrPart, _status.NvmeReadMBs);
                else
                    nvmeVal = string.Format("{0}{1}{2} {3:0.0}W", nvmeT, tUnit, nvmePwrPart, _status.NvmeWriteMBs);
            }
            else if (ShowNvmeDual && _status.Nvme2Temp > 0)
            {
                int nvme2T = TelemetrySessionStats.ToDisplayTemp(_status.Nvme2Temp, UseFahrenheit);
                nvmeVal = string.Format("{0}{1} / {2}{1}{3}", nvmeT, tUnit, nvme2T, nvmePwrPart);
            }
            string pwrVal = FormatPowerOverlayValue();
            string f1Val  = (IsEffectivelyReadOnly() && _status.Fan1Rpm <= 0) ? "--" : string.Format("{0}r", _status.Fan1Rpm);
            string f2Val  = (IsEffectivelyReadOnly() && _status.Fan2Rpm <= 0) ? "--" : string.Format("{0}r", _status.Fan2Rpm);

            string cpuPeak = FormatCpuOverlayPeak();
            string gpuPeak = FormatGpuOverlayPeak();
            string nvmePeak = FormatSingleTracker(Stats != null ? Stats.MinNvmeTemp : int.MaxValue, Stats != null ? Stats.MaxNvmeTemp : 0, Stats != null ? Stats.AvgNvmeTemp : 0.0, "°");
            string pwrPeak = FormatSingleTracker(Stats != null ? Stats.MinPowerWatts : int.MaxValue, Stats != null ? Stats.MaxPowerWatts : 0, Stats != null ? Stats.AvgPowerWatts : 0.0, "W");
            string ramPeak = FormatRamOverlayPeak();

            if (ShowGameName)
            {
                string gameTitle = FormatGameName(_status.RtssProcessName);
                if (!string.IsNullOrEmpty(gameTitle))
                {
                    list.Add(new HorizColumn {
                        Key = "GAME", Label = "GAME", Value = gameTitle, Peak = null,
                        LabelBrush = bLabel, ValueBrush = bWhite, PeakBrush = null,
                        SparkSamples = null, SparkColor = Color.Empty
                    });
                }
            }

            if (ShowCpu)
            {
                list.Add(new HorizColumn {
                    Key = "CPU", Label = "CPU", Value = cpuVal, Peak = cpuPeak,
                    LabelBrush = bLabel, ValueBrush = bCpu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.CpuHistory : null) : null,
                    SparkColor = cpuCol
                });
            }

            if (ShowGpu || ShowGpuLoad)
            {
                string gpuLbl = _status.GpuShortLabel;
                list.Add(new HorizColumn {
                    Key = "iGPU", Label = gpuLbl, Value = gpuVal, Peak = gpuPeak,
                    LabelBrush = bLabel, ValueBrush = bGpu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? (showGpuTemp ? Stats.GpuHistory : Stats.GpuLoadHistory) : null) : null,
                    SparkColor = gpuCol
                });
            }

            if (ShowRam && _status.RamTotalGb > 0)
            {
                string ramVal = FormatRamOverlayValue();
                list.Add(new HorizColumn {
                    Key = "RAM", Label = "RAM", Value = ramVal, Peak = ramPeak,
                    LabelBrush = bLabel, ValueBrush = bRam, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? (Stats.RamTempHistory.Count > 0 ? Stats.RamTempHistory : Stats.RamPowerHistory) : null) : null,
                    SparkColor = Color.FromArgb(0x81, 0x8C, 0xF8)
                });
            }

            if (ShowFabricClock && _status.FabricClockMhz > 0)
            {
                string fclkVal = string.Format("{0,4}MHz", _status.FabricClockMhz);
                string fclkPeak = ShowFclkSync ? (_status.IsFabricSynced ? "1:1 Sync" : "Async") : null;
                Color fclkCol = Color.FromArgb(0x10, 0xB9, 0x81);
                list.Add(new HorizColumn {
                    Key = "FCLK", Label = "FCLK", Value = fclkVal, Peak = fclkPeak,
                    LabelBrush = bLabel, ValueBrush = bFclk, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.FabricClockHistory : null) : null,
                    SparkColor = fclkCol
                });
            }

            if (ShowNvme && _status.NvmeTemp > 0)
            {
                list.Add(new HorizColumn {
                    Key = "NVMe", Label = "NVMe", Value = nvmeVal, Peak = nvmePeak,
                    LabelBrush = bLabel, ValueBrush = bNvme, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.NvmeHistory : null) : null,
                    SparkColor = CfxTeal
                });
            }

            if (ShowPower)
            {
                list.Add(new HorizColumn {
                    Key = "PWR", Label = "PWR", Value = pwrVal, Peak = pwrPeak,
                    LabelBrush = bLabel, ValueBrush = bPwr, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.PowerHistory : null) : null,
                    SparkColor = pwrCol
                });
            }

            if (ShowVrmCurrent && (_status.VddTdcCurrentAmps > 0 || _status.VrmTemp > 0))
            {
                string vrmEst = _status.IsVrmEstimated ? " [Est]" : "";
                string vrmTempPart = _status.VrmTemp > 0 ? string.Format(" ({0}{1}{2})", TelemetrySessionStats.ToDisplayTemp(_status.VrmTemp, UseFahrenheit), tUnit, vrmEst) : "";
                string vrmVal = _status.VddTdcCurrentAmps > 0
                    ? string.Format("{0:0}A{1}", _status.VddTdcCurrentAmps, vrmTempPart)
                    : string.Format("{0}{1}{2}", TelemetrySessionStats.ToDisplayTemp(_status.VrmTemp, UseFahrenheit), tUnit, vrmEst);
                string vrmPeak = null;
                if (ShowPeaks)
                {
                    if (Stats != null && Stats.MaxVrmTemp > 0)
                        vrmPeak = string.Format("▲{0}°", TelemetrySessionStats.ToDisplayTemp(Stats.MaxVrmTemp, UseFahrenheit));
                    else if (_status.VrmTemp > 0)
                        vrmPeak = string.Format("▲{0}°", TelemetrySessionStats.ToDisplayTemp(_status.VrmTemp, UseFahrenheit));
                    else if (_status.VddEdcCurrentAmps > 0)
                        vrmPeak = string.Format("▲{0:0}A", _status.VddEdcCurrentAmps);
                }
                Color vrmCol = Color.FromArgb(0xF5, 0x9E, 0x0B);
                list.Add(new HorizColumn {
                    Key = "VRM", Label = "VRM", Value = vrmVal, Peak = vrmPeak,
                    LabelBrush = bLabel, ValueBrush = bVrm, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.VrmHistory : null) : null,
                    SparkColor = vrmCol
                });
            }

            if (ShowAmbient && _status.AmbientTemp > 0)
            {
                string ambPeak = FormatSingleTracker(Stats != null ? Stats.MinAmbientTemp : int.MaxValue, Stats != null ? Stats.MaxAmbientTemp : 0, Stats != null ? Stats.AvgAmbientTemp : 0.0, "°");
                list.Add(new HorizColumn {
                    Key = "AMB", Label = "CHAS", Value = string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(_status.AmbientTemp, UseFahrenheit), TelemetrySessionStats.TempUnit(UseFahrenheit)), Peak = ambPeak,
                    LabelBrush = bLabel, ValueBrush = bWhite, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.AmbientHistory : null) : null,
                    SparkColor = Color.FromArgb(0xE2, 0xE8, 0xF0)
                });
            }

            if (ShowNpu && _status.NpuActivityPct >= 0)
            {
                string npuVal = (ShowClocks && _status.NpuClockMhz > 0)
                    ? string.Format("{0}% @ {1}M", _status.NpuActivityPct, _status.NpuClockMhz)
                    : string.Format("{0}%", _status.NpuActivityPct);
                string npuPeak = FormatSingleTracker(Stats != null ? Stats.MinNpuActivityPct : int.MaxValue, Stats != null ? Stats.MaxNpuActivityPct : 0, Stats != null ? Stats.AvgNpuActivityPct : 0.0, "%", true);
                Color npuCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                list.Add(new HorizColumn {
                    Key = "NPU", Label = "◈ NPU", Value = npuVal, Peak = npuPeak,
                    LabelBrush = bLabel, ValueBrush = bNpu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.NpuHistory : null) : null,
                    SparkColor = npuCol
                });
            }

            if (ShowFps)
            {
                bool hasFps = _status.RtssFps > 0;
                string lbl = "FPS";
                string val = hasFps ? string.Format("{0}", _status.RtssFps) : "--";
                int sessionAvg = ComputeDisplayAvgFps(_status.RtssFps);

                string peak = hasFps
                    ? (ShowFpsAvg && sessionAvg > 0 ? string.Format("Avg:{0}", sessionAvg) : (ShowPeaks && Stats != null && Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null))
                    : (_status.RtssRunning ? "RTSS Idle" : (ShowPeaks ? "Standby" : null));
                Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);

                list.Add(new HorizColumn {
                    Key = "FPS", Label = lbl, Value = val, Peak = peak,
                    LabelBrush = bLabel, ValueBrush = bFps, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.FpsHistory : null) : null,
                    SparkColor = fpsCol
                });
            }

            if (ShowLows)
            {
                bool hasFps = _status.RtssFps > 0;
                int low01 = _status.RtssP01LowFps > 0 ? _status.RtssP01LowFps : (Stats != null ? Stats.P01LowFps : 0);
                if (hasFps && _status.RtssFps >= 10 && low01 <= 2)
                {
                    if (Stats != null && Stats.P01LowFps > 2)
                        low01 = Stats.P01LowFps;
                    else
                        low01 = (int)Math.Max(1, Math.Round(_status.RtssFps * 0.78));
                }
                string lbl = "0.1%";
                string val = (hasFps && low01 > 0) ? string.Format("{0}", low01) : (low01 > 0 ? string.Format("{0}", low01) : "--");
                Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85);

                list.Add(new HorizColumn {
                    Key = "0.1%", Label = lbl, Value = val, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bLow, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.P01LowHistory : null) : null,
                    SparkColor = lowCol
                });
            }


            if (ShouldShowApuFanRow())
            {
                list.Add(new HorizColumn {
                    Key = "APU", Label = "APU", Value = f1Val, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bApu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null,
                    SparkColor = apuCol
                });
            }

            if (ShouldShowSysFanRow())
            {
                list.Add(new HorizColumn {
                    Key = "SYS", Label = "SYS", Value = f2Val, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bSys, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null,
                    SparkColor = sysCol
                });
            }

            if (ShouldShowModeRow())
            {
                string modeVal = !string.IsNullOrEmpty(_modeSummary) ? _modeSummary : (!string.IsNullOrEmpty(_status.Mode) ? _status.Mode : "Auto");
                list.Add(new HorizColumn {
                    Key = "MODE", Label = "MODE", Value = modeVal, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bWhite, PeakBrush = bPeak,
                    SparkSamples = null, SparkColor = Color.Empty
                });
            }

            if (ShowDriverVersion && !string.IsNullOrEmpty(_status.RadeonSoftwareVer))
            {
                list.Add(new HorizColumn {
                    Key = "DRV", Label = "DRV", Value = _status.RadeonSoftwareVer, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bWhite, PeakBrush = bPeak,
                    SparkSamples = null, SparkColor = Color.Empty
                });
            }

            if (ShowRadeonSettings)
            {
                List<string> radTags = (_status.RadeonFeaturesAvailable && _status.GetActiveRadeonTags(ShowRadeonExtendedTags) != null)
                    ? new List<string>(_status.GetActiveRadeonTags(ShowRadeonExtendedTags))
                    : new List<string>();

                if (!string.IsNullOrEmpty(ProcessorBadgeText) && !radTags.Contains(ProcessorBadgeText))
                {
                    radTags.Insert(0, ProcessorBadgeText);
                }

                if (radTags.Count > 0)
                {
                    list.Add(new HorizColumn {
                        Key = "RADEON", Label = "RADEON", Value = null, Peak = null,
                        LabelBrush = bLabel, ValueBrush = null, PeakBrush = null,
                        SparkSamples = null, SparkColor = Color.Empty,
                        RadeonTags = radTags
                    });
                }
            }

            // Dynamically calculate required minimum content width for each column
            foreach (var col in list)
            {
                if (col.Key == "RADEON")
                {
                    float hdrW = g.MeasureString("RADEON", _fontHdr).Width;
                    float tagsW = 0f;
                    int padH = (int)Math.Round(3 * sc);
                    if (col.RadeonTags != null && col.RadeonTags.Count > 0)
                    {
                        foreach (string t in col.RadeonTags)
                        {
                            SizeF sz = g.MeasureString(t, _fontTrace);
                            tagsW += (float)Math.Ceiling(sz.Width) + padH * 2 + (4f * sc);
                        }
                    }
                    float radW = Math.Max(hdrW, tagsW) + (12f * sc);
                    col.MinWidth = Math.Max(80f * sc, radW);
                }
                else if (col.Key == "MODE")
                {
                    float lblW = g.MeasureString("MODE", _fontLabel).Width;
                    float valW = g.MeasureString(col.Value, _fontVal).Width;
                    float modeW = lblW + valW + (10f * sc);
                    col.MinWidth = Math.Max(38f * sc, modeW);
                }
                else
                {
                    float lblW = g.MeasureString(col.Label, _fontLabel).Width;
                    float valW = g.MeasureString(col.Value, _fontVal).Width;
                    float pkW = !string.IsNullOrEmpty(col.Peak) ? g.MeasureString(col.Peak, _fontPeak).Width + (3f * sc) : 0f;
                    float topRowW = lblW + pkW + (8f * sc);
                    float valRowW = valW + (6f * sc);
                    float contentW = Math.Max(topRowW, valRowW);

                    if (showSpark && col.SparkSamples != null)
                    {
                        col.MinWidth = Math.Max(70f * sc, contentW);
                    }
                    else
                    {
                        col.MinWidth = Math.Max(42f * sc, contentW);
                    }
                }
            }

            return list;
        }

        private float MeasureHorizontalRequiredWidth(float sc, bool showSpark)
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                List<HorizColumn> cols = BuildHorizontalColumns(
                    g, sc, showSpark,
                    null, null, null, null, null, null, null, null, null, null, null, null, null,
                    null, null, null,
                    Color.Empty, Color.Empty, Color.Empty, Color.Empty, Color.Empty, Color.Empty);
                float total = 0f;
                foreach (var c in cols)
                {
                    total += c.MinWidth;
                }
                return total;
            }
        }

    }
}
