using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MiniFanControl
{
    public partial class HardwareBackend
    {
        public static bool ProbeSuperIo(ushort cfgPort, out ushort chipId, out ushort hwBase, bool allowConfigWrites = false)
        {
            chipId = 0;
            hwBase = 0;
            using (var lockScope = EcBusMutex.AcquireEc(40))
            {
                if (!lockScope.Acquired) return false; // Fail-closed: synchronize discovery with HWiNFO / LibreHardwareMonitor
                ushort valPort = (ushort)(cfgPort + 1);
                bool usePawnLpc = (NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO && PawnIoDriver.HasLpcModule);
                try
                {
                    if (usePawnLpc)
                    {
                        PawnIoDriver.TrySelectSuperIoSlot(cfgPort == 0x4E ? 1 : 0);
                    }

                    // IT87 enter sequence: 0x87, 0x01, 0x55, (0xAA on 0x4E, 0x55 on 0x2E)
                    WritePortByte(cfgPort, 0x87);
                    WritePortByte(cfgPort, 0x01);
                    WritePortByte(cfgPort, 0x55);
                    WritePortByte(cfgPort, (byte)(cfgPort == 0x4E ? 0xAA : 0x55));

                    // Read Chip ID (Register 0x20, 0x21)
                    WritePortByte(cfgPort, 0x20);
                    byte idHi = ReadPortByte(valPort);
                    WritePortByte(cfgPort, 0x21);
                    byte idLo = ReadPortByte(valPort);
                    ushort id = (ushort)((idHi << 8) | idLo);

                    if (id != 0 && id != 0xFFFF && (idHi == 0x86 || idHi == 0x87 || idHi == 0x55 || id == 0x8733))
                    {
                        chipId = id;
                        // Select Logical Device 4 (Hardware Monitor)
                        WritePortByte(cfgPort, 0x07);
                        WritePortByte(valPort, 0x04);

                        // Read Base Address (Register 0x60, 0x61) configured by BIOS
                        WritePortByte(cfgPort, 0x60);
                        byte baseHi = ReadPortByte(valPort);
                        WritePortByte(cfgPort, 0x61);
                        byte baseLo = ReadPortByte(valPort);
                        ushort candidateBase = (ushort)((baseHi << 8) | baseLo);

                        // PawnIO LpcIO.bin only permits ioctl_pio_inb/outb on BARs discovered via ioctl_find_bars
                        // while the Super I/O chip is in config mode. Re-select LDN 4 afterwards since find_bars scans LDNs.
                        if (usePawnLpc)
                        {
                            PawnIoDriver.TryFindSuperIoBars();
                            WritePortByte(cfgPort, 0x07);
                            WritePortByte(valPort, 0x04);
                        }

                        bool verified = (candidateBase != 0 && candidateBase != 0xFFFF && VerifyIt87HwBase(candidateBase, allowConfigWrites));

                        // Only reprogram base address or activate LDN4 if explicitly allowed for an active verified profile
                        if (!verified && allowConfigWrites)
                        {
                            WritePortByte(cfgPort, 0x07);
                            WritePortByte(valPort, 0x04);
                            WritePortByte(cfgPort, 0x30);
                            byte actVal = ReadPortByte(valPort);
                            if ((actVal & 0x01) == 0)
                            {
                                WritePortByte(valPort, 0x01);
                            }

                            // Some motherboards (e.g. TOPC TS-12C, Ryzen AI 9 HX 370) do not decode default 0x0290,
                            // but forward specific LPC port ranges (0x0A30 / 0x0A00 / 0x0A10 / 0x0A20) via FCH.
                            ushort[] candidates = new ushort[] { 0x0A30, 0x0A00, 0x0A10, 0x0A20, 0x0290, 0x02A0 };
                            foreach (ushort cand in candidates)
                            {
                                WritePortByte(cfgPort, 0x07);
                                WritePortByte(valPort, 0x04);
                                WritePortByte(cfgPort, 0x60);
                                WritePortByte(valPort, (byte)(cand >> 8));
                                WritePortByte(cfgPort, 0x61);
                                WritePortByte(valPort, (byte)(cand & 0xFF));
                                if (usePawnLpc)
                                {
                                    PawnIoDriver.TryFindSuperIoBars();
                                    WritePortByte(cfgPort, 0x07);
                                    WritePortByte(valPort, 0x04);
                                }
                                if (VerifyIt87HwBase(cand, true))
                                {
                                    candidateBase = cand;
                                    verified = true;
                                    break;
                                }
                            }
                        }

                        if (verified)
                        {
                            hwBase = candidateBase;
                            if (allowConfigWrites)
                            {
                                // Ensure monitor operations are started (Bit 0 of Reg 0x00)
                                ushort ap = (ushort)(hwBase + 5);
                                ushort dp = (ushort)(hwBase + 6);
                                WritePortByte(ap, 0x00);
                                byte cfg0 = ReadPortByte(dp);
                                if (cfg0 != 0xFF && (cfg0 & 0x01) == 0)
                                {
                                    WritePortByte(dp, (byte)(cfg0 | 0x01));
                                }

                                // Ensure 16-bit TachMode is enabled on the hardware monitor
                                WritePortByte(ap, 0x0C);
                                byte tMode = ReadPortByte(dp);
                                if (tMode != 0xFF && (tMode & 0x07) != 0x07)
                                {
                                    WritePortByte(dp, (byte)(tMode | 0x07));
                                }

                                // Ensure temperature sensor channels 1, 2, 3 are enabled in Reg 0x51
                                WritePortByte(ap, 0x51);
                                byte tEn = ReadPortByte(dp);
                                if (tEn != 0xFF && (tEn & 0x07) != 0x07)
                                {
                                    WritePortByte(dp, (byte)(tEn | 0x07));
                                }
                            }

                            // Exit config mode
                            WritePortByte(cfgPort, 0x02);
                            WritePortByte(valPort, 0x02);
                            return true;
                        }

                        // Exit config mode on failed verification
                        WritePortByte(cfgPort, 0x02);
                        WritePortByte(valPort, 0x02);
                        return false;
                    }

                    // Try Nuvoton enter sequence: 0x87, 0x87
                    WritePortByte(cfgPort, 0x87);
                    WritePortByte(cfgPort, 0x87);
                    WritePortByte(cfgPort, 0x20);
                    idHi = ReadPortByte(valPort);
                    WritePortByte(cfgPort, 0x21);
                    idLo = ReadPortByte(valPort);
                    id = (ushort)((idHi << 8) | idLo);

                    // Scoped Nuvoton NCT Super I/O chip ID validation (0xC3, 0xC5, 0xC8, 0xD4, 0xD8, etc.)
                    if (idHi != 0 && idHi != 0xFF && (idHi == 0xC3 || idHi == 0xC5 || idHi == 0xC8 || idHi == 0xD4 || idHi == 0xD8 || id == 0xB473 || id == 0xB571))
                    {
                        chipId = id;
                        // Select LDN 8 (Hardware Monitor)
                        WritePortByte(cfgPort, 0x07);
                        WritePortByte(valPort, 0x08);
                        WritePortByte(cfgPort, 0x60);
                        byte nBaseHi = ReadPortByte(valPort);
                        WritePortByte(cfgPort, 0x61);
                        byte nBaseLo = ReadPortByte(valPort);
                        hwBase = (ushort)((nBaseHi << 8) | nBaseLo);
                        if (usePawnLpc && hwBase != 0 && hwBase != 0xFFFF)
                        {
                            PawnIoDriver.TryFindSuperIoBars();
                        }
                        WritePortByte(cfgPort, 0xAA); // exit
                        return hwBase != 0 && hwBase != 0xFFFF;
                    }

                    WritePortByte(cfgPort, 0xAA);
                }
                catch (Exception)
                {
                    // Super I/O discovery safely aborted on port access contention
                }
                return false;
            }
        }

        public static bool VerifyIt87HwBase(ushort baseAddr, bool allowConfigWrites = false)

        {

            if (baseAddr == 0 || baseAddr == 0xFFFF) return false;

            try

            {

                ushort addrPort = (ushort)(baseAddr + 5);

                ushort dataPort = (ushort)(baseAddr + 6);

                WritePortByte(addrPort, 0x58); // VENDOR_ID_REGISTER

                byte vendorId = ReadPortByte(dataPort);

                if (vendorId == 0x90 || vendorId == 0x7F)

                {

                    WritePortByte(addrPort, 0x00); // CONFIGURATION_REGISTER

                    byte cfg = ReadPortByte(dataPort);

                    if (cfg != 0xFF)

                    {

                        if (allowConfigWrites && (cfg & 0x01) == 0)

                        {

                            WritePortByte(dataPort, (byte)(cfg | 0x01));

                        }

                        return true;

                    }

                }

            }

            catch (Exception)
            {
                // Non-existent or unresponsive hardware monitor base address candidate
            }

            return false;

        }

        private static string GetWritableDiagnosticPath(string fileName)
        {
            return AppPaths.GetWritablePath(fileName);
        }

        public void DumpSuperIoDiagnostic(string logFile)

        {

            try

            {

                StringBuilder sb = new StringBuilder();

                sb.AppendLine("=== SUPER I/O & FAN CONTROL DIAGNOSTIC ===");

                sb.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                sb.AppendLine("AccessMethod: " + AccessMethodName);

                sb.AppendLine(string.Format("SuperIoChipId: 0x{0:X4}, SuperIoBaseAddr: 0x{1:X4}", SuperIoChipId, SuperIoBaseAddr));

                using (var lockScope = EcBusMutex.AcquireEc(40))
                {
                    if (!lockScope.Acquired) return;

                    // Only probe fallback candidate bases if an active Super I/O base hasn't already been verified
                    if (SuperIoBaseAddr == 0)
                    {
                        ushort[] cands = new ushort[] { 0x0A00, 0x0290, 0x0A10, 0x0A20, 0x0A30, 0x02A0 };
                        foreach (ushort c in cands)
                        {
                            ushort ap = (ushort)(c + 5);
                            ushort dp = (ushort)(c + 6);
                            WritePortByte(ap, 0x58);
                            byte v58 = ReadPortByte(dp);
                            WritePortByte(ap, 0x00);
                            byte v00 = ReadPortByte(dp);
                            sb.AppendFormat("Candidate Base 0x{0:X4}: Reg 0x58=0x{1:X2}, Reg 0x00=0x{2:X2} (Verified: {3})\r\n", c, v58, v00, (v58 == 0x90 || v58 == 0x7F));
                        }
                    }

                    if (SuperIoBaseAddr != 0)
                    {
                        sb.AppendLine("--- Active HW Monitor Registers ---");
                        byte reg0C = ReadSuperIoHwReg(0x0C);
                        byte reg0B = ReadSuperIoHwReg(0x0B);
                        byte reg13 = ReadSuperIoHwReg(0x13);
                        byte reg14 = ReadSuperIoHwReg(0x14);
                        byte reg15 = ReadSuperIoHwReg(0x15);
                        byte reg16 = ReadSuperIoHwReg(0x16);
                        byte reg17 = ReadSuperIoHwReg(0x17);
                        byte reg63 = ReadSuperIoHwReg(0x63);
                        byte reg6B = ReadSuperIoHwReg(0x6B);
                        byte reg73 = ReadSuperIoHwReg(0x73);
                        byte t29 = ReadSuperIoHwReg(0x29);
                        byte t2A = ReadSuperIoHwReg(0x2A);
                        byte t2B = ReadSuperIoHwReg(0x2B);
                        byte t2C = ReadSuperIoHwReg(0x2C);

                        sb.AppendFormat("Reg 0x0C (TachMode)=0x{0:X2}, 0x0B (Divisor)=0x{1:X2}, 0x13 (MainCtrl)=0x{2:X2}, 0x14 (FanCtl)=0x{3:X2} (Polarity={4})\r\n",
                            reg0C, reg0B, reg13, reg14, ((reg14 & 0x80) == 0 && reg14 != 0xFF) ? "Active-Low/Inverted" : "Active-High/Direct");
                        sb.AppendFormat("Fan 1: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0D=0x{2:X2}, 0x18=0x{3:X2} -> RPM={4}\r\n", reg15, reg63, ReadSuperIoHwReg(0x0D), ReadSuperIoHwReg(0x18), ReadSuperIoFanRpm(1));
                        sb.AppendFormat("Fan 2: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0E=0x{2:X2}, 0x19=0x{3:X2} -> RPM={4}\r\n", reg16, reg6B, ReadSuperIoHwReg(0x0E), ReadSuperIoHwReg(0x19), ReadSuperIoFanRpm(2));
                        sb.AppendFormat("Fan 3: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0F=0x{2:X2}, 0x1A=0x{3:X2} -> RPM={4}\r\n", reg17, reg73, ReadSuperIoHwReg(0x0F), ReadSuperIoHwReg(0x1A), ReadSuperIoFanRpm(3));
                        sb.AppendFormat("Fan 4: Tach 0x80=0x{0:X2}, 0x81=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x80), ReadSuperIoHwReg(0x81), ReadSuperIoFanRpm(4));
                        sb.AppendFormat("Fan 5: Tach 0x82=0x{0:X2}, 0x83=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x82), ReadSuperIoHwReg(0x83), ReadSuperIoFanRpm(5));
                        sb.AppendFormat("Temps: 0x29={0}C (Temp1), 0x2A={1}C (Temp2), 0x2B={2}C (Temp3/CPU), 0x2C={3}C (Temp4/Amb)\r\n", t29, t2A, t2B, t2C);
                    }
                }

                File.WriteAllText(logFile, sb.ToString());

            }

            catch { }

        }

        public byte ReadSuperIoHwReg(byte reg)

        {

            if (_isSimulatedMode) return _simulatedSioRegs[reg];

            if (SuperIoBaseAddr == 0) return 0xFF;

            try

            {

                ushort addrPort = (ushort)(SuperIoBaseAddr + 5);

                ushort dataPort = (ushort)(SuperIoBaseAddr + 6);

                WritePortByte(addrPort, reg);

                return ReadPortByte(dataPort);

            }

            catch (Exception)
            {
                // InpOut driver access blocked or port unmapped; return safe 0xFF
                return 0xFF;
            }

        }

        private bool _simulatedReg14Configured = false;
        private bool? _sioPolarityOverride = null;

        public void SetSuperIoPolarityOverride(bool? inverted)
        {
            _sioPolarityOverride = inverted;
        }

        /// <summary>
        /// Reads ITE IT8613E / IT87xx Register 0x14 (IT87_REG_FAN_CTL) Bit 7 (PWM_POL):
        /// - Bit 7 = 1 (Active High, e.g. 0xC7): Direct polarity (0x00 = 0% stop, 0xFF = 100% full speed)
        /// - Bit 7 = 0 (Active Low / chip reset default after sleep/resume): Inverted polarity (0x00 = 100% full, 0xFF = 0% stop)
        /// Dynamically tracking Reg 0x14 prevents fans running backwards if BIOS or Modern Standby resets Bit 7.
        /// </summary>
        public bool IsSuperIoPwmInverted()
        {
            if (SuperIoBaseAddr == 0) return false;
            if (_sioPolarityOverride.HasValue) return _sioPolarityOverride.Value;
            byte reg14 = ReadSuperIoHwReg(0x14);
            if (reg14 == 0xFF) return false;
            if (_isSimulatedMode && !_simulatedReg14Configured) return false;
            return (reg14 & 0x80) == 0;
        }

        public static byte EncodeSuperIoDuty(int pct, bool inverted)
        {
            byte direct = (pct <= 0) ? (byte)0 : (byte)Math.Max(1, Math.Min(255, (pct * 255) / 100));
            return inverted ? (byte)(255 - direct) : direct;
        }

        public static int DecodeSuperIoDutyPct(byte extReg, bool inverted)
        {
            int effective = inverted ? (255 - extReg) : extReg;
            return Math.Max(0, Math.Min(100, (int)Math.Round(effective * 100.0 / 255.0)));
        }

        private DateTime _lastSioAutoRecoverUtc = DateTime.MinValue;

        public bool WriteSuperIoHwReg(byte reg, byte val)
        {
            if (SuperIoBaseAddr == 0) return false;
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            if (_isSimulatedMode)
            {
                if (SimulateSuperIoWriteFailure) return false;
                _simulatedSioRegs[reg] = val;
                if (reg == 0x14) _simulatedReg14Configured = true;
                return true;
            }
            try
            {
                ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
                ushort dataPort = (ushort)(SuperIoBaseAddr + 6);
                WritePortByte(addrPort, reg);
                WritePortByte(dataPort, val);
                byte readBack = ReadSuperIoHwReg(reg);
                if (readBack != val)
                {
                    System.Threading.Thread.Sleep(1);
                    readBack = ReadSuperIoHwReg(reg);
                }
                if (readBack == val) return true;

                // Self-healing Modern Standby (S0ix) recovery:
                // When Windows 11 enters/exits S0ix sleep without a legacy S3 Resume broadcast,
                // ITE IT8613E resets LDN4 (0x0A30) so reads return 0xFF. Re-probe Super I/O +
                // PawnIO LpcIO BARs (throttled to once per 5s) and retry the write once.
                if (readBack == 0xFF && val != 0xFF && (DateTime.UtcNow - _lastSioAutoRecoverUtc).TotalSeconds >= 5.0)
                {
                    _lastSioAutoRecoverUtc = DateTime.UtcNow;
                    WatchdogSupervisor.LogWatchdogEvent(string.Format(
                        "Super I/O write read-back returned 0xFF on reg 0x{0:X2} (possible S0ix reset) — reinitializing Super I/O & PawnIO BARs.", reg));
                    ReinitializeHardwareAfterResume();
                    if (SuperIoBaseAddr != 0)
                    {
                        addrPort = (ushort)(SuperIoBaseAddr + 5);
                        dataPort = (ushort)(SuperIoBaseAddr + 6);
                        WritePortByte(addrPort, reg);
                        WritePortByte(dataPort, val);
                        readBack = ReadSuperIoHwReg(reg);
                        if (readBack != val)
                        {
                            System.Threading.Thread.Sleep(1);
                            readBack = ReadSuperIoHwReg(reg);
                        }
                        return (readBack == val);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private int _latchedSioFan1Channel = 0; // 0=auto, 1=Ch1 (0x0D/0x18), 2=Ch2 (0x0E/0x19)
        private int _latchedSioFan2Channel = 0; // 0=auto, 3=Ch3 (0x0F/0x1A), 2=Ch2 (0x0E/0x19)

        public void ResetSuperIoChannelLatch()
        {
            _latchedSioFan1Channel = 0;
            _latchedSioFan2Channel = 0;
        }

        public int ReadSuperIoFanRpm(int fanIndex)

        {

            if (SuperIoBaseAddr == 0 || fanIndex < 1 || fanIndex > 6) return 0;

            try

            {

                // Ensure 16-bit tachometer mode (0x0C) and tachometer input enables (0x13):

                byte modeReg = ReadSuperIoHwReg(0x0C);

                if ((modeReg & 0x3F) != 0x3F)

                {

                    WriteSuperIoHwReg(0x0C, (byte)(modeReg | 0x3F));

                }

                byte mainCtrl = ReadSuperIoHwReg(0x13);

                if ((mainCtrl & 0x77) != 0x77)

                {

                    WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x77));

                }

                // Atomic double-read latching to prevent high/low byte counter rollover tearing
                Func<byte, byte, int> evalTach16 = (lsbReg, msbReg) =>
                {
                    byte hi1 = ReadSuperIoHwReg(msbReg);
                    byte lo1 = ReadSuperIoHwReg(lsbReg);
                    byte hi2 = ReadSuperIoHwReg(msbReg);
                    byte lo = (hi1 == hi2) ? lo1 : ReadSuperIoHwReg(lsbReg);
                    byte hi = hi2;
                    int raw = (hi << 8) | lo;
                    if (raw <= 0 || raw >= 0xFFFF) return 0;
                    // Valid raw 16-bit counter window for blowers (80 .. 6750 -> ~100 .. 8000 RPM)
                    if (raw < 80 || raw > 6750) return 0;
                    int rpm = (int)(1.35e6f / (raw * 2));
                    if (rpm < 100 || rpm > 8000) return 0;
                    return rpm;
                };

                int rawRpm = 0;
                if (fanIndex == 1)
                {
                    // Primary APU blower: Channel 1 (0x0D / 0x18) = HWiNFO64 "CPU".
                    // Latch observed wiring so a stopped fan reports genuine 0 RPM instead of mirroring Channel 2.
                    int ch1 = evalTach16(0x0D, 0x18);
                    if (ch1 > 0)
                    {
                        _latchedSioFan1Channel = 1;
                        rawRpm = ch1;
                    }
                    else if (_latchedSioFan1Channel == 1)
                    {
                        rawRpm = 0;
                    }
                    else if (_latchedSioFan2Channel != 2)
                    {
                        int ch2 = evalTach16(0x0E, 0x19);
                        if (ch2 > 0)
                        {
                            _latchedSioFan1Channel = 2;
                            rawRpm = ch2;
                        }
                    }

                    if (rawRpm > 0)
                    {
                        lock (_filterLock)
                        {
                            _fan1FilterWindow.Add(rawRpm);
                            if (_fan1FilterWindow.Count > 3) _fan1FilterWindow.RemoveAt(0);
                            var sorted = new List<int>(_fan1FilterWindow); sorted.Sort();
                            return sorted[sorted.Count / 2]; // 3-sample median filter
                        }
                    }
                    lock (_filterLock)
                    {
                        _fan1FilterWindow.Clear();
                    }
                    return 0;
                }
                else if (fanIndex == 2)
                {
                    // Secondary Sys blower: Channel 3 (0x0F / 0x1A) = HWiNFO64 "Power".
                    // Never fall back to Channel 2 if Channel 3 has been observed active or Channel 2 is owned by Fan 1
                    // (e.g. TS-12C / KTHIN K8 where APU=Ch2 and Sys=Ch3).
                    int ch3 = evalTach16(0x0F, 0x1A);
                    if (ch3 > 0)
                    {
                        _latchedSioFan2Channel = 3;
                        rawRpm = ch3;
                    }
                    else if (_latchedSioFan2Channel == 3)
                    {
                        rawRpm = 0;
                    }
                    else if (_latchedSioFan1Channel != 2)
                    {
                        int ch2 = evalTach16(0x0E, 0x19);
                        if (ch2 > 0)
                        {
                            _latchedSioFan2Channel = 2;
                            rawRpm = ch2;
                        }
                    }

                    if (rawRpm > 0)
                    {
                        lock (_filterLock)
                        {
                            _fan2FilterWindow.Add(rawRpm);
                            if (_fan2FilterWindow.Count > 3) _fan2FilterWindow.RemoveAt(0);
                            var sorted = new List<int>(_fan2FilterWindow); sorted.Sort();
                            return sorted[sorted.Count / 2]; // 3-sample median filter
                        }
                    }
                    lock (_filterLock)
                    {
                        _fan2FilterWindow.Clear();
                    }
                    return 0;
                }

                else if (fanIndex == 3)

                {

                    return evalTach16(0x0F, 0x1A);

                }

                else if (fanIndex == 4)

                {

                    return evalTach16(0x80, 0x81);

                }

                else if (fanIndex == 5)

                {

                    return evalTach16(0x82, 0x83);

                }

                else if (fanIndex == 6)

                {

                    return evalTach16(0x4C, 0x4D);

                }

                return 0;

            }

            catch

            {

                return 0;

            }

        }


        private readonly BiosFanStateSnapshot _biosSnapshot = new BiosFanStateSnapshot();

        public bool HasCapturedBiosSmartGuardianCurve { get { return _biosSnapshot.HasCapturedSuperIo && _biosSnapshot.HasSmartGuardianCurve; } }
        public int CapturedBiosEcMode { get { return _biosSnapshot.HasCapturedEcMode ? _biosSnapshot.InitialEcMode : -1; } }

        private void CaptureInitialSuperIoBiosStateIfNeeded()
        {
            if (_biosSnapshot.HasCapturedSuperIo || SuperIoBaseAddr == 0) return;
            _biosSnapshot.TryCaptureSuperIo(
                ReadSuperIoHwReg(0x15),
                ReadSuperIoHwReg(0x16),
                ReadSuperIoHwReg(0x17),
                ReadSuperIoHwReg(0x63),
                ReadSuperIoHwReg(0x6B),
                ReadSuperIoHwReg(0x73));
        }

        private void CaptureInitialEcBiosStateIfNeeded()
        {
            if (_biosSnapshot.HasCapturedEcMode) return;
            if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E) return;
            DeviceProfile p = Profile ?? DeviceProfile.FindProfile("unknown-safe");
            if (p == null || p.IsReadOnly) return;
            _biosSnapshot.TryCaptureEcMode(ReadRegInternal(p.RegModeStatus));
        }

        public bool RestoreNativeEcAuto()
        {
            if (Profile != null && Profile.IsReadOnly) return false;
            DeviceProfile p = Profile ?? DeviceProfile.FindProfile("unknown-safe");
            if (p.IsReadOnly) return false;
            if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
            {
                return true;
            }

            WriteRegInternal(p.RegModeCmd, _biosSnapshot.ResolveEcRestoreModeCommand());
            WriteRegInternal(p.RegFan1Ctrl, 0x00);
            WriteRegInternal(p.RegFan2Ctrl, 0x00);

            byte f1 = ReadRegInternal(p.RegFan1Ctrl);
            byte f2 = ReadRegInternal(p.RegFan2Ctrl);
            return (f1 == 0x00 && f2 == 0x00);
        }

        /// <summary>
        /// Computes the safe Super I/O manual fallback PWM percentage when handing off control
        /// (exit, crash watchdog, suspend, or anti-cheat pause).
        /// Preserves quiet 35% baseline at cool/idle temperatures while escalating to 60–75%
        /// under thermal load or anti-cheat gaming handoff so blowers never stall below cooling demand.
        /// </summary>
        public static int ComputeSafeSuperIoFallbackPct(int cpuTempC, bool thermalLoadContext)
        {
            if (cpuTempC >= 80 && cpuTempC <= 115) return 75;
            if (thermalLoadContext || (cpuTempC >= 65 && cpuTempC <= 115)) return 60;
            return 35;
        }

        public bool ApplySafeSuperIoFallback(bool thermalLoadContext = false, int? hintCpuTempC = null)
        {
            if (Profile != null && Profile.IsReadOnly) return false;
            if (SuperIoBaseAddr == 0) return true;

            bool allOk = true;
            byte mainCtrl = ReadSuperIoHwReg(0x13);
            if ((mainCtrl & 0x77) != 0x77)
            {
                if (!WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x77))) allOk = false;
            }
            byte modeReg = ReadSuperIoHwReg(0x0C);
            if ((modeReg & 0x3F) != 0x3F)
            {
                if (!WriteSuperIoHwReg(0x0C, (byte)(modeReg | 0x3F))) allOk = false;
            }
            ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
            ushort dataPort = (ushort)(SuperIoBaseAddr + 6);

            // If the motherboard BIOS originally booted with SmartGuardian / Custom BIOS Fan Curve
            // active (Bit 7 = 1 on Reg 0x15..0x17), faithfully restore the exact captured BIOS registers
            // so the user's BIOS fan curve resumes immediately.
            // Otherwise (factory BIOS booted with Reg 0x15..0x17 == 0x00 Extended PWM mode, e.g. TS-12C / EVO-X1),
            // keep Bit 7 = 0 (setting Bit 7 = 1 on unconfigured thermal diodes blasts 100%) and write a safe
            // temperature-aware fallback duty into 0x63 / 0x6B / 0x73.
            if (_biosSnapshot.HasCapturedSuperIo && _biosSnapshot.HasSmartGuardianCurve)
            {
                byte r15 = (_biosSnapshot.SioReg15 != 0xFF) ? _biosSnapshot.SioReg15 : (byte)0x00;
                byte r16 = (_biosSnapshot.SioReg16 != 0xFF) ? _biosSnapshot.SioReg16 : (byte)0x00;
                byte r17 = (_biosSnapshot.SioReg17 != 0xFF) ? _biosSnapshot.SioReg17 : (byte)0x00;
                WritePortByte(addrPort, 0x15);
                WritePortByte(dataPort, r15);
                WritePortByte(addrPort, 0x16);
                WritePortByte(dataPort, r16);
                WritePortByte(addrPort, 0x17);
                WritePortByte(dataPort, r17);
                if (_isSimulatedMode)
                {
                    _simulatedSioRegs[0x15] = r15;
                    _simulatedSioRegs[0x16] = r16;
                    _simulatedSioRegs[0x17] = r17;
                }

                // Restore original extended duty for any channel that was not in SmartGuardian mode
                if ((r15 & 0x80) == 0 && _biosSnapshot.SioReg63 != 0xFF)
                {
                    if (!WriteSuperIoHwReg(0x63, _biosSnapshot.SioReg63)) allOk = false;
                }
                if ((r16 & 0x80) == 0 && _biosSnapshot.SioReg6B != 0xFF)
                {
                    if (!WriteSuperIoHwReg(0x6B, _biosSnapshot.SioReg6B)) allOk = false;
                }
                if ((Profile == null || Profile.HasDualFans) && (r17 & 0x80) == 0 && _biosSnapshot.SioReg73 != 0xFF)
                {
                    if (!WriteSuperIoHwReg(0x73, _biosSnapshot.SioReg73)) allOk = false;
                }
                return allOk;
            }

            byte autoModeVal = 0x00;
            WritePortByte(addrPort, 0x15);
            WritePortByte(dataPort, autoModeVal);
            WritePortByte(addrPort, 0x16);
            WritePortByte(dataPort, autoModeVal);
            WritePortByte(addrPort, 0x17);
            WritePortByte(dataPort, autoModeVal);
            if (_isSimulatedMode)
            {
                _simulatedSioRegs[0x15] = autoModeVal;
                _simulatedSioRegs[0x16] = autoModeVal;
                _simulatedSioRegs[0x17] = autoModeVal;
            }

            // Determine current CPU temperature so hot workloads / anti-cheat pauses never drop to 35%
            int effectiveCpuTemp = 0;
            if (hintCpuTempC.HasValue)
            {
                effectiveCpuTemp = hintCpuTempC.Value;
            }
            else
            {
                int t3 = (sbyte)ReadSuperIoHwReg(0x2B);
                int t1 = (sbyte)ReadSuperIoHwReg(0x29);
                if (t3 >= 15 && t3 <= 115) effectiveCpuTemp = t3;
                else if (t1 >= 15 && t1 <= 115) effectiveCpuTemp = t1;
                else if (_lastStatusCache.CpuTemp >= 15 && _lastStatusCache.CpuTemp <= 115) effectiveCpuTemp = _lastStatusCache.CpuTemp;
            }

            // Safe baseline PWM duty: 35% quiet floor when cool/idle, 60% under active load / anti-cheat pause, 75% at >= 80°C
            int safePct = ComputeSafeSuperIoFallbackPct(effectiveCpuTemp, thermalLoadContext);
            byte safeDuty = EncodeSuperIoDuty(safePct, IsSuperIoPwmInverted());
            if (!WriteSuperIoHwReg(0x63, safeDuty)) allOk = false;
            if (!WriteSuperIoHwReg(0x6B, safeDuty)) allOk = false;
            if (Profile == null || Profile.HasDualFans)
            {
                if (!WriteSuperIoHwReg(0x73, safeDuty)) allOk = false;
            }

            return allOk;
        }

    }
}
