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
        private ContextMenuStrip BuildOverlayMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Configure OSD Overlay Studio...", null, (s, e) =>
            {
                if (RequestOpenOsdConfig != null) RequestOpenOsdConfig(this, EventArgs.Empty);
            });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem scaleMenu = new ToolStripMenuItem("OSD Scale & Size");
            scaleMenu.DropDownItems.Add("100% Standard", null, (s, e) => SetScaleTierFromMenu(0));
            scaleMenu.DropDownItems.Add("125% Large", null, (s, e) => SetScaleTierFromMenu(1));
            scaleMenu.DropDownItems.Add("150% Extra Large", null, (s, e) => SetScaleTierFromMenu(2));
            scaleMenu.DropDownItems.Add("185% Jumbo (4K / TV)", null, (s, e) => SetScaleTierFromMenu(3));
            menu.Items.Add(scaleMenu);

            ToolStripMenuItem graphMenu = new ToolStripMenuItem("OSD Live Graphs");
            graphMenu.DropDownItems.Add("Both (Mini Sparklines + Combined Graph)", null, (s, e) => SetGraphModeFromMenu(3));
            graphMenu.DropDownItems.Add("Combined Live Graph Only", null, (s, e) => SetGraphModeFromMenu(2));
            graphMenu.DropDownItems.Add("Mini Sparklines per Metric Only", null, (s, e) => SetGraphModeFromMenu(1));
            graphMenu.DropDownItems.Add("Text Only (No Graphs)", null, (s, e) => SetGraphModeFromMenu(0));
            graphMenu.DropDownItems.Add("Graphs Only (No Metric Rows)", null, (s, e) => SetGraphModeFromMenu(4));
            graphMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem heightMenu = new ToolStripMenuItem("Graph Height");
            heightMenu.DropDownItems.Add("75px Compact", null, (s, e) => SetGraphHeightFromMenu(75));
            heightMenu.DropDownItems.Add("105px Medium", null, (s, e) => SetGraphHeightFromMenu(105));
            heightMenu.DropDownItems.Add("140px Large", null, (s, e) => SetGraphHeightFromMenu(140));
            heightMenu.DropDownItems.Add("180px Extra Large", null, (s, e) => SetGraphHeightFromMenu(180));
            graphMenu.DropDownItems.Add(heightMenu);
            graphMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMetricToggleMenuItem(graphMenu, "Live Trend Graph (Multi-Sensor)", () => ShowLiveTrend, v => ShowLiveTrend = v);
            ToolStripMenuItem trendSeriesMenu = new ToolStripMenuItem("Live Trend Series (Show / Hide)");
            AddMetricToggleMenuItem(trendSeriesMenu, "CPU Temp", () => TrendCpu, v => TrendCpu = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "iGPU Temp", () => TrendGpu, v => TrendGpu = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "RAM Temp / Power", () => TrendRam, v => TrendRam = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "NVMe Temp", () => TrendNvme, v => TrendNvme = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "APU Power (W)", () => TrendPower, v => TrendPower = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "Chassis Ambient", () => TrendAmbient, v => TrendAmbient = v);
            AddMetricToggleMenuItem(trendSeriesMenu, "APU Fan RPM", () => TrendApuFan, v => TrendApuFan = v);
            if (HasDualFans)
                AddMetricToggleMenuItem(trendSeriesMenu, "Sys Fan RPM", () => TrendSysFan, v => TrendSysFan = v);
            trendSeriesMenu.DropDownItems.Add(new ToolStripSeparator());
            trendSeriesMenu.DropDownItems.Add("Show All Trends", null, (s, e) =>
            {
                TrendCpu = TrendGpu = TrendRam = TrendNvme = TrendPower = TrendAmbient = TrendApuFan = true;
                TrendSysFan = HasDualFans;
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            trendSeriesMenu.DropDownItems.Add("Temps Only (CPU / iGPU / RAM / NVMe / Chas)", null, (s, e) =>
            {
                TrendCpu = TrendGpu = TrendRam = TrendNvme = TrendAmbient = true;
                TrendPower = TrendApuFan = TrendSysFan = false;
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            trendSeriesMenu.DropDownItems.Add("Power & Fans Only (PWR / APU / SYS)", null, (s, e) =>
            {
                TrendCpu = TrendGpu = TrendRam = TrendNvme = TrendAmbient = false;
                TrendPower = TrendApuFan = true;
                TrendSysFan = HasDualFans;
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            graphMenu.DropDownItems.Add(trendSeriesMenu);
            AddMetricToggleMenuItem(graphMenu, "FPS & Frametime Graph", () => ShowFpsGraph, v => ShowFpsGraph = v);
            AddMetricToggleMenuItem(graphMenu, "Plot FPS on Live Trend", () => PlotFpsOnTrend, v => PlotFpsOnTrend = v);
            AddMetricToggleMenuItem(graphMenu, "Digital Foundry (DF) Style Graphs", () => DfGraphStyle, v => DfGraphStyle = v);
            AddMetricToggleMenuItem(graphMenu, "DF Corner Inset (Frame-Time in FPS Corner)", () => DfCornerInset, v => { DfCornerInset = v; if (v) DfGraphStyle = true; });
            AddMetricToggleMenuItem(graphMenu, "Show DF Graphs Before Live Trend", () => DfGraphFirst, v => DfGraphFirst = v);
            AddMetricToggleMenuItem(graphMenu, "Background Sparklines (Underlay)", () => SparklinesAsBackground, v => { SparklinesAsBackground = v; Invalidate(); });
            menu.Items.Add(graphMenu);

            ToolStripMenuItem samplingMenu = new ToolStripMenuItem("Sampling Rate");
            int[] sampleRates = new int[] { 100, 250, 500, 1000, 1500, 2000, 3000 };
            string[] sampleLabels = new string[]
            {
                "100 ms (Extreme • 10 Hz)",
                "250 ms (Ultra • 4 Hz)",
                "500 ms (Fast • 2 Hz)",
                "1000 ms (Standard • 1.0s)",
                "1500 ms (Balanced • 1.5s)",
                "2000 ms (Relaxed • 2.0s)",
                "3000 ms (Low CPU • 3.0s)"
            };
            ToolStripMenuItem[] sampleItems = new ToolStripMenuItem[sampleRates.Length];
            for (int i = 0; i < sampleRates.Length; i++)
            {
                int msVal = sampleRates[i];
                ToolStripMenuItem item = new ToolStripMenuItem(sampleLabels[i]) { Checked = (SampleIntervalMs == msVal) };
                item.Click += (s, e) =>
                {
                    SampleIntervalMs = msVal;
                    if (RequestSamplingInterval != null) RequestSamplingInterval(this, msVal);
                    Invalidate();
                };
                sampleItems[i] = item;
                samplingMenu.DropDownItems.Add(item);
            }
            samplingMenu.DropDownOpening += (s, e) =>
            {
                for (int i = 0; i < sampleRates.Length; i++)
                    sampleItems[i].Checked = (SampleIntervalMs == sampleRates[i]);
            };
            menu.Items.Add(samplingMenu);

            ToolStripMenuItem metricsMenu = new ToolStripMenuItem("Visible Telemetry Metrics");
            AddMetricToggleMenuItem(metricsMenu, "Active Game Title", () => ShowGameName, v => { ShowGameName = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "CPU Temperature", () => ShowCpu, v => ShowCpu = v);
            AddMetricToggleMenuItem(metricsMenu, "iGPU Temperature", () => ShowGpu, v => ShowGpu = v);
            AddMetricToggleMenuItem(metricsMenu, "Clock Speeds (@ GHz / MHz)", () => ShowClocks, v => { ShowClocks = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "NVMe SSD Temp", () => ShowNvme, v => ShowNvme = v);
            AddMetricToggleMenuItem(metricsMenu, "APU Power (W)", () => ShowPower, v => ShowPower = v);
            AddMetricToggleMenuItem(metricsMenu, "RAM Load & Power", () => ShowRam, v => ShowRam = v);
            AddMetricToggleMenuItem(metricsMenu, "VRM Current (TDC/EDC)", () => ShowVrmCurrent, v => { ShowVrmCurrent = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "Chassis / Ambient Temp", () => ShowAmbient, v => { ShowAmbient = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "Infinity Fabric (FCLK)", () => ShowFabricClock, v => { ShowFabricClock = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "Ryzen AI NPU", () => ShowNpu, v => { ShowNpu = v; UpdateDimensions(); Invalidate(); });
            ToolStripItem npuMenuItem = metricsMenu.DropDownItems[metricsMenu.DropDownItems.Count - 1];
            metricsMenu.DropDownOpening += (s, e) =>
            {
                npuMenuItem.Visible = (_status.NpuActivityPct >= 0 || AmdAdlGpuSensor.HasNpuHardware);
            };
            AddMetricToggleMenuItem(metricsMenu, "FPS & Frametime", () => ShowFps, v => ShowFps = v);
            AddMetricToggleMenuItem(metricsMenu, "0.1% Low & Avg FPS", () => ShowLows, v => ShowLows = v);
            AddMetricToggleMenuItem(metricsMenu, "GPU Load & Clock", () => ShowGpuLoad, v => ShowGpuLoad = v);
            AddMetricToggleMenuItem(metricsMenu, "APU Fan Speed", () => ShowApuFan, v => ShowApuFan = v);
            if (HasDualFans)
                AddMetricToggleMenuItem(metricsMenu, "System Fan Speed", () => ShowSysFan, v => ShowSysFan = v);
            AddMetricToggleMenuItem(metricsMenu, "Thermal Mode", () => ShowMode, v => ShowMode = v);
            AddMetricToggleMenuItem(metricsMenu, "Peak / Low Trackers", () => ShowPeaks, v => ShowPeaks = v);
            ToolStripMenuItem trackerModeMenu = new ToolStripMenuItem("Tracker Mode (Peaks / Lows / Avg)");
            Action<int> setTrackerMode = m => { PeakTrackerMode = m; UpdateDimensions(); Invalidate(); if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty); };
            ToolStripMenuItem tmPeaks = new ToolStripMenuItem("Peaks (▲)") { Checked = (PeakTrackerMode == 0) };
            tmPeaks.Click += (s, e) => setTrackerMode(0);
            ToolStripMenuItem tmLows = new ToolStripMenuItem("Lows (▼)") { Checked = (PeakTrackerMode == 1) };
            tmLows.Click += (s, e) => setTrackerMode(1);
            ToolStripMenuItem tmMinMax = new ToolStripMenuItem("Min/Max (▼▲)") { Checked = (PeakTrackerMode == 2) };
            tmMinMax.Click += (s, e) => setTrackerMode(2);
            ToolStripMenuItem tmAvg = new ToolStripMenuItem("Session Avg (~)") { Checked = (PeakTrackerMode == 3) };
            tmAvg.Click += (s, e) => setTrackerMode(3);
            trackerModeMenu.DropDownItems.AddRange(new ToolStripItem[] { tmPeaks, tmLows, tmMinMax, tmAvg });
            metricsMenu.DropDownItems.Add(trackerModeMenu);
            AddMetricToggleMenuItem(metricsMenu, "Radeon Driver Version", () => ShowDriverVersion, v => ShowDriverVersion = v);
            AddMetricToggleMenuItem(metricsMenu, "Radeon Features (Tags: FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)", () => ShowRadeonSettings, v => ShowRadeonSettings = v);
            AddMetricToggleMenuItem(metricsMenu, "Extended Radeon Tags (SAM, EnhSync, FRTC)", () => ShowRadeonExtendedTags, v => ShowRadeonExtendedTags = v);
            menu.Items.Add(metricsMenu);

            ToolStripMenuItem subFieldsMenu = new ToolStripMenuItem("RAM & Sensor Sub-Fields");
            subFieldsMenu.DropDownItems.Add("Preset: Balanced Default (35/41°C 22% ~W)", null, (s, e) =>
            {
                ShowRamTemp = true; ShowRamDualSticks = true; ShowRamSpread = false;
                ShowRamLoad = true; ShowRamPower = true; ShowRamClock = false;
                UpdateDimensions(); Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            subFieldsMenu.DropDownItems.Add("Preset: Compact Single-Temp (41°C 22% ~W)", null, (s, e) =>
            {
                ShowRamTemp = true; ShowRamDualSticks = false; ShowRamSpread = false;
                ShowRamLoad = true; ShowRamPower = true; ShowRamClock = false;
                UpdateDimensions(); Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            subFieldsMenu.DropDownItems.Add("Preset: Minimal Temp & Load (41°C 22%)", null, (s, e) =>
            {
                ShowRamTemp = true; ShowRamDualSticks = false; ShowRamSpread = false;
                ShowRamLoad = true; ShowRamPower = false; ShowRamClock = false;
                UpdateDimensions(); Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            subFieldsMenu.DropDownItems.Add("Preset: Full Detailed (35°/41°C Δ6° 22% @ MHz ~W)", null, (s, e) =>
            {
                ShowRamTemp = true; ShowRamDualSticks = true; ShowRamSpread = true;
                ShowRamLoad = true; ShowRamPower = true; ShowRamClock = true;
                UpdateDimensions(); Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            subFieldsMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Temperature (°C)", () => ShowRamTemp, v => ShowRamTemp = v);
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Dual-Stick Split (35/41°C vs 41°C)", () => ShowRamDualSticks, v => ShowRamDualSticks = v);
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Spread Delta (Δ°C)", () => ShowRamSpread, v => ShowRamSpread = v);
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Load (%)", () => ShowRamLoad, v => ShowRamLoad = v);
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Power Draw (~W)", () => ShowRamPower, v => ShowRamPower = v);
            AddMetricToggleMenuItem(subFieldsMenu, "RAM Clock Speed (@ MHz)", () => ShowRamClock, v => ShowRamClock = v);
            subFieldsMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMetricToggleMenuItem(subFieldsMenu, "NVMe Power Draw (~W)", () => ShowNvmePower, v => ShowNvmePower = v);
            AddMetricToggleMenuItem(subFieldsMenu, "NVMe I/O Throughput (MB/s)", () => ShowNvmeIo, v => ShowNvmeIo = v);
            AddMetricToggleMenuItem(subFieldsMenu, "NVMe Dual-Drive Split (°C/°C)", () => ShowNvmeDual, v => ShowNvmeDual = v);
            subFieldsMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMetricToggleMenuItem(subFieldsMenu, "CPU Load (%)", () => ShowCpuLoad, v => ShowCpuLoad = v);
            AddMetricToggleMenuItem(subFieldsMenu, "iGPU Hotspot (🔥°C)", () => ShowGpuHotspot, v => ShowGpuHotspot = v);
            AddMetricToggleMenuItem(subFieldsMenu, "APU Power Limit (/W)", () => ShowPowerLimit, v => ShowPowerLimit = v);
            AddMetricToggleMenuItem(subFieldsMenu, "FCLK Sync Status (1:1)", () => ShowFclkSync, v => ShowFclkSync = v);
            AddMetricToggleMenuItem(subFieldsMenu, "FPS Frametime (ms)", () => ShowFrametimeMs, v => ShowFrametimeMs = v);
            AddMetricToggleMenuItem(subFieldsMenu, "FPS Session Avg", () => ShowFpsAvg, v => ShowFpsAvg = v);
            AddMetricToggleMenuItem(subFieldsMenu, "Display Latency (ETW ms)", () => ShowLatency, v => ShowLatency = v);
            menu.Items.Add(subFieldsMenu);

            ToolStripMenuItem themeMenu = new ToolStripMenuItem("OSD Theme Palette");
            themeMenu.DropDownItems.Add("Match Application Theme", null, (s, e) => { MatchAppTheme = true; Invalidate(); });
            themeMenu.DropDownItems.Add("Classic MiniFanControl Dark (Cyan/Amber)", null, (s, e) => { MatchAppTheme = false; IsRadeonTheme = false; IsAfterburnerTheme = false; Invalidate(); });
            themeMenu.DropDownItems.Add("AMD Radeon Ruby Red", null, (s, e) => { MatchAppTheme = false; IsRadeonTheme = true; IsAfterburnerTheme = false; Invalidate(); });
            themeMenu.DropDownItems.Add("MSI Afterburner Toxic Green", null, (s, e) => { MatchAppTheme = false; IsRadeonTheme = false; IsAfterburnerTheme = true; Invalidate(); });
            menu.Items.Add(themeMenu);

            ToolStripMenuItem snapMenu = new ToolStripMenuItem("Snap Location");
            snapMenu.DropDownItems.Add("Top-Left", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 0); });
            snapMenu.DropDownItems.Add("Center-Top", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 4); });
            snapMenu.DropDownItems.Add("Top-Right", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 1); });
            snapMenu.DropDownItems.Add("Bottom-Left", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 2); });
            snapMenu.DropDownItems.Add("Center-Bottom", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 5); });
            snapMenu.DropDownItems.Add("Bottom-Right", null, (s, e) => { if (RequestSnapLocation != null) RequestSnapLocation(this, 3); });
            menu.Items.Add(snapMenu);

            menu.Items.Add("Switch Layout (Horizontal Bar / Vertical Stack)", null, (s, e) =>
            {
                VerticalLayout = !VerticalLayout;
                UpdateDimensions();
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            menu.Items.Add("Lock OSD (Click-Through Mode)", null, (s, e) =>
            {
                if (RequestToggleLock != null) RequestToggleLock(this, EventArgs.Empty);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide OSD Overlay", null, (s, e) =>
            {
                if (RequestHideOverlay != null) RequestHideOverlay(this, EventArgs.Empty);
            });
            return menu;
        }

        private void SetScaleTierFromMenu(int tier)
        {
            OsdScaleTier = Math.Max(0, Math.Min(3, tier));
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        private void SetGraphHeightFromMenu(int h)
        {
            GraphHeight = h;
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        private void SetGraphModeFromMenu(int mode)
        {
            GraphMode = mode;
            ShowLiveTrend = (mode == 2 || mode == 3);
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        private void AddMetricToggleMenuItem(ToolStripMenuItem parent, string label, Func<bool> getter, Action<bool> setter)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Checked = getter();
            item.Click += (s, e) =>
            {
                bool next = !getter();
                setter(next);
                item.Checked = next;
                UpdateDimensions();
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            };
            parent.DropDownItems.Add(item);
        }

    }
}
