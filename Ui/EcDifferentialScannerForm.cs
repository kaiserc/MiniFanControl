using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiniFanControl
{
    public class EcDifferentialScannerForm : Form
    {
        private readonly IHardwareBackend _hw;
        private readonly DeviceProfile _profile;
        private readonly SystemHardwareInfo _sysInfo;

        public Action<DeviceProfile> OnCustomProfileApplied;

        private EcSnapshot _idleSnapshot;
        private EcSnapshot _loadSnapshot;
        private EcDiffReport _activeReport;

        private Button _btnCaptureIdle;
        private Button _btnCaptureLoad;
        private Button _btnAnalyze;
        private Button _btnCopyReport;
        private Button _btnSaveReport;
        private Button _btnSubmitGitHub;
        private Button _btnApplyProfile;
        private Button _btnExportJson;
        private Label _statusLabel;
        private Label _idleSummaryLabel;
        private Label _loadSummaryLabel;
        private TabControl _tabControl;
        private ListView _candidatesListView;
        private RichTextBox _matrixTextBox;
        private RichTextBox _rawReportTextBox;
        private ProgressBar _progressBar;

        private static readonly Color BgDark = Color.FromArgb(16, 18, 28);
        private static readonly Color SurfaceDark = Color.FromArgb(24, 27, 40);
        private static readonly Color SurfaceLight = Color.FromArgb(34, 38, 56);
        private static readonly Color AccentGreen = Color.FromArgb(0x00, 0xE6, 0x76);
        private static readonly Color AccentCyan = Color.FromArgb(0x00, 0xE5, 0xFF);
        private static readonly Color AccentAmber = Color.FromArgb(0xFF, 0x91, 0x00);
        private static readonly Color TextWhite = Color.FromArgb(240, 244, 248);
        private static readonly Color TextDim = Color.FromArgb(148, 163, 184);

        public EcDifferentialScannerForm(IHardwareBackend hw, SystemHardwareInfo sysInfo)
        {
            _hw = hw;
            _sysInfo = sysInfo ?? SystemHardwareInfo.Detect();
            _profile = _sysInfo.MatchedProfile;

            Text = "MiniFanControl • EC Motherboard Telemetry & Differential Scanner (Tier 2 Discovery)";
            Size = new Size(880, 720);
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgDark;
            ForeColor = TextWhite;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            BuildUi();
        }

        public EcDifferentialScannerForm(IHardwareBackend hw, DeviceProfile profile)
            : this(hw, new SystemHardwareInfo { MatchedProfile = profile })
        {
        }

        private void BuildUi()
        {
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 200,
                BackColor = SurfaceDark,
                Padding = new Padding(12)
            };

            Label titleLabel = new Label
            {
                Text = "🔬 EC Motherboard Telemetry & Differential Sensor Scanner",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = AccentGreen,
                Location = new Point(12, 10),
                AutoSize = true
            };
            topPanel.Controls.Add(titleLabel);

            Label subtitleLabel = new Label
            {
                Text = "Discover motherboard thermal zones (VRM power stages, ambient thermistors), fan duty registers, and DC power rails by comparing idle vs load snapshots.",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = TextDim,
                Location = new Point(14, 34),
                Size = new Size(820, 20)
            };
            topPanel.Controls.Add(subtitleLabel);

            // Row 1: Snapshot Acquisition & Differential Analysis
            _btnCaptureIdle = new Button
            {
                Text = "1. Capture Idle Baseline (3s)",
                Location = new Point(14, 56),
                Size = new Size(185, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = AccentCyan,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCaptureIdle.FlatAppearance.BorderColor = AccentCyan;
            _btnCaptureIdle.Click += (s, e) => CaptureAsync("Idle Baseline", true);
            topPanel.Controls.Add(_btnCaptureIdle);

            _btnCaptureLoad = new Button
            {
                Text = "2. Capture Thermal Load (3s)",
                Location = new Point(207, 56),
                Size = new Size(195, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = AccentAmber,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnCaptureLoad.FlatAppearance.BorderColor = AccentAmber;
            _btnCaptureLoad.Click += (s, e) => CaptureAsync("Thermal Load", false);
            topPanel.Controls.Add(_btnCaptureLoad);

            _btnAnalyze = new Button
            {
                Text = "3. Analyze Differential",
                Location = new Point(410, 56),
                Size = new Size(160, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = AccentGreen,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnAnalyze.FlatAppearance.BorderColor = AccentGreen;
            _btnAnalyze.Click += (s, e) => RunAnalysis();
            topPanel.Controls.Add(_btnAnalyze);

            // Row 2: Community Submission & Dynamic Profile Deployment
            _btnCopyReport = new Button
            {
                Text = "📋 Copy Report",
                Location = new Point(14, 96),
                Size = new Size(115, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = TextWhite,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnCopyReport.FlatAppearance.BorderColor = TextDim;
            _btnCopyReport.Click += (s, e) => CopyReportToClipboard();
            topPanel.Controls.Add(_btnCopyReport);

            _btnSaveReport = new Button
            {
                Text = "💾 Save Report...",
                Location = new Point(136, 96),
                Size = new Size(120, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = TextWhite,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnSaveReport.FlatAppearance.BorderColor = TextDim;
            _btnSaveReport.Click += (s, e) => SaveReportToFile();
            topPanel.Controls.Add(_btnSaveReport);

            _btnSubmitGitHub = new Button
            {
                Text = "🚀 Submit to GitHub (1-Click)",
                Location = new Point(263, 96),
                Size = new Size(195, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = AccentGreen,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnSubmitGitHub.FlatAppearance.BorderColor = AccentGreen;
            _btnSubmitGitHub.Click += (s, e) => SubmitToGitHub();
            topPanel.Controls.Add(_btnSubmitGitHub);

            _btnApplyProfile = new Button
            {
                Text = "✨ Apply as Custom Profile",
                Location = new Point(465, 96),
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = AccentAmber,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnApplyProfile.FlatAppearance.BorderColor = AccentAmber;
            _btnApplyProfile.Click += (s, e) => ApplyDiscoveredAsCustomProfile();
            topPanel.Controls.Add(_btnApplyProfile);

            _btnExportJson = new Button
            {
                Text = "💾 Export Profile (.json)",
                Location = new Point(672, 96),
                Size = new Size(165, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceLight,
                ForeColor = TextWhite,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnExportJson.FlatAppearance.BorderColor = TextDim;
            _btnExportJson.Click += (s, e) => ExportProfileJson();
            topPanel.Controls.Add(_btnExportJson);

            _progressBar = new ProgressBar
            {
                Location = new Point(14, 136),
                Size = new Size(823, 6),
                Visible = false,
                Style = ProgressBarStyle.Marquee
            };
            topPanel.Controls.Add(_progressBar);

            _statusLabel = new Label
            {
                Text = "Ready. Step 1: Ensure system is idling, then click 'Capture Idle Baseline'.",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = TextWhite,
                Location = new Point(14, 148),
                Size = new Size(820, 18)
            };
            topPanel.Controls.Add(_statusLabel);

            _idleSummaryLabel = new Label
            {
                Text = "Idle: (Not Captured)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = AccentCyan,
                Location = new Point(14, 172),
                AutoSize = true
            };
            topPanel.Controls.Add(_idleSummaryLabel);

            _loadSummaryLabel = new Label
            {
                Text = "Load: (Not Captured)",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = AccentAmber,
                Location = new Point(360, 172),
                AutoSize = true
            };
            topPanel.Controls.Add(_loadSummaryLabel);

            Controls.Add(topPanel);

            // Center TabControl for discoveries, matrix, and full report
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark
            };

            // Tab 1: Discovered Sensors ListView
            TabPage tabDiscoveries = new TabPage("🎯 Discovered Telemetry Channels")
            {
                BackColor = BgDark,
                Padding = new Padding(8)
            };
            _candidatesListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = SurfaceDark,
                ForeColor = TextWhite,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            _candidatesListView.Columns.Add("Register", 75);
            _candidatesListView.Columns.Add("Classification", 160);
            _candidatesListView.Columns.Add("Suggested Role / Hardware Channel", 270);
            _candidatesListView.Columns.Add("Idle Val", 75);
            _candidatesListView.Columns.Add("Load Val", 75);
            _candidatesListView.Columns.Add("Delta", 65);
            _candidatesListView.Columns.Add("Details", 160);
            tabDiscoveries.Controls.Add(_candidatesListView);
            _tabControl.TabPages.Add(tabDiscoveries);

            // Tab 2: 16x16 Differential Hex Matrix
            TabPage tabMatrix = new TabPage("▦ 256-Byte EC Diff Matrix (0x00 .. 0xFF)")
            {
                BackColor = BgDark,
                Padding = new Padding(8)
            };
            _matrixTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 12, 18),
                ForeColor = TextWhite,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                ReadOnly = true,
                WordWrap = false
            };
            tabMatrix.Controls.Add(_matrixTextBox);
            _tabControl.TabPages.Add(tabMatrix);

            // Tab 3: Full Text Report
            TabPage tabReport = new TabPage("📝 Full Text Report")
            {
                BackColor = BgDark,
                Padding = new Padding(8)
            };
            _rawReportTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 12, 18),
                ForeColor = TextWhite,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                ReadOnly = true,
                WordWrap = false
            };
            tabReport.Controls.Add(_rawReportTextBox);
            _tabControl.TabPages.Add(tabReport);

            Controls.Add(_tabControl);
            _tabControl.BringToFront();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _btnCaptureIdle.Enabled = enabled;
            _btnCaptureLoad.Enabled = enabled;
            _btnAnalyze.Enabled = enabled && (_idleSnapshot != null && _loadSnapshot != null);
            _btnCopyReport.Enabled = enabled && (_activeReport != null);
            _btnSaveReport.Enabled = enabled && (_activeReport != null);
            _btnSubmitGitHub.Enabled = enabled && (_activeReport != null);
            _btnApplyProfile.Enabled = enabled && (_activeReport != null);
            _btnExportJson.Enabled = enabled && (_activeReport != null);
        }

        private void CaptureAsync(string label, bool isIdle)
        {
            SetButtonsEnabled(false);
            _progressBar.Visible = true;
            _statusLabel.Text = string.Format("Sampling {0} across multiple passes to filter bus noise...", label);

            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    EcSnapshot snap = EcDifferentialScanner.CaptureSnapshot(_hw, label, 6, 150);
                    Invoke(new Action(() =>
                    {
                        if (isIdle)
                        {
                            _idleSnapshot = snap;
                            _idleSummaryLabel.Text = string.Format("Idle: CPU {0}°C • GPU {1}°C • APU {2} RPM • Sys {3} RPM (~{4}W)",
                                snap.CpuTemp, snap.GpuTemp, snap.Fan1Rpm, snap.Fan2Rpm, snap.ApuPowerWatts);
                            _statusLabel.Text = "✓ Idle baseline captured! Step 2: Run a thermal benchmark or game, then click 'Capture Thermal Load'.";
                        }
                        else
                        {
                            _loadSnapshot = snap;
                            _loadSummaryLabel.Text = string.Format("Load: CPU {0}°C • GPU {1}°C • APU {2} RPM • Sys {3} RPM (~{4}W)",
                                snap.CpuTemp, snap.GpuTemp, snap.Fan1Rpm, snap.Fan2Rpm, snap.ApuPowerWatts);
                            _statusLabel.Text = "✓ Thermal load captured! Step 3: Click 'Analyze Differential' to isolate motherboard sensors.";
                        }
                        _progressBar.Visible = false;
                        SetButtonsEnabled(true);
                    }));
                }
                catch (Exception ex)
                {
                    Invoke(new Action(() =>
                    {
                        _statusLabel.Text = "Error during snapshot: " + ex.Message;
                        _progressBar.Visible = false;
                        SetButtonsEnabled(true);
                    }));
                }
            });
        }

        private void RunAnalysis()
        {
            if (_idleSnapshot == null || _loadSnapshot == null) return;

            _activeReport = EcDifferentialScanner.Analyze(_idleSnapshot, _loadSnapshot, _profile);

            // Populate Discovered ListView
            _candidatesListView.Items.Clear();

            Action<List<EcRegisterDiff>, Color> addCategory = (list, col) =>
            {
                foreach (var d in list)
                {
                    ListViewItem item = new ListViewItem(string.Format("0x{0:X2}", d.Register));
                    item.SubItems.Add(d.Category.ToString());
                    item.SubItems.Add(d.SuggestedRole);
                    item.SubItems.Add(string.Format("0x{0:X2} ({1})", d.IdleVal, d.IdleVal));
                    item.SubItems.Add(string.Format("0x{0:X2} ({1})", d.LoadVal, d.LoadVal));
                    item.SubItems.Add(d.Delta > 0 ? "+" + d.Delta : d.Delta.ToString());
                    item.SubItems.Add(d.Details);
                    item.ForeColor = col;
                    _candidatesListView.Items.Add(item);
                }
            };

            addCategory(_activeReport.ThermalCandidates, AccentAmber);
            addCategory(_activeReport.FanPwmCandidates, AccentCyan);
            addCategory(_activeReport.TachometerCandidates, AccentGreen);
            addCategory(_activeReport.VoltageCandidates, Color.FromArgb(167, 139, 250));
            addCategory(_activeReport.FlagCandidates, TextWhite);

            // Populate Full Text Report
            string textReport = EcDifferentialScanner.GenerateTextReport(_activeReport);
            _rawReportTextBox.Text = textReport;

            // Populate Color-Coded Differential Matrix
            RenderDiffMatrix();

            _statusLabel.Text = string.Format("✓ Analysis complete: Discovered {0} thermal zones, {1} fan channels, and {2} voltage ADC candidates.",
                _activeReport.ThermalCandidates.Count,
                _activeReport.FanPwmCandidates.Count + _activeReport.TachometerCandidates.Count,
                _activeReport.VoltageCandidates.Count);

            _btnCopyReport.Enabled = true;
            _btnSaveReport.Enabled = true;
            _btnSubmitGitHub.Enabled = true;
            _btnApplyProfile.Enabled = true;
            _btnExportJson.Enabled = true;
        }

        private void RenderDiffMatrix()
        {
            if (_activeReport == null) return;

            _matrixTextBox.Clear();
            _matrixTextBox.AppendText("256-BYTE EC DIFFERENTIAL MATRIX (0x00 .. 0xFF):\n");
            _matrixTextBox.AppendText("      00    01    02    03    04    05    06    07    08    09    0A    0B    0C    0D    0E    0F\n");
            _matrixTextBox.AppendText("      ---------------------------------------------------------------------------------------------\n");

            for (int row = 0; row < 16; row++)
            {
                _matrixTextBox.AppendText(string.Format("0x{0:X1}0: ", row));
                for (int col = 0; col < 16; col++)
                {
                    int r = row * 16 + col;
                    byte vi = _activeReport.IdleSnapshot.Registers[r];
                    byte vl = _activeReport.LoadSnapshot.Registers[r];
                    int d = (int)vl - (int)vi;

                    if (d == 0)
                    {
                        _matrixTextBox.SelectionColor = TextDim;
                        _matrixTextBox.AppendText(string.Format("{0:X2}    ", vi));
                    }
                    else if (d > 0)
                    {
                        _matrixTextBox.SelectionColor = AccentAmber;
                        _matrixTextBox.AppendText(string.Format("{0:X2}+{1,-2} ", vl, Math.Min(99, d)));
                    }
                    else
                    {
                        _matrixTextBox.SelectionColor = AccentCyan;
                        _matrixTextBox.AppendText(string.Format("{0:X2}{1,-3} ", vl, Math.Max(-99, d)));
                    }
                }
                _matrixTextBox.AppendText("\n");
            }
        }

        private void CopyReportToClipboard()
        {
            if (_activeReport == null) return;
            string text = EcDifferentialScanner.GenerateTextReport(_activeReport);
            Clipboard.SetText(text);
            _statusLabel.Text = "✓ Differential telemetry report copied to Windows clipboard!";
        }

        private void SaveReportToFile()
        {
            if (_activeReport == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Title = "Save EC Motherboard Differential Report";
                sfd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                sfd.FileName = string.Format("MiniFanControl-EC-Diff-{0:yyyyMMdd-HHmmss}.txt", DateTime.Now);
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(sfd.FileName, EcDifferentialScanner.GenerateTextReport(_activeReport));
                    _statusLabel.Text = "✓ Report saved to: " + sfd.FileName;
                }
            }
        }

        private void SubmitToGitHub()
        {
            if (_activeReport == null) return;
            try
            {
                string textReport = EcDifferentialScanner.GenerateTextReport(_activeReport);
                string dmiProduct = _sysInfo != null ? _sysInfo.ProductName : "Unknown Mini PC";
                string dmiBoard = _sysInfo != null ? _sysInfo.BaseBoard : "Unknown Board";
                string dmiBios = _sysInfo != null ? _sysInfo.BiosVersion : "Unknown";
                string dmiCpu = _sysInfo != null ? _sysInfo.CpuName : "Unknown CPU";

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("### Hardware & System Information");
                sb.AppendLine(string.Format("- **System / Product Name**: {0}", dmiProduct));
                sb.AppendLine(string.Format("- **BaseBoard Product**: {0}", dmiBoard));
                sb.AppendLine(string.Format("- **BIOS Version**: {0}", dmiBios));
                sb.AppendLine(string.Format("- **Processor (APU)**: {0}", dmiCpu));
                sb.AppendLine();
                sb.AppendLine("### Discovered EC Motherboard Differential Telemetry");
                sb.AppendLine("```");
                sb.AppendLine(textReport);
                sb.AppendLine("```");

                string fullSubmission = sb.ToString();
                try { Clipboard.SetText(fullSubmission); } catch { }

                string backupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ec-differential-report.txt");
                try { File.WriteAllText(backupPath, fullSubmission, Encoding.UTF8); } catch { }

                string title = Uri.EscapeDataString(string.Format("Hardware Profile: {0} ({1})", dmiProduct, dmiBoard));
                string url = "https://github.com/kaiserc/MiniFanControl/issues/new?title=" + title;

                OpenUrlSafe(url);

                _statusLabel.Text = "✓ Report copied to clipboard! Opening GitHub New Issue in browser...";

                MessageBox.Show(this,
                    "Your hardware telemetry report has been copied to your Windows clipboard and saved to:\n" + backupPath +
                    "\n\nOpening the GitHub New Issue page in your browser — simply press Ctrl+V to paste into the issue body!",
                    "Submit Hardware Profile to GitHub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open GitHub submission:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyDiscoveredAsCustomProfile()
        {
            if (_activeReport == null) return;
            try
            {
                DeviceProfile custom = DeviceProfile.CreateFromDiffReport(_activeReport, _sysInfo);
                DeviceProfile.SaveCustomProfile(custom);

                if (OnCustomProfileApplied != null)
                {
                    OnCustomProfileApplied(custom);
                }

                _statusLabel.Text = "✓ Activated custom profile: " + custom.DisplayName;

                string msg = string.Format(
                    "✓ Discovered channels successfully activated as your live hardware profile!\n\n" +
                    "Profile: {0}\n\n" +
                    "Discovered Registers:\n" +
                    "  • CPU Thermistor  : Reg 0x{1:X2}\n" +
                    "  • Fan 1 PWM Duty  : Reg 0x{2:X2}\n" +
                    "  • Fan 1 Tach RPM  : Reg 0x{3:X2}..0x{4:X2}\n" +
                    "  • Dual Fans       : {5}\n" +
                    "  • VRM Thermistor  : {6}\n" +
                    "  • Ambient Sensor  : {7}\n\n" +
                    "Saved to 'custom-profile.json'. Fan control sliders and custom curves are now unlocked and active for this Mini PC!",
                    custom.DisplayName,
                    custom.RegCpuTemp,
                    custom.RegFan1Ctrl,
                    custom.RegFan1Hi,
                    custom.RegFan1Lo,
                    custom.HasDualFans ? "Yes (APU + Sys)" : "No (1 Fan)",
                    custom.RegVrmTemp > 0 ? string.Format("Reg 0x{0:X2}", custom.RegVrmTemp) : "None",
                    custom.RegAmbientTemp > 0 ? string.Format("Reg 0x{0:X2}", custom.RegAmbientTemp) : "None");

                MessageBox.Show(this, msg, "Custom Profile Activated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not apply custom profile:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportProfileJson()
        {
            if (_activeReport == null) return;
            try
            {
                DeviceProfile custom = DeviceProfile.CreateFromDiffReport(_activeReport, _sysInfo);
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Title = "Export Device Profile JSON";
                    sfd.Filter = "JSON Profile (*.json)|*.json|All Files (*.*)|*.*";
                    sfd.FileName = "custom-profile.json";
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        File.WriteAllText(sfd.FileName, custom.ToJson(), Encoding.UTF8);
                        _statusLabel.Text = "✓ Exported profile JSON to: " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show(this,
                            "Device profile exported successfully:\n" + sfd.FileName +
                            "\n\nThis JSON profile can be shared with other users of this Mini PC model or submitted to GitHub!",
                            "Profile JSON Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export profile JSON:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void OpenUrlSafe(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                try { Process.Start("explorer.exe", "\"" + url + "\""); } catch { }
            }
        }
    }
}
