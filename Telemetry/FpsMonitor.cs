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
    public struct RtssTelemetry
    {
        public bool IsAvailable;
        public bool IsRtssRunning;   // True if RTSS shared memory was successfully opened and verified
        public int Fps;              // Live Instantaneous Framerate (e.g. 144)
        public double FrametimeMs;   // Live Instantaneous Frametime in milliseconds (e.g. 6.94 ms)
        public int AvgFps;           // Statistical / High-speed average framerate
        public int MinFps;           // Minimum instantaneous framerate
        public int MaxFps;           // Maximum instantaneous framerate
        public int P1LowFps;         // 1% Low Framerate (from 1024-frame high-speed buffer)
        public int P01LowFps;        // 0.1% Low Framerate (from 1024-frame high-speed buffer)
        public string ProcessName;   // Name of active 3D process presenting frames
        public int FrameCount;       // Number of valid sampled frames in circular buffer
    }

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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const uint FILE_MAP_READ = 0x0004;
        private const uint FILE_MAP_ALL_ACCESS = 0x000F001F;
        private const uint FILE_MAP_COPY = 0x0001;

        private static bool IsIgnoredProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = Path.GetFileName(name).ToLowerInvariant();
            return n == "dwm.exe" ||
                   n == "explorer.exe" ||
                   n == "sechealthui.exe" ||
                   n == "shellexperiencehost.exe" ||
                   n == "startmenuexperiencehost.exe" ||
                   n == "searchhost.exe" ||
                   n == "applicationframehost.exe" ||
                   n == "systemsettings.exe" ||
                   n == "textinputhost.exe" ||
                   n == "lockapp.exe" ||
                   n == "taskmgr.exe" ||
                   n == "minifancontrol.exe" ||
                   n == "minifan-helper.exe" ||
                   n == "steam.exe" ||
                   n == "steamwebhelper.exe" ||
                   n == "gameoverlayui.exe" ||
                   n == "amdow.exe" ||
                   n == "amdrsserv.exe" ||
                   n == "radeonsoftware.exe" ||
                   n == "discord.exe" ||
                   n == "discordcanary.exe" ||
                   n == "discordptb.exe" ||
                   n == "spotify.exe" ||
                   n == "chrome.exe" ||
                   n == "msedge.exe" ||
                   n == "firefox.exe" ||
                   n == "epicgameslauncher.exe" ||
                   n == "epicwebhelper.exe" ||
                   n == "galaxyclient.exe" ||
                   n == "battlenet.exe" ||
                   n == "rtss.exe" ||
                   n == "rtsshooksloader.exe" ||
                   n == "rtsshooksloader64.exe";
        }

        private static readonly string[] MappingNames = new[]
        {
            "RTSSSharedMemoryV2",
            "Global\\RTSSSharedMemoryV2",
            "Local\\RTSSSharedMemoryV2",
            "RTSSSharedMemory",
            "Global\\RTSSSharedMemory"
        };

        private static readonly uint[] DesiredAccesses = new uint[]
        {
            FILE_MAP_READ,
            FILE_MAP_ALL_ACCESS,
            FILE_MAP_COPY
        };

        /// <summary>
        /// Reads live in-game FPS, Frametime (ms), Average FPS, and 0.1% / 1% Lows from RTSS shared memory if available.
        /// Returns an unavailable RtssTelemetry struct if RTSS is not running or no 3D app is currently presenting.
        /// </summary>
        public static RtssTelemetry GetTelemetry()
        {
            RtssTelemetry best = new RtssTelemetry
            {
                IsAvailable = false,
                IsRtssRunning = false,
                Fps = -1,
                FrametimeMs = 0.0,
                AvgFps = 0,
                MinFps = 0,
                MaxFps = 0,
                P1LowFps = 0,
                P01LowFps = 0,
                ProcessName = null,
                FrameCount = 0
            };

            IntPtr hMap = IntPtr.Zero;
            IntPtr pView = IntPtr.Zero;
            uint grantedAccess = FILE_MAP_READ;

            try
            {
                foreach (string name in MappingNames)
                {
                    foreach (uint access in DesiredAccesses)
                    {
                        hMap = OpenFileMapping(access, false, name);
                        if (hMap != IntPtr.Zero)
                        {
                            grantedAccess = access;
                            break;
                        }
                    }
                    if (hMap != IntPtr.Zero) break;
                }

                if (hMap == IntPtr.Zero)
                    return best;

                pView = MapViewOfFile(hMap, grantedAccess == FILE_MAP_ALL_ACCESS ? FILE_MAP_ALL_ACCESS : FILE_MAP_READ, 0, 0, UIntPtr.Zero);
                if (pView == IntPtr.Zero)
                {
                    pView = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
                }
                if (pView == IntPtr.Zero)
                    return best;

                // Check signature 'RTSS' (0x53535452 in little-endian or 0x52545353)
                int sig = Marshal.ReadInt32(pView, 0);
                if (sig != 0x53535452 && sig != 0x52545353)
                    return best;

                // RTSS shared memory is verified open and active
                best.IsRtssRunning = true;

                int appEntrySize = Marshal.ReadInt32(pView, 8);
                int val12 = Marshal.ReadInt32(pView, 12);
                int val16 = Marshal.ReadInt32(pView, 16);

                // In RTSS headers: dwAppArrOffset is the byte offset into shared memory (typically >= 32, often 256 or 64).
                // dwAppArrSize is the entry count (typically 1 to 256). Detect if swapped.
                int appArrOffset = val12;
                int appArrSize = val16;
                if (val12 < 32 && val16 >= 32)
                {
                    appArrOffset = val16;
                    appArrSize = val12;
                }
                else if (val12 >= 32 && val16 > 0 && val16 <= 1024)
                {
                    appArrOffset = val12;
                    appArrSize = val16;
                }

                if (appEntrySize <= 0) appEntrySize = 4440;
                if (appArrOffset <= 0) appArrOffset = 256;
                if (appArrSize <= 0 || appArrSize > 256) appArrSize = 32;

                uint fgPid = 0;
                try
                {
                    IntPtr hFg = GetForegroundWindow();
                    if (hFg != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hFg, out fgPid);
                    }
                }
                catch { }

                bool bestIsForeground = false;

                for (int i = 0; i < appArrSize; i++)
                {
                    IntPtr pEntry = new IntPtr(pView.ToInt64() + appArrOffset + i * appEntrySize);
                    int pid = Marshal.ReadInt32(pEntry, 0);
                    if (pid == 0) continue;

                    string procName = null;
                    try
                    {
                        byte[] nameBytes = new byte[260];
                        Marshal.Copy(new IntPtr(pEntry.ToInt64() + 4), nameBytes, 0, 260);
                        int nullIdx = Array.IndexOf(nameBytes, (byte)0);
                        if (nullIdx > 0)
                        {
                            string raw = Encoding.ASCII.GetString(nameBytes, 0, nullIdx).Trim();
                            procName = Path.GetFileName(raw);
                        }
                    }
                    catch { }

                    if (IsIgnoredProcess(procName))
                        continue;

                    // Timing fields: dwTime0 (268), dwTime1 (272), dwFrames (276), dwFrameTime (280 in µs)
                    int time0 = Marshal.ReadInt32(pEntry, 268);
                    int time1 = Marshal.ReadInt32(pEntry, 272);
                    int frames = Marshal.ReadInt32(pEntry, 276);
                    int rawFrameTime = Marshal.ReadInt32(pEntry, 280);

                    double curFrametimeMs = 0.0;
                    if (rawFrameTime > 0)
                    {
                        curFrametimeMs = rawFrameTime / 1000.0;
                    }

                    int intervalFps = 0;
                    if (time1 > time0 && frames > 0)
                    {
                        int dt = time1 - time0;
                        if (dt > 0)
                        {
                            intervalFps = (int)Math.Round((1000.0 * frames) / dt);
                        }
                    }

                    // Statistical aggregate fields (offsets 304, 308, 312)
                    int statMin = Marshal.ReadInt32(pEntry, 304);
                    int statAvg = Marshal.ReadInt32(pEntry, 308);
                    int statMax = Marshal.ReadInt32(pEntry, 312);

                    int curMin = statMin;
                    int curAvg = statAvg;
                    int curMax = statMax;
                    int curP1 = 0;
                    int curP01 = 0;
                    int validCount = 0;

                    // High-speed frame time buffer (RTSS v2.5+, dwStatFrameTimeBuf[1024] at offset 332 or 336)
                    int bufOffset = (appEntrySize >= 4436) ? 332 : 336;
                    int calculatedAvg = 0;
                    if (appEntrySize >= 4436)
                    {
                        int[] buf = new int[1024];
                        Marshal.Copy(new IntPtr(pEntry.ToInt64() + bufOffset), buf, 0, 1024);

                        List<int> validFrames = new List<int>(1024);
                        long sumUs = 0;
                        for (int k = 0; k < 1024; k++)
                        {
                            int us = buf[k];
                            // Valid frame duration between 250 µs (4,000 FPS) and 1,000,000 µs (1 FPS)
                            if (us >= 250 && us <= 1000000)
                            {
                                validFrames.Add(us);
                                sumUs += us;
                            }
                        }

                        if (validFrames.Count >= 20)
                        {
                            validCount = validFrames.Count;
                            validFrames.Sort(); // Ascending: index 0 is fastest frame, end is slowest frame
                            double avgUs = (double)sumUs / validFrames.Count;
                            if (avgUs > 0)
                            {
                                calculatedAvg = (int)Math.Round(1000000.0 / avgUs);
                                if (calculatedAvg > 0) curAvg = calculatedAvg;
                            }

                            // 1% Low is the 99th percentile slowest frame (1000000 / frameTimeUs)
                            int idx1 = Math.Min(validFrames.Count - 1, (int)Math.Floor(validFrames.Count * 0.99));
                            int p1Us = validFrames[idx1];
                            if (p1Us > 0) curP1 = (int)Math.Round(1000000.0 / p1Us);

                            // 0.1% Low is the 99.9th percentile slowest frame
                            int idx01 = Math.Min(validFrames.Count - 1, (int)Math.Floor(validFrames.Count * 0.999));
                            int p01Us = validFrames[idx01];
                            if (p01Us > 0) curP01 = (int)Math.Round(1000000.0 / p01Us);
                        }
                    }

                    // Primary Live Framerate: intervalFps (true aggregate frames / second over RTSS sampling window)
                    // Fallbacks: circular buffer calculatedAvg, instantaneous rawFrameTime (1M/us), or statAvg
                    int curFps = 0;
                    if (intervalFps > 0)
                    {
                        curFps = intervalFps;
                    }
                    else if (calculatedAvg > 0)
                    {
                        curFps = calculatedAvg;
                    }
                    else if (rawFrameTime > 0 && rawFrameTime <= 2000000)
                    {
                        curFps = (int)Math.Round(1000000.0 / rawFrameTime);
                    }
                    else if (curAvg > 0)
                    {
                        curFps = curAvg;
                    }

                    if (curFrametimeMs <= 0.0 && curFps > 0)
                    {
                        curFrametimeMs = 1000.0 / curFps;
                    }

                    // Fallbacks if high-speed buffer was not active
                    if (curAvg <= 0) curAvg = curFps;
                    if (curMin <= 0) curMin = curFps;
                    if (curMax <= 0) curMax = curFps;
                    if (curP1 <= 0) curP1 = curMin > 0 ? curMin : (int)(curAvg * 0.88);
                    if (curP01 <= 0) curP01 = curMin > 0 ? curMin : (int)(curAvg * 0.78);

                    // Select this entry if it is producing frames (curFps > 0)
                    if (curFps > 0)
                    {
                        bool isForeground = (fgPid != 0 && (uint)pid == fgPid);
                        bool shouldPick = false;
                        if (isForeground)
                        {
                            shouldPick = true;
                        }
                        else if (!best.IsAvailable)
                        {
                            shouldPick = true;
                        }
                        else if (bestIsForeground)
                        {
                            // Never replace active foreground game with background process
                            shouldPick = false;
                        }
                        else if (curFps > best.Fps)
                        {
                            shouldPick = true;
                        }

                        if (shouldPick)
                        {
                            best.IsAvailable = true;
                            best.Fps = curFps;
                            best.FrametimeMs = curFrametimeMs;
                            best.AvgFps = curAvg;
                            best.MinFps = curMin;
                            best.MaxFps = curMax;
                            best.P1LowFps = curP1;
                            best.P01LowFps = curP01;
                            best.ProcessName = procName;
                            best.FrameCount = validCount;
                            bestIsForeground = isForeground;
                        }
                    }
                }
                return best;
            }
            catch
            {
                return best;
            }
            finally
            {
                if (pView != IntPtr.Zero) UnmapViewOfFile(pView);
                if (hMap != IntPtr.Zero) CloseHandle(hMap);
            }
        }

        /// <summary>
        /// Reads live in-game FPS from RTSS shared memory if available.
        /// Returns -1 if RTSS is not running or no 3D app is currently presenting.
        /// </summary>
        public static int TryGetRtssFps()
        {
            RtssTelemetry t = GetTelemetry();
            return t.IsAvailable ? t.Fps : -1;
        }

        /// <summary>
        /// Computes 1% and 0.1% lows from an array of frame times in microseconds.
        /// Provided as a pure helper function for unit testing and offline frametime analysis.
        /// </summary>
        public static void CalculateLowsFromMicroseconds(int[] frameTimesUs, out int avgFps, out int p1LowFps, out int p01LowFps)
        {
            avgFps = 0;
            p1LowFps = 0;
            p01LowFps = 0;
            if (frameTimesUs == null || frameTimesUs.Length == 0) return;

            List<int> valid = new List<int>(frameTimesUs.Length);
            long sum = 0;
            for (int i = 0; i < frameTimesUs.Length; i++)
            {
                int us = frameTimesUs[i];
                if (us >= 250 && us <= 1000000)
                {
                    valid.Add(us);
                    sum += us;
                }
            }
            if (valid.Count == 0) return;

            valid.Sort();
            double avgUs = (double)sum / valid.Count;
            avgFps = avgUs > 0 ? (int)Math.Round(1000000.0 / avgUs) : 0;

            int idx1 = Math.Min(valid.Count - 1, (int)Math.Floor(valid.Count * 0.99));
            p1LowFps = valid[idx1] > 0 ? (int)Math.Round(1000000.0 / valid[idx1]) : 0;

            int idx01 = Math.Min(valid.Count - 1, (int)Math.Floor(valid.Count * 0.999));
            p01LowFps = valid[idx01] > 0 ? (int)Math.Round(1000000.0 / valid[idx01]) : 0;
        }
    }
}
