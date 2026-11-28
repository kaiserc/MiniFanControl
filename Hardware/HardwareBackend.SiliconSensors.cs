using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace MiniFanControl
{
    public partial class HardwareBackend
    {
        private static bool? _isAmdCpuCached = null;
        internal static bool? SimulatedIsAmdCpuForTest = null;

        /// <summary>
        /// Verifies via the Windows registry (zero kernel I/O) whether the host processor is AMD (AuthenticAMD).
        /// Prevents AMD-specific PCI SMN, SMU mailbox, and FCH SMBus accesses on Intel or non-AMD CPUs.
        /// </summary>
        public static bool IsAmdCpuPlatform()
        {
            if (SimulatedIsAmdCpuForTest.HasValue) return SimulatedIsAmdCpuForTest.Value;
            if (_isAmdCpuCached.HasValue) return _isAmdCpuCached.Value;

            bool isAmd = false;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                {
                    if (key != null)
                    {
                        string vendor = key.GetValue("VendorIdentifier") as string;
                        string name = key.GetValue("ProcessorNameString") as string;
                        if ((!string.IsNullOrEmpty(vendor) && vendor.IndexOf("AuthenticAMD", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(name) && name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            isAmd = true;
                        }
                    }
                }
            }
            catch { }

            _isAmdCpuCached = isAmd;
            return isAmd;
        }

        /// <summary>
        /// True only when running on an AMD CPU with a verified, writable hardware profile.
        /// Read-only / unverified desktop systems and non-AMD CPUs must never issue active
        /// SMU MP1 mailbox commands or SMBus host write transactions.
        /// </summary>
        public static bool CanRunActiveSiliconProbes(DeviceProfile profile, bool isSimulated = false)
        {
            if (isSimulated) return false;
            if (!IsAmdCpuPlatform()) return false;
            if (profile == null || profile.IsReadOnly || !profile.IsVerified) return false;
            return true;
        }

        private static ulong _amdPcieEcamBase = 0;
        private static bool _amdPcieEcamProbed = false;

        private static ulong GetAmdPcieEcamBaseNoLock()
        {
            if (_amdPcieEcamProbed) return _amdPcieEcamBase;
            _amdPcieEcamProbed = true;

            // Standard x86/AMD MCFG PCIe ECAM physical base addresses for Segment 0, Bus 0, Device 0, Function 0
            ulong[] candidates = new ulong[] { 0xE0000000UL, 0xF0000000UL, 0xF8000000UL, 0xC0000000UL };
            foreach (ulong baseAddr in candidates)
            {
                IntPtr hMem = IntPtr.Zero;
                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = NativeInpOut.MapPhysToLinSafe((IntPtr)(long)baseAddr, 4096, out hMem);
                    if (ptr != IntPtr.Zero)
                    {
                        uint vidDid = unchecked((uint)Marshal.ReadInt32(ptr, 0x00));
                        // Verify AMD Root Complex Vendor ID (0x1022)
                        if ((vidDid & 0xFFFF) == 0x1022)
                        {
                            _amdPcieEcamBase = baseAddr;
                            return _amdPcieEcamBase;
                        }
                    }
                }
                catch { }
                finally
                {
                    if (ptr != IntPtr.Zero)
                    {
                        NativeInpOut.UnmapPhysicalMemory(hMem, ptr);
                    }
                }
            }
            return 0;
        }

        internal static uint ReadAmdSmnViaPcieEcamNoLock(uint smnAddress)
        {
            ulong ecamBase = GetAmdPcieEcamBaseNoLock();
            if (ecamBase == 0) return 0;

            IntPtr hMem = IntPtr.Zero;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = NativeInpOut.MapPhysToLinSafe((IntPtr)(long)ecamBase, 4096, out hMem);
                if (ptr == IntPtr.Zero) return 0;

                // Use PCI [0:0:0] SMN aperture 0xB8 (Index) / 0xBC (Data) with atomic index restoration
                int origIdx = Marshal.ReadInt32(ptr, 0xB8);
                try
                {
                    Marshal.WriteInt32(ptr, 0xB8, unchecked((int)smnAddress));
                    return unchecked((uint)Marshal.ReadInt32(ptr, 0xBC));
                }
                finally
                {
                    try { Marshal.WriteInt32(ptr, 0xB8, origIdx); } catch { }
                }
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    NativeInpOut.UnmapPhysicalMemory(hMem, ptr);
                }
            }
        }

        public static uint ReadAmdSmn(uint smnAddress)
        {
            bool locked = EcBusMutex.WaitPci(35);
            if (!locked)
            {
                // Quick yield and retry once to cleanly interleave with concurrent monitoring tools (e.g. HWiNFO64)
                System.Threading.Thread.Sleep(5);
                locked = EcBusMutex.WaitPci(35);
                if (!locked) return 0; // Fail-closed: abort PCI read if mutex timed out
            }

            uint origCf8 = 0;
            try
            {
                // Tier 0: Official HVCI-safe PawnIO AMDFamily17 module (zero legacy driver / VBS trap issues)
                if (NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO && PawnIoDriver.HasAmdSmnModule)
                {
                    uint pawnVal;
                    if (PawnIoDriver.TryReadAmdSmn(smnAddress, out pawnVal) && pawnVal != 0 && pawnVal != 0xFFFFFFFF)
                    {
                        return pawnVal;
                    }
                }

                // Tier 1: PCIe MMIO ECAM (MCFG 0xE0000000 / 0xF0000000).
                // On Windows 11 with Hyper-V / Virtualization-Based Security (VBS), legacy 0xCF8/0xCFC
                // port I/O is trapped by the hypervisor and returns 0xFFFFFFFF, whereas PCIe MMIO ECAM
                // accesses Bus 0, Device 0, Function 0 (AMD Root Complex 0x1022) directly in physical memory.
                uint ecamVal = ReadAmdSmnViaPcieEcamNoLock(smnAddress);
                if (ecamVal != 0 && ecamVal != 0xFFFFFFFF)
                {
                    return ecamVal;
                }

                // Tier 2: Legacy PCI Config Space Port I/O (0xCF8 / 0xCFC) fallback
                try { origCf8 = NativeInpOut.DlPortReadPortUlong(0xCF8); } catch { }

                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000060);
                NativeInpOut.DlPortWritePortUlong(0xCFC, smnAddress);
                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000064);

                return NativeInpOut.DlPortReadPortUlong(0xCFC);
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (origCf8 != 0)
                {
                    try { NativeInpOut.DlPortWritePortUlong(0xCF8, origCf8); } catch { }
                }
                EcBusMutex.ReleasePci();
            }
        }

        public static bool WriteAmdSmn(uint smnAddress, uint value)
        {
            bool locked = EcBusMutex.WaitPci(25);
            if (!locked) return false; // Fail-closed: abort PCI write if mutex timed out
            uint origCf8 = 0;
            try
            {
                try { origCf8 = NativeInpOut.DlPortReadPortUlong(0xCF8); } catch { }
                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000060);
                NativeInpOut.DlPortWritePortUlong(0xCFC, smnAddress);
                NativeInpOut.DlPortWritePortUlong(0xCF8, 0x80000064);
                NativeInpOut.DlPortWritePortUlong(0xCFC, value);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { NativeInpOut.DlPortWritePortUlong(0xCF8, origCf8); } catch { }
                EcBusMutex.ReleasePci();
            }
        }

        private static int _lastValidSmnCpuTemp = 0;
        private static DateTime _lastValidSmnTime = DateTime.MinValue;

        public static int DecodeAmdSmnTemp(uint raw)
        {
            if (raw == 0 || raw == 0xFFFFFFFF) return 0;
            uint tRaw = (raw >> 21) & 0x7FF;
            double t = tRaw * 0.125;
            // CUR_TEMP_RANGE_SEL is bit 19 (0x00080000). Only subtract 49 C if bit 19 is explicitly set
            // AND the resulting temperature remains physically valid (>= 20 C). Modern Zen 4/5 APUs
            // (e.g. Ryzen AI 9 HX 370) report standard 0..255 C range without offset; bits 17:16
            // (0x00030000) are TJ_SEL (sensor diode selector) and must NEVER trigger -49 C subtraction.
            if ((raw & 0x00080000) != 0 && (t - 49.0 >= 20.0))
            {
                t -= 49.0;
            }

            int val = (int)Math.Round(t);
            if (val >= 15 && val <= 115)
            {
                return val;
            }
            return 0;
        }

        public static int ReadAmdCpuTempTctl()
        {
            if (!IsAmdCpuPlatform()) return 0;
            try
            {
                uint raw = ReadAmdSmn(0x00059800); // AMD Zen / Strix Point THM_TCON_CUR_TMP (Tctl/Tdie)
                int val = DecodeAmdSmnTemp(raw);
                if (val > 0)
                {
                    _lastValidSmnCpuTemp = val;
                    _lastValidSmnTime = DateTime.UtcNow;
                    return val;
                }

                // If SMN read momentarily collided with another tool (e.g. HWiNFO64) on 0xCF8/0xCFC mutex,
                // return the last valid SMN Tctl reading if it's less than 4.5 seconds old (bridges relaxed 2.0s polling).
                if (_lastValidSmnCpuTemp >= 15 && (DateTime.UtcNow - _lastValidSmnTime).TotalMilliseconds < 4500)
                {
                    return _lastValidSmnCpuTemp;
                }
            }
            catch { }
            return 0;
        }

        private static void ResetSpdBackoffState()
        {
            Ddr5SpdSensor.ResetBackoffState();
        }

        private static int GetDdrStickCount(double totalGb, bool allowSmbusProbe = false)
        {
            return Ddr5SpdSensor.GetPhysicalStickCount(totalGb, allowSmbusProbe);
        }

        public static int EstimateApuPowerWatts(int cpuTemp, int gpuTemp, int modeCode)

        {

            int peakTemp = Math.Max(cpuTemp, gpuTemp);

            if (peakTemp <= 0) return 0;

            int maxTdp = modeCode == 1 ? 65 : (modeCode == 2 ? 35 : 54);

            double norm = Math.Max(0.0, Math.Min(1.0, (peakTemp - 36.0) / 52.0));

            int est = (int)Math.Round(8.0 + Math.Pow(norm, 1.35) * (maxTdp - 8.0));

            return Math.Max(6, Math.Min(maxTdp, est));

        }

    }
}
