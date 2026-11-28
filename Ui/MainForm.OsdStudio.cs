using System;
using System.Drawing;
using System.Windows.Forms;

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
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlay;
                else SetOverlayLocked(!_lockOverlay);
            };
            _overlayForm.RequestOpenOsdConfig += (s, e) => OpenOsdConfigTab();
            _overlayForm.RequestSnapLocation += (s, corner) => SetOverlayCornerPosition(corner);
            _overlayForm.RequestSamplingInterval += (s, ms) => SetRefreshInterval(ms);

            // Pre-seed live telemetry into overlay before first display so dimensions account for all active rows
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

        private OsdStudioControl CreateOsdStudioControl()
        {
            OsdStudioControl studio = new OsdStudioControl(
                _osd,
                () => CurrentPalette,
                _uiToolTip,
                () => _hasDualFans,
                _showOverlay,
                _lockOverlay);
            studio.ConfigChanged += (s, e) =>
            {
                SyncOverlayFromStudio();
                SyncOsdStudioUiFromOverlay(false);
            };
            studio.OpacityPreviewChanged += (s, pct) =>
            {
                if (_overlayForm != null) _overlayForm.ApplyOpacity(pct);
            };
            studio.OpacityCommitted += (s, pct) =>
            {
                if (_overlayForm != null) _overlayForm.ApplyOpacity(pct);
                SaveSettings();
            };
            studio.SnapCornerRequested += (s, corner) => SetOverlayCornerPosition(corner);
            studio.ToggleOverlayRequested += (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlay;
                else SetOverlayVisible(!_showOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            studio.ToggleLockRequested += (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlay;
                else SetOverlayLocked(!_lockOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            studio.StatusMessageRequested += (s, msg) =>
            {
                if (_statusBarLabel != null && !string.IsNullOrEmpty(msg))
                    _statusBarLabel.Text = msg;
            };
            return studio;
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

            if (_osdConfigPanel != null)
            {
                bool hasNpu = _demoMode || _lastStatus.NpuActivityPct >= 0 || AmdAdlGpuSensor.HasNpuHardware;
                bool hasHotspot = _demoMode || _lastStatus.GpuHotspotTemp > 0;
                _osdConfigPanel.SyncFromConfig(_showOverlay, _lockOverlay, _hasDualFans, hasNpu, hasHotspot);
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
                WindowState = FormWindowState.Normal;
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
                    if (_perfCanvas != null) { _perfCanvas.Visible = true; _perfCanvas.Invalidate(); }
                }
                else if (_showingLiveTelemetry)
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = false;
                    if (_perfCanvas != null) _perfCanvas.Visible = false;
                    if (_liveCanvas != null) { _liveCanvas.Visible = true; _liveCanvas.Invalidate(); }
                }
                else
                {
                    if (_liveCanvas != null) _liveCanvas.Visible = false;
                    if (_perfCanvas != null) _perfCanvas.Visible = false;
                    if (_curveCanvas != null) { _curveCanvas.Visible = true; _curveCanvas.Invalidate(); }
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
            UpdateOsdStudioStyles();
            SaveSettings();
            RebuildDriversMenu();
        }

        internal void SafeDisposeContextMenuDeferred(ContextMenuStrip oldMenu)
        {
            if (oldMenu == null || oldMenu.IsDisposed) return;

            // Close first while the handle is still alive so WinForms' ToolStripManager.ModalMenuFilter
            // cleanly pops this dropdown (and any open submenus) off its active modal stack.
            try
            {
                if (oldMenu.Visible)
                    oldMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
            }
            catch { }

            // Defer Dispose() past the current WM_LBUTTONUP / PreFilterMessage / Item.Click dispatch so
            // ModalMenuFilter can never touch a disposed ToolStripDropDown handle (ObjectDisposedException).
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        try
                        {
                            if (!oldMenu.IsDisposed)
                            {
                                if (oldMenu.Visible) oldMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
                                oldMenu.Dispose();
                            }
                        }
                        catch { }
                    });
                    return;
                }
                catch { }
            }

            try
            {
                if (!oldMenu.IsDisposed) oldMenu.Dispose();
            }
            catch { }
        }

        private void RebuildDriversMenu()
        {
            ContextMenuStrip oldMenu = _driversMenu;
            _driversMenu = CreateDriversAndToolsMenu();
            SafeDisposeContextMenuDeferred(oldMenu);
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

        private void UpdateOsdStudioStyles()
        {
            ThemePalette pal = CurrentPalette;
            if (_optCardOsdConfigBtn != null)
            {
                Win11VisualStyle.StyleModernButton(_optCardOsdConfigBtn, pal.Surface2, pal.Text, pal.Border, pal.IsDark);
            }
            if (_osdConfigPanel != null)
            {
                _osdConfigPanel.ApplyTheme(pal, _showOverlay, _lockOverlay);
            }
        }
    }
}
