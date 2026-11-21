using System;
using System.Runtime.InteropServices;

namespace MiniFanControl
{
    /// <summary>
    /// Native Windows 10/11 Power Mode Overlay API (powrprof.dll) — maps 1:1 to Windows Settings -> Power mode:
    /// - Mode 2 (Silent):      Best Power Efficiency (961cc777-2547-4f9d-8174-7d86181b8a7a)
    /// - Mode 0 (Balanced):    Balanced              (00000000-0000-0000-0000-000000000000)
    /// - Mode 1 (Performance): Best Performance      (ded574b5-45a0-4f42-8737-46345c09c238)
    /// Routes safely through Windows Power Manager and signed AMD PPM/PMF kernel drivers with zero ring-0 SMN races.
    /// </summary>
    public static class WindowsPowerMode
    {
        public static readonly Guid GuidBestPowerEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid GuidBalanced            = Guid.Empty;
        public static readonly Guid GuidBestPerformance     = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
        private static extern uint PowerSetActiveOverlayScheme(ref Guid overlaySchemeGuid);

        [DllImport("powrprof.dll", EntryPoint = "PowerGetActualOverlayScheme")]
        private static extern uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

        public static Guid ModeCodeToGuid(int modeCode)
        {
            switch (modeCode)
            {
                case 2: return GuidBestPowerEfficiency;
                case 1: return GuidBestPerformance;
                default: return GuidBalanced;
            }
        }

        public static int GuidToModeCode(Guid overlayGuid)
        {
            if (overlayGuid == GuidBestPowerEfficiency) return 2;
            if (overlayGuid == GuidBestPerformance) return 1;
            return 0;
        }

        public static string GetModeDisplayName(int modeCode)
        {
            switch (modeCode)
            {
                case 2: return "Best Power Efficiency (Silent)";
                case 1: return "Best Performance";
                default: return "Balanced";
            }
        }

        public static bool IsSupported()
        {
            try
            {
                Guid g;
                return PowerGetActualOverlayScheme(out g) == 0;
            }
            catch
            {
                return false;
            }
        }

        public static int GetActiveModeCode()
        {
            try
            {
                Guid g;
                if (PowerGetActualOverlayScheme(out g) == 0)
                    return GuidToModeCode(g);
            }
            catch { }
            return -1;
        }

        public static bool SetModeCode(int modeCode)
        {
            if (modeCode < 0 || modeCode > 2) return false;
            try
            {
                Guid g = ModeCodeToGuid(modeCode);
                return PowerSetActiveOverlayScheme(ref g) == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
