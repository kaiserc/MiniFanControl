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
    public struct FanStatus
    {
        public int CpuTemp;
        public int GpuTemp;
        public int ApuPowerWatts;
        public int CpuPowerWatts;
        public int GfxPowerWatts;
        public int SocPowerWatts;
        public int ApuPowerLimitWatts;
        public int CpuClockMhz;
        public int GfxClockMhz;
        public int GfxActivityPct;
        public int GfxVoltageMv;
        public int RtssFps;
        public int Fps { get { return RtssFps; } set { RtssFps = value; } }
        public string PowerSource;
        public int Fan1Rpm;
        public int Fan2Rpm;
        public string Mode;
        public int ModeCode;
        public int Fan1Pct;
        public int Fan2Pct;
        public int Fan1Manual;
        public int Fan2Manual;

        public string FormatPowerTooltip(TelemetrySessionStats stats = null)
        {
            string modeTdpDesc = ApuPowerLimitWatts > 0
                ? string.Format("{0} ({1}W SmartShift Limit)", Mode ?? "Balanced", ApuPowerLimitWatts)
                : (ModeCode == 1 ? "Performance (~65W BIOS limit)"
                    : (ModeCode == 2 ? "Silent (~35W BIOS limit)" : "Balanced (~54W BIOS limit)"));

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Total APU Package Power (PPT): {0} W\r\n", ApuPowerWatts);
            sb.AppendFormat("Active EC Power Envelope: {0}\r\n", modeTdpDesc);
            if (stats != null && stats.SampleCount > 0 && stats.MaxPowerWatts > 0)
            {
                sb.AppendFormat("Session Power (Min / Avg / Peak): {0}W / {1:0.0}W / {2}W  (Click box to reset)\r\n",
                    stats.MinPowerWatts, stats.AvgPowerWatts, stats.MaxPowerWatts);
            }
            sb.AppendLine("────────────────────────────────────────────");
            sb.AppendLine("What this figure is made up of (AMD Ryzen APU Socket):");

            if (CpuPowerWatts > 0 || GfxPowerWatts > 0 || SocPowerWatts > 0)
            {
                sb.AppendLine("Direct Sensor Readings (Measured AMD APU Package PMLOG):");
                sb.AppendFormat("  • CPU Cores + Cache (VDDCR_CPU • PMLOG_CPU_POWER) : {0} W{1}\r\n",
                    CpuPowerWatts, CpuClockMhz > 0 ? string.Format("  ({0} MHz)", CpuClockMhz) : "  (Zen Cores + L2/L3)");
                sb.AppendFormat("  • Radeon iGPU Engine (VDDCR_GFX • PMLOG_GFX_POWER): {0} W{1}\r\n",
                    GfxPowerWatts, GfxClockMhz > 0 ? string.Format("  ({0} MHz, {1}% Load)", GfxClockMhz, GfxActivityPct) : "  (Radeon Graphics CUs)");
                sb.AppendFormat("  • SoC / Memory / I/O (VDDCR_SOC • PMLOG_SOC_POWER): {0} W  (DDR5 Controller, Fabric, PCIe)\r\n", SocPowerWatts);
            }
            else if (ApuPowerWatts > 0)
            {
                int estSoc = Math.Max(4, Math.Min(12, (int)Math.Round(ApuPowerWatts * 0.18)));
                int rem = Math.Max(0, ApuPowerWatts - estSoc);
                double gpuShare = (GpuTemp > CpuTemp - 4 && GpuTemp >= 48) ? 0.45 : 0.22;
                int estGfx = Math.Max(2, (int)Math.Round(rem * gpuShare));
                int estCpu = Math.Max(2, rem - estGfx);
                sb.AppendLine("Algorithmic Breakdown [Estimated - Individual Rail Telemetry Unexposed]:");
                sb.AppendFormat("  • CPU Cores + Cache (VDDCR_CPU) : ~{0} W [Estimated] (Zen Cores + L2/L3)\r\n", estCpu);
                sb.AppendFormat("  • Radeon iGPU Engine (VDDCR_GFX): ~{0} W [Estimated] (Radeon Graphics CUs)\r\n", estGfx);
                sb.AppendFormat("  • SoC / Memory / I/O (VDDCR_SOC): ~{0} W [Estimated] (DDR5 Controller, Fabric, PCIe)\r\n", estSoc);
            }

            sb.AppendLine("────────────────────────────────────────────");
            sb.AppendFormat("Sensor Source: {0}\r\n", !string.IsNullOrEmpty(PowerSource) ? PowerSource : "AMD APU Package Telemetry");
            sb.Append("Note: Measures APU silicon package power only. Excludes DDR5, NVMe, fans & DC losses.");
            return sb.ToString();
        }

        public string FormatCpuTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("APU / CPU Package Temperature: {0}°C (EC Register 0x70 • Tctl/Tdie)\r\n", CpuTemp);
            if (CpuClockMhz > 0 || CpuPowerWatts > 0)
            {
                if (CpuClockMhz > 0) sb.AppendFormat("  • CPU Core Clock (PMLOG_CLK_CPUCLK) : {0} MHz\r\n", CpuClockMhz);
                if (CpuPowerWatts > 0) sb.AppendFormat("  • CPU Core Power (PMLOG_CPU_POWER)  : {0} W\r\n", CpuPowerWatts);
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxCpuTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session CPU Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C  (Click box to reset)\r\n",
                    stats.MinCpuTemp, stats.AvgCpuTemp, stats.MaxCpuTemp);
            }
            sb.Append("Primary thermal input used by hardware and custom fan curves.");
            return sb.ToString();
        }

        public string FormatIgpuTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Integrated Radeon Graphics Temperature: {0}°C\r\n", GpuTemp);
            if (GfxClockMhz > 0 || GfxActivityPct >= 0 || GfxVoltageMv > 0 || GfxPowerWatts > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                if (GfxClockMhz > 0) sb.AppendFormat("  • Core Clock (PMLOG_CLK_GFXCLK)      : {0} MHz\r\n", GfxClockMhz);
                if (GfxActivityPct >= 0) sb.AppendFormat("  • iGPU Activity (PMLOG_ACTIVITY_GFX) : {0}%\r\n", GfxActivityPct);
                if (GfxPowerWatts > 0) sb.AppendFormat("  • iGPU Power (PMLOG_GFX_POWER)       : {0} W\r\n", GfxPowerWatts);
                if (GfxVoltageMv > 0) sb.AppendFormat("  • Core Voltage (PMLOG_GFX_VOLTAGE)   : {0} mV\r\n", GfxVoltageMv);
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxGpuTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session iGPU Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C  (Click box to reset)\r\n",
                    stats.MinGpuTemp, stats.AvgGpuTemp, stats.MaxGpuTemp);
            }
            sb.Append("Reads live Radeon iGPU telemetry via AMD ADL2 (atiadlxx.dll).");
            return sb.ToString();
        }

        public string FormatApuFanTooltip(TelemetrySessionStats stats = null, int ratedMaxRpm = 0, DeviceProfile profile = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("APU / CPU Fan Tachometer: {0} RPM (PWM Duty: {1}%)\r\n", Fan1Rpm, Fan1Pct);
            if (profile != null && profile.Id == "ryzen-hx370-ec")
            {
                sb.AppendLine("• Hardware Sensor: IT8613E Super I/O Register 0x0E/0x19 (High/Low Tachometer Byte)");
            }
            else
            {
                byte hi = profile != null ? profile.RegFan1Hi : (byte)0x35;
                byte lo = profile != null ? profile.RegFan1Lo : (byte)0x36;
                sb.AppendFormat("• Hardware Sensor: EC Register 0x{0:X2}/0x{1:X2} (High/Low Tachometer Byte)\r\n", hi, lo);
            }
            sb.AppendFormat("• Minimum Spin Floor: ~{0}% PWM • Active EC Mode: {1}\r\n", FanCurveProfile.ApuMinSpinPct, Mode ?? "Auto");
            if (stats != null && stats.SampleCount > 0 && stats.MaxFan1Rpm > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session APU Fan (Min / Avg / Peak): {0} RPM / {1:0} RPM / {2} RPM  (Click box to reset)\r\n",
                    stats.MinFan1Rpm < int.MaxValue ? stats.MinFan1Rpm : 0, stats.AvgFan1Rpm, stats.MaxFan1Rpm);
                sb.AppendFormat("Recent Trend (~90s): {0}\r\n", stats.GetFan1Sparkline());
            }
            if (ratedMaxRpm > 0)
            {
                sb.AppendFormat("Calibrated Hardware Ceiling: ~{0} RPM\r\n", ratedMaxRpm);
            }
            sb.Append("Left-click to spotlight APU Fan on big graph • Right-click to reset peaks.");
            return sb.ToString();
        }

        public string FormatSysFanTooltip(TelemetrySessionStats stats = null, int ratedMaxRpm = 0, bool hasDualFans = true, DeviceProfile profile = null)
        {
            if (!hasDualFans)
            {
                return "Single-fan mini PC configuration.\r\nSystem RAM/NVMe secondary fan is disabled.";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("System RAM / NVMe Fan Tachometer: {0} RPM (PWM Duty: {1}%)\r\n", Fan2Rpm, Fan2Pct);
            if (profile != null && profile.Id == "ryzen-hx370-ec")
            {
                sb.AppendLine("• Hardware Sensor: IT8613E Super I/O Register 0x0F/0x1A (High/Low Tachometer Byte)");
            }
            else
            {
                byte hi = profile != null ? profile.RegFan2Hi : (byte)0x37;
                byte lo = profile != null ? profile.RegFan2Lo : (byte)0x38;
                sb.AppendFormat("• Hardware Sensor: EC Register 0x{0:X2}/0x{1:X2} (High/Low Tachometer Byte)\r\n", hi, lo);
            }
            sb.AppendFormat("• Minimum Spin Floor: ~{0}% PWM • Active EC Mode: {1}\r\n", FanCurveProfile.SysMinSpinPct, Mode ?? "Auto");
            if (stats != null && stats.SampleCount > 0 && stats.MaxFan2Rpm > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session Sys Fan (Min / Avg / Peak): {0} RPM / {1:0} RPM / {2} RPM  (Click box to reset)\r\n",
                    stats.MinFan2Rpm < int.MaxValue ? stats.MinFan2Rpm : 0, stats.AvgFan2Rpm, stats.MaxFan2Rpm);
                sb.AppendFormat("Recent Trend (~90s): {0}\r\n", stats.GetFan2Sparkline());
            }
            if (ratedMaxRpm > 0)
            {
                sb.AppendFormat("Calibrated Hardware Ceiling: ~{0} RPM\r\n", ratedMaxRpm);
            }
            sb.Append("Left-click to spotlight Sys Fan on big graph • Right-click to reset peaks.");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tracks session Min / Avg / Peak (Max) telemetry for CPU Temp, iGPU Temp, APU Power, and Fan RPMs,
    /// plus a rolling 120-sample (~3-minute) ring buffer for the Combined Live Telemetry Graph & sparklines.
    /// </summary>
}
