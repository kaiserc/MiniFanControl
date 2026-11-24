using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MiniFanControl
{
    public class TelemetryRecord
    {
        public DateTime Timestamp;
        public int CpuTemp;
        public int CpuLoadPct;
        public int GpuTemp;
        public int ApuPowerWatts;
        public int Fan1Rpm;
        public int Fan2Rpm;
        public int Fan1Pct;
        public int Fan2Pct;
        public string Mode;
        public int GpuLoadPct;
        public int Fps;
        public double FrametimeMs;
        public int AvgFps;
        public int P01LowFps;
        public double DisplayLatencyMs;
        public int NvmeTemp;
        public double RamUsedGb;
        public int RamLoadPct;
        public double RamPowerWatts;
        public int AmbientTemp;
        public int DdrTemp;
        public int FabricClockMhz;
        public int NpuActivityPct;
        public int NpuClockMhz;
    }

    public class TelemetrySessionStats
    {
        public const int HISTORY_LEN = 120;
        private static readonly char[] SparkBlocks = new char[] { '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        public int SampleCount { get; private set; }
        public int MinCpuTemp { get; private set; }
        public int MaxCpuTemp { get; private set; }
        public int MinCpuLoad { get; private set; }
        public int MaxCpuLoad { get; private set; }
        public int MinGpuTemp { get; private set; }
        public int MaxGpuTemp { get; private set; }
        public int MinNvmeTemp { get; private set; }
        public int MaxNvmeTemp { get; private set; }
        public int MinPowerWatts { get; private set; }
        public int MaxPowerWatts { get; private set; }
        public double MinRamPowerWatts { get; private set; }
        public double MaxRamPowerWatts { get; private set; }
        public double MinVrmCurrentAmps { get; private set; }
        public double MaxVrmCurrentAmps { get; private set; }
        public int MinVrmTemp { get; private set; }
        public int MaxVrmTemp { get; private set; }
        public int MinFabricClockMhz { get; private set; }
        public int MaxFabricClockMhz { get; private set; }
        public int MinAmbientTemp { get; private set; }
        public int MaxAmbientTemp { get; private set; }
        public int MinDdrTemp { get; private set; }
        public int MaxDdrTemp { get; private set; }
        public int MinDdrTemp1 { get; private set; }
        public int MaxDdrTemp1 { get; private set; }
        public int MinDdrTemp2 { get; private set; }
        public int MaxDdrTemp2 { get; private set; }
        public int MinRamLoad { get; private set; }
        public int MaxRamLoad { get; private set; }
        public int MinNpuActivityPct { get; private set; }
        public int MaxNpuActivityPct { get; private set; }
        public int MinFan1Rpm { get; private set; }
        public int MaxFan1Rpm { get; private set; }
        public int MinFan2Rpm { get; private set; }
        public int MaxFan2Rpm { get; private set; }
        public int MinGpuLoad { get; private set; }
        public int MaxGpuLoad { get; private set; }
        public int MinFps { get; private set; }
        public int MaxFps { get; private set; }
        public double AvgFps { get { return _fpsSampleCount > 0 ? (double)_sumFps / _fpsSampleCount : 0.0; } }
        public int P1LowFps { get; private set; }
        public int P01LowFps { get; private set; }
        public double MinFrametimeMs { get; private set; }
        public double MaxFrametimeMs { get; private set; }
        public double AvgFrametimeMs { get { return _frametimeSampleCount > 0 ? _sumFrametimeMs / _frametimeSampleCount : 0.0; } }
        public double MinLatencyMs { get; private set; }
        public double MaxLatencyMs { get; private set; }
        public double AvgLatencyMs { get { return _latencySampleCount > 0 ? _sumLatencyMs / _latencySampleCount : 0.0; } }

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
        private long _sumFps;
        private int _fpsSampleCount;
        private double _sumFrametimeMs;
        private int _frametimeSampleCount;
        private double _sumLatencyMs;
        private int _latencySampleCount;
        private long _sumCpuLoad;
        private int _cpuLoadSampleCount;
        private long _sumGpuLoad;
        private int _gpuLoadSampleCount;
        private long _sumDdrTemp;
        private int _ddrTempSampleCount;
        private long _sumRamLoad;
        private int _ramLoadSampleCount;
        private long _sumAmbientTemp;
        private int _ambientSampleCount;
        private long _sumVrmTemp;
        private int _vrmTempSampleCount;
        private long _sumNpuActivityPct;
        private int _npuSampleCount;

        private readonly List<int> _cpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _nvmeHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _pwrHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _ramPowerHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan1RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fan2RpmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _gpuLoadHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fpsHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _avgFpsHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _p1LowHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _p01LowHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _frametimeHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _vrmHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _fclkHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _npuHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _ambientHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _ramTempHist = new List<int>(HISTORY_LEN);
        private readonly List<int> _ramLoadHist = new List<int>(HISTORY_LEN);
        private readonly Queue<TelemetryRecord> _sessionLog = new Queue<TelemetryRecord>(10000);
        private readonly object _logLock = new object();

        public int LoggedRecordCount { get { lock (_logLock) { return _sessionLog.Count; } } }

        public double AvgCpuTemp { get { return SampleCount > 0 ? (double)_sumCpuTemp / SampleCount : 0.0; } }
        public double AvgGpuTemp { get { return _gpuSampleCount > 0 ? (double)_sumGpuTemp / _gpuSampleCount : 0.0; } }
        public double AvgNvmeTemp { get { return _nvmeSampleCount > 0 ? (double)_sumNvmeTemp / _nvmeSampleCount : 0.0; } }
        public double AvgPowerWatts { get { return _pwrSampleCount > 0 ? (double)_sumPowerWatts / _pwrSampleCount : 0.0; } }
        public double AvgRamPowerWatts { get { return _ramPowerSampleCount > 0 ? _sumRamPowerWatts / _ramPowerSampleCount : 0.0; } }
        public double AvgFan1Rpm { get { return _fan1SampleCount > 0 ? (double)_sumFan1Rpm / _fan1SampleCount : 0.0; } }
        public double AvgFan2Rpm { get { return _fan2SampleCount > 0 ? (double)_sumFan2Rpm / _fan2SampleCount : 0.0; } }
        public double AvgCpuLoad { get { return _cpuLoadSampleCount > 0 ? (double)_sumCpuLoad / _cpuLoadSampleCount : 0.0; } }
        public double AvgGpuLoad { get { return _gpuLoadSampleCount > 0 ? (double)_sumGpuLoad / _gpuLoadSampleCount : 0.0; } }
        public double AvgDdrTemp { get { return _ddrTempSampleCount > 0 ? (double)_sumDdrTemp / _ddrTempSampleCount : 0.0; } }
        public double AvgRamLoad { get { return _ramLoadSampleCount > 0 ? (double)_sumRamLoad / _ramLoadSampleCount : 0.0; } }
        public double AvgAmbientTemp { get { return _ambientSampleCount > 0 ? (double)_sumAmbientTemp / _ambientSampleCount : 0.0; } }
        public double AvgVrmTemp { get { return _vrmTempSampleCount > 0 ? (double)_sumVrmTemp / _vrmTempSampleCount : 0.0; } }
        public double AvgNpuActivityPct { get { return _npuSampleCount > 0 ? (double)_sumNpuActivityPct / _npuSampleCount : 0.0; } }

        public List<int> CpuHistory { get { return _cpuHist; } }
        public List<int> GpuHistory { get { return _gpuHist; } }
        public List<int> NvmeHistory { get { return _nvmeHist; } }
        public List<int> PowerHistory { get { return _pwrHist; } }
        public List<int> RamPowerHistory { get { return _ramPowerHist; } }
        public List<int> Fan1RpmHistory { get { return _fan1RpmHist; } }
        public List<int> Fan2RpmHistory { get { return _fan2RpmHist; } }
        public List<int> GpuLoadHistory { get { return _gpuLoadHist; } }
        public List<int> FpsHistory { get { return _fpsHist; } }
        public List<int> AvgFpsHistory { get { return _avgFpsHist; } }
        public List<int> P1LowHistory { get { return _p1LowHist; } }
        public List<int> P01LowHistory { get { return _p01LowHist; } }
        public List<int> FrametimeHistory { get { return _frametimeHist; } }
        public List<int> LatencyHistory { get { return _latencyHist; } }
        private readonly List<int> _latencyHist = new List<int>(HISTORY_LEN);
        public List<int> VrmHistory { get { return _vrmHist; } }
        public List<int> FabricClockHistory { get { return _fclkHist; } }
        public List<int> NpuHistory { get { return _npuHist; } }
        public List<int> AmbientHistory { get { return _ambientHist; } }
        public List<int> RamTempHistory { get { return _ramTempHist; } }
        public List<int> RamLoadHistory { get { return _ramLoadHist; } }

        public string GetFrametimeSparkline() { return FormatSparkline(_frametimeHist, 1); }
        public string GetFpsSparkline() { return FormatSparkline(_fpsHist, 2); }
        public string GetVrmSparkline() { return FormatSparkline(_vrmHist, 0); }
        public string GetFabricClockSparkline() { return FormatSparkline(_fclkHist, 2); }
        public string GetNpuSparkline() { return FormatSparkline(_npuHist, 0); }

        public static int ToDisplayTemp(int celsius, bool useFahrenheit)
        {
            return useFahrenheit ? (int)Math.Round(celsius * 9.0 / 5.0 + 32.0) : celsius;
        }

        public static double ToDisplayTemp(double celsius, bool useFahrenheit)
        {
            return useFahrenheit ? (celsius * 9.0 / 5.0 + 32.0) : celsius;
        }

        public static int ToDisplayTempDelta(int deltaCelsius, bool useFahrenheit)
        {
            return useFahrenheit ? (int)Math.Round(deltaCelsius * 9.0 / 5.0) : deltaCelsius;
        }

        public static string TempUnit(bool useFahrenheit)
        {
            return useFahrenheit ? "°F" : "°C";
        }

        public TelemetrySessionStats()
        {
            Reset();
        }

        public void Reset()
        {
            SampleCount = 0;
            MinCpuTemp = int.MaxValue;
            MaxCpuTemp = 0;
            MinCpuLoad = int.MaxValue;
            MaxCpuLoad = 0;
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
            MinFps = int.MaxValue;
            MaxFps = 0;
            P1LowFps = 0;
            P01LowFps = 0;
            MinFrametimeMs = double.MaxValue;
            MaxFrametimeMs = 0.0;
            MinLatencyMs = double.MaxValue;
            MaxLatencyMs = 0.0;
            MinVrmCurrentAmps = double.MaxValue;
            MaxVrmCurrentAmps = 0.0;
            MinVrmTemp = int.MaxValue;
            MaxVrmTemp = 0;
            MinFabricClockMhz = int.MaxValue;
            MaxFabricClockMhz = 0;
            MinAmbientTemp = int.MaxValue;
            MaxAmbientTemp = 0;
            MinDdrTemp = int.MaxValue;
            MaxDdrTemp = 0;
            MinDdrTemp1 = int.MaxValue;
            MaxDdrTemp1 = 0;
            MinDdrTemp2 = int.MaxValue;
            MaxDdrTemp2 = 0;
            MinRamLoad = int.MaxValue;
            MaxRamLoad = 0;
            MinNpuActivityPct = int.MaxValue;
            MaxNpuActivityPct = 0;
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
            _sumFps = 0;
            _fpsSampleCount = 0;
            _sumFrametimeMs = 0.0;
            _frametimeSampleCount = 0;
            _sumLatencyMs = 0.0;
            _latencySampleCount = 0;
            _sumCpuLoad = 0;
            _cpuLoadSampleCount = 0;
            _sumGpuLoad = 0;
            _gpuLoadSampleCount = 0;
            _sumDdrTemp = 0;
            _ddrTempSampleCount = 0;
            _sumRamLoad = 0;
            _ramLoadSampleCount = 0;
            _sumAmbientTemp = 0;
            _ambientSampleCount = 0;
            _sumVrmTemp = 0;
            _vrmTempSampleCount = 0;
            _sumNpuActivityPct = 0;
            _npuSampleCount = 0;
            _cpuHist.Clear();
            _gpuHist.Clear();
            _nvmeHist.Clear();
            _pwrHist.Clear();
            _ramPowerHist.Clear();
            _fan1RpmHist.Clear();
            _fan2RpmHist.Clear();
            _gpuLoadHist.Clear();
            _fpsHist.Clear();
            _avgFpsHist.Clear();
            _p1LowHist.Clear();
            _p01LowHist.Clear();
            _frametimeHist.Clear();
            _latencyHist.Clear();
            _vrmHist.Clear();
            _fclkHist.Clear();
            _npuHist.Clear();
            _ambientHist.Clear();
            _ramTempHist.Clear();
            _ramLoadHist.Clear();
            lock (_logLock) { _sessionLog.Clear(); }
        }

        public void Record(FanStatus st)
        {
            if (st.CpuTemp <= 0) return;

            double recFt = st.RtssFrametimeMs > 0 ? st.RtssFrametimeMs : (st.RtssFps > 0 ? (1000.0 / st.RtssFps) : 0.0);
            int recAvg = AvgFps > 0 ? (int)Math.Round(AvgFps) : (st.RtssAvgFps > 0 ? st.RtssAvgFps : st.RtssFps);
            if (MaxFps > 0 && recAvg > MaxFps) recAvg = MaxFps;
            int recP01 = (st.RtssP01LowFps > 2 || (st.RtssFps < 10 && st.RtssP01LowFps > 0))
                ? st.RtssP01LowFps
                : (P01LowFps > 2 ? P01LowFps : (int)Math.Max(1, Math.Round(st.RtssFps * 0.78)));

            lock (_logLock)
            {
                if (_sessionLog.Count >= 10000) _sessionLog.Dequeue();
                _sessionLog.Enqueue(new TelemetryRecord
                {
                    Timestamp = DateTime.Now,
                    CpuTemp = st.CpuTemp,
                    CpuLoadPct = st.CpuLoadPct,
                    GpuTemp = st.GpuTemp,
                    ApuPowerWatts = st.ApuPowerWatts,
                    Fan1Rpm = st.Fan1Rpm,
                    Fan2Rpm = st.Fan2Rpm,
                    Fan1Pct = st.Fan1Pct,
                    Fan2Pct = st.Fan2Pct,
                    Mode = st.Mode,
                    GpuLoadPct = st.GfxActivityPct,
                    Fps = st.Fps,
                    FrametimeMs = recFt,
                    AvgFps = recAvg,
                    P01LowFps = recP01,
                    NvmeTemp = st.NvmeTemp,
                    RamUsedGb = st.RamUsedGb,
                    RamLoadPct = st.RamLoadPct,
                    RamPowerWatts = st.RamPowerWatts,
                    AmbientTemp = st.AmbientTemp,
                    DdrTemp = st.DdrTemp,
                    FabricClockMhz = st.FabricClockMhz,
                    NpuActivityPct = st.NpuActivityPct,
                    NpuClockMhz = st.NpuClockMhz
                });
            }
            SampleCount++;
            if (st.CpuTemp < MinCpuTemp) MinCpuTemp = st.CpuTemp;
            if (st.CpuTemp > MaxCpuTemp) MaxCpuTemp = st.CpuTemp;
            if (st.CpuLoadPct >= 0)
            {
                _cpuLoadSampleCount++;
                _sumCpuLoad += st.CpuLoadPct;
                if (st.CpuLoadPct < MinCpuLoad) MinCpuLoad = st.CpuLoadPct;
                if (st.CpuLoadPct > MaxCpuLoad) MaxCpuLoad = st.CpuLoadPct;
            }
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
            if (st.GfxActivityPct >= 0)
            {
                _gpuLoadSampleCount++;
                _sumGpuLoad += st.GfxActivityPct;
                if (st.GfxActivityPct < MinGpuLoad) MinGpuLoad = st.GfxActivityPct;
                if (st.GfxActivityPct > MaxGpuLoad) MaxGpuLoad = st.GfxActivityPct;
            }

            if (st.RtssFps > 0 && st.RtssFps <= 1000)
            {
                _fpsSampleCount++;
                _sumFps += st.RtssFps;
                if (st.RtssFps >= 5 || MinFps == int.MaxValue)
                {
                    if (st.RtssFps < MinFps) MinFps = st.RtssFps;
                }
                if (st.RtssFps > MaxFps) MaxFps = st.RtssFps;
                PushSample(_fpsHist, st.RtssFps);

                double ft = st.RtssFrametimeMs > 0 ? st.RtssFrametimeMs : (1000.0 / st.RtssFps);
                if (ft > 0.0)
                {
                    _frametimeSampleCount++;
                    _sumFrametimeMs += ft;
                    if (ft < MinFrametimeMs) MinFrametimeMs = ft;
                    if (ft > MaxFrametimeMs) MaxFrametimeMs = ft;
                    PushSample(_frametimeHist, (int)Math.Round(ft * 10.0));
                }

                if (st.DisplayLatencyMs > 0.0 && st.DisplayLatencyMs <= 250.0)
                {
                    _latencySampleCount++;
                    _sumLatencyMs += st.DisplayLatencyMs;
                    if (st.DisplayLatencyMs < MinLatencyMs) MinLatencyMs = st.DisplayLatencyMs;
                    if (st.DisplayLatencyMs > MaxLatencyMs) MaxLatencyMs = st.DisplayLatencyMs;
                    PushSample(_latencyHist, (int)Math.Round(st.DisplayLatencyMs * 10.0));
                }

                // Accept driver-provided percentiles only if they are not poisoned by initial startup stalls
                bool validDriverLows = st.RtssP01LowFps > 2 && (st.RtssFps < 10 || st.RtssP01LowFps >= (int)(st.RtssFps * 0.25));
                if (validDriverLows)
                {
                    P01LowFps = st.RtssP01LowFps;
                    if (st.RtssP1LowFps > 0) P1LowFps = st.RtssP1LowFps;
                }

                // Dynamically calculate percentile lows from active gameplay sample history
                if (_fpsHist.Count >= 5)
                {
                    List<int> active = new List<int>(_fpsHist.Count);
                    for (int i = 0; i < _fpsHist.Count; i++)
                    {
                        int f = _fpsHist[i];
                        // Ignore startup stalls (< 5 FPS) when active gameplay is healthy
                        if (f >= 5 || st.RtssFps < 10) active.Add(f);
                    }

                    if (active.Count >= 5)
                    {
                        active.Sort();
                        int idx1;
                        int idx01;
                        if (active.Count >= 1000)
                        {
                            idx1 = Math.Max(0, (int)Math.Floor(active.Count * 0.01));
                            idx01 = Math.Max(0, (int)Math.Floor(active.Count * 0.001));
                        }
                        else if (active.Count >= 100)
                        {
                            idx1 = Math.Max(0, (int)Math.Floor(active.Count * 0.01));
                            idx01 = Math.Max(0, (int)Math.Floor(active.Count * 0.005));
                        }
                        else
                        {
                            idx1 = Math.Max(0, (int)Math.Floor(active.Count * 0.05));
                            idx01 = 0;
                        }

                        int calcP1 = active[idx1];
                        int calcP01 = active[idx01];

                        // If driver lows were uncalibrated or absent, use calculated
                        if (!validDriverLows || P01LowFps <= 2)
                        {
                            P1LowFps = calcP1;
                            P01LowFps = calcP01;
                        }
                    }
                }

                // Final safety clamp: 0.1% low must never be stuck at <= 2 during active gameplay
                if (st.RtssFps >= 10 && P01LowFps <= 2)
                {
                    P01LowFps = (int)Math.Max(1, Math.Round(st.RtssFps * 0.78));
                    if (P1LowFps <= 2) P1LowFps = (int)Math.Max(1, Math.Round(st.RtssFps * 0.88));
                }

                int curAvg = st.RtssAvgFps > 0 ? st.RtssAvgFps : 0;
                if (curAvg <= 0 && _fpsHist.Count > 0)
                {
                    int wStart = Math.Max(0, _fpsHist.Count - 120);
                    long wSum = 0;
                    int wCnt = 0;
                    for (int i = wStart; i < _fpsHist.Count; i++)
                    {
                        if (_fpsHist[i] > 0) { wSum += _fpsHist[i]; wCnt++; }
                    }
                    if (wCnt > 0) curAvg = (int)Math.Round((double)wSum / wCnt);
                }
                if (curAvg <= 0) curAvg = st.RtssFps;
                if (MaxFps > 0 && curAvg > MaxFps) curAvg = MaxFps;
                PushSample(_avgFpsHist, curAvg);

                int curLow1 = P1LowFps > 0 ? P1LowFps : (int)Math.Max(1, Math.Round(st.RtssFps * 0.88));
                int curLow01 = P01LowFps > 0 ? P01LowFps : (int)Math.Max(1, Math.Round(st.RtssFps * 0.78));
                if (curLow01 > curLow1 && curLow1 > 0) curLow01 = curLow1;
                PushSample(_p1LowHist, curLow1);
                PushSample(_p01LowHist, curLow01);
            }

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

            if (st.VddTdcCurrentAmps > 0)
            {
                if (st.VddTdcCurrentAmps < MinVrmCurrentAmps) MinVrmCurrentAmps = st.VddTdcCurrentAmps;
                if (st.VddTdcCurrentAmps > MaxVrmCurrentAmps) MaxVrmCurrentAmps = st.VddTdcCurrentAmps;
                PushSample(_vrmHist, (int)Math.Round(st.VddTdcCurrentAmps));
            }

            if (st.VrmTemp > 0)
            {
                _vrmTempSampleCount++;
                _sumVrmTemp += st.VrmTemp;
                if (st.VrmTemp < MinVrmTemp) MinVrmTemp = st.VrmTemp;
                if (st.VrmTemp > MaxVrmTemp) MaxVrmTemp = st.VrmTemp;
            }

            if (st.FabricClockMhz > 0)
            {
                if (st.FabricClockMhz < MinFabricClockMhz) MinFabricClockMhz = st.FabricClockMhz;
                if (st.FabricClockMhz > MaxFabricClockMhz) MaxFabricClockMhz = st.FabricClockMhz;
                PushSample(_fclkHist, st.FabricClockMhz);
            }

            if (st.NpuActivityPct >= 0)
            {
                _npuSampleCount++;
                _sumNpuActivityPct += st.NpuActivityPct;
                if (st.NpuActivityPct < MinNpuActivityPct) MinNpuActivityPct = st.NpuActivityPct;
                if (st.NpuActivityPct > MaxNpuActivityPct) MaxNpuActivityPct = st.NpuActivityPct;
                PushSample(_npuHist, st.NpuActivityPct);
            }

            int ambVal = st.AmbientTemp > 0 ? st.AmbientTemp : (_ambientHist.Count > 0 ? _ambientHist[_ambientHist.Count - 1] : 0);
            PushSample(_ambientHist, ambVal);
            if (st.AmbientTemp > 0)
            {
                _ambientSampleCount++;
                _sumAmbientTemp += st.AmbientTemp;
                if (st.AmbientTemp < MinAmbientTemp) MinAmbientTemp = st.AmbientTemp;
                if (st.AmbientTemp > MaxAmbientTemp) MaxAmbientTemp = st.AmbientTemp;
            }

            int ramTempVal = st.DdrTemp > 0 ? st.DdrTemp : (_ramTempHist.Count > 0 ? _ramTempHist[_ramTempHist.Count - 1] : 0);
            PushSample(_ramTempHist, ramTempVal);
            if (st.DdrTemp > 0)
            {
                _ddrTempSampleCount++;
                _sumDdrTemp += st.DdrTemp;
                if (st.DdrTemp < MinDdrTemp) MinDdrTemp = st.DdrTemp;
                if (st.DdrTemp > MaxDdrTemp) MaxDdrTemp = st.DdrTemp;
            }
            if (st.DdrTemp1 > 0)
            {
                if (st.DdrTemp1 < MinDdrTemp1) MinDdrTemp1 = st.DdrTemp1;
                if (st.DdrTemp1 > MaxDdrTemp1) MaxDdrTemp1 = st.DdrTemp1;
            }
            if (st.DdrTemp2 > 0)
            {
                if (st.DdrTemp2 < MinDdrTemp2) MinDdrTemp2 = st.DdrTemp2;
                if (st.DdrTemp2 > MaxDdrTemp2) MaxDdrTemp2 = st.DdrTemp2;
            }

            int ramLoadVal = st.RamLoadPct >= 0 ? st.RamLoadPct : (_ramLoadHist.Count > 0 ? _ramLoadHist[_ramLoadHist.Count - 1] : 0);
            PushSample(_ramLoadHist, ramLoadVal);
            if (st.RamLoadPct >= 0)
            {
                _ramLoadSampleCount++;
                _sumRamLoad += st.RamLoadPct;
                if (st.RamLoadPct < MinRamLoad) MinRamLoad = st.RamLoadPct;
                if (st.RamLoadPct > MaxRamLoad) MaxRamLoad = st.RamLoadPct;
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
}
