using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniFanControl
{
    public struct NvmeDiskInfo
    {
        public int DiskIndex;
        public string Model;
        public string SerialNumber;
        public int CompositeTemp;
        public int HealthPct;
        public byte CriticalWarning;
        public byte AvailableSpare;
        public byte PercentageUsed;
        public double ReadRateMBs;
        public double WriteRateMBs;
        public int QueueDepth;
    }

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

        // Multi-NVMe & Real-Time Throughput
        public int DriveCount;
        public double TotalReadRateMBs;
        public double TotalWriteRateMBs;
        public int QueueDepth;

        // Drive 2 (Slot 2) if installed
        public int Drive2Temp;
        public int Drive2HealthPct;
        public string Drive2Model;
        public double Drive2ReadRateMBs;
        public double Drive2WriteRateMBs;
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
    /// Uses standard Win32 kernel32 APIs (GlobalMemoryStatusEx and DeviceIoControl with IOCTL_STORAGE_QUERY_PROPERTY & IOCTL_DISK_PERFORMANCE).
    /// Bypasses raw PCI port I/O to avoid controller contention or bus freezes on AMD Ryzen platforms.
    /// Accommodates multiple NVMe M.2 drives (PhysicalDrive0-3 / C: volume fallback).
    /// </summary>
    public static class SystemStorageMemorySensor
    {
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

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

        private class DriveIoTracker
        {
            public long PrevBytesRead;
            public long PrevBytesWritten;
            public DateTime PrevSampleUtc = DateTime.MinValue;
            public double ReadRateMBs;
            public double WriteRateMBs;
            public int QueueDepth;
            public string Model;
            public string Serial;
            public bool StaticProbed;

            // Cached SMART health values to avoid querying the disk log page on every poll
            public int CachedTemp;
            public int CachedHealthPct;
            public byte CachedCritWarn;
            public byte CachedAvailSpare;
            public byte CachedPctUsed;
            public bool HasCachedSmart;
        }

        private static readonly DriveIoTracker[] _trackers = new DriveIoTracker[4]
        {
            new DriveIoTracker(), new DriveIoTracker(), new DriveIoTracker(), new DriveIoTracker()
        };
        private static readonly DriveIoTracker _cDriveTracker = new DriveIoTracker();

        private static NvmeTelemetry _cachedNvme = new NvmeTelemetry { IsAvailable = false };
        private static readonly RefreshThrottle _nvmePollThrottle = new RefreshThrottle(TimeSpan.FromMilliseconds(400));
        private static readonly RefreshThrottle _nvmeSmartThrottle = new RefreshThrottle(TimeSpan.FromSeconds(3.0));
        private static readonly object _syncLock = new object();

        // High-Precision Native CPU Usage Tracking (kernel32 GetSystemTimes)
        private static long _prevIdle = 0;
        private static long _prevKernel = 0;
        private static long _prevUser = 0;
        private static int _lastCpuLoad = 0;
        private static bool _firstCpuSample = true;
        private static readonly object _cpuLock = new object();

        /// <summary>
        /// Queries total real-time system CPU utilization percentage (0-100%) via GetSystemTimes.
        /// Zero allocation, sub-microsecond latency, no WMI or COM dependencies.
        /// </summary>
        public static int GetCpuUsage()
        {
            try
            {
                lock (_cpuLock)
                {
                    long idle, kernel, user;
                    if (!GetSystemTimes(out idle, out kernel, out user))
                    {
                        return _lastCpuLoad;
                    }

                    if (_firstCpuSample)
                    {
                        _prevIdle = idle;
                        _prevKernel = kernel;
                        _prevUser = user;
                        _firstCpuSample = false;
                        return _lastCpuLoad;
                    }

                    long dIdle = idle - _prevIdle;
                    long dKernel = kernel - _prevKernel;
                    long dUser = user - _prevUser;

                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;

                    long dTotal = dKernel + dUser;
                    if (dTotal > 0)
                    {
                        long dBusy = dTotal - dIdle;
                        if (dBusy < 0) dBusy = 0;
                        int pct = (int)Math.Round((double)(dBusy * 100) / dTotal);
                        _lastCpuLoad = Math.Max(0, Math.Min(100, pct));
                    }
                    return _lastCpuLoad;
                }
            }
            catch
            {
                return _lastCpuLoad;
            }
        }

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

            // 2. Query NVMe Storage (IOCTL_STORAGE_QUERY_PROPERTY & IOCTL_DISK_PERFORMANCE)
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
            DateTime now = DateTime.UtcNow;
            if (!_nvmePollThrottle.TryBegin() && _cachedNvme.IsAvailable)
            {
                nvme = _cachedNvme;
                return;
            }

            bool querySmartNow = _nvmeSmartThrottle.TryBegin();
            List<NvmeDiskInfo> activeDisks = new List<NvmeDiskInfo>(4);

            // Probe PhysicalDrive0 through PhysicalDrive3
            bool drive0Found = false;
            for (int i = 0; i < 4; i++)
            {
                string path = string.Format(@"\\.\PhysicalDrive{0}", i);
                using (SafeFileHandle h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;

                    DriveIoTracker tracker = _trackers[i];
                    if (!tracker.StaticProbed)
                    {
                        string m = null, s = null;
                        ProbeStaticDeviceInfo(h, ref m, ref s);
                        if (!string.IsNullOrEmpty(m)) tracker.Model = m;
                        if (!string.IsNullOrEmpty(s)) tracker.Serial = s;
                        tracker.StaticProbed = true;
                    }

                    NvmeDiskInfo disk = new NvmeDiskInfo
                    {
                        DiskIndex = i,
                        Model = tracker.Model ?? "NVMe SSD",
                        SerialNumber = tracker.Serial ?? ""
                    };

                    if (querySmartNow || !tracker.HasCachedSmart)
                    {
                        QueryNvmeHealth(h, ref disk);
                        tracker.CachedTemp = disk.CompositeTemp;
                        tracker.CachedHealthPct = disk.HealthPct;
                        tracker.CachedCritWarn = disk.CriticalWarning;
                        tracker.CachedAvailSpare = disk.AvailableSpare;
                        tracker.CachedPctUsed = disk.PercentageUsed;
                        tracker.HasCachedSmart = true;
                    }
                    else
                    {
                        disk.CompositeTemp = tracker.CachedTemp;
                        disk.HealthPct = tracker.CachedHealthPct;
                        disk.CriticalWarning = tracker.CachedCritWarn;
                        disk.AvailableSpare = tracker.CachedAvailSpare;
                        disk.PercentageUsed = tracker.CachedPctUsed;
                    }

                    QueryDiskPerformance(h, tracker, now);

                    disk.ReadRateMBs = tracker.ReadRateMBs;
                    disk.WriteRateMBs = tracker.WriteRateMBs;
                    disk.QueueDepth = tracker.QueueDepth;

                    activeDisks.Add(disk);
                    if (i == 0) drive0Found = true;
                }
            }

            // Fallback for Drive 0 if unprivileged or physical handle blocked: query \\.\C:
            if (!drive0Found)
            {
                using (SafeFileHandle h = CreateFile(@"\\.\C:", 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (!h.IsInvalid)
                    {
                        DriveIoTracker tracker = _cDriveTracker;
                        if (!tracker.StaticProbed)
                        {
                            string m = null, s = null;
                            ProbeStaticDeviceInfo(h, ref m, ref s);
                            if (!string.IsNullOrEmpty(m)) tracker.Model = m;
                            if (!string.IsNullOrEmpty(s)) tracker.Serial = s;
                            tracker.StaticProbed = true;
                        }

                        NvmeDiskInfo disk = new NvmeDiskInfo
                        {
                            DiskIndex = 0,
                            Model = tracker.Model ?? "NVMe SSD",
                            SerialNumber = tracker.Serial ?? ""
                        };

                        if (querySmartNow || !tracker.HasCachedSmart)
                        {
                            QueryNvmeHealth(h, ref disk);
                            tracker.CachedTemp = disk.CompositeTemp;
                            tracker.CachedHealthPct = disk.HealthPct;
                            tracker.CachedCritWarn = disk.CriticalWarning;
                            tracker.CachedAvailSpare = disk.AvailableSpare;
                            tracker.CachedPctUsed = disk.PercentageUsed;
                            tracker.HasCachedSmart = true;
                        }
                        else
                        {
                            disk.CompositeTemp = tracker.CachedTemp;
                            disk.HealthPct = tracker.CachedHealthPct;
                            disk.CriticalWarning = tracker.CachedCritWarn;
                            disk.AvailableSpare = tracker.CachedAvailSpare;
                            disk.PercentageUsed = tracker.CachedPctUsed;
                        }

                        QueryDiskPerformance(h, tracker, now);

                        disk.ReadRateMBs = tracker.ReadRateMBs;
                        disk.WriteRateMBs = tracker.WriteRateMBs;
                        disk.QueueDepth = tracker.QueueDepth;

                        activeDisks.Insert(0, disk);
                    }
                }
            }

            if (activeDisks.Count == 0)
            {
                nvme = _cachedNvme;
                return;
            }

            nvme.IsAvailable = true;
            nvme.DriveCount = activeDisks.Count;

            NvmeDiskInfo primary = activeDisks[0];
            nvme.Model = primary.Model;
            nvme.SerialNumber = primary.SerialNumber;
            nvme.CompositeTemp = primary.CompositeTemp;
            nvme.HealthPct = primary.HealthPct;
            nvme.CriticalWarning = primary.CriticalWarning;
            nvme.AvailableSpare = primary.AvailableSpare;
            nvme.PercentageUsed = primary.PercentageUsed;

            double totRead = 0.0;
            double totWrite = 0.0;
            int totQd = 0;
            for (int k = 0; k < activeDisks.Count; k++)
            {
                totRead += activeDisks[k].ReadRateMBs;
                totWrite += activeDisks[k].WriteRateMBs;
                totQd += activeDisks[k].QueueDepth;
            }
            nvme.TotalReadRateMBs = Math.Round(totRead, 1);
            nvme.TotalWriteRateMBs = Math.Round(totWrite, 1);
            nvme.QueueDepth = totQd;

            if (activeDisks.Count > 1)
            {
                NvmeDiskInfo sec = activeDisks[1];
                nvme.Drive2Temp = sec.CompositeTemp;
                nvme.Drive2HealthPct = sec.HealthPct;
                nvme.Drive2Model = sec.Model;
                nvme.Drive2ReadRateMBs = sec.ReadRateMBs;
                nvme.Drive2WriteRateMBs = sec.WriteRateMBs;

                // For composite thermal tracking: report the higher of the two drives so fans respond appropriately
                if (sec.CompositeTemp > nvme.CompositeTemp)
                {
                    nvme.CompositeTemp = sec.CompositeTemp;
                }
            }

            _cachedNvme = nvme;
        }

        private static void ProbeStaticDeviceInfo(SafeFileHandle h, ref string outModel, ref string outSerial)
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
                    // Ensure null-termination within the returned byte window
                    if (ret < outSize) Marshal.WriteByte(outBuf, (int)ret, 0);
                    else Marshal.WriteByte(outBuf, outSize - 1, 0);

                    int productOffset = Marshal.ReadInt32(outBuf, 16);
                    int snOffset = Marshal.ReadInt32(outBuf, 24);

                    if (productOffset > 0 && productOffset < ret)
                    {
                        string prod = Marshal.PtrToStringAnsi(new IntPtr(outBuf.ToInt64() + productOffset));
                        if (!string.IsNullOrEmpty(prod)) outModel = prod.Trim();
                    }
                    if (snOffset > 0 && snOffset < ret)
                    {
                        string sn = Marshal.PtrToStringAnsi(new IntPtr(outBuf.ToInt64() + snOffset));
                        if (!string.IsNullOrEmpty(sn)) outSerial = sn.Trim();
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }

        private static bool QueryNvmeHealth(SafeFileHandle h, ref NvmeDiskInfo disk)
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

                        disk.CriticalWarning = crit;
                        disk.CompositeTemp = compTempK > 0 ? compTempK - 273 : 0;
                        disk.AvailableSpare = availSpare;
                        disk.PercentageUsed = pctUsed;
                        disk.HealthPct = Math.Max(0, Math.Min(100, 100 - (int)pctUsed));
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }
        }

        private static void QueryDiskPerformance(SafeFileHandle h, DriveIoTracker tracker, DateTime now)
        {
            IntPtr buf = Marshal.AllocHGlobal(128);
            try
            {
                uint ret;
                if (DeviceIoControl(h, IOCTL_DISK_PERFORMANCE, IntPtr.Zero, 0, buf, 128, out ret, IntPtr.Zero) && ret >= 16)
                {
                    long bytesRead = Marshal.ReadInt64(buf, 0);
                    long bytesWritten = Marshal.ReadInt64(buf, 8);
                    int qDepth = (ret >= 52) ? Marshal.ReadInt32(buf, 48) : 0;
                    tracker.QueueDepth = Math.Max(0, qDepth);

                    if (tracker.PrevSampleUtc != DateTime.MinValue)
                    {
                        double dt = (now - tracker.PrevSampleUtc).TotalSeconds;
                        if (dt >= 0.1 && dt <= 10.0)
                        {
                            long dRead = bytesRead - tracker.PrevBytesRead;
                            long dWrite = bytesWritten - tracker.PrevBytesWritten;
                            if (dRead >= 0)
                                tracker.ReadRateMBs = Math.Round((dRead / (1024.0 * 1024.0)) / dt, 1);
                            if (dWrite >= 0)
                                tracker.WriteRateMBs = Math.Round((dWrite / (1024.0 * 1024.0)) / dt, 1);
                        }
                    }
                    tracker.PrevBytesRead = bytesRead;
                    tracker.PrevBytesWritten = bytesWritten;
                    tracker.PrevSampleUtc = now;
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
