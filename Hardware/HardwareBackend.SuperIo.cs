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
                try
                {
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

                        bool verified = (candidateBase != 0 && candidateBase != 0xFFFF && VerifyIt87HwBase(candidateBase, allowConfigWrites));

                        // Only reprogram base address or activate LDN4 if explicitly allowed for an active verified profile
                        if (!verified && allowConfigWrites)
                        {
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
                                WritePortByte(cfgPort, 0x60);
                                WritePortByte(valPort, (byte)(cand >> 8));
                                WritePortByte(cfgPort, 0x61);
                                WritePortByte(valPort, (byte)(cand & 0xFF));
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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            bool inProgramFiles = (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase)) ||
                                  (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase));
            if (inProgramFiles)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    string dir = Path.Combine(localAppData, "MiniFanControl");
                    try { Directory.CreateDirectory(dir); } catch { }
                    return Path.Combine(dir, fileName);
                }
            }
            return Path.Combine(baseDir, fileName);
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

                if (SuperIoBaseAddr != 0)

                {

                    sb.AppendLine("--- Active HW Monitor Registers ---");

                    byte reg0C = ReadSuperIoHwReg(0x0C);

                    byte reg0B = ReadSuperIoHwReg(0x0B);

                    byte reg13 = ReadSuperIoHwReg(0x13);

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

                    sb.AppendFormat("Reg 0x0C (TachMode)=0x{0:X2}, 0x0B (Divisor)=0x{1:X2}, 0x13 (MainCtrl)=0x{2:X2}\r\n", reg0C, reg0B, reg13);
                    sb.AppendFormat("Fan 1: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0D=0x{2:X2}, 0x18=0x{3:X2} -> RPM={4}\r\n", reg15, reg63, ReadSuperIoHwReg(0x0D), ReadSuperIoHwReg(0x18), ReadSuperIoFanRpm(1));
                    sb.AppendFormat("Fan 2: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0E=0x{2:X2}, 0x19=0x{3:X2} -> RPM={4}\r\n", reg16, reg6B, ReadSuperIoHwReg(0x0E), ReadSuperIoHwReg(0x19), ReadSuperIoFanRpm(2));
                    sb.AppendFormat("Fan 3: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0F=0x{2:X2}, 0x1A=0x{3:X2} -> RPM={4}\r\n", reg17, reg73, ReadSuperIoHwReg(0x0F), ReadSuperIoHwReg(0x1A), ReadSuperIoFanRpm(3));
                    sb.AppendFormat("Fan 4: Tach 0x80=0x{0:X2}, 0x81=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x80), ReadSuperIoHwReg(0x81), ReadSuperIoFanRpm(4));
                    sb.AppendFormat("Fan 5: Tach 0x82=0x{0:X2}, 0x83=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x82), ReadSuperIoHwReg(0x83), ReadSuperIoFanRpm(5));
                    sb.AppendFormat("Temps: 0x29={0}C (Temp1), 0x2A={1}C (Temp2), 0x2B={2}C (Temp3/CPU), 0x2C={3}C (Temp4/Amb)\r\n", t29, t2A, t2B, t2C);

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

        public bool WriteSuperIoHwReg(byte reg, byte val)
        {
            if (SuperIoBaseAddr == 0) return false;
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            if (_isSimulatedMode)
            {
                if (SimulateSuperIoWriteFailure) return false;
                _simulatedSioRegs[reg] = val;
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
                return (readBack == val);
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


        public bool RestoreNativeEcAuto()
        {
            if (Profile != null && Profile.IsReadOnly) return false;
            DeviceProfile p = Profile ?? DeviceProfile.FindProfile("unknown-safe");
            if (p.IsReadOnly) return false;
            if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
            {
                return true;
            }

            WriteRegInternal(p.RegModeCmd, 0x80);
            WriteRegInternal(p.RegFan1Ctrl, 0x00);
            WriteRegInternal(p.RegFan2Ctrl, 0x00);

            byte f1 = ReadRegInternal(p.RegFan1Ctrl);
            byte f2 = ReadRegInternal(p.RegFan2Ctrl);
            return (f1 == 0x00 && f2 == 0x00);
        }

        public bool ApplySafeSuperIoFallback()
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

            // Re-engage Safe Super I/O Fallback (Extended PWM mode matching factory BIOS: Reg 0x15..0x17 = 0x00)
            // Bit 7: 0 = Extended PWM mode (Reg 0x63, 0x6B, 0x73 govern duty cycle).
            // We deliberately keep Bit 7 = 0 because setting Bit 7 = 1 (IT87 SmartGuardian Auto) causes
            // the hardware to ramp fans to 100% full blast if thermal diode clocks gate during sleep or if
            // temperature inputs are unconfigured in motherboard firmware!
            // In factory BIOS state on TS-12C / GMKtec EVO-X1, Reg 0x15..0x17 are 0x00.
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

            // Safe baseline PWM floor duty (~35% / ~1100 RPM quiet cooling safety net: 89/255 = 35%)
            // matching factory quiet operation
            byte safeDuty = 89;
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
