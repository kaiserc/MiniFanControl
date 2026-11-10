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
    public class TelemetryRecord
    {
        public DateTime Timestamp;
        public int CpuTemp;
        public int GpuTemp;
        public int ApuPowerWatts;
        public int Fan1Rpm;
        public int Fan2Rpm;
        public int Fan1Pct;
        public int Fan2Pct;
        public string Mode;
        public int GpuLoadPct;
        public int Fps;
        public int NvmeTemp;
        public double RamUsedGb;
        public int RamLoadPct;
        public double RamPowerWatts;
    }

    public class TelemetrySessionStats
    {
        public const int HISTORY_LEN = 120;
        private static readonly char[] SparkBlocks = new char[] { '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        public int SampleCount { get; private set; }
        public int MinCpuTemp { get; private set; }
        public int MaxCpuTemp { get; private set; }
        public int MinGpuTemp { get; private set; }
        public int MaxGpuTemp { get; private set; }
        public int MinNvmeTemp { get; private set; }
        public int MaxNvmeTemp { get; private set; }
        public int MinPowerWatts { get; private set; }
        public int MaxPowerWatts { get; private set; }
        public double MinRamPowerWatts { get; private set; }
        public double MaxRamPowerWatts { get; private set; }
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
        private long _sumNvmeTemp;
        private int _nvmeSampleCount;
        private long _sumPowerWatts;
        private int _pwrSampleCount;
        private double _sumRamPowerWatts;
        private int _ramPowerSampleCount;
        private long _sumFan1Rpm;
        private int _fan1SampleCount;
        private long _sumFan2Rpm;
        private int _fan2SampleCount;

        private readonly List<int> _cpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _nvmeHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _pwrHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _ramPowerHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan1RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan2RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuLoadHist = new List<int>(HISTORY_LEN);
        private readonly List<TelemetryRecord> _sessionLog = new List<TelemetryRecord>();
        private readonly object _logLock = new object();

        public int LoggedRecordCount { get { lock (_logLock) { return _sessionLog.Count; } } }

        public double AvgCpuTemp { get { return SampleCount > 0 ? (double)_sumCpuTemp / SampleCount : 0.0; } }
        public double AvgGpuTemp { get { return _gpuSampleCount > 0 ? (double)_sumGpuTemp / _gpuSampleCount : 0.0; } }
        public double AvgNvmeTemp { get { return _nvmeSampleCount > 0 ? (double)_sumNvmeTemp / _nvmeSampleCount : 0.0; } }
        public double AvgPowerWatts { get { return _pwrSampleCount > 0 ? (double)_sumPowerWatts / _pwrSampleCount : 0.0; } }
        public double AvgRamPowerWatts { get { return _ramPowerSampleCount > 0 ? _sumRamPowerWatts / _ramPowerSampleCount : 0.0; } }
        public double AvgFan1Rpm { get { return _fan1SampleCount > 0 ? (double)_sumFan1Rpm / _fan1SampleCount : 0.0; } }
        public double AvgFan2Rpm { get { return _fan2SampleCount > 0 ? (double)_sumFan2Rpm / _fan2SampleCount : 0.0; } }

        public List<int> CpuHistory { get { return _cpuHist; } }
        public List<int> GpuHistory { get { return _gpuHist; } }
        public List<int> NvmeHistory { get { return _nvmeHist; } }
        public List<int> PowerHistory { get { return _pwrHist; } }
        public List<int> RamPowerHistory { get { return _ramPowerHist; } }
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
            MinNvmeTemp = int.MaxValue;
            MaxNvmeTemp = 0;
            MinPowerWatts = int.MaxValue;
            MaxPowerWatts = 0;
            MinRamPowerWatts = double.MaxValue;
            MaxRamPowerWatts = 0.0;
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
            _sumNvmeTemp = 0;
            _nvmeSampleCount = 0;
            _sumPowerWatts = 0;
            _pwrSampleCount = 0;
            _sumRamPowerWatts = 0.0;
            _ramPowerSampleCount = 0;
            _sumFan1Rpm = 0;
            _fan1SampleCount = 0;
            _sumFan2Rpm = 0;
            _fan2SampleCount = 0;
            _cpuHist.Clear();
            _gpuHist.Clear();
            _nvmeHist.Clear();
            _pwrHist.Clear();
            _ramPowerHist.Clear();
            _fan1RpmHist.Clear();
            _fan2RpmHist.Clear();
            _gpuLoadHist.Clear();
            lock (_logLock) { _sessionLog.Clear(); }
        }

        public void Record(FanStatus st)
        {
            if (st.CpuTemp <= 0) return;
            lock (_logLock)
            {
                if (_sessionLog.Count >= 10000) _sessionLog.RemoveAt(0);
                _sessionLog.Add(new TelemetryRecord
                {
                    Timestamp = DateTime.Now,
                    CpuTemp = st.CpuTemp,
                    GpuTemp = st.GpuTemp,
                    ApuPowerWatts = st.ApuPowerWatts,
                    Fan1Rpm = st.Fan1Rpm,
                    Fan2Rpm = st.Fan2Rpm,
                    Fan1Pct = st.Fan1Pct,
                    Fan2Pct = st.Fan2Pct,
                    Mode = st.Mode,
                    GpuLoadPct = st.GfxActivityPct,
                    Fps = st.Fps,
                    NvmeTemp = st.NvmeTemp,
                    RamUsedGb = st.RamUsedGb,
                    RamLoadPct = st.RamLoadPct,
                    RamPowerWatts = st.RamPowerWatts
                });
            }
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

            int nvmeVal = st.NvmeTemp > 0 ? st.NvmeTemp : (_nvmeHist.Count > 0 ? _nvmeHist[_nvmeHist.Count - 1] : 0);
            PushSample(_nvmeHist, nvmeVal);
            if (st.NvmeTemp > 0)
            {
                _nvmeSampleCount++;
                if (st.NvmeTemp < MinNvmeTemp) MinNvmeTemp = st.NvmeTemp;
                if (st.NvmeTemp > MaxNvmeTemp) MaxNvmeTemp = st.NvmeTemp;
                _sumNvmeTemp += st.NvmeTemp;
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

            double ramPwr = st.RamPowerWatts;
            int ramScaled = ramPwr > 0 ? (int)Math.Round(ramPwr * 10.0) : (_ramPowerHist.Count > 0 ? _ramPowerHist[_ramPowerHist.Count - 1] : 0);
            PushSample(_ramPowerHist, ramScaled);
            if (ramPwr > 0)
            {
                _ramPowerSampleCount++;
                if (ramPwr < MinRamPowerWatts) MinRamPowerWatts = ramPwr;
                if (ramPwr > MaxRamPowerWatts) MaxRamPowerWatts = ramPwr;
                _sumRamPowerWatts += ramPwr;
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
        public string GetNvmeSparkline() { return FormatSparkline(_nvmeHist, 2); }
        public string GetPowerSparkline() { return FormatSparkline(_pwrHist, 2); }
        public string GetRamPowerSparkline() { return FormatSparkline(_ramPowerHist, 1); }
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

        public List<TelemetryRecord> GetSessionRecords()
        {
            lock (_logLock) { return new List<TelemetryRecord>(_sessionLog); }
        }

        public bool ExportToCsv(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,CPU_Temp_C,iGPU_Temp_C,APU_Power_W,Fan1_RPM,Fan2_RPM,Fan1_PWM_Pct,Fan2_PWM_Pct,Mode,iGPU_Load_Pct,FPS,NVMe_Temp_C,RAM_Used_GB,RAM_Load_Pct,RAM_Power_W");
                lock (_logLock)
                {
                    foreach (var r in _sessionLog)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            "{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6},{7},\"{8}\",{9},{10},{11},{12:0.0},{13},{14:0.0}\r\n",
                            r.Timestamp, r.CpuTemp, r.GpuTemp, r.ApuPowerWatts, r.Fan1Rpm, r.Fan2Rpm, r.Fan1Pct, r.Fan2Pct,
                            r.Mode ?? "Auto", r.GpuLoadPct, r.Fps, r.NvmeTemp, r.RamUsedGb, r.RamLoadPct, r.RamPowerWatts);
                    }
                }
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Disposable guard providing fail-closed mutual exclusion for Embedded Controller bus access.
    /// Automatically releases acquired mutexes upon disposal.
    /// </summary>
}
