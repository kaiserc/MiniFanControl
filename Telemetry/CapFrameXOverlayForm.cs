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
    public class CapFrameXOverlayForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE       = -20;
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW  = 0x00000080;

        // Classic CapFrameX & Radeon OSD Palettes
        private static readonly Color OsdBgDark    = Color.FromArgb(16, 18, 28);
        private static readonly Color OsdBorderDark = Color.FromArgb(52, 58, 82);
        private static readonly Color OsdBgRadeon  = Color.FromArgb(20, 8, 12);
        private static readonly Color OsdBorderRadeon = Color.FromArgb(110, 24, 38);

        private static readonly Color CfxAmber   = Color.FromArgb(0xF5, 0x9E, 0x0B);
        private static readonly Color CfxRuby    = Color.FromArgb(0xF4, 0x3F, 0x5E);
        private static readonly Color CfxCyan    = Color.FromArgb(0x38, 0xBD, 0xF8);
        private static readonly Color CfxGreen   = Color.FromArgb(0x22, 0xC5, 0x5E);
        private static readonly Color CfxYellow  = Color.FromArgb(0xFB, 0xBF, 0x24);
        private static readonly Color CfxViolet  = Color.FromArgb(0xC0, 0x84, 0xFC);
        private static readonly Color CfxTeal    = Color.FromArgb(0x2D, 0xD4, 0xBF);
        private static readonly Color CfxWhite   = Color.FromArgb(0xF8, 0xFA, 0xFC);

        public TelemetrySessionStats Stats;
        public bool VerticalLayout = false;
        public bool ClickThrough = false;
        public bool HasDualFans = true;

        // 0 = Text Only, 1 = Mini Sparklines per Metric, 2 = Combined Live Graph, 3 = Both Sparklines + Combined Graph
        public int GraphMode = 3;
        public int GraphHeight = 65; // 45=Compact, 65=Medium, 95=Large, 130=XL
        // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        public int OsdScaleTier = 0;
        public int OpacityPct = 88;
        public bool MatchAppTheme = true;
        public bool IsRadeonTheme = false;

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowPower = true;
        public bool ShowGpuLoad = true;
        public bool ShowApuFan = true;
        public bool ShowSysFan = true;
        public bool ShowMode = true;
        public bool ShowPeaks = true;
        public bool ShowNvme = true;
        public bool ShowRam = true;

        public event EventHandler PositionOrLayoutChanged;
        public event EventHandler RequestHideOverlay;
        public event EventHandler RequestToggleLock;
        public event EventHandler RequestOpenOsdConfig;

        private FanStatus _status;
        private string _modeSummary = "Balanced";
        private bool _dragging;
        private Point _dragStartMouse;
        private Point _dragStartForm;

        private Font _fontLabel;
        private Font _fontVal;
        private Font _fontPeak;
        private Font _fontHdr;
        private Font _fontChart;

        public float GetScaleMultiplier()
        {
            switch (OsdScaleTier)
            {
                case 1: return 1.25f;
                case 2: return 1.50f;
                case 3: return 1.85f;
                default: return 1.0f;
            }
        }

        public void UpdateFontsForScale()
        {
            float sc = GetScaleMultiplier();
            if (_fontLabel != null) _fontLabel.Dispose();
            if (_fontVal != null) _fontVal.Dispose();
            if (_fontPeak != null) _fontPeak.Dispose();
            if (_fontHdr != null) _fontHdr.Dispose();
            if (_fontChart != null) _fontChart.Dispose();

            _fontLabel = new Font("Consolas", 8.75f * sc, FontStyle.Bold);
            _fontVal   = new Font("Consolas", 9.5f * sc, FontStyle.Bold);
            _fontPeak  = new Font("Segoe UI", 6.75f * sc, FontStyle.Regular);
            _fontHdr   = new Font("Segoe UI", 7.5f * sc, FontStyle.Bold);
            _fontChart = new Font("Segoe UI", 6.75f * sc, FontStyle.Bold);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontLabel != null) { _fontLabel.Dispose(); _fontLabel = null; }
                if (_fontVal != null) { _fontVal.Dispose(); _fontVal = null; }
                if (_fontPeak != null) { _fontPeak.Dispose(); _fontPeak = null; }
                if (_fontHdr != null) { _fontHdr.Dispose(); _fontHdr = null; }
                if (_fontChart != null) { _fontChart.Dispose(); _fontChart = null; }
            }
            base.Dispose(disposing);
        }

        public CapFrameXOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(24, 24);
            BackColor = OsdBgDark;
            ForeColor = CfxWhite;
            Opacity = 0.88;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            UpdateFontsForScale();
            UpdateDimensions();
            ContextMenuStrip = BuildOverlayMenu();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                if (ClickThrough)
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void ApplyOpacity(int pct)
        {
            OpacityPct = Math.Max(35, Math.Min(100, pct));
            try
            {
                Opacity = OpacityPct / 100.0;
            }
            catch { }
        }

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
            graphMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem heightMenu = new ToolStripMenuItem("Graph Height");
            heightMenu.DropDownItems.Add("45px Compact", null, (s, e) => SetGraphHeightFromMenu(45));
            heightMenu.DropDownItems.Add("65px Medium", null, (s, e) => SetGraphHeightFromMenu(65));
            heightMenu.DropDownItems.Add("95px Large", null, (s, e) => SetGraphHeightFromMenu(95));
            heightMenu.DropDownItems.Add("130px Extra Large", null, (s, e) => SetGraphHeightFromMenu(130));
            graphMenu.DropDownItems.Add(heightMenu);
            menu.Items.Add(graphMenu);

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
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        public void ApplyClickThrough(bool clickThrough)
        {
            ClickThrough = clickThrough;
            if (IsHandleCreated)
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                if (clickThrough)
                    ex |= (WS_EX_LAYERED | WS_EX_TRANSPARENT);
                else
                    ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
            }
            Cursor = clickThrough ? Cursors.Default : Cursors.SizeAll;
            Invalidate();
        }

        public int CountVisibleMetrics()
        {
            int c = 0;
            if (ShowCpu) c++;
            if (ShowGpu) c++;
            if (ShowPower) c++;
            if (ShowGpuLoad) c++;
            if (ShowApuFan) c++;
            if (HasDualFans && ShowSysFan) c++;
            if (ShowMode) c++;
            if (ShowNvme && _status.NvmeTemp > 0) c++;
            if (ShowRam && _status.RamTotalGb > 0) c++;
            return Math.Max(1, c);
        }

        public int TargetWidth { get; private set; }
        public int TargetHeight { get; private set; }

        public void UpdateDimensions()
        {
            UpdateFontsForScale();
            float sc = GetScaleMultiplier();
            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = (GraphMode == 2 || GraphMode == 3);
            int cols = CountVisibleMetrics();
            int scaledGraphH = (int)Math.Round(GraphHeight * sc);

            if (VerticalLayout)
            {
                int baseW = showSpark ? 244 : (ShowPeaks ? 220 : 192);
                int w = (int)Math.Round(baseW * sc);
                int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                int topH = (int)Math.Round(28 * sc);
                int h = topH + cols * rowH + (int)Math.Round(6 * sc);
                if (showCombined)
                {
                    h += scaledGraphH + (int)Math.Round(10 * sc);
                }
                TargetWidth = w;
                TargetHeight = h;
                Size = new Size(w, h);
            }
            else
            {
                int baseColW = ShowPeaks ? 100 : 88;
                int colW = (int)Math.Round(baseColW * sc);
                int minW = (int)Math.Round((showCombined ? 380 : 160) * sc);
                int w = Math.Max(minW, (int)Math.Round(14 * sc) + cols * colW);
                int topH = (int)Math.Round((showSpark ? 50 : 32) * sc);
                int h = topH + (showCombined ? (scaledGraphH + (int)Math.Round(8 * sc)) : 0);
                TargetWidth = w;
                TargetHeight = h;
                Size = new Size(w, h);
            }
        }

        public void UpdateMetrics(FanStatus st, string modeSummary, bool hasDualFans)
        {
            bool layoutChanged = (HasDualFans != hasDualFans);
            _status = st;
            _modeSummary = string.IsNullOrEmpty(modeSummary) ? st.Mode : modeSummary;
            HasDualFans = hasDualFans;
            if (layoutChanged)
                UpdateDimensions();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !ClickThrough)
            {
                _dragging = true;
                _dragStartMouse = Control.MousePosition;
                _dragStartForm = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point cur = Control.MousePosition;
                Location = new Point(
                    _dragStartForm.X + (cur.X - _dragStartMouse.X),
                    _dragStartForm.Y + (cur.Y - _dragStartMouse.Y));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                if (PositionOrLayoutChanged != null)
                    PositionOrLayoutChanged(this, EventArgs.Empty);
            }
        }

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

        private void DrawCombinedOsdGraph(Graphics g, Rectangle rect, Color cpuCol, Color gpuCol, Color pwrCol, Color apuCol, Color sysCol, Color gridCol)
        {
            if (rect.Width < 20 || rect.Height < 16) return;
            float sc = GetScaleMultiplier();

            Color chartBg = (MatchAppTheme && IsRadeonTheme)
                ? Color.FromArgb(180, 10, 4, 6)
                : Color.FromArgb(180, 10, 12, 18);

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

            int total = Stats.CpuHistory.Count;
            int take = Math.Min(total, 60);
            int start = total - take;

            int tMin = int.MaxValue, tMax = int.MinValue;
            int rMin = int.MaxValue, rMax = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int idx = start + i;
                if (ShowCpu && idx < Stats.CpuHistory.Count)
                {
                    int v = Stats.CpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowGpu && idx < Stats.GpuHistory.Count && Stats.GpuHistory[idx] > 0)
                {
                    int v = Stats.GpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowPower && idx < Stats.PowerHistory.Count && Stats.PowerHistory[idx] > 0)
                {
                    int v = Stats.PowerHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowApuFan && idx < Stats.Fan1RpmHistory.Count)
                {
                    int v = Stats.Fan1RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
                if (HasDualFans && ShowSysFan && idx < Stats.Fan2RpmHistory.Count)
                {
                    int v = Stats.Fan2RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
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

            float rpmLo = (rMin <= rMax) ? Math.Max(0f, rMin - 150f) : 0f;
            float rpmHi = (rMin <= rMax) ? (rMax + 150f) : 3000f;
            if (rpmHi - rpmLo < 400f)
            {
                rpmLo = Math.Max(0f, rMin - 200f);
                rpmHi = rpmLo + 400f;
            }

            int topPad = (int)Math.Round(14f * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - 4), Math.Max(6, rect.Height - topPad - 2));

            if (HasDualFans && ShowSysFan)
                DrawCombinedSubSeries(g, plot, Stats.Fan2RpmHistory, start, take, rpmLo, rpmHi, sysCol, false, true, sc);
            if (ShowApuFan)
                DrawCombinedSubSeries(g, plot, Stats.Fan1RpmHistory, start, take, rpmLo, rpmHi, apuCol, false, true, sc);
            if (ShowPower)
                DrawCombinedSubSeries(g, plot, Stats.PowerHistory, start, take, leftLo, leftHi, pwrCol, false, false, sc);
            if (ShowGpu)
                DrawCombinedSubSeries(g, plot, Stats.GpuHistory, start, take, leftLo, leftHi, gpuCol, false, false, sc);
            if (ShowCpu)
                DrawCombinedSubSeries(g, plot, Stats.CpuHistory, start, take, leftLo, leftHi, cpuCol, true, false, sc);

            // Compact Legend & Scale Header inside the top of the Combined OSD Graph
            string hdr = (tMin <= tMax)
                ? string.Format("LIVE TREND ({0}–{1}°C/W)", tMin, tMax)
                : "LIVE TREND";
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding);
        }

        private static void DrawCombinedSubSeries(Graphics g, Rectangle plot, List<int> list, int start, int take, float lo, float hi, Color col, bool fillArea, bool dashed, float sc)
        {
            if (list == null || list.Count == 0 || take <= 0) return;
            float span = Math.Max(1f, hi - lo);
            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                int idx = Math.Min(list.Count - 1, start);
                float norm = (list[idx] - lo) / span;
                norm = Math.Max(0f, Math.Min(1f, norm));
                float y = plot.Bottom - norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = Math.Min(list.Count - 1, start + i);
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float norm = (list[idx] - lo) / span;
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plot.Bottom - norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }

            if (fillArea)
            {
                PointF[] poly = new PointF[pts.Length + 2];
                Array.Copy(pts, 0, poly, 0, pts.Length);
                poly[pts.Length] = new PointF(pts[pts.Length - 1].X, plot.Bottom);
                poly[pts.Length + 1] = new PointF(pts[0].X, plot.Bottom);
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(plot.X, plot.Y - 2, Math.Max(1, plot.Width), Math.Max(1, plot.Height + 4)),
                    Color.FromArgb(65, col),
                    Color.FromArgb(4, col),
                    LinearGradientMode.Vertical))
                {
                    g.FillPolygon(br, poly);
                }
            }

            using (Pen pen = new Pen(col, (fillArea ? 1.8f : 1.4f) * Math.Max(1f, sc * 0.9f)))
            {
                pen.LineJoin = LineJoin.Round;
                if (dashed) pen.DashStyle = DashStyle.Dash;
                g.DrawLines(pen, pts);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float sc = GetScaleMultiplier();
            int scaledGraphH = (int)Math.Round(GraphHeight * sc);

            bool radeon = MatchAppTheme && IsRadeonTheme;
            Color bg = radeon ? OsdBgRadeon : OsdBgDark;
            Color border = radeon ? OsdBorderRadeon : OsdBorderDark;
            Color accentCol = radeon ? CfxRuby : CfxAmber;
            Color cpuCol = radeon ? CfxRuby : CfxCyan;
            Color gpuCol = radeon ? CfxCyan : CfxGreen;
            Color pwrCol = CfxYellow;
            Color apuCol = CfxViolet;
            Color sysCol = CfxTeal;

            g.Clear(bg);

            // Left accent bar
            using (Brush accentBrush = new SolidBrush(accentCol))
            {
                g.FillRectangle(accentBrush, 0, 0, Math.Max(3, (int)Math.Round(3 * sc)), Height);
            }

            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = (GraphMode == 2 || GraphMode == 3);

            string cpuVal = string.Format("{0}°C", _status.CpuTemp);
            string gpuVal = _status.GpuTemp > 0 ? string.Format("{0}°C", _status.GpuTemp) : "N/A";
            string pwrVal = _status.ApuPowerWatts > 0 ? string.Format("{0}W", _status.ApuPowerWatts) : "--W";
            string f1Val  = string.Format("{0}", _status.Fan1Rpm);
            string f2Val  = _status.Fan2Rpm > 0 ? string.Format("{0}", _status.Fan2Rpm) : "--";

            string cpuPeak = (ShowPeaks && Stats != null && Stats.MaxCpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxCpuTemp) : null;
            string gpuPeak = (ShowPeaks && Stats != null && Stats.MaxGpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxGpuTemp) : null;
            string pwrPeak = (ShowPeaks && Stats != null && Stats.MaxPowerWatts > 0) ? string.Format("▲{0}W", Stats.MaxPowerWatts) : null;

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
                    int cols = CountVisibleMetrics();
                    int availW = Width - (int)Math.Round(12 * sc);
                    float colW = (float)availW / cols;
                    int colIdx = 0;

                    if (ShowCpu)
                        DrawHorizCell(g, colIdx++, colW, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu)
                        DrawHorizCell(g, colIdx++, colW, "iGPU", gpuVal, gpuPeak, bLabel, bGpu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.GpuHistory : null) : null, gpuCol, sc);
                    if (ShowPower)
                        DrawHorizCell(g, colIdx++, colW, "PWR", pwrVal, pwrPeak, bLabel, bPwr, bPeak, sepPen, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowGpuLoad)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = hasFps ? "FPS" : "LOAD";
                        string val = hasFps ? string.Format("{0}", _status.RtssFps) : string.Format("{0}%", _status.GfxActivityPct);
                        string peak = (ShowPeaks && Stats != null)
                            ? (hasFps ? (Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null)
                                      : (Stats.MaxGpuLoad > 0 ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null))
                            : null;
                        Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);
                        using (Brush bLoad = new SolidBrush(loadCol))
                        {
                            DrawHorizCell(g, colIdx++, colW, lbl, val, peak, bLabel, bLoad, bPeak, sepPen, showSpark ? (Stats != null ? Stats.GpuLoadHistory : null) : null, loadCol, sc);
                        }
                    }
                    if (ShowApuFan)
                        DrawHorizCell(g, colIdx++, colW, "APU", f1Val + "r", null, bLabel, bApu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (HasDualFans && ShowSysFan)
                        DrawHorizCell(g, colIdx++, colW, "SYS", f2Val + "r", null, bLabel, bSys, bPeak, sepPen, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShowNvme && _status.NvmeTemp > 0)
                        DrawHorizCell(g, colIdx++, colW, "NVMe", _status.NvmeTemp + "°C", null, bLabel, bNvme, bPeak, sepPen, null, CfxTeal, sc);
                    if (ShowRam && _status.RamTotalGb > 0)
                        DrawHorizCell(g, colIdx++, colW, "RAM", string.Format("{0:0}G", _status.RamUsedGb), null, bLabel, bRam, bPeak, sepPen, null, Color.FromArgb(0x81, 0x8C, 0xF8), sc);
                    if (ShowMode)
                        DrawHorizCell(g, colIdx++, colW, "MODE", _modeSummary, null, bLabel, bWhite, bPeak, sepPen, null, CfxWhite, sc);

                    if (showCombined)
                    {
                        int topH = (int)Math.Round((showSpark ? 50 : 32) * sc);
                        Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), topH, Width - (int)Math.Round(14 * sc), scaledGraphH);
                        DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                    }
                }
                else
                {
                    g.DrawString(radeon ? "RADEON TELEMETRY OSD" : "MINIFANCONTROL OSD", _fontHdr, bLabel, 10f * sc, 6f * sc);
                    g.DrawLine(sepPen, (int)Math.Round(10 * sc), (int)Math.Round(24 * sc), Width - (int)Math.Round(10 * sc), (int)Math.Round(24 * sc));

                    int row = 0;
                    int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                    if (ShowCpu)
                        DrawVertRow(g, row++, rowH, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu)
                        DrawVertRow(g, row++, rowH, "iGPU", gpuVal, gpuPeak, bLabel, bGpu, bPeak, showSpark ? (Stats != null ? Stats.GpuHistory : null) : null, gpuCol, sc);
                    if (ShowPower)
                        DrawVertRow(g, row++, rowH, "Power", pwrVal, pwrPeak, bLabel, bPwr, bPeak, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowGpuLoad)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = hasFps ? "FPS" : "GPU Load";
                        string val;
                        if (hasFps)
                        {
                            val = _status.GfxActivityPct > 0
                                ? string.Format("{0} ({1}%)", _status.RtssFps, _status.GfxActivityPct)
                                : string.Format("{0} FPS", _status.RtssFps);
                        }
                        else
                        {
                            val = _status.GfxClockMhz > 0
                                ? string.Format("{0}% @ {1}MHz", _status.GfxActivityPct, _status.GfxClockMhz)
                                : string.Format("{0}%", _status.GfxActivityPct);
                        }
                        string peak = (ShowPeaks && Stats != null)
                            ? (hasFps ? (Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null)
                                      : (Stats.MaxGpuLoad > 0 ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null))
                            : null;
                        Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);
                        using (Brush bLoad = new SolidBrush(loadCol))
                        {
                            DrawVertRow(g, row++, rowH, lbl, val, peak, bLabel, bLoad, bPeak, showSpark ? (Stats != null ? Stats.GpuLoadHistory : null) : null, loadCol, sc);
                        }
                    }
                    if (ShowApuFan)
                        DrawVertRow(g, row++, rowH, "APU Fan", f1Val + " RPM", null, bLabel, bApu, bPeak, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (HasDualFans && ShowSysFan)
                        DrawVertRow(g, row++, rowH, "Sys Fan", f2Val + " RPM", null, bLabel, bSys, bPeak, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShowNvme && _status.NvmeTemp > 0)
                        DrawVertRow(g, row++, rowH, "NVMe", _status.NvmeTemp + "°C", null, bLabel, bNvme, bPeak, null, CfxTeal, sc);
                    if (ShowRam && _status.RamTotalGb > 0)
                        DrawVertRow(g, row++, rowH, "RAM", string.Format("{0:0.0}G ({1}%)", _status.RamUsedGb, _status.RamLoadPct), null, bLabel, bRam, bPeak, null, Color.FromArgb(0x81, 0x8C, 0xF8), sc);
                    if (ShowMode)
                        DrawVertRow(g, row++, rowH, "Mode", _modeSummary, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);

                    if (showCombined)
                    {
                        int yComb = (int)Math.Round(28 * sc) + row * rowH + (int)Math.Round(4 * sc);
                        Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), yComb, Width - (int)Math.Round(16 * sc), scaledGraphH);
                        DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                    }
                }
            }

            using (Pen borderPen = new Pen(border, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void DrawHorizCell(Graphics g, int colIdx, float colW, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, Pen sepPen, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float x = (8f * sc) + colIdx * colW;
            if (colIdx > 0)
            {
                g.DrawLine(sepPen, x - 3f * sc, 5f * sc, x - 3f * sc, (GraphMode == 1 || GraphMode == 3) ? (45f * sc) : (27f * sc));
            }

            g.DrawString(lbl, _fontLabel, bLbl, x, 6f * sc);
            float lblW = g.MeasureString(lbl, _fontLabel).Width;
            g.DrawString(val, _fontVal, bVal, x + lblW - 1f, 5f * sc);

            if (!string.IsNullOrEmpty(peak))
            {
                float valW = g.MeasureString(val, _fontVal).Width;
                g.DrawString(peak, _fontPeak, bPeak, x + lblW + valW - 3f * sc, 7f * sc);
            }

            if (sparkSamples != null)
            {
                int sparkY = (int)Math.Round(26f * sc);
                int sparkH = (int)Math.Round(20f * sc);
                int sparkW = Math.Max((int)Math.Round(16 * sc), (int)colW - (int)Math.Round(8 * sc));
                Rectangle sparkRect = new Rectangle((int)x + 1, sparkY, sparkW, sparkH);
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }
        }

        private void DrawVertRow(Graphics g, int rowIndex, int rowH, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float y = (27f * sc) + rowIndex * rowH;
            g.DrawString(lbl, _fontLabel, bLbl, 10f * sc, y);

            SizeF sz = g.MeasureString(val, _fontVal);
            float pkW = !string.IsNullOrEmpty(peak) ? g.MeasureString(peak, _fontPeak).Width : 0f;
            float rightX = Width - sz.Width - pkW - (10f * sc);

            if (sparkSamples != null)
            {
                int sparkLeft = (int)Math.Round(68f * sc);
                int sparkW = Math.Max((int)Math.Round(24 * sc), (int)rightX - sparkLeft - (int)Math.Round(6 * sc));
                Rectangle sparkRect = new Rectangle(sparkLeft, (int)y + 1, sparkW, (int)Math.Round(18f * sc));
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }

            g.DrawString(val, _fontVal, bVal, rightX, y - 1f);
            if (!string.IsNullOrEmpty(peak))
            {
                g.DrawString(peak, _fontPeak, bPeak, rightX + sz.Width - 2f, y + 1f);
            }
        }
    }

}
