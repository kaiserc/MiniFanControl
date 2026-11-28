using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Self-contained OSD Overlay Studio panel extracted from MainForm.
    /// Owns all OSD Studio controls, 1-click presets, metric category filters, opacity slider,
    /// quick-snap buttons, and theme styling behind a clean OsdOverlayConfig + event boundary.
    /// </summary>
    public sealed class OsdStudioControl : Panel
    {
        private static readonly Color RadeonOrange = Color.FromArgb(0xF9, 0x73, 0x16);
        private static readonly Font PillFontBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font PillFontRegular = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        private readonly OsdOverlayConfig _osd;
        private readonly Func<ThemePalette> _paletteProvider;
        private readonly ToolTip _toolTip;
        private readonly Func<bool> _hasDualFansProvider;

        private bool _suppressEvents = false;
        private int _activeCategory = 0; // 0=All, 1=CPU&Power, 2=iGPU&RAM, 3=Storage&Sys, 4=FPS&Graphs
        private readonly List<Label> _sectionHeaders = new List<Label>();
        private readonly List<Tuple<Control, int, int>> _catControls = new List<Tuple<Control, int, int>>();

        private Button _btnScale100, _btnScale125, _btnScale150, _btnScale185;
        private Button _btnBothGraphs, _btnCombinedGraph, _btnSparklines, _btnTextOnly, _btnGraphsOnly;
        private Button _btnHeightCompact, _btnHeightMedium, _btnHeightLarge, _btnHeightXl;
        private Button _btnLayoutH, _btnLayoutV;
        private ModernSlider _opacitySlider;
        private Label _opacityPctLabel;
        private Label _titleLabel, _graphModeLabel, _graphHeightLabel, _scaleLabel, _layoutLabel;
        private Label _opacityLabel, _snapLabel, _metricsHeaderLabel, _subFieldsHeaderLabel, _tipLabel;
        private Panel _metricsScrollPanel;
        private ComboBox _trackerModeCombo;
        private Button _selectAllBtn, _deselectAllBtn, _resetDefaultsBtn;
        private Button _quickPosTl, _quickPosTc, _quickPosTr, _quickPosBl, _quickPosBc, _quickPosBr;
        private Button _toggleOverlayBtn, _toggleLockBtn;
        private Button _presetMinimalBtn, _presetCompactBtn, _presetDfBtn, _presetFullBtn;
        private Button[] _catButtons;

        private CheckBox _matchThemeCb, _showCpuCb, _showGpuCb, _showNvmeCb, _showPowerCb, _showRamCb;
        private CheckBox _showVrmCurrentCb, _showAmbientCb, _showFabricClockCb, _showNpuCb;
        private CheckBox _showFpsCb, _showLowsCb, _showGpuLoadCb, _showApuFanCb, _showSysFanCb;
        private CheckBox _showModeCb, _showPeaksCb, _showRadeonCb, _showDriverVerCb, _showClocksCb;
        private CheckBox _showGameNameCb, _sparklineBgCb, _showLiveTrendCb, _showFpsGraphCb;
        private CheckBox _plotFpsOnTrendCb, _dfGraphStyleCb, _dfCornerInsetCb, _dfGraphFirstCb;
        private CheckBox _showRamTempCb, _showRamDualSticksCb, _showRamSpreadCb, _showRamLoadCb;
        private CheckBox _showRamPowerCb, _showRamClockCb, _showNvmePowerCb, _showNvmeIoCb, _showNvmeDualCb;
        private CheckBox _showCpuLoadCb, _showGpuHotspotCb, _showPowerLimitCb, _showFclkSyncCb;
        private CheckBox _showFrametimeMsCb, _showFpsAvgCb, _showLatencyCb, _showRadeonExtTagsCb;

        // Internal accessors preserved for layout anti-clipping self-tests
        internal Label OpacityLabel { get { return _opacityLabel; } }
        internal Label SnapLabel { get { return _snapLabel; } }
        internal Label MetricsHeaderLabel { get { return _metricsHeaderLabel; } }
        internal Button QuickPosBc { get { return _quickPosBc; } }
        internal Button QuickPosBr { get { return _quickPosBr; } }
        internal Panel MetricsScrollPanel { get { return _metricsScrollPanel; } }

        public event EventHandler ConfigChanged;
        public event EventHandler<int> OpacityPreviewChanged;
        public event EventHandler<int> OpacityCommitted;
        public event EventHandler<int> SnapCornerRequested;
        public event EventHandler ToggleOverlayRequested;
        public event EventHandler ToggleLockRequested;
        public event EventHandler<string> StatusMessageRequested;

        public OsdStudioControl(
            OsdOverlayConfig config,
            Func<ThemePalette> paletteProvider,
            ToolTip toolTip,
            Func<bool> hasDualFansProvider,
            bool initialShowOverlay,
            bool initialLockOverlay)
        {
            _osd = config ?? new OsdOverlayConfig();
            _paletteProvider = paletteProvider;
            _toolTip = toolTip;
            _hasDualFansProvider = hasDualFansProvider;

            BuildControls(initialShowOverlay, initialLockOverlay);
        }

        private ThemePalette ResolvePalette()
        {
            return _paletteProvider != null ? _paletteProvider() : ThemePalette.Resolve(ThemePreference.Dark, true);
        }

        private bool HasDualFans()
        {
            return _hasDualFansProvider == null || _hasDualFansProvider();
        }

        private void SetTip(Control c, string tip)
        {
            if (_toolTip != null && c != null && !string.IsNullOrEmpty(tip))
                _toolTip.SetToolTip(c, tip);
        }

        private void RaiseConfigChanged()
        {
            EventHandler h = ConfigChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void UpdateScrollPanelHeight(int studioHeight)
        {
            if (_metricsScrollPanel != null)
            {
                _metricsScrollPanel.Size = new Size(1048, Math.Max(140, studioHeight - 198));
            }
        }

        private void BuildControls(bool showOverlay, bool lockOverlay)
        {
            // Row 1 (y=5): Title + 1-Click Presets + Quick Toggle Buttons
            _titleLabel = new Label
            {
                Text = "OSD OVERLAY STUDIO",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(164, 22),
                UseMnemonic = false
            };
            Controls.Add(_titleLabel);

            Font presetFont = new Font("Segoe UI", 7.75f, FontStyle.Bold);
            _presetMinimalBtn = new Button
            {
                Text = "Minimal FPS",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(178, 5),
                Size = new Size(104, 26),
                Cursor = Cursors.Hand
            };
            _presetMinimalBtn.Click += (s, e) => ApplyPreset("Minimal FPS", () => _osd.ApplyPresetMinimalFps());
            SetTip(_presetMinimalBtn, "Preset: Clean horizontal FPS, 0.1% Lows, CPU & iGPU temps bar (Text Only)");
            Controls.Add(_presetMinimalBtn);

            _presetCompactBtn = new Button
            {
                Text = "Compact",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(288, 5),
                Size = new Size(92, 26),
                Cursor = Cursors.Hand
            };
            _presetCompactBtn.Click += (s, e) => ApplyPreset("Compact Gaming", () => _osd.ApplyPresetCompactGaming(HasDualFans()));
            SetTip(_presetCompactBtn, "Preset: Compact vertical gaming HUD with sparklines and DF corner-inset frametime graph");
            Controls.Add(_presetCompactBtn);

            _presetDfBtn = new Button
            {
                Text = "Digital Foundry",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(386, 5),
                Size = new Size(122, 26),
                Cursor = Cursors.Hand
            };
            _presetDfBtn.Click += (s, e) => ApplyPreset("Digital Foundry", () => _osd.ApplyPresetDigitalFoundry());
            SetTip(_presetDfBtn, "Preset: Digital Foundry twin Frame-Time + Frame-Rate benchmark overlay with core telemetry");
            Controls.Add(_presetDfBtn);

            _presetFullBtn = new Button
            {
                Text = "Full Audit",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(514, 5),
                Size = new Size(94, 26),
                Cursor = Cursors.Hand
            };
            _presetFullBtn.Click += (s, e) => ApplyPreset("Full Audit", () => _osd.ApplyPresetFullAudit(HasDualFans()));
            SetTip(_presetFullBtn, "Preset: Full hardware benchmark audit (all sensors, VRM currents, dual-stick DDR5, and both live graphs)");
            Controls.Add(_presetFullBtn);

            _toggleOverlayBtn = new Button
            {
                Text = showOverlay ? "● OSD Active" : "○ OSD Hidden",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(746, 5),
                Size = new Size(140, 26),
                Cursor = Cursors.Hand
            };
            _toggleOverlayBtn.FlatAppearance.BorderSize = 0;
            _toggleOverlayBtn.Click += (s, e) => { EventHandler h = ToggleOverlayRequested; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(_toggleOverlayBtn);

            _toggleLockBtn = new Button
            {
                Text = lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(894, 5),
                Size = new Size(154, 26),
                Cursor = Cursors.Hand
            };
            _toggleLockBtn.FlatAppearance.BorderSize = 0;
            _toggleLockBtn.Click += (s, e) => { EventHandler h = ToggleLockRequested; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(_toggleLockBtn);

            // Row 2 (y=38): Live Graph Display Mode (Left) & Combined Graph Height (Right)
            _graphModeLabel = new Label
            {
                Text = "Live Graph Display Mode:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 38),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            Controls.Add(_graphModeLabel);

            _graphHeightLabel = new Label
            {
                Text = "Combined Live Graph Height:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(492, 38),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            Controls.Add(_graphHeightLabel);

            _btnBothGraphs = CreateFlatButton("Both (All)", new Point(8, 56), new Size(88, 26), new Font("Segoe UI", 8.0f, FontStyle.Bold),
                () => { _osd.GraphMode = 3; _osd.ShowLiveTrend = true; RaiseConfigChanged(); },
                "Show both mini sparklines per metric and combined live trend graph");
            _btnCombinedGraph = CreateFlatButton("Combined", new Point(100, 56), new Size(88, 26), new Font("Segoe UI", 8.0f, FontStyle.Bold),
                () => { _osd.GraphMode = 2; _osd.ShowLiveTrend = true; RaiseConfigChanged(); },
                "Show combined multi-series live trend graph only");
            _btnSparklines = CreateFlatButton("Sparklines", new Point(192, 56), new Size(88, 26), new Font("Segoe UI", 8.0f, FontStyle.Bold),
                () => { _osd.GraphMode = 1; _osd.ShowLiveTrend = false; RaiseConfigChanged(); },
                "Show mini inline sparklines per metric only");
            _btnTextOnly = CreateFlatButton("Text Only", new Point(284, 56), new Size(86, 26), new Font("Segoe UI", 8.0f, FontStyle.Bold),
                () => { _osd.GraphMode = 0; _osd.ShowLiveTrend = false; _osd.ShowFpsGraph = false; RaiseConfigChanged(); },
                "Display metrics as text only without live graphs");
            _btnGraphsOnly = CreateFlatButton("Graphs Only", new Point(374, 56), new Size(108, 26), new Font("Segoe UI", 8.0f, FontStyle.Bold),
                () =>
                {
                    _osd.GraphMode = 4;
                    if (!_osd.ShowFpsGraph && !_osd.ShowLiveTrend) _osd.ShowFpsGraph = true;
                    RaiseConfigChanged();
                },
                "Hide all metric text rows and display ONLY the enabled graphs (toggle FPS & Frametime Graph and/or Live Trend Graph below).");

            Font regBtnFont = new Font("Segoe UI", 8.25f, FontStyle.Regular);
            _btnHeightCompact = CreateFlatButton("75px Compact", new Point(492, 56), new Size(134, 26), regBtnFont,
                () => { _osd.GraphHeight = 75; RaiseConfigChanged(); }, null);
            _btnHeightMedium = CreateFlatButton("105px Medium", new Point(632, 56), new Size(134, 26), regBtnFont,
                () => { _osd.GraphHeight = 105; RaiseConfigChanged(); }, null);
            _btnHeightLarge = CreateFlatButton("140px Large", new Point(772, 56), new Size(134, 26), regBtnFont,
                () => { _osd.GraphHeight = 140; RaiseConfigChanged(); }, null);
            _btnHeightXl = CreateFlatButton("180px XL", new Point(912, 56), new Size(136, 26), regBtnFont,
                () => { _osd.GraphHeight = 180; RaiseConfigChanged(); }, null);

            // Row 3 (y=88): Scale Tier (Left) & Layout Direction (Right)
            _scaleLabel = new Label
            {
                Text = "Overlay Scale Tier:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 88),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            Controls.Add(_scaleLabel);

            _layoutLabel = new Label
            {
                Text = "Layout Direction:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(492, 88),
                Size = new Size(300, 16),
                UseMnemonic = false
            };
            Controls.Add(_layoutLabel);

            _btnScale100 = CreateFlatButton("100% Std", new Point(8, 106), new Size(115, 26), regBtnFont,
                () => { _osd.ScaleTier = 0; RaiseConfigChanged(); }, null);
            _btnScale125 = CreateFlatButton("125% Lg", new Point(127, 106), new Size(115, 26), regBtnFont,
                () => { _osd.ScaleTier = 1; RaiseConfigChanged(); }, null);
            _btnScale150 = CreateFlatButton("150% XL", new Point(246, 106), new Size(115, 26), regBtnFont,
                () => { _osd.ScaleTier = 2; RaiseConfigChanged(); }, null);
            _btnScale185 = CreateFlatButton("185% 4K", new Point(365, 106), new Size(117, 26), regBtnFont,
                () => { _osd.ScaleTier = 3; RaiseConfigChanged(); }, null);

            Font boldLayoutFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            _btnLayoutH = CreateFlatButton("▬ Horizontal Bar", new Point(492, 106), new Size(275, 26), boldLayoutFont,
                () => { _osd.VerticalLayout = false; RaiseConfigChanged(); }, null);
            _btnLayoutV = CreateFlatButton("▮ Vertical Stack", new Point(773, 106), new Size(275, 26), boldLayoutFont,
                () => { _osd.VerticalLayout = true; RaiseConfigChanged(); }, null);

            // Row 4 (y=136): Opacity Slider & Quick Snap Corner Buttons
            _opacityLabel = new Label
            {
                Text = "Opacity:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(8, 140),
                Size = new Size(62, 20),
                UseMnemonic = false
            };
            Controls.Add(_opacityLabel);

            _opacitySlider = new ModernSlider
            {
                Minimum = 35,
                Maximum = 100,
                Value = Math.Max(35, Math.Min(100, _osd.OpacityPct)),
                ZeroLabel = null,
                UnitSuffix = "%",
                PaletteProvider = () => ResolvePalette(),
                Location = new Point(72, 134),
                Size = new Size(130, 30),
                AccessibleName = "OSD Overlay Opacity Percentage"
            };
            _opacitySlider.Scroll += (s, e) =>
            {
                if (_suppressEvents) return;
                _osd.OpacityPct = _opacitySlider.Value;
                _opacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);
                EventHandler<int> h = OpacityPreviewChanged;
                if (h != null) h(this, _osd.OpacityPct);
            };
            _opacitySlider.ValueCommitted += (s, e) =>
            {
                if (_suppressEvents) return;
                _osd.OpacityPct = _opacitySlider.Value;
                _opacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);
                EventHandler<int> h = OpacityCommitted;
                if (h != null) h(this, _osd.OpacityPct);
            };
            Controls.Add(_opacitySlider);

            _opacityPctLabel = new Label
            {
                Text = string.Format("{0}%", _osd.OpacityPct),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(206, 138),
                Size = new Size(42, 20),
                UseMnemonic = false
            };
            Controls.Add(_opacityPctLabel);

            _snapLabel = new Label
            {
                Text = "Quick Snap:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(244, 138),
                Size = new Size(88, 20),
                UseMnemonic = false
            };
            Controls.Add(_snapLabel);

            Font snapBtnFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);
            _quickPosTl = CreateFlatButton("Top-Left", new Point(334, 136), new Size(112, 26), snapBtnFont, () => RaiseSnap(0), "Snap OSD to Top-Left screen corner");
            _quickPosTc = CreateFlatButton("Center-Top", new Point(450, 136), new Size(116, 26), snapBtnFont, () => RaiseSnap(4), "Snap OSD to Center-Top screen edge");
            _quickPosTr = CreateFlatButton("Top-Right", new Point(570, 136), new Size(112, 26), snapBtnFont, () => RaiseSnap(1), "Snap OSD to Top-Right screen corner");
            _quickPosBl = CreateFlatButton("Bottom-Left", new Point(686, 136), new Size(116, 26), snapBtnFont, () => RaiseSnap(2), "Snap OSD to Bottom-Left screen corner");
            _quickPosBc = CreateFlatButton("Center-Bottom", new Point(806, 136), new Size(122, 26), snapBtnFont, () => RaiseSnap(5), "Snap OSD to Center-Bottom screen edge");
            _quickPosBr = CreateFlatButton("Bottom-Right", new Point(932, 136), new Size(116, 26), snapBtnFont, () => RaiseSnap(3), "Snap OSD to Bottom-Right screen corner");

            // Row 5 (y=170..195): Metrics Category Sub-Tabs + Select All / Deselect All / Defaults
            _metricsHeaderLabel = new Label
            {
                Text = "Metrics:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(8, 174),
                Size = new Size(64, 20),
                UseMnemonic = false
            };
            Controls.Add(_metricsHeaderLabel);

            string[] catTitles = new[] { "All", "CPU & Power", "iGPU & RAM", "Storage & Fans", "FPS & Graphs" };
            int[] catWidths = new[] { 58, 114, 108, 126, 114 };
            _catButtons = new Button[catTitles.Length];
            Font catFont = new Font("Segoe UI", 7.75f, FontStyle.Bold);
            int catX = 76;
            for (int ci = 0; ci < catTitles.Length; ci++)
            {
                int catIdx = ci;
                Button cb = new Button
                {
                    Text = catTitles[ci],
                    FlatStyle = FlatStyle.Flat,
                    Font = catFont,
                    Location = new Point(catX, 170),
                    Size = new Size(catWidths[ci], 25),
                    Cursor = Cursors.Hand,
                    UseMnemonic = false
                };
                cb.Click += (s, e) => ApplyCategoryFilter(catIdx);
                Controls.Add(cb);
                _catButtons[ci] = cb;
                catX += catWidths[ci] + 5;
            }

            _selectAllBtn = CreateFlatButton("Select All", new Point(712, 170), new Size(104, 25), catFont,
                () => { _osd.SelectAllMetrics(HasDualFans()); RaiseConfigChanged(); }, null);
            _deselectAllBtn = CreateFlatButton("Deselect All", new Point(824, 170), new Size(108, 25), catFont,
                () => { _osd.DeselectAllMetrics(); RaiseConfigChanged(); }, null);
            _resetDefaultsBtn = CreateFlatButton("Defaults", new Point(940, 170), new Size(108, 25), catFont,
                () => { _osd.ResetDefaults(HasDualFans()); RaiseConfigChanged(); }, null);

            // Row 6+ (y=198..408): Per-Metric Configuration Sections
            _metricsScrollPanel = new Panel
            {
                Location = new Point(4, 198),
                Size = new Size(1048, 210),
                AutoScroll = true
            };
            Controls.Add(_metricsScrollPanel);
            _sectionHeaders.Clear();
            _catControls.Clear();
            _activeCategory = 0;

            BuildMetricCheckboxes();
        }

        private void BuildMetricCheckboxes()
        {
            bool dualFans = HasDualFans();
            int[] colX = new[] { 8, 260, 512, 764 };
            const int colW = 246;
            const int cbH = 22;
            const int rowStep = 21;
            int curY = 0;

            // Category 1: CPU (left) & Power & VRM (right)
            int pairY = curY;
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "CPU", ref curY, 8, 490), 1);
            _showCpuCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 8, curY, 242, cbH, "CPU Temp", _osd.ShowCpu, v => { _osd.ShowCpu = v; RaiseConfigChanged(); }, null), 1);
            _showCpuLoadCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 258, curY, 242, cbH, "CPU Load (%)", _osd.ShowCpuLoad, v => { _osd.ShowCpuLoad = v; RaiseConfigChanged(); },
                "Show CPU utilization percentage on the CPU overlay row"), 1);
            _showClocksCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 8, curY + rowStep, 242, cbH, "Clock Speeds (@ GHz/MHz)", _osd.ShowClocks, v => { _osd.ShowClocks = v; RaiseConfigChanged(); },
                "Display effective CPU, iGPU, and RAM clock frequencies (@ 4.1G / @ 800MHz). Uncheck to maximize space for sparklines."), 1);

            int rightY = pairY;
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "Power & VRM", ref rightY, 518, 490), 1);
            _showPowerCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 518, rightY, 242, cbH, "APU Power (W)", _osd.ShowPower, v => { _osd.ShowPower = v; RaiseConfigChanged(); }, null), 1);
            _showPowerLimitCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 768, rightY, 242, cbH, "APU Power Limit (/W)", _osd.ShowPowerLimit, v => { _osd.ShowPowerLimit = v; RaiseConfigChanged(); },
                "Show active PPT/STAPM power limit (/65W) next to APU Power"), 1);
            _showVrmCurrentCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 518, rightY + rowStep, 242, cbH, "VRM Current (TDC/EDC)", _osd.ShowVrmCurrent, v => { _osd.ShowVrmCurrent = v; RaiseConfigChanged(); },
                "Display deep silicon VRM amperage draw (continuous TDC and peak EDC amps for VDD & SOC rails)"), 1);
            curY += rowStep * 2;

            // Category 2: iGPU & Radeon + RAM (DDR5)
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "iGPU & Radeon", ref curY), 2);
            _showGpuCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY, colW, cbH, "iGPU Temp", _osd.ShowGpu, v => { _osd.ShowGpu = v; RaiseConfigChanged(); }, null), 2);
            _showGpuHotspotCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY, colW, cbH, "GPU Hotspot (HS °C)", _osd.ShowGpuHotspot, v => { _osd.ShowGpuHotspot = v; RaiseConfigChanged(); },
                "Show GPU junction hotspot temperature (HS °C) when available"), 2);
            _showGpuLoadCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY, colW, cbH, "GPU Load & Clock", _osd.ShowGpuLoad, v => { _osd.ShowGpuLoad = v; RaiseConfigChanged(); }, null), 2);
            _showRadeonCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY, colW, cbH, "Radeon Feature Tags", _osd.ShowRadeon, v => { _osd.ShowRadeon = v; RaiseConfigChanged(); },
                "Display compact pill tags for active AMD Radeon features (FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)"), 2);
            _showDriverVerCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Radeon Driver Version", _osd.ShowDriverVer, v => { _osd.ShowDriverVer = v; RaiseConfigChanged(); },
                "Show the installed AMD Radeon Software (Adrenalin) driver version on the OSD"), 2);
            _showRadeonExtTagsCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "SAM, EnhSync & FRTC Tags", _osd.ShowRadeonExtTags, v => { _osd.ShowRadeonExtTags = v; RaiseConfigChanged(); },
                "Include Smart Access Memory (SAM), Enhanced Sync, and FRTC cap pills in the OSD Radeon tags row"), 2);
            curY += rowStep * 2;

            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "RAM (DDR5)", ref curY), 2);
            _showRamCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY, colW, cbH, "RAM Load & Power", _osd.ShowRam, v => { _osd.ShowRam = v; RaiseConfigChanged(); }, null), 2);
            _showRamTempCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY, colW, cbH, "RAM Temp (°C)", _osd.ShowRamTemp, v => { _osd.ShowRamTemp = v; RaiseConfigChanged(); },
                "Show DDR5 temperature on the RAM overlay row"), 2);
            _showRamDualSticksCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY, colW, cbH, "RAM Dual-Stick (35/41°C)", _osd.ShowRamDualSticks, v => { _osd.ShowRamDualSticks = v; RaiseConfigChanged(); },
                "Show both SODIMM temperatures (35/41°C). Uncheck to show only the single hottest stick (41°C)."), 2);
            _showRamSpreadCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY, colW, cbH, "RAM Spread Delta (Δ°C)", _osd.ShowRamSpread, v => { _osd.ShowRamSpread = v; RaiseConfigChanged(); },
                "Show thermal spread delta badge (Δ6°) between stacked SODIMM slots"), 2);
            _showRamLoadCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "RAM Load (%)", _osd.ShowRamLoad, v => { _osd.ShowRamLoad = v; RaiseConfigChanged(); },
                "Show memory utilization percentage (e.g. 22%) on the RAM overlay row"), 2);
            _showRamPowerCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "RAM Power Draw (~W)", _osd.ShowRamPower, v => { _osd.ShowRamPower = v; RaiseConfigChanged(); },
                "Show DDR5 power draw (~3.6W) on the RAM overlay row"), 2);
            _showRamClockCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "RAM Clock (@ MHz)", _osd.ShowRamClock, v => { _osd.ShowRamClock = v; RaiseConfigChanged(); },
                "Show effective DDR data rate (@ 5600MHz) on the RAM overlay row (independent of CPU/iGPU clocks)"), 2);
            curY += rowStep * 2;

            // Category 3: NVMe Storage (left), Chassis & Silicon (right), and Fans
            pairY = curY;
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "NVMe Storage", ref curY, 8, 490), 3);
            _showNvmeCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 8, curY, 242, cbH, "NVMe SSD Temp", _osd.ShowNvme, v => { _osd.ShowNvme = v; RaiseConfigChanged(); }, null), 3);
            _showNvmePowerCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 258, curY, 242, cbH, "NVMe Power Draw (~W)", _osd.ShowNvmePower, v => { _osd.ShowNvmePower = v; RaiseConfigChanged(); },
                "Show PCIe NVMe SSD power draw (~0.9W) on the NVMe overlay row"), 3);
            _showNvmeIoCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 8, curY + rowStep, 242, cbH, "NVMe I/O (MB/s)", _osd.ShowNvmeIo, v => { _osd.ShowNvmeIo = v; RaiseConfigChanged(); },
                "Show live NVMe read/write throughput (MB/s) on the NVMe overlay row"), 3);
            _showNvmeDualCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 258, curY + rowStep, 242, cbH, "NVMe Dual-Drive (°C/°C)", _osd.ShowNvmeDual, v => { _osd.ShowNvmeDual = v; RaiseConfigChanged(); },
                "Show second NVMe drive temperature when idle and a second drive is detected"), 3);

            rightY = pairY;
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "Chassis & Silicon", ref rightY, 518, 490), 3);
            _showAmbientCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 518, rightY, 242, cbH, "Chassis Ambient", _osd.ShowAmbient, v => { _osd.ShowAmbient = v; RaiseConfigChanged(); },
                "Display motherboard chassis ambient temperature (IT8613 Sensor 4)"), 3);
            _showFabricClockCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 768, rightY, 242, cbH, "Infinity Fabric (FCLK)", _osd.ShowFabricClock, v => { _osd.ShowFabricClock = v; RaiseConfigChanged(); },
                "Display AMD Infinity Fabric clock frequency (FCLK) and 1:1 memory bus synchronization status"), 3);
            _showFclkSyncCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 518, rightY + rowStep, 242, cbH, "FCLK Sync Badge (1:1)", _osd.ShowFclkSync, v => { _osd.ShowFclkSync = v; RaiseConfigChanged(); },
                "Show 1:1 Sync / Async badge on the Infinity Fabric row"), 3);
            _showNpuCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, 768, rightY + rowStep, 242, cbH, "Ryzen AI NPU", _osd.ShowNpu, v => { _osd.ShowNpu = v; RaiseConfigChanged(); },
                "Display AMD Ryzen AI NPU activity percentage (PMLOG_NPU_BUSY_AVG) and inference clock"), 3);
            curY += rowStep * 2;

            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "Fans", ref curY), 3);
            _showApuFanCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY, colW, cbH, "APU Fan RPM", _osd.ShowApuFan, v => { _osd.ShowApuFan = v; RaiseConfigChanged(); }, null), 3);
            _showSysFanCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY, colW, cbH, dualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)", _osd.ShowSysFan && dualFans, v => { _osd.ShowSysFan = v; RaiseConfigChanged(); }, null), 3);
            _showSysFanCb.Enabled = dualFans;
            curY += rowStep;

            // Category 4: FPS & Frametime + Graphs, Theme & Global Overlay
            RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "FPS & Frametime", ref curY), 4);
            _showFpsCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY, colW, cbH, "Live FPS & Frametime", _osd.ShowFps, v => { _osd.ShowFps = v; RaiseConfigChanged(); }, null), 4);
            _showFrametimeMsCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY, colW, cbH, "FPS Frametime (ms)", _osd.ShowFrametimeMs, v => { _osd.ShowFrametimeMs = v; RaiseConfigChanged(); },
                "Show frame time in milliseconds (e.g. 16.7ms) next to FPS"), 4);
            _showLowsCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY, colW, cbH, "0.1% Low & Avg FPS", _osd.ShowLows, v => { _osd.ShowLows = v; RaiseConfigChanged(); }, null), 4);
            _showFpsAvgCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY, colW, cbH, "FPS Session Avg", _osd.ShowFpsAvg, v => { _osd.ShowFpsAvg = v; RaiseConfigChanged(); },
                "Show session average FPS on the FPS row instead of peak/min tracker"), 4);
            curY += rowStep;

            _subFieldsHeaderLabel = RegisterCatControl(AddSectionHeader(_metricsScrollPanel, "Graphs, Theme & Global Overlay", ref curY), 4);
            _showLiveTrendCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY, colW, cbH, "Live Trend Graph", _osd.ShowLiveTrend, v =>
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
                    else if (_osd.GraphMode == 4 && !_osd.ShowFpsGraph) _osd.GraphMode = 0;
                }
                RaiseConfigChanged();
            }, "Display the multi-sensor live history trend chart (CPU, iGPU, NVMe, Power, Fan RPM)"), 4);
            _showFpsGraphCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY, colW, cbH, "FPS & Frametime Graph", _osd.ShowFpsGraph, v =>
            {
                _osd.ShowFpsGraph = v;
                if (!v && _osd.GraphMode == 4 && !_osd.ShowLiveTrend) _osd.GraphMode = 0;
                RaiseConfigChanged();
            }, null), 4);
            _dfGraphStyleCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY, colW, cbH, "DF Twin Graphs", _osd.DfGraphStyle, v => { _osd.DfGraphStyle = v; RaiseConfigChanged(); },
                "Render Digital Foundry style twin graphs: FRAME-TIME (MS) 0-50-100 and FRAME-RATE (FPS) 0-40-80-120"), 4);
            _dfGraphFirstCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY, colW, cbH, "DF Graphs on Top", _osd.DfGraphFirst, v => { _osd.DfGraphFirst = v; RaiseConfigChanged(); },
                "Render Digital Foundry style twin graphs above the multi-sensor Live Trend graph"), 4);

            _plotFpsOnTrendCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Plot FPS on Live Trend", _osd.PlotFpsOnTrend, v => { _osd.PlotFpsOnTrend = v; RaiseConfigChanged(); }, null), 4);
            _sparklineBgCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "Background Sparklines", _osd.SparklineBg, v => { _osd.SparklineBg = v; RaiseConfigChanged(); },
                "Render sparklines as full-width glowing background underlays across each metric row"), 4);
            _showModeCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "Thermal Mode", _osd.ShowMode, v => { _osd.ShowMode = v; RaiseConfigChanged(); }, null), 4);
            _showGameNameCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY + rowStep, colW, cbH, "Active Game Title", _osd.ShowGameName, v => { _osd.ShowGameName = v; RaiseConfigChanged(); },
                "Display the active 3D game process name on the OSD header and horizontal bar"), 4);

            _matchThemeCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[0], curY + rowStep * 2, colW, cbH, "Match App Theme", _osd.MatchTheme, v => { _osd.MatchTheme = v; RaiseConfigChanged(); }, null), 4);
            _showPeaksCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[1], curY + rowStep * 2, 96, cbH, "Trackers", _osd.ShowPeaks, v =>
            {
                _osd.ShowPeaks = v;
                if (_trackerModeCombo != null) _trackerModeCombo.Enabled = _osd.ShowPeaks;
                RaiseConfigChanged();
            }, "Show session Peak (▲), Low (▼), Min/Max (▼▲), or Avg (~) trackers for temperature, power, and usage."), 4);

            _trackerModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                Location = new Point(colX[1] + 98, curY + 2 * rowStep),
                Size = new Size(134, 24),
                Enabled = _osd.ShowPeaks,
                AccessibleName = "OSD Peak Tracker Mode",
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.EnableThemedComboBox(_trackerModeCombo, () => ResolvePalette());
            _trackerModeCombo.Items.AddRange(new object[] { "Peaks (▲)", "Lows (▼)", "Min/Max (▼▲)", "Avg (~)" });
            _trackerModeCombo.SelectedIndex = Math.Max(0, Math.Min(3, _osd.PeakTrackerMode));
            _trackerModeCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                if (_trackerModeCombo.SelectedIndex >= 0)
                {
                    _osd.PeakTrackerMode = _trackerModeCombo.SelectedIndex;
                    RaiseConfigChanged();
                }
            };
            SetTip(_trackerModeCombo, "Choose whether the right-hand tracker column displays Peaks (▲), Lows (▼), Min/Max (▼▲), or Session Average (~).");
            _metricsScrollPanel.Controls.Add(_trackerModeCombo);
            RegisterCatControl(_trackerModeCombo, 4);

            _dfCornerInsetCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[2], curY + rowStep * 2, colW, cbH, "DF Corner Inset", _osd.DfCornerInset, v =>
            {
                _osd.DfCornerInset = v;
                if (v)
                {
                    _osd.ShowFpsGraph = true;
                    _osd.DfGraphStyle = true;
                }
                RaiseConfigChanged();
            }, "Render a compact 30-sample Digital Foundry Frame-Time box tucked inside the top-left corner of the FPS graph."), 4);
            _showLatencyCb = RegisterCatControl(AddCheckBox(_metricsScrollPanel, colX[3], curY + rowStep * 2, colW, cbH, "Display Latency (ETW)", _osd.ShowLatency, v =>
            {
                _osd.ShowLatency = v;
                RaiseConfigChanged();
            }, "Show native hook-free Windows ETW Present-to-Display scan-out latency (ms) and Flip Mode (MPO / Indep. Flip / Composed) on the OSD."), 4);
            curY += rowStep * 3 + 4;

            _tipLabel = new Label
            {
                Text = "Tip: Drag OSD with mouse. Right-click for options. Ctrl+Alt+F9 toggles.",
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                Location = new Point(8, curY),
                Size = new Size(990, 22),
                Padding = new Padding(4, 2, 4, 2),
                UseMnemonic = false
            };
            SetTip(_tipLabel, "Drag OSD anywhere across multiple monitors with your mouse. Right-click the OSD anytime for quick scale, graph modes, heights & click-through lock, or press Ctrl+Alt+F9 to toggle overlay.");
            _metricsScrollPanel.Controls.Add(_tipLabel);
            RegisterCatControl(_tipLabel, 0);
        }

        private Button CreateFlatButton(string text, Point loc, Size size, Font font, Action onClick, string tooltip)
        {
            Button btn = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = font,
                Location = loc,
                Size = size,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            if (onClick != null) btn.Click += (s, e) => onClick();
            SetTip(btn, tooltip);
            Controls.Add(btn);
            return btn;
        }

        private void RaiseSnap(int corner)
        {
            EventHandler<int> h = SnapCornerRequested;
            if (h != null) h(this, corner);
        }

        private void ApplyPreset(string presetName, Action applyAction)
        {
            if (applyAction == null) return;
            applyAction();
            RaiseConfigChanged();
            EventHandler<string> sh = StatusMessageRequested;
            if (sh != null) sh(this, "✓ Applied OSD Preset: " + presetName);
        }

        private T RegisterCatControl<T>(T ctrl, int categoryId) where T : Control
        {
            if (ctrl != null) _catControls.Add(new Tuple<Control, int, int>(ctrl, categoryId, ctrl.Top));
            return ctrl;
        }

        private Label AddSectionHeader(Panel parent, string title, ref int curY, int x = 8, int width = 990)
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
            _sectionHeaders.Add(lbl);
            curY += 17;
            return lbl;
        }

        private CheckBox AddCheckBox(Panel parent, int x, int y, int width, int height, string text, bool initialChecked, Action<bool> onChanged, string tooltip)
        {
            CheckBox cb = new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Checked = initialChecked,
                UseMnemonic = false
            };
            Win11VisualStyle.EnableThemedCheckBox(cb, () => ResolvePalette());
            cb.CheckedChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                onChanged(cb.Checked);
            };
            SetTip(cb, tooltip);
            parent.Controls.Add(cb);
            return cb;
        }

        public void ApplyCategoryFilter(int catId)
        {
            _activeCategory = catId;
            if (_metricsScrollPanel == null) return;
            _metricsScrollPanel.SuspendLayout();
            try
            {
                _metricsScrollPanel.AutoScrollPosition = Point.Empty;
                if (catId == 0)
                {
                    for (int i = 0; i < _catControls.Count; i++)
                    {
                        var entry = _catControls[i];
                        entry.Item1.Top = entry.Item3;
                        entry.Item1.Visible = true;
                    }
                }
                else
                {
                    int minY = int.MaxValue;
                    for (int i = 0; i < _catControls.Count; i++)
                    {
                        var entry = _catControls[i];
                        if (entry.Item2 == catId && entry.Item3 < minY) minY = entry.Item3;
                    }
                    if (minY == int.MaxValue) minY = 0;

                    int maxBottom = 0;
                    for (int i = 0; i < _catControls.Count; i++)
                    {
                        var entry = _catControls[i];
                        if (entry.Item2 == catId)
                        {
                            entry.Item1.Top = entry.Item3 - minY;
                            entry.Item1.Visible = true;
                            int bot = entry.Item1.Top + entry.Item1.Height;
                            if (bot > maxBottom) maxBottom = bot;
                        }
                        else if (entry.Item2 != 0)
                        {
                            entry.Item1.Visible = false;
                        }
                    }

                    if (_tipLabel != null)
                    {
                        _tipLabel.Top = maxBottom + 8;
                        _tipLabel.Visible = true;
                    }
                }
            }
            finally
            {
                _metricsScrollPanel.ResumeLayout(true);
            }
            UpdateCategoryStyles(ResolvePalette());
        }

        public void SyncFromConfig(bool showOverlay, bool lockOverlay, bool hasDualFans, bool hasNpu, bool hasGpuHotspot)
        {
            bool prevSuppress = _suppressEvents;
            _suppressEvents = true;
            try
            {
                if (_opacitySlider != null && _opacitySlider.Value != _osd.OpacityPct)
                    _opacitySlider.Value = Math.Max(_opacitySlider.Minimum, Math.Min(_opacitySlider.Maximum, _osd.OpacityPct));
                if (_opacityPctLabel != null)
                    _opacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);

                if (_showCpuCb != null) _showCpuCb.Checked = _osd.ShowCpu;
                if (_showGpuCb != null) _showGpuCb.Checked = _osd.ShowGpu;
                if (_showNvmeCb != null) _showNvmeCb.Checked = _osd.ShowNvme;
                if (_showPowerCb != null) _showPowerCb.Checked = _osd.ShowPower;
                if (_showRamCb != null) _showRamCb.Checked = _osd.ShowRam;
                if (_showVrmCurrentCb != null) _showVrmCurrentCb.Checked = _osd.ShowVrmCurrent;
                if (_showAmbientCb != null) _showAmbientCb.Checked = _osd.ShowAmbient;
                if (_showFabricClockCb != null) _showFabricClockCb.Checked = _osd.ShowFabricClock;
                if (_showNpuCb != null)
                {
                    _showNpuCb.Checked = _osd.ShowNpu && hasNpu;
                    _showNpuCb.Enabled = hasNpu;
                    _showNpuCb.Text = hasNpu ? "Ryzen AI NPU" : "Ryzen AI NPU (N/A)";
                }
                if (_showFpsCb != null) _showFpsCb.Checked = _osd.ShowFps;
                if (_showLowsCb != null) _showLowsCb.Checked = _osd.ShowLows;
                if (_showGpuLoadCb != null) _showGpuLoadCb.Checked = _osd.ShowGpuLoad;
                if (_showApuFanCb != null) _showApuFanCb.Checked = _osd.ShowApuFan;
                if (_showSysFanCb != null)
                {
                    _showSysFanCb.Checked = _osd.ShowSysFan && hasDualFans;
                    _showSysFanCb.Enabled = hasDualFans;
                    _showSysFanCb.Text = hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)";
                }
                if (_showModeCb != null) _showModeCb.Checked = _osd.ShowMode;
                if (_showPeaksCb != null) _showPeaksCb.Checked = _osd.ShowPeaks;
                if (_trackerModeCombo != null)
                {
                    int tmIdx = Math.Max(0, Math.Min(3, _osd.PeakTrackerMode));
                    if (_trackerModeCombo.SelectedIndex != tmIdx) _trackerModeCombo.SelectedIndex = tmIdx;
                    _trackerModeCombo.Enabled = _osd.ShowPeaks;
                }
                if (_showRadeonCb != null) _showRadeonCb.Checked = _osd.ShowRadeon;
                if (_showRadeonExtTagsCb != null) _showRadeonExtTagsCb.Checked = _osd.ShowRadeonExtTags;
                if (_showDriverVerCb != null) _showDriverVerCb.Checked = _osd.ShowDriverVer;
                if (_showClocksCb != null) _showClocksCb.Checked = _osd.ShowClocks;
                if (_showGameNameCb != null) _showGameNameCb.Checked = _osd.ShowGameName;
                if (_sparklineBgCb != null) _sparklineBgCb.Checked = _osd.SparklineBg;
                if (_showLiveTrendCb != null) _showLiveTrendCb.Checked = _osd.ShowLiveTrend;
                if (_showFpsGraphCb != null) _showFpsGraphCb.Checked = _osd.ShowFpsGraph;
                if (_plotFpsOnTrendCb != null) _plotFpsOnTrendCb.Checked = _osd.PlotFpsOnTrend;
                if (_dfGraphStyleCb != null) _dfGraphStyleCb.Checked = _osd.DfGraphStyle;
                if (_dfCornerInsetCb != null) _dfCornerInsetCb.Checked = _osd.DfCornerInset;
                if (_dfGraphFirstCb != null) _dfGraphFirstCb.Checked = _osd.DfGraphFirst;
                if (_showRamTempCb != null) _showRamTempCb.Checked = _osd.ShowRamTemp;
                if (_showRamDualSticksCb != null) _showRamDualSticksCb.Checked = _osd.ShowRamDualSticks;
                if (_showRamSpreadCb != null) _showRamSpreadCb.Checked = _osd.ShowRamSpread;
                if (_showRamLoadCb != null) _showRamLoadCb.Checked = _osd.ShowRamLoad;
                if (_showRamPowerCb != null) _showRamPowerCb.Checked = _osd.ShowRamPower;
                if (_showRamClockCb != null) _showRamClockCb.Checked = _osd.ShowRamClock;
                if (_showNvmePowerCb != null) _showNvmePowerCb.Checked = _osd.ShowNvmePower;
                if (_showNvmeIoCb != null) _showNvmeIoCb.Checked = _osd.ShowNvmeIo;
                if (_showNvmeDualCb != null) _showNvmeDualCb.Checked = _osd.ShowNvmeDual;
                if (_showCpuLoadCb != null) _showCpuLoadCb.Checked = _osd.ShowCpuLoad;
                if (_showGpuHotspotCb != null)
                {
                    _showGpuHotspotCb.Checked = _osd.ShowGpuHotspot && hasGpuHotspot;
                    _showGpuHotspotCb.Enabled = hasGpuHotspot;
                    _showGpuHotspotCb.Text = hasGpuHotspot ? "GPU Hotspot (HS °C)" : "GPU Hotspot (N/A)";
                }
                if (_showPowerLimitCb != null) _showPowerLimitCb.Checked = _osd.ShowPowerLimit;
                if (_showFclkSyncCb != null) _showFclkSyncCb.Checked = _osd.ShowFclkSync;
                if (_showFrametimeMsCb != null) _showFrametimeMsCb.Checked = _osd.ShowFrametimeMs;
                if (_showFpsAvgCb != null) _showFpsAvgCb.Checked = _osd.ShowFpsAvg;
                if (_showLatencyCb != null) _showLatencyCb.Checked = _osd.ShowLatency;
                if (_matchThemeCb != null) _matchThemeCb.Checked = _osd.MatchTheme;

                if (_toggleOverlayBtn != null)
                    _toggleOverlayBtn.Text = showOverlay ? "● OSD Active" : "○ OSD Hidden";
                if (_toggleLockBtn != null)
                    _toggleLockBtn.Text = lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)";
            }
            finally
            {
                _suppressEvents = prevSuppress;
            }
        }

        private void UpdateCategoryStyles(ThemePalette pal)
        {
            if (_catButtons == null || pal == null) return;
            for (int ci = 0; ci < _catButtons.Length; ci++)
            {
                if (_catButtons[ci] != null)
                {
                    bool catActive = (_activeCategory == ci);
                    Win11VisualStyle.StylePillButton(_catButtons[ci], pal, catActive, pal.SecondaryAccent, pal.ActiveFg);
                }
            }
        }

        public void ApplyTheme(ThemePalette pal, bool showOverlay, bool lockOverlay)
        {
            if (pal == null) return;
            Color surface = pal.Surface;
            Color surface2 = pal.Surface2;
            Color text = pal.Text;
            Color textDim = pal.TextDim;
            Color accent = pal.Accent;
            Color border = pal.Border;

            BackColor = surface;

            Button[] modeBtns = new[] { _btnTextOnly, _btnSparklines, _btnCombinedGraph, _btnBothGraphs, _btnGraphsOnly };
            for (int i = 0; i < modeBtns.Length; i++)
            {
                if (modeBtns[i] == null) continue;
                bool active = (_osd.GraphMode == i);
                Win11VisualStyle.StylePillButton(modeBtns[i], pal, active);
                modeBtns[i].Font = active ? PillFontBold : PillFontRegular;
            }

            Button[] scaleBtns = new[] { _btnScale100, _btnScale125, _btnScale150, _btnScale185 };
            for (int i = 0; i < scaleBtns.Length; i++)
            {
                if (scaleBtns[i] == null) continue;
                bool active = (_osd.ScaleTier == i);
                Win11VisualStyle.StylePillButton(scaleBtns[i], pal, active);
                scaleBtns[i].Font = active ? PillFontBold : PillFontRegular;
            }

            if (_btnLayoutH != null)
            {
                bool activeH = !_osd.VerticalLayout;
                Win11VisualStyle.StylePillButton(_btnLayoutH, pal, activeH);
                _btnLayoutH.Font = activeH ? PillFontBold : PillFontRegular;
            }
            if (_btnLayoutV != null)
            {
                bool activeV = _osd.VerticalLayout;
                Win11VisualStyle.StylePillButton(_btnLayoutV, pal, activeV);
                _btnLayoutV.Font = activeV ? PillFontBold : PillFontRegular;
            }

            if (_btnHeightCompact != null)
            {
                bool active = (_osd.GraphHeight <= 85);
                Win11VisualStyle.StylePillButton(_btnHeightCompact, pal, active);
                _btnHeightCompact.Font = active ? PillFontBold : PillFontRegular;
            }
            if (_btnHeightMedium != null)
            {
                bool active = (_osd.GraphHeight > 85 && _osd.GraphHeight <= 120);
                Win11VisualStyle.StylePillButton(_btnHeightMedium, pal, active);
                _btnHeightMedium.Font = active ? PillFontBold : PillFontRegular;
            }
            if (_btnHeightLarge != null)
            {
                bool active = (_osd.GraphHeight > 120 && _osd.GraphHeight <= 160);
                Win11VisualStyle.StylePillButton(_btnHeightLarge, pal, active);
                _btnHeightLarge.Font = active ? PillFontBold : PillFontRegular;
            }
            if (_btnHeightXl != null)
            {
                bool active = (_osd.GraphHeight > 160);
                Win11VisualStyle.StylePillButton(_btnHeightXl, pal, active);
                _btnHeightXl.Font = active ? PillFontBold : PillFontRegular;
            }

            foreach (Button b in new[] { _quickPosTl, _quickPosTc, _quickPosTr, _quickPosBl, _quickPosBc, _quickPosBr })
            {
                if (b != null) Win11VisualStyle.StyleModernButton(b, surface2, text, border, pal.IsDark);
            }

            foreach (Button pBtn in new[] { _presetMinimalBtn, _presetCompactBtn, _presetDfBtn, _presetFullBtn })
            {
                if (pBtn != null)
                {
                    Color pBorder = Win11VisualStyle.Blend(border, accent, pal.IsDark ? 0.35f : 0.25f);
                    Win11VisualStyle.StyleModernButton(pBtn, surface2, text, pBorder, pal.IsDark);
                }
            }
            UpdateCategoryStyles(pal);

            foreach (Button actBtn in new[] { _selectAllBtn, _deselectAllBtn, _resetDefaultsBtn })
            {
                if (actBtn != null) Win11VisualStyle.StyleModernButton(actBtn, surface2, text, border, pal.IsDark);
            }

            if (_toggleOverlayBtn != null)
            {
                _toggleOverlayBtn.Text = showOverlay ? "● OSD Active" : "○ OSD Hidden";
                Win11VisualStyle.StylePillButton(_toggleOverlayBtn, pal, showOverlay);
            }
            if (_toggleLockBtn != null)
            {
                _toggleLockBtn.Text = lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)";
                Color lockAccent = pal.IsRadeon ? RadeonOrange : Color.FromArgb(0xEA, 0x58, 0x0C);
                Win11VisualStyle.StylePillButton(_toggleLockBtn, pal, lockOverlay, lockAccent, Color.White);
            }

            if (_opacitySlider != null)
            {
                _opacitySlider.BackColor = surface;
                _opacitySlider.AccentOverride = pal.Accent;
                _opacitySlider.Invalidate();
            }
            if (_opacityPctLabel != null) { _opacityPctLabel.BackColor = surface; _opacityPctLabel.ForeColor = text; }

            if (_titleLabel != null) { _titleLabel.BackColor = surface; _titleLabel.ForeColor = accent; }
            foreach (Label lbl in new[] { _graphModeLabel, _graphHeightLabel, _scaleLabel, _layoutLabel, _opacityLabel, _snapLabel, _metricsHeaderLabel, _subFieldsHeaderLabel })
            {
                if (lbl != null) { lbl.BackColor = surface; lbl.ForeColor = text; }
            }
            if (_metricsScrollPanel != null)
            {
                _metricsScrollPanel.BackColor = surface;
                _metricsScrollPanel.ForeColor = text;
                Win11VisualStyle.ApplyDarkExplorerTheme(_metricsScrollPanel, pal.IsDark);
            }
            foreach (Label secHdr in _sectionHeaders)
            {
                if (secHdr != null) { secHdr.BackColor = surface; secHdr.ForeColor = accent; }
            }
            if (_tipLabel != null) { _tipLabel.BackColor = surface2; _tipLabel.ForeColor = textDim; }

            CheckBox[] cbs = new[]
            {
                _showCpuCb, _showGpuCb, _showNvmeCb, _showRamCb,
                _showVrmCurrentCb, _showAmbientCb, _showFabricClockCb, _showNpuCb,
                _showApuFanCb, _showSysFanCb, _showPowerCb, _showGpuLoadCb,
                _showFpsCb, _showLowsCb,
                _showModeCb, _showPeaksCb, _showRadeonCb, _showRadeonExtTagsCb, _showDriverVerCb,
                _showClocksCb, _showGameNameCb, _sparklineBgCb, _matchThemeCb,
                _showLiveTrendCb, _showFpsGraphCb, _plotFpsOnTrendCb, _dfGraphStyleCb, _dfCornerInsetCb, _dfGraphFirstCb,
                _showRamTempCb, _showRamDualSticksCb, _showRamSpreadCb, _showRamLoadCb,
                _showRamPowerCb, _showRamClockCb, _showNvmePowerCb,
                _showNvmeIoCb, _showNvmeDualCb, _showCpuLoadCb, _showGpuHotspotCb,
                _showPowerLimitCb, _showFclkSyncCb, _showFrametimeMsCb, _showFpsAvgCb, _showLatencyCb
            };
            foreach (CheckBox cb in cbs)
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                    cb.Invalidate();
                }
            }
            if (_trackerModeCombo != null)
            {
                Win11VisualStyle.ApplyDarkComboTheme(_trackerModeCombo, pal);
            }
        }
    }
}
