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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const int GWL_EXSTYLE       = -20;
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW  = 0x00000080;
        private const uint LWA_ALPHA        = 0x02;
        private const uint SWP_NOSIZE       = 0x0001;
        private const uint SWP_NOMOVE       = 0x0002;
        private const uint SWP_NOACTIVATE   = 0x0010;
        private const uint SWP_SHOWWINDOW   = 0x0040;

        // Classic CapFrameX & Radeon OSD Palettes
        private static readonly Color OsdBgDark    = Color.FromArgb(16, 18, 28);
        private static readonly Color OsdBorderDark = Color.FromArgb(52, 58, 82);
        private static readonly Color OsdBgRadeon  = Color.FromArgb(20, 8, 12);
        private static readonly Color OsdBorderRadeon = Color.FromArgb(110, 24, 38);

        // MSI Afterburner Homage Palette (Gunmetal carbon, toxic neon green & cyberpunk cyan)
        private static readonly Color OsdBgAfterburner     = Color.FromArgb(17, 19, 23);
        private static readonly Color OsdBorderAfterburner = Color.FromArgb(0x00, 0xCE, 0x68);
        private static readonly Color AbGreen  = Color.FromArgb(0x00, 0xE6, 0x76);
        private static readonly Color AbCyan   = Color.FromArgb(0x00, 0xE5, 0xFF);
        private static readonly Color AbAmber  = Color.FromArgb(0xFF, 0x91, 0x00);
        private static readonly Color AbWhite  = Color.FromArgb(0xEE, 0xF2, 0xF6);

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
        public int GraphHeight = 105; // 75=Compact, 105=Medium, 140=Large, 180=XL
        // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        public int OsdScaleTier = 0;
        public int OpacityPct = 88;
        public bool MatchAppTheme = true;
        public bool IsRadeonTheme = false;
        public bool IsAfterburnerTheme = false;

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowPower = true;
        public bool ShowFps = true;
        public bool ShowLows = true;
        public bool ShowGpuLoad = true;
        public bool ShowApuFan = true;
        public bool ShowSysFan = true;
        public bool ShowMode = true;
        public bool ShowPeaks = true;
        public bool ShowNvme = true;
        public bool ShowRam = true;
        public bool ShowDriverVersion = false;
        public bool ShowClocks = false;
        public bool SparklinesAsBackground = false;
        public bool ShowRadeonSettings = true;
        public bool ShowLiveTrend = true;
        public bool PlotFpsOnTrend = true;
        public bool ShowFpsGraph = true;
        public bool DfGraphStyle = false;
        public bool DfGraphFirst = false;

        public event EventHandler PositionOrLayoutChanged;
        public event EventHandler RequestHideOverlay;
        public event EventHandler RequestToggleLock;
        public event EventHandler RequestOpenOsdConfig;
        public event EventHandler<int> RequestSnapLocation;

        private FanStatus _status;
        private string _modeSummary = "Balanced";
        private bool _dragging;
        private Point _dragStartMouse;
        private Point _dragStartForm;

        private Rectangle _combRect;
        private Rectangle _fpsRect;
        private Rectangle _dfFtRect;
        private Rectangle _dfFpsRect;
        private int _hoverMouseX = -1;
        private int _hoverMouseY = -1;

        private int _currentFpsCeiling = 80;
        private int _fpsCeilingHoldCounter = 0;
        private int _currentFtCeiling = 50;
        private int _ftCeilingHoldCounter = 0;

        private Font _fontLabel;
        private Font _fontVal;
        private Font _fontPeak;
        private Font _fontHdr;
        private Font _fontChart;
        private Font _fontTrace;
        private Font _fontDfHdr;
        private Font _fontDfAxis;

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
            if (_fontTrace != null) _fontTrace.Dispose();
            if (_fontDfHdr != null) _fontDfHdr.Dispose();
            if (_fontDfAxis != null) _fontDfAxis.Dispose();

            _fontLabel = new Font("Consolas", 8.75f * sc, FontStyle.Bold);
            _fontVal   = new Font("Consolas", 9.5f * sc, FontStyle.Bold);
            _fontPeak  = new Font("Segoe UI", 6.75f * sc, FontStyle.Regular);
            _fontHdr   = new Font("Segoe UI", 7.5f * sc, FontStyle.Bold);
            _fontChart = new Font("Segoe UI", 6.75f * sc, FontStyle.Bold);
            _fontTrace = new Font("Segoe UI", 6.0f * sc, FontStyle.Bold);
            _fontDfHdr = TryCreateCondensedFont("Bahnschrift", 6.8f * sc, FontStyle.Bold, "Segoe UI");
            _fontDfAxis = TryCreateCondensedFont("Bahnschrift", 5.8f * sc, FontStyle.Bold, "Segoe UI");
        }

        private static Font TryCreateCondensedFont(string preferredFamily, float sizePt, FontStyle style, string fallbackFamily)
        {
            try
            {
                using (FontFamily ff = new FontFamily(preferredFamily))
                {
                    if (ff.Name.Equals(preferredFamily, StringComparison.OrdinalIgnoreCase))
                        return new Font(preferredFamily, sizePt, style);
                }
            }
            catch { }
            return new Font(fallbackFamily, sizePt, style);
        }

        public static int GetDfFrametimeHeight(float sc)
        {
            return GetDfFrametimeHeight(sc, (int)Math.Round(105 * sc));
        }

        public static int GetDfFrametimeHeight(float sc, int scaledGraphH)
        {
            // Proportionally scale frametime graph with selected graph height tier so it is never a flat squished box
            return Math.Max((int)Math.Round(52 * sc), (int)Math.Round(scaledGraphH * 0.52f));
        }

        public static int GetDfFramerateHeight(float sc, int scaledGraphH)
        {
            // Generous vertical room across all tiers (75px, 105px, 140px, 180px) ensuring 4-tier DF gridlines fit cleanly
            return Math.Max((int)Math.Round(80 * sc), (int)Math.Round(scaledGraphH * 0.95f));
        }

        public static int CalculateDfCeiling(int curLiveFps, IList<int> fpsHistory)
        {
            int total = (fpsHistory != null) ? fpsHistory.Count : 0;
            int lookback = Math.Min(total, 20);
            int activeMaxFps = curLiveFps > 0 ? curLiveFps : 60;
            if (lookback > 0)
            {
                List<int> recent = new List<int>(lookback);
                for (int i = 0; i < lookback; i++)
                {
                    int f = fpsHistory[total - lookback + i];
                    if (f > 0) recent.Add(f);
                }
                if (recent.Count > 0)
                {
                    recent.Sort();
                    int p90Idx = Math.Min(recent.Count - 1, (int)Math.Ceiling(recent.Count * 0.90) - 1);
                    activeMaxFps = Math.Max(recent[p90Idx], curLiveFps);
                }
            }

            if (activeMaxFps > 235)
                return (int)(Math.Ceiling(activeMaxFps / 60.0) * 60);
            if (activeMaxFps > 170)
                return 240;
            if (activeMaxFps > 115)
                return 180;
            if (activeMaxFps > 75)
                return 120;
            if (activeMaxFps > 38)
                return 80; // 58-62 FPS stays rock solid at 80 ceiling with 20 FPS headroom above 60 line
            return 60;
        }

        public static int CalculateDfFrametimeCeiling(double curFtMs, IList<int> ftHistory)
        {
            double activeMaxFt = curFtMs > 0 ? curFtMs : 16.6;
            if (ftHistory != null && ftHistory.Count > 0)
            {
                int total = ftHistory.Count;
                int lookback = Math.Min(total, 30);
                List<double> recent = new List<double>(lookback);
                for (int i = 0; i < lookback; i++)
                {
                    int raw = ftHistory[total - lookback + i];
                    if (raw > 0) recent.Add(raw / 10.0);
                }
                if (recent.Count > 0)
                {
                    recent.Sort();
                    int p90 = Math.Min(recent.Count - 1, (int)Math.Ceiling(recent.Count * 0.90) - 1);
                    activeMaxFt = Math.Max(recent[p90], curFtMs);
                }
            }

            if (activeMaxFt > 95.0)
                return (int)(Math.Ceiling(activeMaxFt / 50.0) * 50);
            if (activeMaxFt > 65.0)
                return 100;
            if (activeMaxFt > 38.0)
                return 75;
            return 50; // Zoomed in default for clean pacing (16.6ms / 19.3ms gameplay)
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
                if (_fontTrace != null) { _fontTrace.Dispose(); _fontTrace = null; }
                if (_fontDfHdr != null) { _fontDfHdr.Dispose(); _fontDfHdr = null; }
                if (_fontDfAxis != null) { _fontDfAxis.Dispose(); _fontDfAxis = null; }
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayeredAttributes();
            EnsureTopMost();
        }

        public void EnsureTopMost()
        {
            if (IsHandleCreated)
            {
                try
                {
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
                catch { }
            }
        }

        public void ApplyLayeredAttributes()
        {
            if (IsHandleCreated)
            {
                try
                {
                    byte alpha = (byte)Math.Max(30, Math.Min(255, (int)Math.Round(OpacityPct * 255.0 / 100.0)));
                    SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
                }
                catch { }
            }
        }

        public void ApplyOpacity(int pct)
        {
            OpacityPct = Math.Max(35, Math.Min(100, pct));
            try
            {
                if (OpacityPct >= 100)
                    Opacity = 1.0;
                else
                    Opacity = OpacityPct / 100.0;
            }
            catch { }
            ApplyLayeredAttributes();
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
            heightMenu.DropDownItems.Add("75px Compact", null, (s, e) => SetGraphHeightFromMenu(75));
            heightMenu.DropDownItems.Add("105px Medium", null, (s, e) => SetGraphHeightFromMenu(105));
            heightMenu.DropDownItems.Add("140px Large", null, (s, e) => SetGraphHeightFromMenu(140));
            heightMenu.DropDownItems.Add("180px Extra Large", null, (s, e) => SetGraphHeightFromMenu(180));
            graphMenu.DropDownItems.Add(heightMenu);
            graphMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMetricToggleMenuItem(graphMenu, "Live Trend Graph (Multi-Sensor)", () => ShowLiveTrend, v => ShowLiveTrend = v);
            AddMetricToggleMenuItem(graphMenu, "FPS & Frametime Graph", () => ShowFpsGraph, v => ShowFpsGraph = v);
            AddMetricToggleMenuItem(graphMenu, "Plot FPS on Live Trend", () => PlotFpsOnTrend, v => PlotFpsOnTrend = v);
            AddMetricToggleMenuItem(graphMenu, "Digital Foundry (DF) Style Graphs", () => DfGraphStyle, v => DfGraphStyle = v);
            AddMetricToggleMenuItem(graphMenu, "Show DF Graphs Before Live Trend", () => DfGraphFirst, v => DfGraphFirst = v);
            AddMetricToggleMenuItem(graphMenu, "Background Sparklines (Underlay)", () => SparklinesAsBackground, v => { SparklinesAsBackground = v; Invalidate(); });
            menu.Items.Add(graphMenu);

            ToolStripMenuItem metricsMenu = new ToolStripMenuItem("Visible Telemetry Metrics");
            AddMetricToggleMenuItem(metricsMenu, "CPU Temperature", () => ShowCpu, v => ShowCpu = v);
            AddMetricToggleMenuItem(metricsMenu, "iGPU Temperature", () => ShowGpu, v => ShowGpu = v);
            AddMetricToggleMenuItem(metricsMenu, "Clock Speeds (@ GHz / MHz)", () => ShowClocks, v => { ShowClocks = v; UpdateDimensions(); Invalidate(); });
            AddMetricToggleMenuItem(metricsMenu, "NVMe SSD Temp", () => ShowNvme, v => ShowNvme = v);
            AddMetricToggleMenuItem(metricsMenu, "APU Power (W)", () => ShowPower, v => ShowPower = v);
            AddMetricToggleMenuItem(metricsMenu, "RAM Load & Power", () => ShowRam, v => ShowRam = v);
            AddMetricToggleMenuItem(metricsMenu, "FPS & Frametime", () => ShowFps, v => ShowFps = v);
            AddMetricToggleMenuItem(metricsMenu, "0.1% Low & Avg FPS", () => ShowLows, v => ShowLows = v);
            AddMetricToggleMenuItem(metricsMenu, "GPU Load & Clock", () => ShowGpuLoad, v => ShowGpuLoad = v);
            AddMetricToggleMenuItem(metricsMenu, "APU Fan Speed", () => ShowApuFan, v => ShowApuFan = v);
            if (HasDualFans)
                AddMetricToggleMenuItem(metricsMenu, "System Fan Speed", () => ShowSysFan, v => ShowSysFan = v);
            AddMetricToggleMenuItem(metricsMenu, "Thermal Mode", () => ShowMode, v => ShowMode = v);
            AddMetricToggleMenuItem(metricsMenu, "Peak Values (▲)", () => ShowPeaks, v => ShowPeaks = v);
            AddMetricToggleMenuItem(metricsMenu, "Radeon Driver Version", () => ShowDriverVersion, v => ShowDriverVersion = v);
            AddMetricToggleMenuItem(metricsMenu, "Radeon Features (Tags: FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)", () => ShowRadeonSettings, v => ShowRadeonSettings = v);
            menu.Items.Add(metricsMenu);

            ToolStripMenuItem themeMenu = new ToolStripMenuItem("OSD Theme Palette");
            themeMenu.DropDownItems.Add("Match Application Theme", null, (s, e) => { MatchAppTheme = true; Invalidate(); });
            themeMenu.DropDownItems.Add("Classic CapFrameX Dark (Cyan/Amber)", null, (s, e) => { MatchAppTheme = false; IsRadeonTheme = false; IsAfterburnerTheme = false; Invalidate(); });
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

        public void ApplyClickThrough(bool clickThrough)
        {
            ClickThrough = clickThrough;
            if (IsHandleCreated)
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                if (clickThrough)
                    ex |= (WS_EX_LAYERED | WS_EX_TRANSPARENT);
                else
                    ex = (ex | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
                ApplyLayeredAttributes();
                EnsureTopMost();
            }
            Cursor = clickThrough ? Cursors.Default : Cursors.SizeAll;
            Invalidate();
        }

        public int CountVisibleMetrics()
        {
            int c = 0;
            if (ShowCpu) c++;
            if (ShowGpu || ShowGpuLoad) c++;
            if (ShowNvme && _status.NvmeTemp > 0) c++;
            if (ShowPower) c++;
            if (ShowRam && _status.RamTotalGb > 0) c++;
            if (ShowFps) c++;
            if (ShowLows) c++;
            if (ShowApuFan) c++;
            if (HasDualFans && ShowSysFan) c++;
            if (ShowMode) c++;
            if (ShowDriverVersion && !string.IsNullOrEmpty(_status.RadeonSoftwareVer)) c++;
            if (ShowRadeonSettings && _status.RadeonFeaturesAvailable)
            {
                List<string> tags = _status.GetActiveRadeonTags();
                if (tags != null && tags.Count > 0) c++;
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

        private List<HorizColumn> BuildHorizontalColumns(
            Graphics g, float sc, bool showSpark,
            Brush bLabel, Brush bCpu, Brush bGpu, Brush bNvme, Brush bPwr, Brush bRam,
            Brush bFps, Brush bLow, Brush bLoad, Brush bApu, Brush bSys, Brush bWhite, Brush bPeak,
            Color cpuCol, Color gpuCol, Color pwrCol, Color loadCol, Color apuCol, Color sysCol)
        {
            List<HorizColumn> list = new List<HorizColumn>();

            string cpuVal;
            if (ShowClocks && _status.CpuClockMhz > 0)
            {
                cpuVal = _status.CpuLoadPct >= 0
                    ? string.Format("{0}°C {1}% @ {2:0.0}G", _status.CpuTemp, _status.CpuLoadPct, _status.CpuClockMhz / 1000.0)
                    : string.Format("{0}°C @ {1:0.0}G", _status.CpuTemp, _status.CpuClockMhz / 1000.0);
            }
            else
            {
                cpuVal = _status.CpuLoadPct >= 0
                    ? string.Format("{0}°C {1}%", _status.CpuTemp, _status.CpuLoadPct)
                    : string.Format("{0}°C", _status.CpuTemp);
            }

            bool showGpuTemp = ShowGpu;
            bool showGpuLoad = ShowGpuLoad;

            bool hasGpuTemp = showGpuTemp && _status.GpuTemp > 0;
            bool hasGpuLoad = showGpuLoad && (_status.GfxActivityPct >= 0);
            bool hasGpuClock = showGpuLoad && (_status.GfxClockMhz > 0);

            string horizGpuTempPart = hasGpuTemp
                ? (_status.GpuHotspotTemp > 0 ? string.Format("{0}°C (🔥{1}°)", _status.GpuTemp, _status.GpuHotspotTemp) : string.Format("{0}°C", _status.GpuTemp))
                : null;

            string horizGpuLoadPart = null;
            if (hasGpuLoad && ShowClocks && hasGpuClock)
                horizGpuLoadPart = string.Format("{0}% @ {1}M", _status.GfxActivityPct, _status.GfxClockMhz);
            else if (hasGpuLoad)
                horizGpuLoadPart = string.Format("{0}%", _status.GfxActivityPct);
            else if (ShowClocks && hasGpuClock)
                horizGpuLoadPart = string.Format("@ {0}M", _status.GfxClockMhz);

            string gpuVal;
            if (horizGpuTempPart != null && horizGpuLoadPart != null)
                gpuVal = string.Format("{0} {1}", horizGpuTempPart, horizGpuLoadPart);
            else if (horizGpuTempPart != null)
                gpuVal = horizGpuTempPart;
            else if (horizGpuLoadPart != null)
                gpuVal = horizGpuLoadPart;
            else
                gpuVal = "N/A";

            string nvmeVal = string.Format("{0}°C", _status.NvmeTemp);
            string pwrVal = _status.ApuPowerWatts > 0
                ? (_status.ApuPowerLimitWatts > 0 ? string.Format("{0}W/{1}W", _status.ApuPowerWatts, _status.ApuPowerLimitWatts) : string.Format("{0}W", _status.ApuPowerWatts))
                : "--W";
            string f1Val  = string.Format("{0}r", _status.Fan1Rpm);
            string f2Val  = _status.Fan2Rpm > 0 ? string.Format("{0}r", _status.Fan2Rpm) : "--";

            string cpuTempPeak = (ShowPeaks && Stats != null && Stats.MaxCpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxCpuTemp) : null;
            string cpuLoadPeak = (ShowPeaks && Stats != null && Stats.MaxCpuLoad > 0) ? string.Format("▲{0}%", Stats.MaxCpuLoad) : null;
            string cpuPeak = null;
            if (cpuTempPeak != null && cpuLoadPeak != null)
                cpuPeak = string.Format("{0} {1}", cpuTempPeak, cpuLoadPeak);
            else if (cpuTempPeak != null)
                cpuPeak = cpuTempPeak;
            else if (cpuLoadPeak != null)
                cpuPeak = cpuLoadPeak;
            string gpuTempPeak = (ShowPeaks && showGpuTemp && Stats != null && Stats.MaxGpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxGpuTemp) : null;
            string gpuLoadPeak = (ShowPeaks && showGpuLoad && Stats != null && Stats.MaxGpuLoad > 0) ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null;
            string gpuPeak = null;
            if (gpuTempPeak != null && gpuLoadPeak != null)
                gpuPeak = string.Format("{0} {1}", gpuTempPeak, gpuLoadPeak);
            else if (gpuTempPeak != null)
                gpuPeak = gpuTempPeak;
            else if (gpuLoadPeak != null)
                gpuPeak = gpuLoadPeak;

            string nvmePeak = (ShowPeaks && Stats != null && Stats.MaxNvmeTemp > 0) ? string.Format("▲{0}°", Stats.MaxNvmeTemp) : null;
            string pwrPeak = (ShowPeaks && Stats != null && Stats.MaxPowerWatts > 0) ? string.Format("▲{0}W", Stats.MaxPowerWatts) : null;
            string ramPeak = (ShowPeaks && Stats != null && Stats.MaxRamPowerWatts > 0) ? string.Format("▲{0:0.0}W", Stats.MaxRamPowerWatts) : null;

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
                list.Add(new HorizColumn {
                    Key = "iGPU", Label = "iGPU", Value = gpuVal, Peak = gpuPeak,
                    LabelBrush = bLabel, ValueBrush = bGpu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? (showGpuTemp ? Stats.GpuHistory : Stats.GpuLoadHistory) : null) : null,
                    SparkColor = gpuCol
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

            if (ShowRam && _status.RamTotalGb > 0)
            {
                string ramVal = (ShowClocks && _status.MemClockMhz > 0)
                    ? string.Format("{0}% @ {1}M", _status.RamLoadPct, _status.MemClockMhz)
                    : (_status.RamPowerWatts > 0 ? string.Format("{0}% ~{1:0}W", _status.RamLoadPct, _status.RamPowerWatts) : string.Format("{0}%", _status.RamLoadPct));
                list.Add(new HorizColumn {
                    Key = "RAM", Label = "RAM", Value = ramVal, Peak = ramPeak,
                    LabelBrush = bLabel, ValueBrush = bRam, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.RamPowerHistory : null) : null,
                    SparkColor = Color.FromArgb(0x81, 0x8C, 0xF8)
                });
            }

            if (ShowFps)
            {
                bool hasFps = _status.RtssFps > 0;
                string lbl = "FPS";
                string val = hasFps ? string.Format("{0}", _status.RtssFps) : "--";
                int sessionAvg = (Stats != null && Stats.AvgFps > 0)
                    ? (int)Math.Round(Stats.AvgFps)
                    : (_status.RtssAvgFps > 0 ? _status.RtssAvgFps : _status.RtssFps);
                if (Stats != null && Stats.MaxFps > 0 && sessionAvg > Stats.MaxFps)
                    sessionAvg = Stats.MaxFps;

                string peak = hasFps
                    ? (sessionAvg > 0 ? string.Format("Avg:{0}", sessionAvg) : (ShowPeaks && Stats != null && Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null))
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


            if (ShowApuFan)
            {
                list.Add(new HorizColumn {
                    Key = "APU", Label = "APU", Value = f1Val, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bApu, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null,
                    SparkColor = apuCol
                });
            }

            if (HasDualFans && ShowSysFan)
            {
                list.Add(new HorizColumn {
                    Key = "SYS", Label = "SYS", Value = f2Val, Peak = null,
                    LabelBrush = bLabel, ValueBrush = bSys, PeakBrush = bPeak,
                    SparkSamples = showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null,
                    SparkColor = sysCol
                });
            }

            if (ShowMode)
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

            if (ShowRadeonSettings && _status.RadeonFeaturesAvailable)
            {
                List<string> radTags = _status.GetActiveRadeonTags();
                if (radTags != null && radTags.Count > 0)
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
                    float contentW = lblW + valW + pkW + (10f * sc);

                    if (showSpark && col.SparkSamples != null)
                    {
                        col.MinWidth = Math.Max(65f * sc, contentW);
                    }
                    else
                    {
                        col.MinWidth = Math.Max(38f * sc, contentW);
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
                    Color.Empty, Color.Empty, Color.Empty, Color.Empty, Color.Empty, Color.Empty);
                float total = 0f;
                foreach (var c in cols)
                {
                    total += c.MinWidth;
                }
                return total;
            }
        }

        public void UpdateDimensions()
        {
            UpdateFontsForScale();
            float sc = GetScaleMultiplier();
            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = ShowLiveTrend;
            bool hasFpsGraph = ShowFpsGraph;
            int cols = CountVisibleMetrics();
            int scaledGraphH = (int)Math.Round(GraphHeight * sc);

            List<string> tags = (ShowRadeonSettings && _status.RadeonFeaturesAvailable) ? _status.GetActiveRadeonTags() : null;
            bool hasRadeonTags = tags != null && tags.Count > 0;

            if (VerticalLayout)
            {
                int baseW = showSpark ? 295 : (ShowPeaks ? 285 : 235);
                if (showCombined || hasFpsGraph)
                {
                    baseW = Math.Max(baseW, 285);
                }
                if (hasRadeonTags)
                {
                    int neededW = 20;
                    foreach (string t in tags)
                    {
                        neededW += (int)Math.Round((t.Length * 6.5f) + 14);
                    }
                    baseW = Math.Max(baseW, Math.Max(230, neededW));
                }
                int w = (int)Math.Round(baseW * sc);
                int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                int topH = (int)Math.Round(24 * sc);
                int h = topH + (cols > 0 ? cols * rowH + (int)Math.Round(4 * sc) : (int)Math.Round(2 * sc));
                if (showCombined)
                {
                    h += scaledGraphH + (int)Math.Round(10 * sc);
                }
                if (hasFpsGraph)
                {
                    if (DfGraphStyle)
                        h += GetDfFrametimeHeight(sc, scaledGraphH) + GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(16 * sc);
                    else
                        h += scaledGraphH + (int)Math.Round(10 * sc);
                }
                TargetWidth = Math.Max((int)Math.Round(200 * sc), w);
                TargetHeight = Math.Max((int)Math.Round(28 * sc), h);
                Size = new Size(TargetWidth, TargetHeight);
            }
            else
            {
                int topH = (cols > 0) ? (int)Math.Round((showSpark ? 50 : 36) * sc) : (int)Math.Round(24 * sc);
                int h = topH;
                if (showCombined) h += scaledGraphH + (int)Math.Round(10 * sc);
                if (hasFpsGraph)
                {
                    if (DfGraphStyle)
                        h += GetDfFrametimeHeight(sc, scaledGraphH) + GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(16 * sc);
                    else
                        h += scaledGraphH + (int)Math.Round(10 * sc);
                }

                int numGraphs = (showCombined ? 1 : 0) + (hasFpsGraph ? (DfGraphStyle ? 2 : 1) : 0);
                int minW = (int)Math.Round((numGraphs > 0 ? 420 : 180) * sc);

                float requiredColsW = MeasureHorizontalRequiredWidth(sc, showSpark);
                int w = Math.Max(minW, (int)Math.Ceiling(requiredColsW + (16f * sc)));

                Screen scr = Screen.FromControl(this);
                Rectangle wa = (scr != null) ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;
                if (w > wa.Width - 16)
                {
                    w = wa.Width - 16;
                }

                TargetWidth = w;
                TargetHeight = Math.Max((int)Math.Round(28 * sc), h);
                Size = new Size(TargetWidth, TargetHeight);

                // Ensure the horizontal overlay bar does not spill off the right edge of the monitor
                if (Location.X + TargetWidth > wa.Right - 8)
                {
                    int newX = Math.Max(wa.Left + 8, wa.Right - TargetWidth - 8);
                    if (newX != Location.X)
                    {
                        Location = new Point(newX, Location.Y);
                    }
                }
            }
        }

        public void UpdateMetrics(FanStatus st, string modeSummary, bool hasDualFans)
        {
            int prevCols = CountVisibleMetrics();
            int prevTags = (_status.GetActiveRadeonTags() != null) ? _status.GetActiveRadeonTags().Count : 0;
            bool layoutChanged = (HasDualFans != hasDualFans);
            string prevMode = _modeSummary;
            _status = st;
            _modeSummary = string.IsNullOrEmpty(modeSummary) ? st.Mode : modeSummary;
            HasDualFans = hasDualFans;

            int newCols = CountVisibleMetrics();
            int newTags = (_status.GetActiveRadeonTags() != null) ? _status.GetActiveRadeonTags().Count : 0;
            bool modeChanged = (prevMode != _modeSummary);

            if (layoutChanged || modeChanged || prevCols != newCols || prevTags != newTags || TargetHeight != Height || TargetWidth != Width)
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
            else if (!ClickThrough)
            {
                int oldX = _hoverMouseX;
                int oldY = _hoverMouseY;
                _hoverMouseX = e.X;
                _hoverMouseY = e.Y;
                if (_combRect.Contains(e.Location) || _fpsRect.Contains(e.Location) ||
                    _dfFtRect.Contains(e.Location) || _dfFpsRect.Contains(e.Location) ||
                    (_combRect.Width > 0 && _combRect.Contains(oldX, oldY)) ||
                    (_fpsRect.Width > 0 && _fpsRect.Contains(oldX, oldY)) ||
                    (_dfFtRect.Width > 0 && _dfFtRect.Contains(oldX, oldY)) ||
                    (_dfFpsRect.Width > 0 && _dfFpsRect.Contains(oldX, oldY)))
                {
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverMouseX != -1 || _hoverMouseY != -1)
            {
                _hoverMouseX = -1;
                _hoverMouseY = -1;
                Invalidate();
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

        private static void DrawEndpointBadges(Graphics g, Rectangle plot, Rectangle bounds, List<EndpointBadge> badges, Font font, float sc)
        {
            if (badges == null || badges.Count == 0) return;

            badges.Sort((a, b) => a.TargetY.CompareTo(b.TargetY));
            float minSpacing = Math.Max(9f, 9.5f * sc);
            for (int i = 0; i < badges.Count; i++)
                badges[i].DisplayY = badges[i].TargetY;

            for (int i = 1; i < badges.Count; i++)
            {
                if (badges[i].DisplayY < badges[i - 1].DisplayY + minSpacing)
                    badges[i].DisplayY = badges[i - 1].DisplayY + minSpacing;
            }

            int bh = (int)Math.Round(10 * sc);
            float maxBottom = plot.Bottom - (float)Math.Round(bh / 2f) - 1;
            if (badges[badges.Count - 1].DisplayY > maxBottom)
            {
                badges[badges.Count - 1].DisplayY = maxBottom;
                for (int i = badges.Count - 2; i >= 0; i--)
                {
                    if (badges[i].DisplayY > badges[i + 1].DisplayY - minSpacing)
                        badges[i].DisplayY = badges[i + 1].DisplayY - minSpacing;
                }
            }

            float minTop = plot.Top + (float)Math.Round(bh / 2f) + 1;
            for (int i = 0; i < badges.Count; i++)
            {
                if (badges[i].DisplayY < minTop)
                    badges[i].DisplayY = minTop;
            }

            int rightMargin = (int)Math.Round(3 * sc);
            int badgeX = plot.Right + (int)Math.Round(2 * sc);
            int maxBadgeW = Math.Max(12, bounds.Right - badgeX - rightMargin);

            using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(195, 15, 23, 42)))
            {
                for (int i = 0; i < badges.Count; i++)
                {
                    EndpointBadge b = badges[i];
                    Size sz = TextRenderer.MeasureText(g, b.Text, font, new Size(maxBadgeW, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    int bw = Math.Min(maxBadgeW, sz.Width + (int)Math.Round(3 * sc));
                    int by = (int)Math.Round(b.DisplayY - bh / 2f);
                    // Anchor badge comfortably inside bounds so it never clips or overflows on the right edge
                    int curX = Math.Min(badgeX, bounds.Right - bw - rightMargin);
                    if (curX < plot.Right + 1) curX = plot.Right + 1;
                    Rectangle badgeRect = new Rectangle(curX, by, bw, bh);

                    using (Pen tickPen = new Pen(Color.FromArgb(130, b.Color), 1f))
                    {
                        g.DrawLine(tickPen, plot.Right, b.TargetY, curX, b.DisplayY);
                    }

                    g.FillRectangle(bgBr, badgeRect);
                    using (Pen borderPen = new Pen(Color.FromArgb(150, b.Color), 1f))
                    {
                        if (b.Dashed) borderPen.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(borderPen, badgeRect);
                    }
                    TextRenderer.DrawText(g, b.Text, font, new Point(curX + (int)Math.Round(1.5f * sc), by + (int)Math.Round(1 * sc)), b.Color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
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

            bool afterburner = MatchAppTheme && IsAfterburnerTheme;
            Color chartBg = afterburner
                ? Color.FromArgb(190, 12, 14, 18)
                : ((MatchAppTheme && IsRadeonTheme)
                    ? Color.FromArgb(180, 10, 4, 6)
                    : Color.FromArgb(180, 10, 12, 18));

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
                if (ShowNvme && idx < Stats.NvmeHistory.Count && Stats.NvmeHistory[idx] > 0)
                {
                    int v = Stats.NvmeHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowPower && idx < Stats.PowerHistory.Count && Stats.PowerHistory[idx] > 0)
                {
                    int v = Stats.PowerHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowRam && idx < Stats.RamPowerHistory.Count && Stats.RamPowerHistory[idx] > 0)
                {
                    int v = (int)Math.Round(Stats.RamPowerHistory[idx] / 10.0);
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

            float rpmLo = (rMin <= rMax) ? Math.Max(0f, rMin - 150f) : 0f;
            float rpmHi = (rMin <= rMax) ? (rMax + 150f) : 3000f;
            if (rpmHi - rpmLo < 400f)
            {
                rpmLo = Math.Max(0f, rMin - 200f);
                rpmHi = rpmLo + 400f;
            }

            int topPad = (int)Math.Round(14f * sc);
            int rightPad = (int)Math.Round(38f * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - 2));

            List<EndpointBadge> badges = new List<EndpointBadge>();

            if (HasDualFans && ShowSysFan)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.Fan2RpmHistory, start, take, rpmLo, rpmHi, sysCol, false, true, sc);
                if (pt.HasValue && Stats.Fan2RpmHistory.Count > 0)
                {
                    int v = Stats.Fan2RpmHistory[Stats.Fan2RpmHistory.Count - 1];
                    if (v > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0.0}k", v / 1000.0), Color = sysCol, Dashed = true });
                }
            }
            if (ShowApuFan)
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
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.P01LowHistory, start, take, leftLo, leftHi, Color.FromArgb(0xFB, 0x71, 0x85), false, false, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.P01LowHistory[Stats.P01LowHistory.Count - 1].ToString(), Color = Color.FromArgb(0xFB, 0x71, 0x85) });
            }
            if (plotFpsHere && (ShowFps || ShowLows) && Stats.AvgFpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.AvgFpsHistory, start, take, leftLo, leftHi, Color.FromArgb(0x38, 0xBD, 0xF8), false, true, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = "Avg:" + Stats.AvgFpsHistory[Stats.AvgFpsHistory.Count - 1], Color = Color.FromArgb(0x38, 0xBD, 0xF8), Dashed = true });
            }
            if (plotFpsHere && ShowFps && Stats.FpsHistory.Count > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.FpsHistory, start, take, leftLo, leftHi, Color.FromArgb(0x38, 0xBD, 0xF8), false, false, sc);
                if (pt.HasValue) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.FpsHistory[Stats.FpsHistory.Count - 1].ToString(), Color = Color.FromArgb(0x38, 0xBD, 0xF8) });
            }
            if (ShowRam && _status.RamPowerWatts > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.RamPowerHistory, start, take, leftLo, leftHi, Color.FromArgb(0x81, 0x8C, 0xF8), false, true, sc);
                if (pt.HasValue && Stats.RamPowerHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = string.Format("{0:0}W", Stats.RamPowerHistory[Stats.RamPowerHistory.Count - 1] / 10.0), Color = Color.FromArgb(0x81, 0x8C, 0xF8), Dashed = true });
            }
            if (ShowPower)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.PowerHistory, start, take, leftLo, leftHi, pwrCol, false, false, sc);
                if (pt.HasValue && Stats.PowerHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.PowerHistory[Stats.PowerHistory.Count - 1] + "W", Color = pwrCol });
            }
            if (ShowNvme && _status.NvmeTemp > 0)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.NvmeHistory, start, take, leftLo, leftHi, CfxTeal, false, false, sc);
                if (pt.HasValue && Stats.NvmeHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.NvmeHistory[Stats.NvmeHistory.Count - 1] + "°", Color = CfxTeal });
            }
            if (ShowGpu)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.GpuHistory, start, take, leftLo, leftHi, gpuCol, false, false, sc);
                if (pt.HasValue && Stats.GpuHistory.Count > 0 && Stats.GpuHistory[Stats.GpuHistory.Count - 1] > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.GpuHistory[Stats.GpuHistory.Count - 1] + "°", Color = gpuCol });
            }
            if (ShowCpu)
            {
                PointF? pt = DrawCombinedSubSeries(g, plot, Stats.CpuHistory, start, take, leftLo, leftHi, cpuCol, true, false, sc);
                if (pt.HasValue && Stats.CpuHistory.Count > 0) badges.Add(new EndpointBadge { TargetY = pt.Value.Y, Text = Stats.CpuHistory[Stats.CpuHistory.Count - 1] + "°", Color = cpuCol });
            }

            DrawEndpointBadges(g, plot, rect, badges, _fontTrace, sc);

            // Compact Legend & Scale Header inside the top of the Combined OSD Graph
            string unitSuffix = (plotFpsHere && (ShowFps || ShowLows) && Stats.FpsHistory.Count > 0 && Stats.FpsHistory[Stats.FpsHistory.Count - 1] > 0) ? "°C/W/FPS" : "°C/W";
            string hdr = (tMin <= tMax)
                ? string.Format("LIVE TREND ({0}–{1}{2})", tMin, tMax, unitSuffix)
                : "LIVE TREND";
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int legX = rect.X + (int)Math.Round(4 * sc) + TextRenderer.MeasureText(g, hdr, _fontChart, new Size(240, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + (int)Math.Round(6 * sc);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● CPU", cpuCol, _fontTrace);
            if (ShowGpu) DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● iGPU", gpuCol, _fontTrace);
            if (ShowNvme && _status.NvmeTemp > 0) DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● NVMe", CfxTeal, _fontTrace);
            if (ShowPower) DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● PWR", pwrCol, _fontTrace);
            if (ShowApuFan) DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● APU", apuCol, _fontTrace);
            if (HasDualFans && ShowSysFan) DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● SYS", sysCol, _fontTrace);

            // Interactive Hover Scrubbing Crosshair & Readout Card
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float norm = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                norm = Math.Max(0f, Math.Min(1f, norm));
                int sampleIdx = start + (int)Math.Round(norm * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int secAgo = (int)Math.Round((take - 1 - (sampleIdx - start)) * 1.5);
                int c = sampleIdx < Stats.CpuHistory.Count ? Stats.CpuHistory[sampleIdx] : 0;
                int g_ = (sampleIdx < Stats.GpuHistory.Count) ? Stats.GpuHistory[sampleIdx] : 0;
                int n = (Stats.NvmeHistory != null && sampleIdx < Stats.NvmeHistory.Count) ? Stats.NvmeHistory[sampleIdx] : 0;
                int p = (sampleIdx < Stats.PowerHistory.Count) ? Stats.PowerHistory[sampleIdx] : 0;
                int f1 = (sampleIdx < Stats.Fan1RpmHistory.Count) ? Stats.Fan1RpmHistory[sampleIdx] : 0;
                int f2 = (sampleIdx < Stats.Fan2RpmHistory.Count) ? Stats.Fan2RpmHistory[sampleIdx] : 0;

                string hud = string.Format("{0} | CPU {1}° • iGPU {2}° • NVMe {3}° • PWR {4}W • APU {5}",
                    secAgo == 0 ? "Now" : "-" + secAgo + "s", c, g_, n, p, f1);
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
            catch { return null; }
        }

        private void DrawFpsOsdGraph(Graphics g, Rectangle rect, Color border, float sc)
        {
            if (rect.Width <= 4 || rect.Height <= 4) return;

            using (Brush bgBr = new SolidBrush(Color.FromArgb(170, 15, 23, 42)))
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
                TextRenderer.DrawText(g, "FPS & FRAMETIME (Standby)", _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
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
                using (Pen guidePen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(guidePen, plot.Left, y60, plot.Right, y60);
                }
            }

            Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);  // Cyan
            Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85);  // Coral/Rose
            Color ftCol  = Color.FromArgb(0xFB, 0xBF, 0x24);  // Amber Gold

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

            DrawEndpointBadges(g, plot, rect, badges, _fontTrace, sc);

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
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int legX = rect.X + (int)Math.Round(4 * sc) + TextRenderer.MeasureText(g, hdr, _fontChart, new Size(240, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + (int)Math.Round(6 * sc);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● FPS", fpsCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● FT", ftCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● Avg", fpsCol, _fontTrace);
            DrawLegendDot(g, ref legX, rect.Right - 4, rect.Y + 1, "● 0.1%", lowCol, _fontTrace);

            // Interactive Hover Scrubbing Crosshair & Readout Card
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
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

        private void DrawDfFrametimeGraph(Graphics g, Rectangle rect, float sc)
        {
            if (rect.Width <= 10 || rect.Height <= 10) return;

            using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(195, 10, 14, 20)))
            using (Pen borderPen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f))
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
            string ftHdr = (curFt > 0)
                ? string.Format("FRAME-TIME (MS)  •  NOW: {0:0.0} ms", curFt)
                : "FRAME-TIME (MS)";
            TextRenderer.DrawText(g, ftHdr, _fontDfHdr, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + (int)Math.Round(1.5f * sc)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int topPad = (int)Math.Round(14 * sc);
            int btmPad = (int)Math.Round(8 * sc);
            int rightPad = (int)Math.Round(32 * sc);
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
            using (Pen divPen = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
            {
                g.DrawLine(divPen, midX, plot.Top, midX, plot.Bottom);
            }
            // Vertical right border line closing the plot area
            using (Pen boundPen = new Pen(Color.FromArgb(180, 255, 255, 255), 1f))
            {
                g.DrawLine(boundPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
            }

            // Subtle dashed midpoint horizontal reference line
            using (Pen midGridPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
            {
                float midY = plot.Top + plot.Height / 2f;
                g.DrawLine(midGridPen, plot.Left, midY, plot.Right, midY);
            }

            // Right Axis Labels: 0 (top), mid (middle), ftCeiling (bottom) - Dynamic Zoom (50ms / 75ms / 100ms)
            string lbl0 = "0";
            string lblMid = (ftCeiling / 2).ToString();
            string lblMax = ftCeiling.ToString();

            Size sz0 = TextRenderer.MeasureText(g, lbl0, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Size szMid = TextRenderer.MeasureText(g, lblMid, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            Size sz100 = TextRenderer.MeasureText(g, lblMax, _fontDfAxis, new Size(rightPad, 16), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int y0 = Math.Max(rect.Y + 2, (int)Math.Round(plot.Top - sz0.Height / 2f));
            int y50 = (int)Math.Round(plot.Top + plot.Height / 2f - szMid.Height / 2f);
            int y100 = Math.Min(rect.Bottom - 2 - sz100.Height, (int)Math.Round(plot.Bottom - sz100.Height / 2f));

            TextRenderer.DrawText(g, lbl0, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), y0), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, lblMid, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), y50), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, lblMax, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), y100), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (Stats == null || Stats.FrametimeHistory == null || Stats.FrametimeHistory.Count == 0)
                return;

            int total = Stats.FrametimeHistory.Count;
            int take = Math.Min(total, 60);
            int start = total - take;

            PointF[] pts = new PointF[take];
            for (int i = 0; i < take; i++)
            {
                int idx = start + i;
                float ftMs = (idx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[idx] > 0)
                    ? Stats.FrametimeHistory[idx] / 10.0f
                    : 16.6f;
                // Inverted: 0ms is at plot.Top, ftCeiling ms is at plot.Bottom (stutter drops downward)
                float norm = Math.Max(0f, Math.Min(1f, ftMs / (float)ftCeiling));
                float x = (take == 1) ? plot.Left : (plot.Left + (plot.Width * i) / (float)(take - 1));
                float y = plot.Top + norm * plot.Height;
                pts[i] = new PointF(x, y);
            }

            // Neon Cyan / Lime Jitter Line
            Color ftCol = Color.FromArgb(0x22, 0xD3, 0xEE); // Electric Cyan
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
                using (SolidBrush bgPill = new SolidBrush(Color.FromArgb(225, 10, 14, 20)))
                {
                    g.FillRectangle(bgPill, ftBadgeRect);
                }
                using (Pen borderPill = new Pen(ftCol, 1.2f))
                {
                    g.DrawRectangle(borderPill, ftBadgeRect);
                }
                TextRenderer.DrawText(g, ftBadge, _fontDfAxis, new Point(ftBx + (int)Math.Round(3 * sc), ftBy + (int)Math.Round(1.5f * sc)), ftCol, TextFormatFlags.NoPadding);
            }

            // Hover scrubbing tooltip
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sampleIdx = start + (int)Math.Round(normX * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int secAgo = (int)Math.Round((take - 1 - (sampleIdx - start)) * 1.5);
                double ft = (sampleIdx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[sampleIdx] > 0)
                    ? Stats.FrametimeHistory[sampleIdx] / 10.0
                    : 16.6;
                string hud = string.Format("{0} | Frametime: {1:0.0} ms", secAgo == 0 ? "Now" : "-" + secAgo + "s", ft);
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

            int sessionAvg = (Stats != null && Stats.AvgFps > 0)
                ? (int)Math.Round(Stats.AvgFps)
                : (_status.RtssAvgFps > 0 ? _status.RtssAvgFps : curLiveFps);

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

            using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(195, 10, 14, 20)))
            using (Pen borderPen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f))
            {
                g.FillRectangle(bgBr, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            // Bold Header: FRAME-RATE (FPS) with live readout and average
            string fpsHdr = (curLiveFps > 0)
                ? string.Format("FRAME-RATE (FPS)  •  NOW: {0} FPS{1}", curLiveFps, sessionAvg > 0 ? string.Format("  •  AVG: {0}", sessionAvg) : "")
                : "FRAME-RATE (FPS)";
            TextRenderer.DrawText(g, fpsHdr, _fontDfHdr, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + (int)Math.Round(1.5f * sc)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            int topPad = (int)Math.Round(14 * sc);
            int btmPad = (int)Math.Round(9 * sc);
            int rightPad = (int)Math.Round(32 * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - rightPad - 4), Math.Max(6, rect.Height - topPad - btmPad));

            // Grid lines and Axis Labels
            using (Pen gridPen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
            using (Pen boundPen = new Pen(Color.FromArgb(180, 255, 255, 255), 1f))
            using (Pen tickPen = new Pen(Color.FromArgb(35, 255, 255, 255), 1f))
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
                    if (lblY < rect.Y + 2) lblY = rect.Y + 2;
                    if (lblY + sz.Height > rect.Bottom - 2) lblY = rect.Bottom - 2 - sz.Height;
                    TextRenderer.DrawText(g, lbl, _fontDfAxis, new Point(plot.Right + (int)Math.Round(4 * sc), lblY), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }

                // Vertical boundary line separating the plot from the axis labels
                g.DrawLine(boundPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
            }

            if (total == 0)
                return;

            // Session Avg line (Smooth Blue / Cyan)
            if (Stats.AvgFpsHistory != null && Stats.AvgFpsHistory.Count > 0)
            {
                PointF[] avgPts = new PointF[take];
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    float f = (idx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[idx] : 0;
                    float norm = Math.Max(0f, Math.Min(1f, f / fpsCeiling));
                    float x = (take == 1) ? plot.Left : (plot.Left + (plot.Width * i) / (float)(take - 1));
                    float y = plot.Bottom - norm * plot.Height;
                    avgPts[i] = new PointF(x, y);
                }
                using (Pen avgPen = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), 1.4f * Math.Max(1f, sc * 0.9f)))
                {
                    avgPen.DashStyle = DashStyle.Dash;
                    g.DrawLines(avgPen, avgPts);
                }
            }

            // Live FPS curve (Vibrant Neon Lime Green) with 3-point rolling moving average for Digital Foundry aesthetic
            PointF[] fpsPts = new PointF[take];
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
                float x = (take == 1) ? plot.Left : (plot.Left + (plot.Width * i) / (float)(take - 1));
                float y = plot.Bottom - norm * plot.Height;
                fpsPts[i] = new PointF(x, y);
            }
            Color neonGreen = Color.FromArgb(0x4A, 0xDE, 0x80); // Neon Lime Green
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

                // High-visibility live endpoint badge on the FPS curve
                string fpsBadge = string.Format("{0} FPS", curLiveFps);
                Size bSz = TextRenderer.MeasureText(g, fpsBadge, _fontDfAxis, new Size(80, 16), TextFormatFlags.NoPadding);
                int bw = bSz.Width + (int)Math.Round(6 * sc);
                int bh = (int)Math.Round(12 * sc);
                int bx = (int)Math.Round(last.X - bw - 6 * sc);
                if (bx < plot.Left + 2) bx = plot.Left + 2;
                int by = (int)Math.Round(last.Y - bh / 2f);
                if (by < plot.Top + 1) by = plot.Top + 1;
                if (by + bh > plot.Bottom - 1) by = plot.Bottom - 1 - bh;
                Rectangle badgeRect = new Rectangle(bx, by, bw, bh);

                using (Pen tickPen = new Pen(Color.FromArgb(180, neonGreen), 1f))
                {
                    g.DrawLine(tickPen, last.X, last.Y, bx + bw, by + bh / 2f);
                }
                using (SolidBrush bgPill = new SolidBrush(Color.FromArgb(225, 10, 14, 20)))
                {
                    g.FillRectangle(bgPill, badgeRect);
                }
                using (Pen borderPill = new Pen(neonGreen, 1.2f))
                {
                    g.DrawRectangle(borderPill, badgeRect);
                }
                TextRenderer.DrawText(g, fpsBadge, _fontDfAxis, new Point(bx + (int)Math.Round(3 * sc), by + (int)Math.Round(1.5f * sc)), neonGreen, TextFormatFlags.NoPadding);

                // Show companion Session Avg badge if separated from live badge
                if (Stats.AvgFpsHistory != null && Stats.AvgFpsHistory.Count > 0 && sessionAvg > 0)
                {
                    float normAvg = Math.Max(0f, Math.Min(1f, (float)sessionAvg / fpsCeiling));
                    float avgY = plot.Bottom - normAvg * plot.Height;
                    if (Math.Abs(avgY - last.Y) > bh + 6)
                    {
                        string avgBadge = string.Format("{0} Avg", sessionAvg);
                        Size aSz = TextRenderer.MeasureText(g, avgBadge, _fontDfAxis, new Size(80, 16), TextFormatFlags.NoPadding);
                        int abw = aSz.Width + (int)Math.Round(6 * sc);
                        int abx = (int)Math.Round(last.X - abw - 6 * sc);
                        if (abx < plot.Left + 2) abx = plot.Left + 2;
                        int aby = (int)Math.Round(avgY - bh / 2f);
                        if (aby < plot.Top + 1) aby = plot.Top + 1;
                        if (aby + bh > plot.Bottom - 1) aby = plot.Bottom - 1 - bh;
                        Rectangle aRect = new Rectangle(abx, aby, abw, bh);

                        Color avgCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                        using (SolidBrush bgPill = new SolidBrush(Color.FromArgb(225, 10, 14, 20)))
                        {
                            g.FillRectangle(bgPill, aRect);
                        }
                        using (Pen borderPill = new Pen(avgCol, 1f) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawRectangle(borderPill, aRect);
                        }
                        TextRenderer.DrawText(g, avgBadge, _fontDfAxis, new Point(abx + (int)Math.Round(3 * sc), aby + (int)Math.Round(1.5f * sc)), avgCol, TextFormatFlags.NoPadding);
                    }
                }
            }

            // Hover scrubbing tooltip
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sampleIdx = start + (int)Math.Round(normX * (take - 1));
                sampleIdx = Math.Max(0, Math.Min(total - 1, sampleIdx));

                int secAgo = (int)Math.Round((take - 1 - (sampleIdx - start)) * 1.5);
                int f = (sampleIdx < Stats.FpsHistory.Count) ? Stats.FpsHistory[sampleIdx] : 0;
                int avg = (sampleIdx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[sampleIdx] : 0;
                int low = (sampleIdx < Stats.P01LowHistory.Count) ? Stats.P01LowHistory[sampleIdx] : 0;

                string hud = string.Format("{0} | FPS: {1} • Avg: {2} • 0.1%: {3}",
                    secAgo == 0 ? "Now" : "-" + secAgo + "s", f, avg, low > 0 ? low.ToString() : "--");
                DrawHoverHudBox(g, rect, plot, hud, _fontTrace, _hoverMouseX, _hoverMouseY, sc);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                float sc = GetScaleMultiplier();
                int scaledGraphH = (int)Math.Round(GraphHeight * sc);

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

            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = ShowLiveTrend;

            string cpuVal;
            if (ShowClocks && _status.CpuClockMhz > 0)
            {
                cpuVal = _status.CpuLoadPct >= 0
                    ? string.Format("{0}°C {1}% @ {2:0.0}G", _status.CpuTemp, _status.CpuLoadPct, _status.CpuClockMhz / 1000.0)
                    : string.Format("{0}°C @ {1:0.0}G", _status.CpuTemp, _status.CpuClockMhz / 1000.0);
            }
            else
            {
                cpuVal = _status.CpuLoadPct >= 0
                    ? string.Format("{0}°C {1}%", _status.CpuTemp, _status.CpuLoadPct)
                    : string.Format("{0}°C", _status.CpuTemp);
            }
            bool vertShowGpuTemp = ShowGpu;
            bool vertShowGpuLoad = ShowGpuLoad;

            bool vertHasGpuTemp = vertShowGpuTemp && _status.GpuTemp > 0;
            bool vertHasGpuLoad = vertShowGpuLoad && (_status.GfxActivityPct >= 0);
            bool vertHasGpuClock = vertShowGpuLoad && (_status.GfxClockMhz > 0);

            string vertGpuTempPart = vertHasGpuTemp
                ? (_status.GpuHotspotTemp > 0 ? string.Format("{0}°C (🔥{1}°)", _status.GpuTemp, _status.GpuHotspotTemp) : string.Format("{0}°C", _status.GpuTemp))
                : null;

            string vertGpuLoadPart = null;
            if (vertHasGpuLoad && ShowClocks && vertHasGpuClock)
                vertGpuLoadPart = string.Format("{0}% @ {1}MHz", _status.GfxActivityPct, _status.GfxClockMhz);
            else if (vertHasGpuLoad)
                vertGpuLoadPart = string.Format("{0}%", _status.GfxActivityPct);
            else if (ShowClocks && vertHasGpuClock)
                vertGpuLoadPart = string.Format("@ {0}MHz", _status.GfxClockMhz);

            string gpuVal;
            if (vertGpuTempPart != null && vertGpuLoadPart != null)
                gpuVal = string.Format("{0} {1}", vertGpuTempPart, vertGpuLoadPart);
            else if (vertGpuTempPart != null)
                gpuVal = vertGpuTempPart;
            else if (vertGpuLoadPart != null)
                gpuVal = vertGpuLoadPart;
            else
                gpuVal = "N/A";

            string pwrVal = _status.ApuPowerWatts > 0
                ? (_status.ApuPowerLimitWatts > 0 ? string.Format("{0}W/{1}W", _status.ApuPowerWatts, _status.ApuPowerLimitWatts) : string.Format("{0}W", _status.ApuPowerWatts))
                : "--W";
            string f1Val  = string.Format("{0}", _status.Fan1Rpm);
            string f2Val  = _status.Fan2Rpm > 0 ? string.Format("{0}", _status.Fan2Rpm) : "--";

            string cpuTempPeak = (ShowPeaks && Stats != null && Stats.MaxCpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxCpuTemp) : null;
            string cpuLoadPeak = (ShowPeaks && Stats != null && Stats.MaxCpuLoad > 0) ? string.Format("▲{0}%", Stats.MaxCpuLoad) : null;
            string cpuPeak = null;
            if (cpuTempPeak != null && cpuLoadPeak != null)
                cpuPeak = string.Format("{0} {1}", cpuTempPeak, cpuLoadPeak);
            else if (cpuTempPeak != null)
                cpuPeak = cpuTempPeak;
            else if (cpuLoadPeak != null)
                cpuPeak = cpuLoadPeak;
            string vertGpuTempPeak = (ShowPeaks && vertShowGpuTemp && Stats != null && Stats.MaxGpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxGpuTemp) : null;
            string vertGpuLoadPeak = (ShowPeaks && vertShowGpuLoad && Stats != null && Stats.MaxGpuLoad > 0) ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null;
            string gpuPeak = null;
            if (vertGpuTempPeak != null && vertGpuLoadPeak != null)
                gpuPeak = string.Format("{0} {1}", vertGpuTempPeak, vertGpuLoadPeak);
            else if (vertGpuTempPeak != null)
                gpuPeak = vertGpuTempPeak;
            else if (vertGpuLoadPeak != null)
                gpuPeak = vertGpuLoadPeak;
            string nvmePeak = (ShowPeaks && Stats != null && Stats.MaxNvmeTemp > 0) ? string.Format("▲{0}°", Stats.MaxNvmeTemp) : null;
            string pwrPeak = (ShowPeaks && Stats != null && Stats.MaxPowerWatts > 0) ? string.Format("▲{0}W", Stats.MaxPowerWatts) : null;
            string ramPeak = (ShowPeaks && Stats != null && Stats.MaxRamPowerWatts > 0) ? string.Format("▲{0:0.0}W", Stats.MaxRamPowerWatts) : null;

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
                    Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);
                    Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85);
                    Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);

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

                    int curY = (CountVisibleMetrics() > 0) ? (int)Math.Round((showSpark ? 50 : 36) * sc) : (int)Math.Round(24 * sc);

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
                            if (DfGraphStyle)
                            {
                                int dfFtH = GetDfFrametimeHeight(sc, scaledGraphH);
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH);
                                Rectangle ftRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(14 * sc), dfFtH);
                                _dfFtRect = ftRect;
                                DrawDfFrametimeGraph(g, ftRect, sc);
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
                    g.DrawString(afterburner ? "AFTERBURNER OSD" : (radeon ? "RADEON TELEMETRY OSD" : "MINIFANCONTROL OSD"), _fontHdr, bLabel, 10f * sc, 6f * sc);
                    g.DrawLine(sepPen, (int)Math.Round(10 * sc), (int)Math.Round(24 * sc), Width - (int)Math.Round(10 * sc), (int)Math.Round(24 * sc));

                    int row = 0;
                    int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                    if (ShowCpu)
                        DrawVertRow(g, row++, rowH, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu || ShowGpuLoad)
                        DrawVertRow(g, row++, rowH, "iGPU", gpuVal, gpuPeak, bLabel, bGpu, bPeak, showSpark ? (Stats != null ? (vertShowGpuTemp ? Stats.GpuHistory : Stats.GpuLoadHistory) : null) : null, gpuCol, sc);
                    if (ShowNvme && _status.NvmeTemp > 0)
                        DrawVertRow(g, row++, rowH, "NVMe", _status.NvmeTemp + "°C", nvmePeak, bLabel, bNvme, bPeak, showSpark ? (Stats != null ? Stats.NvmeHistory : null) : null, CfxTeal, sc);
                    if (ShowPower)
                        DrawVertRow(g, row++, rowH, "Power", pwrVal, pwrPeak, bLabel, bPwr, bPeak, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowRam && _status.RamTotalGb > 0)
                    {
                        string ramVal = (ShowClocks && _status.MemClockMhz > 0)
                            ? string.Format("{0}% @ {1}M", _status.RamLoadPct, _status.MemClockMhz)
                            : (_status.RamPowerWatts > 0 ? string.Format("{0}% ~{1:0.0}W", _status.RamLoadPct, _status.RamPowerWatts) : string.Format("{0}%", _status.RamLoadPct));
                        DrawVertRow(g, row++, rowH, "RAM", ramVal, ramPeak, bLabel, bRam, bPeak, showSpark ? (Stats != null ? Stats.RamPowerHistory : null) : null, Color.FromArgb(0x81, 0x8C, 0xF8), sc);
                    }
                    if (ShowFps)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = "FPS";
                        double ft = _status.RtssFrametimeMs > 0 ? _status.RtssFrametimeMs : (hasFps ? (1000.0 / _status.RtssFps) : 0.0);
                        string val;
                        if (hasFps)
                        {
                            val = ft > 0 ? string.Format("{0} ({1:0.0}ms)", _status.RtssFps, ft) : string.Format("{0} FPS", _status.RtssFps);
                        }
                        else
                        {
                            val = "-- (Standby)";
                        }

                        int sessionAvg = (Stats != null && Stats.AvgFps > 0)
                            ? (int)Math.Round(Stats.AvgFps)
                            : (_status.RtssAvgFps > 0 ? _status.RtssAvgFps : _status.RtssFps);
                        if (Stats != null && Stats.MaxFps > 0 && sessionAvg > Stats.MaxFps)
                            sessionAvg = Stats.MaxFps;
                        if (Stats != null && Stats.MinFps > 0 && Stats.MinFps < int.MaxValue && sessionAvg < Stats.MinFps)
                            sessionAvg = Stats.MinFps;

                        string peak = hasFps
                            ? (sessionAvg > 0 ? string.Format("Avg: {0}", sessionAvg) : (ShowPeaks && Stats != null && Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null))
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
                    if (ShowApuFan)
                        DrawVertRow(g, row++, rowH, "APU Fan", f1Val + " RPM", null, bLabel, bApu, bPeak, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (HasDualFans && ShowSysFan)
                        DrawVertRow(g, row++, rowH, "Sys Fan", f2Val + " RPM", null, bLabel, bSys, bPeak, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShowMode)
                        DrawVertRow(g, row++, rowH, "Mode", _modeSummary, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);
                    if (ShowDriverVersion && !string.IsNullOrEmpty(_status.RadeonSoftwareVer))
                        DrawVertRow(g, row++, rowH, "Driver", _status.RadeonSoftwareVer, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);
                    if (ShowRadeonSettings && _status.RadeonFeaturesAvailable)
                    {
                        List<string> radTags = _status.GetActiveRadeonTags();
                        if (radTags != null && radTags.Count > 0)
                        {
                            DrawRadeonTagsRow(g, row++, rowH, radTags, sc);
                        }
                    }

                    int curY = (int)Math.Round(24 * sc) + (row > 0 ? row * rowH + (int)Math.Round(4 * sc) : (int)Math.Round(4 * sc));

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
                            if (DfGraphStyle)
                            {
                                int dfFtH = GetDfFrametimeHeight(sc, scaledGraphH);
                                int dfFpsH = GetDfFramerateHeight(sc, scaledGraphH);
                                Rectangle ftRect = new Rectangle((int)Math.Round(8 * sc), curY, Width - (int)Math.Round(16 * sc), dfFtH);
                                _dfFtRect = ftRect;
                                DrawDfFrametimeGraph(g, ftRect, sc);
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
                g.DrawLine(sepPen, cellX - 3f * sc, 5f * sc, cellX - 3f * sc, (GraphMode == 1 || GraphMode == 3) ? (45f * sc) : (30f * sc));
            }

            g.DrawString(lbl, _fontLabel, bLbl, cellX, 6f * sc);
            float lblW = g.MeasureString(lbl, _fontLabel).Width;

            float pkW = 0f;
            if (!string.IsNullOrEmpty(peak))
            {
                pkW = g.MeasureString(peak, _fontPeak).Width + (3f * sc);
            }

            float availValW = Math.Max(10f, colW - lblW - pkW - (4f * sc));
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
                    RectangleF valRect = new RectangleF(cellX + lblW - 1f, 5f * sc, availValW, 20f * sc);
                    using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    {
                        g.DrawString(drawVal, drawFont, bVal, valRect, sf);
                    }
                }
                else
                {
                    g.DrawString(drawVal, drawFont, bVal, cellX + lblW - 1f, 5f * sc);
                }
            }
            finally
            {
                if (disposeFont && drawFont != null)
                {
                    drawFont.Dispose();
                }
            }

            if (!string.IsNullOrEmpty(peak))
            {
                float actualValW = Math.Min(valW, availValW);
                g.DrawString(peak, _fontPeak, bPeak, cellX + lblW + actualValW + (3f * sc), 7f * sc);
            }

            if (sparkSamples != null)
            {
                int sparkY = (int)Math.Round(26f * sc);
                int sparkH = (int)Math.Round(20f * sc);
                int sparkW = Math.Max((int)Math.Round(16 * sc), (int)colW - (int)Math.Round(8 * sc));
                Rectangle sparkRect = new Rectangle((int)cellX + 1, sparkY, sparkW, sparkH);
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }
        }

        private void DrawVertRow(Graphics g, int rowIndex, int rowH, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float y = (27f * sc) + rowIndex * rowH;

            float rightMargin = 14f * sc;
            float lblW = g.MeasureString(lbl, _fontLabel).Width;
            float minValX = (10f * sc) + lblW + (6f * sc);

            float pkW = 0f;
            float peakX = 0f;
            if (!string.IsNullOrEmpty(peak))
            {
                float rawPkW = g.MeasureString(peak, _fontPeak).Width;
                pkW = rawPkW + 6f * sc;
                peakX = Width - rightMargin - rawPkW;
            }

            float valW = g.MeasureString(val, _fontVal).Width;
            float targetRight = Width - rightMargin - pkW;
            float valX = targetRight - valW;

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
                else
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
            }

            g.DrawString(lbl, _fontLabel, bLbl, 10f * sc, y);
            if (!string.IsNullOrEmpty(peak))
            {
                g.DrawString(peak, _fontPeak, bPeak, peakX, y + 1f);
            }

            float availW = targetRight - valX;
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
                    SizeF sz = g.MeasureString(tag, _fontTrace);
                    int pillW = (int)Math.Ceiling(sz.Width) + padH * 2;
                    Rectangle pillRect = new Rectangle(curX, y + (rowH - pillH) / 2, pillW, pillH);

                    DrawRoundedRectangle(g, borderPen, bgBrush, pillRect, (int)Math.Round(3 * sc));
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
                g.DrawLine(sepPen, cellX - 3f * sc, 5f * sc, cellX - 3f * sc, (GraphMode == 1 || GraphMode == 3) ? (45f * sc) : (30f * sc));
            }

            int padH = (int)Math.Round(3 * sc);
            int pillH = (int)Math.Round(13 * sc);
            float curX = cellX;
            float curY = 18f * sc;

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
                        DrawRoundedRectangle(g, borderPen, bgBrush, pillRect, (int)Math.Round(2 * sc));
                        g.DrawString(tag, _fontTrace, textBrush, pillRect.X + padH, pillRect.Y);
                        curX += pillW + (3f * sc);
                    }
                }
            }
        }
    }

}
