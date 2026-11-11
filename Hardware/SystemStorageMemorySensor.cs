using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniFanControl
{
    public struct NvmeTelemetry
    {
        public bool IsAvailable;
        public string Model;
        public string SerialNumber;
        public int CompositeTemp;
        public int HealthPct;
        public byte CriticalWarning;
        public byte AvailableSpare;
        public byte PercentageUsed;
    }

    public struct MemoryTelemetry
    {
        public bool IsAvailable;
        public int LoadPct;
        public double TotalGb;
        public double UsedGb;
        public double AvailGb;
    }

    /// <summary>
    /// Native Windows NVMe Storage and System Memory Telemetry Sensor.
    /// Uses standard Win32 kernel32 APIs (GlobalMemoryStatusEx and DeviceIoControl with IOCTL_STORAGE_QUERY_PROPERTY).
    /// Bypasses raw PCI port I/O to avoid controller contention or bus freezes on AMD Ryzen platforms.
    /// </summary>
    public static class SystemStorageMemorySensor
    {
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const int StorageDeviceProperty = 0;
        private const int StorageDeviceProtocolSpecificProperty = 48;
        private const int StorageAdapterProtocolSpecificProperty = 49;
        private const int ProtocolTypeNvme = 3;
        private const int NVMeDataTypeLogPage = 2;
        private const int NVME_LOG_PAGE_HEALTH_INFO = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private static string _cachedModel = null;
        private static string _cachedSerial = null;
        private static bool _staticInfoProbed = false;
        private static NvmeTelemetry _cachedNvme = new NvmeTelemetry { IsAvailable = false };
        private static DateTime _lastNvmeQueryUtc = DateTime.MinValue;
        private static readonly object _syncLock = new object();

        public static void Sample(out NvmeTelemetry nvme, out MemoryTelemetry mem)
        {
            nvme = default(NvmeTelemetry);
            mem = default(MemoryTelemetry);

            // 1. Query System RAM (GlobalMemoryStatusEx)
            try
            {
                var m = new MEMORYSTATUSEX();
                m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref m))
                {
                    mem.IsAvailable = true;
                    mem.LoadPct = (int)m.dwMemoryLoad;
                    mem.TotalGb = m.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    mem.AvailGb = m.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    mem.UsedGb = Math.Max(0.0, mem.TotalGb - mem.AvailGb);
                }
            }
            catch { }

            // 2. Query NVMe Storage (IOCTL_STORAGE_QUERY_PROPERTY)
            try
            {
                lock (_syncLock)
                {
                    SampleNvme(ref nvme);
                }
            }
            catch { }
        }

        private static void SampleNvme(ref NvmeTelemetry nvme)
        {
            // Decouple NVMe IOCTL polling: firmware updates SMART composite temp once per ~1 sec.
            // Serve from cache if queried within 850ms to eliminate disk controller bus pressure.
            if ((DateTime.UtcNow - _lastNvmeQueryUtc).TotalMilliseconds < 850 && _cachedNvme.IsAvailable)
            {
                nvme = _cachedNvme;
                return;
            }

            // Probe volume handle first (accessible unprivileged), fallback to physical drive
            string[] candidatePaths = new string[] { @"\\.\C:", @"\\.\PhysicalDrive0" };
            foreach (string path in candidatePaths)
            {
                using (SafeFileHandle h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;

                    if (!_staticInfoProbed)
                    {
                        ProbeStaticDeviceInfo(h);
                    }

                    nvme.Model = _cachedModel ?? "NVMe SSD";
                    nvme.SerialNumber = _cachedSerial ?? "";

                    if (QueryNvmeHealth(h, ref nvme))
                    {
                        _cachedNvme = nvme;
                        _lastNvmeQueryUtc = DateTime.UtcNow;
                        return;
                    }
                }
            }
        }

        private static void ProbeStaticDeviceInfo(SafeFileHandle h)
        {
            int inSize = 12;
            int outSize = 2048;
            IntPtr inBuf = Marshal.AllocHGlobal(inSize);
            IntPtr outBuf = Marshal.AllocHGlobal(outSize);
            try
            {
                Marshal.WriteInt32(inBuf, 0, StorageDeviceProperty);
                Marshal.WriteInt32(inBuf, 4, 0); // StandardQuery
                Marshal.WriteInt32(inBuf, 8, 0);

                uint ret;
                if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, inBuf, (uint)inSize, outBuf, (uint)outSize, out ret, IntPtr.Zero))
                {
                    int productOffset = Marshal.ReadInt32(outBuf, 16);
                    int snOffset = Marshal.ReadInt32(outBuf, 24);

                    if (productOffset > 0 && productOffset < ret)
                    {
                        string prod = Marshal.PtrToStringAnsi(new IntPtr(outBuf.ToInt64() + productOffset));
                        if (!string.IsNullOrEmpty(prod)) _cachedModel = prod.Trim();
                    }
                    if (snOffset > 0 && snOffset < ret)
                    {
                        string sn = Marshal.PtrToStringAnsi(new IntPtr(outBuf.ToInt64() + snOffset));
                        if (!string.IsNullOrEmpty(sn)) _cachedSerial = sn.Trim();
                    }
                    _staticInfoProbed = true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }

        private static bool QueryNvmeHealth(SafeFileHandle h, ref NvmeTelemetry nvme)
        {
            int bufSize = 1024;
            IntPtr inBuf = Marshal.AllocHGlobal(bufSize);
            IntPtr outBuf = Marshal.AllocHGlobal(bufSize);
            try
            {
                int[] queryPropIds = new int[] { StorageAdapterProtocolSpecificProperty, StorageDeviceProtocolSpecificProperty };
                foreach (int propId in queryPropIds)
                {
                    for (int i = 0; i < bufSize; i++) { Marshal.WriteByte(inBuf, i, 0); Marshal.WriteByte(outBuf, i, 0); }

                    // STORAGE_PROPERTY_QUERY
                    Marshal.WriteInt32(inBuf, 0, propId);
                    Marshal.WriteInt32(inBuf, 4, 0); // PropertyStandardQuery

                    // STORAGE_PROTOCOL_SPECIFIC_DATA (offset 8)
                    Marshal.WriteInt32(inBuf, 8, ProtocolTypeNvme); // 3
                    Marshal.WriteInt32(inBuf, 12, NVMeDataTypeLogPage); // 2
                    Marshal.WriteInt32(inBuf, 16, NVME_LOG_PAGE_HEALTH_INFO); // 0x02
                    Marshal.WriteInt32(inBuf, 20, 0); // SubValue
                    Marshal.WriteInt32(inBuf, 24, 40); // ProtocolDataOffset = 40 (sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA))
                    Marshal.WriteInt32(inBuf, 28, 512); // ProtocolDataLength

                    uint ret;
                    if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, inBuf, (uint)bufSize, outBuf, (uint)bufSize, out ret, IntPtr.Zero) && ret >= 88)
                    {
                        int logStart = 8 + 40; // 48
                        byte crit = Marshal.ReadByte(outBuf, logStart + 0);
                        int compTempK = Marshal.ReadInt16(outBuf, logStart + 1);
                        byte availSpare = Marshal.ReadByte(outBuf, logStart + 3);
                        byte pctUsed = Marshal.ReadByte(outBuf, logStart + 5);

                        nvme.IsAvailable = true;
                        nvme.CriticalWarning = crit;
                        nvme.CompositeTemp = compTempK > 0 ? compTempK - 273 : 0;
                        nvme.AvailableSpare = availSpare;
                        nvme.PercentageUsed = pctUsed;
                        nvme.HealthPct = Math.Max(0, Math.Min(100, 100 - (int)pctUsed));
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }
    }
}
