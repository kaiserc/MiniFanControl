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
        public bool RtssRunning;
        public double RtssFrametimeMs;
        public int RtssAvgFps;
        public int RtssMinFps;
        public int RtssMaxFps;
        public int RtssP1LowFps;
        public int RtssP01LowFps;
        public string RtssProcessName;
        public string PowerSource;
        public int Fan1Rpm;
        public int Fan2Rpm;
        public string Mode;
        public int ModeCode;
        public int Fan1Pct;
        public int Fan2Pct;
        public int Fan1Manual;
        public int Fan2Manual;

        // Storage & Memory Telemetry (NVMe SSD & System RAM)
        public int NvmeTemp;
        public int NvmeHealthPct;
        public string NvmeModel;
        public byte NvmeCritWarning;
        public byte NvmeAvailableSpare;
        public byte NvmeWearPct;
        public double RamUsedGb;
        public double RamTotalGb;
        public int RamLoadPct;
        public double RamPowerWatts
        {
            get
            {
                if (RamTotalGb <= 0) return 0.0;
                int numModules = RamTotalGb >= 32.0 ? 2 : 1;
                return numModules * (1.3 + (Math.Max(0, Math.Min(100, RamLoadPct)) / 100.0) * 2.2);
            }
        }

        // Active AMD Radeon Software Features (FreeSync, Anti-Lag, AFMF, Chill, Boost, etc.)
        public bool RadeonFeaturesAvailable;
        public string RadeonFeaturesSummary;
        public string RadeonSoftwareVer;
        public bool RadeonFreeSync;
        public bool RadeonAntiLag;
        public bool RadeonAfmf;
        public bool RadeonBoost;
        public bool RadeonChill;
        public bool RadeonRsr;
        public bool RadeonRis;
        public bool RadeonEnhancedSync;
        public bool RadeonSam;

        public List<string> GetActiveRadeonTags()
        {
            List<string> tags = new List<string>(7);
            if (RadeonFreeSync) tags.Add("FreeSync");
            if (RadeonAntiLag) tags.Add("Anti-Lag");
            if (RadeonAfmf) tags.Add("AFMF");
            if (RadeonChill) tags.Add("Chill");
            if (RadeonBoost) tags.Add("Boost");
            if (RadeonRsr) tags.Add("RSR");
            if (RadeonRis) tags.Add("RIS");
            return tags;
        }

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
            if (NvmeTemp > 0)
            {
                sb.AppendFormat("• NVMe Thermal Coupling: Active (M.2 Temp: {0}°C)\r\n", NvmeTemp);
            }
            sb.Append("Left-click to spotlight Sys Fan on big graph • Right-click to reset peaks.");
            return sb.ToString();
        }

        public string FormatNvmeTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("NVMe Storage: {0}\r\n", !string.IsNullOrEmpty(NvmeModel) ? NvmeModel : "NVMe Solid State Drive");
            sb.AppendFormat("• Live Composite Temperature: {0}\r\n", NvmeTemp > 0 ? string.Format("{0}°C", NvmeTemp) : "N/A");
            sb.AppendFormat("• Drive Health: {0}% ({1}% Lifetime Wear)\r\n", NvmeHealthPct, NvmeWearPct);
            sb.AppendFormat("• Available Spare Blocks: {0}%\r\n", NvmeAvailableSpare);
            sb.AppendFormat("• SMART Reliability Status: {0}\r\n", NvmeCritWarning == 0 ? "Healthy (0x00)" : string.Format("WARNING (0x{0:X2})", NvmeCritWarning));
            if (stats != null && stats.SampleCount > 0 && stats.MaxNvmeTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session NVMe Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C  (Click box to reset)\r\n",
                    stats.MinNvmeTemp < int.MaxValue ? stats.MinNvmeTemp : 0, stats.AvgNvmeTemp, stats.MaxNvmeTemp);
                sb.AppendFormat("Recent Trend (~90s): {0}\r\n", stats.GetNvmeSparkline());
            }
            sb.AppendLine("────────────────────────────────────────────");
            sb.Append("Query Method: Native Windows IOCTL_STORAGE_QUERY_PROPERTY (NVMe Log 0x02).\r\nExcludes raw PCI port I/O for zero controller bus contention.\r\nCoupled to Fan 2 (Sys Fan) for proactive M.2 SSD cooling.\r\nLeft-click to spotlight NVMe on the big graph • Right-click to reset peaks.");
            return sb.ToString();
        }

        public string FormatRamTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("System Physical Memory: {0:F1} GB Used / {1:F1} GB Total ({2}% Load)\r\n", RamUsedGb, RamTotalGb, RamLoadPct);
            sb.AppendFormat("• Available Physical RAM: {0:F1} GB\r\n", Math.Max(0.0, RamTotalGb - RamUsedGb));
            if (RamTotalGb > 0)
            {
                int numModules = RamTotalGb >= 32.0 ? 2 : 1;
                double loadWatts = RamPowerWatts;
                sb.AppendFormat("• Estimated DDR5 Power: ~{0:F1} W ({1}x SO-DIMMs @ 1.1V JEDEC PMIC)\r\n", loadWatts, numModules);
            }
            if (SocPowerWatts > 0)
            {
                sb.AppendFormat("• APU Memory Controller & Fabric (VDD_SOC): {0} W\r\n", SocPowerWatts);
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxRamPowerWatts > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session RAM Power (Min / Avg / Peak): ~{0:0.0}W / ~{1:0.0}W / ~{2:0.0}W  (Click box to reset)\r\n",
                    stats.MinRamPowerWatts < double.MaxValue ? stats.MinRamPowerWatts : 0.0, stats.AvgRamPowerWatts, stats.MaxRamPowerWatts);
                sb.AppendFormat("Recent Trend (~90s): {0}\r\n", stats.GetRamPowerSparkline());
            }
            sb.AppendLine("────────────────────────────────────────────");
            sb.Append("Query Method: Win32 GlobalMemoryStatusEx (sub-microsecond latency, zero overhead).\r\nNote: Physical RAM sticks use motherboard/on-DIMM PMIC rails without public Win32 shunt sensors;\r\nMemory controller power is tracked by APU SoC rail.\r\nLeft-click to spotlight RAM Power on the big graph • Right-click to reset peaks.");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tracks session Min / Avg / Peak (Max) telemetry for CPU Temp, iGPU Temp, APU Power, and Fan RPMs,
    /// plus a rolling 120-sample (~3-minute) ring buffer for the Combined Live Telemetry Graph & sparklines.
    /// </summary>
}
