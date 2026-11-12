using System;
using System.IO;
using System.Runtime.InteropServices;

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
    /// Auto-detects and seamlessly interfaces with either WinRing0 1.3.1b (WinRing0x64.dll)
    /// or InpOut64 (inpoutx64.dll) for LPC port I/O, Super I/O, EC, and MMIO register access.
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

        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH       = 0x00000008;

        public const ulong MIN_SAFE_PHYSICAL_ADDR = 0x000E0000;
        public const uint MAX_SAFE_MAP_SIZE = 16 * 1024 * 1024;

        private static readonly object _driverLock = new object();
        private static bool _dllSearchHardened = false;
        private static IntPtr _hDriverDll = IntPtr.Zero;

        public static HardwareDriverEngine ActiveDriver { get; private set; }

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

                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

                // Priority 1: Check for WinRing0 (WinRing0x64.dll or WinRing0.dll)
                string[] winringCandidates = new string[]
                {
                    Path.Combine(appDir, "WinRing0x64.dll"),
                    Path.Combine(appDir, "WinRing0.dll"),
                    Path.Combine(sys32, "WinRing0x64.dll"),
                    Path.Combine(sys32, "WinRing0.dll")
                };

                foreach (string candidate in winringCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            IntPtr hDll = LoadLibraryEx(candidate, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
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
                                        return true;
                                    }
                                }
                                FreeLibrary(hDll);
                            }
                        }
                        catch { }
                    }
                }

                // Priority 2: Check for InpOut (inpoutx64.dll)
                string[] inpoutCandidates = new string[]
                {
                    Path.Combine(appDir, "inpoutx64.dll"),
                    Path.Combine(sys32, "inpoutx64.dll")
                };

                foreach (string candidate in inpoutCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            IntPtr hDll = LoadLibraryEx(candidate, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
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
                                return true;
                            }
                        }
                        catch { }
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
            if (addr < MIN_SAFE_PHYSICAL_ADDR || dwPhysSize == 0 || dwPhysSize > MAX_SAFE_MAP_SIZE)
            {
                return IntPtr.Zero;
            }

            if (ActiveDriver == HardwareDriverEngine.None) PreloadDriverDll();

            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutMapPhys != null)
            {
                try { return _inpoutMapPhys(pbPhysAddr, dwPhysSize, out pPhysicalMemoryHandle); } catch { return IntPtr.Zero; }
            }
            if (ActiveDriver == HardwareDriverEngine.WinRing0 && _winringReadMem != null)
            {
                try
                {
                    byte[] buffer = new byte[dwPhysSize];
                    uint ok = _winringReadMem(new UIntPtr(addr), buffer, dwPhysSize, 1);
                    if (ok != 0)
                    {
                        IntPtr unmanaged = Marshal.AllocHGlobal((int)dwPhysSize);
                        Marshal.Copy(buffer, 0, unmanaged, (int)dwPhysSize);
                        pPhysicalMemoryHandle = new IntPtr(0x57494E52); // "WINR" tag
                        return unmanaged;
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        public static int UnmapPhysicalMemory(IntPtr PhysicalMemoryHandle, IntPtr pbLinAddr)
        {
            if (pbLinAddr == IntPtr.Zero) return 0;

            if (PhysicalMemoryHandle == new IntPtr(0x57494E52))
            {
                try
                {
                    Marshal.FreeHGlobal(pbLinAddr);
                    return 1;
                }
                catch { return 0; }
            }
            if (ActiveDriver == HardwareDriverEngine.InpOut64 && _inpoutUnmap != null)
            {
                try { return _inpoutUnmap(PhysicalMemoryHandle, pbLinAddr); } catch { return 0; }
            }
            return 0;
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
            }
        }
    }
}
