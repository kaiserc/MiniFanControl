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
        public FanStatus Status { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsDarkMode { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsRadeonTheme { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsAfterburnerTheme { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsXmasTheme { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool HasDualFans { get; set; }

        // 0 = All / Default (CPU), 1 = Highlight CPU Temp, 2 = Highlight iGPU Temp, 3 = Highlight Power W, 7 = RAM, 8 = FCLK, 9 = Ambient
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int HighlightMetric { get; set; }

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowRam = true;
        public bool ShowFabricClock = false;
        public bool ShowNvme = true;
        public bool ShowPower = true;
        public bool ShowAmbient = true;
        public bool ShowApuRpm = true;
        public bool ShowSysRpm = true;
        public bool AutoScale = true;
        public bool UseFahrenheit = false;
        public bool IsPaused = false;
        public bool ShowRamPower { get { return ShowRam; } set { ShowRam = value; } }

        public event EventHandler ResetPeaksRequested;
        public event EventHandler HighlightMetricChanged;
        public event EventHandler TrendVisibilityChanged;

        private int _hoverSampleIdx = -1;
        private int _hoverMouseX = -1;
        private int _hoverPillIdx = -1;
        private readonly Rectangle[] _pillRects = new Rectangle[15]; // 0..8: metrics, 9: AutoZoom, 10: Reset, 11: Pause, 12: All, 13: Temps, 14: Cool
        private TelemetrySessionStats _pausedSnapshot;

        private static TelemetrySessionStats CaptureSnapshot(TelemetrySessionStats src)
        {
            if (src == null) return null;
            TelemetrySessionStats snap = new TelemetrySessionStats();
            if (src.CpuHistory != null) snap.CpuHistory.AddRange(src.CpuHistory);
            if (src.GpuHistory != null) snap.GpuHistory.AddRange(src.GpuHistory);
            if (src.RamTempHistory != null) snap.RamTempHistory.AddRange(src.RamTempHistory);
            if (src.RamPowerHistory != null) snap.RamPowerHistory.AddRange(src.RamPowerHistory);
            if (src.RamLoadHistory != null) snap.RamLoadHistory.AddRange(src.RamLoadHistory);
            if (src.FabricClockHistory != null) snap.FabricClockHistory.AddRange(src.FabricClockHistory);
            if (src.NvmeHistory != null) snap.NvmeHistory.AddRange(src.NvmeHistory);
            if (src.PowerHistory != null) snap.PowerHistory.AddRange(src.PowerHistory);
            if (src.AmbientHistory != null) snap.AmbientHistory.AddRange(src.AmbientHistory);
            if (src.Fan1RpmHistory != null) snap.Fan1RpmHistory.AddRange(src.Fan1RpmHistory);
            if (src.Fan2RpmHistory != null) snap.Fan2RpmHistory.AddRange(src.Fan2RpmHistory);
            return snap;
        }

        private void TogglePause()
        {
            IsPaused = !IsPaused;
            _pausedSnapshot = IsPaused ? CaptureSnapshot(Stats) : null;
            Invalidate();
        }

        private static readonly Font AxisFont = new Font("Segoe UI", 7.0f, FontStyle.Regular);
        private static readonly Font PillFontBold = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font PillFontReg = new Font("Segoe UI", 7.25f, FontStyle.Regular);
        private static readonly Font HudFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        public LiveTelemetryCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "Live Telemetry Trend Graph";
            AccessibleDescription = "Multi-sensor historical trend chart. Press R to reset session peaks.";
            AccessibleRole = AccessibleRole.Chart;
            IsDarkMode = true;
            HasDualFans = true;
            HighlightMetric = 0;
            Cursor = Cursors.Cross;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.R && ResetPeaksRequested != null)
            {
                IsPaused = false;
                _pausedSnapshot = null;
                ResetPeaksRequested(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.P || e.KeyCode == Keys.Space)
            {
                TogglePause();
                e.Handled = true;
            }
        }

        private int _legendRows = 1;

        private Rectangle GetPlotRect()
        {
            int top = _legendRows > 1 ? 48 : 30;
            return new Rectangle(38, top, Math.Max(60, Width - 82), Math.Max(40, Height - top - 20));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool needRepaint = (_hoverSampleIdx != -1 || _hoverPillIdx != -1);
            _hoverSampleIdx = -1;
            _hoverMouseX = -1;
            _hoverPillIdx = -1;
            if (needRepaint) Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int foundPill = -1;
            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    foundPill = i;
                    break;
                }
            }
            bool pillChanged = (foundPill != _hoverPillIdx);
            _hoverPillIdx = foundPill;
            Cursor = foundPill >= 0 ? Cursors.Hand : Cursors.Cross;

            TelemetrySessionStats activeStats = (IsPaused && _pausedSnapshot != null) ? _pausedSnapshot : Stats;
            Rectangle plot = GetPlotRect();
            int count = (activeStats != null && activeStats.CpuHistory != null) ? activeStats.CpuHistory.Count : 0;
            if (count > 0 && plot.Contains(e.Location))
            {
                float normX = (float)(e.X - plot.Left) / Math.Max(1, plot.Width);
                int idx = count == 1 ? 0 : (int)Math.Round(normX * (count - 1));
                idx = Math.Max(0, Math.Min(count - 1, idx));
                if (idx != _hoverSampleIdx || Math.Abs(e.X - _hoverMouseX) > 1 || pillChanged)
                {
                    _hoverSampleIdx = idx;
                    _hoverMouseX = e.X;
                    Invalidate();
                }
            }
            else if (_hoverSampleIdx != -1 || pillChanged)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                Invalidate();
            }
        }

        public void SaveGraphAsPng(string path)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                ContextMenuStrip cm = new ContextMenuStrip();
                cm.Items.Add("Show All Trends", null, (s, args) =>
                {
                    ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowPower = ShowAmbient = ShowApuRpm = true;
                    ShowSysRpm = HasDualFans;
                    Invalidate();
                    if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                });
                cm.Items.Add("Temps Only (CPU / iGPU / RAM / NVMe / Chas)", null, (s, args) =>
                {
                    ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowAmbient = true;
                    ShowPower = ShowApuRpm = ShowSysRpm = false;
                    Invalidate();
                    if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                });
                cm.Items.Add("Power && Fans Only (PWR / APU / SYS)", null, (s, args) =>
                {
                    ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowAmbient = false;
                    ShowPower = ShowApuRpm = true;
                    ShowSysRpm = HasDualFans;
                    Invalidate();
                    if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                });
                cm.Items.Add(new ToolStripSeparator());
                cm.Items.Add("Save Live Graph Snapshot as PNG...", null, (s, args) =>
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "PNG Image (*.png)|*.png";
                        sfd.FileName = string.Format("telemetry-graph-{0:yyyyMMdd-HHmmss}.png", DateTime.Now);
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            SaveGraphAsPng(sfd.FileName);
                        }
                    }
                });
                cm.Items.Add(new ToolStripSeparator());
                cm.Items.Add(IsPaused ? "▶ Resume Live Timeline" : "⏸ Pause / Freeze Timeline", null, (s, args) =>
                {
                    TogglePause();
                });
                cm.Items.Add("Reset Session Peaks", null, (s, args) =>
                {
                    IsPaused = false;
                    _pausedSnapshot = null;
                    if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                });
                Win11VisualStyle.ApplyThemeToMenu(cm, ThemePalette.ResolveFromFlags(IsDarkMode, IsRadeonTheme, IsAfterburnerTheme, IsXmasTheme));
                cm.Closed += (s, args) => { try { cm.Dispose(); } catch { } };
                cm.Show(this, e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            // 1. Priority check for Action & Quick-Filter Pills (Pause, Auto-Zoom, Reset, All, Temps, Cool)
            if (_pillRects[11].Width > 0 && _pillRects[11].Contains(e.Location))
            {
                TogglePause();
                return;
            }
            if (_pillRects[9].Width > 0 && _pillRects[9].Contains(e.Location))
            {
                AutoScale = !AutoScale;
                Invalidate();
                return;
            }
            if (_pillRects[10].Width > 0 && _pillRects[10].Contains(e.Location))
            {
                IsPaused = false;
                _pausedSnapshot = null;
                if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                Invalidate();
                return;
            }
            if (_pillRects[12].Width > 0 && _pillRects[12].Contains(e.Location))
            {
                ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowPower = ShowAmbient = ShowApuRpm = true;
                ShowSysRpm = HasDualFans;
                if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                Invalidate();
                return;
            }
            if (_pillRects[13].Width > 0 && _pillRects[13].Contains(e.Location))
            {
                ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowAmbient = true;
                ShowPower = ShowApuRpm = ShowSysRpm = false;
                if (HighlightMetric == 3 || HighlightMetric == 4 || HighlightMetric == 5)
                {
                    HighlightMetric = 1;
                    if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                }
                if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                Invalidate();
                return;
            }
            if (_pillRects[14].Width > 0 && _pillRects[14].Contains(e.Location))
            {
                ShowCpu = ShowGpu = ShowRam = ShowNvme = ShowAmbient = false;
                ShowPower = ShowApuRpm = true;
                ShowSysRpm = HasDualFans;
                if (HighlightMetric != 3 && HighlightMetric != 4 && HighlightMetric != 5)
                {
                    HighlightMetric = 4;
                    if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                }
                if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                Invalidate();
                return;
            }

            // 2. Metric Pills (0..8) — Shift+Click solos a single series; Left-click spotlights -> hides -> shows
            bool shiftHeld = (Control.ModifierKeys & Keys.Shift) != 0;
            for (int i = 0; i < 9; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    int metricId = (i == 0) ? 1 : (i == 1 ? 2 : (i == 2 ? 7 : (i == 3 ? 8 : (i == 4 ? 6 : (i == 5 ? 3 : (i == 6 ? 9 : (i == 7 ? 4 : 5)))))));
                    if (shiftHeld)
                    {
                        ShowCpu = (i == 0);
                        ShowGpu = (i == 1);
                        ShowRam = (i == 2);
                        ShowFabricClock = (i == 3);
                        ShowNvme = (i == 4);
                        ShowPower = (i == 5);
                        ShowAmbient = (i == 6);
                        ShowApuRpm = (i == 7);
                        ShowSysRpm = (i == 8 && HasDualFans);
                        HighlightMetric = metricId;
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                        if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                        Invalidate();
                        return;
                    }

                    bool curVisible = (i == 0) ? ShowCpu : (i == 1 ? ShowGpu : (i == 2 ? ShowRam : (i == 3 ? ShowFabricClock : (i == 4 ? ShowNvme : (i == 5 ? ShowPower : (i == 6 ? ShowAmbient : (i == 7 ? ShowApuRpm : ShowSysRpm)))))));
                    bool curHighlighted = (HighlightMetric == metricId);
                    bool newVisible;
                    int newHighlight = HighlightMetric;

                    if (!curVisible)
                    {
                        newVisible = true;
                        newHighlight = metricId;
                    }
                    else if (!curHighlighted)
                    {
                        newVisible = true;
                        newHighlight = metricId;
                    }
                    else
                    {
                        newVisible = false;
                        newHighlight = 0;
                    }

                    if (i == 0) ShowCpu = newVisible;
                    else if (i == 1) ShowGpu = newVisible;
                    else if (i == 2) ShowRam = newVisible;
                    else if (i == 3) ShowFabricClock = newVisible;
                    else if (i == 4) ShowNvme = newVisible;
                    else if (i == 5) ShowPower = newVisible;
                    else if (i == 6) ShowAmbient = newVisible;
                    else if (i == 7) ShowApuRpm = newVisible;
                    else if (i == 8) ShowSysRpm = newVisible;

                    HighlightMetric = newHighlight;
                    if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    if (TrendVisibilityChanged != null) TrendVisibilityChanged(this, EventArgs.Empty);
                    Invalidate();
                    return;
                }
            }
        }

        internal Rectangle TestGetPillRect(int idx)
        {
            if (idx >= 0 && idx < _pillRects.Length) return _pillRects[idx];
            return Rectangle.Empty;
        }

        internal void TestOnMouseDown(MouseEventArgs e)
        {
            OnMouseDown(e);
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

                    Win11VisualStyle.FillRoundedRect(g, bgBr, badgeRect, 3);
                    Win11VisualStyle.DrawRoundedCardBorder(g, badgeRect, b.Highlighted ? b.Color : Color.FromArgb(150, b.Color), 3, b.Highlighted ? 1.5f : 1f);
                    TextRenderer.DrawText(g, b.Text, AxisFont, new Point(bx + 4, by + 1), b.Color, TextFormatFlags.NoPadding);
                }
            }
        }

        private void DrawLegendPillAt(Graphics g, int idx, int x, int y, int w, string text, Color seriesColor, bool active, bool highlighted)
        {
            Font f = highlighted ? PillFontBold : PillFontReg;
            int h = 18;
            Rectangle r = new Rectangle(x, y, w, h);
            _pillRects[idx] = r;
            bool hovered = (_hoverPillIdx == idx);

            Color pillBg = !active
                ? (IsDarkMode ? Color.FromArgb(0x1E, 0x1E, 0x2A) : Color.FromArgb(0xE2, 0xE8, 0xF0))
                : (highlighted
                    ? Color.FromArgb(IsDarkMode ? 88 : 56, seriesColor)
                    : Color.FromArgb(hovered ? (IsDarkMode ? 54 : 36) : (IsDarkMode ? 32 : 22), seriesColor));
            Color borderCol = !active
                ? (hovered ? seriesColor : (IsDarkMode ? Color.FromArgb(0x38, 0x3A, 0x4C) : Color.FromArgb(0xCB, 0xD5, 0xE1)))
                : ((highlighted || hovered) ? seriesColor : Color.FromArgb(140, seriesColor));
            Color textCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x6C, 0x70, 0x86) : Color.FromArgb(0x94, 0xA3, 0xB8))
                : (IsDarkMode ? Color.White : Color.Black);

            using (SolidBrush bgBr = new SolidBrush(pillBg))
            {
                Win11VisualStyle.FillRoundedRect(g, bgBr, r, 4);
            }
            Win11VisualStyle.DrawRoundedCardBorder(g, r, borderCol, 4, (highlighted || hovered) ? 1.4f : 1f);
            Size sz = TextRenderer.MeasureText(g, text, f, new Size(w, 20), TextFormatFlags.NoPadding);
            int tx = x + Math.Max(2, (w - sz.Width) / 2);
            TextRenderer.DrawText(g, text, f, new Point(tx, y + 2), textCol, TextFormatFlags.NoPadding);
        }

        private void DrawFlowPill(Graphics g, int idx, ref int curX, ref int curY, int actionRightLimit, string text, Color seriesColor, bool active, bool highlighted)
        {
            if (!active && text != null && text.StartsWith("●"))
                text = "○" + text.Substring(1);
            Font f = highlighted ? PillFontBold : PillFontReg;
            Size sz = TextRenderer.MeasureText(g, text, f, new Size(200, 20), TextFormatFlags.NoPadding);
            int w = sz.Width + 12;
            int h = 18;

            // If pill exceeds row boundary (action buttons on row 1, or right edge on row 2), wrap to row 2
            int limit = (curY == 5) ? actionRightLimit : (Width - 10);
            if (curX + w > limit && curX > 6)
            {
                curY = 26;
                curX = 6;
            }

            Rectangle r = new Rectangle(curX, curY, w, h);
            _pillRects[idx] = r;
            bool hovered = (_hoverPillIdx == idx);

            Color pillBg = !active
                ? (IsDarkMode ? Color.FromArgb(0x1E, 0x1E, 0x2A) : Color.FromArgb(0xE2, 0xE8, 0xF0))
                : (highlighted
                    ? Color.FromArgb(IsDarkMode ? 88 : 56, seriesColor)
                    : Color.FromArgb(hovered ? (IsDarkMode ? 54 : 36) : (IsDarkMode ? 32 : 22), seriesColor));
            Color borderCol = !active
                ? (hovered ? seriesColor : (IsDarkMode ? Color.FromArgb(0x38, 0x3A, 0x4C) : Color.FromArgb(0xCB, 0xD5, 0xE1)))
                : ((highlighted || hovered) ? seriesColor : Color.FromArgb(140, seriesColor));
            Color textCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x6C, 0x70, 0x86) : Color.FromArgb(0x94, 0xA3, 0xB8))
                : (IsDarkMode ? Color.White : Color.Black);

            using (SolidBrush bgBr = new SolidBrush(pillBg))
            {
                Win11VisualStyle.FillRoundedRect(g, bgBr, r, 4);
            }
            Win11VisualStyle.DrawRoundedCardBorder(g, r, borderCol, 4, (highlighted || hovered) ? 1.4f : 1f);
            TextRenderer.DrawText(g, text, f, new Point(curX + 6, curY + 2), textCol, TextFormatFlags.NoPadding);
            curX += w + 5;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = IsXmasTheme
                ? Color.White
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x13, 0x15, 0x19)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0x16, 0x0A, 0x0E)
                        : (IsDarkMode ? Color.FromArgb(0x16, 0x16, 0x22) : Color.FromArgb(0xF8, 0xFA, 0xFC))));
            Color gridColor = IsXmasTheme
                ? Color.FromArgb(0xE4, 0xE4, 0xE7)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x28, 0x30, 0x3A)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0x42, 0x1C, 0x26)
                        : (IsDarkMode ? Color.FromArgb(0x2D, 0x2F, 0x42) : Color.FromArgb(0xE2, 0xE8, 0xF0))));
            Color labelColor = IsXmasTheme
                ? Color.FromArgb(0x18, 0x18, 0x1B)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x8A, 0x96, 0xA8)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                        : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69))));

            Color cpuColor = IsXmasTheme
                ? Color.FromArgb(0xDC, 0x26, 0x26)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x00, 0xE6, 0x76)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                        : (IsDarkMode ? Color.FromArgb(0x60, 0xA5, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB))));
            Color gpuColor = IsXmasTheme
                ? Color.FromArgb(0x18, 0x18, 0x1B)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x00, 0xE5, 0xFF)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0x38, 0xBD, 0xF8)
                        : (IsDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x16, 0xA3, 0x4A))));
            Color nvmeColor = IsXmasTheme ? Color.FromArgb(0x99, 0x1B, 0x1B) : (IsDarkMode ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2));
            Color pwrColor = IsXmasTheme
                ? Color.FromArgb(0xEF, 0x44, 0x44)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0xFF, 0x91, 0x00)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xFB, 0x92, 0x3C)
                        : (IsDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xD9, 0x77, 0x06))));
            Color ramColor = IsXmasTheme ? Color.FromArgb(0x3F, 0x3F, 0x46) : (IsDarkMode ? Color.FromArgb(0x81, 0x8C, 0xF8) : Color.FromArgb(0x4F, 0x46, 0xE5));
            Color fclkColor = IsXmasTheme ? Color.FromArgb(0x52, 0x52, 0x5B) : Color.FromArgb(0x10, 0xB9, 0x81);
            Color ambColor = IsXmasTheme ? Color.FromArgb(0x71, 0x71, 0x7A) : (IsDarkMode ? Color.FromArgb(0xE2, 0xE8, 0xF0) : Color.FromArgb(0x47, 0x55, 0x69));
            Color apuRpmColor = IsXmasTheme ? Color.FromArgb(0xB9, 0x1C, 0x1C) : (IsAfterburnerTheme ? Color.FromArgb(0x00, 0xE6, 0x76) : (IsDarkMode ? Color.FromArgb(0xC0, 0x84, 0xFC) : Color.FromArgb(0x7C, 0x3A, 0xED)));
            Color sysRpmColor = IsXmasTheme ? Color.FromArgb(0x09, 0x09, 0x0B) : (IsAfterburnerTheme ? Color.FromArgb(0x00, 0xE5, 0xFF) : (IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88)));

            g.Clear(bg);
            if (IsXmasTheme && ClientSize.Width > 180 && ClientSize.Height > 100)
            {
                Win11VisualStyle.DrawSnowflake(g, ClientSize.Width - 46f, 64f, 20f, Color.FromArgb(20, 0xDC, 0x26, 0x26), 1.4f);
            }

            TelemetrySessionStats data = (IsPaused && _pausedSnapshot != null) ? _pausedSnapshot : Stats;
            int count = (data != null && data.CpuHistory != null) ? data.CpuHistory.Count : 0;
            int lastCpu = count > 0 ? data.CpuHistory[count - 1] : 0;
            int lastGpu = (data != null && data.GpuHistory.Count > 0) ? data.GpuHistory[data.GpuHistory.Count - 1] : 0;
            int lastRamTemp = (data != null && data.RamTempHistory.Count > 0) ? data.RamTempHistory[data.RamTempHistory.Count - 1] : 0;
            int lastRamLoad = (data != null && data.RamLoadHistory.Count > 0) ? data.RamLoadHistory[data.RamLoadHistory.Count - 1] : 0;
            int lastFclk = (data != null && data.FabricClockHistory.Count > 0) ? data.FabricClockHistory[data.FabricClockHistory.Count - 1] : 0;
            int lastNvme = (data != null && data.NvmeHistory.Count > 0) ? data.NvmeHistory[data.NvmeHistory.Count - 1] : 0;
            int lastPwr = (data != null && data.PowerHistory.Count > 0) ? data.PowerHistory[data.PowerHistory.Count - 1] : 0;
            int lastAmb = (data != null && data.AmbientHistory.Count > 0 && data.AmbientHistory[data.AmbientHistory.Count - 1] > 0) ? data.AmbientHistory[data.AmbientHistory.Count - 1] : (Status.AmbientTemp > 0 ? Status.AmbientTemp : 0);
            int lastF1  = (data != null && data.Fan1RpmHistory.Count > 0) ? data.Fan1RpmHistory[data.Fan1RpmHistory.Count - 1] : 0;
            int lastF2  = (data != null && data.Fan2RpmHistory.Count > 0) ? data.Fan2RpmHistory[data.Fan2RpmHistory.Count - 1] : 0;
            int memClock = Status.RamSpeedMhz > 0 ? Status.RamSpeedMhz : (Status.MemClockMhz * 2);
            string tUnit = TelemetrySessionStats.TempUnit(UseFahrenheit);
            string ramTempPart = (Status.DdrStickCount == 2 && Status.DdrTemp1 > 0 && Status.DdrTemp2 > 0 && Status.DdrTemp1 != Status.DdrTemp2)
                ? string.Format("{0}/{1}{2}", TelemetrySessionStats.ToDisplayTemp(Status.DdrTemp1, UseFahrenheit), TelemetrySessionStats.ToDisplayTemp(Status.DdrTemp2, UseFahrenheit), tUnit)
                : (lastRamTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(lastRamTemp, UseFahrenheit), tUnit) : "");

            string ramPillText;
            if (!string.IsNullOrEmpty(ramTempPart))
            {
                ramPillText = string.Format("● RAM {0}", ramTempPart);
            }
            else if (Status.RamPowerWatts > 0)
            {
                ramPillText = string.Format("● RAM ~{0:0.0}W", Status.RamPowerWatts);
            }
            else
            {
                ramPillText = string.Format("● RAM {0}%", lastRamLoad);
            }

            for (int i = 0; i < _pillRects.Length; i++) _pillRects[i] = Rectangle.Empty;

            // 1. Right Action & Quick-Filter Pills (All, Temps, Cool | Pause, Auto-Zoom, Reset)
            bool isAllPreset = ShowCpu && ShowGpu && ShowRam && ShowNvme && ShowPower && ShowApuRpm && (!HasDualFans || ShowSysRpm);
            bool isTempsPreset = ShowCpu && ShowGpu && ShowRam && ShowNvme && !ShowPower && !ShowApuRpm && !ShowSysRpm;
            bool isCoolPreset = !ShowCpu && !ShowGpu && !ShowRam && !ShowNvme && ShowPower && ShowApuRpm;

            int resetW = 44;
            int autoZoomW = AutoScale ? 70 : 52;
            int pauseW = IsPaused ? 60 : 54;
            int coolW = 38;
            int tempsW = 48;
            int allW = 30;
            int btnY = 5;
            int resetX = Width - resetW - 6;
            int autoZoomX = resetX - autoZoomW - 4;
            int pauseX = autoZoomX - pauseW - 4;
            int coolX = pauseX - coolW - 8;
            int tempsX = coolX - tempsW - 4;
            int allX = tempsX - allW - 4;
            int actionLimit = allX - 8;

            DrawLegendPillAt(g, 10, resetX, btnY, resetW, "Reset", labelColor, true, false);
            DrawLegendPillAt(g, 9, autoZoomX, btnY, autoZoomW, AutoScale ? "Auto-Zoom" : "0–100", labelColor, true, AutoScale);
            DrawLegendPillAt(g, 11, pauseX, btnY, pauseW, IsPaused ? "▶ Live" : "⏸ Pause", IsPaused ? pwrColor : labelColor, true, IsPaused);
            DrawLegendPillAt(g, 14, coolX, btnY, coolW, "Cool", apuRpmColor, true, isCoolPreset);
            DrawLegendPillAt(g, 13, tempsX, btnY, tempsW, "Temps", gpuColor, true, isTempsPreset);
            DrawLegendPillAt(g, 12, allX, btnY, allW, "All", cpuColor, true, isAllPreset);

            // 2. Metric Pills (0..8) - flow left-to-right, wrapping cleanly to row 2 before actionLimit
            int curX = 6;
            int curY = 5;
            DrawFlowPill(g, 0, ref curX, ref curY, actionLimit, string.Format("● CPU {0}{1}", TelemetrySessionStats.ToDisplayTemp(lastCpu, UseFahrenheit), tUnit), cpuColor, ShowCpu, HighlightMetric == 1);
            DrawFlowPill(g, 1, ref curX, ref curY, actionLimit, string.Format("● iGPU {0}{1}", TelemetrySessionStats.ToDisplayTemp(lastGpu, UseFahrenheit), tUnit), gpuColor, ShowGpu, HighlightMetric == 2);
            DrawFlowPill(g, 2, ref curX, ref curY, actionLimit, ramPillText, ramColor, ShowRam, HighlightMetric == 7);
            if (ShowFabricClock && lastFclk > 0)
            {
                DrawFlowPill(g, 3, ref curX, ref curY, actionLimit, string.Format("● FCLK {0}M", lastFclk), fclkColor, ShowFabricClock, HighlightMetric == 8);
            }
            DrawFlowPill(g, 4, ref curX, ref curY, actionLimit, string.Format("● NVMe {0}{1}", TelemetrySessionStats.ToDisplayTemp(lastNvme, UseFahrenheit), tUnit), nvmeColor, ShowNvme, HighlightMetric == 6);
            DrawFlowPill(g, 5, ref curX, ref curY, actionLimit, string.Format("● Pwr {0}W", lastPwr), pwrColor, ShowPower, HighlightMetric == 3);
            if (lastAmb > 0)
            {
                DrawFlowPill(g, 6, ref curX, ref curY, actionLimit, string.Format("● Chas {0}{1}", TelemetrySessionStats.ToDisplayTemp(lastAmb, UseFahrenheit), tUnit), ambColor, ShowAmbient, HighlightMetric == 9);
            }
            DrawFlowPill(g, 7, ref curX, ref curY, actionLimit, string.Format("● APU {0}", lastF1), apuRpmColor, ShowApuRpm, HighlightMetric == 4);
            if (HasDualFans)
            {
                DrawFlowPill(g, 8, ref curX, ref curY, actionLimit, string.Format("● Sys {0}", lastF2), sysRpmColor, ShowSysRpm, HighlightMetric == 5);
            }

            int neededRows = (curY > 5) ? 2 : 1;
            if (neededRows != _legendRows)
            {
                _legendRows = neededRows;
            }
            Rectangle plot = GetPlotRect();

            // Compute Left Axis (Temp °C / Power W) & Right Axis (Fan RPM) scales
            float leftMin = 0f, leftMax = 100f;
            float rpmMin = 0f, rpmMax = 3000f;
            float fclkScaleMin = 0f, fclkScaleMax = 3000f;

            if (AutoScale && count > 0)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                int hl = HighlightMetric == 0 ? 1 : HighlightMetric;

                if (hl == 1 && ShowCpu && Stats.CpuHistory.Count > 0)
                {
                    // Spotlight CPU Temp (Auto-Zoom directly to CPU range, e.g. 45..65°C)
                    for (int i = 0; i < count && i < Stats.CpuHistory.Count; i++)
                    {
                        int v = Stats.CpuHistory[i];
                        if (v > 0)
                        {
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }
                else if (hl == 2 && ShowGpu && Stats.GpuHistory.Count > 0)
                {
                    // Spotlight iGPU Temp
                    for (int i = 0; i < count && i < Stats.GpuHistory.Count; i++)
                    {
                        int v = Stats.GpuHistory[i];
                        if (v > 0)
                        {
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }
                else if (hl == 3 && ShowPower && Stats.PowerHistory.Count > 0)
                {
                    // Spotlight APU Power W
                    for (int i = 0; i < count && i < Stats.PowerHistory.Count; i++)
                    {
                        int v = Stats.PowerHistory[i];
                        if (v > 0)
                        {
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }
                else if (hl == 6 && ShowNvme && Stats.NvmeHistory.Count > 0)
                {
                    // Spotlight NVMe Temp
                    for (int i = 0; i < count && i < Stats.NvmeHistory.Count; i++)
                    {
                        int v = Stats.NvmeHistory[i];
                        if (v > 0)
                        {
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }
                else if (hl == 7 && ShowRam)
                {
                    // Spotlight RAM
                    if (Stats.RamTempHistory.Count > 0 && Stats.RamTempHistory[count - 1] > 0)
                    {
                        for (int i = 0; i < count && i < Stats.RamTempHistory.Count; i++)
                        {
                            int v = Stats.RamTempHistory[i];
                            if (v > 0)
                            {
                                if (v < lo) lo = v;
                                if (v > hi) hi = v;
                            }
                        }
                    }
                    else if (Stats.RamPowerHistory.Count > 0)
                    {
                        for (int i = 0; i < count && i < Stats.RamPowerHistory.Count; i++)
                        {
                            int v = (int)Math.Round(Stats.RamPowerHistory[i] / 10.0);
                            if (v > 0)
                            {
                                if (v < lo) lo = v;
                                if (v > hi) hi = v;
                            }
                        }
                    }
                }
                else if (hl == 9 && ShowAmbient && Stats.AmbientHistory.Count > 0)
                {
                    // Spotlight Chassis Ambient
                    for (int i = 0; i < count && i < Stats.AmbientHistory.Count; i++)
                    {
                        int v = Stats.AmbientHistory[i];
                        if (v > 0)
                        {
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }
                else
                {
                    // Overview: Base scale on thermal sensors (exclude power W so baseline isn't pinned to 0)
                    for (int i = 0; i < count; i++)
                    {
                        if (ShowCpu && i < Stats.CpuHistory.Count && Stats.CpuHistory[i] > 0)
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
                        if (ShowRam && i < Stats.RamTempHistory.Count && Stats.RamTempHistory[i] > 0)
                        {
                            int v = Stats.RamTempHistory[i];
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                        if (ShowNvme && i < Stats.NvmeHistory.Count && Stats.NvmeHistory[i] > 0)
                        {
                            int v = Stats.NvmeHistory[i];
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                        if (ShowAmbient && i < Stats.AmbientHistory.Count && Stats.AmbientHistory[i] > 0)
                        {
                            int v = Stats.AmbientHistory[i];
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                    }
                }

                if (lo <= hi)
                {
                    leftMin = Math.Max(0f, lo - 4f);
                    leftMax = hi + 4f;
                    if (leftMax - leftMin < 12f)
                    {
                        float mid = (leftMin + leftMax) * 0.5f;
                        leftMin = Math.Max(0f, mid - 6f);
                        leftMax = leftMin + 12f;
                    }
                }

                // Right Axis: Fans or FCLK
                int rLo = int.MaxValue, rHi = int.MinValue;
                if (hl == 8 && ShowFabricClock && Stats.FabricClockHistory.Count > 0)
                {
                    // Spotlight FCLK (MHz)
                    for (int i = 0; i < count && i < Stats.FabricClockHistory.Count; i++)
                    {
                        int v = Stats.FabricClockHistory[i];
                        if (v > 0)
                        {
                            if (v < rLo) rLo = v;
                            if (v > rHi) rHi = v;
                        }
                    }
                }
                else
                {
                    // Fan Tachometers (strictly RPM, never MHz)
                    for (int i = 0; i < count; i++)
                    {
                        if (ShowApuRpm && i < Stats.Fan1RpmHistory.Count && Stats.Fan1RpmHistory[i] > 0)
                        {
                            int v = Stats.Fan1RpmHistory[i];
                            if (v < rLo) rLo = v;
                            if (v > rHi) rHi = v;
                        }
                        if (HasDualFans && ShowSysRpm && i < Stats.Fan2RpmHistory.Count && Stats.Fan2RpmHistory[i] > 0)
                        {
                            int v = Stats.Fan2RpmHistory[i];
                            if (v < rLo) rLo = v;
                            if (v > rHi) rHi = v;
                        }
                    }
                }

                if (rLo <= rHi)
                {
                    rpmMin = Math.Max(0f, rLo - 100f);
                    rpmMax = rHi + 100f;
                    if (rpmMax - rpmMin < 400f)
                    {
                        float rMid = (rpmMin + rpmMax) * 0.5f;
                        rpmMin = Math.Max(0f, rMid - 200f);
                        rpmMax = rpmMin + 400f;
                    }
                }

                // Fabric Clock separate scale if fans are active on right axis
                if (ShowFabricClock && Stats != null && Stats.FabricClockHistory.Count > 0)
                {
                    if (hl == 8)
                    {
                        fclkScaleMin = rpmMin;
                        fclkScaleMax = rpmMax;
                    }
                    else
                    {
                        int fLo = int.MaxValue, fHi = int.MinValue;
                        for (int i = 0; i < count && i < Stats.FabricClockHistory.Count; i++)
                        {
                            int v = Stats.FabricClockHistory[i];
                            if (v > 0)
                            {
                                if (v < fLo) fLo = v;
                                if (v > fHi) fHi = v;
                            }
                        }
                        if (fLo <= fHi)
                        {
                            fclkScaleMin = Math.Max(0f, fLo - 100f);
                            fclkScaleMax = fHi + 100f;
                        }
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
                    int hlTag = HighlightMetric == 0 ? 1 : HighlightMetric;
                    bool isTempAxis = (hlTag == 1 || hlTag == 2 || hlTag == 6 || hlTag == 9);
                    int dispLeftVal = (UseFahrenheit && isTempAxis) ? TelemetrySessionStats.ToDisplayTemp(leftVal, true) : leftVal;
                    string lUnit = (hlTag == 3) ? "W" : (isTempAxis ? tUnit : (UseFahrenheit ? "°F/W" : "°/W"));
                    string lStr = dispLeftVal + lUnit;
                    SizeF lSz = g.MeasureString(lStr, AxisFont);
                    g.DrawString(lStr, AxisFont, lblBrush, plot.Left - lSz.Width - 2, y - lSz.Height / 2);

                    int rVal = (int)Math.Round(rpmMin + frac * (rpmMax - rpmMin));
                    string rStr = (HighlightMetric == 8) ? (rVal + "M") : rVal.ToString();
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
                PointF[] cpuPts = ShowCpu ? BuildSeriesPoints(data.CpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] gpuPts = ShowGpu ? BuildSeriesPoints(data.GpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] ramPts = null;
                if (ShowRam && data != null)
                {
                    if (data.RamTempHistory.Count > 0 && data.RamTempHistory[Math.Min(count - 1, data.RamTempHistory.Count - 1)] > 0)
                        ramPts = BuildSeriesPoints(data.RamTempHistory, count, plot, leftMin, leftMax);
                    else if (data.RamPowerHistory.Count > 0)
                        ramPts = BuildSeriesPoints(data.RamPowerHistory, count, plot, leftMin, leftMax, 0.1f);
                }
                PointF[] fclkPts = (ShowFabricClock && data != null && data.FabricClockHistory.Count > 0) ? BuildSeriesPoints(data.FabricClockHistory, count, plot, fclkScaleMin, fclkScaleMax) : null;
                PointF[] nvmePts = (ShowNvme && data != null && data.NvmeHistory.Count > 0) ? BuildSeriesPoints(data.NvmeHistory, count, plot, leftMin, leftMax) : null;
                PointF[] pwrPts = ShowPower ? BuildSeriesPoints(data.PowerHistory, count, plot, leftMin, leftMax) : null;
                PointF[] ambPts = (ShowAmbient && data != null && data.AmbientHistory.Count > 0) ? BuildSeriesPoints(data.AmbientHistory, count, plot, leftMin, leftMax) : null;
                PointF[] f1Pts  = ShowApuRpm ? BuildSeriesPoints(data.Fan1RpmHistory, count, plot, rpmMin, rpmMax) : null;
                PointF[] f2Pts  = (HasDualFans && ShowSysRpm) ? BuildSeriesPoints(data.Fan2RpmHistory, count, plot, rpmMin, rpmMax) : null;

                int hl = HighlightMetric == 0 ? 1 : HighlightMetric;

                // Draw non-highlighted series first, then the highlighted spotlight series on top
                if (f2Pts != null && hl != 5) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, false, true);
                if (f1Pts != null && hl != 4) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, false, true);
                if (ambPts != null && hl != 9) DrawSeriesLine(g, ambPts, plot, ambColor, false, false);
                if (pwrPts != null && hl != 3) DrawSeriesLine(g, pwrPts, plot, pwrColor, false, false);
                if (nvmePts != null && hl != 6) DrawSeriesLine(g, nvmePts, plot, nvmeColor, false, false);
                if (fclkPts != null && hl != 8) DrawSeriesLine(g, fclkPts, plot, fclkColor, false, true);
                if (ramPts != null && hl != 7) DrawSeriesLine(g, ramPts, plot, ramColor, false, false);
                if (gpuPts != null && hl != 2) DrawSeriesLine(g, gpuPts, plot, gpuColor, false, false);
                if (cpuPts != null && hl != 1) DrawSeriesLine(g, cpuPts, plot, cpuColor, false, false);

                if (hl == 1 && cpuPts != null) DrawSeriesLine(g, cpuPts, plot, cpuColor, true, false);
                else if (hl == 2 && gpuPts != null) DrawSeriesLine(g, gpuPts, plot, gpuColor, true, false);
                else if (hl == 7 && ramPts != null) DrawSeriesLine(g, ramPts, plot, ramColor, true, false);
                else if (hl == 8 && fclkPts != null) DrawSeriesLine(g, fclkPts, plot, fclkColor, true, false);
                else if (hl == 6 && nvmePts != null) DrawSeriesLine(g, nvmePts, plot, nvmeColor, true, false);
                else if (hl == 3 && pwrPts != null) DrawSeriesLine(g, pwrPts, plot, pwrColor, true, false);
                else if (hl == 9 && ambPts != null) DrawSeriesLine(g, ambPts, plot, ambColor, true, false);
                else if (hl == 4 && f1Pts != null) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, true, false);
                else if (hl == 5 && f2Pts != null) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, true, false);

                // Endpoint trace badges anchored to the right tip of each curve
                List<CanvasEndpointBadge> badges = new List<CanvasEndpointBadge>();
                if (f2Pts != null && data.Fan2RpmHistory.Count > 0)
                {
                    int v = data.Fan2RpmHistory[Math.Min(count - 1, data.Fan2RpmHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = f2Pts[f2Pts.Length - 1].Y, Text = v + " RPM", Color = sysRpmColor, Highlighted = (hl == 5) });
                }
                if (f1Pts != null && data.Fan1RpmHistory.Count > 0)
                {
                    int v = data.Fan1RpmHistory[Math.Min(count - 1, data.Fan1RpmHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = f1Pts[f1Pts.Length - 1].Y, Text = v + " RPM", Color = apuRpmColor, Highlighted = (hl == 4) });
                }
                if (ambPts != null && data.AmbientHistory.Count > 0)
                {
                    int v = data.AmbientHistory[Math.Min(count - 1, data.AmbientHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = ambPts[ambPts.Length - 1].Y, Text = TelemetrySessionStats.ToDisplayTemp(v, UseFahrenheit) + tUnit, Color = ambColor, Highlighted = (hl == 9) });
                }
                if (pwrPts != null && data.PowerHistory.Count > 0)
                {
                    int v = data.PowerHistory[Math.Min(count - 1, data.PowerHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = pwrPts[pwrPts.Length - 1].Y, Text = v + "W", Color = pwrColor, Highlighted = (hl == 3) });
                }
                if (nvmePts != null && data.NvmeHistory.Count > 0)
                {
                    int v = data.NvmeHistory[Math.Min(count - 1, data.NvmeHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = nvmePts[nvmePts.Length - 1].Y, Text = TelemetrySessionStats.ToDisplayTemp(v, UseFahrenheit) + tUnit, Color = nvmeColor, Highlighted = (hl == 6) });
                }
                if (fclkPts != null && data.FabricClockHistory.Count > 0)
                {
                    int v = data.FabricClockHistory[Math.Min(count - 1, data.FabricClockHistory.Count - 1)];
                    if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = fclkPts[fclkPts.Length - 1].Y, Text = v + " MHz", Color = fclkColor, Highlighted = (hl == 8) });
                }
                if (ramPts != null)
                {
                    if (data.RamTempHistory.Count > 0 && data.RamTempHistory[Math.Min(count - 1, data.RamTempHistory.Count - 1)] > 0)
                    {
                        int v = data.RamTempHistory[Math.Min(count - 1, data.RamTempHistory.Count - 1)];
                        badges.Add(new CanvasEndpointBadge { TargetY = ramPts[ramPts.Length - 1].Y, Text = TelemetrySessionStats.ToDisplayTemp(v, UseFahrenheit) + tUnit, Color = ramColor, Highlighted = (hl == 7) });
                    }
                    else if (data.RamPowerHistory.Count > 0)
                    {
                        int v = data.RamPowerHistory[Math.Min(count - 1, data.RamPowerHistory.Count - 1)];
                        if (v > 0) badges.Add(new CanvasEndpointBadge { TargetY = ramPts[ramPts.Length - 1].Y, Text = string.Format("{0:0.0}W", v / 10.0), Color = ramColor, Highlighted = (hl == 7) });
                    }
                }
                if (gpuPts != null && data.GpuHistory.Count > 0 && data.GpuHistory[Math.Min(count - 1, data.GpuHistory.Count - 1)] > 0)
                {
                    int v = data.GpuHistory[Math.Min(count - 1, data.GpuHistory.Count - 1)];
                    badges.Add(new CanvasEndpointBadge { TargetY = gpuPts[gpuPts.Length - 1].Y, Text = TelemetrySessionStats.ToDisplayTemp(v, UseFahrenheit) + tUnit, Color = gpuColor, Highlighted = (hl == 2) });
                }
                if (cpuPts != null && data.CpuHistory.Count > 0)
                {
                    int v = data.CpuHistory[Math.Min(count - 1, data.CpuHistory.Count - 1)];
                    badges.Add(new CanvasEndpointBadge { TargetY = cpuPts[cpuPts.Length - 1].Y, Text = TelemetrySessionStats.ToDisplayTemp(v, UseFahrenheit) + tUnit, Color = cpuColor, Highlighted = (hl == 1) });
                }

                DrawCanvasBadges(g, plot, badges, IsDarkMode);

                // Subtle frozen-timeline pill indicator when Paused
                if (IsPaused)
                {
                    string pauseBadge = "⏸ TIMELINE PAUSED (Space/P to resume)";
                    Size pSz = TextRenderer.MeasureText(g, pauseBadge, HudFont, new Size(280, 18), TextFormatFlags.NoPadding);
                    int pw = pSz.Width + 12;
                    int ph = 18;
                    int px = plot.Left + Math.Max(4, (plot.Width - pw) / 2);
                    int py = plot.Top + 6;
                    Rectangle pRect = new Rectangle(px, py, pw, ph);
                    using (SolidBrush pBg = new SolidBrush(Color.FromArgb(225, IsDarkMode ? Color.FromArgb(0x2A, 0x1E, 0x0C) : Color.FromArgb(0xFF, 0xFB, 0xEB))))
                    {
                        Win11VisualStyle.FillRoundedRect(g, pBg, pRect, 4);
                    }
                    Win11VisualStyle.DrawRoundedCardBorder(g, pRect, pwrColor, 4, 1.2f);
                    TextRenderer.DrawText(g, pauseBadge, HudFont, new Point(px + 6, py + 2), pwrColor, TextFormatFlags.NoPadding);
                }

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
                    int hCpu = _hoverSampleIdx < data.CpuHistory.Count ? data.CpuHistory[_hoverSampleIdx] : 0;
                    int hGpu = _hoverSampleIdx < data.GpuHistory.Count ? data.GpuHistory[_hoverSampleIdx] : 0;
                    int hRamTemp = (data != null && _hoverSampleIdx < data.RamTempHistory.Count) ? data.RamTempHistory[_hoverSampleIdx] : 0;
                    int hRamLoad = (data != null && _hoverSampleIdx < data.RamLoadHistory.Count) ? data.RamLoadHistory[_hoverSampleIdx] : 0;
                    int hFclk = (data != null && _hoverSampleIdx < data.FabricClockHistory.Count) ? data.FabricClockHistory[_hoverSampleIdx] : 0;
                    int hNvme = (data != null && _hoverSampleIdx < data.NvmeHistory.Count) ? data.NvmeHistory[_hoverSampleIdx] : 0;
                    int hPwr = _hoverSampleIdx < data.PowerHistory.Count ? data.PowerHistory[_hoverSampleIdx] : 0;
                    int hAmb = (data != null && _hoverSampleIdx < data.AmbientHistory.Count) ? data.AmbientHistory[_hoverSampleIdx] : 0;
                    int hF1  = _hoverSampleIdx < data.Fan1RpmHistory.Count ? data.Fan1RpmHistory[_hoverSampleIdx] : 0;
                    int hF2  = _hoverSampleIdx < data.Fan2RpmHistory.Count ? data.Fan2RpmHistory[_hoverSampleIdx] : 0;

                    string hTempPart = (Status.DdrStickCount == 2 && Status.DdrTemp1 > 0 && Status.DdrTemp2 > 0 && Status.DdrTemp1 != Status.DdrTemp2)
                        ? string.Format("{0}/{1}{2}", TelemetrySessionStats.ToDisplayTemp(Status.DdrTemp1, UseFahrenheit), TelemetrySessionStats.ToDisplayTemp(Status.DdrTemp2, UseFahrenheit), tUnit)
                        : (hRamTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(hRamTemp, UseFahrenheit), tUnit) : "");

                    string hPwrPart = Status.RamPowerWatts > 0 ? string.Format(" ~{0:0.0}W", Status.RamPowerWatts) : "";
                    string hNvmePwrPart = Status.NvmePowerWatts > 0 ? string.Format(" (~{0:0.0}W)", Status.NvmePowerWatts) : "";
                    string hRamStr = !string.IsNullOrEmpty(hTempPart)
                        ? (memClock > 0 ? string.Format("RAM {0} {1}% @ {2}MHz{3}", hTempPart, hRamLoad, memClock, hPwrPart) : string.Format("RAM {0} {1}%{2}", hTempPart, hRamLoad, hPwrPart))
                        : (memClock > 0 ? string.Format("RAM {0}% @ {1}MHz{2}", hRamLoad, memClock, hPwrPart) : string.Format("RAM {0}%{1}", hRamLoad, hPwrPart));

                    string fclkPart = (ShowFabricClock && hFclk > 0) ? string.Format(" • FCLK {0}MHz", hFclk) : "";
                    string ambPart = hAmb > 0 ? string.Format(" • Chas {0}{1}", TelemetrySessionStats.ToDisplayTemp(hAmb, UseFahrenheit), tUnit) : "";
                    string sysPart = (HasDualFans && hF2 > 0) ? string.Format(" • Sys {0} RPM", hF2) : "";

                    string hud = string.Format("{0}  |  CPU {1}{11} • iGPU {2}{11} • {3}{4} • NVMe {5}{11}{6} • Pwr {7}W{8} • APU {9} RPM{10}",
                        secAgo == 0 ? (IsPaused ? "Frozen" : "Now") : "-" + secAgo + "s",
                        TelemetrySessionStats.ToDisplayTemp(hCpu, UseFahrenheit),
                        TelemetrySessionStats.ToDisplayTemp(hGpu, UseFahrenheit),
                        hRamStr, fclkPart,
                        TelemetrySessionStats.ToDisplayTemp(hNvme, UseFahrenheit),
                        hNvmePwrPart, hPwr, ambPart, hF1, sysPart, tUnit);

                    Size hudSz = TextRenderer.MeasureText(g, hud, HudFont, new Size(520, 24), TextFormatFlags.NoPadding);
                    int boxW = hudSz.Width + 12;
                    int boxH = hudSz.Height + 6;
                    int boxX = Math.Max(plot.Left + 4, Math.Min(plot.Right - boxW - 4, (int)cx - boxW / 2));
                    int boxY = plot.Top + 4;

                    Color hudBg = IsDarkMode ? Color.FromArgb(232, 0x0F, 0x11, 0x1A) : Color.FromArgb(242, 255, 255, 255);
                    Color hudFg = IsDarkMode ? Color.White : Color.Black;
                    Rectangle hudRect = new Rectangle(boxX, boxY, boxW, boxH);
                    using (SolidBrush hBr = new SolidBrush(hudBg))
                    {
                        Win11VisualStyle.FillRoundedRect(g, hBr, hudRect, 4);
                    }
                    Win11VisualStyle.DrawRoundedCardBorder(g, hudRect, gridColor, 4, 1f);
                    TextRenderer.DrawText(g, hud, HudFont, new Point(boxX + 6, boxY + 3), hudFg, TextFormatFlags.NoPadding);
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4)));
            }
        }
    }

}
