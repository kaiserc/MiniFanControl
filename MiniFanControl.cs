using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
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
                sb.AppendFormat("  • CPU Cores + Cache (VDDCR_CPU) : ~{0} W  (Zen Cores + L2/L3)\r\n", estCpu);
                sb.AppendFormat("  • Radeon iGPU Engine (VDDCR_GFX): ~{0} W  (Radeon Graphics CUs)\r\n", estGfx);
                sb.AppendFormat("  • SoC / Memory / I/O (VDDCR_SOC): ~{0} W  (DDR5 Controller, Fabric, PCIe)\r\n", estSoc);
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

        public string FormatApuFanTooltip(TelemetrySessionStats stats = null, int ratedMaxRpm = 0)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("APU / CPU Fan Tachometer: {0} RPM (PWM Duty: {1}%)\r\n", Fan1Rpm, Fan1Pct);
            sb.AppendLine("• Hardware Sensor: EC Register 0x30/0x31 (High/Low Tachometer Byte)");
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

        public string FormatSysFanTooltip(TelemetrySessionStats stats = null, int ratedMaxRpm = 0, bool hasDualFans = true)
        {
            if (!hasDualFans)
            {
                return "Single-fan mini PC configuration.\r\nSystem RAM/NVMe secondary fan is disabled.";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("System RAM / NVMe Fan Tachometer: {0} RPM (PWM Duty: {1}%)\r\n", Fan2Rpm, Fan2Pct);
            sb.AppendLine("• Hardware Sensor: EC Register 0x32/0x33 (High/Low Tachometer Byte)");
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
    public class TelemetrySessionStats
    {
        public const int HISTORY_LEN = 120;
        private static readonly char[] SparkBlocks = new char[] { '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        public int SampleCount { get; private set; }
        public int MinCpuTemp { get; private set; }
        public int MaxCpuTemp { get; private set; }
        public int MinGpuTemp { get; private set; }
        public int MaxGpuTemp { get; private set; }
        public int MinPowerWatts { get; private set; }
        public int MaxPowerWatts { get; private set; }
        public int MinFan1Rpm { get; private set; }
        public int MaxFan1Rpm { get; private set; }
        public int MinFan2Rpm { get; private set; }
        public int MaxFan2Rpm { get; private set; }
        public int MinGpuLoad { get; private set; }
        public int MaxGpuLoad { get; private set; }
        public int MaxFps { get; private set; }

        private long _sumCpuTemp;
        private long _sumGpuTemp;
        private int _gpuSampleCount;
        private long _sumPowerWatts;
        private int _pwrSampleCount;
        private long _sumFan1Rpm;
        private int _fan1SampleCount;
        private long _sumFan2Rpm;
        private int _fan2SampleCount;

        private readonly List<int> _cpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _pwrHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan1RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan2RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuLoadHist = new List<int>(HISTORY_LEN);

        public double AvgCpuTemp { get { return SampleCount > 0 ? (double)_sumCpuTemp / SampleCount : 0.0; } }
        public double AvgGpuTemp { get { return _gpuSampleCount > 0 ? (double)_sumGpuTemp / _gpuSampleCount : 0.0; } }
        public double AvgPowerWatts { get { return _pwrSampleCount > 0 ? (double)_sumPowerWatts / _pwrSampleCount : 0.0; } }
        public double AvgFan1Rpm { get { return _fan1SampleCount > 0 ? (double)_sumFan1Rpm / _fan1SampleCount : 0.0; } }
        public double AvgFan2Rpm { get { return _fan2SampleCount > 0 ? (double)_sumFan2Rpm / _fan2SampleCount : 0.0; } }

        public List<int> CpuHistory { get { return _cpuHist; } }
        public List<int> GpuHistory { get { return _gpuHist; } }
        public List<int> PowerHistory { get { return _pwrHist; } }
        public List<int> Fan1RpmHistory { get { return _fan1RpmHist; } }
        public List<int> Fan2RpmHistory { get { return _fan2RpmHist; } }
        public List<int> GpuLoadHistory { get { return _gpuLoadHist; } }

        public TelemetrySessionStats()
        {
            Reset();
        }

        public void Reset()
        {
            SampleCount = 0;
            MinCpuTemp = int.MaxValue;
            MaxCpuTemp = 0;
            MinGpuTemp = int.MaxValue;
            MaxGpuTemp = 0;
            MinPowerWatts = int.MaxValue;
            MaxPowerWatts = 0;
            MinFan1Rpm = int.MaxValue;
            MaxFan1Rpm = 0;
            MinFan2Rpm = int.MaxValue;
            MaxFan2Rpm = 0;
            MinGpuLoad = int.MaxValue;
            MaxGpuLoad = 0;
            MaxFps = 0;
            _sumCpuTemp = 0;
            _sumGpuTemp = 0;
            _gpuSampleCount = 0;
            _sumPowerWatts = 0;
            _pwrSampleCount = 0;
            _sumFan1Rpm = 0;
            _fan1SampleCount = 0;
            _sumFan2Rpm = 0;
            _fan2SampleCount = 0;
            _cpuHist.Clear();
            _gpuHist.Clear();
            _pwrHist.Clear();
            _fan1RpmHist.Clear();
            _fan2RpmHist.Clear();
            _gpuLoadHist.Clear();
        }

        public void Record(FanStatus st)
        {
            if (st.CpuTemp <= 0) return;
            SampleCount++;
            if (st.CpuTemp < MinCpuTemp) MinCpuTemp = st.CpuTemp;
            if (st.CpuTemp > MaxCpuTemp) MaxCpuTemp = st.CpuTemp;
            _sumCpuTemp += st.CpuTemp;
            PushSample(_cpuHist, st.CpuTemp);

            int gpuVal = st.GpuTemp > 0 ? st.GpuTemp : (_gpuHist.Count > 0 ? _gpuHist[_gpuHist.Count - 1] : 0);
            PushSample(_gpuHist, gpuVal);
            if (st.GpuTemp > 0)
            {
                _gpuSampleCount++;
                if (st.GpuTemp < MinGpuTemp) MinGpuTemp = st.GpuTemp;
                if (st.GpuTemp > MaxGpuTemp) MaxGpuTemp = st.GpuTemp;
                _sumGpuTemp += st.GpuTemp;
            }

            int pwrVal = st.ApuPowerWatts > 0 ? st.ApuPowerWatts : (_pwrHist.Count > 0 ? _pwrHist[_pwrHist.Count - 1] : 0);
            PushSample(_pwrHist, pwrVal);
            if (st.ApuPowerWatts > 0)
            {
                _pwrSampleCount++;
                if (st.ApuPowerWatts < MinPowerWatts) MinPowerWatts = st.ApuPowerWatts;
                if (st.ApuPowerWatts > MaxPowerWatts) MaxPowerWatts = st.ApuPowerWatts;
                _sumPowerWatts += st.ApuPowerWatts;
            }

            PushSample(_fan1RpmHist, Math.Max(0, st.Fan1Rpm));
            PushSample(_fan2RpmHist, Math.Max(0, st.Fan2Rpm));

            int loadSample = st.RtssFps > 0 ? st.RtssFps : Math.Max(0, st.GfxActivityPct);
            PushSample(_gpuLoadHist, loadSample);
            if (st.GfxActivityPct > 0)
            {
                if (st.GfxActivityPct < MinGpuLoad) MinGpuLoad = st.GfxActivityPct;
                if (st.GfxActivityPct > MaxGpuLoad) MaxGpuLoad = st.GfxActivityPct;
            }
            if (st.RtssFps > MaxFps) MaxFps = st.RtssFps;

            if (st.Fan1Rpm > 0)
            {
                _fan1SampleCount++;
                if (st.Fan1Rpm < MinFan1Rpm) MinFan1Rpm = st.Fan1Rpm;
                if (st.Fan1Rpm > MaxFan1Rpm) MaxFan1Rpm = st.Fan1Rpm;
                _sumFan1Rpm += st.Fan1Rpm;
            }
            if (st.Fan2Rpm > 0)
            {
                _fan2SampleCount++;
                if (st.Fan2Rpm < MinFan2Rpm) MinFan2Rpm = st.Fan2Rpm;
                if (st.Fan2Rpm > MaxFan2Rpm) MaxFan2Rpm = st.Fan2Rpm;
                _sumFan2Rpm += st.Fan2Rpm;
            }
        }

        private static void PushSample(List<int> list, int val)
        {
            if (list.Count >= HISTORY_LEN)
                list.RemoveAt(0);
            list.Add(val);
        }

        public string GetCpuSparkline() { return FormatSparkline(_cpuHist, 2); }
        public string GetGpuSparkline() { return FormatSparkline(_gpuHist, 2); }
        public string GetPowerSparkline() { return FormatSparkline(_pwrHist, 2); }
        public string GetFan1Sparkline() { return FormatSparkline(_fan1RpmHist, 50); }
        public string GetFan2Sparkline() { return FormatSparkline(_fan2RpmHist, 50); }

        public static string FormatSparkline(List<int> samples, int minSpan = 2, int unusedMax = 0)
        {
            if (samples == null || samples.Count == 0) return "";
            int lo = int.MaxValue;
            int hi = int.MinValue;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i] < lo) lo = samples[i];
                if (samples[i] > hi) hi = samples[i];
            }
            if (hi == lo)
            {
                // Steady state: use mid-height block instead of U+2581 which renders as an underline
                return new string('▄', Math.Min(samples.Count, 24));
            }
            int span = Math.Max(Math.Max(1, minSpan), hi - lo);
            int count = Math.Min(samples.Count, 24);
            int startIdx = samples.Count - count;
            char[] chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                int v = samples[startIdx + i];
                int idx = (int)Math.Round(((double)(v - lo) / span) * (SparkBlocks.Length - 1));
                idx = Math.Max(0, Math.Min(SparkBlocks.Length - 1, idx));
                chars[i] = SparkBlocks[idx];
            }
            return new string(chars);
        }
    }

    /// <summary>
    /// Disposable guard providing fail-closed mutual exclusion for Embedded Controller bus access.
    /// Automatically releases acquired mutexes upon disposal.
    /// </summary>
    public struct EcLockScope : IDisposable
    {
        public readonly bool Acquired;
        private readonly bool _holdsEc;
        private readonly bool _holdsIsa;

        internal EcLockScope(bool holdsEc, bool holdsIsa)
        {
            _holdsEc = holdsEc;
            _holdsIsa = holdsIsa;
            Acquired = holdsEc && holdsIsa;
        }

        public void Dispose()
        {
            if (_holdsIsa) EcBusMutex.ReleaseIsaInternal();
            if (_holdsEc) EcBusMutex.ReleaseEcInternal();
        }
    }

    /// <summary>
    /// Synchronizes Embedded Controller (EC) access with CapFrameX, LibreHardwareMonitor, and HWiNFO
    /// using the standard system-wide named mutex "Global\Access_EC".
    /// </summary>
    public static class EcBusMutex
    {
        private static Mutex _ecMutex;
        private static Mutex _isaBusMutex;
        private static Mutex _pciBusMutex;
        private static bool _initDone;
        private static int _contentionCount;

        [ThreadStatic]
        private static bool _threadHoldsEc;
        [ThreadStatic]
        private static bool _threadHoldsIsa;
        [ThreadStatic]
        private static bool _threadHoldsPci;

        public static int ContentionCount
        {
            get { return _contentionCount; }
        }

        public static void EnsureCreated()
        {
            if (_initDone) return;
            _initDone = true;
            _ecMutex = CreateMutex(@"Global\Access_EC");
            _isaBusMutex = CreateMutex(@"Global\Access_ISABUS.HTP.Method");
            _pciBusMutex = CreateMutex(@"Global\Access_PCI");
        }

        private static Mutex CreateMutex(string name)
        {
            try
            {
                MutexSecurity sec = new MutexSecurity();
                SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                sec.AddAccessRule(new MutexAccessRule(everyone, MutexRights.FullControl, AccessControlType.Allow));
                bool createdNew;
                return new Mutex(false, name, out createdNew, sec);
            }
            catch
            {
                try { return Mutex.OpenExisting(name); }
                catch { return null; }
            }
        }

        /// <summary>
        /// Acquires exclusive EC bus access using a fail-closed pattern.
        /// Both Global\Access_EC and Global\Access_ISABUS.HTP.Method must be acquired.
        /// If either fails, any partial lock is immediately released.
        /// </summary>
        public static EcLockScope AcquireEc(int timeoutMs = 35)
        {
            EnsureCreated();
            bool gotEc = false;
            bool gotIsa = false;

            if (_ecMutex != null)
            {
                try { gotEc = _ecMutex.WaitOne(timeoutMs, false); }
                catch (AbandonedMutexException) { gotEc = true; }
                catch { gotEc = false; }
            }
            else
            {
                gotEc = true;
            }

            if (!gotEc)
            {
                Interlocked.Increment(ref _contentionCount);
                return new EcLockScope(false, false);
            }

            if (_isaBusMutex != null)
            {
                try { gotIsa = _isaBusMutex.WaitOne(timeoutMs, false); }
                catch (AbandonedMutexException) { gotIsa = true; }
                catch { gotIsa = false; }
            }
            else
            {
                gotIsa = true;
            }

            if (!gotIsa)
            {
                if (gotEc && _ecMutex != null)
                {
                    try { _ecMutex.ReleaseMutex(); } catch { }
                }
                Interlocked.Increment(ref _contentionCount);
                return new EcLockScope(false, false);
            }

            return new EcLockScope(gotEc, gotIsa);
        }

        internal static void ReleaseEcInternal()
        {
            if (_ecMutex != null)
            {
                try { _ecMutex.ReleaseMutex(); } catch { }
            }
        }

        internal static void ReleaseIsaInternal()
        {
            if (_isaBusMutex != null)
            {
                try { _isaBusMutex.ReleaseMutex(); } catch { }
            }
        }

        public static bool WaitEc(int timeoutMs = 25)
        {
            var scope = AcquireEc(timeoutMs);
            if (scope.Acquired)
            {
                _threadHoldsEc = true;
                _threadHoldsIsa = true;
                return true;
            }
            return false;
        }

        public static void ReleaseEc()
        {
            if (_threadHoldsIsa)
            {
                _threadHoldsIsa = false;
                ReleaseIsaInternal();
            }
            if (_threadHoldsEc)
            {
                _threadHoldsEc = false;
                ReleaseEcInternal();
            }
        }

        public static bool WaitPci(int timeoutMs = 25)
        {
            EnsureCreated();
            if (_pciBusMutex == null) return false;
            try
            {
                bool got = _pciBusMutex.WaitOne(timeoutMs, false);
                _threadHoldsPci = got;
                return got;
            }
            catch (AbandonedMutexException)
            {
                _threadHoldsPci = true;
                return true;
            }
            catch
            {
                _threadHoldsPci = false;
                return false;
            }
        }

        public static void ReleasePci()
        {
            if (_pciBusMutex == null || !_threadHoldsPci) return;
            try
            {
                _threadHoldsPci = false;
                _pciBusMutex.ReleaseMutex();
            }
            catch { }
        }
    }

    public class DeviceProfile
    {
        public string Id;
        public string DisplayName;
        public string ChipsetNotes;
        public ulong EcBaseAddress = 0xFE0B0400UL;
        public uint EcSize = 0x100;
        public byte RegModeStatus = 0x31;
        public byte RegModeCmd    = 0x32;
        public byte RegFan1Ctrl   = 0x33;
        public byte RegFan2Ctrl   = 0x34;
        public byte RegFan1Hi     = 0x35;
        public byte RegFan1Lo     = 0x36;
        public byte RegFan2Hi     = 0x37;
        public byte RegFan2Lo     = 0x38;
        public byte RegCpuTemp    = 0x70;
        public byte RegGpuTemp    = 0x71;
        public bool HasDualFans   = true;

        public static readonly DeviceProfile[] KnownProfiles = new DeviceProfile[]
        {
            new DeviceProfile
            {
                Id = "gmktec-k12",
                DisplayName = "GMKtec NucBox K12 (Ryzen 7 H 255 / Radeon 780M)",
                ChipsetNotes = "ITE IT5570E EC @ 0xFE0B0400 • Dual Fans (APU + RAM/NVMe)"
            },
            new DeviceProfile
            {
                Id = "gmktec-k8-k11",
                DisplayName = "GMKtec NucBox K8 Plus / K11 (Ryzen 8845HS / 8945HS)",
                ChipsetNotes = "ITE IT5570E EC @ 0xFE0B0400 • Dual Fans (APU + System)"
            },
            new DeviceProfile
            {
                Id = "ryzen-hx370-ec",
                DisplayName = "Ryzen AI 9 HX 370 Mini PC (KTHIN PRO / EVO-X1 / TOPC TS-12C)",
                ChipsetNotes = "ITE IT8613E Super I/O @ 0x2E/0x0A30 + EC • Dual Fans • Radeon 890M via ADL2 / PMLOG"
            },
            new DeviceProfile
            {
                Id = "adreamer-ariesbox10",
                DisplayName = "ADREAMER AriesBox 10 / PB07 (Ryzen 7 7840HS / Radeon 780M)",
                ChipsetNotes = "Dual Fans (APU + RAM/System) • Radeon 780M via ADL2 • AMD Zen 4 Phoenix"
            },
            new DeviceProfile
            {
                Id = "generic-it5570e",
                DisplayName = "Generic AMD Ryzen Mini PC (ITE IT5570E / AMI Aptio V)",
                ChipsetNotes = "Standard AMD LPC/eSPI EC window @ 0xFE0B0400"
            }
        };

        public static DeviceProfile FindProfile(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (DeviceProfile p in KnownProfiles)
                {
                    if (p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            return KnownProfiles[0];
        }
    }

    public class SystemHardwareInfo
    {
        public string Manufacturer = "Unknown";
        public string ProductName = "Unknown";
        public string BaseBoard = "Unknown";
        public string BiosVersion = "Unknown";
        public string CpuName = "AMD Ryzen APU";
        public DeviceProfile MatchedProfile = DeviceProfile.KnownProfiles[0];

        public static SystemHardwareInfo Detect()
        {
            SystemHardwareInfo info = new SystemHardwareInfo();
            try
            {
                using (RegistryKey biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (biosKey != null)
                    {
                        info.Manufacturer = ReadRegStr(biosKey, "SystemManufacturer", info.Manufacturer);
                        info.ProductName  = ReadRegStr(biosKey, "SystemProductName", info.ProductName);
                        info.BaseBoard    = ReadRegStr(biosKey, "BaseBoardProduct", info.BaseBoard);
                        info.BiosVersion  = ReadRegStr(biosKey, "BIOSVersion", info.BiosVersion);
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (cpuKey != null)
                    {
                        info.CpuName = ReadRegStr(cpuKey, "ProcessorNameString", info.CpuName);
                    }
                }
            }
            catch { }

            string combined = (info.ProductName + " " + info.BaseBoard + " " + info.CpuName).ToUpperInvariant();
            info.MatchedProfile = MatchProfile(info.ProductName, info.BaseBoard, info.CpuName);

            return info;
        }

        public static DeviceProfile MatchProfile(string productName, string baseBoard, string cpuName)
        {
            string combined = ((productName ?? "") + " " + (baseBoard ?? "") + " " + (cpuName ?? "")).ToUpperInvariant();
            if (combined.Contains("HX 370") || combined.Contains("HX370") || combined.Contains("EVO-X1") || combined.Contains("KTHIN") || combined.Contains("TS-12C") || combined.Contains("STRIX"))
                return DeviceProfile.FindProfile("ryzen-hx370-ec");
            if (combined.Contains("ADREAMER") || combined.Contains("ARIESBOX") || combined.Contains("PB07") || combined.Contains("7840") || combined.Contains("7940"))
                return DeviceProfile.FindProfile("adreamer-ariesbox10");
            if (combined.Contains("K8") || combined.Contains("K11") || combined.Contains("8845") || combined.Contains("8945"))
                return DeviceProfile.FindProfile("gmktec-k8-k11");
            if (combined.Contains("K12") || combined.Contains("H 255") || combined.Contains("H255"))
                return DeviceProfile.FindProfile("gmktec-k12");
            return DeviceProfile.KnownProfiles[0];
        }

        private static string ReadRegStr(RegistryKey key, string valueName, string fallback)
        {
            object val = key.GetValue(valueName);
            if (val is string)
            {
                string s = ((string)val).Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
            else if (val is string[])
            {
                string[] arr = (string[])val;
                if (arr.Length > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0].Trim();
            }
            return fallback;
        }

        public string GetCompactSummary()
        {
            string prod = (ProductName ?? "").Replace("_", " ").Trim();
            if (string.IsNullOrEmpty(prod) || prod.Equals("Default string", StringComparison.OrdinalIgnoreCase) || prod.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                prod = (BaseBoard ?? "").Replace("_", " ").Trim();

            string shortCpu = CpuName
                .Replace("AMD ", "")
                .Replace("Processor", "")
                .Replace("w/ Radeon 780M Graphics", "")
                .Replace("w/ Radeon 890M Graphics", "")
                .Replace("with Radeon Graphics", "")
                .Trim();

            while (shortCpu.Contains("  "))
                shortCpu = shortCpu.Replace("  ", " ");

            if (!string.IsNullOrEmpty(prod) && !prod.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                return string.Format("{0} • {1}", prod, shortCpu);
            return shortCpu;
        }

        public string BuildProbeReport(HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendLine("  MINIFANCONTROL • RYZEN MINI-PC HARDWARE & EC PROBE REPORT");
            sb.AppendLine("======================================================================");
            sb.AppendLine("Timestamp        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Manufacturer     : " + Manufacturer);
            sb.AppendLine("Product Name     : " + ProductName);
            sb.AppendLine("BaseBoard        : " + BaseBoard);
            sb.AppendLine("BIOS Version     : " + BiosVersion);
            sb.AppendLine("CPU / APU        : " + CpuName);
            sb.AppendLine("Active Profile   : " + MatchedProfile.DisplayName);
            sb.AppendLine("EC Communication : " + (hw != null ? hw.AccessMethodName : (demoMode ? "Simulation/Demo" : "ACPI EC Port I/O (0x62/0x66)")));
            sb.AppendLine("EC Physical Base : 0x" + MatchedProfile.EcBaseAddress.ToString("X8") + " (Size: 0x" + MatchedProfile.EcSize.ToString("X2") + ")");
            if (!demoMode)
            {
                sb.AppendLine("----------------------------------------------------------------------");
                sb.AppendLine("LOW-LEVEL LPC & ACPI PORT I/O DIAGNOSTICS:");
                try
                {
                    byte sc66 = HardwareBackend.ReadPortByte(0x66);
                    byte dt62 = HardwareBackend.ReadPortByte(0x62);
                    byte sc6E = HardwareBackend.ReadPortByte(0x6E);
                    byte dt6C = HardwareBackend.ReadPortByte(0x6C);
                    sb.AppendFormat("  ACPI EC Primary   (0x62/0x66) : Status=0x{0:X2}, Data=0x{1:X2}\r\n", sc66, dt62);
                    sb.AppendFormat("  ACPI EC Secondary (0x6C/0x6E) : Status=0x{0:X2}, Data=0x{1:X2}\r\n", sc6E, dt6C);

                    ushort s2eChip, s2eBase;
                    bool s2eOk = HardwareBackend.ProbeSuperIo(0x2E, out s2eChip, out s2eBase);
                    sb.AppendFormat("  Super I/O Port 0x2E Probe     : {0} (Chip ID: 0x{1:X4}, Base: 0x{2:X4})\r\n", s2eOk ? "FOUND" : "Not Detected", s2eChip, s2eBase);

                    ushort s4eChip, s4eBase;
                    bool s4eOk = HardwareBackend.ProbeSuperIo(0x4E, out s4eChip, out s4eBase);
                    sb.AppendFormat("  Super I/O Port 0x4E Probe     : {0} (Chip ID: 0x{1:X4}, Base: 0x{2:X4})\r\n", s4eOk ? "FOUND" : "Not Detected", s4eChip, s4eBase);
                }
                catch (Exception pEx)
                {
                    sb.AppendLine("  Port Probe Exception: " + pEx.Message);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            try
            {
                FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                sb.AppendLine("LIVE SENSOR & FAN READINGS:");
                sb.AppendLine(string.Format("  CPU Temperature (0x70) : {0} °C", st.CpuTemp));
                sb.AppendLine(string.Format("  iGPU Temperature (ADL) : {0} °C", st.GpuTemp));
                sb.AppendLine(string.Format("  APU Power Usage        : {0} W", st.ApuPowerWatts));
                sb.AppendLine(string.Format("  Performance Mode (0x31): {0} (code={1})", st.Mode, st.ModeCode));
                sb.AppendLine(string.Format("  APU Fan (Fan 1, 0x35)  : {0} RPM | Ctrl(0x33)={1}% ({2})",
                    st.Fan1Rpm, st.Fan1Pct, st.Fan1Manual != 0 ? "Manual" : "Auto"));
                sb.AppendLine(string.Format("  Sys Fan (Fan 2, 0x37)  : {0} RPM | Ctrl(0x34)={1}% ({2})",
                    st.Fan2Rpm, st.Fan2Pct, st.Fan2Manual != 0 ? "Manual" : "Auto"));
                sb.AppendLine("----------------------------------------------------------------------");

                if (!demoMode)
                {
                    byte[] regs = hw.ReadAllRegisters();
                    sb.AppendLine(string.Format("EC REGISTER HEX DUMP (0x00 - 0xFE via {0}):", hw != null ? hw.AccessMethodName : "EC"));
                    sb.AppendLine("      00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F");
                    sb.AppendLine("      -----------------------------------------------");
                    for (int row = 0; row < 16; row++)
                    {
                        sb.AppendFormat("0x{0:X1}0: ", row);
                        for (int col = 0; col < 16; col++)
                        {
                            int idx = row * 16 + col;
                            if (idx < regs.Length)
                                sb.AppendFormat("{0:X2} ", regs[idx]);
                            else
                                sb.Append("-- ");
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("----------------------------------------------------------------------");
                    sb.AppendLine("KEY REGISTER ANNOTATIONS (Active Profile Layout):");
                    sb.AppendFormat("  [0x{0:X2}] Mode Status : 0x{1:X2} (0=Balanced, 1=Performance, 2=Silent)\r\n", MatchedProfile.RegModeStatus, regs[MatchedProfile.RegModeStatus]);
                    sb.AppendFormat("  [0x{0:X2}] Mode Cmd    : 0x{1:X2} (0x80=Balanced, 0x81=Performance, 0x82=Silent)\r\n", MatchedProfile.RegModeCmd, regs[MatchedProfile.RegModeCmd]);
                    sb.AppendFormat("  [0x{0:X2}] Fan 1 Ctrl  : 0x{1:X2} (0x00=Auto, 0x80|pct=Manual)\r\n", MatchedProfile.RegFan1Ctrl, regs[MatchedProfile.RegFan1Ctrl]);
                    sb.AppendFormat("  [0x{0:X2}] Fan 2 Ctrl  : 0x{1:X2} (0x00=Auto, 0x80|pct=Manual)\r\n", MatchedProfile.RegFan2Ctrl, regs[MatchedProfile.RegFan2Ctrl]);
                    sb.AppendFormat("  [0x{0:X2}..{1:X2}] Fan1 RPM: 0x{2:X2}{3:X2} ({4} raw)\r\n", MatchedProfile.RegFan1Hi, MatchedProfile.RegFan1Lo, regs[MatchedProfile.RegFan1Hi], regs[MatchedProfile.RegFan1Lo], (regs[MatchedProfile.RegFan1Hi] << 8) | regs[MatchedProfile.RegFan1Lo]);
                    sb.AppendFormat("  [0x{0:X2}..{1:X2}] Fan2 RPM: 0x{2:X2}{3:X2} ({4} raw)\r\n", MatchedProfile.RegFan2Hi, MatchedProfile.RegFan2Lo, regs[MatchedProfile.RegFan2Hi], regs[MatchedProfile.RegFan2Lo], (regs[MatchedProfile.RegFan2Hi] << 8) | regs[MatchedProfile.RegFan2Lo]);
                    sb.AppendFormat("  [0x{0:X2}] CPU Temp    : 0x{1:X2} ({1} °C)\r\n", MatchedProfile.RegCpuTemp, regs[MatchedProfile.RegCpuTemp]);
                    sb.AppendFormat("  [0x{0:X2}] dGPU Temp   : 0x{1:X2} ({1} °C)\r\n", MatchedProfile.RegGpuTemp, regs[MatchedProfile.RegGpuTemp]);
                    sb.AppendLine("----------------------------------------------------------------------");
                    sb.Append(AnalyzeEcCandidates(regs));
                }
                else
                {
                    sb.AppendLine("NOTE: Application is currently in Simulation/Demo Mode.");
                    sb.AppendLine("Uncheck Demo Mode to capture live physical EC registers.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR READING EC HARDWARE: " + ex.Message);
            }

            sb.AppendLine("======================================================================");
            return sb.ToString();
        }

        public static string AnalyzeEcCandidates(byte[] regs)
        {
            if (regs == null || regs.Length < 0x80)
                return "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("AUTO-DISCOVERY & CANDIDATE REGISTER SCAN (FOR NEW MINI PCs):");

            int stdTemp = regs[0x70];
            int stdF1 = (regs[0x35] << 8) | regs[0x36];
            int stdF2 = (regs[0x37] << 8) | regs[0x38];
            byte stdMode = regs[0x31];

            bool stdLayoutValid = (stdTemp >= 25 && stdTemp <= 105) &&
                                  (stdMode <= 2) &&
                                  ((stdF1 >= 300 && stdF1 <= 5000) || stdF1 == 8000 || stdF1 == 0);

            if (stdLayoutValid)
            {
                sb.AppendLine("  [OK] Standard GMKtec / ITE IT5570E layout (0x31..0x38, 0x70) is VALID on this PC!");
            }
            else
            {
                sb.AppendLine("  [!] Standard offsets (0x31..0x38, 0x70) look non-standard on this board.");
                sb.AppendLine("      Check the candidate registers below and add overrides to minifancontrol.cfg:");
            }

            List<string> tempCands = new List<string>();
            for (int r = 0x10; r < regs.Length; r++)
            {
                if (r >= 0x35 && r <= 0x38) continue;
                byte v = regs[r];
                if (v >= 28 && v <= 92)
                {
                    tempCands.Add(string.Format("0x{0:X2}={1}°C", r, v));
                }
            }
            sb.AppendLine("  • Candidate Temp Registers (28..92°C) : " + (tempCands.Count > 0 ? string.Join(", ", tempCands.ToArray()) : "None"));

            List<string> rpmCands = new List<string>();
            for (int r = 0x10; r < regs.Length - 1; r++)
            {
                int be = (regs[r] << 8) | regs[r + 1];
                if ((be >= 400 && be <= 4500) || be == 8000)
                {
                    rpmCands.Add(string.Format("0x{0:X2}..{1:X2}={2}{3}", r, r + 1, be, be == 8000 ? "(Idle)" : " RPM"));
                }
            }
            sb.AppendLine("  • Candidate 16-bit RPM Pairs (BE)     : " + (rpmCands.Count > 0 ? string.Join(", ", rpmCands.ToArray()) : "None"));
            sb.AppendLine("  • Custom Override Keys (minifancontrol.cfg):");
            sb.AppendLine("    RegModeStatus=0x31  RegModeCmd=0x32  RegFan1Ctrl=0x33  RegFan2Ctrl=0x34");
            sb.AppendLine("    RegFan1Hi=0x35      RegFan1Lo=0x36   RegFan2Hi=0x37    RegFan2Lo=0x38    RegCpuTemp=0x70");
            return sb.ToString();
        }
    }

    public struct CurvePoint
    {
        public int Temp;
        public int Pct;

        public CurvePoint(int temp, int pct)
        {
            Temp = temp;
            Pct = pct;
        }
    }

    public class FanCurveProfile
    {
        public const int MIN_SPIN_PCT = 30;
        public static int ApuMinSpinPct = 30;
        public static int SysMinSpinPct = 30;
        public static int ApuMaxRpm = 3150;
        public static int SysMaxRpm = 1940;
        public static string LastCalibratedDate = "";

        public static string FormatCalibrationStatusBadge(bool hasDualFans, out bool isCalibrated, out bool isStale)
        {
            isCalibrated = false;
            isStale = false;
            string raw = (LastCalibratedDate ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return "○ Uncalibrated (Default 30% min — Click to Calibrate)";
            }

            isCalibrated = true;
            string shortDate = raw.Length >= 10 ? raw.Substring(0, 10) : raw;
            DateTime parsedDt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedDt))
            {
                shortDate = parsedDt.ToString("yyyy-MM-dd");
                if ((DateTime.Now - parsedDt).TotalDays > 180)
                {
                    isStale = true;
                }
            }

            string minPart = hasDualFans
                ? string.Format("Min {0}%/{1}%", ApuMinSpinPct, SysMinSpinPct)
                : string.Format("Min {0}%", ApuMinSpinPct);

            if (isStale)
                return string.Format("⚠ Calibrated {0} (>6m ago • {1} — Re-Calibrate)", shortDate, minPart);

            return string.Format("✓ Calibrated {0} ({1})", shortDate, minPart);
        }

        public string Name;
        public bool IsBuiltIn;
        public int BuiltInCode; // 0=Balanced, 1=Performance, 2=Silent, -1=Custom
        public CurvePoint[] ApuPoints; // Fan 1: APU (CPU + iGPU)
        public CurvePoint[] SysPoints; // Fan 2: System (RAM + NVMe)

        public FanCurveProfile(string name, bool isBuiltIn, int builtInCode, CurvePoint[] apuPoints, CurvePoint[] sysPoints)
        {
            Name = name;
            IsBuiltIn = isBuiltIn;
            BuiltInCode = builtInCode;
            ApuPoints = apuPoints;
            SysPoints = sysPoints ?? ClonePoints(apuPoints);
        }

        public static CurvePoint[] ClonePoints(CurvePoint[] src)
        {
            if (src == null) return new CurvePoint[0];
            CurvePoint[] copy = new CurvePoint[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        public FanCurveProfile CloneAsCustom(string newName)
        {
            return new FanCurveProfile(newName, false, -1, ClonePoints(ApuPoints), ClonePoints(SysPoints));
        }

        public static int EvaluateCurve(CurvePoint[] points, int tempC)
        {
            return EvaluateCurve(points, tempC, MIN_SPIN_PCT);
        }

        public static int EvaluateCurve(CurvePoint[] points, int tempC, int minSpinFloor)
        {
            if (points == null || points.Length == 0)
                return 40;

            if (minSpinFloor < 15 || minSpinFloor > 50)
                minSpinFloor = MIN_SPIN_PCT;

            int rawPct;
            bool fromZeroSegment = false;

            if (tempC <= points[0].Temp)
            {
                rawPct = points[0].Pct;
            }
            else
            {
                rawPct = points[points.Length - 1].Pct;
                for (int i = 0; i < points.Length - 1; i++)
                {
                    CurvePoint p0 = points[i];
                    CurvePoint p1 = points[i + 1];
                    if (tempC >= p0.Temp && tempC <= p1.Temp)
                    {
                        if (p1.Temp <= p0.Temp)
                        {
                            rawPct = p1.Pct;
                        }
                        else
                        {
                            fromZeroSegment = (p0.Pct == 0 && p1.Pct > 0);
                            double t = (double)(tempC - p0.Temp) / (p1.Temp - p0.Temp);
                            rawPct = (int)Math.Round(p0.Pct + t * (p1.Pct - p0.Pct));
                        }
                        break;
                    }
                }
            }

            rawPct = Math.Max(0, Math.Min(100, rawPct));
            if (rawPct == 0)
                return 0;

            // Fan motors stall below minSpinFloor (default 30% PWM on K12).
            // On a 0%->spin ramp, stay at 0% (Idle) until past midpoint, then step to >= minSpinFloor so the fan spins cleanly.
            int stepThreshold = Math.Max(12, (minSpinFloor * 3) / 5);
            if (fromZeroSegment && rawPct < stepThreshold)
                return 0;
            if (rawPct > 0 && rawPct < minSpinFloor)
                return minSpinFloor;

            return rawPct;
        }

        public int EvaluateApu(int tempC)
        {
            return EvaluateCurve(ApuPoints, tempC, ApuMinSpinPct);
        }

        public int EvaluateSys(int tempC)
        {
            return EvaluateCurve(SysPoints, tempC, SysMinSpinPct);
        }
    }

    public struct FanCalibrationProgress
    {
        public int CurrentPass;
        public int TotalPasses;
        public int CurrentStepPct;
        public int ProgressPercent;
        public string PhaseTitle;
        public string LogLine;
        public FanStatus LiveStatus;
    }

    public class FanCalibrationResult
    {
        public static readonly int[] CalibrationSteps = new int[] { 20, 22, 24, 26, 28, 30, 32, 35, 60, 100 };

        public bool HasDualFans = true;
        public bool WasCanceled = false;
        public int PassesCompleted = 0;
        public int ApuBareSpinPct = 28;
        public int SysBareSpinPct = 28;
        public int ApuMinSpinPct = 30;
        public int SysMinSpinPct = 30;
        public int ApuMaxRpm = 3150;
        public int SysMaxRpm = 1940;
        public int[] PassApuMin = new int[0];
        public int[] PassSysMin = new int[0];
        public string CalibratedAt = "";
        public string SummaryReport = "";

        public static FanCalibrationResult RunCalibration(
            HardwareBackend hw,
            DemoBackend demo,
            bool demoMode,
            int stepDelayMs,
            int numPasses = 1,
            Action<FanCalibrationProgress> onProgress = null,
            Func<bool> checkCanceled = null)
        {
            if (numPasses < 1) numPasses = 1;
            if (numPasses > 5) numPasses = 5;

            FanCalibrationResult res = new FanCalibrationResult();
            res.PassApuMin = new int[numPasses];
            res.PassSysMin = new int[numPasses];

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendFormat("  MINIFANCONTROL - FAN CALIBRATION REPORT ({0}-PASS VERIFICATION)\r\n", numPasses);
            sb.AppendLine("======================================================================");

            int worstApuBare = 0;
            int worstSysBare = 0;
            int maxApu = 0;
            int maxSys = 0;

            if (!demoMode && stepDelayMs > 0 && stepDelayMs < 5000)
            {
                stepDelayMs = 5000; // Incorporate a 5-second wait before measuring fan speed for rotor stabilization
            }
            int totalUnits = numPasses * (CalibrationSteps.Length + 2);
            int completedUnits = 0;
            int tickMs = stepDelayMs >= 400 ? 250 : Math.Max(10, stepDelayMs / 3);

            try
            {
                for (int pass = 1; pass <= numPasses; pass++)
                {
                    if (checkCanceled != null && checkCanceled())
                    {
                        res.WasCanceled = true;
                        break;
                    }

                    string passHeader = string.Format("--- PASS {0} OF {1} (Spin-Down to 0 RPM + 10-Step Sweep) ---", pass, numPasses);
                    sb.AppendLine(passHeader);
                    EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • Stopping fans (waiting for 0 RPM coast-down)...", pass, numPasses),
                        passHeader, new FanStatus());

                    // 1. Command 1% manual (0x81 = manual stop) and wait until rotors coast to 0 RPM
                    if (demoMode) { demo.SetFan(1, 1); demo.SetFan(2, 1); }
                    else { hw.SetFan(1, 1); hw.SetFan(2, 1); }

                    int maxSpinDownMs = stepDelayMs > 0 ? Math.Max(stepDelayMs * 2, 5500) : 0;
                    if (demoMode && stepDelayMs < 500) maxSpinDownMs = stepDelayMs * 2;

                    int elapsedSpinDown = 0;
                    int zeroConsecutive = 0;
                    FanStatus lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                    while (elapsedSpinDown < maxSpinDownMs)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                        Thread.Sleep(tickMs);
                        elapsedSpinDown += tickMs;
                        lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                        if (lastSt.Fan1Rpm == 0 && lastSt.Fan2Rpm == 0)
                        {
                            zeroConsecutive++;
                            if (zeroConsecutive >= 4) // Confirmed 0 RPM for ~1 second
                                break;
                        }
                        else
                        {
                            zeroConsecutive = 0;
                        }

                        EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits + 1, totalUnits),
                            string.Format("Pass {0}/{1} • Coasting to 0 RPM (APU: {2} RPM, Sys: {3} RPM, {4:0.0}s)...",
                                pass, numPasses, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0),
                            null, lastSt);
                    }

                    if (res.WasCanceled) break;
                    completedUnits += 2;

                    string spinDownMsg = string.Format("  [Pass {0}] Baseline Stop (1% PWM) -> APU: {1,4} RPM | Sys: {2,4} RPM (after {3:0.0}s)",
                        pass, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0);
                    sb.AppendLine(spinDownMsg);
                    EmitProgress(onProgress, pass, numPasses, 1, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • 0 RPM baseline ready", pass, numPasses),
                        spinDownMsg, lastSt);

                    int passApuMin = 0;
                    int passSysMin = 0;

                    // 2. Step through CalibrationSteps (20%..100%) with stabilization polling
                    for (int sIdx = 0; sIdx < CalibrationSteps.Length; sIdx++)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }

                        int pct = CalibrationSteps[sIdx];
                        if (demoMode) { demo.SetFan(1, pct); demo.SetFan(2, pct); }
                        else { hw.SetFan(1, pct); hw.SetFan(2, pct); }

                        int elapsedStep = 0;
                        int sumF1 = 0, sumF2 = 0, sampleCount = 0;

                        if (stepDelayMs <= 0)
                        {
                            lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();
                            sumF1 = lastSt.Fan1Rpm;
                            sumF2 = lastSt.Fan2Rpm;
                            sampleCount = 1;
                        }
                        else
                        {
                            while (elapsedStep < stepDelayMs)
                            {
                                if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                                int sleepChunk = Math.Min(tickMs, stepDelayMs - elapsedStep);
                                Thread.Sleep(sleepChunk);
                                elapsedStep += sleepChunk;

                                lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                                // Average readings during the final 35% of the step window after rotor stabilizes
                                if (elapsedStep >= (stepDelayMs * 65) / 100 || elapsedStep >= stepDelayMs)
                                {
                                    sumF1 += lastSt.Fan1Rpm;
                                    sumF2 += lastSt.Fan2Rpm;
                                    sampleCount++;
                                }

                                double remSec = Math.Max(0.0, (stepDelayMs - elapsedStep) / 1000.0);
                                EmitProgress(onProgress, pass, numPasses, pct, CalcPct(completedUnits, totalUnits),
                                    string.Format("Pass {0}/{1} • Testing {2}% PWM (stabilizing {3:0.0}s left — APU: {4} RPM)",
                                        pass, numPasses, pct, remSec, lastSt.Fan1Rpm),
                                    null, lastSt);
                            }
                        }

                        if (res.WasCanceled) break;
                        completedUnits++;

                        int avgF1 = sampleCount > 0 ? (sumF1 / sampleCount) : lastSt.Fan1Rpm;
                        int avgF2 = sampleCount > 0 ? (sumF2 / sampleCount) : lastSt.Fan2Rpm;

                        bool apuJustSpun = (avgF1 >= 250 && passApuMin == 0);
                        bool sysJustSpun = (avgF2 >= 250 && passSysMin == 0);

                        if (apuJustSpun) passApuMin = pct;
                        if (sysJustSpun) passSysMin = pct;
                        if (avgF1 > maxApu) maxApu = avgF1;
                        if (avgF2 > maxSys) maxSys = avgF2;

                        string note = "";
                        if (apuJustSpun && sysJustSpun) note = "  <-- APU & Sys Spin-Up!";
                        else if (apuJustSpun) note = "  <-- APU Fan Spin-Up!";
                        else if (sysJustSpun) note = "  <-- Sys Fan Spin-Up!";

                        string stepLine = string.Format("  [Pass {0}] Step {1,3}% PWM -> APU Fan: {2,4} RPM | Sys Fan: {3,4} RPM{4}",
                            pass, pct, avgF1, avgF2, note);
                        sb.AppendLine(stepLine);

                        EmitProgress(onProgress, pass, numPasses, pct, CalcPct(completedUnits, totalUnits),
                            string.Format("Pass {0}/{1} • Step {2}% complete (APU: {3} RPM, Sys: {4} RPM)",
                                pass, numPasses, pct, avgF1, avgF2),
                            stepLine, lastSt);
                    }

                    if (res.WasCanceled) break;

                    res.PassApuMin[pass - 1] = passApuMin;
                    res.PassSysMin[pass - 1] = passSysMin;
                    res.PassesCompleted = pass;

                    if (passApuMin > worstApuBare) worstApuBare = passApuMin;
                    if (passSysMin > worstSysBare) worstSysBare = passSysMin;
                }
            }
            finally
            {
                try
                {
                    if (demoMode) demo.SetAuto();
                    else hw.SetAuto();
                }
                catch { }
            }

            res.HasDualFans = (maxSys >= 250);
            res.ApuBareSpinPct = worstApuBare > 0 ? worstApuBare : 28;
            res.SysBareSpinPct = worstSysBare > 0 ? worstSysBare : 28;

            // Add +2% safety margin above the highest bare spin-up threshold across all passes
            res.ApuMinSpinPct = worstApuBare > 0 ? Math.Max(20, Math.Min(45, worstApuBare + 2)) : 30;
            res.SysMinSpinPct = worstSysBare > 0 ? Math.Max(20, Math.Min(45, worstSysBare + 2)) : 30;
            res.ApuMaxRpm = maxApu > 0 ? maxApu : 3150;
            res.SysMaxRpm = maxSys > 0 ? maxSys : (res.HasDualFans ? 1940 : 0);
            res.CalibratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            sb.AppendLine("----------------------------------------------------------------------");
            if (res.WasCanceled)
            {
                sb.AppendLine("  STATUS                : CANCELED BY USER (Fans restored to Auto)");
            }
            else
            {
                sb.AppendFormat("  Calibrated On         : {0}\r\n", res.CalibratedAt);
                sb.AppendFormat("  Passes Completed      : {0} / {1}\r\n", res.PassesCompleted, numPasses);
                if (numPasses > 1)
                {
                    sb.AppendFormat("  APU Bare Spin-Up/Pass : [{0}]% PWM\r\n", string.Join("%, ", Array.ConvertAll(res.PassApuMin, x => x.ToString())));
                    if (res.HasDualFans)
                        sb.AppendFormat("  Sys Bare Spin-Up/Pass : [{0}]% PWM\r\n", string.Join("%, ", Array.ConvertAll(res.PassSysMin, x => x.ToString())));
                }
                sb.AppendFormat("  Detected Fan Layout   : {0}\r\n", res.HasDualFans ? "2 Fans (APU Fan + System RAM/NVMe Fan)" : "1 Fan (APU Fan Only)");
                sb.AppendFormat("  APU Safe Min / Max RPM: {0}% PWM (Bare {1}% + 2% margin) / {2} RPM\r\n",
                    res.ApuMinSpinPct, res.ApuBareSpinPct, res.ApuMaxRpm);
                if (res.HasDualFans)
                    sb.AppendFormat("  Sys Safe Min / Max RPM: {0}% PWM (Bare {1}% + 2% margin) / {2} RPM\r\n",
                        res.SysMinSpinPct, res.SysBareSpinPct, res.SysMaxRpm);
                else
                    sb.AppendLine("  Sys Fan               : Not Detected (Single-Fan Mode Enabled)");
            }
            sb.AppendLine("======================================================================");

            res.SummaryReport = sb.ToString();
            return res;
        }

        private static int CalcPct(int done, int total)
        {
            if (total <= 0) return 0;
            return Math.Max(0, Math.Min(100, (done * 100) / total));
        }

        private static void EmitProgress(
            Action<FanCalibrationProgress> cb,
            int pass,
            int totalPasses,
            int stepPct,
            int progressPct,
            string phaseTitle,
            string logLine,
            FanStatus live)
        {
            if (cb == null) return;
            cb(new FanCalibrationProgress
            {
                CurrentPass = pass,
                TotalPasses = totalPasses,
                CurrentStepPct = stepPct,
                ProgressPercent = progressPct,
                PhaseTitle = phaseTitle,
                LogLine = logLine,
                LiveStatus = live
            });
        }
    }

    public static class NativeInpOut
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH       = 0x00000008;

        public const ulong MIN_SAFE_PHYSICAL_ADDR = 0x000E0000;
        public const uint MAX_SAFE_MAP_SIZE = 16 * 1024 * 1024;

        private static bool _dllSearchHardened = false;
        private static IntPtr _hDriverDll = IntPtr.Zero;

        public static void HardenDllSearchPath()
        {
            if (_dllSearchHardened) return;
            try
            {
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
                _dllSearchHardened = true;
            }
            catch { }
        }

        public static bool PreloadDriverDll()
        {
            HardenDllSearchPath();
            if (_hDriverDll != IntPtr.Zero) return true;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(appDir, "inpoutx64.dll");

            if (!File.Exists(dllPath))
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                dllPath = Path.Combine(sys32, "inpoutx64.dll");
                if (!File.Exists(dllPath)) return false;
            }

            try
            {
                FileInfo fi = new FileInfo(dllPath);
                if (fi.Length < 1024) return false;

                _hDriverDll = LoadLibraryEx(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                return _hDriverDll != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("inpoutx64.dll", EntryPoint = "IsInpOutDriverOpen")]
        public static extern int IsInpOutDriverOpen();

        [DllImport("inpoutx64.dll", EntryPoint = "MapPhysToLin")]
        public static extern IntPtr MapPhysToLin(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle);

        public static IntPtr MapPhysToLinSafe(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle)
        {
            pPhysicalMemoryHandle = IntPtr.Zero;
            ulong addr = unchecked((ulong)pbPhysAddr.ToInt64());
            if (addr < MIN_SAFE_PHYSICAL_ADDR || dwPhysSize == 0 || dwPhysSize > MAX_SAFE_MAP_SIZE)
            {
                return IntPtr.Zero;
            }
            return MapPhysToLin(pbPhysAddr, dwPhysSize, out pPhysicalMemoryHandle);
        }

        [DllImport("inpoutx64.dll", EntryPoint = "UnmapPhysicalMemory")]
        public static extern int UnmapPhysicalMemory(IntPtr PhysicalMemoryHandle, IntPtr pbLinAddr);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortReadPortUchar")]
        public static extern byte DlPortReadPortUchar(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortWritePortUchar")]
        public static extern void DlPortWritePortUchar(ushort port, byte value);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortReadPortUlong")]
        public static extern uint DlPortReadPortUlong(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortWritePortUlong")]
        public static extern void DlPortWritePortUlong(ushort port, uint value);

        [DllImport("inpoutx64.dll", EntryPoint = "Inp32")]
        public static extern int Inp32(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "Out32")]
        public static extern void Out32(ushort port, short value);
    }

    public static class DriverServiceManager
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        }

        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint SERVICE_STOP = 0x0020;
        private const uint SERVICE_CONTROL_STOP = 0x00000001;

        public const int SERVICE_STOPPED = 1;
        public const int SERVICE_RUNNING = 4;

        public static bool IsDriverServiceRunning(string serviceName = "inpoutx64")
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            try
            {
                IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero) return false;
                try
                {
                    SERVICE_STATUS status;
                    if (QueryServiceStatus(svc, out status))
                    {
                        return status.dwCurrentState == SERVICE_RUNNING;
                    }
                    return false;
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static bool StopDriverService(string serviceName = "inpoutx64")
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero) return false;
            try
            {
                IntPtr svc = OpenService(scm, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero) return false;
                try
                {
                    SERVICE_STATUS status;
                    return ControlService(svc, SERVICE_CONTROL_STOP, out status);
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }
    }

    /// <summary>
    /// Passive in-game FPS and framerate monitor.
    /// Safely reads live framerate metrics from RTSS (RivaTuner Statistics Server / CapFrameX / MSI Afterburner)
    /// shared memory without injecting any DLL or triggering anti-cheat heuristics in games.
    /// </summary>
    public static class FpsMonitor
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint FILE_MAP_READ = 0x0004;

        /// <summary>
        /// Reads live in-game FPS from RTSS shared memory if available.
        /// Returns -1 if RTSS is not running or no 3D app is currently presenting.
        /// </summary>
        public static int TryGetRtssFps()
        {
            IntPtr hMap = IntPtr.Zero;
            IntPtr pView = IntPtr.Zero;
            try
            {
                hMap = OpenFileMapping(FILE_MAP_READ, false, "RTSSSharedMemoryV2");
                if (hMap == IntPtr.Zero)
                    return -1;

                pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)65536);
                if (pView == IntPtr.Zero)
                    return -1;

                // Check signature 'RTSS' (0x53535452)
                int sig = Marshal.ReadInt32(pView, 0);
                if (sig != 0x53535452)
                    return -1;

                int appEntrySize = Marshal.ReadInt32(pView, 8);
                int appArrOffset = Marshal.ReadInt32(pView, 12);
                int appArrSize   = Marshal.ReadInt32(pView, 16);

                if (appEntrySize <= 0 || appArrOffset <= 0 || appArrSize <= 0)
                    return -1;

                int maxFps = -1;
                for (int i = 0; i < appArrSize; i++)
                {
                    IntPtr pEntry = new IntPtr(pView.ToInt64() + appArrOffset + i * appEntrySize);
                    int pid = Marshal.ReadInt32(pEntry, 0);
                    if (pid == 0) continue;

                    // dwInstantaneousFramerate is at offset 280 (framerate * 10)
                    int rawFps10 = Marshal.ReadInt32(pEntry, 280);
                    int fps = (rawFps10 > 0) ? (rawFps10 / 10) : 0;
                    if (fps > maxFps)
                        maxFps = fps;
                }
                return maxFps;
            }
            catch
            {
                return -1;
            }
            finally
            {
                if (pView != IntPtr.Zero) UnmapViewOfFile(pView);
                if (hMap != IntPtr.Zero) CloseHandle(hMap);
            }
        }
    }

    public class AmdAdlGpuSensor : IDisposable
    {
        // Official AMD ADL_PMLOG_SENSORS IDs (verified from RadeonTuner / adl_defines.h)
        private const int ADL_PMLOG_CLK_GFXCLK           = 1;
        private const int ADL_PMLOG_CLK_MEMCLK           = 2;
        private const int ADL_PMLOG_TEMPERATURE_EDGE     = 8;
        private const int ADL_PMLOG_SOC_VOLTAGE          = 16;
        private const int ADL_PMLOG_SOC_POWER            = 17;
        private const int ADL_PMLOG_INFO_ACTIVITY_GFX    = 19;
        private const int ADL_PMLOG_GFX_VOLTAGE          = 21;
        private const int ADL_PMLOG_ASIC_POWER           = 23;
        private const int ADL_PMLOG_TEMPERATURE_HOTSPOT  = 27;
        private const int ADL_PMLOG_TEMPERATURE_GFX      = 28;
        private const int ADL_PMLOG_TEMPERATURE_SOC      = 29;
        private const int ADL_PMLOG_GFX_POWER            = 30;
        private const int ADL_PMLOG_TEMPERATURE_CPU      = 32;
        private const int ADL_PMLOG_CPU_POWER            = 33;
        private const int ADL_PMLOG_CLK_CPUCLK           = 34;
        private const int ADL_PMLOG_SMART_POWERSHIFT_CPU = 38;
        private const int ADL_PMLOG_SSPAIRED_ASICPOWER   = 46;
        private const int ADL_PMLOG_SSTOTAL_POWERLIMIT   = 47;
        private const int ADL_PMLOG_SSAPU_POWERLIMIT     = 48;
        private const int ADL_PMLOG_CLK_NPUCLK           = 71;
        private const int ADL_PMLOG_NPU_BUSY_AVG         = 72;
        private const int ADL_PMLOG_BOARD_POWER          = 73;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ADL_Main_Memory_Alloc_Delegate(int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct ADLPMLogDataOutput
        {
            public int size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256 * 2)]
            public int[] sensors;
        }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc_Delegate callback, int iEnumConnectedAdapters, out IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int iAdapterIndex, out ADLPMLogDataOutput output);

        private readonly ADL_Main_Memory_Alloc_Delegate _allocDelegate;
        private IntPtr _context = IntPtr.Zero;
        private int _numAdapters = 0;
        private int _preferredAdapterIndex = -1;
        private bool _initAttempted = false;

        public AmdAdlGpuSensor()
        {
            _allocDelegate = new ADL_Main_Memory_Alloc_Delegate(AllocCallback);
        }

        private static IntPtr AllocCallback(int size)
        {
            return Marshal.AllocHGlobal(size);
        }

        private void EnsureInitialized()
        {
            if (_initAttempted)
                return;
            _initAttempted = true;

            try
            {
                if (ADL2_Main_Control_Create(_allocDelegate, 1, out _context) == 0 && _context != IntPtr.Zero)
                {
                    ADL2_Adapter_NumberOfAdapters_Get(_context, out _numAdapters);
                }
            }
            catch
            {
                _context = IntPtr.Zero;
                _numAdapters = 0;
            }
        }

        private static int ReadSensorVal(int[] sensors, int id, int minVal, int maxVal)
        {
            if (sensors == null || id < 0 || (id * 2 + 1) >= sensors.Length)
                return 0;
            int supported = sensors[id * 2];
            int val = sensors[id * 2 + 1];
            if (supported != 0 && val >= minVal && val <= maxVal)
                return val;
            return 0;
        }

        public void ReadTelemetry(out int igpuTemp, out int apuPowerWatts)
        {
            int cpuT, cpuP, gfxP, socP, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt;
            string src;
            ReadTelemetryDetailed(out igpuTemp, out cpuT, out apuPowerWatts, out cpuP, out gfxP, out socP,
                out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out src);
        }

        public void ReadTelemetryDetailed(
            out int igpuTemp,
            out int adlCpuTemp,
            out int apuPowerWatts,
            out int cpuPowerWatts,
            out int gfxPowerWatts,
            out int socPowerWatts,
            out int apuPowerLimitWatts,
            out int cpuClockMhz,
            out int gfxClockMhz,
            out int gfxActivityPct,
            out int gfxVoltageMv,
            out string powerSource)
        {
            igpuTemp = 0;
            adlCpuTemp = 0;
            apuPowerWatts = 0;
            cpuPowerWatts = 0;
            gfxPowerWatts = 0;
            socPowerWatts = 0;
            apuPowerLimitWatts = 0;
            cpuClockMhz = 0;
            gfxClockMhz = 0;
            gfxActivityPct = -1;
            gfxVoltageMv = 0;
            powerSource = "";
            EnsureInitialized();
            if (_context == IntPtr.Zero || _numAdapters <= 0)
                return;

            try
            {
                for (int pass = 0; pass < _numAdapters; pass++)
                {
                    int i = (_preferredAdapterIndex >= 0 && _preferredAdapterIndex < _numAdapters)
                        ? ((pass == 0) ? _preferredAdapterIndex : (pass <= _preferredAdapterIndex ? pass - 1 : pass))
                        : pass;
                    ADLPMLogDataOutput pm;
                    if (ADL2_New_QueryPMLogData_Get(_context, i, out pm) == 0 && pm.sensors != null)
                    {
                        foreach (int sensorId in new[] { ADL_PMLOG_TEMPERATURE_GFX, ADL_PMLOG_TEMPERATURE_SOC, ADL_PMLOG_TEMPERATURE_EDGE })
                        {
                            int t = ReadSensorVal(pm.sensors, sensorId, 1, 140);
                            if (t > 0)
                            {
                                igpuTemp = t;
                                break;
                            }
                        }

                        adlCpuTemp = ReadSensorVal(pm.sensors, ADL_PMLOG_TEMPERATURE_CPU, 1, 140);
                        cpuPowerWatts = ReadSensorVal(pm.sensors, ADL_PMLOG_CPU_POWER, 1, 140);
                        gfxPowerWatts = ReadSensorVal(pm.sensors, ADL_PMLOG_GFX_POWER, 1, 140);
                        socPowerWatts = ReadSensorVal(pm.sensors, ADL_PMLOG_SOC_POWER, 1, 80);
                        apuPowerLimitWatts = ReadSensorVal(pm.sensors, ADL_PMLOG_SSAPU_POWERLIMIT, 10, 200);
                        if (apuPowerLimitWatts == 0)
                            apuPowerLimitWatts = ReadSensorVal(pm.sensors, ADL_PMLOG_SSTOTAL_POWERLIMIT, 10, 200);

                        cpuClockMhz = ReadSensorVal(pm.sensors, ADL_PMLOG_CLK_CPUCLK, 100, 7000);
                        gfxClockMhz = ReadSensorVal(pm.sensors, ADL_PMLOG_CLK_GFXCLK, 50, 4500);
                        gfxActivityPct = ReadSensorVal(pm.sensors, ADL_PMLOG_INFO_ACTIVITY_GFX, 0, 100);
                        gfxVoltageMv = ReadSensorVal(pm.sensors, ADL_PMLOG_GFX_VOLTAGE, 200, 2000);

                        // 1. Check SmartShift Paired APU Power / Board Power / ASIC Power first
                        foreach (int pwrId in new[] { ADL_PMLOG_SSPAIRED_ASICPOWER, ADL_PMLOG_BOARD_POWER, ADL_PMLOG_ASIC_POWER })
                        {
                            int p = ReadSensorVal(pm.sensors, pwrId, 1, 200);
                            if (p > 0)
                            {
                                apuPowerWatts = p;
                                powerSource = "AMD ADL2 SmartShift / SVI2 Package Sensor (atiadlxx.dll)";
                                break;
                            }
                        }

                        // 2. Otherwise sum live SVI2 rails (CPU + GFX + SoC)
                        int railSum = cpuPowerWatts + gfxPowerWatts + socPowerWatts;
                        if (apuPowerWatts == 0 && railSum > 0 && railSum <= 200)
                        {
                            apuPowerWatts = railSum;
                            powerSource = "AMD ADL2 SVI2 Rail Sum (PMLOG_CPU + GFX + SOC)";
                        }

                        if (igpuTemp > 0 || apuPowerWatts > 0 || gfxPowerWatts > 0)
                        {
                            _preferredAdapterIndex = i;
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public int ReadIgpuTemperature()
        {
            int t, p;
            ReadTelemetry(out t, out p);
            return t;
        }

        public void Dispose()
        {
            if (_context != IntPtr.Zero)
            {
                try
                {
                    ADL2_Main_Control_Destroy(_context);
                }
                catch { }
                _context = IntPtr.Zero;
            }
        }
    }

    public class HardwareBackend : IDisposable
    {
        public const ulong EC_BASE = 0xFE0B0400UL;
        public const uint EC_SIZE = 0xFF;

        public const byte REG_MODE_STATUS = 0x31;
        public const byte REG_MODE_CMD    = 0x32;
        public const byte REG_FAN1_CTRL   = 0x33;
        public const byte REG_FAN2_CTRL   = 0x34;
        public const byte REG_FAN1_HI     = 0x35;
        public const byte REG_FAN1_LO     = 0x36;
        public const byte REG_FAN2_HI     = 0x37;
        public const byte REG_FAN2_LO     = 0x38;
        public const byte REG_CPU_TEMP    = 0x70;
        public const byte REG_GPU_TEMP    = 0x71;

        public enum EcAccessMethod
        {
            Unknown = 0, // Hardware not detected or unverified
            PortIo62,    // Standard ACPI EC ports 0x62 (Data) / 0x66 (Command/Status)
            PortIo6C,    // Secondary ACPI EC ports 0x6C (Data) / 0x6E (Command/Status)
            SuperIo2E,   // Super I/O (IT87xx / IT86xx) via LPC Port 0x2E
            SuperIo4E,   // Super I/O (IT87xx / IT86xx) via LPC Port 0x4E
            Mmio         // Memory-Mapped I/O (e.g. 0xFE0B0400)
        }

        public DeviceProfile Profile = DeviceProfile.KnownProfiles[0];
        public EcAccessMethod AccessMethod = EcAccessMethod.Unknown;
        public ushort SuperIoChipId = 0;
        public ushort SuperIoBaseAddr = 0;
        private static bool _superIoFan3Present = false;

        public const int EMERGENCY_TEMP_TRIP_C = 90;
        public const int EMERGENCY_TEMP_RECOVER_C = 75;

        public static bool CheckEmergencyThermalTripwire(int temp, ref bool activeState)
        {
            if (temp >= EMERGENCY_TEMP_TRIP_C)
            {
                activeState = true;
            }
            else if (activeState && temp <= EMERGENCY_TEMP_RECOVER_C)
            {
                activeState = false;
            }
            return activeState;
        }

        public string AccessMethodName
        {
            get
            {
                switch (AccessMethod)
                {
                    case EcAccessMethod.PortIo62: return "ACPI EC Port I/O (0x62 / 0x66)";
                    case EcAccessMethod.PortIo6C: return "ACPI EC Port I/O (0x6C / 0x6E)";
                    case EcAccessMethod.SuperIo2E: return string.Format("Super I/O IT{0:X4} @ LPC 0x2E (HW Monitor Base: 0x{1:X4})", SuperIoChipId, SuperIoBaseAddr);
                    case EcAccessMethod.SuperIo4E: return string.Format("Super I/O IT{0:X4} @ LPC 0x4E (HW Monitor Base: 0x{1:X4})", SuperIoChipId, SuperIoBaseAddr);
                    case EcAccessMethod.Mmio: return string.Format("Physical MMIO (0x{0:X8})", Profile != null ? Profile.EcBaseAddress : EC_BASE);
                    default: return "Unverified Hardware / Safe Read-Only";
                }
            }
        }

        private const byte EC_CMD_READ  = 0x80;
        private const byte EC_CMD_WRITE = 0x81;
        private const byte EC_FLAG_OBF  = 0x01;
        private const byte EC_FLAG_IBF  = 0x02;

        private IntPtr _linAddr = IntPtr.Zero;
        private IntPtr _physHandle = IntPtr.Zero;
        private bool _isInitialized = false;
        private readonly AmdAdlGpuSensor _adlSensor = new AmdAdlGpuSensor();

        public HardwareBackend()
        {
        }

        public HardwareBackend(DeviceProfile profile)
        {
            if (profile != null) Profile = profile;
        }

        public static bool IsPortWhitelisted(ushort port, bool isWrite)
        {
            if (isWrite)
            {
                if (port == 0x70 || port == 0x71 || port == 0x92 || port == 0xCF9 ||
                    port == 0x20 || port == 0x21 || port == 0xA0 || port == 0xA1 ||
                    port == 0x40 || port == 0x41 || port == 0x42 || port == 0x43 ||
                    port == 0x61)
                {
                    return false;
                }
            }

            if (port == 0x62 || port == 0x66 || port == 0x68 || port == 0x6C || port == 0x6A || port == 0x6E)
                return true;

            if (port == 0x2E || port == 0x2F || port == 0x4E || port == 0x4F)
                return true;

            if (port == 0xCF8 || port == 0xCFC)
                return true;

            if ((port >= 0x0290 && port <= 0x0297) ||
                (port >= 0x02A0 && port <= 0x02A7) ||
                (port >= 0x0A00 && port <= 0x0A3F) ||
                (port >= 0x0E00 && port <= 0x0E3F))
            {
                return true;
            }

            return !isWrite;
        }

        public static byte ReadPortByte(ushort port)
        {
            if (!IsPortWhitelisted(port, false)) return 0xFF;
            try
            {
                byte v = NativeInpOut.DlPortReadPortUchar(port);
                if (v != 0xFF) return v;
                int alt = NativeInpOut.Inp32(port);
                return (byte)(alt & 0xFF);
            }
            catch
            {
                return 0xFF;
            }
        }

        public static void WritePortByte(ushort port, byte val)
        {
            if (!IsPortWhitelisted(port, true)) return;
            try
            {
                NativeInpOut.DlPortWritePortUchar(port, val);
            }
            catch
            {
                try { NativeInpOut.Out32(port, (short)val); } catch { }
            }
        }

        private static bool EcWaitIbfClear(ushort cmdPort, int timeoutMs = 30)
        {
            byte status = ReadPortByte(cmdPort);
            if (status == 0xFF) return false;
            if ((status & EC_FLAG_IBF) == 0) return true;

            Stopwatch sw = Stopwatch.StartNew();
            while ((status = ReadPortByte(cmdPort)) != 0xFF && (status & EC_FLAG_IBF) != 0)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) return false;
                Thread.SpinWait(40);
            }
            return status != 0xFF && (status & EC_FLAG_IBF) == 0;
        }

        private static bool EcWaitObfSet(ushort cmdPort, int timeoutMs = 30)
        {
            byte status = ReadPortByte(cmdPort);
            if (status == 0xFF) return false;
            if ((status & EC_FLAG_OBF) != 0) return true;

            Stopwatch sw = Stopwatch.StartNew();
            while ((status = ReadPortByte(cmdPort)) != 0xFF && (status & EC_FLAG_OBF) == 0)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) return false;
                Thread.SpinWait(40);
            }
            return status != 0xFF && (status & EC_FLAG_OBF) != 0;
        }

        private static void EcDrainObf(ushort dataPort, ushort cmdPort)
        {
            try
            {
                byte status = ReadPortByte(cmdPort);
                if (status != 0xFF && (status & EC_FLAG_OBF) != 0)
                {
                    ReadPortByte(dataPort);
                }
            }
            catch { }
        }

        public static bool TryPortIoRead(ushort dataPort, ushort cmdPort, byte reg, out byte val)
        {
            val = 0xFF;
            EcDrainObf(dataPort, cmdPort);

            if (!EcWaitIbfClear(cmdPort)) return false;
            WritePortByte(cmdPort, EC_CMD_READ);

            if (!EcWaitIbfClear(cmdPort)) return false;
            WritePortByte(dataPort, reg);

            if (!EcWaitIbfClear(cmdPort) || !EcWaitObfSet(cmdPort)) return false;
            val = ReadPortByte(dataPort);
            return true;
        }

        public static bool TryPortIoWrite(ushort dataPort, ushort cmdPort, byte reg, byte val)
        {
            EcDrainObf(dataPort, cmdPort);

            if (!EcWaitIbfClear(cmdPort)) return false;
            WritePortByte(cmdPort, EC_CMD_WRITE);

            if (!EcWaitIbfClear(cmdPort)) return false;
            WritePortByte(dataPort, reg);

            if (!EcWaitIbfClear(cmdPort)) return false;
            WritePortByte(dataPort, val);

            return EcWaitIbfClear(cmdPort);
        }

        public static bool ProbeSuperIo(ushort cfgPort, out ushort chipId, out ushort hwBase)
        {
            chipId = 0;
            hwBase = 0;
            ushort valPort = (ushort)(cfgPort + 1);
            try
            {
                // IT87 enter sequence: 0x87, 0x01, 0x55, (0xAA on 0x4E, 0x55 on 0x2E)
                WritePortByte(cfgPort, 0x87);
                WritePortByte(cfgPort, 0x01);
                WritePortByte(cfgPort, 0x55);
                WritePortByte(cfgPort, (byte)(cfgPort == 0x4E ? 0xAA : 0x55));

                // Read Chip ID (Register 0x20, 0x21)
                WritePortByte(cfgPort, 0x20);
                byte idHi = ReadPortByte(valPort);
                WritePortByte(cfgPort, 0x21);
                byte idLo = ReadPortByte(valPort);

                ushort id = (ushort)((idHi << 8) | idLo);
                if (id != 0 && id != 0xFFFF && (idHi == 0x86 || idHi == 0x87 || idHi == 0x55 || id == 0x8733))
                {
                    chipId = id;
                    // Select Logical Device 4 (Hardware Monitor)
                    WritePortByte(cfgPort, 0x07);
                    WritePortByte(valPort, 0x04);

                    // Ensure Logical Device 4 is Activated in the Super I/O chip
                    WritePortByte(cfgPort, 0x30);
                    byte actVal = ReadPortByte(valPort);
                    if ((actVal & 0x01) == 0)
                    {
                        WritePortByte(valPort, 0x01);
                    }

                    // Read Base Address (Register 0x60, 0x61)
                    WritePortByte(cfgPort, 0x60);
                    byte baseHi = ReadPortByte(valPort);
                    WritePortByte(cfgPort, 0x61);
                    byte baseLo = ReadPortByte(valPort);
                    ushort candidateBase = (ushort)((baseHi << 8) | baseLo);

                    bool verified = VerifyIt87HwBase(candidateBase);
                    if (!verified)
                    {
                        // Some motherboards (e.g. TOPC TS-12C, Ryzen AI 9 HX 370) do not decode default 0x0290,
                        // but forward specific LPC port ranges (0x0A30 / 0x0A00 / 0x0A10 / 0x0A20) via FCH.
                        ushort[] candidates = new ushort[] { 0x0A30, 0x0A00, 0x0A10, 0x0A20, 0x0290, 0x02A0 };
                        foreach (ushort cand in candidates)
                        {
                            WritePortByte(cfgPort, 0x60);
                            WritePortByte(valPort, (byte)(cand >> 8));
                            WritePortByte(cfgPort, 0x61);
                            WritePortByte(valPort, (byte)(cand & 0xFF));
                            if (VerifyIt87HwBase(cand))
                            {
                                candidateBase = cand;
                                verified = true;
                                break;
                            }
                        }
                    }

                    if (verified)
                    {
                        hwBase = candidateBase;
                        // Ensure monitor operations are started (Bit 0 of Reg 0x00)
                        ushort ap = (ushort)(hwBase + 5);
                        ushort dp = (ushort)(hwBase + 6);
                        WritePortByte(ap, 0x00);
                        byte cfg0 = ReadPortByte(dp);
                        if (cfg0 != 0xFF && (cfg0 & 0x01) == 0)
                        {
                            WritePortByte(dp, (byte)(cfg0 | 0x01));
                        }
                        // Ensure 16-bit TachMode is enabled on the hardware monitor
                        WritePortByte(ap, 0x0C);
                        byte tMode = ReadPortByte(dp);
                        if (tMode != 0xFF && (tMode & 0x07) != 0x07)
                        {
                            WritePortByte(dp, (byte)(tMode | 0x07));
                        }

                        // Exit config mode
                        WritePortByte(cfgPort, 0x02);
                        WritePortByte(valPort, 0x02);
                        return true;
                    }

                    // Exit config mode on failed verification
                    WritePortByte(cfgPort, 0x02);
                    WritePortByte(valPort, 0x02);
                    return false;
                }

                // Try Nuvoton enter sequence: 0x87, 0x87
                WritePortByte(cfgPort, 0x87);
                WritePortByte(cfgPort, 0x87);
                WritePortByte(cfgPort, 0x20);
                idHi = ReadPortByte(valPort);
                WritePortByte(cfgPort, 0x21);
                idLo = ReadPortByte(valPort);
                id = (ushort)((idHi << 8) | idLo);
                if (idHi != 0 && idHi != 0xFF && (idHi >= 0xB0 && idHi <= 0xDF))
                {
                    chipId = id;
                    // Select LDN 8 or 11 (Hardware Monitor)
                    WritePortByte(cfgPort, 0x07);
                    WritePortByte(valPort, 0x08);
                    WritePortByte(cfgPort, 0x60);
                    byte nBaseHi = ReadPortByte(valPort);
                    WritePortByte(cfgPort, 0x61);
                    byte nBaseLo = ReadPortByte(valPort);
                    hwBase = (ushort)((nBaseHi << 8) | nBaseLo);
                    WritePortByte(cfgPort, 0xAA); // exit
                    return hwBase != 0 && hwBase != 0xFFFF;
                }
                WritePortByte(cfgPort, 0xAA);
            }
            catch { }
            return false;
        }

        public static bool VerifyIt87HwBase(ushort baseAddr)
        {
            if (baseAddr == 0 || baseAddr == 0xFFFF) return false;
            try
            {
                ushort addrPort = (ushort)(baseAddr + 5);
                ushort dataPort = (ushort)(baseAddr + 6);
                WritePortByte(addrPort, 0x58); // VENDOR_ID_REGISTER
                byte vendorId = ReadPortByte(dataPort);
                if (vendorId == 0x90 || vendorId == 0x7F)
                {
                    WritePortByte(addrPort, 0x00); // CONFIGURATION_REGISTER
                    byte cfg = ReadPortByte(dataPort);
                    if (cfg != 0xFF)
                    {
                        if ((cfg & 0x01) == 0)
                        {
                            WritePortByte(dataPort, (byte)(cfg | 0x01));
                        }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public void DumpSuperIoDiagnostic(string logFile)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== SUPER I/O & FAN CONTROL DIAGNOSTIC ===");
                sb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("AccessMethod: " + AccessMethodName);
                sb.AppendLine(string.Format("SuperIoChipId: 0x{0:X4}, SuperIoBaseAddr: 0x{1:X4}", SuperIoChipId, SuperIoBaseAddr));
                
                ushort[] cands = new ushort[] { 0x0A00, 0x0290, 0x0A10, 0x0A20, 0x0A30, 0x02A0 };
                foreach (ushort c in cands)
                {
                    ushort ap = (ushort)(c + 5);
                    ushort dp = (ushort)(c + 6);
                    WritePortByte(ap, 0x58);
                    byte v58 = ReadPortByte(dp);
                    WritePortByte(ap, 0x00);
                    byte v00 = ReadPortByte(dp);
                    sb.AppendFormat("Candidate Base 0x{0:X4}: Reg 0x58=0x{1:X2}, Reg 0x00=0x{2:X2} (Verified: {3})\r\n", c, v58, v00, (v58 == 0x90 || v58 == 0x7F));
                }

                if (SuperIoBaseAddr != 0)
                {
                    sb.AppendLine("--- Active HW Monitor Registers ---");
                    byte reg0C = ReadSuperIoHwReg(0x0C);
                    byte reg0B = ReadSuperIoHwReg(0x0B);
                    byte reg13 = ReadSuperIoHwReg(0x13);
                    byte reg15 = ReadSuperIoHwReg(0x15);
                    byte reg16 = ReadSuperIoHwReg(0x16);
                    byte reg17 = ReadSuperIoHwReg(0x17);
                    byte reg63 = ReadSuperIoHwReg(0x63);
                    byte reg6B = ReadSuperIoHwReg(0x6B);
                    byte reg73 = ReadSuperIoHwReg(0x73);
                    byte t29 = ReadSuperIoHwReg(0x29);
                    byte t2A = ReadSuperIoHwReg(0x2A);
                    sb.AppendFormat("Reg 0x0C (TachMode)=0x{0:X2}, 0x0B (Divisor)=0x{1:X2}, 0x13 (MainCtrl)=0x{2:X2}\r\n", reg0C, reg0B, reg13);
                    sb.AppendFormat("Fan 1: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0D=0x{2:X2}, 0x18=0x{3:X2} -> RPM={4}\r\n", reg15, reg63, ReadSuperIoHwReg(0x0D), ReadSuperIoHwReg(0x18), ReadSuperIoFanRpm(1));
                    sb.AppendFormat("Fan 2: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0E=0x{2:X2}, 0x19=0x{3:X2} -> RPM={4}\r\n", reg16, reg6B, ReadSuperIoHwReg(0x0E), ReadSuperIoHwReg(0x19), ReadSuperIoFanRpm(2));
                    sb.AppendFormat("Fan 3: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0F=0x{2:X2}, 0x1A=0x{3:X2} -> RPM={4}\r\n", reg17, reg73, ReadSuperIoHwReg(0x0F), ReadSuperIoHwReg(0x1A), ReadSuperIoFanRpm(3));
                    sb.AppendFormat("Fan 4: Tach 0x80=0x{0:X2}, 0x81=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x80), ReadSuperIoHwReg(0x81), ReadSuperIoFanRpm(4));
                    sb.AppendFormat("Fan 5: Tach 0x82=0x{0:X2}, 0x83=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x82), ReadSuperIoHwReg(0x83), ReadSuperIoFanRpm(5));
                    sb.AppendFormat("Temps: 0x29={0}C, 0x2A={1}C\r\n", t29, t2A);
                }
                File.WriteAllText(logFile, sb.ToString());
            }
            catch { }
        }

        public byte ReadSuperIoHwReg(byte reg)
        {
            if (SuperIoBaseAddr == 0) return 0xFF;
            try
            {
                ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
                ushort dataPort = (ushort)(SuperIoBaseAddr + 6);
                WritePortByte(addrPort, reg);
                return ReadPortByte(dataPort);
            }
            catch
            {
                return 0xFF;
            }
        }

        public void WriteSuperIoHwReg(byte reg, byte val)
        {
            if (SuperIoBaseAddr == 0) return;
            try
            {
                ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
                ushort dataPort = (ushort)(SuperIoBaseAddr + 6);
                WritePortByte(addrPort, reg);
                WritePortByte(dataPort, val);
            }
            catch { }
        }

        public int ReadSuperIoFanRpm(int fanIndex)
        {
            if (SuperIoBaseAddr == 0 || fanIndex < 1 || fanIndex > 6) return 0;
            try
            {
                // Ensure 16-bit tachometer mode is active in register 0x0C:
                byte modeReg = ReadSuperIoHwReg(0x0C);
                if ((modeReg & 0x07) != 0x07)
                {
                    WriteSuperIoHwReg(0x0C, (byte)(modeReg | 0x07));
                }

                Func<byte, byte, int> evalPair = (msb, lsb) =>
                {
                    byte aHi = ReadSuperIoHwReg(msb);
                    byte aLo1 = ReadSuperIoHwReg(lsb);
                    byte aLo2 = ReadSuperIoHwReg(lsb);
                    byte aLo = (aLo1 == aLo2) ? aLo1 : aLo2;
                    if (aHi != 0xFF && (aHi > 0 || aLo > 0) && !(aHi == 0xFF && aLo == 0xFF))
                    {
                        int aRaw = (aHi << 8) | aLo;
                        // Valid RPM range for Mini PC blowers (300 RPM .. 6000 RPM -> raw 112 .. 2250):
                        if (aRaw >= 110 && aRaw <= 2250)
                        {
                            int aRpm = (int)(1.35e6f / (aRaw * 2));
                            if (aRpm >= 300 && aRpm <= 6000) return aRpm;
                        }
                    }
                    return 0;
                };

                // On ITE IT8613E (and Ryzen AI 9 HX 370 / TS-12C / KTHIN PRO):
                // Fan 1 / APU Blower: Extended Pair C (Reg 0x9C/0x9D), fallback to Pair A (0x8C/0x8D) or std 0x0D/0x18
                // Fan 2 / Sys Blower: Extended Pair A (Reg 0x8C/0x8D), fallback to Pair B (0x8E/0x8F) or std 0x0E/0x19
                // Fan 3: Extended Pair C (Reg 0x9C/0x9D)
                // Fan 4: Extended Pair D (Reg 0x9E/0x9F)
                int rpm = 0;
                switch (fanIndex)
                {
                    case 1:
                        rpm = evalPair(0x9C, 0x9D);
                        if (rpm == 0) rpm = evalPair(0x18, 0x0D);
                        if (rpm == 0) rpm = evalPair(0x1A, 0x0F);
                        break;
                    case 2:
                        rpm = evalPair(0x8C, 0x8D);
                        if (rpm == 0) rpm = evalPair(0x8E, 0x8F);
                        if (rpm == 0) rpm = evalPair(0x9E, 0x9F);
                        if (rpm == 0) rpm = evalPair(0x19, 0x0E);
                        break;
                    case 3:
                        rpm = evalPair(0x9C, 0x9D);
                        if (rpm == 0) rpm = evalPair(0x1A, 0x0F);
                        break;
                    case 4:
                        rpm = evalPair(0x9E, 0x9F);
                        if (rpm == 0) rpm = evalPair(0x81, 0x80);
                        break;
                    case 5:
                        rpm = evalPair(0x83, 0x82);
                        break;
                    case 6:
                        rpm = evalPair(0x4D, 0x4C);
                        break;
                }
                if (rpm > 0) return rpm;

                // 8-bit fallback if 16-bit hi counter was 0
                byte lsbReg = (fanIndex == 1) ? (byte)0x0D : ((fanIndex == 2) ? (byte)0x0E : (byte)0x0F);
                byte lo = ReadSuperIoHwReg(lsbReg);
                if (lo > 0 && lo < 0xFF)
                {
                    byte divReg = ReadSuperIoHwReg(0x0B);
                    int shift = (fanIndex == 1) ? (divReg & 0x07) : ((divReg >> 3) & 0x07);
                    int divisor = 1 << shift;
                    if (divisor <= 0) divisor = 2;
                    int rpm8 = (int)(1.35e6f / (lo * divisor));
                    if (rpm8 > 0 && rpm8 <= 9000) return rpm8;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private void EnsureOpen()
        {
            if (_isInitialized)
                return;

            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inpoutx64.dll");
            if (!File.Exists(dllPath))
            {
                throw new InvalidOperationException(
                    "Driver 'inpoutx64.dll' not found in application folder. " +
                    "Place inpoutx64.dll in this folder and run as Administrator (or enable Demo Mode below).");
            }

            try
            {
                if (NativeInpOut.IsInpOutDriverOpen() == 0)
                {
                    throw new InvalidOperationException(
                        "inpoutx64 driver could not be opened. Please run as Administrator " +
                        "(and check Windows Vulnerable Driver Blocklist in Core Isolation if blocked).");
                }

                // 0. Test candidate IT87 Hardware Monitor base addresses directly (0x58=0x90):
                ushort[] candidateBases = new ushort[] { 0x0A00, 0x0290, 0x0A10, 0x0A20, 0x0A30, 0x02A0 };
                foreach (ushort cand in candidateBases)
                {
                    if (VerifyIt87HwBase(cand))
                    {
                        SuperIoChipId = 0x8613;
                        SuperIoBaseAddr = cand;
                        AccessMethod = EcAccessMethod.SuperIo2E;
                        _isInitialized = true;
                        DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));
                        return;
                    }
                }

                // 1. Probe LPC Super I/O on Port 0x2E
                ushort cId, hBase;
                if (ProbeSuperIo(0x2E, out cId, out hBase) && VerifyIt87HwBase(hBase))
                {
                    SuperIoChipId = cId;
                    SuperIoBaseAddr = hBase;
                    AccessMethod = EcAccessMethod.SuperIo2E;
                    _isInitialized = true;
                    DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));
                    return;
                }

                // 2. Probe LPC Super I/O on Port 0x4E
                if (ProbeSuperIo(0x4E, out cId, out hBase) && VerifyIt87HwBase(hBase))
                {
                    SuperIoChipId = cId;
                    SuperIoBaseAddr = hBase;
                    AccessMethod = EcAccessMethod.SuperIo4E;
                    _isInitialized = true;
                    DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));
                    return;
                }

                // 3. Probe standard ACPI EC Port I/O (0x62 / 0x66)
                byte sc66 = ReadPortByte(0x66);
                if (sc66 != 0xFF)
                {
                    byte probeVal;
                    byte probeReg = Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP;
                    if (TryPortIoRead(0x62, 0x66, probeReg, out probeVal) && probeVal > 0 && probeVal < 115)
                    {
                        AccessMethod = EcAccessMethod.PortIo62;
                        _isInitialized = true;
                        return;
                    }
                    byte probeMode = Profile != null ? Profile.RegModeStatus : REG_MODE_STATUS;
                    if (TryPortIoRead(0x62, 0x66, probeMode, out probeVal) && probeVal <= 2)
                    {
                        AccessMethod = EcAccessMethod.PortIo62;
                        _isInitialized = true;
                        return;
                    }
                }

                // 4. Probe secondary ACPI EC Port I/O (0x6C / 0x6E)
                byte sc6E = ReadPortByte(0x6E);
                if (sc6E != 0xFF)
                {
                    byte probeVal;
                    byte probeReg = Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP;
                    if (TryPortIoRead(0x6C, 0x6E, probeReg, out probeVal) && probeVal > 0 && probeVal < 115)
                    {
                        AccessMethod = EcAccessMethod.PortIo6C;
                        _isInitialized = true;
                        return;
                    }
                }

                // 5. Try MMIO mapping (e.g. 0xFE0B0400)
                ulong baseAddr = Profile != null ? Profile.EcBaseAddress : EC_BASE;
                IntPtr handle;
                IntPtr mapped = NativeInpOut.MapPhysToLinSafe(new IntPtr(unchecked((long)baseAddr)), EC_SIZE, out handle);
                if (mapped != IntPtr.Zero)
                {
                    byte m0 = Marshal.ReadByte(mapped, 0);
                    byte m1 = Marshal.ReadByte(mapped, 1);
                    byte mCpu = Marshal.ReadByte(mapped, Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP);
                    if (!(m0 == 0xFF && m1 == 0xFF && mCpu == 0xFF))
                    {
                        _physHandle = handle;
                        _linAddr = mapped;
                        AccessMethod = EcAccessMethod.Mmio;
                        _isInitialized = true;
                        return;
                    }
                    NativeInpOut.UnmapPhysicalMemory(handle, mapped);
                }

                // Fail-Closed Safety Check:
                // If probing methods 0..5 all failed to verify known EC/Super I/O signatures,
                // do NOT perform active port writes. Flag unverified to protect user hardware.
                AccessMethod = EcAccessMethod.Unknown;
                _isInitialized = false;
                throw new NotSupportedException(
                    "No verified Embedded Controller or Super I/O was detected on this PC.\n" +
                    "Active fan writes are disabled to protect hardware.\n" +
                    "Probes on IT87 Super I/O (0x2E/0x4E), ACPI EC (0x62/0x66 & 0x6C/0x6E), and MMIO all failed or returned 0xFF.");
            }
            catch (DllNotFoundException)
            {
                throw new InvalidOperationException(
                    "inpoutx64.dll could not be loaded. Please place 64-bit inpoutx64.dll in this folder.");
            }
            catch (BadImageFormatException)
            {
                throw new InvalidOperationException(
                    "inpoutx64.dll has the wrong architecture (64-bit DLL required).");
            }
        }

        private byte ReadRegInternal(byte reg)
        {
            if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
            {
                return ReadSuperIoHwReg(reg);
            }
            if (AccessMethod == EcAccessMethod.PortIo62)
            {
                byte val;
                if (TryPortIoRead(0x62, 0x66, reg, out val)) return val;
                return 0xFF;
            }
            if (AccessMethod == EcAccessMethod.PortIo6C)
            {
                byte val;
                if (TryPortIoRead(0x6C, 0x6E, reg, out val)) return val;
                return 0xFF;
            }
            if (_linAddr != IntPtr.Zero)
            {
                return Marshal.ReadByte(_linAddr, reg);
            }
            return 0xFF;
        }

        private void WriteRegInternal(byte reg, byte val)
        {
            if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
            {
                if (reg >= 0x30 && reg <= 0x3F) return;
                WriteSuperIoHwReg(reg, val);
            }
            else if (AccessMethod == EcAccessMethod.PortIo62)
            {
                TryPortIoWrite(0x62, 0x66, reg, val);
            }
            else if (AccessMethod == EcAccessMethod.PortIo6C)
            {
                TryPortIoWrite(0x6C, 0x6E, reg, val);
            }
            else if (_linAddr != IntPtr.Zero)
            {
                Marshal.WriteByte(_linAddr, reg, val);
            }
        }

        public byte ReadReg(byte reg)
        {
            EnsureOpen();
            using (var lockScope = EcBusMutex.AcquireEc(30))
            {
                if (!lockScope.Acquired)
                {
                    return 0xFF; // Fail-closed: do not perform unsynchronized read
                }
                return ReadRegInternal(reg);
            }
        }

        public void WriteReg(byte reg, byte val)
        {
            EnsureOpen();
            using (var lockScope = EcBusMutex.AcquireEc(30))
            {
                if (!lockScope.Acquired)
                {
                    return; // Fail-closed: do not write without lock
                }
                WriteRegInternal(reg, val);
            }
        }

        public static uint ReadAmdSmn(uint smnAddress)
        {
            bool locked = EcBusMutex.WaitPci(25);
            if (!locked) return 0; // Fail-closed: abort PCI read if mutex timed out
            uint origCf8 = 0;
            try
            {
                // Save original PCI config address to prevent collision with Windows kernel storage drivers
                try { origCf8 = NativeInpOut.DlPortReadPortUlong(0xCF8); } catch { }

                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000060);
                NativeInpOut.DlPortWritePortUlong(0xCFC, smnAddress);
                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000064);
                return NativeInpOut.DlPortReadPortUlong(0xCFC);
            }
            catch
            {
                return 0;
            }
            finally
            {
                // Always restore original PCI config address so stornvme.sys / pci.sys are uninterrupted
                try
                {
                    NativeInpOut.DlPortWritePortUlong(0xCF8, origCf8);
                }
                catch { }
                EcBusMutex.ReleasePci();
            }
        }

        public static int ReadAmdCpuTempTctl()
        {
            try
            {
                uint raw = ReadAmdSmn(0x00059800); // AMD Zen / Strix Point THM_TCON_CUR_TMP
                if (raw == 0 || raw == 0xFFFFFFFF) return 0;
                uint tRaw = (raw >> 21) & 0x7FF;
                double t = tRaw * 0.125;
                if ((raw & 0x00080000) != 0) // AMD temperature offset flag (-49 C)
                    t -= 49.0;
                int val = (int)Math.Round(t);
                if (val >= 15 && val <= 115)
                    return val;
            }
            catch { }
            return 0;
        }

        public static int EstimateApuPowerWatts(int cpuTemp, int gpuTemp, int modeCode)
        {
            int peakTemp = Math.Max(cpuTemp, gpuTemp);
            if (peakTemp <= 0) return 0;
            int maxTdp = modeCode == 1 ? 65 : (modeCode == 2 ? 35 : 54);
            double norm = Math.Max(0.0, Math.Min(1.0, (peakTemp - 36.0) / 52.0));
            int est = (int)Math.Round(8.0 + Math.Pow(norm, 1.35) * (maxTdp - 8.0));
            return Math.Max(6, Math.Min(maxTdp, est));
        }

        private FanStatus _lastStatusCache;

        public FanStatus GetStatus()
        {
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            EnsureOpen();

            byte f1h = 0, f1l = 0, f2h = 0, f2l = 0, modeVal = 0, f1Ctrl = 0, f2Ctrl = 0;
            int ecCpuTemp = 0, ecGpuTemp = 0;
            int sioFan1 = 0, sioFan2 = 0;

            using (var lockScope = EcBusMutex.AcquireEc(35))
            {
                if (!lockScope.Acquired)
                {
                    // Fail-closed on mutex contention: return last cached status rather than corrupting readings
                    if (_lastStatusCache.CpuTemp > 0)
                    {
                        return _lastStatusCache;
                    }
                }
                else
                {
                    if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
                    {
                        sioFan1 = ReadSuperIoFanRpm(1);
                        sioFan2 = ReadSuperIoFanRpm(2);
                        ecCpuTemp = ReadSuperIoHwReg(0x29); // IT87 Temp 1
                        ecGpuTemp = ReadSuperIoHwReg(0x2A); // IT87 Temp 2
                        f1h = 0; f1l = 0; f2h = 0; f2l = 0;
                        modeVal = 0;

                        byte ext1 = ReadSuperIoHwReg(0x6B); // Fan 1 APU duty (Normal polarity)
                        byte ext2 = ReadSuperIoHwReg(0x73); // Fan 2 Sys duty (Inverted polarity)

                        bool f1Auto = (ext1 <= 40 || ext1 == 0xFF);
                        bool f2Auto = (ext2 >= 240 || ext2 == 0x00);

                        int f1Duty = f1Auto ? 0 : (int)Math.Round(ext1 * 100.0 / 255.0);
                        int f2Duty = f2Auto ? 0 : (int)Math.Round((255 - ext2) * 100.0 / 255.0);

                        f1Ctrl = (byte)(f1Auto ? 0x00 : (0x80 | (f1Duty & 0x7F)));
                        f2Ctrl = (byte)(f2Auto ? 0x00 : (0x80 | (f2Duty & 0x7F)));
                    }
                    else
                    {
                        f1h = ReadRegInternal(p.RegFan1Hi);
                        f1l = ReadRegInternal(p.RegFan1Lo);
                        f2h = ReadRegInternal(p.RegFan2Hi);
                        f2l = ReadRegInternal(p.RegFan2Lo);
                        ecCpuTemp = ReadRegInternal(p.RegCpuTemp);
                        ecGpuTemp = ReadRegInternal(p.RegGpuTemp);
                        modeVal = ReadRegInternal(p.RegModeStatus);
                        f1Ctrl  = ReadRegInternal(p.RegFan1Ctrl);
                        f2Ctrl  = ReadRegInternal(p.RegFan2Ctrl);
                    }
                }
            }

            int adlTemp, adlCpuTemp, adlPowerWatts, cpuPwr, gfxPwr, socPwr, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt;
            string pwrSrc;
            _adlSensor.ReadTelemetryDetailed(out adlTemp, out adlCpuTemp, out adlPowerWatts, out cpuPwr, out gfxPwr, out socPwr,
                out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out pwrSrc);

            // Safe multi-tier temperature resolution:
            // Tier 1: AMD ADL2 PMLOG (via atiadlxx.dll / amdfendrm.sys) - official AMD kernel driver, ZERO raw port I/O
            // Tier 2: EC / Super I/O thermistor register
            // Tier 3: Last-resort fallback to AMD SMN silicon register (restores 0xCF8)
            int cpuTemp = 0;
            if (adlCpuTemp > 0 && adlCpuTemp <= 115)
            {
                cpuTemp = adlCpuTemp;
            }
            else if (ecCpuTemp > 0 && ecCpuTemp < 255)
            {
                cpuTemp = ecCpuTemp;
            }
            else
            {
                cpuTemp = ReadAmdCpuTempTctl();
            }
            int gpuTemp = (adlTemp > 0)
                ? adlTemp
                : ((ecGpuTemp > 0 && ecGpuTemp < 255) ? ecGpuTemp : 0);

            string modeStr = "Unknown";
            int modeCode = (modeVal <= 2) ? (int)modeVal : -1;
            switch (modeVal)
            {
                case 0: modeStr = "Balanced"; break;
                case 1: modeStr = "Performance"; break;
                case 2: modeStr = "Silent"; break;
                default: modeCode = -1; break;
            }

            int sioFan3 = ReadSuperIoFanRpm(3);
            int sioFan4 = ReadSuperIoFanRpm(4);
            int sioFan5 = ReadSuperIoFanRpm(5);
            int f1Ec = DecodeTachometerRpm(f1h, f1l);
            int f2Ec = DecodeTachometerRpm(f2h, f2l);

            int resolvedSio1 = 0;
            int resolvedSio2 = 0;

            if (SuperIoBaseAddr != 0)
            {
                resolvedSio1 = sioFan1 > 0 ? sioFan1 : sioFan3;
                resolvedSio2 = sioFan2 > 0 ? sioFan2 : ReadSuperIoFanRpm(4);
                if (resolvedSio1 == 0 && sioFan3 > 0) resolvedSio1 = sioFan3;
            }

            int fan1Rpm = (resolvedSio1 > 0) ? resolvedSio1 : f1Ec;
            int fan2Rpm = (resolvedSio2 > 0) ? resolvedSio2 : f2Ec;
            int apuPower = adlPowerWatts > 0
                ? adlPowerWatts
                : EstimateApuPowerWatts(cpuTemp, gpuTemp, modeCode);
            if (adlPowerWatts <= 0)
            {
                pwrSrc = "EC Thermal-TDP Model (CPU/iGPU Temp + Active BIOS Mode)";
            }

            FanStatus st = new FanStatus
            {
                CpuTemp = cpuTemp,
                GpuTemp = gpuTemp,
                ApuPowerWatts = apuPower,
                CpuPowerWatts = cpuPwr,
                GfxPowerWatts = gfxPwr,
                SocPowerWatts = socPwr,
                ApuPowerLimitWatts = pwrLim,
                CpuClockMhz = cpuClk,
                GfxClockMhz = gfxClk,
                GfxActivityPct = gfxAct,
                GfxVoltageMv = gfxVolt,
                RtssFps = FpsMonitor.TryGetRtssFps(),
                PowerSource = pwrSrc,
                Fan1Rpm = fan1Rpm,
                Fan2Rpm = fan2Rpm,
                Mode = modeStr,
                ModeCode = modeCode,
                Fan1Pct = (f1Ctrl != 0xFF) ? (f1Ctrl & 0x7F) : 0,
                Fan2Pct = (f2Ctrl != 0xFF) ? (f2Ctrl & 0x7F) : 0,
                Fan1Manual = (f1Ctrl != 0xFF && (f1Ctrl & 0x80) != 0) ? 1 : 0,
                Fan2Manual = (f2Ctrl != 0xFF && (f2Ctrl & 0x80) != 0) ? 1 : 0
            };
            _lastStatusCache = st;
            return st;
        }

        public static int DecodeTachometerRpm(byte hi, byte lo)
        {
            if (hi == 0xFF && lo == 0xFF) return 0;
            int rpm = (hi << 8) | lo;
            if (rpm == 8000 || rpm == 65535 || rpm < 0) return 0;
            return rpm;
        }

        public static byte EncodeModeCommand(int code)
        {
            switch (code)
            {
                case 0: return 0x80;
                case 1: return 0x81;
                case 2: return 0x82;
                default: throw new ArgumentException("Invalid mode (0=Balanced, 1=Performance, 2=Silent)");
            }
        }

        public static byte EncodeFanCommand(int pct)
        {
            if (pct < 0 || pct > 100)
                throw new ArgumentException("Percentage must be between 0 and 100.");
            if (pct == 0)
                return 0x00;
            return (byte)(0x80 | (pct & 0x7F));
        }

        public void SetMode(int code)
        {
            EnsureOpen();
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            WriteReg(p.RegModeCmd, EncodeModeCommand(code));
        }

        public void SetFan(int fanId, int pct)
        {
            EnsureOpen();
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            byte val = EncodeFanCommand(pct);
            byte reg = (fanId == 1) ? p.RegFan1Ctrl : p.RegFan2Ctrl;

            using (var lockScope = EcBusMutex.AcquireEc(35))
            {
                if (!lockScope.Acquired)
                {
                    return; // Fail-closed: do not write to fan control registers on contention
                }

                WriteRegInternal(reg, val);

                if (SuperIoBaseAddr != 0)
                {
                    // On IT8613 / IT87: Enable PWM output and manual mode (0x7F, never 0x80 failsafe)
                    byte mainCtrl = ReadSuperIoHwReg(0x13);
                    if ((mainCtrl & 0x07) != 0x07)
                    {
                        WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x07));
                    }
                    WriteSuperIoHwReg(0x15, 0x7F);
                    WriteSuperIoHwReg(0x16, 0x7F);
                    WriteSuperIoHwReg(0x17, 0x7F);

                    if (fanId == 1)
                    {
                        // APU Fan: PWM 2 (0x6B) + sync PWM 1 (0x63), Normal Polarity (35-40 min for ~2700 RPM)
                        byte duty = (pct <= 0) ? (byte)40 : (byte)Math.Max(35, Math.Min(255, (pct * 255) / 100));
                        WriteSuperIoHwReg(0x6B, duty);
                        WriteSuperIoHwReg(0x63, duty);
                    }
                    else
                    {
                        // Sys Fan: PWM 3 (0x73), Inverted Polarity (240 idle = quiet ~2800 RPM)
                        byte duty = (pct <= 0) ? (byte)240 : (byte)Math.Max(0, Math.Min(240, 255 - ((pct * 255) / 100)));
                        WriteSuperIoHwReg(0x73, duty);
                    }
                }
            }
        }

        public void SetAuto()
        {
            EnsureOpen();
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using (var lockScope = EcBusMutex.AcquireEc(40))
                {
                    if (!lockScope.Acquired)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    WriteRegInternal(p.RegModeCmd, 0x80);
                    WriteRegInternal(p.RegFan1Ctrl, 0x00);
                    WriteRegInternal(p.RegFan2Ctrl, 0x00);

                    if (SuperIoBaseAddr != 0)
                    {
                        // Ensure Super I/O PWM driving is active in manual mode (never 0x80 SmartGuardian blast)
                        byte mainCtrl = ReadSuperIoHwReg(0x13);
                        if ((mainCtrl & 0x07) != 0x07)
                        {
                            WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x07));
                        }
                        WriteSuperIoHwReg(0x15, 0x7F);
                        WriteSuperIoHwReg(0x16, 0x7F);
                        WriteSuperIoHwReg(0x17, 0x7F);
                        WriteSuperIoHwReg(0x6B, 40);   // APU Fan quiet idle (~2600-2800 RPM)
                        WriteSuperIoHwReg(0x63, 40);   // APU Fan sync
                        WriteSuperIoHwReg(0x73, 240);  // Sys Fan quiet idle (~2600-2800 RPM, inverted)
                    }
                    return;
                }
            }
        }

        public byte[] ReadAllRegisters()
        {
            EnsureOpen();
            byte[] buf = new byte[EC_SIZE];
            using (var lockScope = EcBusMutex.AcquireEc(60))
            {
                if (!lockScope.Acquired)
                {
                    for (int i = 0; i < EC_SIZE; i++) buf[i] = 0xFF;
                    return buf;
                }
                for (int i = 0; i < EC_SIZE; i++)
                {
                    buf[i] = ReadRegInternal((byte)i);
                }
                return buf;
            }
        }

        public void Dispose()
        {
            _adlSensor.Dispose();
            if (_linAddr != IntPtr.Zero)
            {
                try
                {
                    NativeInpOut.UnmapPhysicalMemory(_physHandle, _linAddr);
                }
                catch { }
                _linAddr = IntPtr.Zero;
            }
        }
    }

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
                PowerSource = "Simulated APU SVI2 Telemetry (Demo Mode)",
                Fan1Rpm = f1Rpm,
                Fan2Rpm = f2Rpm,
                Mode = modes[_modeCode],
                ModeCode = _modeCode,
                Fan1Pct = _fan1Pct,
                Fan2Pct = _fan2Pct,
                Fan1Manual = _fan1Manual,
                Fan2Manual = _fan2Manual
            };
        }

        public void SetMode(int code)
        {
            if (code >= 0 && code <= 2) _modeCode = code;
        }

        public void SetFan(int fanId, int pct)
        {
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
        }

        public void SetAuto()
        {
            _modeCode = 0;
            _fan1Manual = 0;
            _fan2Manual = 0;
            _fan1Pct = 0;
            _fan2Pct = 0;
        }
    }

    public enum ThemePreference
    {
        System = 0,
        Dark = 1,
        Light = 2,
        Radeon = 3
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }
    }

    public class FanCurveCanvas : Control
    {
        public const int MIN_TEMP = 30;
        public const int MAX_TEMP = 95;

        public FanCurveProfile ApuProfile;
        public FanCurveProfile SysProfile;
        public bool EditSysFan = false; // false = APU Fan (Fan 1), true = Sys Fan (Fan 2: RAM/NVMe)
        public bool ShowSecondaryCurve = true;
        public int CurrentApuTemp = 45;
        public bool IsDarkMode = true;
        public bool IsRadeonTheme = false;

        public event EventHandler CurveModified;
        public event EventHandler BuiltInCurveDragStarted;

        private int _dragIndex = -1;
        private int _hoverIndex = -1;

        public FanCurveCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Cross;
        }

        public FanCurveProfile GetActiveProfile()
        {
            return EditSysFan ? SysProfile : ApuProfile;
        }

        private CurvePoint[] GetActivePoints()
        {
            if (EditSysFan)
                return SysProfile != null ? SysProfile.SysPoints : null;
            return ApuProfile != null ? ApuProfile.ApuPoints : null;
        }

        private CurvePoint[] GetSecondaryPoints()
        {
            if (EditSysFan)
                return ApuProfile != null ? ApuProfile.ApuPoints : null;
            return SysProfile != null ? SysProfile.SysPoints : null;
        }

        private Rectangle GetPlotRect()
        {
            int left = 36;
            int top = 12;
            int right = Width - 14;
            int bottom = Height - 22;
            return new Rectangle(left, top, Math.Max(40, right - left), Math.Max(30, bottom - top));
        }

        private PointF PointToScreen(CurvePoint pt, Rectangle plot)
        {
            float tx = (float)(pt.Temp - MIN_TEMP) / (MAX_TEMP - MIN_TEMP);
            float ty = (float)pt.Pct / 100f;
            float x = plot.Left + tx * plot.Width;
            float y = plot.Bottom - ty * plot.Height;
            return new PointF(x, y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            CurvePoint[] pts = GetActivePoints();
            FanCurveProfile prof = GetActiveProfile();
            if (pts == null || prof == null || e.Button != MouseButtons.Left)
                return;

            Rectangle plot = GetPlotRect();
            for (int i = 0; i < pts.Length; i++)
            {
                PointF p = PointToScreen(pts[i], plot);
                float dx = e.X - p.X;
                float dy = e.Y - p.Y;
                if (dx * dx + dy * dy <= 100f)
                {
                    if (prof.IsBuiltIn && BuiltInCurveDragStarted != null)
                    {
                        BuiltInCurveDragStarted(this, EventArgs.Empty);
                    }
                    _dragIndex = i;
                    Invalidate();
                    break;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            CurvePoint[] pts = GetActivePoints();
            if (pts == null)
                return;

            Rectangle plot = GetPlotRect();

            if (_dragIndex >= 0 && _dragIndex < pts.Length)
            {
                float normX = (e.X - plot.Left) / (float)plot.Width;
                float normY = (plot.Bottom - e.Y) / (float)plot.Height;

                int newTemp = (int)Math.Round(MIN_TEMP + normX * (MAX_TEMP - MIN_TEMP));
                int newPct = (int)Math.Round(normY * 100f);

                int minT = (_dragIndex == 0) ? MIN_TEMP : pts[_dragIndex - 1].Temp + 2;
                int maxT = (_dragIndex == pts.Length - 1) ? MAX_TEMP : pts[_dragIndex + 1].Temp - 2;

                newTemp = Math.Max(minT, Math.Min(maxT, newTemp));
                newPct = Math.Max(0, Math.Min(100, newPct));

                pts[_dragIndex] = new CurvePoint(newTemp, newPct);
                Invalidate();
                return;
            }

            int found = -1;
            for (int i = 0; i < pts.Length; i++)
            {
                PointF p = PointToScreen(pts[i], plot);
                float dx = e.X - p.X;
                float dy = e.Y - p.Y;
                if (dx * dx + dy * dy <= 100f)
                {
                    found = i;
                    break;
                }
            }
            if (found != _hoverIndex)
            {
                _hoverIndex = found;
                Cursor = _hoverIndex >= 0 ? Cursors.Hand : Cursors.Cross;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragIndex >= 0)
            {
                _dragIndex = -1;
                Invalidate();
                if (CurveModified != null)
                    CurveModified(this, EventArgs.Empty);
            }
        }

        private List<PointF> BuildScreenPolyline(CurvePoint[] pts, Rectangle plot)
        {
            List<PointF> list = new List<PointF>();
            list.Add(PointToScreen(new CurvePoint(MIN_TEMP, pts[0].Pct), plot));
            for (int i = 0; i < pts.Length; i++)
                list.Add(PointToScreen(pts[i], plot));
            list.Add(PointToScreen(new CurvePoint(MAX_TEMP, pts[pts.Length - 1].Pct), plot));
            return list;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = IsRadeonTheme
                ? Color.FromArgb(0x16, 0x0A, 0x0E)
                : (IsDarkMode ? Color.FromArgb(0x16, 0x16, 0x22) : Color.FromArgb(0xF8, 0xFA, 0xFC));
            Color gridColor = IsRadeonTheme
                ? Color.FromArgb(0x42, 0x1C, 0x26)
                : (IsDarkMode ? Color.FromArgb(0x2D, 0x2F, 0x42) : Color.FromArgb(0xE2, 0xE8, 0xF0));
            Color labelColor = IsRadeonTheme
                ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69));

            Color apuColor = IsRadeonTheme
                ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                : (IsDarkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB));
            Color sysColor = IsRadeonTheme
                ? Color.FromArgb(0xFB, 0x92, 0x3C)
                : (IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88));

            Color primaryColor = EditSysFan ? sysColor : apuColor;
            Color secondaryColor = EditSysFan ? apuColor : sysColor;

            Color fillTop = Color.FromArgb(IsDarkMode ? 65 : 45, primaryColor.R, primaryColor.G, primaryColor.B);
            Color fillBot = Color.FromArgb(5, primaryColor.R, primaryColor.G, primaryColor.B);
            Color liveColor = IsRadeonTheme
                ? Color.FromArgb(0xFA, 0xCC, 0x15)
                : (IsDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x16, 0xA3, 0x4A));

            g.Clear(bg);
            Rectangle plot = GetPlotRect();

            using (Font axisFont = new Font("Segoe UI", 7.25f, FontStyle.Regular))
            using (Brush lblBrush = new SolidBrush(labelColor))
            using (Pen gridPen = new Pen(gridColor, 1f))
            {
                for (int pct = 0; pct <= 100; pct += 25)
                {
                    float y = plot.Bottom - (pct / 100f) * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    string yStr = pct + "%";
                    SizeF sz = g.MeasureString(yStr, axisFont);
                    g.DrawString(yStr, axisFont, lblBrush, plot.Left - sz.Width - 3, y - sz.Height / 2);
                }

                for (int t = 30; t <= 90; t += 15)
                {
                    float x = plot.Left + ((float)(t - MIN_TEMP) / (MAX_TEMP - MIN_TEMP)) * plot.Width;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    string xStr = t + "°C";
                    SizeF sz = g.MeasureString(xStr, axisFont);
                    g.DrawString(xStr, axisFont, lblBrush, x - sz.Width / 2, plot.Bottom + 3);
                }
            }

            // Secondary (background) fan curve as dashed reference line
            CurvePoint[] secPts = ShowSecondaryCurve ? GetSecondaryPoints() : null;
            if (secPts != null && secPts.Length > 0)
            {
                List<PointF> secLine = BuildScreenPolyline(secPts, plot);
                using (Pen secPen = new Pen(Color.FromArgb(110, secondaryColor.R, secondaryColor.G, secondaryColor.B), 1.5f))
                {
                    secPen.DashStyle = DashStyle.Dash;
                    g.DrawLines(secPen, secLine.ToArray());
                }
            }

            // Primary (active editable) fan curve
            CurvePoint[] activePts = GetActivePoints();
            if (activePts != null && activePts.Length > 0)
            {
                List<PointF> pts = BuildScreenPolyline(activePts, plot);

                using (GraphicsPath areaPath = new GraphicsPath())
                {
                    areaPath.AddLines(pts.ToArray());
                    areaPath.AddLine(pts[pts.Count - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    areaPath.CloseFigure();
                    using (LinearGradientBrush areaBrush = new LinearGradientBrush(
                        new Point(plot.Left, plot.Top),
                        new Point(plot.Left, plot.Bottom),
                        fillTop, fillBot))
                    {
                        g.FillPath(areaBrush, areaPath);
                    }
                }

                using (Pen curvePen = new Pen(primaryColor, 2.25f))
                {
                    g.DrawLines(curvePen, pts.ToArray());
                }

                // Live Temperature vertical marker + operating point dot
                int clampedTemp = Math.Max(MIN_TEMP, Math.Min(MAX_TEMP, CurrentApuTemp));
                int evalPct = FanCurveProfile.EvaluateCurve(activePts, CurrentApuTemp);
                PointF livePt = PointToScreen(new CurvePoint(clampedTemp, evalPct), plot);

                using (Pen liveDash = new Pen(Color.FromArgb(140, liveColor.R, liveColor.G, liveColor.B), 1.2f))
                {
                    liveDash.DashStyle = DashStyle.Dot;
                    g.DrawLine(liveDash, livePt.X, plot.Top, livePt.X, plot.Bottom);
                }
                using (Brush liveBrush = new SolidBrush(liveColor))
                {
                    g.FillEllipse(liveBrush, livePt.X - 4.5f, livePt.Y - 4.5f, 9f, 9f);
                }

                // Draggable control points
                for (int i = 0; i < activePts.Length; i++)
                {
                    PointF p = PointToScreen(activePts[i], plot);
                    bool activePt = (i == _dragIndex || i == _hoverIndex);
                    float r = activePt ? 6f : 4.5f;

                    using (Brush ptBg = new SolidBrush(IsDarkMode ? Color.Black : Color.White))
                    using (Pen ptPen = new Pen(activePt ? Color.FromArgb(0xF9, 0xE2, 0xAF) : primaryColor, 2.2f))
                    {
                        g.FillEllipse(ptBg, p.X - r, p.Y - r, r * 2, r * 2);
                        g.DrawEllipse(ptPen, p.X - r, p.Y - r, r * 2, r * 2);
                    }

                    if (activePt)
                    {
                        string fanTag = EditSysFan ? "Sys" : "APU";
                        string tip = string.Format("{0}: {1}°C → {2}%", fanTag, activePts[i].Temp, activePts[i].Pct);
                        using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                        using (Brush bBg = new SolidBrush(IsDarkMode ? Color.Black : Color.White))
                        using (Brush bFg = new SolidBrush(IsDarkMode ? Color.White : Color.Black))
                        {
                            SizeF sz = g.MeasureString(tip, f);
                            float bx = Math.Max(plot.Left, Math.Min(plot.Right - sz.Width - 4, p.X - sz.Width / 2));
                            float by = p.Y - sz.Height - 8;
                            if (by < 2) by = p.Y + 8;
                            g.FillRectangle(bBg, bx - 2, by - 1, sz.Width + 4, sz.Height + 2);
                            g.DrawString(tip, f, bFg, bx, by);
                        }
                    }
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>
    /// Full-size (552×244) Combined Live Telemetry Trend Graph Canvas.
    /// Displays rolling CPU Temp (°C), iGPU Temp (°C), APU Power (W), APU Fan (RPM), and Sys Fan (RPM)
    /// with interactive legend pills, spotlight highlighting when a metric box is clicked, dual Y-axes,
    /// auto-zoom scaling, and a time-scrubbing hover crosshair.
    /// </summary>
    public class LiveTelemetryCanvas : Control
    {
        public TelemetrySessionStats Stats { get; set; }
        public bool IsDarkMode { get; set; }
        public bool IsRadeonTheme { get; set; }
        public bool HasDualFans { get; set; }

        // 0 = All / Default (CPU), 1 = Highlight CPU Temp, 2 = Highlight iGPU Temp, 3 = Highlight Power W
        public int HighlightMetric { get; set; }

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowPower = true;
        public bool ShowApuRpm = true;
        public bool ShowSysRpm = true;
        public bool AutoScale = true;

        public event EventHandler ResetPeaksRequested;
        public event EventHandler HighlightMetricChanged;

        private int _hoverSampleIdx = -1;
        private int _hoverMouseX = -1;
        private readonly Rectangle[] _pillRects = new Rectangle[7];

        private static readonly Font AxisFont = new Font("Segoe UI", 7.0f, FontStyle.Regular);
        private static readonly Font PillFontBold = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font PillFontReg = new Font("Segoe UI", 7.25f, FontStyle.Regular);
        private static readonly Font HudFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        public LiveTelemetryCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            IsDarkMode = true;
            HasDualFans = true;
            HighlightMetric = 0;
            Cursor = Cursors.Cross;
        }

        private Rectangle GetPlotRect()
        {
            return new Rectangle(38, 30, Math.Max(60, Width - 82), Math.Max(40, Height - 50));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverSampleIdx != -1)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overPill = false;
            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    overPill = true;
                    break;
                }
            }
            Cursor = overPill ? Cursors.Hand : Cursors.Cross;

            Rectangle plot = GetPlotRect();
            int count = (Stats != null && Stats.CpuHistory != null) ? Stats.CpuHistory.Count : 0;
            if (count > 0 && plot.Contains(e.Location))
            {
                float normX = (float)(e.X - plot.Left) / Math.Max(1, plot.Width);
                int idx = count == 1 ? 0 : (int)Math.Round(normX * (count - 1));
                idx = Math.Max(0, Math.Min(count - 1, idx));
                if (idx != _hoverSampleIdx || Math.Abs(e.X - _hoverMouseX) > 1)
                {
                    _hoverSampleIdx = idx;
                    _hoverMouseX = e.X;
                    Invalidate();
                }
            }
            else if (_hoverSampleIdx != -1)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    if (i == 0)
                    {
                        if (HighlightMetric == 1) ShowCpu = !ShowCpu;
                        else { HighlightMetric = 1; ShowCpu = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 1)
                    {
                        if (HighlightMetric == 2) ShowGpu = !ShowGpu;
                        else { HighlightMetric = 2; ShowGpu = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 2)
                    {
                        if (HighlightMetric == 3) ShowPower = !ShowPower;
                        else { HighlightMetric = 3; ShowPower = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 3)
                    {
                        if (HighlightMetric == 4) ShowApuRpm = !ShowApuRpm;
                        else { HighlightMetric = 4; ShowApuRpm = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 4)
                    {
                        if (HighlightMetric == 5) ShowSysRpm = !ShowSysRpm;
                        else { HighlightMetric = 5; ShowSysRpm = true; }
                        if (HighlightMetricChanged != null) HighlightMetricChanged(this, EventArgs.Empty);
                    }
                    else if (i == 5) AutoScale = !AutoScale;
                    else if (i == 6)
                    {
                        if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                    }
                    Invalidate();
                    return;
                }
            }
        }

        private static PointF[] BuildSeriesPoints(List<int> list, int count, Rectangle plot, float scaleMin, float scaleMax)
        {
            PointF[] pts = new PointF[Math.Max(2, count)];
            float span = Math.Max(1f, scaleMax - scaleMin);
            if (count == 1)
            {
                float norm = (list[0] - scaleMin) / span;
                norm = Math.Max(0f, Math.Min(1f, norm));
                float y = plot.Bottom - norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    float x = plot.Left + (plot.Width * i) / (float)(count - 1);
                    int v = i < list.Count ? list[i] : list[list.Count - 1];
                    float norm = (v - scaleMin) / span;
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plot.Bottom - norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }
            return pts;
        }

        private void DrawSeriesLine(Graphics g, PointF[] pts, Rectangle plot, Color color, bool isHighlighted, bool isDashed)
        {
            if (pts == null || pts.Length < 2) return;

            if (isHighlighted)
            {
                using (GraphicsPath areaPath = new GraphicsPath())
                {
                    areaPath.AddLines(pts);
                    areaPath.AddLine(pts[pts.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    areaPath.CloseFigure();
                    using (LinearGradientBrush areaBr = new LinearGradientBrush(
                        new Point(plot.Left, plot.Top),
                        new Point(plot.Left, plot.Bottom),
                        Color.FromArgb(IsDarkMode ? 75 : 55, color),
                        Color.FromArgb(6, color)))
                    {
                        g.FillPath(areaBr, areaPath);
                    }
                }
                using (Pen glowPen = new Pen(Color.FromArgb(55, color), 5.0f))
                {
                    glowPen.LineJoin = LineJoin.Round;
                    g.DrawLines(glowPen, pts);
                }
            }

            using (Pen pen = new Pen(color, isHighlighted ? 2.5f : (isDashed ? 1.5f : 1.85f)))
            {
                pen.LineJoin = LineJoin.Round;
                if (isDashed) pen.DashStyle = DashStyle.Dash;
                g.DrawLines(pen, pts);
            }

            PointF last = pts[pts.Length - 1];
            float r = isHighlighted ? 4.2f : 2.8f;
            using (SolidBrush br = new SolidBrush(color))
            {
                g.FillEllipse(br, last.X - r, last.Y - r, r * 2f, r * 2f);
            }
        }

        private int DrawLegendPill(Graphics g, int idx, int x, int y, string text, Color seriesColor, bool active, bool highlighted)
        {
            Font f = highlighted ? PillFontBold : PillFontReg;
            Size sz = TextRenderer.MeasureText(g, text, f, new Size(200, 20), TextFormatFlags.NoPadding);
            int w = sz.Width + 12;
            int h = 18;
            Rectangle r = new Rectangle(x, y, w, h);
            _pillRects[idx] = r;

            Color pillBg = !active
                ? (IsDarkMode ? Color.FromArgb(0x1E, 0x1E, 0x2A) : Color.FromArgb(0xE2, 0xE8, 0xF0))
                : (highlighted
                    ? Color.FromArgb(IsDarkMode ? 80 : 50, seriesColor)
                    : Color.FromArgb(IsDarkMode ? 32 : 22, seriesColor));
            Color borderCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x38, 0x3A, 0x4C) : Color.FromArgb(0xCB, 0xD5, 0xE1))
                : (highlighted ? seriesColor : Color.FromArgb(140, seriesColor));
            Color textCol = !active
                ? (IsDarkMode ? Color.FromArgb(0x6C, 0x70, 0x86) : Color.FromArgb(0x94, 0xA3, 0xB8))
                : (IsDarkMode ? Color.White : Color.Black);

            using (SolidBrush bgBr = new SolidBrush(pillBg))
            using (Pen borderPen = new Pen(borderCol, highlighted ? 1.5f : 1f))
            {
                g.FillRectangle(bgBr, r);
                g.DrawRectangle(borderPen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            TextRenderer.DrawText(g, text, f, new Point(x + 6, y + 2), textCol, TextFormatFlags.NoPadding);
            return x + w + 5;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = IsRadeonTheme
                ? Color.FromArgb(0x16, 0x0A, 0x0E)
                : (IsDarkMode ? Color.FromArgb(0x16, 0x16, 0x22) : Color.FromArgb(0xF8, 0xFA, 0xFC));
            Color gridColor = IsRadeonTheme
                ? Color.FromArgb(0x42, 0x1C, 0x26)
                : (IsDarkMode ? Color.FromArgb(0x2D, 0x2F, 0x42) : Color.FromArgb(0xE2, 0xE8, 0xF0));
            Color labelColor = IsRadeonTheme
                ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69));

            Color cpuColor = IsRadeonTheme
                ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                : (IsDarkMode ? Color.FromArgb(0x60, 0xA5, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB));
            Color gpuColor = IsRadeonTheme
                ? Color.FromArgb(0x38, 0xBD, 0xF8)
                : (IsDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x16, 0xA3, 0x4A));
            Color pwrColor = IsRadeonTheme
                ? Color.FromArgb(0xFB, 0x92, 0x3C)
                : (IsDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xD9, 0x77, 0x06));
            Color apuRpmColor = IsDarkMode ? Color.FromArgb(0xC0, 0x84, 0xFC) : Color.FromArgb(0x7C, 0x3A, 0xED);
            Color sysRpmColor = IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88);

            g.Clear(bg);
            Rectangle plot = GetPlotRect();

            int count = (Stats != null && Stats.CpuHistory != null) ? Stats.CpuHistory.Count : 0;
            int lastCpu = count > 0 ? Stats.CpuHistory[count - 1] : 0;
            int lastGpu = (Stats != null && Stats.GpuHistory.Count > 0) ? Stats.GpuHistory[Stats.GpuHistory.Count - 1] : 0;
            int lastPwr = (Stats != null && Stats.PowerHistory.Count > 0) ? Stats.PowerHistory[Stats.PowerHistory.Count - 1] : 0;
            int lastF1  = (Stats != null && Stats.Fan1RpmHistory.Count > 0) ? Stats.Fan1RpmHistory[Stats.Fan1RpmHistory.Count - 1] : 0;
            int lastF2  = (Stats != null && Stats.Fan2RpmHistory.Count > 0) ? Stats.Fan2RpmHistory[Stats.Fan2RpmHistory.Count - 1] : 0;

            for (int i = 0; i < _pillRects.Length; i++) _pillRects[i] = Rectangle.Empty;

            // Top Interactive Legend Pills (y = 5)
            int px = 6;
            px = DrawLegendPill(g, 0, px, 5, string.Format("● CPU {0}°C", lastCpu), cpuColor, ShowCpu, HighlightMetric == 1);
            px = DrawLegendPill(g, 1, px, 5, string.Format("● iGPU {0}°C", lastGpu), gpuColor, ShowGpu, HighlightMetric == 2);
            px = DrawLegendPill(g, 2, px, 5, string.Format("● Pwr {0}W", lastPwr), pwrColor, ShowPower, HighlightMetric == 3);
            px = DrawLegendPill(g, 3, px, 5, string.Format("● APU {0}", lastF1), apuRpmColor, ShowApuRpm, HighlightMetric == 4);
            if (HasDualFans)
            {
                px = DrawLegendPill(g, 4, px, 5, string.Format("● Sys {0}", lastF2), sysRpmColor, ShowSysRpm, HighlightMetric == 5);
            }
            px = DrawLegendPill(g, 5, Width - 110, 5, AutoScale ? "Auto-Zoom" : "0–100", labelColor, true, AutoScale);
            DrawLegendPill(g, 6, Width - 48, 5, "Reset", labelColor, true, false);

            // Compute Left Axis (Temp °C / Power W) & Right Axis (Fan RPM) scales
            float leftMin = 0f, leftMax = 100f;
            float rpmMin = 0f, rpmMax = 3000f;

            if (AutoScale && count > 0)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (ShowCpu && i < Stats.CpuHistory.Count)
                    {
                        int v = Stats.CpuHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowGpu && i < Stats.GpuHistory.Count && Stats.GpuHistory[i] > 0)
                    {
                        int v = Stats.GpuHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (ShowPower && i < Stats.PowerHistory.Count && Stats.PowerHistory[i] > 0)
                    {
                        int v = Stats.PowerHistory[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                }
                if (lo <= hi)
                {
                    leftMin = Math.Max(0f, lo - 4f);
                    leftMax = hi + 4f;
                    if (leftMax - leftMin < 10f)
                    {
                        float mid = (leftMin + leftMax) * 0.5f;
                        leftMin = Math.Max(0f, mid - 5f);
                        leftMax = leftMin + 10f;
                    }
                }

                int rLo = int.MaxValue, rHi = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (ShowApuRpm && i < Stats.Fan1RpmHistory.Count)
                    {
                        int v = Stats.Fan1RpmHistory[i];
                        if (v < rLo) rLo = v;
                        if (v > rHi) rHi = v;
                    }
                    if (HasDualFans && ShowSysRpm && i < Stats.Fan2RpmHistory.Count)
                    {
                        int v = Stats.Fan2RpmHistory[i];
                        if (v < rLo) rLo = v;
                        if (v > rHi) rHi = v;
                    }
                }
                if (rLo <= rHi)
                {
                    rpmMin = Math.Max(0f, rLo - 150f);
                    rpmMax = rHi + 150f;
                    if (rpmMax - rpmMin < 400f)
                    {
                        rpmMin = Math.Max(0f, rLo - 200f);
                        rpmMax = rpmMin + 400f;
                    }
                }
            }

            // Draw Grid & Dual Y-Axes + Time X-Axis
            using (Brush lblBrush = new SolidBrush(labelColor))
            using (Pen gridPen = new Pen(gridColor, 1f))
            {
                for (int step = 0; step <= 4; step++)
                {
                    float frac = step / 4f;
                    float y = plot.Bottom - frac * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    int leftVal = (int)Math.Round(leftMin + frac * (leftMax - leftMin));
                    string lStr = leftVal + "°/W";
                    SizeF lSz = g.MeasureString(lStr, AxisFont);
                    g.DrawString(lStr, AxisFont, lblBrush, plot.Left - lSz.Width - 2, y - lSz.Height / 2);

                    int rVal = (int)Math.Round(rpmMin + frac * (rpmMax - rpmMin));
                    string rStr = rVal.ToString();
                    SizeF rSz = g.MeasureString(rStr, AxisFont);
                    g.DrawString(rStr, AxisFont, lblBrush, plot.Right + 3, y - rSz.Height / 2);
                }

                int totalWindowSec = Math.Max(15, (int)Math.Round(Math.Max(2, count) * 1.5));
                for (int tStep = 0; tStep <= 4; tStep++)
                {
                    float frac = tStep / 4f;
                    float x = plot.Left + frac * plot.Width;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    int secAgo = (int)Math.Round((1f - frac) * totalWindowSec);
                    string tStr = secAgo == 0 ? "Now" : string.Format("-{0}s", secAgo);
                    SizeF tSz = g.MeasureString(tStr, AxisFont);
                    g.DrawString(tStr, AxisFont, lblBrush, x - tSz.Width / 2, plot.Bottom + 3);
                }
            }

            if (count > 0)
            {
                PointF[] cpuPts = ShowCpu ? BuildSeriesPoints(Stats.CpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] gpuPts = ShowGpu ? BuildSeriesPoints(Stats.GpuHistory, count, plot, leftMin, leftMax) : null;
                PointF[] pwrPts = ShowPower ? BuildSeriesPoints(Stats.PowerHistory, count, plot, leftMin, leftMax) : null;
                PointF[] f1Pts  = ShowApuRpm ? BuildSeriesPoints(Stats.Fan1RpmHistory, count, plot, rpmMin, rpmMax) : null;
                PointF[] f2Pts  = (HasDualFans && ShowSysRpm) ? BuildSeriesPoints(Stats.Fan2RpmHistory, count, plot, rpmMin, rpmMax) : null;

                int hl = HighlightMetric == 0 ? 1 : HighlightMetric;

                // Draw non-highlighted series first, then the highlighted spotlight series on top
                if (f2Pts != null && hl != 5) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, false, true);
                if (f1Pts != null && hl != 4) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, false, true);
                if (pwrPts != null && hl != 3) DrawSeriesLine(g, pwrPts, plot, pwrColor, false, false);
                if (gpuPts != null && hl != 2) DrawSeriesLine(g, gpuPts, plot, gpuColor, false, false);
                if (cpuPts != null && hl != 1) DrawSeriesLine(g, cpuPts, plot, cpuColor, false, false);

                if (hl == 1 && cpuPts != null) DrawSeriesLine(g, cpuPts, plot, cpuColor, true, false);
                else if (hl == 2 && gpuPts != null) DrawSeriesLine(g, gpuPts, plot, gpuColor, true, false);
                else if (hl == 3 && pwrPts != null) DrawSeriesLine(g, pwrPts, plot, pwrColor, true, false);
                else if (hl == 4 && f1Pts != null) DrawSeriesLine(g, f1Pts, plot, apuRpmColor, true, false);
                else if (hl == 5 && f2Pts != null) DrawSeriesLine(g, f2Pts, plot, sysRpmColor, true, false);

                // Hover Time-Scrub Crosshair & Readout Card
                if (_hoverSampleIdx >= 0 && _hoverSampleIdx < count)
                {
                    float cx = count == 1 ? plot.Left + plot.Width / 2f : plot.Left + (plot.Width * _hoverSampleIdx) / (float)(count - 1);
                    using (Pen crossPen = new Pen(Color.FromArgb(160, labelColor), 1.2f))
                    {
                        crossPen.DashStyle = DashStyle.Dot;
                        g.DrawLine(crossPen, cx, plot.Top, cx, plot.Bottom);
                    }

                    int secAgo = (int)Math.Round((count - 1 - _hoverSampleIdx) * 1.5);
                    int hCpu = _hoverSampleIdx < Stats.CpuHistory.Count ? Stats.CpuHistory[_hoverSampleIdx] : 0;
                    int hGpu = _hoverSampleIdx < Stats.GpuHistory.Count ? Stats.GpuHistory[_hoverSampleIdx] : 0;
                    int hPwr = _hoverSampleIdx < Stats.PowerHistory.Count ? Stats.PowerHistory[_hoverSampleIdx] : 0;
                    int hF1  = _hoverSampleIdx < Stats.Fan1RpmHistory.Count ? Stats.Fan1RpmHistory[_hoverSampleIdx] : 0;
                    int hF2  = _hoverSampleIdx < Stats.Fan2RpmHistory.Count ? Stats.Fan2RpmHistory[_hoverSampleIdx] : 0;

                    string hud = HasDualFans
                        ? string.Format("{0}  |  CPU {1}°C • iGPU {2}°C • Pwr {3}W • APU {4} RPM • Sys {5} RPM",
                            secAgo == 0 ? "Now" : "-" + secAgo + "s", hCpu, hGpu, hPwr, hF1, hF2)
                        : string.Format("{0}  |  CPU {1}°C • iGPU {2}°C • Pwr {3}W • APU {4} RPM",
                            secAgo == 0 ? "Now" : "-" + secAgo + "s", hCpu, hGpu, hPwr, hF1);

                    Size hudSz = TextRenderer.MeasureText(g, hud, HudFont, new Size(520, 24), TextFormatFlags.NoPadding);
                    int boxW = hudSz.Width + 12;
                    int boxH = hudSz.Height + 6;
                    int boxX = Math.Max(plot.Left + 4, Math.Min(plot.Right - boxW - 4, (int)cx - boxW / 2));
                    int boxY = plot.Top + 4;

                    Color hudBg = IsDarkMode ? Color.FromArgb(225, 0x0F, 0x11, 0x1A) : Color.FromArgb(235, 255, 255, 255);
                    Color hudFg = IsDarkMode ? Color.White : Color.Black;
                    using (SolidBrush hBr = new SolidBrush(hudBg))
                    using (Pen hPen = new Pen(gridColor, 1f))
                    {
                        g.FillRectangle(hBr, boxX, boxY, boxW, boxH);
                        g.DrawRectangle(hPen, boxX, boxY, boxW, boxH);
                    }
                    TextRenderer.DrawText(g, hud, HudFont, new Point(boxX + 6, boxY + 3), hudFg, TextFormatFlags.NoPadding);
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

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
    public class FanCalibrationDialog : Form
    {
        public FanCalibrationResult Result { get; private set; }

        private readonly HardwareBackend _hw;
        private readonly DemoBackend _demo;
        private readonly bool _demoMode;
        private readonly Action<FanCalibrationProgress> _hostProgressCallback;

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
            Action<FanCalibrationProgress> hostProgressCallback)
        {
            _hw = hw;
            _demo = demo;
            _demoMode = demoMode;
            _hostProgressCallback = hostProgressCallback;

            Text = "MiniFanControl — Real-Time Fan Calibration";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(540, 420);
            BackColor = darkMode ? Color.FromArgb(0x18, 0x18, 0x26) : Color.FromArgb(0xF3, 0xF4, 0xF6);
            ForeColor = darkMode ? Color.White : Color.Black;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            Color surface = darkMode ? Color.FromArgb(0x22, 0x24, 0x36) : Color.White;
            Color accent  = darkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x1D, 0x4E, 0xD8);
            Color dimFg   = darkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x47, 0x55, 0x69);

            string lastCalInfo = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Current Status: Never calibrated (using default 30% minimum spin floor)."
                : string.Format("Last Calibrated: {0}  •  Saved Floor: APU {1}% ({2} RPM) / Sys {3}% ({4} RPM)",
                    FanCurveProfile.LastCalibratedDate,
                    FanCurveProfile.ApuMinSpinPct, FanCurveProfile.ApuMaxRpm,
                    FanCurveProfile.SysMinSpinPct, FanCurveProfile.SysMaxRpm);

            Label introLbl = new Label
            {
                Text = lastCalInfo + "\r\nWaits for full 0 RPM spin-down and 3.5s stabilization per step (+2% anti-stall safety margin).",
                Location = new Point(14, 10),
                Size = new Size(512, 34),
                ForeColor = dimFg
            };
            Controls.Add(introLbl);

            _threePassRb = new RadioButton
            {
                Text = "3-Pass Verification (Recommended — tests min spin speed 3 times)",
                Location = new Point(16, 46),
                Size = new Size(370, 22),
                Checked = true,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold)
            };
            Controls.Add(_threePassRb);

            _onePassRb = new RadioButton
            {
                Text = "1-Pass Standard",
                Location = new Point(392, 46),
                Size = new Size(134, 22),
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
                ForeColor = darkMode ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x0F, 0x17, 0x2A),
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
                BackColor = darkMode ? Color.FromArgb(0x10, 0x12, 0x1C) : Color.White,
                ForeColor = darkMode ? Color.FromArgb(0xE2, 0xE8, 0xF0) : Color.Black,
                Location = new Point(14, 154),
                Size = new Size(512, 214),
                Text = "Click 'Start Calibration' below.\r\n" +
                       "• Phase 1: Commands 1% PWM and waits until rotors coast to a verified 0 RPM stop.\r\n" +
                       "• Phase 2: Steps through 20%, 22%, 24%, 26%, 28%, 30%, 32%, 35%, 60%, 100% PWM.\r\n" +
                       "• Holds each step for 3.5s on hardware so fan inertia and tachometer settle.\r\n"
            };
            Controls.Add(_logBox);

            _startBtn = new Button
            {
                Text = "▶ Start Calibration",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(14, 378),
                Size = new Size(160, 30),
                BackColor = accent,
                ForeColor = darkMode ? Color.Black : Color.White,
                Cursor = Cursors.Hand
            };
            _startBtn.FlatAppearance.BorderSize = 0;
            _startBtn.Click += (s, e) => StartCalibrationAsync();
            Controls.Add(_startBtn);

            _cancelBtn = new Button
            {
                Text = "Stop / Cancel",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(182, 378),
                Size = new Size(120, 30),
                BackColor = darkMode ? Color.FromArgb(0x31, 0x32, 0x44) : Color.FromArgb(0xE5, 0xE7, 0xEB),
                ForeColor = darkMode ? Color.White : Color.Black,
                Cursor = Cursors.Hand
            };
            _cancelBtn.FlatAppearance.BorderSize = 0;
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
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                Location = new Point(346, 378),
                Size = new Size(180, 30),
                BackColor = Color.FromArgb(0x22, 0xC5, 0x5E),
                ForeColor = Color.Black,
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _applyBtn.FlatAppearance.BorderSize = 0;
            _applyBtn.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_applyBtn);

            FormClosing += OnDialogClosing;
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

            if (res == null || res.WasCanceled)
            {
                _phaseLabel.Text = "Calibration canceled — Fans restored to Auto.";
                if (res != null) _logBox.AppendText("\r\n" + res.SummaryReport);
                return;
            }

            Result = res;
            _progressBar.Value = 100;
            _phaseLabel.Text = string.Format("✓ Calibration Complete! Safe Min Spin: APU {0}% | Sys {1}%",
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

    /// <summary>
    /// CapFrameX / Radeon Adrenalin-style translucent, always-on-top On-Screen Display (OSD) overlay.
    /// Supports Horizontal Compact Bar and Vertical Stack layouts, per-metric live Mini-Sparklines,
    /// full Combined Multi-Series Live Graph, customizable metric visibility, opacity, theme sync,
    /// mouse drag repositioning, and Win32 WS_EX_TRANSPARENT click-through lock mode.
    /// </summary>
    public class CapFrameXOverlayForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE       = -20;
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW  = 0x00000080;

        // Classic CapFrameX & Radeon OSD Palettes
        private static readonly Color OsdBgDark    = Color.FromArgb(16, 18, 28);
        private static readonly Color OsdBorderDark = Color.FromArgb(52, 58, 82);
        private static readonly Color OsdBgRadeon  = Color.FromArgb(20, 8, 12);
        private static readonly Color OsdBorderRadeon = Color.FromArgb(110, 24, 38);

        private static readonly Color CfxAmber   = Color.FromArgb(0xF5, 0x9E, 0x0B);
        private static readonly Color CfxRuby    = Color.FromArgb(0xF4, 0x3F, 0x5E);
        private static readonly Color CfxCyan    = Color.FromArgb(0x38, 0xBD, 0xF8);
        private static readonly Color CfxGreen   = Color.FromArgb(0x22, 0xC5, 0x5E);
        private static readonly Color CfxYellow  = Color.FromArgb(0xFB, 0xBF, 0x24);
        private static readonly Color CfxViolet  = Color.FromArgb(0xC0, 0x84, 0xFC);
        private static readonly Color CfxTeal    = Color.FromArgb(0x2D, 0xD4, 0xBF);
        private static readonly Color CfxWhite   = Color.FromArgb(0xF8, 0xFA, 0xFC);

        public TelemetrySessionStats Stats;
        public bool VerticalLayout = false;
        public bool ClickThrough = false;
        public bool HasDualFans = true;

        // 0 = Text Only, 1 = Mini Sparklines per Metric, 2 = Combined Live Graph, 3 = Both Sparklines + Combined Graph
        public int GraphMode = 3;
        public int GraphHeight = 65; // 45=Compact, 65=Medium, 95=Large, 130=XL
        // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        public int OsdScaleTier = 0;
        public int OpacityPct = 88;
        public bool MatchAppTheme = true;
        public bool IsRadeonTheme = false;

        public bool ShowCpu = true;
        public bool ShowGpu = true;
        public bool ShowPower = true;
        public bool ShowGpuLoad = true;
        public bool ShowApuFan = true;
        public bool ShowSysFan = true;
        public bool ShowMode = true;
        public bool ShowPeaks = true;

        public event EventHandler PositionOrLayoutChanged;
        public event EventHandler RequestHideOverlay;
        public event EventHandler RequestToggleLock;
        public event EventHandler RequestOpenOsdConfig;

        private FanStatus _status;
        private string _modeSummary = "Balanced";
        private bool _dragging;
        private Point _dragStartMouse;
        private Point _dragStartForm;

        private Font _fontLabel;
        private Font _fontVal;
        private Font _fontPeak;
        private Font _fontHdr;
        private Font _fontChart;

        public float GetScaleMultiplier()
        {
            switch (OsdScaleTier)
            {
                case 1: return 1.25f;
                case 2: return 1.50f;
                case 3: return 1.85f;
                default: return 1.0f;
            }
        }

        public void UpdateFontsForScale()
        {
            float sc = GetScaleMultiplier();
            if (_fontLabel != null) _fontLabel.Dispose();
            if (_fontVal != null) _fontVal.Dispose();
            if (_fontPeak != null) _fontPeak.Dispose();
            if (_fontHdr != null) _fontHdr.Dispose();
            if (_fontChart != null) _fontChart.Dispose();

            _fontLabel = new Font("Consolas", 8.75f * sc, FontStyle.Bold);
            _fontVal   = new Font("Consolas", 9.5f * sc, FontStyle.Bold);
            _fontPeak  = new Font("Segoe UI", 6.75f * sc, FontStyle.Regular);
            _fontHdr   = new Font("Segoe UI", 7.5f * sc, FontStyle.Bold);
            _fontChart = new Font("Segoe UI", 6.75f * sc, FontStyle.Bold);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontLabel != null) { _fontLabel.Dispose(); _fontLabel = null; }
                if (_fontVal != null) { _fontVal.Dispose(); _fontVal = null; }
                if (_fontPeak != null) { _fontPeak.Dispose(); _fontPeak = null; }
                if (_fontHdr != null) { _fontHdr.Dispose(); _fontHdr = null; }
                if (_fontChart != null) { _fontChart.Dispose(); _fontChart = null; }
            }
            base.Dispose(disposing);
        }

        public CapFrameXOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(24, 24);
            BackColor = OsdBgDark;
            ForeColor = CfxWhite;
            Opacity = 0.88;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            UpdateFontsForScale();
            UpdateDimensions();
            ContextMenu = BuildOverlayMenu();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                if (ClickThrough)
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void ApplyOpacity(int pct)
        {
            OpacityPct = Math.Max(35, Math.Min(100, pct));
            try
            {
                Opacity = OpacityPct / 100.0;
            }
            catch { }
        }

        private ContextMenu BuildOverlayMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Configure OSD Overlay Studio...", (s, e) =>
            {
                if (RequestOpenOsdConfig != null) RequestOpenOsdConfig(this, EventArgs.Empty);
            });
            menu.MenuItems.Add("-");

            MenuItem scaleMenu = new MenuItem("OSD Scale & Size");
            scaleMenu.MenuItems.Add("100% Standard", (s, e) => SetScaleTierFromMenu(0));
            scaleMenu.MenuItems.Add("125% Large", (s, e) => SetScaleTierFromMenu(1));
            scaleMenu.MenuItems.Add("150% Extra Large", (s, e) => SetScaleTierFromMenu(2));
            scaleMenu.MenuItems.Add("185% Jumbo (4K / TV)", (s, e) => SetScaleTierFromMenu(3));
            menu.MenuItems.Add(scaleMenu);

            MenuItem graphMenu = new MenuItem("OSD Live Graphs");
            graphMenu.MenuItems.Add("Both (Mini Sparklines + Combined Graph)", (s, e) => SetGraphModeFromMenu(3));
            graphMenu.MenuItems.Add("Combined Live Graph Only", (s, e) => SetGraphModeFromMenu(2));
            graphMenu.MenuItems.Add("Mini Sparklines per Metric Only", (s, e) => SetGraphModeFromMenu(1));
            graphMenu.MenuItems.Add("Text Only (No Graphs)", (s, e) => SetGraphModeFromMenu(0));
            graphMenu.MenuItems.Add("-");
            MenuItem heightMenu = new MenuItem("Graph Height");
            heightMenu.MenuItems.Add("45px Compact", (s, e) => SetGraphHeightFromMenu(45));
            heightMenu.MenuItems.Add("65px Medium", (s, e) => SetGraphHeightFromMenu(65));
            heightMenu.MenuItems.Add("95px Large", (s, e) => SetGraphHeightFromMenu(95));
            heightMenu.MenuItems.Add("130px Extra Large", (s, e) => SetGraphHeightFromMenu(130));
            graphMenu.MenuItems.Add(heightMenu);
            menu.MenuItems.Add(graphMenu);

            menu.MenuItems.Add("Switch Layout (Horizontal Bar / Vertical Stack)", (s, e) =>
            {
                VerticalLayout = !VerticalLayout;
                UpdateDimensions();
                Invalidate();
                if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
            });
            menu.MenuItems.Add("Lock OSD (Click-Through Mode)", (s, e) =>
            {
                if (RequestToggleLock != null) RequestToggleLock(this, EventArgs.Empty);
            });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Hide OSD Overlay", (s, e) =>
            {
                if (RequestHideOverlay != null) RequestHideOverlay(this, EventArgs.Empty);
            });
            return menu;
        }

        private void SetScaleTierFromMenu(int tier)
        {
            OsdScaleTier = Math.Max(0, Math.Min(3, tier));
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        private void SetGraphHeightFromMenu(int h)
        {
            GraphHeight = h;
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        private void SetGraphModeFromMenu(int mode)
        {
            GraphMode = mode;
            UpdateDimensions();
            Invalidate();
            if (PositionOrLayoutChanged != null) PositionOrLayoutChanged(this, EventArgs.Empty);
        }

        public void ApplyClickThrough(bool clickThrough)
        {
            ClickThrough = clickThrough;
            if (IsHandleCreated)
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                if (clickThrough)
                    ex |= (WS_EX_LAYERED | WS_EX_TRANSPARENT);
                else
                    ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
            }
            Cursor = clickThrough ? Cursors.Default : Cursors.SizeAll;
            Invalidate();
        }

        public int CountVisibleMetrics()
        {
            int c = 0;
            if (ShowCpu) c++;
            if (ShowGpu) c++;
            if (ShowPower) c++;
            if (ShowGpuLoad) c++;
            if (ShowApuFan) c++;
            if (HasDualFans && ShowSysFan) c++;
            if (ShowMode) c++;
            return Math.Max(1, c);
        }

        public void UpdateDimensions()
        {
            UpdateFontsForScale();
            float sc = GetScaleMultiplier();
            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = (GraphMode == 2 || GraphMode == 3);
            int cols = CountVisibleMetrics();
            int scaledGraphH = (int)Math.Round(GraphHeight * sc);

            if (VerticalLayout)
            {
                int baseW = showSpark ? 244 : (ShowPeaks ? 220 : 192);
                int w = (int)Math.Round(baseW * sc);
                int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                int topH = (int)Math.Round(28 * sc);
                int h = topH + cols * rowH + (int)Math.Round(6 * sc);
                if (showCombined)
                {
                    h += scaledGraphH + (int)Math.Round(10 * sc);
                }
                Size = new Size(w, h);
            }
            else
            {
                int baseColW = ShowPeaks ? 100 : 88;
                int colW = (int)Math.Round(baseColW * sc);
                int minW = (int)Math.Round((showCombined ? 380 : 160) * sc);
                int w = Math.Max(minW, (int)Math.Round(14 * sc) + cols * colW);
                int topH = (int)Math.Round((showSpark ? 50 : 32) * sc);
                int h = topH + (showCombined ? (scaledGraphH + (int)Math.Round(8 * sc)) : 0);
                Size = new Size(w, h);
            }
        }

        public void UpdateMetrics(FanStatus st, string modeSummary, bool hasDualFans)
        {
            bool layoutChanged = (HasDualFans != hasDualFans);
            _status = st;
            _modeSummary = string.IsNullOrEmpty(modeSummary) ? st.Mode : modeSummary;
            HasDualFans = hasDualFans;
            if (layoutChanged)
                UpdateDimensions();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !ClickThrough)
            {
                _dragging = true;
                _dragStartMouse = Control.MousePosition;
                _dragStartForm = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point cur = Control.MousePosition;
                Location = new Point(
                    _dragStartForm.X + (cur.X - _dragStartMouse.X),
                    _dragStartForm.Y + (cur.Y - _dragStartMouse.Y));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                if (PositionOrLayoutChanged != null)
                    PositionOrLayoutChanged(this, EventArgs.Empty);
            }
        }

        private static void DrawInlineSparkline(Graphics g, Rectangle rect, List<int> samples, Color color, float sc)
        {
            if (samples == null || samples.Count == 0 || rect.Width < 8 || rect.Height < 4)
                return;

            int take = Math.Min(samples.Count, 36);
            int start = samples.Count - take;
            int minV = int.MaxValue, maxV = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int v = samples[start + i];
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            float scaleMin = minV - 1.5f;
            float scaleMax = maxV + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minV + maxV) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float y = rect.Y + rect.Height * 0.5f;
                pts[0] = new PointF(rect.X, y);
                pts[1] = new PointF(rect.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    float x = rect.X + (rect.Width * i) / (float)(take - 1);
                    float norm = (samples[start + i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = rect.Bottom - 1 - norm * (rect.Height - 2);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom);

            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(95, color),
                Color.FromArgb(6, color),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen p = new Pen(color, Math.Max(1.4f, 1.4f * sc)))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, pts);
            }
        }

        private void DrawCombinedOsdGraph(Graphics g, Rectangle rect, Color cpuCol, Color gpuCol, Color pwrCol, Color apuCol, Color sysCol, Color gridCol)
        {
            if (rect.Width < 20 || rect.Height < 16) return;
            float sc = GetScaleMultiplier();

            Color chartBg = (MatchAppTheme && IsRadeonTheme)
                ? Color.FromArgb(180, 10, 4, 6)
                : Color.FromArgb(180, 10, 12, 18);

            using (SolidBrush bgBr = new SolidBrush(chartBg))
            using (Pen borderPen = new Pen(gridCol, 1f))
            {
                g.FillRectangle(bgBr, rect);
                int midY = rect.Y + rect.Height / 2;
                g.DrawLine(borderPen, rect.X + 1, midY, rect.Right - 1, midY);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            if (Stats == null || Stats.CpuHistory == null || Stats.CpuHistory.Count == 0)
                return;

            int total = Stats.CpuHistory.Count;
            int take = Math.Min(total, 60);
            int start = total - take;

            int tMin = int.MaxValue, tMax = int.MinValue;
            int rMin = int.MaxValue, rMax = int.MinValue;
            for (int i = 0; i < take; i++)
            {
                int idx = start + i;
                if (ShowCpu && idx < Stats.CpuHistory.Count)
                {
                    int v = Stats.CpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowGpu && idx < Stats.GpuHistory.Count && Stats.GpuHistory[idx] > 0)
                {
                    int v = Stats.GpuHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowPower && idx < Stats.PowerHistory.Count && Stats.PowerHistory[idx] > 0)
                {
                    int v = Stats.PowerHistory[idx];
                    if (v < tMin) tMin = v;
                    if (v > tMax) tMax = v;
                }
                if (ShowApuFan && idx < Stats.Fan1RpmHistory.Count)
                {
                    int v = Stats.Fan1RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
                if (HasDualFans && ShowSysFan && idx < Stats.Fan2RpmHistory.Count)
                {
                    int v = Stats.Fan2RpmHistory[idx];
                    if (v < rMin) rMin = v;
                    if (v > rMax) rMax = v;
                }
            }

            float leftLo = (tMin <= tMax) ? Math.Max(0f, tMin - 3f) : 20f;
            float leftHi = (tMin <= tMax) ? (tMax + 3f) : 90f;
            if (leftHi - leftLo < 8f)
            {
                float mid = (leftLo + leftHi) * 0.5f;
                leftLo = Math.Max(0f, mid - 4f);
                leftHi = leftLo + 8f;
            }

            float rpmLo = (rMin <= rMax) ? Math.Max(0f, rMin - 150f) : 0f;
            float rpmHi = (rMin <= rMax) ? (rMax + 150f) : 3000f;
            if (rpmHi - rpmLo < 400f)
            {
                rpmLo = Math.Max(0f, rMin - 200f);
                rpmHi = rpmLo + 400f;
            }

            int topPad = (int)Math.Round(14f * sc);
            Rectangle plot = new Rectangle(rect.X + 2, rect.Y + topPad, Math.Max(8, rect.Width - 4), Math.Max(6, rect.Height - topPad - 2));

            if (HasDualFans && ShowSysFan)
                DrawCombinedSubSeries(g, plot, Stats.Fan2RpmHistory, start, take, rpmLo, rpmHi, sysCol, false, true, sc);
            if (ShowApuFan)
                DrawCombinedSubSeries(g, plot, Stats.Fan1RpmHistory, start, take, rpmLo, rpmHi, apuCol, false, true, sc);
            if (ShowPower)
                DrawCombinedSubSeries(g, plot, Stats.PowerHistory, start, take, leftLo, leftHi, pwrCol, false, false, sc);
            if (ShowGpu)
                DrawCombinedSubSeries(g, plot, Stats.GpuHistory, start, take, leftLo, leftHi, gpuCol, false, false, sc);
            if (ShowCpu)
                DrawCombinedSubSeries(g, plot, Stats.CpuHistory, start, take, leftLo, leftHi, cpuCol, true, false, sc);

            // Compact Legend & Scale Header inside the top of the Combined OSD Graph
            string hdr = (tMin <= tMax)
                ? string.Format("LIVE TREND ({0}–{1}°C/W)", tMin, tMax)
                : "LIVE TREND";
            TextRenderer.DrawText(g, hdr, _fontChart, new Point(rect.X + (int)Math.Round(4 * sc), rect.Y + 1), Color.FromArgb(200, 226, 232, 240), TextFormatFlags.NoPadding);
        }

        private static void DrawCombinedSubSeries(Graphics g, Rectangle plot, List<int> list, int start, int take, float lo, float hi, Color col, bool fillArea, bool dashed, float sc)
        {
            if (list == null || list.Count == 0 || take <= 0) return;
            float span = Math.Max(1f, hi - lo);
            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                int idx = Math.Min(list.Count - 1, start);
                float norm = (list[idx] - lo) / span;
                norm = Math.Max(0f, Math.Min(1f, norm));
                float y = plot.Bottom - norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = Math.Min(list.Count - 1, start + i);
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float norm = (list[idx] - lo) / span;
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plot.Bottom - norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }

            if (fillArea)
            {
                PointF[] poly = new PointF[pts.Length + 2];
                Array.Copy(pts, 0, poly, 0, pts.Length);
                poly[pts.Length] = new PointF(pts[pts.Length - 1].X, plot.Bottom);
                poly[pts.Length + 1] = new PointF(pts[0].X, plot.Bottom);
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(plot.X, plot.Y - 2, Math.Max(1, plot.Width), Math.Max(1, plot.Height + 4)),
                    Color.FromArgb(65, col),
                    Color.FromArgb(4, col),
                    LinearGradientMode.Vertical))
                {
                    g.FillPolygon(br, poly);
                }
            }

            using (Pen pen = new Pen(col, (fillArea ? 1.8f : 1.4f) * Math.Max(1f, sc * 0.9f)))
            {
                pen.LineJoin = LineJoin.Round;
                if (dashed) pen.DashStyle = DashStyle.Dash;
                g.DrawLines(pen, pts);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float sc = GetScaleMultiplier();
            int scaledGraphH = (int)Math.Round(GraphHeight * sc);

            bool radeon = MatchAppTheme && IsRadeonTheme;
            Color bg = radeon ? OsdBgRadeon : OsdBgDark;
            Color border = radeon ? OsdBorderRadeon : OsdBorderDark;
            Color accentCol = radeon ? CfxRuby : CfxAmber;
            Color cpuCol = radeon ? CfxRuby : CfxCyan;
            Color gpuCol = radeon ? CfxCyan : CfxGreen;
            Color pwrCol = CfxYellow;
            Color apuCol = CfxViolet;
            Color sysCol = CfxTeal;

            g.Clear(bg);

            // Left accent bar
            using (Brush accentBrush = new SolidBrush(accentCol))
            {
                g.FillRectangle(accentBrush, 0, 0, Math.Max(3, (int)Math.Round(3 * sc)), Height);
            }

            bool showSpark = (GraphMode == 1 || GraphMode == 3);
            bool showCombined = (GraphMode == 2 || GraphMode == 3);

            string cpuVal = string.Format("{0}°C", _status.CpuTemp);
            string gpuVal = _status.GpuTemp > 0 ? string.Format("{0}°C", _status.GpuTemp) : "N/A";
            string pwrVal = _status.ApuPowerWatts > 0 ? string.Format("{0}W", _status.ApuPowerWatts) : "--W";
            string f1Val  = string.Format("{0}", _status.Fan1Rpm);
            string f2Val  = string.Format("{0}", _status.Fan2Rpm);

            string cpuPeak = (ShowPeaks && Stats != null && Stats.MaxCpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxCpuTemp) : null;
            string gpuPeak = (ShowPeaks && Stats != null && Stats.MaxGpuTemp > 0) ? string.Format("▲{0}°", Stats.MaxGpuTemp) : null;
            string pwrPeak = (ShowPeaks && Stats != null && Stats.MaxPowerWatts > 0) ? string.Format("▲{0}W", Stats.MaxPowerWatts) : null;

            using (Brush bLabel  = new SolidBrush(accentCol))
            using (Brush bCpu    = new SolidBrush(cpuCol))
            using (Brush bGpu    = new SolidBrush(gpuCol))
            using (Brush bPwr    = new SolidBrush(pwrCol))
            using (Brush bApu    = new SolidBrush(apuCol))
            using (Brush bSys    = new SolidBrush(sysCol))
            using (Brush bWhite  = new SolidBrush(CfxWhite))
            using (Brush bPeak   = new SolidBrush(Color.FromArgb(170, 203, 213, 225)))
            using (Pen sepPen    = new Pen(border, 1f))
            {
                if (!VerticalLayout)
                {
                    int cols = CountVisibleMetrics();
                    int availW = Width - (int)Math.Round(12 * sc);
                    float colW = (float)availW / cols;
                    int colIdx = 0;

                    if (ShowCpu)
                        DrawHorizCell(g, colIdx++, colW, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu)
                        DrawHorizCell(g, colIdx++, colW, "iGPU", gpuVal, gpuPeak, bLabel, bGpu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.GpuHistory : null) : null, gpuCol, sc);
                    if (ShowPower)
                        DrawHorizCell(g, colIdx++, colW, "PWR", pwrVal, pwrPeak, bLabel, bPwr, bPeak, sepPen, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowGpuLoad)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = hasFps ? "FPS" : "LOAD";
                        string val = hasFps ? string.Format("{0}", _status.RtssFps) : string.Format("{0}%", _status.GfxActivityPct);
                        string peak = (ShowPeaks && Stats != null)
                            ? (hasFps ? (Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null)
                                      : (Stats.MaxGpuLoad > 0 ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null))
                            : null;
                        Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);
                        using (Brush bLoad = new SolidBrush(loadCol))
                        {
                            DrawHorizCell(g, colIdx++, colW, lbl, val, peak, bLabel, bLoad, bPeak, sepPen, showSpark ? (Stats != null ? Stats.GpuLoadHistory : null) : null, loadCol, sc);
                        }
                    }
                    if (ShowApuFan)
                        DrawHorizCell(g, colIdx++, colW, "APU", f1Val + "r", null, bLabel, bApu, bPeak, sepPen, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (HasDualFans && ShowSysFan)
                        DrawHorizCell(g, colIdx++, colW, "SYS", f2Val + "r", null, bLabel, bSys, bPeak, sepPen, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShowMode)
                        DrawHorizCell(g, colIdx++, colW, "MODE", _modeSummary, null, bLabel, bWhite, bPeak, sepPen, null, CfxWhite, sc);

                    if (showCombined)
                    {
                        int topH = (int)Math.Round((showSpark ? 50 : 32) * sc);
                        Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), topH, Width - (int)Math.Round(14 * sc), scaledGraphH);
                        DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                    }
                }
                else
                {
                    g.DrawString(radeon ? "RADEON TELEMETRY OSD" : "MINIFANCONTROL OSD", _fontHdr, bLabel, 10f * sc, 6f * sc);
                    g.DrawLine(sepPen, (int)Math.Round(10 * sc), (int)Math.Round(24 * sc), Width - (int)Math.Round(10 * sc), (int)Math.Round(24 * sc));

                    int row = 0;
                    int rowH = (int)Math.Round((showSpark ? 25 : 21) * sc);
                    if (ShowCpu)
                        DrawVertRow(g, row++, rowH, "CPU", cpuVal, cpuPeak, bLabel, bCpu, bPeak, showSpark ? (Stats != null ? Stats.CpuHistory : null) : null, cpuCol, sc);
                    if (ShowGpu)
                        DrawVertRow(g, row++, rowH, "iGPU", gpuVal, gpuPeak, bLabel, bGpu, bPeak, showSpark ? (Stats != null ? Stats.GpuHistory : null) : null, gpuCol, sc);
                    if (ShowPower)
                        DrawVertRow(g, row++, rowH, "Power", pwrVal, pwrPeak, bLabel, bPwr, bPeak, showSpark ? (Stats != null ? Stats.PowerHistory : null) : null, pwrCol, sc);
                    if (ShowGpuLoad)
                    {
                        bool hasFps = _status.RtssFps > 0;
                        string lbl = hasFps ? "FPS" : "GPU Load";
                        string val;
                        if (hasFps)
                        {
                            val = _status.GfxActivityPct > 0
                                ? string.Format("{0} ({1}%)", _status.RtssFps, _status.GfxActivityPct)
                                : string.Format("{0} FPS", _status.RtssFps);
                        }
                        else
                        {
                            val = _status.GfxClockMhz > 0
                                ? string.Format("{0}% @ {1}MHz", _status.GfxActivityPct, _status.GfxClockMhz)
                                : string.Format("{0}%", _status.GfxActivityPct);
                        }
                        string peak = (ShowPeaks && Stats != null)
                            ? (hasFps ? (Stats.MaxFps > 0 ? string.Format("▲{0}", Stats.MaxFps) : null)
                                      : (Stats.MaxGpuLoad > 0 ? string.Format("▲{0}%", Stats.MaxGpuLoad) : null))
                            : null;
                        Color loadCol = radeon ? CfxCyan : Color.FromArgb(0xC0, 0x84, 0xFC);
                        using (Brush bLoad = new SolidBrush(loadCol))
                        {
                            DrawVertRow(g, row++, rowH, lbl, val, peak, bLabel, bLoad, bPeak, showSpark ? (Stats != null ? Stats.GpuLoadHistory : null) : null, loadCol, sc);
                        }
                    }
                    if (ShowApuFan)
                        DrawVertRow(g, row++, rowH, "APU Fan", f1Val + " RPM", null, bLabel, bApu, bPeak, showSpark ? (Stats != null ? Stats.Fan1RpmHistory : null) : null, apuCol, sc);
                    if (HasDualFans && ShowSysFan)
                        DrawVertRow(g, row++, rowH, "Sys Fan", f2Val + " RPM", null, bLabel, bSys, bPeak, showSpark ? (Stats != null ? Stats.Fan2RpmHistory : null) : null, sysCol, sc);
                    if (ShowMode)
                        DrawVertRow(g, row++, rowH, "Mode", _modeSummary, null, bLabel, bWhite, bPeak, null, CfxWhite, sc);

                    if (showCombined)
                    {
                        int yComb = (int)Math.Round(28 * sc) + row * rowH + (int)Math.Round(4 * sc);
                        Rectangle combRect = new Rectangle((int)Math.Round(8 * sc), yComb, Width - (int)Math.Round(16 * sc), scaledGraphH);
                        DrawCombinedOsdGraph(g, combRect, cpuCol, gpuCol, pwrCol, apuCol, sysCol, border);
                    }
                }
            }

            using (Pen borderPen = new Pen(border, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void DrawHorizCell(Graphics g, int colIdx, float colW, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, Pen sepPen, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float x = (8f * sc) + colIdx * colW;
            if (colIdx > 0)
            {
                g.DrawLine(sepPen, x - 3f * sc, 5f * sc, x - 3f * sc, (GraphMode == 1 || GraphMode == 3) ? (45f * sc) : (27f * sc));
            }

            g.DrawString(lbl, _fontLabel, bLbl, x, 6f * sc);
            float lblW = g.MeasureString(lbl, _fontLabel).Width;
            g.DrawString(val, _fontVal, bVal, x + lblW - 1f, 5f * sc);

            if (!string.IsNullOrEmpty(peak))
            {
                float valW = g.MeasureString(val, _fontVal).Width;
                g.DrawString(peak, _fontPeak, bPeak, x + lblW + valW - 3f * sc, 7f * sc);
            }

            if (sparkSamples != null)
            {
                int sparkY = (int)Math.Round(26f * sc);
                int sparkH = (int)Math.Round(20f * sc);
                int sparkW = Math.Max((int)Math.Round(16 * sc), (int)colW - (int)Math.Round(8 * sc));
                Rectangle sparkRect = new Rectangle((int)x + 1, sparkY, sparkW, sparkH);
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }
        }

        private void DrawVertRow(Graphics g, int rowIndex, int rowH, string lbl, string val, string peak, Brush bLbl, Brush bVal, Brush bPeak, List<int> sparkSamples, Color sparkCol, float sc)
        {
            float y = (27f * sc) + rowIndex * rowH;
            g.DrawString(lbl, _fontLabel, bLbl, 10f * sc, y);

            SizeF sz = g.MeasureString(val, _fontVal);
            float pkW = !string.IsNullOrEmpty(peak) ? g.MeasureString(peak, _fontPeak).Width : 0f;
            float rightX = Width - sz.Width - pkW - (10f * sc);

            if (sparkSamples != null)
            {
                int sparkLeft = (int)Math.Round(68f * sc);
                int sparkW = Math.Max((int)Math.Round(24 * sc), (int)rightX - sparkLeft - (int)Math.Round(6 * sc));
                Rectangle sparkRect = new Rectangle(sparkLeft, (int)y + 1, sparkW, (int)Math.Round(18f * sc));
                DrawInlineSparkline(g, sparkRect, sparkSamples, sparkCol, sc);
            }

            g.DrawString(val, _fontVal, bVal, rightX, y - 1f);
            if (!string.IsNullOrEmpty(peak))
            {
                g.DrawString(peak, _fontPeak, bPeak, rightX + sz.Width - 2f, y + 1f);
            }
        }
    }

    public static class AppLogoRenderer
    {
        public static Bitmap RenderConcept(int conceptId, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (conceptId == 2)
                {
                    RenderConceptIsometric3D(g, size);
                }
                else if (conceptId == 3)
                {
                    RenderConceptGlassDeck3D(g, size);
                }
                else if (conceptId == 4)
                {
                    RenderConceptFrontBevel3D(g, size, true);
                }
                else
                {
                    RenderConceptFrontBevel3D(g, size, false);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Concept 1 (Default): 3D Front-Bevel AMD Mini PC Chassis
        /// Features a 3D extruded front I/O panel + side depth, recessed top deck,
        /// 270-degree thermal gauge arc, reduced-size 6-blade fan, and a metallic AMD sticker underneath.
        /// When radeonTheme is true (conceptId == 4), renders in AMD Radeon Ruby-Red & Obsidian styling.
        /// </summary>
        private static void RenderConceptFrontBevel3D(Graphics g, int size, bool radeonTheme)
        {
            // 1. Ambient drop shadow under the 3D Mini PC
            using (GraphicsPath shadowPath = CreateRoundedRect(size * 0.08f, size * 0.78f, size * 0.84f, size * 0.19f, size * 0.08f))
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            {
                g.FillPath(shadowBrush, shadowPath);
            }

            // 2. 3D Lower Chassis Body / Extruded Front I/O Face (gives true 3D Mini PC box depth)
            float bodyX = size * 0.06f;
            float bodyW = size * 0.88f;
            float topLidH = size * 0.72f;
            float frontWallY = size * 0.56f;
            float frontWallH = size * 0.37f;
            float cornerR = size * 0.16f;

            using (GraphicsPath lowerChassis = CreateRoundedRect(bodyX, frontWallY, bodyW, frontWallH, cornerR))
            using (LinearGradientBrush chassisGrad = new LinearGradientBrush(
                new PointF(bodyX, frontWallY), new PointF(bodyX + bodyW, frontWallY + frontWallH),
                Color.FromArgb(0x1E, 0x29, 0x3B),
                Color.FromArgb(0x09, 0x0D, 0x16)))
            {
                g.FillPath(chassisGrad, lowerChassis);
                using (Pen lowerRim = new Pen(Color.FromArgb(0x47, 0x55, 0x69), Math.Max(1.2f, size * 0.022f)))
                {
                    g.DrawPath(lowerRim, lowerChassis);
                }
            }

            // 3. Front I/O Details on the 3D Front Face (Power Button LED, USB-C, Dual USB-A, Vent Slots)
            float ioY = size * 0.825f;
            // Glowing Power Button on front-left
            float pwrR = Math.Max(2f, size * 0.032f);
            float pwrX = size * 0.22f;
            using (Brush pwrGlow = new SolidBrush(Color.FromArgb(90, 0xEF, 0x44, 0x44)))
            using (Brush pwrCore = new SolidBrush(Color.FromArgb(0xF9, 0x73, 0x16)))
            using (Pen pwrRim = new Pen(Color.White, Math.Max(0.8f, size * 0.01f)))
            {
                g.FillEllipse(pwrGlow, pwrX - pwrR * 1.6f, ioY - pwrR * 1.6f, pwrR * 3.2f, pwrR * 3.2f);
                g.FillEllipse(pwrCore, pwrX - pwrR, ioY - pwrR, pwrR * 2f, pwrR * 2f);
                g.DrawEllipse(pwrRim, pwrX - pwrR, ioY - pwrR, pwrR * 2f, pwrR * 2f);
            }

            // Front USB-A & USB-C ports + cooling vent slots
            if (size >= 32)
            {
                using (Brush portBrush = new SolidBrush(Color.FromArgb(0x06, 0x09, 0x11)))
                using (Brush usbBlue = new SolidBrush(Color.FromArgb(0x02, 0x84, 0xC7)))
                using (Pen portRim = new Pen(Color.FromArgb(0x64, 0x74, 0x8B), Math.Max(0.8f, size * 0.01f)))
                {
                    // Two USB 3.2 ports
                    for (int u = 0; u < 2; u++)
                    {
                        float ux = size * (0.33f + u * 0.13f);
                        float uw = size * 0.095f;
                        float uh = size * 0.042f;
                        g.FillRectangle(portBrush, ux, ioY - uh * 0.5f, uw, uh);
                        g.FillRectangle(usbBlue, ux + uw * 0.15f, ioY - uh * 0.15f, uw * 0.7f, uh * 0.35f);
                        g.DrawRectangle(portRim, ux, ioY - uh * 0.5f, uw, uh);
                    }
                    // USB4 / Type-C oval port
                    float cxPort = size * 0.60f;
                    float cwPort = size * 0.07f;
                    float chPort = size * 0.032f;
                    using (GraphicsPath typeC = CreateRoundedRect(cxPort, ioY - chPort * 0.5f, cwPort, chPort, chPort * 0.45f))
                    {
                        g.FillPath(portBrush, typeC);
                        g.DrawPath(portRim, typeC);
                    }
                    // Right-side vent grilles
                    using (Pen ventPen = new Pen(Color.FromArgb(0x09, 0x0D, 0x16), Math.Max(1.2f, size * 0.016f)))
                    {
                        ventPen.StartCap = LineCap.Round;
                        ventPen.EndCap = LineCap.Round;
                        for (int v = 0; v < 4; v++)
                        {
                            float vx = size * (0.72f + v * 0.038f);
                            g.DrawLine(ventPen, vx, ioY - size * 0.025f, vx, ioY + size * 0.025f);
                        }
                    }
                }
            }

            // 4. 3D Top Deck / Lid of the Mini PC (tilted perspective squircle sitting above the front face)
            float lidY = size * 0.045f;
            using (GraphicsPath topLid = CreateRoundedRect(bodyX, lidY, bodyW, topLidH, cornerR))
            using (LinearGradientBrush lidGrad = new LinearGradientBrush(
                new PointF(bodyX, lidY), new PointF(bodyX, lidY + topLidH),
                Color.FromArgb(0x24, 0x30, 0x46),
                Color.FromArgb(0x0D, 0x13, 0x22)))
            {
                g.FillPath(lidGrad, topLid);

                // Inner recessed dark chamber on the top lid
                float inset = size * 0.038f;
                using (GraphicsPath innerWell = CreateRoundedRect(
                    bodyX + inset, lidY + inset, bodyW - inset * 2f, topLidH - inset * 2f, cornerR * 0.80f))
                using (LinearGradientBrush wellGrad = new LinearGradientBrush(
                    new PointF(0, lidY), new PointF(0, lidY + topLidH),
                    Color.FromArgb(0x09, 0x0E, 0x1A),
                    Color.FromArgb(0x13, 0x1C, 0x2E)))
                {
                    g.FillPath(wellGrad, innerWell);
                }

                // Bright 3D metallic bevel rim on top lid (Cyan in Standard, Ruby Red in Radeon Theme)
                using (LinearGradientBrush rimGrad = new LinearGradientBrush(
                    new PointF(bodyX, lidY), new PointF(bodyX + bodyW, lidY + topLidH),
                    radeonTheme ? Color.FromArgb(0xFB, 0x71, 0x85) : Color.FromArgb(0x7D, 0xD3, 0xFC),
                    radeonTheme ? Color.FromArgb(0xBE, 0x12, 0x3C) : Color.FromArgb(0x02, 0x84, 0xC7)))
                using (Pen lidRim = new Pen(rimGrad, Math.Max(1.5f, size * 0.028f)))
                {
                    g.DrawPath(lidRim, topLid);
                }
            }

            // 5. 270-Degree Thermal Gauge Arc on the Top Deck (Option 3 style)
            float cx = size * 0.50f;
            float cy = size * 0.355f;
            float arcR = size * 0.255f;
            DrawThermalGaugeArc(g, cx, cy, arcR, Math.Max(2f, size * 0.048f), size >= 48);

            // 6. Reduced-Size 6-Blade Fan Impeller inside the Thermal Gauge
            float fanR = size * 0.165f;
            DrawImpeller(g, cx, cy, fanR, 6,
                radeonTheme ? Color.FromArgb(0xFF, 0xE4, 0xE6) : Color.FromArgb(0xE2, 0xE8, 0xF0),
                radeonTheme ? Color.FromArgb(0xE1, 0x1D, 0x48) : Color.FromArgb(0x38, 0xBD, 0xF8),
                -10f);

            // 7. 3D AMD RYZEN Metallic Sticker Badge underneath the fan (tucked into lower top deck)
            DrawAmdStickerBadge(g, size * 0.23f, size * 0.555f, size * 0.54f, size * 0.145f, size);
        }

        /// <summary>
        /// Concept 2: 3D Isometric Cube AMD Mini PC
        /// Shows true 3D top, left-side, and front-right perspective faces with reduced fan,
        /// 270-degree thermal gauge, and an AMD sticker badge underneath.
        /// </summary>
        private static void RenderConceptIsometric3D(Graphics g, int size)
        {
            // 3D Isometric / Stepped Mini PC Chassis with right & bottom 3D extrusion
            float frontX = size * 0.06f;
            float frontY = size * 0.05f;
            float boxW = size * 0.80f;
            float boxH = size * 0.72f;
            float depthX = size * 0.08f;
            float depthY = size * 0.16f;
            float cornerR = size * 0.14f;

            // Back/Bottom 3D extruded block
            using (GraphicsPath extruded = CreateRoundedRect(frontX + depthX, frontY + depthY, boxW, boxH, cornerR))
            using (LinearGradientBrush extGrad = new LinearGradientBrush(
                new PointF(frontX, frontY + depthY), new PointF(frontX + boxW + depthX, frontY + boxH + depthY),
                Color.FromArgb(0x0F, 0x17, 0x2A),
                Color.FromArgb(0x05, 0x08, 0x11)))
            {
                g.FillPath(extGrad, extruded);
                using (Pen extRim = new Pen(Color.FromArgb(0xEA, 0x58, 0x0C), Math.Max(1.2f, size * 0.022f)))
                {
                    g.DrawPath(extRim, extruded);
                }
            }

            // Side 3D cooling louver lines on the right extruded wall
            if (size >= 48)
            {
                using (Pen louverPen = new Pen(Color.FromArgb(0x33, 0x41, 0x55), Math.Max(1f, size * 0.015f)))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float ly = size * (0.26f + i * 0.075f);
                        g.DrawLine(louverPen, frontX + boxW + size * 0.015f, ly, frontX + boxW + depthX - size * 0.015f, ly + size * 0.03f);
                    }
                }
            }

            // Top/Front main Mini PC Deck
            using (GraphicsPath topDeck = CreateRoundedRect(frontX, frontY, boxW, boxH, cornerR))
            using (LinearGradientBrush deckGrad = new LinearGradientBrush(
                new PointF(frontX, frontY), new PointF(frontX + boxW, frontY + boxH),
                Color.FromArgb(0x1E, 0x29, 0x3B),
                Color.FromArgb(0x0B, 0x0F, 0x19)))
            {
                g.FillPath(deckGrad, topDeck);
                using (Pen deckRim = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), Math.Max(1.5f, size * 0.028f)))
                {
                    g.DrawPath(deckRim, topDeck);
                }
            }

            // Thermal gauge + smaller fan centered on the top deck
            float cx = frontX + boxW * 0.50f;
            float cy = frontY + boxH * 0.43f;
            DrawThermalGaugeArc(g, cx, cy, size * 0.24f, Math.Max(2f, size * 0.046f), size >= 48);
            DrawImpeller(g, cx, cy, size * 0.155f, 6,
                Color.FromArgb(0x38, 0xBD, 0xF8),
                Color.FromArgb(0x25, 0x63, 0xEB),
                -15f);

            // AMD Sticker underneath the fan
            DrawAmdStickerBadge(g, cx - size * 0.26f, frontY + boxH * 0.72f, size * 0.52f, size * 0.145f, size);
        }

        /// <summary>
        /// Concept 3: 3D Glass-Top K12 Mini PC with Underglow Ring
        /// Deep 3D terraced chassis with an orange/red AMD accent rim, compact turbine fan inside
        /// the 270-degree thermal gauge, and a bold silver/orange AMD sticker badge on the front lip.
        /// </summary>
        private static void RenderConceptGlassDeck3D(Graphics g, int size)
        {
            // Outer 3D Base Plinth (dark metallic base with AMD Orange underglow)
            float basePad = size * 0.05f;
            float baseW = size * 0.90f;
            float baseH = size * 0.90f;
            using (GraphicsPath basePlinth = CreateRoundedRect(basePad, basePad + size * 0.03f, baseW, baseH - size * 0.03f, size * 0.18f))
            using (LinearGradientBrush baseGrad = new LinearGradientBrush(
                new PointF(0, 0), new PointF(0, size),
                Color.FromArgb(0x33, 0x41, 0x55),
                Color.FromArgb(0x09, 0x0D, 0x16)))
            {
                g.FillPath(baseGrad, basePlinth);
                using (Pen glowRim = new Pen(Color.FromArgb(0xF9, 0x73, 0x16), Math.Max(1.5f, size * 0.025f)))
                {
                    g.DrawPath(glowRim, basePlinth);
                }
            }

            // Elevated 3D Glass Top Lid
            float lidPad = size * 0.10f;
            float lidW = size * 0.80f;
            float lidH = size * 0.74f;
            using (GraphicsPath glassLid = CreateRoundedRect(lidPad, size * 0.055f, lidW, lidH, size * 0.15f))
            using (LinearGradientBrush lidGrad = new LinearGradientBrush(
                new PointF(lidPad, size * 0.05f), new PointF(lidPad + lidW, size * 0.80f),
                Color.FromArgb(0x16, 0x20, 0x33),
                Color.FromArgb(0x0A, 0x0E, 0x17)))
            {
                g.FillPath(lidGrad, glassLid);
                using (Pen cyanRim = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), Math.Max(1.5f, size * 0.026f)))
                {
                    g.DrawPath(cyanRim, glassLid);
                }
            }

            // 270-degree Thermal Gauge + Reduced-size 6-blade Impeller
            float cx = size * 0.50f;
            float cy = size * 0.365f;
            DrawThermalGaugeArc(g, cx, cy, size * 0.245f, Math.Max(2f, size * 0.048f), size >= 48);
            DrawImpeller(g, cx, cy, size * 0.155f, 6,
                Color.FromArgb(0xF8, 0xFA, 0xFC),
                Color.FromArgb(0x38, 0xBD, 0xF8),
                0f);

            // Prominent 3D AMD Sticker Badge overlapping the lower lip
            DrawAmdStickerBadge(g, size * 0.21f, size * 0.585f, size * 0.58f, size * 0.155f, size);
        }

        private static void DrawThermalGaugeArc(Graphics g, float cx, float cy, float arcR, float arcW, bool drawTicks)
        {
            RectangleF arcRect = new RectangleF(cx - arcR, cy - arcR, arcR * 2f, arcR * 2f);

            // Subtle dark track behind the gauge
            using (Pen trackPen = new Pen(Color.FromArgb(90, 0x33, 0x41, 0x55), arcW * 1.15f))
            {
                trackPen.StartCap = LineCap.Round;
                trackPen.EndCap = LineCap.Round;
                g.DrawArc(trackPen, arcRect, 135f, 270f);
            }

            using (Pen pCool = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), arcW))
            using (Pen pMid  = new Pen(Color.FromArgb(0x4A, 0xDE, 0x80), arcW))
            using (Pen pHot  = new Pen(Color.FromArgb(0xF9, 0x73, 0x16), arcW))
            {
                pCool.StartCap = LineCap.Round;
                pHot.EndCap = LineCap.Round;
                g.DrawArc(pCool, arcRect, 135f, 90f);
                g.DrawArc(pMid,  arcRect, 225f, 90f);
                g.DrawArc(pHot,  arcRect, 315f, 90f);
            }

            if (drawTicks)
            {
                using (Pen tickPen = new Pen(Color.FromArgb(150, 0x94, 0xA3, 0xB8), Math.Max(1f, arcW * 0.22f)))
                {
                    for (int deg = 135; deg <= 405; deg += 45)
                    {
                        double rad = deg * Math.PI / 180.0;
                        float r1 = arcR - arcW * 0.85f;
                        float r2 = arcR - arcW * 0.45f;
                        g.DrawLine(tickPen,
                            cx + (float)(Math.Cos(rad) * r1),
                            cy + (float)(Math.Sin(rad) * r1),
                            cx + (float)(Math.Cos(rad) * r2),
                            cy + (float)(Math.Sin(rad) * r2));
                    }
                }
            }
        }

        /// <summary>
        /// Draws a crisp 3D metallic AMD Ryzen sticker badge underneath the fan.
        /// Includes the signature AMD orange/red corner chevron + bold "AMD" label.
        /// </summary>
        private static void DrawAmdStickerBadge(Graphics g, float x, float y, float w, float h, int iconSize)
        {
            float r = Math.Max(2f, h * 0.26f);

            // Drop shadow under sticker
            using (GraphicsPath sh = CreateRoundedRect(x, y + h * 0.12f, w, h, r))
            using (Brush sb = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                g.FillPath(sb, sh);
            }

            // Brushed metallic dark-graphite & silver sticker base
            using (GraphicsPath badge = CreateRoundedRect(x, y, w, h, r))
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new PointF(x, y), new PointF(x + w, y + h),
                Color.FromArgb(0x27, 0x32, 0x44),
                Color.FromArgb(0x0F, 0x14, 0x20)))
            {
                g.FillPath(bg, badge);

                // Left AMD Orange/Red accent tab inside the sticker
                float accentW = w * 0.28f;
                using (GraphicsPath leftTab = CreateRoundedRect(x, y, accentW, h, r))
                using (LinearGradientBrush orangeGrad = new LinearGradientBrush(
                    new PointF(x, y), new PointF(x + accentW, y + h),
                    Color.FromArgb(0xFF, 0x57, 0x22),
                    Color.FromArgb(0xDC, 0x26, 0x26)))
                {
                    g.FillPath(orangeGrad, leftTab);
                }

                // White geometric AMD corner arrow inside the orange tab
                float ax = x + accentW * 0.24f;
                float ay = y + h * 0.24f;
                float asz = Math.Min(accentW, h) * 0.52f;
                PointF[] arrowPts = new PointF[]
                {
                    new PointF(ax, ay),
                    new PointF(ax + asz, ay),
                    new PointF(ax + asz, ay + asz),
                    new PointF(ax + asz * 0.68f, ay + asz * 0.68f),
                    new PointF(ax + asz * 0.68f, ay + asz * 0.32f),
                    new PointF(ax + asz * 0.32f, ay + asz * 0.32f)
                };
                using (Brush wb = new SolidBrush(Color.White))
                {
                    g.FillPolygon(wb, arrowPts);
                }

                // Metallic silver/orange border around the sticker
                using (Pen borderPen = new Pen(Color.FromArgb(0xCBD5E1 >> 16 & 0xFF, 0xD5, 0xE1), Math.Max(1f, iconSize * 0.016f)))
                {
                    g.DrawPath(borderPen, badge);
                }

                // Crisp "AMD" text on the right portion of the sticker
                RectangleF textRect = new RectangleF(x + accentW * 0.92f, y - h * 0.04f, w - accentW * 0.92f, h * 1.08f);
                float fontPx = Math.Max(6.5f, h * 0.66f);
                using (Font amdFont = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString("AMD", amdFont, textBrush, textRect, sf);
                }
            }
        }

        private static void DrawImpeller(Graphics g, float cx, float cy, float radius, int blades, Color c1, Color c2, float startDeg)
        {
            using (Pen ringPen = new Pen(Color.FromArgb(140, c1.R, c1.G, c1.B), Math.Max(1.2f, radius * 0.10f)))
            {
                g.DrawEllipse(ringPen, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(startDeg);

            float step = 360f / blades;
            float rInner = radius * 0.22f;
            float rOuter = radius * 0.88f;

            for (int i = 0; i < blades; i++)
            {
                using (GraphicsPath blade = new GraphicsPath())
                {
                    blade.AddBezier(
                        new PointF(rInner * 0.5f, -rInner * 0.4f),
                        new PointF(rOuter * 0.45f, -rOuter * 0.55f),
                        new PointF(rOuter * 0.92f, -rOuter * 0.35f),
                        new PointF(rOuter * 0.94f, rOuter * 0.05f));
                    blade.AddBezier(
                        new PointF(rOuter * 0.94f, rOuter * 0.05f),
                        new PointF(rOuter * 0.65f, rOuter * 0.24f),
                        new PointF(rInner * 1.2f, rInner * 0.25f),
                        new PointF(rInner * 0.5f, -rInner * 0.4f));
                    blade.CloseFigure();

                    using (LinearGradientBrush bb = new LinearGradientBrush(
                        new PointF(0, -rOuter), new PointF(rOuter, rOuter), c1, c2))
                    {
                        g.FillPath(bb, blade);
                    }
                }
                g.RotateTransform(step);
            }
            g.Restore(st);

            float hubR = radius * 0.26f;
            using (Brush hubBrush = new SolidBrush(Color.FromArgb(0x1E, 0x29, 0x3B)))
            using (Pen hubPen = new Pen(c1, Math.Max(1.2f, radius * 0.08f)))
            {
                g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
                g.DrawEllipse(hubPen, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
            }
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void WriteMultiResIco(string icoPath, int conceptId)
        {
            int[] sizes = new int[] { 256, 64, 48, 32, 16 };
            List<byte[]> pngPayloads = new List<byte[]>();

            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = RenderConcept(conceptId, sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngPayloads.Add(ms.ToArray());
                }
            }

            using (FileStream fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); // Reserved
                bw.Write((ushort)1); // Type 1 = ICO
                bw.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    byte[] data = pngPayloads[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)data.Length);
                    bw.Write((uint)offset);
                    offset += data.Length;
                }

                for (int i = 0; i < pngPayloads.Count; i++)
                {
                    bw.Write(pngPayloads[i]);
                }
            }
        }

        public static void WriteConceptShowcasePng(string pngPath)
        {
            int w = 960;
            int h = 360;
            using (Bitmap sheet = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(0x0F, 0x17, 0x2A));

                string[] titles = new string[]
                {
                    "Option 1: 3D Front-I/O Mini PC",
                    "Option 2: 3D Isometric Cube PC",
                    "Option 3: 3D Glass-Top Plinth PC"
                };
                string[] descs = new string[]
                {
                    "3D Front I/O + Power LED, Thermal Gauge, Compact Fan & AMD Sticker",
                    "3D Extruded Side Louvers, Thermal Gauge, Cyan Fan & AMD Sticker",
                    "3D Orange Underglow Plinth, Thermal Gauge, White/Cyan Fan & AMD Badge"
                };

                using (Font fTitle = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                using (Font fDesc  = new Font("Segoe UI", 9.25f, FontStyle.Regular))
                using (Brush bTitle = new SolidBrush(Color.White))
                using (Brush bDesc  = new SolidBrush(Color.FromArgb(0x94, 0xA3, 0xB8)))
                using (Brush bCard  = new SolidBrush(Color.FromArgb(0x1E, 0x29, 0x3B)))
                using (Pen pBorder  = new Pen(Color.FromArgb(0x33, 0x41, 0x55), 1.5f))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int cardX = 24 + i * 310;
                        int cardY = 20;
                        int cardW = 292;
                        int cardH = 320;
                        using (GraphicsPath cp = CreateRoundedRect(cardX, cardY, cardW, cardH, 16f))
                        {
                            g.FillPath(bCard, cp);
                            g.DrawPath(pBorder, cp);
                        }

                        using (Bitmap iconBmp = RenderConcept(i + 1, 184))
                        {
                            g.DrawImage(iconBmp, cardX + (cardW - 184) / 2, cardY + 22, 184, 184);
                        }

                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString(titles[i], fTitle, bTitle, new RectangleF(cardX + 8, cardY + 222, cardW - 16, 28), sf);
                        g.DrawString(descs[i], fDesc, bDesc, new RectangleF(cardX + 12, cardY + 254, cardW - 24, 48), sf);
                    }
                }

                sheet.Save(pngPath, ImageFormat.Png);
            }
        }
    }

    public static class WatchdogSupervisor
    {
        public const string HEARTBEAT_EVENT_PREFIX = "Global\\MiniFanControl_Heartbeat_";
        public const string SHUTDOWN_EVENT_PREFIX = "Global\\MiniFanControl_Shutdown_";
        public const int DEFAULT_TIMEOUT_MS = 3500;
        public const int DEFAULT_POLL_INTERVAL_MS = 250;

        public static string GetHeartbeatEventName(int pid)
        {
            return "MiniFanControl_Heartbeat_" + pid;
        }

        public static string GetShutdownEventName(int pid)
        {
            return "MiniFanControl_Shutdown_" + pid;
        }

        public static EventWaitHandle CreateOrOpenEvent(string baseName, EventResetMode mode, out bool createdNew)
        {
            createdNew = false;
            try
            {
                return new EventWaitHandle(false, mode, "Global\\" + baseName, out createdNew);
            }
            catch
            {
                try
                {
                    return new EventWaitHandle(false, mode, "Local\\" + baseName, out createdNew);
                }
                catch
                {
                    return new EventWaitHandle(false, mode);
                }
            }
        }

        public static void LogWatchdogEvent(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watchdog.log");
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [PID {1}] {2}{3}",
                    DateTime.Now, Process.GetCurrentProcess().Id, message, Environment.NewLine);
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        public static void RestoreAutoFailsafe(bool demoMode)
        {
            try
            {
                LogWatchdogEvent("Triggering emergency hardware failsafe -> restoring BIOS Auto fan control.");
                if (demoMode)
                {
                    Console.WriteLine("[WATCHDOG] Demo mode: Auto failsafe engaged.");
                    return;
                }

                using (HardwareBackend hw = new HardwareBackend())
                {
                    hw.SetAuto();
                    Console.WriteLine("[WATCHDOG] Hardware fan control restored to BIOS Auto successfully.");
                }
            }
            catch (Exception ex)
            {
                string err = "[WATCHDOG ERROR] Failed to restore BIOS Auto: " + ex.Message;
                Console.Error.WriteLine(err);
                LogWatchdogEvent(err);
            }
        }

        public static int Run(int parentPid, int timeoutMs = DEFAULT_TIMEOUT_MS, bool demoMode = false)
        {
            LogWatchdogEvent(string.Format("Supervisor started for parent PID {0} with timeout {1}ms", parentPid, timeoutMs));
            Console.WriteLine(string.Format("[WATCHDOG] Initializing supervisor for PID {0} (Timeout: {1}ms)...", parentPid, timeoutMs));

            Process parentProcess = null;
            try
            {
                parentProcess = Process.GetProcessById(parentPid);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[WATCHDOG] Warning: Parent PID {0} not found or exited: {1}", parentPid, ex.Message));
                if (!demoMode)
                {
                    RestoreAutoFailsafe(demoMode);
                }
                return 1;
            }

            EventWaitHandle heartbeatEvent = null;
            EventWaitHandle shutdownEvent = null;

            try
            {
                bool createdHb, createdSd;
                heartbeatEvent = CreateOrOpenEvent(GetHeartbeatEventName(parentPid), EventResetMode.AutoReset, out createdHb);
                shutdownEvent = CreateOrOpenEvent(GetShutdownEventName(parentPid), EventResetMode.ManualReset, out createdSd);

                return ExecuteLoop(parentProcess, parentPid, timeoutMs, heartbeatEvent, shutdownEvent, demoMode, false);
            }
            finally
            {
                if (heartbeatEvent != null) { try { heartbeatEvent.Close(); } catch { } }
                if (shutdownEvent != null) { try { shutdownEvent.Close(); } catch { } }
                if (parentProcess != null) { try { parentProcess.Dispose(); } catch { } }
            }
        }

        public static int RunSimulated(int parentPid, int timeoutMs, EventWaitHandle heartbeatEvent, EventWaitHandle shutdownEvent, bool initialShutdownSet)
        {
            Process currentProc = Process.GetCurrentProcess();
            if (initialShutdownSet)
            {
                shutdownEvent.Set();
            }
            return ExecuteLoop(currentProc, parentPid, timeoutMs, heartbeatEvent, shutdownEvent, true, true);
        }

        public static int ExecuteLoop(
            Process parentProcess,
            int parentPid,
            int timeoutMs,
            EventWaitHandle heartbeatEvent,
            EventWaitHandle shutdownEvent,
            bool demoMode,
            bool testMode)
        {
            DateTime lastHeartbeatUtc = DateTime.UtcNow;
            bool triggeredFailsafe = false;

            while (true)
            {
                // 1. Check if parent process exited
                bool parentExited = false;
                try
                {
                    parentExited = parentProcess.HasExited;
                }
                catch
                {
                    parentExited = true;
                }

                if (parentExited)
                {
                    bool cleanShutdown = shutdownEvent.WaitOne(50);
                    if (cleanShutdown)
                    {
                        Console.WriteLine(string.Format("[WATCHDOG] Parent PID {0} exited gracefully. Terminating watchdog.", parentPid));
                        LogWatchdogEvent(string.Format("Parent PID {0} exited gracefully. Terminating watchdog.", parentPid));
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine(string.Format("[WATCHDOG TRIGGER] Parent PID {0} terminated unexpectedly! Restoring BIOS fan control...", parentPid));
                        LogWatchdogEvent(string.Format("Parent PID {0} terminated unexpectedly (crash/killed)! Restoring BIOS fan control...", parentPid));
                        RestoreAutoFailsafe(demoMode);
                        return 2;
                    }
                }

                // 2. Check for clean shutdown signal while process is still alive
                if (shutdownEvent.WaitOne(0))
                {
                    Console.WriteLine(string.Format("[WATCHDOG] Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    LogWatchdogEvent(string.Format("Clean shutdown signaled by PID {0}. Terminating watchdog.", parentPid));
                    return 0;
                }

                // 3. Wait on heartbeat with poll interval
                int pollWait = Math.Min(DEFAULT_POLL_INTERVAL_MS, Math.Max(20, timeoutMs / 3));
                if (heartbeatEvent.WaitOne(pollWait))
                {
                    lastHeartbeatUtc = DateTime.UtcNow;
                }
                else
                {
                    double elapsedMs = (DateTime.UtcNow - lastHeartbeatUtc).TotalMilliseconds;
                    if (elapsedMs >= timeoutMs && !triggeredFailsafe)
                    {
                        Console.WriteLine(string.Format("[WATCHDOG TRIGGER] Parent PID {0} heartbeat missed for {1:0}ms (Timeout: {2}ms)! Main thread hung. Restoring BIOS fan control...", parentPid, elapsedMs, timeoutMs));
                        LogWatchdogEvent(string.Format("Parent PID {0} heartbeat missed for {1:0}ms! Restoring BIOS fan control...", parentPid, elapsedMs));
                        RestoreAutoFailsafe(demoMode);
                        triggeredFailsafe = true;
                        if (testMode) return 3;
                    }
                }

                if (testMode && triggeredFailsafe)
                {
                    return 3;
                }
            }
        }
    }

    public class MainForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY      = 0x0312;
        private const uint MOD_ALT       = 0x0001;
        private const uint MOD_CONTROL   = 0x0002;
        private const uint MOD_NOREPEAT  = 0x4000;
        private const uint VK_F9         = 0x78;
        private const uint VK_F10        = 0x79;
        private const uint VK_F11        = 0x7A;
        private const uint VK_F12        = 0x7B;

        private const int HOTKEY_TOGGLE_OSD = 9001;
        private const int HOTKEY_CYCLE_MODE = 9002;
        private const int HOTKEY_LOCK_OSD   = 9003;
        private const int HOTKEY_RESET_AUTO = 9004;

        public const string APP_VERSION         = "2.6.0";
        public const string SCHTASK_NAME        = "MiniFanControl_Autostart";
        public const string URL_GMKTEC_DRIVERS  = "https://www.gmktec.com/pages/drivers-and-software";
        public const string URL_GMKTEC_FIRMWARE = "https://www.gmktec.com/pages/firmware-update";
        public const string URL_AMD_DRIVERS     = "https://www.amd.com/en/support/download/drivers.html";

        private const int DEFAULT_REFRESH_MS = 1000;
        private const int FAST_REFRESH_MS   = 400;

        // Dark Palette
        private static readonly Color DarkBg       = Color.FromArgb(0x14, 0x14, 0x1E);
        private static readonly Color DarkSurface  = Color.FromArgb(0x22, 0x22, 0x32);
        private static readonly Color DarkSurface2 = Color.FromArgb(0x2E, 0x30, 0x44);
        private static readonly Color DarkText     = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color DarkTextDim  = Color.FromArgb(0x94, 0x99, 0xB0);
        private static readonly Color DarkAccent   = Color.FromArgb(0x89, 0xB4, 0xFA);
        private static readonly Color DarkTeal     = Color.FromArgb(0x2D, 0xD4, 0xBF);

        // Light Palette
        private static readonly Color LightBg       = Color.FromArgb(0xF3, 0xF4, 0xF6);
        private static readonly Color LightSurface  = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color LightSurface2 = Color.FromArgb(0xE5, 0xE7, 0xEB);
        private static readonly Color LightText     = Color.FromArgb(0x00, 0x00, 0x00);
        private static readonly Color LightTextDim  = Color.FromArgb(0x4B, 0x55, 0x63);
        private static readonly Color LightAccent   = Color.FromArgb(0x1D, 0x4E, 0xD8);
        private static readonly Color LightTeal     = Color.FromArgb(0x0D, 0x94, 0x88);

        // AMD Radeon (Adrenalin Crimson / Ruby) Palette
        private static readonly Color RadeonBg       = Color.FromArgb(0x14, 0x0A, 0x0D);
        private static readonly Color RadeonSurface  = Color.FromArgb(0x21, 0x11, 0x16);
        private static readonly Color RadeonSurface2 = Color.FromArgb(0x34, 0x19, 0x21);
        private static readonly Color RadeonText     = Color.FromArgb(0xFF, 0xF1, 0xF2);
        private static readonly Color RadeonTextDim  = Color.FromArgb(0xCB, 0x9E, 0xA8);
        private static readonly Color RadeonAccent   = Color.FromArgb(0xE1, 0x1D, 0x48);
        private static readonly Color RadeonOrange   = Color.FromArgb(0xF9, 0x73, 0x16);

        private static readonly Color ErrorBgDark   = Color.FromArgb(0x3A, 0x1A, 0x1A);
        private static readonly Color ErrorFgDark   = Color.FromArgb(0xF3, 0x8B, 0xA8);
        private static readonly Color ErrorBgLight  = Color.FromArgb(0xFE, 0xE2, 0xE2);
        private static readonly Color ErrorFgLight  = Color.FromArgb(0x99, 0x1B, 0x1B);

        // Cached shared button fonts (prevents GDI HFONT allocations on every 1.5s refresh tick)
        private static readonly Font TabFontBold     = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font TabFontRegular  = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private static readonly Font ModeFontBold    = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        private static readonly Font ModeFontRegular = new Font("Segoe UI", 9.25f, FontStyle.Regular);

        private readonly HardwareBackend _hw = new HardwareBackend();
        private readonly DemoBackend _demo = new DemoBackend();
        private readonly SystemHardwareInfo _sysInfo;
        private bool _demoMode;

        private ThemePreference _themePref = ThemePreference.System;
        private bool _isDarkMode = true;
        private bool _isTaskbarDark = true;

        private bool _showCpuTray = true;
        private bool _showGpuTray = true;
        private bool _showOverlay = false;
        private bool _lockOverlay = false;
        private bool _overlayVertical = false;
        private int _overlayX = 24;
        private int _overlayY = 24;
        private bool _restoreAutoOnExit = true;
        private bool _enableGlobalHotkeys = true;
        private bool _hotkeysRegistered = false;
        private bool _startMinimized = false;
        private bool _safeExitRestored = false;
        private readonly TelemetrySessionStats _sessionStats = new TelemetrySessionStats();
        private CapFrameXOverlayForm _overlayForm;

        // CapFrameX OSD Overlay Studio Configuration
        private int _osdScaleTier = 0; // 0 = 100% (Standard), 1 = 125% (Large), 2 = 150% (Extra Large), 3 = 185% (Jumbo 4K)
        private int _osdGraphMode = 3; // 0=None, 1=Sparklines, 2=Combined, 3=Both
        private int _osdGraphHeight = 65; // 45=Compact, 65=Medium, 95=Large, 130=XL
        private int _osdOpacityPct = 88; // 35..100
        private bool _osdMatchTheme = true;
        private bool _osdShowCpu = true;
        private bool _osdShowGpu = true;
        private bool _osdShowPower = true;
        private bool _osdShowGpuLoad = true;
        private bool _osdShowApuFan = true;
        private bool _osdShowSysFan = true;
        private bool _osdShowMode = true;
        private bool _osdShowPeaks = true;

        private IntPtr _cpuIconHandle = IntPtr.Zero;
        private IntPtr _gpuIconHandle = IntPtr.Zero;
        private int _lastCpuIconKey = int.MinValue;
        private int _lastGpuIconKey = int.MinValue;

        private DateTime _rampUntil = DateTime.MinValue;

        private Process _watchdogProcess;
        private EventWaitHandle _watchdogHeartbeatEvent;
        private EventWaitHandle _watchdogShutdownEvent;
        private bool _watchdogStarted = false;
        private SessionEndingEventHandler _sessionEndingHandler;
        private PowerModeChangedEventHandler _powerModeChangedHandler;
        private string _pendingActionText = null;
        private int _rampTargetFan = 0;

        // 3 Built-in EC profiles with separate APU (Fan 1) & System RAM/NVMe (Fan 2) curves
        // Note: Minimum spinning PWM on K12 fans is 30% (0% = Fan Off / Idle).
        private readonly FanCurveProfile[] _builtInProfiles = new FanCurveProfile[]
        {
            new FanCurveProfile("Balanced", true, 0,
                new CurvePoint[] { new CurvePoint(40, 0),  new CurvePoint(48, 35), new CurvePoint(65, 55), new CurvePoint(78, 80), new CurvePoint(88, 100) },
                new CurvePoint[] { new CurvePoint(40, 35), new CurvePoint(55, 42), new CurvePoint(70, 52), new CurvePoint(80, 65), new CurvePoint(90, 80) }),
            new FanCurveProfile("Performance", true, 1,
                new CurvePoint[] { new CurvePoint(38, 40), new CurvePoint(50, 55), new CurvePoint(62, 75), new CurvePoint(74, 92), new CurvePoint(84, 100) },
                new CurvePoint[] { new CurvePoint(38, 45), new CurvePoint(52, 58), new CurvePoint(66, 72), new CurvePoint(78, 85), new CurvePoint(88, 100) }),
            new FanCurveProfile("Silent", true, 2,
                new CurvePoint[] { new CurvePoint(48, 0),  new CurvePoint(56, 30), new CurvePoint(70, 45), new CurvePoint(82, 70), new CurvePoint(92, 100) },
                new CurvePoint[] { new CurvePoint(45, 30), new CurvePoint(60, 34), new CurvePoint(72, 42), new CurvePoint(84, 52), new CurvePoint(92, 65) })
        };

        private readonly List<FanCurveProfile> _customProfiles = new List<FanCurveProfile>();
        private FanCurveProfile _apuProfile;
        private FanCurveProfile _sysProfile;
        private bool _hasDualFans = true;
        private bool _apuManualOverride = false;
        private bool _sysManualOverride = false;
        private int _lastAppliedApuPct = -1;
        private int _lastAppliedSysPct = -1;
        private int _lastAppliedEcMode = -1;
        private int _lastCustomEvalTemp = -100;
        private bool _emergencyThermalActive = false;

        private Thread _hwThread;
        private volatile bool _hwThreadRunning;
        private volatile bool _hwWorkerPaused;
        private readonly AutoResetEvent _hwWakeEvent = new AutoResetEvent(false);

        private readonly List<Label> _sectionHeaders = new List<Label>();
        private readonly List<Panel> _sectionCards = new List<Panel>();
        private readonly List<Label> _staticLabels = new List<Label>();

        private Label _hwBadgeLabel;
        private Label _hwSubLabel;
        private Button _driversMenuBtn;
        private ContextMenu _driversMenu;
        private Button _themeToggleBtn;
        private Label _errorBanner;
        private DoubleBufferedPanel _cpuTempBox;
        private DoubleBufferedPanel _gpuTempBox;
        private DoubleBufferedPanel _pwrBox;
        private DoubleBufferedPanel _fan1Box;
        private DoubleBufferedPanel _fan2Box;
        private Label _samplingLabel;
        private ComboBox _samplingCombo;
        private FanStatus _lastStatus;
        private int _refreshIntervalMs = 1000;
        private string _profileOverrideId = "auto";
        private Label _cpuBoxTitle;
        private Label _gpuBoxTitle;
        private Label _pwrBoxTitle;
        private Label _fan1BoxTitle;
        private Label _fan2BoxTitle;
        private Label _cpuTempLabel;
        private Label _gpuTempLabel;
        private Label _pwrLabel;
        private Label _fan1RpmLabel;
        private Label _fan2RpmLabel;
        private Label _modeLabel;
        private Label _pct1Label;
        private Label _pct2Label;

        private Button[] _modeButtons = new Button[3];
        private ComboBox _customModeCombo;
        private Button _activateCustomBtn;
        private Button _addCustomBtn;
        private Button _deleteCustomBtn;
        private Button _tabApuCurveBtn;
        private Button _tabSysCurveBtn;
        private Button _tabLiveGraphBtn;
        private Button _tabOsdConfigBtn;
        private Panel _osdConfigPanel;
        private bool _showingOsdConfig = false;
        private Label _customLabel;
        private FanCurveCanvas _curveCanvas;
        private LiveTelemetryCanvas _liveCanvas;
        private bool _showingLiveTelemetry = false;
        private int _highlightedMetric = 0;
        private Label _curveHintLabel;
        private Label _calibBadgeLabel;

        // OSD Studio UI Controls
        private Button _osdBtnScale100;
        private Button _osdBtnScale125;
        private Button _osdBtnScale150;
        private Button _osdBtnScale185;
        private Button _osdBtnBothGraphs;
        private Button _osdBtnCombinedGraph;
        private Button _osdBtnSparklines;
        private Button _osdBtnTextOnly;
        private Button _osdBtnHeightCompact;
        private Button _osdBtnHeightMedium;
        private Button _osdBtnHeightLarge;
        private Button _osdBtnHeightXl;
        private Button _osdBtnLayoutH;
        private Button _osdBtnLayoutV;
        private TrackBar _osdOpacitySlider;
        private Label _osdOpacityPctLabel;
        private CheckBox _osdMatchThemeCb;
        private CheckBox _osdShowCpuCb;
        private CheckBox _osdShowGpuCb;
        private CheckBox _osdShowPowerCb;
        private CheckBox _osdShowGpuLoadCb;
        private CheckBox _osdShowApuFanCb;
        private CheckBox _osdShowSysFanCb;
        private CheckBox _osdShowModeCb;
        private CheckBox _osdShowPeaksCb;
        private Button _osdQuickPosTl;
        private Button _osdQuickPosTr;
        private Button _osdQuickPosBl;
        private Button _osdQuickPosBr;
        private Button _osdToggleOverlayBtn;
        private Button _osdToggleLockBtn;
        private Button _optCardOsdConfigBtn;

        private Button _fan1AutoBtn;
        private Button _fan2AutoBtn;
        private Button _allAutoBtn;
        private Button _closeBtn;

        private TrackBar _fan1Slider;
        private TrackBar _fan2Slider;
        private Label _fan1SliderPct;
        private Label _fan2SliderPct;
        private bool _dragging1;
        private bool _dragging2;
        private int _apuManualPct = 0;
        private int _sysManualPct = 0;
        private bool _suppressComboEvent;

        private CheckBox _showCpuTrayCb;
        private CheckBox _showGpuTrayCb;
        private CheckBox _showOverlayCb;
        private CheckBox _lockOverlayCb;
        private CheckBox _autostartCb;
        private CheckBox _trayCb;
        private Label _statusBarLabel;

        private System.Windows.Forms.Timer _refreshTimer;
        private NotifyIcon _cpuTrayIcon;
        private NotifyIcon _gpuTrayIcon;
        private ToolTip _uiToolTip;
        private PictureBox _headerLogoBox;
        private string _lastCpuTooltipText = "";
        private string _lastPowerTooltipText = "";
        private string _lastIgpuTooltipText = "";
        private string _lastApuFanTooltipText = "";
        private string _lastSysFanTooltipText = "";
        private int _lastSelectedModeCode = 0;

        public MainForm(bool startInDemo, bool startMinimized = false)
        {
            _demoMode = startInDemo;
            _startMinimized = startMinimized;
            _sysInfo = SystemHardwareInfo.Detect();
            _hw.Profile = _sysInfo.MatchedProfile;
            _apuProfile = _builtInProfiles[0];
            _sysProfile = _builtInProfiles[0];
            LoadSettings();
            if (_customProfiles.Count == 0)
            {
                _customProfiles.Add(new FanCurveProfile("My Quiet Curve", false, -1,
                    new CurvePoint[]
                    {
                        new CurvePoint(42, 0),
                        new CurvePoint(55, 32),
                        new CurvePoint(68, 52),
                        new CurvePoint(80, 75),
                        new CurvePoint(90, 100)
                    },
                    new CurvePoint[]
                    {
                        new CurvePoint(42, 30),
                        new CurvePoint(58, 35),
                        new CurvePoint(70, 42),
                        new CurvePoint(82, 52),
                        new CurvePoint(90, 65)
                    }));
            }

            DetectSystemTheme(out _isDarkMode, out _isTaskbarDark);

            Text = "MiniFanControl";
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 860);
            try
            {
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) Icon = assoc;
            }
            catch { }

            InitOverlayForm();
            InitTrayIcons();
            BuildUi();
            ApplyFanLayoutMode();
            PopulateCustomCombo();
            ApplyThemeToControls();
            UpdateTrayIcons(0, 0);

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _sessionEndingHandler = (s, e) => RestoreHardwareSafeAutoIfNeeded();
            _powerModeChangedHandler = (s, e) =>
            {
                if (e.Mode == PowerModes.Suspend)
                {
                    RestoreHardwareSafeAutoIfNeeded();
                }
            };
            SystemEvents.SessionEnding += _sessionEndingHandler;
            SystemEvents.PowerModeChanged += _powerModeChangedHandler;
            AppDomain.CurrentDomain.ProcessExit += OnProcessOrCrashExit;
            AppDomain.CurrentDomain.UnhandledException += OnProcessOrCrashExit;

            _refreshTimer = new System.Windows.Forms.Timer { Interval = _refreshIntervalMs };
            _refreshTimer.Tick += (s, e) =>
            {
                // UI-only periodic refresh (theme sync and status bar timer)
                if (_themePref == ThemePreference.System)
                {
                    CheckAndApplyThemeChanges();
                }
                if (_emergencyThermalActive)
                {
                    _statusBarLabel.ForeColor = Color.FromArgb(0xFB, 0x71, 0x85);
                    _statusBarLabel.Text = string.Format("🔥 EMERGENCY THERMAL OVERRIDE (CPU {0}°C) — Fans forced to 100%", _lastStatus.CpuTemp);
                }
            };
            _refreshTimer.Start();

            // Decouple hardware polling and fan curve regulation to a dedicated background worker
            _hwThreadRunning = true;
            _hwThread = new Thread(HardwareWorkerLoop)
            {
                IsBackground = true,
                Name = "MiniFanControl.HardwareWorker",
                Priority = ThreadPriority.AboveNormal
            };
            _hwThread.Start();
            StartWatchdogSupervisor();

            Shown += (s, e) =>
            {
                UpdateGlobalHotkeysRegistration();
                if (_startMinimized)
                {
                    MinimizeToTray();
                }
            };
            FormClosing += OnFormClosing;
            _hwWakeEvent.Set();
        }

        private void OnProcessOrCrashExit(object sender, EventArgs e)
        {
            RestoreHardwareSafeAutoIfNeeded();
        }

        private void RestoreHardwareSafeAutoIfNeeded()
        {
            if (_safeExitRestored) return;
            _safeExitRestored = true;
            StopWatchdogSupervisorGracefully();
            if (!_restoreAutoOnExit || _demoMode) return;

            try
            {
                // If either fan was running on a software-evaluated curve or manual slider override,
                // restore its EC control register to 0x00 (Native BIOS EC Auto) on exit so the fan
                // never stays stuck at a fixed low PWM after MiniFanControl closes.
                bool apuWasSoftwareOrManual = _apuManualOverride || (_apuProfile != null && !_apuProfile.IsBuiltIn) || _lastAppliedApuPct > 0;
                bool sysWasSoftwareOrManual = _sysManualOverride || (_sysProfile != null && !_sysProfile.IsBuiltIn) || _lastAppliedSysPct > 0;

                if (apuWasSoftwareOrManual)
                    _hw.SetFan(1, 0);
                if (_hasDualFans && sysWasSoftwareOrManual)
                    _hw.SetFan(2, 0);
                _hw.SetAuto();
            }
            catch { }
        }

        private void StartWatchdogSupervisor()
        {
            if (_watchdogStarted) return;
            _watchdogStarted = true;

            try
            {
                int myPid = Process.GetCurrentProcess().Id;
                bool createdHb, createdSd;
                _watchdogHeartbeatEvent = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetHeartbeatEventName(myPid), EventResetMode.AutoReset, out createdHb);
                _watchdogShutdownEvent = WatchdogSupervisor.CreateOrOpenEvent(WatchdogSupervisor.GetShutdownEventName(myPid), EventResetMode.ManualReset, out createdSd);

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string helperPath = Path.Combine(exeDir, "minifan-helper.exe");
                if (!File.Exists(helperPath))
                {
                    helperPath = Path.Combine(exeDir, "MiniFanControl.exe");
                }

                if (File.Exists(helperPath))
                {
                    string args = string.Format("watchdog --pid {0} --timeout 3500{1}", myPid, _demoMode ? " --demo" : "");
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = helperPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _watchdogProcess = Process.Start(psi);
                }
            }
            catch { }
        }

        private void PulseWatchdogHeartbeat()
        {
            if (_watchdogHeartbeatEvent != null)
            {
                try { _watchdogHeartbeatEvent.Set(); } catch { }
            }
        }

        private void StopWatchdogSupervisorGracefully()
        {
            try
            {
                if (_watchdogShutdownEvent != null)
                {
                    _watchdogShutdownEvent.Set();
                }
                if (_watchdogProcess != null && !_watchdogProcess.HasExited)
                {
                    _watchdogProcess.WaitForExit(1000);
                }
            }
            catch { }
            finally
            {
                if (_watchdogHeartbeatEvent != null) { try { _watchdogHeartbeatEvent.Close(); } catch { } _watchdogHeartbeatEvent = null; }
                if (_watchdogShutdownEvent != null) { try { _watchdogShutdownEvent.Close(); } catch { } _watchdogShutdownEvent = null; }
                if (_watchdogProcess != null) { try { _watchdogProcess.Dispose(); } catch { } _watchdogProcess = null; }
            }
        }

        private void UpdateGlobalHotkeysRegistration()
        {
            if (!IsHandleCreated) return;
            UnregisterGlobalHotkeys();
            if (!_enableGlobalHotkeys) return;

            try
            {
                uint mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
                bool ok1 = RegisterHotKey(Handle, HOTKEY_TOGGLE_OSD, mods, VK_F9);
                bool ok2 = RegisterHotKey(Handle, HOTKEY_CYCLE_MODE, mods, VK_F10);
                bool ok3 = RegisterHotKey(Handle, HOTKEY_LOCK_OSD,   mods, VK_F11);
                bool ok4 = RegisterHotKey(Handle, HOTKEY_RESET_AUTO, mods, VK_F12);
                _hotkeysRegistered = (ok1 || ok2 || ok3 || ok4);
            }
            catch
            {
                _hotkeysRegistered = false;
            }
        }

        private void UnregisterGlobalHotkeys()
        {
            if (!IsHandleCreated) return;
            try
            {
                UnregisterHotKey(Handle, HOTKEY_TOGGLE_OSD);
                UnregisterHotKey(Handle, HOTKEY_CYCLE_MODE);
                UnregisterHotKey(Handle, HOTKEY_LOCK_OSD);
                UnregisterHotKey(Handle, HOTKEY_RESET_AUTO);
            }
            catch { }
            _hotkeysRegistered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE_OSD)
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                    return;
                }
                else if (id == HOTKEY_CYCLE_MODE)
                {
                    int nextCode = (_lastSelectedModeCode + 1) % 3;
                    ApplyMode(nextCode);
                    return;
                }
                else if (id == HOTKEY_LOCK_OSD)
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                    return;
                }
                else if (id == HOTKEY_RESET_AUTO)
                {
                    ApplyAutoAll();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void ResetSessionPeaks()
        {
            _sessionStats.Reset();
            _lastCpuTooltipText = "";
            _lastIgpuTooltipText = "";
            _lastPowerTooltipText = "";
            _lastApuFanTooltipText = "";
            _lastSysFanTooltipText = "";
            if (_cpuBoxTitle != null) _cpuBoxTitle.Text = "CPU TEMP";
            if (_gpuBoxTitle != null) _gpuBoxTitle.Text = "iGPU TEMP";
            if (_pwrBoxTitle != null) _pwrBoxTitle.Text = "POWER ⓘ";
            if (_fan1BoxTitle != null) _fan1BoxTitle.Text = "APU FAN";
            if (_fan2BoxTitle != null) _fan2BoxTitle.Text = "SYS FAN";
            if (_statusBarLabel != null)
                _statusBarLabel.Text = "✓ Reset session Min/Avg/Peak telemetry stats";
            RefreshStatus();
        }

        private void InitOverlayForm()
        {
            _overlayForm = new CapFrameXOverlayForm
            {
                Location = new Point(Math.Max(0, _overlayX), Math.Max(0, _overlayY)),
                VerticalLayout = _overlayVertical,
                HasDualFans = _hasDualFans,
                Stats = _sessionStats,
                OsdScaleTier = _osdScaleTier,
                GraphMode = _osdGraphMode,
                GraphHeight = _osdGraphHeight,
                OpacityPct = _osdOpacityPct,
                MatchAppTheme = _osdMatchTheme,
                IsRadeonTheme = (_themePref == ThemePreference.Radeon),
                ShowCpu = _osdShowCpu,
                ShowGpu = _osdShowGpu,
                ShowPower = _osdShowPower,
                ShowGpuLoad = _osdShowGpuLoad,
                ShowApuFan = _osdShowApuFan,
                ShowSysFan = _osdShowSysFan,
                ShowMode = _osdShowMode,
                ShowPeaks = _osdShowPeaks
            };
            _overlayForm.ApplyOpacity(_osdOpacityPct);
            _overlayForm.UpdateDimensions();
            _overlayForm.PositionOrLayoutChanged += (s, e) =>
            {
                _overlayX = _overlayForm.Location.X;
                _overlayY = _overlayForm.Location.Y;
                _overlayVertical = _overlayForm.VerticalLayout;
                _osdScaleTier = _overlayForm.OsdScaleTier;
                _osdGraphMode = _overlayForm.GraphMode;
                _osdGraphHeight = _overlayForm.GraphHeight;
                _osdOpacityPct = _overlayForm.OpacityPct;
                SyncOsdStudioUiFromOverlay();
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
            };
            _overlayForm.RequestHideOverlay += (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = false;
                else SetOverlayVisible(false);
            };
            _overlayForm.RequestToggleLock += (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                else SetOverlayLocked(!_lockOverlay);
            };
            _overlayForm.RequestOpenOsdConfig += (s, e) =>
            {
                OpenOsdConfigTab();
            };
            if (_showOverlay)
            {
                _overlayForm.Show();
                _overlayForm.ApplyClickThrough(_lockOverlay);
            }
        }

        private void SyncOverlayFromStudio()
        {
            if (_overlayForm == null) return;
            _overlayForm.Stats = _sessionStats;
            _overlayForm.HasDualFans = _hasDualFans;
            _overlayForm.OsdScaleTier = _osdScaleTier;
            _overlayForm.GraphMode = _osdGraphMode;
            _overlayForm.GraphHeight = _osdGraphHeight;
            _overlayForm.VerticalLayout = _overlayVertical;
            _overlayForm.MatchAppTheme = _osdMatchTheme;
            _overlayForm.IsRadeonTheme = (_themePref == ThemePreference.Radeon);
            _overlayForm.ShowCpu = _osdShowCpu;
            _overlayForm.ShowGpu = _osdShowGpu;
            _overlayForm.ShowPower = _osdShowPower;
            _overlayForm.ShowGpuLoad = _osdShowGpuLoad;
            _overlayForm.ShowApuFan = _osdShowApuFan;
            _overlayForm.ShowSysFan = _osdShowSysFan;
            _overlayForm.ShowMode = _osdShowMode;
            _overlayForm.ShowPeaks = _osdShowPeaks;
            _overlayForm.ApplyOpacity(_osdOpacityPct);
            _overlayForm.UpdateDimensions();
            _overlayForm.Invalidate();
            SaveSettings();
        }

        private void SyncOsdStudioUiFromOverlay()
        {
            if (_overlayForm != null)
            {
                _osdScaleTier = _overlayForm.OsdScaleTier;
                _osdGraphMode = _overlayForm.GraphMode;
                _osdGraphHeight = _overlayForm.GraphHeight;
                _osdOpacityPct = _overlayForm.OpacityPct;
                _overlayVertical = _overlayForm.VerticalLayout;
                _osdShowCpu = _overlayForm.ShowCpu;
                _osdShowGpu = _overlayForm.ShowGpu;
                _osdShowPower = _overlayForm.ShowPower;
                _osdShowGpuLoad = _overlayForm.ShowGpuLoad;
                _osdShowApuFan = _overlayForm.ShowApuFan;
                _osdShowSysFan = _overlayForm.ShowSysFan;
                _osdShowMode = _overlayForm.ShowMode;
                _osdShowPeaks = _overlayForm.ShowPeaks;
            }

            if (_osdOpacitySlider != null && _osdOpacitySlider.Value != _osdOpacityPct)
                _osdOpacitySlider.Value = Math.Max(_osdOpacitySlider.Minimum, Math.Min(_osdOpacitySlider.Maximum, _osdOpacityPct));
            if (_osdOpacityPctLabel != null)
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osdOpacityPct);

            if (_osdShowCpuCb != null) _osdShowCpuCb.Checked = _osdShowCpu;
            if (_osdShowGpuCb != null) _osdShowGpuCb.Checked = _osdShowGpu;
            if (_osdShowPowerCb != null) _osdShowPowerCb.Checked = _osdShowPower;
            if (_osdShowGpuLoadCb != null) _osdShowGpuLoadCb.Checked = _osdShowGpuLoad;
            if (_osdShowApuFanCb != null) _osdShowApuFanCb.Checked = _osdShowApuFan;
            if (_osdShowSysFanCb != null) _osdShowSysFanCb.Checked = _osdShowSysFan;
            if (_osdShowModeCb != null) _osdShowModeCb.Checked = _osdShowMode;
            if (_osdShowPeaksCb != null) _osdShowPeaksCb.Checked = _osdShowPeaks;
            if (_osdMatchThemeCb != null) _osdMatchThemeCb.Checked = _osdMatchTheme;

            if (_osdToggleOverlayBtn != null)
                _osdToggleOverlayBtn.Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden";
            if (_osdToggleLockBtn != null)
                _osdToggleLockBtn.Text = _lockOverlay ? "🔒 Locked" : "🔓 Drag Mode";

            UpdateOsdStudioStyles();
        }

        private void SetOverlayCornerPosition(int corner)
        {
            if (_overlayForm == null) return;
            Screen screen = Screen.FromControl(_overlayForm.Visible ? (Form)_overlayForm : this);
            Rectangle wa = screen.WorkingArea;
            _overlayForm.UpdateDimensions();
            int w = _overlayForm.Width;
            int h = _overlayForm.Height;
            int margin = 24;

            switch (corner)
            {
                case 0: // Top-Left
                    _overlayX = wa.Left + margin;
                    _overlayY = wa.Top + margin;
                    break;
                case 1: // Top-Right
                    _overlayX = wa.Right - w - margin;
                    _overlayY = wa.Top + margin;
                    break;
                case 2: // Bottom-Left
                    _overlayX = wa.Left + margin;
                    _overlayY = wa.Bottom - h - margin;
                    break;
                case 3: // Bottom-Right
                    _overlayX = wa.Right - w - margin;
                    _overlayY = wa.Bottom - h - margin;
                    break;
            }

            _overlayForm.Location = new Point(_overlayX, _overlayY);
            SaveSettings();
            _overlayForm.Invalidate();
        }

        private void OpenOsdConfigTab()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Show();
            Activate();
            BringToFront();

            if (!_showOverlay)
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = true;
                else SetOverlayVisible(true);
            }

            SetOsdConfigViewMode(true);
        }

        private void SetOsdConfigViewMode(bool show)
        {
            _showingOsdConfig = show;
            if (show)
            {
                _showingLiveTelemetry = false;
                _highlightedMetric = 0;
            }

            if (_osdConfigPanel != null) _osdConfigPanel.Visible = show;

            bool showCurveOrLive = !show;
            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] != null) _modeButtons[i].Visible = showCurveOrLive;
            }
            if (_customLabel != null) _customLabel.Visible = showCurveOrLive;
            if (_customModeCombo != null) _customModeCombo.Visible = showCurveOrLive;
            if (_activateCustomBtn != null) _activateCustomBtn.Visible = showCurveOrLive;
            if (_addCustomBtn != null) _addCustomBtn.Visible = showCurveOrLive;
            if (_deleteCustomBtn != null) _deleteCustomBtn.Visible = showCurveOrLive;

            if (show)
            {
                if (_curveCanvas != null) _curveCanvas.Visible = false;
                if (_liveCanvas != null) _liveCanvas.Visible = false;
                if (_curveHintLabel != null)
                    _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                SyncOsdStudioUiFromOverlay();
            }
            else
            {
                if (_showingLiveTelemetry)
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = false;
                    if (_liveCanvas != null) _liveCanvas.Visible = true;
                }
                else
                {
                    if (_curveCanvas != null) _curveCanvas.Visible = true;
                    if (_liveCanvas != null) _liveCanvas.Visible = false;
                }
                UpdateCurveHint();
            }

            UpdateCurveTabStyles();
            UpdateOsdStudioStyles();
            if (_cpuTempBox != null) _cpuTempBox.Invalidate();
            if (_gpuTempBox != null) _gpuTempBox.Invalidate();
            if (_pwrBox != null) _pwrBox.Invalidate();
            if (_fan1Box != null) _fan1Box.Invalidate();
            if (_fan2Box != null) _fan2Box.Invalidate();
        }

        private void SetOverlayVisible(bool visible)
        {
            _showOverlay = visible;
            if (_overlayForm != null)
            {
                if (visible)
                {
                    if (!_overlayForm.Visible) _overlayForm.Show();
                    _overlayForm.ApplyClickThrough(_lockOverlay);
                }
                else
                {
                    _overlayForm.Hide();
                }
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            RefreshStatus();
        }

        private void SetOverlayLocked(bool locked)
        {
            _lockOverlay = locked;
            if (_overlayForm != null)
            {
                _overlayForm.ApplyClickThrough(locked);
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
        }

        private void ToggleOverlayLayoutStyle()
        {
            _overlayVertical = !_overlayVertical;
            if (_overlayForm != null)
            {
                _overlayForm.VerticalLayout = _overlayVertical;
                _overlayForm.UpdateDimensions();
                _overlayForm.Invalidate();
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
        }

        private static void OpenUrlSafe(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open browser link:\n" + url + "\n\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportHardwareProbeReport()
        {
            try
            {
                string report = _sysInfo.BuildProbeReport(_hw, _demo, _demoMode);
                string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ec-probe-report.txt");
                File.WriteAllText(outPath, report, Encoding.UTF8);
                _statusBarLabel.Text = "✓ Saved ec-probe-report.txt";
                try
                {
                    Process.Start("notepad.exe", outPath);
                }
                catch
                {
                    MessageBox.Show(this, "Saved Hardware & EC Probe Report to:\n" + outPath,
                        "EC Probe Report Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to generate EC Probe Report:\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowHardwareCompatibilityDialog()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Detected System:");
            sb.AppendLine("  • Product / Board : " + _sysInfo.ProductName + " (" + _sysInfo.BaseBoard + ")");
            sb.AppendLine("  • Manufacturer    : " + _sysInfo.Manufacturer);
            sb.AppendLine("  • BIOS Version    : " + _sysInfo.BiosVersion);
            sb.AppendLine("  • APU / Processor : " + _sysInfo.CpuName);
            sb.AppendLine("  • Active EC Map   : " + _sysInfo.MatchedProfile.DisplayName);
            sb.AppendLine("  • EC Memory Base  : 0x" + _sysInfo.MatchedProfile.EcBaseAddress.ToString("X8") + " (ITE IT5570E)");
            sb.AppendLine();
            sb.AppendLine("Compatible / Target Ryzen Mini-PC Families:");
            foreach (DeviceProfile dp in DeviceProfile.KnownProfiles)
            {
                sb.AppendLine("  • " + dp.DisplayName);
                sb.AppendLine("    " + dp.ChipsetNotes);
            }
            sb.AppendLine();
            sb.AppendLine("Testing a new Mini PC (e.g., Ryzen AI 9 HX 370)?");
            sb.AppendLine("  Use 'Tools & Drivers ▾ → Export EC Probe Report' or run:");
            sb.AppendLine("  .\\minifan-helper.exe dump   (prints 256-byte EC hex map)");
            sb.AppendLine("  .\\minifan-helper.exe watch  (monitors live EC changes)");

            MessageBox.Show(this, sb.ToString(), "MiniFanControl — Hardware & Multi-Model Compatibility",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAboutDialog()
        {
            bool isCalibrated, isStale;
            string calibSummary = FanCurveProfile.FormatCalibrationStatusBadge(_hasDualFans, out isCalibrated, out isStale);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl v" + APP_VERSION + " (64-bit Native EC Edition)");
            sb.AppendLine("Standalone Embedded Controller Fan Curve & Thermal Utility for AMD Ryzen Mini PCs");
            sb.AppendLine("────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("Hardware & Bus Safety:");
            sb.AppendLine("  • Detected System : " + _sysInfo.ProductName + " (" + _sysInfo.CpuName + ")");
            sb.AppendLine("  • Active EC Map   : " + _sysInfo.MatchedProfile.DisplayName +
                " (ITE IT5570E @ 0x" + _sysInfo.MatchedProfile.EcBaseAddress.ToString("X8") + ")");
            sb.AppendLine("  • EC Bus Mutex    : Global\\Access_EC (Safe alongside CapFrameX, HWiNFO64, AIDA64)");
            sb.AppendLine("  • Power Telemetry : AMD ADL64 PMPM SVI2 / Thermal TDP Estimation");
            sb.AppendLine();
            sb.AppendLine("Fan Calibration & Anti-Stall Protection:");
            sb.AppendLine("  • Status          : " + calibSummary);
            sb.AppendLine(string.Format("  • Active Floor    : APU Min {0}% ({1} RPM Max){2}",
                FanCurveProfile.ApuMinSpinPct,
                FanCurveProfile.ApuMaxRpm,
                _hasDualFans
                    ? string.Format("  |  Sys Min {0}% ({1} RPM Max)", FanCurveProfile.SysMinSpinPct, FanCurveProfile.SysMaxRpm)
                    : "  |  1-Fan Mode"));
            sb.AppendLine("  • Exit Safety     : " + (_restoreAutoOnExit ? "✓ Restores BIOS EC Auto on Exit/Crash" : "Leaves last manual PWM active"));
            sb.AppendLine("  • Maintenance Tip : Re-calibrate every ~6 months or after dusting heatsink fins.");
            sb.AppendLine();
            sb.AppendLine("Global In-Game Hotkeys (" + (_enableGlobalHotkeys ? "✓ Enabled" : "Disabled") + "):");
            sb.AppendLine("  • Ctrl + Alt + F9  : Show / Hide CapFrameX-Style OSD Overlay");
            sb.AppendLine("  • Ctrl + Alt + F10 : Cycle Mode (Balanced → Performance → Silent)");
            sb.AppendLine("  • Ctrl + Alt + F11 : Lock / Unlock OSD Overlay (Click-Through)");
            sb.AppendLine("  • Ctrl + Alt + F12 : Reset Both Fans to BIOS Auto (Balanced)");
            sb.AppendLine();
            sb.AppendLine("Configuration & CLI Companion:");
            sb.AppendLine("  • Settings File   : " + GetSettingsPath());
            sb.AppendLine("  • CLI Commands    : .\\minifan-helper.exe status | calibrate | scan | dump | test");

            MessageBox.Show(this, sb.ToString(), "About MiniFanControl v" + APP_VERSION,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RunGuiFanCalibration()
        {
            _hwWorkerPaused = true;
            bool timerWasEnabled = _refreshTimer != null && _refreshTimer.Enabled;
            if (_refreshTimer != null) _refreshTimer.Stop();

            try
            {
                using (FanCalibrationDialog dlg = new FanCalibrationDialog(
                    _isDarkMode,
                    _hw,
                    _demo,
                    _demoMode,
                    prog =>
                    {
                        _statusBarLabel.Text = "↻ " + (prog.PhaseTitle ?? "Calibrating fans...");
                        FanStatus st = prog.LiveStatus;
                        if (st.CpuTemp > 0)
                        {
                            _cpuTempLabel.Text = string.Format("{0}°C", st.CpuTemp);
                            _gpuTempLabel.Text = st.GpuTemp > 0 ? string.Format("{0}°C", st.GpuTemp) : "N/A";
                            if (_pwrLabel != null)
                                _pwrLabel.Text = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                            _fan1RpmLabel.Text = string.Format("{0} RPM ↻", st.Fan1Rpm);
                            _fan2RpmLabel.Text = _hasDualFans ? string.Format("{0} RPM ↻", st.Fan2Rpm) : "N/A (1-Fan Mode)";
                            UpdateTrayIcons(st.CpuTemp, st.GpuTemp);
                            if (_overlayForm != null && _showOverlay)
                                _overlayForm.UpdateMetrics(st, "Calib", _hasDualFans);
                        }
                    }))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null && !dlg.Result.WasCanceled)
                    {
                        FanCalibrationResult res = dlg.Result;
                        _hasDualFans = res.HasDualFans;
                        FanCurveProfile.ApuMinSpinPct = res.ApuMinSpinPct;
                        FanCurveProfile.SysMinSpinPct = res.SysMinSpinPct;
                        FanCurveProfile.ApuMaxRpm = res.ApuMaxRpm;
                        FanCurveProfile.SysMaxRpm = res.SysMaxRpm;
                        FanCurveProfile.LastCalibratedDate = !string.IsNullOrEmpty(res.CalibratedAt)
                            ? res.CalibratedAt
                            : DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

                        _lastAppliedApuPct = -1;
                        _lastAppliedSysPct = -1;
                        _lastAppliedEcMode = -1;
                        ApplyFanLayoutMode();
                        UpdateCalibrationBadge();
                        SaveSettings();
                        _driversMenu = CreateDriversAndToolsMenu();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Fan Calibration failed:\n" + ex.Message,
                    "MiniFanControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _hwWorkerPaused = false;
                if (_refreshTimer != null && timerWasEnabled)
                    _refreshTimer.Start();
                _hwWakeEvent.Set();
            }
        }

        private void ToggleDualFanMode()
        {
            _hasDualFans = !_hasDualFans;
            _lastAppliedApuPct = -1;
            _lastAppliedSysPct = -1;
            _lastAppliedEcMode = -1;
            ApplyFanLayoutMode();
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            RefreshStatus();
        }

        private void ApplyFanLayoutMode()
        {
            if (_curveCanvas != null)
            {
                if (!_hasDualFans && _curveCanvas.EditSysFan)
                {
                    _curveCanvas.EditSysFan = false;
                }
                _curveCanvas.ShowSecondaryCurve = _hasDualFans;
                _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.HasDualFans = _hasDualFans;
                _liveCanvas.Invalidate();
            }

            if (_tabApuCurveBtn != null && _tabSysCurveBtn != null && _tabLiveGraphBtn != null && _tabOsdConfigBtn != null)
            {
                if (_hasDualFans)
                {
                    _tabApuCurveBtn.Location = new Point(8, 8);
                    _tabApuCurveBtn.Size = new Size(154, 28);
                    _tabSysCurveBtn.Location = new Point(168, 8);
                    _tabSysCurveBtn.Size = new Size(154, 28);
                    _tabSysCurveBtn.Visible = true;
                    _tabLiveGraphBtn.Location = new Point(328, 8);
                    _tabLiveGraphBtn.Size = new Size(154, 28);
                    _tabOsdConfigBtn.Location = new Point(488, 8);
                    _tabOsdConfigBtn.Size = new Size(154, 28);
                }
                else
                {
                    _tabApuCurveBtn.Location = new Point(8, 8);
                    _tabApuCurveBtn.Size = new Size(206, 28);
                    _tabSysCurveBtn.Visible = false;
                    _tabLiveGraphBtn.Location = new Point(222, 8);
                    _tabLiveGraphBtn.Size = new Size(206, 28);
                    _tabOsdConfigBtn.Location = new Point(436, 8);
                    _tabOsdConfigBtn.Size = new Size(206, 28);
                }
            }

            if (_fan1Box != null && _fan2Box != null)
            {
                if (_hasDualFans)
                {
                    _fan1Box.Location = new Point(395, 8);
                    _fan1Box.Size = new Size(120, 70);
                    if (_fan1BoxTitle != null) _fan1BoxTitle.Size = new Size(116, 15);
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Size = new Size(116, 28);
                        _fan1RpmLabel.Font = new Font("Segoe UI", 12.75f, FontStyle.Bold);
                    }
                    _fan2Box.Location = new Point(524, 8);
                    _fan2Box.Size = new Size(120, 70);
                    _fan2Box.Visible = true;
                }
                else
                {
                    _fan2Box.Visible = false;
                    _fan1Box.Location = new Point(395, 8);
                    _fan1Box.Size = new Size(249, 70);
                    if (_fan1BoxTitle != null) _fan1BoxTitle.Size = new Size(245, 15);
                    if (_fan1RpmLabel != null)
                    {
                        _fan1RpmLabel.Size = new Size(245, 28);
                        _fan1RpmLabel.Font = new Font("Segoe UI", 14.5f, FontStyle.Bold);
                    }
                }
            }

            if (_fan2Slider != null) _fan2Slider.Enabled = _hasDualFans;
            if (_fan2AutoBtn != null) _fan2AutoBtn.Enabled = _hasDualFans;

            UpdateCurveTabStyles();
            UpdateCurveHint();
            UpdateCalibrationBadge();
        }

        private ContextMenu CreateDriversAndToolsMenu()
        {
            ContextMenu menu = new ContextMenu();
            string calMenuLabel = string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate)
                ? "Calibrate Fans (Not Yet Calibrated — Default 30% Min)..."
                : string.Format("Calibrate Fans (✓ Last: {0} • Min {1}%)...",
                    FanCurveProfile.LastCalibratedDate, FanCurveProfile.ApuMinSpinPct);
            menu.MenuItems.Add(calMenuLabel, (s, e) => RunGuiFanCalibration());
            string layoutLabel = _hasDualFans
                ? "Fan Layout: 2 Fans (APU + Sys) — Click for 1-Fan Mode"
                : "Fan Layout: 1 Fan (APU Only) — Click for 2-Fan Mode";
            menu.MenuItems.Add(layoutLabel, (s, e) => ToggleDualFanMode());
            menu.MenuItems.Add("-");
            string osdToggleLabel = _showOverlay ? "Hide CapFrameX-Style OSD Overlay" : "Show CapFrameX-Style OSD Overlay";
            menu.MenuItems.Add(osdToggleLabel, (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                else SetOverlayVisible(!_showOverlay);
            });
            string osdStyleLabel = _overlayVertical
                ? "OSD Layout: Vertical Stack — Click for Horizontal Bar"
                : "OSD Layout: Horizontal Bar — Click for Vertical Stack";
            menu.MenuItems.Add(osdStyleLabel, (s, e) => ToggleOverlayLayoutStyle());
            string osdLockLabel = _lockOverlay
                ? "Unlock OSD Overlay (Allow Mouse Drag)"
                : "Lock OSD Overlay (Click-Through Mode)";
            menu.MenuItems.Add(osdLockLabel, (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                else SetOverlayLocked(!_lockOverlay);
            });
            menu.MenuItems.Add("Configure OSD Overlay Studio...", (s, e) => OpenOsdConfigTab());
            menu.MenuItems.Add("-");
            string resetPeaksLabel = _sessionStats.SampleCount > 0
                ? string.Format("Reset Session Peaks (Max CPU {0}°C • iGPU {1}°C • {2}W)",
                    _sessionStats.MaxCpuTemp, _sessionStats.MaxGpuTemp, _sessionStats.MaxPowerWatts)
                : "Reset Session Peak (Max °C / W) Stats";
            menu.MenuItems.Add(resetPeaksLabel, (s, e) => ResetSessionPeaks());
            string safeExitLabel = string.Format("Safety: Restore BIOS Auto Fan on Exit ({0})",
                _restoreAutoOnExit ? "✓ Enabled" : "Disabled");
            menu.MenuItems.Add(safeExitLabel, (s, e) =>
            {
                _restoreAutoOnExit = !_restoreAutoOnExit;
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _restoreAutoOnExit
                    ? "✓ Exit Safety enabled: fans restore to BIOS Auto on close"
                    : "ℹ Exit Safety disabled: custom/manual PWM persists after close";
            });
            string hotkeysLabel = string.Format("Global Hotkeys Ctrl+Alt+F9..F12 ({0})",
                _enableGlobalHotkeys ? "✓ Enabled" : "Disabled");
            menu.MenuItems.Add(hotkeysLabel, (s, e) =>
            {
                _enableGlobalHotkeys = !_enableGlobalHotkeys;
                UpdateGlobalHotkeysRegistration();
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _enableGlobalHotkeys
                    ? "✓ Global hotkeys enabled (Ctrl+Alt+F9..F12)"
                    : "ℹ Global hotkeys disabled";
            });
            string demoLabel = string.Format("Offline Demo / Simulation Mode ({0})", _demoMode ? "✓ Active" : "Disabled");
            menu.MenuItems.Add(demoLabel, (s, e) =>
            {
                _demoMode = !_demoMode;
                SaveSettings();
                _driversMenu = CreateDriversAndToolsMenu();
                _statusBarLabel.Text = _demoMode
                    ? "ℹ Offline Demo Mode active (synthetic telemetry)"
                    : "✓ Hardware Mode active (direct EC registers)";
                RefreshStatus();
            });
            menu.MenuItems.Add("-");
            MenuItem profileSubMenu = new MenuItem("Hardware Profile & EC Target");
            bool isAuto = string.IsNullOrEmpty(_profileOverrideId) || _profileOverrideId.Equals("auto", StringComparison.OrdinalIgnoreCase);
            string autoTitle = string.Format("Auto-Detect: {0} (Active)", _sysInfo.MatchedProfile.DisplayName);
            MenuItem autoItem = new MenuItem(autoTitle, (s, e) => SetHardwareProfile("auto"));
            if (isAuto) autoItem.Checked = true;
            profileSubMenu.MenuItems.Add(autoItem);
            profileSubMenu.MenuItems.Add("-");
            foreach (DeviceProfile dp in DeviceProfile.KnownProfiles)
            {
                string pid = dp.Id;
                MenuItem dpItem = new MenuItem(dp.DisplayName, (s, e) => SetHardwareProfile(pid));
                if (!isAuto && _sysInfo.MatchedProfile.Id == pid) dpItem.Checked = true;
                profileSubMenu.MenuItems.Add(dpItem);
            }
            menu.MenuItems.Add(profileSubMenu);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Export Hardware & EC Probe Report (for HX 370 / New PCs)...", (s, e) => ExportHardwareProbeReport());
            menu.MenuItems.Add("Detected Hardware & Multi-Model Info...", (s, e) => ShowHardwareCompatibilityDialog());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("GMKtec Official Drivers & Manuals (K12 / Mini PCs)...", (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            menu.MenuItems.Add("GMKtec BIOS & Firmware Downloads...", (s, e) => OpenUrlSafe(URL_GMKTEC_FIRMWARE));
            menu.MenuItems.Add("AMD Software: Adrenalin & Chipset Drivers...", (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("About MiniFanControl v" + APP_VERSION + "...", (s, e) => ShowAboutDialog());
            return menu;
        }

        private static bool IsMouseOverControl(Control c)
        {
            if (c == null || !c.IsHandleCreated || !c.Visible) return false;
            Point pt = c.PointToClient(Cursor.Position);
            return c.ClientRectangle.Contains(pt);
        }

        private static bool IsBoxHovered(Control box, Control title, Control label)
        {
            return IsMouseOverControl(box) || IsMouseOverControl(title) || IsMouseOverControl(label);
        }

        private void SetHardwareProfile(string profileId)
        {
            _profileOverrideId = string.IsNullOrEmpty(profileId) ? "auto" : profileId;
            if (_profileOverrideId.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                _sysInfo.MatchedProfile = SystemHardwareInfo.MatchProfile(_sysInfo.ProductName, _sysInfo.BaseBoard, _sysInfo.CpuName);
            }
            else
            {
                DeviceProfile explicitProf = DeviceProfile.FindProfile(_profileOverrideId);
                if (explicitProf != null)
                {
                    _sysInfo.MatchedProfile = explicitProf;
                }
            }
            if (_hw != null) _hw.Profile = _sysInfo.MatchedProfile;
            if (_hwSubLabel != null)
            {
                _hwSubLabel.Text = string.Format("EC Map: {0} • Global\\Access_EC Mutex • 64-bit", _sysInfo.MatchedProfile.DisplayName);
            }
            SaveSettings();
            _driversMenu = CreateDriversAndToolsMenu();
            _statusBarLabel.Text = string.Format("✓ Profile: {0}", _sysInfo.MatchedProfile.DisplayName);
            RefreshStatus();
        }

        private void SetRefreshInterval(int ms)
        {
            if (ms < 250) ms = 250;
            if (ms > 10000) ms = 10000;
            _refreshIntervalMs = ms;
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = _refreshIntervalMs;
            }
            if (_samplingCombo != null)
            {
                int[] intervals = new int[] { 500, 1000, 1500, 2000, 3000 };
                for (int i = 0; i < intervals.Length; i++)
                {
                    if (intervals[i] == _refreshIntervalMs)
                    {
                        if (_samplingCombo.SelectedIndex != i) _samplingCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            _driversMenu = CreateDriversAndToolsMenu();
            SaveSettings();
            if (_statusBarLabel != null)
            {
                double hz = 1000.0 / ms;
                _statusBarLabel.Text = string.Format("? Data sampling rate set to {0} ms ({1:0.0} Hz / {2:0.0}s)", ms, hz, ms / 1000.0);
            }
        }

        private string GetSettingsPath()
        {
            string primary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "minifancontrol.cfg");
            string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "k12-settings.cfg");
            if (!File.Exists(primary) && File.Exists(legacy))
                return legacy;
            return primary;
        }

        public static CurvePoint[] ParsePointList(string str)
        {
            List<CurvePoint> pts = new List<CurvePoint>();
            foreach (string tok in str.Split(';'))
            {
                string[] kv = tok.Split(':');
                int t, p;
                if (kv.Length == 2 && int.TryParse(kv[0], out t) && int.TryParse(kv[1], out p))
                {
                    pts.Add(new CurvePoint(
                        Math.Max(FanCurveCanvas.MIN_TEMP, Math.Min(FanCurveCanvas.MAX_TEMP, t)),
                        Math.Max(0, Math.Min(100, p))));
                }
            }
            return pts.ToArray();
        }

        public static string FormatPointList(CurvePoint[] pts)
        {
            List<string> list = new List<string>();
            if (pts != null)
            {
                foreach (CurvePoint pt in pts)
                    list.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", pt.Temp, pt.Pct));
            }
            return string.Join(";", list.ToArray());
        }

        private FanCurveProfile FindProfileByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (FanCurveProfile bp in _builtInProfiles)
            {
                if (bp.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return bp;
            }
            foreach (FanCurveProfile cp in _customProfiles)
            {
                if (cp.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cp;
            }
            return null;
        }

        public static bool TryParseHexOrDecByte(string s, out byte val)
        {
            val = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val);
            return byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return;
                string legacyCustomName = null;
                string apuModeName = null;
                string sysModeName = null;
                DeviceProfile prof = _sysInfo != null ? _sysInfo.MatchedProfile : DeviceProfile.KnownProfiles[0];

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("CustomProfile=", StringComparison.OrdinalIgnoreCase))
                    {
                        string payload = line.Substring("CustomProfile=".Length);
                        string[] segments = payload.Split('|');
                        if (segments.Length >= 2)
                        {
                            string name = segments[0].Trim();
                            CurvePoint[] apuPts = ParsePointList(segments[1]);
                            CurvePoint[] sysPts = segments.Length >= 3 ? ParsePointList(segments[2]) : FanCurveProfile.ClonePoints(apuPts);
                            if (!string.IsNullOrEmpty(name) && apuPts.Length >= 2)
                            {
                                if (sysPts.Length < 2) sysPts = FanCurveProfile.ClonePoints(apuPts);
                                _customProfiles.Add(new FanCurveProfile(name, false, -1, apuPts, sysPts));
                            }
                        }
                        continue;
                    }

                    string[] parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    string k = parts[0].Trim();
                    string v = parts[1].Trim();
                    byte regByte;
                    int intVal;
                    if (k == "ShowCpuTray") _showCpuTray = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "ShowGpuTray") _showGpuTray = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "ShowOverlay") _showOverlay = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "LockOverlay") _lockOverlay = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OverlayVertical") _overlayVertical = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OverlayX" && int.TryParse(v, out intVal)) _overlayX = Math.Max(0, Math.Min(3800, intVal));
                    else if (k == "OverlayY" && int.TryParse(v, out intVal)) _overlayY = Math.Max(0, Math.Min(2100, intVal));
                    else if (k == "OsdScaleTier" && int.TryParse(v, out intVal)) _osdScaleTier = Math.Max(0, Math.Min(3, intVal));
                    else if (k == "OsdGraphMode" && int.TryParse(v, out intVal)) _osdGraphMode = Math.Max(0, Math.Min(3, intVal));
                    else if (k == "OsdGraphHeight" && int.TryParse(v, out intVal))
                    {
                        if (intVal <= 50) _osdGraphHeight = 45;
                        else if (intVal <= 75) _osdGraphHeight = 65;
                        else if (intVal <= 110) _osdGraphHeight = 95;
                        else _osdGraphHeight = 130;
                    }
                    else if (k == "OsdOpacityPct" && int.TryParse(v, out intVal)) _osdOpacityPct = Math.Max(35, Math.Min(100, intVal));
                    else if (k == "OsdMatchTheme") _osdMatchTheme = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowCpu") _osdShowCpu = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowGpu") _osdShowGpu = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowPower") _osdShowPower = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowGpuLoad") _osdShowGpuLoad = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowApuFan") _osdShowApuFan = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowSysFan") _osdShowSysFan = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowMode") _osdShowMode = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "OsdShowPeaks") _osdShowPeaks = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "RestoreAutoOnExit") _restoreAutoOnExit = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "EnableHotkeys") _enableGlobalHotkeys = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "HasDualFans") _hasDualFans = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                    else if (k == "HardwareProfile" || k == "ProfileId")
                    {
                        _profileOverrideId = v;
                        if (!v.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            DeviceProfile explicitProf = DeviceProfile.FindProfile(v);
                            if (explicitProf != null)
                            {
                                prof = explicitProf;
                                if (_sysInfo != null) _sysInfo.MatchedProfile = explicitProf;
                                if (_hw != null) _hw.Profile = explicitProf;
                            }
                        }
                    }
                    else if (k == "RefreshIntervalMs" && int.TryParse(v, out intVal)) _refreshIntervalMs = Math.Max(250, Math.Min(10000, intVal));
                    else if (k == "ApuMinSpinPct" && int.TryParse(v, out intVal)) FanCurveProfile.ApuMinSpinPct = Math.Max(18, Math.Min(45, intVal));
                    else if (k == "SysMinSpinPct" && int.TryParse(v, out intVal)) FanCurveProfile.SysMinSpinPct = Math.Max(18, Math.Min(45, intVal));
                    else if (k == "ApuMaxRpm" && int.TryParse(v, out intVal)) FanCurveProfile.ApuMaxRpm = Math.Max(500, Math.Min(6000, intVal));
                    else if (k == "SysMaxRpm" && int.TryParse(v, out intVal)) FanCurveProfile.SysMaxRpm = Math.Max(0, Math.Min(6000, intVal));
                    else if (k == "LastCalibratedDate") FanCurveProfile.LastCalibratedDate = v;
                    else if (k == "Theme")
                    {
                        int t;
                        if (int.TryParse(v, out t) && t >= 0 && t <= 3)
                            _themePref = (ThemePreference)t;
                    }
                    else if (k == "ActiveCustom") legacyCustomName = v;
                    else if (k == "ApuMode") apuModeName = v;
                    else if (k == "SysMode") sysModeName = v;
                    else if (k == "RegModeStatus" && TryParseHexOrDecByte(v, out regByte)) prof.RegModeStatus = regByte;
                    else if (k == "RegModeCmd" && TryParseHexOrDecByte(v, out regByte)) prof.RegModeCmd = regByte;
                    else if (k == "RegFan1Ctrl" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Ctrl = regByte;
                    else if (k == "RegFan2Ctrl" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Ctrl = regByte;
                    else if (k == "RegFan1Hi" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Hi = regByte;
                    else if (k == "RegFan1Lo" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan1Lo = regByte;
                    else if (k == "RegFan2Hi" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Hi = regByte;
                    else if (k == "RegFan2Lo" && TryParseHexOrDecByte(v, out regByte)) prof.RegFan2Lo = regByte;
                    else if (k == "RegCpuTemp" && TryParseHexOrDecByte(v, out regByte)) prof.RegCpuTemp = regByte;
                    else if (k == "RegGpuTemp" && TryParseHexOrDecByte(v, out regByte)) prof.RegGpuTemp = regByte;
                    else if (k == "EcAccess" || k == "AccessMethod")
                    {
                        if (v.Equals("PortIo", StringComparison.OrdinalIgnoreCase) || v.Equals("Port", StringComparison.OrdinalIgnoreCase) || v.Equals("0x62", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo62;
                        else if (v.Equals("PortIo6C", StringComparison.OrdinalIgnoreCase) || v.Equals("0x6C", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo6C;
                        else if (v.Equals("Mmio", StringComparison.OrdinalIgnoreCase))
                            _hw.AccessMethod = HardwareBackend.EcAccessMethod.Mmio;
                    }
                }

                FanCurveProfile apuProf = FindProfileByName(apuModeName) ?? FindProfileByName(legacyCustomName);
                FanCurveProfile sysProf = FindProfileByName(sysModeName) ?? FindProfileByName(legacyCustomName);
                if (apuProf != null) _apuProfile = apuProf;
                if (sysProf != null) _sysProfile = sysProf;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("ShowCpuTray=" + (_showCpuTray ? "1" : "0"));
                lines.Add("ShowGpuTray=" + (_showGpuTray ? "1" : "0"));
                lines.Add("ShowOverlay=" + (_showOverlay ? "1" : "0"));
                lines.Add("LockOverlay=" + (_lockOverlay ? "1" : "0"));
                lines.Add("OverlayVertical=" + (_overlayVertical ? "1" : "0"));
                lines.Add("OverlayX=" + _overlayX);
                lines.Add("OverlayY=" + _overlayY);
                lines.Add("OsdScaleTier=" + _osdScaleTier);
                lines.Add("OsdGraphMode=" + _osdGraphMode);
                lines.Add("OsdGraphHeight=" + _osdGraphHeight);
                lines.Add("OsdOpacityPct=" + _osdOpacityPct);
                lines.Add("OsdMatchTheme=" + (_osdMatchTheme ? "1" : "0"));
                lines.Add("OsdShowCpu=" + (_osdShowCpu ? "1" : "0"));
                lines.Add("OsdShowGpu=" + (_osdShowGpu ? "1" : "0"));
                lines.Add("OsdShowPower=" + (_osdShowPower ? "1" : "0"));
                lines.Add("OsdShowGpuLoad=" + (_osdShowGpuLoad ? "1" : "0"));
                lines.Add("OsdShowApuFan=" + (_osdShowApuFan ? "1" : "0"));
                lines.Add("OsdShowSysFan=" + (_osdShowSysFan ? "1" : "0"));
                lines.Add("OsdShowMode=" + (_osdShowMode ? "1" : "0"));
                lines.Add("OsdShowPeaks=" + (_osdShowPeaks ? "1" : "0"));
                lines.Add("RestoreAutoOnExit=" + (_restoreAutoOnExit ? "1" : "0"));
                lines.Add("EnableHotkeys=" + (_enableGlobalHotkeys ? "1" : "0"));
                lines.Add("HasDualFans=" + (_hasDualFans ? "1" : "0"));
                lines.Add("HardwareProfile=" + (_profileOverrideId ?? "auto"));
                lines.Add("RefreshIntervalMs=" + _refreshIntervalMs);
                lines.Add("ApuMinSpinPct=" + FanCurveProfile.ApuMinSpinPct);
                lines.Add("SysMinSpinPct=" + FanCurveProfile.SysMinSpinPct);
                lines.Add("ApuMaxRpm=" + FanCurveProfile.ApuMaxRpm);
                lines.Add("SysMaxRpm=" + FanCurveProfile.SysMaxRpm);
                if (!string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate))
                    lines.Add("LastCalibratedDate=" + FanCurveProfile.LastCalibratedDate);
                lines.Add("Theme=" + (int)_themePref);
                lines.Add("ApuMode=" + (_apuProfile != null ? _apuProfile.Name : "Balanced"));
                lines.Add("SysMode=" + (_sysProfile != null ? _sysProfile.Name : "Balanced"));

                foreach (FanCurveProfile cp in _customProfiles)
                {
                    lines.Add("CustomProfile=" + cp.Name.Replace("|", "/") + "|" + FormatPointList(cp.ApuPoints) + "|" + FormatPointList(cp.SysPoints));
                }

                string primary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "minifancontrol.cfg");
                File.WriteAllLines(primary, lines.ToArray());
                RebuildTrayContextMenus();
            }
            catch { }
        }

        private void DetectSystemTheme(out bool appDark, out bool taskbarDark)
        {
            if (_themePref == ThemePreference.Dark || _themePref == ThemePreference.Radeon)
            {
                appDark = true;
                taskbarDark = true;
                return;
            }
            if (_themePref == ThemePreference.Light)
            {
                appDark = false;
                taskbarDark = false;
                return;
            }

            bool sysAppsDark = true;
            bool sysTaskbarDark = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object appsVal = key.GetValue("AppsUseLightTheme");
                        if (appsVal != null)
                            sysAppsDark = Convert.ToInt32(appsVal) == 0;

                        object sysVal = key.GetValue("SystemUsesLightTheme");
                        if (sysVal != null)
                            sysTaskbarDark = Convert.ToInt32(sysVal) == 0;
                        else
                            sysTaskbarDark = sysAppsDark;
                    }
                }
            }
            catch { }

            appDark = sysAppsDark;
            taskbarDark = sysTaskbarDark;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            CheckAndApplyThemeChanges();
        }

        private void CheckAndApplyThemeChanges()
        {
            bool newAppDark, newTaskbarDark;
            DetectSystemTheme(out newAppDark, out newTaskbarDark);
            if (newAppDark != _isDarkMode || newTaskbarDark != _isTaskbarDark)
            {
                _isDarkMode = newAppDark;
                _isTaskbarDark = newTaskbarDark;
                _lastCpuIconKey = int.MinValue;
                _lastGpuIconKey = int.MinValue;
                ApplyThemeToControls();
                RefreshStatus();
            }
        }

        private void StartRampIndication(string actionDescription, int targetFan)
        {
            _pendingActionText = actionDescription;
            _rampTargetFan = targetFan;
            _rampUntil = DateTime.UtcNow.AddSeconds(4.5);
            if (_refreshTimer != null)
            {
                _refreshTimer.Interval = FAST_REFRESH_MS;
            }
            RefreshStatus();
        }

        private ContextMenu CreateTrayContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Show Window", (s, e) => RestoreFromTray());
            menu.MenuItems.Add(
                _showOverlay ? "Hide CapFrameX OSD Overlay" : "Show CapFrameX OSD Overlay",
                (s, e) =>
                {
                    if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlayCb.Checked;
                    else SetOverlayVisible(!_showOverlay);
                });
            menu.MenuItems.Add(
                _lockOverlay ? "Unlock OSD Overlay (Allow Drag)" : "Lock OSD Overlay (Click-Through)",
                (s, e) =>
                {
                    if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlayCb.Checked;
                    else SetOverlayLocked(!_lockOverlay);
                });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Mode: Balanced", (s, e) => ApplyMode(0));
            menu.MenuItems.Add("Mode: Performance", (s, e) => ApplyMode(1));
            menu.MenuItems.Add("Mode: Silent", (s, e) => ApplyMode(2));
            if (_customProfiles.Count > 0)
            {
                menu.MenuItems.Add("-");
                foreach (FanCurveProfile cp in _customProfiles)
                {
                    FanCurveProfile captured = cp;
                    menu.MenuItems.Add("Custom: " + captured.Name, (s, e) => ActivateCustomProfile(captured));
                }
            }
            menu.MenuItems.Add("-");
            MenuItem linksItem = new MenuItem("Tools && Drivers");
            linksItem.MenuItems.Add("Calibrate Fans...", (s, e) => RunGuiFanCalibration());
            linksItem.MenuItems.Add("Export Hardware && EC Probe Report...", (s, e) => ExportHardwareProbeReport());
            linksItem.MenuItems.Add("Detected Hardware && Multi-Model Info...", (s, e) => ShowHardwareCompatibilityDialog());
            linksItem.MenuItems.Add("-");
            linksItem.MenuItems.Add("GMKtec Official Drivers && Manuals...", (s, e) => OpenUrlSafe(URL_GMKTEC_DRIVERS));
            linksItem.MenuItems.Add("GMKtec BIOS && Firmware Updates...", (s, e) => OpenUrlSafe(URL_GMKTEC_FIRMWARE));
            linksItem.MenuItems.Add("AMD Software: Adrenalin && Chipset Drivers...", (s, e) => OpenUrlSafe(URL_AMD_DRIVERS));
            linksItem.MenuItems.Add("-");
            linksItem.MenuItems.Add("About MiniFanControl v" + APP_VERSION + "...", (s, e) => ShowAboutDialog());
            menu.MenuItems.Add(linksItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Reset to Auto", (s, e) => ApplyAutoAll());
            menu.MenuItems.Add("Exit", (s, e) =>
            {
                if (_cpuTrayIcon != null) _cpuTrayIcon.Visible = false;
                if (_gpuTrayIcon != null) _gpuTrayIcon.Visible = false;
                Close();
            });
            return menu;
        }

        private void RebuildTrayContextMenus()
        {
            if (_cpuTrayIcon != null) _cpuTrayIcon.ContextMenu = CreateTrayContextMenu();
            if (_gpuTrayIcon != null) _gpuTrayIcon.ContextMenu = CreateTrayContextMenu();
        }

        private void InitTrayIcons()
        {
            _cpuTrayIcon = new NotifyIcon
            {
                Text = "CPU Temperature - MiniFanControl",
                ContextMenu = CreateTrayContextMenu(),
                Visible = _showCpuTray
            };
            _cpuTrayIcon.DoubleClick += (s, e) => RestoreFromTray();

            _gpuTrayIcon = new NotifyIcon
            {
                Text = "iGPU Temperature - MiniFanControl",
                ContextMenu = CreateTrayContextMenu(),
                Visible = _showGpuTray
            };
            _gpuTrayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private Icon RenderHighContrastTempIcon(int temp, bool darkBackground, bool isGpu, out IntPtr rawHandle)
        {
            Color bg = darkBackground ? Color.Black : Color.White;
            Color fg = darkBackground ? Color.White : Color.Black;
            Color barColor = isGpu
                ? (darkBackground ? Color.FromArgb(0x22, 0xC5, 0x5E) : Color.FromArgb(0x15, 0x80, 0x3D))
                : (darkBackground ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x1D, 0x4E, 0xD8));

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                using (Brush barBrush = new SolidBrush(barColor))
                {
                    g.FillRectangle(barBrush, 0, 29, 32, 3);
                }

                string label = temp > 0 ? temp.ToString() : "--";
                float fontSize = label.Length >= 3 ? 15f : 21.5f;

                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(fg))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString(label, font, textBrush, new RectangleF(-1f, -1f, 34f, 30f), sf);
                }

                rawHandle = bmp.GetHicon();
                return (Icon)Icon.FromHandle(rawHandle).Clone();
            }
        }

        private void UpdateTrayIcons(int cpuTemp, int gpuTemp)
        {
            int cpuKey = (cpuTemp & 0xFFFF) | (_isTaskbarDark ? 0x10000 : 0);
            if (cpuKey != _lastCpuIconKey || _cpuIconHandle == IntPtr.Zero)
            {
                _lastCpuIconKey = cpuKey;
                IntPtr newCpuHandle;
                Icon cpuIcon = RenderHighContrastTempIcon(cpuTemp, _isTaskbarDark, false, out newCpuHandle);
                if (_cpuTrayIcon != null)
                {
                    _cpuTrayIcon.Icon = cpuIcon;
                }
                if (_cpuIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_cpuIconHandle);
                }
                _cpuIconHandle = newCpuHandle;
            }

            int gpuKey = (gpuTemp & 0xFFFF) | (_isTaskbarDark ? 0x10000 : 0);
            if (gpuKey != _lastGpuIconKey || _gpuIconHandle == IntPtr.Zero)
            {
                _lastGpuIconKey = gpuKey;
                IntPtr newGpuHandle;
                Icon gpuIcon = RenderHighContrastTempIcon(gpuTemp, _isTaskbarDark, true, out newGpuHandle);
                if (_gpuTrayIcon != null)
                {
                    _gpuTrayIcon.Icon = gpuIcon;
                }
                if (_gpuIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_gpuIconHandle);
                }
                _gpuIconHandle = newGpuHandle;
            }

            if (_cpuTrayIcon != null && _cpuTrayIcon.Visible != _showCpuTray)
                _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null && _gpuTrayIcon.Visible != _showGpuTray)
                _gpuTrayIcon.Visible = _showGpuTray;
        }

        private Panel CreateSectionCard(string title, int top, int height)
        {
            Label header = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(14, top)
            };
            Controls.Add(header);
            _sectionHeaders.Add(header);

            Panel card = new Panel
            {
                Location = new Point(14, top + 20),
                Size = new Size(ClientSize.Width - 28, height)
            };
            Controls.Add(card);
            _sectionCards.Add(card);
            return card;
        }

        private Label AddStaticLabel(Control parent, string text, int x, int y, int w, int h)
        {
            Label lbl = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h)
            };
            parent.Controls.Add(lbl);
            _staticLabels.Add(lbl);
            return lbl;
        }

        private static readonly Font ToolTipBodyFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        private static readonly Font ToolTipChartFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);

        private int GetTooltipChartKind(Control c)
        {
            if (c == null) return 0;
            if (c == _cpuTempBox || c == _cpuBoxTitle || c == _cpuTempLabel) return 1;
            if (c == _gpuTempBox || c == _gpuBoxTitle || c == _gpuTempLabel) return 2;
            if (c == _pwrBox || c == _pwrBoxTitle || c == _pwrLabel) return 3;
            if (c == _fan1Box || c == _fan1BoxTitle || c == _fan1RpmLabel) return 4;
            if (c == _fan2Box || c == _fan2BoxTitle || c == _fan2RpmLabel) return 5;
            return 0;
        }

        private void OnToolTipPopup(object sender, PopupEventArgs e)
        {
            string text = _uiToolTip != null ? _uiToolTip.GetToolTip(e.AssociatedControl) : "";
            if (string.IsNullOrEmpty(text)) return;

            Size textSize = TextRenderer.MeasureText(text, ToolTipBodyFont, new Size(440, 800),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            int chartKind = GetTooltipChartKind(e.AssociatedControl);
            int w = Math.Max(textSize.Width + 20, chartKind > 0 ? 330 : 120);
            int h = textSize.Height + 16 + (chartKind > 0 ? 60 : 0);
            e.ToolTipSize = new Size(w, h);
        }

        private void OnToolTipDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color bg = isRadeon ? Color.FromArgb(0x16, 0x0A, 0x0E) : (_isDarkMode ? Color.FromArgb(0x18, 0x18, 0x25) : Color.FromArgb(0xFF, 0xFF, 0xFF));
            Color border = isRadeon ? RadeonAccent : (_isDarkMode ? Color.FromArgb(0x45, 0x47, 0x5A) : Color.FromArgb(0x94, 0xA3, 0xB8));
            Color fg = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);

            Rectangle bounds = e.Bounds;
            using (SolidBrush bgBr = new SolidBrush(bg))
            {
                g.FillRectangle(bgBr, bounds);
            }
            using (Pen borderPen = new Pen(border, 1.5f))
            {
                g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

            int chartKind = GetTooltipChartKind(e.AssociatedControl);
            int textHeight = bounds.Height - (chartKind > 0 ? 64 : 10);
            Rectangle textRect = new Rectangle(bounds.X + 10, bounds.Y + 8, bounds.Width - 20, Math.Max(20, textHeight));
            TextRenderer.DrawText(g, e.ToolTipText, ToolTipBodyFont, textRect, fg,
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            if (chartKind > 0)
            {
                Rectangle chartRect = new Rectangle(bounds.X + 10, bounds.Bottom - 54, bounds.Width - 20, 46);
                List<int> samples = chartKind == 1 ? _sessionStats.CpuHistory
                    : (chartKind == 2 ? _sessionStats.GpuHistory
                    : (chartKind == 3 ? _sessionStats.PowerHistory
                    : (chartKind == 4 ? _sessionStats.Fan1RpmHistory : _sessionStats.Fan2RpmHistory)));
                string unit = chartKind == 3 ? "W" : (chartKind >= 4 ? " RPM" : "°C");
                Color lineCol;
                switch (chartKind)
                {
                    case 1:
                        lineCol = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
                        break;
                    case 2:
                        lineCol = isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E);
                        break;
                    case 3:
                        lineCol = isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B);
                        break;
                    case 4:
                        lineCol = Color.FromArgb(0xC0, 0x84, 0xFC);
                        break;
                    case 5:
                        lineCol = Color.FromArgb(0x2D, 0xD4, 0xBF);
                        break;
                    default:
                        lineCol = isRadeon ? RadeonAccent : DarkAccent;
                        break;
                }
                DrawMiniSparklineChart(g, chartRect, samples, lineCol, unit, true);
            }
        }

        private void DrawMiniSparklineChart(Graphics g, Rectangle rect, List<int> samples, Color lineColor, string unit, bool showLabels)
        {
            if (rect.Width < 12 || rect.Height < 6) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color chartBg = isRadeon ? Color.FromArgb(0x0D, 0x06, 0x08) : (_isDarkMode ? Color.FromArgb(0x11, 0x11, 0x1B) : Color.FromArgb(0xF1, 0xF5, 0xF9));
            Color gridCol = isRadeon ? Color.FromArgb(55, 0xED, 0x1C, 0x24) : (_isDarkMode ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(45, 0, 0, 0));

            if (showLabels)
            {
                using (SolidBrush bgBr = new SolidBrush(chartBg))
                {
                    g.FillRectangle(bgBr, rect);
                }
                using (Pen gridPen = new Pen(gridCol, 1f))
                {
                    int midY = rect.Y + rect.Height / 2;
                    g.DrawLine(gridPen, rect.X + 1, midY, rect.Right - 1, midY);
                    g.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }

            if (samples == null || samples.Count == 0) return;

            int minVal = int.MaxValue;
            int maxVal = int.MinValue;
            for (int i = 0; i < samples.Count; i++)
            {
                int v = samples[i];
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }

            // Auto-scale with a small pad so even a 1-2 degree/watt change renders a clear wave
            float scaleMin = minVal - 1.5f;
            float scaleMax = maxVal + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minVal + maxVal) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            int padX = showLabels ? 4 : 1;
            int padTop = showLabels ? 14 : 2;
            int padBot = showLabels ? 4 : 1;
            float plotX = rect.X + padX;
            float plotW = Math.Max(4, rect.Width - padX * 2);
            float plotY = rect.Y + padTop;
            float plotH = Math.Max(4, rect.Height - padTop - padBot);

            int count = samples.Count;
            PointF[] pts = new PointF[Math.Max(2, count)];
            if (count == 1)
            {
                float y = plotY + plotH * 0.5f;
                pts[0] = new PointF(plotX, y);
                pts[1] = new PointF(plotX + plotW, y);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    float x = plotX + (plotW * i) / (count - 1);
                    float norm = (samples[i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plotY + plotH * (1f - norm);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom - 1);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom - 1);

            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(showLabels ? 115 : 85, lineColor),
                Color.FromArgb(8, lineColor),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen linePen = new Pen(lineColor, showLabels ? 1.8f : 1.4f))
            {
                linePen.LineJoin = LineJoin.Round;
                g.DrawLines(linePen, pts);
            }

            PointF lastPt = pts[pts.Length - 1];
            using (SolidBrush dotBr = new SolidBrush(lineColor))
            {
                float r = showLabels ? 3f : 2f;
                g.FillEllipse(dotBr, lastPt.X - r, lastPt.Y - r, r * 2f, r * 2f);
            }

            if (showLabels)
            {
                Color labelFg = _isDarkMode ? Color.FromArgb(215, 255, 255, 255) : Color.FromArgb(215, 15, 23, 42);
                int curVal = samples[samples.Count - 1];
                string header = string.Format("LIVE GRAPH ({0}–{1}{2}) • NOW {3}{2}", minVal, maxVal, unit, curVal);
                TextRenderer.DrawText(g, header, ToolTipChartFont, new Point(rect.X + 4, rect.Y + 1), labelFg);
            }
        }

        private void PaintMetricBoxSparkline(Graphics g, Panel box, List<int> samples, int chartKind)
        {
            if (box == null) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color lineCol;
            switch (chartKind)
            {
                case 1:
                    lineCol = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
                    break;
                case 2:
                    lineCol = isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E);
                    break;
                case 3:
                    lineCol = isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B);
                    break;
                case 4:
                    lineCol = Color.FromArgb(0xC0, 0x84, 0xFC);
                    break;
                case 5:
                    lineCol = Color.FromArgb(0x2D, 0xD4, 0xBF);
                    break;
                default:
                    lineCol = isRadeon ? RadeonAccent : DarkAccent;
                    break;
            }
            if (samples != null && samples.Count > 0)
            {
                Rectangle sparkRect = new Rectangle(1, 47, box.ClientSize.Width - 2, Math.Max(8, box.ClientSize.Height - 48));
                DrawMiniSparklineChart(g, sparkRect, samples, lineCol, "", false);
            }
            if (_showingLiveTelemetry && _highlightedMetric == chartKind)
            {
                using (Pen activePen = new Pen(lineCol, 2f))
                {
                    g.DrawRectangle(activePen, 1, 1, box.ClientSize.Width - 2, box.ClientSize.Height - 2);
                }
            }
        }

        private void SetGraphViewMode(bool showLive, int highlightMetric)
        {
            _showingOsdConfig = false;
            if (_osdConfigPanel != null) _osdConfigPanel.Visible = false;
            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] != null) _modeButtons[i].Visible = true;
            }
            if (_customLabel != null) _customLabel.Visible = true;
            if (_customModeCombo != null) _customModeCombo.Visible = true;
            if (_activateCustomBtn != null) _activateCustomBtn.Visible = true;
            if (_addCustomBtn != null) _addCustomBtn.Visible = true;
            if (_deleteCustomBtn != null) _deleteCustomBtn.Visible = true;

            _showingLiveTelemetry = showLive;
            if (showLive)
            {
                _highlightedMetric = highlightMetric > 0 ? highlightMetric : 1;
            }
            else
            {
                _highlightedMetric = 0;
            }

            if (_curveCanvas != null) _curveCanvas.Visible = !showLive;
            if (_liveCanvas != null)
            {
                _liveCanvas.HighlightMetric = _highlightedMetric;
                _liveCanvas.Visible = showLive;
                if (showLive) _liveCanvas.Invalidate();
            }

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            UpdateCurveHint();

            if (_cpuTempBox != null) _cpuTempBox.Invalidate();
            if (_gpuTempBox != null) _gpuTempBox.Invalidate();
            if (_pwrBox != null) _pwrBox.Invalidate();
            if (_fan1Box != null) _fan1Box.Invalidate();
            if (_fan2Box != null) _fan2Box.Invalidate();
        }

        private void OnMetricBoxMouseUp(int metricKind, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ResetSessionPeaks();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if (_showingLiveTelemetry && _highlightedMetric == metricKind)
                {
                    SetGraphViewMode(false, 0);
                }
                else
                {
                    SetGraphViewMode(true, metricKind);
                }
            }
        }

        private void BuildUi()
        {
            _uiToolTip = new ToolTip
            {
                AutoPopDelay = 25000,
                InitialDelay = 200,
                ReshowDelay = 100,
                ShowAlways = true,
                OwnerDraw = true
            };
            _uiToolTip.Popup += OnToolTipPopup;
            _uiToolTip.Draw += OnToolTipDraw;

            // 0. Dedicated Top Header Row (Logo, Model Info, Drivers Menu, Theme Toggle)
            _headerLogoBox = new PictureBox
            {
                Location = new Point(14, 8),
                Size = new Size(46, 46),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AppLogoRenderer.RenderConcept(1, 192),
                Cursor = Cursors.Hand
            };
            _headerLogoBox.Click += (s, e) => ShowAboutDialog();
            Controls.Add(_headerLogoBox);
            _uiToolTip.SetToolTip(_headerLogoBox,
                string.Format("{0} ({1})\r\nMiniFanControl v{2} — Click for About & Hardware Details",
                    _sysInfo.ProductName, _sysInfo.CpuName, APP_VERSION));

            string topHeaderTitle = _sysInfo != null && !string.IsNullOrEmpty(_sysInfo.GetCompactSummary())
                ? _sysInfo.GetCompactSummary()
                : "System Telemetry";
            _hwBadgeLabel = new Label
            {
                Text = topHeaderTitle,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(68, 10),
                Size = new Size(320, 22),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwBadgeLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwBadgeLabel);
            _uiToolTip.SetToolTip(_hwBadgeLabel,
                string.Format("{0} ({1})\r\nAPU: {2}\r\nActive EC Map: {3}\r\nClick for Hardware & Multi-Model Details",
                    _sysInfo.ProductName, _sysInfo.BaseBoard, _sysInfo.CpuName, _sysInfo.MatchedProfile.DisplayName));

            _hwSubLabel = new Label
            {
                Text = string.Format("EC Map: {0} • Global\\Access_EC Mutex • 64-bit", _sysInfo.MatchedProfile.DisplayName),
                Font = new Font("Segoe UI", 7.75f, FontStyle.Regular),
                Location = new Point(68, 32),
                Size = new Size(320, 18),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _hwSubLabel.Click += (s, e) => ShowHardwareCompatibilityDialog();
            Controls.Add(_hwSubLabel);

            _driversMenu = CreateDriversAndToolsMenu();

            _driversMenuBtn = new Button
            {
                Text = "Tools && Drivers ▾",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(398, 14),
                Size = new Size(130, 28),
                Cursor = Cursors.Hand
            };
            _driversMenuBtn.FlatAppearance.BorderSize = 0;
            _driversMenuBtn.Click += (s, e) =>
            {
                _driversMenu.Show(_driversMenuBtn, new Point(0, _driversMenuBtn.Height));
            };
            Controls.Add(_driversMenuBtn);

            _themeToggleBtn = new Button
            {
                Text = GetThemeButtonLabel(),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(534, 14),
                Size = new Size(132, 28),
                Cursor = Cursors.Hand
            };
            _themeToggleBtn.FlatAppearance.BorderSize = 0;
            _themeToggleBtn.Click += (s, e) => CycleThemePreference();
            Controls.Add(_themeToggleBtn);

            // 1. Hardware & Live Telemetry Section (top = 62, card = 82..192, height = 110)
            Panel statusCard = CreateSectionCard("System Telemetry & Live Hardware Sensors", 62, 110);

            _cpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(8, 8),
                Size = new Size(120, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _cpuBoxTitle = new Label
            {
                Text = "CPU TEMP",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 3),
                Size = new Size(116, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _cpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 15.75f, FontStyle.Bold),
                Location = new Point(1, 17),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _cpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _cpuTempBox, _sessionStats.CpuHistory, 1);
            _cpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuTempLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(1, e);
            _cpuTempBox.Controls.Add(_cpuBoxTitle);
            _cpuTempBox.Controls.Add(_cpuTempLabel);
            statusCard.Controls.Add(_cpuTempBox);

            string cpuTip = "APU / CPU Package Temperature (EC Register 0x70 • Tctl/Tdie)\r\nLeft-click to highlight CPU Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_cpuTempBox, cpuTip);
            _uiToolTip.SetToolTip(_cpuBoxTitle, cpuTip);
            _uiToolTip.SetToolTip(_cpuTempLabel, cpuTip);

            _gpuTempBox = new DoubleBufferedPanel
            {
                Location = new Point(137, 8),
                Size = new Size(120, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _gpuBoxTitle = new Label
            {
                Text = "iGPU TEMP",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 3),
                Size = new Size(116, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _gpuTempLabel = new Label
            {
                Text = "--°C",
                Font = new Font("Segoe UI", 15.75f, FontStyle.Bold),
                Location = new Point(1, 17),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _gpuTempBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _gpuTempBox, _sessionStats.GpuHistory, 2);
            _gpuTempBox.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuTempLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(2, e);
            _gpuTempBox.Controls.Add(_gpuBoxTitle);
            _gpuTempBox.Controls.Add(_gpuTempLabel);
            statusCard.Controls.Add(_gpuTempBox);

            string gpuTip = "Integrated Radeon Graphics Temperature (AMD ADL2 PMLog GFX/SoC Sensor)\r\nLeft-click to highlight iGPU Trend on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_gpuTempBox, gpuTip);
            _uiToolTip.SetToolTip(_gpuBoxTitle, gpuTip);
            _uiToolTip.SetToolTip(_gpuTempLabel, gpuTip);

            _pwrBox = new DoubleBufferedPanel
            {
                Location = new Point(266, 8),
                Size = new Size(120, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _pwrBoxTitle = new Label
            {
                Text = "POWER ⓘ",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 3),
                Size = new Size(116, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _pwrLabel = new Label
            {
                Text = "--W",
                Font = new Font("Segoe UI", 15.75f, FontStyle.Bold),
                Location = new Point(1, 17),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _pwrBox.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _pwrBox, _sessionStats.PowerHistory, 3);
            _pwrBox.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrBoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(3, e);
            _pwrBox.Controls.Add(_pwrBoxTitle);
            _pwrBox.Controls.Add(_pwrLabel);
            statusCard.Controls.Add(_pwrBox);

            _fan1Box = new DoubleBufferedPanel
            {
                Location = new Point(395, 8),
                Size = new Size(120, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan1BoxTitle = new Label
            {
                Text = "APU FAN",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 3),
                Size = new Size(116, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan1RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 12.75f, FontStyle.Bold),
                Location = new Point(1, 17),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan1Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan1Box, _sessionStats.Fan1RpmHistory, 4);
            _fan1Box.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1BoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1RpmLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(4, e);
            _fan1Box.Controls.Add(_fan1BoxTitle);
            _fan1Box.Controls.Add(_fan1RpmLabel);
            statusCard.Controls.Add(_fan1Box);

            string fan1Tip = "APU / CPU Fan Tachometer (EC Register 0x30/0x31)\r\nLeft-click to highlight APU Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan1Box, fan1Tip);
            _uiToolTip.SetToolTip(_fan1BoxTitle, fan1Tip);
            _uiToolTip.SetToolTip(_fan1RpmLabel, fan1Tip);

            _fan2Box = new DoubleBufferedPanel
            {
                Location = new Point(524, 8),
                Size = new Size(120, 70),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _fan2BoxTitle = new Label
            {
                Text = "SYS FAN",
                Font = new Font("Segoe UI", 7.0f, FontStyle.Bold),
                Location = new Point(1, 3),
                Size = new Size(116, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan2RpmLabel = new Label
            {
                Text = "-- RPM",
                Font = new Font("Segoe UI", 12.75f, FontStyle.Bold),
                Location = new Point(1, 17),
                Size = new Size(116, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _fan2Box.Paint += (s, e) => PaintMetricBoxSparkline(e.Graphics, _fan2Box, _sessionStats.Fan2RpmHistory, 5);
            _fan2Box.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2BoxTitle.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2RpmLabel.MouseUp += (s, e) => OnMetricBoxMouseUp(5, e);
            _fan2Box.Controls.Add(_fan2BoxTitle);
            _fan2Box.Controls.Add(_fan2RpmLabel);
            statusCard.Controls.Add(_fan2Box);

            string fan2Tip = "System RAM / NVMe Fan Tachometer (EC Register 0x32/0x33)\r\nLeft-click to highlight Sys Fan on the big graph • Right-click to reset peaks.";
            _uiToolTip.SetToolTip(_fan2Box, fan2Tip);
            _uiToolTip.SetToolTip(_fan2BoxTitle, fan2Tip);
            _uiToolTip.SetToolTip(_fan2RpmLabel, fan2Tip);

            Action updateCpuTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatCpuTooltip(_sessionStats);
                    _lastCpuTooltipText = tip;
                    _uiToolTip.SetToolTip(_cpuTempBox, tip);
                    _uiToolTip.SetToolTip(_cpuBoxTitle, tip);
                    _uiToolTip.SetToolTip(_cpuTempLabel, tip);
                }
            };
            _cpuTempBox.MouseEnter += (s, e) => updateCpuTip();
            _cpuBoxTitle.MouseEnter += (s, e) => updateCpuTip();
            _cpuTempLabel.MouseEnter += (s, e) => updateCpuTip();

            Action updateGpuTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatIgpuTooltip(_sessionStats);
                    _lastIgpuTooltipText = tip;
                    _uiToolTip.SetToolTip(_gpuTempBox, tip);
                    _uiToolTip.SetToolTip(_gpuBoxTitle, tip);
                    _uiToolTip.SetToolTip(_gpuTempLabel, tip);
                }
            };
            _gpuTempBox.MouseEnter += (s, e) => updateGpuTip();
            _gpuBoxTitle.MouseEnter += (s, e) => updateGpuTip();
            _gpuTempLabel.MouseEnter += (s, e) => updateGpuTip();

            Action updatePwrTip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatPowerTooltip(_sessionStats);
                    _lastPowerTooltipText = tip;
                    _uiToolTip.SetToolTip(_pwrBox, tip);
                    _uiToolTip.SetToolTip(_pwrBoxTitle, tip);
                    _uiToolTip.SetToolTip(_pwrLabel, tip);
                }
            };
            _pwrBox.MouseEnter += (s, e) => updatePwrTip();
            _pwrBoxTitle.MouseEnter += (s, e) => updatePwrTip();
            _pwrLabel.MouseEnter += (s, e) => updatePwrTip();

            Action updateFan1Tip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm);
                    _lastApuFanTooltipText = tip;
                    _uiToolTip.SetToolTip(_fan1Box, tip);
                    _uiToolTip.SetToolTip(_fan1BoxTitle, tip);
                    _uiToolTip.SetToolTip(_fan1RpmLabel, tip);
                }
            };
            _fan1Box.MouseEnter += (s, e) => updateFan1Tip();
            _fan1BoxTitle.MouseEnter += (s, e) => updateFan1Tip();
            _fan1RpmLabel.MouseEnter += (s, e) => updateFan1Tip();

            Action updateFan2Tip = () =>
            {
                if (_lastStatus.CpuTemp > 0 && _uiToolTip != null)
                {
                    string tip = _lastStatus.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans);
                    _lastSysFanTooltipText = tip;
                    _uiToolTip.SetToolTip(_fan2Box, tip);
                    _uiToolTip.SetToolTip(_fan2BoxTitle, tip);
                    _uiToolTip.SetToolTip(_fan2RpmLabel, tip);
                }
            };
            _fan2Box.MouseEnter += (s, e) => updateFan2Tip();
            _fan2BoxTitle.MouseEnter += (s, e) => updateFan2Tip();
            _fan2RpmLabel.MouseEnter += (s, e) => updateFan2Tip();

            AddStaticLabel(statusCard, "Mode:", 10, 84, 44, 20);
            _modeLabel = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                Location = new Point(56, 83),
                Size = new Size(180, 22)
            };
            statusCard.Controls.Add(_modeLabel);

            AddStaticLabel(statusCard, "APU Fan:", 248, 84, 62, 20);
            _pct1Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Location = new Point(312, 84),
                Size = new Size(120, 20)
            };
            statusCard.Controls.Add(_pct1Label);

            AddStaticLabel(statusCard, "Sys Fan:", 446, 84, 58, 20);
            _pct2Label = new Label
            {
                Text = "--",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Location = new Point(506, 84),
                Size = new Size(120, 20)
            };
            statusCard.Controls.Add(_pct2Label);

            // 2. Modes & Fan Curve Section (top = 200, card = 220..592, height = 372)
            Panel modeCard = CreateSectionCard("Modes, Fan Curves & Live Telemetry Graph", 200, 372);

            // Row 1 (y=8): Tab Selector Row (APU Fan | Sys Fan | 📈 Live Trend | 🖥 OSD Studio)
            _tabApuCurveBtn = new Button
            {
                Text = "● APU: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(154, 28),
                Cursor = Cursors.Hand
            };
            _tabApuCurveBtn.FlatAppearance.BorderSize = 0;
            _tabApuCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = false;
                SetGraphViewMode(false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabApuCurveBtn);

            _tabSysCurveBtn = new Button
            {
                Text = "● Sys: Balanced",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(168, 8),
                Size = new Size(154, 28),
                Cursor = Cursors.Hand
            };
            _tabSysCurveBtn.FlatAppearance.BorderSize = 0;
            _tabSysCurveBtn.Click += (s, e) =>
            {
                _curveCanvas.EditSysFan = true;
                SetGraphViewMode(false, 0);
                PopulateCustomCombo();
                _curveCanvas.Invalidate();
            };
            modeCard.Controls.Add(_tabSysCurveBtn);

            _tabLiveGraphBtn = new Button
            {
                Text = "📈 Live Trend",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(328, 8),
                Size = new Size(154, 28),
                Cursor = Cursors.Hand
            };
            _tabLiveGraphBtn.FlatAppearance.BorderSize = 0;
            _tabLiveGraphBtn.Click += (s, e) =>
            {
                SetGraphViewMode(!_showingLiveTelemetry, _highlightedMetric > 0 ? _highlightedMetric : 1);
            };
            modeCard.Controls.Add(_tabLiveGraphBtn);

            _tabOsdConfigBtn = new Button
            {
                Text = "🖥 OSD Studio",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(488, 8),
                Size = new Size(154, 28),
                Cursor = Cursors.Hand
            };
            _tabOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _tabOsdConfigBtn.Click += (s, e) =>
            {
                SetOsdConfigViewMode(!_showingOsdConfig);
            };
            modeCard.Controls.Add(_tabOsdConfigBtn);

            // Row 2 (y=42): Built-in Mode Buttons for the currently selected fan tab
            string[] modeTitles = { "⚖ Balanced", "⚡ Performance", "🌙 Silent" };
            for (int i = 0; i < 3; i++)
            {
                int code = i;
                Button btn = new Button
                {
                    Text = modeTitles[i],
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                    Location = new Point(8 + i * 212, 42),
                    Size = new Size(206, 30),
                    Padding = new Padding(0),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => ApplyMode(code);
                modeCard.Controls.Add(btn);
                _modeButtons[i] = btn;
            }

            // Row 3 (y=74): Custom Modes Row for the currently selected fan tab
            _customLabel = AddStaticLabel(modeCard, "Custom:", 8, 77, 54, 22);

            _customModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(66, 75),
                Size = new Size(270, 24)
            };
            _customModeCombo.SelectedIndexChanged += OnCustomComboSelected;
            modeCard.Controls.Add(_customModeCombo);

            _activateCustomBtn = new Button
            {
                Text = "Use Curve",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(344, 74),
                Size = new Size(96, 26),
                Cursor = Cursors.Hand
            };
            _activateCustomBtn.FlatAppearance.BorderSize = 0;
            _activateCustomBtn.Click += (s, e) =>
            {
                int idx = _customModeCombo.SelectedIndex;
                if (idx >= 0 && idx < _customProfiles.Count)
                {
                    ActivateCustomProfile(_customProfiles[idx]);
                }
            };
            modeCard.Controls.Add(_activateCustomBtn);

            _addCustomBtn = new Button
            {
                Text = "+ New",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(446, 74),
                Size = new Size(96, 26),
                Cursor = Cursors.Hand
            };
            _addCustomBtn.FlatAppearance.BorderSize = 0;
            _addCustomBtn.Click += (s, e) => PromptCreateCustomMode();
            modeCard.Controls.Add(_addCustomBtn);

            _deleteCustomBtn = new Button
            {
                Text = "Del",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(548, 74),
                Size = new Size(94, 26),
                Cursor = Cursors.Hand
            };
            _deleteCustomBtn.FlatAppearance.BorderSize = 0;
            _deleteCustomBtn.Click += (s, e) => DeleteSelectedCustomMode();
            modeCard.Controls.Add(_deleteCustomBtn);

            // Row 4 (y=105): Large Interactive Fan Curve Graph + Combined Live Telemetry Graph (636 x 238)
            _curveCanvas = new FanCurveCanvas
            {
                Location = new Point(8, 105),
                Size = new Size(636, 238),
                ApuProfile = _apuProfile,
                SysProfile = _sysProfile
            };
            _curveCanvas.BuiltInCurveDragStarted += (s, e) =>
            {
                FanCurveProfile curProf = _curveCanvas.GetActiveProfile();
                if (curProf != null && curProf.IsBuiltIn)
                {
                    string customName = curProf.Name + " Custom";
                    FanCurveProfile existing = null;
                    foreach (FanCurveProfile cp in _customProfiles)
                    {
                        if (cp.Name.Equals(customName, StringComparison.OrdinalIgnoreCase))
                        {
                            existing = cp;
                            break;
                        }
                    }
                    if (existing == null)
                    {
                        existing = curProf.CloneAsCustom(customName);
                        _customProfiles.Add(existing);
                    }
                    else
                    {
                        if (_curveCanvas.EditSysFan)
                            existing.SysPoints = FanCurveProfile.ClonePoints(curProf.SysPoints);
                        else
                            existing.ApuPoints = FanCurveProfile.ClonePoints(curProf.ApuPoints);
                    }
                    if (_curveCanvas.EditSysFan)
                    {
                        _sysProfile = existing;
                        _sysManualOverride = false;
                        _curveCanvas.SysProfile = _sysProfile;
                    }
                    else
                    {
                        _apuProfile = existing;
                        _apuManualOverride = false;
                        _curveCanvas.ApuProfile = _apuProfile;
                    }
                    PopulateCustomCombo();
                }
            };
            _curveCanvas.CurveModified += (s, e) =>
            {
                if (_curveCanvas.EditSysFan) _sysManualOverride = false;
                else _apuManualOverride = false;
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                SaveSettings();
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                RefreshStatus();
            };
            modeCard.Controls.Add(_curveCanvas);

            _liveCanvas = new LiveTelemetryCanvas
            {
                Location = new Point(8, 105),
                Size = new Size(636, 238),
                Stats = _sessionStats,
                HasDualFans = _hasDualFans,
                Visible = false
            };
            _liveCanvas.ResetPeaksRequested += (s, e) => ResetSessionPeaks();
            _liveCanvas.HighlightMetricChanged += (s, e) =>
            {
                _highlightedMetric = _liveCanvas.HighlightMetric;
                UpdateCurveHint();
                if (_cpuTempBox != null) _cpuTempBox.Invalidate();
                if (_gpuTempBox != null) _gpuTempBox.Invalidate();
                if (_pwrBox != null) _pwrBox.Invalidate();
                if (_fan1Box != null) _fan1Box.Invalidate();
                if (_fan2Box != null) _fan2Box.Invalidate();
            };
            modeCard.Controls.Add(_liveCanvas);

            // OSD Overlay Configuration Studio Panel (covers rows 2, 3, 4 inside modeCard)
            _osdConfigPanel = new Panel
            {
                Location = new Point(8, 41),
                Size = new Size(636, 304),
                Visible = false
            };
            BuildOsdStudioPanel(_osdConfigPanel);
            modeCard.Controls.Add(_osdConfigPanel);

            _curveHintLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(8, 348),
                Size = new Size(636, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };
            modeCard.Controls.Add(_curveHintLabel);

            // 3. Manual Fan Control Section (top = 598, card = 618..712, height = 94)
            Panel fanCard = CreateSectionCard("Manual Fan Control", 598, 94);

            _calibBadgeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(240, 599),
                Size = new Size(426, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            _calibBadgeLabel.Click += (s, e) => RunGuiFanCalibration();
            Controls.Add(_calibBadgeLabel);
            _calibBadgeLabel.BringToFront();
            _uiToolTip.SetToolTip(_calibBadgeLabel,
                "Shows when your fans were last calibrated and their minimum anti-stall PWM %.\r\nClick to run the 3-pass real-time Fan Calibration Wizard.");

            AddStaticLabel(fanCard, "APU / CPU Fan:", 10, 14, 110, 22);
            _fan1Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(124, 10),
                Size = new Size(384, 32)
            };
            _fan1Slider.Scroll += (s, e) =>
            {
                _dragging1 = true;
                int v = _fan1Slider.Value;
                _fan1SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan1Slider.MouseCaptureChanged += (s, e) =>
            {
                if (_dragging1)
                {
                    _dragging1 = false;
                    ApplyFan(1, _fan1Slider.Value);
                }
            };
            _fan1Slider.MouseUp += (s, e) =>
            {
                if (_dragging1)
                {
                    _dragging1 = false;
                    ApplyFan(1, _fan1Slider.Value);
                }
            };
            _fan1Slider.KeyUp += (s, e) =>
            {
                ApplyFan(1, _fan1Slider.Value);
            };
            fanCard.Controls.Add(_fan1Slider);

            _fan1SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(514, 14),
                Size = new Size(56, 22),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan1SliderPct);

            _fan1AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(576, 12),
                Size = new Size(66, 26),
                Cursor = Cursors.Hand
            };
            _fan1AutoBtn.FlatAppearance.BorderSize = 0;
            _fan1AutoBtn.Click += (s, e) => ApplyFan(1, 0);
            fanCard.Controls.Add(_fan1AutoBtn);

            AddStaticLabel(fanCard, "Sys / RAM Fan:", 10, 52, 110, 22);
            _fan2Slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(124, 48),
                Size = new Size(384, 32)
            };
            _fan2Slider.Scroll += (s, e) =>
            {
                _dragging2 = true;
                int v = _fan2Slider.Value;
                _fan2SliderPct.Text = v == 0 ? "Auto" : string.Format("{0}%", v);
            };
            _fan2Slider.MouseCaptureChanged += (s, e) =>
            {
                if (_dragging2)
                {
                    _dragging2 = false;
                    ApplyFan(2, _fan2Slider.Value);
                }
            };
            _fan2Slider.MouseUp += (s, e) =>
            {
                if (_dragging2)
                {
                    _dragging2 = false;
                    ApplyFan(2, _fan2Slider.Value);
                }
            };
            _fan2Slider.KeyUp += (s, e) =>
            {
                ApplyFan(2, _fan2Slider.Value);
            };
            fanCard.Controls.Add(_fan2Slider);

            _fan2SliderPct = new Label
            {
                Text = "Auto",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(514, 52),
                Size = new Size(56, 22),
                TextAlign = ContentAlignment.MiddleCenter
            };
            fanCard.Controls.Add(_fan2SliderPct);

            _fan2AutoBtn = new Button
            {
                Text = "Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(576, 50),
                Size = new Size(66, 26),
                Cursor = Cursors.Hand
            };
            _fan2AutoBtn.FlatAppearance.BorderSize = 0;
            _fan2AutoBtn.Click += (s, e) => ApplyFan(2, 0);
            fanCard.Controls.Add(_fan2AutoBtn);

            // 4. Options Section (top = 718, card = 738..820, height = 82)
            Panel optCard = CreateSectionCard("Options", 718, 82);

            _samplingLabel = new Label
            {
                Text = "Sampling:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(410, 719),
                AutoSize = true,
                Cursor = Cursors.Default
            };
            Controls.Add(_samplingLabel);
            _samplingLabel.BringToFront();

            _samplingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(480, 715),
                Size = new Size(185, 22),
                Cursor = Cursors.Hand
            };
            int[] sampleIntervals = new int[] { 500, 1000, 1500, 2000, 3000 };
            _samplingCombo.Items.Add("500 ms (Fast • 2 Hz)");
            _samplingCombo.Items.Add("1000 ms (Standard • 1.0s)");
            _samplingCombo.Items.Add("1500 ms (Balanced • 1.5s)");
            _samplingCombo.Items.Add("2000 ms (Relaxed • 2.0s)");
            _samplingCombo.Items.Add("3000 ms (Low CPU • 3.0s)");
            int initialSel = 1;
            for (int i = 0; i < sampleIntervals.Length; i++)
            {
                if (sampleIntervals[i] == _refreshIntervalMs) { initialSel = i; break; }
            }
            _samplingCombo.SelectedIndex = initialSel;
            _samplingCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_samplingCombo.SelectedIndex >= 0 && _samplingCombo.SelectedIndex < sampleIntervals.Length)
                {
                    SetRefreshInterval(sampleIntervals[_samplingCombo.SelectedIndex]);
                }
            };
            Controls.Add(_samplingCombo);
            _samplingCombo.BringToFront();
            _uiToolTip.SetToolTip(_samplingCombo, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");
            _uiToolTip.SetToolTip(_samplingLabel, "Frequency at which MiniFanControl samples EC hardware registers, AMD GPU telemetry, and evaluates fan curves.");

            _showCpuTrayCb = new CheckBox
            {
                Text = "Show CPU temp in taskbar",
                Location = new Point(12, 6),
                Size = new Size(290, 22),
                Checked = _showCpuTray
            };
            _showCpuTrayCb.CheckedChanged += (s, e) =>
            {
                _showCpuTray = _showCpuTrayCb.Checked;
                SaveSettings();
                _lastCpuIconKey = int.MinValue;
                RefreshStatus();
            };
            optCard.Controls.Add(_showCpuTrayCb);

            _showGpuTrayCb = new CheckBox
            {
                Text = "Show iGPU temp in taskbar",
                Location = new Point(340, 6),
                Size = new Size(290, 22),
                Checked = _showGpuTray
            };
            _showGpuTrayCb.CheckedChanged += (s, e) =>
            {
                _showGpuTray = _showGpuTrayCb.Checked;
                SaveSettings();
                _lastGpuIconKey = int.MinValue;
                RefreshStatus();
            };
            optCard.Controls.Add(_showGpuTrayCb);

            _showOverlayCb = new CheckBox
            {
                Text = "Show CapFrameX OSD overlay",
                Location = new Point(12, 30),
                Size = new Size(215, 22),
                Checked = _showOverlay
            };
            _showOverlayCb.CheckedChanged += (s, e) =>
            {
                SetOverlayVisible(_showOverlayCb.Checked);
                if (_osdToggleOverlayBtn != null)
                    _osdToggleOverlayBtn.Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden";
            };
            optCard.Controls.Add(_showOverlayCb);

            _optCardOsdConfigBtn = new Button
            {
                Text = "⚙ Config...",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Location = new Point(230, 29),
                Size = new Size(84, 24),
                Cursor = Cursors.Hand
            };
            _optCardOsdConfigBtn.FlatAppearance.BorderSize = 0;
            _optCardOsdConfigBtn.Click += (s, e) => OpenOsdConfigTab();
            optCard.Controls.Add(_optCardOsdConfigBtn);
            _uiToolTip.SetToolTip(_optCardOsdConfigBtn, "Open the dedicated OSD Studio tab to configure live graphs, placement, opacity and metrics");

            _lockOverlayCb = new CheckBox
            {
                Text = "Lock OSD (click-through)",
                Location = new Point(340, 30),
                Size = new Size(290, 22),
                Checked = _lockOverlay
            };
            _lockOverlayCb.CheckedChanged += (s, e) => SetOverlayLocked(_lockOverlayCb.Checked);
            optCard.Controls.Add(_lockOverlayCb);

            _autostartCb = new CheckBox
            {
                Text = "Start automatically on Windows login",
                Location = new Point(12, 54),
                Size = new Size(290, 22),
                Checked = IsAutostartEnabled()
            };
            _autostartCb.CheckedChanged += (s, e) => ToggleAutostart();
            optCard.Controls.Add(_autostartCb);
            _uiToolTip.SetToolTip(_autostartCb,
                "Registers an elevated Windows Scheduled Task (MiniFanControl_Autostart)\r\nso MiniFanControl starts on Windows login without a UAC prompt.");

            _trayCb = new CheckBox
            {
                Text = "Hide to system tray",
                Location = new Point(340, 54),
                Size = new Size(290, 22)
            };
            _trayCb.CheckedChanged += (s, e) =>
            {
                if (_trayCb.Checked) MinimizeToTray();
            };
            optCard.Controls.Add(_trayCb);

            // Error Banner
            _errorBanner = new Label
            {
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(14, 738),
                Size = new Size(652, 82),
                Padding = new Padding(8),
                Visible = false
            };
            Controls.Add(_errorBanner);
            _errorBanner.BringToFront();

            // 5. Status Bar (y = 824..854)
            _statusBarLabel = new Label
            {
                Text = "⏳ Initializing…",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(14, 827),
                Size = new Size(450, 22)
            };
            Controls.Add(_statusBarLabel);

            _allAutoBtn = new Button
            {
                Text = "Reset Auto",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(478, 823),
                Size = new Size(90, 28),
                Cursor = Cursors.Hand
            };
            _allAutoBtn.FlatAppearance.BorderSize = 0;
            _allAutoBtn.Click += (s, e) => ApplyAutoAll();
            Controls.Add(_allAutoBtn);

            _closeBtn = new Button
            {
                Text = "Close",
                FlatStyle = FlatStyle.Flat,
                Location = new Point(574, 823),
                Size = new Size(92, 28),
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += (s, e) => Close();
            Controls.Add(_closeBtn);
        }

        private void BuildOsdStudioPanel(Panel p)
        {
            // Row 1 (y=4): Title + Quick Toggle Buttons
            Label title = new Label
            {
                Text = "🖥  OSD OVERLAY CONFIGURATION STUDIO",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(4, 7),
                Size = new Size(320, 22),
                UseMnemonic = false
            };
            p.Controls.Add(title);

            _osdToggleOverlayBtn = new Button
            {
                Text = _showOverlay ? "● OSD Active" : "○ OSD Hidden",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(376, 4),
                Size = new Size(124, 26),
                Cursor = Cursors.Hand
            };
            _osdToggleOverlayBtn.FlatAppearance.BorderSize = 0;
            _osdToggleOverlayBtn.Click += (s, e) =>
            {
                if (_showOverlayCb != null) _showOverlayCb.Checked = !_showOverlay;
                else SetOverlayVisible(!_showOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            p.Controls.Add(_osdToggleOverlayBtn);

            _osdToggleLockBtn = new Button
            {
                Text = _lockOverlay ? "🔒 Locked" : "🔓 Drag Mode",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(506, 4),
                Size = new Size(124, 26),
                Cursor = Cursors.Hand
            };
            _osdToggleLockBtn.FlatAppearance.BorderSize = 0;
            _osdToggleLockBtn.Click += (s, e) =>
            {
                if (_lockOverlayCb != null) _lockOverlayCb.Checked = !_lockOverlay;
                else SetOverlayLocked(!_lockOverlay);
                SyncOsdStudioUiFromOverlay();
            };
            p.Controls.Add(_osdToggleLockBtn);

            // Row 2 (y=32): Live Graph Presentation Section
            Label lblGraphMode = new Label
            {
                Text = "Live Graph Display Mode:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 33),
                Size = new Size(240, 15),
                UseMnemonic = false
            };
            p.Controls.Add(lblGraphMode);

            // Row 3 (y=51): 4 Mode Pill Buttons
            _osdBtnBothGraphs = new Button
            {
                Text = "✦ Both (Lines + Graph)",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 49),
                Size = new Size(154, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnBothGraphs.FlatAppearance.BorderSize = 0;
            _osdBtnBothGraphs.Click += (s, e) => { _osdGraphMode = 3; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnBothGraphs);

            _osdBtnCombinedGraph = new Button
            {
                Text = "📈 Combined Graph",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(164, 49),
                Size = new Size(152, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnCombinedGraph.FlatAppearance.BorderSize = 0;
            _osdBtnCombinedGraph.Click += (s, e) => { _osdGraphMode = 2; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnCombinedGraph);

            _osdBtnSparklines = new Button
            {
                Text = "〰 Mini Sparklines",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(322, 49),
                Size = new Size(152, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnSparklines.FlatAppearance.BorderSize = 0;
            _osdBtnSparklines.Click += (s, e) => { _osdGraphMode = 1; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnSparklines);

            _osdBtnTextOnly = new Button
            {
                Text = "Text Only",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(480, 49),
                Size = new Size(150, 26),
                Cursor = Cursors.Hand
            };
            _osdBtnTextOnly.FlatAppearance.BorderSize = 0;
            _osdBtnTextOnly.Click += (s, e) => { _osdGraphMode = 0; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnTextOnly);

            // Row 3 (y=79): Scale & Layout Section
            Label lblScale = new Label
            {
                Text = "Overlay Scale Tier:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 79),
                Size = new Size(280, 15),
                UseMnemonic = false
            };
            p.Controls.Add(lblScale);

            Label lblLayout = new Label
            {
                Text = "Layout Direction:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(340, 79),
                Size = new Size(280, 15),
                UseMnemonic = false
            };
            p.Controls.Add(lblLayout);

            _osdBtnScale100 = new Button
            {
                Text = "100% Std",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(4, 96),
                Size = new Size(76, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnScale100.FlatAppearance.BorderSize = 0;
            _osdBtnScale100.Click += (s, e) => { _osdScaleTier = 0; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale100);

            _osdBtnScale125 = new Button
            {
                Text = "125% Lg",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(84, 96),
                Size = new Size(76, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnScale125.FlatAppearance.BorderSize = 0;
            _osdBtnScale125.Click += (s, e) => { _osdScaleTier = 1; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale125);

            _osdBtnScale150 = new Button
            {
                Text = "150% XL",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(164, 96),
                Size = new Size(76, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnScale150.FlatAppearance.BorderSize = 0;
            _osdBtnScale150.Click += (s, e) => { _osdScaleTier = 2; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale150);

            _osdBtnScale185 = new Button
            {
                Text = "185% 4K",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(244, 96),
                Size = new Size(78, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnScale185.FlatAppearance.BorderSize = 0;
            _osdBtnScale185.Click += (s, e) => { _osdScaleTier = 3; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnScale185);

            _osdBtnLayoutH = new Button
            {
                Text = "▬ Horiz Bar",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(340, 96),
                Size = new Size(140, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutH.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutH.Click += (s, e) => { _overlayVertical = false; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnLayoutH);

            _osdBtnLayoutV = new Button
            {
                Text = "▮ Vert Stack",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(486, 96),
                Size = new Size(144, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnLayoutV.FlatAppearance.BorderSize = 0;
            _osdBtnLayoutV.Click += (s, e) => { _overlayVertical = true; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnLayoutV);

            // Row 4 (y=118): Combined Live Graph Height
            Label lblHeight = new Label
            {
                Text = "Combined Live Graph Height:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 118),
                Size = new Size(240, 15),
                UseMnemonic = false
            };
            p.Controls.Add(lblHeight);

            _osdBtnHeightCompact = new Button
            {
                Text = "45px Compact",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(4, 134),
                Size = new Size(154, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightCompact.FlatAppearance.BorderSize = 0;
            _osdBtnHeightCompact.Click += (s, e) => { _osdGraphHeight = 45; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightCompact);

            _osdBtnHeightMedium = new Button
            {
                Text = "65px Medium",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(162, 134),
                Size = new Size(154, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightMedium.FlatAppearance.BorderSize = 0;
            _osdBtnHeightMedium.Click += (s, e) => { _osdGraphHeight = 65; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightMedium);

            _osdBtnHeightLarge = new Button
            {
                Text = "95px Large",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(320, 134),
                Size = new Size(154, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightLarge.FlatAppearance.BorderSize = 0;
            _osdBtnHeightLarge.Click += (s, e) => { _osdGraphHeight = 95; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightLarge);

            _osdBtnHeightXl = new Button
            {
                Text = "130px Extra Large",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                Location = new Point(478, 134),
                Size = new Size(152, 25),
                Cursor = Cursors.Hand
            };
            _osdBtnHeightXl.FlatAppearance.BorderSize = 0;
            _osdBtnHeightXl.Click += (s, e) => { _osdGraphHeight = 130; SyncOverlayFromStudio(); UpdateOsdStudioStyles(); };
            p.Controls.Add(_osdBtnHeightXl);

            // Row 5 (y=162): Opacity Slider & Quick Snap Corner Buttons
            Label lblOpacity = new Label
            {
                Text = "Opacity:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 166),
                Size = new Size(54, 20),
                UseMnemonic = false
            };
            p.Controls.Add(lblOpacity);

            _osdOpacitySlider = new TrackBar
            {
                Minimum = 35,
                Maximum = 100,
                Value = _osdOpacityPct,
                TickStyle = TickStyle.None,
                Location = new Point(58, 162),
                Size = new Size(140, 28)
            };
            _osdOpacitySlider.Scroll += (s, e) =>
            {
                _osdOpacityPct = _osdOpacitySlider.Value;
                _osdOpacityPctLabel.Text = string.Format("{0}%", _osdOpacityPct);
                if (_overlayForm != null) _overlayForm.ApplyOpacity(_osdOpacityPct);
                SaveSettings();
            };
            p.Controls.Add(_osdOpacitySlider);

            _osdOpacityPctLabel = new Label
            {
                Text = string.Format("{0}%", _osdOpacityPct),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(202, 166),
                Size = new Size(44, 20),
                UseMnemonic = false
            };
            p.Controls.Add(_osdOpacityPctLabel);

            Label lblSnap = new Label
            {
                Text = "Quick Snap:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(256, 166),
                Size = new Size(74, 20),
                UseMnemonic = false
            };
            p.Controls.Add(lblSnap);

            _osdQuickPosTl = new Button { Text = "↖ TL", FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(334, 163), Size = new Size(70, 25), Cursor = Cursors.Hand };
            _osdQuickPosTl.FlatAppearance.BorderSize = 0;
            _osdQuickPosTl.Click += (s, e) => SetOverlayCornerPosition(0);
            p.Controls.Add(_osdQuickPosTl);

            _osdQuickPosTr = new Button { Text = "↗ TR", FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(408, 163), Size = new Size(70, 25), Cursor = Cursors.Hand };
            _osdQuickPosTr.FlatAppearance.BorderSize = 0;
            _osdQuickPosTr.Click += (s, e) => SetOverlayCornerPosition(1);
            p.Controls.Add(_osdQuickPosTr);

            _osdQuickPosBl = new Button { Text = "↙ BL", FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(482, 163), Size = new Size(70, 25), Cursor = Cursors.Hand };
            _osdQuickPosBl.FlatAppearance.BorderSize = 0;
            _osdQuickPosBl.Click += (s, e) => SetOverlayCornerPosition(2);
            p.Controls.Add(_osdQuickPosBl);

            _osdQuickPosBr = new Button { Text = "↘ BR", FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(556, 163), Size = new Size(74, 25), Cursor = Cursors.Hand };
            _osdQuickPosBr.FlatAppearance.BorderSize = 0;
            _osdQuickPosBr.Click += (s, e) => SetOverlayCornerPosition(3);
            p.Controls.Add(_osdQuickPosBr);

            // Row 6 (y=191): Telemetry Metrics Toggles
            Label lblMetrics = new Label
            {
                Text = "Visible Telemetry Metrics:",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                Location = new Point(4, 191),
                Size = new Size(240, 15),
                UseMnemonic = false
            };
            p.Controls.Add(lblMetrics);

            _osdShowCpuCb = new CheckBox { Text = "CPU Temp", Location = new Point(6, 207), Size = new Size(120, 20), Checked = _osdShowCpu };
            _osdShowCpuCb.CheckedChanged += (s, e) => { _osdShowCpu = _osdShowCpuCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowCpuCb);

            _osdShowGpuCb = new CheckBox { Text = "iGPU Temp", Location = new Point(130, 207), Size = new Size(120, 20), Checked = _osdShowGpu };
            _osdShowGpuCb.CheckedChanged += (s, e) => { _osdShowGpu = _osdShowGpuCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowGpuCb);

            _osdShowPowerCb = new CheckBox { Text = "APU Power (W)", Location = new Point(254, 207), Size = new Size(120, 20), Checked = _osdShowPower };
            _osdShowPowerCb.CheckedChanged += (s, e) => { _osdShowPower = _osdShowPowerCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowPowerCb);

            _osdShowGpuLoadCb = new CheckBox { Text = "FPS / GPU Load", Location = new Point(378, 207), Size = new Size(130, 20), Checked = _osdShowGpuLoad };
            _osdShowGpuLoadCb.CheckedChanged += (s, e) => { _osdShowGpuLoad = _osdShowGpuLoadCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowGpuLoadCb);

            _osdShowPeaksCb = new CheckBox { Text = "Peaks (▲)", Location = new Point(512, 207), Size = new Size(118, 20), Checked = _osdShowPeaks };
            _osdShowPeaksCb.CheckedChanged += (s, e) => { _osdShowPeaks = _osdShowPeaksCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowPeaksCb);

            _osdShowApuFanCb = new CheckBox { Text = "APU Fan RPM", Location = new Point(6, 229), Size = new Size(120, 20), Checked = _osdShowApuFan };
            _osdShowApuFanCb.CheckedChanged += (s, e) => { _osdShowApuFan = _osdShowApuFanCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowApuFanCb);

            _osdShowSysFanCb = new CheckBox { Text = "Sys Fan RPM", Location = new Point(130, 229), Size = new Size(120, 20), Checked = _osdShowSysFan };
            _osdShowSysFanCb.CheckedChanged += (s, e) => { _osdShowSysFan = _osdShowSysFanCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowSysFanCb);

            _osdShowModeCb = new CheckBox { Text = "Thermal Mode", Location = new Point(254, 229), Size = new Size(120, 20), Checked = _osdShowMode };
            _osdShowModeCb.CheckedChanged += (s, e) => { _osdShowMode = _osdShowModeCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdShowModeCb);

            _osdMatchThemeCb = new CheckBox { Text = "Match App Theme", Location = new Point(378, 229), Size = new Size(130, 20), Checked = _osdMatchTheme };
            _osdMatchThemeCb.CheckedChanged += (s, e) => { _osdMatchTheme = _osdMatchThemeCb.Checked; SyncOverlayFromStudio(); };
            p.Controls.Add(_osdMatchThemeCb);

            // Row 7 (y=253): Guidance Tip Box
            Label lblTip = new Label
            {
                Text = "💡 Tip: Drag OSD anywhere across multiple monitors with your mouse. Right-click the OSD anytime for quick scale, graph modes, heights & click-through lock, or press Ctrl+Alt+F9 to toggle overlay.",
                Font = new Font("Segoe UI", 7.75f, FontStyle.Regular),
                Location = new Point(4, 253),
                Size = new Size(626, 48),
                Padding = new Padding(4),
                BorderStyle = BorderStyle.FixedSingle,
                UseMnemonic = false
            };
            p.Controls.Add(lblTip);
        }

        private FanCurveProfile GetSelectedTabProfile()
        {
            if (_curveCanvas != null && _curveCanvas.EditSysFan)
                return _sysProfile ?? _builtInProfiles[0];
            return _apuProfile ?? _builtInProfiles[0];
        }

        private void PopulateCustomCombo()
        {
            _suppressComboEvent = true;
            _customModeCombo.Items.Clear();
            FanCurveProfile tabProf = GetSelectedTabProfile();
            int selIdx = 0;
            for (int i = 0; i < _customProfiles.Count; i++)
            {
                _customModeCombo.Items.Add(_customProfiles[i].Name);
                if (tabProf == _customProfiles[i])
                    selIdx = i;
            }
            if (_customModeCombo.Items.Count > 0)
                _customModeCombo.SelectedIndex = selIdx;
            _suppressComboEvent = false;
            UpdateCurveTabStyles();
            UpdateCurveHint();
        }

        private void OnCustomComboSelected(object sender, EventArgs e)
        {
            if (_suppressComboEvent) return;
            int idx = _customModeCombo.SelectedIndex;
            if (idx >= 0 && idx < _customProfiles.Count)
            {
                ActivateCustomProfile(_customProfiles[idx]);
            }
        }

        private void ActivateCustomProfile(FanCurveProfile profile)
        {
            if (profile == null) return;
            bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
            if (editSys)
            {
                _sysProfile = profile;
                _sysManualOverride = false;
                _lastAppliedSysPct = -1;
            }
            else
            {
                _apuProfile = profile;
                _apuManualOverride = false;
                _lastAppliedApuPct = -1;
            }

            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.Invalidate();
            }
            PopulateCustomCombo();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            SaveSettings();
            string fanLabel = editSys ? "Sys Fan" : "APU Fan";
            StartRampIndication(fanLabel + " → " + profile.Name, editSys ? 2 : 1);
        }

        private void PromptCreateCustomMode()
        {
            string suggested = string.Format("Custom {0}", _customProfiles.Count + 1);
            using (NewModeDialog dlg = new NewModeDialog(_isDarkMode, suggested))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.ModeName))
                {
                    FanCurveProfile baseProf = GetSelectedTabProfile();
                    FanCurveProfile created = baseProf.CloneAsCustom(dlg.ModeName);
                    _customProfiles.Add(created);
                    ActivateCustomProfile(created);
                }
            }
        }

        private void DeleteSelectedCustomMode()
        {
            int idx = _customModeCombo.SelectedIndex;
            if (idx < 0 || idx >= _customProfiles.Count) return;

            if (_customProfiles.Count <= 1)
            {
                MessageBox.Show(this, "At least one custom curve profile is kept.", "K12 Fan Control",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FanCurveProfile removed = _customProfiles[idx];
            _customProfiles.RemoveAt(idx);
            if (_apuProfile == removed)
            {
                _apuProfile = _builtInProfiles[0];
                _lastAppliedApuPct = -1;
            }
            if (_sysProfile == removed)
            {
                _sysProfile = _builtInProfiles[0];
                _lastAppliedSysPct = -1;
            }
            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.Invalidate();
            }
            PopulateCustomCombo();
            UpdateModeButtonStyles(_lastSelectedModeCode);
            SaveSettings();
            RefreshStatus();
        }

        private void UpdateCurveTabStyles()
        {
            if (_tabApuCurveBtn == null || _tabSysCurveBtn == null || _curveCanvas == null)
                return;

            string apuModeStr = _apuManualOverride ? "Manual" : (_apuProfile != null ? _apuProfile.Name : "Balanced");
            string sysModeStr = _sysManualOverride ? "Manual" : (_sysProfile != null ? _sysProfile.Name : "Balanced");
            _tabApuCurveBtn.Text = string.Format("● APU: {0}", ShortModeTag(apuModeStr));
            _tabSysCurveBtn.Text = string.Format("● Sys: {0}", ShortModeTag(sysModeStr));
            if (_tabLiveGraphBtn != null)
            {
                string hlTag = _highlightedMetric == 2 ? " (iGPU)"
                    : (_highlightedMetric == 3 ? " (Pwr)"
                    : (_highlightedMetric == 4 ? " (APU)"
                    : (_highlightedMetric == 5 ? " (Sys)"
                    : (_highlightedMetric == 1 ? " (CPU)" : ""))));
                _tabLiveGraphBtn.Text = _showingLiveTelemetry ? ("📈 Live" + hlTag) : "📈 Live Trend";
            }

            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface2 = isRadeon ? RadeonSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2);
            Color text = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);
            Color apuAccent = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
            Color sysAccent = isRadeon ? RadeonOrange : (_isDarkMode ? DarkTeal : LightTeal);
            Color liveAccent = _highlightedMetric == 2
                ? (isRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E))
                : (_highlightedMetric == 3
                    ? (isRadeon ? RadeonOrange : Color.FromArgb(0xF5, 0x9E, 0x0B))
                    : (_highlightedMetric == 4
                        ? Color.FromArgb(0xC0, 0x84, 0xFC)
                        : (_highlightedMetric == 5
                            ? Color.FromArgb(0x2D, 0xD4, 0xBF)
                            : apuAccent)));
            Color activeFg = isRadeon ? Color.White : (_isDarkMode ? Color.Black : Color.White);

            if (_showingOsdConfig)
            {
                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }

                if (_tabOsdConfigBtn != null)
                {
                    _tabOsdConfigBtn.BackColor = isRadeon ? RadeonAccent : apuAccent;
                    _tabOsdConfigBtn.ForeColor = activeFg;
                    if (_tabOsdConfigBtn.Font != TabFontBold) _tabOsdConfigBtn.Font = TabFontBold;
                }
                return;
            }

            if (_tabOsdConfigBtn != null)
            {
                _tabOsdConfigBtn.BackColor = surface2;
                _tabOsdConfigBtn.ForeColor = text;
                if (_tabOsdConfigBtn.Font != TabFontRegular) _tabOsdConfigBtn.Font = TabFontRegular;
            }

            if (_showingLiveTelemetry)
            {
                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = liveAccent;
                    _tabLiveGraphBtn.ForeColor = (_highlightedMetric == 2 || _highlightedMetric == 3) ? Color.Black : activeFg;
                    if (_tabLiveGraphBtn.Font != TabFontBold) _tabLiveGraphBtn.Font = TabFontBold;
                }
            }
            else if (!_curveCanvas.EditSysFan)
            {
                _tabApuCurveBtn.BackColor = apuAccent;
                _tabApuCurveBtn.ForeColor = activeFg;
                if (_tabApuCurveBtn.Font != TabFontBold) _tabApuCurveBtn.Font = TabFontBold;

                _tabSysCurveBtn.BackColor = surface2;
                _tabSysCurveBtn.ForeColor = text;
                if (_tabSysCurveBtn.Font != TabFontRegular) _tabSysCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }
            }
            else
            {
                _tabSysCurveBtn.BackColor = sysAccent;
                _tabSysCurveBtn.ForeColor = activeFg;
                if (_tabSysCurveBtn.Font != TabFontBold) _tabSysCurveBtn.Font = TabFontBold;

                _tabApuCurveBtn.BackColor = surface2;
                _tabApuCurveBtn.ForeColor = text;
                if (_tabApuCurveBtn.Font != TabFontRegular) _tabApuCurveBtn.Font = TabFontRegular;

                if (_tabLiveGraphBtn != null)
                {
                    _tabLiveGraphBtn.BackColor = surface2;
                    _tabLiveGraphBtn.ForeColor = text;
                    if (_tabLiveGraphBtn.Font != TabFontRegular) _tabLiveGraphBtn.Font = TabFontRegular;
                }
            }
        }

        private void UpdateOsdStudioStyles()
        {
            if (_osdConfigPanel == null) return;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface = isRadeon ? RadeonSurface : (_isDarkMode ? DarkSurface : LightSurface);
            Color surface2 = isRadeon ? RadeonSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2);
            Color text = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);
            Color accent = isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent);
            Color activeFg = isRadeon ? Color.White : (_isDarkMode ? Color.Black : Color.White);

            _osdConfigPanel.BackColor = surface;

            // Highlight active Graph Mode button
            Button[] modeBtns = new[] { _osdBtnTextOnly, _osdBtnSparklines, _osdBtnCombinedGraph, _osdBtnBothGraphs };
            for (int i = 0; i < modeBtns.Length; i++)
            {
                if (modeBtns[i] == null) continue;
                bool active = (_osdGraphMode == i);
                modeBtns[i].BackColor = active ? accent : surface2;
                modeBtns[i].ForeColor = active ? activeFg : text;
                modeBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Scale button (100%, 125%, 150%, 185%)
            Button[] scaleBtns = new[] { _osdBtnScale100, _osdBtnScale125, _osdBtnScale150, _osdBtnScale185 };
            for (int i = 0; i < scaleBtns.Length; i++)
            {
                if (scaleBtns[i] == null) continue;
                bool active = (_osdScaleTier == i);
                scaleBtns[i].BackColor = active ? accent : surface2;
                scaleBtns[i].ForeColor = active ? activeFg : text;
                scaleBtns[i].Font = active ? TabFontBold : TabFontRegular;
            }

            // Highlight active Layout button
            if (_osdBtnLayoutH != null)
            {
                bool activeH = !_overlayVertical;
                _osdBtnLayoutH.BackColor = activeH ? accent : surface2;
                _osdBtnLayoutH.ForeColor = activeH ? activeFg : text;
                _osdBtnLayoutH.Font = activeH ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnLayoutV != null)
            {
                bool activeV = _overlayVertical;
                _osdBtnLayoutV.BackColor = activeV ? accent : surface2;
                _osdBtnLayoutV.ForeColor = activeV ? activeFg : text;
                _osdBtnLayoutV.Font = activeV ? TabFontBold : TabFontRegular;
            }

            // Highlight active Height button (45px Compact, 65px Medium, 95px Large, 130px Extra Large)
            if (_osdBtnHeightCompact != null)
            {
                bool active = (_osdGraphHeight <= 50);
                _osdBtnHeightCompact.BackColor = active ? accent : surface2;
                _osdBtnHeightCompact.ForeColor = active ? activeFg : text;
                _osdBtnHeightCompact.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightMedium != null)
            {
                bool active = (_osdGraphHeight > 50 && _osdGraphHeight <= 75);
                _osdBtnHeightMedium.BackColor = active ? accent : surface2;
                _osdBtnHeightMedium.ForeColor = active ? activeFg : text;
                _osdBtnHeightMedium.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightLarge != null)
            {
                bool active = (_osdGraphHeight > 75 && _osdGraphHeight <= 110);
                _osdBtnHeightLarge.BackColor = active ? accent : surface2;
                _osdBtnHeightLarge.ForeColor = active ? activeFg : text;
                _osdBtnHeightLarge.Font = active ? TabFontBold : TabFontRegular;
            }
            if (_osdBtnHeightXl != null)
            {
                bool active = (_osdGraphHeight > 110);
                _osdBtnHeightXl.BackColor = active ? accent : surface2;
                _osdBtnHeightXl.ForeColor = active ? activeFg : text;
                _osdBtnHeightXl.Font = active ? TabFontBold : TabFontRegular;
            }

            // Style quick snap buttons
            Button[] snapBtns = new[] { _osdQuickPosTl, _osdQuickPosTr, _osdQuickPosBl, _osdQuickPosBr };
            foreach (Button b in snapBtns)
            {
                if (b != null)
                {
                    b.BackColor = surface2;
                    b.ForeColor = text;
                }
            }

            // Style quick toggle buttons
            if (_osdToggleOverlayBtn != null)
            {
                _osdToggleOverlayBtn.BackColor = _showOverlay ? accent : surface2;
                _osdToggleOverlayBtn.ForeColor = _showOverlay ? activeFg : text;
            }
            if (_osdToggleLockBtn != null)
            {
                _osdToggleLockBtn.BackColor = _lockOverlay ? (isRadeon ? RadeonOrange : Color.FromArgb(0xEA, 0x58, 0x0C)) : surface2;
                _osdToggleLockBtn.ForeColor = _lockOverlay ? Color.White : text;
            }

            if (_optCardOsdConfigBtn != null)
            {
                _optCardOsdConfigBtn.BackColor = surface2;
                _optCardOsdConfigBtn.ForeColor = text;
            }

            if (_osdOpacitySlider != null) _osdOpacitySlider.BackColor = surface;
            if (_osdOpacityPctLabel != null) _osdOpacityPctLabel.ForeColor = text;

            // CheckBoxes inside studio
            CheckBox[] cbs = new[] { _osdShowCpuCb, _osdShowGpuCb, _osdShowPowerCb, _osdShowGpuLoadCb, _osdShowPeaksCb, _osdShowApuFanCb, _osdShowSysFanCb, _osdShowModeCb, _osdMatchThemeCb };
            foreach (CheckBox cb in cbs)
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                }
            }
        }

        private void UpdateCurveHint()
        {
            if (_curveHintLabel == null || _curveCanvas == null) return;
            if (_showingOsdConfig)
            {
                _curveHintLabel.Text = "OSD Overlay Studio — configure live graphs, placement, opacity and telemetry metrics";
                return;
            }
            if (_showingLiveTelemetry)
            {
                string hlName = _highlightedMetric == 2 ? "iGPU Temp"
                    : (_highlightedMetric == 3 ? "APU Power"
                    : (_highlightedMetric == 4 ? "APU Fan Speed"
                    : (_highlightedMetric == 5 ? "System Fan Speed" : "CPU Temp")));
                _curveHintLabel.Text = string.Format("Spotlighting {0} — click metric boxes or legend pills to switch, hover to inspect", hlName);
                return;
            }
            FanCurveProfile prof = GetSelectedTabProfile();
            string whichFan = _curveCanvas.EditSysFan ? "Sys Fan" : "APU Fan";
            if (prof.IsBuiltIn)
            {
                _curveHintLabel.Text = string.Format("{0}: '{1}' — drag points to customize, or click any metric box for Live Trend", whichFan, prof.Name);
            }
            else
            {
                _curveHintLabel.Text = string.Format("Editing {0} curve '{1}' — click any metric box to view Live Trend", whichFan, prof.Name);
            }
        }

        private void UpdateCalibrationBadge()
        {
            if (_calibBadgeLabel == null) return;
            bool isCalibrated, isStale;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            _calibBadgeLabel.Text = FanCurveProfile.FormatCalibrationStatusBadge(_hasDualFans, out isCalibrated, out isStale);
            _calibBadgeLabel.BackColor = isRadeon ? RadeonBg : (_isDarkMode ? DarkBg : LightBg);
            if (!isCalibrated)
            {
                _calibBadgeLabel.ForeColor = isRadeon ? RadeonTextDim : (_isDarkMode ? DarkTextDim : LightTextDim);
            }
            else if (isStale)
            {
                _calibBadgeLabel.ForeColor = _isDarkMode ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xB4, 0x53, 0x09);
            }
            else
            {
                _calibBadgeLabel.ForeColor = _isDarkMode ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x15, 0x80, 0x3D);
            }
        }

        private string GetThemeButtonLabel()
        {
            switch (_themePref)
            {
                case ThemePreference.Dark: return "Theme: Dark";
                case ThemePreference.Radeon: return "Theme: AMD Radeon";
                case ThemePreference.Light: return "Theme: Light";
                default: return string.Format("Theme: System ({0})", _isDarkMode ? "Dark" : "Light");
            }
        }

        private void CycleThemePreference()
        {
            if (_themePref == ThemePreference.System) _themePref = ThemePreference.Dark;
            else if (_themePref == ThemePreference.Dark) _themePref = ThemePreference.Radeon;
            else if (_themePref == ThemePreference.Radeon) _themePref = ThemePreference.Light;
            else _themePref = ThemePreference.System;

            SaveSettings();
            DetectSystemTheme(out _isDarkMode, out _isTaskbarDark);
            _lastCpuIconKey = int.MinValue;
            _lastGpuIconKey = int.MinValue;
            ApplyThemeToControls();
            RefreshStatus();
        }

        private void ApplyThemeToControls()
        {
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color bg = isRadeon ? RadeonBg : (_isDarkMode ? DarkBg : LightBg);
            Color surface = isRadeon ? RadeonSurface : (_isDarkMode ? DarkSurface : LightSurface);
            Color surface2 = isRadeon ? RadeonSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2);
            Color text = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);
            Color textDim = isRadeon ? RadeonTextDim : (_isDarkMode ? DarkTextDim : LightTextDim);
            Color accent = isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (_isDarkMode ? DarkAccent : LightAccent);

            Color tempBoxBg = isRadeon ? Color.FromArgb(0x0D, 0x05, 0x07) : (_isDarkMode ? Color.Black : Color.White);
            Color tempBoxFg = _isDarkMode ? Color.White : Color.Black;
            Color tempBoxTitleFg = isRadeon
                ? Color.FromArgb(0xFD, 0xA4, 0xAF)
                : (_isDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x47, 0x55, 0x69));

            BackColor = bg;
            ForeColor = text;

            if (_headerLogoBox != null)
            {
                _headerLogoBox.BackColor = surface;
                Image oldImg = _headerLogoBox.Image;
                _headerLogoBox.Image = AppLogoRenderer.RenderConcept(isRadeon ? 4 : 1, 192);
                if (oldImg != null) oldImg.Dispose();
            }

            if (_hwBadgeLabel != null)
            {
                _hwBadgeLabel.BackColor = bg;
                _hwBadgeLabel.ForeColor = text;
            }

            if (_hwSubLabel != null)
            {
                _hwSubLabel.BackColor = bg;
                _hwSubLabel.ForeColor = textDim;
            }

            if (_driversMenuBtn != null)
            {
                _driversMenuBtn.BackColor = surface2;
                _driversMenuBtn.ForeColor = text;
            }

            if (_themeToggleBtn != null)
            {
                _themeToggleBtn.Text = GetThemeButtonLabel();
                _themeToggleBtn.BackColor = isRadeon ? RadeonAccent : surface2;
                _themeToggleBtn.ForeColor = isRadeon ? Color.White : text;
            }

            foreach (Label h in _sectionHeaders)
                h.ForeColor = accent;

            UpdateCalibrationBadge();

            foreach (Panel c in _sectionCards)
                c.BackColor = surface;

            foreach (Label l in _staticLabels)
            {
                l.BackColor = surface;
                l.ForeColor = textDim;
            }

            if (_cpuTempBox != null)
            {
                _cpuTempBox.BackColor = tempBoxBg;
                _cpuBoxTitle.BackColor = tempBoxBg;
                _cpuBoxTitle.ForeColor = tempBoxTitleFg;
                _cpuTempLabel.BackColor = tempBoxBg;
                _cpuTempLabel.ForeColor = tempBoxFg;
            }

            if (_gpuTempBox != null)
            {
                _gpuTempBox.BackColor = tempBoxBg;
                _gpuBoxTitle.BackColor = tempBoxBg;
                _gpuBoxTitle.ForeColor = tempBoxTitleFg;
                _gpuTempLabel.BackColor = tempBoxBg;
                _gpuTempLabel.ForeColor = tempBoxFg;
            }

            if (_pwrBox != null)
            {
                _pwrBox.BackColor = tempBoxBg;
                _pwrBoxTitle.BackColor = tempBoxBg;
                _pwrBoxTitle.ForeColor = tempBoxTitleFg;
                _pwrLabel.BackColor = tempBoxBg;
                _pwrLabel.ForeColor = tempBoxFg;
            }

            if (_fan1Box != null)
            {
                _fan1Box.BackColor = tempBoxBg;
                if (_fan1BoxTitle != null)
                {
                    _fan1BoxTitle.BackColor = tempBoxBg;
                    _fan1BoxTitle.ForeColor = tempBoxTitleFg;
                }
                if (_fan1RpmLabel != null)
                {
                    _fan1RpmLabel.BackColor = tempBoxBg;
                    _fan1RpmLabel.ForeColor = tempBoxFg;
                }
            }

            if (_fan2Box != null)
            {
                _fan2Box.BackColor = tempBoxBg;
                if (_fan2BoxTitle != null)
                {
                    _fan2BoxTitle.BackColor = tempBoxBg;
                    _fan2BoxTitle.ForeColor = tempBoxTitleFg;
                }
                if (_fan2RpmLabel != null)
                {
                    _fan2RpmLabel.BackColor = tempBoxBg;
                    _fan2RpmLabel.ForeColor = tempBoxFg;
                }
            }

            _modeLabel.BackColor = surface;
            _modeLabel.ForeColor = accent;

            _pct1Label.BackColor = surface;
            _pct2Label.BackColor = surface;

            if (_customModeCombo != null)
            {
                _customModeCombo.BackColor = surface2;
                _customModeCombo.ForeColor = text;
            }

            if (_samplingCombo != null)
            {
                _samplingCombo.BackColor = surface2;
                _samplingCombo.ForeColor = text;
            }
            if (_samplingLabel != null)
            {
                _samplingLabel.ForeColor = textDim;
            }

            if (_curveCanvas != null)
            {
                _curveCanvas.IsDarkMode = _isDarkMode;
                _curveCanvas.IsRadeonTheme = isRadeon;
                _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null)
            {
                _liveCanvas.IsDarkMode = _isDarkMode;
                _liveCanvas.IsRadeonTheme = isRadeon;
                _liveCanvas.Invalidate();
            }

            UpdateCurveTabStyles();

            if (_curveHintLabel != null)
            {
                _curveHintLabel.BackColor = surface;
                _curveHintLabel.ForeColor = textDim;
                UpdateCurveHint();
            }

            _fan1Slider.BackColor = surface;
            _fan2Slider.BackColor = surface;
            _fan1SliderPct.BackColor = surface;
            _fan1SliderPct.ForeColor = text;
            _fan2SliderPct.BackColor = surface;
            _fan2SliderPct.ForeColor = text;

            foreach (Button b in new[] { _addCustomBtn, _deleteCustomBtn, _fan1AutoBtn, _fan2AutoBtn, _allAutoBtn, _closeBtn })
            {
                if (b != null)
                {
                    b.BackColor = surface2;
                    b.ForeColor = text;
                }
            }

            foreach (CheckBox cb in new[] { _showCpuTrayCb, _showGpuTrayCb, _showOverlayCb, _lockOverlayCb, _autostartCb, _trayCb })
            {
                if (cb != null)
                {
                    cb.BackColor = surface;
                    cb.ForeColor = text;
                }
            }

            if (_errorBanner != null)
            {
                _errorBanner.BackColor = _isDarkMode ? ErrorBgDark : ErrorBgLight;
                _errorBanner.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
            }

            if (_customLabel != null)
            {
                _customLabel.BackColor = surface;
                _customLabel.ForeColor = textDim;
            }
            if (_optCardOsdConfigBtn != null)
            {
                _optCardOsdConfigBtn.BackColor = surface2;
                _optCardOsdConfigBtn.ForeColor = text;
            }
            if (_overlayForm != null)
            {
                _overlayForm.IsRadeonTheme = isRadeon;
                _overlayForm.Invalidate();
            }
            UpdateOsdStudioStyles();

            UpdateModeButtonStyles(_lastSelectedModeCode);
        }

        private void UpdateModeButtonStyles(int activeCode)
        {
            _lastSelectedModeCode = activeCode;
            bool isRadeon = (_themePref == ThemePreference.Radeon);
            Color surface2 = isRadeon ? RadeonSurface2 : (_isDarkMode ? DarkSurface2 : LightSurface2);
            Color text = isRadeon ? RadeonText : (_isDarkMode ? DarkText : LightText);
            bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
            Color accent = editSys
                ? (isRadeon ? RadeonOrange : (_isDarkMode ? DarkTeal : LightTeal))
                : (isRadeon ? RadeonAccent : (_isDarkMode ? DarkAccent : LightAccent));
            Color activeFg = isRadeon ? Color.White : (_isDarkMode ? Color.Black : Color.White);

            FanCurveProfile tabProf = GetSelectedTabProfile();
            bool tabManual = editSys ? _sysManualOverride : _apuManualOverride;

            for (int i = 0; i < 3; i++)
            {
                if (_modeButtons[i] == null) continue;
                bool isSelected = (!tabManual && tabProf != null && tabProf.IsBuiltIn && tabProf.BuiltInCode == i);
                if (isSelected)
                {
                    _modeButtons[i].BackColor = accent;
                    _modeButtons[i].ForeColor = activeFg;
                    if (_modeButtons[i].Font != ModeFontBold) _modeButtons[i].Font = ModeFontBold;
                }
                else
                {
                    _modeButtons[i].BackColor = surface2;
                    _modeButtons[i].ForeColor = text;
                    if (_modeButtons[i].Font != ModeFontRegular) _modeButtons[i].Font = ModeFontRegular;
                }
            }

            if (_activateCustomBtn != null)
            {
                if (!tabManual && tabProf != null && !tabProf.IsBuiltIn)
                {
                    _activateCustomBtn.BackColor = accent;
                    _activateCustomBtn.ForeColor = activeFg;
                }
                else
                {
                    _activateCustomBtn.BackColor = surface2;
                    _activateCustomBtn.ForeColor = text;
                }
            }
        }

        private string FormatRpmText(int rpm, bool manualSliderOverride, int manualPct, bool isRamping)
        {
            if (isRamping)
            {
                return string.Format("{0} RPM ↻", rpm);
            }
            if (rpm == 0)
            {
                if (manualSliderOverride && manualPct > 0 && manualPct <= 27)
                    return "0 RPM (Stall)";
                return "0 RPM (Idle)";
            }
            return string.Format("{0} RPM", rpm);
        }

        private bool EvaluateAndApplyFanProfiles(int apuTemp)
        {
            if (_apuProfile == null) _apuProfile = _builtInProfiles[0];
            if (_sysProfile == null) _sysProfile = _builtInProfiles[0];

            bool wroteHardware = false;

            // Emergency Thermal Safety Guard (TJMax Tripwire):
            // If APU temperature hits >= 90°C, bypass manual overrides and curves, forcing 100% PWM.
            // Hysteresis holds until temperature recovers below 75°C.
            if (HardwareBackend.CheckEmergencyThermalTripwire(apuTemp, ref _emergencyThermalActive))
            {
                if (_lastAppliedApuPct != 100)
                {
                    if (_demoMode) _demo.SetFan(1, 100);
                    else _hw.SetFan(1, 100);
                    _lastAppliedApuPct = 100;
                    wroteHardware = true;
                }
                if (_hasDualFans && _lastAppliedSysPct != 100)
                {
                    if (_demoMode) _demo.SetFan(2, 100);
                    else _hw.SetFan(2, 100);
                    _lastAppliedSysPct = 100;
                    wroteHardware = true;
                }
                return wroteHardware;
            }

            // Determine which built-in EC mode register (0x32) to use:
            // Prefer APU Fan's built-in mode so APU Fan can run on native EC Auto (0x00),
            // or if APU Fan is custom/manual, use Sys Fan's built-in mode so Sys Fan can run on native EC Auto!
            int desiredEcMode = -1;
            bool isSuperIo = (_hw != null && _hw.SuperIoBaseAddr != 0);
            bool apuUsesNativeEcAuto = false;
            bool sysUsesNativeEcAuto = false;

            if (!isSuperIo)
            {
                if (!_apuManualOverride && _apuProfile.IsBuiltIn)
                {
                    desiredEcMode = _apuProfile.BuiltInCode;
                    apuUsesNativeEcAuto = true;
                    if (_hasDualFans && !_sysManualOverride && _sysProfile.IsBuiltIn && _sysProfile.BuiltInCode == desiredEcMode)
                    {
                        sysUsesNativeEcAuto = true;
                    }
                }
                else if (_hasDualFans && !_sysManualOverride && _sysProfile.IsBuiltIn)
                {
                    desiredEcMode = _sysProfile.BuiltInCode;
                    sysUsesNativeEcAuto = true;
                }
            }

            // 2°C cooldown hysteresis for software curve updates
            bool withinCooldownHysteresis = (_lastCustomEvalTemp > 0 && apuTemp < _lastCustomEvalTemp && (_lastCustomEvalTemp - apuTemp) < 2);

            try
            {
                if (desiredEcMode >= 0 && _lastAppliedEcMode != desiredEcMode)
                {
                    if (_demoMode) _demo.SetMode(desiredEcMode);
                    else _hw.SetMode(desiredEcMode);
                    _lastAppliedEcMode = desiredEcMode;
                    wroteHardware = true;
                }

                // 1. Apply APU Fan (Fan 1)
                if (!_apuManualOverride)
                {
                    if (apuUsesNativeEcAuto)
                    {
                        if (_lastAppliedApuPct != 0)
                        {
                            if (_demoMode) _demo.SetFan(1, 0);
                            else _hw.SetFan(1, 0);
                            _lastAppliedApuPct = 0;
                            wroteHardware = true;
                        }
                    }
                    else
                    {
                        int targetApu = _apuProfile.EvaluateApu(apuTemp);
                        int regPct = targetApu <= 0 ? (isSuperIo ? 0 : 1) : targetApu;
                        if (_lastAppliedApuPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - _lastAppliedApuPct) >= 2) || regPct == 1 || regPct == 100)
                        {
                            if (_demoMode) _demo.SetFan(1, regPct);
                            else _hw.SetFan(1, regPct);
                            _lastAppliedApuPct = regPct;
                            _lastCustomEvalTemp = apuTemp;
                            wroteHardware = true;
                        }
                    }
                }

                // 2. Apply System RAM/NVMe Fan (Fan 2) only if device has 2 fans
                if (_hasDualFans && !_sysManualOverride)
                {
                    if (sysUsesNativeEcAuto)
                    {
                        if (_lastAppliedSysPct != 0)
                        {
                            if (_demoMode) _demo.SetFan(2, 0);
                            else _hw.SetFan(2, 0);
                            _lastAppliedSysPct = 0;
                            wroteHardware = true;
                        }
                    }
                    else
                    {
                        int targetSys = _sysProfile.EvaluateSys(apuTemp);
                        int regPct = targetSys <= 0 ? (isSuperIo ? 0 : 1) : targetSys;
                        if (_lastAppliedSysPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - _lastAppliedSysPct) >= 2) || regPct == 1 || regPct == 100)
                        {
                            if (_demoMode) _demo.SetFan(2, regPct);
                            else _hw.SetFan(2, regPct);
                            _lastAppliedSysPct = regPct;
                            _lastCustomEvalTemp = apuTemp;
                            wroteHardware = true;
                        }
                    }
                }
            }
            catch { }
            return wroteHardware;
        }

        private void HardwareWorkerLoop()
        {
            while (_hwThreadRunning)
            {
                if (!_hwWorkerPaused)
                {
                    try
                    {
                        FanStatus st = _demoMode ? _demo.GetStatus() : _hw.GetStatus();
                        int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);

                        bool hwChanged = EvaluateAndApplyFanProfiles(apuTemp);
                        if (hwChanged)
                        {
                            st = _demoMode ? _demo.GetStatus() : _hw.GetStatus();
                        }

                        _lastStatus = st;

                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke((Action)(() => OnHardwareWorkerSample(st)));
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (IsHandleCreated && !IsDisposed)
                        {
                            try
                            {
                                BeginInvoke((Action)(() => OnHardwareWorkerError(ex)));
                            }
                            catch { }
                        }
                    }
                }

                PulseWatchdogHeartbeat();

                int waitMs = _refreshIntervalMs;
                if (DateTime.UtcNow < _rampUntil) waitMs = Math.Min(waitMs, 250);
                _hwWakeEvent.WaitOne(waitMs);
            }
        }

        private void OnHardwareWorkerSample(FanStatus st)
        {
            try
            {
                ApplyStatusToUi(st);
            }
            catch { }
        }

        private void OnHardwareWorkerError(Exception ex)
        {
            _errorBanner.Text = "⚠️ " + ex.Message;
            _errorBanner.Visible = true;
            _statusBarLabel.ForeColor = _isDarkMode ? ErrorFgDark : ErrorFgLight;
            _statusBarLabel.Text = "⚠️ Driver/Hardware probe notice (see banner)";
        }

        private void ApplyStatusToUi(FanStatus st)
        {
            _errorBanner.Visible = false;
            _sessionStats.Record(st);

            bool isRamping = DateTime.UtcNow < _rampUntil;

            UpdateTrayIcons(st.CpuTemp, st.GpuTemp);

            if (_cpuTrayIcon != null)
            {
                string tip = string.Format("CPU: {0}°C (iGPU: {1}°C)\nAPU: {2} RPM | Sys: {3} RPM",
                    st.CpuTemp, st.GpuTemp, st.Fan1Rpm, st.Fan2Rpm);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_cpuTrayIcon.Text != tip) _cpuTrayIcon.Text = tip;
            }

            if (_gpuTrayIcon != null)
            {
                string tip = string.Format("iGPU: {0}°C (CPU: {1}°C)\nAPU: {2} RPM | Sys: {3} RPM",
                    st.GpuTemp, st.CpuTemp, st.Fan1Rpm, st.Fan2Rpm);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                if (_gpuTrayIcon.Text != tip) _gpuTrayIcon.Text = tip;
            }

            string apuModeName = _apuManualOverride ? "Man" : (_apuProfile != null ? _apuProfile.Name : st.Mode);
            string sysModeName = _sysManualOverride ? "Man" : (_sysProfile != null ? _sysProfile.Name : st.Mode);

            if (_overlayForm != null && _showOverlay)
            {
                string osdMode = (!_hasDualFans || apuModeName == sysModeName)
                    ? ShortModeTag(apuModeName)
                    : string.Format("{0}/{1}", ShortModeTag(apuModeName), ShortModeTag(sysModeName));
                _overlayForm.UpdateMetrics(st, osdMode, _hasDualFans);
            }

            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            Text = string.Format("CPU {0}°C | iGPU {1}°C - MiniFanControl", st.CpuTemp, st.GpuTemp);

            int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);
            if (_curveCanvas != null)
            {
                _curveCanvas.ApuProfile = _apuProfile;
                _curveCanvas.SysProfile = _sysProfile;
                _curveCanvas.CurrentApuTemp = apuTemp;
                if (!_showingLiveTelemetry)
                    _curveCanvas.Invalidate();
            }
            if (_liveCanvas != null && _showingLiveTelemetry)
            {
                _liveCanvas.Invalidate();
            }

            Color tempFg = _isDarkMode ? DarkText : LightText;
            Color dimText = _isDarkMode ? Color.FromArgb(0x93, 0x99, 0xB2) : Color.FromArgb(0x6C, 0x70, 0x86);
            Color rampColor = _isDarkMode ? Color.FromArgb(0xFA, 0xB3, 0x87) : Color.FromArgb(0xEA, 0x58, 0x0C);
            bool isRadeon = (_themePref == ThemePreference.Radeon);

            _cpuTempLabel.Text = st.CpuTemp > 0 ? string.Format("{0}°C", st.CpuTemp) : "N/A";
            _cpuTempLabel.ForeColor = tempFg;
            if (_cpuBoxTitle != null)
            {
                string cpuHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxCpuTemp > 0)
                    ? string.Format("CPU • MAX {0}°", _sessionStats.MaxCpuTemp)
                    : "CPU TEMP";
                if (_cpuBoxTitle.Text != cpuHeader) _cpuBoxTitle.Text = cpuHeader;
            }
            if (_uiToolTip != null && _cpuTempBox != null)
            {
                if (!IsBoxHovered(_cpuTempBox, _cpuBoxTitle, _cpuTempLabel))
                {
                    string cpuTip = st.FormatCpuTooltip(_sessionStats);
                    if (cpuTip != _lastCpuTooltipText)
                    {
                        _lastCpuTooltipText = cpuTip;
                        _uiToolTip.SetToolTip(_cpuTempBox, cpuTip);
                        _uiToolTip.SetToolTip(_cpuBoxTitle, cpuTip);
                        _uiToolTip.SetToolTip(_cpuTempLabel, cpuTip);
                    }
                }
                _cpuTempBox.Invalidate();
            }

            _gpuTempLabel.Text = st.GpuTemp > 0 ? string.Format("{0}°C", st.GpuTemp) : "N/A";
            _gpuTempLabel.ForeColor = tempFg;
            if (_gpuBoxTitle != null)
            {
                string gpuHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxGpuTemp > 0)
                    ? string.Format("iGPU • MAX {0}°", _sessionStats.MaxGpuTemp)
                    : "iGPU TEMP";
                if (_gpuBoxTitle.Text != gpuHeader) _gpuBoxTitle.Text = gpuHeader;
            }
            if (_uiToolTip != null && _gpuTempBox != null)
            {
                if (!IsBoxHovered(_gpuTempBox, _gpuBoxTitle, _gpuTempLabel))
                {
                    string igpuTip = st.FormatIgpuTooltip(_sessionStats);
                    if (igpuTip != _lastIgpuTooltipText)
                    {
                        _lastIgpuTooltipText = igpuTip;
                        _uiToolTip.SetToolTip(_gpuTempBox, igpuTip);
                        _uiToolTip.SetToolTip(_gpuBoxTitle, igpuTip);
                        _uiToolTip.SetToolTip(_gpuTempLabel, igpuTip);
                    }
                }
                _gpuTempBox.Invalidate();
            }

            if (_pwrLabel != null)
            {
                _pwrLabel.Text = st.ApuPowerWatts > 0 ? string.Format("{0}W", st.ApuPowerWatts) : "--W";
                _pwrLabel.ForeColor = tempFg;
                if (_pwrBoxTitle != null)
                {
                    string pwrHeader = (_sessionStats.SampleCount > 0 && _sessionStats.MaxPowerWatts > 0)
                        ? string.Format("PWR • MAX {0}W •", _sessionStats.MaxPowerWatts)
                        : "POWER •";
                    if (_pwrBoxTitle.Text != pwrHeader) _pwrBoxTitle.Text = pwrHeader;
                }
                if (_uiToolTip != null && !IsBoxHovered(_pwrBox, _pwrBoxTitle, _pwrLabel))
                {
                    string pwrTip = st.FormatPowerTooltip(_sessionStats);
                    if (pwrTip != _lastPowerTooltipText)
                    {
                        _lastPowerTooltipText = pwrTip;
                        _uiToolTip.SetToolTip(_pwrBox, pwrTip);
                        _uiToolTip.SetToolTip(_pwrBoxTitle, pwrTip);
                        _uiToolTip.SetToolTip(_pwrLabel, pwrTip);
                    }
                }
                _pwrBox.Invalidate();
            }

            _fan1RpmLabel.Text = string.Format("{0} RPM •", st.Fan1Rpm);
            _fan1RpmLabel.ForeColor = tempFg;
            if (_fan2RpmLabel != null)
            {
                _fan2RpmLabel.Text = !_hasDualFans
                    ? "N/A"
                    : (st.Fan2Rpm > 0 ? string.Format("{0} RPM •", st.Fan2Rpm) : "0 RPM •");
                _fan2RpmLabel.ForeColor = tempFg;
            }
            if (_fan1Box != null)
            {
                if (_fan1BoxTitle != null)
                {
                    string fan1Header = (_sessionStats.SampleCount > 0 && _sessionStats.MaxFan1Rpm > 0)
                        ? string.Format("APU • MAX {0}", _sessionStats.MaxFan1Rpm)
                        : "APU FAN";
                    if (_fan1BoxTitle.Text != fan1Header) _fan1BoxTitle.Text = fan1Header;
                }
                if (_uiToolTip != null && !IsBoxHovered(_fan1Box, _fan1BoxTitle, _fan1RpmLabel))
                {
                    string f1Tip = st.FormatApuFanTooltip(_sessionStats, FanCurveProfile.ApuMaxRpm);
                    if (f1Tip != _lastApuFanTooltipText)
                    {
                        _lastApuFanTooltipText = f1Tip;
                        _uiToolTip.SetToolTip(_fan1Box, f1Tip);
                        _uiToolTip.SetToolTip(_fan1BoxTitle, f1Tip);
                        _uiToolTip.SetToolTip(_fan1RpmLabel, f1Tip);
                    }
                }
                _fan1Box.Invalidate();
            }

            if (_fan2Box != null)
            {
                if (_fan2BoxTitle != null)
                {
                    string fan2Header = !_hasDualFans
                        ? "SYS FAN"
                        : ((_sessionStats.SampleCount > 0 && _sessionStats.MaxFan2Rpm > 0)
                            ? string.Format("SYS • MAX {0}", _sessionStats.MaxFan2Rpm)
                            : "SYS FAN");
                    if (_fan2BoxTitle.Text != fan2Header) _fan2BoxTitle.Text = fan2Header;
                }
                if (_uiToolTip != null && !IsBoxHovered(_fan2Box, _fan2BoxTitle, _fan2RpmLabel))
                {
                    string f2Tip = st.FormatSysFanTooltip(_sessionStats, FanCurveProfile.SysMaxRpm, _hasDualFans);
                    if (f2Tip != _lastSysFanTooltipText)
                    {
                        _lastSysFanTooltipText = f2Tip;
                        _uiToolTip.SetToolTip(_fan2Box, f2Tip);
                        _uiToolTip.SetToolTip(_fan2BoxTitle, f2Tip);
                        _uiToolTip.SetToolTip(_fan2RpmLabel, f2Tip);
                    }
                }
                _fan2Box.Invalidate();
            }

            if (!_hasDualFans || apuModeName == sysModeName)
                _modeLabel.Text = apuModeName;
            else
                _modeLabel.Text = string.Format("{0} / {1}", apuModeName, sysModeName);

            UpdateCurveTabStyles();
            UpdateModeButtonStyles(st.ModeCode);

            Color autoColor = _isDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x15, 0x80, 0x3D);
            Color manColor = isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : (_isDarkMode ? DarkAccent : LightAccent);

            if (_apuManualOverride)
            {
                _pct1Label.Text = string.Format("{0}% (Man)", st.Fan1Pct);
                _pct1Label.ForeColor = manColor;
            }
            else
            {
                int apuEval = _apuProfile != null ? _apuProfile.EvaluateApu(apuTemp) : st.Fan1Pct;
                _pct1Label.Text = (st.Fan1Manual == 0) ? string.Format("Auto ({0})", ShortModeTag(apuModeName)) : string.Format("{0}% ({1})", apuEval, ShortModeTag(apuModeName));
                _pct1Label.ForeColor = autoColor;
            }

            if (!_hasDualFans)
            {
                _pct2Label.Text = "Disabled";
                _pct2Label.ForeColor = dimText;
            }
            else if (_sysManualOverride)
            {
                _pct2Label.Text = string.Format("{0}% (Man)", st.Fan2Pct);
                _pct2Label.ForeColor = manColor;
            }
            else
            {
                int sysEval = _sysProfile != null ? _sysProfile.EvaluateSys(apuTemp) : st.Fan2Pct;
                _pct2Label.Text = (st.Fan2Manual == 0) ? string.Format("Auto ({0})", ShortModeTag(sysModeName)) : string.Format("{0}% ({1})", sysEval, ShortModeTag(sysModeName));
                _pct2Label.ForeColor = autoColor;
            }

            if (_emergencyThermalActive)
            {
                _statusBarLabel.ForeColor = Color.FromArgb(0xFB, 0x71, 0x85);
                _statusBarLabel.Text = string.Format("🔥 EMERGENCY THERMAL OVERRIDE (CPU {0}°C) — Fans at 100%", apuTemp);
            }
            else if (DateTime.UtcNow < _rampUntil)
            {
                double rem = Math.Max(0.1, (_rampUntil - DateTime.UtcNow).TotalSeconds);
                _statusBarLabel.ForeColor = rampColor;
                _statusBarLabel.Text = string.Format("⏳ {0} (ramping {1:0}s…)", _pendingActionText ?? "Adjusting", Math.Ceiling(rem));
            }
            else
            {
                string prefix = _demoMode ? "⚡ DEMO MODE" : "✓ HARDWARE OK";
                _statusBarLabel.ForeColor = dimText;
                _statusBarLabel.Text = string.Format("{0}  |  {1:HH:mm:ss}", prefix, DateTime.Now);
            }
        }

        private void RefreshStatus()
        {
            _hwWakeEvent.Set();
        }

        private static string ShortModeTag(string modeName)
        {
            if (string.IsNullOrEmpty(modeName)) return "Auto";
            if (modeName.StartsWith("Balanced", StringComparison.OrdinalIgnoreCase)) return "Bal";
            if (modeName.StartsWith("Performance", StringComparison.OrdinalIgnoreCase)) return "Perf";
            if (modeName.StartsWith("Silent", StringComparison.OrdinalIgnoreCase)) return "Sil";
            return modeName.Length > 6 ? modeName.Substring(0, 6) : modeName;
        }

        private void ApplyMode(int code)
        {
            string[] names = { "Balanced", "Performance", "Silent" };
            if (code < 0 || code >= _builtInProfiles.Length) return;

            try
            {
                bool editSys = _curveCanvas != null && _curveCanvas.EditSysFan;
                if (editSys)
                {
                    _sysProfile = _builtInProfiles[code];
                    _sysManualOverride = false;
                    _lastAppliedSysPct = -1;
                }
                else
                {
                    _apuProfile = _builtInProfiles[code];
                    _apuManualOverride = false;
                    _lastAppliedApuPct = -1;
                }
                _lastAppliedEcMode = -1;

                if (_curveCanvas != null)
                {
                    _curveCanvas.ApuProfile = _apuProfile;
                    _curveCanvas.SysProfile = _sysProfile;
                    _curveCanvas.Invalidate();
                }
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(code);
                UpdateCurveHint();
                SaveSettings();

                string fanLabel = editSys ? "Sys Fan" : "APU Fan";
                StartRampIndication(fanLabel + " → " + names[code], editSys ? 2 : 1);
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void ApplyFan(int fanId, int pct)
        {
            try
            {
                if (fanId == 1)
                {
                    _apuManualPct = Math.Max(0, Math.Min(100, pct));
                    _apuManualOverride = (_apuManualPct > 0);
                    _lastAppliedApuPct = -1;
                }
                else
                {
                    _sysManualPct = Math.Max(0, Math.Min(100, pct));
                    _sysManualOverride = (_sysManualPct > 0);
                    _lastAppliedSysPct = -1;
                }
                _lastAppliedEcMode = -1;

                UpdateCurveTabStyles();
                UpdateModeButtonStyles(_lastSelectedModeCode);
                UpdateCurveHint();
                SaveSettings();

                if (_demoMode) _demo.SetFan(fanId, pct);
                else _hw.SetFan(fanId, pct);
                string fanLabel = fanId == 1 ? "APU Fan" : "Sys Fan";
                string desc = pct == 0 ? string.Format("{0} → {1}", fanLabel, (fanId == 1 ? _apuProfile : _sysProfile).Name) : string.Format("{0} → {1}%", fanLabel, pct);
                StartRampIndication(desc, fanId);
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void ApplyAutoAll()
        {
            try
            {
                _apuManualPct = 0;
                _sysManualPct = 0;
                _apuManualOverride = false;
                _sysManualOverride = false;
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                _lastAppliedEcMode = -1;
                _apuProfile = _builtInProfiles[0];
                _sysProfile = _builtInProfiles[0];
                if (_curveCanvas != null)
                {
                    _curveCanvas.ApuProfile = _apuProfile;
                    _curveCanvas.SysProfile = _sysProfile;
                    _curveCanvas.Invalidate();
                }
                UpdateCurveTabStyles();
                UpdateModeButtonStyles(0);
                UpdateCurveHint();
                SaveSettings();

                if (_demoMode) _demo.SetAuto();
                else _hw.SetAuto();
                StartRampIndication("Reset both fans → Balanced (Auto)", 0);
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private string GetAutostartPath()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startup, "MiniFanControl.bat");
        }

        private string GetLegacyAutostartPath()
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startup, "K12FanControl.bat");
        }

        private static bool RunSchTasksCommand(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    proc.WaitForExit(3000);
                    return proc.HasExited && proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsScheduledTaskAutostartEnabled()
        {
            return RunSchTasksCommand(string.Format("/Query /TN \"{0}\"", SCHTASK_NAME));
        }

        private bool IsAutostartEnabled()
        {
            if (File.Exists(GetAutostartPath()) || File.Exists(GetLegacyAutostartPath()))
                return true;
            return IsScheduledTaskAutostartEnabled();
        }

        private void ToggleAutostart()
        {
            string path = GetAutostartPath();
            string legacyPath = GetLegacyAutostartPath();
            try
            {
                if (_autostartCb.Checked)
                {
                    string exePath = Application.ExecutablePath;
                    // Try creating a UAC-free Elevated Windows Scheduled Task first (RadeonTuner pattern)
                    string createArgs = string.Format("/Create /F /RL HIGHEST /SC ONLOGON /TN \"{0}\" /TR \"\\\"{1}\\\" --minimized\"", SCHTASK_NAME, exePath);
                    bool taskCreated = RunSchTasksCommand(createArgs);
                    if (taskCreated)
                    {
                        if (File.Exists(path)) File.Delete(path);
                        if (File.Exists(legacyPath)) File.Delete(legacyPath);
                        _statusBarLabel.Text = "✓ Elevated Autostart enabled (UAC-free Scheduled Task)";
                    }
                    else
                    {
                        File.WriteAllText(path, string.Format("@echo off\r\nstart \"\" \"{0}\" --minimized\r\n", exePath));
                        if (File.Exists(legacyPath)) File.Delete(legacyPath);
                        _statusBarLabel.Text = "✓ Autostart enabled (Startup folder)";
                    }
                }
                else
                {
                    RunSchTasksCommand(string.Format("/Delete /F /TN \"{0}\"", SCHTASK_NAME));
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(legacyPath)) File.Delete(legacyPath);
                    _statusBarLabel.Text = "✓ Autostart disabled";
                }
            }
            catch (Exception ex)
            {
                _errorBanner.Text = "⚠ Autostart error: " + ex.Message;
                _errorBanner.Visible = true;
            }
        }

        private void MinimizeToTray()
        {
            if (!_showCpuTray && !_showGpuTray && _cpuTrayIcon != null)
            {
                _cpuTrayIcon.Visible = true;
            }
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _trayCb.Checked = false;
            if (_cpuTrayIcon != null) _cpuTrayIcon.Visible = _showCpuTray;
            if (_gpuTrayIcon != null) _gpuTrayIcon.Visible = _showGpuTray;
            RefreshStatus();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_sessionEndingHandler != null) { SystemEvents.SessionEnding -= _sessionEndingHandler; _sessionEndingHandler = null; }
            if (_powerModeChangedHandler != null) { SystemEvents.PowerModeChanged -= _powerModeChangedHandler; _powerModeChangedHandler = null; }
            StopWatchdogSupervisorGracefully();
            _hwThreadRunning = false;
            _hwWakeEvent.Set();
            if (_hwThread != null && _hwThread.IsAlive)
            {
                _hwThread.Join(500);
            }
            AppDomain.CurrentDomain.ProcessExit -= OnProcessOrCrashExit;
            AppDomain.CurrentDomain.UnhandledException -= OnProcessOrCrashExit;
            UnregisterGlobalHotkeys();
            if (_refreshTimer != null)
                _refreshTimer.Stop();
            RestoreHardwareSafeAutoIfNeeded();
            if (_headerLogoBox != null && _headerLogoBox.Image != null)
            {
                _headerLogoBox.Image.Dispose();
                _headerLogoBox.Image = null;
            }
            if (_cpuTrayIcon != null)
            {
                _cpuTrayIcon.Visible = false;
                _cpuTrayIcon.Dispose();
            }
            if (_gpuTrayIcon != null)
            {
                _gpuTrayIcon.Visible = false;
                _gpuTrayIcon.Dispose();
            }
            if (_cpuIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_cpuIconHandle);
                _cpuIconHandle = IntPtr.Zero;
            }
            if (_gpuIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_gpuIconHandle);
                _gpuIconHandle = IntPtr.Zero;
            }
            if (_overlayForm != null)
            {
                _overlayForm.Close();
                _overlayForm.Dispose();
                _overlayForm = null;
            }
            _hw.Dispose();
        }
    }

    public static class SelfTestSuite
    {
        private static int _passed = 0;
        private static int _failed = 0;

        private static void Check(bool condition, string name, string detail)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  [PASS] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  [FAIL] " + name + " -> " + detail);
            }
        }

        public static int RunAll()
        {
            _passed = 0;
            _failed = 0;
            Console.WriteLine("======================================================================");
            Console.WriteLine("  MiniFanControl - Automated Hardware Safety & Curve Test Suite");
            Console.WriteLine("======================================================================");

            // 1. Motor Dead-Zone / Stall Protection (1%..29% is forbidden across 0..105C)
            CurvePoint[] balApu = new CurvePoint[]
            {
                new CurvePoint(40, 0), new CurvePoint(48, 35), new CurvePoint(65, 55), new CurvePoint(78, 80), new CurvePoint(88, 100)
            };
            CurvePoint[] silApu = new CurvePoint[]
            {
                new CurvePoint(48, 0), new CurvePoint(56, 30), new CurvePoint(70, 45), new CurvePoint(82, 70), new CurvePoint(92, 100)
            };
            bool deadZoneViolated = false;
            int badTemp = -1, badPct = -1;
            for (int t = 0; t <= 105; t++)
            {
                int p1 = FanCurveProfile.EvaluateCurve(balApu, t);
                int p2 = FanCurveProfile.EvaluateCurve(silApu, t);
                if ((p1 >= 1 && p1 < FanCurveProfile.MIN_SPIN_PCT) || (p2 >= 1 && p2 < FanCurveProfile.MIN_SPIN_PCT))
                {
                    deadZoneViolated = true;
                    badTemp = t;
                    badPct = p1 >= 1 && p1 < FanCurveProfile.MIN_SPIN_PCT ? p1 : p2;
                    break;
                }
            }
            Check(!deadZoneViolated,
                "Motor Stall Dead-Zone Protection (never outputs 1%..29% across 0..105C)",
                string.Format("Violated at {0}C with {1}%", badTemp, badPct));

            // 2. Specific 47C Balanced interpolation regression test (previously produced 24% & stalled)
            int at47C = FanCurveProfile.EvaluateCurve(balApu, 47);
            Check(at47C >= FanCurveProfile.MIN_SPIN_PCT,
                "47C Balanced Curve Stall Regression Test (>= 30% PWM)",
                "Got " + at47C + "%");

            // 3. Zero-RPM Idle segment test (flat 0% segment stays 0%, then steps cleanly to >= 30% past midpoint)
            CurvePoint[] flatZeroCurve = new CurvePoint[]
            {
                new CurvePoint(30, 0), new CurvePoint(45, 0), new CurvePoint(55, 35), new CurvePoint(85, 100)
            };
            int idle35 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 35);
            int idle45 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 45);
            int ramp51 = FanCurveProfile.EvaluateCurve(flatZeroCurve, 51);
            Check(idle35 == 0 && idle45 == 0 && ramp51 >= FanCurveProfile.MIN_SPIN_PCT,
                "Zero-RPM Idle Segment & Clean Step-Up Transition (0% <= 45C, >= 30% at 51C)",
                string.Format("35C={0}%, 45C={1}%, 51C={2}%", idle35, idle45, ramp51));

            // 4. Curve Monotonicity & Boundary Clamping
            int lowClamp = FanCurveProfile.EvaluateCurve(balApu, 15);
            int highClamp = FanCurveProfile.EvaluateCurve(balApu, 99);
            Check(lowClamp == 0 && highClamp == 100,
                "Curve Boundary Clamping (15C -> 0%, 99C -> 100%)",
                string.Format("low={0}%, high={1}%", lowClamp, highClamp));

            // 5. Tachometer Glitch & Motor Stop Filter (8000 raw -> 0 RPM)
            int rpmStall8000 = HardwareBackend.DecodeTachometerRpm(0x1F, 0x40); // 8000
            int rpmGlitchFFFF = HardwareBackend.DecodeTachometerRpm(0xFF, 0xFF); // 65535
            int rpmValid592 = HardwareBackend.DecodeTachometerRpm(0x02, 0x50); // 592
            int rpmValid3150 = HardwareBackend.DecodeTachometerRpm(0x0C, 0x4E); // 3150
            Check(rpmStall8000 == 0 && rpmGlitchFFFF == 0 && rpmValid592 == 592 && rpmValid3150 == 3150,
                "Tachometer Stall Filter (8000 -> 0 RPM, 65535 -> 0 RPM, 592 & 3150 preserved)",
                string.Format("8000->{0}, FFFF->{1}, 592->{2}, 3150->{3}", rpmStall8000, rpmGlitchFFFF, rpmValid592, rpmValid3150));

            // 6. EC Mode & Fan Control Register Bitmask Encoding
            byte mBal = HardwareBackend.EncodeModeCommand(0);
            byte mPerf = HardwareBackend.EncodeModeCommand(1);
            byte mSil = HardwareBackend.EncodeModeCommand(2);
            byte fAuto = HardwareBackend.EncodeFanCommand(0);
            byte fMin = HardwareBackend.EncodeFanCommand(1);
            byte f50 = HardwareBackend.EncodeFanCommand(50);
            byte f100 = HardwareBackend.EncodeFanCommand(100);
            bool boundsThrew = false;
            try { HardwareBackend.EncodeFanCommand(105); } catch (ArgumentException) { boundsThrew = true; }
            Check(mBal == 0x80 && mPerf == 0x81 && mSil == 0x82 &&
                  fAuto == 0x00 && fMin == 0x81 && f50 == (0x80 | 50) && f100 == (0x80 | 100) && boundsThrew,
                "EC Register Bitmask Encoding & Safety Bounds (0x32..0x34)",
                "Unexpected register encoding");

            // 7. Custom Curve Serialization / Deserialization Round-Trip
            string serialized = MainForm.FormatPointList(balApu);
            CurvePoint[] parsed = MainForm.ParsePointList(serialized);
            bool roundTripOk = (parsed.Length == balApu.Length);
            for (int i = 0; i < parsed.Length && roundTripOk; i++)
            {
                if (parsed[i].Temp != balApu[i].Temp || parsed[i].Pct != balApu[i].Pct)
                    roundTripOk = false;
            }
            Check(roundTripOk,
                "Custom Curve Config Save/Load Round-Trip (FormatPointList <-> ParsePointList)",
                "Serialized: " + serialized);

            // 8. Multi-Model Ryzen Mini-PC Hardware Profile Detection
            DeviceProfile pK12 = SystemHardwareInfo.MatchProfile("NucBox K12", "GMKtec", "AMD Ryzen 7 H 255");
            DeviceProfile pK8 = SystemHardwareInfo.MatchProfile("NucBox K8 Plus", "GMKtec", "AMD Ryzen 7 8845HS");
            DeviceProfile pHx370 = SystemHardwareInfo.MatchProfile("EVO-X1", "Kthin", "AMD Ryzen AI 9 HX 370");
            DeviceProfile pAdreamer = SystemHardwareInfo.MatchProfile("AriesBox 10", "ADREAMER", "AMD Ryzen 7 7840HS");
            Check(pK12.Id == "gmktec-k12" && pK8.Id == "gmktec-k8-k11" && pHx370.Id == "ryzen-hx370-ec" && pAdreamer.Id == "adreamer-ariesbox10",
                "Multi-Model Ryzen Mini-PC Profile Matching (K12 / K8 Plus / HX 370 / AriesBox 10)",
                string.Format("K12={0}, K8={1}, HX370={2}, AriesBox={3}", pK12.Id, pK8.Id, pHx370.Id, pAdreamer.Id));

            // 9. Demo/Simulation Backend State Transitions
            DemoBackend demo = new DemoBackend();
            demo.SetMode(2);
            demo.SetFan(1, 65);
            demo.SetFan(2, 40);
            FanStatus stMan = demo.GetStatus();
            demo.SetAuto();
            FanStatus stAuto = demo.GetStatus();
            Check(stMan.ModeCode == 2 && stMan.Fan1Manual == 1 && stMan.Fan1Pct == 65 && stMan.Fan2Pct == 40 &&
                  stAuto.ModeCode == 0 && stAuto.Fan1Manual == 0 && stAuto.Fan2Manual == 0,
                "Simulation Backend State Machine (Manual Override -> Reset Auto)",
                "DemoBackend state mismatch");

            // 10. Smart EC Candidate Scanner & Dynamic Config Register Override
            byte[] mockEc = new byte[256];
            mockEc[0x31] = 0;    // Mode Balanced
            mockEc[0x35] = 0x05; // Fan 1 = 0x05DC (1500 RPM)
            mockEc[0x36] = 0xDC;
            mockEc[0x37] = 0x1F; // Fan 2 = 0x1F40 (8000 Idle)
            mockEc[0x38] = 0x40;
            mockEc[0x70] = 47;   // CPU Temp = 47C
            string scanReport = SystemHardwareInfo.AnalyzeEcCandidates(mockEc);
            byte parsedHex, parsedDec;
            bool hexOk = MainForm.TryParseHexOrDecByte("0x70", out parsedHex) && parsedHex == 0x70;
            bool decOk = MainForm.TryParseHexOrDecByte("112", out parsedDec) && parsedDec == 112;
            Check(scanReport.Contains("[OK] Standard GMKtec") && scanReport.Contains("0x70=47") && scanReport.Contains("0x35..36=1500 RPM") && hexOk && decOk,
                "Smart EC Candidate Scanner & Dynamic Register Override Parsing",
                "Candidate scan or hex parsing failed");

            // 11. 3-Pass Fan Calibration Engine, Repeatability Verification, Date Tracking & Anti-Stall Safety Margin
            int progressCallbacks = 0;
            FanCalibrationResult calRes = FanCalibrationResult.RunCalibration(
                null, demo, true, 0, 3,
                prog => { progressCallbacks++; },
                null);
            int customFloorEval = FanCurveProfile.EvaluateCurve(balApu, 47, calRes.ApuMinSpinPct);
            bool isCalFresh, isStaleFresh, isCalOld, isStaleOld;
            FanCurveProfile.ApuMinSpinPct = calRes.ApuMinSpinPct;
            FanCurveProfile.SysMinSpinPct = calRes.SysMinSpinPct;
            FanCurveProfile.LastCalibratedDate = calRes.CalibratedAt;
            string freshBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalFresh, out isStaleFresh);
            FanCurveProfile.LastCalibratedDate = "2024-01-15 12:00";
            string staleBadge = FanCurveProfile.FormatCalibrationStatusBadge(true, out isCalOld, out isStaleOld);
            Check(calRes.HasDualFans &&
                  calRes.PassesCompleted == 3 &&
                  calRes.PassApuMin.Length == 3 &&
                  calRes.PassApuMin[0] == 24 && calRes.PassApuMin[1] == 24 && calRes.PassApuMin[2] == 24 &&
                  calRes.ApuBareSpinPct == 24 &&
                  calRes.ApuMinSpinPct == 26 &&
                  calRes.ApuMaxRpm >= 3000 &&
                  progressCallbacks >= 30 &&
                  customFloorEval >= 26 &&
                  isCalFresh && !isStaleFresh && freshBadge.Contains("Calibrated") &&
                  isCalOld && isStaleOld && staleBadge.Contains(">6m ago"),
                "3-Pass Fan Calibration Engine, Date Tracking & +2% Safety Margin (3 passes verified)",
                string.Format("Passes={0}, Bare={1}%, SafeMin={2}%, Badge={3}", calRes.PassesCompleted, calRes.ApuBareSpinPct, calRes.ApuMinSpinPct, freshBadge));

            // 12. CapFrameX Global\Access_EC Hardware Mutex & APU Power Telemetry + Breakdown Tooltip
            bool mutexLocked = EcBusMutex.WaitEc(50);
            if (mutexLocked) EcBusMutex.ReleaseEc();
            int estIdlePwr = HardwareBackend.EstimateApuPowerWatts(42, 36, 0);
            int estLoadPwr = HardwareBackend.EstimateApuPowerWatts(84, 76, 1);
            string pwrTipTest = stAuto.FormatPowerTooltip();
            Check(mutexLocked && estIdlePwr >= 6 && estIdlePwr <= 22 && estLoadPwr >= 45 && estLoadPwr <= 65 &&
                  stAuto.ApuPowerWatts > 0 && pwrTipTest.Contains("VDDCR_CPU") && pwrTipTest.Contains("VDDCR_GFX") && pwrTipTest.Contains("VDDCR_SOC"),
                "CapFrameX Global\\Access_EC Mutex & APU Power Breakdown Tooltip (W)",
                string.Format("Mutex={0}, IdleW={1}W, LoadW={2}W", mutexLocked, estIdlePwr, estLoadPwr));

            // 13. Session Peak/Min/Avg Tracking & Auto-Scaled Sparkline Telemetry
            TelemetrySessionStats stats = new TelemetrySessionStats();
            stats.Record(new FanStatus { CpuTemp = 42, GpuTemp = 37, ApuPowerWatts = 8, Fan1Rpm = 1400, Fan2Rpm = 650 });
            stats.Record(new FanStatus { CpuTemp = 64, GpuTemp = 38, ApuPowerWatts = 28, Fan1Rpm = 2100, Fan2Rpm = 1200 });
            stats.Record(new FanStatus { CpuTemp = 82, GpuTemp = 39, ApuPowerWatts = 54, Fan1Rpm = 3100, Fan2Rpm = 1850 });
            string cpuSpark = stats.GetCpuSparkline();
            string gpuSpark = stats.GetGpuSparkline();
            string pwrSpark = stats.GetPowerSparkline();
            string fan1Spark = stats.GetFan1Sparkline();
            string fan2Spark = stats.GetFan2Sparkline();
            string enrichedPwrTip = stAuto.FormatPowerTooltip(stats);
            string enrichedCpuTip = stAuto.FormatCpuTooltip(stats);
            string enrichedFan1Tip = stAuto.FormatApuFanTooltip(stats, 3200);
            string enrichedFan2Tip = stAuto.FormatSysFanTooltip(stats, 1900, true);
            Check(stats.SampleCount == 3 &&
                  stats.MinCpuTemp == 42 && stats.MaxCpuTemp == 82 && Math.Abs(stats.AvgCpuTemp - 62.67) < 0.05 &&
                  stats.MinPowerWatts == 8 && stats.MaxPowerWatts == 54 &&
                  stats.MinFan1Rpm == 1400 && stats.MaxFan1Rpm == 3100 &&
                  stats.MinFan2Rpm == 650 && stats.MaxFan2Rpm == 1850 &&
                  stats.Fan1RpmHistory.Count == 3 && stats.Fan2RpmHistory.Count == 3 &&
                  cpuSpark.Length == 3 && pwrSpark.Length == 3 && fan1Spark.Length == 3 && fan2Spark.Length == 3 &&
                  gpuSpark[0] != gpuSpark[2] &&
                  enrichedPwrTip.Contains("Session Power (Min / Avg / Peak)") &&
                  enrichedCpuTip.Contains("Session CPU Temp (Min / Avg / Peak)") &&
                  enrichedFan1Tip.Contains("Session APU Fan") &&
                  enrichedFan2Tip.Contains("Session Sys Fan"),
                "Session Peak/Min/Avg Telemetry & Multi-Series Live Graph History",
                string.Format("Samples={0}, MaxCPU={1}C, MaxPwr={2}W, MaxF1={3}RPM, MaxF2={4}RPM", stats.SampleCount, stats.MaxCpuTemp, stats.MaxPowerWatts, stats.MaxFan1Rpm, stats.MaxFan2Rpm));

            // 14. CapFrameX OSD Multi-Tier Sizing, Live Graphs & Layout Modes
            using (CapFrameXOverlayForm osd = new CapFrameXOverlayForm())
            {
                osd.Stats = stats;
                osd.OsdScaleTier = 0; // 100%
                osd.GraphMode = 0; // Text only
                osd.VerticalLayout = false;
                osd.UpdateDimensions();
                int hText = osd.Height;
                int wTier0 = osd.Width;

                osd.GraphMode = 1; // Sparklines
                osd.UpdateDimensions();
                int hSpark = osd.Height;

                osd.GraphMode = 2; // Combined
                osd.GraphHeight = 45;
                osd.UpdateDimensions();
                int hComb45 = osd.Height;

                osd.GraphHeight = 130;
                osd.UpdateDimensions();
                int hComb130 = osd.Height;

                osd.GraphMode = 3; // Both
                osd.GraphHeight = 65;
                osd.UpdateDimensions();
                int hBoth = osd.Height;

                // Test multi-tier scale sizing (100% -> 125% -> 150% -> 185%)
                osd.OsdScaleTier = 1; osd.UpdateDimensions(); int wTier1 = osd.Width;
                osd.OsdScaleTier = 2; osd.UpdateDimensions(); int wTier2 = osd.Width;
                osd.OsdScaleTier = 3; osd.UpdateDimensions(); int wTier3 = osd.Width;

                osd.VerticalLayout = true;
                osd.UpdateDimensions();
                int hVertBoth = osd.Height;

                Check(hSpark > hText && hComb130 > hComb45 && hBoth > hText && hVertBoth > 0 &&
                      wTier1 > wTier0 && wTier2 > wTier1 && wTier3 > wTier2 &&
                      osd.CountVisibleMetrics() >= 4,
                    "CapFrameX OSD Multi-Tier Sizing (100%-185%), Live Graphs & Layout Modes",
                    string.Format("w100={0}, w125={1}, w150={2}, w185={3}, h45={4}, h130={5}", wTier0, wTier1, wTier2, wTier3, hComb45, hComb130));
            }

            // 15. Fail-Closed EcBusMutex Contention & Multi-Mutex Lock Scope
            bool contentionDetected = false;
            int initialContention = EcBusMutex.ContentionCount;
            Thread holderThread = new Thread(() =>
            {
                using (var holder = EcBusMutex.AcquireEc(500))
                {
                    Thread.Sleep(150);
                }
            });
            holderThread.IsBackground = true;
            holderThread.Start();
            Thread.Sleep(30);

            using (var contested = EcBusMutex.AcquireEc(15))
            {
                contentionDetected = !contested.Acquired && (EcBusMutex.ContentionCount > initialContention);
            }
            holderThread.Join(500);
            Check(contentionDetected,
                "Fail-Closed EcBusMutex Contention & Safe Mutex Isolation",
                "Contention was not flagged or unacquired lock returned true");

            // 16. Emergency Thermal Safety Tripwire & Cooldown Hysteresis
            bool emergencyState = false;
            bool tripBelow = HardwareBackend.CheckEmergencyThermalTripwire(84, ref emergencyState);
            bool tripAtTrip = HardwareBackend.CheckEmergencyThermalTripwire(90, ref emergencyState);
            bool tripInHysteresis = HardwareBackend.CheckEmergencyThermalTripwire(82, ref emergencyState);
            bool tripRecovered = HardwareBackend.CheckEmergencyThermalTripwire(74, ref emergencyState);
            Check(!tripBelow && tripAtTrip && tripInHysteresis && !tripRecovered,
                "Emergency Thermal Safety Tripwire & Cooldown Hysteresis (90C Trip / 75C Recover)",
                string.Format("84C={0}, 90C={1}, 82C={2}, 74C={3}", tripBelow, tripAtTrip, tripInHysteresis, tripRecovered));

            // 17. Safe Fail-Closed Hardware Probe Validation
            bool invalidBaseVerify = HardwareBackend.VerifyIt87HwBase(0x0100);
            HardwareBackend unverifiedHw = new HardwareBackend();
            Check(!invalidBaseVerify && unverifiedHw.AccessMethod == HardwareBackend.EcAccessMethod.Unknown &&
                  unverifiedHw.AccessMethodName.Contains("Safe Read-Only"),
                "Safe Fail-Closed Hardware Probe Validation (No Blind PortIO Fallback)",
                "Unverified backend did not report Unknown / Safe Read-Only");

            // 18. Hardware Watchdog Supervisor Lifecycle (Clean Exit, Heartbeat Hang & Crash Failsafe)
            bool testHbCreated, testSdCreated;
            int currentPid = Process.GetCurrentProcess().Id;
            string hbName = "MiniFanControl_TestHb_" + currentPid;
            string sdName = "MiniFanControl_TestSd_" + currentPid;
            using (EventWaitHandle hb = WatchdogSupervisor.CreateOrOpenEvent(hbName, EventResetMode.AutoReset, out testHbCreated))
            using (EventWaitHandle sd = WatchdogSupervisor.CreateOrOpenEvent(sdName, EventResetMode.ManualReset, out testSdCreated))
            {
                // A: Test Clean Shutdown signal detection
                sd.Set();
                int resClean = WatchdogSupervisor.RunSimulated(currentPid, 300, hb, sd, true);
                sd.Reset();

                // B: Test Heartbeat Timeout detection (no heartbeat sent -> timeout triggers failsafe)
                int resTimeout = WatchdogSupervisor.RunSimulated(currentPid, 120, hb, sd, false);

                // C: Test Heartbeat Sustained (simulate 2 pulses then clean shutdown)
                Thread pulseThread = new Thread(() =>
                {
                    Thread.Sleep(30);
                    hb.Set();
                    Thread.Sleep(30);
                    hb.Set();
                    Thread.Sleep(30);
                    sd.Set();
                });
                pulseThread.IsBackground = true;
                pulseThread.Start();
                int resSustained = WatchdogSupervisor.RunSimulated(currentPid, 200, hb, sd, false);
                pulseThread.Join(500);

                // D: Test Unexpected Exit detection (simulated crash / kill)
                Process dummy = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false });
                dummy.WaitForExit(1000);
                sd.Reset();
                int resCrash = WatchdogSupervisor.ExecuteLoop(dummy, dummy.Id, 500, hb, sd, true, true);
                dummy.Dispose();

                Check(testHbCreated && testSdCreated && resClean == 0 && resTimeout == 3 && resSustained == 0 && resCrash == 2,
                    "Hardware Watchdog Supervisor Lifecycle (Clean Exit, Heartbeat Hang & Crash Failsafe)",
                    string.Format("Clean={0}, Timeout={1}, Sustained={2}, Crash={3}", resClean, resTimeout, resSustained, resCrash));
            }

            // 19. Driver Security Port Whitelist, DLL Search Order & Memory Map Safety Bounds
            bool port62Allow = HardwareBackend.IsPortWhitelisted(0x62, true);
            bool port66Allow = HardwareBackend.IsPortWhitelisted(0x66, true);
            bool port2EAllow = HardwareBackend.IsPortWhitelisted(0x2E, true);
            bool port0A35Allow = HardwareBackend.IsPortWhitelisted(0x0A35, true);

            bool port70Blocked = !HardwareBackend.IsPortWhitelisted(0x70, true);
            bool portCF9Blocked = !HardwareBackend.IsPortWhitelisted(0xCF9, true);
            bool port20Blocked = !HardwareBackend.IsPortWhitelisted(0x20, true);
            bool port40Blocked = !HardwareBackend.IsPortWhitelisted(0x40, true);
            bool port61Blocked = !HardwareBackend.IsPortWhitelisted(0x61, true);

            IntPtr dummyH;
            IntPtr lowMemMap = NativeInpOut.MapPhysToLinSafe(new IntPtr(0x1000), 4096, out dummyH);
            IntPtr hugeMemMap = NativeInpOut.MapPhysToLinSafe(new IntPtr(0xFED00000), 32 * 1024 * 1024, out dummyH);
            bool dllPreloadOk = NativeInpOut.PreloadDriverDll();

            Check(port62Allow && port66Allow && port2EAllow && port0A35Allow &&
                  port70Blocked && portCF9Blocked && port20Blocked && port40Blocked && port61Blocked &&
                  lowMemMap == IntPtr.Zero && hugeMemMap == IntPtr.Zero,
                "Driver Security Port Whitelist & Memory Map Safety Bounds",
                string.Format("Allow=[62:{0},2E:{1},A35:{2}], Block=[70:{3},CF9:{4},20:{5}], SafeMap=[Low:{6},Huge:{7}]",
                    port62Allow, port2EAllow, port0A35Allow, port70Blocked, portCF9Blocked, port20Blocked, lowMemMap == IntPtr.Zero, hugeMemMap == IntPtr.Zero));

            Console.WriteLine("----------------------------------------------------------------------");
            Console.WriteLine(string.Format("  Result: {0} passed, {1} failed", _passed, _failed));
            Console.WriteLine("======================================================================");
            return _failed == 0 ? 0 : 1;
        }
    }

    public static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            NativeInpOut.PreloadDriverDll();
            bool demoMode = false;
            bool startMinimized = false;
            string cmd = null;
            string cmdArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--demo" || a == "--simulate")
                {
                    demoMode = true;
                }
                else if (a == "--minimized" || a == "--tray" || a == "-m")
                {
                    startMinimized = true;
                }
                else if (cmd == null)
                {
                    cmd = a;
                }
                else if (cmdArg == null)
                {
                    cmdArg = a;
                }
            }

            if (!string.IsNullOrEmpty(cmd))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                if (cmd == "test" || cmd == "--test")
                {
                    return SelfTestSuite.RunAll();
                }
                if (cmd == "watchdog")
                {
                    int pid = 0;
                    int timeoutMs = WatchdogSupervisor.DEFAULT_TIMEOUT_MS;
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "--pid" && i + 1 < args.Length)
                        {
                            int.TryParse(args[i + 1], out pid);
                        }
                        else if (args[i] == "--timeout" && i + 1 < args.Length)
                        {
                            int.TryParse(args[i + 1], out timeoutMs);
                        }
                    }
                    if (pid <= 0)
                    {
                        Console.Error.WriteLine("Error: --pid <parentPid> is required for watchdog mode.");
                        return 1;
                    }
                    return WatchdogSupervisor.Run(pid, timeoutMs, demoMode);
                }
                if (cmd == "driver-status")
                {
                    bool isRunning = DriverServiceManager.IsDriverServiceRunning("inpoutx64");
                    Console.WriteLine("{{\"service\":\"inpoutx64\",\"running\":{0}}}", isRunning ? "true" : "false");
                    return 0;
                }
                else if (cmd == "driver-stop")
                {
                    bool stopped = DriverServiceManager.StopDriverService("inpoutx64");
                    Console.WriteLine("{{\"service\":\"inpoutx64\",\"action\":\"stop\",\"success\":{0}}}", stopped ? "true" : "false");
                    return 0;
                }
                if (cmd == "gen-icon" || cmd == "--gen-icon")
                {
                    int conceptId = 1;
                    int parsedConcept;
                    if (!string.IsNullOrEmpty(cmdArg) && int.TryParse(cmdArg, out parsedConcept) && parsedConcept >= 1 && parsedConcept <= 3)
                    {
                        conceptId = parsedConcept;
                    }
                    string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                    string logoPngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                    string showcasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo-concepts.png");
                    AppLogoRenderer.WriteMultiResIco(icoPath, conceptId);
                    using (Bitmap hiResLogo = AppLogoRenderer.RenderConcept(conceptId, 512))
                    {
                        hiResLogo.Save(logoPngPath, ImageFormat.Png);
                    }
                    AppLogoRenderer.WriteConceptShowcasePng(showcasePath);
                    Console.WriteLine(string.Format("[OK] Generated multi-res icon (Option #{0}): {1}", conceptId, icoPath));
                    Console.WriteLine(string.Format("[OK] Generated 512x512 logo PNG (Option #{0}): {1}", conceptId, logoPngPath));
                    Console.WriteLine(string.Format("[OK] Generated 3-concept logo sheet: {0}", showcasePath));
                    return 0;
                }
                try
                {
                    using (HardwareBackend hw = new HardwareBackend())
                    {
                        DemoBackend demo = new DemoBackend();
                        if (cmd == "status")
                        {
                            FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                            Console.WriteLine(
                                "{{\"cpu_temp\":{0},\"gpu_temp\":{1},\"apu_power_w\":{2},\"fan1_rpm\":{3},\"fan2_rpm\":{4}," +
                                "\"mode\":\"{5}\",\"mode_code\":{6},\"fan1_pct\":{7},\"fan2_pct\":{8}," +
                                "\"fan1_manual\":{9},\"fan2_manual\":{10}}}",
                                st.CpuTemp, st.GpuTemp, st.ApuPowerWatts, st.Fan1Rpm, st.Fan2Rpm,
                                st.Mode, st.ModeCode, st.Fan1Pct, st.Fan2Pct,
                                st.Fan1Manual, st.Fan2Manual);
                            return 0;
                        }
                        else if (cmd == "mode")
                        {
                            int code = int.Parse(cmdArg ?? "-1");
                            if (demoMode) demo.SetMode(code); else hw.SetMode(code);
                            Console.WriteLine("{{\"ok\":true,\"mode_code\":{0}}}", code);
                            return 0;
                        }
                        else if (cmd == "fan1" || cmd == "fan2")
                        {
                            int fanId = (cmd == "fan1") ? 1 : 2;
                            int pct = int.Parse(cmdArg ?? "-1");
                            if (demoMode) demo.SetFan(fanId, pct); else hw.SetFan(fanId, pct);
                            if (pct == 0)
                                Console.WriteLine("{{\"ok\":true,\"fan\":{0},\"mode\":\"auto\"}}", fanId);
                            else
                                Console.WriteLine("{{\"ok\":true,\"fan\":{0},\"pct\":{1},\"mode\":\"manual\"}}", fanId, pct);
                            return 0;
                        }
                        else if (cmd == "auto")
                        {
                            if (demoMode) demo.SetAuto(); else hw.SetAuto();
                            Console.WriteLine("{\"ok\":true,\"action\":\"auto\",\"mode\":\"Balanced\",\"fan1\":\"auto\",\"fan2\":\"auto\"}");
                            return 0;
                        }
                        else if (cmd == "dump" || cmd == "probe")
                        {
                            SystemHardwareInfo sysInfo = SystemHardwareInfo.Detect();
                            Console.WriteLine(sysInfo.BuildProbeReport(hw, demo, demoMode));
                            return 0;
                        }
                        else if (cmd == "calibrate")
                        {
                            int passes = 3;
                            if (!string.IsNullOrEmpty(cmdArg)) int.TryParse(cmdArg, out passes);
                            if (passes < 1 || passes > 5) passes = 3;
                            Console.WriteLine(string.Format("Running MiniFanControl {0}-pass real-time fan calibration...", passes));
                            FanCalibrationResult cal = FanCalibrationResult.RunCalibration(
                                hw, demo, demoMode,
                                demoMode ? 60 : 5000, // 5-second stabilization wait before fan measurement
                                passes,
                                prog =>
                                {
                                    if (!string.IsNullOrEmpty(prog.LogLine))
                                        Console.WriteLine(prog.LogLine);
                                },
                                null);
                            Console.WriteLine();
                            Console.WriteLine(cal.SummaryReport);
                            return 0;
                        }
                        else if (cmd == "watch" || cmd == "rgb-scan")
                        {
                            byte[] baseline = hw.ReadAllRegisters();
                            if (cmd == "rgb-scan")
                            {
                                Console.WriteLine("Initial lighting registers: KBLT(0x09)=0x{0:X2}, KBF(0x39)=0x{1:X2}, KBFL(0x3A)=0x{2:X2}, KBCH(0x3C)=0x{3:X2}, KBCL(0x3D)=0x{4:X2}",
                                    baseline[0x09], baseline[0x39], baseline[0x3A], baseline[0x3C], baseline[0x3D]);
                                Console.WriteLine("Watching all 255 EC registers for 12 seconds... Press the physical RGB button on the K12 now!");
                            }
                            else
                            {
                                Console.WriteLine("Watching all 255 EC registers for 15 seconds (change BIOS mode, fan speed, or load to identify registers)...");
                            }

                            DateTime until = DateTime.UtcNow.AddSeconds(cmd == "rgb-scan" ? 12 : 15);
                            bool anyChanged = false;
                            while (DateTime.UtcNow < until)
                            {
                                System.Threading.Thread.Sleep(150);
                                byte[] cur = hw.ReadAllRegisters();
                                for (int r = 0; r < cur.Length; r++)
                                {
                                    if (cmd == "rgb-scan")
                                    {
                                        if (r >= 0x35 && r <= 0x38) continue;
                                        if (r >= 0x70 && r <= 0x73) continue;
                                    }
                                    if (cur[r] != baseline[r])
                                    {
                                        Console.WriteLine("  [CHANGE] Register 0x{0:X2}: 0x{1:X2} -> 0x{2:X2}", r, baseline[r], cur[r]);
                                        baseline[r] = cur[r];
                                        anyChanged = true;
                                    }
                                }
                            }
                            if (!anyChanged)
                            {
                                Console.WriteLine("No EC register changed during the watch window.");
                            }
                            return 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("{{\"error\":\"{0}\"}}", ex.Message.Replace("\"", "\\\""));
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    using (HardwareBackend hw = new HardwareBackend())
                    {
                        hw.SetAuto();
                    }
                }
                catch { }
                MessageBox.Show(
                    "An unexpected error occurred in MiniFanControl. Fans have been restored to BIOS Auto for hardware safety.\n\n" + e.Exception.Message,
                    "MiniFanControl - Safety Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    using (HardwareBackend hw = new HardwareBackend())
                    {
                        hw.SetAuto();
                    }
                }
                catch { }
            };
            Application.Run(new MainForm(demoMode, startMinimized));
            return 0;
        }
    }
}
