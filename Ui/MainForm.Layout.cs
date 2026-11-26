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
        private static readonly Font ToolTipChartFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);

        private int GetTooltipChartKind(Control c)
        {
            if (c == null) return 0;
            if (c == _cpuTempBox || c == _cpuBoxTitle || c == _cpuTempLabel) return 1;
            if (c == _gpuTempBox || c == _gpuBoxTitle || c == _gpuTempLabel) return 2;
            if (c == _nvmeTempBox || c == _nvmeBoxTitle || c == _nvmeTempLabel) return 6;
            if (c == _pwrBox || c == _pwrBoxTitle || c == _pwrLabel) return 3;
            if (c == _ramPowerBox || c == _ramBoxTitle || c == _ramPowerLabel) return 7;
            if (c == _ambientBox || c == _ambientBoxTitle || c == _ambientLabel) return 9;
            if (c == _fan1Box || c == _fan1BoxTitle || c == _fan1RpmLabel) return 4;
            if (c == _fan2Box || c == _fan2BoxTitle || c == _fan2RpmLabel) return 5;
            return 0;
        }

        private string _activePopupTooltipText = null;

        private string ResolveTooltipText(Control c)
        {
            if (c == null) return "";
            if (_lastStatus.CpuTemp > 0)
            {
                if (c == _cpuTempBox || c == _cpuBoxTitle || c == _cpuTempLabel)
                    return _lastStatus.FormatCpuTooltip(_sessionStats);
                if (c == _gpuTempBox || c == _gpuBoxTitle || c == _gpuTempLabel)
                    return _lastStatus.FormatIgpuTooltip(_sessionStats);
                if (c == _pwrBox || c == _pwrBoxTitle || c == _pwrLabel)
                    return _lastStatus.FormatPowerTooltip(_sessionStats);
                if (c == _fan1Box || c == _fan1BoxTitle || c == _fan1RpmLabel)
                    return _lastStatus.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm, _hw != null ? _hw.Profile : null);
                if (c == _fan2Box || c == _fan2BoxTitle || c == _fan2RpmLabel)
                    return _lastStatus.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans, _hw != null ? _hw.Profile : null);
            }
            if ((c == _nvmeTempBox || c == _nvmeBoxTitle || c == _nvmeTempLabel) && _lastStatus.NvmeTemp > 0)
                return _lastStatus.FormatNvmeTooltip(_sessionStats);
            if ((c == _ramPowerBox || c == _ramBoxTitle || c == _ramPowerLabel || c == _ramCard || c == _ramLabel) && _lastStatus.RamTotalGb > 0)
                return _lastStatus.FormatRamTooltip(_sessionStats);
            if ((c == _ambientBox || c == _ambientBoxTitle || c == _ambientLabel) && _lastStatus.AmbientTemp > 0)
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

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color bg = isRadeon ? Color.FromArgb(0x16, 0x0A, 0x0E) : (_isDarkMode ? Color.FromArgb(0x18, 0x18, 0x25) : Color.FromArgb(0xFF, 0xFF, 0xFF));
            Color border = isRadeon ? RadeonAccent : (_isDarkMode ? Color.FromArgb(0x45, 0x47, 0x5A) : Color.FromArgb(0x94, 0xA3, 0xB8));
            Color fg = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);

            Rectangle bounds = e.Bounds;
            using (SolidBrush bgBr = new SolidBrush(bg))
            {
                g.FillRectangle(bgBr, bounds);
            }
            using (SolidBrush topAccent = new SolidBrush(isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent)))
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
                Color lineCol;
                switch (chartKind)
                {
                    case 1:
                        lineCol = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
                        break;
                    case 2:
                        lineCol = isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E);
                        break;
                    case 6:
                        lineCol = _isDarkMode ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2);
                        break;
                    case 3:
                        lineCol = isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B);
                        break;
                    case 7:
                        lineCol = Color.FromArgb(0x81, 0x8C, 0xF8);
                        break;
                    case 9:
                        lineCol = _isDarkMode ? Color.FromArgb(0xE2, 0xE8, 0xF0) : Color.FromArgb(0x47, 0x55, 0x69);
                        break;
                    case 4:
                        lineCol = Color.FromArgb(0xC0, 0x84, 0xFC);
                        break;
                    case 5:
                        lineCol = Color.FromArgb(0x2D, 0xD4, 0xBF);
                        break;
                    default:
                        lineCol = isRadeon ? RadeonAccent : DarkAccent;
                        break;
                }
                DrawMiniSparklineChart(g, chartRect, samples, lineCol, unit, true, chartKind == 7 && !ramHasTemp);
            }
        }

        private void DrawMiniSparklineChart(Graphics g, Rectangle rect, List<int> samples, Color lineColor, string unit, bool showLabels, bool isTenths = false)
        {
            if (rect.Width < 12 || rect.Height < 6) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color chartBg = isRadeon ? Color.FromArgb(0x0D, 0x06, 0x08) : (_isDarkMode ? Color.FromArgb(0x11, 0x11, 0x1B) : Color.FromArgb(0xF1, 0xF5, 0xF9));
            Color gridCol = isRadeon ? Color.FromArgb(55, 0xED, 0x1C, 0x24) : (_isDarkMode ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(45, 0, 0, 0));

            if (showLabels)
            {
                using (SolidBrush bgBr = new SolidBrush(chartBg))
                {
                    g.FillRectangle(bgBr, rect);
                }
                using (Pen gridPen = new Pen(gridCol, 1f))
                {
                    int midY = rect.Y + rect.Height / 2;
                    g.DrawLine(gridPen, rect.X + 1, midY, rect.Right - 1, midY);
                    g.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }

            if (samples == null || samples.Count == 0) return;

            int minVal = int.MaxValue;
            int maxVal = int.MinValue;
            for (int i = 0; i < samples.Count; i++)
            {
                int v = samples[i];
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }

            // Auto-scale with a small pad so even a 1-2 degree/watt change renders a clear wave
            float scaleMin = minVal - 1.5f;
            float scaleMax = maxVal + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minVal + maxVal) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            int padX = showLabels ? 4 : 1;
            int padTop = showLabels ? 14 : 2;
            int padBot = showLabels ? 4 : 1;
            float plotX = rect.X + padX;
            float plotW = Math.Max(4, rect.Width - padX * 2);
            float plotY = rect.Y + padTop;
            float plotH = Math.Max(4, rect.Height - padTop - padBot);

            int count = samples.Count;
            PointF[] pts = new PointF[Math.Max(2, count)];
            if (count == 1)
            {
                float y = plotY + plotH * 0.5f;
                pts[0] = new PointF(plotX, y);
                pts[1] = new PointF(plotX + plotW, y);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    float x = plotX + (plotW * i) / (count - 1);
                    float norm = (samples[i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plotY + plotH * (1f - norm);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom - 1);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom - 1);

            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(showLabels ? 115 : 85, lineColor),
                Color.FromArgb(8, lineColor),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen linePen = new Pen(lineColor, showLabels ? 1.8f : 1.4f))
            {
                linePen.LineJoin = LineJoin.Round;
                g.DrawLines(linePen, pts);
            }

            PointF lastPt = pts[pts.Length - 1];
            using (SolidBrush dotBr = new SolidBrush(lineColor))
            {
                float r = showLabels ? 3f : 2f;
                g.FillEllipse(dotBr, lastPt.X - r, lastPt.Y - r, r * 2f, r * 2f);
            }

            if (showLabels)
            {
                Color labelFg = _isDarkMode ? Color.FromArgb(215, 255, 255, 255) : Color.FromArgb(215, 15, 23, 42);
                int curVal = samples[samples.Count - 1];
                string header = isTenths
                    ? string.Format("LIVE GRAPH ({0:0.0}–{1:0.0}{2}) • NOW {3:0.0}{2}", minVal / 10.0, maxVal / 10.0, unit, curVal / 10.0)
                    : string.Format("LIVE GRAPH ({0}–{1}{2}) • NOW {3}{2}", minVal, maxVal, unit, curVal);
                TextRenderer.DrawText(g, header, ToolTipChartFont, new Point(rect.X + 4, rect.Y + 1), labelFg);
            }
        }

        private void SetMetricDetail(int chartKind, string peakText, string subText, string detailText, string detailText2, Color subColor, Color detail2Color = default(Color))
        {
            MetricBoxDetail d;
            if (!_metricDetails.TryGetValue(chartKind, out d))
            {
                d = new MetricBoxDetail();
                _metricDetails[chartKind] = d;
            }
            d.PeakText = peakText ?? "";
            d.SubText = subText ?? "";
            d.DetailText = detailText ?? "";
            d.DetailText2 = detailText2 ?? "";
            d.SubColor = subColor;
            d.Detail2Color = detail2Color;
        }

        private void PaintMetricBoxSparkline(Graphics g, Panel box, Label titleLbl, Label valLbl, List<int> samples, int chartKind)
        {
            if (box == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = IsXmas;
            Color lineCol;
            if (isXmas)
            {
                // Strict White / Black / Red holiday harmony: alternate Festive Crimson Red & Jet Obsidian Black
                switch (chartKind)
                {
                    case 1: case 3: case 4: case 6:
                        lineCol = XmasRed;
                        break;
                    default:
                        lineCol = XmasBlack;
                        break;
                }
            }
            else
            {
                switch (chartKind)
                {
                    case 1:
                        lineCol = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
                        break;
                    case 2:
                        lineCol = isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E);
                        break;
                    case 6:
                        lineCol = _isDarkMode ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2);
                        break;
                    case 3:
                        lineCol = isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B);
                        break;
                    case 7:
                        lineCol = Color.FromArgb(0x81, 0x8C, 0xF8);
                        break;
                    case 9:
                        lineCol = _isDarkMode ? Color.FromArgb(0xE2, 0xE8, 0xF0) : Color.FromArgb(0x47, 0x55, 0x69);
                        break;
                    case 4:
                        lineCol = Color.FromArgb(0xC0, 0x84, 0xFC);
                        break;
                    case 5:
                        lineCol = Color.FromArgb(0x2D, 0xD4, 0xBF);
                        break;
                    default:
                        lineCol = isRadeon ? RadeonAccent : DarkAccent;
                        break;
                }
            }

            int cw = box.ClientSize.Width;
            int ch = box.ClientSize.Height;
            Color dimCol = isXmas ? XmasTextDim : (isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim)));
            Color textCol = isXmas ? XmasText : (isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText)));

            // Subtle top ambient sheen tinted with the sensor's signature color
            int sheenH = Math.Min(24, Math.Max(8, ch / 4));
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, cw), sheenH),
                Color.FromArgb(isXmas ? 14 : (_isDarkMode ? 26 : 15), lineCol),
                Color.FromArgb(0, lineCol),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(sheen, 1, 1, Math.Max(1, cw - 2), sheenH);
            }
            if (isXmas && cw > 90 && ch > 50)
            {
                // Delicate festive snowflake watermark in top-right of Xmas white sensor tiles
                Win11VisualStyle.DrawSnowflake(g, cw - 18f, 22f, 11f, Color.FromArgb(20, XmasRed), 1.1f);
            }

            // Sleek 3px left accent strip matching the metric series color
            using (SolidBrush accentBrush = new SolidBrush(lineCol))
            {
                g.FillRectangle(accentBrush, 0, 1, 3, Math.Max(1, ch - 2));
            }

            MetricBoxDetail detail;
            _metricDetails.TryGetValue(chartKind, out detail);

            // Row 1 (y = 3..17): Title (left) + Peak tracker (right)
            int peakW = 0;
            if (detail != null && !string.IsNullOrEmpty(detail.PeakText))
            {
                Size peakSz = TextRenderer.MeasureText(g, detail.PeakText, MetricPeakFont, new Size(cw - 14, 15), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                peakW = Math.Min(cw - 54, peakSz.Width + 4);
                Rectangle peakRect = new Rectangle(cw - 6 - peakW, 3, peakW, 14);
                TextRenderer.DrawText(g, detail.PeakText, MetricPeakFont, peakRect, dimCol,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            if (titleLbl != null && !string.IsNullOrEmpty(titleLbl.Text))
            {
                int titleMaxW = Math.Max(40, cw - 14 - peakW);
                Rectangle titleRect = new Rectangle(7, 3, titleMaxW, 14);
                TextRenderer.DrawText(g, titleLbl.Text, titleLbl.Font, titleRect, titleLbl.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 2 (y = 17..44): Hero Value (left) + Primary Live Sub-Stat (right)
            int valRight = 8;
            if (valLbl != null && !string.IsNullOrEmpty(valLbl.Text))
            {
                Size valSz = TextRenderer.MeasureText(g, valLbl.Text, valLbl.Font, new Size(cw - 14, 27), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int valW = Math.Min(cw - 14, valSz.Width + 2);
                Rectangle valRect = new Rectangle(7, 17, valW, 27);
                TextRenderer.DrawText(g, valLbl.Text, valLbl.Font, valRect, valLbl.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                valRight = 7 + valW + 6;
            }
            if (detail != null && !string.IsNullOrEmpty(detail.SubText) && cw - 6 - valRight >= 28)
            {
                Rectangle subRect = new Rectangle(valRight, 19, cw - 6 - valRight, 24);
                Color subFg = detail.SubColor.IsEmpty ? textCol : detail.SubColor;
                TextRenderer.DrawText(g, detail.SubText, MetricSubFont, subRect, subFg,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 3 (y = 44..60): Full-width Detail Line 1 (range / secondary live telemetry)
            if (detail != null && !string.IsNullOrEmpty(detail.DetailText))
            {
                Rectangle detailRect = new Rectangle(7, 44, Math.Max(20, cw - 13), 16);
                TextRenderer.DrawText(g, detail.DetailText, MetricDetailFont, detailRect, dimCol,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 4 (y = 60..76): Full-width Detail Line 2 (hardware / tuning profile / features)
            if (detail != null && !string.IsNullOrEmpty(detail.DetailText2))
            {
                Rectangle detailRect2 = new Rectangle(7, 60, Math.Max(20, cw - 13), 16);
                Color d2Fg = detail.Detail2Color.IsEmpty ? dimCol : detail.Detail2Color;
                TextRenderer.DrawText(g, detail.DetailText2, MetricDetailFont, detailRect2, d2Fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 5 (y = 77..ch-2): Wide, tall live sparkline
            if (samples != null && samples.Count > 0)
            {
                Rectangle sparkRect = new Rectangle(4, 77, Math.Max(20, cw - 6), Math.Max(14, ch - 79));
                bool isDeciWatts = (samples == _sessionStats.RamPowerHistory);
                DrawMiniSparklineChart(g, sparkRect, samples, lineCol, "", false, isDeciWatts);
            }
            if (_showingLiveTelemetry && _highlightedMetric == chartKind)
            {
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch), lineCol, 6, 2f);
            }
            else if (!SystemInformation.HighContrast)
            {
                Color subtleBdr = isXmas
                    ? Color.FromArgb(0x27, 0x27, 0x2A)
                    : (isRadeon
                        ? Color.FromArgb(0x46, 0x19, 0x22)
                        : (IsAb ? Color.FromArgb(0x20, 0x33, 0x28) : (_isDarkMode ? Color.FromArgb(0x2C, 0x30, 0x44) : Color.FromArgb(0xD8, 0xDF, 0xE8))));
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch), subtleBdr, 6, 1f);
            }
        }

        private static bool IsSliderValueChangeKey(Keys keyCode)
        {
            return keyCode == Keys.Left || keyCode == Keys.Right ||
                   keyCode == Keys.Up || keyCode == Keys.Down ||
                   keyCode == Keys.PageUp || keyCode == Keys.PageDown ||
                   keyCode == Keys.Home || keyCode == Keys.End;
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
            if (_cpuTempBox != null) _cpuTempBox.Invalidate();
            if (_gpuTempBox != null) _gpuTempBox.Invalidate();
            if (_nvmeTempBox != null) _nvmeTempBox.Invalidate();
            if (_pwrBox != null) _pwrBox.Invalidate();
            if (_ramPowerBox != null) _ramPowerBox.Invalidate();
            if (_ambientBox != null) _ambientBox.Invalidate();
            if (_fan1Box != null) _fan1Box.Invalidate();
            if (_fan2Box != null) _fan2Box.Invalidate();
        }

        private void OnMetricBoxKeyboardActivate(int metricKind)
        {
            if (_showingLiveTelemetry && _highlightedMetric == metricKind)
                SetGraphViewMode(false, 0);
            else
                SetGraphViewMode(true, metricKind);
        }

        private void OnMetricBoxMouseUp(int metricKind, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ResetSessionPeaks();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if (_showingLiveTelemetry && _highlightedMetric == metricKind)
                {
                    SetGraphViewMode(false, 0);
                }
                else
                {
                    SetGraphViewMode(true, metricKind);
                }
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
                Text = string.Format("EC Map: {0} • Driver: {1} • Global\\Access_EC Mutex • 64-bit", _sysInfo.MatchedProfile.DisplayName, initialDrv),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Regular),
                Location = new Point(74, 34),
                Size = new Size(358, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwSubLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwSubLabel);

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
            _themeToggleBtn.Click += (s, e) => CycleThemePreference();
            Controls.Add(_themeToggleBtn);
            _themeToggleBtn.BringToFront();

            // 1. Hardware & Live Telemetry Section (top = 64, card = 84..338, height = 254) — 2x4 Metric Grid
            Panel statusCard = CreateSectionCard("System Telemetry & Live Hardware Sensors", 64, 254);

            const int boxW = 257;
            const int boxH = 105;
            const int boxGapX = 6;
            const int row1Y = 7;
            const int row2Y = 118;

            // Row 1 (Core Silicon & Memory/Storage): CPU | iGPU | RAM | NVMe
            _cpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(12, row1Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _cpuBoxTitle = new Label
            {
                Text = "CPU",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _cpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _cpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _cpuTempBox, _cpuBoxTitle, _cpuTempLabel, _sessionStats.CpuHistory, 1);
            _cpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuTempBox.EnableKeyboardActivation("CPU Temperature", "Press Enter or Space to toggle CPU trend highlight on the live graph.");
            _cpuTempBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(1);
            statusCard.Controls.Add(_cpuTempBox);

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
            _uiToolTip.SetToolTip(_cpuTempBox, cpuTip);

            _gpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 1, row1Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _gpuBoxTitle = new Label
            {
                Text = "iGPU",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _gpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _gpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _gpuTempBox, _gpuBoxTitle, _gpuTempLabel, _sessionStats.GpuHistory, 2);
            _gpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuTempBox.EnableKeyboardActivation("iGPU Temperature", "Press Enter or Space to toggle iGPU trend highlight on the live graph.");
            _gpuTempBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(2);
            statusCard.Controls.Add(_gpuTempBox);

            string gpuTip = "Integrated Radeon Graphics Temperature (AMD ADL2 PMLog GFX/SoC Sensor)\r\nLeft-click to highlight iGPU Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_gpuTempBox, gpuTip);

            _ramPowerBox = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 2, row1Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _ramBoxTitle = new Label
            {
                Text = "RAM (DDR5)",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _ramPowerLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _ramPowerBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _ramPowerBox, _ramBoxTitle, _ramPowerLabel,
                (_sessionStats.RamTempHistory.Count > 0 ? _sessionStats.RamTempHistory : _sessionStats.RamPowerHistory), 7);
            _ramPowerBox.MouseUp += (s, e) => OnMetricBoxMouseUp(7, e);
            _ramPowerBox.EnableKeyboardActivation("RAM Temperature and Power", "Press Enter or Space to toggle RAM trend highlight on the live graph.");
            _ramPowerBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(7);
            statusCard.Controls.Add(_ramPowerBox);

            string ramPwrTip = "DDR5 Memory Temperature, Dual-Stick Delta, Capacity & Speed\r\nLeft-click to highlight RAM Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_ramPowerBox, ramPwrTip);

            _nvmeTempBox = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 3, row1Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _nvmeBoxTitle = new Label
            {
                Text = "NVMe",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _nvmeTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _nvmeTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _nvmeTempBox, _nvmeBoxTitle, _nvmeTempLabel, _sessionStats.NvmeHistory, 6);
            _nvmeTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(6, e);
            _nvmeTempBox.EnableKeyboardActivation("NVMe SSD Temperature", "Press Enter or Space to toggle NVMe trend highlight on the live graph.");
            _nvmeTempBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(6);
            statusCard.Controls.Add(_nvmeTempBox);

            string nvmeTip = "NVMe Storage Temperature, Health, Power & I/O Throughput\r\nLeft-click to highlight NVMe Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_nvmeTempBox, nvmeTip);

            // Row 2 (Power, Platform & Cooling): Power | Chassis / FCLK | APU Fan | Sys Fan
            _pwrBox = new DoubleBufferedPanel
            {
                Location = new Point(12, row2Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _pwrBoxTitle = new Label
            {
                Text = "POWER",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _pwrLabel = new Label
            {
                Text = "--W",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _pwrBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _pwrBox, _pwrBoxTitle, _pwrLabel, _sessionStats.PowerHistory, 3);
            _pwrBox.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrBox.EnableKeyboardActivation("APU Package Power", "Press Enter or Space to toggle APU Power trend highlight on the live graph.");
            _pwrBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(3);
            statusCard.Controls.Add(_pwrBox);

            string pwrTip = "APU Package Power (CPU + iGPU + SoC Telemetry & VRM Limits)\r\nLeft-click to highlight Power Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_pwrBox, pwrTip);

            _ambientBox = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 1, row2Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _ambientBoxTitle = new Label
            {
                Text = "CHASSIS",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _ambientLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _ambientBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _ambientBox, _ambientBoxTitle, _ambientLabel, _sessionStats.AmbientHistory, 9);
            _ambientBox.MouseUp += (s, e) => OnMetricBoxMouseUp(9, e);
            _ambientBox.EnableKeyboardActivation("Chassis Ambient Temperature", "Press Enter or Space to toggle Chassis Ambient trend highlight on the live graph.");
            _ambientBox.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(9);
            statusCard.Controls.Add(_ambientBox);

            string ambTip = "Motherboard Chassis Ambient Temperature, Infinity Fabric (FCLK) & Live FPS\r\nLeft-click to toggle Live Trend • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_ambientBox, ambTip);

            _fan1Box = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 2, row2Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan1BoxTitle = new Label
            {
                Text = "APU FAN",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _fan1RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _fan1Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan1Box, _fan1BoxTitle, _fan1RpmLabel, _sessionStats.Fan1RpmHistory, 4);
            _fan1Box.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1Box.EnableKeyboardActivation("APU Fan Speed", "Press Enter or Space to toggle APU Fan speed trend highlight on the live graph.");
            _fan1Box.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(4);
            statusCard.Controls.Add(_fan1Box);

            string fan1Tip = "APU / CPU Fan Tachometer & Active Duty\r\nLeft-click to highlight APU Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan1Box, fan1Tip);

            _fan2Box = new DoubleBufferedPanel
            {
                Location = new Point(12 + (boxW + boxGapX) * 3, row2Y),
                Size = new Size(boxW, boxH),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan2BoxTitle = new Label
            {
                Text = "SYS FAN",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(7, 3),
                Size = new Size(boxW - 14, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _fan2RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Location = new Point(7, 18),
                Size = new Size(boxW - 14, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _fan2Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan2Box, _fan2BoxTitle, _fan2RpmLabel, _sessionStats.Fan2RpmHistory, 5);
            _fan2Box.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2Box.EnableKeyboardActivation("System Fan Speed", "Press Enter or Space to toggle System Fan speed trend highlight on the live graph.");
            _fan2Box.KeyboardActivated += (s, e) => OnMetricBoxKeyboardActivate(5);
            statusCard.Controls.Add(_fan2Box);

            string fan2Tip = "System RAM / NVMe Fan Tachometer & Active Duty\r\nLeft-click to highlight Sys Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan2Box, fan2Tip);

            // Legacy RAM/SSD banner folded into the wider RAM & NVMe telemetry boxes above
            _ramCard = new DoubleBufferedPanel
            {
                Location = new Point(12, 200),
                Size = new Size(1046, 1),
                Visible = false
            };
            _ramLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Visible = false
            };

            // Row 3: Active Fan Modes & Duty Status
            Label modeStaticLbl = AddStaticLabel(statusCard, "Mode:", 12, 229, 52, 20);
            modeStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _modeLabel = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(62, 229),
                Size = new Size(266, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_modeLabel);

            Label apuStaticLbl = AddStaticLabel(statusCard, "APU Fan:", 332, 229, 68, 20);
            apuStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct1Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(400, 229),
                Size = new Size(226, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct1Label);

            Label sysStaticLbl = AddStaticLabel(statusCard, "Sys Fan:", 630, 229, 64, 20);
            sysStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct2Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(694, 229),
                Size = new Size(248, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct2Label);

            // 2. Modes & Fan Curve Section (top = 344, card = 364..730, height = 366; expands to 476 when Manual Fan hidden)
            Panel modeCard = CreateSectionCard("Modes, Fan Curves & Live Telemetry Graph", 344, 366);
            _modeCardPanel = modeCard;

            // Row 1 (y=8): Tab Selector Row (📈 Live Trend | 🎮 Live Perf | ● APU Fan | ● Sys Fan | 🖥 OSD Studio)
            _tabLiveGraphBtn = new Button
            {
                Text = "● Live Trend",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(8, 8),
                Size = new Size(184, 28),
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
                Location = new Point(196, 8),
                Size = new Size(184, 28),
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
                Location = new Point(384, 8),
                Size = new Size(184, 28),
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
                Location = new Point(572, 8),
                Size = new Size(184, 28),
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
                Location = new Point(760, 8),
                Size = new Size(184, 28),
                Cursor = Cursors.Hand
            };
            _tabOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _tabOsdConfigBtn.Click += (s, e) =>
            {
                SetOsdConfigViewMode(!_showingOsdConfig);
            };
            modeCard.Controls.Add(_tabOsdConfigBtn);

            // Row 2 (y=42): Built-in Mode Buttons for the currently selected fan tab
            string[] modeTitles = { "⚖ Balanced", "⚡ Performance", "🌙 Silent", "🤖 AI Workload" };
            for (int i = 0; i < 4; i++)
            {
                int code = i;
                Button btn = new Button
                {
                    Text = modeTitles[i],
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                    Location = new Point(8 + i * 234, 42),
                    Size = new Size(226, 30),
                    Padding = new Padding(0),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => ApplyMode(code);
                modeCard.Controls.Add(btn);
                _modeButtons[i] = btn;
            }

            // Row 3 (y=76): Custom Modes Row for the currently selected fan tab
            _customLabel = AddStaticLabel(modeCard, "Custom:", 8, 79, 68, 22);

            _customModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(78, 77),
                Size = new Size(380, 24)
            };
            _customModeCombo.SelectedIndexChanged += OnCustomComboSelected;
            modeCard.Controls.Add(_customModeCombo);

            _activateCustomBtn = new Button
            {
                Text = "Use Curve",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(466, 76),
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
                Location = new Point(580, 76),
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
                Location = new Point(734, 76),
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
                Location = new Point(838, 76),
                Size = new Size(100, 26),
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

            // OSD Overlay Configuration Studio Panel (covers rows 2, 3, 4 inside modeCard)
            _osdConfigPanel = new Panel
            {
                Location = new Point(8, 41),
                Size = new Size(1056, 409),
                Visible = false
            };
            BuildOsdStudioPanel(_osdConfigPanel);
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

            AddStaticLabel(fanCard, "APU / CPU Fan:", 10, 10, 120, 24);
            _fan1Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(134, 6),
                Size = new Size(642, 32),
                AccessibleName = "APU / CPU Fan Manual Speed Percentage (0 equals Auto)"
            };
            _fan1Slider.Scroll += (s, e) =>
            {
                _dragging1 = true;
                int v = _fan1Slider.Value;
                _fan1SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan1Slider.MouseCaptureChanged += (s, e) =>
            {
                if (_dragging1)
                {
                    _dragging1 = false;
                    ApplyFan(1, _fan1Slider.Value);
                }
            };
            _fan1Slider.MouseUp += (s, e) =>
            {
                if (_dragging1)
                {
                    _dragging1 = false;
                    ApplyFan(1, _fan1Slider.Value);
                }
            };
            _fan1Slider.KeyUp += (s, e) =>
            {
                if (IsSliderValueChangeKey(e.KeyCode))
                {
                    _fan1SliderPct.Text = _fan1Slider.Value == 0 ? "Auto" : string.Format("{0}%", _fan1Slider.Value);
                    ApplyFan(1, _fan1Slider.Value);
                }
            };
            fanCard.Controls.Add(_fan1Slider);

            _fan1SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(782, 10),
                Size = new Size(60, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan1SliderPct);

            _fan1AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 8),
                Size = new Size(80, 26),
                Cursor = Cursors.Hand
            };
            _fan1AutoBtn.FlatAppearance.BorderSize = 0;
            _fan1AutoBtn.Click += (s, e) => ApplyFan(1, 0);
            fanCard.Controls.Add(_fan1AutoBtn);

            AddStaticLabel(fanCard, "Sys / RAM Fan:", 10, 46, 120, 24);
            _fan2Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(134, 42),
                Size = new Size(642, 32),
                AccessibleName = "System / RAM Fan Manual Speed Percentage (0 equals Auto)"
            };
            _fan2Slider.Scroll += (s, e) =>
            {
                _dragging2 = true;
                int v = _fan2Slider.Value;
                _fan2SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan2Slider.MouseCaptureChanged += (s, e) =>
            {
                if (_dragging2)
                {
                    _dragging2 = false;
                    ApplyFan(2, _fan2Slider.Value);
                }
            };
            _fan2Slider.MouseUp += (s, e) =>
            {
                if (_dragging2)
                {
                    _dragging2 = false;
                    ApplyFan(2, _fan2Slider.Value);
                }
            };
            _fan2Slider.KeyUp += (s, e) =>
            {
                if (IsSliderValueChangeKey(e.KeyCode))
                {
                    _fan2SliderPct.Text = _fan2Slider.Value == 0 ? "Auto" : string.Format("{0}%", _fan2Slider.Value);
                    ApplyFan(2, _fan2Slider.Value);
                }
            };
            fanCard.Controls.Add(_fan2Slider);

            _fan2SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(782, 46),
                Size = new Size(60, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan2SliderPct);

            _fan2AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 44),
                Size = new Size(80, 26),
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
                Location = new Point(766, 9),
                AutoSize = true,
                Cursor = Cursors.Default
            };
            optCard.Controls.Add(_samplingLabel);

            _samplingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(854, 6),
                Size = new Size(204, 22),
                Cursor = Cursors.Hand
            };
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
                if (_osdToggleOverlayBtn != null)
                    _osdToggleOverlayBtn.Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden";
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
                Size = new Size(260, 22),
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
                RefreshStatus();
            };
            optCard.Controls.Add(_useFahrenheitCb);
            _uiToolTip.SetToolTip(_useFahrenheitCb, "Display all temperatures in Fahrenheit (°F) across telemetry cards, graphs, tray icons, and the OSD overlay.");

            _trayCb = new CheckBox
            {
                Text = "Close button minimizes to tray",
                Location = new Point(660, 52),
                Size = new Size(260, 22),
                Checked = _closeToTray
            };
            _trayCb.CheckedChanged += (s, e) =>
            {
                _closeToTray = _trayCb.Checked;
                SaveSettings();
            };
            optCard.Controls.Add(_trayCb);
            _uiToolTip.SetToolTip(_trayCb, "When enabled, clicking 'X' minimizes MiniFanControl to the system tray so your custom fan curves keep regulating temperatures in the background.\r\nTo quit and restore BIOS Auto, right-click the tray icon and select 'Exit (Restore BIOS Auto)'.");

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
            }
            if (_osdMetricsScrollPanel != null)
            {
                _osdMetricsScrollPanel.Size = new Size(1048, Math.Max(140, (canvasHeight + 1) - 198));
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
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = IsXmas;
            Color surface2 = isXmas ? XmasSurface2 : (isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2)));
            Color text = isXmas ? XmasText : (isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText)));
            Color accent = isXmas ? XmasRed : (isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent)));
            Color activeFg = (isXmas || isRadeon) ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            _wideTrendBtn.BackColor = !_wideRightShowPerf ? accent : surface2;
            _wideTrendBtn.ForeColor = !_wideRightShowPerf ? activeFg : text;
            _widePerfBtn.BackColor = _wideRightShowPerf ? accent : surface2;
            _widePerfBtn.ForeColor = _wideRightShowPerf ? activeFg : text;
        }

    }
}
