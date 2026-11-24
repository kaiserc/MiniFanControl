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
        private static readonly List<int> _fan1FilterWindow = new List<int>();

        private static readonly List<int> _fan2FilterWindow = new List<int>();

        private static readonly object _filterLock = new object();

        private static int _lastValidFan1Rpm = 0;
        private static int _fan1ZeroStreak = 0;
        private static int _fan1SpikeStreak = 0;
        private static int _lastValidFan2Rpm = 0;
        private static int _fan2ZeroStreak = 0;
        private static int _fan2SpikeStreak = 0;

        public static void ResetTachometerFilterState()
        {
            lock (_filterLock)
            {
                _lastValidFan1Rpm = 0;
                _fan1ZeroStreak = 0;
                _fan1SpikeStreak = 0;
                _lastValidFan2Rpm = 0;
                _fan2ZeroStreak = 0;
                _fan2SpikeStreak = 0;
                _fan1FilterWindow.Clear();
                _fan2FilterWindow.Clear();
            }
        }

        /// <summary>
        /// Stabilizes EC/Super-I/O tachometer readings against two common hardware glitches:
        /// 1) Implausible instant upward RPM spikes (> +2000 RPM in a single poll, e.g. 965 -> 5023 RPM EC spin-up glitch):
        ///    holds last valid RPM for up to 20 polls (~10s at 2 Hz) until the counter settles back into a plausible range.
        /// 2) Transient zero-glitches (e.g. high-RPM counter rollover or ACPI EC register contention):
        ///    holds last valid RPM for up to 3 polls (~1.5s) at normal speeds or 8 polls (~4s) at high load
        ///    (PWM >= 50% or >= 1500 RPM), while reporting genuine stalls or intentional Zero-RPM (0% duty) immediately.
        /// </summary>
        public static int DebounceTachometerZero(int fanIndex, int rawRpm, bool zeroDutyCommanded, int commandedPct = -1)
        {
            // Never mask genuine emergency / full-speed spin-ups when >= 95% PWM is commanded
            bool bypassSpikeFilter = (commandedPct >= 95);
            lock (_filterLock)
            {
                if (fanIndex == 1)
                {
                    if (rawRpm > 0)
                    {
                        _fan1ZeroStreak = 0;
                        if (!bypassSpikeFilter && _lastValidFan1Rpm >= 400 && (rawRpm - _lastValidFan1Rpm) > 2000 && _fan1SpikeStreak < 20)
                        {
                            _fan1SpikeStreak++;
                            return _lastValidFan1Rpm;
                        }
                        _fan1SpikeStreak = 0;
                        _lastValidFan1Rpm = rawRpm;
                        return rawRpm;
                    }
                    _fan1SpikeStreak = 0;
                    _fan1ZeroStreak++;
                    int maxHoldPolls = (commandedPct >= 50 || _lastValidFan1Rpm >= 1500) ? 8 : 3;
                    if (!zeroDutyCommanded && _lastValidFan1Rpm >= 600 && _fan1ZeroStreak <= maxHoldPolls)
                    {
                        return _lastValidFan1Rpm;
                    }
                    _lastValidFan1Rpm = 0;
                    return 0;
                }
                else
                {
                    if (rawRpm > 0)
                    {
                        _fan2ZeroStreak = 0;
                        if (!bypassSpikeFilter && _lastValidFan2Rpm >= 400 && (rawRpm - _lastValidFan2Rpm) > 2000 && _fan2SpikeStreak < 20)
                        {
                            _fan2SpikeStreak++;
                            return _lastValidFan2Rpm;
                        }
                        _fan2SpikeStreak = 0;
                        _lastValidFan2Rpm = rawRpm;
                        return rawRpm;
                    }
                    _fan2SpikeStreak = 0;
                    _fan2ZeroStreak++;
                    int maxHoldPolls = (commandedPct >= 50 || _lastValidFan2Rpm >= 1500) ? 8 : 3;
                    if (!zeroDutyCommanded && _lastValidFan2Rpm >= 600 && _fan2ZeroStreak <= maxHoldPolls)
                    {
                        return _lastValidFan2Rpm;
                    }
                    _lastValidFan2Rpm = 0;
                    return 0;
                }
            }
        }

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


        public static bool IsPortWhitelisted(ushort port, bool isWrite)
        {
            return NativeInpOut.IsPortWhitelisted(port, isWrite);
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

            if (dataPort == 0x62 && cmdPort == 0x66 &&
                NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO &&
                PawnIoDriver.HasAcpiEcModule)
            {
                return PawnIoDriver.TryReadAcpiEcByte(reg, out val);
            }

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

            if (dataPort == 0x62 && cmdPort == 0x66 &&
                NativeInpOut.ActiveDriver == HardwareDriverEngine.PawnIO &&
                PawnIoDriver.HasAcpiEcModule)
            {
                return PawnIoDriver.TryWriteAcpiEcByte(reg, val);
            }

            EcDrainObf(dataPort, cmdPort);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(cmdPort, EC_CMD_WRITE);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(dataPort, reg);

            if (!EcWaitIbfClear(cmdPort)) return false;

            WritePortByte(dataPort, val);

            return EcWaitIbfClear(cmdPort);

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


        public static int DecodeTachometerRpm(byte hi, byte lo)

        {

            if (hi == 0xFF && lo == 0xFF) return 0;

            int rpm = (hi << 8) | lo;

            // Reject motor-stop sentinel (8000 / 0x1F40), bus float (65535 / 0xFFFF), and counter rollover spikes (>= 7500 RPM)
            if (rpm <= 0 || rpm >= 7500) return 0;

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

    }
}
