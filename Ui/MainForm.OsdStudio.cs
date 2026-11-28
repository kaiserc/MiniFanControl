using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public partial class MainForm
    {
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

        private void SyncOsdStudioUiFromOverlay(bool readFromOverlay = true)
        {
            if (readFromOverlay && _overlayForm != null)
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

            bool prevSuppress = _suppressOsdStudioEvents;
            _suppressOsdStudioEvents = true;
            try
            {
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
                    _osdShowGpuHotspotCb.Text = hasHotspot ? "GPU Hotspot (HS °C)" : "GPU Hotspot (N/A)";
                }
                if (_osdShowPowerLimitCb != null) _osdShowPowerLimitCb.Checked = _osd.ShowPowerLimit;
                if (_osdShowFclkSyncCb != null) _osdShowFclkSyncCb.Checked = _osd.ShowFclkSync;
                if (_osdShowFrametimeMsCb != null) _osdShowFrametimeMsCb.Checked = _osd.ShowFrametimeMs;
                if (_osdShowFpsAvgCb != null) _osdShowFpsAvgCb.Checked = _osd.ShowFpsAvg;
                if (_osdShowLatencyCb != null) _osdShowLatencyCb.Checked = _osd.ShowLatency;
                if (_osdMatchThemeCb != null) _osdMatchThemeCb.Checked = _osd.MatchTheme;

                if (_osdToggleOverlayBtn != null)
                    _osdToggleOverlayBtn.Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden";
                if (_osdToggleLockBtn != null)
                    _osdToggleLockBtn.Text = _lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)";
            }
            finally
            {
                _suppressOsdStudioEvents = prevSuppress;
            }

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

            int clientH = _testClientHeightOverride > 0 ? _testClientHeightOverride : ClientSize.Height;
            ApplyDynamicVerticalLayout(clientH);
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

        private string GetCurrentOsdModeSummary(FanStatus st)
        {
            if (IsReadOnlyMonitoringMode) return "Read-Only";
            if (_biosAutoControlActive) return "BIOS Auto";
            string apuModeName = _apuManualOverride ? "Man" : (_apuProfile != null ? _apuProfile.Name : st.Mode);
            string sysModeName = _sysManualOverride ? "Man" : (_sysProfile != null ? _sysProfile.Name : st.Mode);
            return (!_hasDualFans || apuModeName == sysModeName)
                ? ShortModeTag(apuModeName)
                : string.Format("{0}/{1}", ShortModeTag(apuModeName), ShortModeTag(sysModeName));
        }

        private void SetOverlayVisible(bool visible)
        {
            _showOverlay = visible;
            if (_overlayForm != null)
            {
                if (visible)
                {
                    _overlayForm.IsReadOnlyMode = IsReadOnlyMonitoringMode;
                    _overlayForm.SampleIntervalMs = _refreshIntervalMs;
                    if (!_overlayForm.Visible) _overlayForm.Show();
                    _overlayForm.ApplyClickThrough(_lockOverlay);
                    _overlayForm.EnsureTopMost();
                    _overlayForm.BringToFront();
                    if (_lastStatus.CpuTemp > 0 || _lastStatus.RamTotalGb > 0)
                    {
                        _overlayForm.UpdateMetrics(_lastStatus, GetCurrentOsdModeSummary(_lastStatus), _hasDualFans);
                    }
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
            if (_osdToggleLockBtn != null)
                _osdToggleLockBtn.Text = _lockOverlay ? "Locked (Click-Thru)" : "Unlocked (Drag)";
            UpdateOsdStudioStyles();
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


        private void BuildOsdStudioPanel(Panel p)
        {
            // Row 1 (y=5): Title + 1-Click Presets + Quick Toggle Buttons
            _osdTitleLabel = new Label
            {
                Text = "OSD OVERLAY STUDIO",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(164, 22),
                UseMnemonic = false
            };
            p.Controls.Add(_osdTitleLabel);

            Font presetFont = new Font("Segoe UI", 7.75f, FontStyle.Bold);
            _osdPresetMinimalBtn = new Button
            {
                Text = "Minimal FPS",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(178, 5),
                Size = new Size(104, 26),
                Cursor = Cursors.Hand
            };
            _osdPresetMinimalBtn.Click += (s, e) => ApplyOsdPreset("Minimal FPS", () => _osd.ApplyPresetMinimalFps());
            _uiToolTip.SetToolTip(_osdPresetMinimalBtn, "Preset: Clean horizontal FPS, 0.1% Lows, CPU & iGPU temps bar (Text Only)");
            p.Controls.Add(_osdPresetMinimalBtn);

            _osdPresetCompactBtn = new Button
            {
                Text = "Compact",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(288, 5),
                Size = new Size(92, 26),
                Cursor = Cursors.Hand
            };
            _osdPresetCompactBtn.Click += (s, e) => ApplyOsdPreset("Compact Gaming", () => _osd.ApplyPresetCompactGaming(_hasDualFans));
            _uiToolTip.SetToolTip(_osdPresetCompactBtn, "Preset: Compact vertical gaming HUD with sparklines and DF corner-inset frametime graph");
            p.Controls.Add(_osdPresetCompactBtn);

            _osdPresetDfBtn = new Button
            {
                Text = "Digital Foundry",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(386, 5),
                Size = new Size(122, 26),
                Cursor = Cursors.Hand
            };
            _osdPresetDfBtn.Click += (s, e) => ApplyOsdPreset("Digital Foundry", () => _osd.ApplyPresetDigitalFoundry());
            _uiToolTip.SetToolTip(_osdPresetDfBtn, "Preset: Digital Foundry twin Frame-Time + Frame-Rate benchmark overlay with core telemetry");
            p.Controls.Add(_osdPresetDfBtn);

            _osdPresetFullBtn = new Button
            {
                Text = "Full Audit",
                FlatStyle = FlatStyle.Flat,
                Font = presetFont,
                Location = new Point(514, 5),
                Size = new Size(94, 26),
                Cursor = Cursors.Hand
            };
            _osdPresetFullBtn.Click += (s, e) => ApplyOsdPreset("Full Audit", () => _osd.ApplyPresetFullAudit(_hasDualFans));
            _uiToolTip.SetToolTip(_osdPresetFullBtn, "Preset: Full hardware benchmark audit (all sensors, VRM currents, dual-stick DDR5, and both live graphs)");
            p.Controls.Add(_osdPresetFullBtn);

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
            _osdBtnBothGraphs.Click += (s, e) => { _osd.GraphMode = 3; _osd.ShowLiveTrend = true; SyncOverlayFromStudio(); SyncOsdStudioUiFromOverlay(false); };
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
            _osdBtnCombinedGraph.Click += (s, e) => { _osd.GraphMode = 2; _osd.ShowLiveTrend = true; SyncOverlayFromStudio(); SyncOsdStudioUiFromOverlay(false); };
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
            _osdBtnSparklines.Click += (s, e) => { _osd.GraphMode = 1; _osd.ShowLiveTrend = false; SyncOverlayFromStudio(); SyncOsdStudioUiFromOverlay(false); };
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
            _osdBtnTextOnly.Click += (s, e) => { _osd.GraphMode = 0; _osd.ShowLiveTrend = false; _osd.ShowFpsGraph = false; SyncOverlayFromStudio(); SyncOsdStudioUiFromOverlay(false); };
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
                }
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
            };
            _uiToolTip.SetToolTip(_osdBtnGraphsOnly, "Hide all metric text rows and display ONLY the enabled graphs (toggle FPS & Frametime Graph and/or Live Trend Graph below).");
            p.Controls.Add(_osdBtnGraphsOnly);

            // Right side: Height buttons — flush-aligned across x=492..1048
            _osdBtnHeightCompact = new Button
            {
                Text = "75px Compact",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(492, 56),
                Size = new Size(134, 26),
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
                Location = new Point(632, 56),
                Size = new Size(134, 26),
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
                Location = new Point(772, 56),
                Size = new Size(134, 26),
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
                Location = new Point(912, 56),
                Size = new Size(136, 26),
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

            // Row 3 Buttons (y=106, h=26): flush-aligned across x=8..482 matching Graph Mode row above
            _osdBtnScale100 = new Button
            {
                Text = "100% Std",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 106),
                Size = new Size(115, 26),
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
                Location = new Point(127, 106),
                Size = new Size(115, 26),
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
                Location = new Point(246, 106),
                Size = new Size(115, 26),
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
                Location = new Point(365, 106),
                Size = new Size(117, 26),
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
                Size = new Size(275, 26),
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
                Location = new Point(773, 106),
                Size = new Size(275, 26),
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

            _osdOpacitySlider = new ModernSlider
            {
                Minimum = 35,
                Maximum = 100,
                Value = Math.Max(35, Math.Min(100, _osd.OpacityPct)),
                ZeroLabel = null,
                UnitSuffix = "%",
                PaletteProvider = () => CurrentPalette,
                Location = new Point(72, 134),
                Size = new Size(130, 30),
                AccessibleName = "OSD Overlay Opacity Percentage"
            };
            _osdOpacitySlider.Scroll += (s, e) =>
            {
                _osd.OpacityPct = _osdOpacitySlider.Value;
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osd.OpacityPct);
                if (_overlayForm != null) _overlayForm.ApplyOpacity(_osd.OpacityPct);
            };
            _osdOpacitySlider.ValueCommitted += (s, e) =>
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

            // Row 5 (y=170..195): Metrics Category Sub-Tabs + Select All / Deselect All / Defaults
            _osdMetricsHeaderLabel = new Label
            {
                Text = "Metrics:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(8, 174),
                Size = new Size(64, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdMetricsHeaderLabel);

            string[] catTitles = new[] { "All", "CPU & Power", "iGPU & RAM", "Storage & Fans", "FPS & Graphs" };
            int[] catWidths = new[] { 58, 114, 108, 126, 114 };
            _osdCatButtons = new Button[catTitles.Length];
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
                cb.Click += (s, e) => ApplyOsdMetricCategoryFilter(catIdx);
                p.Controls.Add(cb);
                _osdCatButtons[ci] = cb;
                catX += catWidths[ci] + 5;
            }

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
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
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
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
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
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
            };
            p.Controls.Add(_osdResetDefaultsBtn);

            // Row 6+ (y=198..408): Per-Metric Configuration Sections (scrollable inside compact 976px main window)
            _osdMetricsScrollPanel = new Panel
            {
                Location = new Point(4, 198),
                Size = new Size(1048, 210),
                AutoScroll = true
            };
            p.Controls.Add(_osdMetricsScrollPanel);
            _osdSectionHeaders.Clear();
            _osdCatControls.Clear();
            _osdActiveCategory = 0;

            int[] colX = new[] { 8, 260, 512, 764 };
            const int colW = 246;
            const int cbH = 22;
            const int rowStep = 21;
            int curY = 0;

            // Category 1: CPU (left) & Power & VRM (right)
            int pairY = curY;
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "CPU", ref curY, 8, 490), 1);
            _osdShowCpuCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY, 242, cbH, "CPU Temp", _osd.ShowCpu, v => { _osd.ShowCpu = v; SyncOverlayFromStudio(); }, null), 1);
            _osdShowCpuLoadCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY, 242, cbH, "CPU Load (%)", _osd.ShowCpuLoad, v => { _osd.ShowCpuLoad = v; SyncOverlayFromStudio(); },
                "Show CPU utilization percentage on the CPU overlay row"), 1);
            _osdShowClocksCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY + rowStep, 242, cbH, "Clock Speeds (@ GHz/MHz)", _osd.ShowClocks, v => { _osd.ShowClocks = v; SyncOverlayFromStudio(); },
                "Display effective CPU, iGPU, and RAM clock frequencies (@ 4.1G / @ 800MHz). Uncheck to maximize space for sparklines."), 1);

            int rightY = pairY;
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "Power & VRM", ref rightY, 518, 490), 1);
            _osdShowPowerCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY, 242, cbH, "APU Power (W)", _osd.ShowPower, v => { _osd.ShowPower = v; SyncOverlayFromStudio(); }, null), 1);
            _osdShowPowerLimitCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY, 242, cbH, "APU Power Limit (/W)", _osd.ShowPowerLimit, v => { _osd.ShowPowerLimit = v; SyncOverlayFromStudio(); },
                "Show active PPT/STAPM power limit (/65W) next to APU Power"), 1);
            _osdShowVrmCurrentCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY + rowStep, 242, cbH, "VRM Current (TDC/EDC)", _osd.ShowVrmCurrent, v => { _osd.ShowVrmCurrent = v; SyncOverlayFromStudio(); },
                "Display deep silicon VRM amperage draw (continuous TDC and peak EDC amps for VDD & SOC rails)"), 1);
            curY += rowStep * 2;

            // Category 2: iGPU & Radeon + RAM (DDR5)
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "iGPU & Radeon", ref curY), 2);
            _osdShowGpuCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "iGPU Temp", _osd.ShowGpu, v => { _osd.ShowGpu = v; SyncOverlayFromStudio(); }, null), 2);
            _osdShowGpuHotspotCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "GPU Hotspot (HS °C)", _osd.ShowGpuHotspot, v => { _osd.ShowGpuHotspot = v; SyncOverlayFromStudio(); },
                "Show GPU junction hotspot temperature (HS °C) when available"), 2);
            _osdShowGpuLoadCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "GPU Load & Clock", _osd.ShowGpuLoad, v => { _osd.ShowGpuLoad = v; SyncOverlayFromStudio(); }, null), 2);
            _osdShowRadeonCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "Radeon Feature Tags", _osd.ShowRadeon, v => { _osd.ShowRadeon = v; SyncOverlayFromStudio(); },
                "Display compact pill tags for active AMD Radeon features (FreeSync, Anti-Lag, AFMF, Chill, Boost, RSR, RIS)"), 2);
            _osdShowDriverVerCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Radeon Driver Version", _osd.ShowDriverVer, v => { _osd.ShowDriverVer = v; SyncOverlayFromStudio(); },
                "Show the installed AMD Radeon Software (Adrenalin) driver version on the OSD"), 2);
            _osdShowRadeonExtTagsCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "SAM, EnhSync & FRTC Tags", _osd.ShowRadeonExtTags, v => { _osd.ShowRadeonExtTags = v; SyncOverlayFromStudio(); },
                "Include Smart Access Memory (SAM), Enhanced Sync, and FRTC cap pills in the OSD Radeon tags row"), 2);
            curY += rowStep * 2;

            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "RAM (DDR5)", ref curY), 2);
            _osdShowRamCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "RAM Load & Power", _osd.ShowRam, v => { _osd.ShowRam = v; SyncOverlayFromStudio(); }, null), 2);
            _osdShowRamTempCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "RAM Temp (°C)", _osd.ShowRamTemp, v => { _osd.ShowRamTemp = v; SyncOverlayFromStudio(); },
                "Show DDR5 temperature on the RAM overlay row"), 2);
            _osdShowRamDualSticksCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "RAM Dual-Stick (35/41°C)", _osd.ShowRamDualSticks, v => { _osd.ShowRamDualSticks = v; SyncOverlayFromStudio(); },
                "Show both SODIMM temperatures (35/41°C). Uncheck to show only the single hottest stick (41°C)."), 2);
            _osdShowRamSpreadCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "RAM Spread Delta (Δ°C)", _osd.ShowRamSpread, v => { _osd.ShowRamSpread = v; SyncOverlayFromStudio(); },
                "Show thermal spread delta badge (Δ6°) between stacked SODIMM slots"), 2);
            _osdShowRamLoadCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "RAM Load (%)", _osd.ShowRamLoad, v => { _osd.ShowRamLoad = v; SyncOverlayFromStudio(); },
                "Show memory utilization percentage (e.g. 22%) on the RAM overlay row"), 2);
            _osdShowRamPowerCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "RAM Power Draw (~W)", _osd.ShowRamPower, v => { _osd.ShowRamPower = v; SyncOverlayFromStudio(); },
                "Show DDR5 power draw (~3.6W) on the RAM overlay row"), 2);
            _osdShowRamClockCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "RAM Clock (@ MHz)", _osd.ShowRamClock, v => { _osd.ShowRamClock = v; SyncOverlayFromStudio(); },
                "Show effective DDR data rate (@ 5600MHz) on the RAM overlay row (independent of CPU/iGPU clocks)"), 2);
            curY += rowStep * 2;

            // Category 3: NVMe Storage (left), Chassis & Silicon (right), and Fans
            pairY = curY;
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "NVMe Storage", ref curY, 8, 490), 3);
            _osdShowNvmeCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY, 242, cbH, "NVMe SSD Temp", _osd.ShowNvme, v => { _osd.ShowNvme = v; SyncOverlayFromStudio(); }, null), 3);
            _osdShowNvmePowerCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY, 242, cbH, "NVMe Power Draw (~W)", _osd.ShowNvmePower, v => { _osd.ShowNvmePower = v; SyncOverlayFromStudio(); },
                "Show PCIe NVMe SSD power draw (~0.9W) on the NVMe overlay row"), 3);
            _osdShowNvmeIoCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 8, curY + rowStep, 242, cbH, "NVMe I/O (MB/s)", _osd.ShowNvmeIo, v => { _osd.ShowNvmeIo = v; SyncOverlayFromStudio(); },
                "Show live NVMe read/write throughput (MB/s) on the NVMe overlay row"), 3);
            _osdShowNvmeDualCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 258, curY + rowStep, 242, cbH, "NVMe Dual-Drive (°C/°C)", _osd.ShowNvmeDual, v => { _osd.ShowNvmeDual = v; SyncOverlayFromStudio(); },
                "Show second NVMe drive temperature when idle and a second drive is detected"), 3);

            rightY = pairY;
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "Chassis & Silicon", ref rightY, 518, 490), 3);
            _osdShowAmbientCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY, 242, cbH, "Chassis Ambient", _osd.ShowAmbient, v => { _osd.ShowAmbient = v; SyncOverlayFromStudio(); },
                "Display motherboard chassis ambient temperature (IT8613 Sensor 4)"), 3);
            _osdShowFabricClockCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY, 242, cbH, "Infinity Fabric (FCLK)", _osd.ShowFabricClock, v => { _osd.ShowFabricClock = v; SyncOverlayFromStudio(); },
                "Display AMD Infinity Fabric clock frequency (FCLK) and 1:1 memory bus synchronization status"), 3);
            _osdShowFclkSyncCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 518, rightY + rowStep, 242, cbH, "FCLK Sync Badge (1:1)", _osd.ShowFclkSync, v => { _osd.ShowFclkSync = v; SyncOverlayFromStudio(); },
                "Show 1:1 Sync / Async badge on the Infinity Fabric row"), 3);
            _osdShowNpuCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, 768, rightY + rowStep, 242, cbH, "Ryzen AI NPU", _osd.ShowNpu, v => { _osd.ShowNpu = v; SyncOverlayFromStudio(); },
                "Display AMD Ryzen AI NPU activity percentage (PMLOG_NPU_BUSY_AVG) and inference clock"), 3);
            curY += rowStep * 2;

            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "Fans", ref curY), 3);
            _osdShowApuFanCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "APU Fan RPM", _osd.ShowApuFan, v => { _osd.ShowApuFan = v; SyncOverlayFromStudio(); }, null), 3);
            _osdShowSysFanCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, _hasDualFans ? "Sys Fan RPM" : "Sys Fan RPM (N/A)", _osd.ShowSysFan && _hasDualFans, v => { _osd.ShowSysFan = v; SyncOverlayFromStudio(); }, null), 3);
            _osdShowSysFanCb.Enabled = _hasDualFans;
            curY += rowStep;

            // Category 4: FPS & Frametime + Graphs, Theme & Global Overlay
            RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "FPS & Frametime", ref curY), 4);
            _osdShowFpsCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "Live FPS & Frametime", _osd.ShowFps, v => { _osd.ShowFps = v; SyncOverlayFromStudio(); }, null), 4);
            _osdShowFrametimeMsCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "FPS Frametime (ms)", _osd.ShowFrametimeMs, v => { _osd.ShowFrametimeMs = v; SyncOverlayFromStudio(); },
                "Show frame time in milliseconds (e.g. 16.7ms) next to FPS"), 4);
            _osdShowLowsCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "0.1% Low & Avg FPS", _osd.ShowLows, v => { _osd.ShowLows = v; SyncOverlayFromStudio(); }, null), 4);
            _osdShowFpsAvgCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "FPS Session Avg", _osd.ShowFpsAvg, v => { _osd.ShowFpsAvg = v; SyncOverlayFromStudio(); },
                "Show session average FPS on the FPS row instead of peak/min tracker"), 4);
            curY += rowStep;

            _osdSubFieldsHeaderLabel = RegisterOsdCatControl(AddOsdSectionHeader(_osdMetricsScrollPanel, "Graphs, Theme & Global Overlay", ref curY), 4);
            _osdShowLiveTrendCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY, colW, cbH, "Live Trend Graph", _osd.ShowLiveTrend, v =>
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
                SyncOverlayFromStudio();
                UpdateOsdStudioStyles();
            }, "Display the multi-sensor live history trend chart (CPU, iGPU, NVMe, Power, Fan RPM)"), 4);
            _osdShowFpsGraphCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY, colW, cbH, "FPS & Frametime Graph", _osd.ShowFpsGraph, v =>
            {
                _osd.ShowFpsGraph = v;
                if (!v && _osd.GraphMode == 4 && !_osd.ShowLiveTrend)
                {
                    _osd.GraphMode = 0;
                }
                SyncOverlayFromStudio();
                UpdateOsdStudioStyles();
            }, null), 4);
            _osdDfGraphStyleCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY, colW, cbH, "DF Twin Graphs", _osd.DfGraphStyle, v => { _osd.DfGraphStyle = v; SyncOverlayFromStudio(); },
                "Render Digital Foundry style twin graphs: FRAME-TIME (MS) 0-50-100 and FRAME-RATE (FPS) 0-40-80-120"), 4);
            _osdDfGraphFirstCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY, colW, cbH, "DF Graphs on Top", _osd.DfGraphFirst, v => { _osd.DfGraphFirst = v; SyncOverlayFromStudio(); },
                "Render Digital Foundry style twin graphs above the multi-sensor Live Trend graph"), 4);

            _osdPlotFpsOnTrendCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep, colW, cbH, "Plot FPS on Live Trend", _osd.PlotFpsOnTrend, v => { _osd.PlotFpsOnTrend = v; SyncOverlayFromStudio(); }, null), 4);
            _osdSparklineBgCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep, colW, cbH, "Background Sparklines", _osd.SparklineBg, v => { _osd.SparklineBg = v; SyncOverlayFromStudio(); },
                "Render sparklines as full-width glowing background underlays across each metric row"), 4);
            _osdShowModeCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep, colW, cbH, "Thermal Mode", _osd.ShowMode, v => { _osd.ShowMode = v; SyncOverlayFromStudio(); }, null), 4);
            _osdShowGameNameCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY + rowStep, colW, cbH, "Active Game Title", _osd.ShowGameName, v => { _osd.ShowGameName = v; SyncOverlayFromStudio(); },
                "Display the active 3D game process name on the OSD header and horizontal bar"), 4);

            _osdMatchThemeCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[0], curY + rowStep * 2, colW, cbH, "Match App Theme", _osd.MatchTheme, v => { _osd.MatchTheme = v; SyncOverlayFromStudio(); }, null), 4);
            _osdShowPeaksCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[1], curY + rowStep * 2, 96, cbH, "Trackers", _osd.ShowPeaks, v =>
            {
                _osd.ShowPeaks = v;
                if (_osdTrackerModeCombo != null) _osdTrackerModeCombo.Enabled = _osd.ShowPeaks;
                SyncOverlayFromStudio();
            }, "Show session Peak (▲), Low (▼), Min/Max (▼▲), or Avg (~) trackers for temperature, power, and usage."), 4);

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
            Win11VisualStyle.EnableThemedComboBox(_osdTrackerModeCombo, () => CurrentPalette);
            _osdTrackerModeCombo.Items.AddRange(new object[] { "Peaks (▲)", "Lows (▼)", "Min/Max (▼▲)", "Avg (~)" });
            _osdTrackerModeCombo.SelectedIndex = Math.Max(0, Math.Min(3, _osd.PeakTrackerMode));
            _osdTrackerModeCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressOsdStudioEvents) return;
                if (_osdTrackerModeCombo.SelectedIndex >= 0)
                {
                    _osd.PeakTrackerMode = _osdTrackerModeCombo.SelectedIndex;
                    SyncOverlayFromStudio();
                }
            };
            _uiToolTip.SetToolTip(_osdTrackerModeCombo, "Choose whether the right-hand tracker column displays Peaks (▲), Lows (▼), Min/Max (▼▲), or Session Average (~).");
            _osdMetricsScrollPanel.Controls.Add(_osdTrackerModeCombo);
            RegisterOsdCatControl(_osdTrackerModeCombo, 4);
            _osdDfCornerInsetCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[2], curY + rowStep * 2, colW, cbH, "DF Corner Inset", _osd.DfCornerInset, v =>
            {
                _osd.DfCornerInset = v;
                if (v)
                {
                    _osd.ShowFpsGraph = true;
                    _osd.DfGraphStyle = true;
                }
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
            }, "Render a compact 30-sample Digital Foundry Frame-Time box tucked inside the top-left corner of the FPS graph."), 4);
            _osdShowLatencyCb = RegisterOsdCatControl(AddOsdCheckBox(_osdMetricsScrollPanel, colX[3], curY + rowStep * 2, colW, cbH, "Display Latency (ETW)", _osd.ShowLatency, v =>
            {
                _osd.ShowLatency = v;
                SyncOverlayFromStudio();
            }, "Show native hook-free Windows ETW Present-to-Display scan-out latency (ms) and Flip Mode (MPO / Indep. Flip / Composed) on the OSD."), 4);
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
            RegisterOsdCatControl(_osdTipLabel, 0);
        }

        private T RegisterOsdCatControl<T>(T ctrl, int categoryId) where T : Control
        {
            if (ctrl != null)
            {
                _osdCatControls.Add(new Tuple<Control, int, int>(ctrl, categoryId, ctrl.Top));
            }
            return ctrl;
        }

        private void ApplyOsdPreset(string presetName, Action applyAction)
        {
            if (applyAction == null) return;
            applyAction();
            SyncOverlayFromStudio();
            SyncOsdStudioUiFromOverlay(false);
            if (_statusBarLabel != null)
            {
                _statusBarLabel.Text = "✓ Applied OSD Preset: " + presetName;
            }
        }

        private void ApplyOsdMetricCategoryFilter(int catId)
        {
            _osdActiveCategory = catId;
            if (_osdMetricsScrollPanel == null) return;
            _osdMetricsScrollPanel.SuspendLayout();
            try
            {
                _osdMetricsScrollPanel.AutoScrollPosition = Point.Empty;
                if (catId == 0)
                {
                    for (int i = 0; i < _osdCatControls.Count; i++)
                    {
                        var entry = _osdCatControls[i];
                        entry.Item1.Top = entry.Item3;
                        entry.Item1.Visible = true;
                    }
                }
                else
                {
                    int minY = int.MaxValue;
                    for (int i = 0; i < _osdCatControls.Count; i++)
                    {
                        var entry = _osdCatControls[i];
                        if (entry.Item2 == catId && entry.Item3 < minY)
                            minY = entry.Item3;
                    }
                    if (minY == int.MaxValue) minY = 0;

                    int maxBottom = 0;
                    for (int i = 0; i < _osdCatControls.Count; i++)
                    {
                        var entry = _osdCatControls[i];
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

                    if (_osdTipLabel != null)
                    {
                        _osdTipLabel.Top = maxBottom + 8;
                        _osdTipLabel.Visible = true;
                    }
                }
            }
            finally
            {
                _osdMetricsScrollPanel.ResumeLayout(true);
            }
            UpdateOsdStudioStyles();
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
            Win11VisualStyle.EnableThemedCheckBox(cb, () => CurrentPalette);
            cb.CheckedChanged += (s, e) =>
            {
                if (_suppressOsdStudioEvents) return;
                onChanged(cb.Checked);
            };
            if (!string.IsNullOrEmpty(tooltip))
                _uiToolTip.SetToolTip(cb, tooltip);
            parent.Controls.Add(cb);
            return cb;
        }


        private void UpdateOsdStudioStyles()
        {
            if (_osdConfigPanel == null) return;
            ThemePalette pal = CurrentPalette;
            Color surface = pal.Surface;
            Color surface2 = pal.Surface2;
            Color text = pal.Text;
            Color textDim = pal.TextDim;
            Color accent = pal.Accent;
            Color border = pal.Border;

            _osdConfigPanel.BackColor = surface;

            // Highlight active Graph Mode button with crisp Win11 pill borders & hover feedback
            Button[] modeBtns = new[] { _osdBtnTextOnly, _osdBtnSparklines, _osdBtnCombinedGraph, _osdBtnBothGraphs, _osdBtnGraphsOnly };
            for (int i = 0; i < modeBtns.Length; i++)
            {
                if (modeBtns[i] == null) continue;
                bool active = (_osd.GraphMode == i);
                Win11VisualStyle.StylePillButton(modeBtns[i], pal, active);
                modeBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Scale button (100%, 125%, 150%, 185%)
            Button[] scaleBtns = new[] { _osdBtnScale100, _osdBtnScale125, _osdBtnScale150, _osdBtnScale185 };
            for (int i = 0; i < scaleBtns.Length; i++)
            {
                if (scaleBtns[i] == null) continue;
                bool active = (_osd.ScaleTier == i);
                Win11VisualStyle.StylePillButton(scaleBtns[i], pal, active);
                scaleBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Layout button
            if (_osdBtnLayoutH != null)
            {
                bool activeH = !_osd.VerticalLayout;
                Win11VisualStyle.StylePillButton(_osdBtnLayoutH, pal, activeH);
                _osdBtnLayoutH.Font = activeH ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnLayoutV != null)
            {
                bool activeV = _osd.VerticalLayout;
                Win11VisualStyle.StylePillButton(_osdBtnLayoutV, pal, activeV);
                _osdBtnLayoutV.Font = activeV ? TabFontBold : TabFontRegular;
            }

            // Highlight active Height button (75px Compact, 105px Medium, 140px Large, 180px XL)
            if (_osdBtnHeightCompact != null)
            {
                bool active = (_osd.GraphHeight <= 85);
                Win11VisualStyle.StylePillButton(_osdBtnHeightCompact, pal, active);
                _osdBtnHeightCompact.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightMedium != null)
            {
                bool active = (_osd.GraphHeight > 85 && _osd.GraphHeight <= 120);
                Win11VisualStyle.StylePillButton(_osdBtnHeightMedium, pal, active);
                _osdBtnHeightMedium.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightLarge != null)
            {
                bool active = (_osd.GraphHeight > 120 && _osd.GraphHeight <= 160);
                Win11VisualStyle.StylePillButton(_osdBtnHeightLarge, pal, active);
                _osdBtnHeightLarge.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightXl != null)
            {
                bool active = (_osd.GraphHeight > 160);
                Win11VisualStyle.StylePillButton(_osdBtnHeightXl, pal, active);
                _osdBtnHeightXl.Font = active ? TabFontBold : TabFontRegular;
            }

            // Style quick snap buttons with crisp borders & hover
            Button[] snapBtns = new[] { _osdQuickPosTl, _osdQuickPosTc, _osdQuickPosTr, _osdQuickPosBl, _osdQuickPosBc, _osdQuickPosBr };
            foreach (Button b in snapBtns)
            {
                if (b != null)
                {
                    Win11VisualStyle.StyleModernButton(b, surface2, text, border, pal.IsDark);
                }
            }

            // Style 1-click OSD Preset buttons & Category sub-tabs
            foreach (Button pBtn in new[] { _osdPresetMinimalBtn, _osdPresetCompactBtn, _osdPresetDfBtn, _osdPresetFullBtn })
            {
                if (pBtn != null)
                {
                    Color pBorder = Win11VisualStyle.Blend(border, accent, pal.IsDark ? 0.35f : 0.25f);
                    Win11VisualStyle.StyleModernButton(pBtn, surface2, text, pBorder, pal.IsDark);
                }
            }
            if (_osdCatButtons != null)
            {
                for (int ci = 0; ci < _osdCatButtons.Length; ci++)
                {
                    if (_osdCatButtons[ci] != null)
                    {
                        bool catActive = (_osdActiveCategory == ci);
                        Win11VisualStyle.StylePillButton(_osdCatButtons[ci], pal, catActive, pal.SecondaryAccent, pal.ActiveFg);
                    }
                }
            }

            // Style quick action buttons (Select All / Deselect All / Defaults)
            foreach (Button actBtn in new[] { _osdSelectAllBtn, _osdDeselectAllBtn, _osdResetDefaultsBtn, _optCardOsdConfigBtn })
            {
                if (actBtn != null)
                {
                    Win11VisualStyle.StyleModernButton(actBtn, surface2, text, border, pal.IsDark);
                }
            }

            // Style quick toggle buttons
            if (_osdToggleOverlayBtn != null)
            {
                Win11VisualStyle.StylePillButton(_osdToggleOverlayBtn, pal, _showOverlay);
            }
            if (_osdToggleLockBtn != null)
            {
                Color lockAccent = pal.IsRadeon ? RadeonOrange : Color.FromArgb(0xEA, 0x58, 0x0C);
                Win11VisualStyle.StylePillButton(_osdToggleLockBtn, pal, _lockOverlay, lockAccent, Color.White);
            }

            if (_osdOpacitySlider != null)
            {
                _osdOpacitySlider.BackColor = surface;
                _osdOpacitySlider.AccentOverride = pal.Accent;
                _osdOpacitySlider.Invalidate();
            }
            if (_osdOpacityPctLabel != null)
            {
                _osdOpacityPctLabel.BackColor = surface;
                _osdOpacityPctLabel.ForeColor = text;
            }

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
            if (_osdMetricsScrollPanel != null)
            {
                _osdMetricsScrollPanel.BackColor = surface;
                _osdMetricsScrollPanel.ForeColor = text;
                Win11VisualStyle.ApplyDarkExplorerTheme(_osdMetricsScrollPanel, pal.IsDark);
            }
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
                _osdShowPowerLimitCb, _osdShowFclkSyncCb, _osdShowFrametimeMsCb, _osdShowFpsAvgCb, _osdShowLatencyCb
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
            if (_osdTrackerModeCombo != null)
            {
                Win11VisualStyle.ApplyDarkComboTheme(_osdTrackerModeCombo, pal);
            }
        }

    }
}
