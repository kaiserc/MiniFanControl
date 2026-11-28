using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public class NewModeDialog : Form
    {
        public string ModeName { get; private set; }
        private TextBox _textBox;

        private readonly ThemePalette _palette;

        public NewModeDialog(bool darkMode, string suggestedName)
            : this(darkMode, suggestedName, darkMode ? ThemePreference.Dark : ThemePreference.Light)
        {
        }

        public NewModeDialog(bool darkMode, string suggestedName, ThemePreference themePref)
        {
            _palette = ThemePalette.Resolve(themePref, darkMode);

            Text = "Add Custom Fan Mode";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 122);
            BackColor = _palette.Bg;
            ForeColor = _palette.Text;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lbl = new Label
            {
                Text = "Mode Name:",
                ForeColor = _palette.TextDim,
                Location = new Point(14, 14),
                Size = new Size(292, 20)
            };
            Controls.Add(lbl);

            _textBox = new TextBox
            {
                Text = suggestedName,
                Location = new Point(14, 38),
                Size = new Size(292, 26),
                BackColor = _palette.Surface2,
                ForeColor = _palette.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_textBox);

            Button okBtn = new Button
            {
                Text = "Create",
                DialogResult = DialogResult.OK,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(134, 78),
                Size = new Size(82, 28),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(okBtn, _palette, true);
            okBtn.Click += (s, e) =>
            {
                string val = (_textBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(val))
                {
                    DialogResult = DialogResult.None;
                    return;
                }
                if (val.IndexOfAny(new[] { '|', ';', '=' }) >= 0)
                {
                    MessageBox.Show("Profile name cannot contain the characters: |, ;, or =", "Invalid Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                ModeName = val;
            };
            Controls.Add(okBtn);

            Button cancelBtn = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(224, 78),
                Size = new Size(82, 28),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(cancelBtn, _palette, false);
            Controls.Add(cancelBtn);

            AcceptButton = okBtn;
            CancelButton = cancelBtn;
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
