using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MiniFanControl
{
    public class SetupAssistantDialog : Form
    {
        private readonly MainForm _mainForm;
        private readonly bool _darkMode;
        private readonly ThemePreference _themePref;
        private readonly ThemePalette _palette;
        private readonly bool _isReadOnlyMode;

        private Label _calibBadge;
        private Label _autoTuneResultLabel;
        private CheckBox _showOnStartupCb;
        private Button _calibrateBtn;
        private Button _autoTuneBtn;

        public bool ShowOnStartupIfUncalibrated
        {
            get { return _showOnStartupCb != null && _showOnStartupCb.Checked; }
        }

        public SetupAssistantDialog(MainForm mainForm, bool darkMode, ThemePreference themePref, bool showOnStartupDefault, bool isReadOnlyMode = false)
        {
            _mainForm = mainForm;
            _darkMode = darkMode;
            _themePref = themePref;
            _isReadOnlyMode = isReadOnlyMode;

            Text = Loc.Get("SetupAssistant.Title", "MiniFanControl — First-Time Setup Assistant & Auto-Tune Guide");
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 580);

            _palette = ThemePalette.Resolve(themePref, darkMode);
            bool isRadeon = _palette.IsRadeon;
            Color bg = _palette.Bg;
            Color cardBg = _palette.Surface;
            Color cardBorder = _palette.Border;
            Color textMain = _palette.Text;
            Color textDim = _palette.TextDim;
            Color accent = _palette.Accent;

            BackColor = bg;
            ForeColor = textMain;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            // Title Banner
            Label title = new Label
            {
                Text = Loc.Get("SetupAssistant.Welcome", "🚀 Welcome to MiniFanControl"),
                Font = new Font("Segoe UI", 13.0f, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(20, 16),
                Size = new Size(580, 26)
            };
            Controls.Add(title);

            Label sub = new Label
            {
                Text = _isReadOnlyMode
                    ? "Unverified hardware detected — running in Safe Read-Only Monitoring Mode (fan writes disabled)."
                    : Loc.Get("SetupAssistant.Subtitle", "Follow these two quick steps to tune optimal cooling acoustics for your Mini PC."),
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ForeColor = textDim,
                Location = new Point(22, 44),
                Size = new Size(576, 20)
            };
            Controls.Add(sub);

            int cardW = 576;
            int curY = 74;

            // STEP 1 CARD: Calibration
            Panel card1 = CreateStepCard(20, curY, cardW, 134, cardBg, cardBorder);
            Controls.Add(card1);

            Label h1 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step1Header", "Step 1: Measure Motor Stall Floor"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = textMain,
                Location = new Point(14, 10),
                Size = new Size(300, 20),
                UseMnemonic = false
            };
            card1.Controls.Add(h1);

            _calibBadge = new Label
            {
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(316, 10),
                Size = new Size(248, 20),
                UseMnemonic = false
            };
            card1.Controls.Add(_calibBadge);
            RefreshCalibBadge();

            Label desc1 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step1Desc", "Mini PC blower fans have physical rotor inertia and stall below a specific PWM threshold. " +
                       "Running the 3-pass calibration wizard measures your exact spin-up % (e.g., 22% instead of generic 30%), " +
                       "unlocking whisper-quiet low-speed operation while guaranteeing the fan never stalls."),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = textDim,
                Location = new Point(14, 34),
                Size = new Size(548, 52)
            };
            card1.Controls.Add(desc1);

            _calibrateBtn = new Button
            {
                Text = Loc.Get("SetupAssistant.Step1Btn", "1. Run 3-Pass Calibration Wizard..."),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(14, 92),
                Size = new Size(260, 30),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(_calibrateBtn, _palette, true);
            _calibrateBtn.Enabled = !_isReadOnlyMode;
            _calibrateBtn.Click += (s, e) =>
            {
                if (_mainForm != null)
                {
                    _mainForm.RunGuiFanCalibration();
                    RefreshCalibBadge();
                }
            };
            card1.Controls.Add(_calibrateBtn);

            curY += 144;

            // STEP 2 CARD: Auto-Tune
            Panel card2 = CreateStepCard(20, curY, cardW, 140, cardBg, cardBorder);
            Controls.Add(card2);

            Label h2 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step2Header", "Step 2: Synthesize Auto-Tuned Fan Curve"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = textMain,
                Location = new Point(14, 10),
                Size = new Size(548, 20)
            };
            card2.Controls.Add(h2);

            Label desc2 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step2Desc", "Auto-Tune reads your measured calibration floor, observed idle baseline (anti-hunting), " +
                       "and APU TDP power envelope (acoustic knee) to build a custom 5-point APU and Sys fan curve. " +
                       "Running Auto-Tune AFTER calibration yields the quietest possible acoustics with 100% safety."),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = textDim,
                Location = new Point(14, 34),
                Size = new Size(548, 52)
            };
            card2.Controls.Add(desc2);

            _autoTuneBtn = new Button
            {
                Text = Loc.Get("SetupAssistant.Step2Btn", "2. ✨ Generate Auto-Tuned Curve"),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(14, 94),
                Size = new Size(260, 30),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StyleModernButton(_autoTuneBtn, _palette.Surface2, accent, Win11VisualStyle.Blend(cardBorder, accent, 0.55f), _palette.IsDark);
            _autoTuneBtn.Enabled = !_isReadOnlyMode;
            _autoTuneBtn.Click += (s, e) =>
            {
                if (_mainForm != null)
                {
                    _mainForm.RunAutoTuneCurve();
                    string fmt = Loc.Get("SetupAssistant.Step2Success", "✓ Auto-Tuned Curve Active! (APU Min: {0}%, Sys Min: {1}%)");
                    _autoTuneResultLabel.Text = string.Format(fmt, FanCurveProfile.ApuMinSpinPct, FanCurveProfile.SysMinSpinPct);
                    _autoTuneResultLabel.ForeColor = isRadeon ? Color.FromArgb(0x4A, 0xDE, 0x80) : (_darkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x16, 0x65, 0x34));
                }
            };
            card2.Controls.Add(_autoTuneBtn);

            _autoTuneResultLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = textDim,
                Location = new Point(282, 99),
                Size = new Size(280, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            card2.Controls.Add(_autoTuneResultLabel);

            curY += 150;

            // STEP 3 CARD: Monitoring & Safety
            Panel card3 = CreateStepCard(20, curY, cardW, 110, cardBg, cardBorder);
            Controls.Add(card3);

            Label h3 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step3Header", "Step 3: Monitor in Games & Everyday Work"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = textMain,
                Location = new Point(14, 10),
                Size = new Size(548, 20),
                UseMnemonic = false
            };
            card3.Controls.Add(h3);

            Label desc3 = new Label
            {
                Text = Loc.Get("SetupAssistant.Step3Desc", "• Press Ctrl+Alt+F9 anytime to toggle the transparent in-game OSD overlay.\r\n" +
                       "• Customize points on the curve canvas or switch to 'Balanced' / 'Performance' tabs.\r\n" +
                       "• Click 'Reset Auto' anytime to safely revert to native BIOS automatic control."),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = textDim,
                Location = new Point(14, 34),
                Size = new Size(548, 66)
            };
            card3.Controls.Add(desc3);

            curY += 122;

            // Bottom Bar: Checkbox & Done Button
            _showOnStartupCb = new CheckBox
            {
                Text = Loc.Get("SetupAssistant.ShowOnStartup", "Show this Setup Assistant on launch if uncalibrated"),
                Checked = showOnStartupDefault,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = textDim,
                Location = new Point(24, curY + 6),
                Size = new Size(380, 22)
            };
            Controls.Add(_showOnStartupCb);

            Button closeBtn = new Button
            {
                Text = Loc.Get("SetupAssistant.DoneBtn", "Done & Start Using App"),
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(406, curY + 2),
                Size = new Size(190, 32),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK,
                UseMnemonic = false
            };
            Win11VisualStyle.StylePillButton(closeBtn, _palette, true);
            Controls.Add(closeBtn);
            AcceptButton = closeBtn;
            CancelButton = closeBtn;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_palette != null)
            {
                Win11VisualStyle.ApplyWindowFrame(this, _palette.IsDark, _palette.Bg, _palette.Text, _palette.WindowBorder);
            }
        }

        private Panel CreateStepCard(int x, int y, int w, int h, Color bg, Color border)
        {
            Panel p = new DoubleBufferedPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = bg
            };
            p.Paint += (s, e) =>
            {
                Win11VisualStyle.DrawRoundedCardBorder(e.Graphics, p.ClientRectangle, border, 8, 1.0f);
            };
            return p;
        }

        private void RefreshCalibBadge()
        {
            if (_calibBadge == null) return;
            if (_isReadOnlyMode)
            {
                _calibBadge.Text = "👁️ Read-Only (Writes Disabled)";
                _calibBadge.ForeColor = _darkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
                return;
            }
            bool isCal, isStale;
            string text = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCal, out isStale);
            _calibBadge.Text = text;
            if (!isCal)
            {
                _calibBadge.ForeColor = _darkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
            }
            else if (isStale)
            {
                _calibBadge.ForeColor = _darkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xB4, 0x53, 0x09);
            }
            else
            {
                _calibBadge.ForeColor = _darkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x15, 0x80, 0x3D);
            }
        }
    }
}
