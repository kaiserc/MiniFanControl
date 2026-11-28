using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public partial class MainForm
    {
        private Panel CreateSectionCard(string title, int top, int height)
        {
            Label header = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(14, top)
            };
            Controls.Add(header);
            _sectionHeaders.Add(header);

            Panel card = new DoubleBufferedPanel
            {
                Location = new Point(14, top + 20),
                Size = new Size(1072, height)
            };
            card.Paint += (s, e) =>
            {
                if (SystemInformation.HighContrast) return;
                bool isRadeon = (_themePref == ThemePreference.Radeon);
                bool isXmas = IsXmas;
                Color bdr = isXmas
                    ? XmasBlack
                    : (isRadeon
                        ? Color.FromArgb(0x4C, 0x18, 0x22)
                        : (IsAb ? Color.FromArgb(0x22, 0x35, 0x2A) : (_isDarkMode ? Color.FromArgb(0x32, 0x36, 0x4C) : Color.FromArgb(0xD5, 0xDC, 0xE6))));
                Win11VisualStyle.DrawRoundedCardBorder(e.Graphics, card.ClientRectangle, bdr, 8, 1f);
                if (isXmas && card.ClientSize.Width > 120)
                {
                    // Crisp festive crimson top hairline accent inside white Xmas cards
                    using (Pen festPen = new Pen(Color.FromArgb(145, XmasRed), 2f))
                    {
                        e.Graphics.DrawLine(festPen, 10, 1, card.ClientSize.Width - 11, 1);
                    }
                }
            };
            Controls.Add(card);
            _sectionCards.Add(card);
            return card;
        }

        private Label AddStaticLabel(Control parent, string text, int x, int y, int w, int h)
        {
            Label lbl = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h)
            };
            parent.Controls.Add(lbl);
            _staticLabels.Add(lbl);
            return lbl;
        }

        private static readonly Font ToolTipBodyFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        private int GetTooltipChartKind(Control c)
        {
            TelemetryTileControl tile = c as TelemetryTileControl;
            return tile != null ? tile.MetricKind : 0;
        }

        private string _activePopupTooltipText = null;

        private string ResolveTooltipText(Control c)
        {
            if (c == null) return "";
            TelemetryTileControl tile = c as TelemetryTileControl;
            if (tile != null)
            {
                if (_lastStatus.CpuTemp > 0)
                {
                    switch (tile.MetricKind)
                    {
                        case 1: return _lastStatus.FormatCpuTooltip(_sessionStats);
                        case 2: return _lastStatus.FormatIgpuTooltip(_sessionStats);
                        case 3: return _lastStatus.FormatPowerTooltip(_sessionStats);
                        case 4: return _lastStatus.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm, _hw != null ? _hw.Profile : null);
                        case 5: return _lastStatus.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans, _hw != null ? _hw.Profile : null);
                    }
                }
                if (tile.MetricKind == 6 && _lastStatus.NvmeTemp > 0)
                    return _lastStatus.FormatNvmeTooltip(_sessionStats);
                if (tile.MetricKind == 7 && _lastStatus.RamTotalGb > 0)
                    return _lastStatus.FormatRamTooltip(_sessionStats);
                if (tile.MetricKind == 9 && _lastStatus.AmbientTemp > 0)
                {
                    string tUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
                    string ambMinMax = (_sessionStats != null && _sessionStats.MaxAmbientTemp > 0)
                        ? string.Format("\r\nSession Min/Max: {0}{3} / {1}{3} (Avg {2:0.0}{3})",
                            TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinAmbientTemp < int.MaxValue ? _sessionStats.MinAmbientTemp : _lastStatus.AmbientTemp, _useFahrenheit),
                            TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxAmbientTemp, _useFahrenheit),
                            TelemetrySessionStats.ToDisplayTemp(_sessionStats.AvgAmbientTemp, _useFahrenheit),
                            tUnit)
                        : "";
                    return string.Format("Chassis Ambient Temperature (IT8613 Sensor 4): {0}{1}{2}\r\nLeft-click to toggle Live Trend • Right-click to reset peaks.",
                        TelemetrySessionStats.ToDisplayTemp(_lastStatus.AmbientTemp, _useFahrenheit), tUnit, ambMinMax);
                }
            }

            return _uiToolTip != null ? _uiToolTip.GetToolTip(c) : "";
        }

        private void OnToolTipPopup(object sender, PopupEventArgs e)
        {
            string text = ResolveTooltipText(e.AssociatedControl);
            _activePopupTooltipText = text;
            if (string.IsNullOrEmpty(text)) return;

            Size textSize = TextRenderer.MeasureText(text, ToolTipBodyFont, new Size(440, 800),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            int chartKind = GetTooltipChartKind(e.AssociatedControl);
            int w = Math.Max(textSize.Width + 20, chartKind > 0 ? 330 : 120);
            int h = textSize.Height + 16 + (chartKind > 0 ? 60 : 0);
            e.ToolTipSize = new Size(w, h);
        }

        private void OnToolTipDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            ThemePalette pal = CurrentPalette;
            Color bg = pal.TooltipBg;
            Color border = pal.TooltipBorder;
            Color fg = pal.Text;

            Rectangle bounds = e.Bounds;
            using (SolidBrush bgBr = new SolidBrush(bg))
            {
                g.FillRectangle(bgBr, bounds);
            }
            using (SolidBrush topAccent = new SolidBrush(pal.Accent))
            {
                g.FillRectangle(topAccent, bounds.X, bounds.Y, bounds.Width, 2);
            }
            Win11VisualStyle.DrawRoundedCardBorder(g, bounds, border, 6, 1.2f);

            int chartKind = GetTooltipChartKind(e.AssociatedControl);
            int textHeight = bounds.Height - (chartKind > 0 ? 64 : 10);
            Rectangle textRect = new Rectangle(bounds.X + 10, bounds.Y + 8, bounds.Width - 20, Math.Max(20, textHeight));
            string drawText = !string.IsNullOrEmpty(_activePopupTooltipText) ? _activePopupTooltipText : e.ToolTipText;
            TextRenderer.DrawText(g, drawText, ToolTipBodyFont, textRect, fg,
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            if (chartKind > 0)
            {
                Rectangle chartRect = new Rectangle(bounds.X + 10, bounds.Bottom - 54, bounds.Width - 20, 46);
                bool ramHasTemp = (_sessionStats.RamTempHistory.Count > 0);
                List<int> samples = chartKind == 1 ? _sessionStats.CpuHistory
                    : (chartKind == 2 ? _sessionStats.GpuHistory
                    : (chartKind == 6 ? _sessionStats.NvmeHistory
                    : (chartKind == 3 ? _sessionStats.PowerHistory
                    : (chartKind == 7 ? (ramHasTemp ? _sessionStats.RamTempHistory : _sessionStats.RamPowerHistory)
                    : (chartKind == 9 ? _sessionStats.AmbientHistory
                    : (chartKind == 4 ? _sessionStats.Fan1RpmHistory : _sessionStats.Fan2RpmHistory))))));
                string unit = (chartKind == 3 || (chartKind == 7 && !ramHasTemp)) ? "W" : ((chartKind == 4 || chartKind == 5) ? " RPM" : TelemetrySessionStats.TempUnit(_useFahrenheit));
                Color lineCol = TelemetryTileControl.ResolveSeriesColor(chartKind, pal);
                TelemetryTileControl.DrawMiniSparklineChart(g, chartRect, samples, lineCol, unit, true, pal, chartKind == 7 && !ramHasTemp);
            }
        }

        private void SetGraphViewMode(bool showLive, int highlightMetric)
        {
            SetGraphViewMode(showLive, false, highlightMetric);
        }

        private void SetGraphViewMode(bool showLive, bool showPerf, int highlightMetric)
        {
            _showingOsdConfig = false;
            if (_osdConfigPanel != null) _osdConfigPanel.Visible = false;

            if (ClientSize.Width >= 1450 && (showLive || showPerf))
            {
                _wideRightShowPerf = showPerf;
                if (showLive && highlightMetric > 0 && _liveCanvas != null)
                {
                    _highlightedMetric = highlightMetric;
                    _liveCanvas.HighlightMetric = _highlightedMetric;
                }
                showLive = false;
                showPerf = false;
            }

            _showingLiveTelemetry = showLive;
            _showingPerfGraph = showPerf;
            if (showLive)
            {
                _highlightedMetric = highlightMetric > 0 ? highlightMetric : 1;
            }
            else if (ClientSize.Width < 1450)
            {
                _highlightedMetric = 0;
            }

            bool isFanTab = !showLive && !showPerf;
            SetPresetsAndCustomVisible(isFanTab);
            SetManualFanControlVisible(isFanTab);

            if (showPerf)
            {
                if (_curveCanvas != null) _curveCanvas.Visible = false;
                if (_liveCanvas != null) _liveCanvas.Visible = false;
                if (_perfCanvas != null)
                {
                    _perfCanvas.Visible = true;
                    _perfCanvas.Invalidate();
                }
            }
            else if (showLive)
            {
                if (_curveCanvas != null) _curveCanvas.Visible = false;
                if (_perfCanvas != null) _perfCanvas.Visible = false;
                if (_liveCanvas != null)
                {
                    _liveCanvas.HighlightMetric = _highlightedMetric;
                    _liveCanvas.Visible = true;
                    _liveCanvas.Invalidate();
                }
            }
            else
            {
                if (_liveCanvas != null) _liveCanvas.Visible = false;
                if (_perfCanvas != null) _perfCanvas.Visible = false;
                if (_curveCanvas != null)
                {
                    _curveCanvas.Visible = true;
                    _curveCanvas.Invalidate();
                }
            }

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            UpdateCurveHint();
            InvalidateAllMetricBoxes();
            UpdateWideLayout();
        }

        private void InvalidateAllMetricBoxes()
        {
            if (_telemetryTiles == null) return;
            for (int i = 0; i < _telemetryTiles.Length; i++)
            {
                TelemetryTileControl tile = _telemetryTiles[i];
                if (tile == null) continue;
                tile.IsSpotlit = (_showingLiveTelemetry && _highlightedMetric == tile.MetricKind);
                tile.Invalidate();
            }
        }

        private void OnTelemetryTileActivated(int metricKind)
        {
            if (_showingLiveTelemetry && _highlightedMetric == metricKind)
                SetGraphViewMode(false, 0);
            else
                SetGraphViewMode(true, metricKind);
        }

        private TelemetryTileControl CreateTelemetryTile(
            Panel parent,
            int metricKind,
            string title,
            string initialValue,
            bool isRpmTile,
            int x,
            int y,
            int width,
            int height,
            string accessibleName,
            string accessibleDesc,
            string tooltipText,
            Func<List<int>> samplesProvider,
            Func<bool> isDeciWattsProvider = null)
        {
            TelemetryTileControl tile = new TelemetryTileControl(metricKind, title, initialValue, isRpmTile)
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                PaletteProvider = () => CurrentPalette,
                SamplesProvider = samplesProvider,
                IsDeciWattsProvider = isDeciWattsProvider
            };
            tile.EnableKeyboardActivation(accessibleName, accessibleDesc);
            tile.TileActivated += (s, e) => OnTelemetryTileActivated(metricKind);
            tile.ResetPeaksRequested += (s, e) => ResetSessionPeaks();
            parent.Controls.Add(tile);
            if (!string.IsNullOrEmpty(tooltipText))
                _uiToolTip.SetToolTip(tile, tooltipText);
            return tile;
        }

        private void UpdateTelemetryTiles(
            FanStatus st,
            string tUnit,
            string gpuLbl,
            bool readOnlyMonitor,
            bool biosAutoActive,
            string apuModeName,
            string sysModeName,
            ThemePalette pal,
            Color tempFg,
            Color dimText,
            Color warnRose)
        {
            // 1. CPU Tile (Row 1, Col 0)
            if (_cpuTile != null)
            {
                string cpuValTxt = st.CpuTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit), tUnit) : "N/A";
                Color cpuHeroFg = ResolveThermalHeroColor(st.CpuTemp, 75, 88, tempFg, pal);
                string cpuPeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxCpuTemp > 0)
                    ? string.Format("▼{0}° ~{1:0}° ▲{2}°",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinCpuTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgCpuTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxCpuTemp, _useFahrenheit))
                    : "";
                string cpuSubTxt = "";
                if (st.CpuClockMhz > 0 && st.CpuLoadPct >= 0)
                    cpuSubTxt = string.Format("{0}% • {1} MHz", st.CpuLoadPct, st.CpuClockMhz);
                else if (st.CpuClockMhz > 0)
                    cpuSubTxt = string.Format("{0} MHz", st.CpuClockMhz);
                else if (st.CpuLoadPct > 0)
                    cpuSubTxt = string.Format("{0}% Load", st.CpuLoadPct);
                string cpuLoadRange = (_sessionStats.SampleCount > 0 && _sessionStats.MaxCpuLoad > 0)
                    ? string.Format("Load ▼{0}% ~{1:0}% ▲{2}%", _sessionStats.MinCpuLoad, _sessionStats.AvgCpuLoad, _sessionStats.MaxCpuLoad)
                    : string.Format("Load {0}%", st.CpuLoadPct);
                string cpuRailPart = st.CpuPowerWatts > 0 ? string.Format(" • CPU ~{0}W", st.CpuPowerWatts) : "";
                string cpuDetail2 = st.IsThermalThrottling
                    ? "⚠ THERMAL THROTTLING ACTIVE"
                    : (st.NpuActivityPct > 0 || st.NpuClockMhz > 0
                        ? string.Format("Tctl/Tdie • NPU {0}% ({1} MHz)", st.NpuActivityPct, st.NpuClockMhz)
                        : "Ryzen Package • Tctl/Tdie");
                Color cpuAlertCol = st.IsThermalThrottling ? warnRose : Color.Empty;
                _cpuTile.UpdateMetric("CPU", cpuValTxt, cpuHeroFg, cpuPeakTxt, cpuSubTxt, cpuLoadRange + cpuRailPart, cpuDetail2,
                    cpuAlertCol, cpuAlertCol, string.Format("CPU Temperature: {0}", cpuValTxt));
            }

            // 2. GPU / iGPU Tile (Row 1, Col 1)
            if (_gpuTile != null)
            {
                string gpuValTxt = st.GpuTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit), tUnit) : "N/A";
                Color gpuHeroFg = ResolveThermalHeroColor(st.GpuTemp, 70, 84, tempFg, pal);
                string gpuPeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuTemp > 0)
                    ? string.Format("▼{0}° ~{1:0}° ▲{2}°",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinGpuTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgGpuTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxGpuTemp, _useFahrenheit))
                    : "";
                string gpuSubTxt = st.GfxClockMhz > 0
                    ? string.Format("{0}% • {1} MHz", st.GfxActivityPct, st.GfxClockMhz)
                    : (st.GfxActivityPct > 0 ? string.Format("{0}% Load", st.GfxActivityPct) : "0% • Idle");
                string gpuLoadRange = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuLoad > 0)
                    ? string.Format("Load ▼{0}% ~{1:0}% ▲{2}%", _sessionStats.MinGpuLoad, _sessionStats.AvgGpuLoad, _sessionStats.MaxGpuLoad)
                    : string.Format("Load {0}%", st.GfxActivityPct);
                string gpuExtraPart = st.GpuHotspotTemp > 0
                    ? string.Format(" • HS {0}°", TelemetrySessionStats.ToDisplayTemp(st.GpuHotspotTemp, _useFahrenheit))
                    : (st.GfxPowerWatts > 0 ? string.Format(" • GFX ~{0}W", st.GfxPowerWatts) : "");
                string gpuDrvPart = !string.IsNullOrEmpty(st.RadeonSoftwareVer) ? ("Adrenalin " + st.RadeonSoftwareVer) : (!string.IsNullOrEmpty(st.GpuName) ? st.GpuName : "Radeon 780M");
                string gpuFeatPart = !string.IsNullOrEmpty(st.RadeonFeaturesSummary) ? (" • " + st.RadeonFeaturesSummary) : "";
                _gpuTile.UpdateMetric(gpuLbl, gpuValTxt, gpuHeroFg, gpuPeakTxt, gpuSubTxt, gpuLoadRange + gpuExtraPart, gpuDrvPart + gpuFeatPart,
                    Color.Empty, Color.Empty, string.Format("{0} Temperature: {1}", gpuLbl, gpuValTxt));
            }

            // 3. RAM (DDR5) Tile (Row 1, Col 2)
            if (_ramTile != null)
            {
                string ramValTxt = st.DdrTemp > 0
                    ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.DdrTemp, _useFahrenheit), tUnit)
                    : (st.RamPowerWatts > 0 ? string.Format("~{0:F1}W", st.RamPowerWatts) : ("--" + tUnit));
                Color ramHeroFg = st.DdrTemp > 0 ? ResolveThermalHeroColor(st.DdrTemp, 52, 62, tempFg, pal) : tempFg;
                string ramPeakTxt = "";
                if (st.DdrTemp > 0 && _sessionStats.SampleCount > 0 && _sessionStats.MaxDdrTemp > 0)
                {
                    ramPeakTxt = string.Format("▼{0}° ~{1:0}° ▲{2}°",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinDdrTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgDdrTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxDdrTemp, _useFahrenheit));
                }
                else if (_sessionStats.SampleCount > 0 && _sessionStats.MaxRamPowerWatts > 0)
                {
                    ramPeakTxt = string.Format("▼{0:F1}W ~{1:F1}W ▲{2:F1}W",
                        _sessionStats.MinRamPowerWatts, _sessionStats.AvgRamPowerWatts, _sessionStats.MaxRamPowerWatts);
                }
                string ramSubTxt = st.RamTotalGb > 0
                    ? string.Format("{0:F1}/{1:F1}G ({2}%)", st.RamUsedGb, st.RamTotalGb, st.RamLoadPct)
                    : "N/A";
                string ramStickPart = (st.DdrStickCount == 2 && st.DdrTemp1 > 0 && st.DdrTemp2 > 0)
                    ? string.Format("Sticks {0}/{1}° Δ{2}°",
                        TelemetrySessionStats.ToDisplayTemp(st.DdrTemp1, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(st.DdrTemp2, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTempDelta(st.DdrTempSpread, _useFahrenheit))
                    : (_sessionStats.SampleCount > 0 && _sessionStats.MaxRamLoad > 0
                        ? string.Format("Load ▼{0}% ▲{1}%", _sessionStats.MinRamLoad, _sessionStats.MaxRamLoad)
                        : "DDR5");
                string ramPwrPart = st.RamPowerWatts > 0 ? string.Format(" • ~{0:0.0}W", st.RamPowerWatts) : "";
                string ramLoadPkPart = (st.DdrStickCount == 2 && _sessionStats.MaxRamLoad > 0)
                    ? string.Format(" • ▲{0}%", _sessionStats.MaxRamLoad)
                    : "";
                string ramSpdPart = st.RamSpeedMhz > 0 ? string.Format("{0} MHz", st.RamSpeedMhz) : "DDR5";
                string fclkPart = st.FabricClockMhz > 0
                    ? string.Format(" • FCLK {0} MHz{1}", st.FabricClockMhz, st.IsFabricSynced ? " (1:1)" : "")
                    : "";
                string ramDetail2 = ramSpdPart + fclkPart + (st.IsDdrEstimated ? " [Est]" : "");
                Color ramSubCol = (st.RamLoadPct >= 90) ? warnRose : Color.Empty;
                _ramTile.UpdateMetric("RAM (DDR5)", ramValTxt, ramHeroFg, ramPeakTxt, ramSubTxt, ramStickPart + ramPwrPart + ramLoadPkPart, ramDetail2,
                    ramSubCol, Color.Empty, string.Format("RAM Sensor: {0}", ramValTxt));
            }

            // 4. NVMe Storage Tile (Row 1, Col 3)
            if (_nvmeTile != null)
            {
                string nvmeValTxt = st.NvmeTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.NvmeTemp, _useFahrenheit), tUnit) : "N/A";
                Color nvmeFg = ResolveThermalHeroColor(st.NvmeTemp, 58, 70, tempFg, pal);
                string nvmePeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxNvmeTemp > 0)
                    ? string.Format("▼{0}° ~{1:0}° ▲{2}°",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinNvmeTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgNvmeTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxNvmeTemp, _useFahrenheit))
                    : "";
                string nvmePwrTxt = st.NvmePowerWatts > 0 ? string.Format("~{0:0.0}W", st.NvmePowerWatts) : "";
                string nvmeIoTxt = (st.NvmeReadMBs > 0.1 || st.NvmeWriteMBs > 0.1)
                    ? string.Format("{0:0.#}R/{1:0.#}W MB/s", st.NvmeReadMBs, st.NvmeWriteMBs)
                    : (st.NvmeTemp > 0 ? "Idle I/O" : "");
                string nvmeSubTxt = (!string.IsNullOrEmpty(nvmePwrTxt) && !string.IsNullOrEmpty(nvmeIoTxt))
                    ? (nvmePwrTxt + " • " + nvmeIoTxt)
                    : (!string.IsNullOrEmpty(nvmePwrTxt) ? nvmePwrTxt : nvmeIoTxt);
                string nvmeHealthPart = st.NvmeHealthPct > 0 ? string.Format("Health {0}%", st.NvmeHealthPct) : "SMART OK";
                string nvmeQdPart = string.Format(" • QD {0}", st.NvmeQueueDepth);
                string nvmeSsd2Part = st.Nvme2Temp > 0
                    ? string.Format(" • SSD2 {0}{1}", TelemetrySessionStats.ToDisplayTemp(st.Nvme2Temp, _useFahrenheit), tUnit)
                    : "";
                string nvmeDetail2 = !string.IsNullOrEmpty(st.NvmeModel) ? st.NvmeModel : "NVMe SSD";
                _nvmeTile.UpdateMetric("NVMe", nvmeValTxt, nvmeFg, nvmePeakTxt, nvmeSubTxt, nvmeHealthPart + nvmeQdPart + nvmeSsd2Part, nvmeDetail2,
                    Color.Empty, Color.Empty, string.Format("NVMe Temperature: {0}", nvmeValTxt));
            }

            // 5. APU Power Tile (Row 2, Col 0)
            if (_pwrTile != null)
            {
                string pwrValTxt = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                string pwrPeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxPowerWatts > 0)
                    ? string.Format("▼{0}W ~{1:0}W ▲{2}W", _sessionStats.MinPowerWatts, _sessionStats.AvgPowerWatts, _sessionStats.MaxPowerWatts)
                    : "";
                string pwrSubTxt = st.ApuPowerLimitWatts > 0
                    ? string.Format("/ {0}W Limit", st.ApuPowerLimitWatts)
                    : (st.VrmTemp > 0 ? string.Format("VRM {0}{1}", TelemetrySessionStats.ToDisplayTemp(st.VrmTemp, _useFahrenheit), tUnit) : "Package");
                string pwrDetail1 = (st.CpuPowerWatts > 0 || st.GfxPowerWatts > 0 || st.SocPowerWatts > 0)
                    ? string.Format("CPU {0}W • GFX {1}W • SoC {2}W", st.CpuPowerWatts, st.GfxPowerWatts, st.SocPowerWatts)
                    : (st.ApuPowerWatts > 0 && st.ApuPowerLimitWatts > 0
                        ? string.Format("{0}% of {1}W TDP Budget", Math.Min(999, (int)Math.Round(st.ApuPowerWatts * 100.0 / st.ApuPowerLimitWatts)), st.ApuPowerLimitWatts)
                        : (st.IsApuPowerEstimated ? "Estimated TDP Envelope" : ((st.IsDiscreteGpu || readOnlyMonitor) ? "Package / Board Rail" : "APU Package Rail")));
                string pwrDetail2;
                if (st.VddTdcCurrentAmps > 0.1 || st.VddEdcCurrentAmps > 0.1)
                {
                    string vrmPart = st.VrmTemp > 0 ? string.Format(" • VRM {0}°", TelemetrySessionStats.ToDisplayTemp(st.VrmTemp, _useFahrenheit)) : "";
                    pwrDetail2 = string.Format("TDC {0:0.#}A • EDC {1:0.#}A{2}", st.VddTdcCurrentAmps, st.VddEdcCurrentAmps, vrmPart);
                }
                else if (st.VrmTemp > 0)
                {
                    pwrDetail2 = string.Format("VRM {0}{1}{2}", TelemetrySessionStats.ToDisplayTemp(st.VrmTemp, _useFahrenheit), tUnit, st.IsVrmEstimated ? " [Est]" : "");
                }
                else if (st.DcInputVolts > 0.1)
                {
                    pwrDetail2 = string.Format("DC Input Rail {0:0.0}V", st.DcInputVolts);
                }
                else
                {
                    pwrDetail2 = st.PowerSource ?? "AC Power";
                }
                _pwrTile.UpdateMetric("POWER", pwrValTxt, tempFg, pwrPeakTxt, pwrSubTxt, pwrDetail1, pwrDetail2,
                    Color.Empty, Color.Empty, string.Format("APU Power: {0}", pwrValTxt));
            }

            // 6. Chassis / VRM / Live FPS Tile (Row 2, Col 1)
            if (_ambientTile != null)
            {
                string ambValTxt = st.AmbientTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.AmbientTemp, _useFahrenheit), tUnit) : "N/A";
                Color ambHeroFg = ResolveThermalHeroColor(st.AmbientTemp, 45, 55, tempFg, pal);
                string ambPeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxAmbientTemp > 0)
                    ? string.Format("▼{0}° ~{1:0}° ▲{2}°",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinAmbientTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgAmbientTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxAmbientTemp, _useFahrenheit))
                    : "";
                string ambSubTxt = st.RtssFps > 0
                    ? string.Format("{0} FPS ({1:0.0}ms)", st.RtssFps, st.RtssFrametimeMs)
                    : (st.VrmTemp > 0
                        ? string.Format("VRM {0}{1}", TelemetrySessionStats.ToDisplayTemp(st.VrmTemp, _useFahrenheit), tUnit)
                        : (st.IsAmbientEstimated ? "Ambient [Est]" : "Board Ambient"));
                string ambDetail1;
                if (st.RtssFps > 0)
                {
                    string lowPart = st.RtssP01LowFps > 0 ? string.Format(" • 0.1% {0}", st.RtssP01LowFps) : "";
                    ambDetail1 = string.Format("FPS ▼{0} ~{1:0} ▲{2}{3}", _sessionStats.MinFps, _sessionStats.AvgFps, _sessionStats.MaxFps, lowPart);
                }
                else if (st.VrmTemp > 0 && _sessionStats.MaxVrmTemp > 0)
                {
                    ambDetail1 = string.Format("VRM ▼{0}° ~{1:0}° ▲{2}°{3}",
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinVrmTemp, _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgVrmTemp), _useFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxVrmTemp, _useFahrenheit),
                        st.IsVrmEstimated ? " [Est]" : "");
                }
                else if (st.CpuTemp > 0 && st.AmbientTemp > 0)
                {
                    int delta = Math.Max(0, st.CpuTemp - st.AmbientTemp);
                    ambDetail1 = string.Format("CPU Δ+{0}{1} over Ambient", TelemetrySessionStats.ToDisplayTempDelta(delta, _useFahrenheit), tUnit);
                }
                else
                {
                    ambDetail1 = st.IsAmbientEstimated ? "Modeled Chassis Ambient" : "Motherboard Thermistor";
                }
                string ambDeltaPart = (st.CpuTemp > 0 && st.AmbientTemp > 0)
                    ? string.Format(" • CPU Δ+{0}°", TelemetrySessionStats.ToDisplayTempDelta(Math.Max(0, st.CpuTemp - st.AmbientTemp), _useFahrenheit))
                    : "";
                string ambDcPart = st.DcInputVolts > 0.1 ? string.Format(" • {0:0.0}V DC", st.DcInputVolts) : "";
                string ambDetail2 = (st.IsAmbientEstimated ? "Board Ambient [Est]" : "Board Ambient") + ambDeltaPart + ambDcPart;
                _ambientTile.UpdateMetric("CHASSIS", ambValTxt, ambHeroFg, ambPeakTxt, ambSubTxt, ambDetail1, ambDetail2,
                    Color.Empty, Color.Empty, string.Format("Chassis Ambient Temperature: {0}", ambValTxt));
            }

            // 7. APU Fan Tile (Row 2, Col 2)
            if (_fan1Tile != null)
            {
                string f1ValTxt = (readOnlyMonitor && st.Fan1Rpm == 0) ? "-- RPM" : string.Format("{0} RPM", st.Fan1Rpm);
                string fan1PeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxFan1Rpm > 0)
                    ? string.Format("▼{0} ~{1:0} ▲{2}", _sessionStats.MinFan1Rpm, _sessionStats.AvgFan1Rpm, _sessionStats.MaxFan1Rpm)
                    : "";
                string fan1Detail1 = readOnlyMonitor
                    ? "APU / CPU Cooler • Motherboard Controlled"
                    : (_emergencyThermalActive
                        ? "APU / CPU Cooler • Emergency Override"
                        : (biosAutoActive
                            ? "APU / CPU Cooler • BIOS Auto (Hands-Off)"
                            : (_apuManualOverride
                                ? "APU / CPU Cooler • Manual Override"
                                : (st.Fan1Manual == 0 ? "APU / CPU Cooler • BIOS Auto" : "APU / CPU Cooler • Active Curve"))));
                string fan1Detail2 = readOnlyMonitor ? "Safe Read-Only Mode" : apuModeName;
                _fan1Tile.UpdateMetric("APU FAN", f1ValTxt, tempFg, fan1PeakTxt, _pct1Label.Text, fan1Detail1, fan1Detail2,
                    _pct1Label.ForeColor, readOnlyMonitor ? dimText : tempFg, string.Format("APU Fan Speed: {0} RPM", st.Fan1Rpm));
            }

            // 8. Sys Fan Tile (Row 2, Col 3)
            if (_fan2Tile != null)
            {
                string f2ValTxt = !_hasDualFans ? "N/A" : (st.Fan2Rpm > 0 ? string.Format("{0} RPM", st.Fan2Rpm) : "-- RPM");
                string fan2PeakTxt = (_hasDualFans && _sessionStats.SampleCount > 0 && _sessionStats.MaxFan2Rpm > 0)
                    ? string.Format("▼{0} ~{1:0} ▲{2}", _sessionStats.MinFan2Rpm, _sessionStats.AvgFan2Rpm, _sessionStats.MaxFan2Rpm)
                    : "";
                string fan2Detail1 = !_hasDualFans
                    ? "Single-Fan Chassis"
                    : (readOnlyMonitor
                        ? "System Fan • Motherboard Controlled"
                        : (_emergencyThermalActive
                            ? "RAM / NVMe Cooler • Emergency Override"
                            : (biosAutoActive
                                ? "RAM / NVMe Cooler • BIOS Auto (Hands-Off)"
                                : (_sysManualOverride
                                    ? "RAM / NVMe Cooler • Manual Override"
                                    : (st.Fan2Manual == 0 ? "RAM / NVMe Cooler • BIOS Auto" : "RAM / NVMe Cooler • Active Curve")))));
                string fan2Detail2 = !_hasDualFans ? "Disabled" : (readOnlyMonitor ? "Safe Read-Only Mode" : sysModeName);
                _fan2Tile.UpdateMetric("SYS FAN", f2ValTxt, tempFg, fan2PeakTxt, _pct2Label.Text, fan2Detail1, fan2Detail2,
                    _pct2Label.ForeColor, (!_hasDualFans || readOnlyMonitor) ? dimText : tempFg,
                    !_hasDualFans ? "System Fan: Disabled" : string.Format("System Fan Speed: {0} RPM", st.Fan2Rpm));
            }
        }

        private void BuildUi()
        {
            _uiToolTip = new ToolTip
            {
                AutoPopDelay = 25000,
                InitialDelay = 120,
                ReshowDelay = 30,
                ShowAlways = true,
                OwnerDraw = true
            };
            _uiToolTip.Popup += OnToolTipPopup;
            _uiToolTip.Draw += OnToolTipDraw;

            // 0. Dedicated Top Header Row (Logo, Model Info, Drivers Menu, Theme Toggle)
            _headerLogoBox = new PictureBox
            {
                Location = new Point(10, 4),
                Size = new Size(56, 56),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppLogoRenderer.RenderConcept(1, 256),
                Cursor = Cursors.Hand
            };
            _headerLogoBox.Click += (s, e) => ShowAboutDialog();
            Controls.Add(_headerLogoBox);
            _uiToolTip.SetToolTip(_headerLogoBox,
                string.Format("{0} ({1})\r\nMiniFanControl v{2} — Click for About & Hardware Details",
                    _sysInfo.ProductName, _sysInfo.CpuName, APP_VERSION));

            string topHeaderTitle = _sysInfo != null && !string.IsNullOrEmpty(_sysInfo.GetCompactSummary())
                ? _sysInfo.GetCompactSummary()
                : "System Telemetry";
            _hwBadgeLabel = new Label
            {
                Text = topHeaderTitle,
                Font = new Font("Segoe UI", 11.0f, FontStyle.Bold),
                Location = new Point(74, 10),
                Size = new Size(358, 22),
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwBadgeLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwBadgeLabel);
            _uiToolTip.SetToolTip(_hwBadgeLabel,
                string.Format("{0} ({1})\r\nAPU: {2}\r\nActive EC Map: {3}\r\nClick for Hardware & Multi-Model Details",
                    _sysInfo.ProductName, _sysInfo.BaseBoard, _sysInfo.CpuName, _sysInfo.MatchedProfile.DisplayName));

            string initialDrv = NativeInpOut.ActiveDriverName;
            _hwSubLabel = new Label
            {
                Text = string.Format("EC: {0} • {1}", _sysInfo.MatchedProfile.DisplayName, initialDrv),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Regular),
                Location = new Point(74, 34),
                Size = new Size(358, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwSubLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwSubLabel);
            _uiToolTip.SetToolTip(_hwSubLabel,
                string.Format("EC Map: {0}\r\nActive Driver: {1}\r\nSync: Global\\Access_EC Mutex • 64-bit\r\nClick for Hardware & Multi-Model Details",
                    _sysInfo.MatchedProfile.DisplayName, initialDrv));

            // Explicit Power Mode Switcher — BIOS EC 0x31/0x32 on EC boards, or native Windows Power Mode on Super I/O boards
            bool readOnlyMonitor = IsReadOnlyMonitoringMode;
            bool usesWinPower = (!readOnlyMonitor && !_demoMode && _hw != null && _hw.IsSuperIoControlled && WindowsPowerMode.IsSupported());
            bool showBiosPowerBar = !readOnlyMonitor && (_demoMode || _hw == null || !_hw.IsSuperIoControlled || usesWinPower);
            if (!showBiosPowerBar)
            {
                _hwBadgeLabel.Size = new Size(706, 22);
                _hwSubLabel.Size = new Size(706, 18);
            }
            string biosPowerTip = GetPowerBarTooltip(usesWinPower);
            _biosPowerLabel = new Label
            {
                Text = usesWinPower ? "Power:" : "BIOS:",
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(436, 20),
                Size = new Size(58, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Visible = showBiosPowerBar,
                UseMnemonic = false
            };
            Controls.Add(_biosPowerLabel);
            _biosPowerLabel.BringToFront();
            _uiToolTip.SetToolTip(_biosPowerLabel, biosPowerTip);

            int[] biosOrder = new int[] { 2, 0, 1 }; // Visual order: Silent | Balanced | Perf
            string[] biosLabels = new string[] { "⚖ Balanced", "⚡ Perf", "🌙 Silent" };
            int[] biosWidths = new int[] { 102, 82, 88 };
            int biosX = 498;
            for (int k = 0; k < biosOrder.Length; k++)
            {
                int code = biosOrder[k];
                int w = biosWidths[code];
                Button b = new Button
                {
                    Text = biosLabels[code],
                    FlatStyle = FlatStyle.Flat,
                    Font = BiosModeFontRegular,
                    Location = new Point(biosX, 15),
                    Size = new Size(w, 28),
                    Padding = new Padding(0),
                    Cursor = Cursors.Hand,
                    Visible = showBiosPowerBar,
                    UseMnemonic = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => ApplyBiosPowerMode(code);
                Controls.Add(b);
                b.BringToFront();
                _uiToolTip.SetToolTip(b, biosPowerTip);
                _biosModeButtons[code] = b;
                biosX += w + 4;
            }

            RebuildDriversMenu();

            _driversMenuBtn = new Button
            {
                Text = "Tools && Drivers ▾",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(788, 15),
                Size = new Size(136, 28),
                Cursor = Cursors.Hand
            };
            _driversMenuBtn.FlatAppearance.BorderSize = 0;
            _driversMenuBtn.Click += (s, e) =>
            {
                if (_driversMenu == null || _driversMenu.IsDisposed)
                    _driversMenu = CreateDriversAndToolsMenu();
                _driversMenu.Show(_driversMenuBtn, new Point(0, _driversMenuBtn.Height));
            };
            Controls.Add(_driversMenuBtn);
            _driversMenuBtn.BringToFront();

            _themeToggleBtn = new Button
            {
                Text = GetThemeButtonLabel(),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(930, 15),
                Size = new Size(156, 28),
                Cursor = Cursors.Hand
            };
            _themeToggleBtn.FlatAppearance.BorderSize = 0;
            _themeToggleBtn.Click += (s, e) => ShowThemePickerMenu();
            Controls.Add(_themeToggleBtn);
            _themeToggleBtn.BringToFront();
            _uiToolTip.SetToolTip(_themeToggleBtn, "Pick UI theme (System, Dark, AMD Radeon, Afterburner, Light, Xmas Edition)");

            // 1. Hardware & Live Telemetry Section (top = 64, card = 84..338, height = 254) — 2x4 Metric Grid
            Panel statusCard = CreateSectionCard("System Telemetry & Live Hardware Sensors", 64, 254);

            const int boxW = 257;
            const int boxH = 108;
            const int boxGapX = 6;
            const int row1Y = 6;
            const int row2Y = 119;

            bool isIt5570eEc = (_hw == null || _hw.SuperIoBaseAddr == 0);
            string cpuTip = isIt5570eEc
                ? "APU / CPU Package Temperature (EC Register 0x70 • Tctl/Tdie)\r\n" +
                  "• Emergency Thermal Override: Forces both fans to 100% at ≥90°C and holds until ≤75°C (15°C cooldown hysteresis).\r\n" +
                  "• BIOS Power Modes (EC 0x31/0x32): Silent, Balanced (~47–53W), and Performance (~59W+) are the native BIOS TDP presets.\r\n" +
                  "• EC Slew-Rate Limiting: IT5570E firmware ramps fan speed smoothly (~4–5%/s, ~15–20s to max) in both BIOS Auto and Custom Curves.\r\n" +
                  "• Tip: Use the BIOS Power bar at the top (Silent / Balanced / Perf) to change TDP independently of any Custom Fan Curve.\r\n" +
                  "Left-click to highlight CPU Trend on the big graph • Right-click to reset peaks."
                : "APU / CPU Package Temperature (Tctl/Tdie)\r\n" +
                  "• Emergency Thermal Override: Forces both fans to 100% at ≥90°C and holds until ≤75°C (15°C cooldown hysteresis).\r\n" +
                  "Left-click to highlight CPU Trend on the big graph • Right-click to reset peaks.";

            // Row 1 (Core Silicon & Memory/Storage): CPU | iGPU | RAM | NVMe
            _cpuTile = CreateTelemetryTile(statusCard, 1, "CPU", "--°C", false,
                12, row1Y, boxW, boxH,
                "CPU Temperature", "Press Enter or Space to toggle CPU trend highlight on the live graph.",
                cpuTip, () => _sessionStats.CpuHistory);

            _gpuTile = CreateTelemetryTile(statusCard, 2, "iGPU", "--°C", false,
                12 + (boxW + boxGapX) * 1, row1Y, boxW, boxH,
                "iGPU Temperature", "Press Enter or Space to toggle iGPU trend highlight on the live graph.",
                "Integrated Radeon Graphics Temperature (AMD ADL2 PMLog GFX/SoC Sensor)\r\nLeft-click to highlight iGPU Trend on the big graph • Right-click to reset peaks.",
                () => _sessionStats.GpuHistory);

            _ramTile = CreateTelemetryTile(statusCard, 7, "RAM (DDR5)", "--°C", false,
                12 + (boxW + boxGapX) * 2, row1Y, boxW, boxH,
                "RAM Temperature and Power", "Press Enter or Space to toggle RAM trend highlight on the live graph.",
                "DDR5 Memory Temperature, Dual-Stick Delta, Capacity & Speed\r\nLeft-click to highlight RAM Trend on the big graph • Right-click to reset peaks.",
                () => _sessionStats.RamTempHistory.Count > 0 ? _sessionStats.RamTempHistory : _sessionStats.RamPowerHistory,
                () => _sessionStats.RamTempHistory.Count == 0);

            _nvmeTile = CreateTelemetryTile(statusCard, 6, "NVMe", "--°C", false,
                12 + (boxW + boxGapX) * 3, row1Y, boxW, boxH,
                "NVMe SSD Temperature", "Press Enter or Space to toggle NVMe trend highlight on the live graph.",
                "NVMe Storage Temperature, Health, Power & I/O Throughput\r\nLeft-click to highlight NVMe Trend on the big graph • Right-click to reset peaks.",
                () => _sessionStats.NvmeHistory);

            // Row 2 (Power, Platform & Cooling): Power | Chassis / FCLK | APU Fan | Sys Fan
            _pwrTile = CreateTelemetryTile(statusCard, 3, "POWER", "--W", false,
                12, row2Y, boxW, boxH,
                "APU Package Power", "Press Enter or Space to toggle APU Power trend highlight on the live graph.",
                "APU Package Power (CPU + iGPU + SoC Telemetry & VRM Limits)\r\nLeft-click to highlight Power Trend on the big graph • Right-click to reset peaks.",
                () => _sessionStats.PowerHistory);

            _ambientTile = CreateTelemetryTile(statusCard, 9, "CHASSIS", "--°C", false,
                12 + (boxW + boxGapX) * 1, row2Y, boxW, boxH,
                "Chassis Ambient Temperature", "Press Enter or Space to toggle Chassis Ambient trend highlight on the live graph.",
                "Motherboard Chassis Ambient Temperature, Infinity Fabric (FCLK) & Live FPS\r\nLeft-click to toggle Live Trend • Right-click to reset peaks.",
                () => _sessionStats.AmbientHistory);

            _fan1Tile = CreateTelemetryTile(statusCard, 4, "APU FAN", "-- RPM", true,
                12 + (boxW + boxGapX) * 2, row2Y, boxW, boxH,
                "APU Fan Speed", "Press Enter or Space to toggle APU Fan speed trend highlight on the live graph.",
                "APU / CPU Fan Tachometer & Active Duty\r\nLeft-click to highlight APU Fan on the big graph • Right-click to reset peaks.",
                () => _sessionStats.Fan1RpmHistory);

            _fan2Tile = CreateTelemetryTile(statusCard, 5, "SYS FAN", "-- RPM", true,
                12 + (boxW + boxGapX) * 3, row2Y, boxW, boxH,
                "System Fan Speed", "Press Enter or Space to toggle System Fan speed trend highlight on the live graph.",
                "System RAM / NVMe Fan Tachometer & Active Duty\r\nLeft-click to highlight Sys Fan on the big graph • Right-click to reset peaks.",
                () => _sessionStats.Fan2RpmHistory);

            _telemetryTiles = new[]
            {
                _cpuTile, _gpuTile, _ramTile, _nvmeTile,
                _pwrTile, _ambientTile, _fan1Tile, _fan2Tile
            };

            // Row 3: Active Fan Modes & Duty Status
            Label modeStaticLbl = AddStaticLabel(statusCard, "Mode:", 12, 231, 52, 19);
            modeStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _modeLabel = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(62, 231),
                Size = new Size(266, 19),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_modeLabel);

            Label apuStaticLbl = AddStaticLabel(statusCard, "APU Fan:", 332, 231, 68, 19);
            apuStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct1Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(400, 231),
                Size = new Size(226, 19),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct1Label);

            Label sysStaticLbl = AddStaticLabel(statusCard, "Sys Fan:", 630, 231, 64, 19);
            sysStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct2Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(694, 231),
                Size = new Size(248, 19),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct2Label);

            // 2. Modes & Fan Curve Section (top = 344, card = 364..730, height = 366; expands to 476 when Manual Fan hidden)
            Panel modeCard = CreateSectionCard("Modes, Fan Curves & Live Telemetry Graph", 344, 366);
            _modeCardPanel = modeCard;

            // Row 1 (y=8): Tab Selector Row — flush-aligned across x=8..1064 (5 × 208px with 4px gaps)
            _tabLiveGraphBtn = new Button
            {
                Text = "● Live Trend",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(8, 8),
                Size = new Size(208, 28),
                Cursor = Cursors.Hand
            };
            _tabLiveGraphBtn.FlatAppearance.BorderSize = 0;
            _tabLiveGraphBtn.Click += (s, e) =>
            {
                SetGraphViewMode(true, false, _highlightedMetric > 0 ? _highlightedMetric : 1);
            };
            modeCard.Controls.Add(_tabLiveGraphBtn);

            _tabPerfGraphBtn = new Button
            {
                Text = "● Live Perf",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(220, 8),
                Size = new Size(208, 28),
                Cursor = Cursors.Hand
            };
            _tabPerfGraphBtn.FlatAppearance.BorderSize = 0;
            _tabPerfGraphBtn.Click += (s, e) =>
            {
                SetGraphViewMode(false, true, 0);
            };
            modeCard.Controls.Add(_tabPerfGraphBtn);

            _tabApuCurveBtn = new Button
            {
                Text = "● APU Fan: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(432, 8),
                Size = new Size(208, 28),
                Cursor = Cursors.Hand
            };
            _tabApuCurveBtn.FlatAppearance.BorderSize = 0;
            _tabApuCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = false;
                SetGraphViewMode(false, false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabApuCurveBtn);

            _tabSysCurveBtn = new Button
            {
                Text = "● Sys Fan: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(644, 8),
                Size = new Size(208, 28),
                Cursor = Cursors.Hand
            };
            _tabSysCurveBtn.FlatAppearance.BorderSize = 0;
            _tabSysCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = true;
                SetGraphViewMode(false, false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabSysCurveBtn);

            _tabOsdConfigBtn = new Button
            {
                Text = "● OSD Studio",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(856, 8),
                Size = new Size(208, 28),
                Cursor = Cursors.Hand
            };
            _tabOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _tabOsdConfigBtn.Click += (s, e) =>
            {
                SetOsdConfigViewMode(!_showingOsdConfig);
            };
            modeCard.Controls.Add(_tabOsdConfigBtn);

            // Row 2 (y=42): Built-in Mode Buttons for the currently selected fan tab — flush-aligned to x=8..1064
            string[] modeTitles = { "⚖ Balanced", "⚡ Performance", "🌙 Silent", "🤖 AI Workload" };
            for (int i = 0; i < 4; i++)
            {
                int code = i;
                Button btn = new Button
                {
                    Text = modeTitles[i],
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                    Location = new Point(8 + i * 266, 42),
                    Size = new Size(258, 30),
                    Padding = new Padding(0),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => ApplyMode(code);
                modeCard.Controls.Add(btn);
                _modeButtons[i] = btn;
            }

            // Row 3 (y=76): Custom Modes Row for the currently selected fan tab — flush-aligned to x=8..1064
            _customLabel = AddStaticLabel(modeCard, "Custom:", 8, 79, 68, 22);

            _customModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(78, 77),
                Size = new Size(486, 24)
            };
            Win11VisualStyle.EnableThemedComboBox(_customModeCombo, () => CurrentPalette);
            _customModeCombo.SelectedIndexChanged += OnCustomComboSelected;
            modeCard.Controls.Add(_customModeCombo);

            _activateCustomBtn = new Button
            {
                Text = "Use Curve",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(572, 76),
                Size = new Size(106, 26),
                Cursor = Cursors.Hand
            };
            _activateCustomBtn.FlatAppearance.BorderSize = 0;
            _activateCustomBtn.Click += (s, e) =>
            {
                int idx = _customModeCombo.SelectedIndex;
                if (idx >= 0 && idx < _customProfiles.Count)
                {
                    ActivateCustomProfile(_customProfiles[idx]);
                }
            };
            modeCard.Controls.Add(_activateCustomBtn);

            _autoTuneCustomBtn = new Button
            {
                Text = "✨ Auto-Tune",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(686, 76),
                Size = new Size(146, 26),
                Cursor = Cursors.Hand
            };
            _autoTuneCustomBtn.FlatAppearance.BorderSize = 0;
            _autoTuneCustomBtn.Click += (s, e) => RunAutoTuneCurve();
            modeCard.Controls.Add(_autoTuneCustomBtn);
            _uiToolTip.SetToolTip(_autoTuneCustomBtn,
                "Synthesizes an optimal 5-point APU + Sys fan curve tailored to your calibrated anti-stall floor,\r\nobserved idle baseline (anti-hunting), and active BIOS Power Mode TDP envelope.\r\nApplies to both APU Fan and Sys Fan automatically.");

            _addCustomBtn = new Button
            {
                Text = "+ New",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(840, 76),
                Size = new Size(96, 26),
                Cursor = Cursors.Hand
            };
            _addCustomBtn.FlatAppearance.BorderSize = 0;
            _addCustomBtn.Click += (s, e) => PromptCreateCustomMode();
            modeCard.Controls.Add(_addCustomBtn);

            _deleteCustomBtn = new Button
            {
                Text = "Del",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(944, 76),
                Size = new Size(120, 26),
                Cursor = Cursors.Hand
            };
            _deleteCustomBtn.FlatAppearance.BorderSize = 0;
            _deleteCustomBtn.Click += (s, e) => DeleteSelectedCustomMode();
            modeCard.Controls.Add(_deleteCustomBtn);

            // Row 4 (y=106): Interactive Fan Curve Graph + Combined Live Telemetry Graph (1056 x 234)
            _curveCanvas = new FanCurveCanvas
            {
                Location = new Point(8, 106),
                Size = new Size(1056, 234),
                ApuProfile = _apuProfile,
                SysProfile = _sysProfile
            };
            _curveCanvas.BuiltInCurveDragStarted += (s, e) =>
            {
                FanCurveProfile curProf = _curveCanvas.GetActiveProfile();
                if (curProf != null && curProf.IsBuiltIn)
                {
                    string customName = curProf.Name + " Custom";
                    FanCurveProfile existing = null;
                    foreach (FanCurveProfile cp in _customProfiles)
                    {
                        if (cp.Name.Equals(customName, StringComparison.OrdinalIgnoreCase))
                        {
                            existing = cp;
                            break;
                        }
                    }
                    if (existing == null)
                    {
                        existing = curProf.CloneAsCustom(customName);
                        _customProfiles.Add(existing);
                    }
                    else
                    {
                        if (_curveCanvas.EditSysFan)
                            existing.SysPoints = FanCurveProfile.ClonePoints(curProf.SysPoints);
                        else
                            existing.ApuPoints = FanCurveProfile.ClonePoints(curProf.ApuPoints);
                    }
                    if (_curveCanvas.EditSysFan)
                    {
                        if (_controller != null) _controller.SetProfileTarget(existing, true);
                        _curveCanvas.SysProfile = _sysProfile;
                    }
                    else
                    {
                        if (_controller != null) _controller.SetProfileTarget(existing, false);
                        _curveCanvas.ApuProfile = _apuProfile;
                    }
                    PopulateCustomCombo();
                }
            };
            _curveCanvas.CurveModified += (s, e) =>
            {
                if (_controller != null)
                {
                    _controller.SetProfileTarget(_curveCanvas.EditSysFan ? _sysProfile : _apuProfile, _curveCanvas.EditSysFan);
                }
                SaveSettings();
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                RefreshStatus();
            };
            modeCard.Controls.Add(_curveCanvas);

            _liveCanvas = new LiveTelemetryCanvas
            {
                Location = new Point(8, 42),
                Size = new Size(1056, 408),
                Stats = _sessionStats,
                HasDualFans = _hasDualFans,
                ShowCpu = _osd.TrendCpu,
                ShowGpu = _osd.TrendGpu,
                ShowRam = _osd.TrendRam,
                ShowNvme = _osd.TrendNvme,
                ShowPower = _osd.TrendPower,
                ShowAmbient = _osd.TrendAmbient,
                ShowApuRpm = _osd.TrendApuFan,
                ShowSysRpm = _osd.TrendSysFan,
                Visible = false
            };
            _liveCanvas.ResetPeaksRequested += (s, e) => ResetSessionPeaks();
            _liveCanvas.HighlightMetricChanged += (s, e) =>
            {
                _highlightedMetric = _liveCanvas.HighlightMetric;
                UpdateCurveHint();
                InvalidateAllMetricBoxes();
            };
            _liveCanvas.TrendVisibilityChanged += (s, e) =>
            {
                _osd.TrendCpu = _liveCanvas.ShowCpu;
                _osd.TrendGpu = _liveCanvas.ShowGpu;
                _osd.TrendRam = _liveCanvas.ShowRam;
                _osd.TrendNvme = _liveCanvas.ShowNvme;
                _osd.TrendPower = _liveCanvas.ShowPower;
                _osd.TrendAmbient = _liveCanvas.ShowAmbient;
                _osd.TrendApuFan = _liveCanvas.ShowApuRpm;
                _osd.TrendSysFan = _liveCanvas.ShowSysRpm;
                SyncOverlayFromStudio();
            };
            modeCard.Controls.Add(_liveCanvas);

            _perfCanvas = new LivePerformanceCanvas
            {
                Location = new Point(8, 42),
                Size = new Size(1056, 408),
                Stats = _sessionStats,
                Status = _lastStatus,
                Visible = false
            };
            _perfCanvas.ResetPeaksRequested += (s, e) => ResetSessionPeaks();
            modeCard.Controls.Add(_perfCanvas);

            // OSD Overlay Configuration Studio Panel (self-contained OsdStudioControl inside modeCard)
            _osdConfigPanel = CreateOsdStudioControl();
            _osdConfigPanel.Location = new Point(8, 41);
            _osdConfigPanel.Size = new Size(1056, 409);
            _osdConfigPanel.Visible = false;
            modeCard.Controls.Add(_osdConfigPanel);

            _curveHintLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 344),
                Size = new Size(1056, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };
            modeCard.Controls.Add(_curveHintLabel);

            // 3. Manual Fan Control Section (top = 736, card = 756..840, height = 84)
            Panel fanCard = CreateSectionCard("Manual Fan Control", 736, 84);
            _fanCardPanel = fanCard;
            _manualFanHeaderLabel = _sectionHeaders.Count > 0 ? _sectionHeaders[_sectionHeaders.Count - 1] : null;

            _calibBadgeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(544, 734),
                Size = new Size(540, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            _calibBadgeLabel.Click += (s, e) => RunGuiFanCalibration();
            Controls.Add(_calibBadgeLabel);
            _calibBadgeLabel.BringToFront();
            _uiToolTip.SetToolTip(_calibBadgeLabel,
                "Shows when your fans were last calibrated and their minimum anti-stall PWM %.\r\nClick to run the 3-pass real-time Fan Calibration Wizard.");

            AddStaticLabel(fanCard, "APU / CPU Fan:", 12, 10, 118, 24);
            _fan1Slider = new ModernSlider
            {
                Minimum = 0,
                Maximum = 100,
                ZeroLabel = "Auto",
                UnitSuffix = "%",
                FloorThreshold = FanCurveProfile.ApuMinSpinPct,
                PaletteProvider = () => CurrentPalette,
                Location = new Point(134, 6),
                Size = new Size(758, 32),
                AccessibleName = "APU / CPU Fan Manual Speed Percentage (0 equals Auto)"
            };
            _fan1Slider.Scroll += (s, e) =>
            {
                int v = _fan1Slider.Value;
                _fan1SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan1Slider.ValueCommitted += (s, e) =>
            {
                int v = _fan1Slider.Value;
                _fan1SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
                ApplyFan(1, v);
            };
            _uiToolTip.SetToolTip(_fan1Slider,
                "Drag, scroll mouse wheel, or use Arrow keys to set manual APU fan speed (0 = Auto).\r\nAmber rail marker indicates your calibrated Anti-Stall Minimum Spin Floor.");
            fanCard.Controls.Add(_fan1Slider);

            _fan1SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(898, 10),
                Size = new Size(72, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan1SliderPct);

            _fan1AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(978, 8),
                Size = new Size(86, 26),
                Cursor = Cursors.Hand
            };
            _fan1AutoBtn.FlatAppearance.BorderSize = 0;
            _fan1AutoBtn.Click += (s, e) => ApplyFan(1, 0);
            fanCard.Controls.Add(_fan1AutoBtn);

            AddStaticLabel(fanCard, "Sys / RAM Fan:", 12, 46, 118, 24);
            _fan2Slider = new ModernSlider
            {
                Minimum = 0,
                Maximum = 100,
                ZeroLabel = "Auto",
                UnitSuffix = "%",
                FloorThreshold = FanCurveProfile.SysMinSpinPct,
                PaletteProvider = () => CurrentPalette,
                Location = new Point(134, 42),
                Size = new Size(758, 32),
                AccessibleName = "System / RAM Fan Manual Speed Percentage (0 equals Auto)"
            };
            _fan2Slider.Scroll += (s, e) =>
            {
                int v = _fan2Slider.Value;
                _fan2SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan2Slider.ValueCommitted += (s, e) =>
            {
                int v = _fan2Slider.Value;
                _fan2SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
                ApplyFan(2, v);
            };
            _uiToolTip.SetToolTip(_fan2Slider,
                "Drag, scroll mouse wheel, or use Arrow keys to set manual System RAM/NVMe fan speed (0 = Auto).\r\nAmber rail marker indicates your calibrated Anti-Stall Minimum Spin Floor.");
            fanCard.Controls.Add(_fan2Slider);

            _fan2SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(898, 46),
                Size = new Size(72, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan2SliderPct);

            _fan2AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(978, 44),
                Size = new Size(86, 26),
                Cursor = Cursors.Hand
            };
            _fan2AutoBtn.FlatAppearance.BorderSize = 0;
            _fan2AutoBtn.Click += (s, e) => ApplyFan(2, 0);
            fanCard.Controls.Add(_fan2AutoBtn);

            // 4. Options Section (dynamically anchored to bottom by ApplyDynamicVerticalLayout)
            Panel optCard = CreateSectionCard("Options", 846, 78);
            _optCardPanel = optCard;
            _optHeaderLabel = _sectionHeaders.Count > 0 ? _sectionHeaders[_sectionHeaders.Count - 1] : null;

            _samplingLabel = new Label
            {
                Text = "Sampling:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(660, 9),
                AutoSize = true,
                Cursor = Cursors.Default
            };
            optCard.Controls.Add(_samplingLabel);

            _samplingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(736, 6),
                Size = new Size(324, 22),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.EnableThemedComboBox(_samplingCombo, () => CurrentPalette);
            int[] sampleIntervals = new int[] { 100, 250, 500, 1000, 1500, 2000, 3000 };
            _samplingCombo.Items.Add("100 ms (Extreme • 10 Hz)");
            _samplingCombo.Items.Add("250 ms (Ultra • 4 Hz)");
            _samplingCombo.Items.Add("500 ms (Fast • 2 Hz)");
            _samplingCombo.Items.Add("1000 ms (Standard • 1.0s)");
            _samplingCombo.Items.Add("1500 ms (Balanced • 1.5s)");
            _samplingCombo.Items.Add("2000 ms (Relaxed • 2.0s)");
            _samplingCombo.Items.Add("3000 ms (Low CPU • 3.0s)");
            int initialSel = 2;
            for (int i = 0; i < sampleIntervals.Length; i++)
            {
                if (sampleIntervals[i] == _refreshIntervalMs) { initialSel = i; break; }
            }
            _samplingCombo.SelectedIndex = initialSel;
            _samplingCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_samplingCombo.SelectedIndex >= 0 && _samplingCombo.SelectedIndex < sampleIntervals.Length)
                {
                    SetRefreshInterval(sampleIntervals[_samplingCombo.SelectedIndex]);
                }
            };
            optCard.Controls.Add(_samplingCombo);
            _uiToolTip.SetToolTip(_samplingCombo, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");
            _uiToolTip.SetToolTip(_samplingLabel, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");

            _showCpuTrayCb = new CheckBox
            {
                Text = "Show CPU temp in taskbar",
                Location = new Point(12, 6),
                Size = new Size(310, 22),
                Checked = _showCpuTray
            };
            _showCpuTrayCb.CheckedChanged += (s, e) =>
            {
                _showCpuTray = _showCpuTrayCb.Checked;
                SaveSettings();
                _lastCpuIconKey = int.MinValue;
                _trayUpdateThrottle.Reset();
                RefreshStatus();
            };
            optCard.Controls.Add(_showCpuTrayCb);

            _showGpuTrayCb = new CheckBox
            {
                Text = "Show iGPU temp in taskbar",
                Location = new Point(340, 6),
                Size = new Size(300, 22),
                Checked = _showGpuTray
            };
            _showGpuTrayCb.CheckedChanged += (s, e) =>
            {
                _showGpuTray = _showGpuTrayCb.Checked;
                SaveSettings();
                _lastGpuIconKey = int.MinValue;
                _trayUpdateThrottle.Reset();
                RefreshStatus();
            };
            optCard.Controls.Add(_showGpuTrayCb);

            _showOverlayCb = new CheckBox
            {
                Text = "Show In-Game OSD overlay",
                Location = new Point(12, 29),
                Size = new Size(216, 22),
                Checked = _showOverlay
            };
            _showOverlayCb.CheckedChanged += (s, e) =>
            {
                SetOverlayVisible(_showOverlayCb.Checked);
                UpdateOsdStudioStyles();
            };
            optCard.Controls.Add(_showOverlayCb);

            _optCardOsdConfigBtn = new Button
            {
                Text = "⚙ Config...",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Location = new Point(232, 28),
                Size = new Size(84, 23),
                Cursor = Cursors.Hand
            };
            _optCardOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _optCardOsdConfigBtn.Click += (s, e) => OpenOsdConfigTab();
            optCard.Controls.Add(_optCardOsdConfigBtn);
            _uiToolTip.SetToolTip(_optCardOsdConfigBtn, "Open the dedicated OSD Studio tab to configure live graphs, placement, opacity and metrics");

            _lockOverlayCb = new CheckBox
            {
                Text = "Lock OSD (click-through)",
                Location = new Point(340, 29),
                Size = new Size(300, 22),
                Checked = _lockOverlay
            };
            _lockOverlayCb.CheckedChanged += (s, e) => SetOverlayLocked(_lockOverlayCb.Checked);
            optCard.Controls.Add(_lockOverlayCb);

            _autostartCb = new CheckBox
            {
                Text = "Start automatically on Windows login",
                Location = new Point(12, 52),
                Size = new Size(310, 22),
                Checked = IsAutostartEnabled()
            };
            _autostartCb.CheckedChanged += (s, e) => ToggleAutostart();
            optCard.Controls.Add(_autostartCb);

            _alwaysOnTopCb = new CheckBox
            {
                Text = "Keep app on top",
                Location = new Point(340, 52),
                Size = new Size(300, 22),
                Checked = _alwaysOnTop
            };
            _alwaysOnTopCb.CheckedChanged += (s, e) =>
            {
                _alwaysOnTop = _alwaysOnTopCb.Checked;
                TopMost = _alwaysOnTop;
                SaveSettings();
            };
            optCard.Controls.Add(_alwaysOnTopCb);
            TopMost = _alwaysOnTop;
            _uiToolTip.SetToolTip(_alwaysOnTopCb, "Keep the MiniFanControl window above all other windows (including games in borderless/windowed mode).");
            _uiToolTip.SetToolTip(_autostartCb,
                "Registers an elevated Windows Scheduled Task (MiniFanControl_Autostart)\r\nso MiniFanControl starts on Windows login without a UAC prompt.");

            _useFahrenheitCb = new CheckBox
            {
                Text = "Use Fahrenheit (°F)",
                Location = new Point(660, 29),
                Size = new Size(396, 22),
                Checked = _useFahrenheit
            };
            _useFahrenheitCb.CheckedChanged += (s, e) =>
            {
                _useFahrenheit = _useFahrenheitCb.Checked;
                if (_curveCanvas != null) { _curveCanvas.UseFahrenheit = _useFahrenheit; _curveCanvas.Invalidate(); }
                if (_liveCanvas != null) { _liveCanvas.UseFahrenheit = _useFahrenheit; _liveCanvas.Invalidate(); }
                if (_overlayForm != null)
                    SyncOverlayFromStudio();
                else
                    SaveSettings();
                _trayUpdateThrottle.Reset();
                RefreshStatus();
            };
            optCard.Controls.Add(_useFahrenheitCb);
            _uiToolTip.SetToolTip(_useFahrenheitCb, "Display all temperatures in Fahrenheit (°F) across telemetry cards, graphs, tray icons, and the OSD overlay.");

            _trayCb = new CheckBox
            {
                Text = "Close button minimizes to tray",
                Location = new Point(660, 52),
                Size = new Size(396, 22),
                Checked = _closeToTray
            };
            _trayCb.CheckedChanged += (s, e) =>
            {
                _closeToTray = _trayCb.Checked;
                SaveSettings();
                RebuildDriversMenu();
            };
            optCard.Controls.Add(_trayCb);
            _uiToolTip.SetToolTip(_trayCb,
                "Keeps the full GUI hidden in the system tray when clicking 'X' (keeps taskbar temps, In-Game OSD, and curves running).\r\n" +
                "• To fully exit when enabled: right-click the tray icon → Exit.\r\n" +
                "• Prefer lowest RAM (~3–5 MB) with the GUI fully closed? Use Tools & Drivers → Background Service Mode instead.");

            foreach (CheckBox optCb in new[] { _showCpuTrayCb, _showGpuTrayCb, _showOverlayCb, _lockOverlayCb, _autostartCb, _alwaysOnTopCb, _useFahrenheitCb, _trayCb })
            {
                Win11VisualStyle.EnableThemedCheckBox(optCb, () => CurrentPalette);
            }

            // Error Banner
            _errorBanner = new Label
            {
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(14, 866),
                Size = new Size(1072, 78),
                Padding = new Padding(8),
                Visible = false
            };
            _errorBanner.Click += (s, e) =>
            {
                if (_errorBanner.Text != null && _errorBanner.Text.Contains("pawnio.eu"))
                {
                    ShowKernelDriverSecurityDialog(false);
                }
            };
            Controls.Add(_errorBanner);
            _errorBanner.BringToFront();

            // 5. Status Bar (y = 947..973)
            _statusBarLabel = new Label
            {
                Text = "⏳ Initializing…",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(14, 950),
                Size = new Size(820, 20)
            };
            Controls.Add(_statusBarLabel);

            _allAutoBtn = new Button
            {
                Text = "Reset Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 947),
                Size = new Size(106, 26),
                Cursor = Cursors.Hand
            };
            _allAutoBtn.FlatAppearance.BorderSize = 0;
            _allAutoBtn.Click += (s, e) => ApplyAutoAll();
            Controls.Add(_allAutoBtn);

            _closeBtn = new Button
            {
                Text = "Close",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(966, 947),
                Size = new Size(106, 26),
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => Close();
            Controls.Add(_closeBtn);

            // 6. Wide-Screen Companion Panel (2-Column Mode when ClientSize.Width >= 1450)
            _wideRightPanel = new DoubleBufferedPanel
            {
                Location = new Point(1096, 10),
                Size = new Size(780, 900),
                Visible = false
            };
            _wideRightPanel.Paint += (s, e) =>
            {
                if (SystemInformation.HighContrast) return;
                Win11VisualStyle.DrawRoundedCardBorder(e.Graphics, _wideRightPanel.ClientRectangle, CurrentPalette.Border, 8, 1f);
            };
            _wideRightHeaderLabel = new Label
            {
                Text = "LIVE TELEMETRY & PERFORMANCE MONITOR",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(10, 9),
                Size = new Size(340, 20),
                UseMnemonic = false
            };
            _wideRightPanel.Controls.Add(_wideRightHeaderLabel);

            _wideTrendBtn = new Button
            {
                Text = "• Live Trend",
                FlatStyle = FlatStyle.Flat,
                Font = TabFontBold,
                Size = new Size(106, 24),
                Cursor = Cursors.Hand
            };
            _wideTrendBtn.FlatAppearance.BorderSize = 0;
            _wideTrendBtn.Click += (s, e) =>
            {
                _wideRightShowPerf = false;
                UpdateWideLayout();
            };
            _wideRightPanel.Controls.Add(_wideTrendBtn);

            _widePerfBtn = new Button
            {
                Text = "• Live Perf",
                FlatStyle = FlatStyle.Flat,
                Font = TabFontBold,
                Size = new Size(106, 24),
                Cursor = Cursors.Hand
            };
            _widePerfBtn.FlatAppearance.BorderSize = 0;
            _widePerfBtn.Click += (s, e) =>
            {
                _wideRightShowPerf = true;
                UpdateWideLayout();
            };
            _wideRightPanel.Controls.Add(_widePerfBtn);
            Controls.Add(_wideRightPanel);

            SizeChanged += (s, e) => UpdateWideLayout();
            UpdateWideLayout();
        }

        private int _testClientWidthOverride = 0;
        private int _testClientHeightOverride = 0;

        internal void TestApplyWideLayoutSize(int width, int height)
        {
            _testClientWidthOverride = width;
            _testClientHeightOverride = height;
            UpdateWideLayout();
        }

        private void ApplyDynamicVerticalLayout(int rawClientH)
        {
            if (_modeCardPanel == null || _optCardPanel == null)
                return;

            int bottom = Math.Max(760, rawClientH);

            // Bottom status bar (y = bottom - 29 .. bottom - 3)
            if (_statusBarLabel != null) _statusBarLabel.Location = new Point(14, bottom - 26);
            if (_allAutoBtn != null) _allAutoBtn.Location = new Point(850, bottom - 29);
            if (_closeBtn != null) _closeBtn.Location = new Point(966, bottom - 29);

            // Options section (header = 20px, card = 78px, 3px gap above status bar)
            int optHeaderY = bottom - 130;
            int optCardY = bottom - 110;
            if (_optHeaderLabel != null) _optHeaderLabel.Location = new Point(14, optHeaderY);
            _optCardPanel.Location = new Point(14, optCardY);
            if (_errorBanner != null) _errorBanner.Location = new Point(14, optCardY);

            // Middle zone ends 6px above Options header
            int middleBottom = optHeaderY - 6;
            const int modeCardTop = 364;

            // Manual Fan Control section (header = 20px, card = 84px) anchored above Options
            int fanHeaderY = middleBottom - 104;
            int fanCardY = middleBottom - 84;
            if (_manualFanHeaderLabel != null) _manualFanHeaderLabel.Location = new Point(14, fanHeaderY);
            if (_calibBadgeLabel != null) _calibBadgeLabel.Location = new Point(544, fanHeaderY - 2);
            if (_fanCardPanel != null) _fanCardPanel.Location = new Point(14, fanCardY);

            // Modes & Graph card expands dynamically to fill all available vertical space
            int modeCardBottom = _manualFanControlVisible ? (fanHeaderY - 6) : middleBottom;
            int cardHeight = Math.Max(260, modeCardBottom - modeCardTop);
            int canvasHeight = Math.Max(180, cardHeight - 68);
            int curveHeight = Math.Max(140, cardHeight - 132);
            int hintY = cardHeight - 22;

            if (_modeCardPanel.Height != cardHeight)
                _modeCardPanel.Height = cardHeight;

            if (_curveHintLabel != null)
                _curveHintLabel.Location = new Point(8, hintY);

            if (_curveCanvas != null)
            {
                _curveCanvas.Location = new Point(8, 106);
                _curveCanvas.Size = new Size(1056, curveHeight);
            }

            if (_osdConfigPanel != null)
            {
                _osdConfigPanel.Location = new Point(8, 41);
                _osdConfigPanel.Size = new Size(1056, canvasHeight + 1);
                _osdConfigPanel.UpdateScrollPanelHeight(canvasHeight + 1);
            }

            if (_liveCanvas != null && _liveCanvas.Parent == _modeCardPanel)
            {
                _liveCanvas.Location = new Point(8, 42);
                _liveCanvas.Size = new Size(1056, canvasHeight);
            }
            if (_perfCanvas != null && _perfCanvas.Parent == _modeCardPanel)
            {
                _perfCanvas.Location = new Point(8, 42);
                _perfCanvas.Size = new Size(1056, canvasHeight);
            }
        }

        private void UpdateWideLayout()
        {
            if (_wideRightPanel == null || _modeCardPanel == null || _liveCanvas == null || _perfCanvas == null)
                return;

            int clientW = _testClientWidthOverride > 0 ? _testClientWidthOverride : ClientSize.Width;
            int clientH = _testClientHeightOverride > 0 ? _testClientHeightOverride : ClientSize.Height;

            ApplyDynamicVerticalLayout(clientH);

            bool wide = clientW >= 1450;
            if (wide)
            {
                int rx = 1096;
                int rw = Math.Max(340, clientW - rx - 12);
                int rh = Math.Max(520, clientH - 24);
                _wideRightPanel.Location = new Point(rx, 10);
                _wideRightPanel.Size = new Size(rw, rh);
                if (_widePerfBtn != null) _widePerfBtn.Location = new Point(rw - 114, 7);
                if (_wideTrendBtn != null) _wideTrendBtn.Location = new Point(rw - 226, 7);

                if (_showingLiveTelemetry || _showingPerfGraph)
                {
                    // Keep the left column on the Fan Curve editor when wide mode docks Live/Perf on the right
                    _showingLiveTelemetry = false;
                    _showingPerfGraph = false;
                    SetPresetsAndCustomVisible(!_showingOsdConfig);
                    SetManualFanControlVisible(!_showingOsdConfig);
                    if (!_showingOsdConfig && _curveCanvas != null)
                    {
                        _curveCanvas.Visible = true;
                    }
                    UpdateCurveTabStyles();
                    UpdateCurveHint();
                }

                Control activeRight = _wideRightShowPerf ? (Control)_perfCanvas : (Control)_liveCanvas;
                Control inactiveRight = _wideRightShowPerf ? (Control)_liveCanvas : (Control)_perfCanvas;

                if (inactiveRight.Parent != _modeCardPanel)
                {
                    inactiveRight.Parent = _modeCardPanel;
                }
                inactiveRight.Visible = false;

                if (activeRight.Parent != _wideRightPanel)
                {
                    activeRight.Parent = _wideRightPanel;
                }
                activeRight.Location = new Point(8, 38);
                activeRight.Size = new Size(rw - 16, rh - 46);
                activeRight.Visible = true;
                activeRight.Invalidate();

                if (!_wideRightPanel.Visible) _wideRightPanel.Visible = true;
                UpdateWideRightButtonStyles();
            }
            else
            {
                if (_wideRightPanel.Visible) _wideRightPanel.Visible = false;
                // Collapse hidden wide panel bounds and reset scroll state so un-maximizing from full-screen
                // never leaves lingering horizontal or vertical scrollbars.
                _wideRightPanel.Bounds = new Rectangle(0, 0, 0, 0);

                if (_liveCanvas.Parent != _modeCardPanel) _liveCanvas.Parent = _modeCardPanel;
                _liveCanvas.Visible = _showingLiveTelemetry;

                if (_perfCanvas.Parent != _modeCardPanel) _perfCanvas.Parent = _modeCardPanel;
                _perfCanvas.Visible = _showingPerfGraph;

                ApplyDynamicVerticalLayout(clientH);

                if (AutoScroll)
                {
                    AutoScrollPosition = Point.Empty;
                    PerformLayout();
                }
            }
        }

        private void UpdateWideRightButtonStyles()
        {
            if (_wideTrendBtn == null || _widePerfBtn == null) return;
            ThemePalette pal = CurrentPalette;
            Win11VisualStyle.StylePillButton(_wideTrendBtn, pal, !_wideRightShowPerf);
            Win11VisualStyle.StylePillButton(_widePerfBtn, pal, _wideRightShowPerf);
        }

    }
}
