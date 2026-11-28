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
            RebuildTrayContextMenus();
            SaveSettings();
            string fanLabel = editSys ? "Sys Fan" : "APU Fan";
            StartRampIndication(fanLabel + " → " + profile.Name, editSys ? 2 : 1);
        }

        private void PromptCreateCustomMode()
        {
            string suggested = string.Format("Custom {0}", _customProfiles.Count + 1);
            using (NewModeDialog dlg = new NewModeDialog(_isDarkMode, suggested, _themePref))
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
                MessageBox.Show(this, "At least one custom curve profile is kept.", "MiniFanControl",
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
            RebuildTrayContextMenus();
            SaveSettings();
            RefreshStatus();
        }

        private void UpdateCurveTabStyles()
        {
            if (_tabApuCurveBtn == null || _tabSysCurveBtn == null || _curveCanvas == null)
                return;

            if (IsReadOnlyMonitoringMode)
            {
                _tabApuCurveBtn.Text = "● APU Fan: Read-Only";
                _tabSysCurveBtn.Text = "● Sys Fan: Read-Only";
            }
            else
            {
                string apuModeStr = _apuManualOverride ? "Manual" : (_apuProfile != null ? _apuProfile.Name : "Balanced");
                string sysModeStr = _sysManualOverride ? "Manual" : (_sysProfile != null ? _sysProfile.Name : "Balanced");
                _tabApuCurveBtn.Text = string.Format("● APU Fan: {0}", ShortModeTag(apuModeStr));
                _tabSysCurveBtn.Text = string.Format("● Sys Fan: {0}", ShortModeTag(sysModeStr));
            }
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

            ThemePalette pal = CurrentPalette;
            bool isRadeon = pal.IsRadeon;
            bool isXmas = pal.IsXmas;
            Color apuAccent = pal.Accent;
            Color sysAccent = pal.SecondaryAccent;
            Color liveAccent = isXmas
                ? ((_highlightedMetric == 1 || _highlightedMetric == 3 || _highlightedMetric == 4 || _highlightedMetric == 6) ? XmasRed : XmasBlack)
                : (_highlightedMetric == 2
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
                                        : apuAccent))))));
            Color activeFg = pal.ActiveFg;
            Color liveFg = isXmas ? Color.White : ((_highlightedMetric == 2 || _highlightedMetric == 3 || _highlightedMetric == 6 || _highlightedMetric == 7) ? Color.Black : activeFg);

            bool osdActive = _showingOsdConfig;
            bool perfActive = !osdActive && _showingPerfGraph;
            bool liveActive = !osdActive && !perfActive && _showingLiveTelemetry;
            bool apuActive = !osdActive && !perfActive && !liveActive && !_curveCanvas.EditSysFan;
            bool sysActive = !osdActive && !perfActive && !liveActive && _curveCanvas.EditSysFan;

            if (_tabLiveGraphBtn != null)
            {
                Win11VisualStyle.StylePillButton(_tabLiveGraphBtn, pal, liveActive, liveAccent, liveFg);
                Font f = liveActive ? TabFontBold : TabFontRegular;
                if (_tabLiveGraphBtn.Font != f) _tabLiveGraphBtn.Font = f;
            }
            if (_tabPerfGraphBtn != null)
            {
                Color perfAcc = isXmas ? XmasRed : Color.FromArgb(0x22, 0xD3, 0xEE);
                Color perfFg = isXmas ? Color.White : Color.Black;
                Win11VisualStyle.StylePillButton(_tabPerfGraphBtn, pal, perfActive, perfAcc, perfFg);
                Font f = perfActive ? TabFontBold : TabFontRegular;
                if (_tabPerfGraphBtn.Font != f) _tabPerfGraphBtn.Font = f;
            }
            Win11VisualStyle.StylePillButton(_tabApuCurveBtn, pal, apuActive, apuAccent, activeFg);
            {
                Font f = apuActive ? TabFontBold : TabFontRegular;
                if (_tabApuCurveBtn.Font != f) _tabApuCurveBtn.Font = f;
            }
            Win11VisualStyle.StylePillButton(_tabSysCurveBtn, pal, sysActive, sysAccent, activeFg);
            {
                Font f = sysActive ? TabFontBold : TabFontRegular;
                if (_tabSysCurveBtn.Font != f) _tabSysCurveBtn.Font = f;
            }
            if (_tabOsdConfigBtn != null)
            {
                Win11VisualStyle.StylePillButton(_tabOsdConfigBtn, pal, osdActive, isRadeon ? RadeonAccent : apuAccent, activeFg);
                Font f = osdActive ? TabFontBold : TabFontRegular;
                if (_tabOsdConfigBtn.Font != f) _tabOsdConfigBtn.Font = f;
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
                _curveHintLabel.Text = string.Format("Spotlighting {0} — click pills to toggle • Shift+click to solo • Space/P = Pause • right-click for PNG export", hlName);
                return;
            }
            if (_biosAutoControlActive)
            {
                _curveHintLabel.Text = "BIOS Auto active — motherboard BIOS is controlling fans (click any mode, custom curve, or slider to resume MiniFanControl)";
                return;
            }
            FanCurveProfile prof = GetSelectedTabProfile();
            string whichFan = _curveCanvas.EditSysFan ? "Sys Fan" : "APU Fan";
            if (prof.IsBuiltIn)
            {
                _curveHintLabel.Text = string.Format("{0}: '{1}' — drag points to customize (Shift = 5°/5% snap • PgUp/PgDn + Arrows = nudge)", whichFan, prof.Name);
            }
            else
            {
                _curveHintLabel.Text = string.Format("Editing {0} curve '{1}' — Shift = 5°/5% snap • Ctrl+Z = undo • PgUp/PgDn + Arrows = nudge", whichFan, prof.Name);
            }
        }

        private void UpdateCalibrationBadge()
        {
            if (_fan1Slider != null) _fan1Slider.FloorThreshold = FanCurveProfile.ApuMinSpinPct;
            if (_fan2Slider != null) _fan2Slider.FloorThreshold = FanCurveProfile.SysMinSpinPct;
            if (_calibBadgeLabel == null) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = IsXmas;
            _calibBadgeLabel.BackColor = isXmas ? XmasBg : (isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg)));
            if (IsReadOnlyMonitoringMode)
            {
                _calibBadgeLabel.Text = "👁️ Safe Read-Only Monitoring (Fan writes disabled)";
                _calibBadgeLabel.ForeColor = isXmas ? XmasTextDim : (isRadeon ? RadeonTextDim : (_isDarkMode ? DarkTextDim : LightTextDim));
                return;
            }
            bool isCalibrated, isStale;
            _calibBadgeLabel.Text = FanCurveProfile.FormatCalibrationStatusBadge(_hasDualFans, out isCalibrated, out isStale);
            if (!isCalibrated)
            {
                _calibBadgeLabel.ForeColor = isXmas ? XmasTextDim : (isRadeon ? RadeonTextDim : (_isDarkMode ? DarkTextDim : LightTextDim));
            }
            else if (isStale)
            {
                _calibBadgeLabel.ForeColor = isXmas ? XmasRed : (_isDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xB4, 0x53, 0x09));
            }
            else
            {
                _calibBadgeLabel.ForeColor = isXmas ? XmasRedDeep : (_isDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x15, 0x80, 0x3D));
            }
        }


        private void UpdateModeButtonStyles(int activeCode)
        {
            _lastSelectedModeCode = activeCode;
            ThemePalette pal = CurrentPalette;
            bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
            Color accent = editSys ? pal.SecondaryAccent : pal.Accent;

            FanCurveProfile tabProf = GetSelectedTabProfile();
            bool tabManual = editSys ? _sysManualOverride : _apuManualOverride;
            bool biosAuto = _biosAutoControlActive;

            for (int i = 0; i < _modeButtons.Length; i++)
            {
                if (_modeButtons[i] == null) continue;
                bool isSelected = (!biosAuto && !tabManual && tabProf != null && tabProf.IsBuiltIn && tabProf.BuiltInCode == i);
                Win11VisualStyle.StylePillButton(_modeButtons[i], pal, isSelected, accent, pal.ActiveFg);
                Font f = isSelected ? ModeFontBold : ModeFontRegular;
                if (_modeButtons[i].Font != f) _modeButtons[i].Font = f;
            }

            if (_activateCustomBtn != null)
            {
                bool customActive = (!biosAuto && !tabManual && tabProf != null && !tabProf.IsBuiltIn);
                Win11VisualStyle.StylePillButton(_activateCustomBtn, pal, customActive, accent, pal.ActiveFg);
            }

            if (_allAutoBtn != null)
            {
                string autoLbl = biosAuto ? "✓ BIOS Auto" : "Reset Auto";
                if (_allAutoBtn.Text != autoLbl) _allAutoBtn.Text = autoLbl;
                if (biosAuto)
                {
                    Win11VisualStyle.StylePillButton(_allAutoBtn, pal, true, pal.Success, pal.ActiveFg);
                }
                else
                {
                    Win11VisualStyle.StyleModernButton(_allAutoBtn, pal.Surface2, pal.Text, pal.Border, pal.IsDark);
                }
            }
        }

        private static readonly Font BiosModeFontBold = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        private static readonly Font BiosModeFontRegular = new Font("Segoe UI", 8.0f, FontStyle.Regular);
        private bool _lastPowerBarIsWinMode = false;

        private bool IsUsingWindowsPowerMode()
        {
            return (!_demoMode && _hw != null && _hw.IsSuperIoControlled && WindowsPowerMode.IsSupported());
        }

        private static string GetPowerBarTooltip(bool usesWinPower)
        {
            if (usesWinPower)
            {
                return "Windows Power Mode (Synced with Windows Settings → System → Power mode)\r\n" +
                       "• 🌙 Silent (Best Power Efficiency)  |  ⚖ Balanced  |  ⚡ Perf (Best Performance)\r\n" +
                       "• Safely adjusts AMD Ryzen boost & power behavior via Windows / AMD PPM kernel driver.\r\n" +
                       "• Adapts ✨ Auto-Tuned fan curves automatically; leaves Custom Curves & 🔒 presets untouched.";
            }
            return "BIOS Power Mode (EC 0x31/0x32) — Live APU TDP Preset!\r\n" +
                   "• 🌙 Silent (~35W)  |  ⚖ Balanced (~48W)  |  ⚡ Perf (~60W+ Peak)\r\n" +
                   "• When ✨ Auto-Tuned is active, switching BIOS Power also adapts your Auto-Tuned fan knee automatically.\r\n" +
                   "• When using a manual Custom Curve or 🔒 preset, switching BIOS Power changes TDP without replacing your curve.";
        }

        private void RefreshPowerBarCapabilityUi()
        {
            if (_biosPowerLabel == null) return;
            bool readOnlyMonitor = IsReadOnlyMonitoringMode;
            bool usesWinPower = !readOnlyMonitor && IsUsingWindowsPowerMode();
            bool shouldShow = !readOnlyMonitor && (_demoMode || _hw == null || !_hw.IsSuperIoControlled || usesWinPower);
            int headerLabelW = shouldShow ? 358 : 706;
            if (_hwBadgeLabel != null && _hwBadgeLabel.Width != headerLabelW)
                _hwBadgeLabel.Size = new Size(headerLabelW, _hwBadgeLabel.Height);
            if (_hwSubLabel != null && _hwSubLabel.Width != headerLabelW)
                _hwSubLabel.Size = new Size(headerLabelW, _hwSubLabel.Height);

            if (_biosPowerLabel.Visible != shouldShow || _lastPowerBarIsWinMode != usesWinPower)
            {
                _lastPowerBarIsWinMode = usesWinPower;
                _biosPowerLabel.Visible = shouldShow;
                _biosPowerLabel.Text = usesWinPower ? "Power:" : "BIOS:";
                string tip = GetPowerBarTooltip(usesWinPower);
                if (_uiToolTip != null) _uiToolTip.SetToolTip(_biosPowerLabel, tip);
                for (int i = 0; i < _biosModeButtons.Length; i++)
                {
                    if (_biosModeButtons[i] != null)
                    {
                        _biosModeButtons[i].Visible = shouldShow;
                        if (_uiToolTip != null) _uiToolTip.SetToolTip(_biosModeButtons[i], tip);
                    }
                }
            }
        }

        private void UpdateBiosPowerModeStyles(int activeCode)
        {
            RefreshPowerBarCapabilityUi();
            if (activeCode >= 0 && activeCode <= 2)
                _lastActiveBiosModeCode = activeCode;

            ThemePalette pal = CurrentPalette;
            for (int i = 0; i < _biosModeButtons.Length; i++)
            {
                Button b = _biosModeButtons[i];
                if (b == null) continue;
                bool isActive = (i == _lastActiveBiosModeCode);
                Win11VisualStyle.StylePillButton(b, pal, isActive);
                Font targetFont = isActive ? BiosModeFontBold : BiosModeFontRegular;
                if (b.Font != targetFont)
                    b.Font = targetFont;
            }
        }

        private void ApplyBiosPowerMode(int code)
        {
            if (code < 0 || code > 2) return;
            bool usesWinPower = IsUsingWindowsPowerMode();
            string[] ecNames = { "Balanced (~48W)", "Performance (~60W+)", "Silent (~35W)" };
            try
            {
                bool ok = usesWinPower
                    ? WindowsPowerMode.SetModeCode(code)
                    : (_controller != null && _controller.SetBiosPowerMode(code));

                if (!ok)
                {
                    if (_errorBanner != null)
                    {
                        _errorBanner.Text = usesWinPower
                            ? "⚠ Windows Power Mode could not be changed (ensure Windows Power Plan is set to Balanced)."
                            : "⚠ BIOS Power Mode change was not accepted by hardware EC.";
                        _errorBanner.Visible = true;
                    }
                    return;
                }

                UpdateBiosPowerModeStyles(code);
                RebuildTrayContextMenus();

                // If Auto-Tuned is active on either fan, adapt its TDP knee automatically for the new Power Mode
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
                string label = usesWinPower
                    ? ("Windows Power Mode → " + WindowsPowerMode.GetModeDisplayName(code))
                    : ("BIOS Power Mode → " + ecNames[code]);
                StartRampIndication(label, 0);
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

    }
}
