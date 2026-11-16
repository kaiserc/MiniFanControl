using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MiniFanControl
{
    public class EcSnapshot
    {
        public DateTime Timestamp;
        public string Label;
        public byte[] Registers; // 256 bytes (0x00 .. 0xFF)
        public int CpuTemp;
        public int GpuTemp;
        public int Fan1Rpm;
        public int Fan2Rpm;
        public int Fan1Pct;
        public int Fan2Pct;
        public int ApuPowerWatts;

        public EcSnapshot()
        {
            Timestamp = DateTime.Now;
            Label = "Snapshot";
            Registers = new byte[256];
        }
    }

    public enum EcRegisterCategory
    {
        Unchanged,
        ThermalCandidate,
        FanPwmDuty,
        TachometerRpm,
        VoltageAdcSag,
        StatusFlagChange,
        GeneralVariable
    }

    public class EcRegisterDiff
    {
        public byte Register;
        public byte IdleVal;
        public byte LoadVal;
        public int Delta;
        public EcRegisterCategory Category;
        public string SuggestedRole;
        public string Details;
    }

    public class EcDiffReport
    {
        public EcSnapshot IdleSnapshot;
        public EcSnapshot LoadSnapshot;
        public List<EcRegisterDiff> RegisterDiffs = new List<EcRegisterDiff>();
        public List<EcRegisterDiff> ThermalCandidates = new List<EcRegisterDiff>();
        public List<EcRegisterDiff> FanPwmCandidates = new List<EcRegisterDiff>();
        public List<EcRegisterDiff> TachometerCandidates = new List<EcRegisterDiff>();
        public List<EcRegisterDiff> VoltageCandidates = new List<EcRegisterDiff>();
        public List<EcRegisterDiff> FlagCandidates = new List<EcRegisterDiff>();
        public DateTime GeneratedAt = DateTime.Now;
        public string SummaryNotes;
    }

    public static class EcDifferentialScanner
    {
        /// <summary>
        /// Captures an EC snapshot with median filtering across multiple samples to reject bus glitches.
        /// Guaranteed strictly read-only and synchronized via EcBusMutex.
        /// </summary>
        public static EcSnapshot CaptureSnapshot(IHardwareBackend hw, string label = "Snapshot", int sampleCount = 5, int delayMs = 120)
        {
            EcSnapshot snap = new EcSnapshot();
            snap.Label = label;
            snap.Timestamp = DateTime.Now;

            if (hw == null)
                return snap;

            FanStatus st = hw.GetStatus();
            snap.CpuTemp = st.CpuTemp;
            snap.GpuTemp = st.GpuTemp;
            snap.Fan1Rpm = st.Fan1Rpm;
            snap.Fan2Rpm = st.Fan2Rpm;
            snap.Fan1Pct = st.Fan1Pct;
            snap.Fan2Pct = st.Fan2Pct;
            snap.ApuPowerWatts = st.ApuPowerWatts;

            sampleCount = Math.Max(1, Math.Min(15, sampleCount));
            List<byte[]> samples = new List<byte[]>();

            for (int s = 0; s < sampleCount; s++)
            {
                byte[] raw = hw.ReadAllRegisters();
                if (raw != null && raw.Length >= 256)
                {
                    samples.Add(raw);
                }
                if (s < sampleCount - 1 && delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                }
            }

            if (samples.Count == 0)
            {
                return snap;
            }

            // Median filter each register byte across the collected samples
            for (int r = 0; r < 256; r++)
            {
                List<byte> values = new List<byte>(samples.Count);
                for (int s = 0; s < samples.Count; s++)
                {
                    values.Add(samples[s][r]);
                }
                values.Sort();
                snap.Registers[r] = values[values.Count / 2];
            }

            return snap;
        }

        /// <summary>
        /// Executes differential analysis between Idle Baseline and Thermal Load snapshots.
        /// Identifies VRM power delivery thermistors, ambient sensors, fan PWM duty, and voltage rails.
        /// </summary>
        public static EcDiffReport Analyze(EcSnapshot idle, EcSnapshot load, DeviceProfile profile = null)
        {
            EcDiffReport report = new EcDiffReport();
            report.IdleSnapshot = idle ?? new EcSnapshot { Label = "Idle" };
            report.LoadSnapshot = load ?? new EcSnapshot { Label = "Load" };

            byte knownCpu = profile != null ? profile.RegCpuTemp : (byte)0x70;
            byte knownGpu = profile != null ? profile.RegGpuTemp : (byte)0x71;
            byte knownF1Ctrl = profile != null ? profile.RegFan1Ctrl : (byte)0x33;
            byte knownF2Ctrl = profile != null ? profile.RegFan2Ctrl : (byte)0x34;
            byte knownF1Hi = profile != null ? profile.RegFan1Hi : (byte)0x35;
            byte knownF1Lo = profile != null ? profile.RegFan1Lo : (byte)0x36;
            byte knownF2Hi = profile != null ? profile.RegFan2Hi : (byte)0x37;
            byte knownF2Lo = profile != null ? profile.RegFan2Lo : (byte)0x38;

            for (int r = 0; r < 256; r++)
            {
                byte reg = (byte)r;
                byte vIdle = report.IdleSnapshot.Registers[r];
                byte vLoad = report.LoadSnapshot.Registers[r];
                int delta = (int)vLoad - (int)vIdle;

                EcRegisterDiff diff = new EcRegisterDiff
                {
                    Register = reg,
                    IdleVal = vIdle,
                    LoadVal = vLoad,
                    Delta = delta
                };

                // Known registers get immediate attribution
                if (reg == knownCpu)
                {
                    diff.Category = EcRegisterCategory.ThermalCandidate;
                    diff.SuggestedRole = "CPU Die / Core Thermistor (Active EC Reading)";
                    diff.Details = string.Format("Idle: {0}°C -> Load: {1}°C (Δ+{2}°C)", vIdle, vLoad, delta);
                    report.ThermalCandidates.Add(diff);
                }
                else if (reg == knownGpu)
                {
                    diff.Category = EcRegisterCategory.ThermalCandidate;
                    diff.SuggestedRole = "iGPU / APU SoC Thermistor";
                    diff.Details = string.Format("Idle: {0}°C -> Load: {1}°C (Δ+{2}°C)", vIdle, vLoad, delta);
                    report.ThermalCandidates.Add(diff);
                }
                else if (reg == knownF1Ctrl)
                {
                    diff.Category = EcRegisterCategory.FanPwmDuty;
                    diff.SuggestedRole = "Fan 1 (APU) PWM Control Register";
                    diff.Details = string.Format("Idle: 0x{0:X2} ({1}%) -> Load: 0x{2:X2} ({3}%)", vIdle, vIdle & 0x7F, vLoad, vLoad & 0x7F);
                    report.FanPwmCandidates.Add(diff);
                }
                else if (reg == knownF2Ctrl)
                {
                    diff.Category = EcRegisterCategory.FanPwmDuty;
                    diff.SuggestedRole = "Fan 2 (System) PWM Control Register";
                    diff.Details = string.Format("Idle: 0x{0:X2} ({1}%) -> Load: 0x{2:X2} ({3}%)", vIdle, vIdle & 0x7F, vLoad, vLoad & 0x7F);
                    report.FanPwmCandidates.Add(diff);
                }
                else if (reg == knownF1Hi || reg == knownF1Lo || reg == knownF2Hi || reg == knownF2Lo)
                {
                    diff.Category = EcRegisterCategory.TachometerRpm;
                    diff.SuggestedRole = (reg == knownF1Hi || reg == knownF1Lo) ? "Fan 1 Tachometer Byte" : "Fan 2 Tachometer Byte";
                    diff.Details = string.Format("Tach byte: 0x{0:X2} -> 0x{1:X2}", vIdle, vLoad);
                    report.TachometerCandidates.Add(diff);
                }
                else if (delta == 0)
                {
                    diff.Category = EcRegisterCategory.Unchanged;
                    diff.SuggestedRole = "Static Config / Unchanged";
                    diff.Details = string.Format("Steady: 0x{0:X2} ({0})", vIdle);
                }
                // Thermal candidate detection: values in 25°C..105°C with positive temperature rise (Δ >= +4°C)
                else if (vIdle >= 25 && vIdle <= 85 && vLoad >= 30 && vLoad <= 105 && delta >= 4)
                {
                    diff.Category = EcRegisterCategory.ThermalCandidate;
                    if (reg >= 0x72 && reg <= 0x7F)
                    {
                        diff.SuggestedRole = string.Format("VRM / Motherboard Thermal Zone (Bank 0x70, Reg 0x{0:X2})", reg);
                    }
                    else if (reg >= 0x20 && reg <= 0x2F)
                    {
                        diff.SuggestedRole = string.Format("Chassis Ambient / Auxiliary Thermistor (Reg 0x{0:X2})", reg);
                    }
                    else
                    {
                        diff.SuggestedRole = string.Format("Candidate Thermistor / Thermal Zone (Reg 0x{0:X2})", reg);
                    }
                    diff.Details = string.Format("Idle: {0}°C -> Load: {1}°C (Δ+{2}°C)", vIdle, vLoad, delta);
                    report.ThermalCandidates.Add(diff);
                }
                // Fan PWM duty candidate: 0%..100% duty tracking with moderate to large delta
                else if (vIdle <= 100 && vLoad <= 100 && delta >= 15 && vIdle >= 15)
                {
                    diff.Category = EcRegisterCategory.FanPwmDuty;
                    diff.SuggestedRole = string.Format("Candidate Fan PWM Duty Register (Reg 0x{0:X2})", reg);
                    diff.Details = string.Format("Idle: {0}% -> Load: {1}% (Δ+{2}%)", vIdle, vLoad, delta);
                    report.FanPwmCandidates.Add(diff);
                }
                // Voltage ADC rail candidate: High base value (120..250) with slight negative load drop (-1..-5)
                else if (vIdle >= 120 && vIdle <= 250 && delta >= -6 && delta <= -1)
                {
                    diff.Category = EcRegisterCategory.VoltageAdcSag;
                    diff.SuggestedRole = string.Format("Candidate DC Jack / Power Rail ADC (Reg 0x{0:X2})", reg);
                    diff.Details = string.Format("Idle ADC: {0} -> Load ADC: {1} (Load Sag: {2})", vIdle, vLoad, delta);
                    report.VoltageCandidates.Add(diff);
                }
                // Single-bit status flag changes (PROCHOT, thermal trips, mode bits)
                else if (Math.Abs(delta) == 1 || (vIdle ^ vLoad) == 0x80 || (vIdle ^ vLoad) == 0x01)
                {
                    diff.Category = EcRegisterCategory.StatusFlagChange;
                    diff.SuggestedRole = string.Format("EC Hardware Status / State Flag (Reg 0x{0:X2})", reg);
                    diff.Details = string.Format("Bitmask: 0x{0:X2} -> 0x{1:X2} (XOR: 0x{2:X2})", vIdle, vLoad, vIdle ^ vLoad);
                    report.FlagCandidates.Add(diff);
                }
                else
                {
                    diff.Category = EcRegisterCategory.GeneralVariable;
                    diff.SuggestedRole = string.Format("Dynamic Variable (Reg 0x{0:X2})", reg);
                    diff.Details = string.Format("0x{0:X2} -> 0x{1:X2} (Δ{2})", vIdle, vLoad, delta > 0 ? "+" + delta : delta.ToString());
                }

                report.RegisterDiffs.Add(diff);
            }

            // Also check for 16-bit Big-Endian tachometer RPM candidates outside known fans
            for (int r = 0x10; r < 254; r++)
            {
                if ((r >= 0x35 && r <= 0x38) || (r + 1 >= 0x35 && r + 1 <= 0x38)) continue;

                int rpmIdle = (report.IdleSnapshot.Registers[r] << 8) | report.IdleSnapshot.Registers[r + 1];
                int rpmLoad = (report.LoadSnapshot.Registers[r] << 8) | report.LoadSnapshot.Registers[r + 1];

                bool isIdleRpm = (rpmIdle >= 350 && rpmIdle <= 6000) || rpmIdle == 8000;
                bool isLoadRpm = (rpmLoad >= 450 && rpmLoad <= 6500) || rpmLoad == 8000;

                if (isIdleRpm && isLoadRpm && rpmLoad != rpmIdle)
                {
                    EcRegisterDiff rpmDiff = new EcRegisterDiff
                    {
                        Register = (byte)r,
                        IdleVal = (byte)(rpmIdle >> 8),
                        LoadVal = (byte)(rpmLoad >> 8),
                        Delta = rpmLoad - rpmIdle,
                        Category = EcRegisterCategory.TachometerRpm,
                        SuggestedRole = string.Format("Candidate 16-bit Fan Tachometer (Reg 0x{0:X2}..0x{1:X2})", r, r + 1),
                        Details = string.Format("Idle: {0} RPM -> Load: {1} RPM (Δ{2} RPM)",
                            rpmIdle == 8000 ? "0 (Idle)" : rpmIdle.ToString(),
                            rpmLoad == 8000 ? "0 (Idle)" : rpmLoad.ToString(),
                            (rpmLoad - rpmIdle) > 0 ? "+" + (rpmLoad - rpmIdle) : (rpmLoad - rpmIdle).ToString())
                    };
                    report.TachometerCandidates.Add(rpmDiff);
                }
            }

            return report;
        }

        /// <summary>
        /// Generates a comprehensive plain-text and markdown-ready report of the differential analysis.
        /// </summary>
        public static string GenerateTextReport(EcDiffReport report)
        {
            if (report == null) return "No differential data available.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendLine("  MINIFANCONTROL • TIER 2 EC MOTHERBOARD DIFFERENTIAL TELEMETRY REPORT");
            sb.AppendLine("======================================================================");
            sb.AppendLine("Timestamp           : " + report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(string.Format("Baseline (Idle)     : CPU {0}°C • GPU {1}°C • APU Fan {2} RPM • Sys Fan {3} RPM • Power ~{4}W",
                report.IdleSnapshot.CpuTemp, report.IdleSnapshot.GpuTemp, report.IdleSnapshot.Fan1Rpm, report.IdleSnapshot.Fan2Rpm, report.IdleSnapshot.ApuPowerWatts));
            sb.AppendLine(string.Format("Thermal Load        : CPU {0}°C • GPU {1}°C • APU Fan {2} RPM • Sys Fan {3} RPM • Power ~{4}W",
                report.LoadSnapshot.CpuTemp, report.LoadSnapshot.GpuTemp, report.LoadSnapshot.Fan1Rpm, report.LoadSnapshot.Fan2Rpm, report.LoadSnapshot.ApuPowerWatts));
            sb.AppendLine("----------------------------------------------------------------------");

            sb.AppendLine("DISCOVERED TIER 2 THERMAL ZONES (VRM, MOTHERBOARD & CHASSIS):");
            if (report.ThermalCandidates.Count == 0)
            {
                sb.AppendLine("  [!] No thermal rising candidates detected between snapshots.");
                sb.AppendLine("      Tip: Ensure the system was put under sustained thermal load (e.g. Cinebench/Game).");
            }
            else
            {
                foreach (var c in report.ThermalCandidates)
                {
                    sb.AppendFormat("  • [Reg 0x{0:X2}] {1,-38} : {2}\r\n", c.Register, c.SuggestedRole, c.Details);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            sb.AppendLine("FAN CONTROL & TACHOMETER CHANNELS:");
            if (report.FanPwmCandidates.Count == 0 && report.TachometerCandidates.Count == 0)
            {
                sb.AppendLine("  No fan dynamic changes observed between snapshots.");
            }
            else
            {
                foreach (var c in report.FanPwmCandidates)
                {
                    sb.AppendFormat("  • [Reg 0x{0:X2}] {1,-38} : {2}\r\n", c.Register, c.SuggestedRole, c.Details);
                }
                foreach (var c in report.TachometerCandidates)
                {
                    sb.AppendFormat("  • [Reg 0x{0:X2}] {1,-38} : {2}\r\n", c.Register, c.SuggestedRole, c.Details);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            sb.AppendLine("DC POWER & VOLTAGE RAIL ADC CANDIDATES:");
            if (report.VoltageCandidates.Count == 0)
            {
                sb.AppendLine("  None detected with characteristic load sag.");
            }
            else
            {
                foreach (var c in report.VoltageCandidates)
                {
                    sb.AppendFormat("  • [Reg 0x{0:X2}] {1,-38} : {2}\r\n", c.Register, c.SuggestedRole, c.Details);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            sb.AppendLine("HARDWARE STATUS & BITMASK FLAG CHANGES:");
            if (report.FlagCandidates.Count == 0)
            {
                sb.AppendLine("  No single-bit state transitions recorded.");
            }
            else
            {
                foreach (var c in report.FlagCandidates)
                {
                    sb.AppendFormat("  • [Reg 0x{0:X2}] {1,-38} : {2}\r\n", c.Register, c.SuggestedRole, c.Details);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            sb.AppendLine("256-BYTE EC DIFFERENTIAL MATRIX (0x00 .. 0xFF) [Idle -> Load / Delta]:");
            sb.AppendLine("      00    01    02    03    04    05    06    07    08    09    0A    0B    0C    0D    0E    0F");
            sb.AppendLine("      ---------------------------------------------------------------------------------------------");

            for (int row = 0; row < 16; row++)
            {
                sb.AppendFormat("0x{0:X1}0: ", row);
                for (int col = 0; col < 16; col++)
                {
                    int r = row * 16 + col;
                    byte vi = report.IdleSnapshot.Registers[r];
                    byte vl = report.LoadSnapshot.Registers[r];
                    int d = (int)vl - (int)vi;

                    if (d == 0)
                    {
                        sb.AppendFormat("{0:X2}    ", vi);
                    }
                    else if (d > 0)
                    {
                        sb.AppendFormat("{0:X2}+{1,-2} ", vl, Math.Min(99, d));
                    }
                    else
                    {
                        sb.AppendFormat("{0:X2}{1,-3} ", vl, Math.Max(-99, d));
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("======================================================================");
            return sb.ToString();
        }
    }
}
