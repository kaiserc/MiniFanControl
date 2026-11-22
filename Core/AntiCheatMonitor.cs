using System;

namespace MiniFanControl
{
    public enum AntiCheatAction
    {
        None = 0,
        PauseForAntiCheat,
        ResumeAfterAntiCheat
    }

    /// <summary>
    /// Opt-in anti-cheat watcher that coordinates safe BIOS Auto handoff and legacy kernel driver
    /// unload while per-game kernel anti-cheat services (Easy Anti-Cheat, BattlEye) are active,
    /// and cleanly detects boot-time Riot Vanguard (vgk) for user status diagnostics.
    /// </summary>
    public class AntiCheatMonitor
    {
        public static readonly string[] PerGameServiceNames = new string[]
        {
            "EasyAntiCheat",
            "EasyAntiCheat_EOS",
            "BEService"
        };

        public const string BootVanguardServiceName = "vgk";
        public const int DEFAULT_RESUME_COOLDOWN_MS = 3000;

        private readonly Func<string, bool> _isServiceRunning;
        private readonly Func<long> _clockMs;
        private readonly int _resumeCooldownMs;

        private long _clearSinceMs = -1;

        public bool Enabled { get; set; }
        public bool IsPaused { get; private set; }
        public string ActiveAntiCheatName { get; private set; }
        public bool IsBootVanguardDetected { get; private set; }

        public AntiCheatMonitor(
            Func<string, bool> isServiceRunning = null,
            Func<long> clockMs = null,
            int resumeCooldownMs = DEFAULT_RESUME_COOLDOWN_MS)
        {
            _isServiceRunning = isServiceRunning ?? new Func<string, bool>(DriverServiceManager.IsDriverServiceRunning);
            _clockMs = clockMs ?? new Func<long>(() => Environment.TickCount & 0x7FFFFFFF);
            _resumeCooldownMs = Math.Max(0, resumeCooldownMs);
            ActiveAntiCheatName = "";
        }

        public static string FriendlyName(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return "";
            if (serviceName.Equals("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                serviceName.Equals("EasyAntiCheat_EOS", StringComparison.OrdinalIgnoreCase))
                return "Easy Anti-Cheat";
            if (serviceName.Equals("BEService", StringComparison.OrdinalIgnoreCase))
                return "BattlEye";
            if (serviceName.Equals("vgk", StringComparison.OrdinalIgnoreCase))
                return "Riot Vanguard";
            return serviceName;
        }

        /// <summary>
        /// Probes whether any per-game kernel anti-cheat service is currently running.
        /// Also refreshes boot-time Vanguard detection for UI diagnostics.
        /// </summary>
        public string DetectActivePerGameAntiCheat()
        {
            try
            {
                IsBootVanguardDetected = _isServiceRunning(BootVanguardServiceName);
            }
            catch
            {
                IsBootVanguardDetected = false;
            }

            for (int i = 0; i < PerGameServiceNames.Length; i++)
            {
                try
                {
                    if (_isServiceRunning(PerGameServiceNames[i]))
                        return FriendlyName(PerGameServiceNames[i]);
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Evaluates anti-cheat transitions on each telemetry poll.
        /// Caller must restore BIOS Auto BEFORE unloading the legacy driver on PauseForAntiCheat.
        /// </summary>
        public AntiCheatAction Poll()
        {
            string detected = DetectActivePerGameAntiCheat();

            if (!Enabled)
            {
                if (IsPaused)
                {
                    IsPaused = false;
                    ActiveAntiCheatName = "";
                    _clearSinceMs = -1;
                    return AntiCheatAction.ResumeAfterAntiCheat;
                }
                return AntiCheatAction.None;
            }

            if (!string.IsNullOrEmpty(detected))
            {
                _clearSinceMs = -1;
                ActiveAntiCheatName = detected;
                if (!IsPaused)
                {
                    IsPaused = true;
                    return AntiCheatAction.PauseForAntiCheat;
                }
                return AntiCheatAction.None;
            }

            // No per-game anti-cheat running right now
            if (IsPaused)
            {
                long now = _clockMs();
                if (_clearSinceMs < 0)
                {
                    _clearSinceMs = now;
                    return AntiCheatAction.None;
                }

                long elapsed = now - _clearSinceMs;
                if (elapsed < 0) elapsed = _resumeCooldownMs; // TickCount wrap safety
                if (elapsed >= _resumeCooldownMs)
                {
                    IsPaused = false;
                    ActiveAntiCheatName = "";
                    _clearSinceMs = -1;
                    return AntiCheatAction.ResumeAfterAntiCheat;
                }
            }

            return AntiCheatAction.None;
        }

        /// <summary>
        /// Executes the safe pause handoff in strict order:
        /// 1. Restore BIOS Auto / Safe Super I/O Fallback while driver is still loaded.
        /// 2. Unload legacy kernel driver (skipped if HVCI-signed PawnIO is active).
        /// </summary>
        public static bool ExecuteSafePauseHandoff(
            Func<bool> restoreBiosAuto,
            Action shutdownUserDriver,
            Func<string, bool> stopDriverService,
            HardwareDriverEngine activeDriver)
        {
            bool autoRestored = false;
            if (restoreBiosAuto != null)
            {
                try { autoRestored = restoreBiosAuto(); } catch { autoRestored = false; }
            }

            // PawnIO is EV-signed and HVCI-compatible; only unload legacy WinRing0 / InpOut64 drivers
            if (activeDriver != HardwareDriverEngine.PawnIO)
            {
                if (shutdownUserDriver != null)
                {
                    try { shutdownUserDriver(); } catch { }
                }
                if (stopDriverService != null)
                {
                    try { stopDriverService("inpoutx64"); } catch { }
                    try { stopDriverService("WinRing0_1_2_0"); } catch { }
                }
            }
            return autoRestored;
        }
    }
}
