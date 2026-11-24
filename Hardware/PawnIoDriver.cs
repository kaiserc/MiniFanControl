using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

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
        private static readonly ReaderWriterLockSlim _ioLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private static IntPtr _hLib = IntPtr.Zero;
        private static IntPtr _hLpcModule = IntPtr.Zero;
        private static IntPtr _hAcpiEcModule = IntPtr.Zero;

        private static PawnIoVersionDelegate _fnVersion;
        private static PawnIoOpenDelegate _fnOpen;
        private static PawnIoLoadDelegate _fnLoad;
        private static PawnIoExecuteDelegate _fnExecute;
        private static PawnIoCloseDelegate _fnClose;

        // Injectable test hooks for verifying PawnIO module orchestration & Authenticode gating without kernel driver
        internal static Func<string, ulong[], ulong[]> SimulatedExecuteHook = null;
        internal static bool SimulatedAvailable = false;
        internal static Func<string, bool> SimulatedSignatureVerifier = null;

        public static bool IsLibraryLoaded { get; private set; }
        public static bool IsSignatureVerified { get; private set; }
        public static bool HasLpcModule { get; private set; }
        public static bool HasAcpiEcModule { get; private set; }
        public static uint DriverVersionRaw { get; private set; }
        public static string LibraryPath { get; private set; }
        public static string LastStatus { get; private set; }
        public static string LastIntegrityError { get; private set; }

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
        /// Verifies the Windows Authenticode signature of a PawnIO DLL fail-closed before LoadLibraryEx
        /// to prevent DLL planting in writable portable directories.
        /// </summary>
        public static bool VerifyLibrarySignature(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            if (SimulatedSignatureVerifier != null)
            {
                try { return SimulatedSignatureVerifier(filePath); } catch { return false; }
            }

            IntPtr pFilePath = IntPtr.Zero;
            IntPtr pFileInfo = IntPtr.Zero;
            WINTRUST_DATA data = new WINTRUST_DATA();
            bool needClose = false;
            try
            {
                pFilePath = Marshal.StringToCoTaskMemUni(filePath);
                WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                    pcwszFilePath = pFilePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };
                pFileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                data.dwUIChoice = WTD_UI_NONE;
                data.fdwRevocationChecks = WTD_REVOKE_NONE;
                data.dwUnionChoice = WTD_CHOICE_FILE;
                data.pFile = pFileInfo;
                data.dwStateAction = WTD_STATEACTION_VERIFY;
                data.dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL;

                int result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
                needClose = true;
                return result == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (needClose)
                {
                    try
                    {
                        data.dwStateAction = WTD_STATEACTION_CLOSE;
                        WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
                    }
                    catch { }
                }
                if (pFileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(pFileInfo);
                if (pFilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(pFilePath);
            }
        }

        /// <summary>
        /// Returns ordered candidate paths for PawnIOLib.dll, strictly prioritizing the protected
        /// Program Files\PawnIO installation directory over writable portable application folders.
        /// </summary>
        public static string[] GetCandidateLibraryPaths(string appBaseDir = null, string programFilesDir = null)
        {
            string baseDir = !string.IsNullOrEmpty(appBaseDir) ? appBaseDir : AppDomain.CurrentDomain.BaseDirectory;
            string progFiles = programFilesDir != null ? programFilesDir : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            List<string> candidates = new List<string>(3);
            if (!string.IsNullOrEmpty(progFiles))
                candidates.Add(Path.Combine(progFiles, "PawnIO", "PawnIOLib.dll"));
            if (!string.IsNullOrEmpty(baseDir))
            {
                candidates.Add(Path.Combine(baseDir, "PawnIO", "PawnIOLib.dll"));
                candidates.Add(Path.Combine(baseDir, "PawnIOLib.dll"));
            }
            return candidates.ToArray();
        }

        /// <summary>
        /// Locates PawnIOLib.dll, preferring the admin-protected Program Files\PawnIO install directory
        /// before falling back to the application directory.
        /// </summary>
        public static string FindPawnIoLibraryPath()
        {
            string[] candidates = GetCandidateLibraryPaths();
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                    return candidates[i];
            }
            return null;
        }

        /// <summary>
        /// Locates a signed PawnIO module blob (.bin / .amx), preferring Program Files\PawnIO\modules
        /// before falling back to appdir\PawnIO.
        /// </summary>
        public static string FindModuleBlobPath(string moduleBaseName)
        {
            if (string.IsNullOrEmpty(moduleBaseName)) return null;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string[] dirs = new string[]
            {
                string.IsNullOrEmpty(progFiles) ? "" : Path.Combine(progFiles, "PawnIO", "modules"),
                string.IsNullOrEmpty(progFiles) ? "" : Path.Combine(progFiles, "PawnIO"),
                Path.Combine(baseDir, "PawnIO", "modules"),
                Path.Combine(baseDir, "PawnIO"),
                baseDir
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
        /// Enforces Authenticode verification under a FileShare.Read lock before LoadLibraryEx to prevent
        /// DLL planting / TOCTOU swaps, and cleanly releases the library handle if no modules are present.
        /// </summary>
        public static bool TryInitialize()
        {
            lock (_sync)
            {
                LastIntegrityError = null;
                if (SimulatedAvailable)
                {
                    IsLibraryLoaded = true;
                    IsSignatureVerified = true;
                    HasLpcModule = true;
                    HasAcpiEcModule = true;
                    LastStatus = "PawnIO active (simulated test mode)";
                    return true;
                }

                if (IsAvailable) return true;

                string[] candidates = GetCandidateLibraryPaths();
                string dllPath = null;
                IntPtr hLib = IntPtr.Zero;
                for (int i = 0; i < candidates.Length; i++)
                {
                    string cand = candidates[i];
                    if (string.IsNullOrEmpty(cand) || !File.Exists(cand)) continue;
                    try
                    {
                        // Hold a read-only FileShare.Read handle across Authenticode verification and LoadLibraryEx
                        // to eliminate TOCTOU binary swapping in writable portable directories.
                        using (FileStream pinLock = new FileStream(cand, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (!VerifyLibrarySignature(cand))
                            {
                                LastIntegrityError = string.Format(
                                    "Blocked untrusted {0} at '{1}': Authenticode signature verification failed.",
                                    Path.GetFileName(cand), cand);
                                LastStatus = LastIntegrityError;
                                WatchdogSupervisor.LogWatchdogEvent("SECURITY: " + LastIntegrityError);
                                continue;
                            }

                            IntPtr loaded = LoadLibraryEx(cand, IntPtr.Zero, SAFE_LOAD_FLAGS);
                            if (loaded != IntPtr.Zero)
                            {
                                hLib = loaded;
                                dllPath = cand;
                                break;
                            }
                            else
                            {
                                LastStatus = "Failed to load PawnIOLib.dll (Win32 error " + Marshal.GetLastWin32Error() + ")";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LastStatus = "PawnIOLib.dll load error: " + ex.Message;
                    }
                }

                if (hLib == IntPtr.Zero)
                {
                    if (string.IsNullOrEmpty(LastIntegrityError))
                    {
                        LastStatus = "PawnIOLib.dll not found (install PawnIO from https://pawnio.eu for HVCI support)";
                    }
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
                IsSignatureVerified = true;
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
                    string verStr = VersionString;
                    // Release DLL handle & clear delegates so repeated retries never leak HMODULE references
                    try { FreeLibrary(_hLib); } catch { }
                    _hLib = IntPtr.Zero;
                    _fnVersion = null;
                    _fnOpen = null;
                    _fnLoad = null;
                    _fnExecute = null;
                    _fnClose = null;
                    IsLibraryLoaded = false;
                    IsSignatureVerified = false;
                    LastStatus = "PawnIOLib verified (v" + verStr + "), awaiting signed LpcIO.bin / LpcACPIEC.bin modules";
                    return false;
                }

                List<string> mods = new List<string>();
                if (HasLpcModule) mods.Add("LpcIO");
                if (HasAcpiEcModule) mods.Add("LpcACPIEC");
                LastIntegrityError = null;
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

            _ioLock.EnterReadLock();
            try
            {
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
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Writes a single byte to a whitelisted LPC / Super I/O port via the PawnIO LpcIO module.
        /// Enforces NativeInpOut.IsPortWhitelisted() fail-closed before calling kernel bytecode.
        /// </summary>
        public static bool TryWritePortByte(ushort port, byte value)
        {
            if (!NativeInpOut.IsPortWhitelisted(port, true)) return false;

            _ioLock.EnterReadLock();
            try
            {
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
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Reads a single ACPI EC register via the PawnIO LpcACPIEC module.
        /// </summary>
        public static bool TryReadAcpiEcByte(byte reg, out byte value)
        {
            value = 0xFF;
            _ioLock.EnterReadLock();
            try
            {
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
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Writes a single ACPI EC register via the PawnIO LpcACPIEC module.
        /// </summary>
        public static bool TryWriteAcpiEcByte(byte reg, byte value)
        {
            _ioLock.EnterReadLock();
            try
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
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                _ioLock.EnterWriteLock();
                try
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
                    _fnVersion = null;
                    _fnOpen = null;
                    _fnLoad = null;
                    _fnExecute = null;
                    _fnClose = null;
                    if (_hLib != IntPtr.Zero)
                    {
                        try { FreeLibrary(_hLib); } catch { }
                        _hLib = IntPtr.Zero;
                    }
                    IsLibraryLoaded = false;
                    IsSignatureVerified = false;
                    HasLpcModule = false;
                    HasAcpiEcModule = false;
                    DriverVersionRaw = 0;
                    LastIntegrityError = null;
                    SimulatedAvailable = false;
                    SimulatedExecuteHook = null;
                    SimulatedSignatureVerifier = null;
                }
                finally
                {
                    _ioLock.ExitWriteLock();
                }
            }
        }
    }
}
