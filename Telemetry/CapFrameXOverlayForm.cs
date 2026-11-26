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
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public partial class CapFrameXOverlayForm : Form
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
        private static readonly Color OsdBgXmas            = Color.FromArgb(250, 250, 250);
        private static readonly Color OsdBorderXmas        = Color.FromArgb(0xDC, 0x26, 0x26);
        private static readonly Color XmasRed              = Color.FromArgb(0xDC, 0x26, 0x26);
        private static readonly Color XmasRedDeep          = Color.FromArgb(0x99, 0x1B, 0x1B);
        private static readonly Color XmasBlack            = Color.FromArgb(0x09, 0x09, 0x0B);
        private static readonly Color XmasCharcoal         = Color.FromArgb(0x3F, 0x3F, 0x46);
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
        public bool VerticalLayout = true;
        public bool ClickThrough = false;
        public bool HasDualFans = true;
        public bool IsReadOnlyMode = false;

        // 0 = Text Only, 1 = Mini Sparklines per Metric, 2 = Combined Live Graph, 3 = Both Sparklines + Combined Graph
        public int GraphMode = 3;
        public int GraphHeight = 140; // 75=Compact, 105=Medium, 140=Large, 180=XL
        // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        public int OsdScaleTier = 1;
        public int OpacityPct = 80;
        public bool MatchAppTheme = true;
        public bool IsRadeonTheme = false;
        public bool IsAfterburnerTheme = false;
        public bool IsXmasTheme = false;

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
        // 0 = Peaks (▲), 1 = Lows (▼), 2 = Min/Max (▼▲), 3 = Avg (~)
        public int PeakTrackerMode = 0;
        public bool ShowNvme = true;
        public bool ShowRam = true;
        public bool ShowVrmCurrent = false;
        public bool ShowFabricClock = true;
        public bool ShowNpu = true;
        public bool ShowAmbient = true;
        public bool ShowDriverVersion = true;
        public bool ShowClocks = true;
        public bool ShowGameName = true;
        public bool SparklinesAsBackground = true;
        private string _lastGameTitle = null;
        public bool ShowRadeonSettings = true;
        public bool ShowLiveTrend = true;
        public bool TrendCpu = true;
        public bool TrendGpu = true;
        public bool TrendRam = true;
        public bool TrendNvme = true;
        public bool TrendPower = true;
        public bool TrendAmbient = true;
        public bool TrendApuFan = true;
        public bool TrendSysFan = true;
        public bool PlotFpsOnTrend = true;
        public bool ShowFpsGraph = true;
        public bool DfGraphStyle = true;
        public bool DfCornerInset = false;
        public int SampleIntervalMs = 1000;

        private static readonly HashSet<string> _loggedPaintErrors = new HashSet<string>();

        /// <summary>Logs a paint/draw failure once per distinct location so a broken overlay is diagnosable without flooding the log.</summary>
        private static void LogPaintErrorOnce(string where, Exception ex)
        {
            try
            {
                bool first;
                lock (_loggedPaintErrors) { first = _loggedPaintErrors.Add(where); }
                if (first)
                    WatchdogSupervisor.LogWatchdogEvent("Overlay paint failure in " + where + ": " + ex);
            }
            catch { }
        }

        private bool IsEffectivelyReadOnly()
        {
            return IsReadOnlyMode || string.Equals(_modeSummary, "Read-Only", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldShowApuFanRow()
        {
            if (!ShowApuFan) return false;
            if (IsEffectivelyReadOnly() && _status.Fan1Rpm <= 0) return false;
            return true;
        }

        private bool ShouldShowSysFanRow()
        {
            if (!HasDualFans || !ShowSysFan) return false;
            if (IsEffectivelyReadOnly() && _status.Fan2Rpm <= 0) return false;
            return true;
        }

        private bool ShouldShowModeRow()
        {
            if (!ShowMode) return false;
            if (IsEffectivelyReadOnly()) return false;
            return true;
        }

        private int ComputeDisplayAvgFps(int curLiveFps)
        {
            if (_status.RtssAvgFps > 0) return _status.RtssAvgFps;
            int sessionAvg = (Stats != null && Stats.AvgFps > 0)
                ? (int)Math.Round(Stats.AvgFps)
                : curLiveFps;
            if (Stats != null && Stats.MaxFps > 0 && sessionAvg > Stats.MaxFps)
                sessionAvg = Stats.MaxFps;
            if (Stats != null && Stats.MinFps > 0 && Stats.MinFps < int.MaxValue && sessionAvg < Stats.MinFps)
                sessionAvg = Stats.MinFps;
            return sessionAvg;
        }

        private string FormatSampleTimeAgo(int samplesAgo)
        {
            if (samplesAgo <= 0) return "Now";
            double secPerSample = Math.Max(0.1, SampleIntervalMs / 1000.0);
            double secs = samplesAgo * secPerSample;
            if (secPerSample < 0.95)
                return string.Format("-{0:0.0}s", secs);
            return string.Format("-{0:0}s", secs);
        }
        public bool DfGraphFirst = true;
        public bool ClampToScreen = true;
        public string ProcessorBadgeText = null;

        // Granular Sub-Field Toggles for Overlay Rows
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
        public bool ShowLatency = true;
        public bool ShowRadeonExtendedTags = true;
        public bool UseFahrenheit = false;

        // Diagnostics for Hook-Free Zero-Impact Invalidation (CapFrameX dirty-check model)
        public long SkippedRepaintCount { get; private set; }
        public long TotalRepaintCount { get; private set; }
        private long _lastRenderSignature = 0;

        public event EventHandler PositionOrLayoutChanged;
        public event EventHandler RequestHideOverlay;
        public event EventHandler RequestToggleLock;
        public event EventHandler RequestOpenOsdConfig;
        public event EventHandler<int> RequestSnapLocation;
        public event EventHandler<int> RequestSamplingInterval;

        private FanStatus _status;
        private string _modeSummary = "Balanced";
        private bool _dragging;
        private Point _dragStartMouse;
        private Point _dragStartForm;
        private int _effectiveGraphHeight = 0;
        private int _effectiveRowHeight = 0;
        private float _effectiveScale = 0f;

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
            UpdateFontsForScale(0f);
        }

        public void UpdateFontsForScale(float scOverride)
        {
            float sc = scOverride > 0 ? scOverride : GetScaleMultiplier();
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
                return Math.Min(200, (int)(Math.Ceiling(activeMaxFt / 50.0) * 50));
            if (activeMaxFt > 65.0)
                return 100;
            if (activeMaxFt > 38.0)
                return 75;
            return 50; // Zoomed in default for clean pacing (16.6ms / 19.3ms gameplay)
        }

        private static readonly Dictionary<string, string> KnownGameNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "metro", "METRO" },
            { "metroll", "METRO LAST LIGHT" },
            { "metro2033", "METRO 2033" },
            { "metroexodus", "METRO EXODUS" },
            { "cyberpunk2077", "CYBERPUNK 2077" },
            { "witcher3", "THE WITCHER 3" },
            { "witcher2", "THE WITCHER 2" },
            { "witcher", "THE WITCHER" },
            { "starfield", "STARFIELD" },
            { "rdr2", "RED DEAD REDEMPTION 2" },
            { "gta5", "GTA V" },
            { "gtav", "GTA V" },
            { "cs2", "COUNTER-STRIKE 2" },
            { "csgo", "CS:GO" },
            { "dota2", "DOTA 2" },
            { "eldenring", "ELDEN RING" },
            { "baldursgate3", "BALDUR'S GATE 3" },
            { "bg3", "BALDUR'S GATE 3" },
            { "blackmythwukong", "BLACK MYTH: WUKONG" },
            { "b1", "BLACK MYTH: WUKONG" },
            { "hogwartslegacy", "HOGWARTS LEGACY" },
            { "forzahorizon5", "FORZA HORIZON 5" },
            { "forza_horizon_5", "FORZA HORIZON 5" },
            { "forza5", "FORZA HORIZON 5" },
            { "forzahorizon4", "FORZA HORIZON 4" },
            { "godofwar", "GOD OF WAR" },
            { "gow", "GOD OF WAR" },
            { "spiderman", "SPIDER-MAN" },
            { "milesmorales", "MILES MORALES" },
            { "horizonzerodawn", "HORIZON ZERO DAWN" },
            { "horizonforbiddenwest", "HORIZON FORBIDDEN WEST" },
            { "r5apex", "APEX LEGENDS" },
            { "overwatch", "OVERWATCH" },
            { "overwatch2", "OVERWATCH 2" },
            { "valorant", "VALORANT" },
            { "fortniteclient", "FORTNITE" },
            { "fortnite", "FORTNITE" },
            { "doom", "DOOM" },
            { "doometernal", "DOOM ETERNAL" },
            { "haloinfinite", "HALO INFINITE" },
            { "helldivers2", "HELLDIVERS 2" },
            { "palworld", "PALWORLD" },
            { "fallout4", "FALLOUT 4" },
            { "skyrim", "SKYRIM" },
            { "skyrimse", "SKYRIM SE" }
        };

        public static string FormatGameName(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return null;
            string clean = Path.GetFileNameWithoutExtension(procName).Trim();
            if (string.IsNullOrEmpty(clean) || FpsMonitor.IsIgnoredProcess(clean)) return null;

            string lower = clean.ToLowerInvariant();
            if (lower.Contains("launcher") || lower.Contains("antigravity") || lower.Contains("artcraft") ||
                lower.Contains("installer") || lower.Contains("updater") || lower.Contains("setup") ||
                lower.Contains("helper") || lower.Contains("service") || lower.Contains("crash"))
                return null;

            string[] suffixes = new[] {
                "-Win64-Shipping", "_Shipping", "-Win64", "_Win64", "_x64", "-x64",
                "_DX11", "_DX12", "_VK", "_vk", "_Release", "_Final"
            };
            foreach (string sfx in suffixes)
            {
                if (clean.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring(0, clean.Length - sfx.Length);
                    break;
                }
            }

            if (clean.EndsWith("Client", StringComparison.OrdinalIgnoreCase) && clean.Length > 6)
            {
                clean = clean.Substring(0, clean.Length - 6);
            }

            string known;
            if (KnownGameNames.TryGetValue(clean, out known))
            {
                return known;
            }

            string norm = clean.Replace("_", "").Replace("-", "").Replace(" ", "");
            if (KnownGameNames.TryGetValue(norm, out known))
            {
                return known;
            }

            string spaced = Regex.Replace(clean, @"([a-z])([A-Z])", "$1 $2");
            spaced = Regex.Replace(spaced, @"([A-Za-z])([0-9])", "$1 $2");
            spaced = Regex.Replace(spaced, @"([0-9])([A-Za-z])", "$1 $2");
            spaced = spaced.Replace('_', ' ').Replace('-', ' ');
            spaced = Regex.Replace(spaced, @"\s+", " ").Trim();

            if (spaced.Length > 22)
            {
                spaced = spaced.Substring(0, 20).TrimEnd() + "..";
            }

            return spaced.ToUpperInvariant();
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


        public void UpdateDimensions()
        {
            float baseSc = GetScaleMultiplier();
            bool graphsOnly = (GraphMode == 4);
            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = ShowLiveTrend;
            bool hasFpsGraph = ShowFpsGraph;
            int cols = graphsOnly ? 0 : CountVisibleMetrics();

            List<string> tags = (ShowRadeonSettings && _status.RadeonFeaturesAvailable && _status.GetActiveRadeonTags(ShowRadeonExtendedTags) != null)
                ? new List<string>(_status.GetActiveRadeonTags(ShowRadeonExtendedTags))
                : new List<string>();
            if (ShowRadeonSettings && !string.IsNullOrEmpty(ProcessorBadgeText) && !tags.Contains(ProcessorBadgeText))
                tags.Insert(0, ProcessorBadgeText);
            bool hasRadeonTags = tags.Count > 0;

            Screen scr = Screen.FromControl(this);
            Rectangle wa = (scr != null) ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;

            if (VerticalLayout)
            {
                int baseW = showSpark ? 350 : (ShowPeaks ? 340 : 280);
                if (showCombined || hasFpsGraph)
                {
                    baseW = Math.Max(baseW, 340);
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
                if (ShowGameName)
                {
                    string gameTitle = FormatGameName(_status.RtssProcessName);
                    if (!string.IsNullOrEmpty(gameTitle))
                    {
                        int neededGameW = (int)Math.Round((("GAME: " + gameTitle).Length * 7.5f) + 32);
                        baseW = Math.Max(baseW, neededGameW);
                    }
                }

                // 1. Initial calculation at unconstrained user scale tier
                float sc = baseSc;
                int baseGraphH = (int)Math.Round(GraphHeight * sc);
                int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                int topH = graphsOnly ? (int)Math.Round(6 * sc) : (int)Math.Round(24 * sc);
                int rowsH = graphsOnly ? 0 : (cols > 0 ? cols * rowH + (int)Math.Round(4 * sc) : (int)Math.Round(2 * sc));

                int dfFtH = (DfGraphStyle && !DfCornerInset) ? GetDfFrametimeHeight(sc, baseGraphH) : baseGraphH;
                int dfFpsH = DfGraphStyle ? (DfCornerInset ? GetDfFramerateHeight(sc, baseGraphH) + (int)Math.Round(22 * sc) : GetDfFramerateHeight(sc, baseGraphH)) : 0;

                int graphH = 0;
                if (showCombined) graphH += baseGraphH + (int)Math.Round(10 * sc);
                if (hasFpsGraph)
                {
                    if (DfGraphStyle && DfCornerInset) graphH += dfFpsH + (int)Math.Round(10 * sc);
                    else if (DfGraphStyle) graphH += dfFtH + dfFpsH + (int)Math.Round(16 * sc);
                    else graphH += baseGraphH + (int)Math.Round(10 * sc);
                }

                int totalH = topH + rowsH + graphH;
                int maxAllowedH = (ClampToScreen && wa.Height > 0) ? (wa.Height - 20) : totalH;

                // 2. Intelligent Auto-Fit Scaling for 1440p / 1080p and high-DPI displays:
                // When overlay is fully populated and exceeds monitor working area, proportionally compress to fit above taskbar
                if (ClampToScreen && totalH > maxAllowedH && maxAllowedH > 200)
                {
                    // Stage A: First compress graph heights if graphs are active
                    int excess = totalH - maxAllowedH;
                    if (graphH > 0)
                    {
                        int minGraphH = (int)Math.Max(35, Math.Round(40 * sc));
                        float compressRatio = Math.Max(0.40f, (float)(graphH - excess) / Math.Max(1, graphH));
                        baseGraphH = Math.Max(minGraphH, (int)Math.Round(baseGraphH * compressRatio));
                        dfFtH = (DfGraphStyle && !DfCornerInset) ? GetDfFrametimeHeight(sc, baseGraphH) : baseGraphH;
                        dfFpsH = DfGraphStyle ? (DfCornerInset ? GetDfFramerateHeight(sc, baseGraphH) + (int)Math.Round(22 * sc) : GetDfFramerateHeight(sc, baseGraphH)) : 0;

                        graphH = 0;
                        if (showCombined) graphH += baseGraphH + (int)Math.Round(10 * sc);
                        if (hasFpsGraph)
                        {
                            if (DfGraphStyle && DfCornerInset) graphH += dfFpsH + (int)Math.Round(10 * sc);
                            else if (DfGraphStyle) graphH += dfFtH + dfFpsH + (int)Math.Round(16 * sc);
                            else graphH += baseGraphH + (int)Math.Round(10 * sc);
                        }
                        totalH = topH + rowsH + graphH;
                    }

                    // Stage B: If still exceeding (e.g. 185% 4K tier on a 1440p/1080p display), apply proportional scale
                    if (totalH > maxAllowedH)
                    {
                        float fitFactor = Math.Max(0.55f, (float)maxAllowedH / Math.Max(1, totalH));
                        sc = Math.Max(0.65f, sc * fitFactor);
                        baseGraphH = (int)Math.Max(32, Math.Round(baseGraphH * fitFactor));
                        rowH = (int)Math.Max(16, Math.Round(rowH * fitFactor));
                        topH = (int)Math.Round(24 * sc);
                        rowsH = (cols > 0 ? cols * rowH + (int)Math.Round(4 * sc) : (int)Math.Round(2 * sc));
                        dfFtH = (DfGraphStyle && !DfCornerInset) ? GetDfFrametimeHeight(sc, baseGraphH) : baseGraphH;
                        dfFpsH = DfGraphStyle ? (DfCornerInset ? GetDfFramerateHeight(sc, baseGraphH) + (int)Math.Round(22 * sc) : GetDfFramerateHeight(sc, baseGraphH)) : 0;

                        graphH = 0;
                        if (showCombined) graphH += baseGraphH + (int)Math.Round(10 * sc);
                        if (hasFpsGraph)
                        {
                            if (DfGraphStyle && DfCornerInset) graphH += dfFpsH + (int)Math.Round(10 * sc);
                            else if (DfGraphStyle) graphH += dfFtH + dfFpsH + (int)Math.Round(16 * sc);
                            else graphH += baseGraphH + (int)Math.Round(10 * sc);
                        }
                        totalH = topH + rowsH + graphH;
                    }
                }

                _effectiveScale = sc;
                _effectiveGraphHeight = baseGraphH;
                _effectiveRowHeight = rowH;
                UpdateFontsForScale(_effectiveScale);

                int w = (int)Math.Round(baseW * sc);
                TargetWidth = Math.Max((int)Math.Round(200 * sc), w);
                TargetHeight = Math.Max((int)Math.Round(28 * sc), totalH);
                Size = new Size(TargetWidth, TargetHeight);

                // 3. Ensure the vertical overlay strictly respects the taskbar (wa.Bottom) and screen edges
                if (ClampToScreen && wa.Bottom > 0 && Location.Y + TargetHeight > wa.Bottom - 8)
                {
                    int newY = Math.Max(wa.Top + 8, wa.Bottom - TargetHeight - 8);
                    if (newY != Location.Y)
                    {
                        Location = new Point(Location.X, newY);
                    }
                }
                if (ClampToScreen && wa.Top > 0 && Location.Y < wa.Top + 8)
                {
                    Location = new Point(Location.X, wa.Top + 8);
                }
                if (ClampToScreen && wa.Right > 0 && Location.X + TargetWidth > wa.Right - 8)
                {
                    int newX = Math.Max(wa.Left + 8, wa.Right - TargetWidth - 8);
                    if (newX != Location.X)
                    {
                        Location = new Point(newX, Location.Y);
                    }
                }
                if (ClampToScreen && wa.Left > 0 && Location.X < wa.Left + 8)
                {
                    Location = new Point(wa.Left + 8, Location.Y);
                }
            }
            else
            {
                // Horizontal layout
                _effectiveScale = baseSc;
                _effectiveGraphHeight = (int)Math.Round(GraphHeight * baseSc);
                _effectiveRowHeight = (int)Math.Round((showSpark ? 25 : 21) * baseSc);
                UpdateFontsForScale(_effectiveScale);

                float sc = baseSc;
                int scaledGraphH = _effectiveGraphHeight;
                int topH = graphsOnly ? (int)Math.Round(6 * sc) : ((cols > 0) ? (int)Math.Round((showSpark ? 58 : 42) * sc) : (int)Math.Round(24 * sc));
                int h = topH;
                if (showCombined) h += scaledGraphH + (int)Math.Round(10 * sc);
                if (hasFpsGraph)
                {
                    if (DfGraphStyle && DfCornerInset)
                        h += GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(32 * sc);
                    else if (DfGraphStyle)
                        h += GetDfFrametimeHeight(sc, scaledGraphH) + GetDfFramerateHeight(sc, scaledGraphH) + (int)Math.Round(16 * sc);
                    else
                        h += scaledGraphH + (int)Math.Round(10 * sc);
                }

                int numGraphs = (showCombined ? 1 : 0) + (hasFpsGraph ? ((DfGraphStyle && !DfCornerInset) ? 2 : 1) : 0);
                int minW = (int)Math.Round((graphsOnly ? 640 : (numGraphs > 0 ? 420 : 180)) * sc);

                float requiredColsW = graphsOnly ? 0f : MeasureHorizontalRequiredWidth(sc, showSpark);
                int w = Math.Max(minW, (int)Math.Ceiling(requiredColsW + (16f * sc)));

                if (ClampToScreen && wa.Width > 0 && w > wa.Width - 16)
                {
                    w = wa.Width - 16;
                }

                TargetWidth = w;
                TargetHeight = Math.Max((int)Math.Round(28 * sc), h);
                Size = new Size(TargetWidth, TargetHeight);

                // Ensure the horizontal overlay bar does not spill off the right edge or bottom of the monitor
                if (ClampToScreen && wa.Right > 0 && Location.X + TargetWidth > wa.Right - 8)
                {
                    int newX = Math.Max(wa.Left + 8, wa.Right - TargetWidth - 8);
                    if (newX != Location.X)
                    {
                        Location = new Point(newX, Location.Y);
                    }
                }
                if (ClampToScreen && wa.Left > 0 && Location.X < wa.Left + 8)
                {
                    Location = new Point(wa.Left + 8, Location.Y);
                }
                if (ClampToScreen && wa.Bottom > 0 && Location.Y + TargetHeight > wa.Bottom - 8)
                {
                    int newY = Math.Max(wa.Top + 8, wa.Bottom - TargetHeight - 8);
                    if (newY != Location.Y)
                    {
                        Location = new Point(Location.X, newY);
                    }
                }
            }
        }

        public long ComputeRenderSignature(FanStatus st, string mode, string game)
        {
            unchecked
            {
                long hash = 17;
                hash = hash * 31 + st.CpuTemp;
                hash = hash * 31 + st.CpuLoadPct;
                hash = hash * 31 + st.CpuClockMhz;
                hash = hash * 31 + st.GpuTemp;
                hash = hash * 31 + st.GpuHotspotTemp;
                hash = hash * 31 + (st.IsDiscreteGpu ? 1 : 0);
                hash = hash * 31 + (IsReadOnlyMode ? 1 : 0);
                hash = hash * 31 + st.GfxClockMhz;
                hash = hash * 31 + st.GfxActivityPct;
                hash = hash * 31 + st.ApuPowerWatts;
                hash = hash * 31 + st.Fan1Rpm;
                hash = hash * 31 + st.Fan2Rpm;
                hash = hash * 31 + st.NvmeTemp;
                hash = hash * 31 + (int)Math.Round((st.NvmeReadMBs + st.NvmeWriteMBs) * 10);
                hash = hash * 31 + st.RamLoadPct;
                hash = hash * 31 + (int)Math.Round(st.RamPowerWatts * 10);
                hash = hash * 31 + (int)Math.Round(st.NvmePowerWatts * 10);
                hash = hash * 31 + st.DdrTemp1;
                hash = hash * 31 + st.DdrTemp2;
                hash = hash * 31 + st.AmbientTemp;
                hash = hash * 31 + (ShowRamTemp ? 1 : 0);
                hash = hash * 31 + (ShowRamDualSticks ? 1 : 0);
                hash = hash * 31 + (ShowRamSpread ? 1 : 0);
                hash = hash * 31 + (ShowRamLoad ? 1 : 0);
                hash = hash * 31 + (ShowRamPower ? 1 : 0);
                hash = hash * 31 + (ShowRamClock ? 1 : 0);
                hash = hash * 31 + (ShowNvmePower ? 1 : 0);
                hash = hash * 31 + (ShowNvmeIo ? 1 : 0);
                hash = hash * 31 + (ShowNvmeDual ? 1 : 0);
                hash = hash * 31 + (ShowCpuLoad ? 1 : 0);
                hash = hash * 31 + (ShowGpuHotspot ? 1 : 0);
                hash = hash * 31 + (ShowPowerLimit ? 1 : 0);
                hash = hash * 31 + (ShowFclkSync ? 1 : 0);
                hash = hash * 31 + (ShowFrametimeMs ? 1 : 0);
                hash = hash * 31 + (ShowFpsAvg ? 1 : 0);
                hash = hash * 31 + (ShowLatency ? 1 : 0);
                hash = hash * 31 + (int)Math.Round(st.DisplayLatencyMs * 10);
                if (st.PresentFlipMode != null) hash = hash * 31 + st.PresentFlipMode.GetHashCode();
                hash = hash * 31 + (ShowRadeonExtendedTags ? 1 : 0);
                hash = hash * 31 + (UseFahrenheit ? 1 : 0);
                hash = hash * 31 + PeakTrackerMode;
                hash = hash * 31 + (int)Math.Round(st.VddTdcCurrentAmps * 10);
                hash = hash * 31 + st.Fps;
                hash = hash * 31 + st.RtssAvgFps;
                hash = hash * 31 + st.RtssP1LowFps;
                hash = hash * 31 + st.RtssP01LowFps;
                hash = hash * 31 + (int)Math.Round((double)st.RtssFrametimeMs * 10);
                if (GraphMode != 3 && Stats != null)
                {
                    hash = hash * 31 + Stats.CpuHistory.Count;
                }
                if (mode != null) hash = hash * 31 + mode.GetHashCode();
                if (game != null) hash = hash * 31 + game.GetHashCode();
                if (ProcessorBadgeText != null) hash = hash * 31 + ProcessorBadgeText.GetHashCode();
                return hash;
            }
        }

        public void UpdateMetrics(FanStatus st, string modeSummary, bool hasDualFans)
        {
            int prevCols = CountVisibleMetrics();
            int prevTags = (_status.GetActiveRadeonTags() != null) ? _status.GetActiveRadeonTags().Count : 0;
            bool layoutChanged = (HasDualFans != hasDualFans);
            string prevMode = _modeSummary;
            string prevGame = _lastGameTitle;
            _status = st;
            _modeSummary = string.IsNullOrEmpty(modeSummary) ? st.Mode : modeSummary;
            HasDualFans = hasDualFans;
            _lastGameTitle = ShowGameName ? FormatGameName(st.RtssProcessName) : null;
            bool gameChanged = (prevGame != _lastGameTitle);

            int newCols = CountVisibleMetrics();
            int newTags = (_status.GetActiveRadeonTags() != null) ? _status.GetActiveRadeonTags().Count : 0;
            bool modeChanged = (prevMode != _modeSummary);

            if (layoutChanged || modeChanged || gameChanged || prevCols != newCols || prevTags != newTags || TargetHeight != Height || TargetWidth != Width)
                UpdateDimensions();

            // When the overlay window is hidden or minimized, skip GDI+ rendering and Win32 blits completely
            if (!Visible || Opacity <= 0)
            {
                SkippedRepaintCount++;
                return;
            }

            // Hook-Free Overlay Game-Impact Optimization (CapFrameX dirty-check pattern):
            // If the visible metric values, active mode, game title, and graph series have not changed
            // and no user mouse interaction (hover/drag) is occurring, skip the GDI+ rasterization and Win32 blit.
            long currentSig = ComputeRenderSignature(st, _modeSummary, _lastGameTitle);
            if (!_dragging && _hoverMouseX == -1 && _hoverMouseY == -1 && currentSig == _lastRenderSignature)
            {
                SkippedRepaintCount++;
                return;
            }

            _lastRenderSignature = currentSig;
            TotalRepaintCount++;
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
                if (ClampToScreen)
                {
                    Screen scr = Screen.FromControl(this);
                    Rectangle wa = (scr != null) ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;
                    if (wa.Width > 0 && wa.Height > 0)
                    {
                        int newX = Math.Max(wa.Left + 8, Math.Min(wa.Right - Width - 8, Location.X));
                        int newY = Math.Max(wa.Top + 8, Math.Min(wa.Bottom - Height - 8, Location.Y));
                        if (newX != Location.X || newY != Location.Y)
                        {
                            Location = new Point(newX, newY);
                        }
                    }
                }
                if (PositionOrLayoutChanged != null)
                    PositionOrLayoutChanged(this, EventArgs.Empty);
            }
        }

    }
}
