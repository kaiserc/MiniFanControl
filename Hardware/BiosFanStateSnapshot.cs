using System;

namespace MiniFanControl
{
    /// <summary>
    /// Captures untouched motherboard BIOS fan registers ONCE before MiniFanControl's first PWM/mode write
    /// so Reset Auto and safe exit can faithfully restore a user's BIOS custom SmartGuardian curve (IT8613E)
    /// or initial EC thermal mode (Silent / Balanced / Performance) without clobbering BIOS defaults.
    /// </summary>
    public sealed class BiosFanStateSnapshot
    {
        public bool HasCapturedSuperIo { get; private set; }
        public bool HasSmartGuardianCurve { get; private set; }
        public byte SioReg15 { get; private set; }
        public byte SioReg16 { get; private set; }
        public byte SioReg17 { get; private set; }
        public byte SioReg63 { get; private set; }
        public byte SioReg6B { get; private set; }
        public byte SioReg73 { get; private set; }

        public bool HasCapturedEcMode { get; private set; }
        public int InitialEcMode { get; private set; }

        public BiosFanStateSnapshot()
        {
            InitialEcMode = -1;
        }

        public void TryCaptureSuperIo(byte r15, byte r16, byte r17, byte r63, byte r6B, byte r73)
        {
            if (HasCapturedSuperIo) return;

            // Reject bus-float reads (all 0xFF) so disconnected hardware never looks like SmartGuardian
            if (r15 == 0xFF && r16 == 0xFF && r17 == 0xFF) return;

            SioReg15 = r15;
            SioReg16 = r16;
            SioReg17 = r17;
            SioReg63 = r63;
            SioReg6B = r6B;
            SioReg73 = r73;
            HasSmartGuardianCurve =
                (r15 != 0xFF && (r15 & 0x80) != 0) ||
                (r16 != 0xFF && (r16 & 0x80) != 0) ||
                (r17 != 0xFF && (r17 & 0x80) != 0);
            HasCapturedSuperIo = true;
        }

        public void TryCaptureEcMode(byte modeStatus)
        {
            if (HasCapturedEcMode) return;
            if (modeStatus <= 2)
            {
                InitialEcMode = (int)modeStatus;
                HasCapturedEcMode = true;
            }
        }

        public byte ResolveEcRestoreModeCommand()
        {
            if (HasCapturedEcMode && InitialEcMode >= 0 && InitialEcMode <= 2)
                return HardwareBackend.EncodeModeCommand(InitialEcMode);
            return 0x80;
        }
    }
}
