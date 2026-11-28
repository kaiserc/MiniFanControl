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
            return CurveProfileCatalog.ResolveRyzenTierBadge(_sysInfo);
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
                _hwSubLabel.Text = string.Format("EC: {0} • {1}", customProf.DisplayName, drvInfo);
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

            using (ThemedInfoDialog dlg = new ThemedInfoDialog(
                "MiniFanControl — Hardware & Multi-Model Compatibility",
                "Hardware & Multi-Model Compatibility",
                string.Format("{0} ({1}) • Active Map: {2}", _sysInfo.ProductName, _sysInfo.CpuName, _sysInfo.MatchedProfile.DisplayName),
                sb.ToString(),
                _themePref,
                _isDarkMode,
                640,
                470))
            {
                dlg.ShowDialog(this);
            }
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

            using (ThemedInfoDialog dlg = new ThemedInfoDialog(
                "About MiniFanControl v" + APP_VERSION,
                "MiniFanControl v" + APP_VERSION + " (64-bit Native EC Edition)",
                "Standalone Embedded Controller Fan Curve & Thermal Utility for AMD Ryzen Mini PCs",
                sb.ToString(),
                _themePref,
                _isDarkMode,
                650,
                500))
            {
                dlg.ShowDialog(this);
            }
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
                    },
                    _themePref))
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
                const int row2Y = 119;
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

        private static ToolStripMenuItem AddToolMenuItem(ContextMenuStrip menu, string text, string tooltip, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, onClick);
            if (!string.IsNullOrEmpty(tooltip))
            {
                item.ToolTipText = tooltip;
                item.AutoToolTip = false;
            }
            menu.Items.Add(item);
            return item;
        }

        private ContextMenuStrip CreateDriversAndToolsMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowItemToolTips = true;
            bool svcInstalled = !_isUnitTest && WindowsServiceHost.IsServiceInstalled();

            AddToolMenuItem(menu,
                "✨ First-Time Setup Assistant & Auto-Tune Guide...",
                "Opens the 2-step guided setup to calibrate your fan spin-up floor and generate an Auto-Tuned curve.",
                (s, e) => ShowSetupAssistantDialog(true));
            menu.Items.Add(new ToolStripSeparator());

            string calMenuLabel = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Calibrate Fans (Not Yet Calibrated — Default 30% Min)..."
                : string.Format("Calibrate Fans (✓ Last: {0} • Min {1}%)...",
                    FanCurveProfile.LastCalibratedDate, FanCurveProfile.ApuMinSpinPct);
            AddToolMenuItem(menu,
                calMenuLabel,
                "Measures each fan's real minimum spin-up PWM floor (+2% safety margin) and max RPM so curves never stall blower motors.",
                (s, e) => RunGuiFanCalibration());

            string layoutLabel = _hasDualFans
                ? "Fan Layout: 2 Fans (APU + Sys) — Click for 1-Fan Mode"
                : "Fan Layout: 1 Fan (APU Only) — Click for 2-Fan Mode";
            AddToolMenuItem(menu,
                layoutLabel,
                "Switch between dual-fan layout (APU cooler + RAM/NVMe Sys fan) and single-fan Mini PC layout.",
                (s, e) => ToggleDualFanMode());
            menu.Items.Add(new ToolStripSeparator());

            string osdToggleLabel = _showOverlay ? "Hide In-Game OSD Overlay" : "Show In-Game OSD Overlay";
            AddToolMenuItem(menu,
                osdToggleLabel,
                "Show or hide the hook-free floating In-Game OSD overlay (hotkey: Ctrl+Alt+F9).",
                (s, e) =>
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                });

            string osdStyleLabel = _osd.VerticalLayout
                ? "OSD Layout: Vertical Stack — Click for Horizontal Bar"
                : "OSD Layout: Horizontal Bar — Click for Vertical Stack";
            AddToolMenuItem(menu,
                osdStyleLabel,
                "Toggle the In-Game OSD between a compact Vertical Stack and a wide top/bottom Horizontal Bar.",
                (s, e) => ToggleOverlayLayoutStyle());

            string osdLockLabel = _lockOverlay
                ? "Unlock OSD Overlay (Allow Mouse Drag)"
                : "Lock OSD Overlay (Click-Through Mode)";
            AddToolMenuItem(menu,
                osdLockLabel,
                "When locked, mouse clicks pass straight through the OSD to your game. Unlock to reposition it by dragging.",
                (s, e) =>
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                });

            AddToolMenuItem(menu,
                "Configure OSD Overlay Studio...",
                "Jump to the OSD Studio tab to choose visible sensors, graph modes, scale tier (100%–185%), opacity, and quick snaps.",
                (s, e) => OpenOsdConfigTab());
            menu.Items.Add(new ToolStripSeparator());

            string resetPeaksLabel = _sessionStats.SampleCount > 0
                ? string.Format("Reset Session Peaks (Max CPU {0}°C • iGPU {1}°C • NVMe {2}°C • {3}W)",
                    _sessionStats.MaxCpuTemp, _sessionStats.MaxGpuTemp, _sessionStats.MaxNvmeTemp, _sessionStats.MaxPowerWatts)
                : "Reset Session Peak (Max °C / W) Stats";
            AddToolMenuItem(menu,
                resetPeaksLabel,
                "Clears recorded session Min / Avg / Peak temperatures, power, FPS lows, and RPM stats.",
                (s, e) => ResetSessionPeaks());

            string exportCsvLabel = string.Format("📊 Export Session Telemetry to CSV ({0} samples logged)...", _sessionStats.LoggedRecordCount);
            AddToolMenuItem(menu,
                exportCsvLabel,
                "Saves all recorded live telemetry samples from this session to a CSV file for Excel or benchmark analysis.",
                (s, e) => ExportSessionTelemetryToCsv());

            AddToolMenuItem(menu,
                "✨ Auto-Tune Optimal Fan Curve (from Telemetry & Calibration)...",
                "Synthesizes a quiet 5-point fan curve around your calibrated spin floor and current BIOS TDP envelope.",
                (s, e) => RunAutoTuneCurve());

            AddToolMenuItem(menu,
                "📋 Copy Active Fan Curve to Clipboard (Share Curve)...",
                "Copies your active APU & Sys fan curve points to the clipboard so you can back them up or share them.",
                (s, e) => CopyCurveToClipboard());

            AddToolMenuItem(menu,
                "📥 Import Fan Curve from Clipboard...",
                "Loads a shared MiniFanControl fan curve from your clipboard.",
                (s, e) => ImportCurveFromClipboard());

            string safeExitLabel = string.Format("Exit Safety: Restore BIOS Auto on Close ({0})",
                _restoreAutoOnExit ? "✓ Enabled" : "Disabled (Keep Custom PWM)");
            string safeExitTip = svcInstalled
                ? "Applies when fully closing MiniFanControl WITHOUT Background Service Mode.\r\n" +
                  "• Background Service Mode is INSTALLED, so closing the GUI hands\r\n  control back to the low-RAM service instead of resetting to BIOS Auto.\r\n" +
                  "• If Service Mode is removed: Enabled restores safe BIOS Auto on exit."
                : "Controls what happens when MiniFanControl completely exits (not minimized).\r\n" +
                  "• Enabled (Recommended): Restores safe BIOS Auto fan control on exit/crash.\r\n" +
                  "• Disabled: Leaves your last manual/curve PWM active after exit.";
            AddToolMenuItem(menu, safeExitLabel, safeExitTip, (s, e) =>
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
            string closeToTrayTip = "Controls what clicking the window 'X' button does (same setting as the Options checkbox).\r\n• Enabled: Hides the full GUI into the system tray so tray temps, OSD, and curves stay live.\r\n• Disabled: Fully exits the GUI (hands off to Background Service Mode if installed, otherwise follows Exit Safety).";
            AddToolMenuItem(menu, closeToTrayLabel, closeToTrayTip, (s, e) =>
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
            AddToolMenuItem(menu,
                silentSleepLabel,
                "Controls fan behavior when Windows enters Sleep / Modern Standby.\r\n• Silent Zero-RPM: Gently coasts fans down without a noisy 2–4s BIOS fan burst.\r\n• BIOS Auto: Hands fans straight back to motherboard firmware during sleep.",
                (s, e) =>
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
            AddToolMenuItem(menu,
                hotkeysLabel,
                "Enables system-wide shortcuts while gaming: Ctrl+Alt+F9 (OSD), F10 (Balanced), F11 (Performance), F12 (Silent / Reset Auto).",
                (s, e) =>
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
            AddToolMenuItem(menu,
                demoLabel,
                "Runs synthetic sensors and simulated fans without touching real hardware — useful for testing curves, OSD, and themes safely.",
                (s, e) =>
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
            profileSubMenu.ToolTipText = "View or override the detected Mini PC hardware profile and EC/Super I/O register map (Auto-Detect is recommended).";
            profileSubMenu.AutoToolTip = false;
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

            AddToolMenuItem(menu,
                "Export Hardware && EC Probe Report (for HX 370 / New PCs)...",
                "Saves a full 256-byte EC/Super I/O diagnostic dump (ec-probe-report.txt) for troubleshooting or verifying new Mini PCs.",
                (s, e) => ExportHardwareProbeReport());

            AddToolMenuItem(menu,
                "🔬 EC Motherboard Telemetry && Differential Scanner (Tier 2 Discovery)...",
                "Interactive scanner that watches EC registers change under load to discover fan/sensor offsets on new Mini PC models.",
                (s, e) => ShowEcDifferentialScannerDialog());

            AddToolMenuItem(menu,
                "🚀 Submit Hardware Profile to GitHub (1-Click)...",
                "Generates a PII-redacted hardware JSON report and opens a pre-filled GitHub Issue to request native support for your Mini PC.",
                (s, e) => SubmitHardwareProfileOnline());

            AddToolMenuItem(menu,
                "Detected Hardware && Multi-Model Info...",
                "Displays your detected motherboard DMI info, active EC/Super I/O controller, and supported Ryzen Mini PC models.",
                (s, e) => ShowHardwareCompatibilityDialog());

            menu.Items.Add(new ToolStripSeparator());
            AddToolMenuItem(menu,
                "GMKtec Official Drivers && Manuals (K12 / Mini PCs)...",
                "Opens GMKtec's official driver and BIOS downloads page in your web browser.",
                (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));

            AddToolMenuItem(menu,
                "AMD Software: Adrenalin && Chipset Drivers...",
                "Opens AMD's official Adrenalin & Ryzen chipset driver downloads page in your web browser.",
                (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));

            menu.Items.Add(new ToolStripSeparator());
            AddToolMenuItem(menu,
                "🛡️ Kernel Driver && HVCI Security Status...",
                "Inspect active driver (PawnIO HVCI-safe / InpOut64 / Read-Only), SHA256 pin verification, and EC mutex status.",
                (s, e) => ShowKernelDriverSecurityDialog());

            string svcMenuText = svcInstalled
                ? (_servicePausedByGui || _serviceResumeOnExit
                    ? "⚙ Background Service Mode (Active — Resumes on Close)..."
                    : "⚙ Background Service Mode (Installed)...")
                : "⚙ Enable Background Service Mode (Zero-UI / Boot Autostart)...";
            ToolStripMenuItem svcModeItem = AddToolMenuItem(menu,
                svcMenuText,
                "Runs your saved curves headlessly as a Windows Service (~3–5 MB RAM,\r\n" +
                "0% CPU) starting at Windows boot.\r\n" +
                "• Unlike 'Minimize to Tray', you can fully close the GUI and curves keep running.\r\n" +
                "• Opening the GUI pauses the service; closing it hands control right back.",
                (s, e) => ToggleBackgroundServiceMode());
            svcModeItem.Checked = svcInstalled;

            ToolStripMenuItem acPauseItem = AddToolMenuItem(menu,
                "🎮 Auto-Pause for Anti-Cheat (EAC / BattlEye)",
                "Automatically restores BIOS Auto (and unloads legacy non-PawnIO drivers) whenever Easy Anti-Cheat or BattlEye is running, then resumes curves on exit.",
                (s, e) =>
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

            ToolStripMenuItem autoWorkloadItem = AddToolMenuItem(menu,
                "🤖 Auto-Switch Profiles for Games && AI Workloads",
                "Automatically switches to Performance mode when games launch and AI Workload mode during local LLM / NPU activity, then returns cleanly after cooldown.",
                (s, e) =>
                {
                    _settings.AutoSwitchWorkloads = !_settings.AutoSwitchWorkloads;
                    _workloadSwitcher.Enabled = _settings.AutoSwitchWorkloads;
                    SaveSettings();
                    RebuildDriversMenu();
                    if (_statusBarLabel != null)
                    {
                        _statusBarLabel.Text = _settings.AutoSwitchWorkloads
                            ? "✓ Auto-Switch Profiles enabled (switches to Performance for games & AI Workload for LLMs/NPU)"
                            : "✓ Auto-Switch Profiles disabled";
                    }
                });
            autoWorkloadItem.Checked = _settings.AutoSwitchWorkloads;

            ToolStripMenuItem langMenu = new ToolStripMenuItem("🌐 Language / 语言 / Sprache");
            langMenu.ToolTipText = "Choose the UI language (supports Auto, English, Chinese, German, Spanish, French, Japanese, and custom lang/*.json files).";
            langMenu.AutoToolTip = false;
            AddLanguageMenuItem(langMenu, "Auto (System Default)", AppLanguage.Auto);
            AddLanguageMenuItem(langMenu, "English", AppLanguage.English);
            AddLanguageMenuItem(langMenu, "简体中文 (Chinese Simplified)", AppLanguage.Chinese);
            AddLanguageMenuItem(langMenu, "Deutsch (German)", AppLanguage.German);
            AddLanguageMenuItem(langMenu, "Español (Spanish)", AppLanguage.Spanish);
            AddLanguageMenuItem(langMenu, "Français (French)", AppLanguage.French);
            AddLanguageMenuItem(langMenu, "日本語 (Japanese)", AppLanguage.Japanese);
            menu.Items.Add(langMenu);
            menu.Items.Add(new ToolStripSeparator());
            AddToolMenuItem(menu,
                "↻ Check for Updates (GitHub Releases)...",
                "Manually checks GitHub Releases for a newer version of MiniFanControl (never phones home automatically).",
                (s, e) => CheckForUpdatesAsync());
            AddToolMenuItem(menu,
                "About MiniFanControl v" + APP_VERSION + "...",
                "Version info, active hardware profile, driver status, and project links.",
                (s, e) => ShowAboutDialog());
            Win11VisualStyle.ApplyThemeToMenu(menu, CurrentPalette);
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

        private void CheckForUpdatesAsync()
        {
            if (_isUnitTest) return;
            if (_statusBarLabel != null)
                _statusBarLabel.Text = "Checking GitHub Releases for updates...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = UpdateChecker.CheckLatestRelease(APP_VERSION);
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed) return;
                        if (!result.Success)
                        {
                            if (_statusBarLabel != null)
                                _statusBarLabel.Text = "Update check unavailable (offline or GitHub API limit).";
                            string errBody = string.Format(
                                "Installed Version: v{0}\n" +
                                "Status:            Unable to query GitHub Releases\n\n" +
                                "Details:\n{1}\n\n" +
                                "You can always check manually at:\n{2}",
                                APP_VERSION, result.ErrorMessage, UpdateChecker.LatestReleasePageUrl);
                            using (ThemedInfoDialog dlg = new ThemedInfoDialog(
                                "Check for Updates",
                                "Update Check Unavailable",
                                "On-demand GitHub Releases check (no background telemetry)",
                                errBody,
                                _themePref,
                                _isDarkMode,
                                560,
                                320,
                                "Open Releases Page",
                                delegate { OpenUrlSafe(UpdateChecker.LatestReleasePageUrl); }))
                            {
                                dlg.ShowDialog(this);
                            }
                            return;
                        }

                        if (result.UpdateAvailable)
                        {
                            if (_statusBarLabel != null)
                                _statusBarLabel.Text = string.Format("✓ Update available: v{0} (installed: v{1})", result.LatestVersion, APP_VERSION);
                            string body = string.Format(
                                "Installed Version: v{0}\n" +
                                "Latest Release:    v{1} ({2})\n" +
                                "Published:         {3}\n" +
                                "Download URL:      {4}\n\n" +
                                "Release Highlights:\n{5}",
                                APP_VERSION,
                                result.LatestVersion,
                                result.ReleaseName,
                                string.IsNullOrEmpty(result.PublishedAt) ? "—" : result.PublishedAt,
                                result.ReleaseUrl,
                                string.IsNullOrEmpty(result.ReleaseNotesExcerpt) ? "(See GitHub Releases for full changelog.)" : result.ReleaseNotesExcerpt);
                            using (ThemedInfoDialog dlg = new ThemedInfoDialog(
                                "Update Available — MiniFanControl",
                                "MiniFanControl v" + result.LatestVersion + " Is Available",
                                "You are running v" + APP_VERSION + " • Signed SLSA provenance available on GitHub",
                                body,
                                _themePref,
                                _isDarkMode,
                                620,
                                430,
                                "Open Release Page",
                                delegate { OpenUrlSafe(result.ReleaseUrl); }))
                            {
                                dlg.ShowDialog(this);
                            }
                        }
                        else
                        {
                            if (_statusBarLabel != null)
                                _statusBarLabel.Text = string.Format("✓ MiniFanControl v{0} is up to date", APP_VERSION);
                            string body = string.Format(
                                "Installed Version: v{0}\n" +
                                "Latest Release:    v{1}\n" +
                                "Status:            You are running the latest release!\n\n" +
                                "Releases Page:     {2}",
                                APP_VERSION,
                                result.LatestVersion,
                                result.ReleaseUrl);
                            using (ThemedInfoDialog dlg = new ThemedInfoDialog(
                                "Check for Updates",
                                "You're Up to Date",
                                "MiniFanControl v" + APP_VERSION + " matches or exceeds latest release v" + result.LatestVersion,
                                body,
                                _themePref,
                                _isDarkMode,
                                560,
                                290,
                                "View Releases",
                                delegate { OpenUrlSafe(result.ReleaseUrl); }))
                            {
                                dlg.ShowDialog(this);
                            }
                        }
                    });
                }
                catch { }
            });
        }

        private void ShowKernelDriverSecurityDialog(bool isStartupPrompt = false)
        {
            if (_isUnitTest) return;
            using (KernelDriverDialog dlg = new KernelDriverDialog(
                _isDarkMode,
                _themePref,
                isStartupPrompt,
                _settings.AutoPauseForAntiCheat,
                _settings.ShowDriverInstallPrompt,
                delegate
                {
                    bool ok = NativeInpOut.TryReloadDriver(true);
                    if (ok && NativeInpOut.IsDriverOpen())
                    {
                        if (_hw != null) _hw.ResetDriverInitialization();
                        _uiWriteErrorBannerDetail = null;
                        _uiWriteErrorFailCount = 0;
                        if (_controller != null) _controller.ClearStaleWriteErrors();
                        if (_errorBanner != null)
                        {
                            _errorBanner.Visible = false;
                            _errorBanner.Cursor = Cursors.Default;
                        }
                        UpdateReadOnlyUiState();
                        RefreshStatus();
                    }
                    return ok;
                }))
            {
                dlg.ShowDialog(this);
                if (isStartupPrompt)
                {
                    _settings.ShowDriverInstallPrompt = dlg.ShowPromptOnStartup;
                    SaveSettings();
                }
                if (dlg.DriverActivatedDuringSession)
                {
                    UpdateReadOnlyUiState();
                    RefreshStatus();
                }
            }
        }

        private void ToggleBackgroundServiceMode()
        {
            if (_isUnitTest) return;
            bool installed = WindowsServiceHost.IsServiceInstalled();
            string helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "minifan-helper.exe");

            if (!installed)
            {
                if (!File.Exists(helperPath))
                {
                    MessageBox.Show(this,
                        "Could not locate minifan-helper.exe next to MiniFanControl.exe.\n\nKeep minifan-helper.exe in the same folder to enable Background Service Mode.",
                        "Background Service Mode",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string cfgPath = AppSettings.ResolveServiceConfigPath(true);
                DialogResult dr = MessageBox.Show(this,
                    "Enable Zero-UI Background Service Mode?\n\n" +
                    "• Starts automatically at Windows boot (before login — ideal for headless Mini PCs, servers & HTPCs)\n" +
                    "• Runs with minimal background RAM (~5-10 MB) and zero tray icon clutter\n" +
                    "• Shares your fan curves & profiles via:\n  " + cfgPath + "\n" +
                    "• Whenever you open MiniFanControl to adjust curves, the service pauses automatically and resumes seamlessly when you close the app.\n\n" +
                    "Install MiniFanControlService now?",
                    "Background Service Mode (Zero-UI Autostart)",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                SaveSettings();
                AppSettings.SyncConfigToServicePath(AppSettings.GetSettingsPath());

                string msg;
                bool ok = WindowsServiceHost.InstallService(helperPath, cfgPath, out msg);
                if (!ok)
                {
                    MessageBox.Show(this,
                        "Failed to install Background Service:\n\n" + msg + "\n\nEnsure MiniFanControl is running as Administrator.",
                        "Background Service Mode",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                _serviceResumeOnExit = true;
                RebuildDriversMenu();
                if (_statusBarLabel != null)
                {
                    _statusBarLabel.Text = "✓ Background Service installed — close GUI whenever you're ready to hand control to the service";
                }

                DialogResult closeNow = MessageBox.Show(this,
                    "Background Service Mode is now installed!\n\n" +
                    "Would you like to close the GUI right now and hand fan control over to the low-RAM background service immediately?\n\n" +
                    "(Click 'No' to keep tweaking curves — the service will start automatically as soon as you close MiniFanControl.)",
                    "Background Service Ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (closeNow == DialogResult.Yes)
                {
                    _forceFullExit = true;
                    Close();
                }
            }
            else
            {
                string stateDesc = WindowsServiceHost.IsServiceRunning()
                    ? "Running"
                    : (_servicePausedByGui || _serviceResumeOnExit ? "Paused while GUI is open (resumes on close)" : "Installed (Stopped)");
                DialogResult dr = MessageBox.Show(this,
                    "MiniFanControlService is currently installed.\n" +
                    "Status: " + stateDesc + "\n" +
                    "Shared Config: " + AppSettings.ResolveServiceConfigPath(false) + "\n\n" +
                    "Do you want to STOP and UNINSTALL Background Service Mode?",
                    "Background Service Mode",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;

                string msg;
                bool ok = WindowsServiceHost.UninstallService(out msg);
                if (ok)
                {
                    _servicePausedByGui = false;
                    _serviceResumeOnExit = false;
                    RebuildDriversMenu();
                    if (_statusBarLabel != null)
                    {
                        _statusBarLabel.Text = "✓ Background Service uninstalled (interactive GUI mode active)";
                    }
                    MessageBox.Show(this, msg, "Background Service Mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this,
                        "Failed to uninstall Background Service:\n\n" + msg + "\n\nEnsure MiniFanControl is running as Administrator.",
                        "Background Service Mode",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
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

        private int GetLiveActivePowerModeCode()
        {
            if (!IsReadOnlyMonitoringMode && IsUsingWindowsPowerMode())
            {
                int winCode = WindowsPowerMode.GetActiveModeCode();
                if (winCode >= 0 && winCode <= 2)
                {
                    _lastActiveBiosModeCode = winCode;
                    return winCode;
                }
            }
            if (_lastActiveBiosModeCode >= 0 && _lastActiveBiosModeCode <= 2)
                return _lastActiveBiosModeCode;
            if (_controller != null && _controller.BiosPowerModeOverride >= 0 && _controller.BiosPowerModeOverride <= 2)
                return _controller.BiosPowerModeOverride;
            return 0;
        }

        private ContextMenuStrip CreateTrayContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowItemToolTips = true;
            menu.Items.Add("Show Window", null, (s, e) => RestoreFromTray());
            ToolStripMenuItem osdShowItem = new ToolStripMenuItem(
                _showOverlay ? "Hide In-Game OSD Overlay" : "Show In-Game OSD Overlay",
                null,
                (s, e) =>
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                });
            menu.Items.Add(osdShowItem);
            ToolStripMenuItem osdLockItem = new ToolStripMenuItem(
                _lockOverlay ? "Unlock OSD Overlay (Allow Drag)" : "Lock OSD Overlay (Click-Through)",
                null,
                (s, e) =>
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                });
            menu.Items.Add(osdLockItem);

            bool readOnlyMonitor = IsReadOnlyMonitoringMode;
            bool usesWinPower = !readOnlyMonitor && IsUsingWindowsPowerMode();
            bool showPowerMenu = !readOnlyMonitor && (_demoMode || _hw == null || !_hw.IsSuperIoControlled || usesWinPower);
            ToolStripMenuItem powerSub = null;
            ToolStripMenuItem pSilent = null;
            ToolStripMenuItem pBal = null;
            ToolStripMenuItem pPerf = null;
            if (showPowerMenu)
            {
                menu.Items.Add(new ToolStripSeparator());
                int activePower = GetLiveActivePowerModeCode();
                string powerRootTitle = usesWinPower ? "Power Mode (Windows)" : "BIOS Power Mode (TDP)";
                powerSub = new ToolStripMenuItem(powerRootTitle);
                powerSub.ToolTipText = GetPowerBarTooltip(usesWinPower);
                powerSub.AutoToolTip = false;

                pSilent = new ToolStripMenuItem(
                    usesWinPower ? "🌙 Silent (Best Power Efficiency)" : "🌙 Silent (~35W TDP)",
                    null, (s, e) => ApplyBiosPowerMode(2));
                pSilent.Checked = (activePower == 2);
                powerSub.DropDownItems.Add(pSilent);

                pBal = new ToolStripMenuItem(
                    usesWinPower ? "⚖ Balanced" : "⚖ Balanced (~48W TDP)",
                    null, (s, e) => ApplyBiosPowerMode(0));
                pBal.Checked = (activePower == 0);
                powerSub.DropDownItems.Add(pBal);

                pPerf = new ToolStripMenuItem(
                    usesWinPower ? "⚡ Performance (Best Performance)" : "⚡ Performance (~60W+ TDP)",
                    null, (s, e) => ApplyBiosPowerMode(1));
                pPerf.Checked = (activePower == 1);
                powerSub.DropDownItems.Add(pPerf);

                menu.Items.Add(powerSub);
            }

            menu.Items.Add(new ToolStripSeparator());
            string curApuMode = (_apuProfile != null) ? _apuProfile.Name : "";
            ToolStripMenuItem mBal = new ToolStripMenuItem("Fan Curve: Balanced", null, (s, e) => ApplyMode(0));
            mBal.Checked = curApuMode.Equals("Balanced", StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(mBal);
            ToolStripMenuItem mPerf = new ToolStripMenuItem("Fan Curve: Performance", null, (s, e) => ApplyMode(1));
            mPerf.Checked = curApuMode.Equals("Performance", StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(mPerf);
            ToolStripMenuItem mSilent = new ToolStripMenuItem("Fan Curve: Silent", null, (s, e) => ApplyMode(2));
            mSilent.Checked = curApuMode.Equals("Silent", StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(mSilent);
            ToolStripMenuItem mAi = new ToolStripMenuItem("Fan Curve: AI Workload", null, (s, e) => ApplyMode(3));
            mAi.Checked = curApuMode.Equals("AI Workload", StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(mAi);
            List<KeyValuePair<ToolStripMenuItem, string>> customItems = new List<KeyValuePair<ToolStripMenuItem, string>>();
            if (_customProfiles.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                foreach (FanCurveProfile cp in _customProfiles)
                {
                    FanCurveProfile captured = cp;
                    ToolStripMenuItem cItem = new ToolStripMenuItem("Custom: " + captured.Name, null, (s, e) => ActivateCustomProfile(captured));
                    cItem.Checked = curApuMode.Equals(captured.Name, StringComparison.OrdinalIgnoreCase);
                    menu.Items.Add(cItem);
                    customItems.Add(new KeyValuePair<ToolStripMenuItem, string>(cItem, captured.Name));
                }
            }

            Action syncTrayChecks = () =>
            {
                osdShowItem.Text = _showOverlay ? "Hide In-Game OSD Overlay" : "Show In-Game OSD Overlay";
                osdLockItem.Text = _lockOverlay ? "Unlock OSD Overlay (Allow Drag)" : "Lock OSD Overlay (Click-Through)";
                if (pSilent != null && pBal != null && pPerf != null)
                {
                    int livePower = GetLiveActivePowerModeCode();
                    pSilent.Checked = (livePower == 2);
                    pBal.Checked = (livePower == 0);
                    pPerf.Checked = (livePower == 1);
                    UpdateBiosPowerModeStyles(livePower);
                }
                string activeCurve = (_apuProfile != null) ? _apuProfile.Name : "";
                mBal.Checked = activeCurve.Equals("Balanced", StringComparison.OrdinalIgnoreCase);
                mPerf.Checked = activeCurve.Equals("Performance", StringComparison.OrdinalIgnoreCase);
                mSilent.Checked = activeCurve.Equals("Silent", StringComparison.OrdinalIgnoreCase);
                mAi.Checked = activeCurve.Equals("AI Workload", StringComparison.OrdinalIgnoreCase);
                for (int ci = 0; ci < customItems.Count; ci++)
                {
                    customItems[ci].Key.Checked = activeCurve.Equals(customItems[ci].Value, StringComparison.OrdinalIgnoreCase);
                }
            };
            menu.Opening += (s, e) => syncTrayChecks();
            if (powerSub != null) powerSub.DropDownOpening += (s, e) => syncTrayChecks();
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
            Win11VisualStyle.ApplyThemeToMenu(menu, CurrentPalette);
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

        private void UpdateTrayIcons(int cpuTemp, int gpuTemp)
        {
            TrayIconRenderer.UpdateIconSlot(_cpuTrayIcon, Icon, cpuTemp, _isTaskbarDark, false, ref _lastCpuIconKey, ref _cpuIconHandle);
            TrayIconRenderer.UpdateIconSlot(_gpuTrayIcon, Icon, gpuTemp, _isTaskbarDark, true, ref _lastGpuIconKey, ref _gpuIconHandle);

            if (_cpuTrayIcon != null && _cpuTrayIcon.Visible != _showCpuTray)
                _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null && _gpuTrayIcon.Visible != _showGpuTray)
                _gpuTrayIcon.Visible = _showGpuTray;
        }

    }
}
