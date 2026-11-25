using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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

    public partial class HardwareBackend : IHardwareBackend, IDisposable

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
        public bool IsSimulatedMode { get { return _isSimulatedMode; } }
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
                bool profileWritable = (hw.Profile != null && !hw.Profile.IsReadOnly);
                // Safe Read-Only Mode: never emit Super I/O LPC config-mode sequences on unverified/read-only hardware
                if (profileWritable && NativeInpOut.IsDriverOpen())
                {
                    ushort cId, hBase;
                    bool allowWrites = (hw.Profile.Id == "ryzen-hx370-ec");
                    if (ProbeSuperIo(0x2E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase, allowWrites))
                    {
                        hw.SuperIoChipId = cId;
                        hw.SuperIoBaseAddr = hBase;
                        hw.AccessMethod = EcAccessMethod.SuperIo2E;
                    }
                    else if (ProbeSuperIo(0x4E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase, allowWrites))
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
        private readonly NvmlGpuSensor _nvmlSensor = new NvmlGpuSensor();

        public HardwareBackend()

        {

        }

        public HardwareBackend(DeviceProfile profile)

        {

            if (profile != null) Profile = profile;

        }


        public bool IsSuperIoControlled
        {
            get
            {
                return SuperIoBaseAddr != 0 || AccessMethod == EcAccessMethod.SuperIo2E || AccessMethod == EcAccessMethod.SuperIo4E;
            }
        }

        /// <summary>
        /// Returns the lowest manual PWM percentage supported without reverting to BIOS Auto.
        /// Super I/O (IT8613E/IT87xx) supports true 0% manual PWM (duty=0x00, Bit 7=0);
        /// ACPI EC (IT5570E) uses 1% (0x81) because 0x00 restores BIOS Auto.
        /// </summary>
        public int GetManualStopPct()
        {
            try { EnsureOpen(); } catch { }
            return IsSuperIoControlled ? 0 : 1;
        }

        public void ResetDriverInitialization()
        {
            if (_isSimulatedMode) return;
            if (_linAddr != IntPtr.Zero)
            {
                try { NativeInpOut.UnmapPhysicalMemory(_physHandle, _linAddr); } catch { }
                _linAddr = IntPtr.Zero;
                _physHandle = IntPtr.Zero;
            }
            SuperIoBaseAddr = 0;
            SuperIoChipId = 0;
            AccessMethod = EcAccessMethod.Unknown;
            _isInitialized = false;
            ResetTachometerFilterState();
            ResetSuperIoChannelLatch();
            SetSuperIoPolarityOverride(null);
        }

        public void ReinitializeHardwareAfterResume()
        {
            if (_isSimulatedMode) return;
            if (_linAddr != IntPtr.Zero)
            {
                try { NativeInpOut.UnmapPhysicalMemory(_physHandle, _linAddr); } catch { }
                _linAddr = IntPtr.Zero;
                _physHandle = IntPtr.Zero;
            }
            SuperIoBaseAddr = 0;
            SuperIoChipId = 0;
            AccessMethod = EcAccessMethod.Unknown;
            _isInitialized = false;
            ResetTachometerFilterState();
            ResetSuperIoChannelLatch();
            SetSuperIoPolarityOverride(null);
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

            try

            {

                if (!NativeInpOut.IsDriverOpen())

                {

                    string integrityNote = !string.IsNullOrEmpty(NativeInpOut.LastDriverIntegrityError)
                        ? ("\nIntegrity check: " + NativeInpOut.LastDriverIntegrityError)
                        : "";
                    throw new InvalidOperationException(
                        string.Format("No supported hardware access driver could be opened ({0}). " +
                        "Install HVCI-safe PawnIO (https://pawnio.eu) or place inpoutx64.dll / WinRing0x64.dll in the application folder and run as Administrator " +
                        "(or enable Demo Mode).{1}",
                        NativeInpOut.ActiveDriverName, integrityNote));

                }

                // 1. Probe LPC Super I/O on Port 0x2E / 0x4E (only for verified writable profiles;
                //    Safe Read-Only Mode skips Super I/O LPC config-mode write sequences entirely)

                bool allowWrites = (Profile != null && !Profile.IsReadOnly);

                if (allowWrites)

                {

                    ushort cId, hBase;

                    if (ProbeSuperIo(0x2E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase, allowWrites))

                    {

                        SuperIoChipId = cId;

                        SuperIoBaseAddr = hBase;

                        AccessMethod = EcAccessMethod.SuperIo2E;

                        _isInitialized = true;

                        DumpSuperIoDiagnostic(GetWritableDiagnosticPath("sio-diag.txt"));

                        return;

                    }

                    // 2. Probe LPC Super I/O on Port 0x4E

                    if (ProbeSuperIo(0x4E, out cId, out hBase, allowWrites) && VerifyIt87HwBase(hBase, allowWrites))

                    {

                        SuperIoChipId = cId;

                        SuperIoBaseAddr = hBase;

                        AccessMethod = EcAccessMethod.SuperIo4E;

                        _isInitialized = true;

                        DumpSuperIoDiagnostic(GetWritableDiagnosticPath("sio-diag.txt"));

                        return;

                    }

                    // 3. Fallback candidate IT87 Hardware Monitor base addresses (0x58=0x90):

                    ushort[] candidateBases = new ushort[] { 0x0A30, 0x0A00, 0x0A10, 0x0A20, 0x0290, 0x02A0 };

                    foreach (ushort cand in candidateBases)

                    {

                        if (VerifyIt87HwBase(cand, allowWrites))

                        {

                            SuperIoChipId = 0x8613;

                            SuperIoBaseAddr = cand;

                            AccessMethod = EcAccessMethod.SuperIo2E;

                            _isInitialized = true;

                            DumpSuperIoDiagnostic(GetWritableDiagnosticPath("sio-diag.txt"));

                            return;

                        }

                    }

                }

                // 3. Probe standard ACPI EC Port I/O (0x62 / 0x66) or PawnIO LpcACPIEC module directly

                bool pawnAcpiEcReady = (NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO && PawnIoDriver.HasAcpiEcModule);

                byte sc66 = pawnAcpiEcReady ? (byte)0x00 : ReadPortByte(0x66);

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
                uint ecSize = Profile != null && Profile.EcSize > 0 ? Profile.EcSize : EC_SIZE;
                NativeInpOut.RegisterAllowedEcMmioWindow(baseAddr, ecSize);
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


        private FanStatus _lastStatusCache;

        public bool AllowEtwAutoStart = true;

        /// <summary>
        /// When enabled (used by HeadlessDaemon / Windows Service), skips heavy GUI-only telemetry
        /// (ETW PresentMon, RTSS FPS, Radeon Settings, SMU PM tables, DDR5 SMBus polling, and
        /// unnecessary ADL/NVML native DLL loads when EC/SMN temperatures are already available)
        /// to keep background memory usage minimal.
        /// </summary>
        public bool LeanServiceMode = false;

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
                            int winPowerMode = _isSimulatedMode ? -1 : WindowsPowerMode.GetActiveModeCode();
                            modeVal = (winPowerMode >= 0 && winPowerMode <= 2) ? (byte)winPowerMode : (byte)0;
                            byte ext1 = ReadSuperIoHwReg(0x6B); // APU Fan extended duty (0x6B)
                            byte ext2 = ReadSuperIoHwReg(0x73); // Sys Fan extended duty (0x73)

                            byte reg16 = ReadSuperIoHwReg(0x16); // APU Fan manual mode register (0x16)
                            byte reg17 = ReadSuperIoHwReg(0x17); // Sys Fan manual mode register (0x17)
                            bool f1Auto = (reg16 & 0x80) != 0;
                            bool f2Auto = (reg17 & 0x80) != 0;

                            // Dynamically match IT8613E Reg 0x14 Bit 7 PWM polarity (Direct Active-High vs Inverted Active-Low)
                            bool sioInverted = IsSuperIoPwmInverted();
                            int f1Duty = DecodeSuperIoDutyPct(ext1, sioInverted);
                            int f2Duty = DecodeSuperIoDutyPct(ext2, sioInverted);
                            f1Ctrl = (byte)(f1Auto ? 0x00 : (0x80 | (f1Duty & 0x7F)));
                            f2Ctrl = (byte)(f2Auto ? 0x00 : (0x80 | (f2Duty & 0x7F)));
                        }
                        else
                        {
                            // Atomic double-read latching on 16-bit EC tachometer registers to prevent MSB/LSB rollover tearing
                            byte f1h1 = ReadRegInternal(p.RegFan1Hi);
                            byte f1l1 = ReadRegInternal(p.RegFan1Lo);
                            byte f1h2 = ReadRegInternal(p.RegFan1Hi);
                            f1h = f1h2;
                            f1l = (f1h1 == f1h2) ? f1l1 : ReadRegInternal(p.RegFan1Lo);

                            byte f2h1 = ReadRegInternal(p.RegFan2Hi);
                            byte f2l1 = ReadRegInternal(p.RegFan2Lo);
                            byte f2h2 = ReadRegInternal(p.RegFan2Hi);
                            f2h = f2h2;
                            f2l = (f2h1 == f2h2) ? f2l1 : ReadRegInternal(p.RegFan2Lo);

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

            int adlTemp = 0, adlCpuTemp = 0, adlPowerWatts = 0, cpuPwr = 0, gfxPwr = 0, socPwr = 0, pwrLim = 0, cpuClk = 0, gfxClk = 0, gfxAct = 0, gfxVolt = 0, memClk = 0, hotspot = 0, npuClk = 0, npuAct = 0;
            int adlFclk = 0, adlGfxCurr = 0, adlSocCurr = 0;
            string pwrSrc = "";
            NvmlTelemetry nvml = default(NvmlTelemetry);

            bool allowActiveSilicon = CanRunActiveSiliconProbes(Profile, _isSimulatedMode);
            int preSmnTctl = (LeanServiceMode && IsAmdCpuPlatform() && NativeInpOut.ActiveDriver != HardwareDriverEngine.None)
                ? ReadAmdCpuTempTctl()
                : 0;
            bool skipHeavyGpuDlls = LeanServiceMode && (preSmnTctl >= 15 && preSmnTctl <= 115 || (ecCpuTemp >= 15 && ecCpuTemp <= 115));

            if (!skipHeavyGpuDlls)
            {
                _adlSensor.ReadTelemetryDetailed(out adlTemp, out adlCpuTemp, out adlPowerWatts, out cpuPwr, out gfxPwr, out socPwr,
                    out pwrLim, out cpuClk, out gfxClk, out gfxAct, out gfxVolt, out pwrSrc,
                    out memClk, out hotspot, out npuClk, out npuAct, out adlFclk, out adlGfxCurr, out adlSocCurr);

                // Multi-vendor desktop fallback: read WHQL NVIDIA NVML (nvml.dll) when AMD ADL2 reports no GPU
                if (adlTemp <= 0 && adlPowerWatts <= 0 && gfxClk <= 0)
                {
                    nvml = _nvmlSensor.ReadTelemetry();
                    if (nvml.IsAvailable)
                    {
                        if (nvml.GpuTempC > 0) adlTemp = nvml.GpuTempC;
                        if (nvml.PowerWatts > 0)
                        {
                            adlPowerWatts = nvml.PowerWatts;
                            gfxPwr = nvml.PowerWatts;
                            pwrSrc = "NVIDIA NVML (nvml.dll)";
                        }
                        if (nvml.PowerLimitWatts > 0) pwrLim = nvml.PowerLimitWatts;
                        if (nvml.GraphicsClockMhz > 0) gfxClk = nvml.GraphicsClockMhz;
                        if (nvml.MemoryClockMhz > 0) memClk = nvml.MemoryClockMhz;
                        if (nvml.GpuLoadPct >= 0) gfxAct = nvml.GpuLoadPct;
                    }
                }
            }

            // High-precision silicon junction resolution (matches HWiNFO64 / LibreHardwareMonitor / Ryzen Master):
            // Tier 1: AMD SMN silicon register 0x00059800 (Tctl/Tdie) - exact silicon die junction temperature
            // Tier 2: AMD ADL2 PMLOG (via atiadlxx.dll) - official Radeon driver on-die CPU junction temperature
            // Tier 3: Super I/O CPU Socket Thermistor (Register 0x2B on IT8613 = HWiNFO64 Temperature 3)
            // In read-only desktop mode, prefer WHQL ADL2 first so PCI config space isn't touched unnecessarily.
            int cpuTemp = 0;
            if (!allowActiveSilicon && adlCpuTemp >= 15 && adlCpuTemp <= 115)
            {
                cpuTemp = adlCpuTemp;
            }
            else
            {
                int smnTctl = (preSmnTctl >= 15 && preSmnTctl <= 115)
                    ? preSmnTctl
                    : ((IsAmdCpuPlatform() && NativeInpOut.ActiveDriver != HardwareDriverEngine.None) ? ReadAmdCpuTempTctl() : 0);
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

            int rawFan1Rpm = (resolvedSio1 > 0) ? resolvedSio1 : f1Ec;
            int rawFan2Rpm = (resolvedSio2 > 0) ? resolvedSio2 : f2Ec;
            bool f1ManualActive = (f1Ctrl != 0xFF && (f1Ctrl & 0x80) != 0);
            bool f2ManualActive = (f2Ctrl != 0xFF && (f2Ctrl & 0x80) != 0);
            int f1CmdPct = f1ManualActive ? (f1Ctrl & 0x7F) : -1;
            int f2CmdPct = f2ManualActive ? (f2Ctrl & 0x7F) : -1;
            bool f1ZeroCmd = f1ManualActive && (f1CmdPct == 0);
            bool f2ZeroCmd = f2ManualActive && (f2CmdPct == 0);
            int fan1Rpm = DebounceTachometerZero(1, rawFan1Rpm, f1ZeroCmd, f1CmdPct);
            int fan2Rpm = DebounceTachometerZero(2, rawFan2Rpm, f2ZeroCmd, f2CmdPct);

            int apuPower = adlPowerWatts > 0

                ? adlPowerWatts

                : EstimateApuPowerWatts(cpuTemp, gpuTemp, modeCode);

            if (adlPowerWatts <= 0)

            {
                pwrSrc = "EC Thermal-TDP Model (CPU/iGPU Temp + Active BIOS Mode)";
            }

            NvmeTelemetry nvme = default(NvmeTelemetry);
            MemoryTelemetry mem = default(MemoryTelemetry);
            AmdSmuTelemetry smu = default(AmdSmuTelemetry);
            RtssTelemetry rtss = default(RtssTelemetry);
            EtwLatencyTelemetry etw = default(EtwLatencyTelemetry);
            RadeonSettingsTelemetry rad = default(RadeonSettingsTelemetry);
            string activeGameProc = null;

            if (!LeanServiceMode)
            {
                SystemStorageMemorySensor.Sample(out nvme, out mem);
                smu = AmdSmuSiliconSensor.Sample(
                    cpuPwr > 0 ? cpuPwr : (apuPower > 0 ? apuPower : 15),
                    cpuClk, memClk, gfxVolt, adlFclk, adlGfxCurr, adlSocCurr,
                    false, allowActiveSilicon);
                rtss = FpsMonitor.GetTelemetry();
                etw = EtwLatencyMonitor.GetTelemetry(AllowEtwAutoStart && !_isSimulatedMode);
                activeGameProc = !string.IsNullOrEmpty(rtss.ProcessName) ? rtss.ProcessName : etw.ProcessName;
                rad = RadeonSettingsSensor.Sample(activeGameProc);
            }
            else if (Profile == null || Profile.HasDualFans)
            {
                // Keep lightweight NVMe temp sampling on dual-fan boards so Sys Fan reacts to NVMe >= 65C
                SystemStorageMemorySensor.Sample(out nvme, out mem);
            }

            // DDR5 Dual Stacked SO-DIMM Thermal Resolution (skipped in LeanServiceMode)
            int ddrCount = LeanServiceMode ? 0 : GetDdrStickCount(mem.TotalGb, allowActiveSilicon);
            int ddrTemp1 = 0;
            int ddrTemp2 = 0;
            int ddrComposite = 0;

            ushort smbusBase = (!LeanServiceMode && allowActiveSilicon) ? GetAmdSmbusBase() : (ushort)0;
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

            string resolvedGpuName = !string.IsNullOrEmpty(nvml.GpuName) ? nvml.GpuName : rad.GpuDesc;
            bool isDiscreteGpu = nvml.IsAvailable || RadeonSettingsSensor.IsDiscreteGpuDescription(resolvedGpuName) ||
                (Profile != null && Profile.IsReadOnly && (rad.SmartAccessMemory || adlPowerWatts > 95 || gfxClk > 2950));

            FanStatus st = new FanStatus
            {
                CpuTemp = cpuTemp,
                CpuLoadPct = LeanServiceMode ? 0 : SystemStorageMemorySensor.GetCpuUsage(),
                GpuTemp = gpuTemp,
                GpuName = resolvedGpuName,
                IsDiscreteGpu = isDiscreteGpu,
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
                RtssFps = rtss.IsAvailable ? rtss.Fps : (etw.HasLiveFrames && etw.Fps > 0 ? etw.Fps : -1),
                RtssRunning = rtss.IsRtssRunning,
                RtssFrametimeMs = rtss.IsAvailable ? rtss.FrametimeMs : (etw.HasLiveFrames ? etw.FrametimeMs : 0.0),
                RtssAvgFps = rtss.IsAvailable ? rtss.AvgFps : (etw.HasLiveFrames ? etw.AvgFps : 0),
                RtssMinFps = rtss.IsAvailable ? rtss.MinFps : 0,
                RtssMaxFps = rtss.IsAvailable ? rtss.MaxFps : 0,
                RtssP1LowFps = rtss.IsAvailable ? rtss.P1LowFps : (etw.HasLiveFrames ? etw.P1LowFps : 0),
                RtssP01LowFps = rtss.IsAvailable ? rtss.P01LowFps : (etw.HasLiveFrames ? etw.P01LowFps : 0),
                RtssProcessName = !string.IsNullOrEmpty(activeGameProc) ? activeGameProc : null,
                DisplayLatencyMs = etw.HasLiveFrames ? etw.DisplayLatencyMs : 0.0,
                RenderLatencyMs = etw.HasLiveFrames ? etw.RenderLatencyMs : 0.0,
                PresentFlipMode = etw.HasLiveFrames ? (etw.FlipMode ?? "") : "",
                EtwLatencyActive = etw.IsEtwActive,
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
                RadeonAdrenalinInstalled = rad.AdrenalinInstalled,
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
                RadeonFsr = rad.Fsr,
                RadeonFrtc = rad.Frtc,
                RadeonFrtcFps = rad.FrtcFps
            };

            _lastStatusCache = st;

            return st;

        }


        public bool SetMode(int code)
        {
            if (code < 0 || code > 2) return false;
            if (Profile != null && Profile.IsReadOnly) return false; // Fail-closed: read-only unverified hardware protection
            try { EnsureOpen(); } catch { return false; }
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

                    // Dynamic Super I/O IT8613E blower duty cycle matching hardware Reg 0x14 Bit 7 (PWM_POL):
                    // - Active-High (Bit 7 = 1, e.g. 0xC7): 0x00 = 0% stop, 0xFF = 100% full speed
                    // - Active-Low  (Bit 7 = 0, sleep/reset default): 0xFF = 0% stop, 0x00 = 100% full speed
                    byte duty = EncodeSuperIoDuty(pct, IsSuperIoPwmInverted());
                    byte duty7 = (byte)((duty * 127) / 255); // 7-bit synced duty (Bit 7 = 0 manual mode)
                    if (fanId == 1)
                    {
                        // Set manual mode (Bit 7 = 0) and 7-bit duty (Bits 6:0) on Channel 1 (0x15) and Channel 2 (0x16)
                        WritePortByte(addrPort, 0x15);
                        WritePortByte(dataPort, duty7);
                        WritePortByte(addrPort, 0x16);
                        WritePortByte(dataPort, duty7);
                        if (_isSimulatedMode && !SimulateSuperIoWriteFailure)
                        {
                            _simulatedSioRegs[0x15] = duty7;
                            _simulatedSioRegs[0x16] = duty7;
                        }

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
                        if (_isSimulatedMode && !SimulateSuperIoWriteFailure)
                        {
                            _simulatedSioRegs[0x17] = duty7;
                        }

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


        public bool SetAuto()
        {
            return SetAuto(false);
        }

        public bool SetAuto(bool thermalLoadContext)
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
                        bool sioOk = ApplySafeSuperIoFallback(thermalLoadContext);
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

        


        public void Dispose()

        {

            _adlSensor.Dispose();
            _nvmlSensor.Dispose();

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

