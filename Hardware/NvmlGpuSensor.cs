using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniFanControl
{
    public struct NvmlTelemetry
    {
        public bool IsAvailable;
        public string GpuName;
        public int GpuTempC;
        public int GpuLoadPct;
        public int PowerWatts;
        public int PowerLimitWatts;
        public int GraphicsClockMhz;
        public int MemoryClockMhz;
        public int FanSpeedPct;
    }

    /// <summary>
    /// Zero-dependency read-only NVIDIA GPU telemetry via NVIDIA's official WHQL driver library (nvml.dll).
    /// 100% HVCI-compatible and anti-cheat-safe; loads nvml.dll strictly from System32 or Program Files\NVIDIA Corporation\NVSMI.
    /// Enables accurate desktop GPU monitoring (temp, power, limit, clocks, utilization) on NVIDIA GeForce / RTX systems.
    /// </summary>
    public class NvmlGpuSensor : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32     = 0x00000800;
        private const uint SAFE_NVML_LOAD_FLAGS             = LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32;

        private const uint NVML_TEMPERATURE_GPU = 0;
        private const uint NVML_CLOCK_GRAPHICS  = 0;
        private const uint NVML_CLOCK_MEM       = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint Gpu;
            public uint Memory;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetCountDelegate(out uint deviceCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetNameDelegate(IntPtr device, [Out] byte[] name, uint length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, uint sensorType, out uint temp);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, out NvmlUtilization utilization);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetPowerUsageDelegate(IntPtr device, out uint powerMw);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetPowerManagementLimitDelegate(IntPtr device, out uint limitMw);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetClockInfoDelegate(IntPtr device, uint clockType, out uint clockMhz);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvmlDeviceGetFanSpeedDelegate(IntPtr device, out uint speedPct);

        internal static Func<NvmlTelemetry> SimulatedTelemetryHook = null;

        private bool _initAttempted = false;
        private bool _initialized = false;
        private IntPtr _hLib = IntPtr.Zero;
        private IntPtr _hDevice = IntPtr.Zero;
        private string _cachedGpuName = null;

        private NvmlShutdownDelegate _fnShutdown;
        private NvmlDeviceGetTemperatureDelegate _fnGetTemp;
        private NvmlDeviceGetUtilizationRatesDelegate _fnGetUtil;
        private NvmlDeviceGetPowerUsageDelegate _fnGetPower;
        private NvmlDeviceGetPowerManagementLimitDelegate _fnGetPowerLimit;
        private NvmlDeviceGetClockInfoDelegate _fnGetClock;
        private NvmlDeviceGetFanSpeedDelegate _fnGetFan;

        public static string[] GetTrustedNvmlPaths(string systemDir = null, string programFilesDir = null)
        {
            string sys = !string.IsNullOrEmpty(systemDir) ? systemDir : Environment.SystemDirectory;
            string pf = programFilesDir != null ? programFilesDir : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return new string[]
            {
                string.IsNullOrEmpty(sys) ? "" : Path.Combine(sys, "nvml.dll"),
                string.IsNullOrEmpty(pf) ? "" : Path.Combine(pf, "NVIDIA Corporation", "NVSMI", "nvml.dll")
            };
        }

        private static T GetProc<T>(IntPtr hLib, string name) where T : class
        {
            IntPtr p = GetProcAddress(hLib, name);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        private void EnsureInitialized()
        {
            if (_initAttempted) return;
            _initAttempted = true;

            try
            {
                string[] paths = GetTrustedNvmlPaths();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (string.IsNullOrEmpty(paths[i]) || !File.Exists(paths[i])) continue;
                    IntPtr h = LoadLibraryEx(paths[i], IntPtr.Zero, SAFE_NVML_LOAD_FLAGS);
                    if (h != IntPtr.Zero)
                    {
                        _hLib = h;
                        break;
                    }
                }
                if (_hLib == IntPtr.Zero) return;

                var fnInit = GetProc<NvmlInitDelegate>(_hLib, "nvmlInit_v2") ?? GetProc<NvmlInitDelegate>(_hLib, "nvmlInit");
                _fnShutdown = GetProc<NvmlShutdownDelegate>(_hLib, "nvmlShutdown");
                var fnCount = GetProc<NvmlDeviceGetCountDelegate>(_hLib, "nvmlDeviceGetCount_v2") ?? GetProc<NvmlDeviceGetCountDelegate>(_hLib, "nvmlDeviceGetCount");
                var fnHandle = GetProc<NvmlDeviceGetHandleByIndexDelegate>(_hLib, "nvmlDeviceGetHandleByIndex_v2") ?? GetProc<NvmlDeviceGetHandleByIndexDelegate>(_hLib, "nvmlDeviceGetHandleByIndex");
                var fnName = GetProc<NvmlDeviceGetNameDelegate>(_hLib, "nvmlDeviceGetName");

                if (fnInit == null || fnCount == null || fnHandle == null || fnInit() != 0)
                {
                    CleanupLibrary();
                    return;
                }

                uint count;
                if (fnCount(out count) != 0 || count == 0)
                {
                    try { if (_fnShutdown != null) _fnShutdown(); } catch { }
                    CleanupLibrary();
                    return;
                }

                IntPtr dev;
                if (fnHandle(0, out dev) != 0 || dev == IntPtr.Zero)
                {
                    try { if (_fnShutdown != null) _fnShutdown(); } catch { }
                    CleanupLibrary();
                    return;
                }

                _hDevice = dev;
                if (fnName != null)
                {
                    byte[] buf = new byte[96];
                    if (fnName(_hDevice, buf, (uint)buf.Length) == 0)
                    {
                        int z = Array.IndexOf(buf, (byte)0);
                        _cachedGpuName = Encoding.ASCII.GetString(buf, 0, z >= 0 ? z : buf.Length).Trim();
                    }
                }

                _fnGetTemp = GetProc<NvmlDeviceGetTemperatureDelegate>(_hLib, "nvmlDeviceGetTemperature");
                _fnGetUtil = GetProc<NvmlDeviceGetUtilizationRatesDelegate>(_hLib, "nvmlDeviceGetUtilizationRates");
                _fnGetPower = GetProc<NvmlDeviceGetPowerUsageDelegate>(_hLib, "nvmlDeviceGetPowerUsage");
                _fnGetPowerLimit = GetProc<NvmlDeviceGetPowerManagementLimitDelegate>(_hLib, "nvmlDeviceGetPowerManagementLimit");
                _fnGetClock = GetProc<NvmlDeviceGetClockInfoDelegate>(_hLib, "nvmlDeviceGetClockInfo");
                _fnGetFan = GetProc<NvmlDeviceGetFanSpeedDelegate>(_hLib, "nvmlDeviceGetFanSpeed");
                _initialized = true;
            }
            catch
            {
                CleanupLibrary();
            }
        }

        private void CleanupLibrary()
        {
            _initialized = false;
            _hDevice = IntPtr.Zero;
            _fnShutdown = null;
            _fnGetTemp = null;
            _fnGetUtil = null;
            _fnGetPower = null;
            _fnGetPowerLimit = null;
            _fnGetClock = null;
            _fnGetFan = null;
            if (_hLib != IntPtr.Zero)
            {
                try { FreeLibrary(_hLib); } catch { }
                _hLib = IntPtr.Zero;
            }
        }

        public NvmlTelemetry ReadTelemetry()
        {
            if (SimulatedTelemetryHook != null)
            {
                try { return SimulatedTelemetryHook(); } catch { return default(NvmlTelemetry); }
            }

            NvmlTelemetry t = new NvmlTelemetry { GpuLoadPct = -1 };
            EnsureInitialized();
            if (!_initialized || _hDevice == IntPtr.Zero) return t;

            try
            {
                t.IsAvailable = true;
                t.GpuName = _cachedGpuName;

                uint uVal;
                if (_fnGetTemp != null && _fnGetTemp(_hDevice, NVML_TEMPERATURE_GPU, out uVal) == 0 && uVal > 0 && uVal <= 120)
                    t.GpuTempC = (int)uVal;

                NvmlUtilization util;
                if (_fnGetUtil != null && _fnGetUtil(_hDevice, out util) == 0 && util.Gpu <= 100)
                    t.GpuLoadPct = (int)util.Gpu;

                if (_fnGetPower != null && _fnGetPower(_hDevice, out uVal) == 0 && uVal > 0 && uVal < 1500000)
                    t.PowerWatts = (int)Math.Round(uVal / 1000.0);

                if (_fnGetPowerLimit != null && _fnGetPowerLimit(_hDevice, out uVal) == 0 && uVal > 0 && uVal < 1500000)
                    t.PowerLimitWatts = (int)Math.Round(uVal / 1000.0);

                if (_fnGetClock != null && _fnGetClock(_hDevice, NVML_CLOCK_GRAPHICS, out uVal) == 0 && uVal <= 6000)
                    t.GraphicsClockMhz = (int)uVal;

                if (_fnGetClock != null && _fnGetClock(_hDevice, NVML_CLOCK_MEM, out uVal) == 0 && uVal <= 30000)
                    t.MemoryClockMhz = (int)uVal;

                if (_fnGetFan != null && _fnGetFan(_hDevice, out uVal) == 0 && uVal <= 100)
                    t.FanSpeedPct = (int)uVal;
            }
            catch { }

            return t;
        }

        public void Dispose()
        {
            if (_initialized && _fnShutdown != null)
            {
                try { _fnShutdown(); } catch { }
            }
            CleanupLibrary();
        }
    }
}
