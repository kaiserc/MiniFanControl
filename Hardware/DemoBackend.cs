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
    public class DemoBackend
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
                NvmeTemp = Math.Max(32, Math.Min(65, _cpuTemp - 10 + _rng.Next(-1, 2))),
                NvmeHealthPct = 96,
                NvmeModel = "Samsung 990 PRO 2TB (Demo)",
                NvmeCritWarning = 0,
                NvmeAvailableSpare = 100,
                NvmeWearPct = 4,
                RamUsedGb = 18.4,
                RamTotalGb = 32.0,
                RamLoadPct = 58,
                RadeonFeaturesAvailable = true,
                RadeonFeaturesSummary = "FreeSync • Anti-Lag • AFMF 2 • Boost",
                RadeonSoftwareVer = "26.10.1",
                RadeonFreeSync = true,
                RadeonAntiLag = true,
                RadeonAfmf = true,
                RadeonBoost = true,
                RadeonRsr = true,
                RadeonRis = true
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
    }

}
