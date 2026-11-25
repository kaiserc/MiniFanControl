using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MiniFanControl
{
    /// <summary>
    /// Official PawnIO (namazso/PawnIO) client supporting both Authenticode-verified PawnIOLib.dll
    /// and direct \\?\GLOBALROOT\Device\PawnIO DeviceIoControl fallback (for systems where PawnIO
    /// was installed by CapFrameX, FanControl, HWiNFO, or LibreHardwareMonitor without PawnIOLib.dll).
    /// Uses official signed PawnIO.Modules (.bin) ABI:
    /// - LpcIO.bin: ioctl_pio_inb / ioctl_pio_outb
    /// - LpcACPIEC.bin: ioctl_pio_read / ioctl_pio_write (ports 0x62 / 0x66)
    /// - AMDFamily17.bin: ioctl_read_smn
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[] lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

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

        // Direct PawnIO kernel device IOCTL constants (matching official namazso/PawnIO ABI)
        private const string PAWNIO_DEVICE_PATH = @"\\?\GLOBALROOT\Device\PawnIO";
        private const uint GENERIC_READ = 0x80000000u;
        private const uint GENERIC_WRITE = 0x40000000u;
        private const uint FILE_SHARE_READ = 0x00000001u;
        private const uint FILE_SHARE_WRITE = 0x00000002u;
        private const uint OPEN_EXISTING = 3u;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080u;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const uint PAWNIO_DEVICE_TYPE = 41394u << 16;
        private const uint IOCTL_PIO_LOAD_BINARY = PAWNIO_DEVICE_TYPE | (0x821u << 2);
        private const uint IOCTL_PIO_EXECUTE_FN  = PAWNIO_DEVICE_TYPE | (0x841u << 2);
        private const int FN_NAME_LENGTH = 32;

        // Standard ACPI EC command/status definitions for LpcACPIEC.bin port I/O
        private const byte ACPI_EC_DATA_PORT = 0x62;
        private const byte ACPI_EC_CMD_PORT = 0x66;
        private const byte ACPI_EC_CMD_READ = 0x80;
        private const byte ACPI_EC_CMD_WRITE = 0x81;
        private const byte ACPI_EC_FLAG_OBF = 0x01;
        private const byte ACPI_EC_FLAG_IBF = 0x02;

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
        private static IntPtr _hAmdSmnModule = IntPtr.Zero;

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
        public static bool IsDirectDeviceMode { get; private set; }
        public static bool IsSignatureVerified { get; private set; }
        public static bool HasLpcModule { get; private set; }
        public static bool HasAcpiEcModule { get; private set; }
        public static bool HasAmdSmnModule { get; private set; }
        public static uint DriverVersionRaw { get; private set; }
        public static string LibraryPath { get; private set; }
        public static string LastStatus { get; private set; }
        public static string LastIntegrityError { get; private set; }

        public static string LoadedModulesSummary
        {
            get
            {
                List<string> mods = new List<string>(3);
                if (HasLpcModule) mods.Add("LpcIO");
                if (HasAcpiEcModule) mods.Add("LpcACPIEC");
                if (HasAmdSmnModule) mods.Add("AMDFamily17");
                return mods.Count > 0 ? string.Join(", ", mods.ToArray()) : "None";
            }
        }

        /// <summary>
        /// Lists the official signed PawnIO bytecode modules (.bin) discovered on disk
        /// (bundled under PawnIO\modules\ or installed under Program Files\PawnIO\modules\).
        /// </summary>
        public static string AvailableBundledModulesSummary
        {
            get
            {
                List<string> found = new List<string>(3);
                if (!string.IsNullOrEmpty(FindModuleBlobPath("LpcIO"))) found.Add("LpcIO.bin");
                if (!string.IsNullOrEmpty(FindModuleBlobPath("LpcACPIEC"))) found.Add("LpcACPIEC.bin");
                if (!string.IsNullOrEmpty(FindModuleBlobPath("AMDFamily17") ?? FindModuleBlobPath("RyzenSMU"))) found.Add("AMDFamily17.bin");
                return found.Count > 0 ? string.Join(", ", found.ToArray()) : "None";
            }
        }

        static PawnIoDriver()
        {
            LastStatus = "Not initialized";
        }

        public static bool IsAvailable
        {
            get
            {
                if (SimulatedAvailable) return true;
                return (IsLibraryLoaded || IsDirectDeviceMode) && (HasLpcModule || HasAcpiEcModule || HasAmdSmnModule);
            }
        }

        public static string VersionString
        {
            get
            {
                if (SimulatedAvailable && DriverVersionRaw == 0) return "2.0.0 (Simulated)";
                if (DriverVersionRaw == 0) return IsDirectDeviceMode ? "2.x (Direct Device)" : "Unknown";
                uint major = (DriverVersionRaw >> 16) & 0xFF;
                uint minor = (DriverVersionRaw >> 8) & 0xFF;
                uint patch = DriverVersionRaw & 0xFF;
                return string.Format("{0}.{1}.{2}", major, minor, patch);
            }
        }

        public static bool IsDeviceAccessible()
        {
            if (SimulatedAvailable) return true;
            try
            {
                IntPtr h = CreateFileW(
                    PAWNIO_DEVICE_PATH,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);
                if (h != IntPtr.Zero && h != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(h);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsServiceInstalledAndRunning()
        {
            if (SimulatedAvailable) return true;
            return DriverServiceManager.IsDriverServiceRunning("PawnIO") || IsDeviceAccessible();
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
        /// before falling back to bundled appdir\PawnIO\modules.
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
        /// Attempts to initialize PawnIO (via Authenticode-verified PawnIOLib.dll or direct
        /// \\?\GLOBALROOT\Device\PawnIO IOCTL transport) and load available signed hardware modules
        /// (LpcIO.bin, LpcACPIEC.bin, AMDFamily17.bin).
        /// </summary>
        public static bool TryInitialize()
        {
            lock (_sync)
            {
                LastIntegrityError = null;
                if (SimulatedAvailable)
                {
                    IsLibraryLoaded = true;
                    IsDirectDeviceMode = false;
                    IsSignatureVerified = true;
                    HasLpcModule = true;
                    HasAcpiEcModule = true;
                    HasAmdSmnModule = true;
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

                bool directMode = false;
                if (hLib != IntPtr.Zero)
                {
                    IntPtr pOpen = GetProcAddress(hLib, "pawnio_open");
                    IntPtr pLoad = GetProcAddress(hLib, "pawnio_load");
                    IntPtr pExec = GetProcAddress(hLib, "pawnio_execute");
                    IntPtr pClose = GetProcAddress(hLib, "pawnio_close");
                    IntPtr pVer = GetProcAddress(hLib, "pawnio_version");

                    if (pOpen == IntPtr.Zero || pLoad == IntPtr.Zero || pExec == IntPtr.Zero || pClose == IntPtr.Zero)
                    {
                        FreeLibrary(hLib);
                        hLib = IntPtr.Zero;
                        LastStatus = "PawnIOLib.dll missing required pawnio_* exports";
                    }
                    else
                    {
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
                        IsDirectDeviceMode = false;
                    }
                }

                // Fallback: If PawnIOLib.dll wasn't present (e.g. PawnIO installed via CapFrameX, FanControl,
                // HWiNFO, or LibreHardwareMonitor), talk directly to \\?\GLOBALROOT\Device\PawnIO via DeviceIoControl.
                // PawnIO.sys enforces EV signature verification on every loaded .bin module inside the kernel.
                if (!IsLibraryLoaded)
                {
                    if (!IsDeviceAccessible())
                    {
                        if (string.IsNullOrEmpty(LastIntegrityError))
                        {
                            LastStatus = "PawnIO driver not installed (install from https://pawnio.eu for HVCI support)";
                        }
                        return false;
                    }
                    directMode = true;
                    IsDirectDeviceMode = true;
                    IsSignatureVerified = true;
                    LibraryPath = PAWNIO_DEVICE_PATH;
                }

                // Load signed LPC Super I/O module if present
                string lpcBlob = FindModuleBlobPath("LpcIO");
                if (!string.IsNullOrEmpty(lpcBlob))
                {
                    _hLpcModule = TryOpenAndLoadModule(lpcBlob, directMode);
                    HasLpcModule = (_hLpcModule != IntPtr.Zero);
                    if (HasLpcModule)
                    {
                        // Initialize default primary Super I/O slot (0x2E/0x2F) on LpcIO.bin (best-effort)
                        ulong retSz;
                        ExecuteModuleFunction(_hLpcModule, "ioctl_select_slot", new ulong[] { 0UL }, null, out retSz);
                    }
                }

                // Load signed ACPI EC module if present
                string ecBlob = FindModuleBlobPath("LpcACPIEC");
                if (!string.IsNullOrEmpty(ecBlob))
                {
                    _hAcpiEcModule = TryOpenAndLoadModule(ecBlob, directMode);
                    HasAcpiEcModule = (_hAcpiEcModule != IntPtr.Zero);
                }

                // Load signed AMD Zen SMN telemetry module if present (AMDFamily17.bin / RyzenSMU.bin)
                string smnBlob = FindModuleBlobPath("AMDFamily17") ?? FindModuleBlobPath("RyzenSMU");
                if (!string.IsNullOrEmpty(smnBlob))
                {
                    _hAmdSmnModule = TryOpenAndLoadModule(smnBlob, directMode);
                    HasAmdSmnModule = (_hAmdSmnModule != IntPtr.Zero);
                }

                if (!HasLpcModule && !HasAcpiEcModule && !HasAmdSmnModule)
                {
                    string verStr = VersionString;
                    // Release DLL handle & clear delegates so repeated retries never leak HMODULE references
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
                    IsDirectDeviceMode = false;
                    IsSignatureVerified = false;
                    LastStatus = "PawnIO detected (" + verStr + "), but no signed LpcIO.bin / LpcACPIEC.bin / AMDFamily17.bin modules could be loaded";
                    return false;
                }

                LastIntegrityError = null;
                LastStatus = string.Format("PawnIO {0} active (HVCI-safe modules: {1})",
                    directMode ? "Direct Device" : ("v" + VersionString),
                    LoadedModulesSummary);
                return true;
            }
        }

        private static IntPtr TryOpenAndLoadModule(string blobPath, bool directMode)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                byte[] blob = File.ReadAllBytes(blobPath);
                if (blob == null || blob.Length < 16 || blob.Length > 2 * 1024 * 1024)
                    return IntPtr.Zero;

                if (directMode)
                {
                    h = CreateFileW(
                        PAWNIO_DEVICE_PATH,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);
                    if (h == IntPtr.Zero || h == INVALID_HANDLE_VALUE)
                        return IntPtr.Zero;

                    uint returned;
                    bool ok = DeviceIoControl(h, IOCTL_PIO_LOAD_BINARY, blob, (uint)blob.Length, null, 0u, out returned, IntPtr.Zero);
                    if (!ok)
                    {
                        CloseHandle(h);
                        return IntPtr.Zero;
                    }
                    return h;
                }
                else
                {
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
            }
            catch
            {
                if (h != IntPtr.Zero && h != INVALID_HANDLE_VALUE)
                {
                    try
                    {
                        if (directMode) CloseHandle(h);
                        else if (_fnClose != null) _fnClose(h);
                    }
                    catch { }
                }
                return IntPtr.Zero;
            }
        }

        private static bool ExecuteModuleFunction(IntPtr handle, string fnName, ulong[] input, ulong[] output, out ulong returnSize)
        {
            returnSize = 0;
            if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE || string.IsNullOrEmpty(fnName))
                return false;

            try
            {
                if (IsDirectDeviceMode)
                {
                    int inCount = input != null ? input.Length : 0;
                    int outCount = output != null ? output.Length : 0;
                    byte[] totalIn = new byte[FN_NAME_LENGTH + (inCount * sizeof(ulong))];
                    byte[] nameBytes = Encoding.ASCII.GetBytes(fnName);
                    Buffer.BlockCopy(nameBytes, 0, totalIn, 0, Math.Min(FN_NAME_LENGTH - 1, nameBytes.Length));
                    if (inCount > 0)
                        Buffer.BlockCopy(input, 0, totalIn, FN_NAME_LENGTH, inCount * sizeof(ulong));

                    byte[] rawOut = outCount > 0 ? new byte[outCount * sizeof(ulong)] : null;
                    uint bytesRet;
                    bool ok = DeviceIoControl(
                        handle,
                        IOCTL_PIO_EXECUTE_FN,
                        totalIn,
                        (uint)totalIn.Length,
                        rawOut,
                        rawOut != null ? (uint)rawOut.Length : 0u,
                        out bytesRet,
                        IntPtr.Zero);
                    if (!ok) return false;

                    if (rawOut != null && output != null && bytesRet > 0)
                    {
                        int copyBytes = Math.Min((int)bytesRet, output.Length * sizeof(ulong));
                        Buffer.BlockCopy(rawOut, 0, output, 0, copyBytes);
                        returnSize = (ulong)(copyBytes / sizeof(ulong));
                    }
                    return true;
                }
                else
                {
                    if (_fnExecute == null) return false;
                    ulong[] safeIn = input ?? new ulong[0];
                    ulong[] safeOut = output ?? new ulong[0];
                    UIntPtr retSz;
                    int hr = _fnExecute(handle, fnName, safeIn, new UIntPtr((uint)safeIn.Length), safeOut, new UIntPtr((uint)safeOut.Length), out retSz);
                    if (hr == 0)
                    {
                        returnSize = retSz.ToUInt64();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryExecuteAcpiEcPortRead(byte port, out byte value)
        {
            value = 0xFF;
            if (port != ACPI_EC_DATA_PORT && port != ACPI_EC_CMD_PORT) return false;

            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_pio_read", new ulong[] { port });
                if (res != null && res.Length > 0)
                {
                    value = (byte)(res[0] & 0xFF);
                    return true;
                }
                return false;
            }

            if (!HasAcpiEcModule || _hAcpiEcModule == IntPtr.Zero) return false;
            ulong[] outp = new ulong[1];
            ulong retSize;
            if (ExecuteModuleFunction(_hAcpiEcModule, "ioctl_pio_read", new ulong[] { port }, outp, out retSize) && retSize >= 1)
            {
                value = (byte)(outp[0] & 0xFF);
                return true;
            }
            return false;
        }

        private static bool TryExecuteAcpiEcPortWrite(byte port, byte value)
        {
            if (port != ACPI_EC_DATA_PORT && port != ACPI_EC_CMD_PORT) return false;

            if (SimulatedAvailable && SimulatedExecuteHook != null)
            {
                ulong[] res = SimulatedExecuteHook("ioctl_pio_write", new ulong[] { port, value });
                return res != null;
            }

            if (!HasAcpiEcModule || _hAcpiEcModule == IntPtr.Zero) return false;
            ulong retSize;
            return ExecuteModuleFunction(_hAcpiEcModule, "ioctl_pio_write", new ulong[] { port, value }, null, out retSize);
        }

        /// <summary>
        /// Selects the active LPC Super I/O configuration port slot in PawnIO LpcIO.bin
        /// (slot 0 = 0x2E/0x2F, slot 1 = 0x4E/0x4F). Note: LpcIO resets its discovered BAR list
        /// whenever the slot changes, so TryFindSuperIoBars() must be called after entering config mode.
        /// </summary>
        public static bool TrySelectSuperIoSlot(int slot)
        {
            if (slot != 0 && slot != 1) return false;

            _ioLock.EnterReadLock();
            try
            {
                if (SimulatedAvailable && SimulatedExecuteHook != null)
                {
                    ulong[] res = SimulatedExecuteHook("ioctl_select_slot", new ulong[] { (ulong)slot });
                    return res != null;
                }

                if (!HasLpcModule || _hLpcModule == IntPtr.Zero) return false;
                ulong retSize;
                return ExecuteModuleFunction(_hLpcModule, "ioctl_select_slot", new ulong[] { (ulong)slot }, null, out retSize);
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Invokes LpcIO.bin ioctl_find_bars while the Super I/O chip is in configuration mode so PawnIO
        /// populates its internal allowed BAR table (e.g. IT8613E Hardware Monitor @ 0x0A30) before
        /// subsequent ioctl_pio_inb / ioctl_pio_outb calls on those hardware monitor ports.
        /// Caller must hold the global ISA/EC mutex and re-select LDN (Reg 0x07) afterwards if needed.
        /// </summary>
        public static bool TryFindSuperIoBars()
        {
            _ioLock.EnterReadLock();
            try
            {
                if (SimulatedAvailable && SimulatedExecuteHook != null)
                {
                    ulong[] res = SimulatedExecuteHook("ioctl_find_bars", new ulong[0]);
                    return res != null;
                }

                if (!HasLpcModule || _hLpcModule == IntPtr.Zero) return false;
                ulong retSize;
                return ExecuteModuleFunction(_hLpcModule, "ioctl_find_bars", null, null, out retSize);
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Reads a single byte from a whitelisted LPC / Super I/O or ACPI EC port via PawnIO.
        /// Routes ports 0x62/0x66 to LpcACPIEC.bin (ioctl_pio_read) and LPC/Super I/O ports to
        /// LpcIO.bin (ioctl_pio_inb). Enforces NativeInpOut.IsPortWhitelisted() fail-closed first.
        /// </summary>
        public static bool TryReadPortByte(ushort port, out byte value)
        {
            value = 0xFF;
            if (!NativeInpOut.IsPortWhitelisted(port, false)) return false;

            _ioLock.EnterReadLock();
            try
            {
                if ((port == ACPI_EC_DATA_PORT || port == ACPI_EC_CMD_PORT) && (HasAcpiEcModule || SimulatedAvailable))
                {
                    if (TryExecuteAcpiEcPortRead((byte)port, out value))
                        return true;
                }

                if (SimulatedAvailable && SimulatedExecuteHook != null)
                {
                    ulong[] res = SimulatedExecuteHook("ioctl_pio_inb", new ulong[] { port });
                    if (res != null && res.Length > 0)
                    {
                        value = (byte)(res[0] & 0xFF);
                        return true;
                    }
                    return false;
                }

                if (!HasLpcModule || _hLpcModule == IntPtr.Zero) return false;
                ulong[] outp = new ulong[1];
                ulong retSize;
                if (ExecuteModuleFunction(_hLpcModule, "ioctl_pio_inb", new ulong[] { port }, outp, out retSize) && retSize >= 1)
                {
                    value = (byte)(outp[0] & 0xFF);
                    return true;
                }
                return false;
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Writes a single byte to a whitelisted LPC / Super I/O or ACPI EC port via PawnIO.
        /// Routes ports 0x62/0x66 to LpcACPIEC.bin (ioctl_pio_write) and LPC/Super I/O ports to
        /// LpcIO.bin (ioctl_pio_outb). Enforces NativeInpOut.IsPortWhitelisted() fail-closed first.
        /// </summary>
        public static bool TryWritePortByte(ushort port, byte value)
        {
            if (!NativeInpOut.IsPortWhitelisted(port, true)) return false;

            _ioLock.EnterReadLock();
            try
            {
                if ((port == ACPI_EC_DATA_PORT || port == ACPI_EC_CMD_PORT) && (HasAcpiEcModule || SimulatedAvailable))
                {
                    if (TryExecuteAcpiEcPortWrite((byte)port, value))
                        return true;
                }

                if (SimulatedAvailable && SimulatedExecuteHook != null)
                {
                    ulong[] res = SimulatedExecuteHook("ioctl_pio_outb", new ulong[] { port, value });
                    return res != null;
                }

                if (!HasLpcModule || _hLpcModule == IntPtr.Zero) return false;
                ulong retSize;
                return ExecuteModuleFunction(_hLpcModule, "ioctl_pio_outb", new ulong[] { port, value }, null, out retSize);
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        private static bool WaitAcpiEcStatus(byte mask, bool wantSet, int timeoutMs)
        {
            byte status;
            if (!TryExecuteAcpiEcPortRead(ACPI_EC_CMD_PORT, out status) || status == 0xFF)
                return false;
            if (wantSet ? ((status & mask) != 0) : ((status & mask) == 0))
                return true;

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds <= timeoutMs)
            {
                Thread.SpinWait(40);
                if (!TryExecuteAcpiEcPortRead(ACPI_EC_CMD_PORT, out status) || status == 0xFF)
                    return false;
                if (wantSet ? ((status & mask) != 0) : ((status & mask) == 0))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Reads a single ACPI EC register via the official PawnIO LpcACPIEC.bin module
        /// (performing the standard 0x66/0x62 IBF/OBF ACPI EC protocol over ioctl_pio_read/write).
        /// </summary>
        public static bool TryReadAcpiEcByte(byte reg, out byte value)
        {
            value = 0xFF;
            _ioLock.EnterReadLock();
            try
            {
                if (!HasAcpiEcModule && !SimulatedAvailable) return false;

                // Drain stale OBF if present
                byte st;
                if (TryExecuteAcpiEcPortRead(ACPI_EC_CMD_PORT, out st) && st != 0xFF && (st & ACPI_EC_FLAG_OBF) != 0)
                {
                    byte discard;
                    TryExecuteAcpiEcPortRead(ACPI_EC_DATA_PORT, out discard);
                }

                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!TryExecuteAcpiEcPortWrite(ACPI_EC_CMD_PORT, ACPI_EC_CMD_READ)) return false;
                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!TryExecuteAcpiEcPortWrite(ACPI_EC_DATA_PORT, reg)) return false;
                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_OBF, true, 30)) return false;
                return TryExecuteAcpiEcPortRead(ACPI_EC_DATA_PORT, out value);
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Writes a single ACPI EC register via the official PawnIO LpcACPIEC.bin module
        /// (performing the standard 0x66/0x62 IBF ACPI EC protocol over ioctl_pio_read/write).
        /// </summary>
        public static bool TryWriteAcpiEcByte(byte reg, byte value)
        {
            _ioLock.EnterReadLock();
            try
            {
                if (!HasAcpiEcModule && !SimulatedAvailable) return false;

                byte st;
                if (TryExecuteAcpiEcPortRead(ACPI_EC_CMD_PORT, out st) && st != 0xFF && (st & ACPI_EC_FLAG_OBF) != 0)
                {
                    byte discard;
                    TryExecuteAcpiEcPortRead(ACPI_EC_DATA_PORT, out discard);
                }

                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!TryExecuteAcpiEcPortWrite(ACPI_EC_CMD_PORT, ACPI_EC_CMD_WRITE)) return false;
                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!TryExecuteAcpiEcPortWrite(ACPI_EC_DATA_PORT, reg)) return false;
                if (!WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30)) return false;
                if (!TryExecuteAcpiEcPortWrite(ACPI_EC_DATA_PORT, value)) return false;
                return WaitAcpiEcStatus(ACPI_EC_FLAG_IBF, false, 30);
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Fail-closed allowlist for read-only AMD SMN thermal & telemetry registers via PawnIO AMDFamily17.
        /// </summary>
        public static bool IsSmnReadAddressAllowed(uint smnAddress)
        {
            // THM_TCON_CUR_TMP (0x00059800), THM_TCON_HTC (0x00059808), and Zen CCD Tdie sensors (0x00059954..0x00059970)
            if (smnAddress == 0x00059800U || smnAddress == 0x00059808U) return true;
            if (smnAddress >= 0x00059954U && smnAddress <= 0x00059970U && ((smnAddress & 3U) == 0)) return true;
            return false;
        }

        /// <summary>
        /// Reads a 32-bit AMD SMN telemetry register (e.g. 0x00059800 Tctl/Tdie) via the signed PawnIO AMDFamily17 module.
        /// Enables HVCI-compatible silicon junction temperature monitoring without legacy vulnerable drivers.
        /// </summary>
        public static bool TryReadAmdSmn(uint smnAddress, out uint value)
        {
            value = 0;
            if (!IsSmnReadAddressAllowed(smnAddress)) return false;

            _ioLock.EnterReadLock();
            try
            {
                if (SimulatedAvailable && SimulatedExecuteHook != null)
                {
                    ulong[] res = SimulatedExecuteHook("ioctl_read_smn", new ulong[] { smnAddress });
                    if (res != null && res.Length > 0)
                    {
                        value = unchecked((uint)(res[0] & 0xFFFFFFFFUL));
                        return true;
                    }
                    return false;
                }

                if (!HasAmdSmnModule || _hAmdSmnModule == IntPtr.Zero) return false;
                ulong[] outp = new ulong[1];
                ulong retSize;
                if (ExecuteModuleFunction(_hAmdSmnModule, "ioctl_read_smn", new ulong[] { smnAddress }, outp, out retSize) && retSize >= 1)
                {
                    value = unchecked((uint)(outp[0] & 0xFFFFFFFFUL));
                    return true;
                }
                return false;
            }
            finally
            {
                _ioLock.ExitReadLock();
            }
        }

        private static void CloseModuleHandle(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE)
            {
                handle = IntPtr.Zero;
                return;
            }
            try
            {
                if (IsDirectDeviceMode) CloseHandle(handle);
                else if (_fnClose != null) _fnClose(handle);
            }
            catch { }
            handle = IntPtr.Zero;
        }

        public static void Shutdown()
        {
            lock (_sync)
            {
                _ioLock.EnterWriteLock();
                try
                {
                    CloseModuleHandle(ref _hLpcModule);
                    CloseModuleHandle(ref _hAcpiEcModule);
                    CloseModuleHandle(ref _hAmdSmnModule);

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
                    IsDirectDeviceMode = false;
                    IsSignatureVerified = false;
                    HasLpcModule = false;
                    HasAcpiEcModule = false;
                    HasAmdSmnModule = false;
                    DriverVersionRaw = 0;
                    LibraryPath = null;
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
