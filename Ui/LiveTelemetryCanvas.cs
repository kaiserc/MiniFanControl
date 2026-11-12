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
    public class LiveTelemetryCanvas : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public TelemetrySessionStats Stats { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsDarkMode { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsRadeonTheme { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsAfterburnerTheme { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool HasDualFans { get; set; }

        // 0 = All / Default (CPU), 1 = Highlight CPU Temp, 2 = Highlight iGPU Temp, 3 = Highlight Power W
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int HighlightMetric { get; set; }

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowNvme = true;
        public bool ShowPower = true;
        public bool ShowRamPower = true;
        public bool ShowApuRpm = true;
        public bool ShowSysRpm = true;
        public bool AutoScale = true;

        public event EventHandler ResetPeaksRequested;
        public event EventHandler HighlightMetricChanged;

        private int _hoverSampleIdx = -1;
        private int _hoverMouseX = -1;
        private readonly Rectangle[] _pillRects = new Rectangle[9];

        private static readonly Font AxisFont = new Font("Segoe UI", 7.0f, FontStyle.Regular);
        private static readonly Font PillFontBold = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font PillFontReg = new Font("Segoe UI", 7.25f, FontStyle.Regular);
        private static readonly Font HudFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        public LiveTelemetryCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            IsDarkMode = true;
            HasDualFans = true;
            HighlightMetric = 0;
            Cursor = Cursors.Cross;
        }

        private Rectangle GetPlotRect()
        {
            return new Rectangle(38, 30, Math.Max(60, Width - 82), Math.Max(40, Height - 50));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverSampleIdx != -1)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overPill = false;
            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    overPill = true;
                    break;
                }
            }
            Cursor = overPill ? Cursors.Hand : Cursors.Cross;

            Rectangle plot = GetPlotRect();
            int count = (Stats != null && Stats.CpuHistory != null) ? Stats.CpuHistory.Count : 0;
            if (count > 0 && plot.Contains(e.Location))
            {
                float normX = (float)(e.X - plot.Left) / Math.Max(1, plot.Width);
                int idx = count == 1 ? 0 : (int)Math.Round(normX * (count - 1));
                idx = Math.Max(0, Math.Min(count - 1, idx));
                if (idx != _hoverSampleIdx || Math.Abs(e.X - _hoverMouseX) > 1)
                {
                    _hoverSampleIdx = idx;
                    _hoverMouseX = e.X;
                    Invalidate();
                }
            }
            else if (_hoverSampleIdx != -1)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    if (i == 0)
                    {
                        if (HighlightMetric == 1) ShowCpu = !ShowCpu;
                        else { HighlightMetric = 1; ShowCpu = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 1)
                    {
                        if (HighlightMetric == 2) ShowGpu = !ShowGpu;
                        else { HighlightMetric = 2; ShowGpu = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 2)
                    {
                        if (HighlightMetric == 6) ShowNvme = !ShowNvme;
                        else { HighlightMetric = 6; ShowNvme = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 3)
                    {
                        if (HighlightMetric == 3) ShowPower = !ShowPower;
                        else { HighlightMetric = 3; ShowPower = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 4)
                    {
                        if (HighlightMetric == 7) ShowRamPower = !ShowRamPower;
                        else { HighlightMetric = 7; ShowRamPower = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 5)
                    {
                        if (HighlightMetric == 4) ShowApuRpm = !ShowApuRpm;
                        else { HighlightMetric = 4; ShowApuRpm = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 6)
                    {
                        if (HighlightMetric == 5) ShowSysRpm = !ShowSysRpm;
                        else { HighlightMetric = 5; ShowSysRpm = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 7) AutoScale = !AutoScale;
                    else if (i == 8)
                    {
                        if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                    }
                    Invalidate();
                    return;
                }
            }
        }

        private static PointF[] BuildSeriesPoints(List<int> list, int count, Rectangle plot, float scaleMin, float scaleMax, float multiplier = 1.0f)
        {
            PointF[] pts = new PointF[Math.Max(2, count)];
            float span = Math.Max(1f, scaleMax - scaleMin);
            if (count == 1)
            {
                float norm = (list[0] * multiplier - scaleMin) / span;
                norm = Math.Max(0f, Math.Min(1f, norm));
                float y = plot.Bottom - norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    float x = plot.Left + (plot.Width * i) / (float)(count - 1);
                    int v = i < list.Count ? list[i] : list[list.Count - 1];
                    float norm = (v * multiplier - scaleMin) / span;
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plot.Bottom - norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }
            return pts;
        }

        private void DrawSeriesLine(Graphics g, PointF[] pts, Rectangle plot, Color color, bool isHighlighted, bool isDashed)
        {
            if (pts == null || pts.Length < 2) return;

            if (isHighlighted)
            {
                using (GraphicsPath areaPath = new GraphicsPath())
                {
                    areaPath.AddLines(pts);
                    areaPath.AddLine(pts[pts.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    areaPath.CloseFigure();
                    using (LinearGradientBrush areaBr = new LinearGradientBrush(
                        new Point(plot.Left, plot.Top),
                        new Point(plot.Left, plot.Bottom),
                        Color.FromArgb(IsDarkMode ? 75 : 55, color),
                        Color.FromArgb(6, color)))
                    {
                        g.FillPath(areaBr, areaPath);
                    }
                }
                using (Pen glowPen = new Pen(Color.FromArgb(55, color), 5.0f))
                {
                    glowPen.LineJoin = LineJoin.Round;
                    g.DrawLines(glowPen, pts);
                }
            }

            using (Pen pen = new Pen(color, isHighlighted ? 2.5f : (isDashed ? 1.5f : 1.85f)))
            {
                pen.LineJoin = LineJoin.Round;
                if (isDashed) pen.DashStyle = DashStyle.Dash;
                g.DrawLines(pen, pts);
            }

            PointF last = pts[pts.Length - 1];
            float r = isHighlighted ? 4.2f : 2.8f;
            using (SolidBrush br = new SolidBrush(color))
            {
                g.FillEllipse(br, last.X - r, last.Y - r, r * 2f, r * 2f);
            }
        }

        private class CanvasEndpointBadge
        {
            public float TargetY;
            public float DisplayY;
            public string Text;
            public Color Color;
            public bool Highlighted;
        }

        private static void DrawCanvasBadges(Graphics g, Rectangle plot, List<CanvasEndpointBadge> badges, bool isDark)
        {
            if (badges == null || badges.Count == 0) return;

            badges.Sort((a, b) => a.TargetY.CompareTo(b.TargetY));
            float minSpacing = 13.5f;
            for (int i = 0; i < badges.Count; i++)
                badges[i].DisplayY = badges[i].TargetY;

            for (int i = 1; i < badges.Count; i++)
            {
                if (badges[i].DisplayY < badges[i - 1].DisplayY + minSpacing)
                    badges[i].DisplayY = badges[i - 1].DisplayY + minSpacing;
            }

            float maxBottom = plot.Bottom - 6;
            if (badges[badges.Count - 1].DisplayY > maxBottom)
            {
                badges[badges.Count - 1].DisplayY = maxBottom;
                for (int i = badges.Count - 2; i >= 0; i--)
                {
                    if (badges[i].DisplayY > badges[i + 1].DisplayY - minSpacing)
                        badges[i].DisplayY = badges[i + 1].DisplayY - minSpacing;
                }
            }

            float minTop = plot.Top + 6;
            for (int i = 0; i < badges.Count; i++)
            {
                if (badges[i].DisplayY < minTop)
                    badges[i].DisplayY = minTop;
            }

            Color bgPill = isDark ? Color.FromArgb(220, 15, 23, 42) : Color.FromArgb(235, 248, 250, 252);
            using (SolidBrush bgBr = new SolidBrush(bgPill))
            {
                for (int i = 0; i < badges.Count; i++)
                {
                    CanvasEndpointBadge b = badges[i];
                    Size sz = TextRenderer.MeasureText(g, b.Text, AxisFont, new Size(80, 16), TextFormatFlags.NoPadding);
                    int bw = sz.Width + 8;
                    int bh = 13;
                    int bx = plot.Right - bw - 2;
                    int by = (int)Math.Round(b.DisplayY - bh / 2f);
                    Rectangle badgeRect = new Rectangle(bx, by, bw, bh);

                    using (Pen tickPen = new Pen(Color.FromArgb(140, b.Color), 1f))
                    {
                        g.DrawLine(tickPen, plot.Right, b.TargetY, bx + bw, b.DisplayY);
                    }

                    g.FillRectangle(bgBr, badgeRect);
                    using (Pen bPen = new Pen(b.Highlighted ? b.Color : Color.FromArgb(150, b.Color), b.Highlighted ? 1.5f : 1f))
                    {
                        g.DrawRectangle(bPen, badgeRect);
                    }
                    TextRenderer.DrawText(g, b.Text, AxisFont, new Point(bx + 4, by + 1), b.Color, TextFormatFlags.NoPadding);
                }
            }
        }

        private int DrawLegendPill(Graphics g, int idx, int x, int y, string text, Color seriesColor, bool active, bool highlighted)
        {
            Font f = highlighted ? PillFontBold : PillFontReg;
            Size sz = TextRenderer.MeasureText(g, text, f, new Size(200, 20), TextFormatFlags.NoPadding);
            int w = sz.Width + 12;
            int h = 18;
            Rectangle r = new Rectangle(x, y, w, h);
            _pillRects[idx] = r;

            Color pillBg = !active
                ? (IsDarkMode ? Color.FromArgb(0x1E, 0x1E, 0x2A) : Color.FromArgb(0xE2, 0xE8, 0xF0))
                : (highlighted
                    ? Color.FromArgb(IsDarkMode ? 80 : 50, seriesColor)
                    : Color.FromArgb(IsDarkMode ? 32 : 22, seriesColor));
            Color borderCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x38, 0x3A, 0x4C) : Color.FromArgb(0xCB, 0xD5, 0xE1))
                : (highlighted ? seriesColor : Color.FromArgb(140, seriesColor));
            Color textCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x6C, 0x70, 0x86) : Color.FromArgb(0x94, 0xA3, 0xB8))
                : (IsDarkMode ? Color.White : Color.Black);

            using (SolidBrush bgBr = new SolidBrush(pillBg))
            using (Pen borderPen = new Pen(borderCol, highlighted ? 1.5f : 1f))
            {
                g.FillRectangle(bgBr, r);
                g.DrawRectangle(borderPen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            TextRenderer.DrawText(g, text, f, new Point(x + 6, y + 2), textCol, TextFormatFlags.NoPadding);
            return x + w + 5;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = IsAfterburnerTheme
                ? Color.FromArgb(0x13, 0x15, 0x19)
                : (IsRadeonTheme
                    ? Color.FromArgb(0x16, 0x0A, 0x0E)
                    : (IsDarkMode ? Color.FromArgb(0x16, 0x16, 0x22) : Color.FromArgb(0xF8, 0xFA, 0xFC)));
            Color gridColor = IsAfterburnerTheme
                ? Color.FromArgb(0x28, 0x30, 0x3A)
                : (IsRadeonTheme
                    ? Color.FromArgb(0x42, 0x1C, 0x26)
                    : (IsDarkMode ? Color.FromArgb(0x2D, 0x2F, 0x42) : Color.FromArgb(0xE2, 0xE8, 0xF0)));
            Color labelColor = IsAfterburnerTheme
                ? Color.FromArgb(0x8A, 0x96, 0xA8)
                : (IsRadeonTheme
                    ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                    : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69)));

            Color cpuColor = IsAfterburnerTheme
                ? Color.FromArgb(0x00, 0xE6, 0x76)
                : (IsRadeonTheme
                    ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                    : (IsDarkMode ? Color.FromArgb(0x60, 0xA5, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB)));
            Color gpuColor = IsAfterburnerTheme
                ? Color.FromArgb(0x00, 0xE5, 0xFF)
                : (IsRadeonTheme
                    ? Color.FromArgb(0x38, 0xBD, 0xF8)
                    : (IsDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x16, 0xA3, 0x4A)));
            Color nvmeColor = IsDarkMode ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2);
            Color pwrColor = IsAfterburnerTheme
                ? Color.FromArgb(0xFF, 0x91, 0x00)
                : (IsRadeonTheme
                    ? Color.FromArgb(0xFB, 0x92, 0x3C)
                    : (IsDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xD9, 0x77, 0x06)));
            Color ramPwrColor = IsDarkMode ? Color.FromArgb(0x81, 0x8C, 0xF8) : Color.FromArgb(0x4F, 0x46, 0xE5);
            Color apuRpmColor = IsAfterburnerTheme ? Color.FromArgb(0x00, 0xE6, 0x76) : (IsDarkMode ? Color.FromArgb(0xC0, 0x84, 0xFC) : Color.FromArgb(0x7C, 0x3A, 0xED));
            Color sysRpmColor = IsAfterburnerTheme ? Color.FromArgb(0x00, 0xE5, 0xFF) : (IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88));

            g.Clear(bg);
            Rectangle plot = GetPlotRect();

            int count = (Stats != null && Stats.CpuHistory != null) ? Stats.CpuHistory.Count : 0;
            int lastCpu = count > 0 ? Stats.CpuHistory[count - 1] : 0;
            int lastGpu = (Stats != null && Stats.GpuHistory.Count > 0) ? Stats.GpuHistory[Stats.GpuHistory.Count - 1] : 0;
            int lastNvme = (Stats != null && Stats.NvmeHistory.Count > 0) ? Stats.NvmeHistory[Stats.NvmeHistory.Count - 1] : 0;
            int lastPwr = (Stats != null && Stats.PowerHistory.Count > 0) ? Stats.PowerHistory[Stats.PowerHistory.Count - 1] : 0;
            double lastRamPwr = (Stats != null && Stats.RamPowerHistory.Count > 0) ? Stats.RamPowerHistory[Stats.RamPowerHistory.Count - 1] / 10.0 : 0.0;
            int lastF1  = (Stats != null && Stats.Fan1RpmHistory.Count > 0) ? Stats.Fan1RpmHistory[Stats.Fan1RpmHistory.Count - 1] : 0;
            int lastF2  = (Stats != null && Stats.Fan2RpmHistory.Count > 0) ? Stats.Fan2RpmHistory[Stats.Fan2RpmHistory.Count - 1] : 0;

            for (int i = 0; i < _pillRects.Length; i++) _pillRects[i] = Rectangle.Empty;

            // Top Interactive Legend Pills (y = 5)
            int px = 6;
            px = DrawLegendPill(g, 0, px, 5, string.Format("● CPU {0}°C", lastCpu), cpuColor, ShowCpu, HighlightMetric == 1);
            px = DrawLegendPill(g, 1, px, 5, string.Format("● iGPU {0}°C", lastGpu), gpuColor, ShowGpu, HighlightMetric == 2);
            px = DrawLegendPill(g, 2, px, 5, string.Format("● NVMe {0}°C", lastNvme), nvmeColor, ShowNvme, HighlightMetric == 6);
            px = DrawLegendPill(g, 3, px, 5, string.Format("● Pwr {0}W", lastPwr), pwrColor, ShowPower, HighlightMetric == 3);
            px = DrawLegendPill(g, 4, px, 5, string.Format("● RAM {0:0.0}W", lastRamPwr), ramPwrColor, ShowRamPower, HighlightMetric == 7);
            px = DrawLegendPill(g, 5, px, 5, string.Format("● APU {0}", lastF1), apuRpmColor, ShowApuRpm, HighlightMetric == 4);
            if (HasDualFans)
            {
                px = DrawLegendPill(g, 6, px, 5, string.Format("● Sys {0}", lastF2), sysRpmColor, ShowSysRpm, HighlightMetric == 5);
            }
            px = DrawLegendPill(g, 7, Width - 110, 5, AutoScale ? "Auto-Zoom" : "0–100", labelColor, true, AutoScale);
            DrawLegendPill(g, 8, Width - 48, 5, "Reset", labelColor, true, false);

            // Compute Left Axis (Temp °C / Power W) & Right Axis (Fan RPM) scales
            float leftMin = 0f, leftMax = 100f;
            float rpmMin = 0f, rpmMax = 3000f;

            if (AutoScale && count > 0)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (ShowCpu && i < Stats.CpuHistory.Count)
                    {
                        int v = Stats.CpuHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowGpu && i < Stats.GpuHistory.Count && Stats.GpuHistory[i] > 0)
                    {
                        int v = Stats.GpuHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowNvme && i < Stats.NvmeHistory.Count && Stats.NvmeHistory[i] > 0)
                    {
                        int v = Stats.NvmeHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowPower && i < Stats.PowerHistory.Count && Stats.PowerHistory[i] > 0)
                    {
                        int v = Stats.PowerHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowRamPower && i < Stats.RamPowerHistory.Count && Stats.RamPowerHistory[i] > 0)
                    {
                        int v = (int)Math.Round(Stats.RamPowerHistory[i] / 10.0);
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                }
                if (lo <= hi)
                {
                    leftMin = Math.Max(0f, lo - 4f);
                    leftMax = hi + 4f;
                    if (leftMax - leftMin < 10f)
                    {
                        float mid = (leftMin + leftMax) * 0.5f;
                        leftMin = Math.Max(0f, mid - 5f);
                        leftMax = leftMin + 10f;
                    }
                }

                int rLo = int.MaxValue, rHi = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (ShowApuRpm && i < Stats.Fan1RpmHistory.Count)
                    {
                        int v = Stats.Fan1RpmHistory[i];
                        if (v < rLo) rLo = v;
                        if (v > rHi) rHi = v;
                    }
                    if (HasDualFans && ShowSysRpm && i < Stats.Fan2RpmHistory.Count)
                    {
                        int v = Stats.Fan2RpmHistory[i];
                        if (v < rLo) rLo = v;
                        if (v > rHi) rHi = v;
                    }
                }
                if (rLo <= rHi)
                {
                    rpmMin = Math.Max(0f, rLo - 150f);
                    rpmMax = rHi + 150f;
                    if (rpmMax - rpmMin < 400f)
                    {
                        rpmMin = Math.Max(0f, rLo - 200f);
                        rpmMax = rpmMin + 400f;
                    }
                }
            }

            // Draw Grid & Dual Y-Axes + Time X-Axis
            using (Brush lblBrush = new SolidBrush(labelColor))
            using (Pen gridPen = new Pen(gridColor, 1f))
            {
                for (int step = 0; step <= 4; step++)
                {
                    float frac = step / 4f;
                    float y = plot.Bottom - frac * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    int leftVal = (int)Math.Round(leftMin + frac * (leftMax - leftMin));
                    string lStr = leftVal + "°/W";
                    SizeF lSz = g.MeasureString(lStr, AxisFont);
                    g.DrawString(lStr, AxisFont, lblBrush, plot.Left - lSz.Width - 2, y - lSz.Height / 2);

                    int rVal = (int)Math.Round(rpmMin + frac * (rpmMax - rpmMin));
                    string rStr = rVal.ToString();
                    SizeF rSz = g.MeasureString(rStr, AxisFont);
                    g.DrawString(rStr, AxisFont, lblBrush, plot.Right + 3, y - rSz.Height / 2);
                }

                int totalWindowSec = Math.Max(15, (int)Math.Round(Math.Max(2, count) * 1.5));
                for (int tStep = 0; tStep <= 4; tStep++)
                {
                    float frac = tStep / 4f;
                    float x = plot.Left + frac * plot.Width;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    int secAgo = (int)Math.Round((1f - frac) * totalWindowSec);
                    string tStr = secAgo == 0 ? "Now" : string.Format("-{0}s", secAgo);
                    SizeF tSz = g.MeasureString(tStr, AxisFont);
                    g.DrawString(tStr, AxisFont, lblBrush, x - tSz.Width / 2, plot.Bottom + 3);
                }
            }

            if (count > 0)
            {
                PointF[] cpuPts = ShowCpu ? BuildSeriesPoints(Stats.CpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] gpuPts = ShowGpu ? BuildSeriesPoints(Stats.GpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] nvmePts = (ShowNvme && Stats != null && Stats.NvmeHistory.Count > 0) ? BuildSeriesPoints(Stats.NvmeHistory, count, plot, leftMin, leftMax) : null;
                PointF[] pwrPts = ShowPower ? BuildSeriesPoints(Stats.PowerHistory, count, plot, leftMin, leftMax) : null;
                PointF[] ramPts = (ShowRamPower && Stats != null && Stats.RamPowerHistory.Count > 0) ? BuildSeriesPoints(Stats.RamPowerHistory, count, plot, leftMin, leftMax, 0.1f) : null;
                PointF[] f1Pts  = ShowApuRpm ? BuildSeriesPoints(Stats.Fan1RpmHistory, count, plot, rpmMin, rpmMax) : null;
                PointF[] f2Pts  = (HasDualFans && ShowSysRpm) ? BuildSeriesPoints(Stats.Fan2RpmHistory, count, plot, rpmMin, rpmMax) : null;

                int hl = HighlightMetric == 0 ? 1 : HighlightMetric;

                // Draw non-highlighted series first, then the highlighted spotlight series on top
                if (f2Pts != null && hl != 5) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, false, true);
                if (f1Pts != null && hl != 4) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, false, true);
                if (ramPts != null && hl != 7) DrawSeriesLine(g, ramPts, plot, ramPwrColor, false, false);
                if (pwrPts != null && hl != 3) DrawSeriesLine(g, pwrPts, plot, pwrColor, false, false);
                if (nvmePts != null && hl != 6) DrawSeriesLine(g, nvmePts, plot, nvmeColor, false, false);
                if (gpuPts != null && hl != 2) DrawSeriesLine(g, gpuPts, plot, gpuColor, false, false);
                if (cpuPts != null && hl != 1) DrawSeriesLine(g, cpuPts, plot, cpuColor, false, false);

                if (hl == 1 && cpuPts != null) DrawSeriesLine(g, cpuPts, plot, cpuColor, true, false);
                else if (hl == 2 && gpuPts != null) DrawSeriesLine(g, gpuPts, plot, gpuColor, true, false);
                else if (hl == 6 && nvmePts != null) DrawSeriesLine(g, nvmePts, plot, nvmeColor, true, false);
                else if (hl == 3 && pwrPts != null) DrawSeriesLine(g, pwrPts, plot, pwrColor, true, false);
                else if (hl == 7 && ramPts != null) DrawSeriesLine(g, ramPts, plot, ramPwrColor, true, false);
                else if (hl == 4 && f1Pts != null) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, true, false);
                else if (hl == 5 && f2Pts != null) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, true, false);

                // Endpoint trace badges anchored to the right tip of each curve
                List<CanvasEndpointBadge> badges = new List<CanvasEndpointBadge>();
                if (f2Pts != null && Stats.Fan2RpmHistory.Count > 0)
                {
                    int v = Stats.Fan2RpmHistory[count - 1];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = f2Pts[f2Pts.Length - 1].Y, Text = v + " RPM", Color = sysRpmColor, Highlighted = (hl == 5) });
                }
                if (f1Pts != null && Stats.Fan1RpmHistory.Count > 0)
                {
                    int v = Stats.Fan1RpmHistory[count - 1];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = f1Pts[f1Pts.Length - 1].Y, Text = v + " RPM", Color = apuRpmColor, Highlighted = (hl == 4) });
                }
                if (ramPts != null && Stats.RamPowerHistory.Count > 0)
                {
                    int v = Stats.RamPowerHistory[count - 1];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = ramPts[ramPts.Length - 1].Y, Text = string.Format("{0:0.0}W", v / 10.0), Color = ramPwrColor, Highlighted = (hl == 7) });
                }
                if (pwrPts != null && Stats.PowerHistory.Count > 0)
                {
                    int v = Stats.PowerHistory[count - 1];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = pwrPts[pwrPts.Length - 1].Y, Text = v + "W", Color = pwrColor, Highlighted = (hl == 3) });
                }
                if (nvmePts != null && Stats.NvmeHistory.Count > 0)
                {
                    int v = Stats.NvmeHistory[count - 1];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = nvmePts[nvmePts.Length - 1].Y, Text = v + "°C", Color = nvmeColor, Highlighted = (hl == 6) });
                }
                if (gpuPts != null && Stats.GpuHistory.Count > 0 && Stats.GpuHistory[count - 1] > 0)
                {
                    int v = Stats.GpuHistory[count - 1];
                    badges.Add(new CanvasEndpointBadge { TargetY = gpuPts[gpuPts.Length - 1].Y, Text = v + "°C", Color = gpuColor, Highlighted = (hl == 2) });
                }
                if (cpuPts != null && Stats.CpuHistory.Count > 0)
                {
                    int v = Stats.CpuHistory[count - 1];
                    badges.Add(new CanvasEndpointBadge { TargetY = cpuPts[cpuPts.Length - 1].Y, Text = v + "°C", Color = cpuColor, Highlighted = (hl == 1) });
                }

                DrawCanvasBadges(g, plot, badges, IsDarkMode);

                // Hover Time-Scrub Crosshair & Readout Card
                if (_hoverSampleIdx >= 0 && _hoverSampleIdx < count)
                {
                    float cx = count == 1 ? plot.Left + plot.Width / 2f : plot.Left + (plot.Width * _hoverSampleIdx) / (float)(count - 1);
                    using (Pen crossPen = new Pen(Color.FromArgb(160, labelColor), 1.2f))
                    {
                        crossPen.DashStyle = DashStyle.Dot;
                        g.DrawLine(crossPen, cx, plot.Top, cx, plot.Bottom);
                    }

                    int secAgo = (int)Math.Round((count - 1 - _hoverSampleIdx) * 1.5);
                    int hCpu = _hoverSampleIdx < Stats.CpuHistory.Count ? Stats.CpuHistory[_hoverSampleIdx] : 0;
                    int hGpu = _hoverSampleIdx < Stats.GpuHistory.Count ? Stats.GpuHistory[_hoverSampleIdx] : 0;
                    int hNvme = (Stats != null && _hoverSampleIdx < Stats.NvmeHistory.Count) ? Stats.NvmeHistory[_hoverSampleIdx] : 0;
                    int hPwr = _hoverSampleIdx < Stats.PowerHistory.Count ? Stats.PowerHistory[_hoverSampleIdx] : 0;
                    double hRam = (Stats != null && _hoverSampleIdx < Stats.RamPowerHistory.Count) ? Stats.RamPowerHistory[_hoverSampleIdx] / 10.0 : 0.0;
                    int hF1  = _hoverSampleIdx < Stats.Fan1RpmHistory.Count ? Stats.Fan1RpmHistory[_hoverSampleIdx] : 0;
                    int hF2  = _hoverSampleIdx < Stats.Fan2RpmHistory.Count ? Stats.Fan2RpmHistory[_hoverSampleIdx] : 0;

                    string hud = HasDualFans
                        ? string.Format("{0}  |  CPU {1}°C • iGPU {2}°C • NVMe {3}°C • Pwr {4}W • RAM {5:0.0}W • APU {6} RPM • Sys {7} RPM",
                            secAgo == 0 ? "Now" : "-" + secAgo + "s", hCpu, hGpu, hNvme, hPwr, hRam, hF1, hF2)
                        : string.Format("{0}  |  CPU {1}°C • iGPU {2}°C • NVMe {3}°C • Pwr {4}W • RAM {5:0.0}W • APU {6} RPM",
                            secAgo == 0 ? "Now" : "-" + secAgo + "s", hCpu, hGpu, hNvme, hPwr, hRam, hF1);

                    Size hudSz = TextRenderer.MeasureText(g, hud, HudFont, new Size(520, 24), TextFormatFlags.NoPadding);
                    int boxW = hudSz.Width + 12;
                    int boxH = hudSz.Height + 6;
                    int boxX = Math.Max(plot.Left + 4, Math.Min(plot.Right - boxW - 4, (int)cx - boxW / 2));
                    int boxY = plot.Top + 4;

                    Color hudBg = IsDarkMode ? Color.FromArgb(225, 0x0F, 0x11, 0x1A) : Color.FromArgb(235, 255, 255, 255);
                    Color hudFg = IsDarkMode ? Color.White : Color.Black;
                    using (SolidBrush hBr = new SolidBrush(hudBg))
                    using (Pen hPen = new Pen(gridColor, 1f))
                    {
                        g.FillRectangle(hBr, boxX, boxY, boxW, boxH);
                        g.DrawRectangle(hPen, boxX, boxY, boxW, boxH);
                    }
                    TextRenderer.DrawText(g, hud, HudFont, new Point(boxX + 6, boxY + 3), hudFg, TextFormatFlags.NoPadding);
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

}
