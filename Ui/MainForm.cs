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
    public partial class MainForm : Form
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

        public const string APP_VERSION         = "1.3.0";
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

        // Xmas Edition Palette (Crisp Snow-White background, Jet Obsidian Black ink & borders, Festive Crimson Red accents)
        private static readonly Color XmasBg        = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color XmasSurface   = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color XmasSurface2  = Color.FromArgb(0xF4, 0xF4, 0xF5);
        private static readonly Color XmasText      = Color.FromArgb(0x09, 0x09, 0x0B);
        private static readonly Color XmasTextDim   = Color.FromArgb(0x3F, 0x3F, 0x46);
        private static readonly Color XmasRed       = Color.FromArgb(0xDC, 0x26, 0x26);
        private static readonly Color XmasRedDeep   = Color.FromArgb(0x99, 0x1B, 0x1B);
        private static readonly Color XmasBlack     = Color.FromArgb(0x18, 0x18, 0x1B);

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
        private bool IsXmas { get { return _themePref == ThemePreference.Xmas; } }
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
        private bool IsReadOnlyMonitoringMode
        {
            get
            {
                return !_demoMode && _hw != null && _hw.Profile != null &&
                       _hw.Profile.IsReadOnly &&
                       (!_hw.Profile.IsVerified || _hw.Profile.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase));
            }
        }
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
        internal Panel TestOsdMetricsScrollPanel { get { return _osdMetricsScrollPanel; } }
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
        // Single source of truth shared with HeadlessDaemon via FanCurveProfile.CreateBuiltInProfiles()
        private readonly FanCurveProfile[] _builtInProfiles = FanCurveProfile.CreateBuiltInProfiles();

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
        private Label _optHeaderLabel;
        private Panel _optCardPanel;
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
        private CheckBox _osdShowLatencyCb;
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
        private readonly AntiCheatMonitor _antiCheatMonitor = new AntiCheatMonitor();
        private readonly WorkloadProfileSwitcher _workloadSwitcher = new WorkloadProfileSwitcher();
        private bool _osdHiddenForAntiCheat = false;
        private bool _servicePausedByGui = false;
        private bool _serviceResumeOnExit = false;

        public MainForm(bool startInDemo, bool startMinimized = false, bool isUnitTest = false)
        {
            _initialDemoMode = startInDemo;
            _startMinimized = startMinimized;
            _isUnitTest = isUnitTest;
            // Seamless GUI <-> Background Service Mode handoff: pause MiniFanControlService
            // while the interactive GUI is open so curves, Auto-Tune & calibration never contend
            // over EC registers, and resume it automatically when the GUI closes.
            if (!isUnitTest && !startInDemo && WindowsServiceHost.IsServiceRunning())
            {
                string svcStopMsg;
                if (WindowsServiceHost.StopService(out svcStopMsg, 4000))
                {
                    _servicePausedByGui = true;
                    _serviceResumeOnExit = true;
                }
            }
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
            _antiCheatMonitor.Enabled = _settings.AutoPauseForAntiCheat;
            _workloadSwitcher.Enabled = _settings.AutoSwitchWorkloads;
            _workloadSwitcher.Rules.Clear();
            _workloadSwitcher.Rules.AddRange(_settings.WorkloadRules);
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
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(860, 640);
            AutoScroll = true;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle wa = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea : new Rectangle(0, 0, 1280, 1024);
            const int idealClientH = 1216; // Fits all OSD Studio metrics without inner scroll & full 648px Live/Perf graphs
            const int minNoScrollH = 760;  // Dynamic vertical layout adapts down to 760px with zero outer scrollbar
            int desiredH = Math.Min(idealClientH, Math.Max(640, wa.Height - 40));
            int extraScrollW = (desiredH < minNoScrollH) ? (SystemInformation.VerticalScrollBarWidth + 4) : 0;
            int desiredW = Math.Min(1100 + extraScrollW, Math.Max(860, wa.Width - 24));
            ClientSize = new Size(desiredW, desiredH);
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
            UpdateReadOnlyUiState();
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
                    if (!_demoMode && !_isSystemSuspended && _antiCheatMonitor.Enabled)
                    {
                        AntiCheatAction acAction = _antiCheatMonitor.Poll();
                        if (acAction == AntiCheatAction.PauseForAntiCheat)
                        {
                            OnAntiCheatPause(_antiCheatMonitor.ActiveAntiCheatName);
                        }
                        else if (acAction == AntiCheatAction.ResumeAfterAntiCheat)
                        {
                            OnAntiCheatResume();
                        }
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
                    if (_servicePausedByGui && _statusBarLabel != null)
                    {
                        _statusBarLabel.Text = "⚙ Background Service paused while GUI is open — close app to hand control back seamlessly";
                    }
                    if (_startMinimized)
                    {
                        MinimizeToTray();
                    }
                    else if (!_isUnitTest && !_demoMode && _settings.ShowDriverInstallPrompt && !NativeInpOut.IsDriverOpen())
                    {
                        BeginInvoke((Action)(() => ShowKernelDriverSecurityDialog(true)));
                    }
                    else if (!_isUnitTest && !IsReadOnlyMonitoringMode && _showSetupAssistant && string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate))
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
            try { EtwLatencyMonitor.Shutdown(); } catch { }
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
            ApplyWin11WindowFrame();
            UpdateGlobalHotkeysRegistration();
            RegisterModernStandbyNotifications();
        }

        private void OnAntiCheatPause(string antiCheatName)
        {
            if (_controller != null) _controller.PauseForSuspend();
            StopWatchdogSupervisorGracefully();
            AntiCheatMonitor.ExecuteSafePauseHandoff(
                delegate { return _hw != null && _hw.SetAuto(true); },
                delegate { NativeInpOut.Shutdown(); },
                delegate(string svc) { return DriverServiceManager.StopDriverService(svc); },
                NativeInpOut.ActiveDriver);

            if (_overlayForm != null && _overlayForm.Visible)
            {
                _overlayForm.Visible = false;
                _osdHiddenForAntiCheat = true;
            }
            if (_errorBanner != null)
            {
                _errorBanner.Text = string.Format("🛡️ Anti-Cheat Safe Pause ({0}) — BIOS Auto restored & legacy kernel driver unloaded until game exits.", antiCheatName);
                _errorBanner.Visible = true;
            }
            if (_statusBarLabel != null)
            {
                _statusBarLabel.Text = string.Format("🛡️ Paused for {0} (BIOS Auto active)", antiCheatName);
            }
        }

        private void OnAntiCheatResume()
        {
            if (_errorBanner != null && _errorBanner.Text != null && _errorBanner.Text.Contains("Anti-Cheat Safe Pause"))
            {
                _errorBanner.Visible = false;
            }
            if (_controller != null) _controller.ResumeFromSuspend(true);
            StartWatchdogSupervisor();
            if (_osdHiddenForAntiCheat && _overlayForm != null && _showOverlay)
            {
                _overlayForm.Visible = true;
            }
            _osdHiddenForAntiCheat = false;
            if (_statusBarLabel != null)
            {
                _statusBarLabel.Text = "✓ Anti-cheat exited — hardware fan control resumed";
            }
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
            if (_controller != null) _controller.ClearStaleWriteErrors();
            UpdateReadOnlyUiState();
            SaveSettings();
            RebuildDriversMenu();
            _statusBarLabel.Text = string.Format("✓ Profile: {0}", _sysInfo.MatchedProfile.DisplayName);
            RefreshStatus();
        }

        private void UpdateReadOnlyUiState()
        {
            bool readOnly = IsReadOnlyMonitoringMode;
            if (readOnly && _controller != null)
            {
                _controller.ClearStaleWriteErrors();
            }
            if (readOnly && _errorBanner != null && _errorBanner.Visible)
            {
                _errorBanner.Visible = false;
            }
            if (_hwSubLabel != null && _sysInfo != null && _sysInfo.MatchedProfile != null)
            {
                string drvInfo = NativeInpOut.ActiveDriverName;
                if (readOnly)
                {
                    _hwSubLabel.Text = string.Format("Safe Read-Only Monitoring (Fan writes disabled) • Driver: {0}", drvInfo);
                }
                else
                {
                    _hwSubLabel.Text = string.Format("EC Map: {0} • Driver: {1} • Global\\Access_EC Mutex • 64-bit", _sysInfo.MatchedProfile.DisplayName, drvInfo);
                }
            }

            RefreshPowerBarCapabilityUi();
            UpdateCalibrationBadge();

            if (_fan1Slider != null) _fan1Slider.Enabled = !readOnly;
            if (_fan1AutoBtn != null) _fan1AutoBtn.Enabled = !readOnly;
            if (_fan2Slider != null) _fan2Slider.Enabled = !readOnly && _hasDualFans;
            if (_fan2AutoBtn != null) _fan2AutoBtn.Enabled = !readOnly && _hasDualFans;
            if (_allAutoBtn != null) _allAutoBtn.Enabled = !readOnly;
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
            RebuildDriversMenu();
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
            if (!_isUnitTest && (_servicePausedByGui || _serviceResumeOnExit || WindowsServiceHost.IsServiceInstalled()))
            {
                AppSettings.SyncConfigToServicePath(AppSettings.GetSettingsPath());
            }
            RebuildTrayContextMenus();
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
                FanCurveProfile curApu = _apuProfile;
                FanCurveProfile curSys = _sysProfile;
                string wsMsg;
                WorkloadSwitchAction wsAction = AutomationCoordinator.EvaluateAndApplyWorkloadSwitch(
                    _workloadSwitcher,
                    _controller,
                    st,
                    IsReadOnlyMonitoringMode || _isSystemSuspended,
                    _antiCheatMonitor.IsPaused,
                    FindProfileByName,
                    ref curApu,
                    ref curSys,
                    out wsMsg);
                if (wsAction != WorkloadSwitchAction.None)
                {
                    _apuProfile = curApu;
                    _sysProfile = curSys;
                    if (!string.IsNullOrEmpty(wsMsg) && _statusBarLabel != null)
                        _statusBarLabel.Text = wsMsg;
                }
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

            string gpuLbl = st.GpuShortLabel;
            if (_cpuTrayIcon != null)
            {
                string tipUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
                string tip = string.Format("CPU: {0}{4} ({5}: {1}{4})\nAPU: {2} RPM | Sys: {3} RPM",
                    TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                    st.Fan1Rpm, st.Fan2Rpm, tipUnit, gpuLbl);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_cpuTrayIcon.Text != tip) _cpuTrayIcon.Text = tip;
            }

            if (_gpuTrayIcon != null)
            {
                string tipUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
                string tip = string.Format("{5}: {0}{4} (CPU: {1}{4})\nAPU: {2} RPM | Sys: {3} RPM",
                    TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                    TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                    st.Fan1Rpm, st.Fan2Rpm, tipUnit, gpuLbl);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_gpuTrayIcon.Text != tip) _gpuTrayIcon.Text = tip;
            }

            string apuModeName = _apuManualOverride ? "Man" : (_apuProfile != null ? _apuProfile.Name : st.Mode);
            string sysModeName = _sysManualOverride ? "Man" : (_sysProfile != null ? _sysProfile.Name : st.Mode);

            if (_overlayForm != null && _showOverlay)
            {
                _overlayForm.IsReadOnlyMode = IsReadOnlyMonitoringMode;
                string osdMode = IsReadOnlyMonitoringMode
                    ? "Read-Only"
                    : ((!_hasDualFans || apuModeName == sysModeName)
                        ? ShortModeTag(apuModeName)
                        : string.Format("{0}/{1}", ShortModeTag(apuModeName), ShortModeTag(sysModeName)));
                _overlayForm.UpdateMetrics(st, osdMode, _hasDualFans);
            }

            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            string tUnit = TelemetrySessionStats.TempUnit(_useFahrenheit);
            string winTitle = string.Format("CPU {0}{2} | {3} {1}{2} - MiniFanControl",
                TelemetrySessionStats.ToDisplayTemp(st.CpuTemp, _useFahrenheit),
                TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit),
                tUnit, gpuLbl);
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
            if (!_demoMode && !NativeInpOut.IsDriverOpen() &&
                _errorBanner != null && string.IsNullOrEmpty(_uiWriteErrorBannerDetail))
            {
                _errorBanner.Text = "⚠ No active kernel driver (Windows 11 HVCI may have blocked InpOut64) — click here to install HVCI-safe PawnIO (pawnio.eu) or inspect Kernel Driver Status.";
                _errorBanner.Cursor = Cursors.Hand;
                if (!_errorBanner.Visible) _errorBanner.Visible = true;
            }
            else if (!_demoMode && NativeInpOut.IsDriverOpen() && EcBusMutex.UsesLocalNamespaceFallback &&
                _errorBanner != null && string.IsNullOrEmpty(_uiWriteErrorBannerDetail))
            {
                _errorBanner.Text = "⚠ Cross-session Global\\Access_EC mutex unavailable (Local\\ fallback active) — HWiNFO64/CapFrameX bus coordination is degraded. Run as Administrator for full synchronization.";
                _errorBanner.Cursor = Cursors.Default;
                if (!_errorBanner.Visible) _errorBanner.Visible = true;
            }
            else if (!_demoMode && NativeInpOut.IsDriverOpen() && !EcBusMutex.UsesLocalNamespaceFallback &&
                _errorBanner != null && _errorBanner.Visible &&
                _errorBanner.Text != null && (_errorBanner.Text.Contains("pawnio.eu") || _errorBanner.Text.Contains("Local\\ fallback")))
            {
                _errorBanner.Visible = false;
                _errorBanner.Cursor = Cursors.Default;
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
                {
                    _perfCanvas.Invalidate();
                    if (_showingPerfGraph) UpdateCurveHint();
                }
            }
            if (_overlayForm != null)
            {
                _overlayForm.SampleIntervalMs = _refreshIntervalMs;
            }

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = (_themePref == ThemePreference.Xmas);
            Color tempFg = isXmas ? XmasText : (_isDarkMode ? DarkText : LightText);
            Color dimText = isXmas ? XmasTextDim : (_isDarkMode ? Color.FromArgb(0x93, 0x99, 0xB2) : Color.FromArgb(0x6C, 0x70, 0x86));
            Color rampColor = isXmas ? XmasRed : (_isDarkMode ? Color.FromArgb(0xFA, 0xB3, 0x87) : Color.FromArgb(0xEA, 0x58, 0x0C));

            Color autoColor = isXmas ? XmasRed : (_isDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x15, 0x80, 0x3D));
            Color manColor = isXmas ? XmasBlack : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (_isDarkMode ? DarkAccent : LightAccent));
            Color warnRose = isXmas ? XmasRedDeep : Color.FromArgb(0xFB, 0x71, 0x85);

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

            // 2. GPU / iGPU Box (Row 1, Col 1)
            string gpuValTxt = st.GpuTemp > 0 ? string.Format("{0}{1}", TelemetrySessionStats.ToDisplayTemp(st.GpuTemp, _useFahrenheit), tUnit) : "N/A";
            if (_gpuTempLabel.Text != gpuValTxt) _gpuTempLabel.Text = gpuValTxt;
            if (_gpuTempLabel.ForeColor != tempFg) _gpuTempLabel.ForeColor = tempFg;
            if (_gpuBoxTitle != null && _gpuBoxTitle.Text != gpuLbl) _gpuBoxTitle.Text = gpuLbl;
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
                ? string.Format(" • HS {0}°", TelemetrySessionStats.ToDisplayTemp(st.GpuHotspotTemp, _useFahrenheit))
                : (st.GfxPowerWatts > 0 ? string.Format(" • GFX ~{0}W", st.GfxPowerWatts) : "");
            string gpuDetail1 = gpuLoadRange + gpuExtraPart;
            string gpuDrvPart = !string.IsNullOrEmpty(st.RadeonSoftwareVer) ? ("Adrenalin " + st.RadeonSoftwareVer) : (!string.IsNullOrEmpty(st.GpuName) ? st.GpuName : "Radeon 780M");
            string gpuFeatPart = !string.IsNullOrEmpty(st.RadeonFeaturesSummary) ? (" • " + st.RadeonFeaturesSummary) : "";
            SetMetricDetail(2, gpuPeakTxt, gpuSubTxt, gpuDetail1, gpuDrvPart + gpuFeatPart, Color.Empty);
            if (_gpuTempBox != null)
            {
                _gpuTempBox.AccessibleName = string.Format("{0} Temperature: {1}", gpuLbl, gpuValTxt);
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
                        : (st.IsApuPowerEstimated ? "Estimated TDP Envelope" : ((st.IsDiscreteGpu || IsReadOnlyMonitoringMode) ? "Package / Board Rail" : "APU Package Rail")));
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

            bool readOnlyMonitor = IsReadOnlyMonitoringMode;

            if (readOnlyMonitor)
                _modeLabel.Text = "Read-Only Monitoring (Motherboard Auto)";
            else if (!_hasDualFans || apuModeName == sysModeName)
                _modeLabel.Text = apuModeName;
            else
                _modeLabel.Text = string.Format("{0} / {1}", apuModeName, sysModeName);

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(st.ModeCode);
            int liveBiosCode = (st.ModeCode >= 0 && st.ModeCode <= 2)
                ? st.ModeCode
                : (_controller != null ? _controller.BiosPowerModeOverride : -1);
            UpdateBiosPowerModeStyles(liveBiosCode);

            if (readOnlyMonitor)
            {
                _pct1Label.Text = "Read-Only";
                _pct1Label.ForeColor = dimText;
            }
            else if (_emergencyThermalActive)
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
            else if (readOnlyMonitor)
            {
                _pct2Label.Text = "Read-Only";
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
            string f1ValTxt = (readOnlyMonitor && st.Fan1Rpm == 0) ? "-- RPM" : string.Format("{0} RPM", st.Fan1Rpm);
            if (_fan1RpmLabel.Text != f1ValTxt) _fan1RpmLabel.Text = f1ValTxt;
            if (_fan1RpmLabel.ForeColor != tempFg) _fan1RpmLabel.ForeColor = tempFg;
            if (_fan1Box != null)
            {
                if (_fan1BoxTitle != null && _fan1BoxTitle.Text != "APU FAN") _fan1BoxTitle.Text = "APU FAN";
                string fan1PeakTxt = (_sessionStats.SampleCount > 0 && _sessionStats.MaxFan1Rpm > 0)
                    ? string.Format("▼{0} ~{1:0} ▲{2}", _sessionStats.MinFan1Rpm, _sessionStats.AvgFan1Rpm, _sessionStats.MaxFan1Rpm)
                    : "";
                string fan1Detail1 = readOnlyMonitor
                    ? "APU / CPU Cooler • Motherboard Controlled"
                    : (_emergencyThermalActive
                        ? "APU / CPU Cooler • Emergency Override"
                        : (_apuManualOverride
                            ? "APU / CPU Cooler • Manual Override"
                            : (st.Fan1Manual == 0 ? "APU / CPU Cooler • BIOS Auto" : "APU / CPU Cooler • Active Curve")));
                string fan1Detail2 = readOnlyMonitor ? "Safe Read-Only Mode" : apuModeName;
                SetMetricDetail(4, fan1PeakTxt, _pct1Label.Text, fan1Detail1, fan1Detail2, _pct1Label.ForeColor, readOnlyMonitor ? dimText : tempFg);
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
                    : (readOnlyMonitor
                        ? "System Fan • Motherboard Controlled"
                        : (_emergencyThermalActive
                            ? "RAM / NVMe Cooler • Emergency Override"
                            : (_sysManualOverride
                                ? "RAM / NVMe Cooler • Manual Override"
                                : (st.Fan2Manual == 0 ? "RAM / NVMe Cooler • BIOS Auto" : "RAM / NVMe Cooler • Active Curve"))));
                string fan2Detail2 = !_hasDualFans ? "Disabled" : (readOnlyMonitor ? "Safe Read-Only Mode" : sysModeName);
                SetMetricDetail(5, fan2PeakTxt, _pct2Label.Text, fan2Detail1, fan2Detail2, _pct2Label.ForeColor, (!_hasDualFans || readOnlyMonitor) ? dimText : tempFg);
                _fan2Box.AccessibleName = !_hasDualFans ? "System Fan: Disabled" : string.Format("System Fan Speed: {0} RPM", st.Fan2Rpm);
                _fan2Box.Invalidate();
            }

            if (_emergencyThermalActive)
            {
                _statusBarLabel.ForeColor = Color.FromArgb(0xFB, 0x71, 0x85);
                _statusBarLabel.Text = string.Format("🔥 EMERGENCY THERMAL OVERRIDE (CPU {0}°C) — Fans at 100%", apuTemp);
            }
            else if (ReadOnlyFailsafeLatched)
            {
                _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                _statusBarLabel.Text = string.Format("🛡️ FAILSAFE READ-ONLY (BIOS Auto restored)  |  {0:HH:mm:ss}", DateTime.Now);
            }
            else if (!string.IsNullOrEmpty(_lastHardwareWriteError))
            {
                _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
                _statusBarLabel.Text = string.Format("⚠️ Hardware write rejected ({0} retries)  |  {1:HH:mm:ss}", _consecutiveWriteFailures, DateTime.Now);
            }
            else if (readOnlyMonitor)
            {
                _statusBarLabel.ForeColor = dimText;
                _statusBarLabel.Text = string.Format("👁️ SAFE READ-ONLY MONITORING (Unverified Hardware — Fan writes disabled)  |  {0:HH:mm:ss}", DateTime.Now);
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
            if (IsReadOnlyMonitoringMode)
            {
                if (_statusBarLabel != null)
                    _statusBarLabel.Text = "👁️ Fan writes are disabled in Safe Read-Only Monitoring Mode (unverified hardware)";
                return;
            }

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

        private void ApplyFan(int fanId, int pct, bool allowBelowFloor = false)
        {
            if (IsReadOnlyMonitoringMode)
            {
                if (_statusBarLabel != null)
                    _statusBarLabel.Text = "👁️ Fan writes are disabled in Safe Read-Only Monitoring Mode (unverified hardware)";
                return;
            }
            try
            {
                int appliedPct = pct;
                bool wasClamped = false;
                int minFloor = FanCurveProfile.MIN_SPIN_PCT;
                bool ok = _controller != null && _controller.ApplyManualFan(fanId, pct, allowBelowFloor, out appliedPct, out wasClamped, out minFloor);

                // Keep manual slider UI synchronized when anti-stall clamping elevates sub-floor requests
                if (wasClamped)
                {
                    if (fanId == 1 && _fan1Slider != null)
                    {
                        _fan1Slider.Value = Math.Max(_fan1Slider.Minimum, Math.Min(_fan1Slider.Maximum, appliedPct));
                        if (_fan1SliderPct != null) _fan1SliderPct.Text = string.Format("{0}%", appliedPct);
                    }
                    else if (fanId == 2 && _fan2Slider != null)
                    {
                        _fan2Slider.Value = Math.Max(_fan2Slider.Minimum, Math.Min(_fan2Slider.Maximum, appliedPct));
                        if (_fan2SliderPct != null) _fan2SliderPct.Text = string.Format("{0}%", appliedPct);
                    }
                }

                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                SaveSettings();

                if (ok)
                {
                    string fanLabel = fanId == 1 ? "APU Fan" : "Sys Fan";
                    string desc;
                    if (appliedPct == 0)
                        desc = string.Format("{0} → {1}", fanLabel, (fanId == 1 ? _apuProfile : _sysProfile).Name);
                    else if (wasClamped)
                        desc = string.Format("{0} → {1}% (clamped from {2}% to min spin floor)", fanLabel, appliedPct, pct);
                    else
                        desc = string.Format("{0} → {1}%", fanLabel, appliedPct);
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
            if (IsReadOnlyMonitoringMode)
            {
                if (_statusBarLabel != null)
                    _statusBarLabel.Text = "👁️ Safe Read-Only Monitoring Mode — motherboard BIOS already controls fans";
                return;
            }
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
            HeadlessDaemon.TrimProcessWorkingSet();
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
            bool handoffToService = (!_isUnitTest && !_demoMode && (_servicePausedByGui || _serviceResumeOnExit) && WindowsServiceHost.IsServiceInstalled());
            if (handoffToService)
            {
                try { SaveSettings(); } catch { }
            }
            else
            {
                RestoreHardwareSafeAutoIfNeeded();
            }
            try { EtwLatencyMonitor.Shutdown(); } catch { }
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
            if (handoffToService)
            {
                try
                {
                    string svcStartMsg;
                    WindowsServiceHost.StartService(out svcStartMsg, 4000);
                }
                catch { }
            }
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
