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
    public static class FpsMonitor
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint FILE_MAP_READ = 0x0004;

        /// <summary>
        /// Reads live in-game FPS from RTSS shared memory if available.
        /// Returns -1 if RTSS is not running or no 3D app is currently presenting.
        /// </summary>
        public static int TryGetRtssFps()
        {
            IntPtr hMap = IntPtr.Zero;
            IntPtr pView = IntPtr.Zero;
            try
            {
                hMap = OpenFileMapping(FILE_MAP_READ, false, "RTSSSharedMemoryV2");
                if (hMap == IntPtr.Zero)
                    return -1;

                pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, (UIntPtr)65536);
                if (pView == IntPtr.Zero)
                    return -1;

                // Check signature 'RTSS' (0x53535452)
                int sig = Marshal.ReadInt32(pView, 0);
                if (sig != 0x53535452)
                    return -1;

                int appEntrySize = Marshal.ReadInt32(pView, 8);
                int appArrOffset = Marshal.ReadInt32(pView, 12);
                int appArrSize   = Marshal.ReadInt32(pView, 16);

                if (appEntrySize <= 0 || appArrOffset <= 0 || appArrSize <= 0)
                    return -1;

                int maxFps = -1;
                for (int i = 0; i < appArrSize; i++)
                {
                    IntPtr pEntry = new IntPtr(pView.ToInt64() + appArrOffset + i * appEntrySize);
                    int pid = Marshal.ReadInt32(pEntry, 0);
                    if (pid == 0) continue;

                    // dwInstantaneousFramerate is at offset 280 (framerate * 10)
                    int rawFps10 = Marshal.ReadInt32(pEntry, 280);
                    int fps = (rawFps10 > 0) ? (rawFps10 / 10) : 0;
                    if (fps > maxFps)
                        maxFps = fps;
                }
                return maxFps;
            }
            catch
            {
                return -1;
            }
            finally
            {
                if (pView != IntPtr.Zero) UnmapViewOfFile(pView);
                if (hMap != IntPtr.Zero) CloseHandle(hMap);
            }
        }
    }

}
