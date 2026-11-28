using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Reusable theme-aware informational dialog (replaces stark white Win32 MessageBox dumps
    /// for About & Hardware Compatibility details) with one-click Copy Summary support.
    /// </summary>
    public class ThemedInfoDialog : Form
    {
        private readonly ThemePalette _palette;
        private readonly string _bodyText;
        private Label _feedbackLabel;

        public ThemedInfoDialog(
            string windowTitle,
            string heading,
            string subtitle,
            string bodyText,
            ThemePreference themePref,
            bool darkMode,
            int width = 620,
            int height = 470,
            string primaryActionText = null,
            Action primaryAction = null)
        {
            _palette = ThemePalette.Resolve(themePref, darkMode);
            _bodyText = bodyText ?? "";

            Text = windowTitle ?? "MiniFanControl";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(width, height);
            BackColor = _palette.Bg;
            ForeColor = _palette.Text;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            Label titleLbl = new Label
            {
                Text = heading ?? windowTitle ?? "MiniFanControl",
                Font = new Font("Segoe UI", 12.0f, FontStyle.Bold),
                ForeColor = _palette.Accent,
                BackColor = _palette.Bg,
                Location = new Point(18, 14),
                Size = new Size(width - 36, 24),
                UseMnemonic = false
            };
            Controls.Add(titleLbl);

            int cardTop = 44;
            if (!string.IsNullOrEmpty(subtitle))
            {
                Label subLbl = new Label
                {
                    Text = subtitle,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = _palette.TextDim,
                    BackColor = _palette.Bg,
                    Location = new Point(18, 40),
                    Size = new Size(width - 36, 18),
                    UseMnemonic = false
                };
                Controls.Add(subLbl);
                cardTop = 64;
            }

            int cardH = height - cardTop - 56;
            Panel card = new DoubleBufferedPanel
            {
                Location = new Point(18, cardTop),
                Size = new Size(width - 36, cardH),
                BackColor = _palette.Surface
            };
            card.Paint += (s, e) =>
            {
                if (!SystemInformation.HighContrast)
                {
                    Win11VisualStyle.DrawRoundedCardBorder(e.Graphics, card.ClientRectangle, _palette.Border, 8, 1f);
                }
            };
            Controls.Add(card);

            TextBox bodyBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.75f, FontStyle.Regular),
                BackColor = _palette.Surface,
                ForeColor = _palette.Text,
                Location = new Point(10, 10),
                Size = new Size(card.Width - 20, card.Height - 20),
                Text = _bodyText.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                TabStop = true
            };
            bodyBox.SelectionStart = 0;
            bodyBox.SelectionLength = 0;
            Win11VisualStyle.ApplyDarkExplorerTheme(bodyBox, _palette.IsDark);
            card.Controls.Add(bodyBox);

            int btnY = height - 42;
            bool hasPrimaryAction = !string.IsNullOrEmpty(primaryActionText) && primaryAction != null;
            int rightEdge = width - 18;

            Button closeBtn = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(rightEdge - 96, btnY),
                Size = new Size(96, 28),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(closeBtn, _palette, !hasPrimaryAction);
            Controls.Add(closeBtn);
            rightEdge -= 104;

            if (hasPrimaryAction)
            {
                int actW = Math.Max(136, TextRenderer.MeasureText(primaryActionText, new Font("Segoe UI", 8.5f, FontStyle.Bold)).Width + 24);
                Button actionBtn = new Button
                {
                    Text = primaryActionText,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Location = new Point(rightEdge - actW, btnY),
                    Size = new Size(actW, 28),
                    Cursor = Cursors.Hand
                };
                Win11VisualStyle.StylePillButton(actionBtn, _palette, true);
                actionBtn.Click += (s, e) =>
                {
                    try { primaryAction(); } catch { }
                };
                Controls.Add(actionBtn);
                rightEdge -= (actW + 8);
            }

            Button copyBtn = new Button
            {
                Text = "Copy Summary",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(rightEdge - 120, btnY),
                Size = new Size(120, 28),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StyleModernButton(copyBtn, _palette.Surface2, _palette.Text, _palette.Border, _palette.IsDark);
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_bodyText);
                    _feedbackLabel.ForeColor = _palette.Success;
                    _feedbackLabel.Text = "✓ Copied summary to clipboard.";
                }
                catch (Exception ex)
                {
                    _feedbackLabel.ForeColor = _palette.Danger;
                    _feedbackLabel.Text = "Clipboard error: " + ex.Message;
                }
            };
            Controls.Add(copyBtn);
            rightEdge -= 128;

            _feedbackLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = _palette.Success,
                BackColor = _palette.Bg,
                Location = new Point(18, btnY + 6),
                Size = new Size(Math.Max(60, rightEdge - 18), 18),
                UseMnemonic = false
            };
            Controls.Add(_feedbackLabel);

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
    }
}
