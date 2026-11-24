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
                string json = HardwareProfileSubmission.BuildSanitizedJson(_sysInfo, _hw, APP_VERSION);
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string outPath = Path.Combine(baseDir, "ec-probe-report.txt");
                string jsonPath = Path.Combine(baseDir, "hardware-profile-submission.json");
                File.WriteAllText(outPath, report, Encoding.UTF8);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                string combinedClipboard = "```json\n" + json + "\n```\n\n" + report;
                try
                {
                    Clipboard.SetText(combinedClipboard);
                }
                catch { }

                string url = HardwareProfileSubmission.BuildGitHubIssueUrl(_sysInfo, _hw, APP_VERSION);
                OpenUrlSafe(url);

                MessageBox.Show(this,
                    "Your sanitized hardware profile JSON & probe report have been copied to your clipboard and saved to:\n" +
                    "• " + jsonPath + "\n• " + outPath +
                    "\n\nOpening a pre-filled GitHub Issue in your browser — review it and paste (Ctrl+V) the full EC report if needed!",
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
                RebuildDriversMenu();
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
            if (IsReadOnlyMonitoringMode)
            {
                MessageBox.Show(this,
                    "Fan calibration is disabled in Safe Read-Only Monitoring Mode because this hardware is unverified.\n\n" +
                    "MiniFanControl safely monitors your sensors without writing to unknown EC or Super I/O registers.\n" +
                    "To test calibration safely on this PC, enable 'Offline Demo / Simulation Mode' in Tools & Drivers.",
                    "MiniFanControl — Safe Read-Only Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
            using (SetupAssistantDialog dlg = new SetupAssistantDialog(this, _isDarkMode, _themePref, _showSetupAssistant, IsReadOnlyMonitoringMode))
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
                if (_controller != null) _controller.ClearStaleWriteErrors();
                UpdateReadOnlyUiState();
                SaveSettings();
                RebuildDriversMenu();
                _statusBarLabel.Text = _demoMode
                    ? "ℹ Offline Demo Mode active (synthetic telemetry)"
                    : (IsReadOnlyMonitoringMode
                        ? "👁️ Safe Read-Only Monitoring Mode active (unverified hardware)"
                        : "✓ Hardware Mode active (direct EC registers)");
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
            ToolStripMenuItem acPauseItem = new ToolStripMenuItem("🎮 Auto-Pause for Anti-Cheat (EAC / BattlEye)", null, (s, e) =>
            {
                _settings.AutoPauseForAntiCheat = !_settings.AutoPauseForAntiCheat;
                _antiCheatMonitor.Enabled = _settings.AutoPauseForAntiCheat;
                SaveSettings();
                RebuildDriversMenu();
                if (_statusBarLabel != null)
                {
                    _statusBarLabel.Text = _settings.AutoPauseForAntiCheat
                        ? "✓ Anti-Cheat Safe Auto-Pause enabled (restores BIOS Auto & unloads legacy driver during EAC/BattlEye)"
                        : "✓ Anti-Cheat Safe Auto-Pause disabled";
                }
            });
            acPauseItem.Checked = _settings.AutoPauseForAntiCheat;
            menu.Items.Add(acPauseItem);
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
            bool pawnSvc = PawnIoDriver.IsServiceInstalledAndRunning();
            bool vanguardDetected = DriverServiceManager.IsDriverServiceRunning(AntiCheatMonitor.BootVanguardServiceName);
            string drv = NativeInpOut.ActiveDriverName;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl Kernel Driver, PawnIO & HVCI Security Status\n");
            sb.AppendLine("• Active Kernel Driver Engine: " + drv);
            sb.AppendLine("• Binary SHA256 / Signature Check: " + (NativeInpOut.IsDriverHashVerified
                ? ("VERIFIED (" + (NativeInpOut.ActiveDriverSha256 ?? "") + ")")
                : "Not Verified / Not Loaded"));
            sb.AppendLine("• Windows 11 Memory Integrity (HVCI): " + (hvci ? "ENABLED (Strict Kernel Code Integrity)" : "Disabled"));
            sb.AppendLine("• PawnIO HVCI Driver Service: " + (pawnSvc ? ("Running (" + PawnIoDriver.LastStatus + ")") : "Not Installed (optional: https://pawnio.eu)"));
            sb.AppendLine("• Anti-Cheat Safe Auto-Pause (EAC/BattlEye): " + (_settings.AutoPauseForAntiCheat ? "ENABLED" : "Disabled (opt-in via Tools & Drivers)"));
            if (vanguardDetected)
                sb.AppendLine("• Boot Anti-Cheat Notice: Riot Vanguard (vgk) is active at boot — PawnIO is recommended if legacy drivers conflict.");
            sb.AppendLine();
            if (NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO)
            {
                sb.AppendLine("STATUS: PawnIO Active (HVCI / Memory Integrity Compatible)");
                sb.AppendLine("EV-signed PawnIO bytecode modules are active. You can keep Windows 11 Core Isolation / Memory Integrity enabled.");
            }
            else if (NativeInpOut.ActiveDriver == HardwareDriverEngine.InpOut64)
            {
                sb.AppendLine("STATUS: InpOut64 Active (inpoutx64.dll)");
                sb.AppendLine("Standard InpOut64 driver passed runtime SHA256 pin verification before loading. Note that with Windows 11 Memory Integrity (HVCI) enabled, Microsoft's vulnerable driver blocklist may block InpOut64 service startup.");
                if (hvci) sb.AppendLine("HVCI is enabled: Install PawnIO (https://pawnio.eu) for signed HVCI support, or run in Safe Read-Only Mode.");
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

    }
}
