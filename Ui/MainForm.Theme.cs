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
        private void DetectSystemTheme(out bool appDark, out bool taskbarDark)
        {
            if (_themePref == ThemePreference.Dark || _themePref == ThemePreference.Radeon || _themePref == ThemePreference.Afterburner)
            {
                appDark = true;
                taskbarDark = true;
                return;
            }
            if (_themePref == ThemePreference.Light || _themePref == ThemePreference.Xmas)
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


        private string GetThemeButtonLabel()
        {
            switch (_themePref)
            {
                case ThemePreference.Dark: return "Theme: Dark";
                case ThemePreference.Radeon: return "Theme: AMD Radeon";
                case ThemePreference.Afterburner: return "Theme: Afterburner";
                case ThemePreference.Light: return "Theme: Light";
                case ThemePreference.Xmas: return "❄ Theme: Xmas";
                default: return string.Format("Theme: System ({0})", _isDarkMode ? "Dark" : "Light");
            }
        }

        private void CycleThemePreference()
        {
            if (_themePref == ThemePreference.System) _themePref = ThemePreference.Dark;
            else if (_themePref == ThemePreference.Dark) _themePref = ThemePreference.Radeon;
            else if (_themePref == ThemePreference.Radeon) _themePref = ThemePreference.Afterburner;
            else if (_themePref == ThemePreference.Afterburner) _themePref = ThemePreference.Light;
            else if (_themePref == ThemePreference.Light) _themePref = ThemePreference.Xmas;
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
            bool isXmas = IsXmas;
            Color bg = isXmas ? XmasBg : (isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg)));
            Color surface = isXmas ? XmasSurface : (isRadeon ? RadeonSurface : (IsAb ? AbSurface : (_isDarkMode ? DarkSurface : LightSurface)));
            Color surface2 = isXmas ? XmasSurface2 : (isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2)));
            Color text = isXmas ? XmasText : (isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText)));
            Color textDim = isXmas ? XmasTextDim : (isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim)));
            Color accent = isXmas ? XmasRed : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent)));

            Color tempBoxBg = isXmas ? Color.White : (isRadeon ? Color.FromArgb(0x0D, 0x05, 0x07) : (IsAb ? Color.FromArgb(0x0B, 0x0D, 0x10) : (_isDarkMode ? Color.Black : Color.White)));
            Color tempBoxFg = isXmas ? XmasText : (_isDarkMode ? Color.White : Color.Black);
            Color tempBoxTitleFg = isXmas
                ? XmasRed
                : (isRadeon
                    ? Color.FromArgb(0xFD, 0xA4, 0xAF)
                    : (IsAb ? AbGreen : (_isDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x47, 0x55, 0x69))));

            BackColor = bg;
            ForeColor = text;

            if (_headerLogoBox != null)
            {
                _headerLogoBox.BackColor = surface;
                Image oldImg = _headerLogoBox.Image;
                _headerLogoBox.Image = AppLogoRenderer.RenderConcept(isXmas ? 6 : (isRadeon ? 4 : (IsAb ? 5 : 1)), 192);
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

            Color cardBorder = isXmas
                ? XmasBlack
                : (isRadeon
                    ? Color.FromArgb(0x52, 0x1A, 0x24)
                    : (IsAb ? Color.FromArgb(0x22, 0x35, 0x2A) : (_isDarkMode ? Color.FromArgb(0x36, 0x3A, 0x52) : Color.FromArgb(0xCB, 0xD5, 0xE1))));

            if (_driversMenuBtn != null)
            {
                Win11VisualStyle.StyleModernButton(_driversMenuBtn, surface2, text, cardBorder, _isDarkMode);
            }

            if (_themeToggleBtn != null)
            {
                _themeToggleBtn.Text = GetThemeButtonLabel();
                Color themeBtnBg = isXmas ? XmasRed : (isRadeon ? RadeonAccent : (IsAb ? AbGreen : surface2));
                Color themeBtnFg = (isXmas || isRadeon) ? Color.White : (IsAb ? Color.Black : text);
                Color themeBtnBorder = isXmas ? XmasBlack : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (IsAb ? AbGreen : cardBorder));
                Win11VisualStyle.StyleModernButton(_themeToggleBtn, themeBtnBg, themeBtnFg, themeBtnBorder, _isDarkMode || isRadeon || isXmas);
            }

            foreach (Label h in _sectionHeaders)
                h.ForeColor = accent;

            UpdateCalibrationBadge();

            foreach (Panel c in _sectionCards)
            {
                c.BackColor = surface;
                c.Invalidate();
            }

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
                _curveCanvas.IsRadeonTheme = isRadeon; _curveCanvas.IsAfterburnerTheme = IsAb; _curveCanvas.IsXmasTheme = isXmas;
                _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.IsDarkMode = _isDarkMode;
                _liveCanvas.IsRadeonTheme = isRadeon; _liveCanvas.IsAfterburnerTheme = IsAb; _liveCanvas.IsXmasTheme = isXmas;
                _liveCanvas.Invalidate();
            }
            if (_perfCanvas != null)
            {
                _perfCanvas.IsDarkMode = _isDarkMode;
                _perfCanvas.IsRadeonTheme = isRadeon; _perfCanvas.IsAfterburnerTheme = IsAb; _perfCanvas.IsXmasTheme = isXmas;
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
                    Color btnFg = (b == _autoTuneCustomBtn) ? accent : text;
                    Color btnBorder = (b == _autoTuneCustomBtn) ? Win11VisualStyle.Blend(cardBorder, accent, 0.55f) : cardBorder;
                    Win11VisualStyle.StyleModernButton(b, surface2, btnFg, btnBorder, _isDarkMode);
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
                Win11VisualStyle.StyleModernButton(_optCardOsdConfigBtn, surface2, text, cardBorder, _isDarkMode);
            }
            if (_overlayForm != null)
            {
                _overlayForm.IsRadeonTheme = isRadeon; _overlayForm.IsAfterburnerTheme = IsAb; _overlayForm.IsXmasTheme = isXmas;
                _overlayForm.Invalidate();
            }
            UpdateOsdStudioStyles();

            UpdateModeButtonStyles(_lastSelectedModeCode);
            ApplyWin11WindowFrame();
        }

        private void ApplyWin11WindowFrame()
        {
            if (!IsHandleCreated || IsDisposed) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = IsXmas;
            Color bg = isXmas ? XmasBg : (isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg)));
            Color text = isXmas ? XmasText : (isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText)));
            Color border = isXmas
                ? XmasRed
                : (isRadeon
                    ? Color.FromArgb(0x9F, 0x12, 0x39)
                    : (IsAb ? Color.FromArgb(0x16, 0x65, 0x34) : (_isDarkMode ? Color.FromArgb(0x3B, 0x42, 0x61) : Color.FromArgb(0xCB, 0xD5, 0xE1))));
            Win11VisualStyle.ApplyWindowFrame(this, _isDarkMode || isRadeon || IsAb, bg, text, border);
        }

    }
}
