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
                _trayUpdateThrottle.Reset();
                ApplyThemeToControls();
                RefreshStatus();
            }
        }


        private string GetThemeButtonLabel()
        {
            switch (_themePref)
            {
                case ThemePreference.Dark: return "Theme: Dark ▾";
                case ThemePreference.Radeon: return "Theme: AMD Radeon ▾";
                case ThemePreference.Afterburner: return "Theme: Afterburner ▾";
                case ThemePreference.Light: return "Theme: Light ▾";
                case ThemePreference.Xmas: return "❄ Theme: Xmas ▾";
                default: return string.Format("Theme: System ({0}) ▾", _isDarkMode ? "Dark" : "Light");
            }
        }

        private void SelectThemePreference(ThemePreference pref)
        {
            if (_themePref == pref) return;
            _themePref = pref;
            SaveSettings();
            DetectSystemTheme(out _isDarkMode, out _isTaskbarDark);
            _lastCpuIconKey = int.MinValue;
            _lastGpuIconKey = int.MinValue;
            _trayUpdateThrottle.Reset();
            ApplyThemeToControls();
            RefreshStatus();
        }

        private void ShowThemePickerMenu()
        {
            if (_themeToggleBtn == null) return;
            if (_themeMenu != null)
            {
                ContextMenuStrip prevThemeMenu = _themeMenu;
                _themeMenu = null;
                SafeDisposeContextMenuDeferred(prevThemeMenu);
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            AddThemeMenuItem(menu, "System (Auto Dark/Light)", ThemePreference.System,
                _isDarkMode ? DarkBg : LightBg, _isDarkMode ? DarkAccent : LightAccent, _isDarkMode ? Color.FromArgb(0x45, 0x47, 0x5A) : Color.FromArgb(0x94, 0xA3, 0xB8));
            AddThemeMenuItem(menu, "Dark (Midnight Slate)", ThemePreference.Dark,
                DarkBg, DarkAccent, Color.FromArgb(0x45, 0x47, 0x5A));
            AddThemeMenuItem(menu, "AMD Radeon (Adrenalin Crimson)", ThemePreference.Radeon,
                RadeonBg, RadeonAccent, Color.FromArgb(0x9F, 0x12, 0x39));
            AddThemeMenuItem(menu, "Afterburner (Carbon Neon)", ThemePreference.Afterburner,
                AbBg, AbGreen, Color.FromArgb(0x16, 0x65, 0x34));
            AddThemeMenuItem(menu, "Light (Clean Daylight)", ThemePreference.Light,
                LightBg, LightAccent, Color.FromArgb(0x94, 0xA3, 0xB8));
            AddThemeMenuItem(menu, "❄ Xmas Edition (Snow & Crimson)", ThemePreference.Xmas,
                XmasBg, XmasRed, XmasBlack);

            Win11VisualStyle.ApplyThemeToMenu(menu, CurrentPalette);
            _themeMenu = menu;
            menu.Show(_themeToggleBtn, new Point(0, _themeToggleBtn.Height));
        }

        private void AddThemeMenuItem(ContextMenuStrip menu, string title, ThemePreference pref, Color fill, Color accent, Color border)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(title);
            item.Image = Win11VisualStyle.CreateColorSwatch(fill, accent, border);
            item.Checked = (_themePref == pref);
            item.Click += (s, e) => SelectThemePreference(pref);
            menu.Items.Add(item);
        }

        private void CycleThemePreference()
        {
            ThemePreference next;
            if (_themePref == ThemePreference.System) next = ThemePreference.Dark;
            else if (_themePref == ThemePreference.Dark) next = ThemePreference.Radeon;
            else if (_themePref == ThemePreference.Radeon) next = ThemePreference.Afterburner;
            else if (_themePref == ThemePreference.Afterburner) next = ThemePreference.Light;
            else if (_themePref == ThemePreference.Light) next = ThemePreference.Xmas;
            else next = ThemePreference.System;

            SelectThemePreference(next);
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
                if (_telemetryTiles != null)
                {
                    ThemePalette hcPal = CurrentPalette;
                    for (int i = 0; i < _telemetryTiles.Length; i++)
                    {
                        if (_telemetryTiles[i] != null) _telemetryTiles[i].ApplyTheme(hcPal);
                    }
                }
                if (_curveCanvas != null) _curveCanvas.Invalidate();
                if (_liveCanvas != null) _liveCanvas.Invalidate();
                if (_perfCanvas != null) _perfCanvas.Invalidate();
                return;
            }

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = IsXmas;
            ThemePalette pal = CurrentPalette;
            Color bg = isXmas ? XmasBg : (isRadeon ? RadeonBg : (IsAb ? AbBg : (_isDarkMode ? DarkBg : LightBg)));
            Color surface = isXmas ? XmasSurface : (isRadeon ? RadeonSurface : (IsAb ? AbSurface : (_isDarkMode ? DarkSurface : LightSurface)));
            Color surface2 = isXmas ? XmasSurface2 : (isRadeon ? RadeonSurface2 : (IsAb ? AbSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2)));
            Color text = isXmas ? XmasText : (isRadeon ? RadeonText : (IsAb ? AbText : (_isDarkMode ? DarkText : LightText)));
            Color textDim = isXmas ? XmasTextDim : (isRadeon ? RadeonTextDim : (IsAb ? AbTextDim : (_isDarkMode ? DarkTextDim : LightTextDim)));
            Color accent = isXmas ? XmasRed : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (IsAb ? AbGreen : (_isDarkMode ? DarkAccent : LightAccent)));

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

            if (_telemetryTiles != null)
            {
                for (int i = 0; i < _telemetryTiles.Length; i++)
                {
                    if (_telemetryTiles[i] != null) _telemetryTiles[i].ApplyTheme(pal);
                }
            }

            _modeLabel.BackColor = surface;
            _modeLabel.ForeColor = accent;

            _pct1Label.BackColor = surface;
            _pct2Label.BackColor = surface;

            if (_customModeCombo != null)
            {
                Win11VisualStyle.ApplyDarkComboTheme(_customModeCombo, pal);
            }

            if (_samplingCombo != null)
            {
                Win11VisualStyle.ApplyDarkComboTheme(_samplingCombo, pal);
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
            _fan1Slider.AccentOverride = pal.Accent;
            _fan1Slider.FloorThreshold = FanCurveProfile.ApuMinSpinPct;
            _fan1Slider.Invalidate();
            _fan2Slider.BackColor = surface;
            _fan2Slider.AccentOverride = pal.SecondaryAccent;
            _fan2Slider.FloorThreshold = FanCurveProfile.SysMinSpinPct;
            _fan2Slider.Invalidate();
            Win11VisualStyle.ApplyDarkExplorerTheme(this, pal.IsDark);
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
                    cb.Invalidate();
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

            if (_driversMenu != null && !_driversMenu.IsDisposed)
            {
                Win11VisualStyle.ApplyThemeToMenu(_driversMenu, CurrentPalette);
            }
            RebuildTrayContextMenus();

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
