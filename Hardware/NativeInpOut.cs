using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace MiniFanControl
{
    public enum HardwareDriverEngine
    {
        None = 0,
        WinRing0,
        InpOut64
    }

    /// <summary>
    /// Unified Low-Level Kernel Hardware Access Driver Provider.
    /// Auto-detects and interfaces with InpOut64 (inpoutx64.dll) or WinRing0 1.3.1b (WinRing0x64.dll)
    /// for LPC port I/O, Super I/O, EC, and MMIO register access.
    /// Modern scriptable drivers (PawnIO via PawnIOLib bytecode execution) are planned on the roadmap for HVCI compatibility.
    /// </summary>
    public static class NativeInpOut
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR    = 0x00000100;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint SAFE_DRIVER_LOAD_FLAGS              = LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32;

        public const ulong MIN_SAFE_PHYSICAL_ADDR = 0x000E0000;
        public const uint MAX_SAFE_MAP_SIZE = 16 * 1024 * 1024;
        public const ulong DEFAULT_EC_MMIO_BASE = 0xFE0B0400UL;
        public const uint DEFAULT_EC_MMIO_SIZE = 0x100;

        private static readonly object _physWindowLock = new object();
        private static ulong _registeredEcMmioBase = DEFAULT_EC_MMIO_BASE;
        private static uint _registeredEcMmioSize = DEFAULT_EC_MMIO_SIZE;
        private static ulong _registeredSmuPmTableBase = 0;
        private static uint _registeredSmuPmTableSize = 0;

        /// <summary>
        /// Registers the active verified EC MMIO window (strictly within AMD FCH/EC MMIO space >= 0xFE000000, <= 4 KB).
        /// </summary>
        public static bool RegisterAllowedEcMmioWindow(ulong baseAddr, uint size)
        {
            if (baseAddr < 0xFE000000UL || size == 0 || size > 0x1000 || baseAddr > ulong.MaxValue - size)
                return false;
            lock (_physWindowLock)
            {
                _registeredEcMmioBase = baseAddr;
                _registeredEcMmioSize = size;
            }
            return true;
        }

        /// <summary>
        /// Registers the read-only AMD SMU PM-Table DRAM window returned by the SMU mailbox (<= 16 KB).
        /// </summary>
        public static bool RegisterAllowedSmuPmTableWindow(ulong baseAddr, uint size)
        {
            if (baseAddr < 0x00100000UL || size == 0 || size > 0x4000 || baseAddr > ulong.MaxValue - size)
                return false;
            lock (_physWindowLock)
            {
                _registeredSmuPmTableBase = baseAddr;
                _registeredSmuPmTableSize = size;
            }
            return true;
        }

        internal static void ResetRegisteredPhysicalWindowsForTest()
        {
            lock (_physWindowLock)
            {
                _registeredEcMmioBase = DEFAULT_EC_MMIO_BASE;
                _registeredEcMmioSize = DEFAULT_EC_MMIO_SIZE;
                _registeredSmuPmTableBase = 0;
                _registeredSmuPmTableSize = 0;
            }
        }

        private static bool IsWithinRange(ulong addr, uint size, ulong winBase, uint winSize)
        {
            if (winBase == 0 || winSize == 0) return false;
            if (addr < winBase) return false;
            ulong end = addr + size;
            ulong winEnd = winBase + winSize;
            return end >= addr && end <= winEnd;
        }

        /// <summary>
        /// Strict fail-closed physical memory allowlist.
        /// Writes are permitted ONLY inside the verified AMD FCH/EC MMIO register window.
        /// Reads/maps are permitted ONLY inside EC MMIO, PCIe ECAM Bus 0:0:0 config space (4 KB),
        /// or the runtime-registered read-only AMD SMU PM Table buffer.
        /// </summary>
        public static bool IsPhysicalMemoryRangeAllowed(ulong physAddr, uint size, bool isWrite)
        {
            if (size == 0 || size > MAX_SAFE_MAP_SIZE) return false;
            if (physAddr < MIN_SAFE_PHYSICAL_ADDR) return false;
            if (physAddr > ulong.MaxValue - size) return false;

            ulong ecBase, smuBase;
            uint ecSize, smuSize;
            lock (_physWindowLock)
            {
                ecBase = _registeredEcMmioBase;
                ecSize = _registeredEcMmioSize;
                smuBase = _registeredSmuPmTableBase;
                smuSize = _registeredSmuPmTableSize;
            }

            // 1. AMD FCH / ITE Embedded Controller MMIO window (default 0xFE0B0400..0xFE0B04FF or registered verified window)
            if (IsWithinRange(physAddr, size, DEFAULT_EC_MMIO_BASE, DEFAULT_EC_MMIO_SIZE) ||
                IsWithinRange(physAddr, size, ecBase, ecSize))
            {
                return true;
            }

            // Strict write barrier: NEVER permit physical memory writes outside the verified EC MMIO register window
            if (isWrite) return false;

            // 2. Standard PCIe ECAM Bus 0, Device 0, Function 0 config aperture (4 KB each, read/SMN probe)
            if (IsWithinRange(physAddr, size, 0xC0000000UL, 0x1000) ||
                IsWithinRange(physAddr, size, 0xE0000000UL, 0x1000) ||
                IsWithinRange(physAddr, size, 0xF0000000UL, 0x1000) ||
                IsWithinRange(physAddr, size, 0xF8000000UL, 0x1000))
            {
                return true;
            }

            // 3. Runtime-registered read-only AMD SMU PM Table DRAM window (<= 16 KB)
            if (IsWithinRange(physAddr, size, smuBase, smuSize))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verified SHA256 pin for bundled InpOut64 (inpoutx64.dll v1.5.0.0, Phillip Gibbons / Highresolution Enterprises).
        /// Checked fail-closed before LoadLibraryEx to prevent DLL planting / local privilege escalation.
        /// </summary>
        public const string INPOUTX64_SHA256 = "5F27ED4D5CD58A1EE23DEEB802E09E73F3A1D884CE2135F6E827F67B171269E7";

        /// <summary>
        /// Verified SHA256 pins for optional WinRing0 x64 fallback DLL (OpenLibSys 1.2.0.5 / LibreHardwareMonitor).
        /// </summary>
        public static readonly string[] WINRING0X64_ALLOWED_SHA256 = new string[]
        {
            "11BD2C9F9E2397C9A16E0990E4ED2CF0679498FE0FD418A3DFDAC60B5C160EE5"
        };

        private static readonly object _driverLock = new object();
        private static bool _dllSearchHardened = false;
        private static IntPtr _hDriverDll = IntPtr.Zero;

        public static HardwareDriverEngine ActiveDriver { get; private set; }
        public static bool IsDriverHashVerified { get; private set; }
        public static string ActiveDriverSha256 { get; private set; }
        public static string LastDriverIntegrityError { get; private set; }

        /// <summary>
        /// Single fail-closed choke point for all x86 I/O port reads and writes.
        /// Explicitly blocks critical motherboard/CPU control ports and rejects any unlisted port.
        /// </summary>
        public static bool IsPortWhitelisted(ushort port, bool isWrite)
        {
            // Explicit blocklist: Never permit access to critical motherboard/CPU control ports
            if (port == 0x70 || port == 0x71 || port == 0x92 || port == 0xCF9 ||
                port == 0x20 || port == 0x21 || port == 0xA0 || port == 0xA1 ||
                port == 0x40 || port == 0x41 || port == 0x42 || port == 0x43 ||
                port == 0x61)
            {
                return false;
            }

            // Standard ACPI Embedded Controller ports
            if (port == 0x62 || port == 0x66 || port == 0x68 || port == 0x6C || port == 0x6A || port == 0x6E)
                return true;

            // LPC / Super I/O indexing ports
            if (port == 0x2E || port == 0x2F || port == 0x4E || port == 0x4F)
                return true;

            // PCI Configuration Mechanism #1 address and data ports (used strictly for AMD SMN/FCH access)
            if (port == 0xCF8 || port == 0xCFC)
                return true;

            // Hardware monitor base ranges (IT8613E / IT87xx / NCT6779) & AMD SMBus controller
            if ((port >= 0x0290 && port <= 0x0297) ||
                (port >= 0x02A0 && port <= 0x02A7) ||
                (port >= 0x0A00 && port <= 0x0A3F) ||
                (port >= 0x0B00 && port <= 0x0B3F) ||
                (port >= 0x0E00 && port <= 0x0E3F))
            {
                return true;
            }

            // Strict fail-closed: Any unlisted port is rejected for both reads and writes
            return false;
        }

        private static string ComputeStreamSha256(Stream stream)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        public static string ComputeFileSha256(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return ComputeStreamSha256(fs);
            }
        }

        public static bool VerifyFileSha256(string filePath, string[] allowedHashes, out string actualHash)
        {
            actualHash = null;
            if (string.IsNullOrEmpty(filePath) || allowedHashes == null || allowedHashes.Length == 0 || !File.Exists(filePath))
                return false;
            try
            {
                actualHash = ComputeFileSha256(filePath);
                if (string.IsNullOrEmpty(actualHash)) return false;
                for (int i = 0; i < allowedHashes.Length; i++)
                {
                    if (!string.IsNullOrEmpty(allowedHashes[i]) &&
                        actualHash.Equals(allowedHashes[i].Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                actualHash = null;
            }
            return false;
        }

        private static bool IsHashAllowed(string actualHash, string[] allowedHashes)
        {
            if (string.IsNullOrEmpty(actualHash) || allowedHashes == null) return false;
            for (int i = 0; i < allowedHashes.Length; i++)
            {
                if (!string.IsNullOrEmpty(allowedHashes[i]) &&
                    actualHash.Equals(allowedHashes[i].Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static string ActiveDriverName
        {
            get
            {
                switch (ActiveDriver)
                {
                    case HardwareDriverEngine.WinRing0:
                        return "WinRing0 1.3.1b (WinRing0x64.dll)";
                    case HardwareDriverEngine.InpOut64:
                        return "InpOut64 (inpoutx64.dll)";
                    default:
                        return "None / Not Loaded";
                }
            }
        }

        public static bool IsHvciEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("Enabled");
                        if (val is int && (int)val == 1) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        static NativeInpOut()
        {
            ActiveDriver = HardwareDriverEngine.None;
        }

        #region WinRing0 Delegates
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OlsInitializeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void OlsDeinitializeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint OlsGetDllStatusDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate byte OlsReadIoPortByteDelegate(ushort port);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void OlsWriteIoPortByteDelegate(ushort port, byte value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint OlsReadIoPortDwordDelegate(ushort port);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void OlsWriteIoPortDwordDelegate(ushort port, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint OlsReadPhysicalMemoryDelegate(UIntPtr address, [Out] byte[] buffer, uint count, uint unitSize);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint OlsWritePhysicalMemoryDelegate(UIntPtr address, [In] byte[] buffer, uint count, uint unitSize);

        private static OlsInitializeDelegate _winringInit;
        private static OlsDeinitializeDelegate _winringDeinit;
        private static OlsGetDllStatusDelegate _winringStatus;
        private static OlsReadIoPortByteDelegate _winringReadByte;
        private static OlsWriteIoPortByteDelegate _winringWriteByte;
        private static OlsReadIoPortDwordDelegate _winringReadDword;
        private static OlsWriteIoPortDwordDelegate _winringWriteDword;
        private static OlsReadPhysicalMemoryDelegate _winringReadMem;
        private static OlsWritePhysicalMemoryDelegate _winringWriteMem;
        #endregion

        #region InpOut Delegates
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InpOutIsOpenDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate byte InpOutReadUcharDelegate(ushort port);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void InpOutWriteUcharDelegate(ushort port, byte value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint InpOutReadUlongDelegate(ushort port);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void InpOutWriteUlongDelegate(ushort port, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InpOutInp32Delegate(ushort port);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void InpOutOut32Delegate(ushort port, short value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr InpOutMapPhysToLinDelegate(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InpOutUnmapPhysicalMemoryDelegate(IntPtr PhysicalMemoryHandle, IntPtr pbLinAddr);

        private static InpOutIsOpenDelegate _inpoutIsOpen;
        private static InpOutReadUcharDelegate _inpoutReadByte;
        private static InpOutWriteUcharDelegate _inpoutWriteByte;
        private static InpOutReadUlongDelegate _inpoutReadDword;
        private static InpOutWriteUlongDelegate _inpoutWriteDword;
        private static InpOutInp32Delegate _inpoutInp32;
        private static InpOutOut32Delegate _inpoutOut32;
        private static InpOutMapPhysToLinDelegate _inpoutMapPhys;
        private static InpOutUnmapPhysicalMemoryDelegate _inpoutUnmap;
        #endregion

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

        private static T GetProcDelegate<T>(IntPtr hModule, string procName) where T : class
        {
            if (hModule == IntPtr.Zero) return null;
            IntPtr pProc = GetProcAddress(hModule, procName);
            if (pProc == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(pProc, typeof(T)) as T;
        }

        public static bool PreloadDriverDll()
        {
            HardenDllSearchPath();
            if (ActiveDriver != HardwareDriverEngine.None) return true;

            lock (_driverLock)
            {
                if (ActiveDriver != HardwareDriverEngine.None) return true;

                LastDriverIntegrityError = null;
                IsDriverHashVerified = false;
                ActiveDriverSha256 = null;

                string appDir = AppDomain.CurrentDomain.BaseDirectory;

                // Priority 1: Check for bundled InpOut (inpoutx64.dll) in application directory
                string[] inpoutCandidates = new string[]
                {
                    Path.Combine(appDir, "inpoutx64.dll")
                };

                string[] inpoutPins = new string[] { INPOUTX64_SHA256 };

                foreach (string candidate in inpoutCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            // Hold a read-only FileShare.Read handle across SHA256 verification and LoadLibraryEx
                            // to eliminate TOCTOU binary swapping in writable portable directories.
                            using (FileStream pinLock = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                string actualHash = ComputeStreamSha256(pinLock);
                                if (!IsHashAllowed(actualHash, inpoutPins))
                                {
                                    LastDriverIntegrityError = string.Format(
                                        "Blocked untrusted {0}: SHA256 mismatch (actual {1}, expected {2}).",
                                        Path.GetFileName(candidate), actualHash, INPOUTX64_SHA256);
                                    WatchdogSupervisor.LogWatchdogEvent("SECURITY: " + LastDriverIntegrityError);
                                    continue;
                                }

                                IntPtr hDll = LoadLibraryEx(candidate, IntPtr.Zero, SAFE_DRIVER_LOAD_FLAGS);
                                if (hDll != IntPtr.Zero)
                                {
                                    _hDriverDll = hDll;
                                    _inpoutIsOpen = GetProcDelegate<InpOutIsOpenDelegate>(hDll, "IsInpOutDriverOpen");
                                    _inpoutReadByte = GetProcDelegate<InpOutReadUcharDelegate>(hDll, "DlPortReadPortUchar");
                                    _inpoutWriteByte = GetProcDelegate<InpOutWriteUcharDelegate>(hDll, "DlPortWritePortUchar");
                                    _inpoutReadDword = GetProcDelegate<InpOutReadUlongDelegate>(hDll, "DlPortReadPortUlong");
                                    _inpoutWriteDword = GetProcDelegate<InpOutWriteUlongDelegate>(hDll, "DlPortWritePortUlong");
                                    _inpoutInp32 = GetProcDelegate<InpOutInp32Delegate>(hDll, "Inp32");
                                    _inpoutOut32 = GetProcDelegate<InpOutOut32Delegate>(hDll, "Out32");
                                    _inpoutMapPhys = GetProcDelegate<InpOutMapPhysToLinDelegate>(hDll, "MapPhysToLin");
                                    _inpoutUnmap = GetProcDelegate<InpOutUnmapPhysicalMemoryDelegate>(hDll, "UnmapPhysicalMemory");

                                    ActiveDriver = HardwareDriverEngine.InpOut64;
                                    IsDriverHashVerified = true;
                                    ActiveDriverSha256 = actualHash;
                                    LastDriverIntegrityError = null;
                                    return true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WatchdogSupervisor.LogWatchdogEvent("InpOut64 preload failed: " + ex.Message);
                        }
                    }
                }

                // Priority 2: Fallback to WinRing0 (WinRing0x64.dll or WinRing0.dll) in application directory ONLY (never probe System32)
                string[] winringCandidates = new string[]
                {
                    Path.Combine(appDir, "WinRing0x64.dll"),
                    Path.Combine(appDir, "WinRing0.dll")
                };

                foreach (string candidate in winringCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            using (FileStream pinLock = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                string actualHash = ComputeStreamSha256(pinLock);
                                if (!IsHashAllowed(actualHash, WINRING0X64_ALLOWED_SHA256))
                                {
                                    LastDriverIntegrityError = string.Format(
                                        "Blocked untrusted {0}: SHA256 mismatch (actual {1}).",
                                        Path.GetFileName(candidate), actualHash);
                                    WatchdogSupervisor.LogWatchdogEvent("SECURITY: " + LastDriverIntegrityError);
                                    continue;
                                }

                                IntPtr hDll = LoadLibraryEx(candidate, IntPtr.Zero, SAFE_DRIVER_LOAD_FLAGS);
                                if (hDll != IntPtr.Zero)
                                {
                                    var initFn = GetProcDelegate<OlsInitializeDelegate>(hDll, "InitializeOls");
                                    var statusFn = GetProcDelegate<OlsGetDllStatusDelegate>(hDll, "GetDllStatus");
                                    var readByteFn = GetProcDelegate<OlsReadIoPortByteDelegate>(hDll, "ReadIoPortByte");
                                    var writeByteFn = GetProcDelegate<OlsWriteIoPortByteDelegate>(hDll, "WriteIoPortByte");

                                    if (initFn != null && readByteFn != null && writeByteFn != null)
                                    {
                                        int initResult = initFn();
                                        uint status = statusFn != null ? statusFn() : 0;
                                        if (initResult != 0 && status == 0)
                                        {
                                            _hDriverDll = hDll;
                                            _winringInit = initFn;
                                            _winringDeinit = GetProcDelegate<OlsDeinitializeDelegate>(hDll, "DeinitializeOls");
                                            _winringStatus = statusFn;
                                            _winringReadByte = readByteFn;
                                            _winringWriteByte = writeByteFn;
                                            _winringReadDword = GetProcDelegate<OlsReadIoPortDwordDelegate>(hDll, "ReadIoPortDword");
                                            _winringWriteDword = GetProcDelegate<OlsWriteIoPortDwordDelegate>(hDll, "WriteIoPortDword");
                                            _winringReadMem = GetProcDelegate<OlsReadPhysicalMemoryDelegate>(hDll, "ReadPhysicalMemory");
                                            _winringWriteMem = GetProcDelegate<OlsWritePhysicalMemoryDelegate>(hDll, "WritePhysicalMemory");

                                            ActiveDriver = HardwareDriverEngine.WinRing0;
                                            IsDriverHashVerified = true;
                                            ActiveDriverSha256 = actualHash;
                                            LastDriverIntegrityError = null;
                                            return true;
                                        }
                                    }
                                    FreeLibrary(hDll);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WatchdogSupervisor.LogWatchdogEvent("WinRing0 preload failed: " + ex.Message);
                        }
                    }
                }

                ActiveDriver = HardwareDriverEngine.None;
                return false;
            }
        }

        public static bool IsDriverOpen()
        {
            if (ActiveDriver == HardwareDriverEngine.None)
            {
                PreloadDriverDll();
            }
            if (ActiveDriver == HardwareDriverEngine.WinRing0)
            {
                return _winringStatus != null ? (_winringStatus() == 0) : true;
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64)
            {
                return _inpoutIsOpen != null ? (_inpoutIsOpen() != 0) : false;
            }
            return false;
        }

        public static int IsInpOutDriverOpen()
        {
            return IsDriverOpen() ? 1 : 0;
        }

        public static byte DlPortReadPortUchar(ushort port)
        {
            if (!IsPortWhitelisted(port, false)) return 0xFF;

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringReadByte != null)
            {
                try { return _winringReadByte(port); } catch { return 0xFF; }
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64)
            {
                if (_inpoutReadByte != null)
                {
                    try { return _inpoutReadByte(port); } catch { }
                }
                if (_inpoutInp32 != null)
                {
                    try { return (byte)(_inpoutInp32(port) & 0xFF); } catch { }
                }
            }
            return 0xFF;
        }

        public static void DlPortWritePortUchar(ushort port, byte value)
        {
            if (!IsPortWhitelisted(port, true)) return;

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringWriteByte != null)
            {
                try { _winringWriteByte(port, value); return; } catch { return; }
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64)
            {
                if (_inpoutWriteByte != null)
                {
                    try { _inpoutWriteByte(port, value); return; } catch { }
                }
                if (_inpoutOut32 != null)
                {
                    try { _inpoutOut32(port, (short)value); return; } catch { }
                }
            }
        }

        public static uint DlPortReadPortUlong(ushort port)
        {
            // Strict PCI/SMN Dword Whitelist: Only PCI Config Address (0xCF8) and Data (0xCFC) are permitted
            if (port != 0xCF8 && port != 0xCFC) return 0;

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringReadDword != null)
            {
                try { return _winringReadDword(port); } catch { return 0; }
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutReadDword != null)
            {
                try { return _inpoutReadDword(port); } catch { return 0; }
            }
            return 0;
        }

        public static void DlPortWritePortUlong(ushort port, uint value)
        {
            // Strict PCI/SMN Dword Whitelist: Only PCI Config Address (0xCF8) and Data (0xCFC) are permitted
            if (port != 0xCF8 && port != 0xCFC) return;

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringWriteDword != null)
            {
                try { _winringWriteDword(port, value); return; } catch { return; }
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutWriteDword != null)
            {
                try { _inpoutWriteDword(port, value); return; } catch { return; }
            }
        }

        public static int Inp32(ushort port)
        {
            return (int)DlPortReadPortUchar(port);
        }

        public static void Out32(ushort port, short value)
        {
            DlPortWritePortUchar(port, (byte)(value & 0xFF));
        }

        public static IntPtr MapPhysToLinSafe(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle)
        {
            pPhysicalMemoryHandle = IntPtr.Zero;
            ulong addr = unchecked((ulong)pbPhysAddr.ToInt64());
            if (!IsPhysicalMemoryRangeAllowed(addr, dwPhysSize, isWrite: false))
            {
                return IntPtr.Zero;
            }

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            // WinRing0 does NOT support process virtual linear mapping (it only provides per-access physical R/W IOCTLs).
            // Never fake a linear mapping with AllocHGlobal as that produces disconnected heap snapshots that defeat write verification.
            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutMapPhys != null)
            {
                try { return _inpoutMapPhys(pbPhysAddr, dwPhysSize, out pPhysicalMemoryHandle); } catch { return IntPtr.Zero; }
            }
            return IntPtr.Zero;
        }

        public static int UnmapPhysicalMemory(IntPtr PhysicalMemoryHandle, IntPtr pbLinAddr)
        {
            if (pbLinAddr == IntPtr.Zero) return 0;

            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutUnmap != null)
            {
                try { return _inpoutUnmap(PhysicalMemoryHandle, pbLinAddr); } catch { return 0; }
            }
            return 0;
        }

        public static byte ReadPhysicalByteSafe(ulong physAddr)
        {
            if (!IsPhysicalMemoryRangeAllowed(physAddr, 1, isWrite: false)) return 0xFF;
            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringReadMem != null)
            {
                try
                {
                    byte[] buffer = new byte[1];
                    uint ok = _winringReadMem(new UIntPtr(physAddr), buffer, 1, 1);
                    if (ok != 0) return buffer[0];
                }
                catch { }
            }
            return 0xFF;
        }

        public static bool WritePhysicalByteSafe(ulong physAddr, byte value)
        {
            if (!IsPhysicalMemoryRangeAllowed(physAddr, 1, isWrite: true)) return false;
            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringWriteMem != null)
            {
                try
                {
                    byte[] buffer = new byte[] { value };
                    uint ok = _winringWriteMem(new UIntPtr(physAddr), buffer, 1, 1);
                    return ok != 0;
                }
                catch { }
            }
            return false;
        }

        public static void Shutdown()
        {
            lock (_driverLock)
            {
                if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringDeinit != null)
                {
                    try { _winringDeinit(); } catch { }
                }
                if (_hDriverDll != IntPtr.Zero)
                {
                    try { FreeLibrary(_hDriverDll); } catch { }
                    _hDriverDll = IntPtr.Zero;
                }
                ActiveDriver = HardwareDriverEngine.None;
                IsDriverHashVerified = false;
                ActiveDriverSha256 = null;
            }
        }
    }
}
