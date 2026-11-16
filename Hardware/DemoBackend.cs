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
    public class DemoBackend : IHardwareBackend
    {
        private int _modeCode = 0;
        private int _fan1Pct = 0;
        private int _fan2Pct = 0;
        private int _fan1Manual = 0;
        private int _fan2Manual = 0;
        private int _cpuTemp = 48;
        private int _gpuTemp = 42;
        private readonly Random _rng = new Random();

        public bool FailWrites { get; set; }

        public FanStatus GetStatus()
        {
            _cpuTemp = Math.Max(36, Math.Min(85, _cpuTemp + _rng.Next(-1, 2)));
            _gpuTemp = Math.Max(32, Math.Min(78, _cpuTemp - 5 + _rng.Next(-1, 2)));

            string[] modes = { "Balanced", "Performance", "Silent" };
            int baseRpm = _modeCode == 1 ? 2600 : (_modeCode == 2 ? 1200 : 1800);
            int f1Rpm = _fan1Manual != 0
                ? (_fan1Pct < 23 ? 0 : (int)(3150 * (_fan1Pct / 100.0)))
                : baseRpm + _rng.Next(-40, 41);
            int f2Rpm = _fan2Manual != 0
                ? (_fan2Pct < 23 ? 0 : (int)(1940 * (_fan2Pct / 100.0)))
                : (int)(baseRpm * 0.65) + _rng.Next(-30, 31);

            int pwrWatts = Math.Max(6, HardwareBackend.EstimateApuPowerWatts(_cpuTemp, _gpuTemp, _modeCode) + _rng.Next(-1, 2));
            int socW = Math.Max(4, (int)Math.Round(pwrWatts * 0.18));
            int gfxW = Math.Max(3, (int)Math.Round((pwrWatts - socW) * 0.25));
            int cpuW = Math.Max(3, pwrWatts - socW - gfxW);

            return new FanStatus
            {
                CpuTemp = _cpuTemp,
                CpuLoadPct = Math.Max(2, Math.Min(98, (_cpuTemp - 38) * 3)),
                GpuTemp = _gpuTemp,
                ApuPowerWatts = pwrWatts,
                CpuPowerWatts = cpuW,
                GfxPowerWatts = gfxW,
                SocPowerWatts = socW,
                ApuPowerLimitWatts = _modeCode == 1 ? 65 : (_modeCode == 2 ? 35 : 54),
                CpuClockMhz = 3800 + (_cpuTemp - 40) * 28,
                GfxClockMhz = 1100 + (_gpuTemp - 35) * 32,
                GfxActivityPct = Math.Max(2, Math.Min(98, (_gpuTemp - 32) * 2)),
                GfxVoltageMv = 780 + (_gpuTemp - 32) * 8,
                MemClockMhz = 2800,
                GpuHotspotTemp = _gpuTemp + 6,
                NpuClockMhz = 1600,
                NpuActivityPct = 14,
                RtssFps = _modeCode == 1 ? (144 + _rng.Next(-3, 4)) : (_modeCode == 2 ? 60 : (120 + _rng.Next(-2, 3))),
                RtssRunning = true,
                RtssFrametimeMs = _modeCode == 1 ? 6.9 : (_modeCode == 2 ? 16.6 : 8.3),
                RtssAvgFps = _modeCode == 1 ? 142 : (_modeCode == 2 ? 59 : 119),
                RtssMinFps = _modeCode == 1 ? 118 : (_modeCode == 2 ? 50 : 98),
                RtssMaxFps = _modeCode == 1 ? 154 : (_modeCode == 2 ? 63 : 128),
                RtssP1LowFps = _modeCode == 1 ? 114 : (_modeCode == 2 ? 46 : 94),
                RtssP01LowFps = _modeCode == 1 ? 102 : (_modeCode == 2 ? 39 : 84),
                RtssProcessName = "Cyberpunk2077.exe",
                PowerSource = "Simulated APU SVI2 Telemetry (Demo Mode)",
                Fan1Rpm = f1Rpm,
                Fan2Rpm = f2Rpm,
                Mode = modes[_modeCode],
                ModeCode = _modeCode,
                Fan1Pct = _fan1Pct,
                Fan2Pct = _fan2Pct,
                Fan1Manual = _fan1Manual,
                Fan2Manual = _fan2Manual,
                VrmTemp = Math.Max(28, Math.Min(105, _cpuTemp + 4)),
                IsVrmEstimated = false,
                AmbientTemp = Math.Max(25, Math.Min(60, 32 + (_cpuTemp - 40) / 4)),
                IsAmbientEstimated = false,
                DdrStickCount = 2,
                DdrTemp1 = Math.Max(25, Math.Min(75, 41 + (_cpuTemp - 40) / 4)),
                DdrTemp2 = Math.Max(28, Math.Min(85, 48 + (_cpuTemp - 40) / 3)),
                DdrTemp = Math.Max(28, Math.Min(85, 48 + (_cpuTemp - 40) / 3)),
                IsDdrEstimated = false,
                IsApuPowerEstimated = false,
                DcInputVolts = Math.Round(19.2 - (_cpuTemp > 60 ? 0.4 : 0.1), 1),
                VddTdcCurrentAmps = Math.Round(12.0 + (_cpuTemp - 36) * 0.48 + _rng.Next(-1, 2) * 0.2, 1),
                VddEdcCurrentAmps = Math.Round(24.0 + (_cpuTemp - 36) * 0.85 + _rng.Next(-1, 2) * 0.3, 1),
                SocTdcCurrentAmps = Math.Round(4.5 + (_gpuTemp - 32) * 0.08, 1),
                SocEdcCurrentAmps = Math.Round(8.2 + (_gpuTemp - 32) * 0.12, 1),
                VddTdcLimitAmps = _modeCode == 1 ? 80 : (_modeCode == 2 ? 45 : 65),
                VddEdcLimitAmps = _modeCode == 1 ? 140 : (_modeCode == 2 ? 80 : 110),
                FabricClockMhz = 2000,
                IsFabricSynced = true,
                SmuTelemetrySource = "Simulated AMD SMU / SMN Silicon Telemetry (Demo Mode)",
                NvmeTemp = Math.Max(32, Math.Min(65, _cpuTemp - 10 + _rng.Next(-1, 2))),
                NvmeHealthPct = 96,
                NvmeModel = "Samsung 990 PRO 2TB (Demo)",
                NvmeCritWarning = 0,
                NvmeAvailableSpare = 100,
                NvmeWearPct = 4,
                NvmeDriveCount = 1,
                NvmeReadMBs = 14.5,
                NvmeWriteMBs = 28.2,
                RamUsedGb = 18.4,
                RamTotalGb = 32.0,
                RamLoadPct = 58,
                RadeonFeaturesAvailable = true,
                RadeonFeaturesSummary = "FreeSync • Anti-Lag • AFMF 2 • FSR • Boost",
                RadeonSoftwareVer = "26.10.1",
                RadeonFreeSync = true,
                RadeonAntiLag = true,
                RadeonAfmf = true,
                RadeonBoost = true,
                RadeonRsr = true,
                RadeonRis = true,
                RadeonFsr = true
            };
        }

        public bool SetMode(int code)
        {
            if (FailWrites) return false;
            if (code < 0 || code > 2) return false;
            _modeCode = code;
            return true;
        }

        public bool SetFan(int fanId, int pct)
        {
            if (FailWrites) return false;
            if (fanId != 1 && fanId != 2) return false;
            if (pct < 0 || pct > 100) return false;
            if (fanId == 1)
            {
                _fan1Manual = pct > 0 ? 1 : 0;
                _fan1Pct = pct;
            }
            else
            {
                _fan2Manual = pct > 0 ? 1 : 0;
                _fan2Pct = pct;
            }
            return true;
        }

        public bool SetAuto()
        {
            if (FailWrites) return false;
            _modeCode = 0;
            _fan1Manual = 0;
            _fan2Manual = 0;
            _fan1Pct = 0;
            _fan2Pct = 0;
            return true;
        }

        public byte[] ReadAllRegisters()
        {
            byte[] regs = new byte[256];
            // Initialize with baseline values
            for (int i = 0; i < 256; i++)
            {
                regs[i] = 0x00;
            }

            // Standard IT5570E / GMKtec K12 mapped registers
            regs[0x31] = (byte)_modeCode;
            regs[0x32] = (byte)_modeCode;
            regs[0x33] = (byte)(_fan1Manual != 0 ? (0x80 | (_fan1Pct & 0x7F)) : 0x24);
            regs[0x34] = (byte)(_fan2Manual != 0 ? (0x80 | (_fan2Pct & 0x7F)) : 0x24);

            int f1Rpm = _fan1Manual != 0
                ? (_fan1Pct < 23 ? 0 : (int)(3150 * (_fan1Pct / 100.0)))
                : (_modeCode == 1 ? 2600 : (_modeCode == 2 ? 1200 : 1800));
            regs[0x35] = (byte)((f1Rpm >> 8) & 0xFF);
            regs[0x36] = (byte)(f1Rpm & 0xFF);

            int f2Rpm = _fan2Manual != 0
                ? (_fan2Pct < 23 ? 0 : (int)(1940 * (_fan2Pct / 100.0)))
                : (_modeCode == 1 ? 1690 : (_modeCode == 2 ? 850 : 1170));
            regs[0x37] = (byte)((f2Rpm >> 8) & 0xFF);
            regs[0x38] = (byte)(f2Rpm & 0xFF);

            // CPU & GPU thermistors
            regs[0x70] = (byte)Math.Max(25, Math.Min(105, _cpuTemp));
            regs[0x71] = (byte)Math.Max(25, Math.Min(105, _gpuTemp));

            // Tier 2 Motherboard Telemetry:
            // 0x72: VRM power stage thermistor (tracks CPU with realistic thermal mass)
            regs[0x72] = (byte)Math.Max(28, Math.Min(105, _cpuTemp + 4));
            // 0x74: Chassis ambient intake thermistor
            regs[0x74] = (byte)Math.Max(25, Math.Min(60, 32 + (_cpuTemp - 40) / 4));
            // 0x75: DDR5 SODIMM thermistor
            regs[0x75] = (byte)Math.Max(25, Math.Min(85, 38 + (_cpuTemp - 40) / 3));
            // 0xE0: 19V DC input barrel jack voltage ADC (192 = 19.2V, sags to 186 under load)
            regs[0xE0] = (byte)Math.Max(160, Math.Min(210, 192 - (_cpuTemp > 60 ? 5 : 1)));
            // 0x24: Status flags (single-bit changes)
            regs[0x24] = (byte)(_cpuTemp > 65 ? 0x81 : 0x01);

            return regs;
        }

        public void Dispose()
        {
        }
    }

}
