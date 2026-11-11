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
    public static class NativeInpOut
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);

        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32        = 0x00000800;
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH       = 0x00000008;

        public const ulong MIN_SAFE_PHYSICAL_ADDR = 0x000E0000;
        public const uint MAX_SAFE_MAP_SIZE = 16 * 1024 * 1024;

        private static bool _dllSearchHardened = false;
        private static IntPtr _hDriverDll = IntPtr.Zero;

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

        public static bool PreloadDriverDll()
        {
            HardenDllSearchPath();
            if (_hDriverDll != IntPtr.Zero) return true;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(appDir, "inpoutx64.dll");

            if (!File.Exists(dllPath))
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                dllPath = Path.Combine(sys32, "inpoutx64.dll");
                if (!File.Exists(dllPath)) return false;
            }

            try
            {
                FileInfo fi = new FileInfo(dllPath);
                if (fi.Length < 1024) return false;

                _hDriverDll = LoadLibraryEx(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                return _hDriverDll != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("inpoutx64.dll", EntryPoint = "IsInpOutDriverOpen")]
        public static extern int IsInpOutDriverOpen();

        [DllImport("inpoutx64.dll", EntryPoint = "MapPhysToLin")]
        public static extern IntPtr MapPhysToLin(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle);

        public static IntPtr MapPhysToLinSafe(IntPtr pbPhysAddr, uint dwPhysSize, out IntPtr pPhysicalMemoryHandle)
        {
            pPhysicalMemoryHandle = IntPtr.Zero;
            ulong addr = unchecked((ulong)pbPhysAddr.ToInt64());
            if (addr < MIN_SAFE_PHYSICAL_ADDR || dwPhysSize == 0 || dwPhysSize > MAX_SAFE_MAP_SIZE)
            {
                return IntPtr.Zero;
            }
            return MapPhysToLin(pbPhysAddr, dwPhysSize, out pPhysicalMemoryHandle);
        }

        [DllImport("inpoutx64.dll", EntryPoint = "UnmapPhysicalMemory")]
        public static extern int UnmapPhysicalMemory(IntPtr PhysicalMemoryHandle, IntPtr pbLinAddr);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortReadPortUchar")]
        public static extern byte DlPortReadPortUchar(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortWritePortUchar")]
        public static extern void DlPortWritePortUchar(ushort port, byte value);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortReadPortUlong")]
        public static extern uint DlPortReadPortUlong(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "DlPortWritePortUlong")]
        public static extern void DlPortWritePortUlong(ushort port, uint value);

        [DllImport("inpoutx64.dll", EntryPoint = "Inp32")]
        public static extern int Inp32(ushort port);

        [DllImport("inpoutx64.dll", EntryPoint = "Out32")]
        public static extern void Out32(ushort port, short value);
    }

}
