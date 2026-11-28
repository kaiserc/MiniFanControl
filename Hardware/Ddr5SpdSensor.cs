using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniFanControl
{
    /// <summary>
    /// Encapsulates AMD FCH SMBus DDR5 SPD5118 dual-SODIMM thermal sensing,
    /// SMBIOS Type-17 physical stick-count detection, and 3-miss 30-second backoff throttling.
    /// Synchronized via Global\Access_SMBUS.HTP.Method / Global\Access_PCI with HWiNFO64.
    /// </summary>
    public static class Ddr5SpdSensor
    {
        private static ushort _amdSmbusBase = 0;
        private static bool _smbusBaseProbed = false;
        private static int _cachedDdrStickCount = 0;

        private static readonly RefreshThrottle _spdThrottleStick0 = new RefreshThrottle(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero);
        private static readonly RefreshThrottle _spdThrottleStick1 = new RefreshThrottle(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(250));
        private static int _cachedSpd1 = 0;
        private static int _cachedSpd2 = 0;
        private static bool _cachedSpd1Ok = false;
        private static bool _cachedSpd2Ok = false;
        private static int _spd1ConsecutiveMisses = 0;
        private static int _spd2ConsecutiveMisses = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint ProviderSignature, uint TableId, IntPtr pBuffer, uint BufferSize);

        public static void ResetBackoffState()
        {
            _spd1ConsecutiveMisses = 0;
            _spd2ConsecutiveMisses = 0;
            _spdThrottleStick0.Interval = TimeSpan.FromMilliseconds(1500);
            _spdThrottleStick1.Interval = TimeSpan.FromMilliseconds(1500);
            _spdThrottleStick0.Reset();
            _spdThrottleStick1.Reset();
        }

        /// <summary>
        /// Decodes a JEDEC SPD5118 11-bit signed two's-complement temperature register (0.25°C/LSB).
        /// </summary>
        public static bool DecodeSpd5118Temperature(byte lsb, byte msb, out int tempC)
        {
            tempC = 0;
            int raw = ((msb & 0x07) << 8) | lsb;
            if ((msb & 0x10) != 0) raw = raw - 2048;
            double dec = raw * 0.25;
            if (dec >= 15.0 && dec <= 95.0)
            {
                tempC = (int)Math.Round(dec);
                return true;
            }
            return false;
        }

        public static ushort GetAmdSmbusBase()
        {
            if (_smbusBaseProbed) return _amdSmbusBase;
            _smbusBaseProbed = true;
            try
            {
                // AMD FCH SMBus Controller is PCI device 0:20:0 (0x8000A000); offset 0x90 holds base address (bits 15:5)
                if (EcBusMutex.WaitPci(25))
                {
                    try
                    {
                        uint orig = NativeInpOut.DlPortReadPortUlong(0xCF8);
                        NativeInpOut.DlPortWritePortUlong(0xCF8, 0x8000A090);
                        uint val = NativeInpOut.DlPortReadPortUlong(0xCFC);
                        NativeInpOut.DlPortWritePortUlong(0xCF8, orig);
                        if (val != 0 && val != 0xFFFFFFFF)
                        {
                            ushort p = (ushort)(val & 0xFFE0);
                            if (p >= 0x0B00 && p <= 0x0B3F)
                            {
                                _amdSmbusBase = p;
                                return _amdSmbusBase;
                            }
                        }
                    }
                    finally
                    {
                        EcBusMutex.ReleasePci();
                    }
                }
            }
            catch { }

            foreach (ushort testPort in new ushort[] { 0x0B00, 0x0B20 })
            {
                byte sts = NativeInpOut.DlPortReadPortUchar(testPort);
                if (sts != 0xFF && (sts & 0x01) == 0)
                {
                    _amdSmbusBase = testPort;
                    return _amdSmbusBase;
                }
            }
            return 0;
        }

        public static bool ReadSpdHubTemp(ushort smbusBase, byte slaveAddr, out int tempC)
        {
            tempC = 0;
            if (smbusBase == 0) return false;

            if (!EcBusMutex.WaitSmbus(25)) return false;
            try
            {
                ushort stsPort = smbusBase;
                ushort cntPort = (ushort)(smbusBase + 0x02);
                ushort cmdPort = (ushort)(smbusBase + 0x03);
                ushort addPort = (ushort)(smbusBase + 0x04);
                ushort dat0Port = (ushort)(smbusBase + 0x05);
                ushort dat1Port = (ushort)(smbusBase + 0x06);

                byte sts = NativeInpOut.DlPortReadPortUchar(stsPort);
                if (sts == 0xFF) return false;

                // Clear previous interrupt/error status (W1C bits)
                NativeInpOut.DlPortWritePortUchar(stsPort, (byte)(sts & 0x1E));

                // Wait for host idle (bit 0 == 0)
                int timeout = 50;
                while ((NativeInpOut.DlPortReadPortUchar(stsPort) & 0x01) != 0 && --timeout > 0)
                {
                    Thread.Sleep(1);
                }
                if (timeout == 0) return false;

                // Slave address: (slaveAddr << 1) | 0x01 (Read), JEDEC SPD5118 register 0x31, Word Data + Start (0x4C)
                NativeInpOut.DlPortWritePortUchar(addPort, (byte)((slaveAddr << 1) | 0x01));
                NativeInpOut.DlPortWritePortUchar(cmdPort, 0x31);
                NativeInpOut.DlPortWritePortUchar(cntPort, 0x4C);

                timeout = 100;
                byte finalSts = 0;
                while (--timeout > 0)
                {
                    finalSts = NativeInpOut.DlPortReadPortUchar(stsPort);
                    if ((finalSts & 0x1E) != 0) break;
                    Thread.Sleep(1);
                }

                if ((finalSts & 0x1C) != 0 || (finalSts & 0x02) == 0)
                {
                    NativeInpOut.DlPortWritePortUchar(stsPort, finalSts);
                    return false;
                }

                byte lsb = NativeInpOut.DlPortReadPortUchar(dat0Port);
                byte msb = NativeInpOut.DlPortReadPortUchar(dat1Port);
                NativeInpOut.DlPortWritePortUchar(stsPort, finalSts);

                return DecodeSpd5118Temperature(lsb, msb, out tempC);
            }
            catch
            {
                return false;
            }
            finally
            {
                EcBusMutex.ReleaseSmbus();
            }
        }

        public static void SampleDualSticks(bool allowSmbusProbe, out int spd1, out bool spd1Ok, out int spd2, out bool spd2Ok)
        {
            ushort smbusBase = allowSmbusProbe ? GetAmdSmbusBase() : (ushort)0;
            if (smbusBase != 0)
            {
                if (_spdThrottleStick0.TryBegin())
                {
                    int s1;
                    _cachedSpd1Ok = ReadSpdHubTemp(smbusBase, 0x50, out s1) || ReadSpdHubTemp(smbusBase, 0x51, out s1);
                    if (_cachedSpd1Ok)
                    {
                        _cachedSpd1 = s1;
                        _spd1ConsecutiveMisses = 0;
                        _spdThrottleStick0.Interval = TimeSpan.FromMilliseconds(1500);
                    }
                    else
                    {
                        _spd1ConsecutiveMisses++;
                        if (_spd1ConsecutiveMisses >= 3)
                            _spdThrottleStick0.Interval = TimeSpan.FromSeconds(30);
                    }
                }
                if (_spdThrottleStick1.TryBegin())
                {
                    int s2;
                    _cachedSpd2Ok = ReadSpdHubTemp(smbusBase, 0x52, out s2) || ReadSpdHubTemp(smbusBase, 0x53, out s2);
                    if (_cachedSpd2Ok)
                    {
                        _cachedSpd2 = s2;
                        _spd2ConsecutiveMisses = 0;
                        _spdThrottleStick1.Interval = TimeSpan.FromMilliseconds(1500);
                    }
                    else
                    {
                        _spd2ConsecutiveMisses++;
                        if (_spd2ConsecutiveMisses >= 3)
                            _spdThrottleStick1.Interval = TimeSpan.FromSeconds(30);
                    }
                }
            }

            spd1 = _cachedSpd1;
            spd2 = _cachedSpd2;
            spd1Ok = _cachedSpd1Ok;
            spd2Ok = _cachedSpd2Ok;
        }

        private static int DetectPhysicalMemoryStickCountFromSmbios()
        {
            try
            {
                uint sig = 0x52534D42; // 'RSMB'
                uint size = GetSystemFirmwareTable(sig, 0, IntPtr.Zero, 0);
                if (size == 0 || size > 65536) return 0;

                byte[] buf = new byte[size];
                GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    uint read = GetSystemFirmwareTable(sig, 0, h.AddrOfPinnedObject(), size);
                    if (read < 8) return 0;

                    int offset = 8;
                    int count = 0;
                    while (offset + 4 < read)
                    {
                        byte type = buf[offset];
                        byte len = buf[offset + 1];
                        if (len < 4) break;

                        if (type == 17 && offset + len <= read && offset + 0x0E <= read)
                        {
                            ushort memSize = (ushort)(buf[offset + 0x0C] | (buf[offset + 0x0D] << 8));
                            if (memSize > 0 && memSize != 0xFFFF) count++;
                        }

                        offset += len;
                        while (offset + 1 < read && !(buf[offset] == 0 && buf[offset + 1] == 0))
                            offset++;
                        offset += 2;
                    }
                    return count;
                }
                finally
                {
                    h.Free();
                }
            }
            catch
            {
                return 0;
            }
        }

        public static int GetPhysicalStickCount(double totalGb, bool allowSmbusProbe = false)
        {
            if (_cachedDdrStickCount > 0) return _cachedDdrStickCount;

            int smbiosCount = DetectPhysicalMemoryStickCountFromSmbios();
            if (smbiosCount > 0)
            {
                _cachedDdrStickCount = smbiosCount;
                return _cachedDdrStickCount;
            }

            ushort smbus = allowSmbusProbe ? GetAmdSmbusBase() : (ushort)0;
            if (smbus != 0)
            {
                int t1, t2;
                bool ok1 = ReadSpdHubTemp(smbus, 0x50, out t1) || ReadSpdHubTemp(smbus, 0x51, out t1);
                bool ok2 = ReadSpdHubTemp(smbus, 0x52, out t2) || ReadSpdHubTemp(smbus, 0x53, out t2);
                if (ok1 && ok2)
                {
                    _cachedDdrStickCount = 2;
                    return 2;
                }
            }

            _cachedDdrStickCount = (totalGb >= 45.0 || (totalGb >= 20.0 && totalGb <= 30.0)) ? 2 : 1;
            return _cachedDdrStickCount;
        }
    }
}
