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
        private static void DrawInlineSparkline(Graphics g, Rectangle rect, List<int> samples, Color color, float sc)
        {
            if (samples == null || samples.Count == 0 || rect.Width < 8 || rect.Height < 4)
                return;

            int take = Math.Min(samples.Count, 36);
            int start = samples.Count - take;
            int minV = int.MaxValue, maxV = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int v = samples[start + i];
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            float scaleMin = minV - 1.5f;
            float scaleMax = maxV + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minV + maxV) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float y = rect.Y + rect.Height * 0.5f;
                pts[0] = new PointF(rect.X, y);
                pts[1] = new PointF(rect.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    float x = rect.X + (rect.Width * i) / (float)(take - 1);
                    float norm = (samples[start + i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = rect.Bottom - 1 - norm * (rect.Height - 2);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom);

            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(95, color),
                Color.FromArgb(6, color),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen p = new Pen(color, Math.Max(1.4f, 1.4f * sc)))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, pts);
            }
        }

        private static void DrawBackgroundSparkline(Graphics g, Rectangle rect, List<int> samples, Color color, float sc)
        {
            if (samples == null || samples.Count == 0 || rect.Width < 8 || rect.Height < 4)
                return;

            int take = Math.Min(samples.Count, 40);
            int start = samples.Count - take;
            int minV = int.MaxValue, maxV = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int v = samples[start + i];
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            float scaleMin = minV - 1.5f;
            float scaleMax = maxV + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minV + maxV) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float y = rect.Y + rect.Height * 0.5f;
                pts[0] = new PointF(rect.X, y);
                pts[1] = new PointF(rect.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    float x = rect.X + (rect.Width * i) / (float)(take - 1);
                    float norm = (samples[start + i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = rect.Bottom - 1 - norm * (rect.Height - 3);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom);

            // Subtle glowing underlay gradient: softly visible behind numbers with perfect text contrast
            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(45, color),
                Color.FromArgb(4, color),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen p = new Pen(Color.FromArgb(135, color), Math.Max(1.0f, 1.2f * sc)))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, pts);
            }
        }

        private class EndpointBadge
        {
            public float TargetY;
            public float DisplayY;
            public string Text;
            public Color Color;
            public bool Dashed;
        }

        private static void DrawEndpointBadges(Graphics g, Rectangle plot, Rectangle bounds, List<EndpointBadge> badges, Font font, float sc, bool isXmas = false)
        {
            if (badges == null || badges.Count == 0) return;

            badges.Sort((a, b) => a.TargetY.CompareTo(b.TargetY));
            int bh = (int)Math.Round(10 * sc);
            float minGap = Math.Max(1.5f, 1.5f * sc);
            float minSpacing = bh + minGap;

            float availTop = plot.Top + 1f;
            float availBottom = bounds.Bottom - Math.Max(3f, 3f * sc);
            float availH = Math.Max(bh, availBottom - availTop);

            if (badges.Count > 1 && badges.Count * minSpacing > availH)
            {
                float slotH = availH / badges.Count;
                int minBh = Math.Max(8, (int)Math.Round(8.5f * sc));
                bh = Math.Max(minBh, Math.Min(bh, (int)Math.Floor(slotH - 1f)));
                minSpacing = Math.Max(bh + 0.5f, slotH);
            }

            for (int i = 0; i < badges.Count; i++)
                badges[i].DisplayY = badges[i].TargetY;

            for (int i = 1; i < badges.Count; i++)
            {
                if (badges[i].DisplayY < badges[i - 1].DisplayY + minSpacing)
                    badges[i].DisplayY = badges[i - 1].DisplayY + minSpacing;
            }

            float maxBottom = availBottom - (bh / 2f);
            if (badges[badges.Count - 1].DisplayY > maxBottom)
            {
                badges[badges.Count - 1].DisplayY = maxBottom;
                for (int i = badges.Count - 2; i >= 0; i--)
                {
                    if (badges[i].DisplayY > badges[i + 1].DisplayY - minSpacing)
                        badges[i].DisplayY = badges[i + 1].DisplayY - minSpacing;
                }
            }

            float minTop = availTop + (bh / 2f);
            if (badges[0].DisplayY < minTop)
            {
                badges[0].DisplayY = minTop;
                for (int i = 1; i < badges.Count; i++)
                {
                    if (badges[i].DisplayY < badges[i - 1].DisplayY + minSpacing)
                        badges[i].DisplayY = badges[i - 1].DisplayY + minSpacing;
                }
            }

            // If still overflowing at the bottom (extremely dense stack), distribute evenly within [minTop, maxBottom]
            if (badges.Count > 1 && badges[badges.Count - 1].DisplayY > maxBottom && maxBottom > minTop)
            {
                float step = (maxBottom - minTop) / (badges.Count - 1);
                for (int i = 0; i < badges.Count; i++)
                    badges[i].DisplayY = minTop + i * step;
            }

            int rightMargin = (int)Math.Round(3 * sc);
            int badgeX = plot.Right + (int)Math.Round(2 * sc);
            int maxBadgeW = Math.Max(12, bounds.Right - badgeX - rightMargin);
            int clampMinY = (int)Math.Round(availTop);
            int clampMaxY = Math.Max(clampMinY, (int)Math.Floor(availBottom) - bh);

            using (SolidBrush bgBr = new SolidBrush(isXmas ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(232, 10, 15, 26)))
            {
                for (int i = 0; i < badges.Count; i++)
                {
                    EndpointBadge b = badges[i];
                    Size sz = TextRenderer.MeasureText(g, b.Text, font, new Size(maxBadgeW, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int bw = Math.Min(maxBadgeW, sz.Width + (int)Math.Round(4 * sc));
                    int by = (int)Math.Round(b.DisplayY - bh / 2f);
                    if (by < clampMinY) by = clampMinY;
                    if (by > clampMaxY) by = clampMaxY;

                    // Anchor badge comfortably inside bounds so it never clips or overflows on the right or bottom edge
                    int curX = Math.Min(badgeX, bounds.Right - bw - rightMargin);
                    if (curX < plot.Right + 1) curX = plot.Right + 1;
                    Rectangle badgeRect = new Rectangle(curX, by, bw, bh);

                    using (Pen tickPen = new Pen(Color.FromArgb(isXmas ? 180 : 135, b.Color), 1f))
                    {
                        g.DrawLine(tickPen, plot.Right, b.TargetY, curX, by + bh / 2f);
                    }

                    using (Pen borderPen = new Pen(Color.FromArgb(isXmas ? 210 : 165, b.Color), 1f))
                    {
                        if (b.Dashed) borderPen.DashStyle = DashStyle.Dash;
                        DrawRoundedRectangle(g, borderPen, bgBr, badgeRect, 2);
                    }
                    TextRenderer.DrawText(g, b.Text, font, badgeRect, b.Color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        private static void DrawLegendDot(Graphics g, ref int curX, int maxX, int y, string text, Color col, Font font)
        {
            Size sz = TextRenderer.MeasureText(g, text, font, new Size(60, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (curX + sz.Width > maxX) return;
            TextRenderer.DrawText(g, text, font, new Point(curX, y), col, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            curX += sz.Width + 4;
        }

        private static void DrawHoverHudBox(Graphics g, Rectangle rect, Rectangle plot, string text, Font font, int mouseX, int mouseY, float sc)
        {
            Size sz = TextRenderer.MeasureText(g, text, font, new Size(rect.Width - 8, 20), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int bw = Math.Min(rect.Width - 8, sz.Width + (int)Math.Round(8 * sc));
            int bh = (int)Math.Round(13 * sc);
            int bx = Math.Max(rect.X + 4, Math.Min(rect.Right - bw - 4, mouseX - bw / 2));
            int by = (mouseY < plot.Top + plot.Height / 2) ? (plot.Bottom - bh - 2) : (plot.Top + 2);
            Rectangle box = new Rectangle(bx, by, bw, bh);

            using (SolidBrush bg = new SolidBrush(Color.FromArgb(235, 15, 23, 42)))
            using (Pen borderPen = new Pen(Color.FromArgb(180, 56, 189, 248), 1f))
            {
                g.FillRectangle(bg, box);
                g.DrawRectangle(borderPen, box);
            }
            TextRenderer.DrawText(g, text, font, new Point(bx + (int)Math.Round(4 * sc), by + (int)Math.Round(1 * sc)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private void DrawCombinedOsdGraph(Graphics g, Rectangle rect, Color cpuCol, Color gpuCol, Color pwrCol, Color apuCol, Color sysCol, Color gridCol)
        {
            if (rect.Width < 20 || rect.Height < 16) return;
            float sc = GetScaleMultiplier();

            bool xmas = IsXmasTheme;
            bool afterburner = !xmas && MatchAppTheme && IsAfterburnerTheme;
            Color chartBg = xmas
                ? Color.White
                : (afterburner
                    ? Color.FromArgb(238, 10, 12, 16)
                    : ((MatchAppTheme && IsRadeonTheme)
                        ? Color.FromArgb(236, 10, 4, 6)
                        : Color.FromArgb(236, 9, 12, 18)));

            using (SolidBrush bgBr = new SolidBrush(chartBg))
            using (Pen borderPen = new Pen(gridCol, 1f))
            {
                g.FillRectangle(bgBr, rect);
                int midY = rect.Y + rect.Height / 2;
                g.DrawLine(borderPen, rect.X + 1, midY, rect.Right - 1, midY);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            if (Stats == null || Stats.CpuHistory == null || Stats.CpuHistory.Count == 0)
                return;

            // When ShowFpsGraph is active, FPS already has its own dedicated high-resolution graph directly underneath.
            // Decoupling FPS keeps the thermal/power graph on a dedicated ~30-75°C/W axis so high-FPS games (e.g. 500+ FPS)
            // never crush thermals and power into flat lines at the bottom!
            bool plotFpsHere = PlotFpsOnTrend && !ShowFpsGraph;

            int total = Stats.CpuHistory.Count;
            int take = Math.Min(total, 60);
            int start = total - take;

            int tMin = int.MaxValue, tMax = int.MinValue;
            int pMin = int.MaxValue, pMax = int.MinValue;
            int rMin = int.MaxValue, rMax = int.MinValue;
            bool showApuTrend = TrendApuFan && (!IsEffectivelyReadOnly() || _status.Fan1Rpm > 0);
            bool showSysTrend = HasDualFans && TrendSysFan && (!IsEffectivelyReadOnly() || _status.Fan2Rpm > 0);
            bool ramHasTemp = (Stats.RamTempHistory != null && Stats.RamTempHistory.Count > 0 && Stats.RamTempHistory[Stats.RamTempHistory.Count - 1] > 0);
            for (int i = 0; i < take; i++)
            {
                int idx = start + i;
                if (TrendCpu && idx < Stats.CpuHistory.Count)
                {
                    int v = Stats.CpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (TrendGpu && idx < Stats.GpuHistory.Count && Stats.GpuHistory[idx] > 0)
                {
                    int v = Stats.GpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (TrendNvme && idx < Stats.NvmeHistory.Count && Stats.NvmeHistory[idx] > 0)
                {
                    int v = Stats.NvmeHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (TrendPower && idx < Stats.PowerHistory.Count && Stats.PowerHistory[idx] > 0)
                {
                    int v = Stats.PowerHistory[idx];
                    if (v < pMin) pMin = v;
                    if (v > pMax) pMax = v;
                }
                if (TrendAmbient && idx < Stats.AmbientHistory.Count && Stats.AmbientHistory[idx] > 0)
                {
                    int v = Stats.AmbientHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (TrendRam)
                {
                    if (ramHasTemp && idx < Stats.RamTempHistory.Count && Stats.RamTempHistory[idx] > 0)
                    {
                        int v = Stats.RamTempHistory[idx];
                        if (v < tMin) tMin = v;
                        if (v > tMax) tMax = v;
                    }
                    else if (idx < Stats.RamPowerHistory.Count && Stats.RamPowerHistory[idx] > 0)
                    {
                        int v = (int)Math.Round(Stats.RamPowerHistory[idx] / 10.0);
                        if (v < pMin) pMin = v;
                        if (v > pMax) pMax = v;
                    }
                }
                if (showApuTrend && idx < Stats.Fan1RpmHistory.Count)
                {
                    int v = Stats.Fan1RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
                if (showSysTrend && idx < Stats.Fan2RpmHistory.Count)
                {
                    int v = Stats.Fan2RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
                if (plotFpsHere && (ShowFps || ShowLows) && idx < Stats.FpsHistory.Count && Stats.FpsHistory[idx] > 0)
                {
                    int v = Stats.FpsHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (plotFpsHere && ShowLows && idx < Stats.P01LowHistory.Count && Stats.P01LowHistory[idx] > 0)
                {
                    int v = Stats.P01LowHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
            }

            float leftLo = (tMin <= tMax) ? Math.Max(0f, tMin - 3f) : 20f;
            float leftHi = (tMin <= tMax) ? (tMax + 3f) : 90f;
            if (leftHi - leftLo < 8f)
            {
                float mid = (leftLo + leftHi) * 0.5f;
                leftLo = Math.Max(0f, mid - 4f);
                leftHi = leftLo + 8f;
            }

            // Independent normalized Y-axis for Power (W) so high desktop wattage (100-350W)
            // never squashes 35-70°C thermal traces into a flat band at the bottom.
            float pwrLo = (pMin <= pMax) ? Math.Max(0f, pMin - Math.Max(4f, (pMax - pMin) * 0.12f)) : 0f;
            float pwrHi = (pMin <= pMax) ? (pMax + Math.Max(4f, (pMax - pMin) * 0.12f)) : 100f;
            if (pwrHi - pwrLo < 10f)
            {
                float mid = (pwrLo + pwrHi) * 0.5f;
                pwrLo = Math.Max(0f, mid - 5f);
                pwrHi = pwrLo + 10f;
            }

            float rpmLo = (rMin <= rMax) ? Math.Max(0f, rMin - 150f) : 0f;
            float rpmHi = (rMin <= rMax) ? (rMax + 150f) : 3000f;
            if (rpmHi - rpmLo < 400f)
            {
                rpmLo = Math.Max(0f, rMin - 200f);
                rpmHi = rpmLo + 400f;
            }

            // Build active legend items upfront so we know whether the legend wraps to a 2nd line
            string tUnitHdr = TelemetrySessionStats.TempUnit(UseFahrenheit);
            string tempSuffix = (plotFpsHere && (ShowFps || ShowLows) && Stats.FpsHistory.Count > 0 && Stats.FpsHistory[Stats.FpsHistory.Count - 1] > 0) ? (tUnitHdr + "/FPS") : tUnitHdr;
            string hdr;
            if (tMin <= tMax && pMin <= pMax)
            {
                hdr = string.Format("TREND {0}–{1}{2} • {3}–{4}W",
                    TelemetrySessionStats.ToDisplayTemp(tMin, UseFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(tMax, UseFahrenheit),
                    tempSuffix, pMin, pMax);
            }
            else if (tMin <= tMax)
            {
                hdr = string.Format("TREND {0}–{1}{2}",
                    TelemetrySessionStats.ToDisplayTemp(tMin, UseFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(tMax, UseFahrenheit),
                    tempSuffix);
            }
            else if (pMin <= pMax)
            {
                hdr = string.Format("TREND {0}–{1}W", pMin, pMax);
            }
            else
            {
                hdr = (rMin <= rMax) ? string.Format("TREND {0}–{1} RPM", rMin, rMax) : "LIVE TREND";
            }

            string gpuLbl = _status.GpuShortLabel;
            Color ramCol = xmas ? XmasBlack : Color.FromArgb(0x81, 0x8C, 0xF8);
            Color nvmeCol = xmas ? XmasCharcoal : CfxTeal;
            Color ambCol = xmas ? Color.FromArgb(0x52, 0x52, 0x5B) : Color.FromArgb(0xE2, 0xE8, 0xF0);
            Color fpsTrendCol = xmas ? XmasBlack : Color.FromArgb(0x38, 0xBD, 0xF8);
            Color lowTrendCol = xmas ? XmasRedDeep : Color.FromArgb(0xFB, 0x71, 0x85);
            List<KeyValuePair<string, Color>> legendItems = new List<KeyValuePair<string, Color>>();
            if (TrendCpu) legendItems.Add(new KeyValuePair<string, Color>("CPU", cpuCol));
            if (TrendGpu) legendItems.Add(new KeyValuePair<string, Color>(gpuLbl, gpuCol));
            if (TrendRam && _status.RamTotalGb > 0) legendItems.Add(new KeyValuePair<string, Color>("RAM", ramCol));
            if (TrendNvme && _status.NvmeTemp > 0) legendItems.Add(new KeyValuePair<string, Color>("NVMe", nvmeCol));
            if (TrendPower) legendItems.Add(new KeyValuePair<string, Color>("PWR", pwrCol));
            if (TrendAmbient && _status.AmbientTemp > 0) legendItems.Add(new KeyValuePair<string, Color>("CHAS", ambCol));
            if (showApuTrend) legendItems.Add(new KeyValuePair<string, Color>("APU", apuCol));
            if (showSysTrend) legendItems.Add(new KeyValuePair<string, Color>("SYS", sysCol));

            int hdrLeftX = rect.X + (int)Math.Round(4 * sc);
            int hdrW = TextRenderer.MeasureText(g, hdr, _fontChart, new Size(280, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int maxLegX = rect.Right - (int)Math.Round(4 * sc);
            int legGap = Math.Max(3, (int)Math.Round(4 * sc));
            int dotSize = Math.Max(3, (int)Math.Round(3.5f * sc));
            int dotPad = Math.Max(2, (int)Math.Round(2f * sc));

            // Keep the TREND header strictly single-line so the plot keeps full vertical headroom for traces and badges
            int topPad = (int)Math.Round(16f * sc);
            int btmPad = Math.Max(3, (int)Math.Round(3f * sc));
            int rightPad = (int)Math.Round(38f * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - btmPad));

            List<EndpointBadge> badges = new List<EndpointBadge>();

            if (showSysTrend)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.Fan2RpmHistory, start, take, rpmLo, rpmHi, sysCol, false, true, sc);
                if (pt.HasValue && Stats.Fan2RpmHistory.Count > 0)
                {
                    int v = Stats.Fan2RpmHistory[Stats.Fan2RpmHistory.Count - 1];
                    if (v > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0.0}k", v / 1000.0), Color = sysCol, Dashed = true });
                }
            }
            if (showApuTrend)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.Fan1RpmHistory, start, take, rpmLo, rpmHi, apuCol, false, true, sc);
                if (pt.HasValue && Stats.Fan1RpmHistory.Count > 0)
                {
                    int v = Stats.Fan1RpmHistory[Stats.Fan1RpmHistory.Count - 1];
                    if (v > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0.0}k", v / 1000.0), Color = apuCol, Dashed = true });
                }
            }
            if (plotFpsHere && ShowLows && Stats.P01LowHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.P01LowHistory, start, take, leftLo, leftHi, lowTrendCol, false, false, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.P01LowHistory[Stats.P01LowHistory.Count - 1].ToString(), Color = lowTrendCol });
            }
            if (plotFpsHere && (ShowFps || ShowLows) && Stats.AvgFpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.AvgFpsHistory, start, take, leftLo, leftHi, fpsTrendCol, false, true, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = "Avg:" + Stats.AvgFpsHistory[Stats.AvgFpsHistory.Count - 1], Color = fpsTrendCol, Dashed = true });
            }
            if (plotFpsHere && ShowFps && Stats.FpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.FpsHistory, start, take, leftLo, leftHi, fpsTrendCol, false, false, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.FpsHistory[Stats.FpsHistory.Count - 1].ToString(), Color = fpsTrendCol });
            }
            if (TrendAmbient && _status.AmbientTemp > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.AmbientHistory, start, take, leftLo, leftHi, ambCol, false, false, sc);
                if (pt.HasValue && Stats.AmbientHistory.Count > 0 && Stats.AmbientHistory[Stats.AmbientHistory.Count - 1] > 0)
                    badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = TelemetrySessionStats.ToDisplayTemp(Stats.AmbientHistory[Stats.AmbientHistory.Count - 1], UseFahrenheit) + "°", Color = ambCol });
            }
            if (TrendRam && _status.RamTotalGb > 0)
            {
                if (ramHasTemp)
                {
                    PointF? pt = DrawCombinedSubSeries(g, plot, Stats.RamTempHistory, start, take, leftLo, leftHi, ramCol, false, false, sc);
                    if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = TelemetrySessionStats.ToDisplayTemp(Stats.RamTempHistory[Stats.RamTempHistory.Count - 1], UseFahrenheit) + "°", Color = ramCol });
                }
                else if (_status.RamPowerWatts > 0)
                {
                    PointF? pt = DrawCombinedSubSeries(g, plot, Stats.RamPowerHistory, start, take, pwrLo * 10f, pwrHi * 10f, ramCol, false, true, sc);
                    if (pt.HasValue && Stats.RamPowerHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0}W", Stats.RamPowerHistory[Stats.RamPowerHistory.Count - 1] / 10.0), Color = ramCol, Dashed = true });
                }
            }
            if (TrendPower)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.PowerHistory, start, take, pwrLo, pwrHi, pwrCol, false, true, sc);
                if (pt.HasValue && Stats.PowerHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.PowerHistory[Stats.PowerHistory.Count - 1] + "W", Color = pwrCol, Dashed = true });
            }
            if (TrendNvme && _status.NvmeTemp > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.NvmeHistory, start, take, leftLo, leftHi, nvmeCol, false, false, sc);
                if (pt.HasValue && Stats.NvmeHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = TelemetrySessionStats.ToDisplayTemp(Stats.NvmeHistory[Stats.NvmeHistory.Count - 1], UseFahrenheit) + "°", Color = nvmeCol });
            }
            if (TrendGpu)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.GpuHistory, start, take, leftLo, leftHi, gpuCol, false, false, sc);
                if (pt.HasValue && Stats.GpuHistory.Count > 0 && Stats.GpuHistory[Stats.GpuHistory.Count - 1] > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = TelemetrySessionStats.ToDisplayTemp(Stats.GpuHistory[Stats.GpuHistory.Count - 1], UseFahrenheit) + "°", Color = gpuCol });
            }
            if (TrendCpu)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.CpuHistory, start, take, leftLo, leftHi, cpuCol, true, false, sc);
                if (pt.HasValue && Stats.CpuHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = TelemetrySessionStats.ToDisplayTemp(Stats.CpuHistory[Stats.CpuHistory.Count - 1], UseFahrenheit) + "°", Color = cpuCol });
            }

            DrawEndpointBadges(g, plot, rect, badges, _fontTrace, sc, xmas);

            // Single-Line Legend & Scale Header inside the top of the Combined OSD Graph
            int hdrY = rect.Y + (int)Math.Round(2 * sc);
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(hdrLeftX, hdrY), xmas ? XmasBlack : Color.FromArgb(220, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int legX = hdrLeftX + hdrW + (int)Math.Round(6 * sc);
            int legY = hdrY;
            int hdrLineH = TextRenderer.MeasureText(g, "A", _fontTrace, new Size(30, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            int dotY = legY + Math.Max(1, (hdrLineH - dotSize) / 2);

            for (int li = 0; li < legendItems.Count; li++)
            {
                string ltxt = legendItems[li].Key;
                Color lcol = legendItems[li].Value;
                int iw = dotSize + dotPad + TextRenderer.MeasureText(g, ltxt, _fontTrace, new Size(90, 14), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

                int remaining = legendItems.Count - li;
                int reserveMoreW = (remaining > 1) ? TextRenderer.MeasureText(g, "+" + remaining, _fontTrace, new Size(36, 14), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + legGap : 0;

                if (legX + iw > maxLegX - (remaining > 1 ? 0 : 0))
                {
                    string moreTxt = "+" + remaining;
                    int moreW = TextRenderer.MeasureText(g, moreTxt, _fontTrace, new Size(36, 14), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    if (legX + moreW <= maxLegX)
                    {
                        TextRenderer.DrawText(g, moreTxt, _fontTrace, new Point(legX, legY), xmas ? Color.FromArgb(140, 0x18, 0x18, 0x1B) : Color.FromArgb(150, 148, 163, 184), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    }
                    break;
                }

                // If drawing this item leaves no room for a "+N" indicator for subsequent items that won't fit, check next item
                if (remaining > 1)
                {
                    string nextTxt = legendItems[li + 1].Key;
                    int nextIw = dotSize + dotPad + TextRenderer.MeasureText(g, nextTxt, _fontTrace, new Size(90, 14), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    if (legX + iw + legGap + nextIw > maxLegX && legX + iw + reserveMoreW > maxLegX)
                    {
                        string moreTxt = "+" + remaining;
                        int moreW = TextRenderer.MeasureText(g, moreTxt, _fontTrace, new Size(36, 14), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                        if (legX + moreW <= maxLegX)
                        {
                            TextRenderer.DrawText(g, moreTxt, _fontTrace, new Point(legX, legY), xmas ? Color.FromArgb(140, 0x18, 0x18, 0x1B) : Color.FromArgb(150, 148, 163, 184), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                        }
                        break;
                    }
                }

                using (SolidBrush dotBr = new SolidBrush(lcol))
                {
                    g.FillEllipse(dotBr, legX, dotY, dotSize, dotSize);
                }
                TextRenderer.DrawText(g, ltxt, _fontTrace, new Point(legX + dotSize + dotPad, legY), lcol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                legX += iw + legGap;
            }

            // Interactive Hover Scrubbing Crosshair & Readout Card
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(xmas ? Color.FromArgb(165, 0x18, 0x18, 0x1B) : Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float norm = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                norm = Math.Max(0f, Math.Min(1f, norm));
                int sampleIdx = start + (int)Math.Round(norm * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int samplesAgo = take - 1 - (sampleIdx - start);
                int c = sampleIdx < Stats.CpuHistory.Count ? Stats.CpuHistory[sampleIdx] : 0;
                int g_ = (sampleIdx < Stats.GpuHistory.Count) ? Stats.GpuHistory[sampleIdx] : 0;
                int n = (Stats.NvmeHistory != null && sampleIdx < Stats.NvmeHistory.Count) ? Stats.NvmeHistory[sampleIdx] : 0;
                int p = (sampleIdx < Stats.PowerHistory.Count) ? Stats.PowerHistory[sampleIdx] : 0;
                int f1 = (sampleIdx < Stats.Fan1RpmHistory.Count) ? Stats.Fan1RpmHistory[sampleIdx] : 0;
                int f2 = (sampleIdx < Stats.Fan2RpmHistory.Count) ? Stats.Fan2RpmHistory[sampleIdx] : 0;
                int rTemp = (Stats.RamTempHistory != null && sampleIdx < Stats.RamTempHistory.Count) ? Stats.RamTempHistory[sampleIdx] : 0;
                int rLoad = (Stats.RamLoadHistory != null && sampleIdx < Stats.RamLoadHistory.Count) ? Stats.RamLoadHistory[sampleIdx] : _status.RamLoadPct;
                int amb = (Stats.AmbientHistory != null && sampleIdx < Stats.AmbientHistory.Count) ? Stats.AmbientHistory[sampleIdx] : 0;

                string rTempStr = (_status.DdrStickCount == 2 && _status.DdrTemp1 > 0 && _status.DdrTemp2 > 0)
                    ? string.Format("{0}/{1}°", TelemetrySessionStats.ToDisplayTemp(_status.DdrTemp1, UseFahrenheit), TelemetrySessionStats.ToDisplayTemp(_status.DdrTemp2, UseFahrenheit))
                    : (rTemp > 0 ? string.Format("{0}°", TelemetrySessionStats.ToDisplayTemp(rTemp, UseFahrenheit)) : "");
                int ddrSpd = _status.RamSpeedMhz;
                string rStr = !string.IsNullOrEmpty(rTempStr)
                    ? (ddrSpd > 0 ? string.Format("{0} {1}%@{2}M", rTempStr, rLoad, ddrSpd) : string.Format("{0} {1}%", rTempStr, rLoad))
                    : (ddrSpd > 0 ? string.Format("{0}%@{1}M", rLoad, ddrSpd) : string.Format("{0}%", rLoad));

                string hud = string.Format("{0} | CPU {1}° • {2} {3}° • RAM {4} • NVMe {5}° • PWR {6}W",
                    FormatSampleTimeAgo(samplesAgo),
                    TelemetrySessionStats.ToDisplayTemp(c, UseFahrenheit),
                    gpuLbl,
                    TelemetrySessionStats.ToDisplayTemp(g_, UseFahrenheit),
                    rStr,
                    TelemetrySessionStats.ToDisplayTemp(n, UseFahrenheit), p);
                if (ShowAmbient && amb > 0) hud += string.Format(" • CHAS {0}°", TelemetrySessionStats.ToDisplayTemp(amb, UseFahrenheit));
                if (f1 > 0) hud += string.Format(" • APU {0}", f1);
                if (HasDualFans && ShowSysFan && f2 > 0) hud += string.Format(" • SYS {0}", f2);

                DrawHoverHudBox(g, rect, plot, hud, _fontTrace, _hoverMouseX, _hoverMouseY, sc);
            }
        }

        private static PointF? DrawCombinedSubSeries(Graphics g, Rectangle plot, List<int> list, int start, int take, float lo, float hi, Color col, bool fillArea, bool dashed, float sc)
        {
            if (list == null || list.Count == 0 || take <= 0 || plot.Width < 8 || plot.Height < 4) return null;
            try
            {
                float span = Math.Max(1f, hi - lo);
                int count = Math.Min(take, list.Count);
                int listStart = list.Count - count;

                PointF[] pts = new PointF[Math.Max(2, count)];
                if (count == 1)
                {
                    int val = list[list.Count - 1];
                    float norm = (val - lo) / span;
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plot.Bottom - norm * plot.Height;
                    pts[0] = new PointF(plot.Left, y);
                    pts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        int idx = listStart + i;
                        float x = (count == take)
                            ? (plot.Left + (plot.Width * i) / (float)(take - 1))
                            : (plot.Right - (plot.Width * (count - 1 - i)) / (float)(take - 1));
                        float norm = (list[idx] - lo) / span;
                        norm = Math.Max(0f, Math.Min(1f, norm));
                        float y = plot.Bottom - norm * plot.Height;
                        pts[i] = new PointF(x, y);
                    }
                }

                if (fillArea && pts.Length >= 2)
                {
                    PointF[] poly = new PointF[pts.Length + 2];
                    Array.Copy(pts, 0, poly, 0, pts.Length);
                    poly[pts.Length] = new PointF(pts[pts.Length - 1].X, plot.Bottom);
                    poly[pts.Length + 1] = new PointF(pts[0].X, plot.Bottom);
                    try
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(
                            new Rectangle(plot.X, plot.Y - 2, Math.Max(1, plot.Width), Math.Max(1, plot.Height + 4)),
                            Color.FromArgb(65, col),
                            Color.FromArgb(4, col),
                            LinearGradientMode.Vertical))
                        {
                            g.FillPolygon(br, poly);
                        }
                    }
                    catch { }
                }

                if (pts.Length >= 2)
                {
                    using (Pen pen = new Pen(col, (fillArea ? 1.8f : 1.4f) * Math.Max(1f, sc * 0.9f)))
                    {
                        pen.LineJoin = LineJoin.Round;
                        if (dashed) pen.DashStyle = DashStyle.Dash;
                        g.DrawLines(pen, pts);
                    }
                }
                return pts[pts.Length - 1];
            }
            catch (Exception seriesEx) { LogPaintErrorOnce("DrawCombinedSubSeries", seriesEx); return null; }
        }

        private void DrawFpsOsdGraph(Graphics g, Rectangle rect, Color border, float sc)
        {
            if (rect.Width <= 4 || rect.Height <= 4) return;
            bool xmas = IsXmasTheme;

            using (Brush bgBr = new SolidBrush(xmas ? Color.White : Color.FromArgb(170, 15, 23, 42)))
            using (Pen borderPen = new Pen(border, 1f))
            {
                g.FillRectangle(bgBr, rect);
                int midY = rect.Y + rect.Height / 2;
                g.DrawLine(borderPen, rect.X + 1, midY, rect.Right - 1, midY);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            int topPad = (int)Math.Round(14f * sc);
            int rightPad = (int)Math.Round(38f * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - 2));

            if (Stats == null || Stats.FpsHistory == null || Stats.FpsHistory.Count == 0)
            {
                TextRenderer.DrawText(g, "FPS & FRAMETIME (Standby)", _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), xmas ? XmasBlack : Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                return;
            }

            int total = Stats.FpsHistory.Count;
            int take = Math.Min(total, 60);
            int start = total - take;

            int fMin = int.MaxValue, fMax = int.MinValue;
            int ftMin = int.MaxValue, ftMax = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int idx = start + i;
                if (idx < Stats.FpsHistory.Count && Stats.FpsHistory[idx] > 0)
                {
                    int f = Stats.FpsHistory[idx];
                    if (f < fMin) fMin = f;
                    if (f > fMax) fMax = f;
                }
                if (idx < Stats.P01LowHistory.Count && Stats.P01LowHistory[idx] > 0)
                {
                    int f = Stats.P01LowHistory[idx];
                    if (f < fMin) fMin = f;
                    if (f > fMax) fMax = f;
                }
                if (idx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[idx] > 0)
                {
                    int ft = Stats.FrametimeHistory[idx];
                    if (ft < ftMin) ftMin = ft;
                    if (ft > ftMax) ftMax = ft;
                }
            }

            float fpsLo = (fMin <= fMax) ? Math.Max(0f, fMin - 4f) : 0f;
            float fpsHi = (fMin <= fMax) ? (fMax + 4f) : 100f;
            if (fpsHi - fpsLo < 15f)
            {
                float mid = (fpsLo + fpsHi) * 0.5f;
                fpsLo = Math.Max(0f, mid - 8f);
                fpsHi = fpsLo + 16f;
            }

            // Draw subtle 60 FPS reference guide line if inside range
            if (60f >= fpsLo && 60f <= fpsHi)
            {
                float norm60 = (60f - fpsLo) / (fpsHi - fpsLo);
                float y60 = plot.Bottom - norm60 * plot.Height;
                using (Pen guidePen = new Pen(xmas ? Color.FromArgb(45, 0x18, 0x18, 0x1B) : Color.FromArgb(45, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(guidePen, plot.Left, y60, plot.Right, y60);
                }
            }

            Color fpsCol = xmas ? XmasRed : Color.FromArgb(0x38, 0xBD, 0xF8);      // Crimson / Cyan
            Color lowCol = xmas ? XmasRedDeep : Color.FromArgb(0xFB, 0x71, 0x85);  // Deep Red / Coral
            Color ftCol  = xmas ? XmasBlack : Color.FromArgb(0xFB, 0xBF, 0x24);    // Jet Black / Amber Gold

            // Frametime range (stored as 0.1ms, so 33.3ms is 333)
            float ftLo = 0f;
            float ftHi = (ftMax > 0) ? Math.Max(333f, ftMax + 40f) : 333f;

            List<EndpointBadge> badges = new List<EndpointBadge>();

            // 1. Frametime (ms) plotted as amber line
            if (Stats.FrametimeHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.FrametimeHistory, start, take, ftLo, ftHi, ftCol, false, true, sc);
                if (pt.HasValue)
                {
                    double ftVal = Stats.FrametimeHistory[Stats.FrametimeHistory.Count - 1] / 10.0;
                    badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0.0}ms", ftVal), Color = ftCol, Dashed = true });
                }
            }

            // 2. 0.1% Lows plotted with subtle translucent fill underneath
            if (Stats.P01LowHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.P01LowHistory, start, take, fpsLo, fpsHi, lowCol, true, false, sc);
                if (pt.HasValue)
                {
                    int lowVal = Stats.P01LowHistory[Stats.P01LowHistory.Count - 1];
                    if (lowVal > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("Low:{0}", lowVal), Color = lowCol });
                }
            }

            // 3. Avg FPS plotted as dashed line
            if (Stats.AvgFpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.AvgFpsHistory, start, take, fpsLo, fpsHi, fpsCol, false, true, sc);
                if (pt.HasValue)
                {
                    int avgVal = Stats.AvgFpsHistory[Stats.AvgFpsHistory.Count - 1];
                    if (avgVal > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("Avg:{0}", avgVal), Color = fpsCol, Dashed = true });
                }
            }

            // 4. Live FPS plotted as solid bright line
            if (Stats.FpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.FpsHistory, start, take, fpsLo, fpsHi, fpsCol, false, false, sc);
                if (pt.HasValue)
                {
                    int liveVal = Stats.FpsHistory[Stats.FpsHistory.Count - 1];
                    if (liveVal > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = liveVal.ToString(), Color = fpsCol });
                }
            }

            DrawEndpointBadges(g, plot, rect, badges, _fontTrace, sc, xmas);

            // Legend / Scale Header
            string hdr;
            if (fMin <= fMax && _status.RtssFps > 0)
            {
                double curFt = _status.RtssFrametimeMs > 0 ? _status.RtssFrametimeMs : (1000.0 / _status.RtssFps);
                hdr = string.Format("FPS & FT ({0}–{1} • {2:0.0}ms)", fMin, fMax, curFt);
            }
            else
            {
                hdr = "FPS & FRAMETIME";
            }
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), xmas ? XmasBlack : Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int legX = rect.X + (int)Math.Round(4 * sc) + TextRenderer.MeasureText(g, hdr, _fontChart, new Size(240, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + (int)Math.Round(6 * sc);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● FPS", fpsCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● FT", ftCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● Avg", fpsCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● 0.1%", lowCol, _fontTrace);

            // Interactive Hover Scrubbing Crosshair & Readout Card
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(xmas ? Color.FromArgb(165, 0x18, 0x18, 0x1B) : Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float norm = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                norm = Math.Max(0f, Math.Min(1f, norm));
                int sampleIdx = start + (int)Math.Round(norm * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int secAgo = (int)Math.Round((take - 1 - (sampleIdx - start)) * 1.5);
                int f = (sampleIdx < Stats.FpsHistory.Count) ? Stats.FpsHistory[sampleIdx] : 0;
                double ft = (sampleIdx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[sampleIdx] > 0)
                    ? Stats.FrametimeHistory[sampleIdx] / 10.0
                    : (f > 0 ? 1000.0 / f : 0.0);
                int avg = (sampleIdx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[sampleIdx] : 0;
                int low = (sampleIdx < Stats.P01LowHistory.Count) ? Stats.P01LowHistory[sampleIdx] : 0;

                string hud = string.Format("{0} | FPS {1} • FT {2:0.0}ms • Avg {3} • 0.1% {4}",
                    secAgo == 0 ? "Now" : "-" + secAgo + "s", f, ft, avg, low > 0 ? low.ToString() : "--");

                DrawHoverHudBox(g, rect, plot, hud, _fontTrace, _hoverMouseX, _hoverMouseY, sc);
            }
        }

        private void DrawDfFrametimeGraph(Graphics g, Rectangle rect, float sc, bool compactCorner = false)
        {
            if (rect.Width <= 10 || rect.Height <= 10) return;
            bool xmas = IsXmasTheme;

            Color bgCol = xmas
                ? (compactCorner ? Color.FromArgb(248, 255, 255, 255) : Color.White)
                : (compactCorner ? Color.FromArgb(248, 8, 12, 18) : Color.FromArgb(236, 9, 12, 18));
            Color brdCol = xmas
                ? (compactCorner ? XmasRed : XmasBlack)
                : (compactCorner ? Color.FromArgb(195, 0x22, 0xD3, 0xEE) : Color.FromArgb(220, 255, 255, 255));
            using (SolidBrush bgBr = new SolidBrush(bgCol))
            using (Pen borderPen = new Pen(brdCol, 1.2f))
            {
                g.FillRectangle(bgBr, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            double curFt = _status.RtssFrametimeMs > 0
                ? _status.RtssFrametimeMs
                : (_status.RtssFps > 0
                    ? (1000.0 / _status.RtssFps)
                    : ((Stats != null && Stats.FrametimeHistory != null && Stats.FrametimeHistory.Count > 0)
                        ? Stats.FrametimeHistory[Stats.FrametimeHistory.Count - 1] / 10.0
                        : 0));

            // Bold Header: FRAME-TIME (MS) with live readout
            string ftHdr = compactCorner
                ? ((curFt > 0) ? string.Format("FT {0:0.0}ms", curFt) : "FRAME-TIME")
                : ((curFt > 0)
                    ? string.Format("FRAME-TIME (MS)  •  NOW: {0:0.0} ms", curFt)
                    : "FRAME-TIME (MS)");
            TextRenderer.DrawText(g, ftHdr, _fontDfHdr, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + (int)Math.Round(1.5f * sc)), IsXmasTheme ? XmasRed : (compactCorner ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.White), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int topPad = (int)Math.Round((compactCorner ? 12 : 14) * sc);
            int btmPad = (int)Math.Round((compactCorner ? 7 : 8) * sc);
            int rightPad = (int)Math.Round((compactCorner ? 24 : 32) * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - btmPad));

            int targetFtCeiling = CalculateDfFrametimeCeiling(curFt, (Stats != null) ? Stats.FrametimeHistory : null);
            if (targetFtCeiling > _currentFtCeiling)
            {
                // Immediate scale-out on stutter/spike so it is never clipped
                _currentFtCeiling = targetFtCeiling;
                _ftCeilingHoldCounter = 0;
            }
            else if (targetFtCeiling < _currentFtCeiling)
            {
                // Hold scaled-out view for 12 updates before zooming back in
                _ftCeilingHoldCounter++;
                if (_ftCeilingHoldCounter >= 12)
                {
                    _currentFtCeiling = targetFtCeiling;
                    _ftCeilingHoldCounter = 0;
                }
            }
            else
            {
                _ftCeilingHoldCounter = 0;
            }
            int ftCeiling = _currentFtCeiling;

            // Vertical center dividing line inside plot
            int midX = plot.Left + plot.Width / 2;
            using (Pen divPen = new Pen(xmas ? Color.FromArgb(55, 0x18, 0x18, 0x1B) : Color.FromArgb(140, 255, 255, 255), 1f))
            {
                g.DrawLine(divPen, midX, plot.Top, midX, plot.Bottom);
            }
            // Vertical right border line closing the plot area
            using (Pen boundPen = new Pen(xmas ? Color.FromArgb(130, 0x18, 0x18, 0x1B) : Color.FromArgb(180, 255, 255, 255), 1f))
            {
                g.DrawLine(boundPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
            }

            // Subtle dashed midpoint horizontal reference line + 16.6ms (60 FPS) & 33.3ms (30 FPS) pacing guides
            using (Pen midGridPen = new Pen(xmas ? Color.FromArgb(35, 0x18, 0x18, 0x1B) : Color.FromArgb(45, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
            {
                float midY = plot.Top + plot.Height / 2f;
                g.DrawLine(midGridPen, plot.Left, midY, plot.Right, midY);
            }
            if (16.6f <= ftCeiling)
            {
                float y16 = plot.Bottom - (16.6f / ftCeiling) * plot.Height;
                using (Pen p16 = new Pen(xmas ? Color.FromArgb(80, 0x18, 0x18, 0x1B) : Color.FromArgb(65, 0x22, 0xC5, 0x5E), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(p16, plot.Left, y16, plot.Right, y16);
                }
            }
            if (33.3f <= ftCeiling)
            {
                float y33 = plot.Bottom - (33.3f / ftCeiling) * plot.Height;
                using (Pen p33 = new Pen(xmas ? Color.FromArgb(80, 0xDC, 0x26, 0x26) : Color.FromArgb(65, 0xF5, 0x9E, 0x0B), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(p33, plot.Left, y33, plot.Right, y33);
                }
            }

            // Right Axis Labels: ftCeiling (top), mid (middle), 0 (bottom) - Standard upward stutter scale
            string lblMax = ftCeiling.ToString();
            string lblMid = (ftCeiling / 2).ToString();
            string lbl0 = "0";

            Size szMax = TextRenderer.MeasureText(g, lblMax, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Size szMid = TextRenderer.MeasureText(g, lblMid, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Size sz0 = TextRenderer.MeasureText(g, lbl0, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int yMax = Math.Max(rect.Y + 2, (int)Math.Round(plot.Top - szMax.Height / 2f));
            int yMid = (int)Math.Round(plot.Top + plot.Height / 2f - szMid.Height / 2f);
            int y0Margin = Math.Max(3, (int)Math.Round((compactCorner ? 4 : 3) * sc));
            int y0 = Math.Min(rect.Bottom - y0Margin - sz0.Height, (int)Math.Round(plot.Bottom - sz0.Height / 2f));

            Color ftAxisCol = IsXmasTheme ? XmasBlack : Color.White;
            TextRenderer.DrawText(g, lblMax, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), yMax), ftAxisCol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (!compactCorner || plot.Height >= (int)Math.Round(34 * sc))
            {
                TextRenderer.DrawText(g, lblMid, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), yMid), ftAxisCol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, lbl0, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), y0), ftAxisCol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (Stats == null || Stats.FrametimeHistory == null || Stats.FrametimeHistory.Count == 0)
                return;

            int total = Stats.FrametimeHistory.Count;
            int take = Math.Min(total, compactCorner ? 30 : 60);
            int start = total - take;

            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float ftMs = (start < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[start] > 0)
                    ? Stats.FrametimeHistory[start] / 10.0f
                    : 16.6f;
                float norm = Math.Max(0f, Math.Min(1f, ftMs / (float)ftCeiling));
                float y = plot.Bottom - norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    float ftMs = (idx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[idx] > 0)
                        ? Stats.FrametimeHistory[idx] / 10.0f
                        : 16.6f;
                    // Standard upward scale: 0ms is at plot.Bottom, ftCeiling ms is at plot.Top (stutter spikes upward)
                    float norm = Math.Max(0f, Math.Min(1f, ftMs / (float)ftCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Bottom - norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }

            // Neon Cyan / Lime Jitter Line (Festive Crimson on Snow White in Xmas Edition)
            Color ftCol = xmas ? XmasRed : Color.FromArgb(0x22, 0xD3, 0xEE); // Crimson / Electric Cyan
            using (Pen ftPen = new Pen(ftCol, 1.4f * Math.Max(1f, sc * 0.9f)))
            {
                ftPen.LineJoin = LineJoin.Round;
                g.DrawLines(ftPen, pts);
            }

            if (pts.Length > 0 && curFt > 0)
            {
                PointF last = pts[pts.Length - 1];
                using (SolidBrush dotBr = new SolidBrush(ftCol))
                {
                    g.FillEllipse(dotBr, last.X - 2.5f * sc, last.Y - 2.5f * sc, 5f * sc, 5f * sc);
                }

                // In compact corner mode the header already displays "FT XX.Xms", so skip the floating pill
                // to keep small-scale / short-height traces completely unobstructed.
                if (!compactCorner)
                {
                    // High-visibility live endpoint badge on the frametime line
                    string ftBadge = string.Format("{0:0.0} ms", curFt);
                    Size ftBadgeSz = TextRenderer.MeasureText(g, ftBadge, _fontDfAxis, new Size(80, 16), TextFormatFlags.NoPadding);
                    int ftBw = ftBadgeSz.Width + (int)Math.Round(6 * sc);
                    int ftBh = (int)Math.Round(12 * sc);
                    int ftBx = (int)Math.Round(last.X - ftBw - 6 * sc);
                    if (ftBx < plot.Left + 2) ftBx = plot.Left + 2;
                    int ftBy = (int)Math.Round(last.Y - ftBh / 2f);
                    if (ftBy < plot.Top + 1) ftBy = plot.Top + 1;
                    if (ftBy + ftBh > plot.Bottom - 1) ftBy = plot.Bottom - 1 - ftBh;
                    Rectangle ftBadgeRect = new Rectangle(ftBx, ftBy, ftBw, ftBh);

                    using (Pen tickPen = new Pen(Color.FromArgb(180, ftCol), 1f))
                    {
                        g.DrawLine(tickPen, last.X, last.Y, ftBx + ftBw, ftBy + ftBh / 2f);
                    }
                    using (SolidBrush bgPill = new SolidBrush(xmas ? Color.White : Color.FromArgb(225, 10, 14, 20)))
                    {
                        g.FillRectangle(bgPill, ftBadgeRect);
                    }
                    using (Pen borderPill = new Pen(ftCol, 1.2f))
                    {
                        g.DrawRectangle(borderPill, ftBadgeRect);
                    }
                    TextRenderer.DrawText(g, ftBadge, _fontDfAxis, new Point(ftBx + (int)Math.Round(3 * sc), ftBy + (int)Math.Round(1.5f * sc)), ftCol, TextFormatFlags.NoPadding);
                }
            }

            // Hover scrubbing tooltip
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(xmas ? Color.FromArgb(165, 0x18, 0x18, 0x1B) : Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sampleIdx = start + (int)Math.Round(normX * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int samplesAgo = take - 1 - (sampleIdx - start);
                double ft = (sampleIdx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[sampleIdx] > 0)
                    ? Stats.FrametimeHistory[sampleIdx] / 10.0
                    : 16.6;
                string hud = string.Format("{0} | Frametime: {1:0.0} ms", FormatSampleTimeAgo(samplesAgo), ft);
                DrawHoverHudBox(g, rect, plot, hud, _fontTrace, _hoverMouseX, _hoverMouseY, sc);
            }
        }

        private void DrawDfFramerateGraph(Graphics g, Rectangle rect, float sc)
        {
            if (rect.Width <= 10 || rect.Height <= 10) return;

            int total = (Stats != null && Stats.FpsHistory != null) ? Stats.FpsHistory.Count : 0;
            int take = Math.Min(total, 60);
            int start = total - take;

            int curLiveFps = _status.RtssFps > 0
                ? _status.RtssFps
                : ((total > 0 && Stats.FpsHistory[total - 1] > 0)
                    ? Stats.FpsHistory[total - 1]
                    : 0);

            int sessionAvg = ComputeDisplayAvgFps(curLiveFps);

            int targetFpsCeiling = CalculateDfCeiling(curLiveFps, (Stats != null) ? Stats.FpsHistory : null);
            if (targetFpsCeiling > _currentFpsCeiling)
            {
                // Immediate scale-up when framerate rises so frames are never clipped
                _currentFpsCeiling = targetFpsCeiling;
                _fpsCeilingHoldCounter = 0;
            }
            else if (targetFpsCeiling < _currentFpsCeiling)
            {
                // Smooth down-scale hold window: wait for 12 updates (~3-5 seconds) before stepping down
                _fpsCeilingHoldCounter++;
                if (_fpsCeilingHoldCounter >= 12)
                {
                    _currentFpsCeiling = targetFpsCeiling;
                    _fpsCeilingHoldCounter = 0;
                }
            }
            else
            {
                _fpsCeilingHoldCounter = 0;
            }
            int fpsCeiling = _currentFpsCeiling;

            int[] levels;
            if (fpsCeiling == 60)
                levels = new int[] { 60, 40, 20, 0 };
            else if (fpsCeiling == 80)
                levels = new int[] { 80, 60, 40, 20, 0 };
            else if (fpsCeiling == 90)
                levels = new int[] { 90, 60, 30, 0 };
            else if (fpsCeiling == 120)
                levels = new int[] { 120, 80, 40, 0 };
            else if (fpsCeiling == 180)
                levels = new int[] { 180, 120, 60, 0 };
            else if (fpsCeiling == 240)
                levels = new int[] { 240, 160, 80, 0 };
            else
            {
                int step = fpsCeiling / 3;
                levels = new int[] { fpsCeiling, step * 2, step, 0 };
            }

            bool xmas = IsXmasTheme;
            using (SolidBrush bgBr = new SolidBrush(xmas ? Color.White : Color.FromArgb(236, 9, 12, 18)))
            using (Pen borderPen = new Pen(xmas ? XmasBlack : Color.FromArgb(220, 255, 255, 255), 1.2f))
            {
                g.FillRectangle(bgBr, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            int p1Low = _status.RtssP1LowFps > 0 ? _status.RtssP1LowFps : ((Stats != null && Stats.P1LowFps > 0) ? Stats.P1LowFps : 0);
            int p01Low = _status.RtssP01LowFps > 0 ? _status.RtssP01LowFps : ((Stats != null && Stats.P01LowFps > 0) ? Stats.P01LowFps : 0);

            // Bold Header: FRAME-RATE (FPS) with live readout, average, 1% and 0.1% lows + native ETW Display Latency
            string lowsPart = "";
            if (p1Low > 0) lowsPart += string.Format("  •  1%: {0}", p1Low);
            if (p01Low > 0) lowsPart += string.Format("  •  0.1%: {0}", p01Low);
            string latPart = (ShowLatency && _status.DisplayLatencyMs > 0)
                ? string.Format("  •  LAT: {0:0.0}ms", _status.DisplayLatencyMs)
                : "";
            string fpsHdr;
            if (curLiveFps > 0)
            {
                bool compactHdr = rect.Width < (int)Math.Round(340 * sc);
                fpsHdr = compactHdr
                    ? string.Format("FPS: {0}{1}{2}{3}", curLiveFps, sessionAvg > 0 ? string.Format("  •  AVG: {0}", sessionAvg) : "", lowsPart, latPart)
                    : string.Format("FRAME-RATE (FPS)  •  NOW: {0}{1}{2}{3}", curLiveFps, sessionAvg > 0 ? string.Format("  •  AVG: {0}", sessionAvg) : "", lowsPart, latPart);
            }
            else
            {
                fpsHdr = "FRAME-RATE (FPS)";
            }
            TextRenderer.DrawText(g, fpsHdr, _fontDfHdr, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + (int)Math.Round(1.5f * sc)), IsXmasTheme ? XmasRed : Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int topPad = (int)Math.Round(14 * sc);
            int btmPad = (int)Math.Round(9 * sc);
            int rightPad = (int)Math.Round(32 * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - btmPad));

            // Grid lines and Axis Labels
            using (Pen gridPen = new Pen(xmas ? Color.FromArgb(45, 0x18, 0x18, 0x1B) : Color.FromArgb(90, 255, 255, 255), 1f))
            using (Pen boundPen = new Pen(xmas ? Color.FromArgb(145, 0x18, 0x18, 0x1B) : Color.FromArgb(180, 255, 255, 255), 1f))
            using (Pen tickPen = new Pen(xmas ? Color.FromArgb(24, 0x18, 0x18, 0x1B) : Color.FromArgb(35, 255, 255, 255), 1f))
            {
                // Vertical division ticks
                for (int t = 1; t <= 4; t++)
                {
                    float tx = plot.Left + (plot.Width * t) / 5f;
                    g.DrawLine(tickPen, tx, plot.Top, tx, plot.Bottom);
                }

                for (int i = 0; i < levels.Length; i++)
                {
                    float norm = (float)levels[i] / fpsCeiling;
                    float y = plot.Bottom - norm * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    string lbl = levels[i].ToString();
                    Size sz = TextRenderer.MeasureText(g, lbl, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int lblY = (int)Math.Round(y - sz.Height / 2f);
                    int lblBotMargin = Math.Max(3, (int)Math.Round(3 * sc));
                    if (lblY < rect.Y + 2) lblY = rect.Y + 2;
                    if (lblY + sz.Height > rect.Bottom - lblBotMargin) lblY = rect.Bottom - lblBotMargin - sz.Height;
                    TextRenderer.DrawText(g, lbl, _fontDfAxis, new Point(plot.Right + (int)Math.Round(5 * sc), lblY), IsXmasTheme ? XmasBlack : Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }

                // Vertical boundary line separating the plot from the axis labels
                g.DrawLine(boundPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
            }

            if (total == 0)
                return;

            // 0.1% Low shaded floor band (Rose / Coral — matches Live Perf tab)
            if (Stats.P01LowHistory != null && Stats.P01LowHistory.Count > 0)
            {
                PointF[] lowPts = new PointF[Math.Max(2, take)];
                bool hasAnyLow = false;
                if (take == 1)
                {
                    int lowVal = (start < Stats.P01LowHistory.Count && Stats.P01LowHistory[start] > 0) ? Stats.P01LowHistory[start] : p01Low;
                    if (lowVal > 0) hasAnyLow = true;
                    float norm = Math.Max(0f, Math.Min(1f, lowVal / (float)fpsCeiling));
                    float y = plot.Bottom - norm * plot.Height;
                    lowPts[0] = new PointF(plot.Left, y);
                    lowPts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < take; i++)
                    {
                        int idx = start + i;
                        int lowVal = (idx < Stats.P01LowHistory.Count && Stats.P01LowHistory[idx] > 0) ? Stats.P01LowHistory[idx] : p01Low;
                        if (lowVal > 0) hasAnyLow = true;
                        float norm = Math.Max(0f, Math.Min(1f, lowVal / (float)fpsCeiling));
                        float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                        float y = plot.Bottom - norm * plot.Height;
                        lowPts[i] = new PointF(x, y);
                    }
                }
                if (hasAnyLow)
                {
                    using (GraphicsPath lowPath = new GraphicsPath())
                    {
                        lowPath.AddLine(plot.Left, plot.Bottom, lowPts[0].X, lowPts[0].Y);
                        lowPath.AddLines(lowPts);
                        lowPath.AddLine(lowPts[lowPts.Length - 1].X, lowPts[lowPts.Length - 1].Y, plot.Right, plot.Bottom);
                        lowPath.CloseFigure();
                        using (SolidBrush lowFill = new SolidBrush(xmas ? Color.FromArgb(26, 0xDC, 0x26, 0x26) : Color.FromArgb(42, 0xFB, 0x71, 0x85)))
                        {
                            g.FillPath(lowFill, lowPath);
                        }
                    }
                    using (Pen lowPen = new Pen(xmas ? Color.FromArgb(155, 0xB9, 0x1C, 0x1C) : Color.FromArgb(170, 0xFB, 0x71, 0x85), 1.1f * Math.Max(1f, sc * 0.85f)))
                    {
                        lowPen.DashStyle = DashStyle.Dot;
                        g.DrawLines(lowPen, lowPts);
                    }
                }
            }

            // 1% Low dashed line (Amber Orange — visually distinct from 0.1% Low Rose floor)
            if (Stats.P1LowHistory != null && Stats.P1LowHistory.Count > 0)
            {
                PointF[] p1Pts = new PointF[Math.Max(2, take)];
                bool hasAnyP1 = false;
                if (take == 1)
                {
                    int lowVal = (start < Stats.P1LowHistory.Count && Stats.P1LowHistory[start] > 0) ? Stats.P1LowHistory[start] : p1Low;
                    if (lowVal > 0) hasAnyP1 = true;
                    float norm = Math.Max(0f, Math.Min(1f, lowVal / (float)fpsCeiling));
                    float y = plot.Bottom - norm * plot.Height;
                    p1Pts[0] = new PointF(plot.Left, y);
                    p1Pts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < take; i++)
                    {
                        int idx = start + i;
                        int lowVal = (idx < Stats.P1LowHistory.Count && Stats.P1LowHistory[idx] > 0) ? Stats.P1LowHistory[idx] : p1Low;
                        if (lowVal > 0) hasAnyP1 = true;
                        float norm = Math.Max(0f, Math.Min(1f, lowVal / (float)fpsCeiling));
                        float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                        float y = plot.Bottom - norm * plot.Height;
                        p1Pts[i] = new PointF(x, y);
                    }
                }
                if (hasAnyP1)
                {
                    using (Pen p1Pen = new Pen(xmas ? Color.FromArgb(155, 0x52, 0x52, 0x5B) : Color.FromArgb(185, 0xFB, 0x92, 0x3C), 1.15f * Math.Max(1f, sc * 0.85f)))
                    {
                        p1Pen.DashStyle = DashStyle.Dash;
                        g.DrawLines(p1Pen, p1Pts);
                    }
                }
            }

            // Session Avg line (Jet Black in Xmas Edition / Smooth Blue-Cyan)
            if (Stats.AvgFpsHistory != null && Stats.AvgFpsHistory.Count > 0)
            {
                PointF[] avgPts = new PointF[Math.Max(2, take)];
                if (take == 1)
                {
                    float f = (start < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[start] : 0;
                    float norm = Math.Max(0f, Math.Min(1f, f / fpsCeiling));
                    float y = plot.Bottom - norm * plot.Height;
                    avgPts[0] = new PointF(plot.Left, y);
                    avgPts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < take; i++)
                    {
                        int idx = start + i;
                        float f = (idx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[idx] : 0;
                        float norm = Math.Max(0f, Math.Min(1f, f / fpsCeiling));
                        float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                        float y = plot.Bottom - norm * plot.Height;
                        avgPts[i] = new PointF(x, y);
                    }
                }
                using (Pen avgPen = new Pen(xmas ? XmasBlack : Color.FromArgb(0x38, 0xBD, 0xF8), 1.4f * Math.Max(1f, sc * 0.9f)))
                {
                    avgPen.DashStyle = DashStyle.Dash;
                    g.DrawLines(avgPen, avgPts);
                }
            }

            // Live FPS curve (Vibrant Neon Lime Green) with 3-point rolling moving average for Digital Foundry aesthetic
            PointF[] fpsPts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float f = (curLiveFps > 0) ? curLiveFps : ((start < Stats.FpsHistory.Count) ? Stats.FpsHistory[start] : 0);
                float norm = Math.Max(0f, Math.Min(1f, f / fpsCeiling));
                float y = plot.Bottom - norm * plot.Height;
                fpsPts[0] = new PointF(plot.Left, y);
                fpsPts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    float f;
                    if (take <= 2)
                    {
                        f = (idx < Stats.FpsHistory.Count) ? Stats.FpsHistory[idx] : 0;
                    }
                    else if (i == 0)
                    {
                        float f0 = Stats.FpsHistory[idx];
                        float f1 = (idx + 1 < Stats.FpsHistory.Count) ? Stats.FpsHistory[idx + 1] : f0;
                        f = f0 * 0.75f + f1 * 0.25f;
                    }
                    else if (i == take - 1)
                    {
                        // Keep leading edge exact to latest live sample
                        f = (curLiveFps > 0) ? curLiveFps : ((idx < Stats.FpsHistory.Count) ? Stats.FpsHistory[idx] : 0);
                    }
                    else
                    {
                        float fPrev = Stats.FpsHistory[idx - 1];
                        float fCurr = Stats.FpsHistory[idx];
                        float fNext = (idx + 1 < Stats.FpsHistory.Count) ? Stats.FpsHistory[idx + 1] : fCurr;
                        f = fPrev * 0.2f + fCurr * 0.6f + fNext * 0.2f;
                    }

                    float norm = Math.Max(0f, Math.Min(1f, f / fpsCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Bottom - norm * plot.Height;
                    fpsPts[i] = new PointF(x, y);
                }
            }
            Color neonGreen = xmas ? XmasRed : Color.FromArgb(0x4A, 0xDE, 0x80); // Festive Crimson / Neon Lime Green
            using (Pen fpsPen = new Pen(neonGreen, 1.85f * Math.Max(1f, sc * 0.9f)))
            {
                fpsPen.LineJoin = LineJoin.Round;
                g.DrawLines(fpsPen, fpsPts);
            }

            if (fpsPts.Length > 0 && curLiveFps > 0)
            {
                PointF last = fpsPts[fpsPts.Length - 1];
                using (SolidBrush dotBr = new SolidBrush(neonGreen))
                {
                    g.FillEllipse(dotBr, last.X - 2.5f * sc, last.Y - 2.5f * sc, 5f * sc, 5f * sc);
                }

                // High-visibility live endpoint badge on the FPS curve (inset clear of the right-hand axis ticks)
                string fpsBadge = string.Format("{0} FPS", curLiveFps);
                Size bSz = TextRenderer.MeasureText(g, fpsBadge, _fontDfAxis, new Size(80, 16), TextFormatFlags.NoPadding);
                int bw = bSz.Width + (int)Math.Round(7 * sc);
                int bh = (int)Math.Round(13 * sc);
                int bx = (int)Math.Round(last.X - bw - 10 * sc);
                if (bx < plot.Left + 2) bx = plot.Left + 2;
                int by = (int)Math.Round(last.Y - bh / 2f);
                if (by < plot.Top + 1) by = plot.Top + 1;
                if (by + bh > plot.Bottom - 1) by = plot.Bottom - 1 - bh;
                Rectangle badgeRect = new Rectangle(bx, by, bw, bh);

                using (Pen tickPen = new Pen(Color.FromArgb(180, neonGreen), 1f))
                {
                    g.DrawLine(tickPen, last.X, last.Y, bx + bw, by + bh / 2f);
                }
                using (SolidBrush bgPill = new SolidBrush(xmas ? Color.White : Color.FromArgb(242, 8, 12, 18)))
                using (Pen borderPill = new Pen(neonGreen, 1.15f))
                {
                    DrawRoundedRectangle(g, borderPill, bgPill, badgeRect, (int)Math.Round(2.5f * sc));
                }
                TextRenderer.DrawText(g, fpsBadge, _fontDfAxis, badgeRect, neonGreen, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

                // Show companion Session Avg badge if separated from live badge
                if (Stats.AvgFpsHistory != null && Stats.AvgFpsHistory.Count > 0 && sessionAvg > 0)
                {
                    float normAvg = Math.Max(0f, Math.Min(1f, (float)sessionAvg / fpsCeiling));
                    float avgY = plot.Bottom - normAvg * plot.Height;
                    if (Math.Abs(avgY - last.Y) > bh + 6)
                    {
                        string avgBadge = string.Format("{0} Avg", sessionAvg);
                        Size aSz = TextRenderer.MeasureText(g, avgBadge, _fontDfAxis, new Size(80, 16), TextFormatFlags.NoPadding);
                        int abw = aSz.Width + (int)Math.Round(7 * sc);
                        int abx = (int)Math.Round(last.X - abw - 10 * sc);
                        if (abx < plot.Left + 2) abx = plot.Left + 2;
                        int aby = (int)Math.Round(avgY - bh / 2f);
                        if (aby < plot.Top + 1) aby = plot.Top + 1;
                        if (aby + bh > plot.Bottom - 1) aby = plot.Bottom - 1 - bh;
                        Rectangle aRect = new Rectangle(abx, aby, abw, bh);

                        Color avgCol = xmas ? XmasBlack : Color.FromArgb(0x38, 0xBD, 0xF8);
                        using (SolidBrush bgPill = new SolidBrush(xmas ? Color.White : Color.FromArgb(242, 8, 12, 18)))
                        using (Pen borderPill = new Pen(avgCol, 1f) { DashStyle = DashStyle.Dash })
                        {
                            DrawRoundedRectangle(g, borderPill, bgPill, aRect, (int)Math.Round(2.5f * sc));
                        }
                        TextRenderer.DrawText(g, avgBadge, _fontDfAxis, aRect, avgCol, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    }
                }
            }

            // Hover scrubbing tooltip (suppressed when mouse is over the DF Corner inset box)
            bool mouseOverFtInset = (DfGraphStyle && DfCornerInset && !_dfFtRect.IsEmpty && _dfFtRect.Contains(_hoverMouseX, _hoverMouseY));
            if (!mouseOverFtInset && _hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(xmas ? Color.FromArgb(165, 0x18, 0x18, 0x1B) : Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sampleIdx = start + (int)Math.Round(normX * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int samplesAgo = take - 1 - (sampleIdx - start);
                int f = (sampleIdx < Stats.FpsHistory.Count) ? Stats.FpsHistory[sampleIdx] : 0;
                int avg = (sampleIdx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[sampleIdx] : 0;
                int low1Hist = (Stats.P1LowHistory != null && sampleIdx < Stats.P1LowHistory.Count && Stats.P1LowHistory[sampleIdx] > 0) ? Stats.P1LowHistory[sampleIdx] : p1Low;
                int low01 = (sampleIdx < Stats.P01LowHistory.Count) ? Stats.P01LowHistory[sampleIdx] : 0;

                string hud = string.Format("{0} | FPS: {1} • Avg: {2} • 1%: {3} • 0.1%: {4}",
                    FormatSampleTimeAgo(samplesAgo), f, avg, low1Hist > 0 ? low1Hist.ToString() : "--", low01 > 0 ? low01.ToString() : "--");
                DrawHoverHudBox(g, rect, plot, hud, _fontTrace, _hoverMouseX, _hoverMouseY, sc);
            }
        }

    }
}
