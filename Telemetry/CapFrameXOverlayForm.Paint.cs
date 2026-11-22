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
        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                float sc = _effectiveScale > 0 ? _effectiveScale : GetScaleMultiplier();
                int scaledGraphH = _effectiveGraphHeight > 0 ? _effectiveGraphHeight : (int)Math.Round(GraphHeight * sc);
                bool showSpark = (GraphMode == 1 || GraphMode == 3);
                int rowH = _effectiveRowHeight > 0 ? _effectiveRowHeight : (int)Math.Round((showSpark ? 25 : 21) * sc);

                bool radeon = (MatchAppTheme && IsRadeonTheme) || (!MatchAppTheme && IsRadeonTheme);
                bool afterburner = (MatchAppTheme && IsAfterburnerTheme) || (!MatchAppTheme && IsAfterburnerTheme);
                Color bg = afterburner ? OsdBgAfterburner : (radeon ? OsdBgRadeon : OsdBgDark);
                Color border = afterburner ? OsdBorderAfterburner : (radeon ? OsdBorderRadeon : OsdBorderDark);
                Color accentCol = afterburner ? AbGreen : (radeon ? CfxRuby : CfxAmber);
                Color cpuCol = afterburner ? AbGreen : (radeon ? CfxRuby : CfxCyan);
                Color gpuCol = afterburner ? AbCyan : (radeon ? CfxCyan : CfxGreen);
                Color pwrCol = afterburner ? AbAmber : CfxYellow;
                Color apuCol = afterburner ? AbGreen : CfxViolet;
                Color sysCol = afterburner ? AbCyan : CfxTeal;

            g.Clear(bg);

            // Left accent bar
            using (Brush accentBrush = new SolidBrush(accentCol))
            {
                g.FillRectangle(accentBrush, 0, 0, Math.Max(3, (int)Math.Round(3 * sc)), Height);
            }

            bool showCombined = ShowLiveTrend;

            string cpuVal = FormatCpuOverlayValue();
            bool vertShowGpuTemp = ShowGpu;
            string gpuVal = FormatGpuOverlayValue();

            string pwrVal = FormatPowerOverlayValue();
            string f1Val  = (IsEffectivelyReadOnly() && _status.Fan1Rpm <= 0) ? "--" : string.Format("{0}", _status.Fan1Rpm);
            string f2Val  = _status.Fan2Rpm > 0 ? string.Format("{0}", _status.Fan2Rpm) : "--";

            string cpuPeak = FormatCpuOverlayPeak();
            string gpuPeak = FormatGpuOverlayPeak();
            string nvmePeak = FormatSingleTracker(Stats != null ? Stats.MinNvmeTemp : int.MaxValue, Stats != null ? Stats.MaxNvmeTemp : 0, Stats != null ? Stats.AvgNvmeTemp : 0.0, "°");
            string pwrPeak = FormatSingleTracker(Stats != null ? Stats.MinPowerWatts : int.MaxValue, Stats != null ? Stats.MaxPowerWatts : 0, Stats != null ? Stats.AvgPowerWatts : 0.0, "W");
            string ramPeak = FormatRamOverlayPeak();

            using (Brush bLabel  = new SolidBrush(accentCol))
            using (Brush bCpu    = new SolidBrush(cpuCol))
            using (Brush bGpu    = new SolidBrush(gpuCol))
            using (Brush bPwr    = new SolidBrush(pwrCol))
            using (Brush bApu    = new SolidBrush(apuCol))
            using (Brush bSys    = new SolidBrush(sysCol))
            using (Brush bNvme   = new SolidBrush(CfxTeal))
            using (Brush bRam    = new SolidBrush(Color.FromArgb(0x81, 0x8C, 0xF8)))
            using (Brush bWhite  = new SolidBrush(CfxWhite))
            using (Brush bPeak   = new SolidBrush(Color.FromArgb(170, 203, 213, 225)))
            using (Pen sepPen    = new Pen(border, 1f))
            {
                if (!VerticalLayout)
                {
                    bool graphsOnly = (GraphMode == 4);
                    Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                    Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85);
                    Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);

                    if (!graphsOnly)
                    {
                    using (Brush bFps = new SolidBrush(fpsCol))
                    using (Brush bLow = new SolidBrush(lowCol))
                    using (Brush bLoad = new SolidBrush(loadCol))
                    {
                        List<HorizColumn> horizCols = BuildHorizontalColumns(
                            g, sc, showSpark,
                            bLabel, bCpu, bGpu, bNvme, bPwr, bRam, bFps, bLow, bLoad, bApu, bSys, bWhite, bPeak,
                            cpuCol, gpuCol, pwrCol, loadCol, apuCol, sysCol);

                        float totalMinW = 0f;
                        for (int i = 0; i < horizCols.Count; i++)
                        {
                            totalMinW += horizCols[i].MinWidth;
                        }

                        float availW = Math.Max(10f, (float)Width - (16f * sc));
                        float surplus = Math.Max(0f, availW - totalMinW);

                        float curX = 8f * sc;
                        for (int i = 0; i < horizCols.Count; i++)
                        {
                            HorizColumn col = horizCols[i];
                            float wAlloc = col.MinWidth;
                            if (totalMinW > 0f)
                            {
                                if (availW < totalMinW)
                                {
                                    wAlloc = Math.Max(30f * sc, col.MinWidth * (availW / totalMinW));
                                }
                                else if (surplus > 0f)
                                {
                                    wAlloc += surplus * (col.MinWidth / totalMinW);
                                }
                            }
                            col.AllocatedWidth = wAlloc;
                            col.StartX = curX;
                            curX += wAlloc;
                        }

                        for (int i = 0; i < horizCols.Count; i++)
                        {
                            HorizColumn col = horizCols[i];
                            bool drawSep = (i > 0);
                            if (col.Key == "RADEON")
                            {
                                DrawHorizontalRadeonTagsCell(g, col.StartX, col.AllocatedWidth, drawSep, col.RadeonTags, col.LabelBrush, sepPen, sc);
                            }
                            else
                            {
                                DrawHorizCell(g, col.StartX, col.AllocatedWidth, drawSep, col.Label, col.Value, col.Peak, col.LabelBrush, col.ValueBrush, col.PeakBrush, sepPen, col.SparkSamples, col.SparkColor, sc);
                            }
                        }
                    }
                    }

                    int curY = graphsOnly ? (int)Math.Round(6 * sc) : ((CountVisibleMetrics() > 0) ? (int)Math.Round((showSpark ? 58 : 42) * sc) : (int)Math.Round(24 * sc));

                    Action drawHorizTrend = () =>
                    {
                        if (showCombined)
                        {
                            Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), scaledGraphH);
                            _combRect = combRect;
                            DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                            curY += scaledGraphH + (int)Math.Round(6 * sc);
                        }
                        else
                        {
                            _combRect = Rectangle.Empty;
                        }
                    };

                    Action drawHorizFps = () =>
                    {
                        if (ShowFpsGraph)
                        {
                            if (DfGraphStyle && DfCornerInset)
                            {
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(22 * sc);
                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), dfFpsH);
                                _dfFpsRect = fpsRect;
                                DrawDfFramerateGraph(g, fpsRect, sc);

                                int insetW = Math.Max((int)Math.Round(130 * sc), Math.Min((int)Math.Round(240 * sc), (int)(fpsRect.Width * 0.24)));
                                int availH = Math.Max((int)Math.Round(36 * sc), fpsRect.Height - (int)Math.Round(20 * sc));
                                int insetH = Math.Min(availH, Math.Max((int)Math.Round(46 * sc), (int)(fpsRect.Height * (fpsRect.Height < 100 * sc ? 0.68 : 0.52))));
                                Rectangle insetRect = new Rectangle(fpsRect.X + (int)Math.Round(6 * sc), fpsRect.Y + (int)Math.Round(16 * sc), insetW, insetH);
                                _dfFtRect = insetRect;
                                DrawDfFrametimeGraph(g, insetRect, sc, true);

                                curY += dfFpsH + (int)Math.Round(6 * sc);
                                _fpsRect = Rectangle.Empty;
                            }
                            else if (DfGraphStyle)
                            {
                                int dfFtH = GetDfFrametimeHeight(sc, scaledGraphH);
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH);
                                Rectangle ftRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), dfFtH);
                                _dfFtRect = ftRect;
                                DrawDfFrametimeGraph(g, ftRect, sc, false);
                                curY += dfFtH + (int)Math.Round(6 * sc);

                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), dfFpsH);
                                _dfFpsRect = fpsRect;
                                DrawDfFramerateGraph(g, fpsRect, sc);
                                curY += dfFpsH + (int)Math.Round(6 * sc);
                                _fpsRect = Rectangle.Empty;
                            }
                            else
                            {
                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), scaledGraphH);
                                _fpsRect = fpsRect;
                                _dfFtRect = Rectangle.Empty;
                                _dfFpsRect = Rectangle.Empty;
                                DrawFpsOsdGraph(g, fpsRect, border, sc);
                                curY += scaledGraphH + (int)Math.Round(6 * sc);
                            }
                        }
                        else
                        {
                            _fpsRect = Rectangle.Empty;
                            _dfFtRect = Rectangle.Empty;
                            _dfFpsRect = Rectangle.Empty;
                        }
                    };

                    if (DfGraphFirst)
                    {
                        drawHorizFps();
                        drawHorizTrend();
                    }
                    else
                    {
                        drawHorizTrend();
                        drawHorizFps();
                    }
                }
                else
                {
                    bool graphsOnly = (GraphMode == 4);
                    int row = 0;
                    if (!graphsOnly)
                    {
                    string gameTitle = ShowGameName ? FormatGameName(_status.RtssProcessName) : null;
                    string headerText = !string.IsNullOrEmpty(gameTitle)
                        ? "GAME: " + gameTitle
                        : (afterburner ? "AFTERBURNER OSD" : (radeon ? "RADEON TELEMETRY OSD" : "MINIFANCONTROL OSD"));
                    g.DrawString(headerText, _fontHdr, bLabel, 10f * sc, 6f * sc);
                    g.DrawLine(sepPen, (int)Math.Round(10 * sc), (int)Math.Round(24 * sc), Width - (int)Math.Round(10 * sc), (int)Math.Round(24 * sc));
                    if (ShowCpu)
                        DrawVertRow(g, row++, rowH, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu || ShowGpuLoad)
                        DrawVertRow(g, row++, rowH, _status.GpuShortLabel, gpuVal, gpuPeak, bLabel, bGpu, bPeak, showSpark ? (Stats != null ? (vertShowGpuTemp ? Stats.GpuHistory : Stats.GpuLoadHistory) : null) : null, gpuCol, sc);
                    if (ShowRam && _status.RamTotalGb > 0)
                    {
                        string ramVal = FormatRamOverlayValue();
                        DrawVertRow(g, row++, rowH, "RAM", ramVal, ramPeak, bLabel, bRam, bPeak, showSpark ? (Stats != null ? (Stats.RamTempHistory.Count > 0 ? Stats.RamTempHistory : Stats.RamPowerHistory) : null) : null, Color.FromArgb(0x81, 0x8C, 0xF8), sc);
                    }
                    if (ShowFabricClock && _status.FabricClockMhz > 0)
                    {
                        string fclkVal = string.Format("{0,4}MHz", _status.FabricClockMhz);
                        string fclkPeak = ShowFclkSync ? (_status.IsFabricSynced ? "1:1 Sync" : "Async") : null;
                        Color fclkCol = Color.FromArgb(0x10, 0xB9, 0x81);
                        using (Brush bFclk = new SolidBrush(fclkCol))
                        {
                            DrawVertRow(g, row++, rowH, "FCLK", fclkVal, fclkPeak, bLabel, bFclk, bPeak, showSpark ? (Stats != null ? Stats.FabricClockHistory : null) : null, fclkCol, sc);
                        }
                    }
                    if (ShowNvme && _status.NvmeTemp > 0)
                    {
                        string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
                        int nvmeT = TelemetrySessionStats.ToDisplayTemp(_status.NvmeTemp, UseFahrenheit);
                        string nvmePwrPart = (ShowNvmePower && _status.NvmePowerWatts > 0) ? string.Format(" ~{0:0.0}W", _status.NvmePowerWatts) : "";
                        string vertNvmeVal = string.Format("{0}{1}{2}", nvmeT, tUnit, nvmePwrPart);
                        if (ShowNvmeIo && (_status.NvmeReadMBs > 0.1 || _status.NvmeWriteMBs > 0.1))
                        {
                            if (_status.NvmeReadMBs > 0.1 && _status.NvmeWriteMBs > 0.1)
                                vertNvmeVal = string.Format("{0}{1}{2} ({3:0.0}R / {4:0.0}W MB/s)", nvmeT, tUnit, nvmePwrPart, _status.NvmeReadMBs, _status.NvmeWriteMBs);
                            else if (_status.NvmeReadMBs > 0.1)
                                vertNvmeVal = string.Format("{0}{1}{2} ({3:0.0} MB/s R)", nvmeT, tUnit, nvmePwrPart, _status.NvmeReadMBs);
                            else
                                vertNvmeVal = string.Format("{0}{1}{2} ({3:0.0} MB/s W)", nvmeT, tUnit, nvmePwrPart, _status.NvmeWriteMBs);
                        }
                        else if (ShowNvmeDual && _status.Nvme2Temp > 0)
                        {
                            int nvme2T = TelemetrySessionStats.ToDisplayTemp(_status.Nvme2Temp, UseFahrenheit);
                            vertNvmeVal = string.Format("{0}{1} / {2}{1}{3}", nvmeT, tUnit, nvme2T, nvmePwrPart);
                        }
                        DrawVertRow(g, row++, rowH, "NVMe", vertNvmeVal, nvmePeak, bLabel, bNvme, bPeak, showSpark ? (Stats != null ? Stats.NvmeHistory : null) : null, CfxTeal, sc);
                    }
                    if (ShowPower)
                        DrawVertRow(g, row++, rowH, "Power", pwrVal, pwrPeak, bLabel, bPwr, bPeak, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowVrmCurrent && (_status.VddTdcCurrentAmps > 0 || _status.VrmTemp > 0))
                    {
                        string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
                        string vrmEst = _status.IsVrmEstimated ? " [Est]" : "";
                        string vrmTempPart = _status.VrmTemp > 0 ? string.Format(" ({0}{1}{2})", TelemetrySessionStats.ToDisplayTemp(_status.VrmTemp, UseFahrenheit), tUnit, vrmEst) : "";
                        string vrmVal = _status.VddTdcCurrentAmps > 0
                            ? string.Format("{0:0.0}A{1}", _status.VddTdcCurrentAmps, vrmTempPart)
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
                        using (Brush bVrm = new SolidBrush(vrmCol))
                        {
                            DrawVertRow(g, row++, rowH, "VRM", vrmVal, vrmPeak, bLabel, bVrm, bPeak, showSpark ? (Stats != null ? Stats.VrmHistory : null) : null, vrmCol, sc);
                        }
                    }
                    if (ShowAmbient && _status.AmbientTemp > 0)
                    {
                        string ambPeak = FormatSingleTracker(Stats != null ? Stats.MinAmbientTemp : int.MaxValue, Stats != null ? Stats.MaxAmbientTemp : 0, Stats != null ? Stats.AvgAmbientTemp : 0.0, "°");
                        DrawVertRow(g, row++, rowH, "Chassis", string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(_status.AmbientTemp, UseFahrenheit), TelemetrySessionStats.TempUnit(UseFahrenheit)), ambPeak, bLabel, bWhite, bPeak, showSpark ? (Stats != null ? Stats.AmbientHistory : null) : null, Color.FromArgb(0xE2, 0xE8, 0xF0), sc);
                    }
                    if (ShowNpu && _status.NpuActivityPct >= 0)
                    {
                        string npuVal = (ShowClocks && _status.NpuClockMhz > 0)
                            ? string.Format("{0}% @ {1} MHz", _status.NpuActivityPct, _status.NpuClockMhz)
                            : string.Format("{0}%", _status.NpuActivityPct);
                        string npuPeak = FormatSingleTracker(Stats != null ? Stats.MinNpuActivityPct : int.MaxValue, Stats != null ? Stats.MaxNpuActivityPct : 0, Stats != null ? Stats.AvgNpuActivityPct : 0.0, "%", true);
                        Color npuCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                        using (Brush bNpu = new SolidBrush(npuCol))
                        {
                            DrawVertRow(g, row++, rowH, "◈ NPU", npuVal, npuPeak, bLabel, bNpu, bPeak, showSpark ? (Stats != null ? Stats.NpuHistory : null) : null, npuCol, sc);
                        }
                    }
                    if (ShowFps)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = "FPS";
                        double ft = _status.RtssFrametimeMs > 0 ? _status.RtssFrametimeMs : (hasFps ? (1000.0 / _status.RtssFps) : 0.0);
                        string val;
                        if (hasFps)
                        {
                            val = (ShowFrametimeMs && ft > 0) ? string.Format("{0} ({1:0.0}ms)", _status.RtssFps, ft) : string.Format("{0} FPS", _status.RtssFps);
                        }
                        else
                        {
                            val = "-- (Standby)";
                        }

                        int sessionAvg = ComputeDisplayAvgFps(_status.RtssFps);

                        string peak = hasFps
                            ? (ShowFpsAvg && sessionAvg > 0 ? string.Format("Avg: {0}", sessionAvg) : (ShowPeaks && Stats != null && Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null))
                            : (_status.RtssRunning ? "RTSS Idle" : (ShowPeaks ? "Standby" : null));

                        Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                        using (Brush bFps = new SolidBrush(fpsCol))
                        {
                            DrawVertRow(g, row++, rowH, lbl, val, peak, bLabel, bFps, bPeak, showSpark ? (Stats != null ? Stats.FpsHistory : null) : null, fpsCol, sc);
                        }
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
                        string lbl = "0.1% Low";
                        string val;
                        if (low01 > 0)
                        {
                            val = string.Format("{0} FPS", low01);
                        }
                        else
                        {
                            val = "-- (Standby)";
                        }
                        string peak = null;
                        Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85);
                        using (Brush bLow = new SolidBrush(lowCol))
                        {
                            DrawVertRow(g, row++, rowH, lbl, val, peak, bLabel, bLow, bPeak, showSpark ? (Stats != null ? Stats.P01LowHistory : null) : null, lowCol, sc);
                        }
                    }
                    if (ShouldShowApuFanRow())
                        DrawVertRow(g, row++, rowH, "APU Fan", f1Val + " RPM", null, bLabel, bApu, bPeak, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (ShouldShowSysFanRow())
                        DrawVertRow(g, row++, rowH, "Sys Fan", f2Val + " RPM", null, bLabel, bSys, bPeak, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShouldShowModeRow())
                        DrawVertRow(g, row++, rowH, "Mode", _modeSummary, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);
                    if (ShowDriverVersion && !string.IsNullOrEmpty(_status.RadeonSoftwareVer))
                        DrawVertRow(g, row++, rowH, "Driver", _status.RadeonSoftwareVer, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);
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
                            DrawRadeonTagsRow(g, row++, rowH, radTags, sc);
                        }
                    }
                    }

                    int curY = graphsOnly
                        ? (int)Math.Round(6 * sc)
                        : ((int)Math.Round(24 * sc) + (row > 0 ? row * rowH + (int)Math.Round(4 * sc) : (int)Math.Round(4 * sc)));

                    Action drawTrend = () =>
                    {
                        if (showCombined)
                        {
                            Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), scaledGraphH);
                            _combRect = combRect;
                            DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                            curY += scaledGraphH + (int)Math.Round(6 * sc);
                        }
                        else
                        {
                            _combRect = Rectangle.Empty;
                        }
                    };

                    Action drawFps = () =>
                    {
                        if (ShowFpsGraph)
                        {
                            if (DfGraphStyle && DfCornerInset)
                            {
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(22 * sc);
                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), dfFpsH);
                                _dfFpsRect = fpsRect;
                                DrawDfFramerateGraph(g, fpsRect, sc);

                                int insetW = Math.Max((int)Math.Round(112 * sc), Math.Min((int)Math.Round(195 * sc), (int)(fpsRect.Width * 0.44)));
                                int availH = Math.Max((int)Math.Round(36 * sc), fpsRect.Height - (int)Math.Round(20 * sc));
                                int insetH = Math.Min(availH, Math.Max((int)Math.Round(46 * sc), (int)(fpsRect.Height * (fpsRect.Height < 100 * sc ? 0.68 : 0.52))));
                                Rectangle insetRect = new Rectangle(fpsRect.X + (int)Math.Round(6 * sc), fpsRect.Y + (int)Math.Round(16 * sc), insetW, insetH);
                                _dfFtRect = insetRect;
                                DrawDfFrametimeGraph(g, insetRect, sc, true);

                                curY += dfFpsH + (int)Math.Round(6 * sc);
                                _fpsRect = Rectangle.Empty;
                            }
                            else if (DfGraphStyle)
                            {
                                int dfFtH = GetDfFrametimeHeight(sc, scaledGraphH);
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH);
                                Rectangle ftRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), dfFtH);
                                _dfFtRect = ftRect;
                                DrawDfFrametimeGraph(g, ftRect, sc, false);
                                curY += dfFtH + (int)Math.Round(6 * sc);

                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), dfFpsH);
                                _dfFpsRect = fpsRect;
                                DrawDfFramerateGraph(g, fpsRect, sc);
                                curY += dfFpsH + (int)Math.Round(6 * sc);
                                _fpsRect = Rectangle.Empty;
                            }
                            else
                            {
                                Rectangle fpsRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), scaledGraphH);
                                _fpsRect = fpsRect;
                                _dfFtRect = Rectangle.Empty;
                                _dfFpsRect = Rectangle.Empty;
                                DrawFpsOsdGraph(g, fpsRect, border, sc);
                                curY += scaledGraphH + (int)Math.Round(6 * sc);
                            }
                        }
                        else
                        {
                            _fpsRect = Rectangle.Empty;
                            _dfFtRect = Rectangle.Empty;
                            _dfFpsRect = Rectangle.Empty;
                        }
                    };

                    if (DfGraphFirst)
                    {
                        drawFps();
                        drawTrend();
                    }
                    else
                    {
                        drawTrend();
                        drawFps();
                    }
                }
            }

            using (Pen borderPen = new Pen(border, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
        catch (Exception)
        {
            try
            {
                e.Graphics.Clear(Color.FromArgb(200, 15, 23, 42));
                using (Pen p = new Pen(Color.FromArgb(239, 68, 68), 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
            catch { }
        }
    }

        private void DrawHorizCell(Graphics g, float cellX, float colW, bool drawSep, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, Pen sepPen, List<int> sparkSamples, Color sparkCol, float sc)
        {
            if (drawSep && sepPen != null)
            {
                g.DrawLine(sepPen, cellX - 3f * sc, 4f * sc, cellX - 3f * sc, (sparkSamples != null ? (54f * sc) : (38f * sc)));
            }

            // Line 1: Label on left, Peak on right
            g.DrawString(lbl, _fontLabel, bLbl, cellX, 4f * sc);
            float pkW = 0f;
            if (!string.IsNullOrEmpty(peak))
            {
                float rawPkW = g.MeasureString(peak, _fontPeak).Width;
                pkW = rawPkW + (3f * sc);
                float peakX = cellX + colW - rawPkW - (3f * sc);
                g.DrawString(peak, _fontPeak, bPeak, peakX, 4f * sc);
            }

            // Line 2: Value across the full cell width
            float availValW = Math.Max(10f, colW - (4f * sc));
            float valW = g.MeasureString(val, _fontVal).Width;

            string drawVal = val;
            Font drawFont = _fontVal;
            bool disposeFont = false;

            if (valW > availValW)
            {
                // 1. If string contains " @ ", try compact notation without "@" (e.g. "45°C 3.8G" or "74% 2800M")
                if (drawVal.Contains(" @ "))
                {
                    string cand1 = drawVal.Replace(" @ ", " ");
                    float cand1W = g.MeasureString(cand1, _fontVal).Width;
                    if (cand1W <= availValW)
                    {
                        drawVal = cand1;
                        valW = cand1W;
                    }
                    else
                    {
                        string cand2 = cand1.Replace("°C", "°");
                        float cand2W = g.MeasureString(cand2, _fontVal).Width;
                        if (cand2W <= availValW)
                        {
                            drawVal = cand2;
                            valW = cand2W;
                        }
                        else
                        {
                            drawVal = cand1;
                            valW = cand1W;
                        }
                    }
                }

                // 2. If still slightly too wide, dynamically scale the font down to fit with 100% fidelity
                if (valW > availValW && availValW > 16f * sc)
                {
                    float scaleRatio = availValW / valW;
                    if (scaleRatio >= 0.75f)
                    {
                        drawFont = new Font(_fontVal.FontFamily, _fontVal.Size * scaleRatio, _fontVal.Style);
                        disposeFont = true;
                        valW = g.MeasureString(drawVal, drawFont).Width;
                    }
                }
            }

            try
            {
                if (valW > availValW && availValW > 12f)
                {
                    RectangleF valRect = new RectangleF(cellX, 17f * sc, availValW, 16f * sc);
                    using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    {
                        g.DrawString(drawVal, drawFont, bVal, valRect, sf);
                    }
                }
                else
                {
                    g.DrawString(drawVal, drawFont, bVal, cellX, 17f * sc);
                }
            }
            finally
            {
                if (disposeFont && drawFont != null)
                {
                    drawFont.Dispose();
                }
            }

            // Line 3: Sparkline (if enabled)
            if (sparkSamples != null)
            {
                int sparkY = (int)Math.Round(35f * sc);
                int sparkH = (int)Math.Round(18f * sc);
                int sparkW = Math.Max((int)Math.Round(16 * sc), (int)colW - (int)Math.Round(6 * sc));
                Rectangle sparkRect = new Rectangle((int)cellX, sparkY, sparkW, sparkH);
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }
        }

        private void DrawVertRow(Graphics g, int rowIndex, int rowH, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float y = (27f * sc) + rowIndex * rowH;

            float rightMargin = 14f * sc;
            float lblW = g.MeasureString(lbl, _fontLabel).Width;
            float minValX = (10f * sc) + lblW + (6f * sc);

            float fixedPeakColW = ShowPeaks ? ((PeakTrackerMode == 2 ? 84f : 72f) * sc) : 0f;
            float peakLeft = Width - rightMargin - fixedPeakColW;
            float valRight = ShowPeaks ? (peakLeft - (4f * sc)) : (Width - rightMargin);

            float valW = g.MeasureString(val, _fontVal).Width;
            float targetValRight = valRight;

            // If this row has no peak and its text is wide, allow it to use the empty peak margin
            if (string.IsNullOrEmpty(peak) && valW > (valRight - minValX))
            {
                targetValRight = Width - rightMargin;
            }

            float valX = targetValRight - valW;
            if (valX < minValX)
                valX = minValX;

            if (sparkSamples != null)
            {
                int sparkLeft = (int)Math.Round((10f * sc) + lblW + (8f * sc));
                int sparkW = Math.Max(0, (int)valX - sparkLeft - (int)Math.Round(6 * sc));

                if (SparklinesAsBackground)
                {
                    int bgLeft = sparkLeft;
                    int bgRight = Width - (int)Math.Round(rightMargin);
                    int bgW = Math.Max(0, bgRight - bgLeft);
                    if (bgW >= (int)Math.Round(16 * sc))
                    {
                        Rectangle bgRect = new Rectangle(bgLeft, (int)y, bgW, Math.Max(4, rowH - 4));
                        DrawBackgroundSparkline(g, bgRect, sparkSamples, sparkCol, sc);
                    }
                }
                else if (sparkW >= (int)Math.Round(18 * sc))
                {
                    Rectangle sparkRect = new Rectangle(sparkLeft, (int)y + 1, sparkW, (int)Math.Round(18f * sc));
                    DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
                }
            }

            g.DrawString(lbl, _fontLabel, bLbl, 10f * sc, y);
            if (!string.IsNullOrEmpty(peak))
            {
                g.DrawString(peak, _fontPeak, bPeak, peakLeft + (2f * sc), y + 1f);
            }

            float availW = targetValRight - valX;
            string drawVal = val;
            Font drawFont = _fontVal;
            bool disposeFont = false;

            if (valW > availW)
            {
                if (drawVal.Contains(" @ "))
                {
                    string cand1 = drawVal.Replace(" @ ", " ");
                    float cand1W = g.MeasureString(cand1, _fontVal).Width;
                    if (cand1W <= availW)
                    {
                        drawVal = cand1;
                        valW = cand1W;
                    }
                    else
                    {
                        string cand2 = cand1.Replace("°C", "°");
                        float cand2W = g.MeasureString(cand2, _fontVal).Width;
                        if (cand2W <= availW)
                        {
                            drawVal = cand2;
                            valW = cand2W;
                        }
                        else
                        {
                            drawVal = cand1;
                            valW = cand1W;
                        }
                    }
                }

                if (valW > availW && availW > 16f * sc)
                {
                    float scaleRatio = availW / valW;
                    if (scaleRatio >= 0.75f)
                    {
                        drawFont = new Font(_fontVal.FontFamily, _fontVal.Size * scaleRatio, _fontVal.Style);
                        disposeFont = true;
                        valW = g.MeasureString(drawVal, drawFont).Width;
                    }
                }
            }

            try
            {
                if (valW > availW && availW > 12f)
                {
                    RectangleF valRect = new RectangleF(valX, y - 1f, availW, rowH);
                    using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    {
                        g.DrawString(drawVal, drawFont, bVal, valRect, sf);
                    }
                }
                else
                {
                    g.DrawString(drawVal, drawFont, bVal, valX, y - 1f);
                }
            }
            finally
            {
                if (disposeFont && drawFont != null)
                {
                    drawFont.Dispose();
                }
            }
        }

        private static void DrawRoundedRectangle(Graphics g, Pen pen, Brush fill, Rectangle rect, int radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            int r = Math.Max(1, Math.Min(radius, Math.Min(rect.Width / 2, rect.Height / 2)));
            int d = r * 2;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                if (fill != null) g.FillPath(fill, path);
                if (pen != null) g.DrawPath(pen, path);
            }
        }

        private void DrawRadeonTagsRow(Graphics g, int rowIdx, int rowH, List<string> tags, float sc)
        {
            if (tags == null || tags.Count == 0) return;

            int y = (int)Math.Round(24 * sc) + rowIdx * rowH;
            int curX = (int)Math.Round(10 * sc);
            int pillH = (int)Math.Round(15 * sc);
            int padH = (int)Math.Round(5 * sc);
            int textYOffset = (int)Math.Round(1 * sc);

            bool radeon = MatchAppTheme && IsRadeonTheme;
            Color pillBg = IsAfterburnerTheme ? Color.FromArgb(170, 8, 40, 26) : (radeon ? Color.FromArgb(170, 50, 15, 25) : Color.FromArgb(160, 40, 18, 22));
            Color pillBorder = IsAfterburnerTheme ? Color.FromArgb(240, 0, 230, 118) : (radeon ? Color.FromArgb(240, 235, 45, 65) : Color.FromArgb(230, 220, 50, 60));
            Color pillText = Color.FromArgb(255, 255, 240, 242);

            using (SolidBrush bgBrush = new SolidBrush(pillBg))
            using (Pen borderPen = new Pen(pillBorder, 1f))
            using (SolidBrush textBrush = new SolidBrush(pillText))
            {
                foreach (string tag in tags)
                {
                    bool isRyzen = tag.StartsWith("Ryzen", StringComparison.OrdinalIgnoreCase);
                    Color curBg = isRyzen ? Color.FromArgb(190, 60, 24, 10) : pillBg;
                    Color curBorder = isRyzen ? Color.FromArgb(245, 238, 107, 36) : pillBorder;

                    SizeF sz = g.MeasureString(tag, _fontTrace);
                    int pillW = (int)Math.Ceiling(sz.Width) + padH * 2;
                    Rectangle pillRect = new Rectangle(curX, y + (rowH - pillH) / 2, pillW, pillH);

                    using (SolidBrush b = new SolidBrush(curBg))
                    using (Pen p = new Pen(curBorder, 1f))
                    {
                        DrawRoundedRectangle(g, p, b, pillRect, (int)Math.Round(3 * sc));
                    }
                    g.DrawString(tag, _fontTrace, textBrush, pillRect.X + padH, pillRect.Y + textYOffset);

                    curX += pillW + (int)Math.Round(4 * sc);
                }
            }
        }

        private void DrawHorizontalRadeonTagsCell(Graphics g, float cellX, float colW, bool drawSep, List<string> tags, Brush bLabel, Pen sepPen, float sc)
        {
            if (tags == null || tags.Count == 0) return;
            if (drawSep && sepPen != null)
            {
                g.DrawLine(sepPen, cellX - 3f * sc, 4f * sc, cellX - 3f * sc, (GraphMode == 1 || GraphMode == 3) ? (54f * sc) : (38f * sc));
            }

            int padH = (int)Math.Round(3 * sc);
            int pillH = (int)Math.Round(13 * sc);
            float curX = cellX;
            float curY = 17f * sc;

            g.DrawString("RADEON", _fontHdr, bLabel, cellX, 4f * sc);

            bool radeon = MatchAppTheme && IsRadeonTheme;
            Color pillBg = IsAfterburnerTheme ? Color.FromArgb(170, 8, 40, 26) : (radeon ? Color.FromArgb(170, 50, 15, 25) : Color.FromArgb(160, 40, 18, 22));
            Color pillBorder = IsAfterburnerTheme ? Color.FromArgb(240, 0, 230, 118) : (radeon ? Color.FromArgb(240, 235, 45, 65) : Color.FromArgb(230, 220, 50, 60));
            Color pillText = Color.FromArgb(255, 255, 240, 242);

            using (SolidBrush bgBrush = new SolidBrush(pillBg))
            using (Pen borderPen = new Pen(pillBorder, 1f))
            using (SolidBrush textBrush = new SolidBrush(pillText))
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i];
                    bool isRyzen = tag.StartsWith("Ryzen", StringComparison.OrdinalIgnoreCase);
                    Color curBg = isRyzen ? Color.FromArgb(190, 60, 24, 10) : pillBg;
                    Color curBorder = isRyzen ? Color.FromArgb(245, 238, 107, 36) : pillBorder;

                    SizeF sz = g.MeasureString(tag, _fontTrace);
                    int pillW = (int)Math.Ceiling(sz.Width) + padH * 2;
                    bool canWrap = (GraphMode == 1 || GraphMode == 3);
                    if (curX + pillW > cellX + colW - (2f * sc) && i > 0 && canWrap)
                    {
                        curX = cellX;
                        curY += pillH + (2f * sc);
                    }
                    if (curX + pillW <= cellX + colW + (4f * sc))
                    {
                        Rectangle pillRect = new Rectangle((int)curX, (int)curY, pillW, pillH);
                        using (SolidBrush b = new SolidBrush(curBg))
                        using (Pen p = new Pen(curBorder, 1f))
                        {
                            DrawRoundedRectangle(g, p, b, pillRect, (int)Math.Round(2 * sc));
                        }
                        g.DrawString(tag, _fontTrace, textBrush, pillRect.X + padH, pillRect.Y);
                        curX += pillW + (3f * sc);
                    }
                }
            }
        }
    }
}
