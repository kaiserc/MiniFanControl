using System;

namespace MiniFanControl
{
    /// <summary>
    /// Shared orchestration helper unifying WorkloadProfileSwitcher profile transitions
    /// between the WinForms GUI (MainForm) and the zero-UI HeadlessDaemon / Windows Service.
    /// </summary>
    public static class AutomationCoordinator
    {
        public static WorkloadSwitchAction EvaluateAndApplyWorkloadSwitch(
            WorkloadProfileSwitcher switcher,
            FanControllerService controller,
            FanStatus status,
            bool isSuspendedOrReadOnly,
            bool isAntiCheatPaused,
            Func<string, FanCurveProfile> resolveProfile,
            ref FanCurveProfile activeApuProfile,
            ref FanCurveProfile activeSysProfile,
            out string statusMessage)
        {
            statusMessage = null;
            if (switcher == null || !switcher.Enabled || controller == null || isSuspendedOrReadOnly || isAntiCheatPaused)
                return WorkloadSwitchAction.None;

            WorkloadSwitchAction action = switcher.Evaluate(
                status.RtssProcessName,
                status.NpuActivityPct,
                activeApuProfile != null ? activeApuProfile.Name : "Balanced",
                activeSysProfile != null ? activeSysProfile.Name : "Balanced",
                controller.ApuManualOverride || controller.SysManualOverride);

            if (action == WorkloadSwitchAction.ApplyWorkloadProfile)
            {
                FanCurveProfile nextApu = resolveProfile != null ? resolveProfile(switcher.TargetApuProfileName) : null;
                FanCurveProfile nextSys = resolveProfile != null ? resolveProfile(switcher.TargetSysProfileName) : null;
                if (nextApu != null) { activeApuProfile = nextApu; controller.SetProfileTarget(nextApu, false); }
                if (nextSys != null) { activeSysProfile = nextSys; controller.SetProfileTarget(nextSys, true); }
                statusMessage = string.Format("⚡ Auto-switched to {0} for {1}",
                    switcher.TargetApuProfileName, switcher.ActiveWorkloadReason);
            }
            else if (action == WorkloadSwitchAction.RestoreBaselineProfile)
            {
                FanCurveProfile baseApu = resolveProfile != null ? resolveProfile(switcher.BaselineApuProfileName) : null;
                FanCurveProfile baseSys = resolveProfile != null ? resolveProfile(switcher.BaselineSysProfileName) : null;
                if (baseApu != null) { activeApuProfile = baseApu; controller.SetProfileTarget(baseApu, false); }
                if (baseSys != null) { activeSysProfile = baseSys; controller.SetProfileTarget(baseSys, true); }
                statusMessage = string.Format("✓ Workload exited — restored baseline profile ({0})",
                    switcher.BaselineApuProfileName);
            }

            return action;
        }
    }
}
