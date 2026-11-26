using System;
using System.Collections.Generic;

namespace MiniFanControl
{
    /// <summary>
    /// Cohesive fan curve profile catalog & hardware badge resolver extracted from MainForm.
    /// Provides pure, unit-testable profile lookup, deduplicated custom profile upserts,
    /// and AMD Ryzen processor tier badge detection.
    /// </summary>
    public static class CurveProfileCatalog
    {
        public static void UpsertCustomProfile(List<FanCurveProfile> list, FanCurveProfile profile)
        {
            if (list == null || profile == null || string.IsNullOrWhiteSpace(profile.Name)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
                {
                    list[i] = profile;
                    return;
                }
            }
            list.Add(profile);
        }

        public static FanCurveProfile FindProfileByName(
            string name,
            IList<FanCurveProfile> builtInProfiles,
            IList<FanCurveProfile> customProfiles)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (builtInProfiles != null)
            {
                for (int i = 0; i < builtInProfiles.Count; i++)
                {
                    if (builtInProfiles[i] != null && builtInProfiles[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return builtInProfiles[i];
                }
            }
            if (customProfiles != null)
            {
                for (int i = 0; i < customProfiles.Count; i++)
                {
                    if (customProfiles[i] != null && customProfiles[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return customProfiles[i];
                }
            }
            return null;
        }

        public static string ResolveRyzenTierBadge(SystemHardwareInfo sysInfo)
        {
            if (sysInfo == null) return null;
            string combined = (sysInfo.CpuName ?? "") + " " + (sysInfo.MatchedProfile != null ? sysInfo.MatchedProfile.DisplayName : "");
            if (combined.IndexOf("Ryzen AI 9", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen AI 9";
            if (combined.IndexOf("Ryzen 9", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 9";
            if (combined.IndexOf("Ryzen 7", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 7";
            if (combined.IndexOf("Ryzen 5", StringComparison.OrdinalIgnoreCase) >= 0) return "Ryzen 5";
            return null;
        }
    }
}
