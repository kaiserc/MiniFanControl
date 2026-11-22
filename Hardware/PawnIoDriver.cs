using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MiniFanControl
{
    /// <summary>
    /// Official PawnIO (namazso/PawnIO) user-mode client wrapper via PawnIOLib.dll.
    /// Enables HVCI / Memory Integrity-compatible hardware access when the user has PawnIO
    /// and signed PawnIO.Modules (.bin) installed (e.g. LpcIO.bin, LpcACPIEC.bin).
    /// All port I/O remains strictly fail-closed behind NativeInpOut.IsPortWhitelisted().
    /// </summary>
    public static class PawnIoDriver
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR    = 0x00000100;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint SAFE_LOAD_FLAGS = LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32;

        // Official PawnIOLib C ABI (CallingConvention.Winapi / StdCall, returning HRESULT)
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int PawnIoVersionDelegate(out uint version);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int PawnIoOpenDelegate(out IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int PawnIoLoadDelegate(IntPtr handle, [In] byte[] blob, UIntPtr size);

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        private delegate int PawnIoExecuteDelegate(
            IntPtr handle,
            string name,
            [In] ulong[] input,
            UIntPtr inSize,
            [Out] ulong[] output,
            UIntPtr outSize,
            out UIntPtr returnSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int PawnIoCloseDelegate(IntPtr handle);

        private static readonly object _sync = new object();
        private static IntPtr _hLib = IntPtr.Zero;
        private static IntPtr _hLpcModule = IntPtr.Zero;
        private static IntPtr _hAcpiEcModule = IntPtr.Zero;

        private static PawnIoVersionDelegate _fnVersion;
        private static PawnIoOpenDelegate _fnOpen;
        private static PawnIoLoadDelegate _fnLoad;
        private static PawnIoExecuteDelegate _fnExecute;
        private static PawnIoCloseDelegate _fnClose;

        // Injectable test hook for verifying PawnIO module orchestration without kernel driver
        internal static Func<string, ulong[], ulong[]> SimulatedExecuteHook = null;
        internal static bool SimulatedAvailable = false;

        public static bool IsLibraryLoaded { get; private set; }
        public static bool HasLpcModule { get; private set; }
        public static bool HasAcpiEcModule { get; private set; }
        public static uint DriverVersionRaw { get; private set; }
        public static string LibraryPath { get; private set; }
        public static string LastStatus { get; private set; }

        static PawnIoDriver()
        {
            LastStatus = "Not initialized";
        }

        public static bool IsAvailable
        {
            get
            {
                if (SimulatedAvailable) return true;
                return IsLibraryLoaded && (HasLpcModule || HasAcpiEcModule);
            }
        }

        public static string VersionString
        {
            get
            {
                if (SimulatedAvailable && DriverVersionRaw == 0) return "2.0.0 (Simulated)";
                if (DriverVersionRaw == 0) return "Unknown";
                uint major = (DriverVersionRaw >> 16) & 0xFF;
                uint minor = (DriverVersionRaw >> 8) & 0xFF;
                uint patch = DriverVersionRaw & 0xFF;
                return string.Format("{0}.{1}.{2}", major, minor, patch);
            }
        }

        public static bool IsServiceInstalledAndRunning()
        {
            if (SimulatedAvailable) return true;
            return DriverServiceManager.IsDriverServiceRunning("PawnIO");
        }

        /// <summary>
        /// Locates PawnIOLib.dll in the application directory or standard Program Files\PawnIO install directory.
        /// </summary>
        public static string FindPawnIoLibraryPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "PawnIOLib.dll"),
                Path.Combine(baseDir, "PawnIO", "PawnIOLib.dll"),
                string.IsNullOrEmpty(progFiles) ? "" : Path.Combine(progFiles, "PawnIO", "PawnIOLib.dll")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                    return candidates[i];
            }
            return null;
        }

        /// <summary>
        /// Locates a signed PawnIO module blob (.bin / .amx) in appdir\PawnIO or Program Files\PawnIO\modules.
        /// </summary>
        public static string FindModuleBlobPath(string moduleBaseName)
        {
            if (string.IsNullOrEmpty(moduleBaseName)) return null;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string[] dirs = new string[]
            {
                Path.Combine(baseDir, "PawnIO"),
                Path.Combine(baseDir, "PawnIO", "modules"),
                baseDir,
                string.IsNullOrEmpty(progFiles) ? "" : Path.Combine(progFiles, "PawnIO", "modules"),
                string.IsNullOrEmpty(progFiles) ? "" : Path.Combine(progFiles, "PawnIO")
            };
            string[] exts = new string[] { ".bin", ".amx" };

            for (int d = 0; d < dirs.Length; d++)
            {
                if (string.IsNullOrEmpty(dirs[d]) || !Directory.Exists(dirs[d])) continue;
                for (int e = 0; e < exts.Length; e++)
                {
                    string path = Path.Combine(dirs[d], moduleBaseName + exts[e]);
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        /// <summary>
        /// Attempts to initialize PawnIOLib.dll and load available signed hardware modules (LpcIO, LpcACPIEC).
        /// Non-destructive: returns false cleanly if PawnIO is not installed on the system.
        /// </summary>
        public static bool TryInitialize()
        {
            lock (_sync)
            {
                if (SimulatedAvailable)
                {
                    IsLibraryLoaded = true;
                    HasLpcModule = true;
                    HasAcpiEcModule = true;
                    LastStatus = "PawnIO active (simulated test mode)";
                    return true;
                }

                if (IsAvailable) return true;

                string dllPath = FindPawnIoLibraryPath();
                if (string.IsNullOrEmpty(dllPath))
                {
                    LastStatus = "PawnIOLib.dll not found (install PawnIO from https://pawnio.eu for HVCI support)";
                    return false;
                }

                IntPtr hLib = LoadLibraryEx(dllPath, IntPtr.Zero, SAFE_LOAD_FLAGS);
                if (hLib == IntPtr.Zero)
                {
                    LastStatus = "Failed to load PawnIOLib.dll (Win32 error " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                IntPtr pOpen = GetProcAddress(hLib, "pawnio_open");
                IntPtr pLoad = GetProcAddress(hLib, "pawnio_load");
                IntPtr pExec = GetProcAddress(hLib, "pawnio_execute");
                IntPtr pClose = GetProcAddress(hLib, "pawnio_close");
                IntPtr pVer = GetProcAddress(hLib, "pawnio_version");

                if (pOpen == IntPtr.Zero || pLoad == IntPtr.Zero || pExec == IntPtr.Zero || pClose == IntPtr.Zero)
                {
                    FreeLibrary(hLib);
                    LastStatus = "PawnIOLib.dll missing required pawnio_* exports";
                    return false;
                }

                _hLib = hLib;
                LibraryPath = dllPath;
                _fnOpen = (PawnIoOpenDelegate)Marshal.GetDelegateForFunctionPointer(pOpen, typeof(PawnIoOpenDelegate));
                _fnLoad = (PawnIoLoadDelegate)Marshal.GetDelegateForFunctionPointer(pLoad, typeof(PawnIoLoadDelegate));
                _fnExecute = (PawnIoExecuteDelegate)Marshal.GetDelegateForFunctionPointer(pExec, typeof(PawnIoExecuteDelegate));
                _fnClose = (PawnIoCloseDelegate)Marshal.GetDelegateForFunctionPointer(pClose, typeof(PawnIoCloseDelegate));
                if (pVer != IntPtr.Zero)
                {
                    _fnVersion = (PawnIoVersionDelegate)Marshal.GetDelegateForFunctionPointer(pVer, typeof(PawnIoVersionDelegate));
                    try
                    {
                        uint ver;
                        if (_fnVersion(out ver) == 0) DriverVersionRaw = ver;
                    }
                    catch { }
                }

                IsLibraryLoaded = true;

                // Load signed LPC Super I/O module if present
                string lpcBlob = FindModuleBlobPath("LpcIO");
                if (!string.IsNullOrEmpty(lpcBlob))
                {
                    _hLpcModule = TryOpenAndLoadModule(lpcBlob);
                    HasLpcModule = (_hLpcModule != IntPtr.Zero);
                }

                // Load signed ACPI EC module if present
                string ecBlob = FindModuleBlobPath("LpcACPIEC");
                if (!string.IsNullOrEmpty(ecBlob))
                {
                    _hAcpiEcModule = TryOpenAndLoadModule(ecBlob);
                    HasAcpiEcModule = (_hAcpiEcModule != IntPtr.Zero);
                }

                if (!HasLpcModule && !HasAcpiEcModule)
                {
                    LastStatus = "PawnIOLib loaded (v" + VersionString + "), awaiting signed LpcIO.bin / LpcACPIEC.bin modules";
                    return false;
                }

                List<string> mods = new List<string>();
                if (HasLpcModule) mods.Add("LpcIO");
                if (HasAcpiEcModule) mods.Add("LpcACPIEC");
                LastStatus = string.Format("PawnIO v{0} active (HVCI-safe modules: {1})", VersionString, string.Join(", ", mods.ToArray()));
                return true;
            }
        }

        private static IntPtr TryOpenAndLoadModule(string blobPath)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                byte[] blob = File.ReadAllBytes(blobPath);
                if (blob == null || blob.Length < 16 || blob.Length > 2 * 1024 * 1024)
                    return IntPtr.Zero;

                int hrOpen = _fnOpen(out h);
                if (hrOpen != 0 || h == IntPtr.Zero) return IntPtr.Zero;

                int hrLoad = _fnLoad(h, blob, new UIntPtr((uint)blob.Length));
                if (hrLoad != 0)
                {
                    try { _fnClose(h); } catch { }
                    return IntPtr.Zero;
                }
                return h;
            }
            catch
            {
                if (h != IntPtr.Zero)
                {
                    try { _fnClose(h); } catch { }
                }
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Reads a single byte from a whitelisted LPC / Super I/O port via the PawnIO LpcIO module.
        /// Enforces NativeInpOut.IsPortWhitelisted() fail-closed before calling kernel bytecode.
        /// </summary>
        public static bool TryReadPortByte(ushort port, out byte value)
        {
            value = 0xFF;
            if (!NativeInpOut.IsPortWhitelisted(port, false)) return false;

            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_pio_read", new ulong[] { port, 1UL });
                if (res != null && res.Length > 0)
                {
                    value = (byte)(res[0] & 0xFF);
                    return true;
                }
                return false;
            }

            if (!HasLpcModule || _hLpcModule == IntPtr.Zero || _fnExecute == null) return false;
            try
            {
                ulong[] inp = new ulong[] { port, 1UL };
                ulong[] outp = new ulong[1];
                UIntPtr retSize;
                int hr = _fnExecute(_hLpcModule, "ioctl_pio_read", inp, new UIntPtr((uint)inp.Length), outp, new UIntPtr((uint)outp.Length), out retSize);
                if (hr == 0 && retSize.ToUInt64() >= 1)
                {
                    value = (byte)(outp[0] & 0xFF);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Writes a single byte to a whitelisted LPC / Super I/O port via the PawnIO LpcIO module.
        /// Enforces NativeInpOut.IsPortWhitelisted() fail-closed before calling kernel bytecode.
        /// </summary>
        public static bool TryWritePortByte(ushort port, byte value)
        {
            if (!NativeInpOut.IsPortWhitelisted(port, true)) return false;

            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_pio_write", new ulong[] { port, 1UL, value });
                return res != null;
            }

            if (!HasLpcModule || _hLpcModule == IntPtr.Zero || _fnExecute == null) return false;
            try
            {
                ulong[] inp = new ulong[] { port, 1UL, value };
                ulong[] outp = new ulong[1];
                UIntPtr retSize;
                int hr = _fnExecute(_hLpcModule, "ioctl_pio_write", inp, new UIntPtr((uint)inp.Length), outp, new UIntPtr((uint)outp.Length), out retSize);
                return hr == 0;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Reads a single ACPI EC register via the PawnIO LpcACPIEC module.
        /// </summary>
        public static bool TryReadAcpiEcByte(byte reg, out byte value)
        {
            value = 0xFF;
            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_read", new ulong[] { reg });
                if (res != null && res.Length > 0)
                {
                    value = (byte)(res[0] & 0xFF);
                    return true;
                }
                return false;
            }

            if (!HasAcpiEcModule || _hAcpiEcModule == IntPtr.Zero || _fnExecute == null) return false;
            try
            {
                ulong[] inp = new ulong[] { reg };
                ulong[] outp = new ulong[1];
                UIntPtr retSize;
                int hr = _fnExecute(_hAcpiEcModule, "ioctl_read", inp, new UIntPtr((uint)inp.Length), outp, new UIntPtr((uint)outp.Length), out retSize);
                if (hr == 0 && retSize.ToUInt64() >= 1)
                {
                    value = (byte)(outp[0] & 0xFF);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Writes a single ACPI EC register via the PawnIO LpcACPIEC module.
        /// </summary>
        public static bool TryWriteAcpiEcByte(byte reg, byte value)
        {
            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_write", new ulong[] { reg, value });
                return res != null;
            }

            if (!HasAcpiEcModule || _hAcpiEcModule == IntPtr.Zero || _fnExecute == null) return false;
            try
            {
                ulong[] inp = new ulong[] { reg, value };
                ulong[] outp = new ulong[1];
                UIntPtr retSize;
                int hr = _fnExecute(_hAcpiEcModule, "ioctl_write", inp, new UIntPtr((uint)inp.Length), outp, new UIntPtr((uint)outp.Length), out retSize);
                return hr == 0;
            }
            catch { }
            return false;
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                if (_hLpcModule != IntPtr.Zero && _fnClose != null)
                {
                    try { _fnClose(_hLpcModule); } catch { }
                    _hLpcModule = IntPtr.Zero;
                }
                if (_hAcpiEcModule != IntPtr.Zero && _fnClose != null)
                {
                    try { _fnClose(_hAcpiEcModule); } catch { }
                    _hAcpiEcModule = IntPtr.Zero;
                }
                if (_hLib != IntPtr.Zero)
                {
                    try { FreeLibrary(_hLib); } catch { }
                    _hLib = IntPtr.Zero;
                }
                _fnVersion = null;
                _fnOpen = null;
                _fnLoad = null;
                _fnExecute = null;
                _fnClose = null;
                IsLibraryLoaded = false;
                HasLpcModule = false;
                HasAcpiEcModule = false;
                DriverVersionRaw = 0;
                SimulatedAvailable = false;
                SimulatedExecuteHook = null;
            }
        }
    }
}
