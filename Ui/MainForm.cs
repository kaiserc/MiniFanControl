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
    public class MainForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY      = 0x0312;
        private const uint MOD_ALT       = 0x0001;
        private const uint MOD_CONTROL   = 0x0002;
        private const uint MOD_NOREPEAT  = 0x4000;
        private const uint VK_F9         = 0x78;
        private const uint VK_F10        = 0x79;
        private const uint VK_F11        = 0x7A;
        private const uint VK_F12        = 0x7B;

        private const int HOTKEY_TOGGLE_OSD = 9001;
        private const int HOTKEY_CYCLE_MODE = 9002;
        private const int HOTKEY_LOCK_OSD   = 9003;
        private const int HOTKEY_RESET_AUTO = 9004;

        public const string APP_VERSION         = "1.0.0";
        public const string SCHTASK_NAME        = "MiniFanControl_Autostart";
        public const string URL_GMKTEC_DRIVERS  = "https://www.gmktec.com/pages/drivers-and-software";
        public const string URL_GMKTEC_FIRMWARE = "https://www.gmktec.com/pages/firmware-update";
        public const string URL_AMD_DRIVERS     = "https://www.amd.com/en/support/download/drivers.html";

        private const int DEFAULT_REFRESH_MS = 1000;
        private const int FAST_REFRESH_MS   = 400;

        // Dark Palette
        private static readonly Color DarkBg       = Color.FromArgb(0x14, 0x14, 0x1E);
        private static readonly Color DarkSurface  = Color.FromArgb(0x22, 0x22, 0x32);
        private static readonly Color DarkSurface2 = Color.FromArgb(0x2E, 0x30, 0x44);
        private static readonly Color DarkText     = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color DarkTextDim  = Color.FromArgb(0x94, 0x99, 0xB0);
        private static readonly Color DarkAccent   = Color.FromArgb(0x89, 0xB4, 0xFA);
        private static readonly Color DarkTeal     = Color.FromArgb(0x2D, 0xD4, 0xBF);

        // Light Palette
        private static readonly Color LightBg       = Color.FromArgb(0xF3, 0xF4, 0xF6);
        private static readonly Color LightSurface  = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color LightSurface2 = Color.FromArgb(0xE5, 0xE7, 0xEB);
        private static readonly Color LightText     = Color.FromArgb(0x00, 0x00, 0x00);
        private static readonly Color LightTextDim  = Color.FromArgb(0x4B, 0x55, 0x63);
        private static readonly Color LightAccent   = Color.FromArgb(0x1D, 0x4E, 0xD8);
        private static readonly Color LightTeal     = Color.FromArgb(0x0D, 0x94, 0x88);

        // AMD Radeon (Adrenalin Crimson / Ruby) Palette
        private static readonly Color RadeonBg       = Color.FromArgb(0x14, 0x0A, 0x0D);
        private static readonly Color RadeonSurface  = Color.FromArgb(0x21, 0x11, 0x16);
        private static readonly Color RadeonSurface2 = Color.FromArgb(0x34, 0x19, 0x21);
        private static readonly Color RadeonText     = Color.FromArgb(0xFF, 0xF1, 0xF2);
        private static readonly Color RadeonTextDim  = Color.FromArgb(0xCB, 0x9E, 0xA8);
        private static readonly Color RadeonAccent   = Color.FromArgb(0xE1, 0x1D, 0x48);
        private static readonly Color RadeonOrange   = Color.FromArgb(0xF9, 0x73, 0x16);

        // MSI Afterburner Homage Palette (gunmetal carbon, brushed steel, toxic neon green, cyan, amber)
        private static readonly Color AbBg       = Color.FromArgb(0x13, 0x15, 0x19);
        private static readonly Color AbSurface  = Color.FromArgb(0x1C, 0x20, 0x26);
        private static readonly Color AbSurface2 = Color.FromArgb(0x25, 0x2B, 0x35);
        private static readonly Color AbText     = Color.FromArgb(0xEE, 0xF2, 0xF6);
        private static readonly Color AbTextDim  = Color.FromArgb(0x8A, 0x96, 0xA8);
        private static readonly Color AbGreen    = Color.FromArgb(0x00, 0xE6, 0x76);
        private static readonly Color AbCyan     = Color.FromArgb(0x00, 0xE5, 0xFF);
        private static readonly Color AbOrange   = Color.FromArgb(0xFF, 0x91, 0x00);

        private static readonly Color ErrorBgDark   = Color.FromArgb(0x3A, 0x1A, 0x1A);
        private static readonly Color ErrorFgDark   = Color.FromArgb(0xF3, 0x8B, 0xA8);
        private static readonly Color ErrorBgLight  = Color.FromArgb(0xFE, 0xE2, 0xE2);
        private static readonly Color ErrorFgLight  = Color.FromArgb(0x99, 0x1B, 0x1B);

        // Cached shared button fonts (prevents GDI HFONT allocations on every 1.5s refresh tick)
        private static readonly Font TabFontBold     = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font TabFontRegular  = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private static readonly Font ModeFontBold    = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        private static readonly Font ModeFontRegular = new Font("Segoe UI", 9.25f, FontStyle.Regular);

        private readonly HardwareBackend _hw = new HardwareBackend();
        private readonly DemoBackend _demo = new DemoBackend();
        private readonly SystemHardwareInfo _sysInfo;
        private bool _demoMode;

        private bool IsAb { get { return _themePref == ThemePreference.Afterburner; } }
        private ThemePreference _themePref = ThemePreference.System;
        private bool _isDarkMode = true;
        private bool _isTaskbarDark = true;

        private bool _showCpuTray = true;
        private bool _showGpuTray = true;
        private bool _showOverlay = false;
        private bool _lockOverlay = false;
        private bool _overlayVertical = false;
        private int _overlayX = 24;
        private int _overlayY = 24;
        private bool _restoreAutoOnExit = true;
        private bool _alwaysOnTop = false;
        private bool _enableGlobalHotkeys = true;
        private bool _hotkeysRegistered = false;
        private bool _startMinimized = false;
        private bool _safeExitRestored = false;
        private readonly TelemetrySessionStats _sessionStats = new TelemetrySessionStats();
        private CapFrameXOverlayForm _overlayForm;

        // CapFrameX OSD Overlay Studio Configuration
        private int _osdScaleTier = 0; // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        private int _osdGraphMode = 3; // 0=None, 1=Sparklines, 2=Combined, 3=Both
        private int _osdGraphHeight = 105; // 75=Compact, 105=Medium, 140=Large, 180=XL
        private int _osdOpacityPct = 88; // 35..100
        private bool _osdMatchTheme = true;
        private bool _osdShowCpu = true;
        private bool _osdShowGpu = true;
        private bool _osdShowNvme = true;
        private bool _osdShowPower = true;
        private bool _osdShowRam = true;
        private bool _osdShowFps = true;
        private bool _osdShowLows = true;
        private bool _osdShowGpuLoad = true;
        private bool _osdShowApuFan = true;
        private bool _osdShowSysFan = true;
        private bool _osdShowMode = true;
        private bool _osdShowPeaks = true;
        private bool _osdShowRadeon = true;
        private bool _osdShowDriverVer = false;
        private bool _osdShowClocks = false;
        private bool _osdSparklineBg = false;
        private bool _osdShowLiveTrend = true;
        private bool _osdShowFpsGraph = true;
        private bool _osdPlotFpsOnTrend = true;
        private bool _osdDfGraphStyle = false;
        private bool _osdDfGraphFirst = false;

        private IntPtr _cpuIconHandle = IntPtr.Zero;
        private IntPtr _gpuIconHandle = IntPtr.Zero;
        private int _lastCpuIconKey = int.MinValue;
        private int _lastGpuIconKey = int.MinValue;

        private DateTime _rampUntil = DateTime.MinValue;

        private Process _watchdogProcess;
        private EventWaitHandle _watchdogHeartbeatEvent;
        private EventWaitHandle _watchdogShutdownEvent;
        private EventWaitHandle _watchdogReadyEvent;
        private string _lastHardwareWriteError = null;
        private int _consecutiveWriteFailures = 0;

        internal DemoBackend DemoBackend { get { return _demo; } }
        internal int LastAppliedApuPct { get { return _lastAppliedApuPct; } }
        internal int LastAppliedSysPct { get { return _lastAppliedSysPct; } }
        internal int LastAppliedEcMode { get { return _lastAppliedEcMode; } }
        internal string LastHardwareWriteError { get { return _lastHardwareWriteError; } }
        internal int ConsecutiveWriteFailures { get { return _consecutiveWriteFailures; } }
        internal bool TestEvaluateAndApplyFanProfiles(int apuTemp) { return EvaluateAndApplyFanProfiles(apuTemp); }
        internal void TestSetFanManual(int fanId, int pct) { ApplyFan(fanId, pct); }
        internal void TestApplyAutoAll() { ApplyAutoAll(); }
        internal FanCurveProfile ApuProfile { get { return _apuProfile; } set { _apuProfile = value; } }
        internal List<FanCurveProfile> CustomProfiles { get { return _customProfiles; } }
        internal Label TestCpuTempLabel { get { return _cpuTempLabel; } }
        internal Label TestFan1RpmLabel { get { return _fan1RpmLabel; } }
        internal Label TestOsdOpacityLabel { get { return _osdOpacityLabel; } }
        internal Label TestOsdSnapLabel { get { return _osdSnapLabel; } }
        internal Label TestOsdMetricsHeaderLabel { get { return _osdMetricsHeaderLabel; } }
        internal Button TestOsdQuickPosBc { get { return _osdQuickPosBc; } }
        internal Button TestOsdQuickPosBr { get { return _osdQuickPosBr; } }
        internal CheckBox TestAutostartCb { get { return _autostartCb; } }
        internal CheckBox TestAlwaysOnTopCb { get { return _alwaysOnTopCb; } }
        private DateTime _lastWorkerSampleUtc = DateTime.UtcNow;
        private bool _watchdogStarted = false;
        private SessionEndingEventHandler _sessionEndingHandler;
        private PowerModeChangedEventHandler _powerModeChangedHandler;
        private string _pendingActionText = null;
        private int _rampTargetFan = 0;

        // 3 Built-in EC profiles with separate APU (Fan 1) & System RAM/NVMe (Fan 2) curves
        // Note: Minimum spinning PWM on K12 fans is 30% (0% = Fan Off / Idle).
        private readonly FanCurveProfile[] _builtInProfiles = new FanCurveProfile[]
        {
            new FanCurveProfile("Balanced", true, 0,
                new CurvePoint[] { new CurvePoint(40, 0),  new CurvePoint(48, 35), new CurvePoint(65, 55), new CurvePoint(78, 80), new CurvePoint(88, 100) },
                new CurvePoint[] { new CurvePoint(40, 35), new CurvePoint(55, 42), new CurvePoint(70, 52), new CurvePoint(80, 65), new CurvePoint(90, 80) }),
            new FanCurveProfile("Performance", true, 1,
                new CurvePoint[] { new CurvePoint(38, 40), new CurvePoint(50, 55), new CurvePoint(62, 75), new CurvePoint(74, 92), new CurvePoint(84, 100) },
                new CurvePoint[] { new CurvePoint(38, 45), new CurvePoint(52, 58), new CurvePoint(66, 72), new CurvePoint(78, 85), new CurvePoint(88, 100) }),
            new FanCurveProfile("Silent", true, 2,
                new CurvePoint[] { new CurvePoint(48, 0),  new CurvePoint(56, 30), new CurvePoint(70, 45), new CurvePoint(82, 70), new CurvePoint(92, 100) },
                new CurvePoint[] { new CurvePoint(45, 30), new CurvePoint(60, 34), new CurvePoint(72, 42), new CurvePoint(84, 52), new CurvePoint(92, 65) })
        };

        private readonly List<FanCurveProfile> _customProfiles = new List<FanCurveProfile>();
        private FanCurveProfile _apuProfile;
        private FanCurveProfile _sysProfile;
        private bool _hasDualFans = true;
        private bool _apuManualOverride = false;
        private bool _sysManualOverride = false;
        private int _lastAppliedApuPct = -1;
        private int _lastAppliedSysPct = -1;
        private int _lastAppliedEcMode = -1;
        private int _lastCustomEvalTemp = -100;
        private bool _emergencyThermalActive = false;

        private Thread _hwThread;
        private volatile bool _hwThreadRunning;
        private volatile bool _hwWorkerPaused;
        private readonly AutoResetEvent _hwWakeEvent = new AutoResetEvent(false);

        private readonly List<Label> _sectionHeaders = new List<Label>();
        private readonly List<Panel> _sectionCards = new List<Panel>();
        private readonly List<Label> _staticLabels = new List<Label>();

        private Label _hwBadgeLabel;
        private Label _hwSubLabel;
        private Button _driversMenuBtn;
        private ContextMenu _driversMenu;
        private Button _themeToggleBtn;
        private Label _errorBanner;
        private DoubleBufferedPanel _cpuTempBox;
        private DoubleBufferedPanel _gpuTempBox;
        private DoubleBufferedPanel _nvmeTempBox;
        private DoubleBufferedPanel _pwrBox;
        private DoubleBufferedPanel _ramPowerBox;
        private DoubleBufferedPanel _fan1Box;
        private DoubleBufferedPanel _fan2Box;
        private DoubleBufferedPanel _ramCard;
        private Label _ramLabel;
        private string _lastNvmeTooltipText;
        private string _lastRamTooltipText;
        private Label _samplingLabel;
        private ComboBox _samplingCombo;
        private FanStatus _lastStatus;
        private int _refreshIntervalMs = 1000;
        private string _profileOverrideId = "auto";
        private Label _cpuBoxTitle;
        private Label _gpuBoxTitle;
        private Label _nvmeBoxTitle;
        private Label _pwrBoxTitle;
        private Label _ramBoxTitle;
        private Label _fan1BoxTitle;
        private Label _fan2BoxTitle;
        private Label _cpuTempLabel;
        private Label _gpuTempLabel;
        private Label _nvmeTempLabel;
        private Label _pwrLabel;
        private Label _ramPowerLabel;
        private Label _fan1RpmLabel;
        private Label _fan2RpmLabel;
        private Label _modeLabel;
        private Label _pct1Label;
        private Label _pct2Label;

        private Button[] _modeButtons = new Button[3];
        private ComboBox _customModeCombo;
        private Button _activateCustomBtn;
        private Button _addCustomBtn;
        private Button _deleteCustomBtn;
        private Button _tabApuCurveBtn;
        private Button _tabSysCurveBtn;
        private Button _tabLiveGraphBtn;
        private Button _tabOsdConfigBtn;
        private Panel _osdConfigPanel;
        private bool _showingOsdConfig = false;
        private Label _customLabel;
        private FanCurveCanvas _curveCanvas;
        private LiveTelemetryCanvas _liveCanvas;
        private bool _showingLiveTelemetry = false;
        private int _highlightedMetric = 0;
        private Label _curveHintLabel;
        private Label _calibBadgeLabel;

        // OSD Studio UI Controls
        private Button _osdBtnScale100;
        private Button _osdBtnScale125;
        private Button _osdBtnScale150;
        private Button _osdBtnScale185;
        private Button _osdBtnBothGraphs;
        private Button _osdBtnCombinedGraph;
        private Button _osdBtnSparklines;
        private Button _osdBtnTextOnly;
        private Button _osdBtnHeightCompact;
        private Button _osdBtnHeightMedium;
        private Button _osdBtnHeightLarge;
        private Button _osdBtnHeightXl;
        private Button _osdBtnLayoutH;
        private Button _osdBtnLayoutV;
        private TrackBar _osdOpacitySlider;
        private Label _osdOpacityPctLabel;
        private Label _osdTitleLabel;
        private Label _osdGraphModeLabel;
        private Label _osdGraphHeightLabel;
        private Label _osdScaleLabel;
        private Label _osdLayoutLabel;
        private Label _osdOpacityLabel;
        private Label _osdSnapLabel;
        private Label _osdMetricsHeaderLabel;
        private Label _osdTipLabel;
        private CheckBox _osdMatchThemeCb;
        private CheckBox _osdShowCpuCb;
        private CheckBox _osdShowGpuCb;
        private CheckBox _osdShowNvmeCb;
        private CheckBox _osdShowPowerCb;
        private CheckBox _osdShowRamCb;
        private CheckBox _osdShowFpsCb;
        private CheckBox _osdShowLowsCb;
        private CheckBox _osdShowGpuLoadCb;
        private CheckBox _osdShowApuFanCb;
        private CheckBox _osdShowSysFanCb;
        private CheckBox _osdShowModeCb;
        private CheckBox _osdShowPeaksCb;
        private CheckBox _osdShowRadeonCb;
        private CheckBox _osdShowDriverVerCb;
        private CheckBox _osdShowClocksCb;
        private CheckBox _osdSparklineBgCb;
        private CheckBox _osdShowLiveTrendCb;
        private CheckBox _osdShowFpsGraphCb;
        private CheckBox _osdPlotFpsOnTrendCb;
        private CheckBox _osdDfGraphStyleCb;
        private CheckBox _osdDfGraphFirstCb;
        private Button _osdSelectAllBtn;
        private Button _osdDeselectAllBtn;
        private Button _osdResetDefaultsBtn;
        private Button _osdQuickPosTl;
        private Button _osdQuickPosTc;
        private Button _osdQuickPosTr;
        private Button _osdQuickPosBl;
        private Button _osdQuickPosBc;
        private Button _osdQuickPosBr;
        private Button _osdToggleOverlayBtn;
        private Button _osdToggleLockBtn;
        private Button _optCardOsdConfigBtn;

        private Button _fan1AutoBtn;
        private Button _fan2AutoBtn;
        private Button _allAutoBtn;
        private Button _closeBtn;

        private TrackBar _fan1Slider;
        private TrackBar _fan2Slider;
        private Label _fan1SliderPct;
        private Label _fan2SliderPct;
        private bool _dragging1;
        private bool _dragging2;
        private int _apuManualPct = 0;
        private int _sysManualPct = 0;
        private bool _suppressComboEvent;

        private CheckBox _showCpuTrayCb;
        private CheckBox _showGpuTrayCb;
        private CheckBox _showOverlayCb;
        private CheckBox _lockOverlayCb;
        private CheckBox _autostartCb;
        private CheckBox _alwaysOnTopCb;
        private CheckBox _trayCb;
        private Label _statusBarLabel;

        private System.Windows.Forms.Timer _refreshTimer;
        private NotifyIcon _cpuTrayIcon;
        private NotifyIcon _gpuTrayIcon;
        private ToolTip _uiToolTip;
        private PictureBox _headerLogoBox;
        private string _lastCpuTooltipText = "";
        private string _lastPowerTooltipText = "";
        private string _lastIgpuTooltipText = "";
        private string _lastApuFanTooltipText = "";
        private string _lastSysFanTooltipText = "";
        private int _lastSelectedModeCode = 0;

        public MainForm(bool startInDemo, bool startMinimized = false, bool isUnitTest = false)
        {
            _demoMode = startInDemo;
            _startMinimized = startMinimized;
            _sysInfo = SystemHardwareInfo.Detect();
            _hw.Profile = _sysInfo.MatchedProfile;
            _apuProfile = _builtInProfiles[0];
            _sysProfile = _builtInProfiles[0];
            LoadSettings();
            if (_customProfiles.Count == 0)
            {
                _customProfiles.Add(new FanCurveProfile("My Quiet Curve", false, -1,
                    new CurvePoint[]
                    {
                        new CurvePoint(42, 0),
                        new CurvePoint(55, 32),
                        new CurvePoint(68, 52),
                        new CurvePoint(80, 75),
                        new CurvePoint(90, 100)
                    },
                    new CurvePoint[]
                    {
                        new CurvePoint(42, 30),
                        new CurvePoint(58, 35),
                        new CurvePoint(70, 42),
                        new CurvePoint(82, 52),
                        new CurvePoint(90, 65)
                    }));
            }

            DetectSystemTheme(out _isDarkMode, out _isTaskbarDark);

            Text = "MiniFanControl";
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 960);
            try
            {
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) Icon = assoc;
            }
            catch { }

            InitOverlayForm();
            InitTrayIcons();
            BuildUi();
            ApplyFanLayoutMode();
            PopulateCustomCombo();
            ApplyThemeToControls();
            UpdateTrayIcons(0, 0);

            if (!isUnitTest)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _sessionEndingHandler = (s, e) => RestoreHardwareSafeAutoIfNeeded();
            _powerModeChangedHandler = (s, e) =>
            {
                if (e.Mode == PowerModes.Suspend)
                {
                    RestoreHardwareSafeAutoIfNeeded();
                }
            };
            SystemEvents.SessionEnding += _sessionEndingHandler;
            SystemEvents.PowerModeChanged += _powerModeChangedHandler;
            AppDomain.CurrentDomain.ProcessExit += OnProcessOrCrashExit;
            AppDomain.CurrentDomain.UnhandledException += OnProcessOrCrashExit;

            _refreshTimer = new System.Windows.Forms.Timer { Interval = _refreshIntervalMs };
            _refreshTimer.Tick += (s, e) =>
            {
                // UI-thread pulse: Dual-thread heartbeat verifies both background hardware worker and UI message pump are alive
                if ((DateTime.UtcNow - _lastWorkerSampleUtc).TotalMilliseconds < 4500)
                {
                    PulseWatchdogHeartbeat();
                }

                // UI-only periodic refresh (theme sync and status bar timer)
                if (_themePref == ThemePreference.System)
                {
                    CheckAndApplyThemeChanges();
                }
                if (_emergencyThermalActive)
                {
                    _statusBarLabel.ForeColor = Color.FromArgb(0xFB, 0x71, 0x85);
                    _statusBarLabel.Text = string.Format("🔥 EMERGENCY THERMAL OVERRIDE (CPU {0}°C) — Fans forced to 100%", _lastStatus.CpuTemp);
                }
            };
            _refreshTimer.Start();

            // Decouple hardware polling and fan curve regulation to a dedicated background worker
            _hwThreadRunning = true;
            _hwThread = new Thread(HardwareWorkerLoop)
            {
                IsBackground = true,
                Name = "MiniFanControl.HardwareWorker",
                Priority = ThreadPriority.AboveNormal
            };
            _hwThread.Start();
            StartWatchdogSupervisor();

            Shown += (s, e) =>
            {
                UpdateGlobalHotkeysRegistration();
                if (_startMinimized)
                {
                    MinimizeToTray();
                }
            };
                FormClosing += OnFormClosing;
                _hwWakeEvent.Set();
            }
        }

        private void OnProcessOrCrashExit(object sender, EventArgs e)
        {
            RestoreHardwareSafeAutoIfNeeded();
        }

        private void RestoreHardwareSafeAutoIfNeeded()
        {
            if (_safeExitRestored) return;
            _safeExitRestored = true;
            StopWatchdogSupervisorGracefully();
            if (!_restoreAutoOnExit || _demoMode) return;

            try
            {
                // If either fan was running on a software-evaluated curve or manual slider override,
                // restore its EC control register to 0x00 (Native BIOS EC Auto) on exit so the fan
                // never stays stuck at a fixed low PWM after MiniFanControl closes.
                bool apuWasSoftwareOrManual = _apuManualOverride || (_apuProfile != null && !_apuProfile.IsBuiltIn) || _lastAppliedApuPct > 0;
                bool sysWasSoftwareOrManual = _sysManualOverride || (_sysProfile != null && !_sysProfile.IsBuiltIn) || _lastAppliedSysPct > 0;

                if (apuWasSoftwareOrManual)
                    _hw.SetFan(1, 0);
                if (_hasDualFans && sysWasSoftwareOrManual)
                    _hw.SetFan(2, 0);
                _hw.SetAuto();
            }
            catch { }
        }

        private void StartWatchdogSupervisor()
        {
            if (_watchdogStarted && _watchdogProcess != null && !_watchdogProcess.HasExited) return;

            try
            {
                int myPid = Process.GetCurrentProcess().Id;
                bool createdHb, createdSd, createdReady;
                _watchdogHeartbeatEvent = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetHeartbeatEventName(myPid), EventResetMode.AutoReset, out createdHb, WatchdogIpcRole.Signaler);
                _watchdogShutdownEvent = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetShutdownEventName(myPid), EventResetMode.ManualReset, out createdSd, WatchdogIpcRole.Signaler);
                _watchdogReadyEvent = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetReadyEventName(myPid), EventResetMode.ManualReset, out createdReady, WatchdogIpcRole.Waiter);

                if (_watchdogHeartbeatEvent == null || _watchdogShutdownEvent == null || _watchdogReadyEvent == null)
                {
                    WatchdogSupervisor.LogWatchdogEvent("Failed to create secure IPC events for watchdog. Supervisor not started.");
                    StopWatchdogSupervisorGracefully();
                    _watchdogStarted = false;
                    return;
                }

                _watchdogReadyEvent.Reset();

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string helperPath = Path.Combine(exeDir, "minifan-helper.exe");
                if (!File.Exists(helperPath))
                {
                    helperPath = Path.Combine(exeDir, "MiniFanControl.exe");
                }

                if (File.Exists(helperPath))
                {
                    string args = string.Format("watchdog --pid {0} --timeout 3500{1}", myPid, _demoMode ? " --demo" : "");
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = helperPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _watchdogProcess = Process.Start(psi);
                    if (_watchdogProcess != null && !_watchdogProcess.HasExited)
                    {
                        // Explicit handshake: Wait up to 3000ms for child helper to verify IPC and enter monitor loop
                        bool readySignaled = _watchdogReadyEvent.WaitOne(3000);
                        if (readySignaled && !_watchdogProcess.HasExited)
                        {
                            _watchdogStarted = true;
                            WatchdogSupervisor.LogWatchdogEvent(string.Format("Supervisor process PID {0} launched and completed handshake for parent PID {1}.", _watchdogProcess.Id, myPid));
                        }
                        else
                        {
                            _watchdogStarted = false;
                            WatchdogSupervisor.LogWatchdogEvent(string.Format("Supervisor process PID {0} failed handshake (ready={1}, hasExited={2}).",
                                _watchdogProcess.Id, readySignaled, _watchdogProcess.HasExited));
                            StopWatchdogSupervisorGracefully();
                        }
                    }
                    else
                    {
                        _watchdogStarted = false;
                        WatchdogSupervisor.LogWatchdogEvent("Watchdog process failed to stay running after launch.");
                        StopWatchdogSupervisorGracefully();
                    }
                }
                else
                {
                    _watchdogStarted = false;
                    WatchdogSupervisor.LogWatchdogEvent("Watchdog helper executable not found: " + helperPath);
                }
            }
            catch (Exception ex)
            {
                _watchdogStarted = false;
                WatchdogSupervisor.LogWatchdogEvent("Exception while starting watchdog supervisor: " + ex.Message);
            }
        }

        private void PulseWatchdogHeartbeat()
        {
            if (_watchdogHeartbeatEvent != null)
            {
                try { _watchdogHeartbeatEvent.Set(); } catch { }
            }
        }

        private void StopWatchdogSupervisorGracefully()
        {
            try
            {
                if (_watchdogShutdownEvent != null)
                {
                    _watchdogShutdownEvent.Set();
                }
                if (_watchdogProcess != null && !_watchdogProcess.HasExited)
                {
                    _watchdogProcess.WaitForExit(1000);
                }
            }
            catch { }
            finally
            {
                if (_watchdogReadyEvent != null) { try { _watchdogReadyEvent.Close(); } catch { } _watchdogReadyEvent = null; }
                if (_watchdogHeartbeatEvent != null) { try { _watchdogHeartbeatEvent.Close(); } catch { } _watchdogHeartbeatEvent = null; }
                if (_watchdogShutdownEvent != null) { try { _watchdogShutdownEvent.Close(); } catch { } _watchdogShutdownEvent = null; }
                if (_watchdogProcess != null) { try { _watchdogProcess.Dispose(); } catch { } _watchdogProcess = null; }
            }
        }

        private void UpdateGlobalHotkeysRegistration()
        {
            if (!IsHandleCreated) return;
            UnregisterGlobalHotkeys();
            if (!_enableGlobalHotkeys) return;

            try
            {
                uint mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
                bool ok1 = RegisterHotKey(Handle, HOTKEY_TOGGLE_OSD, mods, VK_F9);
                bool ok2 = RegisterHotKey(Handle, HOTKEY_CYCLE_MODE, mods, VK_F10);
                bool ok3 = RegisterHotKey(Handle, HOTKEY_LOCK_OSD,   mods, VK_F11);
                bool ok4 = RegisterHotKey(Handle, HOTKEY_RESET_AUTO, mods, VK_F12);
                _hotkeysRegistered = (ok1 || ok2 || ok3 || ok4);
            }
            catch
            {
                _hotkeysRegistered = false;
            }
        }

        private void UnregisterGlobalHotkeys()
        {
            if (!IsHandleCreated) return;
            try
            {
                UnregisterHotKey(Handle, HOTKEY_TOGGLE_OSD);
                UnregisterHotKey(Handle, HOTKEY_CYCLE_MODE);
                UnregisterHotKey(Handle, HOTKEY_LOCK_OSD);
                UnregisterHotKey(Handle, HOTKEY_RESET_AUTO);
            }
            catch { }
            _hotkeysRegistered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE_OSD)
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                    return;
                }
                else if (id == HOTKEY_CYCLE_MODE)
                {
                    int nextCode = (_lastSelectedModeCode + 1) % 3;
                    ApplyMode(nextCode);
                    return;
                }
                else if (id == HOTKEY_LOCK_OSD)
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                    return;
                }
                else if (id == HOTKEY_RESET_AUTO)
                {
                    ApplyAutoAll();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void ResetSessionPeaks()
        {
            _sessionStats.Reset();
            _lastCpuTooltipText = "";
            _lastIgpuTooltipText = "";
            _lastNvmeTooltipText = "";
            _lastPowerTooltipText = "";
            _lastRamTooltipText = "";
            _lastApuFanTooltipText = "";
            _lastSysFanTooltipText = "";
            if (_cpuBoxTitle != null) _cpuBoxTitle.Text = "CPU TEMP";
            if (_gpuBoxTitle != null) _gpuBoxTitle.Text = "iGPU TEMP";
            if (_nvmeBoxTitle != null) _nvmeBoxTitle.Text = "NVMe TEMP";
            if (_pwrBoxTitle != null) _pwrBoxTitle.Text = "POWER ⓘ";
            if (_ramBoxTitle != null) _ramBoxTitle.Text = "RAM POWER";
            if (_fan1BoxTitle != null) _fan1BoxTitle.Text = "APU FAN";
            if (_fan2BoxTitle != null) _fan2BoxTitle.Text = "SYS FAN";
            if (_statusBarLabel != null)
                _statusBarLabel.Text = "✓ Reset session Min/Avg/Peak telemetry stats";
            RefreshStatus();
        }

        private void ExportSessionTelemetryToCsv()
        {
            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
                    sfd.FileName = string.Format("MiniFanControl-Telemetry-{0:yyyyMMdd-HHmmss}.csv", DateTime.Now);
                    sfd.Title = "Export Session Telemetry to CSV";
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        bool ok = _sessionStats.ExportToCsv(sfd.FileName);
                        if (ok)
                        {
                            MessageBox.Show(this,
                                string.Format("Successfully exported {0} telemetry records to:\n\n{1}", _sessionStats.LoggedRecordCount, sfd.FileName),
                                "Telemetry Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(this, "Failed to write CSV file. Check folder write permissions.", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error exporting telemetry: " + ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitOverlayForm()
        {
            _overlayForm = new CapFrameXOverlayForm
            {
                Location = new Point(Math.Max(0, _overlayX), Math.Max(0, _overlayY)),
                VerticalLayout = _overlayVertical,
                HasDualFans = _hasDualFans,
                Stats = _sessionStats,
                OsdScaleTier = _osdScaleTier,
                GraphMode = _osdGraphMode,
                GraphHeight = _osdGraphHeight,
                OpacityPct = _osdOpacityPct,
                MatchAppTheme = _osdMatchTheme,
                IsRadeonTheme = (_themePref == ThemePreference.Radeon), IsAfterburnerTheme = (_themePref == ThemePreference.Afterburner), ShowDriverVersion = _osdShowDriverVer,
                ShowClocks = _osdShowClocks, SparklinesAsBackground = _osdSparklineBg,
                ShowCpu = _osdShowCpu,
                ShowGpu = _osdShowGpu,
                ShowNvme = _osdShowNvme,
                ShowPower = _osdShowPower,
                ShowRam = _osdShowRam,
                ShowFps = _osdShowFps,
                ShowLows = _osdShowLows,
                ShowGpuLoad = _osdShowGpuLoad,
                ShowApuFan = _osdShowApuFan,
                ShowSysFan = _osdShowSysFan,
                ShowMode = _osdShowMode,
                ShowPeaks = _osdShowPeaks,
                ShowRadeonSettings = _osdShowRadeon,
                ShowLiveTrend = _osdShowLiveTrend,
                ShowFpsGraph = _osdShowFpsGraph,
                PlotFpsOnTrend = _osdPlotFpsOnTrend,
                DfGraphStyle = _osdDfGraphStyle,
                DfGraphFirst = _osdDfGraphFirst
            };
            _overlayForm.ApplyOpacity(_osdOpacityPct);
            _overlayForm.UpdateDimensions();
            _overlayForm.PositionOrLayoutChanged += (s, e) =>
            {
                _overlayX = _overlayForm.Location.X;
                _overlayY = _overlayForm.Location.Y;
                _overlayVertical = _overlayForm.VerticalLayout;
                _osdScaleTier = _overlayForm.OsdScaleTier;
                _osdGraphMode = _overlayForm.GraphMode;
                _osdGraphHeight = _overlayForm.GraphHeight;
                _osdOpacityPct = _overlayForm.OpacityPct;
                SyncOsdStudioUiFromOverlay();
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
            };
            _overlayForm.RequestHideOverlay += (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = false;
                else SetOverlayVisible(false);
            };
            _overlayForm.RequestToggleLock += (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                else SetOverlayLocked(!_lockOverlay);
            };
            _overlayForm.RequestOpenOsdConfig += (s, e) =>
            {
                OpenOsdConfigTab();
            };
            _overlayForm.RequestSnapLocation += (s, corner) =>
            {
                SetOverlayCornerPosition(corner);
            };

            // Pre-seed live telemetry into overlay before first display so dimensions account for all active rows (NVMe, RAM, Mode)
            if (_lastStatus.RamTotalGb <= 0 && _hw != null)
            {
                try { _lastStatus = _hw.GetStatus(); } catch { }
            }
            string initMode = (!_hasDualFans || _apuProfile == null)
                ? ShortModeTag(_apuProfile != null ? _apuProfile.Name : "Auto")
                : string.Format("{0}/{1}", ShortModeTag(_apuProfile.Name), ShortModeTag(_sysProfile != null ? _sysProfile.Name : _apuProfile.Name));
            _overlayForm.UpdateMetrics(_lastStatus, initMode, _hasDualFans);

            if (_showOverlay)
            {
                _overlayForm.Show();
                _overlayForm.ApplyClickThrough(_lockOverlay);
                _overlayForm.EnsureTopMost();
                _overlayForm.BringToFront();
                _overlayForm.Invalidate();
            }
        }

        private void SyncOverlayFromStudio()
        {
            if (_overlayForm == null) return;
            _overlayForm.Stats = _sessionStats;
            _overlayForm.HasDualFans = _hasDualFans;
            _overlayForm.OsdScaleTier = _osdScaleTier;
            _overlayForm.GraphMode = _osdGraphMode;
            _overlayForm.GraphHeight = _osdGraphHeight;
            _overlayForm.VerticalLayout = _overlayVertical;
            _overlayForm.MatchAppTheme = _osdMatchTheme;
            _overlayForm.IsRadeonTheme = (_themePref == ThemePreference.Radeon); _overlayForm.IsAfterburnerTheme = (_themePref == ThemePreference.Afterburner); _overlayForm.ShowDriverVersion = _osdShowDriverVer;
            _overlayForm.ShowClocks = _osdShowClocks;
            _overlayForm.SparklinesAsBackground = _osdSparklineBg;
            _overlayForm.ShowCpu = _osdShowCpu;
            _overlayForm.ShowGpu = _osdShowGpu;
            _overlayForm.ShowNvme = _osdShowNvme;
            _overlayForm.ShowPower = _osdShowPower;
            _overlayForm.ShowRam = _osdShowRam;
            _overlayForm.ShowFps = _osdShowFps;
            _overlayForm.ShowLows = _osdShowLows;
            _overlayForm.ShowGpuLoad = _osdShowGpuLoad;
            _overlayForm.ShowApuFan = _osdShowApuFan;
            _overlayForm.ShowSysFan = _osdShowSysFan;
            _overlayForm.ShowMode = _osdShowMode;
            _overlayForm.ShowPeaks = _osdShowPeaks;
            _overlayForm.ShowRadeonSettings = _osdShowRadeon;
            _overlayForm.ShowLiveTrend = _osdShowLiveTrend;
            _overlayForm.ShowFpsGraph = _osdShowFpsGraph;
            _overlayForm.PlotFpsOnTrend = _osdPlotFpsOnTrend;
            _overlayForm.DfGraphStyle = _osdDfGraphStyle;
            _overlayForm.DfGraphFirst = _osdDfGraphFirst;
            _overlayForm.ApplyOpacity(_osdOpacityPct);
            _overlayForm.UpdateDimensions();
            _overlayForm.Invalidate();
            SaveSettings();
        }

        private void SyncOsdStudioUiFromOverlay()
        {
            if (_overlayForm != null)
            {
                _osdScaleTier = _overlayForm.OsdScaleTier;
                _osdGraphMode = _overlayForm.GraphMode;
                _osdGraphHeight = _overlayForm.GraphHeight;
                _osdOpacityPct = _overlayForm.OpacityPct;
                _overlayVertical = _overlayForm.VerticalLayout;
                _osdShowCpu = _overlayForm.ShowCpu;
                _osdShowGpu = _overlayForm.ShowGpu;
                _osdShowNvme = _overlayForm.ShowNvme;
                _osdShowPower = _overlayForm.ShowPower;
                _osdShowRam = _overlayForm.ShowRam;
                _osdShowFps = _overlayForm.ShowFps;
                _osdShowLows = _overlayForm.ShowLows;
                _osdShowGpuLoad = _overlayForm.ShowGpuLoad;
                _osdShowApuFan = _overlayForm.ShowApuFan;
                _osdShowSysFan = _overlayForm.ShowSysFan;
                _osdShowMode = _overlayForm.ShowMode;
                _osdShowPeaks = _overlayForm.ShowPeaks;
                _osdShowRadeon = _overlayForm.ShowRadeonSettings; _osdShowDriverVer = _overlayForm.ShowDriverVersion;
                _osdShowClocks = _overlayForm.ShowClocks;
                _osdSparklineBg = _overlayForm.SparklinesAsBackground;
                _osdShowLiveTrend = _overlayForm.ShowLiveTrend;
                _osdShowFpsGraph = _overlayForm.ShowFpsGraph;
                _osdPlotFpsOnTrend = _overlayForm.PlotFpsOnTrend;
                _osdDfGraphStyle = _overlayForm.DfGraphStyle;
                _osdDfGraphFirst = _overlayForm.DfGraphFirst;
            }

            if (_osdOpacitySlider != null && _osdOpacitySlider.Value != _osdOpacityPct)
                _osdOpacitySlider.Value = Math.Max(_osdOpacitySlider.Minimum, Math.Min(_osdOpacitySlider.Maximum, _osdOpacityPct));
            if (_osdOpacityPctLabel != null)
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osdOpacityPct);

            if (_osdShowCpuCb != null) _osdShowCpuCb.Checked = _osdShowCpu;
            if (_osdShowGpuCb != null) _osdShowGpuCb.Checked = _osdShowGpu;
            if (_osdShowNvmeCb != null) _osdShowNvmeCb.Checked = _osdShowNvme;
            if (_osdShowPowerCb != null) _osdShowPowerCb.Checked = _osdShowPower;
            if (_osdShowRamCb != null) _osdShowRamCb.Checked = _osdShowRam;
            if (_osdShowFpsCb != null) _osdShowFpsCb.Checked = _osdShowFps;
            if (_osdShowLowsCb != null) _osdShowLowsCb.Checked = _osdShowLows;
            if (_osdShowGpuLoadCb != null) _osdShowGpuLoadCb.Checked = _osdShowGpuLoad;
            if (_osdShowApuFanCb != null) _osdShowApuFanCb.Checked = _osdShowApuFan;
            if (_osdShowSysFanCb != null)
            {
                _osdShowSysFanCb.Checked = _osdShowSysFan && _hasDualFans;
                _osdShowSysFanCb.Enabled = _hasDualFans;
                _osdShowSysFanCb.Text = _hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)";
            }
            if (_osdShowModeCb != null) _osdShowModeCb.Checked = _osdShowMode;
            if (_osdShowPeaksCb != null) _osdShowPeaksCb.Checked = _osdShowPeaks;
            if (_osdShowRadeonCb != null) _osdShowRadeonCb.Checked = _osdShowRadeon;
            if (_osdShowDriverVerCb != null) _osdShowDriverVerCb.Checked = _osdShowDriverVer;
            if (_osdShowClocksCb != null) _osdShowClocksCb.Checked = _osdShowClocks;
            if (_osdSparklineBgCb != null) _osdSparklineBgCb.Checked = _osdSparklineBg;
            if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = _osdShowLiveTrend;
            if (_osdShowFpsGraphCb != null) _osdShowFpsGraphCb.Checked = _osdShowFpsGraph;
            if (_osdPlotFpsOnTrendCb != null) _osdPlotFpsOnTrendCb.Checked = _osdPlotFpsOnTrend;
            if (_osdDfGraphStyleCb != null) _osdDfGraphStyleCb.Checked = _osdDfGraphStyle;
            if (_osdDfGraphFirstCb != null) _osdDfGraphFirstCb.Checked = _osdDfGraphFirst;
            if (_osdMatchThemeCb != null) _osdMatchThemeCb.Checked = _osdMatchTheme;

            if (_osdToggleOverlayBtn != null)
                _osdToggleOverlayBtn.Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden";
            if (_osdToggleLockBtn != null)
                _osdToggleLockBtn.Text = _lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)";

            UpdateOsdStudioStyles();
        }

        private void SetOverlayCornerPosition(int corner)
        {
            if (_overlayForm == null) return;
            Screen screen = Screen.FromControl(_overlayForm.Visible ? (Form)_overlayForm : this);
            Rectangle wa = screen.WorkingArea;
            _overlayForm.UpdateDimensions();
            int w = _overlayForm.Width;
            int h = _overlayForm.Height;
            int margin = 24;

            switch (corner)
            {
                case 0: // Top-Left
                    _overlayX = wa.Left + margin;
                    _overlayY = wa.Top + margin;
                    break;
                case 1: // Top-Right
                    _overlayX = wa.Right - w - margin;
                    _overlayY = wa.Top + margin;
                    break;
                case 2: // Bottom-Left
                    _overlayX = wa.Left + margin;
                    _overlayY = wa.Bottom - h - margin;
                    break;
                case 3: // Bottom-Right
                    _overlayX = wa.Right - w - margin;
                    _overlayY = wa.Bottom - h - margin;
                    break;
                case 4: // Center-Top
                    _overlayX = wa.Left + Math.Max(0, (wa.Width - w) / 2);
                    _overlayY = wa.Top + margin;
                    break;
                case 5: // Center-Bottom
                    _overlayX = wa.Left + Math.Max(0, (wa.Width - w) / 2);
                    _overlayY = wa.Bottom - h - margin;
                    break;
            }

            _overlayForm.Location = new Point(_overlayX, _overlayY);
            SaveSettings();
            _overlayForm.Invalidate();
        }

        private void OpenOsdConfigTab()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Show();
            Activate();
            BringToFront();

            if (!_showOverlay)
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = true;
                else SetOverlayVisible(true);
            }

            SetOsdConfigViewMode(true);
        }

        private void SetOsdConfigViewMode(bool show)
        {
            _showingOsdConfig = show;
            if (show)
            {
                _showingLiveTelemetry = false;
                _highlightedMetric = 0;
            }

            if (_osdConfigPanel != null) _osdConfigPanel.Visible = show;

            bool showCurveOrLive = !show;
            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] != null) _modeButtons[i].Visible = showCurveOrLive;
            }
            if (_customLabel != null) _customLabel.Visible = showCurveOrLive;
            if (_customModeCombo != null) _customModeCombo.Visible = showCurveOrLive;
            if (_activateCustomBtn != null) _activateCustomBtn.Visible = showCurveOrLive;
            if (_addCustomBtn != null) _addCustomBtn.Visible = showCurveOrLive;
            if (_deleteCustomBtn != null) _deleteCustomBtn.Visible = showCurveOrLive;

            if (show)
            {
                if (_curveCanvas != null) _curveCanvas.Visible = false;
                if (_liveCanvas != null) _liveCanvas.Visible = false;
                if (_curveHintLabel != null)
                    _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                SyncOsdStudioUiFromOverlay();
            }
            else
            {
                if (_showingLiveTelemetry)
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = false;
                    if (_liveCanvas != null) _liveCanvas.Visible = true;
                }
                else
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = true;
                    if (_liveCanvas != null) _liveCanvas.Visible = false;
                }
                UpdateCurveHint();
            }

            UpdateCurveTabStyles();
            UpdateOsdStudioStyles();
            if (_cpuTempBox != null) _cpuTempBox.Invalidate();
            if (_gpuTempBox != null) _gpuTempBox.Invalidate();
            if (_nvmeTempBox != null) _nvmeTempBox.Invalidate();
            if (_pwrBox != null) _pwrBox.Invalidate();
            if (_ramPowerBox != null) _ramPowerBox.Invalidate();
            if (_fan1Box != null) _fan1Box.Invalidate();
            if (_fan2Box != null) _fan2Box.Invalidate();
        }

        private void SetOverlayVisible(bool visible)
        {
            _showOverlay = visible;
            if (_overlayForm != null)
            {
                if (visible)
                {
                    if (!_overlayForm.Visible) _overlayForm.Show();
                    _overlayForm.ApplyClickThrough(_lockOverlay);
                    _overlayForm.EnsureTopMost();
                    _overlayForm.BringToFront();
                    _overlayForm.Invalidate();
                }
                else
                {
                    _overlayForm.Hide();
                }
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            RefreshStatus();
        }

        private void SetOverlayLocked(bool locked)
        {
            _lockOverlay = locked;
            if (_overlayForm != null)
            {
                _overlayForm.ApplyClickThrough(locked);
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
        }

        private void ToggleOverlayLayoutStyle()
        {
            _overlayVertical = !_overlayVertical;
            if (_overlayForm != null)
            {
                _overlayForm.VerticalLayout = _overlayVertical;
                _overlayForm.UpdateDimensions();
                _overlayForm.Invalidate();
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
        }

        private static void OpenUrlSafe(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open browser link:\n" + url + "\n\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportHardwareProbeReport()
        {
            try
            {
                string report = _sysInfo.BuildProbeReport(_hw, _demo, _demoMode);
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ec-probe-report.txt");
                File.WriteAllText(outPath, report, Encoding.UTF8);
                _statusBarLabel.Text = "✓ Saved ec-probe-report.txt";
                try
                {
                    Process.Start("notepad.exe", outPath);
                }
                catch
                {
                    MessageBox.Show(this, "Saved Hardware & EC Probe Report to:\n" + outPath,
                        "EC Probe Report Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to generate EC Probe Report:\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SubmitHardwareProfileOnline()
        {
            try
            {
                string report = _sysInfo.BuildProbeReport(_hw, _demo, _demoMode);
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ec-probe-report.txt");
                File.WriteAllText(outPath, report, Encoding.UTF8);

                try
                {
                    Clipboard.SetText(report);
                }
                catch { }

                string url = "https://github.com/kaiserc/MiniFanControl/issues/new?template=unsupported_hardware.md";
                OpenUrlSafe(url);

                MessageBox.Show(this,
                    "Your system hardware probe report has been copied to your clipboard and saved to:\n" + outPath +
                    "\n\nOpening the GitHub New Issue page in your browser — simply paste (Ctrl+V) into the issue description!",
                    "Submit Hardware Compatibility Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not prepare hardware profile submission:\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyCurveToClipboard()
        {
            try
            {
                FanCurveProfile cur = (_curveCanvas != null && _curveCanvas.EditSysFan) ? _sysProfile : _apuProfile;
                string curveStr = cur.Name + "|" + FormatPointList(cur.ApuPoints) + "|" + FormatPointList(cur.SysPoints);
                Clipboard.SetText(curveStr);
                _statusBarLabel.Text = "✓ Copied curve profile '" + cur.Name + "' to clipboard!";
                MessageBox.Show(this,
                    "Active curve snippet copied to clipboard:\n\n" + curveStr +
                    "\n\nYou can share this snippet in GitHub Discussions or Discord with other Mini PC owners!",
                    "Curve Profile Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy curve to clipboard:\n" + ex.Message, "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportCurveFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show(this, "Clipboard is empty or does not contain text.", "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string text = Clipboard.GetText().Trim();
                string[] parts = text.Split('|');
                if (parts.Length < 2)
                {
                    MessageBox.Show(this, "Invalid curve snippet format. Expected: Name|Temp:Pwm,...[|Temp:Pwm,...]", "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string name = parts[0].Trim();
                CurvePoint[] apuPts = ParsePointList(parts[1]);
                CurvePoint[] sysPts = parts.Length >= 3 ? ParsePointList(parts[2]) : FanCurveProfile.ClonePoints(apuPts);
                if (apuPts.Length < 2)
                {
                    MessageBox.Show(this, "Snippet contains insufficient curve points (minimum 2 required).", "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                FanCurveProfile imported = new FanCurveProfile(name, false, -1, apuPts, sysPts);
                _customProfiles.Add(imported);
                ActivateCustomProfile(imported);
                if (_customModeCombo != null)
                {
                    PopulateCustomCombo();
                }
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = "✓ Imported custom curve '" + name + "'!";
                MessageBox.Show(this, "Successfully imported and applied custom curve:\n" + name, "Curve Imported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import curve snippet:\n" + ex.Message, "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowHardwareCompatibilityDialog()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Detected System:");
            sb.AppendLine("  • Product / Board : " + _sysInfo.ProductName + " (" + _sysInfo.BaseBoard + ")");
            sb.AppendLine("  • Manufacturer    : " + _sysInfo.Manufacturer);
            sb.AppendLine("  • BIOS Version    : " + _sysInfo.BiosVersion);
            sb.AppendLine("  • APU / Processor : " + _sysInfo.CpuName);
            sb.AppendLine("  • Active EC Map   : " + _sysInfo.MatchedProfile.DisplayName);
            sb.AppendLine("  • EC Memory Base  : 0x" + _sysInfo.MatchedProfile.EcBaseAddress.ToString("X8") + " (ITE IT5570E)");
            sb.AppendLine();
            sb.AppendLine("Compatible / Target Ryzen Mini-PC Families:");
            foreach (DeviceProfile dp in DeviceProfile.KnownProfiles)
            {
                sb.AppendLine("  • " + dp.DisplayName);
                sb.AppendLine("    " + dp.ChipsetNotes);
            }
            sb.AppendLine();
            sb.AppendLine("Testing a new Mini PC (e.g., Ryzen AI 9 HX 370)?");
            sb.AppendLine("  Use 'Tools & Drivers ▾ → Export EC Probe Report' or run:");
            sb.AppendLine("  .\\minifan-helper.exe dump   (prints 256-byte EC hex map)");
            sb.AppendLine("  .\\minifan-helper.exe watch  (monitors live EC changes)");

            MessageBox.Show(this, sb.ToString(), "MiniFanControl — Hardware & Multi-Model Compatibility",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAboutDialog()
        {
            bool isCalibrated, isStale;
            string calibSummary = FanCurveProfile.FormatCalibrationStatusBadge(_hasDualFans, out isCalibrated, out isStale);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl v" + APP_VERSION + " (64-bit Native EC Edition)");
            sb.AppendLine("Standalone Embedded Controller Fan Curve & Thermal Utility for AMD Ryzen Mini PCs");
            sb.AppendLine("────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("Hardware & Bus Safety:");
            sb.AppendLine("  • Detected System : " + _sysInfo.ProductName + " (" + _sysInfo.CpuName + ")");
            sb.AppendLine("  • Active EC Map   : " + _sysInfo.MatchedProfile.DisplayName +
                " (ITE IT5570E @ 0x" + _sysInfo.MatchedProfile.EcBaseAddress.ToString("X8") + ")");
            sb.AppendLine("  • EC Bus Mutex    : Global\\Access_EC (Safe alongside CapFrameX, HWiNFO64, AIDA64)");
            sb.AppendLine("  • Power Telemetry : AMD ADL64 PMPM SVI2 / Thermal TDP Estimation");
            sb.AppendLine();
            sb.AppendLine("Fan Calibration & Anti-Stall Protection:");
            sb.AppendLine("  • Status          : " + calibSummary);
            sb.AppendLine(string.Format("  • Active Floor    : APU Min {0}% ({1} RPM Max){2}",
                FanCurveProfile.ApuMinSpinPct,
                FanCurveProfile.ApuMaxRpm,
                _hasDualFans
                    ? string.Format("  |  Sys Min {0}% ({1} RPM Max)", FanCurveProfile.SysMinSpinPct, FanCurveProfile.SysMaxRpm)
                    : "  |  1-Fan Mode"));
            sb.AppendLine("  • Exit Safety     : " + (_restoreAutoOnExit ? "✓ Restores BIOS EC Auto on Exit/Crash" : "Leaves last manual PWM active"));
            sb.AppendLine("  • Maintenance Tip : Re-calibrate every ~6 months or after dusting heatsink fins.");
            sb.AppendLine();
            sb.AppendLine("Global In-Game Hotkeys (" + (_enableGlobalHotkeys ? "✓ Enabled" : "Disabled") + "):");
            sb.AppendLine("  • Ctrl + Alt + F9  : Show / Hide CapFrameX-Style OSD Overlay");
            sb.AppendLine("  • Ctrl + Alt + F10 : Cycle Mode (Balanced → Performance → Silent)");
            sb.AppendLine("  • Ctrl + Alt + F11 : Lock / Unlock OSD Overlay (Click-Through)");
            sb.AppendLine("  • Ctrl + Alt + F12 : Reset Both Fans to BIOS Auto (Balanced)");
            sb.AppendLine();
            sb.AppendLine("Configuration & CLI Companion:");
            sb.AppendLine("  • Settings File   : " + GetSettingsPath());
            sb.AppendLine("  • CLI Commands    : .\\minifan-helper.exe status | calibrate | scan | dump | test");

            MessageBox.Show(this, sb.ToString(), "About MiniFanControl v" + APP_VERSION,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RunGuiFanCalibration()
        {
            _hwWorkerPaused = true;
            bool timerWasEnabled = _refreshTimer != null && _refreshTimer.Enabled;
            if (_refreshTimer != null) _refreshTimer.Stop();

            try
            {
                using (FanCalibrationDialog dlg = new FanCalibrationDialog(
                    _isDarkMode,
                    _hw,
                    _demo,
                    _demoMode,
                    prog =>
                    {
                        _statusBarLabel.Text = "↻ " + (prog.PhaseTitle ?? "Calibrating fans...");
                        FanStatus st = prog.LiveStatus;
                        if (st.CpuTemp > 0)
                        {
                            _cpuTempLabel.Text = string.Format("{0}°C", st.CpuTemp);
                            _gpuTempLabel.Text = st.GpuTemp > 0 ? string.Format("{0}°C", st.GpuTemp) : "N/A";
                            if (_pwrLabel != null)
                                _pwrLabel.Text = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                            if (_nvmeTempLabel != null)
                                _nvmeTempLabel.Text = st.NvmeTemp > 0 ? string.Format("{0}°C", st.NvmeTemp) : "N/A";
                            if (_ramPowerLabel != null)
                                _ramPowerLabel.Text = st.RamPowerWatts > 0 ? string.Format("~{0:F1}W", st.RamPowerWatts) : "--W";
                            _fan1RpmLabel.Text = string.Format("{0} RPM ↻", st.Fan1Rpm);
                            _fan2RpmLabel.Text = _hasDualFans ? (st.Fan2Rpm > 0 ? string.Format("{0} RPM •", st.Fan2Rpm) : "-- RPM") : "N/A (1-Fan Mode)";
                            UpdateTrayIcons(st.CpuTemp, st.GpuTemp);
                            if (_overlayForm != null && _showOverlay)
                                _overlayForm.UpdateMetrics(st, "Calib", _hasDualFans);
                        }
                    }))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null && !dlg.Result.WasCanceled)
                    {
                        FanCalibrationResult res = dlg.Result;
                        _hasDualFans = res.HasDualFans;
                        FanCurveProfile.ApuMinSpinPct = res.ApuMinSpinPct;
                        FanCurveProfile.SysMinSpinPct = res.SysMinSpinPct;
                        FanCurveProfile.ApuMaxRpm = res.ApuMaxRpm;
                        FanCurveProfile.SysMaxRpm = res.SysMaxRpm;
                        FanCurveProfile.LastCalibratedDate = !string.IsNullOrEmpty(res.CalibratedAt)
                            ? res.CalibratedAt
                            : DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

                        _lastAppliedApuPct = -1;
                        _lastAppliedSysPct = -1;
                        _lastAppliedEcMode = -1;
                        ApplyFanLayoutMode();
                        UpdateCalibrationBadge();
                        SaveSettings();
                        _driversMenu = CreateDriversAndToolsMenu();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fan Calibration failed:\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _hwWorkerPaused = false;
                if (_refreshTimer != null && timerWasEnabled)
                    _refreshTimer.Start();
                _hwWakeEvent.Set();
            }
        }

        private void ToggleDualFanMode()
        {
            _hasDualFans = !_hasDualFans;
            _lastAppliedApuPct = -1;
            _lastAppliedSysPct = -1;
            _lastAppliedEcMode = -1;
            ApplyFanLayoutMode();
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            RefreshStatus();
        }

        private void ApplyFanLayoutMode()
        {
            if (_curveCanvas != null)
            {
                if (!_hasDualFans && _curveCanvas.EditSysFan)
                {
                    _curveCanvas.EditSysFan = false;
                }
                _curveCanvas.ShowSecondaryCurve = _hasDualFans;
                _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.HasDualFans = _hasDualFans;
                _liveCanvas.Invalidate();
            }

            if (_tabApuCurveBtn != null && _tabSysCurveBtn != null && _tabLiveGraphBtn != null && _tabOsdConfigBtn != null)
            {
                if (_hasDualFans)
                {
                    _tabApuCurveBtn.Location = new Point(8, 8);
                    _tabApuCurveBtn.Size = new Size(228, 28);
                    _tabSysCurveBtn.Location = new Point(244, 8);
                    _tabSysCurveBtn.Size = new Size(228, 28);
                    _tabSysCurveBtn.Visible = true;
                    _tabLiveGraphBtn.Location = new Point(480, 8);
                    _tabLiveGraphBtn.Size = new Size(228, 28);
                    _tabOsdConfigBtn.Location = new Point(716, 8);
                    _tabOsdConfigBtn.Size = new Size(228, 28);
                }
                else
                {
                    _tabApuCurveBtn.Location = new Point(8, 8);
                    _tabApuCurveBtn.Size = new Size(304, 28);
                    _tabSysCurveBtn.Visible = false;
                    _tabLiveGraphBtn.Location = new Point(324, 8);
                    _tabLiveGraphBtn.Size = new Size(304, 28);
                    _tabOsdConfigBtn.Location = new Point(640, 8);
                    _tabOsdConfigBtn.Size = new Size(304, 28);
                }
            }

            if (_fan1Box != null && _fan2Box != null)
            {
                if (_hasDualFans)
                {
                    _fan1Box.Location = new Point(672, 8);
                    _fan1Box.Size = new Size(126, 70);
                    if (_fan1BoxTitle != null) { _fan1BoxTitle.Location = new Point(1, 2); _fan1BoxTitle.Size = new Size(124, 13); }
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Location = new Point(1, 15);
                        _fan1RpmLabel.Size = new Size(124, 33);
                        _fan1RpmLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                    }
                    _fan2Box.Location = new Point(804, 8);
                    _fan2Box.Size = new Size(126, 70);
                    if (_fan2BoxTitle != null) { _fan2BoxTitle.Location = new Point(1, 2); _fan2BoxTitle.Size = new Size(124, 13); }
                    if (_fan2RpmLabel != null)
                    {
                        _fan2RpmLabel.Location = new Point(1, 15);
                        _fan2RpmLabel.Size = new Size(124, 33);
                        _fan2RpmLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                    }
                    _fan2Box.Visible = true;
                }
                else
                {
                    _fan2Box.Visible = false;
                    _fan1Box.Location = new Point(672, 8);
                    _fan1Box.Size = new Size(258, 70);
                    if (_fan1BoxTitle != null) { _fan1BoxTitle.Location = new Point(1, 2); _fan1BoxTitle.Size = new Size(256, 13); }
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Location = new Point(1, 15);
                        _fan1RpmLabel.Size = new Size(256, 33);
                        _fan1RpmLabel.Font = new Font("Segoe UI", 14.0f, FontStyle.Bold);
                    }
                }
            }

            if (_fan2Slider != null) _fan2Slider.Enabled = _hasDualFans;
            if (_fan2AutoBtn != null) _fan2AutoBtn.Enabled = _hasDualFans;

            UpdateCurveTabStyles();
            UpdateCurveHint();
            UpdateCalibrationBadge();
        }

        private ContextMenu CreateDriversAndToolsMenu()
        {
            ContextMenu menu = new ContextMenu();
            string calMenuLabel = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Calibrate Fans (Not Yet Calibrated — Default 30% Min)..."
                : string.Format("Calibrate Fans (✓ Last: {0} • Min {1}%)...",
                    FanCurveProfile.LastCalibratedDate, FanCurveProfile.ApuMinSpinPct);
            menu.MenuItems.Add(calMenuLabel, (s, e) => RunGuiFanCalibration());
            string layoutLabel = _hasDualFans
                ? "Fan Layout: 2 Fans (APU + Sys) — Click for 1-Fan Mode"
                : "Fan Layout: 1 Fan (APU Only) — Click for 2-Fan Mode";
            menu.MenuItems.Add(layoutLabel, (s, e) => ToggleDualFanMode());
            menu.MenuItems.Add("-");
            string osdToggleLabel = _showOverlay ? "Hide CapFrameX-Style OSD Overlay" : "Show CapFrameX-Style OSD Overlay";
            menu.MenuItems.Add(osdToggleLabel, (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                else SetOverlayVisible(!_showOverlay);
            });
            string osdStyleLabel = _overlayVertical
                ? "OSD Layout: Vertical Stack — Click for Horizontal Bar"
                : "OSD Layout: Horizontal Bar — Click for Vertical Stack";
            menu.MenuItems.Add(osdStyleLabel, (s, e) => ToggleOverlayLayoutStyle());
            string osdLockLabel = _lockOverlay
                ? "Unlock OSD Overlay (Allow Mouse Drag)"
                : "Lock OSD Overlay (Click-Through Mode)";
            menu.MenuItems.Add(osdLockLabel, (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                else SetOverlayLocked(!_lockOverlay);
            });
            menu.MenuItems.Add("Configure OSD Overlay Studio...", (s, e) => OpenOsdConfigTab());
            menu.MenuItems.Add("-");
            string resetPeaksLabel = _sessionStats.SampleCount > 0
                ? string.Format("Reset Session Peaks (Max CPU {0}°C • iGPU {1}°C • NVMe {2}°C • {3}W)",
                    _sessionStats.MaxCpuTemp, _sessionStats.MaxGpuTemp, _sessionStats.MaxNvmeTemp, _sessionStats.MaxPowerWatts)
                : "Reset Session Peak (Max °C / W) Stats";
            menu.MenuItems.Add(resetPeaksLabel, (s, e) => ResetSessionPeaks());
            string exportCsvLabel = string.Format("📊 Export Session Telemetry to CSV ({0} samples logged)...", _sessionStats.LoggedRecordCount);
            menu.MenuItems.Add(exportCsvLabel, (s, e) => ExportSessionTelemetryToCsv());
            menu.MenuItems.Add("📋 Copy Active Fan Curve to Clipboard (Share Curve)...", (s, e) => CopyCurveToClipboard());
            menu.MenuItems.Add("📥 Import Fan Curve from Clipboard...", (s, e) => ImportCurveFromClipboard());
            string safeExitLabel = string.Format("Safety: Restore BIOS Auto Fan on Exit ({0})",
                _restoreAutoOnExit ? "✓ Enabled" : "Disabled");
            menu.MenuItems.Add(safeExitLabel, (s, e) =>
            {
                _restoreAutoOnExit = !_restoreAutoOnExit;
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _restoreAutoOnExit
                    ? "✓ Exit Safety enabled: fans restore to BIOS Auto on close"
                    : "ℹ Exit Safety disabled: custom/manual PWM persists after close";
            });
            string hotkeysLabel = string.Format("Global Hotkeys Ctrl+Alt+F9..F12 ({0})",
                _enableGlobalHotkeys ? "✓ Enabled" : "Disabled");
            menu.MenuItems.Add(hotkeysLabel, (s, e) =>
            {
                _enableGlobalHotkeys = !_enableGlobalHotkeys;
                UpdateGlobalHotkeysRegistration();
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _enableGlobalHotkeys
                    ? "✓ Global hotkeys enabled (Ctrl+Alt+F9..F12)"
                    : "ℹ Global hotkeys disabled";
            });
            string demoLabel = string.Format("Offline Demo / Simulation Mode ({0})", _demoMode ? "✓ Active" : "Disabled");
            menu.MenuItems.Add(demoLabel, (s, e) =>
            {
                _demoMode = !_demoMode;
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _demoMode
                    ? "ℹ Offline Demo Mode active (synthetic telemetry)"
                    : "✓ Hardware Mode active (direct EC registers)";
                RefreshStatus();
            });
            menu.MenuItems.Add("-");
            MenuItem profileSubMenu = new MenuItem("Hardware Profile & EC Target");
            bool isAuto = string.IsNullOrEmpty(_profileOverrideId) || _profileOverrideId.Equals("auto", StringComparison.OrdinalIgnoreCase);
            string autoTitle = string.Format("Auto-Detect: {0} (Active)", _sysInfo.MatchedProfile.DisplayName);
            MenuItem autoItem = new MenuItem(autoTitle, (s, e) => SetHardwareProfile("auto"));
            if (isAuto) autoItem.Checked = true;
            profileSubMenu.MenuItems.Add(autoItem);
            profileSubMenu.MenuItems.Add("-");
            foreach (DeviceProfile dp in DeviceProfile.KnownProfiles)
            {
                string pid = dp.Id;
                MenuItem dpItem = new MenuItem(dp.DisplayName, (s, e) => SetHardwareProfile(pid));
                if (!isAuto && _sysInfo.MatchedProfile.Id == pid) dpItem.Checked = true;
                profileSubMenu.MenuItems.Add(dpItem);
            }
            menu.MenuItems.Add(profileSubMenu);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Export Hardware & EC Probe Report (for HX 370 / New PCs)...", (s, e) => ExportHardwareProbeReport());
            menu.MenuItems.Add("🚀 Submit Hardware Profile to GitHub (1-Click)...", (s, e) => SubmitHardwareProfileOnline());
            menu.MenuItems.Add("Detected Hardware & Multi-Model Info...", (s, e) => ShowHardwareCompatibilityDialog());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("GMKtec Official Drivers & Manuals (K12 / Mini PCs)...", (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            menu.MenuItems.Add("GMKtec BIOS & Firmware Downloads...", (s, e) => OpenUrlSafe(URL_GMKTEC_FIRMWARE));
            menu.MenuItems.Add("AMD Software: Adrenalin & Chipset Drivers...", (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("About MiniFanControl v" + APP_VERSION + "...", (s, e) => ShowAboutDialog());
            return menu;
        }

        private static bool IsMouseOverControl(Control c)
        {
            if (c == null || !c.IsHandleCreated || !c.Visible) return false;
            Point pt = c.PointToClient(Cursor.Position);
            return c.ClientRectangle.Contains(pt);
        }

        private static bool IsBoxHovered(Control box, Control title, Control label)
        {
            return IsMouseOverControl(box) || IsMouseOverControl(title) || IsMouseOverControl(label);
        }

        private void SetHardwareProfile(string profileId)
        {
            _profileOverrideId = string.IsNullOrEmpty(profileId) ? "auto" : profileId;
            if (_profileOverrideId.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                _sysInfo.MatchedProfile = SystemHardwareInfo.MatchProfile(_sysInfo.ProductName, _sysInfo.BaseBoard, _sysInfo.CpuName);
            }
            else
            {
                DeviceProfile explicitProf = DeviceProfile.FindProfile(_profileOverrideId);
                if (explicitProf != null)
                {
                    _sysInfo.MatchedProfile = explicitProf;
                }
            }
            if (_hw != null) _hw.Profile = _sysInfo.MatchedProfile;
            if (_hwSubLabel != null)
            {
                string drvInfo = NativeInpOut.ActiveDriverName;
                if (_sysInfo.MatchedProfile.IsReadOnly)
                {
                    _hwSubLabel.Text = string.Format("EC Map: {0} • [SAFE READ-ONLY MODE] • Driver: {1} • Global\\Access_EC Mutex", _sysInfo.MatchedProfile.DisplayName, drvInfo);
                }
                else
                {
                    _hwSubLabel.Text = string.Format("EC Map: {0} • Driver: {1} • Global\\Access_EC Mutex • 64-bit", _sysInfo.MatchedProfile.DisplayName, drvInfo);
                }
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            _statusBarLabel.Text = string.Format("✓ Profile: {0}", _sysInfo.MatchedProfile.DisplayName);
            RefreshStatus();
        }

        private void SetRefreshInterval(int ms)
        {
            if (ms < 250) ms = 250;
            if (ms > 10000) ms = 10000;
            _refreshIntervalMs = ms;
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = _refreshIntervalMs;
            }
            if (_samplingCombo != null)
            {
                int[] intervals = new int[] { 250, 500, 1000, 1500, 2000, 3000 };
                for (int i = 0; i < intervals.Length; i++)
                {
                    if (intervals[i] == _refreshIntervalMs)
                    {
                        if (_samplingCombo.SelectedIndex != i) _samplingCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            _driversMenu = CreateDriversAndToolsMenu();
            SaveSettings();
            if (_statusBarLabel != null)
            {
                double hz = 1000.0 / ms;
                _statusBarLabel.Text = string.Format("? Data sampling rate set to {0} ms ({1:0.0} Hz / {2:0.0}s)", ms, hz, ms / 1000.0);
            }
        }

        private string GetSettingsPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string primary = Path.Combine(baseDir, "minifancontrol.cfg");
            string legacy = Path.Combine(baseDir, "k12-settings.cfg");

            // 1. If configuration file exists in the application directory (portable mode), use it
            if (File.Exists(primary)) return primary;
            if (File.Exists(legacy)) return legacy;

            // 2. Determine LocalAppData path (standard for Windows desktop applications)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDataDir = Path.Combine(localAppData, "MiniFanControl");
            string appDataConfig = Path.Combine(appDataDir, "minifancontrol.cfg");
            if (File.Exists(appDataConfig)) return appDataConfig;

            // 3. If running inside Program Files, write to LocalAppData to avoid UAC / permission failures
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            bool inProgramFiles = (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase));

            if (inProgramFiles)
            {
                try
                {
                    if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
                    return appDataConfig;
                }
                catch { }
            }

            return primary;
        }

        public static CurvePoint[] ParsePointList(string str)
        {
            List<CurvePoint> pts = new List<CurvePoint>();
            foreach (string tok in str.Split(';'))
            {
                string[] kv = tok.Split(':');
                int t, p;
                if (kv.Length == 2 && int.TryParse(kv[0], out t) && int.TryParse(kv[1], out p))
                {
                    pts.Add(new CurvePoint(
                        Math.Max(FanCurveCanvas.MIN_TEMP, Math.Min(FanCurveCanvas.MAX_TEMP, t)),
                        Math.Max(0, Math.Min(100, p))));
                }
            }
            return pts.ToArray();
        }

        public static string FormatPointList(CurvePoint[] pts)
        {
            List<string> list = new List<string>();
            if (pts != null)
            {
                foreach (CurvePoint pt in pts)
                    list.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", pt.Temp, pt.Pct));
            }
            return string.Join(";", list.ToArray());
        }

        private FanCurveProfile FindProfileByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (FanCurveProfile bp in _builtInProfiles)
            {
                if (bp.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return bp;
            }
            foreach (FanCurveProfile cp in _customProfiles)
            {
                if (cp.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cp;
            }
            return null;
        }

        public static bool TryParseHexOrDecByte(string s, out byte val)
        {
            val = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val);
            return byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return;
                string legacyCustomName = null;
                string apuModeName = null;
                string sysModeName = null;
                DeviceProfile prof = _sysInfo != null ? _sysInfo.MatchedProfile : DeviceProfile.KnownProfiles[0];

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("CustomProfile=", StringComparison.OrdinalIgnoreCase))
                    {
                        string payload = line.Substring("CustomProfile=".Length);
                        string[] segments = payload.Split('|');
                        if (segments.Length >= 2)
                        {
                            string name = segments[0].Trim();
                            CurvePoint[] apuPts = ParsePointList(segments[1]);
                            CurvePoint[] sysPts = segments.Length >= 3 ? ParsePointList(segments[2]) : FanCurveProfile.ClonePoints(apuPts);
                            if (!string.IsNullOrEmpty(name) && apuPts.Length >= 2)
                            {
                                if (sysPts.Length < 2) sysPts = FanCurveProfile.ClonePoints(apuPts);
                                _customProfiles.Add(new FanCurveProfile(name, false, -1, apuPts, sysPts));
                            }
                        }
                        continue;
                    }

                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    string k = parts[0].Trim();
                    string v = parts[1].Trim();
                    byte regByte;
                    int intVal;
                    if (k == "ShowCpuTray") _showCpuTray = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "ShowGpuTray") _showGpuTray = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "ShowOverlay") _showOverlay = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "LockOverlay") _lockOverlay = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OverlayVertical") _overlayVertical = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OverlayX" && int.TryParse(v, out intVal)) _overlayX = Math.Max(0, Math.Min(3800, intVal));
                    else if (k == "OverlayY" && int.TryParse(v, out intVal)) _overlayY = Math.Max(0, Math.Min(2100, intVal));
                    else if (k == "OsdScaleTier" && int.TryParse(v, out intVal)) _osdScaleTier = Math.Max(0, Math.Min(3, intVal));
                    else if (k == "OsdGraphMode" && int.TryParse(v, out intVal)) _osdGraphMode = Math.Max(0, Math.Min(3, intVal));
                    else if (k == "OsdGraphHeight" && int.TryParse(v, out intVal))
                    {
                        if (intVal <= 85) _osdGraphHeight = 75;
                        else if (intVal <= 120) _osdGraphHeight = 105;
                        else if (intVal <= 160) _osdGraphHeight = 140;
                        else _osdGraphHeight = 180;
                    }
                    else if (k == "OsdOpacityPct" && int.TryParse(v, out intVal)) _osdOpacityPct = Math.Max(35, Math.Min(100, intVal));
                    else if (k == "OsdMatchTheme") _osdMatchTheme = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowCpu") _osdShowCpu = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowGpu") _osdShowGpu = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowNvme") _osdShowNvme = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowPower") _osdShowPower = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowRam") _osdShowRam = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowFps") _osdShowFps = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowLows") _osdShowLows = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowGpuLoad") _osdShowGpuLoad = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowApuFan") _osdShowApuFan = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowSysFan") _osdShowSysFan = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowMode") _osdShowMode = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowPeaks") _osdShowPeaks = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowRadeon") _osdShowRadeon = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowDriverVer") _osdShowDriverVer = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowClocks") _osdShowClocks = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdSparklineBg") _osdSparklineBg = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowLiveTrend") _osdShowLiveTrend = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowFpsGraph") _osdShowFpsGraph = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdPlotFpsOnTrend") _osdPlotFpsOnTrend = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdDfGraphStyle") _osdDfGraphStyle = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdDfGraphFirst") _osdDfGraphFirst = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "RestoreAutoOnExit") _restoreAutoOnExit = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "AlwaysOnTop") _alwaysOnTop = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "EnableHotkeys") _enableGlobalHotkeys = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "HasDualFans") _hasDualFans = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "HardwareProfile" || k == "ProfileId")
                    {
                        _profileOverrideId = v;
                        if (!v.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            DeviceProfile explicitProf = DeviceProfile.FindProfile(v);
                            if (explicitProf != null)
                            {
                                prof = explicitProf;
                                if (_sysInfo != null) _sysInfo.MatchedProfile = explicitProf;
                                if (_hw != null) _hw.Profile = explicitProf;
                            }
                        }
                    }
                    else if (k == "RefreshIntervalMs" && int.TryParse(v, out intVal)) _refreshIntervalMs = Math.Max(250, Math.Min(10000, intVal));
                    else if (k == "ApuMinSpinPct" && int.TryParse(v, out intVal)) FanCurveProfile.ApuMinSpinPct = Math.Max(18, Math.Min(45, intVal));
                    else if (k == "SysMinSpinPct" && int.TryParse(v, out intVal)) FanCurveProfile.SysMinSpinPct = Math.Max(18, Math.Min(45, intVal));
                    else if (k == "ApuMaxRpm" && int.TryParse(v, out intVal)) FanCurveProfile.ApuMaxRpm = Math.Max(500, Math.Min(6000, intVal));
                    else if (k == "SysMaxRpm" && int.TryParse(v, out intVal)) FanCurveProfile.SysMaxRpm = Math.Max(0, Math.Min(6000, intVal));
                    else if (k == "LastCalibratedDate") FanCurveProfile.LastCalibratedDate = v;
                    else if (k == "Theme")
                    {
                        int t;
                        if (int.TryParse(v, out t) && t >= 0 && t <= 4)
                            _themePref = (ThemePreference)t;
                    }
                    else if (k == "ActiveCustom") legacyCustomName = v;
                    else if (k == "ApuMode") apuModeName = v;
                    else if (k == "SysMode") sysModeName = v;
                    else if (k == "RegModeStatus" && TryParseHexOrDecByte(v, out regByte)) prof.RegModeStatus = regByte;
                    else if (k == "RegModeCmd" && TryParseHexOrDecByte(v, out regByte)) prof.RegModeCmd = regByte;
                    else if (k == "RegFan1Ctrl" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Ctrl = regByte;
                    else if (k == "RegFan2Ctrl" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Ctrl = regByte;
                    else if (k == "RegFan1Hi" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Hi = regByte;
                    else if (k == "RegFan1Lo" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Lo = regByte;
                    else if (k == "RegFan2Hi" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Hi = regByte;
                    else if (k == "RegFan2Lo" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Lo = regByte;
                    else if (k == "RegCpuTemp" && TryParseHexOrDecByte(v, out regByte)) prof.RegCpuTemp = regByte;
                    else if (k == "RegGpuTemp" && TryParseHexOrDecByte(v, out regByte)) prof.RegGpuTemp = regByte;
                    else if (k == "EcAccess" || k == "AccessMethod")
                    {
                        if (v.Equals("PortIo", StringComparison.OrdinalIgnoreCase) || v.Equals("Port", StringComparison.OrdinalIgnoreCase) || v.Equals("0x62", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo62;
                        else if (v.Equals("PortIo6C", StringComparison.OrdinalIgnoreCase) || v.Equals("0x6C", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo6C;
                        else if (v.Equals("Mmio", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.Mmio;
                    }
                }

                FanCurveProfile apuProf = FindProfileByName(apuModeName) ?? FindProfileByName(legacyCustomName);
                FanCurveProfile sysProf = FindProfileByName(sysModeName) ?? FindProfileByName(legacyCustomName);
                if (apuProf != null) _apuProfile = apuProf;
                if (sysProf != null) _sysProfile = sysProf;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("ShowCpuTray=" + (_showCpuTray ? "1" : "0"));
                lines.Add("ShowGpuTray=" + (_showGpuTray ? "1" : "0"));
                lines.Add("ShowOverlay=" + (_showOverlay ? "1" : "0"));
                lines.Add("LockOverlay=" + (_lockOverlay ? "1" : "0"));
                lines.Add("OverlayVertical=" + (_overlayVertical ? "1" : "0"));
                lines.Add("OverlayX=" + _overlayX);
                lines.Add("OverlayY=" + _overlayY);
                lines.Add("OsdScaleTier=" + _osdScaleTier);
                lines.Add("OsdGraphMode=" + _osdGraphMode);
                lines.Add("OsdGraphHeight=" + _osdGraphHeight);
                lines.Add("OsdOpacityPct=" + _osdOpacityPct);
                lines.Add("OsdMatchTheme=" + (_osdMatchTheme ? "1" : "0"));
                lines.Add("OsdShowCpu=" + (_osdShowCpu ? "1" : "0"));
                lines.Add("OsdShowGpu=" + (_osdShowGpu ? "1" : "0"));
                lines.Add("OsdShowNvme=" + (_osdShowNvme ? "1" : "0"));
                lines.Add("OsdShowPower=" + (_osdShowPower ? "1" : "0"));
                lines.Add("OsdShowRam=" + (_osdShowRam ? "1" : "0"));
                lines.Add("OsdShowFps=" + (_osdShowFps ? "1" : "0"));
                lines.Add("OsdShowLows=" + (_osdShowLows ? "1" : "0"));
                lines.Add("OsdShowGpuLoad=" + (_osdShowGpuLoad ? "1" : "0"));
                lines.Add("OsdShowApuFan=" + (_osdShowApuFan ? "1" : "0"));
                lines.Add("OsdShowSysFan=" + (_osdShowSysFan ? "1" : "0"));
                lines.Add("OsdShowMode=" + (_osdShowMode ? "1" : "0"));
                lines.Add("OsdShowPeaks=" + (_osdShowPeaks ? "1" : "0"));
                lines.Add("OsdShowRadeon=" + (_osdShowRadeon ? "1" : "0"));
                lines.Add("OsdShowDriverVer=" + (_osdShowDriverVer ? "1" : "0"));
                lines.Add("OsdShowClocks=" + (_osdShowClocks ? "1" : "0"));
                lines.Add("OsdSparklineBg=" + (_osdSparklineBg ? "1" : "0"));
                lines.Add("OsdShowLiveTrend=" + (_osdShowLiveTrend ? "1" : "0"));
                lines.Add("OsdShowFpsGraph=" + (_osdShowFpsGraph ? "1" : "0"));
                lines.Add("OsdPlotFpsOnTrend=" + (_osdPlotFpsOnTrend ? "1" : "0"));
                lines.Add("OsdDfGraphStyle=" + (_osdDfGraphStyle ? "1" : "0"));
                lines.Add("OsdDfGraphFirst=" + (_osdDfGraphFirst ? "1" : "0"));
                lines.Add("RestoreAutoOnExit=" + (_restoreAutoOnExit ? "1" : "0"));
                lines.Add("AlwaysOnTop=" + (_alwaysOnTop ? "1" : "0"));
                lines.Add("EnableHotkeys=" + (_enableGlobalHotkeys ? "1" : "0"));
                lines.Add("HasDualFans=" + (_hasDualFans ? "1" : "0"));
                lines.Add("HardwareProfile=" + (_profileOverrideId ?? "auto"));
                lines.Add("RefreshIntervalMs=" + _refreshIntervalMs);
                lines.Add("ApuMinSpinPct=" + FanCurveProfile.ApuMinSpinPct);
                lines.Add("SysMinSpinPct=" + FanCurveProfile.SysMinSpinPct);
                lines.Add("ApuMaxRpm=" + FanCurveProfile.ApuMaxRpm);
                lines.Add("SysMaxRpm=" + FanCurveProfile.SysMaxRpm);
                if (!string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate))
                    lines.Add("LastCalibratedDate=" + FanCurveProfile.LastCalibratedDate);
                lines.Add("Theme=" + (int)_themePref);
                lines.Add("ApuMode=" + (_apuProfile != null ? _apuProfile.Name : "Balanced"));
                lines.Add("SysMode=" + (_sysProfile != null ? _sysProfile.Name : "Balanced"));

                foreach (FanCurveProfile cp in _customProfiles)
                {
                    lines.Add("CustomProfile=" + cp.Name.Replace("|", "/") + "|" + FormatPointList(cp.ApuPoints) + "|" + FormatPointList(cp.SysPoints));
                }

                string path = GetSettingsPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllLines(path, lines.ToArray());
                RebuildTrayContextMenus();
            }
            catch { }
        }

        private void DetectSystemTheme(out bool appDark, out bool taskbarDark)
        {
            if (_themePref == ThemePreference.Dark || _themePref == ThemePreference.Radeon || _themePref == ThemePreference.Afterburner)
            {
                appDark = true;
                taskbarDark = true;
                return;
            }
            if (_themePref == ThemePreference.Light)
            {
                appDark = false;
                taskbarDark = false;
                return;
            }

            bool sysAppsDark = true;
            bool sysTaskbarDark = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object appsVal = key.GetValue("AppsUseLightTheme");
                        if (appsVal != null)
                            sysAppsDark = Convert.ToInt32(appsVal) == 0;

                        object sysVal = key.GetValue("SystemUsesLightTheme");
                        if (sysVal != null)
                            sysTaskbarDark = Convert.ToInt32(sysVal) == 0;
                        else
                            sysTaskbarDark = sysAppsDark;
                    }
                }
            }
            catch { }

            appDark = sysAppsDark;
            taskbarDark = sysTaskbarDark;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            CheckAndApplyThemeChanges();
        }

        private void CheckAndApplyThemeChanges()
        {
            bool newAppDark, newTaskbarDark;
            DetectSystemTheme(out newAppDark, out newTaskbarDark);
            if (newAppDark != _isDarkMode || newTaskbarDark != _isTaskbarDark)
            {
                _isDarkMode = newAppDark;
                _isTaskbarDark = newTaskbarDark;
                _lastCpuIconKey = int.MinValue;
                _lastGpuIconKey = int.MinValue;
                ApplyThemeToControls();
                RefreshStatus();
            }
        }

        private void StartRampIndication(string actionDescription, int targetFan)
        {
            _pendingActionText = actionDescription;
            _rampTargetFan = targetFan;
            _rampUntil = DateTime.UtcNow.AddSeconds(4.5);
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = FAST_REFRESH_MS;
            }
            RefreshStatus();
        }

        private ContextMenuStrip CreateTrayContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Show Window", null, (s, e) => RestoreFromTray());
            menu.Items.Add(
                _showOverlay ? "Hide CapFrameX OSD Overlay" : "Show CapFrameX OSD Overlay",
                null,
                (s, e) =>
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                });
            menu.Items.Add(
                _lockOverlay ? "Unlock OSD Overlay (Allow Drag)" : "Lock OSD Overlay (Click-Through)",
                null,
                (s, e) =>
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Mode: Balanced", null, (s, e) => ApplyMode(0));
            menu.Items.Add("Mode: Performance", null, (s, e) => ApplyMode(1));
            menu.Items.Add("Mode: Silent", null, (s, e) => ApplyMode(2));
            if (_customProfiles.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                foreach (FanCurveProfile cp in _customProfiles)
                {
                    FanCurveProfile captured = cp;
                    menu.Items.Add("Custom: " + captured.Name, null, (s, e) => ActivateCustomProfile(captured));
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem linksItem = new ToolStripMenuItem("Tools && Drivers");
            linksItem.DropDownItems.Add("Calibrate Fans...", null, (s, e) => RunGuiFanCalibration());
            linksItem.DropDownItems.Add("Export Hardware && EC Probe Report...", null, (s, e) => ExportHardwareProbeReport());
            linksItem.DropDownItems.Add("Detected Hardware && Multi-Model Info...", null, (s, e) => ShowHardwareCompatibilityDialog());
            linksItem.DropDownItems.Add(new ToolStripSeparator());
            linksItem.DropDownItems.Add("GMKtec Official Drivers && Manuals...", null, (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            linksItem.DropDownItems.Add("GMKtec BIOS && Firmware Updates...", null, (s, e) => OpenUrlSafe(URL_GMKTEC_FIRMWARE));
            linksItem.DropDownItems.Add("AMD Software: Adrenalin && Chipset Drivers...", null, (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            linksItem.DropDownItems.Add(new ToolStripSeparator());
            linksItem.DropDownItems.Add("About MiniFanControl v" + APP_VERSION + "...", null, (s, e) => ShowAboutDialog());
            menu.Items.Add(linksItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Reset to Auto", null, (s, e) => ApplyAutoAll());
            menu.Items.Add("Exit", null, (s, e) =>
            {
                if (_cpuTrayIcon != null) _cpuTrayIcon.Visible = false;
                if (_gpuTrayIcon != null) _gpuTrayIcon.Visible = false;
                Close();
            });
            return menu;
        }

        private void RebuildTrayContextMenus()
        {
            if (_cpuTrayIcon != null) _cpuTrayIcon.ContextMenuStrip = CreateTrayContextMenu();
            if (_gpuTrayIcon != null) _gpuTrayIcon.ContextMenuStrip = CreateTrayContextMenu();
        }

        private void InitTrayIcons()
        {
            _cpuTrayIcon = new NotifyIcon
            {
                Text = "CPU Temperature - MiniFanControl",
                ContextMenuStrip = CreateTrayContextMenu(),
                Visible = _showCpuTray
            };
            _cpuTrayIcon.DoubleClick += (s, e) => RestoreFromTray();

            _gpuTrayIcon = new NotifyIcon
            {
                Text = "iGPU Temperature - MiniFanControl",
                ContextMenuStrip = CreateTrayContextMenu(),
                Visible = _showGpuTray
            };
            _gpuTrayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private Icon RenderHighContrastTempIcon(int temp, bool darkBackground, bool isGpu, out IntPtr rawHandle)
        {
            Color bg = darkBackground ? Color.Black : Color.White;
            Color fg = darkBackground ? Color.White : Color.Black;
            Color barColor = isGpu
                ? (darkBackground ? Color.FromArgb(0x22, 0xC5, 0x5E) : Color.FromArgb(0x15, 0x80, 0x3D))
                : (darkBackground ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x1D, 0x4E, 0xD8));

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                using (Brush barBrush = new SolidBrush(barColor))
                {
                    g.FillRectangle(barBrush, 0, 29, 32, 3);
                }

                string label = temp > 0 ? temp.ToString() : "--";
                float fontSize = label.Length >= 3 ? 15f : 21.5f;

                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(fg))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString(label, font, textBrush, new RectangleF(-1f, -1f, 34f, 30f), sf);
                }

                rawHandle = bmp.GetHicon();
                return (Icon)Icon.FromHandle(rawHandle).Clone();
            }
        }

        private void UpdateTrayIcons(int cpuTemp, int gpuTemp)
        {
            int cpuKey = (cpuTemp & 0xFFFF) | (_isTaskbarDark ? 0x10000 : 0);
            if (cpuKey != _lastCpuIconKey || _cpuIconHandle == IntPtr.Zero)
            {
                _lastCpuIconKey = cpuKey;
                IntPtr newCpuHandle;
                Icon cpuIcon = RenderHighContrastTempIcon(cpuTemp, _isTaskbarDark, false, out newCpuHandle);
                if (_cpuTrayIcon != null)
                {
                    Icon oldIcon = _cpuTrayIcon.Icon;
                    _cpuTrayIcon.Icon = cpuIcon;
                    if (oldIcon != null)
                    {
                        try { oldIcon.Dispose(); } catch { }
                    }
                }
                else if (cpuIcon != null)
                {
                    try { cpuIcon.Dispose(); } catch { }
                }
                if (_cpuIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_cpuIconHandle);
                }
                _cpuIconHandle = newCpuHandle;
            }

            int gpuKey = (gpuTemp & 0xFFFF) | (_isTaskbarDark ? 0x10000 : 0);
            if (gpuKey != _lastGpuIconKey || _gpuIconHandle == IntPtr.Zero)
            {
                _lastGpuIconKey = gpuKey;
                IntPtr newGpuHandle;
                Icon gpuIcon = RenderHighContrastTempIcon(gpuTemp, _isTaskbarDark, true, out newGpuHandle);
                if (_gpuTrayIcon != null)
                {
                    Icon oldIcon = _gpuTrayIcon.Icon;
                    _gpuTrayIcon.Icon = gpuIcon;
                    if (oldIcon != null)
                    {
                        try { oldIcon.Dispose(); } catch { }
                    }
                }
                else if (gpuIcon != null)
                {
                    try { gpuIcon.Dispose(); } catch { }
                }
                if (_gpuIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_gpuIconHandle);
                }
                _gpuIconHandle = newGpuHandle;
            }

            if (_cpuTrayIcon != null && _cpuTrayIcon.Visible != _showCpuTray)
                _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null && _gpuTrayIcon.Visible != _showGpuTray)
                _gpuTrayIcon.Visible = _showGpuTray;
        }

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

            Panel card = new Panel
            {
                Location = new Point(14, top + 20),
                Size = new Size(ClientSize.Width - 28, height)
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
            if (c == _fan1Box || c == _fan1BoxTitle || c == _fan1RpmLabel) return 4;
            if (c == _fan2Box || c == _fan2BoxTitle || c == _fan2RpmLabel) return 5;
            return 0;
        }

        private void OnToolTipPopup(object sender, PopupEventArgs e)
        {
            string text = _uiToolTip != null ? _uiToolTip.GetToolTip(e.AssociatedControl) : "";
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
            using (Pen borderPen = new Pen(border, 1.5f))
            {
                g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

            int chartKind = GetTooltipChartKind(e.AssociatedControl);
            int textHeight = bounds.Height - (chartKind > 0 ? 64 : 10);
            Rectangle textRect = new Rectangle(bounds.X + 10, bounds.Y + 8, bounds.Width - 20, Math.Max(20, textHeight));
            TextRenderer.DrawText(g, e.ToolTipText, ToolTipBodyFont, textRect, fg,
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            if (chartKind > 0)
            {
                Rectangle chartRect = new Rectangle(bounds.X + 10, bounds.Bottom - 54, bounds.Width - 20, 46);
                List<int> samples = chartKind == 1 ? _sessionStats.CpuHistory
                    : (chartKind == 2 ? _sessionStats.GpuHistory
                    : (chartKind == 6 ? _sessionStats.NvmeHistory
                    : (chartKind == 3 ? _sessionStats.PowerHistory
                    : (chartKind == 7 ? _sessionStats.RamPowerHistory
                    : (chartKind == 4 ? _sessionStats.Fan1RpmHistory : _sessionStats.Fan2RpmHistory)))));
                string unit = (chartKind == 3 || chartKind == 7) ? "W" : (chartKind >= 4 && chartKind != 6 ? " RPM" : "°C");
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
                DrawMiniSparklineChart(g, chartRect, samples, lineCol, unit, true, chartKind == 7);
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

        private void PaintMetricBoxSparkline(Graphics g, Panel box, List<int> samples, int chartKind)
        {
            if (box == null) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
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
            if (samples != null && samples.Count > 0)
            {
                Rectangle sparkRect = new Rectangle(1, 49, box.ClientSize.Width - 2, Math.Max(8, box.ClientSize.Height - 51));
                DrawMiniSparklineChart(g, sparkRect, samples, lineCol, "", false, chartKind == 7);
            }
            if (_showingLiveTelemetry && _highlightedMetric == chartKind)
            {
                using (Pen activePen = new Pen(lineCol, 2f))
                {
                    g.DrawRectangle(activePen, 1, 1, box.ClientSize.Width - 2, box.ClientSize.Height - 2);
                }
            }
        }

        private void SetGraphViewMode(bool showLive, int highlightMetric)
        {
            _showingOsdConfig = false;
            if (_osdConfigPanel != null) _osdConfigPanel.Visible = false;
            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] != null) _modeButtons[i].Visible = true;
            }
            if (_customLabel != null) _customLabel.Visible = true;
            if (_customModeCombo != null) _customModeCombo.Visible = true;
            if (_activateCustomBtn != null) _activateCustomBtn.Visible = true;
            if (_addCustomBtn != null) _addCustomBtn.Visible = true;
            if (_deleteCustomBtn != null) _deleteCustomBtn.Visible = true;

            _showingLiveTelemetry = showLive;
            if (showLive)
            {
                _highlightedMetric = highlightMetric > 0 ? highlightMetric : 1;
            }
            else
            {
                _highlightedMetric = 0;
            }

            if (_curveCanvas != null) _curveCanvas.Visible = !showLive;
            if (_liveCanvas != null)
            {
                _liveCanvas.HighlightMetric = _highlightedMetric;
                _liveCanvas.Visible = showLive;
                if (showLive) _liveCanvas.Invalidate();
            }

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            UpdateCurveHint();

            if (_cpuTempBox != null) _cpuTempBox.Invalidate();
            if (_gpuTempBox != null) _gpuTempBox.Invalidate();
            if (_nvmeTempBox != null) _nvmeTempBox.Invalidate();
            if (_pwrBox != null) _pwrBox.Invalidate();
            if (_ramPowerBox != null) _ramPowerBox.Invalidate();
            if (_fan1Box != null) _fan1Box.Invalidate();
            if (_fan2Box != null) _fan2Box.Invalidate();
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
                InitialDelay = 200,
                ReshowDelay = 100,
                ShowAlways = true,
                OwnerDraw = true
            };
            _uiToolTip.Popup += OnToolTipPopup;
            _uiToolTip.Draw += OnToolTipDraw;

            // 0. Dedicated Top Header Row (Logo, Model Info, Drivers Menu, Theme Toggle)
            _headerLogoBox = new PictureBox
            {
                Location = new Point(14, 8),
                Size = new Size(46, 46),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppLogoRenderer.RenderConcept(1, 192),
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
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(68, 10),
                Size = new Size(580, 22),
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
                Location = new Point(68, 32),
                Size = new Size(620, 18),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwSubLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwSubLabel);

            _driversMenu = CreateDriversAndToolsMenu();

            _driversMenuBtn = new Button
            {
                Text = "Tools && Drivers ▾",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(690, 14),
                Size = new Size(136, 28),
                Cursor = Cursors.Hand
            };
            _driversMenuBtn.FlatAppearance.BorderSize = 0;
            _driversMenuBtn.Click += (s, e) =>
            {
                _driversMenu.Show(_driversMenuBtn, new Point(0, _driversMenuBtn.Height));
            };
            Controls.Add(_driversMenuBtn);

            _themeToggleBtn = new Button
            {
                Text = GetThemeButtonLabel(),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(834, 14),
                Size = new Size(132, 28),
                Cursor = Cursors.Hand
            };
            _themeToggleBtn.FlatAppearance.BorderSize = 0;
            _themeToggleBtn.Click += (s, e) => CycleThemePreference();
            Controls.Add(_themeToggleBtn);

            // 1. Hardware & Live Telemetry Section (top = 60, card = 80..218, height = 138)
            Panel statusCard = CreateSectionCard("System Telemetry & Live Hardware Sensors", 60, 138);

            _cpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(12, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _cpuBoxTitle = new Label
            {
                Text = "CPU TEMP",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _cpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _cpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _cpuTempBox, _sessionStats.CpuHistory, 1);
            _cpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuTempLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuTempBox.Controls.Add(_cpuBoxTitle);
            _cpuTempBox.Controls.Add(_cpuTempLabel);
            statusCard.Controls.Add(_cpuTempBox);

            string cpuTip = "APU / CPU Package Temperature (EC Register 0x70 • Tctl/Tdie)\r\nLeft-click to highlight CPU Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_cpuTempBox, cpuTip);
            _uiToolTip.SetToolTip(_cpuBoxTitle, cpuTip);
            _uiToolTip.SetToolTip(_cpuTempLabel, cpuTip);

            _gpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(144, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _gpuBoxTitle = new Label
            {
                Text = "iGPU TEMP",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _gpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _gpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _gpuTempBox, _sessionStats.GpuHistory, 2);
            _gpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuTempLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuTempBox.Controls.Add(_gpuBoxTitle);
            _gpuTempBox.Controls.Add(_gpuTempLabel);
            statusCard.Controls.Add(_gpuTempBox);

            string gpuTip = "Integrated Radeon Graphics Temperature (AMD ADL2 PMLog GFX/SoC Sensor)\r\nLeft-click to highlight iGPU Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_gpuTempBox, gpuTip);
            _uiToolTip.SetToolTip(_gpuBoxTitle, gpuTip);
            _uiToolTip.SetToolTip(_gpuTempLabel, gpuTip);

            _nvmeTempBox = new DoubleBufferedPanel
            {
                Location = new Point(276, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _nvmeBoxTitle = new Label
            {
                Text = "NVMe TEMP",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _nvmeTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _nvmeTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _nvmeTempBox, _sessionStats.NvmeHistory, 6);
            _nvmeTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(6, e);
            _nvmeBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(6, e);
            _nvmeTempLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(6, e);
            _nvmeTempBox.Controls.Add(_nvmeBoxTitle);
            _nvmeTempBox.Controls.Add(_nvmeTempLabel);
            statusCard.Controls.Add(_nvmeTempBox);

            string nvmeTip = "NVMe Storage Temperature & Health\r\nLeft-click to highlight NVMe Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_nvmeTempBox, nvmeTip);
            _uiToolTip.SetToolTip(_nvmeBoxTitle, nvmeTip);
            _uiToolTip.SetToolTip(_nvmeTempLabel, nvmeTip);

            _pwrBox = new DoubleBufferedPanel
            {
                Location = new Point(408, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _pwrBoxTitle = new Label
            {
                Text = "POWER ⓘ",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _pwrLabel = new Label
            {
                Text = "--W",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _pwrBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _pwrBox, _sessionStats.PowerHistory, 3);
            _pwrBox.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrBox.Controls.Add(_pwrBoxTitle);
            _pwrBox.Controls.Add(_pwrLabel);
            statusCard.Controls.Add(_pwrBox);

            _ramPowerBox = new DoubleBufferedPanel
            {
                Location = new Point(540, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _ramBoxTitle = new Label
            {
                Text = "RAM POWER",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _ramPowerLabel = new Label
            {
                Text = "--W",
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _ramPowerBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _ramPowerBox, _sessionStats.RamPowerHistory, 7);
            _ramPowerBox.MouseUp += (s, e) => OnMetricBoxMouseUp(7, e);
            _ramBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(7, e);
            _ramPowerLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(7, e);
            _ramPowerBox.Controls.Add(_ramBoxTitle);
            _ramPowerBox.Controls.Add(_ramPowerLabel);
            statusCard.Controls.Add(_ramPowerBox);

            _fan1Box = new DoubleBufferedPanel
            {
                Location = new Point(672, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan1BoxTitle = new Label
            {
                Text = "APU FAN",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan1RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan1Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan1Box, _sessionStats.Fan1RpmHistory, 4);
            _fan1Box.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1BoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1RpmLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1Box.Controls.Add(_fan1BoxTitle);
            _fan1Box.Controls.Add(_fan1RpmLabel);
            statusCard.Controls.Add(_fan1Box);

            string fan1Tip = "APU / CPU Fan Tachometer\r\nLeft-click to highlight APU Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan1Box, fan1Tip);
            _uiToolTip.SetToolTip(_fan1BoxTitle, fan1Tip);
            _uiToolTip.SetToolTip(_fan1RpmLabel, fan1Tip);

            _fan2Box = new DoubleBufferedPanel
            {
                Location = new Point(804, 8),
                Size = new Size(126, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan2BoxTitle = new Label
            {
                Text = "SYS FAN",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 2),
                Size = new Size(124, 13),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan2RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Location = new Point(1, 15),
                Size = new Size(124, 33),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan2Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan2Box, _sessionStats.Fan2RpmHistory, 5);
            _fan2Box.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2BoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2RpmLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2Box.Controls.Add(_fan2BoxTitle);
            _fan2Box.Controls.Add(_fan2RpmLabel);
            statusCard.Controls.Add(_fan2Box);

            string fan2Tip = "System RAM / NVMe Fan Tachometer\r\nLeft-click to highlight Sys Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan2Box, fan2Tip);
            _uiToolTip.SetToolTip(_fan2BoxTitle, fan2Tip);
            _uiToolTip.SetToolTip(_fan2RpmLabel, fan2Tip);

            Action updateCpuTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatCpuTooltip(_sessionStats);
                    _lastCpuTooltipText = tip;
                    _uiToolTip.SetToolTip(_cpuTempBox, tip);
                    _uiToolTip.SetToolTip(_cpuBoxTitle, tip);
                    _uiToolTip.SetToolTip(_cpuTempLabel, tip);
                }
            };
            _cpuTempBox.MouseEnter += (s, e) => updateCpuTip();
            _cpuBoxTitle.MouseEnter += (s, e) => updateCpuTip();
            _cpuTempLabel.MouseEnter += (s, e) => updateCpuTip();

            Action updateGpuTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatIgpuTooltip(_sessionStats);
                    _lastIgpuTooltipText = tip;
                    _uiToolTip.SetToolTip(_gpuTempBox, tip);
                    _uiToolTip.SetToolTip(_gpuBoxTitle, tip);
                    _uiToolTip.SetToolTip(_gpuTempLabel, tip);
                }
            };
            _gpuTempBox.MouseEnter += (s, e) => updateGpuTip();
            _gpuBoxTitle.MouseEnter += (s, e) => updateGpuTip();
            _gpuTempLabel.MouseEnter += (s, e) => updateGpuTip();

            Action updateNvmeTip = () =>
            {
                if (_uiToolTip != null)
                {
                    string tip = _lastStatus.FormatNvmeTooltip(_sessionStats);
                    _lastNvmeTooltipText = tip;
                    _uiToolTip.SetToolTip(_nvmeTempBox, tip);
                    _uiToolTip.SetToolTip(_nvmeBoxTitle, tip);
                    _uiToolTip.SetToolTip(_nvmeTempLabel, tip);
                }
            };
            _nvmeTempBox.MouseEnter += (s, e) => updateNvmeTip();
            _nvmeBoxTitle.MouseEnter += (s, e) => updateNvmeTip();
            _nvmeTempLabel.MouseEnter += (s, e) => updateNvmeTip();

            Action updatePwrTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatPowerTooltip(_sessionStats);
                    _lastPowerTooltipText = tip;
                    _uiToolTip.SetToolTip(_pwrBox, tip);
                    _uiToolTip.SetToolTip(_pwrBoxTitle, tip);
                    _uiToolTip.SetToolTip(_pwrLabel, tip);
                }
            };
            _pwrBox.MouseEnter += (s, e) => updatePwrTip();
            _pwrBoxTitle.MouseEnter += (s, e) => updatePwrTip();
            _pwrLabel.MouseEnter += (s, e) => updatePwrTip();

            Action updateRamPowerTip = () =>
            {
                if (_uiToolTip != null)
                {
                    string tip = _lastStatus.FormatRamTooltip(_sessionStats);
                    _lastRamTooltipText = tip;
                    _uiToolTip.SetToolTip(_ramPowerBox, tip);
                    _uiToolTip.SetToolTip(_ramBoxTitle, tip);
                    _uiToolTip.SetToolTip(_ramPowerLabel, tip);
                }
            };
            _ramPowerBox.MouseEnter += (s, e) => updateRamPowerTip();
            _ramBoxTitle.MouseEnter += (s, e) => updateRamPowerTip();
            _ramPowerLabel.MouseEnter += (s, e) => updateRamPowerTip();

            Action updateFan1Tip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm, _hw != null ? _hw.Profile : null);
                    _lastApuFanTooltipText = tip;
                    _uiToolTip.SetToolTip(_fan1Box, tip);
                    _uiToolTip.SetToolTip(_fan1BoxTitle, tip);
                    _uiToolTip.SetToolTip(_fan1RpmLabel, tip);
                }
            };
            _fan1Box.MouseEnter += (s, e) => updateFan1Tip();
            _fan1BoxTitle.MouseEnter += (s, e) => updateFan1Tip();
            _fan1RpmLabel.MouseEnter += (s, e) => updateFan1Tip();

            Action updateFan2Tip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans, _hw != null ? _hw.Profile : null);
                    _lastSysFanTooltipText = tip;
                    _uiToolTip.SetToolTip(_fan2Box, tip);
                    _uiToolTip.SetToolTip(_fan2BoxTitle, tip);
                    _uiToolTip.SetToolTip(_fan2RpmLabel, tip);
                }
            };
            _fan2Box.MouseEnter += (s, e) => updateFan2Tip();
            _fan2BoxTitle.MouseEnter += (s, e) => updateFan2Tip();
            _fan2RpmLabel.MouseEnter += (s, e) => updateFan2Tip();

            // Row 2: Secondary Telemetry Row (System RAM & Installed NVMe Drive)
            _ramCard = new DoubleBufferedPanel
            {
                Location = new Point(12, 84),
                Size = new Size(926, 25),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _ramLabel = new Label
            {
                Text = "🧠 RAM: Initializing...",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(6, 3),
                Size = new Size(914, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            _ramCard.Controls.Add(_ramLabel);
            statusCard.Controls.Add(_ramCard);

            string ramTip = "System Physical Memory & Storage Drive Status\r\nLive RAM allocation, memory load %, and installed drive health.";
            _uiToolTip.SetToolTip(_ramCard, ramTip);
            _uiToolTip.SetToolTip(_ramLabel, ramTip);

            Action updateRamTip = () =>
            {
                if (_uiToolTip != null)
                {
                    string tip = _lastStatus.FormatRamTooltip(_sessionStats);
                    _lastRamTooltipText = tip;
                    _uiToolTip.SetToolTip(_ramCard, tip);
                    _uiToolTip.SetToolTip(_ramLabel, tip);
                }
            };
            _ramCard.MouseEnter += (s, e) => updateRamTip();
            _ramLabel.MouseEnter += (s, e) => updateRamTip();

            // Row 3: Active Fan Modes & Duty Status
            AddStaticLabel(statusCard, "Mode:", 12, 114, 44, 20);
            _modeLabel = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                Location = new Point(58, 113),
                Size = new Size(250, 22)
            };
            statusCard.Controls.Add(_modeLabel);

            AddStaticLabel(statusCard, "APU Fan:", 330, 114, 64, 20);
            _pct1Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Location = new Point(396, 114),
                Size = new Size(220, 20)
            };
            statusCard.Controls.Add(_pct1Label);

            AddStaticLabel(statusCard, "Sys Fan:", 640, 114, 62, 20);
            _pct2Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Location = new Point(704, 114),
                Size = new Size(230, 20)
            };
            statusCard.Controls.Add(_pct2Label);

            // 2. Modes & Fan Curve Section (top = 226, card = 246..686, height = 440)
            Panel modeCard = CreateSectionCard("Modes, Fan Curves & Live Telemetry Graph", 226, 440);

            // Row 1 (y=8): Tab Selector Row (APU Fan | Sys Fan | 📈 Live Trend | 🖥 OSD Studio)
            _tabApuCurveBtn = new Button
            {
                Text = "● APU: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(228, 28),
                Cursor = Cursors.Hand
            };
            _tabApuCurveBtn.FlatAppearance.BorderSize = 0;
            _tabApuCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = false;
                SetGraphViewMode(false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabApuCurveBtn);

            _tabSysCurveBtn = new Button
            {
                Text = "● Sys: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(244, 8),
                Size = new Size(228, 28),
                Cursor = Cursors.Hand
            };
            _tabSysCurveBtn.FlatAppearance.BorderSize = 0;
            _tabSysCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = true;
                SetGraphViewMode(false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabSysCurveBtn);

            _tabLiveGraphBtn = new Button
            {
                Text = "● Live Trend",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(480, 8),
                Size = new Size(228, 28),
                Cursor = Cursors.Hand
            };
            _tabLiveGraphBtn.FlatAppearance.BorderSize = 0;
            _tabLiveGraphBtn.Click += (s, e) =>
            {
                SetGraphViewMode(!_showingLiveTelemetry, _highlightedMetric > 0 ? _highlightedMetric : 1);
            };
            modeCard.Controls.Add(_tabLiveGraphBtn);

            _tabOsdConfigBtn = new Button
            {
                Text = "● OSD Studio",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(716, 8),
                Size = new Size(228, 28),
                Cursor = Cursors.Hand
            };
            _tabOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _tabOsdConfigBtn.Click += (s, e) =>
            {
                SetOsdConfigViewMode(!_showingOsdConfig);
            };
            modeCard.Controls.Add(_tabOsdConfigBtn);

            // Row 2 (y=42): Built-in Mode Buttons for the currently selected fan tab
            string[] modeTitles = { "⚖ Balanced", "⚡ Performance", "🌙 Silent" };
            for (int i = 0; i < 3; i++)
            {
                int code = i;
                Button btn = new Button
                {
                    Text = modeTitles[i],
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                    Location = new Point(8 + i * 315, 42),
                    Size = new Size(305, 30),
                    Padding = new Padding(0),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => ApplyMode(code);
                modeCard.Controls.Add(btn);
                _modeButtons[i] = btn;
            }

            // Row 3 (y=76): Custom Modes Row for the currently selected fan tab
            _customLabel = AddStaticLabel(modeCard, "Custom:", 8, 79, 54, 22);

            _customModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(66, 77),
                Size = new Size(530, 24)
            };
            _customModeCombo.SelectedIndexChanged += OnCustomComboSelected;
            modeCard.Controls.Add(_customModeCombo);

            _activateCustomBtn = new Button
            {
                Text = "Use Curve",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(608, 76),
                Size = new Size(104, 26),
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

            _addCustomBtn = new Button
            {
                Text = "+ New",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(720, 76),
                Size = new Size(104, 26),
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
                Location = new Point(832, 76),
                Size = new Size(104, 26),
                Cursor = Cursors.Hand
            };
            _deleteCustomBtn.FlatAppearance.BorderSize = 0;
            _deleteCustomBtn.Click += (s, e) => DeleteSelectedCustomMode();
            modeCard.Controls.Add(_deleteCustomBtn);

            // Row 4 (y=108): Large Interactive Fan Curve Graph + Combined Live Telemetry Graph (936 x 302)
            _curveCanvas = new FanCurveCanvas
            {
                Location = new Point(8, 108),
                Size = new Size(936, 302),
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
                        _sysProfile = existing;
                        _sysManualOverride = false;
                        _curveCanvas.SysProfile = _sysProfile;
                    }
                    else
                    {
                        _apuProfile = existing;
                        _apuManualOverride = false;
                        _curveCanvas.ApuProfile = _apuProfile;
                    }
                    PopulateCustomCombo();
                }
            };
            _curveCanvas.CurveModified += (s, e) =>
            {
                if (_curveCanvas.EditSysFan) _sysManualOverride = false;
                else _apuManualOverride = false;
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                SaveSettings();
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                RefreshStatus();
            };
            modeCard.Controls.Add(_curveCanvas);

            _liveCanvas = new LiveTelemetryCanvas
            {
                Location = new Point(8, 108),
                Size = new Size(936, 302),
                Stats = _sessionStats,
                HasDualFans = _hasDualFans,
                Visible = false
            };
            _liveCanvas.ResetPeaksRequested += (s, e) => ResetSessionPeaks();
            _liveCanvas.HighlightMetricChanged += (s, e) =>
            {
                _highlightedMetric = _liveCanvas.HighlightMetric;
                UpdateCurveHint();
                if (_cpuTempBox != null) _cpuTempBox.Invalidate();
                if (_gpuTempBox != null) _gpuTempBox.Invalidate();
                if (_nvmeTempBox != null) _nvmeTempBox.Invalidate();
                if (_pwrBox != null) _pwrBox.Invalidate();
                if (_ramPowerBox != null) _ramPowerBox.Invalidate();
                if (_fan1Box != null) _fan1Box.Invalidate();
                if (_fan2Box != null) _fan2Box.Invalidate();
            };
            modeCard.Controls.Add(_liveCanvas);

            // OSD Overlay Configuration Studio Panel (covers rows 2, 3, 4 inside modeCard)
            _osdConfigPanel = new Panel
            {
                Location = new Point(8, 41),
                Size = new Size(936, 368),
                Visible = false
            };
            BuildOsdStudioPanel(_osdConfigPanel);
            modeCard.Controls.Add(_osdConfigPanel);

            _curveHintLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 416),
                Size = new Size(936, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };
            modeCard.Controls.Add(_curveHintLabel);

            // 3. Manual Fan Control Section (top = 694, card = 714..808, height = 94)
            Panel fanCard = CreateSectionCard("Manual Fan Control", 694, 94);

            _calibBadgeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(400, 695),
                Size = new Size(540, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            _calibBadgeLabel.Click += (s, e) => RunGuiFanCalibration();
            Controls.Add(_calibBadgeLabel);
            _calibBadgeLabel.BringToFront();
            _uiToolTip.SetToolTip(_calibBadgeLabel,
                "Shows when your fans were last calibrated and their minimum anti-stall PWM %.\r\nClick to run the 3-pass real-time Fan Calibration Wizard.");

            AddStaticLabel(fanCard, "APU / CPU Fan:", 10, 14, 120, 24);
            _fan1Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(134, 10),
                Size = new Size(642, 32)
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
                ApplyFan(1, _fan1Slider.Value);
            };
            fanCard.Controls.Add(_fan1Slider);

            _fan1SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(782, 14),
                Size = new Size(60, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan1SliderPct);

            _fan1AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 12),
                Size = new Size(80, 26),
                Cursor = Cursors.Hand
            };
            _fan1AutoBtn.FlatAppearance.BorderSize = 0;
            _fan1AutoBtn.Click += (s, e) => ApplyFan(1, 0);
            fanCard.Controls.Add(_fan1AutoBtn);

            AddStaticLabel(fanCard, "Sys / RAM Fan:", 10, 52, 120, 24);
            _fan2Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(134, 48),
                Size = new Size(642, 32)
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
                ApplyFan(2, _fan2Slider.Value);
            };
            fanCard.Controls.Add(_fan2Slider);

            _fan2SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(782, 52),
                Size = new Size(60, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan2SliderPct);

            _fan2AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 50),
                Size = new Size(80, 26),
                Cursor = Cursors.Hand
            };
            _fan2AutoBtn.FlatAppearance.BorderSize = 0;
            _fan2AutoBtn.Click += (s, e) => ApplyFan(2, 0);
            fanCard.Controls.Add(_fan2AutoBtn);

            // 4. Options Section (top = 814, card = 834..920, height = 86)
            Panel optCard = CreateSectionCard("Options", 814, 86);

            _samplingLabel = new Label
            {
                Text = "Sampling:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(650, 815),
                AutoSize = true,
                Cursor = Cursors.Default
            };
            Controls.Add(_samplingLabel);
            _samplingLabel.BringToFront();

            _samplingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(720, 811),
                Size = new Size(224, 22),
                Cursor = Cursors.Hand
            };
            int[] sampleIntervals = new int[] { 250, 500, 1000, 1500, 2000, 3000 };
            _samplingCombo.Items.Add("250 ms (Ultra • 4 Hz)");
            _samplingCombo.Items.Add("500 ms (Fast • 2 Hz)");
            _samplingCombo.Items.Add("1000 ms (Standard • 1.0s)");
            _samplingCombo.Items.Add("1500 ms (Balanced • 1.5s)");
            _samplingCombo.Items.Add("2000 ms (Relaxed • 2.0s)");
            _samplingCombo.Items.Add("3000 ms (Low CPU • 3.0s)");
            int initialSel = 1;
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
            Controls.Add(_samplingCombo);
            _samplingCombo.BringToFront();
            _uiToolTip.SetToolTip(_samplingCombo, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");
            _uiToolTip.SetToolTip(_samplingLabel, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");

            _showCpuTrayCb = new CheckBox
            {
                Text = "Show CPU temp in taskbar",
                Location = new Point(12, 8),
                Size = new Size(310, 24),
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
                Location = new Point(340, 8),
                Size = new Size(300, 24),
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
                Text = "Show CapFrameX OSD overlay",
                Location = new Point(12, 32),
                Size = new Size(216, 24),
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
                Location = new Point(232, 31),
                Size = new Size(84, 24),
                Cursor = Cursors.Hand
            };
            _optCardOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _optCardOsdConfigBtn.Click += (s, e) => OpenOsdConfigTab();
            optCard.Controls.Add(_optCardOsdConfigBtn);
            _uiToolTip.SetToolTip(_optCardOsdConfigBtn, "Open the dedicated OSD Studio tab to configure live graphs, placement, opacity and metrics");

            _lockOverlayCb = new CheckBox
            {
                Text = "Lock OSD (click-through)",
                Location = new Point(340, 32),
                Size = new Size(300, 24),
                Checked = _lockOverlay
            };
            _lockOverlayCb.CheckedChanged += (s, e) => SetOverlayLocked(_lockOverlayCb.Checked);
            optCard.Controls.Add(_lockOverlayCb);

            _autostartCb = new CheckBox
            {
                Text = "Start automatically on Windows login",
                Location = new Point(12, 56),
                Size = new Size(310, 24),
                Checked = IsAutostartEnabled()
            };
            _autostartCb.CheckedChanged += (s, e) => ToggleAutostart();
            optCard.Controls.Add(_autostartCb);

            _alwaysOnTopCb = new CheckBox
            {
                Text = "Keep app on top",
                Location = new Point(340, 56),
                Size = new Size(300, 24),
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

            _trayCb = new CheckBox
            {
                Text = "Hide to system tray",
                Location = new Point(660, 56),
                Size = new Size(260, 24)
            };
            _trayCb.CheckedChanged += (s, e) =>
            {
                if (_trayCb.Checked) MinimizeToTray();
            };
            optCard.Controls.Add(_trayCb);

            // Error Banner
            _errorBanner = new Label
            {
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(14, 834),
                Size = new Size(952, 82),
                Padding = new Padding(8),
                Visible = false
            };
            Controls.Add(_errorBanner);
            _errorBanner.BringToFront();

            // 5. Status Bar (y = 920..950)
            _statusBarLabel = new Label
            {
                Text = "⏳ Initializing…",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(14, 925),
                Size = new Size(700, 22)
            };
            Controls.Add(_statusBarLabel);

            _allAutoBtn = new Button
            {
                Text = "Reset Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(730, 922),
                Size = new Size(106, 28),
                Cursor = Cursors.Hand
            };
            _allAutoBtn.FlatAppearance.BorderSize = 0;
            _allAutoBtn.Click += (s, e) => ApplyAutoAll();
            Controls.Add(_allAutoBtn);

            _closeBtn = new Button
            {
                Text = "Close",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(846, 922),
                Size = new Size(106, 28),
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => Close();
            Controls.Add(_closeBtn);
        }

        private void BuildOsdStudioPanel(Panel p)
        {
            // Row 1 (y=5): Title + Quick Toggle Buttons
            _osdTitleLabel = new Label
            {
                Text = "OSD OVERLAY CONFIGURATION STUDIO",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(380, 22),
                UseMnemonic = false
            };
            p.Controls.Add(_osdTitleLabel);

            _osdToggleOverlayBtn = new Button
            {
                Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(626, 5),
                Size = new Size(140, 26),
                Cursor = Cursors.Hand
            };
            _osdToggleOverlayBtn.FlatAppearance.BorderSize = 0;
            _osdToggleOverlayBtn.Click += (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlay;
                else SetOverlayVisible(!_showOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            p.Controls.Add(_osdToggleOverlayBtn);

            _osdToggleLockBtn = new Button
            {
                Text = _lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(774, 5),
                Size = new Size(154, 26),
                Cursor = Cursors.Hand
            };
            _osdToggleLockBtn.FlatAppearance.BorderSize = 0;
            _osdToggleLockBtn.Click += (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlay;
                else SetOverlayLocked(!_lockOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            p.Controls.Add(_osdToggleLockBtn);

            // Row 2 (y=38): Section 1 - Live Graph Display Mode (Left) & Combined Graph Height (Right)
            _osdGraphModeLabel = new Label
            {
                Text = "Live Graph Display Mode:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 38),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            p.Controls.Add(_osdGraphModeLabel);

            _osdGraphHeightLabel = new Label
            {
                Text = "Combined Live Graph Height:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(476, 38),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            p.Controls.Add(_osdGraphHeightLabel);

            // Row 2 Buttons (y=56, h=26, w=108):
            _osdBtnBothGraphs = new Button
            {
                Text = "Both (All)",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnBothGraphs.FlatAppearance.BorderSize = 0;
            _osdBtnBothGraphs.Click += (s, e) => { _osdGraphMode = 3; _osdShowLiveTrend = true; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnBothGraphs, "Show both mini sparklines per metric and combined live trend graph");
            p.Controls.Add(_osdBtnBothGraphs);

            _osdBtnCombinedGraph = new Button
            {
                Text = "Combined",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(124, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnCombinedGraph.FlatAppearance.BorderSize = 0;
            _osdBtnCombinedGraph.Click += (s, e) => { _osdGraphMode = 2; _osdShowLiveTrend = true; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnCombinedGraph, "Show combined multi-series live trend graph only");
            p.Controls.Add(_osdBtnCombinedGraph);

            _osdBtnSparklines = new Button
            {
                Text = "Sparklines",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(240, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnSparklines.FlatAppearance.BorderSize = 0;
            _osdBtnSparklines.Click += (s, e) => { _osdGraphMode = 1; _osdShowLiveTrend = false; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnSparklines, "Show mini inline sparklines per metric only");
            p.Controls.Add(_osdBtnSparklines);

            _osdBtnTextOnly = new Button
            {
                Text = "Text Only",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(356, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnTextOnly.FlatAppearance.BorderSize = 0;
            _osdBtnTextOnly.Click += (s, e) => { _osdGraphMode = 0; _osdShowLiveTrend = false; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnTextOnly, "Display metrics as text only without live graphs");
            p.Controls.Add(_osdBtnTextOnly);

            // Right side: Height buttons
            _osdBtnHeightCompact = new Button
            {
                Text = "75px Compact",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(476, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightCompact.FlatAppearance.BorderSize = 0;
            _osdBtnHeightCompact.Click += (s, e) => { _osdGraphHeight = 75; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightCompact);

            _osdBtnHeightMedium = new Button
            {
                Text = "105px Medium",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(592, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightMedium.FlatAppearance.BorderSize = 0;
            _osdBtnHeightMedium.Click += (s, e) => { _osdGraphHeight = 105; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightMedium);

            _osdBtnHeightLarge = new Button
            {
                Text = "140px Large",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(708, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightLarge.FlatAppearance.BorderSize = 0;
            _osdBtnHeightLarge.Click += (s, e) => { _osdGraphHeight = 140; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightLarge);

            _osdBtnHeightXl = new Button
            {
                Text = "180px XL",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(820, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightXl.FlatAppearance.BorderSize = 0;
            _osdBtnHeightXl.Click += (s, e) => { _osdGraphHeight = 180; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightXl);

            // Row 3 (y=88): Section 2 - Scale Tier (Left) & Layout Direction (Right)
            _osdScaleLabel = new Label
            {
                Text = "Overlay Scale Tier:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 88),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            p.Controls.Add(_osdScaleLabel);

            _osdLayoutLabel = new Label
            {
                Text = "Layout Direction:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(476, 88),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            p.Controls.Add(_osdLayoutLabel);

            // Row 3 Buttons (y=106, h=26):
            _osdBtnScale100 = new Button
            {
                Text = "100% Std",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 106),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnScale100.FlatAppearance.BorderSize = 0;
            _osdBtnScale100.Click += (s, e) => { _osdScaleTier = 0; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale100);

            _osdBtnScale125 = new Button
            {
                Text = "125% Lg",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(124, 106),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnScale125.FlatAppearance.BorderSize = 0;
            _osdBtnScale125.Click += (s, e) => { _osdScaleTier = 1; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale125);

            _osdBtnScale150 = new Button
            {
                Text = "150% XL",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(240, 106),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnScale150.FlatAppearance.BorderSize = 0;
            _osdBtnScale150.Click += (s, e) => { _osdScaleTier = 2; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale150);

            _osdBtnScale185 = new Button
            {
                Text = "185% 4K",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(356, 106),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnScale185.FlatAppearance.BorderSize = 0;
            _osdBtnScale185.Click += (s, e) => { _osdScaleTier = 3; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale185);

            _osdBtnLayoutH = new Button
            {
                Text = "▬ Horizontal Bar",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(476, 106),
                Size = new Size(222, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutH.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutH.Click += (s, e) => { _overlayVertical = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnLayoutH);

            _osdBtnLayoutV = new Button
            {
                Text = "▮ Vertical Stack",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(706, 106),
                Size = new Size(222, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutV.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutV.Click += (s, e) => { _overlayVertical = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnLayoutV);

            // Row 4 (y=136): Section 3 - Opacity Slider & Quick Snap Corner Buttons
            _osdOpacityLabel = new Label
            {
                Text = "Opacity:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 140),
                Size = new Size(62, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdOpacityLabel);

            _osdOpacitySlider = new TrackBar
            {
                Minimum = 35,
                Maximum = 100,
                Value = _osdOpacityPct,
                TickStyle = TickStyle.None,
                Location = new Point(72, 134),
                Size = new Size(130, 30)
            };
            _osdOpacitySlider.Scroll += (s, e) =>
            {
                _osdOpacityPct = _osdOpacitySlider.Value;
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osdOpacityPct);
                if (_overlayForm != null) _overlayForm.ApplyOpacity(_osdOpacityPct);
                SaveSettings();
            };
            p.Controls.Add(_osdOpacitySlider);

            _osdOpacityPctLabel = new Label
            {
                Text = string.Format("{0}%", _osdOpacityPct),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(206, 138),
                Size = new Size(42, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdOpacityPctLabel);

            _osdSnapLabel = new Label
            {
                Text = "Quick Snap:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(252, 138),
                Size = new Size(88, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdSnapLabel);

            Font snapBtnFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);
            _osdQuickPosTl = new Button { Text = "Top-Left", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(344, 136), Size = new Size(94, 26), Cursor = Cursors.Hand };
            _osdQuickPosTl.FlatAppearance.BorderSize = 0;
            _osdQuickPosTl.Click += (s, e) => SetOverlayCornerPosition(0);
            _uiToolTip.SetToolTip(_osdQuickPosTl, "Snap OSD to Top-Left screen corner");
            p.Controls.Add(_osdQuickPosTl);

            _osdQuickPosTc = new Button { Text = "Center-Top", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(442, 136), Size = new Size(94, 26), Cursor = Cursors.Hand };
            _osdQuickPosTc.FlatAppearance.BorderSize = 0;
            _osdQuickPosTc.Click += (s, e) => SetOverlayCornerPosition(4);
            _uiToolTip.SetToolTip(_osdQuickPosTc, "Snap OSD to Center-Top screen edge");
            p.Controls.Add(_osdQuickPosTc);

            _osdQuickPosTr = new Button { Text = "Top-Right", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(540, 136), Size = new Size(94, 26), Cursor = Cursors.Hand };
            _osdQuickPosTr.FlatAppearance.BorderSize = 0;
            _osdQuickPosTr.Click += (s, e) => SetOverlayCornerPosition(1);
            _uiToolTip.SetToolTip(_osdQuickPosTr, "Snap OSD to Top-Right screen corner");
            p.Controls.Add(_osdQuickPosTr);

            _osdQuickPosBl = new Button { Text = "Bottom-Left", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(638, 136), Size = new Size(94, 26), Cursor = Cursors.Hand };
            _osdQuickPosBl.FlatAppearance.BorderSize = 0;
            _osdQuickPosBl.Click += (s, e) => SetOverlayCornerPosition(2);
            _uiToolTip.SetToolTip(_osdQuickPosBl, "Snap OSD to Bottom-Left screen corner");
            p.Controls.Add(_osdQuickPosBl);

            _osdQuickPosBc = new Button { Text = "Center-Bottom", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(736, 136), Size = new Size(96, 26), Cursor = Cursors.Hand };
            _osdQuickPosBc.FlatAppearance.BorderSize = 0;
            _osdQuickPosBc.Click += (s, e) => SetOverlayCornerPosition(5);
            _uiToolTip.SetToolTip(_osdQuickPosBc, "Snap OSD to Center-Bottom screen edge");
            p.Controls.Add(_osdQuickPosBc);

            _osdQuickPosBr = new Button { Text = "Bottom-Right", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(836, 136), Size = new Size(94, 26), Cursor = Cursors.Hand };
            _osdQuickPosBr.FlatAppearance.BorderSize = 0;
            _osdQuickPosBr.Click += (s, e) => SetOverlayCornerPosition(3);
            _uiToolTip.SetToolTip(_osdQuickPosBr, "Snap OSD to Bottom-Right screen corner");
            p.Controls.Add(_osdQuickPosBr);

            // Row 5 (y=180): Section 4 - Telemetry Metrics & Overlay Parts Header + Select All / Defaults Buttons
            _osdMetricsHeaderLabel = new Label
            {
                Text = "Visible Telemetry Metrics & Overlay Parts:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(8, 180),
                Size = new Size(460, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdMetricsHeaderLabel);

            _osdSelectAllBtn = new Button
            {
                Text = "Select All",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(592, 176),
                Size = new Size(104, 25),
                Cursor = Cursors.Hand
            };
            _osdSelectAllBtn.FlatAppearance.BorderSize = 0;
            _osdSelectAllBtn.Click += (s, e) =>
            {
                _osdShowCpu = true;
                _osdShowGpu = true;
                _osdShowNvme = true;
                _osdShowPower = true;
                _osdShowRam = true;
                _osdShowFps = true;
                _osdShowLows = true;
                _osdShowGpuLoad = true;
                _osdShowApuFan = true;
                if (_hasDualFans) _osdShowSysFan = true;
                _osdShowMode = true;
                _osdShowPeaks = true;
                _osdShowRadeon = true;
                _osdShowClocks = false;
                _osdSparklineBg = false;
                _osdShowLiveTrend = true;
                _osdShowFpsGraph = true;
                _osdPlotFpsOnTrend = true;
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdSelectAllBtn);

            _osdDeselectAllBtn = new Button
            {
                Text = "Deselect All",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(704, 176),
                Size = new Size(108, 25),
                Cursor = Cursors.Hand
            };
            _osdDeselectAllBtn.FlatAppearance.BorderSize = 0;
            _osdDeselectAllBtn.Click += (s, e) =>
            {
                _osdShowCpu = false;
                _osdShowGpu = false;
                _osdShowNvme = false;
                _osdShowPower = false;
                _osdShowRam = false;
                _osdShowFps = false;
                _osdShowLows = false;
                _osdShowGpuLoad = false;
                _osdShowApuFan = false;
                _osdShowSysFan = false;
                _osdShowMode = false;
                _osdShowPeaks = false;
                _osdShowRadeon = false;
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdDeselectAllBtn);

            _osdResetDefaultsBtn = new Button
            {
                Text = "Defaults",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(820, 176),
                Size = new Size(108, 25),
                Cursor = Cursors.Hand
            };
            _osdResetDefaultsBtn.FlatAppearance.BorderSize = 0;
            _osdResetDefaultsBtn.Click += (s, e) =>
            {
                _osdShowCpu = true;
                _osdShowGpu = true;
                _osdShowNvme = true;
                _osdShowPower = true;
                _osdShowRam = true;
                _osdShowFps = true;
                _osdShowLows = true;
                _osdShowGpuLoad = true;
                _osdShowApuFan = true;
                _osdShowSysFan = _hasDualFans;
                _osdShowMode = true;
                _osdShowPeaks = true;
                _osdShowRadeon = true;
                _osdShowClocks = false;
                _osdSparklineBg = false;
                _osdShowLiveTrend = true;
                _osdShowFpsGraph = true;
                _osdPlotFpsOnTrend = true;
                _osdDfGraphStyle = false;
                _osdDfGraphFirst = false;
                _osdMatchTheme = true;
                _osdScaleTier = 0;
                _osdGraphMode = 3;
                _osdGraphHeight = 105;
                _osdOpacityPct = 88;
                _overlayVertical = false;
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdResetDefaultsBtn);

            // Row 6 (y=206): Checkboxes Grid (4 Columns)
            // Column 1: x=8 (w=224), Column 2: x=238 (w=224), Column 3: x=476 (w=224), Column 4: x=706 (w=222)
            _osdShowCpuCb = new CheckBox { Text = "CPU Temp", Location = new Point(8, 206), Size = new Size(224, 21), Checked = _osdShowCpu, UseMnemonic = false };
            _osdShowCpuCb.CheckedChanged += (s, e) => { _osdShowCpu = _osdShowCpuCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowCpuCb);

            _osdShowGpuCb = new CheckBox { Text = "iGPU Temp", Location = new Point(238, 206), Size = new Size(224, 21), Checked = _osdShowGpu, UseMnemonic = false };
            _osdShowGpuCb.CheckedChanged += (s, e) => { _osdShowGpu = _osdShowGpuCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowGpuCb);

            _osdShowNvmeCb = new CheckBox { Text = "NVMe SSD Temp", Location = new Point(476, 206), Size = new Size(224, 21), Checked = _osdShowNvme, UseMnemonic = false };
            _osdShowNvmeCb.CheckedChanged += (s, e) => { _osdShowNvme = _osdShowNvmeCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowNvmeCb);

            _osdShowRamCb = new CheckBox { Text = "RAM Load & Power", Location = new Point(706, 206), Size = new Size(222, 21), Checked = _osdShowRam, UseMnemonic = false };
            _osdShowRamCb.CheckedChanged += (s, e) => { _osdShowRam = _osdShowRamCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowRamCb);

            // Row 7 (y=230):
            _osdShowApuFanCb = new CheckBox { Text = "APU Fan RPM", Location = new Point(8, 230), Size = new Size(224, 21), Checked = _osdShowApuFan, UseMnemonic = false };
            _osdShowApuFanCb.CheckedChanged += (s, e) => { _osdShowApuFan = _osdShowApuFanCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowApuFanCb);

            _osdShowSysFanCb = new CheckBox
            {
                Text = _hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)",
                Location = new Point(238, 230),
                Size = new Size(224, 21),
                Checked = _osdShowSysFan && _hasDualFans,
                Enabled = _hasDualFans,
                UseMnemonic = false
            };
            _osdShowSysFanCb.CheckedChanged += (s, e) => { _osdShowSysFan = _osdShowSysFanCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowSysFanCb);

            _osdShowPowerCb = new CheckBox { Text = "APU Power (W)", Location = new Point(476, 230), Size = new Size(224, 21), Checked = _osdShowPower, UseMnemonic = false };
            _osdShowPowerCb.CheckedChanged += (s, e) => { _osdShowPower = _osdShowPowerCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowPowerCb);

            _osdShowGpuLoadCb = new CheckBox { Text = "GPU Load & Clock", Location = new Point(706, 230), Size = new Size(222, 21), Checked = _osdShowGpuLoad, UseMnemonic = false };
            _osdShowGpuLoadCb.CheckedChanged += (s, e) => { _osdShowGpuLoad = _osdShowGpuLoadCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowGpuLoadCb);

            // Row 8 (y=254):
            _osdShowFpsCb = new CheckBox { Text = "Live FPS & Frametime", Location = new Point(8, 254), Size = new Size(224, 21), Checked = _osdShowFps, UseMnemonic = false };
            _osdShowFpsCb.CheckedChanged += (s, e) => { _osdShowFps = _osdShowFpsCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowFpsCb);

            _osdShowLowsCb = new CheckBox { Text = "0.1% Low & Avg FPS", Location = new Point(238, 254), Size = new Size(224, 21), Checked = _osdShowLows, UseMnemonic = false };
            _osdShowLowsCb.CheckedChanged += (s, e) => { _osdShowLows = _osdShowLowsCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowLowsCb);

            _osdShowModeCb = new CheckBox { Text = "Thermal Mode", Location = new Point(476, 254), Size = new Size(224, 21), Checked = _osdShowMode, UseMnemonic = false };
            _osdShowModeCb.CheckedChanged += (s, e) => { _osdShowMode = _osdShowModeCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowModeCb);

            _osdShowPeaksCb = new CheckBox { Text = "Peak Trackers (▲)", Location = new Point(706, 254), Size = new Size(222, 21), Checked = _osdShowPeaks, UseMnemonic = false };
            _osdShowPeaksCb.CheckedChanged += (s, e) => { _osdShowPeaks = _osdShowPeaksCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowPeaksCb);

            // Row 9 (y=278): Graph Styles & Toggles
            _osdMatchThemeCb = new CheckBox { Text = "Match App Theme", Location = new Point(8, 278), Size = new Size(224, 21), Checked = _osdMatchTheme, UseMnemonic = false };
            _osdMatchThemeCb.CheckedChanged += (s, e) => { _osdMatchTheme = _osdMatchThemeCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdMatchThemeCb);

            _osdShowLiveTrendCb = new CheckBox { Text = "Live Trend Graph", Location = new Point(238, 278), Size = new Size(224, 21), Checked = _osdShowLiveTrend, UseMnemonic = false };
            _osdShowLiveTrendCb.CheckedChanged += (s, e) =>
            {
                _osdShowLiveTrend = _osdShowLiveTrendCb.Checked;
                if (_osdShowLiveTrend)
                {
                    if (_osdGraphMode == 0) _osdGraphMode = 2;
                    else if (_osdGraphMode == 1) _osdGraphMode = 3;
                }
                else
                {
                    if (_osdGraphMode == 2) _osdGraphMode = 0;
                    else if (_osdGraphMode == 3) _osdGraphMode = 1;
                }
                SyncOverlayFromStudio();
                UpdateOsdStudioStyles();
            };
            _uiToolTip.SetToolTip(_osdShowLiveTrendCb, "Display the multi-sensor live history trend chart (CPU, iGPU, NVMe, Power, Fan RPM)");
            p.Controls.Add(_osdShowLiveTrendCb);

            _osdShowFpsGraphCb = new CheckBox { Text = "FPS & Frametime Graph", Location = new Point(476, 278), Size = new Size(224, 21), Checked = _osdShowFpsGraph, UseMnemonic = false };
            _osdShowFpsGraphCb.CheckedChanged += (s, e) => { _osdShowFpsGraph = _osdShowFpsGraphCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowFpsGraphCb);

            _osdDfGraphStyleCb = new CheckBox { Text = "DF Twin Graphs", Location = new Point(706, 278), Size = new Size(222, 21), Checked = _osdDfGraphStyle, UseMnemonic = false };
            _osdDfGraphStyleCb.CheckedChanged += (s, e) => { _osdDfGraphStyle = _osdDfGraphStyleCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdDfGraphStyleCb, "Render Digital Foundry style twin graphs: FRAME-TIME (MS) 0-50-100 and FRAME-RATE (FPS) 0-40-80-120");
            p.Controls.Add(_osdDfGraphStyleCb);

            // Row 10 (y=302): Feature Tags & Ordering
            _osdShowRadeonCb = new CheckBox { Text = "Radeon Feature Tags", Location = new Point(8, 302), Size = new Size(224, 21), Checked = _osdShowRadeon, UseMnemonic = false };
            _osdShowRadeonCb.CheckedChanged += (s, e) => { _osdShowRadeon = _osdShowRadeonCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdShowRadeonCb, "Display compact pill tags for active AMD Radeon features (FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)");
            p.Controls.Add(_osdShowRadeonCb);

            _osdShowDriverVerCb = new CheckBox { Text = "Radeon Driver Version", Location = new Point(238, 302), Size = new Size(224, 21), Checked = _osdShowDriverVer, UseMnemonic = false };
            _osdShowDriverVerCb.CheckedChanged += (s, e) => { _osdShowDriverVer = _osdShowDriverVerCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdShowDriverVerCb, "Show the installed AMD Radeon Software (Adrenalin) driver version on the OSD");
            p.Controls.Add(_osdShowDriverVerCb);

            _osdPlotFpsOnTrendCb = new CheckBox { Text = "Plot FPS on Live Trend", Location = new Point(476, 302), Size = new Size(224, 21), Checked = _osdPlotFpsOnTrend, UseMnemonic = false };
            _osdPlotFpsOnTrendCb.CheckedChanged += (s, e) => { _osdPlotFpsOnTrend = _osdPlotFpsOnTrendCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdPlotFpsOnTrendCb);

            _osdDfGraphFirstCb = new CheckBox { Text = "DF Graphs on Top", Location = new Point(706, 302), Size = new Size(222, 21), Checked = _osdDfGraphFirst, UseMnemonic = false };
            _osdDfGraphFirstCb.CheckedChanged += (s, e) => { _osdDfGraphFirst = _osdDfGraphFirstCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdDfGraphFirstCb, "Render Digital Foundry style twin graphs above the multi-sensor Live Trend graph");
            p.Controls.Add(_osdDfGraphFirstCb);

            // Row 11 (y=326): Clock Speeds & Sparkline Styles
            _osdShowClocksCb = new CheckBox { Text = "Clock Speeds (@ GHz/MHz)", Location = new Point(8, 326), Size = new Size(224, 21), Checked = _osdShowClocks, UseMnemonic = false };
            _osdShowClocksCb.CheckedChanged += (s, e) => { _osdShowClocks = _osdShowClocksCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdShowClocksCb, "Display effective CPU, iGPU, and RAM clock frequencies (@ 4.1G / @ 800MHz). Uncheck to maximize space for sparklines.");
            p.Controls.Add(_osdShowClocksCb);

            _osdSparklineBgCb = new CheckBox { Text = "Background Sparklines", Location = new Point(238, 326), Size = new Size(224, 21), Checked = _osdSparklineBg, UseMnemonic = false };
            _osdSparklineBgCb.CheckedChanged += (s, e) => { _osdSparklineBg = _osdSparklineBgCb.Checked; SyncOverlayFromStudio(); };
            _uiToolTip.SetToolTip(_osdSparklineBgCb, "Render sparklines as full-width glowing background underlays across each metric row");
            p.Controls.Add(_osdSparklineBgCb);

            // Row 12 (y=348): Guidance Tip Box
            _osdTipLabel = new Label
            {
                Text = "Tip: Drag OSD anywhere across multiple monitors with your mouse. Right-click the OSD anytime for quick scale, graph modes, heights & click-through lock, or press Ctrl+Alt+F9 to toggle overlay.",
                Font = new Font("Segoe UI", 7.75f, FontStyle.Regular),
                Location = new Point(8, 348),
                Size = new Size(920, 18),
                Padding = new Padding(2, 0, 2, 0),
                UseMnemonic = false
            };
            p.Controls.Add(_osdTipLabel);
        }

        private FanCurveProfile GetSelectedTabProfile()
        {
            if (_curveCanvas != null && _curveCanvas.EditSysFan)
                return _sysProfile ?? _builtInProfiles[0];
            return _apuProfile ?? _builtInProfiles[0];
        }

        private void PopulateCustomCombo()
        {
            _suppressComboEvent = true;
            _customModeCombo.Items.Clear();
            FanCurveProfile tabProf = GetSelectedTabProfile();
            int selIdx = 0;
            for (int i = 0; i < _customProfiles.Count; i++)
            {
                _customModeCombo.Items.Add(_customProfiles[i].Name);
                if (tabProf == _customProfiles[i])
                    selIdx = i;
            }
            if (_customModeCombo.Items.Count > 0)
                _customModeCombo.SelectedIndex = selIdx;
            _suppressComboEvent = false;
            UpdateCurveTabStyles();
            UpdateCurveHint();
        }

        private void OnCustomComboSelected(object sender, EventArgs e)
        {
            if (_suppressComboEvent) return;
            int idx = _customModeCombo.SelectedIndex;
            if (idx >= 0 && idx < _customProfiles.Count)
            {
                ActivateCustomProfile(_customProfiles[idx]);
            }
        }

        private void ActivateCustomProfile(FanCurveProfile profile)
        {
            if (profile == null) return;
            bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
            if (editSys)
            {
                _sysProfile = profile;
                _sysManualOverride = false;
                _lastAppliedSysPct = -1;
            }
            else
            {
                _apuProfile = profile;
                _apuManualOverride = false;
                _lastAppliedApuPct = -1;
            }

            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.Invalidate();
            }
            PopulateCustomCombo();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            SaveSettings();
            string fanLabel = editSys ? "Sys Fan" : "APU Fan";
            StartRampIndication(fanLabel + " → " + profile.Name, editSys ? 2 : 1);
        }

        private void PromptCreateCustomMode()
        {
            string suggested = string.Format("Custom {0}", _customProfiles.Count + 1);
            using (NewModeDialog dlg = new NewModeDialog(_isDarkMode, suggested))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.ModeName))
                {
                    FanCurveProfile baseProf = GetSelectedTabProfile();
                    FanCurveProfile created = baseProf.CloneAsCustom(dlg.ModeName);
                    _customProfiles.Add(created);
                    ActivateCustomProfile(created);
                }
            }
        }

        private void DeleteSelectedCustomMode()
        {
            int idx = _customModeCombo.SelectedIndex;
            if (idx < 0 || idx >= _customProfiles.Count) return;

            if (_customProfiles.Count <= 1)
            {
                MessageBox.Show(this, "At least one custom curve profile is kept.", "K12 Fan Control",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FanCurveProfile removed = _customProfiles[idx];
            _customProfiles.RemoveAt(idx);
            if (_apuProfile == removed)
            {
                _apuProfile = _builtInProfiles[0];
                _lastAppliedApuPct = -1;
            }
            if (_sysProfile == removed)
            {
                _sysProfile = _builtInProfiles[0];
                _lastAppliedSysPct = -1;
            }
            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.Invalidate();
            }
            PopulateCustomCombo();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            SaveSettings();
            RefreshStatus();
        }

        private void UpdateCurveTabStyles()
        {
            if (_tabApuCurveBtn == null || _tabSysCurveBtn == null || _curveCanvas == null)
                return;

            string apuModeStr = _apuManualOverride ? "Manual" : (_apuProfile != null ? _apuProfile.Name : "Balanced");
            string sysModeStr = _sysManualOverride ? "Manual" : (_sysProfile != null ? _sysProfile.Name : "Balanced");
            _tabApuCurveBtn.Text = string.Format("● APU: {0}", ShortModeTag(apuModeStr));
            _tabSysCurveBtn.Text = string.Format("● Sys: {0}", ShortModeTag(sysModeStr));
            if (_tabLiveGraphBtn != null)
            {
                string hlTag = _highlightedMetric == 2 ? " (iGPU)"
                    : (_highlightedMetric == 6 ? " (NVMe)"
                    : (_highlightedMetric == 3 ? " (Pwr)"
                    : (_highlightedMetric == 7 ? " (RAM)"
                    : (_highlightedMetric == 4 ? " (APU)"
                    : (_highlightedMetric == 5 ? " (Sys)"
                    : (_highlightedMetric == 1 ? " (CPU)" : ""))))));
                _tabLiveGraphBtn.Text = _showingLiveTelemetry ? ("● Live" + hlTag) : "● Live Trend";
            }

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            Color apuAccent = isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent));
            Color sysAccent = isRadeon ? RadeonOrange : (IsAb ? AbCyan : (_isDarkMode ? DarkTeal : LightTeal));
            Color liveAccent = _highlightedMetric == 2
                ? (isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E))
                : (_highlightedMetric == 6
                    ? (_isDarkMode ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2))
                    : (_highlightedMetric == 3
                        ? (isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B))
                        : (_highlightedMetric == 7
                            ? Color.FromArgb(0x81, 0x8C, 0xF8)
                            : (_highlightedMetric == 4
                                ? Color.FromArgb(0xC0, 0x84, 0xFC)
                                : (_highlightedMetric == 5
                                    ? Color.FromArgb(0x2D, 0xD4, 0xBF)
                                    : apuAccent)))));
            Color activeFg = isRadeon ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            if (_showingOsdConfig)
            {
                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }

                if (_tabOsdConfigBtn != null)
                {
                    _tabOsdConfigBtn.BackColor = isRadeon ? RadeonAccent : apuAccent;
                    _tabOsdConfigBtn.ForeColor = activeFg;
                    if (_tabOsdConfigBtn.Font != TabFontBold) _tabOsdConfigBtn.Font = TabFontBold;
                }
                return;
            }

            if (_tabOsdConfigBtn != null)
            {
                _tabOsdConfigBtn.BackColor = surface2;
                _tabOsdConfigBtn.ForeColor = text;
                if (_tabOsdConfigBtn.Font != TabFontRegular) _tabOsdConfigBtn.Font = TabFontRegular;
            }

            if (_showingLiveTelemetry)
            {
                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = liveAccent;
                    _tabLiveGraphBtn.ForeColor = (_highlightedMetric == 2 || _highlightedMetric == 3 || _highlightedMetric == 6 || _highlightedMetric == 7) ? Color.Black : activeFg;
                    if (_tabLiveGraphBtn.Font != TabFontBold) _tabLiveGraphBtn.Font = TabFontBold;
                }
            }
            else if (!_curveCanvas.EditSysFan)
            {
                _tabApuCurveBtn.BackColor = apuAccent;
                _tabApuCurveBtn.ForeColor = activeFg;
                if (_tabApuCurveBtn.Font != TabFontBold) _tabApuCurveBtn.Font = TabFontBold;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }
            }
            else
            {
                _tabSysCurveBtn.BackColor = sysAccent;
                _tabSysCurveBtn.ForeColor = activeFg;
                if (_tabSysCurveBtn.Font != TabFontBold) _tabSysCurveBtn.Font = TabFontBold;

                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }
            }
        }

        private void UpdateOsdStudioStyles()
        {
            if (_osdConfigPanel == null) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface = isRadeon ? RadeonSurface : (IsAb ? AbSurface : (_isDarkMode ? DarkSurface : LightSurface));
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            Color textDim = isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim));
            Color accent = isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent));
            Color activeFg = isRadeon ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            _osdConfigPanel.BackColor = surface;

            // Highlight active Graph Mode button
            Button[] modeBtns = new[] { _osdBtnTextOnly, _osdBtnSparklines, _osdBtnCombinedGraph, _osdBtnBothGraphs };
            for (int i = 0; i < modeBtns.Length; i++)
            {
                if (modeBtns[i] == null) continue;
                bool active = (_osdGraphMode == i);
                modeBtns[i].BackColor = active ? accent : surface2;
                modeBtns[i].ForeColor = active ? activeFg : text;
                modeBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Scale button (100%, 125%, 150%, 185%)
            Button[] scaleBtns = new[] { _osdBtnScale100, _osdBtnScale125, _osdBtnScale150, _osdBtnScale185 };
            for (int i = 0; i < scaleBtns.Length; i++)
            {
                if (scaleBtns[i] == null) continue;
                bool active = (_osdScaleTier == i);
                scaleBtns[i].BackColor = active ? accent : surface2;
                scaleBtns[i].ForeColor = active ? activeFg : text;
                scaleBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Layout button
            if (_osdBtnLayoutH != null)
            {
                bool activeH = !_overlayVertical;
                _osdBtnLayoutH.BackColor = activeH ? accent : surface2;
                _osdBtnLayoutH.ForeColor = activeH ? activeFg : text;
                _osdBtnLayoutH.Font = activeH ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnLayoutV != null)
            {
                bool activeV = _overlayVertical;
                _osdBtnLayoutV.BackColor = activeV ? accent : surface2;
                _osdBtnLayoutV.ForeColor = activeV ? activeFg : text;
                _osdBtnLayoutV.Font = activeV ? TabFontBold : TabFontRegular;
            }

            // Highlight active Height button (75px Compact, 105px Medium, 140px Large, 180px XL)
            if (_osdBtnHeightCompact != null)
            {
                bool active = (_osdGraphHeight <= 85);
                _osdBtnHeightCompact.BackColor = active ? accent : surface2;
                _osdBtnHeightCompact.ForeColor = active ? activeFg : text;
                _osdBtnHeightCompact.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightMedium != null)
            {
                bool active = (_osdGraphHeight > 85 && _osdGraphHeight <= 120);
                _osdBtnHeightMedium.BackColor = active ? accent : surface2;
                _osdBtnHeightMedium.ForeColor = active ? activeFg : text;
                _osdBtnHeightMedium.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightLarge != null)
            {
                bool active = (_osdGraphHeight > 120 && _osdGraphHeight <= 160);
                _osdBtnHeightLarge.BackColor = active ? accent : surface2;
                _osdBtnHeightLarge.ForeColor = active ? activeFg : text;
                _osdBtnHeightLarge.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightXl != null)
            {
                bool active = (_osdGraphHeight > 160);
                _osdBtnHeightXl.BackColor = active ? accent : surface2;
                _osdBtnHeightXl.ForeColor = active ? activeFg : text;
                _osdBtnHeightXl.Font = active ? TabFontBold : TabFontRegular;
            }

            // Style quick snap buttons
            Button[] snapBtns = new[] { _osdQuickPosTl, _osdQuickPosTc, _osdQuickPosTr, _osdQuickPosBl, _osdQuickPosBc, _osdQuickPosBr };
            foreach (Button b in snapBtns)
            {
                if (b != null)
                {
                    b.BackColor = surface2;
                    b.ForeColor = text;
                }
            }

            // Style quick action buttons (Select All / Deselect All / Defaults)
            if (_osdSelectAllBtn != null)
            {
                _osdSelectAllBtn.BackColor = surface2;
                _osdSelectAllBtn.ForeColor = text;
            }
            if (_osdDeselectAllBtn != null)
            {
                _osdDeselectAllBtn.BackColor = surface2;
                _osdDeselectAllBtn.ForeColor = text;
            }
            if (_osdResetDefaultsBtn != null)
            {
                _osdResetDefaultsBtn.BackColor = surface2;
                _osdResetDefaultsBtn.ForeColor = text;
            }

            // Style quick toggle buttons
            if (_osdToggleOverlayBtn != null)
            {
                _osdToggleOverlayBtn.BackColor = _showOverlay ? accent : surface2;
                _osdToggleOverlayBtn.ForeColor = _showOverlay ? activeFg : text;
            }
            if (_osdToggleLockBtn != null)
            {
                _osdToggleLockBtn.BackColor = _lockOverlay ? (isRadeon ? RadeonOrange : Color.FromArgb(0xEA, 0x58, 0x0C)) : surface2;
                _osdToggleLockBtn.ForeColor = _lockOverlay ? Color.White : text;
            }

            if (_optCardOsdConfigBtn != null)
            {
                _optCardOsdConfigBtn.BackColor = surface2;
                _optCardOsdConfigBtn.ForeColor = text;
            }

            if (_osdOpacitySlider != null) _osdOpacitySlider.BackColor = surface;
            if (_osdOpacityPctLabel != null) _osdOpacityPctLabel.ForeColor = text;

            // Style all section labels inside OSD Studio
            if (_osdTitleLabel != null) { _osdTitleLabel.BackColor = surface; _osdTitleLabel.ForeColor = accent; }
            if (_osdGraphModeLabel != null) { _osdGraphModeLabel.BackColor = surface; _osdGraphModeLabel.ForeColor = text; }
            if (_osdGraphHeightLabel != null) { _osdGraphHeightLabel.BackColor = surface; _osdGraphHeightLabel.ForeColor = text; }
            if (_osdScaleLabel != null) { _osdScaleLabel.BackColor = surface; _osdScaleLabel.ForeColor = text; }
            if (_osdLayoutLabel != null) { _osdLayoutLabel.BackColor = surface; _osdLayoutLabel.ForeColor = text; }
            if (_osdOpacityLabel != null) { _osdOpacityLabel.BackColor = surface; _osdOpacityLabel.ForeColor = text; }
            if (_osdSnapLabel != null) { _osdSnapLabel.BackColor = surface; _osdSnapLabel.ForeColor = text; }
            if (_osdMetricsHeaderLabel != null) { _osdMetricsHeaderLabel.BackColor = surface; _osdMetricsHeaderLabel.ForeColor = text; }
            if (_osdTipLabel != null)
            {
                _osdTipLabel.BackColor = surface2;
                _osdTipLabel.ForeColor = textDim;
            }

            // CheckBoxes inside studio
            CheckBox[] cbs = new[]
            {
                _osdShowCpuCb, _osdShowGpuCb, _osdShowNvmeCb, _osdShowRamCb,
                _osdShowApuFanCb, _osdShowSysFanCb, _osdShowPowerCb, _osdShowGpuLoadCb,
                _osdShowFpsCb, _osdShowLowsCb,
                _osdShowModeCb, _osdShowPeaksCb, _osdShowRadeonCb, _osdShowDriverVerCb,
                _osdShowClocksCb, _osdSparklineBgCb, _osdMatchThemeCb,
                _osdShowLiveTrendCb, _osdShowFpsGraphCb, _osdPlotFpsOnTrendCb, _osdDfGraphStyleCb, _osdDfGraphFirstCb
            };
            foreach (CheckBox cb in cbs)
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                }
            }
        }

        private void UpdateCurveHint()
        {
            if (_curveHintLabel == null || _curveCanvas == null) return;
            if (_showingOsdConfig)
            {
                _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                return;
            }
            if (_showingLiveTelemetry)
            {
                string hlName = _highlightedMetric == 2 ? "iGPU Temp"
                    : (_highlightedMetric == 6 ? "NVMe Temp"
                    : (_highlightedMetric == 3 ? "APU Power"
                    : (_highlightedMetric == 7 ? "RAM Power"
                    : (_highlightedMetric == 4 ? "APU Fan Speed"
                    : (_highlightedMetric == 5 ? "System Fan Speed" : "CPU Temp")))));
                _curveHintLabel.Text = string.Format("Spotlighting {0} — click metric boxes or legend pills to switch, hover to inspect", hlName);
                return;
            }
            FanCurveProfile prof = GetSelectedTabProfile();
            string whichFan = _curveCanvas.EditSysFan ? "Sys Fan" : "APU Fan";
            if (prof.IsBuiltIn)
            {
                _curveHintLabel.Text = string.Format("{0}: '{1}' — drag points to customize, or click any metric box for Live Trend", whichFan, prof.Name);
            }
            else
            {
                _curveHintLabel.Text = string.Format("Editing {0} curve '{1}' — click any metric box to view Live Trend", whichFan, prof.Name);
            }
        }

        private void UpdateCalibrationBadge()
        {
            if (_calibBadgeLabel == null) return;
            bool isCalibrated, isStale;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            _calibBadgeLabel.Text = FanCurveProfile.FormatCalibrationStatusBadge(_hasDualFans, out isCalibrated, out isStale);
            _calibBadgeLabel.BackColor = isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg));
            if (!isCalibrated)
            {
                _calibBadgeLabel.ForeColor = isRadeon ? RadeonTextDim : (_isDarkMode ? DarkTextDim : LightTextDim);
            }
            else if (isStale)
            {
                _calibBadgeLabel.ForeColor = _isDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xB4, 0x53, 0x09);
            }
            else
            {
                _calibBadgeLabel.ForeColor = _isDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x15, 0x80, 0x3D);
            }
        }

        private string GetThemeButtonLabel()
        {
            switch (_themePref)
            {
                case ThemePreference.Dark: return "Theme: Dark";
                case ThemePreference.Radeon: return "Theme: AMD Radeon";
                case ThemePreference.Afterburner: return "Theme: Afterburner";
                case ThemePreference.Light: return "Theme: Light";
                default: return string.Format("Theme: System ({0})", _isDarkMode ? "Dark" : "Light");
            }
        }

        private void CycleThemePreference()
        {
            if (_themePref == ThemePreference.System) _themePref = ThemePreference.Dark;
            else if (_themePref == ThemePreference.Dark) _themePref = ThemePreference.Radeon;
            else if (_themePref == ThemePreference.Radeon) _themePref = ThemePreference.Afterburner;
            else if (_themePref == ThemePreference.Afterburner) _themePref = ThemePreference.Light;
            else _themePref = ThemePreference.System;

            SaveSettings();
            DetectSystemTheme(out _isDarkMode, out _isTaskbarDark);
            _lastCpuIconKey = int.MinValue;
            _lastGpuIconKey = int.MinValue;
            ApplyThemeToControls();
            RefreshStatus();
        }

        private void ApplyThemeToControls()
        {
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color bg = isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg));
            Color surface = isRadeon ? RadeonSurface : (IsAb ? AbSurface : (_isDarkMode ? DarkSurface : LightSurface));
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            Color textDim = isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim));
            Color accent = isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent));

            Color tempBoxBg = isRadeon ? Color.FromArgb(0x0D, 0x05, 0x07) : (IsAb ? Color.FromArgb(0x0B, 0x0D, 0x10) : (_isDarkMode ? Color.Black : Color.White));
            Color tempBoxFg = _isDarkMode ? Color.White : Color.Black;
            Color tempBoxTitleFg = isRadeon
                ? Color.FromArgb(0xFD, 0xA4, 0xAF)
                : (IsAb ? AbGreen : (_isDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x47, 0x55, 0x69)));

            BackColor = bg;
            ForeColor = text;

            if (_headerLogoBox != null)
            {
                _headerLogoBox.BackColor = surface;
                Image oldImg = _headerLogoBox.Image;
                _headerLogoBox.Image = AppLogoRenderer.RenderConcept(isRadeon ? 4 : (IsAb ? 5 : 1), 192);
                if (oldImg != null) oldImg.Dispose();
            }

            if (_hwBadgeLabel != null)
            {
                _hwBadgeLabel.BackColor = bg;
                _hwBadgeLabel.ForeColor = text;
            }

            if (_hwSubLabel != null)
            {
                _hwSubLabel.BackColor = bg;
                _hwSubLabel.ForeColor = textDim;
            }

            if (_driversMenuBtn != null)
            {
                _driversMenuBtn.BackColor = surface2;
                _driversMenuBtn.ForeColor = text;
            }

            if (_themeToggleBtn != null)
            {
                _themeToggleBtn.Text = GetThemeButtonLabel();
                _themeToggleBtn.BackColor = isRadeon ? RadeonAccent : (IsAb ? AbGreen : surface2);
                _themeToggleBtn.ForeColor = isRadeon ? Color.White : (IsAb ? Color.Black : text);
            }

            foreach (Label h in _sectionHeaders)
                h.ForeColor = accent;

            UpdateCalibrationBadge();

            foreach (Panel c in _sectionCards)
                c.BackColor = surface;

            foreach (Label l in _staticLabels)
            {
                l.BackColor = surface;
                l.ForeColor = textDim;
            }

            if (_cpuTempBox != null)
            {
                _cpuTempBox.BackColor = tempBoxBg;
                _cpuBoxTitle.BackColor = tempBoxBg;
                _cpuBoxTitle.ForeColor = tempBoxTitleFg;
                _cpuTempLabel.BackColor = tempBoxBg;
                _cpuTempLabel.ForeColor = tempBoxFg;
            }

            if (_gpuTempBox != null)
            {
                _gpuTempBox.BackColor = tempBoxBg;
                _gpuBoxTitle.BackColor = tempBoxBg;
                _gpuBoxTitle.ForeColor = tempBoxTitleFg;
                _gpuTempLabel.BackColor = tempBoxBg;
                _gpuTempLabel.ForeColor = tempBoxFg;
            }

            if (_pwrBox != null)
            {
                _pwrBox.BackColor = tempBoxBg;
                _pwrBoxTitle.BackColor = tempBoxBg;
                _pwrBoxTitle.ForeColor = tempBoxTitleFg;
                _pwrLabel.BackColor = tempBoxBg;
                _pwrLabel.ForeColor = tempBoxFg;
            }

            if (_fan1Box != null)
            {
                _fan1Box.BackColor = tempBoxBg;
                if (_fan1BoxTitle != null)
                {
                    _fan1BoxTitle.BackColor = tempBoxBg;
                    _fan1BoxTitle.ForeColor = tempBoxTitleFg;
                }
                if (_fan1RpmLabel != null)
                {
                    _fan1RpmLabel.BackColor = tempBoxBg;
                    _fan1RpmLabel.ForeColor = tempBoxFg;
                }
            }

            if (_fan2Box != null)
            {
                _fan2Box.BackColor = tempBoxBg;
                if (_fan2BoxTitle != null)
                {
                    _fan2BoxTitle.BackColor = tempBoxBg;
                    _fan2BoxTitle.ForeColor = tempBoxTitleFg;
                }
                if (_fan2RpmLabel != null)
                {
                    _fan2RpmLabel.BackColor = tempBoxBg;
                    _fan2RpmLabel.ForeColor = tempBoxFg;
                }
            }

            if (_nvmeTempBox != null)
            {
                _nvmeTempBox.BackColor = tempBoxBg;
                _nvmeBoxTitle.BackColor = tempBoxBg;
                _nvmeBoxTitle.ForeColor = tempBoxTitleFg;
                _nvmeTempLabel.BackColor = tempBoxBg;
                _nvmeTempLabel.ForeColor = tempBoxFg;
            }

            if (_ramPowerBox != null)
            {
                _ramPowerBox.BackColor = tempBoxBg;
                _ramBoxTitle.BackColor = tempBoxBg;
                _ramBoxTitle.ForeColor = tempBoxTitleFg;
                _ramPowerLabel.BackColor = tempBoxBg;
                _ramPowerLabel.ForeColor = tempBoxFg;
            }

            if (_ramCard != null)
            {
                _ramCard.BackColor = tempBoxBg;
                if (_ramLabel != null)
                {
                    _ramLabel.BackColor = tempBoxBg;
                    _ramLabel.ForeColor = text;
                }
            }

            _modeLabel.BackColor = surface;
            _modeLabel.ForeColor = accent;

            _pct1Label.BackColor = surface;
            _pct2Label.BackColor = surface;

            if (_customModeCombo != null)
            {
                _customModeCombo.BackColor = surface2;
                _customModeCombo.ForeColor = text;
            }

            if (_samplingCombo != null)
            {
                _samplingCombo.BackColor = surface2;
                _samplingCombo.ForeColor = text;
            }
            if (_samplingLabel != null)
            {
                _samplingLabel.ForeColor = textDim;
            }

            if (_curveCanvas != null)
            {
                _curveCanvas.IsDarkMode = _isDarkMode;
                _curveCanvas.IsRadeonTheme = isRadeon; _curveCanvas.IsAfterburnerTheme = IsAb;
                _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.IsDarkMode = _isDarkMode;
                _liveCanvas.IsRadeonTheme = isRadeon; _liveCanvas.IsAfterburnerTheme = IsAb;
                _liveCanvas.Invalidate();
            }

            UpdateCurveTabStyles();

            if (_curveHintLabel != null)
            {
                _curveHintLabel.BackColor = surface;
                _curveHintLabel.ForeColor = textDim;
                UpdateCurveHint();
            }

            _fan1Slider.BackColor = surface;
            _fan2Slider.BackColor = surface;
            _fan1SliderPct.BackColor = surface;
            _fan1SliderPct.ForeColor = text;
            _fan2SliderPct.BackColor = surface;
            _fan2SliderPct.ForeColor = text;

            foreach (Button b in new[] { _addCustomBtn, _deleteCustomBtn, _fan1AutoBtn, _fan2AutoBtn, _allAutoBtn, _closeBtn })
            {
                if (b != null)
                {
                    b.BackColor = surface2;
                    b.ForeColor = text;
                }
            }

            foreach (CheckBox cb in new[] { _showCpuTrayCb, _showGpuTrayCb, _showOverlayCb, _lockOverlayCb, _autostartCb, _alwaysOnTopCb, _trayCb })
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                }
            }

            if (_errorBanner != null)
            {
                _errorBanner.BackColor = _isDarkMode ? ErrorBgDark : ErrorBgLight;
                _errorBanner.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
            }

            if (_customLabel != null)
            {
                _customLabel.BackColor = surface;
                _customLabel.ForeColor = textDim;
            }
            if (_optCardOsdConfigBtn != null)
            {
                _optCardOsdConfigBtn.BackColor = surface2;
                _optCardOsdConfigBtn.ForeColor = text;
            }
            if (_overlayForm != null)
            {
                _overlayForm.IsRadeonTheme = isRadeon; _overlayForm.IsAfterburnerTheme = IsAb;
                _overlayForm.Invalidate();
            }
            UpdateOsdStudioStyles();

            UpdateModeButtonStyles(_lastSelectedModeCode);
        }

        private void UpdateModeButtonStyles(int activeCode)
        {
            _lastSelectedModeCode = activeCode;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
            Color accent = editSys
                ? (isRadeon ? RadeonOrange : (IsAb ? AbCyan : (_isDarkMode ? DarkTeal : LightTeal)))
                : (isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent)));
            Color activeFg = isRadeon ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            FanCurveProfile tabProf = GetSelectedTabProfile();
            bool tabManual = editSys ? _sysManualOverride : _apuManualOverride;

            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] == null) continue;
                bool isSelected = (!tabManual && tabProf != null && tabProf.IsBuiltIn && tabProf.BuiltInCode == i);
                if (isSelected)
                {
                    _modeButtons[i].BackColor = accent;
                    _modeButtons[i].ForeColor = activeFg;
                    if (_modeButtons[i].Font != ModeFontBold) _modeButtons[i].Font = ModeFontBold;
                }
                else
                {
                    _modeButtons[i].BackColor = surface2;
                    _modeButtons[i].ForeColor = text;
                    if (_modeButtons[i].Font != ModeFontRegular) _modeButtons[i].Font = ModeFontRegular;
                }
            }

            if (_activateCustomBtn != null)
            {
                if (!tabManual && tabProf != null && !tabProf.IsBuiltIn)
                {
                    _activateCustomBtn.BackColor = accent;
                    _activateCustomBtn.ForeColor = activeFg;
                }
                else
                {
                    _activateCustomBtn.BackColor = surface2;
                    _activateCustomBtn.ForeColor = text;
                }
            }
        }

        private string FormatRpmText(int rpm, bool manualSliderOverride, int manualPct, bool isRamping)
        {
            if (isRamping)
            {
                return string.Format("{0} RPM ↻", rpm);
            }
            if (rpm == 0)
            {
                if (manualSliderOverride && manualPct > 0 && manualPct <= 27)
                    return "0 RPM (Stall)";
                return "0 RPM (Idle)";
            }
            return string.Format("{0} RPM", rpm);
        }

        private bool EvaluateAndApplyFanProfiles(int apuTemp)
        {
            if (_apuProfile == null) _apuProfile = _builtInProfiles[0];
            if (_sysProfile == null) _sysProfile = _builtInProfiles[0];

            bool wroteHardware = false;

            // Emergency Thermal Safety Guard (TJMax Tripwire):
            // If APU temperature hits >= 90°C, bypass manual overrides and curves, forcing 100% PWM.
            // Hysteresis holds until temperature recovers below 75°C.
            if (HardwareBackend.CheckEmergencyThermalTripwire(apuTemp, ref _emergencyThermalActive))
            {
                if (_lastAppliedApuPct != 100)
                {
                    bool ok = _demoMode ? _demo.SetFan(1, 100) : _hw.SetFan(1, 100);
                    if (ok)
                    {
                        _lastAppliedApuPct = 100;
                        wroteHardware = true;
                        ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError("Emergency APU Fan 100% write rejected / read-back verification failed");
                    }
                }
                if (_hasDualFans && _lastAppliedSysPct != 100)
                {
                    bool ok = _demoMode ? _demo.SetFan(2, 100) : _hw.SetFan(2, 100);
                    if (ok)
                    {
                        _lastAppliedSysPct = 100;
                        wroteHardware = true;
                        ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError("Emergency Sys Fan 100% write rejected / read-back verification failed");
                    }
                }
                return wroteHardware;
            }

            // Determine which built-in EC mode register (0x32) to use:
            // Prefer APU Fan's built-in mode so APU Fan can run on native EC Auto (0x00),
            // or if APU Fan is custom/manual, use Sys Fan's built-in mode so Sys Fan can run on native EC Auto!
            int desiredEcMode = -1;
            bool isSuperIo = (_hw != null && _hw.SuperIoBaseAddr != 0);
            bool apuUsesNativeEcAuto = false;
            bool sysUsesNativeEcAuto = false;

            if (!isSuperIo)
            {
                if (!_apuManualOverride && _apuProfile.IsBuiltIn)
                {
                    desiredEcMode = _apuProfile.BuiltInCode;
                    apuUsesNativeEcAuto = true;
                    if (_hasDualFans && !_sysManualOverride && _sysProfile.IsBuiltIn && _sysProfile.BuiltInCode == desiredEcMode)
                    {
                        sysUsesNativeEcAuto = true;
                    }
                }
                else if (_hasDualFans && !_sysManualOverride && _sysProfile.IsBuiltIn)
                {
                    desiredEcMode = _sysProfile.BuiltInCode;
                    sysUsesNativeEcAuto = true;
                }

                // If NVMe SSD is hot (>= 65°C), override native EC auto for Fan 2 so Fan 2 actively cools the SSD
                if (sysUsesNativeEcAuto && _lastStatus.NvmeTemp >= 65)
                {
                    sysUsesNativeEcAuto = false;
                }
            }

            // 2°C cooldown hysteresis for software curve updates
            bool withinCooldownHysteresis = (_lastCustomEvalTemp > 0 && apuTemp < _lastCustomEvalTemp && (_lastCustomEvalTemp - apuTemp) < 2);

            try
            {
                if (desiredEcMode >= 0 && _lastAppliedEcMode != desiredEcMode)
                {
                    bool ok = _demoMode ? _demo.SetMode(desiredEcMode) : _hw.SetMode(desiredEcMode);
                    if (ok)
                    {
                        _lastAppliedEcMode = desiredEcMode;
                        wroteHardware = true;
                        ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError(string.Format("EC Mode change (0x{0:X2}) verification failed", desiredEcMode));
                    }
                }

                // 1. Apply APU Fan (Fan 1)
                if (!_apuManualOverride)
                {
                    if (apuUsesNativeEcAuto)
                    {
                        if (_lastAppliedApuPct != 0)
                        {
                            bool ok = _demoMode ? _demo.SetFan(1, 0) : _hw.SetFan(1, 0);
                            if (ok)
                            {
                                _lastAppliedApuPct = 0;
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError("APU Fan reset to Auto (0%) write verification failed");
                            }
                        }
                    }
                    else
                    {
                        int targetApu = _apuProfile.EvaluateApu(apuTemp);
                        int regPct = targetApu <= 0 ? (isSuperIo ? 0 : 1) : targetApu;
                        if (_lastAppliedApuPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - _lastAppliedApuPct) >= 2) || regPct == 1 || regPct == 100)
                        {
                            bool ok = _demoMode ? _demo.SetFan(1, regPct) : _hw.SetFan(1, regPct);
                            if (ok)
                            {
                                _lastAppliedApuPct = regPct;
                                _lastCustomEvalTemp = apuTemp;
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError(string.Format("APU Fan speed write ({0}%) verification failed", regPct));
                            }
                        }
                    }
                }

                // 2. Apply System RAM/NVMe Fan (Fan 2) only if device has 2 fans
                if (_hasDualFans && !_sysManualOverride)
                {
                    if (sysUsesNativeEcAuto)
                    {
                        if (_lastAppliedSysPct != 0)
                        {
                            bool ok = _demoMode ? _demo.SetFan(2, 0) : _hw.SetFan(2, 0);
                            if (ok)
                            {
                                _lastAppliedSysPct = 0;
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError("Sys Fan reset to Auto (0%) write verification failed");
                            }
                        }
                    }
                    else
                    {
                        int sysThermalSource = (_lastStatus.NvmeTemp > 0) ? Math.Max(apuTemp, _lastStatus.NvmeTemp) : apuTemp;
                        int targetSys = _sysProfile.EvaluateSys(sysThermalSource);
                        int regPct = targetSys <= 0 ? (isSuperIo ? 0 : 1) : targetSys;
                        if (_lastAppliedSysPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - _lastAppliedSysPct) >= 2) || regPct == 1 || regPct == 100)
                        {
                            bool ok = _demoMode ? _demo.SetFan(2, regPct) : _hw.SetFan(2, regPct);
                            if (ok)
                            {
                                _lastAppliedSysPct = regPct;
                                _lastCustomEvalTemp = apuTemp;
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError(string.Format("Sys Fan speed write ({0}%) verification failed", regPct));
                            }
                        }
                    }
                }
            }
            catch { }
            return wroteHardware;
        }

        private void RecordHardwareWriteError(string errorDetail)
        {
            _consecutiveWriteFailures++;
            _lastHardwareWriteError = errorDetail;
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _errorBanner.Text = string.Format("⚠️ Hardware Write Failure: {0} (Attempt {1})", errorDetail, _consecutiveWriteFailures);
                        _errorBanner.Visible = true;
                        _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                        _statusBarLabel.Text = string.Format("⚠️ Hardware write rejected ({0} retries)", _consecutiveWriteFailures);
                    }));
                }
                catch { }
            }
        }

        private void ClearHardwareWriteError()
        {
            if (_consecutiveWriteFailures > 0 || _lastHardwareWriteError != null)
            {
                _consecutiveWriteFailures = 0;
                _lastHardwareWriteError = null;
                if (IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            _errorBanner.Visible = false;
                            if (_statusBarLabel != null) { _statusBarLabel.Text = "Ready"; }
                        }));
                    }
                    catch { }
                }
            }
        }

        private void HardwareWorkerLoop()
        {
            while (_hwThreadRunning)
            {
                if (!_hwWorkerPaused)
                {
                    try
                    {
                        FanStatus st = _demoMode ? _demo.GetStatus() : _hw.GetStatus();
                        int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);

                        bool hwChanged = EvaluateAndApplyFanProfiles(apuTemp);
                        if (hwChanged)
                        {
                            st = _demoMode ? _demo.GetStatus() : _hw.GetStatus();
                        }

                        _lastStatus = st;

                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke((Action)(() => OnHardwareWorkerSample(st)));
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke((Action)(() => OnHardwareWorkerError(ex)));
                            }
                            catch { }
                        }
                    }
                }

                _lastWorkerSampleUtc = DateTime.UtcNow;

                // Child supervisor monitoring: Check if watchdog process crashed or was terminated
                if (_watchdogStarted && _watchdogProcess != null && _watchdogProcess.HasExited)
                {
                    _watchdogStarted = false;
                    int exitCode = -1;
                    try { exitCode = _watchdogProcess.ExitCode; } catch { }
                    WatchdogSupervisor.LogWatchdogEvent(string.Format("ALERT: Safety watchdog supervisor process PID {0} exited unexpectedly (code {1}). Attempting automatic restart...",
                        _watchdogProcess.Id, exitCode));
                    if (IsHandleCreated && !IsDisposed)
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _errorBanner.Text = string.Format("⚠️ Safety Watchdog process stopped (ExitCode: {0}). Restarting supervisor...", exitCode);
                                _errorBanner.Visible = true;
                                StartWatchdogSupervisor();
                            }));
                        }
                        catch { }
                    }
                }

                int waitMs = _refreshIntervalMs;
                if (DateTime.UtcNow < _rampUntil) waitMs = Math.Min(waitMs, 250);
                _hwWakeEvent.WaitOne(waitMs);
            }
        }

        private void OnHardwareWorkerSample(FanStatus st)
        {
            try
            {
                if ((DateTime.UtcNow - _lastWorkerSampleUtc).TotalMilliseconds < 4500)
                {
                    PulseWatchdogHeartbeat();
                }
                ApplyStatusToUi(st);
            }
            catch { }
        }

        private void OnHardwareWorkerError(Exception ex)
        {
            _errorBanner.Text = "⚠️ " + ex.Message;
            _errorBanner.Visible = true;
            _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
            _statusBarLabel.Text = "⚠️ Driver/Hardware probe notice (see banner)";
        }

        private void ApplyStatusToUi(FanStatus st)
        {
            if (!string.IsNullOrEmpty(_lastHardwareWriteError))
            {
                _errorBanner.Text = string.Format("⚠️ Hardware Write Verification Failed: {0} (Failures: {1}, Retrying...)", _lastHardwareWriteError, _consecutiveWriteFailures);
                _errorBanner.Visible = true;
                _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                _statusBarLabel.Text = string.Format("⚠️ Hardware write rejected ({0} retries)", _consecutiveWriteFailures);
            }
            else
            {
                _errorBanner.Visible = false;
            }
            _sessionStats.Record(st);

            bool isRamping = DateTime.UtcNow < _rampUntil;

            UpdateTrayIcons(st.CpuTemp, st.GpuTemp);

            if (_cpuTrayIcon != null)
            {
                string tip = string.Format("CPU: {0}°C (iGPU: {1}°C)\nAPU: {2} RPM | Sys: {3} RPM",
                    st.CpuTemp, st.GpuTemp, st.Fan1Rpm, st.Fan2Rpm);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_cpuTrayIcon.Text != tip) _cpuTrayIcon.Text = tip;
            }

            if (_gpuTrayIcon != null)
            {
                string tip = string.Format("iGPU: {0}°C (CPU: {1}°C)\nAPU: {2} RPM | Sys: {3} RPM",
                    st.GpuTemp, st.CpuTemp, st.Fan1Rpm, st.Fan2Rpm);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_gpuTrayIcon.Text != tip) _gpuTrayIcon.Text = tip;
            }

            string apuModeName = _apuManualOverride ? "Man" : (_apuProfile != null ? _apuProfile.Name : st.Mode);
            string sysModeName = _sysManualOverride ? "Man" : (_sysProfile != null ? _sysProfile.Name : st.Mode);

            if (_overlayForm != null && _showOverlay)
            {
                string osdMode = (!_hasDualFans || apuModeName == sysModeName)
                    ? ShortModeTag(apuModeName)
                    : string.Format("{0}/{1}", ShortModeTag(apuModeName), ShortModeTag(sysModeName));
                _overlayForm.UpdateMetrics(st, osdMode, _hasDualFans);
            }

            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            Text = string.Format("CPU {0}°C | iGPU {1}°C - MiniFanControl", st.CpuTemp, st.GpuTemp);

            if (_hwSubLabel != null && !string.IsNullOrEmpty(st.RadeonSoftwareVer))
            {
                string drvTag = "Radeon Software " + st.RadeonSoftwareVer;
                if (_hwSubLabel.Text.IndexOf("Radeon Software", StringComparison.Ordinal) < 0)
                    _hwSubLabel.Text = _hwSubLabel.Text + " • " + drvTag;
            }

            int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);
            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.CurrentApuTemp = apuTemp;
                if (!_showingLiveTelemetry)
                    _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null && _showingLiveTelemetry)
            {
                _liveCanvas.Invalidate();
            }

            Color tempFg = _isDarkMode ? DarkText : LightText;
            Color dimText = _isDarkMode ? Color.FromArgb(0x93, 0x99, 0xB2) : Color.FromArgb(0x6C, 0x70, 0x86);
            Color rampColor = _isDarkMode ? Color.FromArgb(0xFA, 0xB3, 0x87) : Color.FromArgb(0xEA, 0x58, 0x0C);
            bool isRadeon = (_themePref == ThemePreference.Radeon);

            _cpuTempLabel.Text = st.CpuTemp > 0 ? string.Format("{0}°C", st.CpuTemp) : "N/A";
            _cpuTempLabel.ForeColor = tempFg;
            if (_cpuBoxTitle != null)
            {
                string clkPart = st.CpuClockMhz > 0 ? string.Format(" • {0:0.0}G", st.CpuClockMhz / 1000.0) : "";
                string cpuHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxCpuTemp > 0)
                    ? string.Format("CPU • MAX {0}°{1}", _sessionStats.MaxCpuTemp, clkPart)
                    : (st.CpuClockMhz > 0 ? string.Format("CPU • {0:0.0} GHz", st.CpuClockMhz / 1000.0) : "CPU TEMP");
                if (_cpuBoxTitle.Text != cpuHeader) _cpuBoxTitle.Text = cpuHeader;
            }
            if (_uiToolTip != null && _cpuTempBox != null)
            {
                if (!IsBoxHovered(_cpuTempBox, _cpuBoxTitle, _cpuTempLabel))
                {
                    string cpuTip = st.FormatCpuTooltip(_sessionStats);
                    if (cpuTip != _lastCpuTooltipText)
                    {
                        _lastCpuTooltipText = cpuTip;
                        _uiToolTip.SetToolTip(_cpuTempBox, cpuTip);
                        _uiToolTip.SetToolTip(_cpuBoxTitle, cpuTip);
                        _uiToolTip.SetToolTip(_cpuTempLabel, cpuTip);
                    }
                }
                _cpuTempBox.Invalidate();
            }

            _gpuTempLabel.Text = st.GpuTemp > 0 ? string.Format("{0}°C", st.GpuTemp) : "N/A";
            _gpuTempLabel.ForeColor = tempFg;
            if (_gpuBoxTitle != null)
            {
                string hotPart = st.GpuHotspotTemp > 0 ? string.Format(" • 🔥{0}°", st.GpuHotspotTemp) : "";
                string gpuHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuTemp > 0)
                    ? string.Format("iGPU • MAX {0}°{1}", _sessionStats.MaxGpuTemp, hotPart)
                    : (st.GpuHotspotTemp > 0 ? string.Format("iGPU • 🔥{0}°C", st.GpuHotspotTemp) : "iGPU TEMP");
                if (_gpuBoxTitle.Text != gpuHeader) _gpuBoxTitle.Text = gpuHeader;
            }
            if (_uiToolTip != null && _gpuTempBox != null)
            {
                if (!IsBoxHovered(_gpuTempBox, _gpuBoxTitle, _gpuTempLabel))
                {
                    string igpuTip = st.FormatIgpuTooltip(_sessionStats);
                    if (igpuTip != _lastIgpuTooltipText)
                    {
                        _lastIgpuTooltipText = igpuTip;
                        _uiToolTip.SetToolTip(_gpuTempBox, igpuTip);
                        _uiToolTip.SetToolTip(_gpuBoxTitle, igpuTip);
                        _uiToolTip.SetToolTip(_gpuTempLabel, igpuTip);
                    }
                }
                _gpuTempBox.Invalidate();
            }

            if (_nvmeTempLabel != null)
            {
                _nvmeTempLabel.Text = st.NvmeTemp > 0 ? string.Format("{0}°C", st.NvmeTemp) : "N/A";
                Color nvmeFg = (st.NvmeTemp >= 70) ? Color.FromArgb(0xFB, 0x71, 0x85) : tempFg;
                _nvmeTempLabel.ForeColor = nvmeFg;
                if (_nvmeBoxTitle != null)
                {
                    string nvmeHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxNvmeTemp > 0)
                        ? string.Format("NVMe • MAX {0}°", _sessionStats.MaxNvmeTemp)
                        : "NVMe TEMP";
                    if (_nvmeBoxTitle.Text != nvmeHeader) _nvmeBoxTitle.Text = nvmeHeader;
                }
                if (_uiToolTip != null && _nvmeTempBox != null)
                {
                    if (!IsBoxHovered(_nvmeTempBox, _nvmeBoxTitle, _nvmeTempLabel))
                    {
                        string nvmeTip = st.FormatNvmeTooltip(_sessionStats);
                        if (nvmeTip != _lastNvmeTooltipText)
                        {
                            _lastNvmeTooltipText = nvmeTip;
                            _uiToolTip.SetToolTip(_nvmeTempBox, nvmeTip);
                            _uiToolTip.SetToolTip(_nvmeBoxTitle, nvmeTip);
                            _uiToolTip.SetToolTip(_nvmeTempLabel, nvmeTip);
                        }
                    }
                    _nvmeTempBox.Invalidate();
                }
            }

            if (_pwrLabel != null)
            {
                _pwrLabel.Text = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                _pwrLabel.ForeColor = tempFg;
                if (_pwrBoxTitle != null)
                {
                    string limPart = st.ApuPowerLimitWatts > 0 ? string.Format("/{0}W", st.ApuPowerLimitWatts) : "";
                    string pwrHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxPowerWatts > 0)
                        ? string.Format("PWR • MAX {0}W{1}", _sessionStats.MaxPowerWatts, limPart)
                        : (st.ApuPowerLimitWatts > 0 ? string.Format("PWR • {0}W LIMIT", st.ApuPowerLimitWatts) : "POWER •");
                    if (_pwrBoxTitle.Text != pwrHeader) _pwrBoxTitle.Text = pwrHeader;
                }
                if (_uiToolTip != null && !IsBoxHovered(_pwrBox, _pwrBoxTitle, _pwrLabel))
                {
                    string pwrTip = st.FormatPowerTooltip(_sessionStats);
                    if (pwrTip != _lastPowerTooltipText)
                    {
                        _lastPowerTooltipText = pwrTip;
                        _uiToolTip.SetToolTip(_pwrBox, pwrTip);
                        _uiToolTip.SetToolTip(_pwrBoxTitle, pwrTip);
                        _uiToolTip.SetToolTip(_pwrLabel, pwrTip);
                    }
                }
                _pwrBox.Invalidate();
            }

            if (_ramPowerLabel != null)
            {
                _ramPowerLabel.Text = st.RamPowerWatts > 0 ? string.Format("~{0:F1}W", st.RamPowerWatts) : "--W";
                _ramPowerLabel.ForeColor = tempFg;
                if (_ramBoxTitle != null)
                {
                    string ramHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxRamPowerWatts > 0)
                        ? string.Format("RAM • MAX {0:F1}W", _sessionStats.MaxRamPowerWatts)
                        : "RAM POWER";
                    if (_ramBoxTitle.Text != ramHeader) _ramBoxTitle.Text = ramHeader;
                }
                if (_uiToolTip != null && _ramPowerBox != null && !IsBoxHovered(_ramPowerBox, _ramBoxTitle, _ramPowerLabel))
                {
                    string ramTip = st.FormatRamTooltip(_sessionStats);
                    if (ramTip != _lastRamTooltipText)
                    {
                        _lastRamTooltipText = ramTip;
                        _uiToolTip.SetToolTip(_ramPowerBox, ramTip);
                        _uiToolTip.SetToolTip(_ramBoxTitle, ramTip);
                        _uiToolTip.SetToolTip(_ramPowerLabel, ramTip);
                    }
                }
                if (_ramPowerBox != null) _ramPowerBox.Invalidate();
            }

            _fan1RpmLabel.Text = string.Format("{0} RPM •", st.Fan1Rpm);
            _fan1RpmLabel.ForeColor = tempFg;
            if (_fan2RpmLabel != null)
            {
                _fan2RpmLabel.Text = !_hasDualFans ? "N/A" : (st.Fan2Rpm > 0 ? string.Format("{0} RPM •", st.Fan2Rpm) : "-- RPM");
                _fan2RpmLabel.ForeColor = tempFg;
            }
            if (_fan1Box != null)
            {
                if (_fan1BoxTitle != null)
                {
                    string fan1Header = (_sessionStats.SampleCount > 0 && _sessionStats.MaxFan1Rpm > 0)
                        ? string.Format("APU • MAX {0}", _sessionStats.MaxFan1Rpm)
                        : "APU FAN";
                    if (_fan1BoxTitle.Text != fan1Header) _fan1BoxTitle.Text = fan1Header;
                }
                if (_uiToolTip != null && !IsBoxHovered(_fan1Box, _fan1BoxTitle, _fan1RpmLabel))
                {
                    string f1Tip = st.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm, _hw != null ? _hw.Profile : null);
                    if (f1Tip != _lastApuFanTooltipText)
                    {
                        _lastApuFanTooltipText = f1Tip;
                        _uiToolTip.SetToolTip(_fan1Box, f1Tip);
                        _uiToolTip.SetToolTip(_fan1BoxTitle, f1Tip);
                        _uiToolTip.SetToolTip(_fan1RpmLabel, f1Tip);
                    }
                }
                _fan1Box.Invalidate();
            }

            if (_fan2Box != null)
            {
                if (_fan2BoxTitle != null)
                {
                    string fan2Header = !_hasDualFans
                        ? "SYS FAN"
                        : ((_sessionStats.SampleCount > 0 && _sessionStats.MaxFan2Rpm > 0)
                            ? string.Format("SYS • MAX {0}", _sessionStats.MaxFan2Rpm)
                            : "SYS FAN");
                    if (_fan2BoxTitle.Text != fan2Header) _fan2BoxTitle.Text = fan2Header;
                }
                if (_uiToolTip != null && !IsBoxHovered(_fan2Box, _fan2BoxTitle, _fan2RpmLabel))
                {
                    string f2Tip = st.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans, _hw != null ? _hw.Profile : null);
                    if (f2Tip != _lastSysFanTooltipText)
                    {
                        _lastSysFanTooltipText = f2Tip;
                        _uiToolTip.SetToolTip(_fan2Box, f2Tip);
                        _uiToolTip.SetToolTip(_fan2BoxTitle, f2Tip);
                        _uiToolTip.SetToolTip(_fan2RpmLabel, f2Tip);
                    }
                }
                _fan2Box.Invalidate();
            }

            if (_ramCard != null && _ramLabel != null)
            {
                string memSpeedPart = st.MemClockMhz > 0 ? string.Format(" @ {0}MHz", st.MemClockMhz) : "";
                string ramPart = st.RamTotalGb > 0
                    ? string.Format("🧠 RAM: {0:F1} / {1:F1} GB ({2}% Load{3})", st.RamUsedGb, st.RamTotalGb, st.RamLoadPct, memSpeedPart)
                    : "🧠 RAM: N/A";

                string nvmePart = "";
                if (st.NvmeTemp > 0)
                {
                    string modelDisplay = !string.IsNullOrEmpty(st.NvmeModel) ? st.NvmeModel : "NVMe SSD";
                    if (modelDisplay.Length > 24) modelDisplay = modelDisplay.Substring(0, 22).Trim() + "…";
                    nvmePart = string.Format(" • 💾 SSD: {0} ({1}% Health)", modelDisplay, st.NvmeHealthPct);
                }

                _ramLabel.Text = ramPart + nvmePart;
                Color ramFg = (st.RamLoadPct >= 90) ? Color.FromArgb(0xFB, 0x71, 0x85) : tempFg;
                _ramLabel.ForeColor = ramFg;

                if (_uiToolTip != null && !IsMouseOverControl(_ramCard) && !IsMouseOverControl(_ramLabel))
                {
                    string ramTip = st.FormatRamTooltip(_sessionStats);
                    if (ramTip != _lastRamTooltipText)
                    {
                        _lastRamTooltipText = ramTip;
                        _uiToolTip.SetToolTip(_ramCard, ramTip);
                        _uiToolTip.SetToolTip(_ramLabel, ramTip);
                    }
                }
            }

            if (!_hasDualFans || apuModeName == sysModeName)
                _modeLabel.Text = apuModeName;
            else
                _modeLabel.Text = string.Format("{0} / {1}", apuModeName, sysModeName);

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(st.ModeCode);

            Color autoColor = _isDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x15, 0x80, 0x3D);
            Color manColor = isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (_isDarkMode ? DarkAccent : LightAccent);

            if (_apuManualOverride)
            {
                _pct1Label.Text = string.Format("{0}% (Man)", st.Fan1Pct);
                _pct1Label.ForeColor = manColor;
            }
            else
            {
                int apuEval = _apuProfile != null ? _apuProfile.EvaluateApu(apuTemp) : st.Fan1Pct;
                _pct1Label.Text = (st.Fan1Manual == 0) ? string.Format("Auto ({0})", ShortModeTag(apuModeName)) : string.Format("{0}% ({1})", apuEval, ShortModeTag(apuModeName));
                _pct1Label.ForeColor = autoColor;
            }

            if (!_hasDualFans)
            {
                _pct2Label.Text = "Disabled";
                _pct2Label.ForeColor = dimText;
            }
            else if (_sysManualOverride)
            {
                _pct2Label.Text = string.Format("{0}% (Man)", st.Fan2Pct);
                _pct2Label.ForeColor = manColor;
            }
            else
            {
                int sysThermalSource = (st.NvmeTemp > 0) ? Math.Max(apuTemp, st.NvmeTemp) : apuTemp;
                int sysEval = _sysProfile != null ? _sysProfile.EvaluateSys(sysThermalSource) : st.Fan2Pct;
                string nvmeTag = (st.NvmeTemp > apuTemp && st.NvmeTemp >= 50) ? string.Format(" • NVMe {0}°C", st.NvmeTemp) : "";
                _pct2Label.Text = (st.Fan2Manual == 0) ? string.Format("Auto ({0}{1})", ShortModeTag(sysModeName), nvmeTag) : string.Format("{0}% ({1}{2})", sysEval, ShortModeTag(sysModeName), nvmeTag);
                _pct2Label.ForeColor = autoColor;
            }

            if (_emergencyThermalActive)
            {
                _statusBarLabel.ForeColor = Color.FromArgb(0xFB, 0x71, 0x85);
                _statusBarLabel.Text = string.Format("🔥 EMERGENCY THERMAL OVERRIDE (CPU {0}°C) — Fans at 100%", apuTemp);
            }
            else if (DateTime.UtcNow < _rampUntil)
            {
                double rem = Math.Max(0.1, (_rampUntil - DateTime.UtcNow).TotalSeconds);
                _statusBarLabel.ForeColor = rampColor;
                _statusBarLabel.Text = string.Format("⏳ {0} (ramping {1:0}s…)", _pendingActionText ?? "Adjusting", Math.Ceiling(rem));
            }
            else
            {
                string prefix = _demoMode ? "⚡ DEMO MODE" : "✓ HARDWARE OK";
                _statusBarLabel.ForeColor = dimText;
                _statusBarLabel.Text = string.Format("{0}  |  {1:HH:mm:ss}", prefix, DateTime.Now);
            }
        }

        private void RefreshStatus()
        {
            _hwWakeEvent.Set();
        }

        private static string ShortModeTag(string modeName)
        {
            if (string.IsNullOrEmpty(modeName)) return "Auto";
            if (modeName.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase)) return "Bal";
            if (modeName.StartsWith("Performance", StringComparison.OrdinalIgnoreCase)) return "Perf";
            if (modeName.StartsWith("Silent", StringComparison.OrdinalIgnoreCase)) return "Sil";
            return modeName.Length > 10 ? modeName.Substring(0, 10).TrimEnd() : modeName;
        }

        private void ApplyMode(int code)
        {
            string[] names = { "Balanced", "Performance", "Silent" };
            if (code < 0 || code >= _builtInProfiles.Length) return;

            try
            {
                bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
                if (editSys)
                {
                    _sysProfile = _builtInProfiles[code];
                    _sysManualOverride = false;
                    _lastAppliedSysPct = -1;
                }
                else
                {
                    _apuProfile = _builtInProfiles[code];
                    _apuManualOverride = false;
                    _lastAppliedApuPct = -1;
                }
                _lastAppliedEcMode = -1;

                if (_curveCanvas != null)
                {
                    _curveCanvas.ApuProfile = _apuProfile;
                    _curveCanvas.SysProfile = _sysProfile;
                    _curveCanvas.Invalidate();
                }
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(code);
                UpdateCurveHint();
                SaveSettings();

                string fanLabel = editSys ? "Sys Fan" : "APU Fan";
                StartRampIndication(fanLabel + " → " + names[code], editSys ? 2 : 1);
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void ApplyFan(int fanId, int pct)
        {
            try
            {
                if (fanId == 1)
                {
                    _apuManualPct = Math.Max(0, Math.Min(100, pct));
                    _apuManualOverride = (_apuManualPct > 0);
                    _lastAppliedApuPct = -1;
                }
                else
                {
                    _sysManualPct = Math.Max(0, Math.Min(100, pct));
                    _sysManualOverride = (_sysManualPct > 0);
                    _lastAppliedSysPct = -1;
                }
                _lastAppliedEcMode = -1;

                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                SaveSettings();

                bool ok = _demoMode ? _demo.SetFan(fanId, pct) : _hw.SetFan(fanId, pct);
                if (ok)
                {
                    if (fanId == 1) _lastAppliedApuPct = pct;
                    else _lastAppliedSysPct = pct;
                    ClearHardwareWriteError();
                    string fanLabel = fanId == 1 ? "APU Fan" : "Sys Fan";
                    string desc = pct == 0 ? string.Format("{0} → {1}", fanLabel, (fanId == 1 ? _apuProfile : _sysProfile).Name) : string.Format("{0} → {1}%", fanLabel, pct);
                    StartRampIndication(desc, fanId);
                }
                else
                {
                    RecordHardwareWriteError(string.Format("Manual {0} write ({1}%) verification failed", fanId == 1 ? "APU Fan" : "Sys Fan", pct));
                }
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void ApplyAutoAll()
        {
            try
            {
                _apuManualPct = 0;
                _sysManualPct = 0;
                _apuManualOverride = false;
                _sysManualOverride = false;
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                _lastAppliedEcMode = -1;
                _apuProfile = _builtInProfiles[0];
                _sysProfile = _builtInProfiles[0];
                if (_curveCanvas != null)
                {
                    _curveCanvas.ApuProfile = _apuProfile;
                    _curveCanvas.SysProfile = _sysProfile;
                    _curveCanvas.Invalidate();
                }
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(0);
                UpdateCurveHint();
                SaveSettings();

                bool ok = _demoMode ? _demo.SetAuto() : _hw.SetAuto();
                if (ok)
                {
                    ClearHardwareWriteError();
                    StartRampIndication("Reset both fans → Balanced (Auto)", 0);
                }
                else
                {
                    RecordHardwareWriteError("Reset both fans to Auto verification failed");
                }
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private string GetAutostartPath()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startup, "MiniFanControl.bat");
        }

        private string GetLegacyAutostartPath()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startup, "K12FanControl.bat");
        }

        private static bool RunSchTasksCommand(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    proc.WaitForExit(3000);
                    return proc.HasExited && proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsScheduledTaskAutostartEnabled()
        {
            return RunSchTasksCommand(string.Format("/Query /TN \"{0}\"", SCHTASK_NAME));
        }

        private bool IsAutostartEnabled()
        {
            if (File.Exists(GetAutostartPath()) || File.Exists(GetLegacyAutostartPath()))
                return true;
            return IsScheduledTaskAutostartEnabled();
        }

        private void ToggleAutostart()
        {
            string path = GetAutostartPath();
            string legacyPath = GetLegacyAutostartPath();
            try
            {
                if (_autostartCb.Checked)
                {
                    string exePath = Application.ExecutablePath;
                    // Try creating a UAC-free Elevated Windows Scheduled Task first (RadeonTuner pattern)
                    string createArgs = string.Format("/Create /F /RL HIGHEST /SC ONLOGON /TN \"{0}\" /TR \"\\\"{1}\\\" --minimized\"", SCHTASK_NAME, exePath);
                    bool taskCreated = RunSchTasksCommand(createArgs);
                    if (taskCreated)
                    {
                        if (File.Exists(path)) File.Delete(path);
                        if (File.Exists(legacyPath)) File.Delete(legacyPath);
                        _statusBarLabel.Text = "✓ Elevated Autostart enabled (UAC-free Scheduled Task)";
                    }
                    else
                    {
                        File.WriteAllText(path, string.Format("@echo off\r\nstart \"\" \"{0}\" --minimized\r\n", exePath));
                        if (File.Exists(legacyPath)) File.Delete(legacyPath);
                        _statusBarLabel.Text = "✓ Autostart enabled (Startup folder)";
                    }
                }
                else
                {
                    RunSchTasksCommand(string.Format("/Delete /F /TN \"{0}\"", SCHTASK_NAME));
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(legacyPath)) File.Delete(legacyPath);
                    _statusBarLabel.Text = "✓ Autostart disabled";
                }
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ Autostart error: " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void MinimizeToTray()
        {
            if (!_showCpuTray && !_showGpuTray && _cpuTrayIcon != null)
            {
                _cpuTrayIcon.Visible = true;
            }
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _trayCb.Checked = false;
            if (_cpuTrayIcon != null) _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null) _gpuTrayIcon.Visible = _showGpuTray;
            RefreshStatus();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_sessionEndingHandler != null) { SystemEvents.SessionEnding -= _sessionEndingHandler; _sessionEndingHandler = null; }
            if (_powerModeChangedHandler != null) { SystemEvents.PowerModeChanged -= _powerModeChangedHandler; _powerModeChangedHandler = null; }
            StopWatchdogSupervisorGracefully();
            _hwThreadRunning = false;
            _hwWakeEvent.Set();
            if (_hwThread != null && _hwThread.IsAlive)
            {
                _hwThread.Join(500);
            }
            AppDomain.CurrentDomain.ProcessExit -= OnProcessOrCrashExit;
            AppDomain.CurrentDomain.UnhandledException -= OnProcessOrCrashExit;
            UnregisterGlobalHotkeys();
            if (_refreshTimer != null)
                _refreshTimer.Stop();
            RestoreHardwareSafeAutoIfNeeded();
            if (_headerLogoBox != null && _headerLogoBox.Image != null)
            {
                _headerLogoBox.Image.Dispose();
                _headerLogoBox.Image = null;
            }
            if (_cpuTrayIcon != null)
            {
                _cpuTrayIcon.Visible = false;
                if (_cpuTrayIcon.Icon != null)
                {
                    try { _cpuTrayIcon.Icon.Dispose(); } catch { }
                    _cpuTrayIcon.Icon = null;
                }
                _cpuTrayIcon.Dispose();
            }
            if (_gpuTrayIcon != null)
            {
                _gpuTrayIcon.Visible = false;
                if (_gpuTrayIcon.Icon != null)
                {
                    try { _gpuTrayIcon.Icon.Dispose(); } catch { }
                    _gpuTrayIcon.Icon = null;
                }
                _gpuTrayIcon.Dispose();
            }
            if (_cpuIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_cpuIconHandle);
                _cpuIconHandle = IntPtr.Zero;
            }
            if (_gpuIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_gpuIconHandle);
                _gpuIconHandle = IntPtr.Zero;
            }
            if (_overlayForm != null)
            {
                _overlayForm.Close();
                _overlayForm.Dispose();
                _overlayForm = null;
            }
            _hw.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OnFormClosing(this, new FormClosingEventArgs(CloseReason.UserClosing, false));
            }
            base.Dispose(disposing);
        }
    }

}
