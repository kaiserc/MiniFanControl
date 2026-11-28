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
    public class FanCalibrationDialog : Form
    {
        public FanCalibrationResult Result { get; private set; }

        private readonly HardwareBackend _hw;
        private readonly DemoBackend _demo;
        private readonly bool _demoMode;
        private readonly Action<FanCalibrationProgress> _hostProgressCallback;
        private readonly ThemePalette _palette;

        private RadioButton _threePassRb;
        private RadioButton _onePassRb;
        private Label _phaseLabel;
        private Label _liveTelemetryLabel;
        private ProgressBar _progressBar;
        private TextBox _logBox;
        private Button _startBtn;
        private Button _cancelBtn;
        private Button _applyBtn;

        private volatile bool _isRunning;
        private volatile bool _cancelRequested;
        private Thread _workerThread;

        public FanCalibrationDialog(
            bool darkMode,
            HardwareBackend hw,
            DemoBackend demo,
            bool demoMode,
            Action<FanCalibrationProgress> hostProgressCallback,
            ThemePreference themePref = ThemePreference.System)
        {
            _hw = hw;
            _demo = demo;
            _demoMode = demoMode;
            _hostProgressCallback = hostProgressCallback;
            _palette = ThemePalette.Resolve(themePref, darkMode);

            Text = "MiniFanControl — Real-Time Fan Calibration";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(540, 424);
            BackColor = _palette.Bg;
            ForeColor = _palette.Text;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            Color surface = _palette.Surface;
            Color accent  = _palette.Accent;
            Color dimFg   = _palette.TextDim;

            string lastCalInfo = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Current Status: Never calibrated (using default 30% minimum spin floor)."
                : string.Format("Last Calibrated: {0}  •  Saved Floor: APU {1}% ({2} RPM) / Sys {3}% ({4} RPM)",
                    FanCurveProfile.LastCalibratedDate,
                    FanCurveProfile.ApuMinSpinPct, FanCurveProfile.ApuMaxRpm,
                    FanCurveProfile.SysMinSpinPct, FanCurveProfile.SysMaxRpm);

            Label introLbl = new Label
            {
                Text = lastCalInfo + "\r\nWaits for full 0 RPM coast-down (up to 35s from 100%) and 5.0s+ stabilization per step (+2% margin).",
                Location = new Point(14, 10),
                Size = new Size(512, 34),
                ForeColor = dimFg
            };
            Controls.Add(introLbl);

            _threePassRb = new RadioButton
            {
                Text = "3-Pass Verification (Recommended — 3× min-spin check)",
                Location = new Point(16, 46),
                Size = new Size(372, 22),
                Checked = true,
                ForeColor = _palette.Text,
                BackColor = _palette.Bg,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold)
            };
            Controls.Add(_threePassRb);

            _onePassRb = new RadioButton
            {
                Text = "1-Pass Standard",
                Location = new Point(392, 46),
                Size = new Size(134, 22),
                ForeColor = _palette.Text,
                BackColor = _palette.Bg,
                Checked = false
            };
            Controls.Add(_onePassRb);

            _phaseLabel = new Label
            {
                Text = "Ready — Click 'Start Calibration' to begin real-time sweep",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(14, 74),
                Size = new Size(512, 20)
            };
            Controls.Add(_phaseLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(14, 98),
                Size = new Size(512, 18),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            Controls.Add(_progressBar);

            _liveTelemetryLabel = new Label
            {
                Text = "Live Telemetry: APU Fan: -- RPM  |  Sys Fan: -- RPM  |  CPU: --°C  |  Power: --W",
                Font = new Font("Consolas", 9.0f, FontStyle.Bold),
                BackColor = surface,
                ForeColor = _palette.IsDark ? _palette.SecondaryAccent : _palette.Text,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                Location = new Point(14, 122),
                Size = new Size(512, 26)
            };
            Controls.Add(_liveTelemetryLabel);

            _logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                BackColor = _palette.TileBg,
                ForeColor = _palette.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(14, 154),
                Size = new Size(512, 214),
                Text = "Click 'Start Calibration' below.\r\n" +
                       "• Phase 1: Commands stop PWM (0% Super I/O / 1% EC) until rotors stop or settle at hardware floor.\r\n" +
                       "• Phase 2: Full curve sweep through 12%..35% (2-3% steps) and 40%..100% (5-10% steps).\r\n" +
                       "• Pre-cools if APU is hot (>=72°C), holds each step 5.0s+, and filters tach spikes.\r\n"
            };
            Win11VisualStyle.ApplyDarkExplorerTheme(_logBox, _palette.IsDark);
            Controls.Add(_logBox);

            _startBtn = new Button
            {
                Text = "▶ Start Calibration",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(14, 380),
                Size = new Size(164, 30),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(_startBtn, _palette, true);
            _startBtn.Click += (s, e) => StartCalibrationAsync();
            Controls.Add(_startBtn);

            _cancelBtn = new Button
            {
                Text = "Stop / Cancel",
                Location = new Point(186, 380),
                Size = new Size(124, 30),
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StyleModernButton(_cancelBtn, _palette.Surface2, _palette.Text, _palette.Border, _palette.IsDark);
            _cancelBtn.Click += (s, e) =>
            {
                if (_isRunning)
                {
                    _cancelRequested = true;
                    _phaseLabel.Text = "Stopping calibration and restoring Auto mode...";
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Controls.Add(_cancelBtn);

            _applyBtn = new Button
            {
                Text = "✓ Apply & Save Calibration",
                UseMnemonic = false,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(342, 380),
                Size = new Size(184, 30),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            Win11VisualStyle.StylePillButton(_applyBtn, _palette, true, _palette.Success, _palette.IsXmas ? Color.White : Color.Black);
            _applyBtn.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_applyBtn);

            FormClosing += OnDialogClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_palette != null)
            {
                Win11VisualStyle.ApplyWindowFrame(this, _palette.IsDark, _palette.Bg, _palette.Text, _palette.WindowBorder);
            }
        }

        private void StartCalibrationAsync()
        {
            if (_isRunning) return;
            _isRunning = true;
            _cancelRequested = false;
            Result = null;

            _startBtn.Enabled = false;
            _applyBtn.Enabled = false;
            _threePassRb.Enabled = false;
            _onePassRb.Enabled = false;
            _cancelBtn.Text = "Stop Calibration";
            _progressBar.Value = 0;
            _logBox.Clear();

            int passes = _threePassRb.Checked ? 3 : 1;
            int stepDelayMs = _demoMode ? 350 : 5000; // 5-second stabilization wait before fan measurement

            _workerThread = new Thread(() =>
            {
                try
                {
                    FanCalibrationResult res = FanCalibrationResult.RunCalibration(
                        _hw,
                        _demo,
                        _demoMode,
                        stepDelayMs,
                        passes,
                        OnWorkerProgress,
                        () => _cancelRequested);

                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() => OnWorkerFinished(res, null)));
                    }
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke(new Action(() => OnWorkerFinished(null, ex)));
                    }
                }
            });
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void OnWorkerProgress(FanCalibrationProgress prog)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    _progressBar.Value = Math.Max(0, Math.Min(100, prog.ProgressPercent));
                    if (!string.IsNullOrEmpty(prog.PhaseTitle))
                        _phaseLabel.Text = prog.PhaseTitle;

                    FanStatus st = prog.LiveStatus;
                    if (st.CpuTemp > 0 || st.Fan1Rpm > 0 || st.Fan2Rpm > 0)
                    {
                        _liveTelemetryLabel.Text = string.Format(
                            "Live: APU Fan: {0,4} RPM | Sys Fan: {1,4} RPM | CPU: {2}°C | Power: {3}W",
                            st.Fan1Rpm, st.Fan2Rpm, st.CpuTemp, st.ApuPowerWatts);
                    }

                    if (!string.IsNullOrEmpty(prog.LogLine))
                    {
                        _logBox.AppendText(prog.LogLine + "\r\n");
                    }

                    if (_hostProgressCallback != null)
                        _hostProgressCallback(prog);
                }));
            }
            catch { }
        }

        private void OnWorkerFinished(FanCalibrationResult res, Exception error)
        {
            _isRunning = false;
            _startBtn.Enabled = true;
            _startBtn.Text = "↻ Re-Run Calibration";
            _threePassRb.Enabled = true;
            _onePassRb.Enabled = true;
            _cancelBtn.Text = "Close";

            if (error != null)
            {
                _phaseLabel.Text = "⚠ Calibration Error: " + error.Message;
                _logBox.AppendText("\r\nERROR: " + error.Message + "\r\n");
                return;
            }

            if (res == null || res.WasCanceled || res.WasThermalAborted)
            {
                _phaseLabel.Text = (res != null && res.WasThermalAborted)
                    ? string.Format("⚠ Thermal safety abort (APU hit {0}°C) — Fans restored to Auto. Close background load and retry.", res.ThermalAbortTempC)
                    : "Calibration canceled — Fans restored to Auto.";
                if (res != null) _logBox.AppendText("\r\n" + res.SummaryReport);
                return;
            }

            Result = res;
            _progressBar.Value = 100;
            _phaseLabel.Text = string.Format("✓ Calibration Complete! Safe Min Spin: APU {0}% | Sys {1}",
                res.ApuMinSpinPct, res.HasDualFans ? res.SysMinSpinPct + "%" : "1-Fan Mode");
            _logBox.AppendText("\r\n" + res.SummaryReport);
            _applyBtn.Enabled = true;
            _applyBtn.Focus();
        }

        private void OnDialogClosing(object sender, FormClosingEventArgs e)
        {
            if (_isRunning)
            {
                _cancelRequested = true;
                try
                {
                    if (_workerThread != null && _workerThread.IsAlive)
                        _workerThread.Join(1200);
                }
                catch { }
            }
        }
    }
}
