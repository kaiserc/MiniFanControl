using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MiniFanControl
{
    public class OsdOverlayConfig
    {
        public bool VerticalLayout = true;
        public int OverlayX = 24;
        public int OverlayY = 24;
        public int ScaleTier = 1; // 0 = 100%, 1 = 125%, 2 = 150%, 3 = 185%
        public int GraphMode = 3; // 0=None, 1=Sparklines, 2=Combined, 3=Both
        public int GraphHeight = 140; // 75=Compact, 105=Medium, 140=Large, 180=XL
        public int OpacityPct = 80; // 35..100
        public bool MatchTheme = true;

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowNvme = true;
        public bool ShowPower = true;
        public bool ShowRam = true;
        public bool ShowVrmCurrent = false;
        public bool ShowAmbient = true;
        public bool ShowFabricClock = true;
        public bool ShowNpu = true;
        public bool ShowFps = true;
        public bool ShowLows = true;
        public bool ShowGpuLoad = true;
        public bool ShowApuFan = true;
        public bool ShowSysFan = true;
        public bool ShowMode = true;
        public bool ShowPeaks = true;
        public int PeakTrackerMode = 0; // 0=Peaks, 1=Lows, 2=Min/Max, 3=Avg
        public bool ShowRadeon = true;
        public bool ShowRadeonExtTags = true;
        public bool ShowDriverVer = true;
        public bool ShowClocks = true;
        public bool ShowGameName = true;
        public bool SparklineBg = true;
        public bool ShowLiveTrend = true;
        public bool ShowFpsGraph = true;
        public bool PlotFpsOnTrend = true;
        public bool DfGraphStyle = true;
        public bool DfGraphFirst = true;

        public bool ShowRamTemp = true;
        public bool ShowRamDualSticks = false;
        public bool ShowRamSpread = true;
        public bool ShowRamLoad = true;
        public bool ShowRamPower = true;
        public bool ShowRamClock = false;
        public bool ShowNvmePower = true;
        public bool ShowNvmeIo = true;
        public bool ShowNvmeDual = true;
        public bool ShowCpuLoad = true;
        public bool ShowGpuHotspot = true;
        public bool ShowPowerLimit = true;
        public bool ShowFclkSync = true;
        public bool ShowFrametimeMs = true;
        public bool ShowFpsAvg = true;

        public void ResetDefaults(bool hasDualFans)
        {
            ShowCpu = true;
            ShowGpu = true;
            ShowNvme = true;
            ShowPower = true;
            ShowRam = true;
            ShowVrmCurrent = false;
            ShowAmbient = true;
            ShowFabricClock = true;
            ShowNpu = true;
            ShowFps = true;
            ShowLows = true;
            ShowGpuLoad = true;
            ShowApuFan = true;
            ShowSysFan = hasDualFans;
            ShowMode = true;
            ShowPeaks = true;
            PeakTrackerMode = 0;
            ShowRadeon = true;
            ShowRadeonExtTags = true;
            ShowDriverVer = true;
            ShowClocks = true;
            ShowGameName = true;
            SparklineBg = true;
            ShowLiveTrend = true;
            ShowFpsGraph = true;
            PlotFpsOnTrend = true;
            DfGraphStyle = true;
            DfGraphFirst = true;
            ShowRamTemp = true;
            ShowRamDualSticks = false;
            ShowRamSpread = true;
            ShowRamLoad = true;
            ShowRamPower = true;
            ShowRamClock = false;
            ShowNvmePower = true;
            ShowNvmeIo = true;
            ShowNvmeDual = true;
            ShowCpuLoad = true;
            ShowGpuHotspot = true;
            ShowPowerLimit = true;
            ShowFclkSync = true;
            ShowFrametimeMs = true;
            ShowFpsAvg = true;
            MatchTheme = true;
            ScaleTier = 1;
            GraphMode = 3;
            GraphHeight = 140;
            OpacityPct = 80;
            VerticalLayout = true;
        }

        public void SelectAllMetrics(bool hasDualFans)
        {
            ShowCpu = true;
            ShowGpu = true;
            ShowNvme = true;
            ShowPower = true;
            ShowRam = true;
            ShowVrmCurrent = true;
            ShowAmbient = true;
            ShowFabricClock = true;
            ShowNpu = true;
            ShowFps = true;
            ShowLows = true;
            ShowGpuLoad = true;
            ShowApuFan = true;
            if (hasDualFans) ShowSysFan = true;
            ShowMode = true;
            ShowPeaks = true;
            ShowRadeon = true;
            ShowClocks = false;
            ShowGameName = true;
            SparklineBg = false;
            ShowLiveTrend = true;
            ShowFpsGraph = true;
            PlotFpsOnTrend = true;
        }

        public void DeselectAllMetrics()
        {
            ShowCpu = false;
            ShowGpu = false;
            ShowNvme = false;
            ShowPower = false;
            ShowRam = false;
            ShowVrmCurrent = false;
            ShowAmbient = false;
            ShowFabricClock = false;
            ShowNpu = false;
            ShowFps = false;
            ShowLows = false;
            ShowGpuLoad = false;
            ShowApuFan = false;
            ShowSysFan = false;
            ShowMode = false;
            ShowPeaks = false;
            ShowRadeon = false;
            ShowGameName = false;
        }

        public void ApplyToOverlay(CapFrameXOverlayForm overlay, ThemePreference themePref, bool hasDualFans, bool useFahrenheit)
        {
            if (overlay == null) return;
            overlay.HasDualFans = hasDualFans;
            overlay.OsdScaleTier = ScaleTier;
            overlay.GraphMode = GraphMode;
            overlay.GraphHeight = GraphHeight;
            overlay.VerticalLayout = VerticalLayout;
            overlay.MatchAppTheme = MatchTheme;
            overlay.IsRadeonTheme = (themePref == ThemePreference.Radeon);
            overlay.IsAfterburnerTheme = (themePref == ThemePreference.Afterburner);
            overlay.ShowDriverVersion = ShowDriverVer;
            overlay.ShowClocks = ShowClocks;
            overlay.ShowGameName = ShowGameName;
            overlay.SparklinesAsBackground = SparklineBg;
            overlay.ShowCpu = ShowCpu;
            overlay.ShowGpu = ShowGpu;
            overlay.ShowNvme = ShowNvme;
            overlay.ShowPower = ShowPower;
            overlay.ShowRam = ShowRam;
            overlay.ShowVrmCurrent = ShowVrmCurrent;
            overlay.ShowAmbient = ShowAmbient;
            overlay.ShowFabricClock = ShowFabricClock;
            overlay.ShowNpu = ShowNpu;
            overlay.ShowFps = ShowFps;
            overlay.ShowLows = ShowLows;
            overlay.ShowGpuLoad = ShowGpuLoad;
            overlay.ShowApuFan = ShowApuFan;
            overlay.ShowSysFan = ShowSysFan;
            overlay.ShowMode = ShowMode;
            overlay.ShowPeaks = ShowPeaks;
            overlay.PeakTrackerMode = PeakTrackerMode;
            overlay.ShowRadeonSettings = ShowRadeon;
            overlay.ShowLiveTrend = ShowLiveTrend;
            overlay.ShowFpsGraph = ShowFpsGraph;
            overlay.PlotFpsOnTrend = PlotFpsOnTrend;
            overlay.DfGraphStyle = DfGraphStyle;
            overlay.DfGraphFirst = DfGraphFirst;
            overlay.ShowRamTemp = ShowRamTemp;
            overlay.ShowRamDualSticks = ShowRamDualSticks;
            overlay.ShowRamSpread = ShowRamSpread;
            overlay.ShowRamLoad = ShowRamLoad;
            overlay.ShowRamPower = ShowRamPower;
            overlay.ShowRamClock = ShowRamClock;
            overlay.ShowNvmePower = ShowNvmePower;
            overlay.ShowNvmeIo = ShowNvmeIo;
            overlay.ShowNvmeDual = ShowNvmeDual;
            overlay.ShowCpuLoad = ShowCpuLoad;
            overlay.ShowGpuHotspot = ShowGpuHotspot;
            overlay.ShowPowerLimit = ShowPowerLimit;
            overlay.ShowFclkSync = ShowFclkSync;
            overlay.ShowFrametimeMs = ShowFrametimeMs;
            overlay.ShowFpsAvg = ShowFpsAvg;
            overlay.ShowRadeonExtendedTags = ShowRadeonExtTags;
            overlay.UseFahrenheit = useFahrenheit;
            overlay.ApplyOpacity(OpacityPct);
        }

        public void ReadFromOverlay(CapFrameXOverlayForm overlay)
        {
            if (overlay == null) return;
            OverlayX = overlay.Location.X;
            OverlayY = overlay.Location.Y;
            ScaleTier = overlay.OsdScaleTier;
            GraphMode = overlay.GraphMode;
            GraphHeight = overlay.GraphHeight;
            OpacityPct = overlay.OpacityPct;
            VerticalLayout = overlay.VerticalLayout;
            ShowCpu = overlay.ShowCpu;
            ShowGpu = overlay.ShowGpu;
            ShowNvme = overlay.ShowNvme;
            ShowPower = overlay.ShowPower;
            ShowRam = overlay.ShowRam;
            ShowVrmCurrent = overlay.ShowVrmCurrent;
            ShowAmbient = overlay.ShowAmbient;
            ShowFabricClock = overlay.ShowFabricClock;
            ShowNpu = overlay.ShowNpu;
            ShowFps = overlay.ShowFps;
            ShowLows = overlay.ShowLows;
            ShowGpuLoad = overlay.ShowGpuLoad;
            ShowApuFan = overlay.ShowApuFan;
            ShowSysFan = overlay.ShowSysFan;
            ShowMode = overlay.ShowMode;
            ShowPeaks = overlay.ShowPeaks;
            PeakTrackerMode = overlay.PeakTrackerMode;
            ShowRadeon = overlay.ShowRadeonSettings;
            ShowRadeonExtTags = overlay.ShowRadeonExtendedTags;
            ShowDriverVer = overlay.ShowDriverVersion;
            ShowClocks = overlay.ShowClocks;
            ShowGameName = overlay.ShowGameName;
            SparklineBg = overlay.SparklinesAsBackground;
            ShowLiveTrend = overlay.ShowLiveTrend;
            ShowFpsGraph = overlay.ShowFpsGraph;
            PlotFpsOnTrend = overlay.PlotFpsOnTrend;
            DfGraphStyle = overlay.DfGraphStyle;
            DfGraphFirst = overlay.DfGraphFirst;
            ShowRamTemp = overlay.ShowRamTemp;
            ShowRamDualSticks = overlay.ShowRamDualSticks;
            ShowRamSpread = overlay.ShowRamSpread;
            ShowRamLoad = overlay.ShowRamLoad;
            ShowRamPower = overlay.ShowRamPower;
            ShowRamClock = overlay.ShowRamClock;
            ShowNvmePower = overlay.ShowNvmePower;
            ShowNvmeIo = overlay.ShowNvmeIo;
            ShowNvmeDual = overlay.ShowNvmeDual;
            ShowCpuLoad = overlay.ShowCpuLoad;
            ShowGpuHotspot = overlay.ShowGpuHotspot;
            ShowPowerLimit = overlay.ShowPowerLimit;
            ShowFclkSync = overlay.ShowFclkSync;
            ShowFrametimeMs = overlay.ShowFrametimeMs;
            ShowFpsAvg = overlay.ShowFpsAvg;
        }

        private static bool ParseBool(string v)
        {
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryParseSetting(string k, string v)
        {
            int intVal;
            if (k == "OverlayVertical") { VerticalLayout = ParseBool(v); return true; }
            if (k == "OverlayX" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { OverlayX = Math.Max(0, Math.Min(3800, intVal)); return true; }
            if (k == "OverlayY" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { OverlayY = Math.Max(0, Math.Min(2100, intVal)); return true; }
            if (k == "OsdScaleTier" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { ScaleTier = Math.Max(0, Math.Min(3, intVal)); return true; }
            if (k == "OsdGraphMode" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { GraphMode = Math.Max(0, Math.Min(3, intVal)); return true; }
            if (k == "OsdGraphHeight" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal))
            {
                if (intVal <= 85) GraphHeight = 75;
                else if (intVal <= 120) GraphHeight = 105;
                else if (intVal <= 160) GraphHeight = 140;
                else GraphHeight = 180;
                return true;
            }
            if (k == "OsdOpacityPct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { OpacityPct = Math.Max(35, Math.Min(100, intVal)); return true; }
            if (k == "OsdMatchTheme") { MatchTheme = ParseBool(v); return true; }
            if (k == "OsdShowCpu") { ShowCpu = ParseBool(v); return true; }
            if (k == "OsdShowGpu") { ShowGpu = ParseBool(v); return true; }
            if (k == "OsdShowNvme") { ShowNvme = ParseBool(v); return true; }
            if (k == "OsdShowPower") { ShowPower = ParseBool(v); return true; }
            if (k == "OsdShowRam") { ShowRam = ParseBool(v); return true; }
            if (k == "OsdShowVrmCurrent") { ShowVrmCurrent = ParseBool(v); return true; }
            if (k == "OsdShowAmbient") { ShowAmbient = ParseBool(v); return true; }
            if (k == "OsdShowFabricClock") { ShowFabricClock = ParseBool(v); return true; }
            if (k == "OsdShowNpu") { ShowNpu = ParseBool(v); return true; }
            if (k == "OsdShowFps") { ShowFps = ParseBool(v); return true; }
            if (k == "OsdShowLows") { ShowLows = ParseBool(v); return true; }
            if (k == "OsdShowGpuLoad") { ShowGpuLoad = ParseBool(v); return true; }
            if (k == "OsdShowApuFan") { ShowApuFan = ParseBool(v); return true; }
            if (k == "OsdShowSysFan") { ShowSysFan = ParseBool(v); return true; }
            if (k == "OsdShowMode") { ShowMode = ParseBool(v); return true; }
            if (k == "OsdShowPeaks") { ShowPeaks = ParseBool(v); return true; }
            if (k == "OsdPeakTrackerMode" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) { PeakTrackerMode = Math.Max(0, Math.Min(3, intVal)); return true; }
            if (k == "OsdShowRadeon") { ShowRadeon = ParseBool(v); return true; }
            if (k == "OsdShowDriverVer") { ShowDriverVer = ParseBool(v); return true; }
            if (k == "OsdShowClocks") { ShowClocks = ParseBool(v); return true; }
            if (k == "OsdShowGameName") { ShowGameName = ParseBool(v); return true; }
            if (k == "OsdSparklineBg") { SparklineBg = ParseBool(v); return true; }
            if (k == "OsdShowLiveTrend") { ShowLiveTrend = ParseBool(v); return true; }
            if (k == "OsdShowFpsGraph") { ShowFpsGraph = ParseBool(v); return true; }
            if (k == "OsdPlotFpsOnTrend") { PlotFpsOnTrend = ParseBool(v); return true; }
            if (k == "OsdDfGraphStyle") { DfGraphStyle = ParseBool(v); return true; }
            if (k == "OsdDfGraphFirst") { DfGraphFirst = ParseBool(v); return true; }
            if (k == "OsdShowRamTemp") { ShowRamTemp = ParseBool(v); return true; }
            if (k == "OsdShowRamDualSticks") { ShowRamDualSticks = ParseBool(v); return true; }
            if (k == "OsdShowRamSpread") { ShowRamSpread = ParseBool(v); return true; }
            if (k == "OsdShowRamLoad") { ShowRamLoad = ParseBool(v); return true; }
            if (k == "OsdShowRamPower") { ShowRamPower = ParseBool(v); return true; }
            if (k == "OsdShowRamClock") { ShowRamClock = ParseBool(v); return true; }
            if (k == "OsdShowNvmePower") { ShowNvmePower = ParseBool(v); return true; }
            if (k == "OsdShowNvmeIo") { ShowNvmeIo = ParseBool(v); return true; }
            if (k == "OsdShowNvmeDual") { ShowNvmeDual = ParseBool(v); return true; }
            if (k == "OsdShowCpuLoad") { ShowCpuLoad = ParseBool(v); return true; }
            if (k == "OsdShowGpuHotspot") { ShowGpuHotspot = ParseBool(v); return true; }
            if (k == "OsdShowPowerLimit") { ShowPowerLimit = ParseBool(v); return true; }
            if (k == "OsdShowFclkSync") { ShowFclkSync = ParseBool(v); return true; }
            if (k == "OsdShowFrametimeMs") { ShowFrametimeMs = ParseBool(v); return true; }
            if (k == "OsdShowFpsAvg") { ShowFpsAvg = ParseBool(v); return true; }
            if (k == "OsdShowRadeonExtTags") { ShowRadeonExtTags = ParseBool(v); return true; }
            return false;
        }

        public void AppendSettings(List<string> lines)
        {
            lines.Add("OverlayVertical=" + (VerticalLayout ? "1" : "0"));
            lines.Add("OverlayX=" + OverlayX.ToString(CultureInfo.InvariantCulture));
            lines.Add("OverlayY=" + OverlayY.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdScaleTier=" + ScaleTier.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdGraphMode=" + GraphMode.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdGraphHeight=" + GraphHeight.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdOpacityPct=" + OpacityPct.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdMatchTheme=" + (MatchTheme ? "1" : "0"));
            lines.Add("OsdShowCpu=" + (ShowCpu ? "1" : "0"));
            lines.Add("OsdShowGpu=" + (ShowGpu ? "1" : "0"));
            lines.Add("OsdShowNvme=" + (ShowNvme ? "1" : "0"));
            lines.Add("OsdShowPower=" + (ShowPower ? "1" : "0"));
            lines.Add("OsdShowRam=" + (ShowRam ? "1" : "0"));
            lines.Add("OsdShowVrmCurrent=" + (ShowVrmCurrent ? "1" : "0"));
            lines.Add("OsdShowAmbient=" + (ShowAmbient ? "1" : "0"));
            lines.Add("OsdShowFabricClock=" + (ShowFabricClock ? "1" : "0"));
            lines.Add("OsdShowNpu=" + (ShowNpu ? "1" : "0"));
            lines.Add("OsdShowFps=" + (ShowFps ? "1" : "0"));
            lines.Add("OsdShowLows=" + (ShowLows ? "1" : "0"));
            lines.Add("OsdShowGpuLoad=" + (ShowGpuLoad ? "1" : "0"));
            lines.Add("OsdShowApuFan=" + (ShowApuFan ? "1" : "0"));
            lines.Add("OsdShowSysFan=" + (ShowSysFan ? "1" : "0"));
            lines.Add("OsdShowMode=" + (ShowMode ? "1" : "0"));
            lines.Add("OsdShowPeaks=" + (ShowPeaks ? "1" : "0"));
            lines.Add("OsdPeakTrackerMode=" + PeakTrackerMode.ToString(CultureInfo.InvariantCulture));
            lines.Add("OsdShowRadeon=" + (ShowRadeon ? "1" : "0"));
            lines.Add("OsdShowDriverVer=" + (ShowDriverVer ? "1" : "0"));
            lines.Add("OsdShowClocks=" + (ShowClocks ? "1" : "0"));
            lines.Add("OsdShowGameName=" + (ShowGameName ? "1" : "0"));
            lines.Add("OsdSparklineBg=" + (SparklineBg ? "1" : "0"));
            lines.Add("OsdShowLiveTrend=" + (ShowLiveTrend ? "1" : "0"));
            lines.Add("OsdShowFpsGraph=" + (ShowFpsGraph ? "1" : "0"));
            lines.Add("OsdPlotFpsOnTrend=" + (PlotFpsOnTrend ? "1" : "0"));
            lines.Add("OsdDfGraphStyle=" + (DfGraphStyle ? "1" : "0"));
            lines.Add("OsdDfGraphFirst=" + (DfGraphFirst ? "1" : "0"));
            lines.Add("OsdShowRamTemp=" + (ShowRamTemp ? "1" : "0"));
            lines.Add("OsdShowRamDualSticks=" + (ShowRamDualSticks ? "1" : "0"));
            lines.Add("OsdShowRamSpread=" + (ShowRamSpread ? "1" : "0"));
            lines.Add("OsdShowRamLoad=" + (ShowRamLoad ? "1" : "0"));
            lines.Add("OsdShowRamPower=" + (ShowRamPower ? "1" : "0"));
            lines.Add("OsdShowRamClock=" + (ShowRamClock ? "1" : "0"));
            lines.Add("OsdShowNvmePower=" + (ShowNvmePower ? "1" : "0"));
            lines.Add("OsdShowNvmeIo=" + (ShowNvmeIo ? "1" : "0"));
            lines.Add("OsdShowNvmeDual=" + (ShowNvmeDual ? "1" : "0"));
            lines.Add("OsdShowCpuLoad=" + (ShowCpuLoad ? "1" : "0"));
            lines.Add("OsdShowGpuHotspot=" + (ShowGpuHotspot ? "1" : "0"));
            lines.Add("OsdShowPowerLimit=" + (ShowPowerLimit ? "1" : "0"));
            lines.Add("OsdShowFclkSync=" + (ShowFclkSync ? "1" : "0"));
            lines.Add("OsdShowFrametimeMs=" + (ShowFrametimeMs ? "1" : "0"));
            lines.Add("OsdShowFpsAvg=" + (ShowFpsAvg ? "1" : "0"));
            lines.Add("OsdShowRadeonExtTags=" + (ShowRadeonExtTags ? "1" : "0"));
        }
    }
}
