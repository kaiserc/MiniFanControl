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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

        private const int WM_HOTKEY              = 0x0312;
        private const int WM_POWERBROADCAST      = 0x0218;
        private const int PBT_APMSUSPEND         = 0x0004;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const int PBT_APMRESUMESUSPEND   = 0x0007;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("4838e551-f71f-4e03-9e60-429a4c2b97c6");
        private static readonly Guid GUID_MONITOR_POWER_ON     = new Guid("02731015-4510-4526-99e6-e5a17ebd1aea");
        private static readonly Guid GUID_LOW_POWER_EPOCH      = new Guid("fc8b2612-a7f4-42f1-82bb-23d948fe4457");

        private IntPtr _hDisplayStateNotify = IntPtr.Zero;
        private IntPtr _hMonitorPowerNotify = IntPtr.Zero;
        private IntPtr _hLowPowerEpochNotify = IntPtr.Zero;

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

        public const string APP_VERSION         = "1.1.0";
        public const string SCHTASK_NAME        = "MiniFanControl_Autostart";
        public const string URL_GMKTEC_DRIVERS  = "https://www.gmktec.com/pages/drivers-and-software";
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
        private readonly AppSettings _settings = new AppSettings();
        private OsdOverlayConfig _osd { get { return _settings.Osd; } }
        private FanControllerService _controller;
        private bool _demoMode
        {
            get { return _controller != null ? _controller.DemoMode : _initialDemoMode; }
            set
            {
                _initialDemoMode = value;
                if (_controller != null) _controller.DemoMode = value;
            }
        }
        private bool _initialDemoMode;

        private bool IsAb { get { return _themePref == ThemePreference.Afterburner; } }
        private ThemePreference _themePref
        {
            get { return _settings.ThemePref; }
            set { _settings.ThemePref = value; }
        }
        private bool _isDarkMode = true;
        private bool _isTaskbarDark = true;

        private bool _showCpuTray { get { return _settings.ShowCpuTray; } set { _settings.ShowCpuTray = value; } }
        private bool _showGpuTray { get { return _settings.ShowGpuTray; } set { _settings.ShowGpuTray = value; } }
        private bool _showOverlay { get { return _settings.ShowOverlay; } set { _settings.ShowOverlay = value; } }
        private bool _lockOverlay { get { return _settings.LockOverlay; } set { _settings.LockOverlay = value; } }
        private bool _restoreAutoOnExit { get { return _settings.RestoreAutoOnExit; } set { _settings.RestoreAutoOnExit = value; } }
        private bool _closeToTray { get { return _settings.CloseToTray; } set { _settings.CloseToTray = value; } }
        private bool _useFahrenheit { get { return _settings.UseFahrenheit; } set { _settings.UseFahrenheit = value; } }
        private bool _silentSleepStandby { get { return _settings.SilentSleepStandby; } set { _settings.SilentSleepStandby = value; } }
        private bool _forceFullExit = false;
        private bool _isUnitTest = false;
        private bool _alwaysOnTop { get { return _settings.AlwaysOnTop; } set { _settings.AlwaysOnTop = value; } }
        private bool _enableGlobalHotkeys { get { return _settings.EnableGlobalHotkeys; } set { _settings.EnableGlobalHotkeys = value; } }
        private bool _hotkeysRegistered = false;
        private bool _startMinimized = false;
        private bool _safeExitRestored = false;
        private readonly TelemetrySessionStats _sessionStats = new TelemetrySessionStats();
        private CapFrameXOverlayForm _overlayForm;

        private IntPtr _cpuIconHandle = IntPtr.Zero;
        private IntPtr _gpuIconHandle = IntPtr.Zero;
        private int _lastCpuIconKey = int.MinValue;
        private int _lastGpuIconKey = int.MinValue;

        private DateTime _rampUntil = DateTime.MinValue;

        private Process _watchdogProcess;
        private EventWaitHandle _watchdogHeartbeatEvent;
        private EventWaitHandle _watchdogShutdownEvent;
        private EventWaitHandle _watchdogReadyEvent;
        private string _uiWriteErrorBannerDetail = null;
        private int _uiWriteErrorFailCount = 0;

        internal DemoBackend DemoBackend { get { return _demo; } }
        internal int LastAppliedApuPct { get { return _controller != null ? _controller.LastAppliedApuPct : -1; } }
        internal int LastAppliedSysPct { get { return _controller != null ? _controller.LastAppliedSysPct : -1; } }
        internal int LastAppliedEcMode { get { return _controller != null ? _controller.LastAppliedEcMode : -1; } }
        internal string LastHardwareWriteError { get { return _controller != null ? _controller.LastHardwareWriteError : null; } }
        internal int ConsecutiveWriteFailures { get { return _controller != null ? _controller.ConsecutiveWriteFailures : 0; } }
        internal bool ReadOnlyFailsafeLatched { get { return _controller != null && _controller.ReadOnlyFailsafeLatched; } }
        internal bool TestEvaluateAndApplyFanProfiles(int apuTemp) { return _controller != null && _controller.EvaluateAndApplyFanProfiles(apuTemp); }
        internal void TestSetFanManual(int fanId, int pct) { ApplyFan(fanId, pct); }
        internal void TestApplyAutoAll() { ApplyAutoAll(); }
        internal FanCurveProfile ApuProfile
        {
            get { return _controller != null ? _controller.ApuProfile : _builtInProfiles[0]; }
            set { if (_controller != null) _controller.ApuProfile = value; }
        }
        internal List<FanCurveProfile> CustomProfiles { get { return _customProfiles; } }
        internal Label TestCpuTempLabel { get { return _cpuTempLabel; } }
        internal Panel TestCpuTempBox { get { return _cpuTempBox; } }
        internal string TestResolveTooltipText(Control c) { return ResolveTooltipText(c); }
        internal Label TestFan1RpmLabel { get { return _fan1RpmLabel; } }
        internal Label TestOsdOpacityLabel { get { return _osdOpacityLabel; } }
        internal Label TestOsdSnapLabel { get { return _osdSnapLabel; } }
        internal Label TestOsdMetricsHeaderLabel { get { return _osdMetricsHeaderLabel; } }
        internal Button TestOsdQuickPosBc { get { return _osdQuickPosBc; } }
        internal Button TestOsdQuickPosBr { get { return _osdQuickPosBr; } }
        internal CheckBox TestAutostartCb { get { return _autostartCb; } }
        internal CheckBox TestAlwaysOnTopCb { get { return _alwaysOnTopCb; } }
        internal Button TestTabLiveGraphBtn { get { return _tabLiveGraphBtn; } }
        internal Button TestTabPerfGraphBtn { get { return _tabPerfGraphBtn; } }
        internal Button TestTabApuCurveBtn { get { return _tabApuCurveBtn; } }
        internal Button TestTabSysCurveBtn { get { return _tabSysCurveBtn; } }
        internal Button TestTabOsdConfigBtn { get { return _tabOsdConfigBtn; } }
        internal Panel TestModeCardPanel { get { return _modeCardPanel; } }
        internal Panel TestWideRightPanel { get { return _wideRightPanel; } }
        internal Panel TestFanCardPanel { get { return _fanCardPanel; } }
        internal Label TestManualFanHeaderLabel { get { return _manualFanHeaderLabel; } }
        internal bool TestManualFanControlVisible { get { return _manualFanControlVisible; } }
        internal LiveTelemetryCanvas TestLiveCanvas { get { return _liveCanvas; } }
        internal LivePerformanceCanvas TestPerfCanvas { get { return _perfCanvas; } }
        internal Button TestAutoTuneCustomBtn { get { return _autoTuneCustomBtn; } }
        internal Button TestDeleteCustomBtn { get { return _deleteCustomBtn; } }
        internal ComboBox TestCustomModeCombo { get { return _customModeCombo; } }
        internal ComboBox TestSamplingCombo { get { return _samplingCombo; } }
        internal int TestRefreshIntervalMs { get { return _refreshIntervalMs; } }
        internal void TestSetRefreshInterval(int ms) { SetRefreshInterval(ms); }
        internal void TestRunGuiCalibration() { RunGuiFanCalibration(); }
        internal void TestRunAutoTuneCurve() { RunAutoTuneCurve(); }
        internal void TestDeleteSelectedCustomMode() { DeleteSelectedCustomMode(); }
        internal void TestSetGraphViewMode(bool showLive, bool showPerf, int hl) { SetGraphViewMode(showLive, showPerf, hl); }
        internal void TestSetOsdConfigViewMode(bool show) { SetOsdConfigViewMode(show); }
        private bool _watchdogStarted = false;
        private SessionEndingEventHandler _sessionEndingHandler;
        private PowerModeChangedEventHandler _powerModeChangedHandler;
        private string _pendingActionText = null;
        private int _rampTargetFan = 0;

        // 4 Built-in EC profiles with separate APU (Fan 1) & System RAM/NVMe (Fan 2) curves
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
                new CurvePoint[] { new CurvePoint(45, 30), new CurvePoint(60, 34), new CurvePoint(72, 42), new CurvePoint(84, 52), new CurvePoint(92, 65) }),
            new FanCurveProfile("AI Workload", true, 3,
                new CurvePoint[] { new CurvePoint(38, 35), new CurvePoint(48, 50), new CurvePoint(60, 70), new CurvePoint(72, 88), new CurvePoint(82, 100) },
                new CurvePoint[] { new CurvePoint(35, 45), new CurvePoint(46, 60), new CurvePoint(58, 75), new CurvePoint(70, 90), new CurvePoint(80, 100) })
        };

        private readonly List<FanCurveProfile> _customProfiles = new List<FanCurveProfile>();
        private FanCurveProfile _apuProfile
        {
            get { return _controller != null ? _controller.ApuProfile : _builtInProfiles[0]; }
            set { if (_controller != null) _controller.ApuProfile = value; }
        }
        private FanCurveProfile _sysProfile
        {
            get { return _controller != null ? _controller.SysProfile : _builtInProfiles[0]; }
            set { if (_controller != null) _controller.SysProfile = value; }
        }
        private bool _hasDualFans
        {
            get { return _settings.HasDualFans; }
            set
            {
                _settings.HasDualFans = value;
                if (_controller != null) _controller.HasDualFans = value;
            }
        }
        private bool _emergencyThermalActive
        {
            get { return _controller != null && _controller.EmergencyThermalActive; }
        }

        private readonly List<Label> _sectionHeaders = new List<Label>();
        private readonly List<Panel> _sectionCards = new List<Panel>();
        private readonly List<Label> _staticLabels = new List<Label>();
        private Panel _modeCardPanel;
        private Label _manualFanHeaderLabel;
        private Panel _fanCardPanel;
        private bool _manualFanControlVisible = true;
        private Panel _wideRightPanel;
        private Label _wideRightHeaderLabel;
        private Button _wideTrendBtn;
        private Button _widePerfBtn;
        private bool _wideRightShowPerf = false;

        private Label _hwBadgeLabel;
        private Label _hwSubLabel;
        private Label _biosPowerLabel;
        private readonly Button[] _biosModeButtons = new Button[3]; // 0=Balanced, 1=Performance, 2=Silent
        private int _lastActiveBiosModeCode = -1;
        private Button _driversMenuBtn;
        private ContextMenuStrip _driversMenu;
        private Button _themeToggleBtn;
        private Label _errorBanner;
        private DoubleBufferedPanel _cpuTempBox;
        private DoubleBufferedPanel _gpuTempBox;
        private DoubleBufferedPanel _nvmeTempBox;
        private DoubleBufferedPanel _pwrBox;
        private DoubleBufferedPanel _ramPowerBox;
        private DoubleBufferedPanel _ambientBox;
        private DoubleBufferedPanel _fan1Box;
        private DoubleBufferedPanel _fan2Box;
        private DoubleBufferedPanel _ramCard;
        private Label _ramLabel;
        private sealed class MetricBoxDetail
        {
            public string PeakText = "";
            public string SubText = "";
            public Color SubColor = Color.Empty;
            public string DetailText = "";
            public string DetailText2 = "";
            public Color Detail2Color = Color.Empty;
        }
        private readonly Dictionary<int, MetricBoxDetail> _metricDetails = new Dictionary<int, MetricBoxDetail>();
        private static readonly Font MetricPeakFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font MetricSubFont = new Font("Segoe UI", 7.75f, FontStyle.Bold);
        private static readonly Font MetricDetailFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        private Label _samplingLabel;
        private ComboBox _samplingCombo;
        private FanStatus _lastStatus;
        private int _refreshIntervalMs
        {
            get { return _settings.RefreshIntervalMs; }
            set
            {
                _settings.RefreshIntervalMs = value;
                if (_controller != null) _controller.RefreshIntervalMs = value;
            }
        }
        private string _profileOverrideId
        {
            get { return _settings.ProfileOverrideId; }
            set { _settings.ProfileOverrideId = value; }
        }
        private Label _cpuBoxTitle;
        private Label _gpuBoxTitle;
        private Label _nvmeBoxTitle;
        private Label _pwrBoxTitle;
        private Label _ramBoxTitle;
        private Label _ambientBoxTitle;
        private Label _fan1BoxTitle;
        private Label _fan2BoxTitle;
        private Label _cpuTempLabel;
        private Label _gpuTempLabel;
        private Label _nvmeTempLabel;
        private Label _pwrLabel;
        private Label _ramPowerLabel;
        private Label _ambientLabel;
        private Label _fan1RpmLabel;
        private Label _fan2RpmLabel;
        private Label _modeLabel;
        private Label _pct1Label;
        private Label _pct2Label;

        private Button[] _modeButtons = new Button[4];
        private ComboBox _customModeCombo;
        private Button _activateCustomBtn;
        private Button _autoTuneCustomBtn;
        private Button _addCustomBtn;
        private Button _deleteCustomBtn;
        private Button _tabApuCurveBtn;
        private Button _tabSysCurveBtn;
        private Button _tabLiveGraphBtn;
        private Button _tabPerfGraphBtn;
        private Button _tabOsdConfigBtn;
        private Panel _osdConfigPanel;
        private bool _showingOsdConfig = false;
        private Label _customLabel;
        private FanCurveCanvas _curveCanvas;
        private LiveTelemetryCanvas _liveCanvas;
        private LivePerformanceCanvas _perfCanvas;
        private bool _showingLiveTelemetry = false;
        private bool _showingPerfGraph = false;
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
        private Button _osdBtnGraphsOnly;
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
        private CheckBox _osdShowVrmCurrentCb;
        private CheckBox _osdShowAmbientCb;
        private CheckBox _osdShowFabricClockCb;
        private CheckBox _osdShowNpuCb;
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
        private CheckBox _osdShowGameNameCb;
        private CheckBox _osdSparklineBgCb;
        private CheckBox _osdShowLiveTrendCb;
        private CheckBox _osdShowFpsGraphCb;
        private CheckBox _osdPlotFpsOnTrendCb;
        private CheckBox _osdDfGraphStyleCb;
        private CheckBox _osdDfCornerInsetCb;
        private CheckBox _osdDfGraphFirstCb;
        private Label _osdSubFieldsHeaderLabel;
        private CheckBox _osdShowRamTempCb;
        private CheckBox _osdShowRamDualSticksCb;
        private CheckBox _osdShowRamSpreadCb;
        private CheckBox _osdShowRamLoadCb;
        private CheckBox _osdShowRamPowerCb;
        private CheckBox _osdShowRamClockCb;
        private ComboBox _osdTrackerModeCombo;
        private CheckBox _osdShowNvmePowerCb;
        private CheckBox _osdShowNvmeIoCb;
        private CheckBox _osdShowNvmeDualCb;
        private CheckBox _osdShowCpuLoadCb;
        private CheckBox _osdShowGpuHotspotCb;
        private CheckBox _osdShowPowerLimitCb;
        private CheckBox _osdShowFclkSyncCb;
        private CheckBox _osdShowFrametimeMsCb;
        private CheckBox _osdShowFpsAvgCb;
        private CheckBox _osdShowRadeonExtTagsCb;
        private Panel _osdMetricsScrollPanel;
        private readonly List<Label> _osdSectionHeaders = new List<Label>();
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
        private bool _apuManualOverride { get { return _controller != null && _controller.ApuManualOverride; } }
        private bool _sysManualOverride { get { return _controller != null && _controller.SysManualOverride; } }
        private int _apuManualPct { get { return _controller != null ? _controller.ApuManualPct : 0; } }
        private int _sysManualPct { get { return _controller != null ? _controller.SysManualPct : 0; } }
        private bool _hwWorkerPaused { get { return _controller != null && _controller.IsWorkerPaused; } }
        private string _lastHardwareWriteError { get { return _uiWriteErrorBannerDetail; } }
        private int _consecutiveWriteFailures { get { return _uiWriteErrorFailCount; } }
        private DateTime _lastWorkerSampleUtc { get { return _controller != null ? _controller.LastWorkerSampleUtc : DateTime.UtcNow; } }
        private bool _suppressComboEvent;

        private CheckBox _showCpuTrayCb;
        private CheckBox _showGpuTrayCb;
        private CheckBox _showOverlayCb;
        private CheckBox _lockOverlayCb;
        private CheckBox _autostartCb;
        private CheckBox _alwaysOnTopCb;
        private CheckBox _trayCb;
        private CheckBox _useFahrenheitCb;
        private Label _statusBarLabel;

        private System.Windows.Forms.Timer _refreshTimer;
        private NotifyIcon _cpuTrayIcon;
        private NotifyIcon _gpuTrayIcon;
        private ToolTip _uiToolTip;
        private PictureBox _headerLogoBox;
        private int _lastSelectedModeCode = 0;

        public MainForm(bool startInDemo, bool startMinimized = false, bool isUnitTest = false)
        {
            _initialDemoMode = startInDemo;
            _startMinimized = startMinimized;
            _isUnitTest = isUnitTest;
            _sysInfo = SystemHardwareInfo.Detect();
            _hw.Profile = _sysInfo.MatchedProfile;
            _controller = new FanControllerService(_hw, _demo, _builtInProfiles, startInDemo);
            if (_demoMode && _demo != null)
            {
                try
                {
                    _lastStatus = _demo.GetStatus();
                    _controller.SetInitialStatus(_lastStatus);
                }
                catch { }
            }
            LoadSettings();
            _controller.HasDualFans = _settings.HasDualFans;
            _controller.RefreshIntervalMs = _settings.RefreshIntervalMs;
            EnsureProtectedExamplesPresent();
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
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(860, 640);
            AutoScroll = true;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle wa = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : new Rectangle(0, 0, 1280, 1024);
            ClientSize = new Size(Math.Min(1100, Math.Max(860, wa.Width - 24)), Math.Min(1258, Math.Max(640, wa.Height - 40)));
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
            ApplyLocalization();
            ApplyThemeToControls();
            UpdateTrayIcons(0, 0);
            SetGraphViewMode(true, false, 1);

            WireControllerEvents();

            if (!isUnitTest)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _sessionEndingHandler = (s, e) => RestoreHardwareSafeAutoIfNeeded();
                _powerModeChangedHandler = (s, e) =>
                {
                    if (e.Mode == PowerModes.Suspend)
                    {
                        OnPowerSuspend();
                    }
                    else if (e.Mode == PowerModes.Resume)
                    {
                        OnPowerResume();
                    }
                };
                SystemEvents.SessionEnding += _sessionEndingHandler;
                SystemEvents.PowerModeChanged += _powerModeChangedHandler;
                AppDomain.CurrentDomain.ProcessExit += OnProcessOrCrashExit;
                AppDomain.CurrentDomain.UnhandledException += OnProcessOrCrashExit;

                _refreshTimer = new System.Windows.Forms.Timer { Interval = _refreshIntervalMs };
                _refreshTimer.Tick += (s, e) =>
                {
                    if ((DateTime.UtcNow - _lastWorkerSampleUtc).TotalMilliseconds < 4500)
                    {
                        PulseWatchdogHeartbeat();
                    }

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

                _controller.Start();
                StartWatchdogSupervisor();
                StartSingleInstanceWakeListener();

                Shown += (s, e) =>
                {
                    UpdateGlobalHotkeysRegistration();
                    if (_startMinimized)
                    {
                        MinimizeToTray();
                    }
                    else if (!_isUnitTest && _showSetupAssistant && string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate))
                    {
                        BeginInvoke((Action)(() => ShowSetupAssistantDialog(false)));
                    }
                };
                FormClosing += OnFormClosing;
            }
        }

        private void WireControllerEvents()
        {
            if (_controller == null) return;

            _controller.SampleReceived += st =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                if (Interlocked.CompareExchange(ref _uiSamplePending, 1, 0) == 0)
                {
                    FanStatus sample = st;
                    try
                    {
                        BeginInvoke((Action)(() => OnHardwareWorkerSample(sample)));
                    }
                    catch
                    {
                        Interlocked.Exchange(ref _uiSamplePending, 0);
                    }
                }
            };

            _controller.WorkerError += ex =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => OnHardwareWorkerError(ex))); } catch { }
            };

            _controller.DualFanAutoAdapted += () =>
            {
                _settings.HasDualFans = false;
                if (!IsHandleCreated || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        ApplyFanLayoutMode();
                        RefreshStatus();
                    }));
                }
                catch { }
            };

            _controller.WriteErrorStateChanged += (detail, fails) =>
            {
                _uiWriteErrorBannerDetail = detail;
                _uiWriteErrorFailCount = fails;
                if (!IsHandleCreated || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (string.IsNullOrEmpty(detail))
                        {
                            if (_errorBanner != null) _errorBanner.Visible = false;
                            if (_statusBarLabel != null) _statusBarLabel.Text = "Ready";
                        }
                        else
                        {
                            if (_errorBanner != null)
                            {
                                _errorBanner.Text = string.Format("⚠️ Hardware Write Failure: {0} (Attempt {1})", detail, fails);
                                _errorBanner.Visible = true;
                            }
                            if (_statusBarLabel != null)
                            {
                                _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                                _statusBarLabel.Text = string.Format("⚠️ Hardware write rejected ({0} retries)", fails);
                            }
                        }
                    }));
                }
                catch { }
            };

            _controller.EscalatedFailsafeTriggered += fails =>
            {
                _uiWriteErrorBannerDetail = "Escalated failsafe";
                _uiWriteErrorFailCount = fails;
                if (!IsHandleCreated || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (_errorBanner != null)
                        {
                            _errorBanner.Text = string.Format("CRITICAL: {0} consecutive hardware write failures. Safety failsafe engaged: BIOS Auto restored and manual writes latched read-only.", fails);
                            _errorBanner.Visible = true;
                        }
                        if (_statusBarLabel != null)
                        {
                            _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                            _statusBarLabel.Text = "CRITICAL: Write failures escalated -> BIOS Auto restored (Read-Only Latched)";
                        }
                    }));
                }
                catch { }
            };

            _controller.WorkerIterationCompleted += () =>
            {
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
            };
        }

        private void OnProcessOrCrashExit(object sender, EventArgs e)
        {
            RestoreHardwareSafeAutoIfNeeded();
        }

        private bool _isSystemSuspended = false;
        private bool _isDisplayOff = false;
        internal bool TestIsSystemSuspended { get { return _isSystemSuspended; } }
        internal bool TestIsDisplayOff { get { return _isDisplayOff; } }
        internal bool TestHwWorkerPaused { get { return _hwWorkerPaused; } }
        internal bool TestCloseToTray { get { return _closeToTray; } set { _closeToTray = value; } }
        internal bool TestSilentSleepStandby { get { return _silentSleepStandby; } set { _silentSleepStandby = value; } }
        internal void TestOnPowerSuspend() { OnPowerSuspend(); }
        internal void TestOnPowerResume() { OnPowerResume(); }
        internal void TestHandleModernStandbySettingChange(Guid settingGuid, byte data)
        {
            if (settingGuid == GUID_CONSOLE_DISPLAY_STATE || settingGuid == GUID_MONITOR_POWER_ON)
            {
                // Display state notification (0 = Off, 1 = On, 2 = Dimmed).
                // Note: Monitor screen timeout is NOT system sleep! Keep worker and fan curves running.
                if (data == 0)
                {
                    _isDisplayOff = true;
                    if (_overlayForm != null && _overlayForm.Visible) _overlayForm.Visible = false;
                }
                else if (data == 1)
                {
                    _isDisplayOff = false;
                    if (_overlayForm != null && _showOverlay) _overlayForm.Visible = true;
                }
            }
            else if (settingGuid == GUID_LOW_POWER_EPOCH)
            {
                // GUID_LOW_POWER_EPOCH: 1 = Entering Low Power Epoch (Connected Standby / Sleep)
                //                        0 = Exiting Low Power Epoch (Resume / Wake)
                if (data == 1) OnPowerSuspend();
                else if (data == 0) OnPowerResume();
            }
        }

        private void OnPowerSuspend()
        {
            if (_isSystemSuspended) return;
            _isSystemSuspended = true;

            WatchdogSupervisor.LogWatchdogEvent("Power state: System suspending / entering sleep. Pausing hardware worker and putting fans into safe standby auto.");

            if (_controller != null) _controller.PauseForSuspend();
            if (_refreshTimer != null) _refreshTimer.Stop();

            StopWatchdogSupervisorGracefully();
            StopSingleInstanceWakeListener();

            if (!_demoMode && _hw != null)
            {
                try
                {
                    bool isSuperIo = (_hw.SuperIoBaseAddr != 0 || _hw.AccessMethod == HardwareBackend.EcAccessMethod.SuperIo2E || _hw.AccessMethod == HardwareBackend.EcAccessMethod.SuperIo4E);
                    if (_silentSleepStandby)
                    {
                        _hw.SetFan(1, 0);
                        if (_hasDualFans) _hw.SetFan(2, 0);
                    }
                    else
                    {
                        if (isSuperIo)
                        {
                            _hw.SetAuto();
                        }
                        else
                        {
                            _hw.SetFan(1, 0);
                            if (_hasDualFans)
                            {
                                _hw.SetFan(2, 0);
                            }
                            _hw.SetAuto();
                        }
                    }
                }
                catch (Exception ex)
                {
                    WatchdogSupervisor.LogWatchdogEvent("Error putting fans to safe auto on suspend: " + ex.Message);
                }
            }
        }

        private void OnPowerResume()
        {
            if (!_isSystemSuspended) return;
            _isSystemSuspended = false;

            WatchdogSupervisor.LogWatchdogEvent("Power state: System resumed from sleep / standby. Reinitializing hardware state and resuming worker.");

            _safeExitRestored = false;

            if (_controller != null) _controller.ResumeFromSuspend(true);
            else if (_hw != null) _hw.ReinitializeHardwareAfterResume();

            StartWatchdogSupervisor();
            StartSingleInstanceWakeListener();

            if (_refreshTimer != null) _refreshTimer.Start();
        }

        private void RestoreHardwareSafeAutoIfNeeded()
        {
            if (_safeExitRestored) return;
            _safeExitRestored = true;
            StopWatchdogSupervisorGracefully();
            StopSingleInstanceWakeListener();
            if (_controller != null)
            {
                _controller.RestoreSafeAutoOnExit(_restoreAutoOnExit);
            }
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
                _watchdogStarted = false;
                if (_watchdogReadyEvent != null) { try { _watchdogReadyEvent.Close(); } catch { } _watchdogReadyEvent = null; }
                if (_watchdogHeartbeatEvent != null) { try { _watchdogHeartbeatEvent.Close(); } catch { } _watchdogHeartbeatEvent = null; }
                if (_watchdogShutdownEvent != null) { try { _watchdogShutdownEvent.Close(); } catch { } _watchdogShutdownEvent = null; }
                if (_watchdogProcess != null) { try { _watchdogProcess.Dispose(); } catch { } _watchdogProcess = null; }
            }
        }

        private EventWaitHandle _showAppEvent = null;
        private Thread _showAppThread = null;
        private volatile bool _wakeListenerRunning = false;

        private void StartSingleInstanceWakeListener()
        {
            if (_showAppThread != null && _showAppThread.IsAlive) return;
            try
            {
                bool createdNew;
                _showAppEvent = WatchdogSupervisor.CreateOrOpenEvent("MiniFanControl_ShowMainWindow", EventResetMode.AutoReset, out createdNew, WatchdogIpcRole.Signaler);
                if (_showAppEvent != null)
                {
                    _wakeListenerRunning = true;
                    _showAppThread = new Thread(() =>
                    {
                        while (_wakeListenerRunning)
                        {
                            try
                            {
                                if (_showAppEvent.WaitOne(1000))
                                {
                                    if (IsHandleCreated && !IsDisposed)
                                    {
                                        BeginInvoke((Action)(() =>
                                        {
                                            RestoreFromTray();
                                            Show();
                                            WindowState = FormWindowState.Normal;
                                            BringToFront();
                                            Activate();
                                        }));
                                    }
                                }
                            }
                            catch { break; }
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "MiniFanControl.ShowAppListener"
                    };
                    _showAppThread.Start();
                }
            }
            catch { }
        }

        private void StopSingleInstanceWakeListener()
        {
            _wakeListenerRunning = false;
            try
            {
                if (_showAppEvent != null)
                {
                    _showAppEvent.Close();
                    _showAppEvent = null;
                }
            }
            catch { }
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateGlobalHotkeysRegistration();
            RegisterModernStandbyNotifications();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterModernStandbyNotifications();
            UnregisterGlobalHotkeys();
            base.OnHandleDestroyed(e);
        }

        private void RegisterModernStandbyNotifications()
        {
            if (!IsHandleCreated || Handle == IntPtr.Zero) return;
            UnregisterModernStandbyNotifications();
            try
            {
                Guid gDisplay = GUID_CONSOLE_DISPLAY_STATE;
                _hDisplayStateNotify = RegisterPowerSettingNotification(Handle, ref gDisplay, 0);
                Guid gMonitor = GUID_MONITOR_POWER_ON;
                _hMonitorPowerNotify = RegisterPowerSettingNotification(Handle, ref gMonitor, 0);
                Guid gEpoch = GUID_LOW_POWER_EPOCH;
                _hLowPowerEpochNotify = RegisterPowerSettingNotification(Handle, ref gEpoch, 0);
            }
            catch { }
        }

        private void UnregisterModernStandbyNotifications()
        {
            try
            {
                if (_hDisplayStateNotify != IntPtr.Zero)
                {
                    UnregisterPowerSettingNotification(_hDisplayStateNotify);
                    _hDisplayStateNotify = IntPtr.Zero;
                }
                if (_hMonitorPowerNotify != IntPtr.Zero)
                {
                    UnregisterPowerSettingNotification(_hMonitorPowerNotify);
                    _hMonitorPowerNotify = IntPtr.Zero;
                }
                if (_hLowPowerEpochNotify != IntPtr.Zero)
                {
                    UnregisterPowerSettingNotification(_hLowPowerEpochNotify);
                    _hLowPowerEpochNotify = IntPtr.Zero;
                }
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_POWERBROADCAST)
            {
                int pbt = m.WParam.ToInt32();
                if (pbt == PBT_APMSUSPEND)
                {
                    OnPowerSuspend();
                }
                else if (pbt == PBT_APMRESUMEAUTOMATIC || pbt == PBT_APMRESUMESUSPEND)
                {
                    OnPowerResume();
                }
                else if (pbt == PBT_POWERSETTINGCHANGE && m.LParam != IntPtr.Zero)
                {
                    try
                    {
                        POWERBROADCAST_SETTING pbs = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(POWERBROADCAST_SETTING));
                        if (pbs.PowerSetting == GUID_CONSOLE_DISPLAY_STATE || pbs.PowerSetting == GUID_MONITOR_POWER_ON)
                        {
                            if (pbs.Data == 0) // Display Off (Screen timeout) -> Keep fans & worker running!
                            {
                                WatchdogSupervisor.LogWatchdogEvent(string.Format("Power notification: Display off (Setting: {0}). Keeping fan curves and hardware worker active.", pbs.PowerSetting));
                                _isDisplayOff = true;
                                if (_overlayForm != null && _overlayForm.Visible) _overlayForm.Visible = false;
                            }
                            else if (pbs.Data == 1) // Display On (Screen awake)
                            {
                                WatchdogSupervisor.LogWatchdogEvent(string.Format("Power notification: Display on (Setting: {0}). Restoring OSD visibility.", pbs.PowerSetting));
                                _isDisplayOff = false;
                                if (_overlayForm != null && _showOverlay) _overlayForm.Visible = true;
                            }
                        }
                        else if (pbs.PowerSetting == GUID_LOW_POWER_EPOCH)
                        {
                            if (pbs.Data == 1) // 1 = Entering Low Power Epoch (Connected Standby / Sleep)
                            {
                                WatchdogSupervisor.LogWatchdogEvent("Power notification: Entering Low Power Epoch (Modern Standby sleep). Suspending worker.");
                                OnPowerSuspend();
                            }
                            else if (pbs.Data == 0) // 0 = Exiting Low Power Epoch (Resume / Wake)
                            {
                                WatchdogSupervisor.LogWatchdogEvent("Power notification: Exiting Low Power Epoch (Resume). Resuming worker.");
                                OnPowerResume();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WatchdogSupervisor.LogWatchdogEvent("Power setting change notification parse error: " + ex.Message);
                    }
                }
            }
            else if (m.Msg == WM_HOTKEY)
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
            _activePopupTooltipText = null;
            _metricDetails.Clear();
            if (_cpuBoxTitle != null) _cpuBoxTitle.Text = "CPU";
            if (_gpuBoxTitle != null) _gpuBoxTitle.Text = "iGPU";
            if (_nvmeBoxTitle != null) _nvmeBoxTitle.Text = "NVMe";
            if (_pwrBoxTitle != null) _pwrBoxTitle.Text = "POWER";
            if (_ramBoxTitle != null) _ramBoxTitle.Text = "RAM (DDR5)";
            if (_ambientBoxTitle != null) _ambientBoxTitle.Text = "CHASSIS";
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

        private string GetRyzenTierBadge()
        {
            if (_sysInfo == null) return null;
            string combined = (_sysInfo.CpuName ?? "") + " " + (_sysInfo.MatchedProfile != null ? _sysInfo.MatchedProfile.DisplayName : "");
            if (combined.IndexOf("Ryzen AI 9", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen AI 9";
            if (combined.IndexOf("Ryzen 9", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 9";
            if (combined.IndexOf("Ryzen 7", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 7";
            if (combined.IndexOf("Ryzen 5", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 5";
            return null;
        }

        private void InitOverlayForm()
        {
            _overlayForm = new CapFrameXOverlayForm
            {
                Location = new Point(Math.Max(0, _osd.OverlayX), Math.Max(0, _osd.OverlayY)),
                Stats = _sessionStats,
                ProcessorBadgeText = GetRyzenTierBadge(),
                SampleIntervalMs = _refreshIntervalMs
            };
            _osd.ApplyToOverlay(_overlayForm, _themePref, _hasDualFans, _useFahrenheit);
            _overlayForm.UpdateDimensions();
            _overlayForm.PositionOrLayoutChanged += (s, e) =>
            {
                _osd.ReadFromOverlay(_overlayForm);
                SyncOsdStudioUiFromOverlay();
                SaveSettings();
                RebuildDriversMenu();
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
            _overlayForm.RequestSamplingInterval += (s, ms) =>
            {
                SetRefreshInterval(ms);
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
            _osd.ApplyToOverlay(_overlayForm, _themePref, _hasDualFans, _useFahrenheit);
            _overlayForm.UpdateDimensions();
            _overlayForm.Invalidate();
            SaveSettings();
        }

        private void SyncOsdStudioUiFromOverlay()
        {
            if (_overlayForm != null)
            {
                _osd.ReadFromOverlay(_overlayForm);
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.ShowCpu = _osd.TrendCpu;
                _liveCanvas.ShowGpu = _osd.TrendGpu;
                _liveCanvas.ShowRam = _osd.TrendRam;
                _liveCanvas.ShowNvme = _osd.TrendNvme;
                _liveCanvas.ShowPower = _osd.TrendPower;
                _liveCanvas.ShowAmbient = _osd.TrendAmbient;
                _liveCanvas.ShowApuRpm = _osd.TrendApuFan;
                _liveCanvas.ShowSysRpm = _osd.TrendSysFan;
                _liveCanvas.Invalidate();
            }

            if (_osdOpacitySlider != null && _osdOpacitySlider.Value != _osd.OpacityPct)
                _osdOpacitySlider.Value = Math.Max(_osdOpacitySlider.Minimum, Math.Min(_osdOpacitySlider.Maximum, _osd.OpacityPct));
            if (_osdOpacityPctLabel != null)
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);

            if (_osdShowCpuCb != null) _osdShowCpuCb.Checked = _osd.ShowCpu;
            if (_osdShowGpuCb != null) _osdShowGpuCb.Checked = _osd.ShowGpu;
            if (_osdShowNvmeCb != null) _osdShowNvmeCb.Checked = _osd.ShowNvme;
            if (_osdShowPowerCb != null) _osdShowPowerCb.Checked = _osd.ShowPower;
            if (_osdShowRamCb != null) _osdShowRamCb.Checked = _osd.ShowRam;
            if (_osdShowVrmCurrentCb != null) _osdShowVrmCurrentCb.Checked = _osd.ShowVrmCurrent;
            if (_osdShowAmbientCb != null) _osdShowAmbientCb.Checked = _osd.ShowAmbient;
            if (_osdShowFabricClockCb != null) _osdShowFabricClockCb.Checked = _osd.ShowFabricClock;
            if (_osdShowNpuCb != null)
            {
                bool hasNpu = _demoMode || _lastStatus.NpuActivityPct >= 0 || AmdAdlGpuSensor.HasNpuHardware;
                _osdShowNpuCb.Checked = _osd.ShowNpu && hasNpu;
                _osdShowNpuCb.Enabled = hasNpu;
                _osdShowNpuCb.Text = hasNpu ? "Ryzen AI NPU" : "Ryzen AI NPU (N/A)";
            }
            if (_osdShowFpsCb != null) _osdShowFpsCb.Checked = _osd.ShowFps;
            if (_osdShowLowsCb != null) _osdShowLowsCb.Checked = _osd.ShowLows;
            if (_osdShowGpuLoadCb != null) _osdShowGpuLoadCb.Checked = _osd.ShowGpuLoad;
            if (_osdShowApuFanCb != null) _osdShowApuFanCb.Checked = _osd.ShowApuFan;
            if (_osdShowSysFanCb != null)
            {
                _osdShowSysFanCb.Checked = _osd.ShowSysFan && _hasDualFans;
                _osdShowSysFanCb.Enabled = _hasDualFans;
                _osdShowSysFanCb.Text = _hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)";
            }
            if (_osdShowModeCb != null) _osdShowModeCb.Checked = _osd.ShowMode;
            if (_osdShowPeaksCb != null) _osdShowPeaksCb.Checked = _osd.ShowPeaks;
            if (_osdTrackerModeCombo != null)
            {
                int tmIdx = Math.Max(0, Math.Min(3, _osd.PeakTrackerMode));
                if (_osdTrackerModeCombo.SelectedIndex != tmIdx) _osdTrackerModeCombo.SelectedIndex = tmIdx;
                _osdTrackerModeCombo.Enabled = _osd.ShowPeaks;
            }
            if (_osdShowRadeonCb != null) _osdShowRadeonCb.Checked = _osd.ShowRadeon;
            if (_osdShowRadeonExtTagsCb != null) _osdShowRadeonExtTagsCb.Checked = _osd.ShowRadeonExtTags;
            if (_osdShowDriverVerCb != null) _osdShowDriverVerCb.Checked = _osd.ShowDriverVer;
            if (_osdShowClocksCb != null) _osdShowClocksCb.Checked = _osd.ShowClocks;
            if (_osdShowGameNameCb != null) _osdShowGameNameCb.Checked = _osd.ShowGameName;
            if (_osdSparklineBgCb != null) _osdSparklineBgCb.Checked = _osd.SparklineBg;
            if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = _osd.ShowLiveTrend;
            if (_osdShowFpsGraphCb != null) _osdShowFpsGraphCb.Checked = _osd.ShowFpsGraph;
            if (_osdPlotFpsOnTrendCb != null) _osdPlotFpsOnTrendCb.Checked = _osd.PlotFpsOnTrend;
            if (_osdDfGraphStyleCb != null) _osdDfGraphStyleCb.Checked = _osd.DfGraphStyle;
            if (_osdDfCornerInsetCb != null) _osdDfCornerInsetCb.Checked = _osd.DfCornerInset;
            if (_osdDfGraphFirstCb != null) _osdDfGraphFirstCb.Checked = _osd.DfGraphFirst;
            if (_osdShowRamTempCb != null) _osdShowRamTempCb.Checked = _osd.ShowRamTemp;
            if (_osdShowRamDualSticksCb != null) _osdShowRamDualSticksCb.Checked = _osd.ShowRamDualSticks;
            if (_osdShowRamSpreadCb != null) _osdShowRamSpreadCb.Checked = _osd.ShowRamSpread;
            if (_osdShowRamLoadCb != null) _osdShowRamLoadCb.Checked = _osd.ShowRamLoad;
            if (_osdShowRamPowerCb != null) _osdShowRamPowerCb.Checked = _osd.ShowRamPower;
            if (_osdShowRamClockCb != null) _osdShowRamClockCb.Checked = _osd.ShowRamClock;
            if (_osdShowNvmePowerCb != null) _osdShowNvmePowerCb.Checked = _osd.ShowNvmePower;
            if (_osdShowNvmeIoCb != null) _osdShowNvmeIoCb.Checked = _osd.ShowNvmeIo;
            if (_osdShowNvmeDualCb != null) _osdShowNvmeDualCb.Checked = _osd.ShowNvmeDual;
            if (_osdShowCpuLoadCb != null) _osdShowCpuLoadCb.Checked = _osd.ShowCpuLoad;
            if (_osdShowGpuHotspotCb != null)
            {
                bool hasHotspot = _demoMode || _lastStatus.GpuHotspotTemp > 0;
                _osdShowGpuHotspotCb.Checked = _osd.ShowGpuHotspot && hasHotspot;
                _osdShowGpuHotspotCb.Enabled = hasHotspot;
                _osdShowGpuHotspotCb.Text = hasHotspot ? "iGPU Hotspot (🔥°C)" : "iGPU Hotspot (N/A)";
            }
            if (_osdShowPowerLimitCb != null) _osdShowPowerLimitCb.Checked = _osd.ShowPowerLimit;
            if (_osdShowFclkSyncCb != null) _osdShowFclkSyncCb.Checked = _osd.ShowFclkSync;
            if (_osdShowFrametimeMsCb != null) _osdShowFrametimeMsCb.Checked = _osd.ShowFrametimeMs;
            if (_osdShowFpsAvgCb != null) _osdShowFpsAvgCb.Checked = _osd.ShowFpsAvg;
            if (_osdMatchThemeCb != null) _osdMatchThemeCb.Checked = _osd.MatchTheme;

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
            int margin = 16;

            switch (corner)
            {
                case 0: // Top-Left
                    _osd.OverlayX = wa.Left + margin;
                    _osd.OverlayY = wa.Top + margin;
                    break;
                case 1: // Top-Right
                    _osd.OverlayX = wa.Right - w - margin;
                    _osd.OverlayY = wa.Top + margin;
                    break;
                case 2: // Bottom-Left
                    _osd.OverlayX = wa.Left + margin;
                    _osd.OverlayY = Math.Max(wa.Top + margin, wa.Bottom - h - margin);
                    break;
                case 3: // Bottom-Right
                    _osd.OverlayX = wa.Right - w - margin;
                    _osd.OverlayY = Math.Max(wa.Top + margin, wa.Bottom - h - margin);
                    break;
                case 4: // Center-Top
                    _osd.OverlayX = wa.Left + Math.Max(0, (wa.Width - w) / 2);
                    _osd.OverlayY = wa.Top + margin;
                    break;
                case 5: // Center-Bottom
                    _osd.OverlayX = wa.Left + Math.Max(0, (wa.Width - w) / 2);
                    _osd.OverlayY = Math.Max(wa.Top + margin, wa.Bottom - h - margin);
                    break;
            }

            _overlayForm.Location = new Point(_osd.OverlayX, _osd.OverlayY);
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

        private void SetPresetsAndCustomVisible(bool visible)
        {
            if (_modeButtons != null)
            {
                for (int i = 0; i < _modeButtons.Length; i++)
                {
                    if (_modeButtons[i] != null) _modeButtons[i].Visible = visible;
                }
            }
            if (_customLabel != null) _customLabel.Visible = visible;
            if (_customModeCombo != null) _customModeCombo.Visible = visible;
            if (_activateCustomBtn != null) _activateCustomBtn.Visible = visible;
            if (_autoTuneCustomBtn != null) _autoTuneCustomBtn.Visible = visible;
            if (_addCustomBtn != null) _addCustomBtn.Visible = visible;
            if (_deleteCustomBtn != null) _deleteCustomBtn.Visible = visible;
        }

        private void SetManualFanControlVisible(bool visible)
        {
            _manualFanControlVisible = visible;
            if (_manualFanHeaderLabel != null) _manualFanHeaderLabel.Visible = visible;
            if (_calibBadgeLabel != null) _calibBadgeLabel.Visible = visible;
            if (_fanCardPanel != null) _fanCardPanel.Visible = visible;

            int cardHeight = visible ? 590 : 720;
            int canvasHeight = visible ? 518 : 648;
            int hintY = visible ? 566 : 696;

            if (_modeCardPanel != null && _modeCardPanel.Height != cardHeight)
            {
                _modeCardPanel.Height = cardHeight;
            }

            if (_curveHintLabel != null)
            {
                _curveHintLabel.Location = new Point(8, hintY);
            }

            if (_liveCanvas != null)
            {
                _liveCanvas.Location = new Point(8, 42);
                _liveCanvas.Size = new Size(1056, canvasHeight);
            }
            if (_perfCanvas != null)
            {
                _perfCanvas.Location = new Point(8, 42);
                _perfCanvas.Size = new Size(1056, canvasHeight);
            }
            if (_osdConfigPanel != null)
            {
                _osdConfigPanel.Location = new Point(8, 41);
                _osdConfigPanel.Size = new Size(1056, canvasHeight);
            }
            if (_osdMetricsScrollPanel != null)
            {
                _osdMetricsScrollPanel.Size = new Size(1048, Math.Max(260, canvasHeight - 200));
            }
        }

        private void SetOsdConfigViewMode(bool show)
        {
            _showingOsdConfig = show;
            if (show)
            {
                _showingLiveTelemetry = false;
                _showingPerfGraph = false;
                _highlightedMetric = 0;
            }

            if (_osdConfigPanel != null) _osdConfigPanel.Visible = show;

            bool isFanTab = !show && !_showingLiveTelemetry && !_showingPerfGraph;
            SetPresetsAndCustomVisible(isFanTab);
            SetManualFanControlVisible(isFanTab);

            if (show)
            {
                if (_curveCanvas != null) _curveCanvas.Visible = false;
                if (_liveCanvas != null) _liveCanvas.Visible = false;
                if (_perfCanvas != null) _perfCanvas.Visible = false;
                if (_curveHintLabel != null)
                    _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                SyncOsdStudioUiFromOverlay();
            }
            else
            {
                if (_showingPerfGraph)
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = false;
                    if (_liveCanvas != null) _liveCanvas.Visible = false;
                    if (_perfCanvas != null)
                    {
                        _perfCanvas.Visible = true;
                        _perfCanvas.Invalidate();
                    }
                }
                else if (_showingLiveTelemetry)
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = false;
                    if (_perfCanvas != null) _perfCanvas.Visible = false;
                    if (_liveCanvas != null)
                    {
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
                        _curveCanvas.Location = new Point(8, 108);
                        _curveCanvas.Size = new Size(1056, 454);
                        _curveCanvas.Visible = true;
                        _curveCanvas.Invalidate();
                    }
                }
                UpdateCurveHint();
            }

            UpdateCurveTabStyles();
            UpdateOsdStudioStyles();
            InvalidateAllMetricBoxes();
            UpdateWideLayout();
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
            RebuildDriversMenu();
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
            RebuildDriversMenu();
        }

        private void RebuildDriversMenu()
        {
            ContextMenuStrip oldMenu = _driversMenu;
            _driversMenu = CreateDriversAndToolsMenu();
            if (oldMenu != null)
            {
                try { oldMenu.Dispose(); } catch { }
            }
        }

        private void ToggleOverlayLayoutStyle()
        {
            _osd.VerticalLayout = !_osd.VerticalLayout;
            if (_overlayForm != null)
            {
                _overlayForm.VerticalLayout = _osd.VerticalLayout;
                _overlayForm.UpdateDimensions();
                _overlayForm.Invalidate();
            }
            SaveSettings();
            RebuildDriversMenu();
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

        private void ShowEcDifferentialScannerDialog()
        {
            IHardwareBackend backend = _demoMode ? (IHardwareBackend)_demo : (IHardwareBackend)_hw;
            using (var dlg = new EcDifferentialScannerForm(backend, _sysInfo))
            {
                dlg.OnCustomProfileApplied = customProf =>
                {
                    ApplyCustomProfile(customProf);
                };
                dlg.ShowDialog(this);
            }
        }

        private void ApplyCustomProfile(DeviceProfile customProf)
        {
            if (customProf == null) return;
            DeviceProfile.CustomProfile = customProf;
            _profileOverrideId = customProf.Id;
            _sysInfo.MatchedProfile = customProf;
            if (_hw != null) _hw.Profile = customProf;
            _hasDualFans = customProf.HasDualFans;
            if (_hwSubLabel != null)
            {
                string drvInfo = NativeInpOut.ActiveDriverName;
                _hwSubLabel.Text = string.Format("EC Map: {0} • Driver: {1} • Global\\Access_EC Mutex • 64-bit", customProf.DisplayName, drvInfo);
            }
            SaveSettings();
            RebuildDriversMenu();
            _statusBarLabel.Text = string.Format("✓ Active Profile: {0} (Unlocked)", customProf.DisplayName);
            RefreshStatus();
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
            sb.AppendLine("  • Ctrl + Alt + F9  : Show / Hide In-Game OSD Overlay");
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

        public void RunGuiFanCalibration()
        {
            if (_controller != null) _controller.PauseForSuspend();
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
                            InvalidateAllMetricBoxes();
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

                        if (_controller != null) _controller.InvalidateAppliedCache();
                        ApplyFanLayoutMode();
                        UpdateCalibrationBadge();
                        RefreshAutoTunedCurveAfterCalibration();
                        SaveSettings();
                        RebuildDriversMenu();
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
                if (_controller != null) _controller.ResumeFromSuspend(false);
                if (_refreshTimer != null && timerWasEnabled)
                    _refreshTimer.Start();
            }
        }

        private bool _showSetupAssistant
        {
            get { return _settings.ShowSetupAssistant; }
            set { _settings.ShowSetupAssistant = value; }
        }

        internal void ShowSetupAssistantDialog(bool manual)
        {
            if (_isUnitTest) return;
            using (SetupAssistantDialog dlg = new SetupAssistantDialog(this, _isDarkMode, _themePref, _showSetupAssistant))
            {
                dlg.ShowDialog(this);
                _showSetupAssistant = dlg.ShowOnStartupIfUncalibrated;
                SaveSettings();
            }
        }

        private void ToggleDualFanMode()
        {
            _hasDualFans = !_hasDualFans;
            if (_controller != null) _controller.InvalidateAppliedCache();
            ApplyFanLayoutMode();
            SaveSettings();
            RebuildDriversMenu();
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

            if (_tabApuCurveBtn != null && _tabSysCurveBtn != null && _tabLiveGraphBtn != null && _tabPerfGraphBtn != null && _tabOsdConfigBtn != null)
            {
                if (_hasDualFans)
                {
                    _tabLiveGraphBtn.Location = new Point(8, 8);
                    _tabLiveGraphBtn.Size = new Size(208, 28);
                    _tabPerfGraphBtn.Location = new Point(220, 8);
                    _tabPerfGraphBtn.Size = new Size(208, 28);
                    _tabPerfGraphBtn.Visible = true;
                    _tabApuCurveBtn.Location = new Point(432, 8);
                    _tabApuCurveBtn.Size = new Size(208, 28);
                    _tabSysCurveBtn.Location = new Point(644, 8);
                    _tabSysCurveBtn.Size = new Size(208, 28);
                    _tabSysCurveBtn.Visible = true;
                    _tabOsdConfigBtn.Location = new Point(856, 8);
                    _tabOsdConfigBtn.Size = new Size(208, 28);
                }
                else
                {
                    _tabLiveGraphBtn.Location = new Point(8, 8);
                    _tabLiveGraphBtn.Size = new Size(258, 28);
                    _tabPerfGraphBtn.Location = new Point(274, 8);
                    _tabPerfGraphBtn.Size = new Size(258, 28);
                    _tabPerfGraphBtn.Visible = true;
                    _tabApuCurveBtn.Location = new Point(540, 8);
                    _tabApuCurveBtn.Size = new Size(258, 28);
                    _tabSysCurveBtn.Visible = false;
                    _tabOsdConfigBtn.Location = new Point(806, 8);
                    _tabOsdConfigBtn.Size = new Size(258, 28);
                }
            }

            if (_fan1Box != null && _fan2Box != null)
            {
                const int boxW = 257;
                const int boxH = 108;
                const int boxGapX = 6;
                const int row2Y = 122;
                if (_hasDualFans)
                {
                    _fan1Box.Location = new Point(12 + (boxW + boxGapX) * 2, row2Y);
                    _fan1Box.Size = new Size(boxW, boxH);
                    if (_fan1BoxTitle != null) { _fan1BoxTitle.Location = new Point(7, 3); _fan1BoxTitle.Size = new Size(boxW - 14, 14); }
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Location = new Point(7, 18);
                        _fan1RpmLabel.Size = new Size(boxW - 14, 33);
                        _fan1RpmLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                    }
                    _fan2Box.Location = new Point(12 + (boxW + boxGapX) * 3, row2Y);
                    _fan2Box.Size = new Size(boxW, boxH);
                    if (_fan2BoxTitle != null) { _fan2BoxTitle.Location = new Point(7, 3); _fan2BoxTitle.Size = new Size(boxW - 14, 14); }
                    if (_fan2RpmLabel != null)
                    {
                        _fan2RpmLabel.Location = new Point(7, 18);
                        _fan2RpmLabel.Size = new Size(boxW - 14, 33);
                        _fan2RpmLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                    }
                    _fan2Box.Visible = true;
                }
                else
                {
                    int spanW = boxW * 2 + boxGapX;
                    _fan2Box.Visible = false;
                    _fan1Box.Location = new Point(12 + (boxW + boxGapX) * 2, row2Y);
                    _fan1Box.Size = new Size(spanW, boxH);
                    if (_fan1BoxTitle != null) { _fan1BoxTitle.Location = new Point(7, 3); _fan1BoxTitle.Size = new Size(spanW - 14, 14); }
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Location = new Point(7, 18);
                        _fan1RpmLabel.Size = new Size(spanW - 14, 33);
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

        private ContextMenuStrip CreateDriversAndToolsMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("✨ First-Time Setup Assistant & Auto-Tune Guide...", null, (s, e) => ShowSetupAssistantDialog(true));
            menu.Items.Add(new ToolStripSeparator());
            string calMenuLabel = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Calibrate Fans (Not Yet Calibrated — Default 30% Min)..."
                : string.Format("Calibrate Fans (✓ Last: {0} • Min {1}%)...",
                    FanCurveProfile.LastCalibratedDate, FanCurveProfile.ApuMinSpinPct);
            menu.Items.Add(calMenuLabel, null, (s, e) => RunGuiFanCalibration());
            string layoutLabel = _hasDualFans
                ? "Fan Layout: 2 Fans (APU + Sys) — Click for 1-Fan Mode"
                : "Fan Layout: 1 Fan (APU Only) — Click for 2-Fan Mode";
            menu.Items.Add(layoutLabel, null, (s, e) => ToggleDualFanMode());
            menu.Items.Add(new ToolStripSeparator());
            string osdToggleLabel = _showOverlay ? "Hide In-Game OSD Overlay" : "Show In-Game OSD Overlay";
            menu.Items.Add(osdToggleLabel, null, (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                else SetOverlayVisible(!_showOverlay);
            });
            string osdStyleLabel = _osd.VerticalLayout
                ? "OSD Layout: Vertical Stack — Click for Horizontal Bar"
                : "OSD Layout: Horizontal Bar — Click for Vertical Stack";
            menu.Items.Add(osdStyleLabel, null, (s, e) => ToggleOverlayLayoutStyle());
            string osdLockLabel = _lockOverlay
                ? "Unlock OSD Overlay (Allow Mouse Drag)"
                : "Lock OSD Overlay (Click-Through Mode)";
            menu.Items.Add(osdLockLabel, null, (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                else SetOverlayLocked(!_lockOverlay);
            });
            menu.Items.Add("Configure OSD Overlay Studio...", null, (s, e) => OpenOsdConfigTab());
            menu.Items.Add(new ToolStripSeparator());
            string resetPeaksLabel = _sessionStats.SampleCount > 0
                ? string.Format("Reset Session Peaks (Max CPU {0}°C • iGPU {1}°C • NVMe {2}°C • {3}W)",
                    _sessionStats.MaxCpuTemp, _sessionStats.MaxGpuTemp, _sessionStats.MaxNvmeTemp, _sessionStats.MaxPowerWatts)
                : "Reset Session Peak (Max °C / W) Stats";
            menu.Items.Add(resetPeaksLabel, null, (s, e) => ResetSessionPeaks());
            string exportCsvLabel = string.Format("📊 Export Session Telemetry to CSV ({0} samples logged)...", _sessionStats.LoggedRecordCount);
            menu.Items.Add(exportCsvLabel, null, (s, e) => ExportSessionTelemetryToCsv());
            menu.Items.Add("✨ Auto-Tune Optimal Fan Curve (from Telemetry & Calibration)...", null, (s, e) => RunAutoTuneCurve());
            menu.Items.Add("📋 Copy Active Fan Curve to Clipboard (Share Curve)...", null, (s, e) => CopyCurveToClipboard());
            menu.Items.Add("📥 Import Fan Curve from Clipboard...", null, (s, e) => ImportCurveFromClipboard());
            string safeExitLabel = string.Format("Exit Safety: Restore BIOS Auto on Close ({0})",
                _restoreAutoOnExit ? "✓ Enabled" : "Disabled (Keep Custom PWM)");
            menu.Items.Add(safeExitLabel, null, (s, e) =>
            {
                _restoreAutoOnExit = !_restoreAutoOnExit;
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _restoreAutoOnExit
                    ? "✓ Exit Safety enabled: fans restore to BIOS Auto on close"
                    : "ℹ Exit Safety disabled: custom/manual PWM persists after close";
            });
            string closeToTrayLabel = string.Format("Window Close: Minimize to System Tray ({0})",
                _closeToTray ? "✓ Enabled (Keep Curves)" : "Disabled (Close Exits)");
            menu.Items.Add(closeToTrayLabel, null, (s, e) =>
            {
                _closeToTray = !_closeToTray;
                if (_trayCb != null) _trayCb.Checked = _closeToTray;
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _closeToTray
                    ? "✓ Close to Tray enabled: 'X' keeps curves running in background"
                    : "ℹ Close to Tray disabled: 'X' exits app and restores BIOS Auto";
            });
            string silentSleepLabel = string.Format("Sleep Behavior: Silent Standby Coast-Down ({0})",
                _silentSleepStandby ? "✓ Silent Zero-RPM (No Kickstart)" : "BIOS Auto (2-4s Kickstart)");
            menu.Items.Add(silentSleepLabel, null, (s, e) =>
            {
                _silentSleepStandby = !_silentSleepStandby;
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _silentSleepStandby
                    ? "✓ Silent Standby active: fans gently coast to 0% with no 2-4s kickstart burst"
                    : "ℹ BIOS Auto Standby active: motherboard firmware manages sleep with 2-4s burst";
            });
            string hotkeysLabel = string.Format("Global Hotkeys Ctrl+Alt+F9..F12 ({0})",
                _enableGlobalHotkeys ? "✓ Enabled" : "Disabled");
            menu.Items.Add(hotkeysLabel, null, (s, e) =>
            {
                _enableGlobalHotkeys = !_enableGlobalHotkeys;
                UpdateGlobalHotkeysRegistration();
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _enableGlobalHotkeys
                    ? "✓ Global hotkeys enabled (Ctrl+Alt+F9..F12)"
                    : "ℹ Global hotkeys disabled";
            });
            string demoLabel = string.Format("Offline Demo / Simulation Mode ({0})", _demoMode ? "✓ Active" : "Disabled");
            menu.Items.Add(demoLabel, null, (s, e) =>
            {
                _demoMode = !_demoMode;
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _demoMode
                    ? "ℹ Offline Demo Mode active (synthetic telemetry)"
                    : "✓ Hardware Mode active (direct EC registers)";
                RefreshStatus();
            });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem profileSubMenu = new ToolStripMenuItem("Hardware Profile && EC Target");
            bool isAuto = string.IsNullOrEmpty(_profileOverrideId) || _profileOverrideId.Equals("auto", StringComparison.OrdinalIgnoreCase);
            string autoTitle = string.Format("Auto-Detect: {0} (Active)", _sysInfo.MatchedProfile.DisplayName);
            ToolStripMenuItem autoItem = new ToolStripMenuItem(autoTitle, null, (s, e) => SetHardwareProfile("auto"));
            if (isAuto) autoItem.Checked = true;
            profileSubMenu.DropDownItems.Add(autoItem);
            profileSubMenu.DropDownItems.Add(new ToolStripSeparator());
            if (DeviceProfile.CustomProfile != null)
            {
                string custId = DeviceProfile.CustomProfile.Id;
                string custTitle = "★ " + DeviceProfile.CustomProfile.DisplayName;
                ToolStripMenuItem custItem = new ToolStripMenuItem(custTitle, null, (s, e) => SetHardwareProfile(custId));
                if (!isAuto && _sysInfo.MatchedProfile.Id == custId) custItem.Checked = true;
                profileSubMenu.DropDownItems.Add(custItem);
                profileSubMenu.DropDownItems.Add(new ToolStripSeparator());
            }
            foreach (DeviceProfile dp in DeviceProfile.KnownProfiles)
            {
                string pid = dp.Id;
                ToolStripMenuItem dpItem = new ToolStripMenuItem(dp.DisplayName, null, (s, e) => SetHardwareProfile(pid));
                if (!isAuto && _sysInfo.MatchedProfile.Id == pid) dpItem.Checked = true;
                profileSubMenu.DropDownItems.Add(dpItem);
            }
            menu.Items.Add(profileSubMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Export Hardware && EC Probe Report (for HX 370 / New PCs)...", null, (s, e) => ExportHardwareProbeReport());
            menu.Items.Add("🔬 EC Motherboard Telemetry && Differential Scanner (Tier 2 Discovery)...", null, (s, e) => ShowEcDifferentialScannerDialog());
            menu.Items.Add("🚀 Submit Hardware Profile to GitHub (1-Click)...", null, (s, e) => SubmitHardwareProfileOnline());
            menu.Items.Add("Detected Hardware && Multi-Model Info...", null, (s, e) => ShowHardwareCompatibilityDialog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("GMKtec Official Drivers && Manuals (K12 / Mini PCs)...", null, (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            menu.Items.Add("AMD Software: Adrenalin && Chipset Drivers...", null, (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🛡️ Kernel Driver && HVCI Security Status...", null, (s, e) => ShowKernelDriverSecurityDialog());
            ToolStripMenuItem langMenu = new ToolStripMenuItem("🌐 Language / 语言 / Sprache");
            AddLanguageMenuItem(langMenu, "Auto (System Default)", AppLanguage.Auto);
            AddLanguageMenuItem(langMenu, "English", AppLanguage.English);
            AddLanguageMenuItem(langMenu, "简体中文 (Chinese Simplified)", AppLanguage.Chinese);
            AddLanguageMenuItem(langMenu, "Deutsch (German)", AppLanguage.German);
            AddLanguageMenuItem(langMenu, "Español (Spanish)", AppLanguage.Spanish);
            AddLanguageMenuItem(langMenu, "Français (French)", AppLanguage.French);
            AddLanguageMenuItem(langMenu, "日本語 (Japanese)", AppLanguage.Japanese);
            menu.Items.Add(langMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("About MiniFanControl v" + APP_VERSION + "...", null, (s, e) => ShowAboutDialog());
            return menu;
        }

        private void AddLanguageMenuItem(ToolStripMenuItem parent, string title, AppLanguage lang)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(title, null, (s, e) =>
            {
                Loc.SetLanguage(lang);
                ApplyLocalization();
                SaveSettings();
                RebuildDriversMenu();
                if (_statusBarLabel != null)
                {
                    _statusBarLabel.Text = string.Format("✓ Language: {0}", lang.ToString());
                }
            });
            if (Loc.CurrentLanguage == lang) item.Checked = true;
            parent.DropDownItems.Add(item);
        }

        private void ApplyLocalization()
        {
            if (_modeButtons != null && _modeButtons.Length >= 4)
            {
                if (_modeButtons[0] != null) _modeButtons[0].Text = Loc.T("Btn_Balanced", "⚖ Balanced");
                if (_modeButtons[1] != null) _modeButtons[1].Text = Loc.T("Btn_Performance", "⚡ Performance");
                if (_modeButtons[2] != null) _modeButtons[2].Text = Loc.T("Btn_Silent", "🌙 Silent");
                if (_modeButtons[3] != null) _modeButtons[3].Text = Loc.T("Btn_AiWorkload", "🤖 AI Workload");
            }
            if (_customLabel != null) _customLabel.Text = Loc.T("Label_Custom", "Custom:");
            if (_activateCustomBtn != null) _activateCustomBtn.Text = Loc.T("Btn_UseCurve", "Use Curve");
            if (_autoTuneCustomBtn != null) _autoTuneCustomBtn.Text = Loc.T("Btn_AutoTune", "✨ Auto-Tune");
            if (_addCustomBtn != null) _addCustomBtn.Text = Loc.T("Btn_New", "+ New");
            UpdateCustomRowButtons();
        }

        private void ShowKernelDriverSecurityDialog()
        {
            bool hvci = NativeInpOut.IsHvciEnabled();
            string drv = NativeInpOut.ActiveDriverName;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl Kernel Driver & HVCI Security Status\n");
            sb.AppendLine("• Active Kernel Driver Engine: " + drv);
            sb.AppendLine("• Binary SHA256 Pin Check: " + (NativeInpOut.IsDriverHashVerified
                ? ("VERIFIED (" + (NativeInpOut.ActiveDriverSha256 ?? "") + ")")
                : "Not Verified / Not Loaded"));
            sb.AppendLine("• Windows 11 Memory Integrity (HVCI): " + (hvci ? "ENABLED (Strict Kernel Code Integrity)" : "Disabled"));
            sb.AppendLine();
            if (NativeInpOut.ActiveDriver == HardwareDriverEngine.InpOut64)
            {
                sb.AppendLine("STATUS: InpOut64 Active (inpoutx64.dll)");
                sb.AppendLine("Standard InpOut64 driver passed runtime SHA256 pin verification before loading. Note that with Windows 11 Memory Integrity (HVCI) enabled, Microsoft's vulnerable driver blocklist may block InpOut64 service startup.");
                if (hvci) sb.AppendLine("HVCI is enabled: If hardware writes fail, run in Demo Mode or disable Memory Integrity in Windows Security.");
            }
            else if (NativeInpOut.ActiveDriver == HardwareDriverEngine.WinRing0)
            {
                sb.AppendLine("STATUS: WinRing0 Active (WinRing0x64.dll)");
                sb.AppendLine("WinRing0 driver passed runtime SHA256 pin verification before loading. Note that HVCI blocklists block older WinRing0 drivers.");
            }
            else
            {
                sb.AppendLine("STATUS: NO KERNEL DRIVER LOADED (Demo Mode / Read-Only)");
                if (!string.IsNullOrEmpty(NativeInpOut.LastDriverIntegrityError))
                {
                    sb.AppendLine("SECURITY ALERT: " + NativeInpOut.LastDriverIntegrityError);
                }
                sb.AppendLine("Hardware port I/O is disabled; running in safe read-only monitoring/simulation mode.");
            }
            MessageBox.Show(sb.ToString(), "Kernel Driver & Security Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            _hasDualFans = _sysInfo.MatchedProfile.HasDualFans;
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
            if (ms < 100) ms = 100;
            if (ms > 10000) ms = 10000;
            _refreshIntervalMs = ms;
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = _refreshIntervalMs;
            }
            if (_samplingCombo != null)
            {
                int[] intervals = new int[] { 100, 250, 500, 1000, 1500, 2000, 3000 };
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
                _statusBarLabel.Text = string.Format("✓ Data sampling rate set to {0} ms ({1:0.0} Hz / {2:0.00}s)", ms, hz, ms / 1000.0);
            }
        }

        private string GetSettingsPath()
        {
            return AppSettings.GetSettingsPath();
        }

        public static CurvePoint[] ParsePointList(string str)
        {
            return AppSettings.ParsePointList(str);
        }

        public static string FormatPointList(CurvePoint[] pts)
        {
            return AppSettings.FormatPointList(pts);
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
            return AppSettings.TryParseHexOrDecByte(s, out val);
        }

        private void LoadSettings()
        {
            if (_isUnitTest) return;
            _settings.Load(_sysInfo, _hw, _customProfiles);
            FanCurveProfile apuProf = FindProfileByName(_settings.ApuModeName) ?? FindProfileByName(_settings.LegacyCustomName);
            FanCurveProfile sysProf = FindProfileByName(_settings.SysModeName) ?? FindProfileByName(_settings.LegacyCustomName);
            if (apuProf != null) _apuProfile = apuProf;
            if (sysProf != null) _sysProfile = sysProf;
            if (_controller != null && _settings.BiosPowerMode >= 0 && _settings.BiosPowerMode <= 2)
            {
                _controller.BiosPowerModeOverride = _settings.BiosPowerMode;
                _lastActiveBiosModeCode = _settings.BiosPowerMode;
            }
        }

        private void SaveSettings()
        {
            if (_controller != null)
            {
                _settings.BiosPowerMode = _controller.BiosPowerModeOverride;
            }
            _settings.Save(_apuProfile, _sysProfile, _customProfiles);
            RebuildTrayContextMenus();
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
            if (_controller != null) _controller.SetRampUntil(_rampUntil);
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
                _showOverlay ? "Hide In-Game OSD Overlay" : "Show In-Game OSD Overlay",
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
            menu.Items.Add("Mode: AI Workload", null, (s, e) => ApplyMode(3));
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
            linksItem.DropDownItems.Add("✨ Setup Assistant & Auto-Tune Guide...", null, (s, e) => ShowSetupAssistantDialog(true));
            linksItem.DropDownItems.Add("Calibrate Fans...", null, (s, e) => RunGuiFanCalibration());
            linksItem.DropDownItems.Add("Export Hardware && EC Probe Report...", null, (s, e) => ExportHardwareProbeReport());
            linksItem.DropDownItems.Add("Detected Hardware && Multi-Model Info...", null, (s, e) => ShowHardwareCompatibilityDialog());
            linksItem.DropDownItems.Add(new ToolStripSeparator());
            linksItem.DropDownItems.Add("GMKtec Official Drivers && Manuals...", null, (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            linksItem.DropDownItems.Add("AMD Software: Adrenalin && Chipset Drivers...", null, (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            linksItem.DropDownItems.Add(new ToolStripSeparator());
            linksItem.DropDownItems.Add("About MiniFanControl v" + APP_VERSION + "...", null, (s, e) => ShowAboutDialog());
            menu.Items.Add(linksItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Reset to Auto", null, (s, e) => ApplyAutoAll());
            menu.Items.Add("Exit (Restore BIOS Auto)", null, (s, e) =>
            {
                _forceFullExit = true;
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
            Icon baseCpu = (Icon != null) ? (Icon)Icon.Clone() : SystemIcons.Application;
            Icon baseGpu = (Icon != null) ? (Icon)Icon.Clone() : SystemIcons.Application;
            _cpuTrayIcon = new NotifyIcon
            {
                Text = "CPU Temperature - MiniFanControl",
                Icon = baseCpu,
                ContextMenuStrip = CreateTrayContextMenu(),
                Visible = _showCpuTray
            };
            _cpuTrayIcon.DoubleClick += (s, e) => RestoreFromTray();

            _gpuTrayIcon = new NotifyIcon
            {
                Text = "iGPU Temperature - MiniFanControl",
                Icon = baseGpu,
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
                    if (oldIcon != null && oldIcon != Icon && oldIcon != SystemIcons.Application)
                    {
                        try { oldIcon.Dispose(); } catch { }
                    }
                }
                else if (cpuIcon != null && cpuIcon != Icon && cpuIcon != SystemIcons.Application)
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
                    if (oldIcon != null && oldIcon != Icon && oldIcon != SystemIcons.Application)
                    {
                        try { oldIcon.Dispose(); } catch { }
                    }
                }
                else if (gpuIcon != null && gpuIcon != Icon && gpuIcon != SystemIcons.Application)
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
            using (Pen borderPen = new Pen(border, 1.5f))
            {
                g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

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

            int cw = box.ClientSize.Width;
            int ch = box.ClientSize.Height;
            Color dimCol = isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim));
            Color textCol = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));

            // Sleek 3px left accent strip matching the metric series color
            using (SolidBrush accentBrush = new SolidBrush(lineCol))
            {
                g.FillRectangle(accentBrush, 0, 0, 3, ch);
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
                using (Pen activePen = new Pen(lineCol, 2f))
                {
                    g.DrawRectangle(activePen, 1, 1, cw - 2, ch - 2);
                }
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
                    _perfCanvas.Location = new Point(8, 42);
                    _perfCanvas.Size = new Size(1056, 648);
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
                    _liveCanvas.Location = new Point(8, 42);
                    _liveCanvas.Size = new Size(1056, 648);
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
                    _curveCanvas.Location = new Point(8, 108);
                    _curveCanvas.Size = new Size(1056, 454);
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
                Size = new Size(72, 72),
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
                Location = new Point(88, 16),
                Size = new Size(360, 24),
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
                Location = new Point(88, 42),
                Size = new Size(360, 18),
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwSubLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwSubLabel);

            // Explicit BIOS Power Mode (TDP) Switcher — independent of Custom Fan Curves (EC 0x31/0x32)
            bool showBiosPowerBar = (_demoMode || _hw == null || _hw.SuperIoBaseAddr == 0);
            string biosPowerTip =
                "BIOS Power Mode (EC 0x31/0x32) — Live APU TDP Preset!\r\n" +
                "• 🌙 Silent (~35W)  |  ⚖ Balanced (~48W)  |  ⚡ Perf (~60W+ Peak)\r\n" +
                "• When ✨ Auto-Tuned is active, switching BIOS Power also adapts your Auto-Tuned fan knee automatically.\r\n" +
                "• When using a manual Custom Curve or 🔒 preset, switching BIOS Power changes TDP without replacing your curve.";
            _biosPowerLabel = new Label
            {
                Text = "BIOS:",
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(452, 27),
                Size = new Size(42, 18),
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
                    Location = new Point(biosX, 22),
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

            _driversMenu = CreateDriversAndToolsMenu();

            _driversMenuBtn = new Button
            {
                Text = "Tools && Drivers ▾",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(788, 22),
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
                Location = new Point(930, 22),
                Size = new Size(156, 28),
                Cursor = Cursors.Hand
            };
            _themeToggleBtn.FlatAppearance.BorderSize = 0;
            _themeToggleBtn.Click += (s, e) => CycleThemePreference();
            Controls.Add(_themeToggleBtn);
            _themeToggleBtn.BringToFront();

            // 1. Hardware & Live Telemetry Section (top = 78, card = 98..358, height = 260) — 2x4 Expanded Metric Grid
            Panel statusCard = CreateSectionCard("System Telemetry & Live Hardware Sensors", 78, 260);

            const int boxW = 257;
            const int boxH = 108;
            const int boxGapX = 6;
            const int row1Y = 8;
            const int row2Y = 122;

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
            Label modeStaticLbl = AddStaticLabel(statusCard, "Mode:", 12, 236, 52, 20);
            modeStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _modeLabel = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(62, 236),
                Size = new Size(266, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_modeLabel);

            Label apuStaticLbl = AddStaticLabel(statusCard, "APU Fan:", 332, 236, 68, 20);
            apuStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct1Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(400, 236),
                Size = new Size(226, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct1Label);

            Label sysStaticLbl = AddStaticLabel(statusCard, "Sys Fan:", 630, 236, 64, 20);
            sysStaticLbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _pct2Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(694, 236),
                Size = new Size(248, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(_pct2Label);

            // 2. Modes & Fan Curve Section (top = 366, card = 386..976, height = 590)
            Panel modeCard = CreateSectionCard("Modes, Fan Curves & Live Telemetry Graph", 366, 590);
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

            // Row 4 (y=108): Large Interactive Fan Curve Graph + Combined Live Telemetry Graph (1056 x 454)
            _curveCanvas = new FanCurveCanvas
            {
                Location = new Point(8, 108),
                Size = new Size(1056, 454),
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
                Size = new Size(1056, 648),
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
                Size = new Size(1056, 648),
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
                Size = new Size(1056, 648),
                Visible = false
            };
            BuildOsdStudioPanel(_osdConfigPanel);
            modeCard.Controls.Add(_osdConfigPanel);

            _curveHintLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 566),
                Size = new Size(1056, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };
            modeCard.Controls.Add(_curveHintLabel);

            // 3. Manual Fan Control Section (top = 992, card = 1012..1106, height = 94)
            Panel fanCard = CreateSectionCard("Manual Fan Control", 992, 94);
            _fanCardPanel = fanCard;
            _manualFanHeaderLabel = _sectionHeaders.Count > 0 ? _sectionHeaders[_sectionHeaders.Count - 1] : null;

            _calibBadgeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(544, 990),
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

            // 4. Options Section (top = 1112, card = 1132..1218, height = 86)
            Panel optCard = CreateSectionCard("Options", 1112, 86);

            _samplingLabel = new Label
            {
                Text = "Sampling:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(766, 11),
                AutoSize = true,
                Cursor = Cursors.Default
            };
            optCard.Controls.Add(_samplingLabel);

            _samplingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(854, 7),
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
                Text = "Show In-Game OSD overlay",
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

            _useFahrenheitCb = new CheckBox
            {
                Text = "Use Fahrenheit (°F)",
                Location = new Point(660, 32),
                Size = new Size(260, 24),
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
                Location = new Point(660, 56),
                Size = new Size(260, 24),
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
                Location = new Point(14, 1132),
                Size = new Size(1072, 82),
                Padding = new Padding(8),
                Visible = false
            };
            Controls.Add(_errorBanner);
            _errorBanner.BringToFront();

            // 5. Status Bar (y = 1220..1248)
            _statusBarLabel = new Label
            {
                Text = "⏳ Initializing…",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(14, 1223),
                Size = new Size(820, 22)
            };
            Controls.Add(_statusBarLabel);

            _allAutoBtn = new Button
            {
                Text = "Reset Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(850, 1220),
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
                Location = new Point(966, 1220),
                Size = new Size(106, 28),
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

        private void UpdateWideLayout()
        {
            if (_wideRightPanel == null || _modeCardPanel == null || _liveCanvas == null || _perfCanvas == null)
                return;

            int clientW = _testClientWidthOverride > 0 ? _testClientWidthOverride : ClientSize.Width;
            int clientH = _testClientHeightOverride > 0 ? _testClientHeightOverride : ClientSize.Height;
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
                        _curveCanvas.Location = new Point(8, 108);
                        _curveCanvas.Size = new Size(1056, 454);
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
                int canvasH = _manualFanControlVisible ? 518 : 648;

                if (_liveCanvas.Parent != _modeCardPanel) _liveCanvas.Parent = _modeCardPanel;
                _liveCanvas.Location = new Point(8, 42);
                _liveCanvas.Size = new Size(1056, canvasH);
                _liveCanvas.Visible = _showingLiveTelemetry;

                if (_perfCanvas.Parent != _modeCardPanel) _perfCanvas.Parent = _modeCardPanel;
                _perfCanvas.Location = new Point(8, 42);
                _perfCanvas.Size = new Size(1056, canvasH);
                _perfCanvas.Visible = _showingPerfGraph;

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
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            Color accent = isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent));
            Color activeFg = isRadeon ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            _wideTrendBtn.BackColor = !_wideRightShowPerf ? accent : surface2;
            _wideTrendBtn.ForeColor = !_wideRightShowPerf ? activeFg : text;
            _widePerfBtn.BackColor = _wideRightShowPerf ? accent : surface2;
            _widePerfBtn.ForeColor = _wideRightShowPerf ? activeFg : text;
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
                Location = new Point(746, 5),
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
                Location = new Point(894, 5),
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
                Location = new Point(492, 38),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            p.Controls.Add(_osdGraphHeightLabel);

            // Row 2 Buttons (y=56, h=26): 5 modes across x=8..482
            _osdBtnBothGraphs = new Button
            {
                Text = "Both (All)",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(8, 56),
                Size = new Size(88, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnBothGraphs.FlatAppearance.BorderSize = 0;
            _osdBtnBothGraphs.Click += (s, e) => { _osd.GraphMode = 3; _osd.ShowLiveTrend = true; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnBothGraphs, "Show both mini sparklines per metric and combined live trend graph");
            p.Controls.Add(_osdBtnBothGraphs);

            _osdBtnCombinedGraph = new Button
            {
                Text = "Combined",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(100, 56),
                Size = new Size(88, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnCombinedGraph.FlatAppearance.BorderSize = 0;
            _osdBtnCombinedGraph.Click += (s, e) => { _osd.GraphMode = 2; _osd.ShowLiveTrend = true; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnCombinedGraph, "Show combined multi-series live trend graph only");
            p.Controls.Add(_osdBtnCombinedGraph);

            _osdBtnSparklines = new Button
            {
                Text = "Sparklines",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(192, 56),
                Size = new Size(88, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnSparklines.FlatAppearance.BorderSize = 0;
            _osdBtnSparklines.Click += (s, e) => { _osd.GraphMode = 1; _osd.ShowLiveTrend = false; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnSparklines, "Show mini inline sparklines per metric only");
            p.Controls.Add(_osdBtnSparklines);

            _osdBtnTextOnly = new Button
            {
                Text = "Text Only",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(284, 56),
                Size = new Size(86, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnTextOnly.FlatAppearance.BorderSize = 0;
            _osdBtnTextOnly.Click += (s, e) => { _osd.GraphMode = 0; _osd.ShowLiveTrend = false; if (_osdShowLiveTrendCb != null) _osdShowLiveTrendCb.Checked = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            _uiToolTip.SetToolTip(_osdBtnTextOnly, "Display metrics as text only without live graphs");
            p.Controls.Add(_osdBtnTextOnly);

            _osdBtnGraphsOnly = new Button
            {
                Text = "Graphs Only",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Location = new Point(374, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnGraphsOnly.FlatAppearance.BorderSize = 0;
            _osdBtnGraphsOnly.Click += (s, e) =>
            {
                _osd.GraphMode = 4;
                if (!_osd.ShowFpsGraph && !_osd.ShowLiveTrend)
                {
                    _osd.ShowFpsGraph = true;
                    if (_osdShowFpsGraphCb != null) _osdShowFpsGraphCb.Checked = true;
                }
                SyncOverlayFromStudio();
                UpdateOsdStudioStyles();
            };
            _uiToolTip.SetToolTip(_osdBtnGraphsOnly, "Hide all metric text rows and display ONLY the enabled graphs (toggle FPS & Frametime Graph and/or Live Trend Graph below).");
            p.Controls.Add(_osdBtnGraphsOnly);

            // Right side: Height buttons
            _osdBtnHeightCompact = new Button
            {
                Text = "75px Compact",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(492, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightCompact.FlatAppearance.BorderSize = 0;
            _osdBtnHeightCompact.Click += (s, e) => { _osd.GraphHeight = 75; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightCompact);

            _osdBtnHeightMedium = new Button
            {
                Text = "105px Medium",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(608, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightMedium.FlatAppearance.BorderSize = 0;
            _osdBtnHeightMedium.Click += (s, e) => { _osd.GraphHeight = 105; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightMedium);

            _osdBtnHeightLarge = new Button
            {
                Text = "140px Large",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(724, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightLarge.FlatAppearance.BorderSize = 0;
            _osdBtnHeightLarge.Click += (s, e) => { _osd.GraphHeight = 140; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightLarge);

            _osdBtnHeightXl = new Button
            {
                Text = "180px XL",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(840, 56),
                Size = new Size(108, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightXl.FlatAppearance.BorderSize = 0;
            _osdBtnHeightXl.Click += (s, e) => { _osd.GraphHeight = 180; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
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
                Location = new Point(492, 88),
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
            _osdBtnScale100.Click += (s, e) => { _osd.ScaleTier = 0; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
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
            _osdBtnScale125.Click += (s, e) => { _osd.ScaleTier = 1; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
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
            _osdBtnScale150.Click += (s, e) => { _osd.ScaleTier = 2; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
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
            _osdBtnScale185.Click += (s, e) => { _osd.ScaleTier = 3; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale185);

            _osdBtnLayoutH = new Button
            {
                Text = "▬ Horizontal Bar",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(492, 106),
                Size = new Size(224, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutH.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutH.Click += (s, e) => { _osd.VerticalLayout = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnLayoutH);

            _osdBtnLayoutV = new Button
            {
                Text = "▮ Vertical Stack",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(724, 106),
                Size = new Size(224, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutV.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutV.Click += (s, e) => { _osd.VerticalLayout = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
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
                Value = Math.Max(35, Math.Min(100, _osd.OpacityPct)),
                TickStyle = TickStyle.None,
                Location = new Point(72, 134),
                Size = new Size(130, 30),
                AccessibleName = "OSD Overlay Opacity Percentage"
            };
            _osdOpacitySlider.Scroll += (s, e) =>
            {
                _osd.OpacityPct = _osdOpacitySlider.Value;
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);
                if (_overlayForm != null) _overlayForm.ApplyOpacity(_osd.OpacityPct);
                SaveSettings();
            };
            p.Controls.Add(_osdOpacitySlider);

            _osdOpacityPctLabel = new Label
            {
                Text = string.Format("{0}%", _osd.OpacityPct),
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
                Location = new Point(244, 138),
                Size = new Size(88, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdSnapLabel);

            Font snapBtnFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);
            _osdQuickPosTl = new Button { Text = "Top-Left", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(334, 136), Size = new Size(112, 26), Cursor = Cursors.Hand };
            _osdQuickPosTl.FlatAppearance.BorderSize = 0;
            _osdQuickPosTl.Click += (s, e) => SetOverlayCornerPosition(0);
            _uiToolTip.SetToolTip(_osdQuickPosTl, "Snap OSD to Top-Left screen corner");
            p.Controls.Add(_osdQuickPosTl);

            _osdQuickPosTc = new Button { Text = "Center-Top", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(450, 136), Size = new Size(116, 26), Cursor = Cursors.Hand };
            _osdQuickPosTc.FlatAppearance.BorderSize = 0;
            _osdQuickPosTc.Click += (s, e) => SetOverlayCornerPosition(4);
            _uiToolTip.SetToolTip(_osdQuickPosTc, "Snap OSD to Center-Top screen edge");
            p.Controls.Add(_osdQuickPosTc);

            _osdQuickPosTr = new Button { Text = "Top-Right", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(570, 136), Size = new Size(112, 26), Cursor = Cursors.Hand };
            _osdQuickPosTr.FlatAppearance.BorderSize = 0;
            _osdQuickPosTr.Click += (s, e) => SetOverlayCornerPosition(1);
            _uiToolTip.SetToolTip(_osdQuickPosTr, "Snap OSD to Top-Right screen corner");
            p.Controls.Add(_osdQuickPosTr);

            _osdQuickPosBl = new Button { Text = "Bottom-Left", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(686, 136), Size = new Size(116, 26), Cursor = Cursors.Hand };
            _osdQuickPosBl.FlatAppearance.BorderSize = 0;
            _osdQuickPosBl.Click += (s, e) => SetOverlayCornerPosition(2);
            _uiToolTip.SetToolTip(_osdQuickPosBl, "Snap OSD to Bottom-Left screen corner");
            p.Controls.Add(_osdQuickPosBl);

            _osdQuickPosBc = new Button { Text = "Center-Bottom", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(806, 136), Size = new Size(122, 26), Cursor = Cursors.Hand };
            _osdQuickPosBc.FlatAppearance.BorderSize = 0;
            _osdQuickPosBc.Click += (s, e) => SetOverlayCornerPosition(5);
            _uiToolTip.SetToolTip(_osdQuickPosBc, "Snap OSD to Center-Bottom screen edge");
            p.Controls.Add(_osdQuickPosBc);

            _osdQuickPosBr = new Button { Text = "Bottom-Right", FlatStyle = FlatStyle.Flat, Font = snapBtnFont, Location = new Point(932, 136), Size = new Size(116, 26), Cursor = Cursors.Hand };
            _osdQuickPosBr.FlatAppearance.BorderSize = 0;
            _osdQuickPosBr.Click += (s, e) => SetOverlayCornerPosition(3);
            _uiToolTip.SetToolTip(_osdQuickPosBr, "Snap OSD to Bottom-Right screen corner");
            p.Controls.Add(_osdQuickPosBr);

            // Row 5 (y=174): Section 4 - Telemetry Metrics & Overlay Parts Header + Select All / Defaults Buttons
            _osdMetricsHeaderLabel = new Label
            {
                Text = "Visible Telemetry Metrics & Overlay Parts:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(8, 174),
                Size = new Size(460, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdMetricsHeaderLabel);

            _osdSelectAllBtn = new Button
            {
                Text = "Select All",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(712, 170),
                Size = new Size(104, 25),
                Cursor = Cursors.Hand
            };
            _osdSelectAllBtn.FlatAppearance.BorderSize = 0;
            _osdSelectAllBtn.Click += (s, e) =>
            {
                _osd.SelectAllMetrics(_hasDualFans);
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdSelectAllBtn);

            _osdDeselectAllBtn = new Button
            {
                Text = "Deselect All",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(824, 170),
                Size = new Size(108, 25),
                Cursor = Cursors.Hand
            };
            _osdDeselectAllBtn.FlatAppearance.BorderSize = 0;
            _osdDeselectAllBtn.Click += (s, e) =>
            {
                _osd.DeselectAllMetrics();
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdDeselectAllBtn);

            _osdResetDefaultsBtn = new Button
            {
                Text = "Defaults",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                Location = new Point(940, 170),
                Size = new Size(108, 25),
                Cursor = Cursors.Hand
            };
            _osdResetDefaultsBtn.FlatAppearance.BorderSize = 0;
            _osdResetDefaultsBtn.Click += (s, e) =>
            {
                _osd.ResetDefaults(_hasDualFans);
                SyncOsdStudioUiFromOverlay();
                SyncOverlayFromStudio();
            };
            p.Controls.Add(_osdResetDefaultsBtn);

            // Row 6+ (y=200..648): Per-Metric Configuration Sections (fits cleanly without scrollbar)
            _osdMetricsScrollPanel = new Panel
            {
                Location = new Point(4, 200),
                Size = new Size(1048, 448),
                AutoScroll = true
            };
            p.Controls.Add(_osdMetricsScrollPanel);
            _osdSectionHeaders.Clear();

            int[] colX = new[] { 8, 260, 512, 764 };
            const int colW = 246;
            const int cbH = 22;
            const int rowStep = 21;
            int curY = 0;

            // 1. CPU (left) & Power & VRM (right)
            int pairY = curY;
            AddOsdSectionHeader(_osdMetricsScrollPanel, "CPU", ref curY, 8, 490);
            _osdShowCpuCb = AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY, 242, cbH, "CPU Temp", _osd.ShowCpu, v => { _osd.ShowCpu = v; SyncOverlayFromStudio(); }, null);
            _osdShowCpuLoadCb = AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY, 242, cbH, "CPU Load (%)", _osd.ShowCpuLoad, v => { _osd.ShowCpuLoad = v; SyncOverlayFromStudio(); },
                "Show CPU utilization percentage on the CPU overlay row");
            _osdShowClocksCb = AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY + rowStep, 242, cbH, "Clock Speeds (@ GHz/MHz)", _osd.ShowClocks, v => { _osd.ShowClocks = v; SyncOverlayFromStudio(); },
                "Display effective CPU, iGPU, and RAM clock frequencies (@ 4.1G / @ 800MHz). Uncheck to maximize space for sparklines.");

            int rightY = pairY;
            AddOsdSectionHeader(_osdMetricsScrollPanel, "Power & VRM", ref rightY, 518, 490);
            _osdShowPowerCb = AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY, 242, cbH, "APU Power (W)", _osd.ShowPower, v => { _osd.ShowPower = v; SyncOverlayFromStudio(); }, null);
            _osdShowPowerLimitCb = AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY, 242, cbH, "APU Power Limit (/W)", _osd.ShowPowerLimit, v => { _osd.ShowPowerLimit = v; SyncOverlayFromStudio(); },
                "Show active PPT/STAPM power limit (/65W) next to APU Power");
            _osdShowVrmCurrentCb = AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY + rowStep, 242, cbH, "VRM Current (TDC/EDC)", _osd.ShowVrmCurrent, v => { _osd.ShowVrmCurrent = v; SyncOverlayFromStudio(); },
                "Display deep silicon VRM amperage draw (continuous TDC and peak EDC amps for VDD & SOC rails)");
            curY += rowStep * 2;

            // 2. iGPU & Radeon
            AddOsdSectionHeader(_osdMetricsScrollPanel, "iGPU & Radeon", ref curY);
            _osdShowGpuCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "iGPU Temp", _osd.ShowGpu, v => { _osd.ShowGpu = v; SyncOverlayFromStudio(); }, null);
            _osdShowGpuHotspotCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "iGPU Hotspot (🔥°C)", _osd.ShowGpuHotspot, v => { _osd.ShowGpuHotspot = v; SyncOverlayFromStudio(); },
                "Show iGPU junction hotspot temperature (🔥°C) when available");
            _osdShowGpuLoadCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "GPU Load & Clock", _osd.ShowGpuLoad, v => { _osd.ShowGpuLoad = v; SyncOverlayFromStudio(); }, null);
            _osdShowRadeonCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "Radeon Feature Tags", _osd.ShowRadeon, v => { _osd.ShowRadeon = v; SyncOverlayFromStudio(); },
                "Display compact pill tags for active AMD Radeon features (FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)");
            _osdShowDriverVerCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Radeon Driver Version", _osd.ShowDriverVer, v => { _osd.ShowDriverVer = v; SyncOverlayFromStudio(); },
                "Show the installed AMD Radeon Software (Adrenalin) driver version on the OSD");
            _osdShowRadeonExtTagsCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "SAM, EnhSync & FRTC Tags", _osd.ShowRadeonExtTags, v => { _osd.ShowRadeonExtTags = v; SyncOverlayFromStudio(); },
                "Include Smart Access Memory (SAM), Enhanced Sync, and FRTC cap pills in the OSD Radeon tags row");
            curY += rowStep * 2;

            // 3. RAM (DDR5)
            AddOsdSectionHeader(_osdMetricsScrollPanel, "RAM (DDR5)", ref curY);
            _osdShowRamCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "RAM Load & Power", _osd.ShowRam, v => { _osd.ShowRam = v; SyncOverlayFromStudio(); }, null);
            _osdShowRamTempCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "RAM Temp (°C)", _osd.ShowRamTemp, v => { _osd.ShowRamTemp = v; SyncOverlayFromStudio(); },
                "Show DDR5 temperature on the RAM overlay row");
            _osdShowRamDualSticksCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "RAM Dual-Stick (35/41°C)", _osd.ShowRamDualSticks, v => { _osd.ShowRamDualSticks = v; SyncOverlayFromStudio(); },
                "Show both SODIMM temperatures (35/41°C). Uncheck to show only the single hottest stick (41°C).");
            _osdShowRamSpreadCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "RAM Spread Delta (Δ°C)", _osd.ShowRamSpread, v => { _osd.ShowRamSpread = v; SyncOverlayFromStudio(); },
                "Show thermal spread delta badge (Δ6°) between stacked SODIMM slots");
            _osdShowRamLoadCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "RAM Load (%)", _osd.ShowRamLoad, v => { _osd.ShowRamLoad = v; SyncOverlayFromStudio(); },
                "Show memory utilization percentage (e.g. 22%) on the RAM overlay row");
            _osdShowRamPowerCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "RAM Power Draw (~W)", _osd.ShowRamPower, v => { _osd.ShowRamPower = v; SyncOverlayFromStudio(); },
                "Show DDR5 power draw (~3.6W) on the RAM overlay row");
            _osdShowRamClockCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "RAM Clock (@ MHz)", _osd.ShowRamClock, v => { _osd.ShowRamClock = v; SyncOverlayFromStudio(); },
                "Show effective DDR data rate (@ 5600MHz) on the RAM overlay row (independent of CPU/iGPU clocks)");
            curY += rowStep * 2;

            // 4. NVMe Storage (left) & Chassis & Silicon (right)
            pairY = curY;
            AddOsdSectionHeader(_osdMetricsScrollPanel, "NVMe Storage", ref curY, 8, 490);
            _osdShowNvmeCb = AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY, 242, cbH, "NVMe SSD Temp", _osd.ShowNvme, v => { _osd.ShowNvme = v; SyncOverlayFromStudio(); }, null);
            _osdShowNvmePowerCb = AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY, 242, cbH, "NVMe Power Draw (~W)", _osd.ShowNvmePower, v => { _osd.ShowNvmePower = v; SyncOverlayFromStudio(); },
                "Show PCIe NVMe SSD power draw (~0.9W) on the NVMe overlay row");
            _osdShowNvmeIoCb = AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY + rowStep, 242, cbH, "NVMe I/O (MB/s)", _osd.ShowNvmeIo, v => { _osd.ShowNvmeIo = v; SyncOverlayFromStudio(); },
                "Show live NVMe read/write throughput (MB/s) on the NVMe overlay row");
            _osdShowNvmeDualCb = AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY + rowStep, 242, cbH, "NVMe Dual-Drive (°C/°C)", _osd.ShowNvmeDual, v => { _osd.ShowNvmeDual = v; SyncOverlayFromStudio(); },
                "Show second NVMe drive temperature when idle and a second drive is detected");

            rightY = pairY;
            AddOsdSectionHeader(_osdMetricsScrollPanel, "Chassis & Silicon", ref rightY, 518, 490);
            _osdShowAmbientCb = AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY, 242, cbH, "Chassis Ambient", _osd.ShowAmbient, v => { _osd.ShowAmbient = v; SyncOverlayFromStudio(); },
                "Display motherboard chassis ambient temperature (IT8613 Sensor 4)");
            _osdShowFabricClockCb = AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY, 242, cbH, "Infinity Fabric (FCLK)", _osd.ShowFabricClock, v => { _osd.ShowFabricClock = v; SyncOverlayFromStudio(); },
                "Display AMD Infinity Fabric clock frequency (FCLK) and 1:1 memory bus synchronization status");
            _osdShowFclkSyncCb = AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY + rowStep, 242, cbH, "FCLK Sync Badge (1:1)", _osd.ShowFclkSync, v => { _osd.ShowFclkSync = v; SyncOverlayFromStudio(); },
                "Show 1:1 Sync / Async badge on the Infinity Fabric row");
            _osdShowNpuCb = AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY + rowStep, 242, cbH, "Ryzen AI NPU", _osd.ShowNpu, v => { _osd.ShowNpu = v; SyncOverlayFromStudio(); },
                "Display AMD Ryzen AI NPU activity percentage (PMLOG_NPU_BUSY_AVG) and inference clock");
            curY += rowStep * 2;

            // 5. FPS & Frametime
            AddOsdSectionHeader(_osdMetricsScrollPanel, "FPS & Frametime", ref curY);
            _osdShowFpsCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "Live FPS & Frametime", _osd.ShowFps, v => { _osd.ShowFps = v; SyncOverlayFromStudio(); }, null);
            _osdShowFrametimeMsCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "FPS Frametime (ms)", _osd.ShowFrametimeMs, v => { _osd.ShowFrametimeMs = v; SyncOverlayFromStudio(); },
                "Show frame time in milliseconds (e.g. 16.7ms) next to FPS");
            _osdShowLowsCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "0.1% Low & Avg FPS", _osd.ShowLows, v => { _osd.ShowLows = v; SyncOverlayFromStudio(); }, null);
            _osdShowFpsAvgCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "FPS Session Avg", _osd.ShowFpsAvg, v => { _osd.ShowFpsAvg = v; SyncOverlayFromStudio(); },
                "Show session average FPS on the FPS row instead of peak/min tracker");
            curY += rowStep;

            // 6. Fans
            AddOsdSectionHeader(_osdMetricsScrollPanel, "Fans", ref curY);
            _osdShowApuFanCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "APU Fan RPM", _osd.ShowApuFan, v => { _osd.ShowApuFan = v; SyncOverlayFromStudio(); }, null);
            _osdShowSysFanCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, _hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)", _osd.ShowSysFan && _hasDualFans, v => { _osd.ShowSysFan = v; SyncOverlayFromStudio(); }, null);
            _osdShowSysFanCb.Enabled = _hasDualFans;
            curY += rowStep;

            // 7. Graphs, Theme & Global Overlay
            _osdSubFieldsHeaderLabel = AddOsdSectionHeader(_osdMetricsScrollPanel, "Graphs, Theme & Global Overlay", ref curY);
            _osdShowLiveTrendCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "Live Trend Graph", _osd.ShowLiveTrend, v =>
            {
                _osd.ShowLiveTrend = v;
                if (_osd.ShowLiveTrend)
                {
                    if (_osd.GraphMode == 0) _osd.GraphMode = 2;
                    else if (_osd.GraphMode == 1) _osd.GraphMode = 3;
                }
                else
                {
                    if (_osd.GraphMode == 2) _osd.GraphMode = 0;
                    else if (_osd.GraphMode == 3) _osd.GraphMode = 1;
                }
                SyncOverlayFromStudio();
                UpdateOsdStudioStyles();
            }, "Display the multi-sensor live history trend chart (CPU, iGPU, NVMe, Power, Fan RPM)");
            _osdShowFpsGraphCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "FPS & Frametime Graph", _osd.ShowFpsGraph, v => { _osd.ShowFpsGraph = v; SyncOverlayFromStudio(); }, null);
            _osdDfGraphStyleCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "DF Twin Graphs", _osd.DfGraphStyle, v => { _osd.DfGraphStyle = v; SyncOverlayFromStudio(); },
                "Render Digital Foundry style twin graphs: FRAME-TIME (MS) 0-50-100 and FRAME-RATE (FPS) 0-40-80-120");
            _osdDfGraphFirstCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "DF Graphs on Top", _osd.DfGraphFirst, v => { _osd.DfGraphFirst = v; SyncOverlayFromStudio(); },
                "Render Digital Foundry style twin graphs above the multi-sensor Live Trend graph");

            _osdPlotFpsOnTrendCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Plot FPS on Live Trend", _osd.PlotFpsOnTrend, v => { _osd.PlotFpsOnTrend = v; SyncOverlayFromStudio(); }, null);
            _osdSparklineBgCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "Background Sparklines", _osd.SparklineBg, v => { _osd.SparklineBg = v; SyncOverlayFromStudio(); },
                "Render sparklines as full-width glowing background underlays across each metric row");
            _osdShowModeCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "Thermal Mode", _osd.ShowMode, v => { _osd.ShowMode = v; SyncOverlayFromStudio(); }, null);
            _osdShowGameNameCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY + rowStep, colW, cbH, "Active Game Title", _osd.ShowGameName, v => { _osd.ShowGameName = v; SyncOverlayFromStudio(); },
                "Display the active 3D game process name on the OSD header and horizontal bar");

            _osdMatchThemeCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep * 2, colW, cbH, "Match App Theme", _osd.MatchTheme, v => { _osd.MatchTheme = v; SyncOverlayFromStudio(); }, null);
            _osdShowPeaksCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep * 2, 96, cbH, "Trackers", _osd.ShowPeaks, v =>
            {
                _osd.ShowPeaks = v;
                if (_osdTrackerModeCombo != null) _osdTrackerModeCombo.Enabled = _osd.ShowPeaks;
                SyncOverlayFromStudio();
            }, "Show session Peak (▲), Low (▼), Min/Max (▼▲), or Avg (~) trackers for temperature, power, and usage.");

            _osdTrackerModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                Location = new Point(colX[1] + 98, curY + 2 * rowStep),
                Size = new Size(134, 24),
                Enabled = _osd.ShowPeaks,
                AccessibleName = "OSD Peak Tracker Mode",
                Cursor = Cursors.Hand
            };
            _osdTrackerModeCombo.Items.AddRange(new object[] { "Peaks (▲)", "Lows (▼)", "Min/Max (▼▲)", "Avg (~)" });
            _osdTrackerModeCombo.SelectedIndex = Math.Max(0, Math.Min(3, _osd.PeakTrackerMode));
            _osdTrackerModeCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_osdTrackerModeCombo.SelectedIndex >= 0)
                {
                    _osd.PeakTrackerMode = _osdTrackerModeCombo.SelectedIndex;
                    SyncOverlayFromStudio();
                }
            };
            _uiToolTip.SetToolTip(_osdTrackerModeCombo, "Choose whether the right-hand tracker column displays Peaks (▲), Lows (▼), Min/Max (▼▲), or Session Average (~).");
            _osdMetricsScrollPanel.Controls.Add(_osdTrackerModeCombo);
            _osdDfCornerInsetCb = AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep * 2, colW, cbH, "DF Corner Inset", _osd.DfCornerInset, v =>
            {
                _osd.DfCornerInset = v;
                if (v)
                {
                    _osd.ShowFpsGraph = true;
                    _osd.DfGraphStyle = true;
                    if (_osdShowFpsGraphCb != null) _osdShowFpsGraphCb.Checked = true;
                    if (_osdDfGraphStyleCb != null) _osdDfGraphStyleCb.Checked = true;
                }
                SyncOverlayFromStudio();
            }, "Render a compact 30-sample Digital Foundry Frame-Time box tucked inside the top-left corner of the FPS graph.");
            curY += rowStep * 3 + 4;

            _osdTipLabel = new Label
            {
                Text = "Tip: Drag OSD with mouse. Right-click for options. Ctrl+Alt+F9 toggles.",
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                Location = new Point(8, curY),
                Size = new Size(990, 22),
                Padding = new Padding(4, 2, 4, 2),
                UseMnemonic = false
            };
            _uiToolTip.SetToolTip(_osdTipLabel, "Drag OSD anywhere across multiple monitors with your mouse. Right-click the OSD anytime for quick scale, graph modes, heights & click-through lock, or press Ctrl+Alt+F9 to toggle overlay.");
            _osdMetricsScrollPanel.Controls.Add(_osdTipLabel);
        }

        private Label AddOsdSectionHeader(Panel parent, string title, ref int curY, int x = 8, int width = 990)
        {
            if (curY > 2) curY += 3;
            Label lbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(x, curY),
                Size = new Size(width, 17),
                UseMnemonic = false
            };
            parent.Controls.Add(lbl);
            _osdSectionHeaders.Add(lbl);
            curY += 17;
            return lbl;
        }

        private CheckBox AddOsdCheckBox(Panel parent, int x, int y, int width, int height, string text, bool initialChecked, Action<bool> onChanged, string tooltip)
        {
            CheckBox cb = new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Checked = initialChecked,
                UseMnemonic = false
            };
            cb.CheckedChanged += (s, e) => onChanged(cb.Checked);
            if (!string.IsNullOrEmpty(tooltip))
                _uiToolTip.SetToolTip(cb, tooltip);
            parent.Controls.Add(cb);
            return cb;
        }

        private void EnsureProtectedExamplesPresent()
        {
            List<FanCurveProfile> examples = FanCurveProfile.CreateProtectedExamples();
            int insertPos = 0;
            for (int i = 0; i < examples.Count; i++)
            {
                FanCurveProfile ex = examples[i];
                FanCurveProfile existing = null;
                foreach (FanCurveProfile cp in _customProfiles)
                {
                    if (cp.Name.Equals(ex.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = cp;
                        break;
                    }
                }

                if (existing != null)
                {
                    existing.IsProtectedExample = true;
                    existing.DefaultApuPoints = FanCurveProfile.ClonePoints(ex.DefaultApuPoints);
                    existing.DefaultSysPoints = FanCurveProfile.ClonePoints(ex.DefaultSysPoints);
                }
                else
                {
                    if (insertPos <= _customProfiles.Count)
                        _customProfiles.Insert(insertPos, ex);
                    else
                        _customProfiles.Add(ex);
                    insertPos++;
                }
            }
        }

        private void UpdateCustomRowButtons()
        {
            if (_deleteCustomBtn == null) return;
            bool isProtected = false;
            if (_customModeCombo != null)
            {
                int idx = _customModeCombo.SelectedIndex;
                if (idx >= 0 && idx < _customProfiles.Count)
                {
                    isProtected = _customProfiles[idx].IsProtectedExample;
                }
            }
            _deleteCustomBtn.Text = isProtected
                ? Loc.T("Btn_Reset", "Reset")
                : Loc.T("Btn_Del", "Del");
            if (_uiToolTip != null)
            {
                _uiToolTip.SetToolTip(_deleteCustomBtn, isProtected
                    ? "Protected reference curve (🔒 cannot be deleted). Click Reset to restore its factory curve points."
                    : "Deletes the currently selected custom curve profile.");
            }
        }

        private int GetActiveBiosPowerModeCode()
        {
            if (_controller != null && _controller.BiosPowerModeOverride >= 0 && _controller.BiosPowerModeOverride <= 2)
                return _controller.BiosPowerModeOverride;
            if (_lastStatus.ModeCode >= 0 && _lastStatus.ModeCode <= 2)
                return _lastStatus.ModeCode;
            if (_lastActiveBiosModeCode >= 0 && _lastActiveBiosModeCode <= 2)
                return _lastActiveBiosModeCode;
            return -1;
        }

        private FanCurveProfile FindAutoTunedProfile()
        {
            foreach (FanCurveProfile cp in _customProfiles)
            {
                if (!cp.IsProtectedExample && cp.Name.StartsWith("✨ Auto-Tuned", StringComparison.OrdinalIgnoreCase))
                    return cp;
            }
            return null;
        }

        public void RunAutoTuneCurve()
        {
            string summary;
            int biosCode = GetActiveBiosPowerModeCode();
            FanCurveProfile tuned = FanCurveProfile.SynthesizeAutoTunedCurve(_lastStatus, _sessionStats, out summary, biosCode);

            FanCurveProfile target = FindAutoTunedProfile();
            if (target != null)
            {
                target.Name = tuned.Name;
                target.ApuPoints = FanCurveProfile.ClonePoints(tuned.ApuPoints);
                target.SysPoints = FanCurveProfile.ClonePoints(tuned.SysPoints);
            }
            else
            {
                target = tuned;
                _customProfiles.Add(target);
            }

            if (_showingLiveTelemetry || _showingPerfGraph || _showingOsdConfig)
            {
                SetGraphViewMode(false, false, 0);
            }

            // Apply Auto-Tuned curve to BOTH APU Fan and Sys Fan automatically
            if (_controller != null)
            {
                _controller.SetProfileTarget(target, false);
                if (_hasDualFans)
                    _controller.SetProfileTarget(target, true);
            }
            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.Invalidate();
            }
            PopulateCustomCombo();
            UpdateCurveTabStyles();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            UpdateCurveHint();
            SaveSettings();
            StartRampIndication(_hasDualFans ? "APU + Sys Fans → " + target.Name : "APU Fan → " + target.Name, 0);

            if (_statusBarLabel != null)
            {
                _statusBarLabel.Text = summary;
            }
            if (_curveHintLabel != null)
            {
                _curveHintLabel.Text = summary;
            }
        }

        private void RefreshAutoTunedCurveAfterCalibration()
        {
            FanCurveProfile target = FindAutoTunedProfile();
            if (target == null) return;

            string summary;
            int biosCode = GetActiveBiosPowerModeCode();
            FanCurveProfile tuned = FanCurveProfile.SynthesizeAutoTunedCurve(_lastStatus, _sessionStats, out summary, biosCode);
            target.Name = tuned.Name;
            target.ApuPoints = FanCurveProfile.ClonePoints(tuned.ApuPoints);
            target.SysPoints = FanCurveProfile.ClonePoints(tuned.SysPoints);

            PopulateCustomCombo();
            if (_apuProfile == target || _sysProfile == target)
            {
                if (_controller != null) _controller.InvalidateAppliedCache();
                if (_curveCanvas != null)
                    _curveCanvas.Invalidate();
                if (_statusBarLabel != null)
                    _statusBarLabel.Text = summary;
                if (_curveHintLabel != null)
                    _curveHintLabel.Text = summary;
            }
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
            UpdateCustomRowButtons();
            UpdateCurveTabStyles();
            UpdateCurveHint();
        }

        private void OnCustomComboSelected(object sender, EventArgs e)
        {
            UpdateCustomRowButtons();
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
            if (_controller != null) _controller.SetProfileTarget(profile, editSys);

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

            FanCurveProfile selected = _customProfiles[idx];
            if (selected.IsProtectedExample)
            {
                selected.ResetToDefaults();
                if (_controller != null) _controller.InvalidateAppliedCache();
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
                if (_statusBarLabel != null)
                {
                    _statusBarLabel.Text = "✓ Reset protected example curve to factory defaults: " + selected.Name;
                }
                return;
            }

            if (_customProfiles.Count <= 1)
            {
                MessageBox.Show(this, "At least one custom curve profile is kept.", "K12 Fan Control",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FanCurveProfile removed = selected;
            _customProfiles.RemoveAt(idx);
            if (_controller != null) _controller.OnProfileRemoved(removed);
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
            _tabApuCurveBtn.Text = string.Format("● APU Fan: {0}", ShortModeTag(apuModeStr));
            _tabSysCurveBtn.Text = string.Format("● Sys Fan: {0}", ShortModeTag(sysModeStr));
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
            if (_tabPerfGraphBtn != null)
            {
                int curFps = _lastStatus.RtssFps;
                _tabPerfGraphBtn.Text = (curFps > 0) ? string.Format("● Perf: {0}", curFps) : Loc.T("Tab_LivePerf", "● Live Perf");
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

                if (_tabPerfGraphBtn != null)
                {
                    _tabPerfGraphBtn.BackColor = surface2;
                    _tabPerfGraphBtn.ForeColor = text;
                    if (_tabPerfGraphBtn.Font != TabFontRegular) _tabPerfGraphBtn.Font = TabFontRegular;
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

            if (_showingPerfGraph)
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

                if (_tabPerfGraphBtn != null)
                {
                    _tabPerfGraphBtn.BackColor = Color.FromArgb(0x22, 0xD3, 0xEE);
                    _tabPerfGraphBtn.ForeColor = Color.Black;
                    if (_tabPerfGraphBtn.Font != TabFontBold) _tabPerfGraphBtn.Font = TabFontBold;
                }
            }
            else if (_showingLiveTelemetry)
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

                if (_tabPerfGraphBtn != null)
                {
                    _tabPerfGraphBtn.BackColor = surface2;
                    _tabPerfGraphBtn.ForeColor = text;
                    if (_tabPerfGraphBtn.Font != TabFontRegular) _tabPerfGraphBtn.Font = TabFontRegular;
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

                if (_tabPerfGraphBtn != null)
                {
                    _tabPerfGraphBtn.BackColor = surface2;
                    _tabPerfGraphBtn.ForeColor = text;
                    if (_tabPerfGraphBtn.Font != TabFontRegular) _tabPerfGraphBtn.Font = TabFontRegular;
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

                if (_tabPerfGraphBtn != null)
                {
                    _tabPerfGraphBtn.BackColor = surface2;
                    _tabPerfGraphBtn.ForeColor = text;
                    if (_tabPerfGraphBtn.Font != TabFontRegular) _tabPerfGraphBtn.Font = TabFontRegular;
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
            Button[] modeBtns = new[] { _osdBtnTextOnly, _osdBtnSparklines, _osdBtnCombinedGraph, _osdBtnBothGraphs, _osdBtnGraphsOnly };
            for (int i = 0; i < modeBtns.Length; i++)
            {
                if (modeBtns[i] == null) continue;
                bool active = (_osd.GraphMode == i);
                modeBtns[i].BackColor = active ? accent : surface2;
                modeBtns[i].ForeColor = active ? activeFg : text;
                modeBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Scale button (100%, 125%, 150%, 185%)
            Button[] scaleBtns = new[] { _osdBtnScale100, _osdBtnScale125, _osdBtnScale150, _osdBtnScale185 };
            for (int i = 0; i < scaleBtns.Length; i++)
            {
                if (scaleBtns[i] == null) continue;
                bool active = (_osd.ScaleTier == i);
                scaleBtns[i].BackColor = active ? accent : surface2;
                scaleBtns[i].ForeColor = active ? activeFg : text;
                scaleBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Layout button
            if (_osdBtnLayoutH != null)
            {
                bool activeH = !_osd.VerticalLayout;
                _osdBtnLayoutH.BackColor = activeH ? accent : surface2;
                _osdBtnLayoutH.ForeColor = activeH ? activeFg : text;
                _osdBtnLayoutH.Font = activeH ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnLayoutV != null)
            {
                bool activeV = _osd.VerticalLayout;
                _osdBtnLayoutV.BackColor = activeV ? accent : surface2;
                _osdBtnLayoutV.ForeColor = activeV ? activeFg : text;
                _osdBtnLayoutV.Font = activeV ? TabFontBold : TabFontRegular;
            }

            // Highlight active Height button (75px Compact, 105px Medium, 140px Large, 180px XL)
            if (_osdBtnHeightCompact != null)
            {
                bool active = (_osd.GraphHeight <= 85);
                _osdBtnHeightCompact.BackColor = active ? accent : surface2;
                _osdBtnHeightCompact.ForeColor = active ? activeFg : text;
                _osdBtnHeightCompact.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightMedium != null)
            {
                bool active = (_osd.GraphHeight > 85 && _osd.GraphHeight <= 120);
                _osdBtnHeightMedium.BackColor = active ? accent : surface2;
                _osdBtnHeightMedium.ForeColor = active ? activeFg : text;
                _osdBtnHeightMedium.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightLarge != null)
            {
                bool active = (_osd.GraphHeight > 120 && _osd.GraphHeight <= 160);
                _osdBtnHeightLarge.BackColor = active ? accent : surface2;
                _osdBtnHeightLarge.ForeColor = active ? activeFg : text;
                _osdBtnHeightLarge.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightXl != null)
            {
                bool active = (_osd.GraphHeight > 160);
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
            if (_osdSubFieldsHeaderLabel != null) { _osdSubFieldsHeaderLabel.BackColor = surface; _osdSubFieldsHeaderLabel.ForeColor = text; }
            if (_osdMetricsScrollPanel != null) { _osdMetricsScrollPanel.BackColor = surface; _osdMetricsScrollPanel.ForeColor = text; }
            foreach (Label secHdr in _osdSectionHeaders)
            {
                if (secHdr != null)
                {
                    secHdr.BackColor = surface;
                    secHdr.ForeColor = accent;
                }
            }
            if (_osdTipLabel != null)
            {
                _osdTipLabel.BackColor = surface2;
                _osdTipLabel.ForeColor = textDim;
            }

            // CheckBoxes inside studio
            CheckBox[] cbs = new[]
            {
                _osdShowCpuCb, _osdShowGpuCb, _osdShowNvmeCb, _osdShowRamCb,
                _osdShowVrmCurrentCb, _osdShowAmbientCb, _osdShowFabricClockCb, _osdShowNpuCb,
                _osdShowApuFanCb, _osdShowSysFanCb, _osdShowPowerCb, _osdShowGpuLoadCb,
                _osdShowFpsCb, _osdShowLowsCb,
                _osdShowModeCb, _osdShowPeaksCb, _osdShowRadeonCb, _osdShowRadeonExtTagsCb, _osdShowDriverVerCb,
                _osdShowClocksCb, _osdShowGameNameCb, _osdSparklineBgCb, _osdMatchThemeCb,
                _osdShowLiveTrendCb, _osdShowFpsGraphCb, _osdPlotFpsOnTrendCb, _osdDfGraphStyleCb, _osdDfCornerInsetCb, _osdDfGraphFirstCb,
                _osdShowRamTempCb, _osdShowRamDualSticksCb, _osdShowRamSpreadCb, _osdShowRamLoadCb,
                _osdShowRamPowerCb, _osdShowRamClockCb, _osdShowNvmePowerCb,
                _osdShowNvmeIoCb, _osdShowNvmeDualCb, _osdShowCpuLoadCb, _osdShowGpuHotspotCb,
                _osdShowPowerLimitCb, _osdShowFclkSyncCb, _osdShowFrametimeMsCb, _osdShowFpsAvgCb
            };
            foreach (CheckBox cb in cbs)
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                }
            }
            if (_osdTrackerModeCombo != null)
            {
                _osdTrackerModeCombo.BackColor = surface2;
                _osdTrackerModeCombo.ForeColor = text;
            }
        }

        private void UpdateCurveHint()
        {
            if (_curveHintLabel == null || _curveCanvas == null) return;
            if (_showingOsdConfig)
            {
                if (!_demoMode && !_lastStatus.RadeonFeaturesAvailable)
                    _curveHintLabel.Text = "OSD Overlay Studio — ℹ No AMD display driver detected: Radeon iGPU sensors & OSD feature pills are unavailable";
                else if (!_demoMode && _lastStatus.RadeonFeaturesAvailable && !_lastStatus.RadeonAdrenalinInstalled)
                    _curveHintLabel.Text = "OSD Overlay Studio — ℹ AMD Minimal/Driver-Only install: FreeSync & iGPU sensors work, but RIS/AFMF/per-game FSR pills need Adrenalin";
                else
                    _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                return;
            }
            if (_showingPerfGraph)
            {
                int curFps = _lastStatus.RtssFps;
                double curFt = _lastStatus.RtssFrametimeMs > 0 ? _lastStatus.RtssFrametimeMs : (curFps > 0 ? (1000.0 / curFps) : 0.0);
                string game = CapFrameXOverlayForm.FormatGameName(_lastStatus.RtssProcessName);
                string gameTag = !string.IsNullOrEmpty(game) ? ("🎮 " + game) : (_lastStatus.RtssRunning ? "RTSS Active (Desktop / Idle)" : "RTSS Standby");
                _curveHintLabel.Text = string.Format("Live Performance Analysis — {0} • {1} FPS • {2:0.0} ms frametime — hover to scrub timeline, right-click to export PNG",
                    gameTag, curFps > 0 ? curFps.ToString() : "--", curFt);
                return;
            }
            if (_showingLiveTelemetry)
            {
                string hlName = _highlightedMetric == 2 ? "iGPU Temp"
                    : (_highlightedMetric == 7 ? "RAM"
                    : (_highlightedMetric == 8 ? "Infinity Fabric (FCLK)"
                    : (_highlightedMetric == 6 ? "NVMe Temp"
                    : (_highlightedMetric == 3 ? "APU Power"
                    : (_highlightedMetric == 9 ? "Chassis Ambient Temp"
                    : (_highlightedMetric == 4 ? "APU Fan Speed"
                    : (_highlightedMetric == 5 ? "System Fan Speed" : "CPU Temp")))))));
                _curveHintLabel.Text = string.Format("Spotlighting {0} — click pills to switch, or click '● APU Fan' / '● Sys Fan' tabs above to view & edit fan curve", hlName);
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
            if (SystemInformation.HighContrast)
            {
                BackColor = SystemColors.Window;
                ForeColor = SystemColors.WindowText;
                foreach (Label h in _sectionHeaders)
                    h.ForeColor = SystemColors.HotTrack;
                foreach (Panel c in _sectionCards)
                    c.BackColor = SystemColors.Control;
                foreach (Label l in _staticLabels)
                {
                    l.BackColor = SystemColors.Control;
                    l.ForeColor = SystemColors.ControlText;
                }
                foreach (DoubleBufferedPanel box in new[] { _cpuTempBox, _gpuTempBox, _nvmeTempBox, _pwrBox, _ramPowerBox, _ambientBox, _fan1Box, _fan2Box, _ramCard })
                {
                    if (box != null) box.BackColor = SystemColors.Window;
                }
                foreach (Label lbl in new[] { _cpuBoxTitle, _cpuTempLabel, _gpuBoxTitle, _gpuTempLabel, _nvmeBoxTitle, _nvmeTempLabel, _pwrBoxTitle, _pwrLabel, _ramBoxTitle, _ramPowerLabel, _ambientBoxTitle, _ambientLabel, _fan1BoxTitle, _fan1RpmLabel, _fan2BoxTitle, _fan2RpmLabel, _ramLabel })
                {
                    if (lbl != null)
                    {
                        lbl.BackColor = SystemColors.Window;
                        lbl.ForeColor = SystemColors.WindowText;
                    }
                }
                if (_curveCanvas != null) _curveCanvas.Invalidate();
                if (_liveCanvas != null) _liveCanvas.Invalidate();
                if (_perfCanvas != null) _perfCanvas.Invalidate();
                return;
            }

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

            if (_biosPowerLabel != null)
            {
                _biosPowerLabel.BackColor = bg;
                _biosPowerLabel.ForeColor = textDim;
            }
            UpdateBiosPowerModeStyles(_lastActiveBiosModeCode);

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

            if (_wideRightPanel != null)
                _wideRightPanel.BackColor = surface;
            if (_wideRightHeaderLabel != null)
            {
                _wideRightHeaderLabel.BackColor = surface;
                _wideRightHeaderLabel.ForeColor = accent;
            }
            UpdateWideRightButtonStyles();

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

            if (_ambientBox != null)
            {
                _ambientBox.BackColor = tempBoxBg;
                _ambientBoxTitle.BackColor = tempBoxBg;
                _ambientBoxTitle.ForeColor = tempBoxTitleFg;
                _ambientLabel.BackColor = tempBoxBg;
                _ambientLabel.ForeColor = tempBoxFg;
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
            if (_perfCanvas != null)
            {
                _perfCanvas.IsDarkMode = _isDarkMode;
                _perfCanvas.IsRadeonTheme = isRadeon; _perfCanvas.IsAfterburnerTheme = IsAb;
                _perfCanvas.Invalidate();
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

            foreach (Button b in new[] { _autoTuneCustomBtn, _addCustomBtn, _deleteCustomBtn, _fan1AutoBtn, _fan2AutoBtn, _allAutoBtn, _closeBtn })
            {
                if (b != null)
                {
                    b.BackColor = surface2;
                    b.ForeColor = (b == _autoTuneCustomBtn) ? accent : text;
                }
            }

            foreach (CheckBox cb in new[] { _showCpuTrayCb, _showGpuTrayCb, _showOverlayCb, _lockOverlayCb, _autostartCb, _alwaysOnTopCb, _useFahrenheitCb, _trayCb })
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

            for (int i = 0; i < _modeButtons.Length; i++)
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

        private static readonly Font BiosModeFontBold = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        private static readonly Font BiosModeFontRegular = new Font("Segoe UI", 8.0f, FontStyle.Regular);

        private void UpdateBiosPowerModeStyles(int activeCode)
        {
            if (activeCode >= 0 && activeCode <= 2)
                _lastActiveBiosModeCode = activeCode;

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface2 = isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2));
            Color text = isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText));
            Color activeBg = isRadeon ? RadeonAccent : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent));
            Color activeFg = isRadeon ? Color.White : (IsAb ? Color.Black : (_isDarkMode ? Color.Black : Color.White));

            for (int i = 0; i < _biosModeButtons.Length; i++)
            {
                Button b = _biosModeButtons[i];
                if (b == null) continue;
                bool isActive = (i == _lastActiveBiosModeCode);
                if (b.BackColor != (isActive ? activeBg : surface2))
                    b.BackColor = isActive ? activeBg : surface2;
                if (b.ForeColor != (isActive ? activeFg : text))
                    b.ForeColor = isActive ? activeFg : text;
                Font targetFont = isActive ? BiosModeFontBold : BiosModeFontRegular;
                if (b.Font != targetFont)
                    b.Font = targetFont;
            }
        }

        private void ApplyBiosPowerMode(int code)
        {
            if (code < 0 || code > 2) return;
            string[] names = { "Balanced (~48W)", "Performance (~60W+)", "Silent (~35W)" };
            try
            {
                bool ok = _controller != null && _controller.SetBiosPowerMode(code);
                UpdateBiosPowerModeStyles(code);

                // If Auto-Tuned is active on either fan, adapt its TDP knee automatically for the new BIOS Power Mode
                // while leaving hand-edited custom curves and protected reference presets untouched.
                FanCurveProfile autoTuned = FindAutoTunedProfile();
                if (autoTuned != null && (_apuProfile == autoTuned || _sysProfile == autoTuned))
                {
                    string summary;
                    FanCurveProfile adapted = FanCurveProfile.SynthesizeAutoTunedCurve(_lastStatus, _sessionStats, out summary, code);
                    autoTuned.Name = adapted.Name;
                    autoTuned.ApuPoints = FanCurveProfile.ClonePoints(adapted.ApuPoints);
                    autoTuned.SysPoints = FanCurveProfile.ClonePoints(adapted.SysPoints);
                    if (_controller != null) _controller.InvalidateAppliedCache();
                    PopulateCustomCombo();
                    UpdateCurveTabStyles();
                    if (_curveCanvas != null) _curveCanvas.Invalidate();
                    if (_curveHintLabel != null) _curveHintLabel.Text = summary;
                }

                SaveSettings();
                if (ok)
                {
                    StartRampIndication("BIOS Power Mode → " + names[code], 0);
                }
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
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

        private int _uiSamplePending = 0;

        private void OnHardwareWorkerSample(FanStatus st)
        {
            Interlocked.Exchange(ref _uiSamplePending, 0);
            try
            {
                _lastStatus = st;
                if ((DateTime.UtcNow - _lastWorkerSampleUtc).TotalMilliseconds < 4500)
                {
                    PulseWatchdogHeartbeat();
                }
                ApplyStatusToUi(st);
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("OnHardwareWorkerSample UI update error: " + ex.Message);
            }
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
                string tipUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
                string tip = string.Format("CPU: {0}{4} (iGPU: {1}{4})\nAPU: {2} RPM | Sys: {3} RPM",
                    TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                    st.Fan1Rpm, st.Fan2Rpm, tipUnit);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_cpuTrayIcon.Text != tip) _cpuTrayIcon.Text = tip;
            }

            if (_gpuTrayIcon != null)
            {
                string tipUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
                string tip = string.Format("iGPU: {0}{4} (CPU: {1}{4})\nAPU: {2} RPM | Sys: {3} RPM",
                    TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                    st.Fan1Rpm, st.Fan2Rpm, tipUnit);
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

            string tUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
            string winTitle = string.Format("CPU {0}{2} | iGPU {1}{2} - MiniFanControl",
                TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                tUnit);
            if (Text != winTitle) Text = winTitle;

            if (_hwSubLabel != null && !string.IsNullOrEmpty(st.RadeonSoftwareVer))
            {
                string drvTag = st.RadeonAdrenalinInstalled
                    ? ("Radeon Software " + st.RadeonSoftwareVer)
                    : ("Radeon Driver-Only " + st.RadeonSoftwareVer);
                if (_hwSubLabel.Text.IndexOf("Radeon Software", StringComparison.Ordinal) < 0 &&
                    _hwSubLabel.Text.IndexOf("Radeon Driver-Only", StringComparison.Ordinal) < 0)
                    _hwSubLabel.Text = _hwSubLabel.Text + " • " + drvTag;
            }
            if (!_demoMode && NativeInpOut.ActiveDriver == HardwareDriverEngine.None &&
                _errorBanner != null && string.IsNullOrEmpty(_uiWriteErrorBannerDetail))
            {
                _errorBanner.Text = "⚠ No EC kernel driver loaded (InpOut64/WinRing0 missing or blocked) — fan control & EC telemetry won't work. Run as Administrator or check Tools & Drivers → Kernel Driver Status.";
                if (!_errorBanner.Visible) _errorBanner.Visible = true;
            }

            int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);
            if (_curveCanvas != null)
            {
                int sysThermalSource = (st.NvmeTemp > 0) ? Math.Max(apuTemp, st.NvmeTemp) : apuTemp;
                string apuSrc = (st.GpuTemp > st.CpuTemp) ? "iGPU" : "CPU";
                string sysSrc = (st.NvmeTemp > apuTemp) ? "NVMe" : apuSrc;
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.CurrentApuTemp = apuTemp;
                _curveCanvas.CurrentSysTemp = sysThermalSource;
                _curveCanvas.CurrentApuSource = apuSrc;
                _curveCanvas.CurrentSysSource = sysSrc;
                _curveCanvas.CurrentFan1Rpm = st.Fan1Rpm;
                _curveCanvas.CurrentFan2Rpm = st.Fan2Rpm;
                if (!_showingLiveTelemetry && !_showingPerfGraph && !_showingOsdConfig)
                    _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.UseFahrenheit = _useFahrenheit;
                _liveCanvas.Status = st;
                if (_liveCanvas.Visible)
                    _liveCanvas.Invalidate();
            }
            if (_curveCanvas != null && _curveCanvas.UseFahrenheit != _useFahrenheit)
            {
                _curveCanvas.UseFahrenheit = _useFahrenheit;
                _curveCanvas.Invalidate();
            }
            if (_perfCanvas != null)
            {
                _perfCanvas.SampleIntervalMs = _refreshIntervalMs;
                _perfCanvas.UpdateStatus(st);
                if (_perfCanvas.Visible)
                    _perfCanvas.Invalidate();
            }
            if (_overlayForm != null)
            {
                _overlayForm.SampleIntervalMs = _refreshIntervalMs;
            }

            Color tempFg = _isDarkMode ? DarkText : LightText;
            Color dimText = _isDarkMode ? Color.FromArgb(0x93, 0x99, 0xB2) : Color.FromArgb(0x6C, 0x70, 0x86);
            Color rampColor = _isDarkMode ? Color.FromArgb(0xFA, 0xB3, 0x87) : Color.FromArgb(0xEA, 0x58, 0x0C);
            bool isRadeon = (_themePref == ThemePreference.Radeon);

            Color autoColor = _isDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x15, 0x80, 0x3D);
            Color manColor = isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (_isDarkMode ? DarkAccent : LightAccent);
            Color warnRose = Color.FromArgb(0xFB, 0x71, 0x85);

            // 1. CPU Box (Row 1, Col 0)
            string cpuValTxt = st.CpuTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit), tUnit) : "N/A";
            if (_cpuTempLabel.Text != cpuValTxt) _cpuTempLabel.Text = cpuValTxt;
            if (_cpuTempLabel.ForeColor != tempFg) _cpuTempLabel.ForeColor = tempFg;
            if (_cpuBoxTitle != null && _cpuBoxTitle.Text != "CPU") _cpuBoxTitle.Text = "CPU";
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
            string cpuDetail1 = cpuLoadRange + cpuRailPart;
            string cpuDetail2 = st.IsThermalThrottling
                ? "⚠ THERMAL THROTTLING ACTIVE"
                : (st.NpuActivityPct > 0 || st.NpuClockMhz > 0
                    ? string.Format("Tctl/Tdie • NPU {0}% ({1} MHz)", st.NpuActivityPct, st.NpuClockMhz)
                    : "Ryzen Package • Tctl/Tdie");
            SetMetricDetail(1, cpuPeakTxt, cpuSubTxt, cpuDetail1, cpuDetail2,
                st.IsThermalThrottling ? warnRose : Color.Empty,
                st.IsThermalThrottling ? warnRose : Color.Empty);
            if (_cpuTempBox != null)
            {
                _cpuTempBox.AccessibleName = string.Format("CPU Temperature: {0}", cpuValTxt);
                _cpuTempBox.Invalidate();
            }

            // 2. iGPU Box (Row 1, Col 1)
            string gpuValTxt = st.GpuTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit), tUnit) : "N/A";
            if (_gpuTempLabel.Text != gpuValTxt) _gpuTempLabel.Text = gpuValTxt;
            if (_gpuTempLabel.ForeColor != tempFg) _gpuTempLabel.ForeColor = tempFg;
            if (_gpuBoxTitle != null && _gpuBoxTitle.Text != "iGPU") _gpuBoxTitle.Text = "iGPU";
            string gpuPeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuTemp > 0)
                ? string.Format("▼{0}° ~{1:0}° ▲{2}°",
                    TelemetrySessionStats.ToDisplayTemp(_sessionStats.MinGpuTemp, _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp((int)Math.Round(_sessionStats.AvgGpuTemp), _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(_sessionStats.MaxGpuTemp, _useFahrenheit))
                : "";
            string gpuSubTxt = "";
            if (st.GfxClockMhz > 0)
                gpuSubTxt = string.Format("{0}% • {1} MHz", st.GfxActivityPct, st.GfxClockMhz);
            else if (st.GfxActivityPct > 0)
                gpuSubTxt = string.Format("{0}% Load", st.GfxActivityPct);
            else
                gpuSubTxt = "0% • Idle";
            string gpuLoadRange = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuLoad > 0)
                ? string.Format("Load ▼{0}% ~{1:0}% ▲{2}%", _sessionStats.MinGpuLoad, _sessionStats.AvgGpuLoad, _sessionStats.MaxGpuLoad)
                : string.Format("Load {0}%", st.GfxActivityPct);
            string gpuExtraPart = st.GpuHotspotTemp > 0
                ? string.Format(" • 🔥{0}°", TelemetrySessionStats.ToDisplayTemp(st.GpuHotspotTemp, _useFahrenheit))
                : (st.GfxPowerWatts > 0 ? string.Format(" • GFX ~{0}W", st.GfxPowerWatts) : "");
            string gpuDetail1 = gpuLoadRange + gpuExtraPart;
            string gpuDrvPart = !string.IsNullOrEmpty(st.RadeonSoftwareVer) ? ("Adrenalin " + st.RadeonSoftwareVer) : "Radeon 780M";
            string gpuFeatPart = !string.IsNullOrEmpty(st.RadeonFeaturesSummary) ? (" • " + st.RadeonFeaturesSummary) : "";
            SetMetricDetail(2, gpuPeakTxt, gpuSubTxt, gpuDetail1, gpuDrvPart + gpuFeatPart, Color.Empty);
            if (_gpuTempBox != null)
            {
                _gpuTempBox.AccessibleName = string.Format("iGPU Temperature: {0}", gpuValTxt);
                _gpuTempBox.Invalidate();
            }

            // 3. RAM (DDR5) Box (Row 1, Col 2) — memory + dual-stick + FCLK (where Fabric syncs with DDR5)
            if (_ramPowerLabel != null)
            {
                string ramValTxt = st.DdrTemp > 0
                    ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.DdrTemp, _useFahrenheit), tUnit)
                    : (st.RamPowerWatts > 0 ? string.Format("~{0:F1}W", st.RamPowerWatts) : ("--" + tUnit));
                if (_ramPowerLabel.Text != ramValTxt) _ramPowerLabel.Text = ramValTxt;
                if (_ramPowerLabel.ForeColor != tempFg) _ramPowerLabel.ForeColor = tempFg;
                if (_ramBoxTitle != null && _ramBoxTitle.Text != "RAM (DDR5)") _ramBoxTitle.Text = "RAM (DDR5)";

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
                string ramDetail1 = ramStickPart + ramPwrPart + ramLoadPkPart;

                int ddrSpeed = st.RamSpeedMhz;
                string ramSpdPart = ddrSpeed > 0 ? string.Format("{0} MHz", ddrSpeed) : "DDR5";
                string fclkPart = st.FabricClockMhz > 0
                    ? string.Format(" • FCLK {0} MHz{1}", st.FabricClockMhz, st.IsFabricSynced ? " (1:1)" : "")
                    : "";
                string ramEstPart = st.IsDdrEstimated ? " [Est]" : "";
                string ramDetail2 = ramSpdPart + fclkPart + ramEstPart;

                Color ramSubCol = (st.RamLoadPct >= 90) ? warnRose : Color.Empty;
                SetMetricDetail(7, ramPeakTxt, ramSubTxt, ramDetail1, ramDetail2, ramSubCol);

                if (_ramPowerBox != null)
                {
                    _ramPowerBox.AccessibleName = string.Format("RAM Sensor: {0}", ramValTxt);
                    _ramPowerBox.Invalidate();
                }
            }

            // 4. NVMe Storage Box (Row 1, Col 3)
            if (_nvmeTempLabel != null)
            {
                string nvmeValTxt = st.NvmeTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.NvmeTemp, _useFahrenheit), tUnit) : "N/A";
                if (_nvmeTempLabel.Text != nvmeValTxt) _nvmeTempLabel.Text = nvmeValTxt;
                Color nvmeFg = (st.NvmeTemp >= 70) ? warnRose : tempFg;
                if (_nvmeTempLabel.ForeColor != nvmeFg) _nvmeTempLabel.ForeColor = nvmeFg;
                if (_nvmeBoxTitle != null && _nvmeBoxTitle.Text != "NVMe") _nvmeBoxTitle.Text = "NVMe";

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
                string nvmeDetail1 = nvmeHealthPart + nvmeQdPart + nvmeSsd2Part;
                string nvmeDetail2 = !string.IsNullOrEmpty(st.NvmeModel) ? st.NvmeModel : "NVMe SSD";
                SetMetricDetail(6, nvmePeakTxt, nvmeSubTxt, nvmeDetail1, nvmeDetail2, Color.Empty);

                if (_nvmeTempBox != null)
                {
                    _nvmeTempBox.AccessibleName = string.Format("NVMe Temperature: {0}", nvmeValTxt);
                    _nvmeTempBox.Invalidate();
                }
            }

            // 5. APU Power Box (Row 2, Col 0)
            if (_pwrLabel != null)
            {
                string pwrValTxt = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                if (_pwrLabel.Text != pwrValTxt) _pwrLabel.Text = pwrValTxt;
                if (_pwrLabel.ForeColor != tempFg) _pwrLabel.ForeColor = tempFg;
                if (_pwrBoxTitle != null && _pwrBoxTitle.Text != "POWER") _pwrBoxTitle.Text = "POWER";

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
                        : (st.IsApuPowerEstimated ? "Estimated TDP Envelope" : "APU Package Rail"));
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
                SetMetricDetail(3, pwrPeakTxt, pwrSubTxt, pwrDetail1, pwrDetail2, Color.Empty);

                if (_pwrBox != null)
                {
                    _pwrBox.AccessibleName = string.Format("APU Power: {0}", pwrValTxt);
                    _pwrBox.Invalidate();
                }
            }

            // 6. Chassis / VRM / Live FPS Box (Row 2, Col 1) — clean board thermals (no FCLK here)
            if (_ambientLabel != null)
            {
                string ambValTxt = st.AmbientTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.AmbientTemp, _useFahrenheit), tUnit) : "N/A";
                if (_ambientLabel.Text != ambValTxt) _ambientLabel.Text = ambValTxt;
                if (_ambientLabel.ForeColor != tempFg) _ambientLabel.ForeColor = tempFg;
                if (_ambientBoxTitle != null && _ambientBoxTitle.Text != "CHASSIS") _ambientBoxTitle.Text = "CHASSIS";

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
                SetMetricDetail(9, ambPeakTxt, ambSubTxt, ambDetail1, ambDetail2, Color.Empty);

                if (_ambientBox != null)
                {
                    _ambientBox.AccessibleName = string.Format("Chassis Ambient Temperature: {0}", ambValTxt);
                    _ambientBox.Invalidate();
                }
            }

            if (!_hasDualFans || apuModeName == sysModeName)
                _modeLabel.Text = apuModeName;
            else
                _modeLabel.Text = string.Format("{0} / {1}", apuModeName, sysModeName);

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(st.ModeCode);
            int liveBiosCode = (st.ModeCode >= 0 && st.ModeCode <= 2)
                ? st.ModeCode
                : (_controller != null ? _controller.BiosPowerModeOverride : -1);
            UpdateBiosPowerModeStyles(liveBiosCode);

            if (_emergencyThermalActive)
            {
                _pct1Label.Text = "100% (Emergency)";
                _pct1Label.ForeColor = warnRose;
            }
            else if (_apuManualOverride)
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
            else if (_emergencyThermalActive)
            {
                _pct2Label.Text = "100% (Emergency)";
                _pct2Label.ForeColor = warnRose;
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
                string nvmeTag = (st.NvmeTemp > apuTemp && st.NvmeTemp >= 50)
                    ? string.Format(" • NVMe {0}{1}", TelemetrySessionStats.ToDisplayTemp(st.NvmeTemp, _useFahrenheit), tUnit)
                    : "";
                _pct2Label.Text = (st.Fan2Manual == 0) ? string.Format("Auto ({0}{1})", ShortModeTag(sysModeName), nvmeTag) : string.Format("{0}% ({1}{2})", sysEval, ShortModeTag(sysModeName), nvmeTag);
                _pct2Label.ForeColor = autoColor;
            }

            // 7. APU Fan Box (Row 2, Col 2) — Low/Avg/High RPM + Tuning Profile on its own full line
            string f1ValTxt = string.Format("{0} RPM", st.Fan1Rpm);
            if (_fan1RpmLabel.Text != f1ValTxt) _fan1RpmLabel.Text = f1ValTxt;
            if (_fan1RpmLabel.ForeColor != tempFg) _fan1RpmLabel.ForeColor = tempFg;
            if (_fan1Box != null)
            {
                if (_fan1BoxTitle != null && _fan1BoxTitle.Text != "APU FAN") _fan1BoxTitle.Text = "APU FAN";
                string fan1PeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxFan1Rpm > 0)
                    ? string.Format("▼{0} ~{1:0} ▲{2}", _sessionStats.MinFan1Rpm, _sessionStats.AvgFan1Rpm, _sessionStats.MaxFan1Rpm)
                    : "";
                string fan1Detail1 = _emergencyThermalActive
                    ? "APU / CPU Cooler • Emergency Override"
                    : (_apuManualOverride
                        ? "APU / CPU Cooler • Manual Override"
                        : (st.Fan1Manual == 0 ? "APU / CPU Cooler • BIOS Auto" : "APU / CPU Cooler • Active Curve"));
                string fan1Detail2 = apuModeName;
                SetMetricDetail(4, fan1PeakTxt, _pct1Label.Text, fan1Detail1, fan1Detail2, _pct1Label.ForeColor, tempFg);
                _fan1Box.AccessibleName = string.Format("APU Fan Speed: {0} RPM", st.Fan1Rpm);
                _fan1Box.Invalidate();
            }

            // 8. Sys Fan Box (Row 2, Col 3) — Low/Avg/High RPM + Tuning Profile on its own full line
            if (_fan2RpmLabel != null)
            {
                string f2ValTxt = !_hasDualFans ? "N/A" : (st.Fan2Rpm > 0 ? string.Format("{0} RPM", st.Fan2Rpm) : "-- RPM");
                if (_fan2RpmLabel.Text != f2ValTxt) _fan2RpmLabel.Text = f2ValTxt;
                if (_fan2RpmLabel.ForeColor != tempFg) _fan2RpmLabel.ForeColor = tempFg;
            }
            if (_fan2Box != null)
            {
                if (_fan2BoxTitle != null && _fan2BoxTitle.Text != "SYS FAN") _fan2BoxTitle.Text = "SYS FAN";
                string fan2PeakTxt = (_hasDualFans && _sessionStats.SampleCount > 0 && _sessionStats.MaxFan2Rpm > 0)
                    ? string.Format("▼{0} ~{1:0} ▲{2}", _sessionStats.MinFan2Rpm, _sessionStats.AvgFan2Rpm, _sessionStats.MaxFan2Rpm)
                    : "";
                string fan2Detail1 = !_hasDualFans
                    ? "Single-Fan Chassis"
                    : (_emergencyThermalActive
                        ? "RAM / NVMe Cooler • Emergency Override"
                        : (_sysManualOverride
                            ? "RAM / NVMe Cooler • Manual Override"
                            : (st.Fan2Manual == 0 ? "RAM / NVMe Cooler • BIOS Auto" : "RAM / NVMe Cooler • Active Curve")));
                string fan2Detail2 = !_hasDualFans ? "Disabled" : sysModeName;
                SetMetricDetail(5, fan2PeakTxt, _pct2Label.Text, fan2Detail1, fan2Detail2, _pct2Label.ForeColor, !_hasDualFans ? Color.Empty : tempFg);
                _fan2Box.AccessibleName = !_hasDualFans ? "System Fan: Disabled" : string.Format("System Fan Speed: {0} RPM", st.Fan2Rpm);
                _fan2Box.Invalidate();
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
            if (_controller != null) _controller.RequestImmediateRefresh();
        }

        private static string ShortModeTag(string modeName)
        {
            if (string.IsNullOrEmpty(modeName)) return "Auto";
            if (modeName.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase)) return "Bal";
            if (modeName.StartsWith("Performance", StringComparison.OrdinalIgnoreCase)) return "Perf";
            if (modeName.StartsWith("Silent", StringComparison.OrdinalIgnoreCase)) return "Sil";
            if (modeName.StartsWith("AI", StringComparison.OrdinalIgnoreCase)) return "AI";
            if (modeName.StartsWith("✨ Auto-Tuned", StringComparison.OrdinalIgnoreCase) || modeName.IndexOf("Auto-Tuned", StringComparison.OrdinalIgnoreCase) >= 0) return "AutoTune";
            if (modeName.IndexOf("Zero-RPM", StringComparison.OrdinalIgnoreCase) >= 0) return "0-RPM";
            if (modeName.IndexOf("Gaming Sweet", StringComparison.OrdinalIgnoreCase) >= 0) return "Gaming";
            if (modeName.IndexOf("24/7 Server", StringComparison.OrdinalIgnoreCase) >= 0) return "24/7 LLM";
            if (modeName.IndexOf("Max Cooling", StringComparison.OrdinalIgnoreCase) >= 0) return "MaxCool";
            string clean = modeName.Replace("🔒 ", "").Replace("✨ ", "").Trim();
            return clean.Length > 10 ? clean.Substring(0, 10).TrimEnd() : clean;
        }

        private void ApplyMode(int code)
        {
            string[] names = { "Balanced", "Performance", "Silent", "AI Workload" };
            if (code < 0 || code >= _builtInProfiles.Length) return;

            try
            {
                bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
                if (_controller != null) _controller.SetProfileTarget(_builtInProfiles[code], editSys);

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
                bool ok = _controller != null && _controller.ApplyManualFan(fanId, pct);

                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                SaveSettings();

                if (ok)
                {
                    string fanLabel = fanId == 1 ? "APU Fan" : "Sys Fan";
                    string desc = pct == 0 ? string.Format("{0} → {1}", fanLabel, (fanId == 1 ? _apuProfile : _sysProfile).Name) : string.Format("{0} → {1}%", fanLabel, pct);
                    StartRampIndication(desc, fanId);
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
                bool ok = _controller != null && _controller.ApplyAutoAll();

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

                if (ok)
                {
                    StartRampIndication("Reset both fans → Balanced (Auto)", 0);
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
            if (_trayCb != null) _trayCb.Checked = _closeToTray;
            if (_cpuTrayIcon != null) _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null) _gpuTrayIcon.Visible = _showGpuTray;
            RefreshStatus();
        }

        private bool _resourcesCleanedUp = false;

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isUnitTest && !_forceFullExit && _closeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }
            CleanupManagedResources();
        }

        private void CleanupManagedResources()
        {
            if (_resourcesCleanedUp) return;
            _resourcesCleanedUp = true;

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_sessionEndingHandler != null) { SystemEvents.SessionEnding -= _sessionEndingHandler; _sessionEndingHandler = null; }
            if (_powerModeChangedHandler != null) { SystemEvents.PowerModeChanged -= _powerModeChangedHandler; _powerModeChangedHandler = null; }
            StopWatchdogSupervisorGracefully();
            StopSingleInstanceWakeListener();
            if (_controller != null)
            {
                _controller.Stop(500);
            }
            AppDomain.CurrentDomain.ProcessExit -= OnProcessOrCrashExit;
            AppDomain.CurrentDomain.UnhandledException -= OnProcessOrCrashExit;
            UnregisterGlobalHotkeys();
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
                _refreshTimer = null;
            }
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
                    if (_cpuTrayIcon.Icon != Icon && _cpuTrayIcon.Icon != SystemIcons.Application)
                    {
                        try { _cpuTrayIcon.Icon.Dispose(); } catch { }
                    }
                    _cpuTrayIcon.Icon = null;
                }
                _cpuTrayIcon.Dispose();
                _cpuTrayIcon = null;
            }
            if (_gpuTrayIcon != null)
            {
                _gpuTrayIcon.Visible = false;
                if (_gpuTrayIcon.Icon != null)
                {
                    if (_gpuTrayIcon.Icon != Icon && _gpuTrayIcon.Icon != SystemIcons.Application)
                    {
                        try { _gpuTrayIcon.Icon.Dispose(); } catch { }
                    }
                    _gpuTrayIcon.Icon = null;
                }
                _gpuTrayIcon.Dispose();
                _gpuTrayIcon = null;
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
            if (_driversMenu != null)
            {
                try { _driversMenu.Dispose(); } catch { }
                _driversMenu = null;
            }
            if (_uiToolTip != null)
            {
                try { _uiToolTip.Dispose(); } catch { }
                _uiToolTip = null;
            }
            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }
            _hw.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _forceFullExit = true;
                CleanupManagedResources();
            }
            base.Dispose(disposing);
        }
    }

}
