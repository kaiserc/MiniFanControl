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

        public NewModeDialog(bool darkMode, string suggestedName)
        {
            Text = "Add Custom Fan Mode";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(310, 118);
            BackColor = darkMode ? Color.FromArgb(0x1E, 0x1E, 0x2E) : Color.FromArgb(0xF3, 0xF4, 0xF6);
            ForeColor = darkMode ? Color.White : Color.Black;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lbl = new Label
            {
                Text = "Mode Name:",
                Location = new Point(14, 14),
                Size = new Size(280, 20)
            };
            Controls.Add(lbl);

            _textBox = new TextBox
            {
                Text = suggestedName,
                Location = new Point(14, 38),
                Size = new Size(282, 26),
                BackColor = darkMode ? Color.FromArgb(0x2A, 0x2A, 0x3E) : Color.White,
                ForeColor = darkMode ? Color.White : Color.Black
            };
            Controls.Add(_textBox);

            Button okBtn = new Button
            {
                Text = "Create",
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(126, 76),
                Size = new Size(80, 28),
                BackColor = darkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x1D, 0x4E, 0xD8),
                ForeColor = darkMode ? Color.Black : Color.White
            };
            okBtn.FlatAppearance.BorderSize = 0;
            okBtn.Click += (s, e) =>
            {
                string val = (_textBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(val))
                {
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
                FlatStyle = FlatStyle.Flat,
                Location = new Point(216, 76),
                Size = new Size(80, 28),
                BackColor = darkMode ? Color.FromArgb(0x31, 0x32, 0x44) : Color.FromArgb(0xE5, 0xE7, 0xEB),
                ForeColor = darkMode ? Color.White : Color.Black
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            Controls.Add(cancelBtn);

            AcceptButton = okBtn;
            CancelButton = cancelBtn;
        }
    }

    /// <summary>
    /// Non-blocking real-time Fan Calibration Dialog.
    /// Runs 1-pass or 3-pass fan calibration on a background worker thread while streaming
    /// live RPM, temperature, power, progress bar, and step-by-step log updates to the UI.
    /// </summary>
}
