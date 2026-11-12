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

    public class HardwareBackend : IDisposable

    {

        public const ulong EC_BASE = 0xFE0B0400UL;

        public const uint EC_SIZE = 0xFF;


        public enum AutoRestoreMode
        {
            None = 0,
            NativeBiosAuto,
            SafeSuperIoThermalFallbackAndEcAuto
        }

        public AutoRestoreMode LastAutoRestoreMode { get; set; }

        public string GetAutoRestoreDescription()
        {
            switch (LastAutoRestoreMode)
            {
                case AutoRestoreMode.NativeBiosAuto:
                    return "Native BIOS Auto";
                case AutoRestoreMode.SafeSuperIoThermalFallbackAndEcAuto:
                    return "Safe Super I/O Thermal Fallback & Native EC Auto";
                default:
                    return "None / Unverified";
            }
        }

        private bool _isSimulatedMode = false;
        private readonly byte[] _simulatedEcRegs = new byte[EC_SIZE];
        private readonly byte[] _simulatedSioRegs = new byte[256];
        public bool SimulateSuperIoWriteFailure { get; set; }

        public void EnableSimulationMode()
        {
            _isSimulatedMode = true;
            _isInitialized = true;
        }
        public const byte REG_MODE_STATUS = 0x31;

        public const byte REG_MODE_CMD    = 0x32;

        public const byte REG_FAN1_CTRL   = 0x33;

        public const byte REG_FAN2_CTRL   = 0x34;

        public const byte REG_FAN1_HI     = 0x35;

        public const byte REG_FAN1_LO     = 0x36;

        public const byte REG_FAN2_HI     = 0x37;

        public const byte REG_FAN2_LO     = 0x38;

        public const byte REG_CPU_TEMP    = 0x70;

        public const byte REG_GPU_TEMP    = 0x71;

        public enum EcAccessMethod

        {

            Unknown = 0, // Hardware not detected or unverified

            PortIo62,    // Standard ACPI EC ports 0x62 (Data) / 0x66 (Command/Status)

            PortIo6C,    // Secondary ACPI EC ports 0x6C (Data) / 0x6E (Command/Status)

            SuperIo2E,   // Super I/O (IT87xx / IT86xx) via LPC Port 0x2E

            SuperIo4E,   // Super I/O (IT87xx / IT86xx) via LPC Port 0x4E

            Mmio         // Memory-Mapped I/O (e.g. 0xFE0B0400)

        }

        public DeviceProfile Profile = DeviceProfile.FindProfile("unknown-safe");

        public static HardwareBackend CreateAutoDetected()
        {
            HardwareBackend hw = new HardwareBackend();
            try
            {
                SystemHardwareInfo info = SystemHardwareInfo.Detect();
                hw.Profile = info.MatchedProfile ?? DeviceProfile.FindProfile("unknown-safe");
            }
            catch
            {
                hw.Profile = DeviceProfile.FindProfile("unknown-safe");
            }
            return hw;
        }

        public EcAccessMethod AccessMethod = EcAccessMethod.Unknown;

        public ushort SuperIoChipId = 0;

        public ushort SuperIoBaseAddr = 0;

        private static readonly List<int> _fan1FilterWindow = new List<int>();

        private static readonly List<int> _fan2FilterWindow = new List<int>();

        private static readonly object _filterLock = new object();

        public const int EMERGENCY_TEMP_TRIP_C = 90;

        public const int EMERGENCY_TEMP_RECOVER_C = 75;

        public static bool CheckEmergencyThermalTripwire(int temp, ref bool activeState)

        {

            if (temp >= EMERGENCY_TEMP_TRIP_C)

            {

                activeState = true;

            }

            else if (activeState && temp <= EMERGENCY_TEMP_RECOVER_C)

            {

                activeState = false;

            }

            return activeState;

        }

        public string AccessMethodName

        {

            get

            {

                switch (AccessMethod)

                {

                    case EcAccessMethod.PortIo62: return "ACPI EC Port I/O (0x62 / 0x66)";

                    case EcAccessMethod.PortIo6C: return "ACPI EC Port I/O (0x6C / 0x6E)";

                    case EcAccessMethod.SuperIo2E: return string.Format("Super I/O IT{0:X4} @ LPC 0x2E (HW Monitor Base: 0x{1:X4})", SuperIoChipId, SuperIoBaseAddr);

                    case EcAccessMethod.SuperIo4E: return string.Format("Super I/O IT{0:X4} @ LPC 0x4E (HW Monitor Base: 0x{1:X4})", SuperIoChipId, SuperIoBaseAddr);

                    case EcAccessMethod.Mmio: return string.Format("Physical MMIO (0x{0:X8})", Profile != null ? Profile.EcBaseAddress : EC_BASE);

                    default: return "Unverified Hardware / Safe Read-Only";

                }

            }

        }

        private const byte EC_CMD_READ  = 0x80;

        private const byte EC_CMD_WRITE = 0x81;

        private const byte EC_FLAG_OBF  = 0x01;

        private const byte EC_FLAG_IBF  = 0x02;

        private IntPtr _linAddr = IntPtr.Zero;

        private IntPtr _physHandle = IntPtr.Zero;

        private bool _isInitialized = false;

        private readonly AmdAdlGpuSensor _adlSensor = new AmdAdlGpuSensor();

        public HardwareBackend()

        {

        }

        public HardwareBackend(DeviceProfile profile)

        {

            if (profile != null) Profile = profile;

        }

        public static bool IsPortWhitelisted(ushort port, bool isWrite)
        {
            // Explicit blocklist: Never permit access to critical motherboard/CPU control ports
            if (port == 0x70 || port == 0x71 || port == 0x92 || port == 0xCF9 ||
                port == 0x20 || port == 0x21 || port == 0xA0 || port == 0xA1 ||
                port == 0x40 || port == 0x41 || port == 0x42 || port == 0x43 ||
                port == 0x61)
            {
                return false;
            }

            // Standard ACPI Embedded Controller ports
            if (port == 0x62 || port == 0x66 || port == 0x68 || port == 0x6C || port == 0x6A || port == 0x6E)
                return true;

            // LPC / Super I/O indexing ports
            if (port == 0x2E || port == 0x2F || port == 0x4E || port == 0x4F)
                return true;

            // PCI Configuration Mechanism #1 address and data ports (used strictly for AMD SMN read)
            if (port == 0xCF8 || port == 0xCFC)
                return true;

            // Hardware monitor base ranges (IT8613E / IT87xx / NCT6779)
            if ((port >= 0x0290 && port <= 0x0297) ||
                (port >= 0x02A0 && port <= 0x02A7) ||
                (port >= 0x0A00 && port <= 0x0A3F) ||
                (port >= 0x0E00 && port <= 0x0E3F))
            {
                return true;
            }

            // Strict fail-closed: Any unlisted port is rejected for both reads and writes
            return false;
        }

        public static byte ReadPortByte(ushort port)

        {

            if (!IsPortWhitelisted(port, false)) return 0xFF;

            try

            {

                byte v = NativeInpOut.DlPortReadPortUchar(port);

                if (v != 0xFF) return v;

                int alt = NativeInpOut.Inp32(port);

                return (byte)(alt & 0xFF);

            }

            catch (Exception)
            {
                // InpOut driver access blocked or port unmapped; return safe 0xFF
                return 0xFF;
            }

        }

        public static void WritePortByte(ushort port, byte val)

        {

            if (!IsPortWhitelisted(port, true)) return;

            try

            {

                NativeInpOut.DlPortWritePortUchar(port, val);

            }

            catch (Exception)
            {
                try 
                { 
                    NativeInpOut.Out32(port, (short)val); 
                } 
                catch (Exception) 
                { 
                    // Secondary InpOut fallback write failed safely
                }
            }

        }

        private static bool EcWaitIbfClear(ushort cmdPort, int timeoutMs = 30)

        {

            byte status = ReadPortByte(cmdPort);

            if (status == 0xFF) return false;

            if ((status & EC_FLAG_IBF) == 0) return true;

            Stopwatch sw = Stopwatch.StartNew();

            while ((status = ReadPortByte(cmdPort)) != 0xFF && (status & EC_FLAG_IBF) != 0)

            {

                if (sw.ElapsedMilliseconds > timeoutMs) return false;

                Thread.SpinWait(40);

            }

            return status != 0xFF && (status & EC_FLAG_IBF) == 0;

        }

        private static bool EcWaitObfSet(ushort cmdPort, int timeoutMs = 30)

        {

            byte status = ReadPortByte(cmdPort);

            if (status == 0xFF) return false;

            if ((status & EC_FLAG_OBF) != 0) return true;

            Stopwatch sw = Stopwatch.StartNew();

            while ((status = ReadPortByte(cmdPort)) != 0xFF && (status & EC_FLAG_OBF) == 0)

            {

                if (sw.ElapsedMilliseconds > timeoutMs) return false;

                Thread.SpinWait(40);

            }

            return status != 0xFF && (status & EC_FLAG_OBF) != 0;

        }

        private static void EcDrainObf(ushort dataPort, ushort cmdPort)

        {

            try

            {

                byte status = ReadPortByte(cmdPort);

                if (status != 0xFF && (status & EC_FLAG_OBF) != 0)

                {

                    ReadPortByte(dataPort);

                }

            }

            catch { }

        }

        public static bool TryPortIoRead(ushort dataPort, ushort cmdPort, byte reg, out byte val)

        {

            val = 0xFF;

            EcDrainObf(dataPort, cmdPort);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(cmdPort, EC_CMD_READ);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(dataPort, reg);

            if (!EcWaitIbfClear(cmdPort) || !EcWaitObfSet(cmdPort)) return false;

            val = ReadPortByte(dataPort);

            return true;

        }

        public static bool TryPortIoWrite(ushort dataPort, ushort cmdPort, byte reg, byte val)

        {

            EcDrainObf(dataPort, cmdPort);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(cmdPort, EC_CMD_WRITE);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(dataPort, reg);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(dataPort, val);

            return EcWaitIbfClear(cmdPort);

        }

        public static bool ProbeSuperIo(ushort cfgPort, out ushort chipId, out ushort hwBase)

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

                    // Ensure Logical Device 4 is Activated in the Super I/O chip

                    WritePortByte(cfgPort, 0x30);

                    byte actVal = ReadPortByte(valPort);

                    if ((actVal & 0x01) == 0)

                    {

                        WritePortByte(valPort, 0x01);

                    }

                    // Read Base Address (Register 0x60, 0x61)

                    WritePortByte(cfgPort, 0x60);

                    byte baseHi = ReadPortByte(valPort);

                    WritePortByte(cfgPort, 0x61);

                    byte baseLo = ReadPortByte(valPort);

                    ushort candidateBase = (ushort)((baseHi << 8) | baseLo);

                    bool verified = VerifyIt87HwBase(candidateBase);

                    if (!verified)

                    {

                        // Some motherboards (e.g. TOPC TS-12C, Ryzen AI 9 HX 370) do not decode default 0x0290,

                        // but forward specific LPC port ranges (0x0A30 / 0x0A00 / 0x0A10 / 0x0A20) via FCH.

                        ushort[] candidates = new ushort[] { 0x0A30, 0x0A00, 0x0A10, 0x0A20, 0x0290, 0x02A0 };

                        foreach (ushort cand in candidates)

                        {

                            WritePortByte(cfgPort, 0x60);

                            WritePortByte(valPort, (byte)(cand >> 8));

                            WritePortByte(cfgPort, 0x61);

                            WritePortByte(valPort, (byte)(cand & 0xFF));

                            if (VerifyIt87HwBase(cand))

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

                if (idHi != 0 && idHi != 0xFF && (idHi >= 0xB0 && idHi <= 0xDF))

                {

                    chipId = id;

                    // Select LDN 8 or 11 (Hardware Monitor)

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

        public static bool VerifyIt87HwBase(ushort baseAddr)

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

                        if ((cfg & 0x01) == 0)

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

                    sb.AppendFormat("Reg 0x0C (TachMode)=0x{0:X2}, 0x0B (Divisor)=0x{1:X2}, 0x13 (MainCtrl)=0x{2:X2}\r\n", reg0C, reg0B, reg13);

                    sb.AppendFormat("Fan 1: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0D=0x{2:X2}, 0x18=0x{3:X2} -> RPM={4}\r\n", reg15, reg63, ReadSuperIoHwReg(0x0D), ReadSuperIoHwReg(0x18), ReadSuperIoFanRpm(1));

                    sb.AppendFormat("Fan 2: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0E=0x{2:X2}, 0x19=0x{3:X2} -> RPM={4}\r\n", reg16, reg6B, ReadSuperIoHwReg(0x0E), ReadSuperIoHwReg(0x19), ReadSuperIoFanRpm(2));

                    sb.AppendFormat("Fan 3: PWM=0x{0:X2}, ExtPWM=0x{1:X2}, Tach 0x0F=0x{2:X2}, 0x1A=0x{3:X2} -> RPM={4}\r\n", reg17, reg73, ReadSuperIoHwReg(0x0F), ReadSuperIoHwReg(0x1A), ReadSuperIoFanRpm(3));

                    sb.AppendFormat("Fan 4: Tach 0x80=0x{0:X2}, 0x81=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x80), ReadSuperIoHwReg(0x81), ReadSuperIoFanRpm(4));

                    sb.AppendFormat("Fan 5: Tach 0x82=0x{0:X2}, 0x83=0x{1:X2} -> RPM={2}\r\n", ReadSuperIoHwReg(0x82), ReadSuperIoHwReg(0x83), ReadSuperIoFanRpm(5));

                    sb.AppendFormat("Temps: 0x29={0}C, 0x2A={1}C\r\n", t29, t2A);

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
                return (readBack == val);
            }
            catch
            {
                return false;
            }
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

                if ((mainCtrl & 0x70) != 0x70)

                {

                    WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x70));

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

                    // Valid raw 16-bit counter window for blowers (90 .. 2250 -> ~300 .. 7500 RPM)

                    if (raw < 90 || raw > 2250) return 0;

                    int rpm = (int)(1.35e6f / (raw * 2));

                    if (rpm < 250 || rpm > 7500) return 0;

                    return rpm;

                };

                int rawRpm = 0;

                if (fanIndex == 1)

                {

                    // Primary APU blower: Channel 2 (0x0E / 0x19) is verified active on TS-12C / KTHIN PRO / IT8613

                    rawRpm = evalTach16(0x0E, 0x19);

                    if (rawRpm == 0) rawRpm = evalTach16(0x0D, 0x18);

                    if (rawRpm == 0) rawRpm = evalTach16(0x0F, 0x1A);

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

                    return 0;

                }

                else if (fanIndex == 2)

                {

                    // Secondary Sys blower (if wired on multi-tach boards):

                    rawRpm = evalTach16(0x0D, 0x18);

                    if (rawRpm == 0) rawRpm = evalTach16(0x0F, 0x1A);

                    if (rawRpm == 0) rawRpm = evalTach16(0x80, 0x81);

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

        private void EnsureOpen()

        {

            if (_isInitialized || _isSimulatedMode)

                return;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            bool winringExists = File.Exists(Path.Combine(appDir, "WinRing0x64.dll")) ||
                                 File.Exists(Path.Combine(appDir, "WinRing0.dll"));
            bool inpoutExists = File.Exists(Path.Combine(appDir, "inpoutx64.dll"));

            if (!winringExists && !inpoutExists)
            {
                throw new InvalidOperationException(
                    "No supported hardware driver DLL (WinRing0x64.dll or inpoutx64.dll) found in application folder.\n" +
                    "Place WinRing0x64.dll or inpoutx64.dll in this folder and run as Administrator (or enable Demo Mode below).");
            }

            try

            {

                if (!NativeInpOut.IsDriverOpen())

                {

                    throw new InvalidOperationException(
                        string.Format("Hardware access driver could not be opened ({0}). Please run as Administrator " +
                        "(and check Windows Vulnerable Driver Blocklist / HVCI in Core Isolation if blocked).",
                        NativeInpOut.ActiveDriverName));

                }

                // 1. Probe LPC Super I/O on Port 0x2E (True BIOS-programmed base address)

                ushort cId, hBase;

                if (ProbeSuperIo(0x2E, out cId, out hBase) && VerifyIt87HwBase(hBase))

                {

                    SuperIoChipId = cId;

                    SuperIoBaseAddr = hBase;

                    AccessMethod = EcAccessMethod.SuperIo2E;

                    _isInitialized = true;

                    DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));

                    return;

                }

                // 2. Probe LPC Super I/O on Port 0x4E

                if (ProbeSuperIo(0x4E, out cId, out hBase) && VerifyIt87HwBase(hBase))

                {

                    SuperIoChipId = cId;

                    SuperIoBaseAddr = hBase;

                    AccessMethod = EcAccessMethod.SuperIo4E;

                    _isInitialized = true;

                    DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));

                    return;

                }

                // 3. Fallback candidate IT87 Hardware Monitor base addresses (0x58=0x90):

                ushort[] candidateBases = new ushort[] { 0x0A30, 0x0A00, 0x0A10, 0x0A20, 0x0290, 0x02A0 };

                foreach (ushort cand in candidateBases)

                {

                    if (VerifyIt87HwBase(cand))

                    {

                        SuperIoChipId = 0x8613;

                        SuperIoBaseAddr = cand;

                        AccessMethod = EcAccessMethod.SuperIo2E;

                        _isInitialized = true;

                        DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));

                        return;

                    }

                }

                // 3. Probe standard ACPI EC Port I/O (0x62 / 0x66)

                byte sc66 = ReadPortByte(0x66);

                if (sc66 != 0xFF)

                {

                    byte probeVal;

                    byte probeReg = Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP;

                    if (TryPortIoRead(0x62, 0x66, probeReg, out probeVal) && probeVal > 0 && probeVal < 115)

                    {

                        AccessMethod = EcAccessMethod.PortIo62;

                        _isInitialized = true;

                        return;

                    }

                    byte probeMode = Profile != null ? Profile.RegModeStatus : REG_MODE_STATUS;

                    if (TryPortIoRead(0x62, 0x66, probeMode, out probeVal) && probeVal <= 2)

                    {

                        AccessMethod = EcAccessMethod.PortIo62;

                        _isInitialized = true;

                        return;

                    }

                }

                // 4. Probe secondary ACPI EC Port I/O (0x6C / 0x6E)

                byte sc6E = ReadPortByte(0x6E);

                if (sc6E != 0xFF)

                {

                    byte probeVal;

                    byte probeReg = Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP;

                    if (TryPortIoRead(0x6C, 0x6E, probeReg, out probeVal) && probeVal > 0 && probeVal < 115)

                    {

                        AccessMethod = EcAccessMethod.PortIo6C;

                        _isInitialized = true;

                        return;

                    }

                }

                // 5. Try MMIO mapping (e.g. 0xFE0B0400)

                ulong baseAddr = Profile != null ? Profile.EcBaseAddress : EC_BASE;

                IntPtr handle;

                IntPtr mapped = NativeInpOut.MapPhysToLinSafe(new IntPtr(unchecked((long)baseAddr)), EC_SIZE, out handle);

                if (mapped != IntPtr.Zero)

                {

                    byte m0 = Marshal.ReadByte(mapped, 0);

                    byte m1 = Marshal.ReadByte(mapped, 1);

                    byte mCpu = Marshal.ReadByte(mapped, Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP);

                    if (!(m0 == 0xFF && m1 == 0xFF && mCpu == 0xFF))

                    {

                        _physHandle = handle;

                        _linAddr = mapped;

                        AccessMethod = EcAccessMethod.Mmio;

                        _isInitialized = true;

                        return;

                    }

                    NativeInpOut.UnmapPhysicalMemory(handle, mapped);

                }

                // Fail-Closed Safety Check:

                // If probing methods 0..5 all failed to verify known EC/Super I/O signatures,

                // do NOT perform active port writes. Flag unverified to protect user hardware.

                AccessMethod = EcAccessMethod.Unknown;

                _isInitialized = false;

                throw new NotSupportedException(

                    "No verified Embedded Controller or Super I/O was detected on this PC.\n" +

                    "Active fan writes are disabled to protect hardware.\n" +

                    "Probes on IT87 Super I/O (0x2E/0x4E), ACPI EC (0x62/0x66 & 0x6C/0x6E), and MMIO all failed or returned 0xFF.");

            }

            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "Hardware access driver DLL could not be loaded: " + ex.Message);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException(
                    "Hardware access driver DLL has the wrong architecture (64-bit DLL required): " + ex.Message);
            }

        }

        private byte ReadRegInternal(byte reg)

        {

            if (_isSimulatedMode)

            {

                return (reg < _simulatedEcRegs.Length) ? _simulatedEcRegs[reg] : (byte)0xFF;

            }

            if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)

            {

                return ReadSuperIoHwReg(reg);

            }

            if (AccessMethod == EcAccessMethod.PortIo62)

            {

                byte val;

                if (TryPortIoRead(0x62, 0x66, reg, out val)) return val;

                return 0xFF;

            }

            if (AccessMethod == EcAccessMethod.PortIo6C)

            {

                byte val;

                if (TryPortIoRead(0x6C, 0x6E, reg, out val)) return val;

                return 0xFF;

            }

            if (_linAddr != IntPtr.Zero)

            {

                return Marshal.ReadByte(_linAddr, reg);

            }

            return 0xFF;

        }

        private void WriteRegInternal(byte reg, byte val)

        {

            if (_isSimulatedMode)

            {

                if (reg < _simulatedEcRegs.Length) _simulatedEcRegs[reg] = val;

                return;

            }

            if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)

            {

                if (reg >= 0x30 && reg <= 0x3F) return;

                WriteSuperIoHwReg(reg, val);

            }

            else if (AccessMethod == EcAccessMethod.PortIo62)

            {

                TryPortIoWrite(0x62, 0x66, reg, val);

            }

            else if (AccessMethod == EcAccessMethod.PortIo6C)

            {

                TryPortIoWrite(0x6C, 0x6E, reg, val);

            }

            else if (_linAddr != IntPtr.Zero)

            {

                Marshal.WriteByte(_linAddr, reg, val);

            }

        }

        public byte ReadReg(byte reg)

        {

            EnsureOpen();

            using (var lockScope = EcBusMutex.AcquireEc(30))

            {

                if (!lockScope.Acquired)

                {

                    return 0xFF; // Fail-closed: do not perform unsynchronized read

                }

                return ReadRegInternal(reg);

            }

        }

        public bool WriteReg(byte reg, byte val)
        {
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            EnsureOpen();
            using (var lockScope = EcBusMutex.AcquireEc(30))
            {
                if (!lockScope.Acquired)
                {
                    return false; // Fail-closed: do not write without lock
                }
                if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
                {
                    return WriteSuperIoHwReg(reg, val);
                }
                WriteRegInternal(reg, val);
                byte readBack = ReadRegInternal(reg);
                return (readBack == val);
            }
        }

        public static uint ReadAmdSmn(uint smnAddress)

        {

            bool locked = EcBusMutex.WaitPci(25);

            if (!locked) return 0; // Fail-closed: abort PCI read if mutex timed out

            uint origCf8 = 0;

            try

            {

                // Save original PCI config address to prevent collision with Windows kernel storage drivers

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

                // Always restore original PCI config address so stornvme.sys / pci.sys are uninterrupted

                try

                {

                    NativeInpOut.DlPortWritePortUlong(0xCF8, origCf8);

                }

                catch { }

                EcBusMutex.ReleasePci();

            }

        }

        public static int ReadAmdCpuTempTctl()

        {

            try

            {

                uint raw = ReadAmdSmn(0x00059800); // AMD Zen / Strix Point THM_TCON_CUR_TMP

                if (raw == 0 || raw == 0xFFFFFFFF) return 0;

                uint tRaw = (raw >> 21) & 0x7FF;

                double t = tRaw * 0.125;

                if ((raw & 0x00080000) != 0) // AMD temperature offset flag (-49 C)

                    t -= 49.0;

                int val = (int)Math.Round(t);

                if (val >= 15 && val <= 115)

                    return val;

            }

            catch { }

            return 0;

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

        private FanStatus _lastStatusCache;

        public FanStatus GetStatus()

        {

            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];

            EnsureOpen();

            byte f1h = 0, f1l = 0, f2h = 0, f2l = 0, modeVal = 0, f1Ctrl = 0, f2Ctrl = 0;

            int ecCpuTemp = 0, ecGpuTemp = 0;

            int sioFan1 = 0, sioFan2 = 0;

            using (var lockScope = EcBusMutex.AcquireEc(35))

            {

                if (!lockScope.Acquired)

                {

                    // Fail-closed on mutex contention: return last cached status rather than corrupting readings

                    if (_lastStatusCache.CpuTemp > 0)

                    {

                        return _lastStatusCache;

                    }

                }

                else

                {

                    if (AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)

                    {

                        sioFan1 = ReadSuperIoFanRpm(1);

                        sioFan2 = ReadSuperIoFanRpm(2);

                        ecCpuTemp = ReadSuperIoHwReg(0x29); // IT87 Temp 1

                        ecGpuTemp = ReadSuperIoHwReg(0x2A); // IT87 Temp 2

                        f1h = 0; f1l = 0; f2h = 0; f2l = 0;

                        modeVal = 0;

                        byte ext1 = ReadSuperIoHwReg(0x6B); // Fan 1 APU duty (Inverted polarity: 40 idle, 0 100%)

                        byte ext2 = ReadSuperIoHwReg(0x73); // Fan 2 Sys duty (Inverted polarity: 240 idle, 0 100%)

                        bool f1Auto = (ext1 >= 40 && ext1 <= 50);

                        bool f2Auto = (ext2 >= 240 || ext2 == 0x00);

                        int f1Duty = (ext1 >= 40) ? 0 : (int)Math.Round((40 - ext1) * 100.0 / 40.0);

                        int f2Duty = (ext2 >= 240) ? 0 : (int)Math.Round((240 - ext2) * 100.0 / 240.0);

                        f1Ctrl = (byte)(f1Auto ? 0x00 : (0x80 | (f1Duty & 0x7F)));

                        f2Ctrl = (byte)(f2Auto ? 0x00 : (0x80 | (f2Duty & 0x7F)));

                    }

                    else

                    {

                        f1h = ReadRegInternal(p.RegFan1Hi);

                        f1l = ReadRegInternal(p.RegFan1Lo);

                        f2h = ReadRegInternal(p.RegFan2Hi);

                        f2l = ReadRegInternal(p.RegFan2Lo);

                        ecCpuTemp = ReadRegInternal(p.RegCpuTemp);

                        ecGpuTemp = ReadRegInternal(p.RegGpuTemp);

                        modeVal = ReadRegInternal(p.RegModeStatus);

                        f1Ctrl  = ReadRegInternal(p.RegFan1Ctrl);

                        f2Ctrl  = ReadRegInternal(p.RegFan2Ctrl);

                    }

                }

            }

            int adlTemp, adlCpuTemp, adlPowerWatts, cpuPwr, gfxPwr, socPwr, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt;

            string pwrSrc;

            _adlSensor.ReadTelemetryDetailed(out adlTemp, out adlCpuTemp, out adlPowerWatts, out cpuPwr, out gfxPwr, out socPwr,

                out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out pwrSrc);

            // Safe multi-tier temperature resolution:

            // Tier 1: AMD ADL2 PMLOG (via atiadlxx.dll / amdfendrm.sys) - official AMD kernel driver, ZERO raw port I/O

            // Tier 2: EC / Super I/O thermistor register

            // Tier 3: Last-resort fallback to AMD SMN silicon register (restores 0xCF8)

            int cpuTemp = 0;

            if (adlCpuTemp > 0 && adlCpuTemp <= 115)

            {

                cpuTemp = adlCpuTemp;

            }

            else if (ecCpuTemp > 0 && ecCpuTemp < 255)

            {

                cpuTemp = ecCpuTemp;

            }

            else

            {

                cpuTemp = ReadAmdCpuTempTctl();

            }

            int gpuTemp = (adlTemp > 0)

                ? adlTemp

                : ((ecGpuTemp > 0 && ecGpuTemp < 255) ? ecGpuTemp : 0);

            string modeStr = "Unknown";

            int modeCode = (modeVal <= 2) ? (int)modeVal : -1;

            switch (modeVal)

            {

                case 0: modeStr = "Balanced"; break;

                case 1: modeStr = "Performance"; break;

                case 2: modeStr = "Silent"; break;

                default: modeCode = -1; break;

            }

            int f1Ec = DecodeTachometerRpm(f1h, f1l);

            int f2Ec = DecodeTachometerRpm(f2h, f2l);

            int resolvedSio1 = 0;

            int resolvedSio2 = 0;

            if (SuperIoBaseAddr != 0)

            {

                resolvedSio1 = sioFan1;

                resolvedSio2 = sioFan2;

            }

            int fan1Rpm = (resolvedSio1 > 0) ? resolvedSio1 : f1Ec;

            int fan2Rpm = (resolvedSio2 > 0) ? resolvedSio2 : f2Ec;

            int apuPower = adlPowerWatts > 0

                ? adlPowerWatts

                : EstimateApuPowerWatts(cpuTemp, gpuTemp, modeCode);

            if (adlPowerWatts <= 0)

            {
                pwrSrc = "EC Thermal-TDP Model (CPU/iGPU Temp + Active BIOS Mode)";
            }

            NvmeTelemetry nvme;
            MemoryTelemetry mem;
            SystemStorageMemorySensor.Sample(out nvme, out mem);

            RtssTelemetry rtss = FpsMonitor.GetTelemetry();
            RadeonSettingsTelemetry rad = RadeonSettingsSensor.Sample();

            FanStatus st = new FanStatus
            {
                CpuTemp = cpuTemp,
                GpuTemp = gpuTemp,
                ApuPowerWatts = apuPower,
                CpuPowerWatts = cpuPwr,
                GfxPowerWatts = gfxPwr,
                SocPowerWatts = socPwr,
                ApuPowerLimitWatts = pwrLim,
                CpuClockMhz = cpuClk,
                GfxClockMhz = gfxClk,
                GfxActivityPct = gfxAct,
                GfxVoltageMv = gfxVolt,
                RtssFps = rtss.IsAvailable ? rtss.Fps : -1,
                RtssRunning = rtss.IsRtssRunning,
                RtssFrametimeMs = rtss.IsAvailable ? rtss.FrametimeMs : 0.0,
                RtssAvgFps = rtss.IsAvailable ? rtss.AvgFps : 0,
                RtssMinFps = rtss.IsAvailable ? rtss.MinFps : 0,
                RtssMaxFps = rtss.IsAvailable ? rtss.MaxFps : 0,
                RtssP1LowFps = rtss.IsAvailable ? rtss.P1LowFps : 0,
                RtssP01LowFps = rtss.IsAvailable ? rtss.P01LowFps : 0,
                RtssProcessName = rtss.IsAvailable ? rtss.ProcessName : null,
                PowerSource = pwrSrc,
                Fan1Rpm = fan1Rpm,
                Fan2Rpm = fan2Rpm,
                Mode = modeStr,
                ModeCode = modeCode,
                Fan1Pct = (f1Ctrl != 0xFF) ? (f1Ctrl & 0x7F) : 0,
                Fan2Pct = (f2Ctrl != 0xFF) ? (f2Ctrl & 0x7F) : 0,
                Fan1Manual = (f1Ctrl != 0xFF && (f1Ctrl & 0x80) != 0) ? 1 : 0,
                Fan2Manual = (f2Ctrl != 0xFF && (f2Ctrl & 0x80) != 0) ? 1 : 0,
                NvmeTemp = nvme.IsAvailable ? nvme.CompositeTemp : 0,
                NvmeHealthPct = nvme.IsAvailable ? nvme.HealthPct : 0,
                NvmeModel = nvme.IsAvailable ? nvme.Model : null,
                NvmeCritWarning = nvme.IsAvailable ? nvme.CriticalWarning : (byte)0,
                NvmeAvailableSpare = nvme.IsAvailable ? nvme.AvailableSpare : (byte)0,
                NvmeWearPct = nvme.IsAvailable ? nvme.PercentageUsed : (byte)0,
                RamUsedGb = mem.IsAvailable ? mem.UsedGb : 0.0,
                RamTotalGb = mem.IsAvailable ? mem.TotalGb : 0.0,
                RamLoadPct = mem.IsAvailable ? mem.LoadPct : 0,
                RadeonFeaturesAvailable = rad.IsAvailable,
                RadeonFeaturesSummary = rad.Summary,
                RadeonSoftwareVer = rad.SoftwareVersion,
                RadeonFreeSync = rad.FreeSync,
                RadeonAntiLag = rad.AntiLag,
                RadeonAfmf = rad.Afmf,
                RadeonBoost = rad.RadeonBoost,
                RadeonChill = rad.Chill,
                RadeonRsr = rad.Rsr,
                RadeonRis = rad.Ris,
                RadeonEnhancedSync = rad.EnhancedSync,
                RadeonSam = rad.SmartAccessMemory
            };

            _lastStatusCache = st;

            return st;

        }

        public static int DecodeTachometerRpm(byte hi, byte lo)

        {

            if (hi == 0xFF && lo == 0xFF) return 0;

            int rpm = (hi << 8) | lo;

            if (rpm == 8000 || rpm == 65535 || rpm < 0) return 0;

            return rpm;

        }

        public static byte EncodeModeCommand(int code)

        {

            switch (code)

            {

                case 0: return 0x80;

                case 1: return 0x81;

                case 2: return 0x82;

                default: throw new ArgumentException("Invalid mode (0=Balanced, 1=Performance, 2=Silent)");

            }

        }

        public static byte EncodeFanCommand(int pct)

        {

            if (pct < 0 || pct > 100)

                throw new ArgumentException("Percentage must be between 0 and 100.");

            if (pct == 0)

                return 0x00;

            return (byte)(0x80 | (pct & 0x7F));

        }

        public bool SetMode(int code)
        {
            if (code < 0 || code > 2) return false;
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            EnsureOpen();
            if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
            {
                // Super I/O chips control PWM outputs directly; ACPI EC mode registers do not apply
                return true;
            }
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            byte cmdByte = EncodeModeCommand(code);
            using (var lockScope = EcBusMutex.AcquireEc(35))
            {
                if (!lockScope.Acquired) return false;
                WriteRegInternal(p.RegModeCmd, cmdByte);
                byte readBack = ReadRegInternal(p.RegModeCmd);
                byte statusBack = ReadRegInternal(p.RegModeStatus);
                return (readBack == cmdByte || statusBack == code);
            }
        }

        public bool SetFan(int fanId, int pct)
        {
            if (fanId != 1 && fanId != 2) return false;
            if (pct < 0 || pct > 100) return false;
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            try { EnsureOpen(); } catch { return false; }
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            byte val = EncodeFanCommand(pct);
            byte reg = (fanId == 1) ? p.RegFan1Ctrl : p.RegFan2Ctrl;
            using (var lockScope = EcBusMutex.AcquireEc(35))
            {
                if (!lockScope.Acquired)
                {
                    return false; // Fail-closed: do not write to fan control registers on contention
                }

                // If hardware is controlled via Super I/O (e.g. IT8613 on HX 370 / TS-12C)
                if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
                {
                    bool sioOk = true;
                    // On IT8613 / IT87: Enable PWM output and manual mode (0x7F, never 0x80 failsafe)
                    byte mainCtrl = ReadSuperIoHwReg(0x13);
                    if ((mainCtrl & 0x07) != 0x07)
                    {
                        if (!WriteSuperIoHwReg(0x13, (byte)(mainCtrl | 0x07))) sioOk = false;
                    }
                    if (!WriteSuperIoHwReg(0x15, 0x7F)) sioOk = false;
                    if (!WriteSuperIoHwReg(0x16, 0x7F)) sioOk = false;
                    if (!WriteSuperIoHwReg(0x17, 0x7F)) sioOk = false;
                    byte modeReg = ReadSuperIoHwReg(0x0C);
                    if ((modeReg & 0x3F) != 0x3F)
                    {
                        if (!WriteSuperIoHwReg(0x0C, (byte)(modeReg | 0x3F))) sioOk = false;
                    }
                    if (fanId == 1)
                    {
                        // APU Fan: PWM 2 (0x6B) + sync PWM 1 (0x63), Inverted Polarity (40 quiet idle ~2500 RPM, 0 100% full speed)
                        byte duty = (pct <= 0) ? (byte)40 : (byte)Math.Max(0, Math.Min(40, 40 - ((pct * 40) / 100)));
                        if (!WriteSuperIoHwReg(0x6B, duty)) sioOk = false;
                        if (!WriteSuperIoHwReg(0x63, duty)) sioOk = false;
                    }
                    else
                    {
                        // Sys Fan: PWM 3 (0x73), Inverted Polarity (240 idle = quiet ~2500 RPM, 0 100% full speed)
                        byte duty = (pct <= 0) ? (byte)240 : (byte)Math.Max(0, Math.Min(240, 240 - ((pct * 240) / 100)));
                        if (!WriteSuperIoHwReg(0x73, duty)) sioOk = false;
                    }
                    return sioOk;
                }
                else
                {
                    // EC-based fan control (MMIO 0xFE0B0400 or ACPI Port I/O 0x62/0x66)
                    WriteRegInternal(reg, val);
                    byte readBack = ReadRegInternal(reg);
                    if (readBack != val)
                    {
                        Thread.Sleep(2);
                        readBack = ReadRegInternal(reg);
                    }
                    return (readBack == val);
                }
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
            if (!WriteSuperIoHwReg(0x15, 0x7F)) allOk = false;
            if (!WriteSuperIoHwReg(0x16, 0x7F)) allOk = false;
            if (!WriteSuperIoHwReg(0x17, 0x7F)) allOk = false;
            if (!WriteSuperIoHwReg(0x6B, 40)) allOk = false;   // APU Fan quiet idle (~2500 RPM, inverted)
            if (!WriteSuperIoHwReg(0x63, 40)) allOk = false;   // APU Fan sync
            if (!WriteSuperIoHwReg(0x73, 240)) allOk = false;  // Sys Fan quiet idle (~2500 RPM, inverted)

            return allOk;
        }

        public bool SetAuto()
        {
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            try { EnsureOpen(); } catch { return false; }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using (var lockScope = EcBusMutex.AcquireEc(40))
                {
                    if (!lockScope.Acquired)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E)
                    {
                        bool sioOk = ApplySafeSuperIoFallback();
                        if (sioOk)
                        {
                            LastAutoRestoreMode = AutoRestoreMode.SafeSuperIoThermalFallbackAndEcAuto;
                            return true;
                        }
                    }
                    else
                    {
                        bool ecOk = RestoreNativeEcAuto();
                        if (ecOk)
                        {
                            LastAutoRestoreMode = AutoRestoreMode.NativeBiosAuto;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        

        public byte[] ReadAllRegisters()

        {

            EnsureOpen();

            byte[] buf = new byte[EC_SIZE];

            using (var lockScope = EcBusMutex.AcquireEc(60))

            {

                if (!lockScope.Acquired)

                {

                    for (int i = 0; i < EC_SIZE; i++) buf[i] = 0xFF;

                    return buf;

                }

                for (int i = 0; i < EC_SIZE; i++)

                {

                    buf[i] = ReadRegInternal((byte)i);

                }

                return buf;

            }

        }

        public void Dispose()

        {

            _adlSensor.Dispose();

            if (_linAddr != IntPtr.Zero)

            {

                try

                {

                    NativeInpOut.UnmapPhysicalMemory(_physHandle, _linAddr);

                }

                catch { }

                _linAddr = IntPtr.Zero;

            }

        }

    }

}

