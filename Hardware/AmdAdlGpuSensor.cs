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
            int cpuT, cpuP, gfxP, socP, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt, memClk, hotspot, npuClk, npuAct;
            string src;
            ReadTelemetryDetailed(out igpuTemp, out cpuT, out apuPowerWatts, out cpuP, out gfxP, out socP,
                out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out src,
                out memClk, out hotspot, out npuClk, out npuAct);
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
            int memClk, hotspot, npuClk, npuAct;
            ReadTelemetryDetailed(out igpuTemp, out adlCpuTemp, out apuPowerWatts, out cpuPowerWatts, out gfxPowerWatts, out socPowerWatts,
                out apuPowerLimitWatts, out cpuClockMhz, out gfxClockMhz, out gfxActivityPct, out gfxVoltageMv, out powerSource,
                out memClk, out hotspot, out npuClk, out npuAct);
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
            out string powerSource,
            out int memClockMhz,
            out int gpuHotspotTemp,
            out int npuClockMhz,
            out int npuActivityPct)
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
            memClockMhz = 0;
            gpuHotspotTemp = 0;
            npuClockMhz = 0;
            npuActivityPct = -1;
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

                        gpuHotspotTemp = ReadSensorVal(pm.sensors, ADL_PMLOG_TEMPERATURE_HOTSPOT, 1, 140);
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
                        memClockMhz = ReadSensorVal(pm.sensors, ADL_PMLOG_CLK_MEMCLK, 100, 6000);
                        npuClockMhz = ReadSensorVal(pm.sensors, ADL_PMLOG_CLK_NPUCLK, 50, 4500);
                        npuActivityPct = ReadSensorVal(pm.sensors, ADL_PMLOG_NPU_BUSY_AVG, 0, 100);

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

}
