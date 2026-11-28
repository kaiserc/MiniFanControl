using System;
using System.Collections.Generic;
using System.IO;

namespace MiniFanControl
{
    public class WorkloadRule
    {
        public string ProcessPattern;
        public string ApuProfileName;
        public string SysProfileName;

        public WorkloadRule(string processPattern, string apuProfileName, string sysProfileName = null)
        {
            ProcessPattern = (processPattern ?? "").Trim();
            ApuProfileName = string.IsNullOrWhiteSpace(apuProfileName) ? "Performance" : apuProfileName.Trim();
            SysProfileName = string.IsNullOrWhiteSpace(sysProfileName) ? ApuProfileName : sysProfileName.Trim();
        }

        public bool Matches(string processName)
        {
            if (string.IsNullOrWhiteSpace(ProcessPattern) || string.IsNullOrWhiteSpace(processName))
                return false;

            string p = Path.GetFileName(processName.Trim());
            string pat = ProcessPattern.Trim();

            if (pat == "*") return true;
            if (p.Equals(pat, StringComparison.OrdinalIgnoreCase)) return true;

            string pNoExt = p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 4) : p;
            string patNoExt = pat.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? pat.Substring(0, pat.Length - 4) : pat;
            if (pNoExt.Equals(patNoExt, StringComparison.OrdinalIgnoreCase)) return true;

            if (pat.EndsWith("*", StringComparison.Ordinal) && pat.Length > 1)
            {
                string prefix = pat.Substring(0, pat.Length - 1);
                return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                       pNoExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public string Serialize()
        {
            return ProcessPattern.Replace("|", "") + "|" + ApuProfileName.Replace("|", "/") + "|" + SysProfileName.Replace("|", "/");
        }

        public static WorkloadRule Parse(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string[] parts = payload.Split('|');
            if (parts.Length < 2) return null;
            string pat = parts[0].Trim();
            string apu = parts[1].Trim();
            string sys = (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])) ? parts[2].Trim() : apu;
            if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(apu)) return null;
            return new WorkloadRule(pat, apu, sys);
        }
    }

    public enum WorkloadSwitchAction
    {
        None = 0,
        ApplyWorkloadProfile,
        RestoreBaselineProfile
    }

    /// <summary>
    /// Opt-in automatic per-game and per-workload fan profile switcher powered by RTSS/ETW
    /// foreground 3D process detection and AMD XDNA NPU activity.
    /// Includes a configurable exit cooldown (default 5s) so alt-tabbing or loading screens
    /// never thrash fan profiles, and cleanly restores the user's baseline profile on exit.
    /// </summary>
    public class WorkloadProfileSwitcher
    {
        public const int DEFAULT_EXIT_COOLDOWN_MS = 5000;
        public const int DEFAULT_NPU_TRIGGER_PCT = 30;

        private static readonly string[] KnownAiProcesses = new string[]
        {
            "ollama.exe",
            "ollama_llama_server.exe",
            "llama-server.exe",
            "lm studio.exe",
            "unsloth-studio.exe",
            "unsloth.exe",
            "koboldcpp.exe",
            "comfyui.exe"
        };

        private readonly Func<long> _clockMs;
        private readonly int _exitCooldownMs;
        private long _idleSinceMs = -1;

        public bool Enabled { get; set; }
        public List<WorkloadRule> Rules { get; private set; }
        public bool IsSwitched { get; private set; }
        public string ActiveWorkloadReason { get; private set; }
        public string TargetApuProfileName { get; private set; }
        public string TargetSysProfileName { get; private set; }
        public string BaselineApuProfileName { get; private set; }
        public string BaselineSysProfileName { get; private set; }

        public WorkloadProfileSwitcher(Func<long> clockMs = null, int exitCooldownMs = DEFAULT_EXIT_COOLDOWN_MS)
        {
            _clockMs = clockMs ?? new Func<long>(() => Environment.TickCount & 0x7FFFFFFF);
            _exitCooldownMs = Math.Max(0, exitCooldownMs);
            Rules = new List<WorkloadRule>();
            ActiveWorkloadReason = "";
        }

        public static bool IsKnownAiProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            string n = Path.GetFileName(processName.Trim());
            for (int i = 0; i < KnownAiProcesses.Length; i++)
            {
                if (n.Equals(KnownAiProcesses[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public WorkloadSwitchAction Evaluate(
            string activeProcessName,
            int npuActivityPct,
            string currentApuProfileName,
            string currentSysProfileName,
            bool manualOverrideActive)
        {
            if (!Enabled)
            {
                if (IsSwitched)
                {
                    IsSwitched = false;
                    ActiveWorkloadReason = "";
                    _idleSinceMs = -1;
                    return WorkloadSwitchAction.RestoreBaselineProfile;
                }
                return WorkloadSwitchAction.None;
            }

            if (manualOverrideActive)
            {
                return WorkloadSwitchAction.None;
            }

            string matchedApu = null;
            string matchedSys = null;
            string reason = null;

            // 1. Explicit user-defined process rules take highest priority
            if (!string.IsNullOrWhiteSpace(activeProcessName) && Rules != null)
            {
                for (int i = 0; i < Rules.Count; i++)
                {
                    if (Rules[i] != null && Rules[i].Matches(activeProcessName))
                    {
                        matchedApu = Rules[i].ApuProfileName;
                        matchedSys = Rules[i].SysProfileName;
                        reason = Path.GetFileName(activeProcessName.Trim());
                        break;
                    }
                }
            }

            // 2. Built-in AI/LLM workload detection (local LLM process or sustained NPU activity)
            if (matchedApu == null)
            {
                if (IsKnownAiProcess(activeProcessName))
                {
                    matchedApu = "AI Workload";
                    matchedSys = "AI Workload";
                    reason = Path.GetFileName(activeProcessName.Trim()) + " (AI)";
                }
                else if (npuActivityPct >= DEFAULT_NPU_TRIGGER_PCT)
                {
                    matchedApu = "AI Workload";
                    matchedSys = "AI Workload";
                    reason = "NPU " + npuActivityPct + "%";
                }
            }

            // 3. Built-in 3D game detection (active foreground RTSS/ETW game -> Performance)
            if (matchedApu == null && !string.IsNullOrWhiteSpace(activeProcessName) && !FpsMonitor.IsIgnoredProcess(activeProcessName))
            {
                matchedApu = "Performance";
                matchedSys = "Performance";
                reason = Path.GetFileName(activeProcessName.Trim());
            }

            if (!string.IsNullOrEmpty(matchedApu))
            {
                _idleSinceMs = -1;
                if (!IsSwitched)
                {
                    BaselineApuProfileName = string.IsNullOrEmpty(currentApuProfileName) ? "Balanced" : currentApuProfileName;
                    BaselineSysProfileName = string.IsNullOrEmpty(currentSysProfileName) ? "Balanced" : currentSysProfileName;
                    IsSwitched = true;
                    TargetApuProfileName = matchedApu;
                    TargetSysProfileName = matchedSys;
                    ActiveWorkloadReason = reason ?? "";
                    return WorkloadSwitchAction.ApplyWorkloadProfile;
                }
                if (!string.Equals(TargetApuProfileName, matchedApu, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(TargetSysProfileName, matchedSys, StringComparison.OrdinalIgnoreCase))
                {
                    TargetApuProfileName = matchedApu;
                    TargetSysProfileName = matchedSys;
                    ActiveWorkloadReason = reason ?? "";
                    return WorkloadSwitchAction.ApplyWorkloadProfile;
                }
                return WorkloadSwitchAction.None;
            }

            // No active workload detected — hold for exit cooldown before restoring baseline
            if (IsSwitched)
            {
                long now = _clockMs();
                if (_idleSinceMs < 0)
                {
                    _idleSinceMs = now;
                    return WorkloadSwitchAction.None;
                }
                long elapsed = now - _idleSinceMs;
                if (elapsed < 0) elapsed = _exitCooldownMs; // TickCount wrap safety
                if (elapsed >= _exitCooldownMs)
                {
                    IsSwitched = false;
                    ActiveWorkloadReason = "";
                    TargetApuProfileName = null;
                    TargetSysProfileName = null;
                    _idleSinceMs = -1;
                    return WorkloadSwitchAction.RestoreBaselineProfile;
                }
            }

            return WorkloadSwitchAction.None;
        }
    }
}
