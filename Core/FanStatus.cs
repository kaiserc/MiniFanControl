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
        public int CpuLoadPct;
        public int GfxClockMhz;
        public int GfxActivityPct;
        public int GfxVoltageMv;
        public int MemClockMhz;
        public int GpuHotspotTemp;
        public int NpuClockMhz;
        public int NpuActivityPct;
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

        // Tier 2 Motherboard Telemetry (Discovered EC Sensors & Power Rails)
        public int VrmTemp;          // VRM MOSFET / Power Delivery stage thermistor (°C)
        public bool IsVrmEstimated;  // True if VRM temp is modeled from core load rather than discrete thermistor
        public int AmbientTemp;      // Chassis / Motherboard ambient thermistor (°C)
        public bool IsAmbientEstimated; // True if ambient temp is modeled rather than discrete thermistor
        public int DdrTemp;          // RAM / SODIMM thermistor (°C) - primary/hotter module
        public int DdrTemp1;         // Stick 1 / DIMM 0 (Bottom/Slot 0) SPD Hub Temperature (°C)
        public int DdrTemp2;         // Stick 2 / DIMM 1 (Top/Slot 1) SPD Hub Temperature (°C)
        public bool IsDdrEstimated;  // True if DDR temp is modeled rather than read from SPD5118 hub or EC thermistor
        public bool IsApuPowerEstimated; // True if APU total power is estimated from TDP envelope rather than direct rail telemetry
        public int DdrStickCount;    // Number of physical memory modules detected (1 or 2)
        public int DdrTempSpread     // Thermal differential between dual stacked SODIMMs (|Temp1 - Temp2| in °C)
        {
            get
            {
                if (DdrStickCount >= 2 && DdrTemp1 > 0 && DdrTemp2 > 0)
                    return Math.Abs(DdrTemp1 - DdrTemp2);
                return 0;
            }
        }
        public int RamSpeedMhz       // Dual Data Rate (DDR) effective transfer speed (e.g. 5600 MHz / MT/s)
        {
            get { return MemClockMhz * 2; }
        }
        public double DcInputVolts;  // DC Barrel Jack Input Rail (e.g. 19.0V)
        public bool IsThermalThrottling; // True if AMD HTC or PROCHOT thermal throttle is asserted
        public bool IsProchotAsserted;   // True if processor PROCHOT CPU/EXT pin is active

        // Tier 3 AMD SMU / SMN Deep Silicon Telemetry (VRM Currents & Infinity Fabric)
        public double VddTdcCurrentAmps; // Real-time Core VDDCR_VDD current draw (Amperes)
        public double VddEdcCurrentAmps; // Peak Core VDDCR_VDD instantaneous current (Amperes)
        public double SocTdcCurrentAmps; // Real-time SoC VDDCR_SOC current draw (Amperes)
        public double SocEdcCurrentAmps; // Peak SoC VDDCR_SOC instantaneous current (Amperes)
        public int VddTdcLimitAmps;      // Motherboard VRM sustained current limit (Amperes)
        public int VddEdcLimitAmps;      // Motherboard VRM peak electrical design current limit (Amperes)
        public int FabricClockMhz;       // Infinity Fabric Clock (FCLK in MHz, e.g. 2000 MHz)
        public bool IsFabricSynced;      // True if FCLK is synchronized with DDR5 memory fabric
        public string SmuTelemetrySource;// Source description (e.g. "AMD SMU PM Table (0xCF8/0xCFC)")

        // Storage & Memory Telemetry (NVMe SSD & System RAM)
        public int NvmeTemp;
        public int NvmeHealthPct;
        public string NvmeModel;
        public byte NvmeCritWarning;
        public byte NvmeAvailableSpare;
        public byte NvmeWearPct;
        public int NvmeDriveCount;       // Number of detected NVMe SSDs (e.g. 1 or 2)
        public double NvmeReadMBs;       // Total real-time NVMe Read throughput (MB/s)
        public double NvmeWriteMBs;      // Total real-time NVMe Write throughput (MB/s)
        public int NvmeQueueDepth;       // Total NVMe active I/O queue depth
        public int Nvme2Temp;            // Secondary NVMe composite temp (°C) if installed
        public int Nvme2HealthPct;       // Secondary NVMe health %
        public string Nvme2Model;        // Secondary NVMe model name
        public double Nvme2ReadMBs;      // Secondary NVMe Read throughput (MB/s)
        public double Nvme2WriteMBs;     // Secondary NVMe Write throughput (MB/s)
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

        public double NvmePowerWatts
        {
            get
            {
                if (NvmeTemp <= 0) return 0.0;
                // An NVMe PCIe 4.0 M.2 SSD (e.g. Sabrent Rocket 4.0 Plus) consumes:
                // Base idle / low power state: ~0.9W per drive
                // Active reads/writes: scales up to ~5.5W per drive dynamically based on real-time I/O throughput
                double ioThroughputMBs = Math.Max(0.0, NvmeReadMBs + NvmeWriteMBs);
                if (Nvme2Temp > 0) ioThroughputMBs += Math.Max(0.0, Nvme2ReadMBs + Nvme2WriteMBs);
                int driveCount = Math.Max(1, NvmeDriveCount);
                double baseIdle = driveCount * 0.9;
                double activePower = Math.Min(5.5 * driveCount, (ioThroughputMBs / 250.0) * 4.5);
                return Math.Round(baseIdle + activePower, 1);
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
        public bool RadeonFsr;

        public List<string> GetActiveRadeonTags()
        {
            List<string> tags = new List<string>(8);
            if (RadeonFreeSync) tags.Add("FreeSync");
            if (RadeonAntiLag) tags.Add("Anti-Lag");
            if (RadeonAfmf) tags.Add("AFMF");
            if (RadeonFsr) tags.Add("FSR");
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
            if (VrmTemp > 0 || AmbientTemp > 0)
            {
                sb.AppendLine("Motherboard Power Delivery & Thermal Environment:");
                if (VrmTemp > 0) sb.AppendFormat("  • CPU VDDCR_VDD VRM Temperature : {0} °C (SVI3 Power Stage)\r\n", VrmTemp);
                if (AmbientTemp > 0) sb.AppendFormat("  • Chassis / Ambient Temperature : {0} °C (IT8613 Sensor 4)\r\n", AmbientTemp);
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

            if (VddTdcCurrentAmps > 0 || FabricClockMhz > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendLine("Deep Silicon Telemetry (Tier 3 AMD SMU / SMN 0xCF8/0xCFC):");
                if (VddTdcCurrentAmps > 0)
                {
                    sb.AppendFormat("  • VDDCR_VDD Core Current (TDC / Peak EDC): {0:0.0} A / {1:0.0} A {2}\r\n",
                        VddTdcCurrentAmps, VddEdcCurrentAmps, VddTdcLimitAmps > 0 ? string.Format("(VRM Limit: {0}A)", VddTdcLimitAmps) : "");
                }
                if (SocTdcCurrentAmps > 0)
                {
                    sb.AppendFormat("  • VDDCR_SOC Rail Current (TDC / Peak EDC): {0:0.0} A / {1:0.0} A\r\n",
                        SocTdcCurrentAmps, SocEdcCurrentAmps);
                }
                if (FabricClockMhz > 0)
                {
                    sb.AppendFormat("  • Infinity Fabric Clock (FCLK)           : {0} MHz {1}\r\n",
                        FabricClockMhz, IsFabricSynced ? "(1:1 Fabric Synchronized)" : "(Asynchronous)");
                }
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
            if (CpuClockMhz > 0 || CpuPowerWatts > 0 || CpuLoadPct >= 0)
            {
                if (CpuLoadPct >= 0) sb.AppendFormat("  • CPU Total Load                      : {0}%\r\n", CpuLoadPct);
                if (CpuClockMhz > 0) sb.AppendFormat("  • CPU Effective Clock (PMLOG_CLK_CPUCLK): {0} MHz ({1:0.00} GHz)\r\n", CpuClockMhz, CpuClockMhz / 1000.0);
                if (CpuPowerWatts > 0) sb.AppendFormat("  • CPU Core Power (PMLOG_CPU_POWER)     : {0} W\r\n", CpuPowerWatts);
                sb.AppendFormat("  • Thermal Throttling (HTC / PROCHOT)   : {0}\r\n", IsThermalThrottling ? "⚠️ YES (Active)" : "No (Normal)");
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxCpuTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                string loadPeakPart = stats.MaxCpuLoad > 0 ? string.Format(" (Peak Load: {0}%)", stats.MaxCpuLoad) : "";
                sb.AppendFormat("Session CPU Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C{3}  (Click box to reset)\r\n",
                    stats.MinCpuTemp, stats.AvgCpuTemp, stats.MaxCpuTemp, loadPeakPart);
            }
            sb.Append("Primary thermal input used by hardware and custom fan curves.");
            return sb.ToString();
        }

        public string FormatIgpuTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            string hotspotPart = GpuHotspotTemp > 0 ? string.Format(" (Hotspot: {0}°C)", GpuHotspotTemp) : "";
            sb.AppendFormat("Integrated Radeon Graphics Temperature: {0}°C{1}\r\n", GpuTemp, hotspotPart);
            if (GfxClockMhz > 0 || GfxActivityPct >= 0 || GfxVoltageMv > 0 || GfxPowerWatts > 0 || GpuHotspotTemp > 0 || NpuActivityPct >= 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                if (GfxClockMhz > 0) sb.AppendFormat("  • Core Clock (PMLOG_CLK_GFXCLK)         : {0} MHz\r\n", GfxClockMhz);
                if (GfxActivityPct >= 0) sb.AppendFormat("  • iGPU Activity (PMLOG_ACTIVITY_GFX)    : {0}%\r\n", GfxActivityPct);
                if (GfxPowerWatts > 0) sb.AppendFormat("  • iGPU Power (PMLOG_GFX_POWER)          : {0} W\r\n", GfxPowerWatts);
                if (GfxVoltageMv > 0) sb.AppendFormat("  • Core Voltage (PMLOG_GFX_VOLTAGE)      : {0} mV\r\n", GfxVoltageMv);
                if (GpuHotspotTemp > 0) sb.AppendFormat("  • Junction Hotspot (PMLOG_HOTSPOT)      : {0} °C\r\n", GpuHotspotTemp);
                if (NpuActivityPct >= 0 || NpuClockMhz > 0) sb.AppendFormat("  • Ryzen AI NPU (PMLOG_NPU_BUSY_AVG)     : {0}%{1}\r\n",
                    NpuActivityPct >= 0 ? NpuActivityPct : 0, NpuClockMhz > 0 ? string.Format(" @ {0} MHz", NpuClockMhz) : "");
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxGpuTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session iGPU Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C  (Click box to reset)\r\n",
                    stats.MinGpuTemp, stats.AvgGpuTemp, stats.MaxGpuTemp);
            }
            if (!string.IsNullOrEmpty(RadeonSoftwareVer))
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Radeon Software Driver: {0}\r\n", RadeonSoftwareVer);
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
            if (NvmeDriveCount > 1 || Nvme2Temp > 0)
            {
                sb.AppendFormat("NVMe Storage ({0} Drives Detected • ~{1:0.0}W):\r\n", Math.Max(2, NvmeDriveCount), NvmePowerWatts);
                sb.AppendFormat("  • Drive 1 (Slot 1): {0}\r\n", !string.IsNullOrEmpty(NvmeModel) ? NvmeModel : "NVMe SSD 1");
                sb.AppendFormat("    Temp: {0}°C • Health: {1}% ({2}% Wear) • Spare: {3}%\r\n", NvmeTemp, NvmeHealthPct, NvmeWearPct, NvmeAvailableSpare);
                if (NvmeReadMBs > 0.05 || NvmeWriteMBs > 0.05)
                {
                    sb.AppendFormat("    Active Throughput: {0:0.0} MB/s Read • {1:0.0} MB/s Write\r\n", NvmeReadMBs, NvmeWriteMBs);
                }
                sb.AppendFormat("  • Drive 2 (Slot 2): {0}\r\n", !string.IsNullOrEmpty(Nvme2Model) ? Nvme2Model : "NVMe SSD 2");
                sb.AppendFormat("    Temp: {0}°C • Health: {1}%\r\n", Nvme2Temp > 0 ? Nvme2Temp.ToString() : "N/A", Nvme2HealthPct > 0 ? Nvme2HealthPct.ToString() : "N/A");
                if (Nvme2ReadMBs > 0.05 || Nvme2WriteMBs > 0.05)
                {
                    sb.AppendFormat("    Active Throughput: {0:0.0} MB/s Read • {1:0.0} MB/s Write\r\n", Nvme2ReadMBs, Nvme2WriteMBs);
                }
            }
            else
            {
                sb.AppendFormat("NVMe Storage: {0}\r\n", !string.IsNullOrEmpty(NvmeModel) ? NvmeModel : "NVMe Solid State Drive");
                sb.AppendFormat("• Live Composite Temperature: {0} • Estimated Power: ~{1:0.0} W\r\n", NvmeTemp > 0 ? string.Format("{0}°C", NvmeTemp) : "N/A", NvmePowerWatts);
                if (NvmeReadMBs > 0.05 || NvmeWriteMBs > 0.05)
                {
                    sb.AppendFormat("• Real-Time Throughput: {0:0.0} MB/s Read • {1:0.0} MB/s Write{2}\r\n",
                        NvmeReadMBs, NvmeWriteMBs, NvmeQueueDepth > 0 ? string.Format(" (QD: {0})", NvmeQueueDepth) : "");
                }
                sb.AppendFormat("• Drive Health: {0}% ({1}% Lifetime Wear)\r\n", NvmeHealthPct, NvmeWearPct);
                sb.AppendFormat("• Available Spare Blocks: {0}%\r\n", NvmeAvailableSpare);
                sb.AppendFormat("• SMART Reliability Status: {0}\r\n", NvmeCritWarning == 0 ? "Healthy (0x00)" : string.Format("WARNING (0x{0:X2})", NvmeCritWarning));
            }
            if (stats != null && stats.SampleCount > 0 && stats.MaxNvmeTemp > 0)
            {
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendFormat("Session NVMe Temp (Min / Avg / Peak): {0}°C / {1:0.0}°C / {2}°C  (Click box to reset)\r\n",
                    stats.MinNvmeTemp < int.MaxValue ? stats.MinNvmeTemp : 0, stats.AvgNvmeTemp, stats.MaxNvmeTemp);
                sb.AppendFormat("Recent Trend (~90s): {0}\r\n", stats.GetNvmeSparkline());
            }
            sb.AppendLine("────────────────────────────────────────────");
            sb.Append("Query Method: Native Windows IOCTL_STORAGE_QUERY_PROPERTY & IOCTL_DISK_PERFORMANCE.\r\nMulti-drive enumeration accommodates multiple M.2 NVMe slots (PhysicalDrive0-3 / C:).\r\nCoupled to Fan 2 (Sys Fan) for proactive M.2 SSD cooling.\r\nLeft-click to spotlight NVMe on the big graph • Right-click to reset peaks.");
            return sb.ToString();
        }

        public string FormatRamTooltip(TelemetrySessionStats stats = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("System Physical Memory: {0:F1} GB Used / {1:F1} GB Total ({2}% Load)\r\n", RamUsedGb, RamTotalGb, RamLoadPct);
            sb.AppendFormat("• Available Physical RAM: {0:F1} GB\r\n", Math.Max(0.0, RamTotalGb - RamUsedGb));
            if (MemClockMhz > 0)
            {
                string standard = MemClockMhz >= 3200
                    ? string.Format("LPDDR5X-{0}", MemClockMhz * 2)
                    : (MemClockMhz >= 2400 ? string.Format("DDR5-{0}", MemClockMhz * 2) : string.Format("DDR4-{0}", MemClockMhz * 2));
                sb.AppendFormat("• Memory Speed (Dual Data Rate): {0} MT/s ({1})\r\n", MemClockMhz * 2, standard);
                sb.AppendFormat("  - Base Memory Clock (PMLOG_CLK_MEMCLK): {0} MHz (Real MCLK • {1} MT/s Dual Data Rate)\r\n", MemClockMhz, MemClockMhz * 2);
            }
            if (DdrStickCount == 2 && DdrTemp1 > 0 && DdrTemp2 > 0)
            {
                string peak1 = (stats != null && stats.MaxDdrTemp1 > 0) ? string.Format(" • Peak: {0}°C", stats.MaxDdrTemp1) : "";
                string peak2 = (stats != null && stats.MaxDdrTemp2 > 0) ? string.Format(" • Peak: {0}°C", stats.MaxDdrTemp2) : "";
                sb.AppendFormat("• DDR5 SPD Hub Temperatures (Dual Stacked SO-DIMMs):\r\n");
                sb.AppendFormat("  - DIMM 0 (Channel A / Bottom Stick): {0}°C (SPD5118 Hub{1})\r\n", DdrTemp1, peak1);
                sb.AppendFormat("  - DIMM 1 (Channel B / Top Stick)   : {0}°C (SPD5118 Hub{1})\r\n", DdrTemp2, peak2);
                sb.AppendFormat("  - Stack Thermal Differential (ΔT)  : {0}°C\r\n", Math.Abs(DdrTemp1 - DdrTemp2));
            }
            else if (DdrTemp > 0)
            {
                string peak = (stats != null && stats.MaxDdrTemp > 0) ? string.Format(" • Peak: {0}°C", stats.MaxDdrTemp) : "";
                sb.AppendFormat("• DDR5 SPD Hub Temperature: {0}°C ({1} SO-DIMM • JEDEC JESD300 PMIC{2})\r\n", DdrTemp, DdrStickCount == 1 ? "Single" : "Composite", peak);
            }
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
            if (FabricClockMhz > 0)
            {
                sb.AppendFormat("• Infinity Fabric Clock (FCLK): {0} MHz ({1})\r\n",
                    FabricClockMhz, IsFabricSynced ? "1:1 Fabric Synchronized" : "Asynchronous");
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
