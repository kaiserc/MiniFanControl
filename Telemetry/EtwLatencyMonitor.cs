using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MiniFanControl
{
    public struct EtwLatencyTelemetry
    {
        public bool IsEtwActive;          // True if real-time Windows DXGI/DxgKrnl ETW session is running
        public bool HasLiveFrames;        // True if an active 3D process presented frames recently
        public double DisplayLatencyMs;   // Present() start -> Hardware VSync/HSync scan-out latency (ms)
        public double RenderLatencyMs;    // Present() start -> Hardware MMIO/MPO flip submission (ms)
        public double PresentApiMs;       // CPU time spent inside DXGI Present() call (ms)
        public string FlipMode;           // "Indep. Flip", "MPO Flip", "Composed", or ""
        public int Fps;                   // Native ETW instantaneous FPS (fallback when RTSS is not running)
        public double FrametimeMs;        // Native ETW frametime (ms)
        public int AvgFps;
        public int P1LowFps;
        public int P01LowFps;
        public string ProcessName;
        public uint ProcessId;
    }

    /// <summary>
    /// Pure, deterministic PresentMon-style ETW event correlator for Microsoft-Windows-DXGI
    /// and Microsoft-Windows-DxgKrnl events. Computes Present-to-Display Latency (MsUntilDisplayed),
    /// Render Latency (Present-to-Flip), Flip Mode (Indep. Flip / MPO Flip / Composed), and
    /// native hook-free FPS + 1%/0.1% lows with 1 µs QPC timestamp precision.
    /// </summary>
    public class EtwPresentTracker
    {
        public static readonly Guid ProviderDxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        public static readonly Guid ProviderDxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");

        // Microsoft-Windows-DXGI Event IDs
        public const ushort DXGI_PRESENT_START = 42;
        public const ushort DXGI_PRESENT_STOP = 43;
        public const ushort DXGI_PRESENT_MPO_START = 55;
        public const ushort DXGI_PRESENT_MPO_STOP = 56;

        // Microsoft-Windows-DxgKrnl Event IDs
        public const ushort DXGKRNL_VSYNC_DPC = 17;
        public const ushort DXGKRNL_MMIO_FLIP = 116;
        public const ushort DXGKRNL_BLIT = 166;
        public const ushort DXGKRNL_FLIP = 168;
        public const ushort DXGKRNL_PRESENT = 184;
        public const ushort DXGKRNL_FLIP_MPO = 252;
        public const ushort DXGKRNL_MMIO_FLIP_MPO = 259;
        public const ushort DXGKRNL_VSYNC_DPC_MPO = 273;
        public const ushort DXGKRNL_HSYNC_DPC_MPO = 300; // FreeSync / VRR scan-out completion

        private struct PendingPresent
        {
            public long StartQpc;
            public long FlipQpc;
            public bool Flipped;
        }

        private readonly object _lock = new object();
        private const int IN_FLIGHT_CAP = 8;
        private readonly PendingPresent[] _inFlightBuf = new PendingPresent[IN_FLIGHT_CAP];
        private int _inFlightHead = 0;
        private int _inFlightCount = 0;
        private readonly int[] _frameTimeUsRing = new int[1024];
        private int _ringHead = 0;
        private int _ringCount = 0;

        private uint _trackedPid = 0;
        private string _trackedProcName = null;
        private long _lastPresentStartQpc = 0;
        private long _lastActivePresentQpc = 0;
        private long _currentPresentStartQpc = 0;
        private long _lastScanoutQpc = 0;

        private double _smoothedDisplayLatMs = 0.0;
        private double _smoothedRenderLatMs = 0.0;
        private double _smoothedPresentApiMs = 0.0;
        private double _lastFrametimeMs = 0.0;
        private string _flipMode = "";
        private int _mpoFlipCount = 0;
        private int _indepFlipCount = 0;
        private int _blitCount = 0;

        private readonly Dictionary<uint, string> _pidNameCache = new Dictionary<uint, string>();
        private readonly Func<uint, string> _pidNameResolver;

        public EtwPresentTracker(Func<uint, string> pidNameResolver = null)
        {
            _pidNameResolver = pidNameResolver ?? new Func<uint, string>(DefaultResolveProcessName);
        }

        private static string DefaultResolveProcessName(uint pid)
        {
            if (pid == 0) return null;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch
            {
                return null;
            }
        }

        private string GetProcessNameCached(uint pid)
        {
            string name;
            if (_pidNameCache.TryGetValue(pid, out name)) return name;
            if (_pidNameCache.Count > 256) _pidNameCache.Clear();
            name = _pidNameResolver(pid);
            _pidNameCache[pid] = name;
            return name;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _inFlightHead = 0;
                _inFlightCount = 0;
                _ringHead = 0;
                _ringCount = 0;
                _trackedPid = 0;
                _trackedProcName = null;
                _lastPresentStartQpc = 0;
                _lastActivePresentQpc = 0;
                _currentPresentStartQpc = 0;
                _lastScanoutQpc = 0;
                _smoothedDisplayLatMs = 0.0;
                _smoothedRenderLatMs = 0.0;
                _smoothedPresentApiMs = 0.0;
                _lastFrametimeMs = 0.0;
                _flipMode = "";
                _mpoFlipCount = 0;
                _indepFlipCount = 0;
                _blitCount = 0;
            }
        }

        /// <summary>
        /// Processes a single DXGI or DxgKrnl ETW event timestamped in QPC ticks.
        /// </summary>
        public void ProcessEvent(Guid providerId, ushort eventId, uint pid, long qpcTimestamp, double qpcFreq)
        {
            if (qpcFreq <= 0.0 || qpcTimestamp <= 0) return;

            if (providerId == ProviderDxgi)
            {
                if (eventId == DXGI_PRESENT_START || eventId == DXGI_PRESENT_MPO_START)
                {
                    HandlePresentStart(pid, qpcTimestamp, qpcFreq, eventId == DXGI_PRESENT_MPO_START);
                }
                else if (eventId == DXGI_PRESENT_STOP || eventId == DXGI_PRESENT_MPO_STOP)
                {
                    HandlePresentStop(pid, qpcTimestamp, qpcFreq);
                }
                return;
            }

            if (providerId == ProviderDxgKrnl)
            {
                if (eventId == DXGKRNL_MMIO_FLIP_MPO || eventId == DXGKRNL_FLIP_MPO)
                {
                    HandleFlipEvent(pid, qpcTimestamp, qpcFreq, true);
                }
                else if (eventId == DXGKRNL_MMIO_FLIP || eventId == DXGKRNL_FLIP)
                {
                    HandleFlipEvent(pid, qpcTimestamp, qpcFreq, false);
                }
                else if (eventId == DXGKRNL_BLIT)
                {
                    HandleBlitEvent(pid);
                }
                else if (eventId == DXGKRNL_VSYNC_DPC || eventId == DXGKRNL_VSYNC_DPC_MPO || eventId == DXGKRNL_HSYNC_DPC_MPO)
                {
                    HandleScanoutEvent(qpcTimestamp, qpcFreq);
                }
            }
        }

        private void HandlePresentStart(uint pid, long qpc, double qpcFreq, bool isMpo)
        {
            if (pid == 0) return;

            lock (_lock)
            {
                if (_trackedPid != pid)
                {
                    string procName = GetProcessNameCached(pid);
                    if (string.IsNullOrEmpty(procName) || FpsMonitor.IsIgnoredProcess(procName))
                        return;

                    // Switch target process if no current process or previous process has gone idle (> 1.5s)
                    bool stalePrev = (_trackedPid == 0) || ((qpc - _lastActivePresentQpc) * 1000.0 / qpcFreq > 1500.0);
                    if (!stalePrev) return;

                    _trackedPid = pid;
                    _trackedProcName = procName;
                    _inFlightHead = 0;
                    _inFlightCount = 0;
                    _ringHead = 0;
                    _ringCount = 0;
                    _lastPresentStartQpc = 0;
                    _smoothedDisplayLatMs = 0.0;
                    _smoothedRenderLatMs = 0.0;
                    _smoothedPresentApiMs = 0.0;
                    _lastFrametimeMs = 0.0;
                    _mpoFlipCount = 0;
                    _indepFlipCount = 0;
                    _blitCount = 0;
                    _flipMode = isMpo ? "MPO Flip" : "";
                }

                _lastActivePresentQpc = qpc;
                _currentPresentStartQpc = qpc;

                if (_lastPresentStartQpc > 0 && qpc > _lastPresentStartQpc)
                {
                    double dtMs = (qpc - _lastPresentStartQpc) * 1000.0 / qpcFreq;
                    if (dtMs >= 0.25 && dtMs <= 250.0)
                    {
                        _lastFrametimeMs = dtMs;
                        int dtUs = (int)Math.Round(dtMs * 1000.0);
                        _frameTimeUsRing[_ringHead] = dtUs;
                        _ringHead = (_ringHead + 1) % _frameTimeUsRing.Length;
                        if (_ringCount < _frameTimeUsRing.Length) _ringCount++;
                    }
                }
                _lastPresentStartQpc = qpc;

                if (isMpo)
                {
                    _mpoFlipCount++;
                    UpdateFlipModeLabel();
                }

                // Queue pending present in fixed-size ring (zero allocations, capped at 8)
                if (_inFlightCount >= IN_FLIGHT_CAP)
                {
                    _inFlightHead = (_inFlightHead + 1) & (IN_FLIGHT_CAP - 1);
                    _inFlightCount--;
                }
                int tailIdx = (_inFlightHead + _inFlightCount) & (IN_FLIGHT_CAP - 1);
                _inFlightBuf[tailIdx] = new PendingPresent { StartQpc = qpc, FlipQpc = 0, Flipped = false };
                _inFlightCount++;
            }
        }

        private void HandlePresentStop(uint pid, long qpc, double qpcFreq)
        {
            lock (_lock)
            {
                if (_trackedPid == 0 || pid != _trackedPid || _currentPresentStartQpc <= 0 || qpc < _currentPresentStartQpc)
                    return;

                double apiMs = (qpc - _currentPresentStartQpc) * 1000.0 / qpcFreq;
                if (apiMs >= 0.01 && apiMs <= 100.0)
                {
                    _smoothedPresentApiMs = (_smoothedPresentApiMs <= 0.0) ? apiMs : (_smoothedPresentApiMs * 0.8 + apiMs * 0.2);
                }
            }
        }

        private void HandleFlipEvent(uint pid, long qpc, double qpcFreq, bool isMpo)
        {
            lock (_lock)
            {
                if (_trackedPid == 0 || _inFlightCount == 0) return;
                // Kernel MMIO flips can run on system/DPC PID 0 or 4, so allow pid == 0/4 or tracked pid
                if (pid != 0 && pid != 4 && pid != _trackedPid) return;

                if (isMpo) _mpoFlipCount++;
                else _indepFlipCount++;
                UpdateFlipModeLabel();

                // Attach flip timestamp in-place to the oldest unflipped in-flight present (zero allocations)
                for (int i = 0; i < _inFlightCount; i++)
                {
                    int idx = (_inFlightHead + i) & (IN_FLIGHT_CAP - 1);
                    if (!_inFlightBuf[idx].Flipped && qpc >= _inFlightBuf[idx].StartQpc)
                    {
                        double renderMs = (qpc - _inFlightBuf[idx].StartQpc) * 1000.0 / qpcFreq;
                        if (renderMs >= 0.1 && renderMs <= 200.0)
                        {
                            _inFlightBuf[idx].FlipQpc = qpc;
                            _inFlightBuf[idx].Flipped = true;
                            _smoothedRenderLatMs = (_smoothedRenderLatMs <= 0.0) ? renderMs : (_smoothedRenderLatMs * 0.75 + renderMs * 0.25);
                            break;
                        }
                    }
                }
            }
        }

        private void HandleBlitEvent(uint pid)
        {
            lock (_lock)
            {
                if (_trackedPid == 0 || pid != _trackedPid) return;
                _blitCount++;
                UpdateFlipModeLabel();
            }
        }

        private void HandleScanoutEvent(long qpc, double qpcFreq)
        {
            lock (_lock)
            {
                if (_trackedPid == 0 || _inFlightCount == 0) return;
                // Guard against back-to-back multi-monitor VSync DPCs within 0.5 ms
                if (_lastScanoutQpc > 0 && ((qpc - _lastScanoutQpc) * 1000.0 / qpcFreq) < 0.5)
                    return;

                // Find oldest pending frame ready for this display scan-out.
                // When hardware flip events are active, require the frame to have flipped first so
                // unrelated secondary-monitor VSync DPCs never under-report display latency.
                bool expectFlipBeforeScanout = (_mpoFlipCount + _indepFlipCount) > 0;
                while (_inFlightCount > 0)
                {
                    PendingPresent head = _inFlightBuf[_inFlightHead];
                    if (qpc < head.StartQpc) break;
                    if (expectFlipBeforeScanout && (!head.Flipped || qpc < head.FlipQpc)) break;

                    double latMs = (qpc - head.StartQpc) * 1000.0 / qpcFreq;
                    _inFlightHead = (_inFlightHead + 1) & (IN_FLIGHT_CAP - 1);
                    _inFlightCount--;

                    if (latMs >= 0.4 && latMs <= 250.0)
                    {
                        _smoothedDisplayLatMs = (_smoothedDisplayLatMs <= 0.0) ? latMs : (_smoothedDisplayLatMs * 0.75 + latMs * 0.25);
                        _lastScanoutQpc = qpc;
                        break;
                    }
                }
            }
        }

        private void UpdateFlipModeLabel()
        {
            int hwFlips = _mpoFlipCount + _indepFlipCount;
            if (hwFlips >= _blitCount && hwFlips > 0)
            {
                _flipMode = (_mpoFlipCount >= _indepFlipCount) ? "MPO Flip" : "Indep. Flip";
            }
            else if (_blitCount > 0)
            {
                _flipMode = "Composed";
            }
        }

        public EtwLatencyTelemetry GetSnapshot(long nowQpc, double qpcFreq, bool isEtwActive)
        {
            EtwLatencyTelemetry snap = new EtwLatencyTelemetry();
            snap.IsEtwActive = isEtwActive;
            snap.FlipMode = "";

            lock (_lock)
            {
                if (_trackedPid == 0 || _lastActivePresentQpc <= 0 || qpcFreq <= 0.0)
                    return snap;

                double idleMs = (nowQpc - _lastActivePresentQpc) * 1000.0 / qpcFreq;
                if (idleMs > 2500.0)
                {
                    return snap;
                }

                snap.HasLiveFrames = true;
                snap.ProcessId = _trackedPid;
                snap.ProcessName = _trackedProcName;
                snap.DisplayLatencyMs = Math.Round(_smoothedDisplayLatMs, 1);
                snap.RenderLatencyMs = Math.Round(_smoothedRenderLatMs, 1);
                snap.PresentApiMs = Math.Round(_smoothedPresentApiMs, 2);
                snap.FlipMode = _flipMode;
                snap.FrametimeMs = Math.Round(_lastFrametimeMs, 2);
                snap.Fps = (_lastFrametimeMs > 0.2) ? (int)Math.Round(1000.0 / _lastFrametimeMs) : 0;

                if (_ringCount >= 10)
                {
                    List<int> valid = new List<int>(_ringCount);
                    for (int i = 0; i < _ringCount; i++)
                    {
                        int us = _frameTimeUsRing[i];
                        if (us >= 250 && us <= 200000) valid.Add(us);
                    }
                    if (valid.Count >= 10)
                    {
                        int avg, p1, p01;
                        FpsMonitor.CalculateLowsFromMicroseconds(valid.ToArray(), out avg, out p1, out p01);
                        snap.AvgFps = avg;
                        snap.P1LowFps = p1;
                        snap.P01LowFps = p01;
                    }
                }
            }

            return snap;
        }
    }

    /// <summary>
    /// Native zero-dependency Windows ETW real-time trace host for Present-to-Display Latency.
    /// Consumes Microsoft-Windows-DXGI and Microsoft-Windows-DxgKrnl kernel events without
    /// injecting DLLs into games (100% anti-cheat safe).
    /// </summary>
    public static class EtwLatencyMonitor
    {
        public const string SESSION_NAME = "MiniFanControl_PresentETW";

        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const byte TRACE_LEVEL_INFORMATION = 4;

        // DxgKrnl Base (0x1) | Present (0x40) keeps ETW overhead tiny (< 0.2% CPU)
        private const ulong DXGKRNL_PRESENT_KEYWORDS = 0x41UL;
        private const ulong DXGI_ALL_KEYWORDS = 0xFFFFFFFFFFFFFFFFUL;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void EventRecordCallbackDelegate(IntPtr eventRecord);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SYSTEMTIME
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TIME_ZONE_INFORMATION
        {
            public uint Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string StandardName;
            public SYSTEMTIME StandardDate;
            public uint StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DaylightName;
            public SYSTEMTIME DaylightDate;
            public uint DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public uint KernelTime;
            public uint UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint BufferContext;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TRACE_LOGFILE_HEADER_64
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public uint StartBuffers;
            public uint PointerSize;
            public uint EventsLost;
            public uint CpuSpeedInMHz;
            public ulong LoggerName;
            public ulong LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct EVENT_TRACE_LOGFILE_64
        {
            public IntPtr LogFileName;
            public IntPtr LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER_64 LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(
            ulong traceHandle,
            ref Guid providerId,
            uint controlCode,
            byte level,
            ulong matchAnyKeyword,
            ulong matchAllKeyword,
            uint timeout,
            IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILE_64 logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong traceHandle);

        private static readonly ulong INVALID_PROCESSTRACE_HANDLE = unchecked((ulong)-1L);
        private static readonly object _sync = new object();
        private static readonly EtwPresentTracker _tracker = new EtwPresentTracker();
        private static readonly double _qpcFreq = (double)Stopwatch.Frequency;
        private static readonly long _dxgiGuidLo = BitConverter.ToInt64(EtwPresentTracker.ProviderDxgi.ToByteArray(), 0);
        private static readonly long _dxgiGuidHi = BitConverter.ToInt64(EtwPresentTracker.ProviderDxgi.ToByteArray(), 8);
        private static readonly long _dxgKrnlGuidLo = BitConverter.ToInt64(EtwPresentTracker.ProviderDxgKrnl.ToByteArray(), 0);
        private static readonly long _dxgKrnlGuidHi = BitConverter.ToInt64(EtwPresentTracker.ProviderDxgKrnl.ToByteArray(), 8);

        private static bool _attemptedStart = false;
        private static volatile bool _running = false;
        private static ulong _sessionHandle = 0;
        private static ulong _consumerHandle = 0;
        private static IntPtr _loggerNamePtr = IntPtr.Zero;
        private static EventRecordCallbackDelegate _pinnedCallback = null;
        private static Thread _consumerThread = null;

        public static bool IsActive { get { return _running; } }
        public static string LastDiagnostic { get; private set; }

        static EtwLatencyMonitor()
        {
            LastDiagnostic = "Not started";
        }

        private static IntPtr AllocTracePropertiesBuffer(out int totalBytes)
        {
            // Fixed EVENT_TRACE_PROPERTIES header on x64 is 120 bytes; allocate +512 bytes for session name
            totalBytes = 120 + 512;
            IntPtr buf = Marshal.AllocHGlobal(totalBytes);
            for (int i = 0; i < totalBytes; i++) Marshal.WriteByte(buf, i, 0);

            Marshal.WriteInt32(buf, 0, totalBytes);                      // Wnode.BufferSize
            Marshal.WriteInt32(buf, 40, 1);                              // Wnode.ClientContext = 1 (QPC)
            Marshal.WriteInt32(buf, 44, unchecked((int)WNODE_FLAG_TRACED_GUID)); // Wnode.Flags
            Marshal.WriteInt32(buf, 48, 64);                             // BufferSize (64 KB)
            Marshal.WriteInt32(buf, 52, 4);                              // MinimumBuffers
            Marshal.WriteInt32(buf, 56, 16);                             // MaximumBuffers
            Marshal.WriteInt32(buf, 64, unchecked((int)EVENT_TRACE_REAL_TIME_MODE)); // LogFileMode
            Marshal.WriteInt32(buf, 68, 1);                              // FlushTimer (1s)
            Marshal.WriteInt32(buf, 112, 0);                             // LogFileNameOffset
            Marshal.WriteInt32(buf, 116, 120);                           // LoggerNameOffset
            return buf;
        }

        /// <summary>
        /// Starts the real-time DXGI/DxgKrnl ETW session once (requires x64 Windows & Performance Log Users / Admin).
        /// Fails gracefully with zero exceptions when unprivileged or in headless CI.
        /// </summary>
        public static bool EnsureStarted()
        {
            if (_running) return true;
            if (_attemptedStart) return false;
            if (IntPtr.Size != 8)
            {
                _attemptedStart = true;
                LastDiagnostic = "ETW latency monitor requires 64-bit Windows";
                return false;
            }

            lock (_sync)
            {
                if (_running) return true;
                if (_attemptedStart) return false;
                _attemptedStart = true;

                IntPtr props = IntPtr.Zero;
                try
                {
                    int totalBytes;
                    props = AllocTracePropertiesBuffer(out totalBytes);

                    // Clean up any orphaned session from a previous crash
                    ControlTraceW(0, SESSION_NAME, props, EVENT_TRACE_CONTROL_STOP);

                    Marshal.FreeHGlobal(props);
                    props = AllocTracePropertiesBuffer(out totalBytes);

                    ulong sessHandle;
                    uint startErr = StartTraceW(out sessHandle, SESSION_NAME, props);
                    if (startErr != 0)
                    {
                        LastDiagnostic = "ETW StartTrace unavailable (code " + startErr + " — run as Admin for live Display Latency)";
                        return false;
                    }

                    Guid gDxgi = EtwPresentTracker.ProviderDxgi;
                    Guid gDxgKrnl = EtwPresentTracker.ProviderDxgKrnl;
                    EnableTraceEx2(sessHandle, ref gDxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, DXGI_ALL_KEYWORDS, 0, 0, IntPtr.Zero);
                    EnableTraceEx2(sessHandle, ref gDxgKrnl, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, DXGKRNL_PRESENT_KEYWORDS, 0, 0, IntPtr.Zero);

                    _pinnedCallback = new EventRecordCallbackDelegate(OnEventRecord);
                    _loggerNamePtr = Marshal.StringToHGlobalUni(SESSION_NAME);

                    EVENT_TRACE_LOGFILE_64 logFile = new EVENT_TRACE_LOGFILE_64();
                    logFile.LoggerName = _loggerNamePtr;
                    logFile.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;
                    logFile.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_pinnedCallback);

                    ulong consHandle = OpenTraceW(ref logFile);
                    if (consHandle == 0 || consHandle == INVALID_PROCESSTRACE_HANDLE)
                    {
                        ControlTraceW(sessHandle, SESSION_NAME, props, EVENT_TRACE_CONTROL_STOP);
                        if (_loggerNamePtr != IntPtr.Zero)
                        {
                            try { Marshal.FreeHGlobal(_loggerNamePtr); } catch { }
                            _loggerNamePtr = IntPtr.Zero;
                        }
                        _pinnedCallback = null;
                        LastDiagnostic = "ETW OpenTrace failed (Win32 " + Marshal.GetLastWin32Error() + ")";
                        return false;
                    }

                    _sessionHandle = sessHandle;
                    _consumerHandle = consHandle;
                    _running = true;
                    LastDiagnostic = "Active (DXGI + DxgKrnl QPC ETW)";

                    _consumerThread = new Thread(ConsumerLoop)
                    {
                        IsBackground = true,
                        Name = "MiniFanControl.EtwLatencyConsumer",
                        Priority = ThreadPriority.BelowNormal
                    };
                    _consumerThread.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    LastDiagnostic = "ETW init error: " + ex.Message;
                    return false;
                }
                finally
                {
                    if (props != IntPtr.Zero) Marshal.FreeHGlobal(props);
                }
            }
        }

        private static void ConsumerLoop()
        {
            try
            {
                ulong[] handles = new ulong[] { _consumerHandle };
                ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            finally
            {
                _running = false;
            }
        }

        private static void OnEventRecord(IntPtr pEventRecord)
        {
            if (!_running || pEventRecord == IntPtr.Zero) return;
            try
            {
                // EVENT_HEADER x64 verified byte offsets (zero managed heap allocations per callback):
                // 12: uint ProcessId | 16: long TimeStamp (QPC) | 24: Guid ProviderId (16 bytes) | 40: ushort EventId
                long gLo = Marshal.ReadInt64(pEventRecord, 24);
                long gHi = Marshal.ReadInt64(pEventRecord, 32);
                bool isDxgi = (gLo == _dxgiGuidLo && gHi == _dxgiGuidHi);
                bool isDxgKrnl = !isDxgi && (gLo == _dxgKrnlGuidLo && gHi == _dxgKrnlGuidHi);
                if (!isDxgi && !isDxgKrnl) return;

                uint pid = unchecked((uint)Marshal.ReadInt32(pEventRecord, 12));
                long qpc = Marshal.ReadInt64(pEventRecord, 16);
                ushort eventId = unchecked((ushort)Marshal.ReadInt16(pEventRecord, 40));

                _tracker.ProcessEvent(
                    isDxgi ? EtwPresentTracker.ProviderDxgi : EtwPresentTracker.ProviderDxgKrnl,
                    eventId, pid, qpc, _qpcFreq);
            }
            catch { }
        }

        public static EtwLatencyTelemetry GetTelemetry(bool allowAutoStart = true)
        {
            if (allowAutoStart && !_running && !_attemptedStart)
            {
                EnsureStarted();
            }
            return _tracker.GetSnapshot(Stopwatch.GetTimestamp(), _qpcFreq, _running);
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                _running = false;
                // Stop the kernel ETW session BEFORE CloseTrace so ProcessTrace drains and exits cleanly.
                if (_sessionHandle != 0)
                {
                    IntPtr props = IntPtr.Zero;
                    try
                    {
                        int totalBytes;
                        props = AllocTracePropertiesBuffer(out totalBytes);
                        ControlTraceW(_sessionHandle, SESSION_NAME, props, EVENT_TRACE_CONTROL_STOP);
                    }
                    catch { }
                    finally
                    {
                        if (props != IntPtr.Zero) Marshal.FreeHGlobal(props);
                    }
                    _sessionHandle = 0;
                }
                if (_consumerHandle != 0 && _consumerHandle != INVALID_PROCESSTRACE_HANDLE)
                {
                    try { CloseTrace(_consumerHandle); } catch { }
                    _consumerHandle = 0;
                }
                bool consumerExited = true;
                if (_consumerThread != null && _consumerThread.IsAlive)
                {
                    try { consumerExited = _consumerThread.Join(1500); } catch { consumerExited = false; }
                    if (consumerExited) _consumerThread = null;
                }
                else
                {
                    _consumerThread = null;
                }
                // Only release unmanaged logger name & pinned delegate after ProcessTrace has returned
                // so native advapi32 code can never invoke a collected callback.
                if (consumerExited)
                {
                    if (_loggerNamePtr != IntPtr.Zero)
                    {
                        try { Marshal.FreeHGlobal(_loggerNamePtr); } catch { }
                        _loggerNamePtr = IntPtr.Zero;
                    }
                    _pinnedCallback = null;
                }
                _attemptedStart = false;
                _tracker.Reset();
            }
        }
    }
}
