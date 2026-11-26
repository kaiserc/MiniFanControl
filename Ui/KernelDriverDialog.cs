using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Themed Kernel Driver & HVCI Security Status + PawnIO Onboarding Dialog (Option A).
    /// Shown automatically on startup ONLY when no kernel driver could be opened (e.g. Windows 11
    /// Memory Integrity / HVCI blocked InpOut64 and PawnIO is not installed), and manually via
    /// Tools & Drivers -> Kernel Driver & HVCI Security Status... or clicking the driver banner.
    /// </summary>
    public class KernelDriverDialog : Form
    {
        public const string PAWNIO_DOWNLOAD_URL = "https://pawnio.eu";

        private readonly bool _darkMode;
        private readonly ThemePreference _themePref;
        private readonly bool _isStartupPrompt;
        private readonly bool _autoPauseAntiCheat;
        private readonly Func<bool> _onRecheckDriver;

        private Color _bgColor;
        private Color _cardBgColor;
        private Color _cardBorderColor;
        private Color _textMainColor;
        private Color _textDimColor;
        private Color _accentColor;
        private Color _statusDotColor;

        private Panel _statusDotPanel;
        private Label _statusHeadlineLabel;
        private Label _statusDetailLabel;
        private Label _guideBodyLabel;
        private Label _feedbackLabel;
        private Button _downloadBtn;
        private Button _recheckBtn;
        private Button _copyBtn;
        private Button _closeBtn;
        private CheckBox _dontAskAgainCb;

        public bool ShowPromptOnStartup
        {
            get { return _dontAskAgainCb == null || !_dontAskAgainCb.Checked; }
        }

        public bool DriverActivatedDuringSession { get; private set; }

        public static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    if (id == null) return false;
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static string BuildDiagnosticsReport(bool autoPauseAntiCheat)
        {
            bool driverOpen = NativeInpOut.IsDriverOpen();
            bool hvci = NativeInpOut.IsHvciEnabled();
            bool pawnSvc = PawnIoDriver.IsServiceInstalledAndRunning();
            bool elevated = IsProcessElevated();
            bool vanguardDetected = DriverServiceManager.IsDriverServiceRunning(AntiCheatMonitor.BootVanguardServiceName);
            string drv = NativeInpOut.ActiveDriverName;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl Kernel Driver, PawnIO & HVCI Security Status");
            sb.AppendLine("-----------------------------------------------------------");
            sb.AppendLine("Active Kernel Driver Engine: " + drv + (NativeInpOut.ActiveDriver != HardwareDriverEngine.None ? (driverOpen ? " [OPEN]" : " [BLOCKED / NOT OPEN]") : ""));
            sb.AppendLine("Binary SHA256 / Signature Check: " + (NativeInpOut.IsDriverHashVerified
                ? ("VERIFIED (" + (NativeInpOut.ActiveDriverSha256 ?? "") + ")")
                : "Not Verified / Not Loaded"));
            sb.AppendLine("Process Privileges: " + (elevated ? "Administrator (Elevated)" : "Standard User (Not Elevated)"));
            sb.AppendLine("Windows 11 Memory Integrity (HVCI): " + (hvci ? "ENABLED (Strict Kernel Code Integrity)" : "Disabled"));
            sb.AppendLine("PawnIO HVCI Driver Service: " + (pawnSvc ? ("Running (" + PawnIoDriver.LastStatus + ")") : "Not Installed (https://pawnio.eu)"));
            sb.AppendLine("PawnIO Signed Modules Loaded: " + PawnIoDriver.LoadedModulesSummary);
            sb.AppendLine("Bundled Signed Modules Found: " + PawnIoDriver.AvailableBundledModulesSummary);
            sb.AppendLine("Anti-Cheat Safe Auto-Pause (EAC/BattlEye): " + (autoPauseAntiCheat ? "ENABLED" : "Disabled"));
            if (vanguardDetected)
                sb.AppendLine("Boot Anti-Cheat Notice: Riot Vanguard (vgk) active at boot — PawnIO recommended.");
            if (!string.IsNullOrEmpty(NativeInpOut.LastDriverIntegrityError))
                sb.AppendLine("SECURITY ALERT: " + NativeInpOut.LastDriverIntegrityError);
            return sb.ToString();
        }

        public KernelDriverDialog(
            bool darkMode,
            ThemePreference themePref,
            bool isStartupPrompt,
            bool autoPauseAntiCheat,
            bool showPromptOnStartupDefault,
            Func<bool> onRecheckDriver = null)
        {
            _darkMode = darkMode;
            _themePref = themePref;
            _isStartupPrompt = isStartupPrompt;
            _autoPauseAntiCheat = autoPauseAntiCheat;
            _onRecheckDriver = onRecheckDriver;

            Text = _isStartupPrompt
                ? "MiniFanControl — Hardware Driver Setup (PawnIO Recommended)"
                : "MiniFanControl — Kernel Driver & HVCI Security Status";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, _isStartupPrompt ? 548 : 520);

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            bool isXmas = (_themePref == ThemePreference.Xmas);
            _bgColor = isXmas ? Color.White : (isRadeon ? Color.FromArgb(0x18, 0x12, 0x14) : (_darkMode ? Color.FromArgb(0x18, 0x18, 0x25) : Color.FromArgb(0xF8, 0xFA, 0xFC)));
            _cardBgColor = isXmas ? Color.White : (isRadeon ? Color.FromArgb(0x26, 0x1A, 0x1E) : (_darkMode ? Color.FromArgb(0x22, 0x22, 0x34) : Color.White));
            _cardBorderColor = isXmas ? Color.FromArgb(0x18, 0x18, 0x1B) : (isRadeon ? Color.FromArgb(0x44, 0x26, 0x2E) : (_darkMode ? Color.FromArgb(0x35, 0x37, 0x4E) : Color.FromArgb(0xDD, 0xE2, 0xEC)));
            _textMainColor = isXmas ? Color.FromArgb(0x09, 0x09, 0x0B) : (isRadeon ? Color.FromArgb(0xF8, 0xFA, 0xFC) : (_darkMode ? Color.FromArgb(0xCD, 0xD6, 0xF4) : Color.FromArgb(0x1E, 0x29, 0x3B)));
            _textDimColor = isXmas ? Color.FromArgb(0x3F, 0x3F, 0x46) : (isRadeon ? Color.FromArgb(0xB8, 0x98, 0xA0) : (_darkMode ? Color.FromArgb(0xA6, 0xAD, 0xC8) : Color.FromArgb(0x64, 0x74, 0x8B)));
            _accentColor = isXmas ? Color.FromArgb(0xDC, 0x26, 0x26) : (isRadeon ? Color.FromArgb(0xFF, 0x47, 0x57) : (_darkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x1D, 0x4E, 0xD8)));
            _statusDotColor = Color.FromArgb(0x94, 0xA3, 0xB8);

            BackColor = _bgColor;
            ForeColor = _textMainColor;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            BuildLayout(showPromptOnStartupDefault);
            RefreshDriverState();
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            Panel p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = _cardBgColor
            };
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(_cardBorderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return p;
        }

        private void BuildLayout(bool showPromptOnStartupDefault)
        {
            Label title = new Label
            {
                Text = _isStartupPrompt
                    ? "🛡 Kernel Driver Needed for Fan Control & EC Telemetry"
                    : "🛡 Kernel Driver, PawnIO & HVCI Security Status",
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                ForeColor = _accentColor,
                Location = new Point(20, 16),
                Size = new Size(600, 26),
                UseMnemonic = false
            };
            Controls.Add(title);

            Label subtitle = new Label
            {
                Text = _isStartupPrompt
                    ? "MiniFanControl could not open a kernel hardware driver on this PC. Safe Read-Only monitoring works, but fan curves & EC telemetry require PawnIO."
                    : "Inspect active kernel driver verification, Windows 11 Memory Integrity (HVCI) compatibility, and signed PawnIO modules.",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = _textDimColor,
                Location = new Point(22, 44),
                Size = new Size(596, 32),
                UseMnemonic = false
            };
            Controls.Add(subtitle);

            int cardW = 600;

            // Card 1: Live Driver & Security Status
            Panel statusCard = CreateCard(20, 82, cardW, 204);
            Controls.Add(statusCard);

            _statusDotPanel = new Panel
            {
                Location = new Point(14, 15),
                Size = new Size(12, 12),
                BackColor = _cardBgColor
            };
            _statusDotPanel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(_statusDotColor))
                {
                    e.Graphics.FillEllipse(br, 0, 0, 11, 11);
                }
            };
            statusCard.Controls.Add(_statusDotPanel);

            _statusHeadlineLabel = new Label
            {
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = _textMainColor,
                Location = new Point(32, 11),
                Size = new Size(cardW - 46, 22),
                UseMnemonic = false
            };
            statusCard.Controls.Add(_statusHeadlineLabel);

            _statusDetailLabel = new Label
            {
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = _textDimColor,
                Location = new Point(14, 38),
                Size = new Size(cardW - 28, 158),
                UseMnemonic = false
            };
            statusCard.Controls.Add(_statusDetailLabel);

            // Card 2: Recommended Setup (PawnIO Option A)
            Panel guideCard = CreateCard(20, 296, cardW, 134);
            Controls.Add(guideCard);

            Label guideHeader = new Label
            {
                Text = "Recommended: HVCI-Safe PawnIO Driver (Free, Open-Source & EV-Signed)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = _textMainColor,
                Location = new Point(14, 10),
                Size = new Size(cardW - 28, 20),
                UseMnemonic = false
            };
            guideCard.Controls.Add(guideHeader);

            _guideBodyLabel = new Label
            {
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = _textDimColor,
                Location = new Point(14, 34),
                Size = new Size(cardW - 28, 92),
                UseMnemonic = false
            };
            guideCard.Controls.Add(_guideBodyLabel);

            // Inline live feedback label
            _feedbackLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = _accentColor,
                Location = new Point(22, 438),
                Size = new Size(596, 18),
                UseMnemonic = false
            };
            Controls.Add(_feedbackLabel);

            int btnY = 464;
            _downloadBtn = new Button
            {
                Text = "Download PawnIO (pawnio.eu)",
                Location = new Point(20, btnY),
                Size = new Size(204, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = _accentColor,
                ForeColor = (_darkMode && _themePref != ThemePreference.Radeon) ? Color.FromArgb(0x11, 0x11, 0x1B) : Color.White,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _downloadBtn.FlatAppearance.BorderSize = 0;
            _downloadBtn.Click += (s, e) => OpenPawnIoWebsite();
            Controls.Add(_downloadBtn);

            _recheckBtn = new Button
            {
                Text = "↻ Re-check Driver",
                Location = new Point(232, btnY),
                Size = new Size(134, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = _cardBgColor,
                ForeColor = _textMainColor,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _recheckBtn.FlatAppearance.BorderColor = _cardBorderColor;
            _recheckBtn.Click += (s, e) => OnRecheckClicked();
            Controls.Add(_recheckBtn);

            _copyBtn = new Button
            {
                Text = "Copy Diagnostics",
                Location = new Point(374, btnY),
                Size = new Size(124, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = _cardBgColor,
                ForeColor = _textMainColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _copyBtn.FlatAppearance.BorderColor = _cardBorderColor;
            _copyBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(BuildDiagnosticsReport(_autoPauseAntiCheat));
                    _feedbackLabel.ForeColor = Color.FromArgb(0x22, 0xC5, 0x5E);
                    _feedbackLabel.Text = "✓ Driver diagnostics copied to clipboard.";
                }
                catch (Exception ex)
                {
                    _feedbackLabel.ForeColor = Color.FromArgb(0xEF, 0x44, 0x44);
                    _feedbackLabel.Text = "Could not copy to clipboard: " + ex.Message;
                }
            };
            Controls.Add(_copyBtn);

            _closeBtn = new Button
            {
                Text = _isStartupPrompt ? "Read-Only" : "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(506, btnY),
                Size = new Size(114, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = _cardBgColor,
                ForeColor = _textMainColor,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderColor = _cardBorderColor;
            Controls.Add(_closeBtn);
            AcceptButton = _closeBtn;
            CancelButton = _closeBtn;

            if (_isStartupPrompt)
            {
                _dontAskAgainCb = new CheckBox
                {
                    Text = "Don't show this driver prompt on startup again (you can always open it from Tools & Drivers)",
                    Checked = !showPromptOnStartupDefault,
                    ForeColor = _textDimColor,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                    Location = new Point(22, 508),
                    Size = new Size(596, 22),
                    AutoSize = false
                };
                Controls.Add(_dontAskAgainCb);
            }
        }

        private void OpenPawnIoWebsite()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PAWNIO_DOWNLOAD_URL,
                    UseShellExecute = true
                });
                _feedbackLabel.ForeColor = _accentColor;
                _feedbackLabel.Text = "Opened https://pawnio.eu — install PawnIO, then click 'Re-check Driver' to activate immediately.";
            }
            catch (Exception ex)
            {
                _feedbackLabel.ForeColor = Color.FromArgb(0xEF, 0x44, 0x44);
                _feedbackLabel.Text = "Unable to open browser (" + ex.Message + "). Visit https://pawnio.eu manually.";
            }
        }

        private void OnRecheckClicked()
        {
            _recheckBtn.Enabled = false;
            try
            {
                bool activated = _onRecheckDriver != null
                    ? _onRecheckDriver()
                    : NativeInpOut.TryReloadDriver(true);

                RefreshDriverState();

                if (activated && NativeInpOut.IsDriverOpen())
                {
                    DriverActivatedDuringSession = true;
                    _feedbackLabel.ForeColor = Color.FromArgb(0x22, 0xC5, 0x5E);
                    _feedbackLabel.Text = "✓ " + NativeInpOut.ActiveDriverName + " is now active! You can close this dialog.";
                    if (_closeBtn != null) _closeBtn.Text = "Continue";
                }
                else
                {
                    _feedbackLabel.ForeColor = Color.FromArgb(0xF5, 0x9E, 0x0B);
                    _feedbackLabel.Text = "No active kernel driver detected yet. Complete the PawnIO installer, then click 'Re-check Driver'.";
                }
            }
            catch (Exception ex)
            {
                _feedbackLabel.ForeColor = Color.FromArgb(0xEF, 0x44, 0x44);
                _feedbackLabel.Text = "Driver re-check failed: " + ex.Message;
            }
            finally
            {
                _recheckBtn.Enabled = true;
            }
        }

        private void RefreshDriverState()
        {
            bool driverOpen = NativeInpOut.IsDriverOpen();
            bool hvci = NativeInpOut.IsHvciEnabled();
            bool pawnSvc = PawnIoDriver.IsServiceInstalledAndRunning();
            bool elevated = IsProcessElevated();
            bool vanguardDetected = DriverServiceManager.IsDriverServiceRunning(AntiCheatMonitor.BootVanguardServiceName);
            HardwareDriverEngine engine = NativeInpOut.ActiveDriver;

            if (engine == HardwareDriverEngine.PawnIO && driverOpen)
            {
                _statusDotColor = Color.FromArgb(0x22, 0xC5, 0x5E); // Green
                _statusHeadlineLabel.Text = "STATUS: PawnIO Active (HVCI / Memory Integrity Compatible)";
                _guideBodyLabel.Text =
                    "✓ EV-signed PawnIO is running and loaded with signed bytecode modules (" + PawnIoDriver.LoadedModulesSummary + ").\r\n" +
                    "• You can safely keep Windows 11 Core Isolation / Memory Integrity (HVCI) enabled.\r\n" +
                    "• No legacy vulnerable drivers (InpOut64 / WinRing0) are required.";
                if (_downloadBtn != null) _downloadBtn.Text = "Visit pawnio.eu";
            }
            else if ((engine == HardwareDriverEngine.InpOut64 || engine == HardwareDriverEngine.WinRing0) && driverOpen)
            {
                _statusDotColor = hvci ? Color.FromArgb(0xF5, 0x9E, 0x0B) : Color.FromArgb(0x22, 0xC5, 0x5E);
                _statusHeadlineLabel.Text = "STATUS: " + NativeInpOut.ActiveDriverName + " Active (SHA256 Verified)";
                _guideBodyLabel.Text =
                    "1. Click 'Download PawnIO (pawnio.eu)' and run the official signed PawnIO installer (optional upgrade).\r\n" +
                    "2. MiniFanControl already bundles the signed modules (" + PawnIoDriver.AvailableBundledModulesSummary + ") — nothing else to download.\r\n" +
                    "3. Click 'Re-check Driver' to switch seamlessly from legacy " + engine.ToString() + " to HVCI-safe PawnIO.";
            }
            else
            {
                _statusDotColor = Color.FromArgb(0xEF, 0x44, 0x44); // Red
                if (!elevated)
                    _statusHeadlineLabel.Text = "STATUS: No Kernel Driver Loaded (Not Running as Administrator)";
                else if (hvci)
                    _statusHeadlineLabel.Text = "STATUS: Blocked by Windows 11 Memory Integrity (HVCI)";
                else
                    _statusHeadlineLabel.Text = "STATUS: No Kernel Driver Loaded (Safe Read-Only Mode)";

                _guideBodyLabel.Text =
                    "1. Click 'Download PawnIO (pawnio.eu)' below and run the official signed PawnIO installer.\r\n" +
                    "2. MiniFanControl already bundles the signed modules (" + PawnIoDriver.AvailableBundledModulesSummary + ") — no manual .bin setup needed.\r\n" +
                    "3. Click 'Re-check Driver' to activate fan curves & EC telemetry immediately (or click 'Read-Only' to continue monitoring).";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("• Active Engine: " + NativeInpOut.ActiveDriverName + (engine != HardwareDriverEngine.None ? (driverOpen ? " (Open)" : " (Blocked by Windows / Not Running)") : ""));
            sb.AppendLine("• Signature / SHA256 Check: " + (NativeInpOut.IsDriverHashVerified
                ? ("VERIFIED (" + (NativeInpOut.ActiveDriverSha256 ?? "") + ")")
                : "Not Verified / Not Loaded"));
            sb.AppendLine("• Windows 11 Memory Integrity (HVCI): " + (hvci ? "ENABLED (blocks legacy InpOut64 / WinRing0)" : "Disabled"));
            sb.AppendLine("• PawnIO Service: " + (pawnSvc ? ("Running (" + PawnIoDriver.LastStatus + ")") : "Not Installed (optional: https://pawnio.eu)"));
            sb.AppendLine("• PawnIO Modules: Loaded = " + PawnIoDriver.LoadedModulesSummary + "  |  Bundled = " + PawnIoDriver.AvailableBundledModulesSummary);
            sb.AppendLine("• Privileges & Anti-Cheat: " + (elevated ? "Administrator" : "Standard User") +
                "  |  Auto-Pause = " + (_autoPauseAntiCheat ? "ENABLED" : "Disabled") +
                (vanguardDetected ? "  |  Riot Vanguard detected" : ""));
            if (!string.IsNullOrEmpty(NativeInpOut.LastDriverIntegrityError))
                sb.AppendLine("• SECURITY ALERT: " + NativeInpOut.LastDriverIntegrityError);

            _statusDetailLabel.Text = sb.ToString().TrimEnd();
            if (_statusDotPanel != null) _statusDotPanel.Invalidate();
        }
    }
}
