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

    public interface IHardwareBackend : IDisposable
    {
        FanStatus GetStatus();
        byte[] ReadAllRegisters();
        bool SetFan(int fanId, int pct);
        bool SetMode(int code);
        bool SetAuto();
    }

    public class HardwareBackend : IHardwareBackend, IDisposable

    {

        public const ulong EC_BASE = 0xFE0B0400UL;

        public const uint EC_SIZE = 0x100;


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
            try
            {
                if (NativeInpOut.IsDriverOpen())
                {
                    ushort cId, hBase;
                    bool allowWrites = (hw.Profile != null && !hw.Profile.IsReadOnly && hw.Profile.Id == "ryzen-hx370-ec");
                    if (ProbeSuperIo(0x2E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase))
                    {
                        hw.SuperIoChipId = cId;
                        hw.SuperIoBaseAddr = hBase;
                        hw.AccessMethod = EcAccessMethod.SuperIo2E;
                    }
                    else if (ProbeSuperIo(0x4E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase))
                    {
                        hw.SuperIoChipId = cId;
                        hw.SuperIoBaseAddr = hBase;
                        hw.AccessMethod = EcAccessMethod.SuperIo4E;
                    }
                }
            }
            catch { }
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
            // Reject disconnected bus floats (0xFF = 255), sensor read errors, and uninitialized/floating sensor readings (0x7F = 127)
            if (temp >= EMERGENCY_TEMP_TRIP_C && temp <= 115 && temp != 127)
            {
                activeState = true;
            }
            else if (activeState && temp <= EMERGENCY_TEMP_RECOVER_C && temp >= 0)
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

            // Hardware monitor base ranges (IT8613E / IT87xx / NCT6779) & AMD SMBus controller
            if ((port >= 0x0290 && port <= 0x0297) ||
                (port >= 0x02A0 && port <= 0x02A7) ||
                (port >= 0x0A00 && port <= 0x0A3F) ||
                (port >= 0x0B00 && port <= 0x0B3F) ||
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

                        bool verified = (candidateBase != 0 && candidateBase != 0xFFFF && VerifyIt87HwBase(candidateBase));

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
                    // Primary APU blower: Channel 1 (0x0D / 0x18) = HWiNFO64 "CPU"
                    rawRpm = evalTach16(0x0D, 0x18);
                    // Fallback to Channel 2 (0x0E / 0x19) if Channel 1 is not routed
                    if (rawRpm == 0)
                        rawRpm = evalTach16(0x0E, 0x19);

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
                    // Secondary Sys blower: Channel 3 (0x0F / 0x1A) = HWiNFO64 "Power"
                    rawRpm = evalTach16(0x0F, 0x1A);
                    // Fallback to Channel 2 (0x0E / 0x19) if Channel 3 is not routed
                    if (rawRpm == 0)
                        rawRpm = evalTach16(0x0E, 0x19);

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

        public void ReinitializeHardwareAfterResume()
        {
            if (_isSimulatedMode) return;
            _isInitialized = false;
            try
            {
                EnsureOpen();
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("Hardware reinitialization after resume failed: " + ex.Message);
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

                bool allowWrites = (Profile != null && !Profile.IsReadOnly);

                if (ProbeSuperIo(0x2E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase))

                {

                    SuperIoChipId = cId;

                    SuperIoBaseAddr = hBase;

                    AccessMethod = EcAccessMethod.SuperIo2E;

                    _isInitialized = true;

                    DumpSuperIoDiagnostic(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sio-diag.txt"));

                    return;

                }

                // 2. Probe LPC Super I/O on Port 0x4E

                if (ProbeSuperIo(0x4E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase))

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
                else if (NativeInpOut.ActiveDriver == HardwareDriverEngine.WinRing0)
                {
                    // WinRing0 does not support process linear mapping, but provides direct physical memory IOCTLs.
                    // Probe directly through the driver without creating disconnected heap buffers.
                    byte m0 = NativeInpOut.ReadPhysicalByteSafe(baseAddr);
                    byte m1 = NativeInpOut.ReadPhysicalByteSafe(baseAddr + 1);
                    byte mCpu = NativeInpOut.ReadPhysicalByteSafe(baseAddr + (Profile != null ? Profile.RegCpuTemp : REG_CPU_TEMP));
                    if (!(m0 == 0xFF && m1 == 0xFF && mCpu == 0xFF))
                    {
                        _physHandle = IntPtr.Zero;
                        _linAddr = IntPtr.Zero;
                        AccessMethod = EcAccessMethod.Mmio;
                        _isInitialized = true;
                        return;
                    }
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

            if (AccessMethod == EcAccessMethod.Mmio)
            {
                if (_linAddr != IntPtr.Zero)
                {
                    return Marshal.ReadByte(_linAddr, reg);
                }
                // WinRing0 per-access direct physical memory IOCTL
                ulong baseAddr = Profile != null ? Profile.EcBaseAddress : EC_BASE;
                return NativeInpOut.ReadPhysicalByteSafe(baseAddr + reg);
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
            else if (AccessMethod == EcAccessMethod.Mmio)
            {
                if (_linAddr != IntPtr.Zero)
                {
                    Marshal.WriteByte(_linAddr, reg, val);
                }
                else
                {
                    // WinRing0 per-access direct physical memory IOCTL (writes directly to hardware, not heap)
                    ulong baseAddr = Profile != null ? Profile.EcBaseAddress : EC_BASE;
                    NativeInpOut.WritePhysicalByteSafe(baseAddr + reg, val);
                }
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

        private static ushort _amdSmbusBase = 0;
        private static bool _smbusBaseProbed = false;
        private static int _cachedDdrStickCount = 0;
        private static readonly RefreshThrottle _spdThrottleStick0 = new RefreshThrottle(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero);
        private static readonly RefreshThrottle _spdThrottleStick1 = new RefreshThrottle(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(250));
        private static int _cachedSpd1 = 0;
        private static int _cachedSpd2 = 0;
        private static bool _cachedSpd1Ok = false;
        private static bool _cachedSpd2Ok = false;

        private static ushort GetAmdSmbusBase()
        {
            if (_smbusBaseProbed) return _amdSmbusBase;
            _smbusBaseProbed = true;
            try
            {
                // AMD FCH SMBus Controller is PCI device 0:20:0 (0x8000A000)
                // Offset 0x90 stores the SMBus Base Address (bits 15:5)
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

            // Probe standard AMD FCH SMBus ports
            foreach (ushort testPort in new ushort[] { 0x0B00, 0x0B20 })
            {
                byte sts = ReadPortByte(testPort);
                if (sts != 0xFF && (sts & 0x01) == 0) // Not floating bus, host not permanently busy
                {
                    _amdSmbusBase = testPort;
                    return _amdSmbusBase;
                }
            }
            return 0;
        }

        private static bool ReadDdr5SpdHubTemp(ushort smbusBase, byte slaveAddr, out int tempC)
        {
            tempC = 0;
            if (smbusBase == 0) return false;

            // Strict synchronization with HWiNFO64 & external monitoring software
            if (!EcBusMutex.WaitSmbus(25)) return false;
            try
            {
                ushort stsPort = smbusBase;
                ushort cntPort = (ushort)(smbusBase + 0x02);
                ushort cmdPort = (ushort)(smbusBase + 0x03);
                ushort addPort = (ushort)(smbusBase + 0x04);
                ushort dat0Port = (ushort)(smbusBase + 0x05);
                ushort dat1Port = (ushort)(smbusBase + 0x06);

                byte sts = ReadPortByte(stsPort);
                if (sts == 0xFF) return false;

                // Clear previous interrupt/error status (W1C bits)
                WritePortByte(stsPort, (byte)(sts & 0x1E));

                // Wait for host idle (bit 0 == 0)
                int timeout = 50;
                while ((ReadPortByte(stsPort) & 0x01) != 0 && --timeout > 0)
                {
                    System.Threading.Thread.Sleep(1);
                }
                if (timeout == 0) return false;

                // Slave address: (slaveAddr << 1) | 0x01 (Read)
                WritePortByte(addPort, (byte)((slaveAddr << 1) | 0x01));

                // JEDEC SPD5118 temperature register: 0x31 (LSB)
                WritePortByte(cmdPort, 0x31);

                // Word Data Protocol (011b = 0x0C) + Start (bit 6 = 0x40) -> 0x4C
                WritePortByte(cntPort, 0x4C);

                // Poll for completion (bit 1 INTR) or error (bits 2, 3, 4)
                timeout = 100;
                byte finalSts = 0;
                while (--timeout > 0)
                {
                    finalSts = ReadPortByte(stsPort);
                    if ((finalSts & 0x1E) != 0) break;
                    System.Threading.Thread.Sleep(1);
                }

                // If device error / NACK / bus error, device not responding
                if ((finalSts & 0x1C) != 0 || (finalSts & 0x02) == 0)
                {
                    WritePortByte(stsPort, finalSts);
                    return false;
                }

                byte lsb = ReadPortByte(dat0Port);
                byte msb = ReadPortByte(dat1Port);
                WritePortByte(stsPort, finalSts);

                // JEDEC SPD5118: 11-bit signed two's complement, 0.25 °C resolution
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
            catch
            {
                return false;
            }
            finally
            {
                EcBusMutex.ReleaseSmbus();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint ProviderSignature, uint TableId, IntPtr pBuffer, uint BufferSize);

        private static int DetectPhysicalMemoryStickCountFromSmbios()
        {
            try
            {
                // 'RSMB' = 0x52534D42
                uint sig = 0x52534D42;
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

                        // SMBIOS Type 17: Memory Device
                        if (type == 17 && offset + len <= read)
                        {
                            if (offset + 0x0E <= read)
                            {
                                ushort memSize = (ushort)(buf[offset + 0x0C] | (buf[offset + 0x0D] << 8));
                                // In SMBIOS spec, 0 indicates empty socket; 0xFFFF indicates unknown
                                if (memSize > 0 && memSize != 0xFFFF)
                                {
                                    count++;
                                }
                            }
                        }

                        offset += len;
                        while (offset + 1 < read && !(buf[offset] == 0 && buf[offset + 1] == 0))
                        {
                            offset++;
                        }
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

        private static int GetDdrStickCount(double totalGb)
        {
            if (_cachedDdrStickCount > 0) return _cachedDdrStickCount;

            // 1. Direct physical hardware query via SMBIOS Type 17 (works across standard user desktop & elevated sessions)
            int smbiosCount = DetectPhysicalMemoryStickCountFromSmbios();
            if (smbiosCount > 0)
            {
                _cachedDdrStickCount = smbiosCount;
                return _cachedDdrStickCount;
            }

            // 2. Probe SPD5118 Hub at 0x50/0x51 (Channel A) and 0x52/0x53 (Channel B) via SMBus
            ushort smbus = GetAmdSmbusBase();
            if (smbus != 0)
            {
                int t1, t2;
                bool ok1 = ReadDdr5SpdHubTemp(smbus, 0x50, out t1) || ReadDdr5SpdHubTemp(smbus, 0x51, out t1);
                bool ok2 = ReadDdr5SpdHubTemp(smbus, 0x52, out t2) || ReadDdr5SpdHubTemp(smbus, 0x53, out t2);
                if (ok1 && ok2)
                {
                    _cachedDdrStickCount = 2;
                    return 2;
                }
            }

            // 3. Fallback capacity heuristic:
            // Dual sticks typically >= 16 GB, but APU reserves 4-16 GB for iGPU VRAM.
            // A 64GB machine (2x 32GB) with 16GB VRAM reports 47.6 GB.
            // A 48GB machine (2x 24GB) reports ~38-44 GB.
            // A 32GB machine (2x 16GB) reports ~22-30 GB.
            _cachedDdrStickCount = (totalGb >= 45.0 || (totalGb >= 20.0 && totalGb <= 30.0)) ? 2 : 1;
            return _cachedDdrStickCount;
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

        public bool IsDriverAvailable { get { return NativeInpOut.ActiveDriver != HardwareDriverEngine.None; } }

        public bool IsEcActive { get { return _isInitialized && AccessMethod != EcAccessMethod.Unknown; } }

        public FanStatus GetStatus()
        {
            DeviceProfile p = Profile ?? DeviceProfile.KnownProfiles[0];
            bool ecAvailable = true;
            try
            {
                EnsureOpen();
            }
            catch
            {
                ecAvailable = false;
            }

            byte f1h = 0, f1l = 0, f2h = 0, f2l = 0, modeVal = 0xFF, f1Ctrl = 0, f2Ctrl = 0;
            byte ecVrmTemp = 0, ecAmbientTemp = 0, ecDdrTemp = 0, ecDcAdc = 0;
            int ecCpuTemp = 0, ecGpuTemp = 0;
            int sioFan1 = 0, sioFan2 = 0;

            if (ecAvailable)
            {
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

                            // IT87 hardware monitor temperature channels:
                            // Channel 1: 0x29
                            // Channel 2: 0x2A
                            // Channel 3: 0x2B (HWiNFO64 Temperature 3 - CPU diode on TOPC TS-12C / IT8613, matches Tctl)
                            // Channel 4: 0x2C (HWiNFO64 Temperature 4 - Motherboard ambient)
                            // IT87 hardware monitor temperature channels:
                            // Channel 1: 0x29
                            // Channel 2: 0x2A
                            // Channel 3: 0x2B (HWiNFO64 Temperature 3 - CPU diode on TOPC TS-12C / IT8613, matches Tctl)
                            // Channel 4: 0x2C (HWiNFO64 Temperature 4)
                            // Channel 5: 0x2D (HWiNFO64 Temperature 5 - VRM MOSFET thermistor)
                            // Channel 6: 0x2E (HWiNFO64 Temperature 6 - Motherboard / Chassis Ambient thermistor)
                            // Channel 7: 0x2F (Extended thermistor channel)
                            byte t1 = ReadSuperIoHwReg(0x29);
                            byte t2 = ReadSuperIoHwReg(0x2A);
                            byte t3 = ReadSuperIoHwReg(0x2B);
                            byte t4 = ReadSuperIoHwReg(0x2C);
                            byte t5 = ReadSuperIoHwReg(0x2D);
                            byte t6 = ReadSuperIoHwReg(0x2E);
                            byte t7 = ReadSuperIoHwReg(0x2F);

                            int temp1 = (sbyte)t1;
                            int temp2 = (sbyte)t2;
                            int temp3 = (sbyte)t3;
                            int temp4 = (sbyte)t4;
                            int temp5 = (sbyte)t5;
                            int temp6 = (sbyte)t6;
                            int temp7 = (sbyte)t7;

                            // Prioritize connected channel 3 (0x2B) if valid (15..115C), else channel 1 (0x29)
                            if (temp3 >= 15 && temp3 <= 115) ecCpuTemp = temp3;
                            else if (temp1 >= 15 && temp1 <= 115) ecCpuTemp = temp1;
                            else if (temp2 >= 15 && temp2 <= 115) ecCpuTemp = temp2;
                            else ecCpuTemp = 0;

                            // Motherboard / Chassis Ambient thermistor:
                            // Channel 6 (0x2E) on TOPC TS-12C / IT8613 = HWiNFO64 Temperature 6 (41..49°C)
                            if (temp6 >= 15 && temp6 <= 115) ecAmbientTemp = (byte)temp6;
                            else if (temp7 >= 15 && temp7 <= 115) ecAmbientTemp = (byte)temp7;
                            else if (temp4 >= 15 && temp4 <= 115) ecAmbientTemp = (byte)temp4;
                            else if (temp2 >= 15 && temp2 <= 115) ecAmbientTemp = (byte)temp2;
                            else if (temp1 >= 15 && temp1 <= 115) ecAmbientTemp = (byte)temp1;
                            else if (temp5 >= 15 && temp5 <= 115) ecAmbientTemp = (byte)Math.Max(25, temp5 - 12);

                            // VRM MOSFET thermistor:
                            // Channel 5 (0x2D) on TOPC TS-12C / IT8613 = HWiNFO64 Temperature 5 (43..65°C)
                            if (temp5 >= 15 && temp5 <= 115) ecVrmTemp = (byte)temp5;
                            else if (temp6 >= 15 && temp6 <= 115) ecVrmTemp = (byte)temp6;

                            ecGpuTemp = (temp2 >= 15 && temp2 <= 115) ? temp2 : 0;

                            f1h = 0; f1l = 0; f2h = 0; f2l = 0;
                            modeVal = 0;
                            byte ext1 = ReadSuperIoHwReg(0x6B); // APU Fan extended duty (0x6B)
                            byte ext2 = ReadSuperIoHwReg(0x73); // Sys Fan extended duty (0x73)

                            byte reg16 = ReadSuperIoHwReg(0x16); // APU Fan manual mode register (0x16)
                            byte reg17 = ReadSuperIoHwReg(0x17); // Sys Fan manual mode register (0x17)
                            bool f1Auto = (reg16 & 0x80) != 0;
                            bool f2Auto = (reg17 & 0x80) != 0;

                            // Direct hardware duty: 0 (0x00) = 0% speed (stopped/quiet), 255 (0xFF) = 100% full speed
                            int f1Duty = Math.Max(0, Math.Min(100, (int)Math.Round(ext1 * 100.0 / 255.0)));
                            int f2Duty = Math.Max(0, Math.Min(100, (int)Math.Round(ext2 * 100.0 / 255.0)));
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
                            if (p.RegVrmTemp > 0) ecVrmTemp = ReadRegInternal(p.RegVrmTemp);
                            if (p.RegAmbientTemp > 0) ecAmbientTemp = ReadRegInternal(p.RegAmbientTemp);
                            if (p.RegDdrTemp > 0) ecDdrTemp = ReadRegInternal(p.RegDdrTemp);
                            if (p.RegDcVoltsAdc > 0) ecDcAdc = ReadRegInternal(p.RegDcVoltsAdc);
                        }
                    }
                }
            }

            int adlTemp, adlCpuTemp, adlPowerWatts, cpuPwr, gfxPwr, socPwr, pwrLim, cpuClk, gfxClk, gfxAct, gfxVolt, memClk, hotspot, npuClk, npuAct;
            int adlFclk, adlGfxCurr, adlSocCurr;
            string pwrSrc;
            _adlSensor.ReadTelemetryDetailed(out adlTemp, out adlCpuTemp, out adlPowerWatts, out cpuPwr, out gfxPwr, out socPwr,
                out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out pwrSrc,
                out memClk, out hotspot, out npuClk, out npuAct, out adlFclk, out adlGfxCurr, out adlSocCurr);

            // High-precision silicon junction resolution (matches HWiNFO64 / LibreHardwareMonitor / Ryzen Master):
            // Tier 1: AMD SMN silicon register 0x00059800 (Tctl/Tdie) - exact silicon die junction temperature
            // Tier 2: AMD ADL2 PMLOG (via atiadlxx.dll) - official Radeon driver on-die CPU junction temperature
            // Tier 3: Super I/O CPU Socket Thermistor (Register 0x2B on IT8613 = HWiNFO64 Temperature 3)
            int cpuTemp = 0;
            int smnTctl = ReadAmdCpuTempTctl();
            if (smnTctl >= 15 && smnTctl <= 115)
            {
                cpuTemp = smnTctl;
            }
            else if (adlCpuTemp >= 15 && adlCpuTemp <= 115)
            {
                cpuTemp = adlCpuTemp;
            }
            else if (ecCpuTemp >= 15 && ecCpuTemp <= 115)
            {
                cpuTemp = ecCpuTemp;
            }

            int gpuTemp = (adlTemp > 0)
                ? adlTemp
                : ((ecGpuTemp > 0 && ecGpuTemp <= 115) ? ecGpuTemp : 0);

            string modeStr = "Unknown";
            int modeCode = (modeVal <= 2) ? (int)modeVal : -1;
            if (!ecAvailable)
            {
                modeStr = "[Safe Telemetry]";
            }
            else
            {
                switch (modeVal)
                {
                    case 0: modeStr = "Balanced"; break;
                    case 1: modeStr = "Performance"; break;
                    case 2: modeStr = "Silent"; break;
                    default: modeCode = -1; break;
                }
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

            AmdSmuTelemetry smu = AmdSmuSiliconSensor.Sample(
                cpuPwr > 0 ? cpuPwr : (apuPower > 0 ? apuPower : 15),
                cpuClk, memClk, gfxVolt, adlFclk, adlGfxCurr, adlSocCurr);

            RtssTelemetry rtss = FpsMonitor.GetTelemetry();
            RadeonSettingsTelemetry rad = RadeonSettingsSensor.Sample();

            // DDR5 Dual Stacked SO-DIMM Thermal Resolution
            int ddrCount = GetDdrStickCount(mem.TotalGb);
            int ddrTemp1 = 0;
            int ddrTemp2 = 0;
            int ddrComposite = 0;

            ushort smbusBase = GetAmdSmbusBase();
            if (smbusBase != 0)
            {
                if (_spdThrottleStick0.TryBegin() || !_cachedSpd1Ok)
                {
                    int s1;
                    _cachedSpd1Ok = ReadDdr5SpdHubTemp(smbusBase, 0x50, out s1) || ReadDdr5SpdHubTemp(smbusBase, 0x51, out s1);
                    if (_cachedSpd1Ok) _cachedSpd1 = s1;
                }
                if (_spdThrottleStick1.TryBegin() || (!_cachedSpd2Ok && _cachedSpd1Ok))
                {
                    int s2;
                    _cachedSpd2Ok = ReadDdr5SpdHubTemp(smbusBase, 0x52, out s2) || ReadDdr5SpdHubTemp(smbusBase, 0x53, out s2);
                    if (_cachedSpd2Ok) _cachedSpd2 = s2;
                }
            }
            int spd1 = _cachedSpd1;
            int spd2 = _cachedSpd2;
            bool spd1Ok = _cachedSpd1Ok;
            bool spd2Ok = _cachedSpd2Ok;

            if (spd1Ok && spd2Ok)
            {
                ddrCount = 2;
                ddrTemp1 = spd1;
                ddrTemp2 = spd2;
                ddrComposite = Math.Max(ddrTemp1, ddrTemp2);
            }
            else if (spd1Ok && !spd2Ok)
            {
                if (ddrCount == 2)
                {
                    // 2 physical sticks verified: Slot 0 measured, Slot 1 (stacked top SODIMM) modelled +4°C warmer
                    ddrTemp1 = spd1;
                    ddrTemp2 = Math.Min(95, spd1 + 4);
                    ddrComposite = Math.Max(ddrTemp1, ddrTemp2);
                }
                else
                {
                    ddrCount = 1;
                    ddrTemp1 = spd1;
                    ddrTemp2 = 0;
                    ddrComposite = spd1;
                }
            }
            else if (!spd1Ok && spd2Ok)
            {
                if (ddrCount == 2)
                {
                    ddrTemp2 = spd2;
                    ddrTemp1 = Math.Max(25, spd2 - 4);
                    ddrComposite = Math.Max(ddrTemp1, ddrTemp2);
                }
                else
                {
                    ddrCount = 1;
                    ddrTemp1 = spd2;
                    ddrTemp2 = 0;
                    ddrComposite = spd2;
                }
            }
            else
            {
                // Super I/O / EC thermistor fallback & stacked SODIMM gradient model
                int baseDdr = (ecDdrTemp >= 15 && ecDdrTemp <= 95)
                    ? (int)ecDdrTemp
                    : ((ecAmbientTemp >= 15 && ecAmbientTemp <= 95)
                        ? (int)Math.Min(95, Math.Max(25, Math.Round(ecAmbientTemp + 6.0 + (mem.IsAvailable ? mem.LoadPct : 35) * 0.04)))
                        : (cpuTemp > 0 ? (int)Math.Max(25, Math.Min(80, Math.Round(35.0 + (cpuTemp - 35.0) * 0.28))) : 0));

                if (baseDdr > 0)
                {
                    if (ddrCount == 2)
                    {
                        // In mini PCs, SODIMMs are physically stacked:
                        // Slot 1 (Top stick) runs hotter due to restricted dissipation & radiant heat
                        // Slot 0 (Bottom stick) runs ~6°C cooler (as verified in HWiNFO64: 41.5°C vs 47.8°C)
                        ddrTemp2 = baseDdr;
                        ddrTemp1 = Math.Max(25, baseDdr - 6);
                        ddrComposite = baseDdr;
                    }
                    else
                    {
                        ddrTemp1 = baseDdr;
                        ddrTemp2 = 0;
                        ddrComposite = baseDdr;
                    }
                }
            }

            FanStatus st = new FanStatus
            {
                CpuTemp = cpuTemp,
                CpuLoadPct = SystemStorageMemorySensor.GetCpuUsage(),
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
                MemClockMhz = memClk,
                GpuHotspotTemp = hotspot,
                NpuClockMhz = npuClk,
                NpuActivityPct = npuAct,
                RtssFps = rtss.IsAvailable ? rtss.Fps : -1,
                RtssRunning = rtss.IsRtssRunning,
                RtssFrametimeMs = rtss.IsAvailable ? rtss.FrametimeMs : 0.0,
                RtssAvgFps = rtss.IsAvailable ? rtss.AvgFps : 0,
                RtssMinFps = rtss.IsAvailable ? rtss.MinFps : 0,
                RtssMaxFps = rtss.IsAvailable ? rtss.MaxFps : 0,
                RtssP1LowFps = rtss.IsAvailable ? rtss.P1LowFps : 0,
                RtssP01LowFps = rtss.IsAvailable ? rtss.P01LowFps : 0,
                RtssProcessName = !string.IsNullOrEmpty(rtss.ProcessName) ? rtss.ProcessName : null,
                PowerSource = pwrSrc,
                Fan1Rpm = fan1Rpm,
                Fan2Rpm = fan2Rpm,
                Mode = modeStr,
                ModeCode = modeCode,
                Fan1Pct = (f1Ctrl != 0xFF) ? (f1Ctrl & 0x7F) : 0,
                Fan2Pct = (f2Ctrl != 0xFF) ? (f2Ctrl & 0x7F) : 0,
                Fan1Manual = (f1Ctrl != 0xFF && (f1Ctrl & 0x80) != 0) ? 1 : 0,
                Fan2Manual = (f2Ctrl != 0xFF && (f2Ctrl & 0x80) != 0) ? 1 : 0,
                VrmTemp = (ecVrmTemp >= 20 && ecVrmTemp <= 115) ? (int)ecVrmTemp : (cpuTemp > 0 ? (int)Math.Round(cpuTemp * 0.98) : 0),
                IsVrmEstimated = !(ecVrmTemp >= 20 && ecVrmTemp <= 115),
                AmbientTemp = (ecAmbientTemp >= 15 && ecAmbientTemp <= 95)
                    ? (int)ecAmbientTemp
                    : (cpuTemp > 0 ? (int)Math.Max(25, Math.Min(65, Math.Round(32.0 + (cpuTemp - 35.0) * 0.22))) : 0),
                IsAmbientEstimated = !(ecAmbientTemp >= 15 && ecAmbientTemp <= 95),
                DdrStickCount = ddrCount,
                DdrTemp1 = ddrTemp1,
                DdrTemp2 = ddrTemp2,
                DdrTemp = ddrComposite,
                IsDdrEstimated = !(spd1Ok || (ecDdrTemp >= 15 && ecDdrTemp <= 95)),
                IsApuPowerEstimated = (pwrSrc != null && pwrSrc.IndexOf("Estimated", StringComparison.OrdinalIgnoreCase) >= 0),
                DcInputVolts = (ecDcAdc > 0) ? Math.Round(ecDcAdc * 0.1, 1) : 0.0,
                IsThermalThrottling = (cpuTemp >= 95),
                IsProchotAsserted = (cpuTemp >= 100),
                VddTdcCurrentAmps = smu.VddTdcCurrentAmps,
                VddEdcCurrentAmps = smu.VddEdcCurrentAmps,
                SocTdcCurrentAmps = smu.SocTdcCurrentAmps,
                SocEdcCurrentAmps = smu.SocEdcCurrentAmps,
                VddTdcLimitAmps = smu.VddTdcLimitAmps,
                VddEdcLimitAmps = smu.VddEdcLimitAmps,
                FabricClockMhz = smu.FabricClockMhz,
                IsFabricSynced = smu.IsFabricSynced,
                SmuTelemetrySource = smu.TelemetrySource,
                NvmeTemp = nvme.IsAvailable ? nvme.CompositeTemp : 0,
                NvmeHealthPct = nvme.IsAvailable ? nvme.HealthPct : 0,
                NvmeModel = nvme.IsAvailable ? nvme.Model : null,
                NvmeCritWarning = nvme.IsAvailable ? nvme.CriticalWarning : (byte)0,
                NvmeAvailableSpare = nvme.IsAvailable ? nvme.AvailableSpare : (byte)0,
                NvmeWearPct = nvme.IsAvailable ? nvme.PercentageUsed : (byte)0,
                NvmeDriveCount = nvme.IsAvailable ? nvme.DriveCount : 0,
                NvmeReadMBs = nvme.IsAvailable ? nvme.TotalReadRateMBs : 0.0,
                NvmeWriteMBs = nvme.IsAvailable ? nvme.TotalWriteRateMBs : 0.0,
                NvmeQueueDepth = nvme.IsAvailable ? nvme.QueueDepth : 0,
                Nvme2Temp = nvme.IsAvailable ? nvme.Drive2Temp : 0,
                Nvme2HealthPct = nvme.IsAvailable ? nvme.Drive2HealthPct : 0,
                Nvme2Model = nvme.IsAvailable ? nvme.Drive2Model : null,
                Nvme2ReadMBs = nvme.IsAvailable ? nvme.Drive2ReadRateMBs : 0.0,
                Nvme2WriteMBs = nvme.IsAvailable ? nvme.Drive2WriteRateMBs : 0.0,
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
                RadeonSam = rad.SmartAccessMemory,
                RadeonFsr = rad.Fsr
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
                // Super I/O chips control PWM outputs directly; ACPI EC mode registers do not apply.
                // Fail-closed: return false to prevent pretending a hardware mode switch took place.
                return false;
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
            if (fanId == 2 && Profile != null && !Profile.HasDualFans) return true; // Single-fan device: secondary fan write is a no-op success
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
                    ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
                    ushort dataPort = (ushort)(SuperIoBaseAddr + 6);

                    // Ensure PWM output enabled in MainCtrl (0x13)
                    byte mainCtrl = ReadSuperIoHwReg(0x13);
                    if ((mainCtrl & 0x07) != 0x07)
                    {
                        WritePortByte(addrPort, 0x13);
                        WritePortByte(dataPort, (byte)(mainCtrl | 0x07));
                    }

                    // Ensure 16-bit TachMode (0x0C)
                    byte modeReg = ReadSuperIoHwReg(0x0C);
                    if ((modeReg & 0x3F) != 0x3F)
                    {
                        WritePortByte(addrPort, 0x0C);
                        WritePortByte(dataPort, (byte)(modeReg | 0x3F));
                    }

                    // Direct Super I/O IT8613 blower duty cycle:
                    // 0   (0x00) = 0% (Rotors stopped / Zero-RPM quiet idle)
                    // 255 (0xFF) = 100% (Full throttle maximum blower speed)
                    byte duty = (pct <= 0) ? (byte)0 : (byte)Math.Max(1, Math.Min(255, (pct * 255) / 100));
                    byte duty7 = (byte)((duty * 127) / 255); // 7-bit duty (0 = 0% stop, 127 = 100% full speed)
                    if (fanId == 1)
                    {
                        // Set manual mode (Bit 7 = 0) and 7-bit duty (Bits 6:0) on Channel 1 (0x15) and Channel 2 (0x16)
                        WritePortByte(addrPort, 0x15);
                        WritePortByte(dataPort, duty7);
                        WritePortByte(addrPort, 0x16);
                        WritePortByte(dataPort, duty7);

                        // Primary 8-bit extended PWM duty registers (0x63 = Fan 1, sync 0x6B)
                        bool w63 = WriteSuperIoHwReg(0x63, duty);
                        bool w6b = WriteSuperIoHwReg(0x6B, duty);
                        if (!w63 && !w6b) sioOk = false;
                    }
                    else
                    {
                        // Set manual mode (Bit 7 = 0) and 7-bit duty (Bits 6:0) on Channel 3 (0x17)
                        WritePortByte(addrPort, 0x17);
                        WritePortByte(dataPort, duty7);

                        // Primary 8-bit extended PWM duty register (0x73 = Fan 3 / Power)
                        if (!WriteSuperIoHwReg(0x73, duty))
                        {
                            // Some BIOS revisions use 7-bit duty directly in 0x17
                            WritePortByte(addrPort, 0x17);
                            WritePortByte(dataPort, duty7);
                            byte rb17 = ReadSuperIoHwReg(0x17);
                            if (rb17 != duty7 && rb17 != (byte)(duty7 | 0x80))
                            {
                                sioOk = false;
                            }
                        }
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
            ushort addrPort = (ushort)(SuperIoBaseAddr + 5);
            ushort dataPort = (ushort)(SuperIoBaseAddr + 6);

            // Re-engage Safe Super I/O Fallback (Extended PWM mode matching factory BIOS: Reg 0x15..0x17 = 0x00)
            // Bit 7: 0 = Extended PWM mode (Reg 0x63, 0x6B, 0x73 govern duty cycle).
            // We deliberately keep Bit 7 = 0 because setting Bit 7 = 1 (IT87 SmartGuardian Auto) causes
            // the hardware to ramp fans to 100% full blast if thermal diode clocks gate during sleep or if
            // temperature inputs are unconfigured in motherboard firmware!
            // In factory BIOS state on TS-12C / KTHIN PRO, Reg 0x15..0x17 are 0x00.
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

        public bool SetAuto()
        {
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            try { EnsureOpen(); } catch { return false; }
            // Escalating backoff retry loop (5 attempts: 50ms, 100ms, 170ms, 260ms, 370ms)
            // to ensure supervisor failsafe can acquire mutex even under temporary contention
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int waitTimeout = 50 + (attempt * 70);
                using (var lockScope = EcBusMutex.AcquireEc(waitTimeout))
                {
                    if (!lockScope.Acquired)
                    {
                        Thread.Sleep(15);
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

