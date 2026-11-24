using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MiniFanControl
{
    public class AppSettings
    {
        public bool ShowCpuTray = true;
        public bool ShowGpuTray = true;
        public bool ShowOverlay = false;
        public bool LockOverlay = false;
        public bool RestoreAutoOnExit = true;
        public bool CloseToTray = false;
        public bool UseFahrenheit = false;
        public bool ShowSetupAssistant = true;
        public bool SilentSleepStandby = true;
        public bool AutoPauseForAntiCheat = false;
        public bool AutoSwitchWorkloads = false;
        public readonly List<WorkloadRule> WorkloadRules = new List<WorkloadRule>();
        public bool AlwaysOnTop = false;
        public bool EnableGlobalHotkeys = true;
        public bool HasDualFans = true;
        public string ProfileOverrideId = "auto";
        public int RefreshIntervalMs = 1000;
        public ThemePreference ThemePref = ThemePreference.System;
        public string ApuModeName = "Balanced";
        public string SysModeName = "Balanced";
        public int BiosPowerMode = -1; // -1 = follow profile/BIOS default, 0 = Balanced, 1 = Performance, 2 = Silent
        public string LegacyCustomName = null;

        public readonly OsdOverlayConfig Osd = new OsdOverlayConfig();

        public static string GetSettingsPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string primary = Path.Combine(baseDir, "minifancontrol.cfg");
            string legacy = Path.Combine(baseDir, "k12-settings.cfg");

            if (File.Exists(primary)) return primary;
            if (File.Exists(legacy)) return legacy;

            string appDataConfig = Path.Combine(AppPaths.GetAppDataDir(false), "minifancontrol.cfg");
            if (File.Exists(appDataConfig)) return appDataConfig;

            if (AppPaths.IsInstalledInProgramFiles())
            {
                return Path.Combine(AppPaths.GetAppDataDir(true), "minifancontrol.cfg");
            }

            return primary;
        }

        public static CurvePoint[] ParsePointList(string str)
        {
            List<CurvePoint> pts = new List<CurvePoint>();
            if (string.IsNullOrEmpty(str)) return pts.ToArray();
            foreach (string tok in str.Split(';'))
            {
                string[] kv = tok.Split(':');
                int t, p;
                if (kv.Length == 2 &&
                    int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out t) &&
                    int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out p))
                {
                    pts.Add(new CurvePoint(
                        Math.Max(FanCurveCanvas.MIN_TEMP, Math.Min(FanCurveCanvas.MAX_TEMP, t)),
                        Math.Max(0, Math.Min(100, p))));
                }
            }
            return pts.ToArray();
        }

        public static string FormatPointList(CurvePoint[] pts)
        {
            List<string> list = new List<string>();
            if (pts != null)
            {
                foreach (CurvePoint pt in pts)
                    list.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}", pt.Temp, pt.Pct));
            }
            return string.Join(";", list.ToArray());
        }

        public static bool TryParseHexOrDecByte(string s, out byte val)
        {
            val = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val);
            return byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);
        }

        private static bool ParseBool(string v)
        {
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void Load(SystemHardwareInfo sysInfo, HardwareBackend hw, List<FanCurveProfile> customProfiles)
        {
            LoadFromPath(GetSettingsPath(), sysInfo, hw, customProfiles);
        }

        public void LoadFromPath(string path, SystemHardwareInfo sysInfo, HardwareBackend hw, List<FanCurveProfile> customProfiles)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                LegacyCustomName = null;
                ApuModeName = null;
                SysModeName = null;
                DeviceProfile prof = sysInfo != null ? sysInfo.MatchedProfile : (hw != null && hw.Profile != null ? hw.Profile : DeviceProfile.KnownProfiles[0]);
                bool profCloned = false;
                Action ensureClonedProfile = delegate
                {
                    if (!profCloned && prof != null)
                    {
                        prof = prof.Clone();
                        profCloned = true;
                        if (sysInfo != null) sysInfo.MatchedProfile = prof;
                        if (hw != null) hw.Profile = prof;
                    }
                };

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("CustomProfile=", StringComparison.OrdinalIgnoreCase))
                    {
                        string payload = line.Substring("CustomProfile=".Length);
                        string[] segments = payload.Split('|');
                        if (segments.Length >= 2)
                        {
                            string name = segments[0].Trim();
                            CurvePoint[] apuPts = ParsePointList(segments[1]);
                            CurvePoint[] sysPts = segments.Length >= 3 ? ParsePointList(segments[2]) : FanCurveProfile.ClonePoints(apuPts);
                            if (!string.IsNullOrEmpty(name) && apuPts.Length >= 2 && customProfiles != null)
                            {
                                if (sysPts.Length < 2) sysPts = FanCurveProfile.ClonePoints(apuPts);
                                FanCurveProfile loadedProf = new FanCurveProfile(name, false, -1, apuPts, sysPts);
                                int existingIdx = -1;
                                for (int idx = 0; idx < customProfiles.Count; idx++)
                                {
                                    if (customProfiles[idx] != null && customProfiles[idx].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        existingIdx = idx;
                                        break;
                                    }
                                }
                                if (existingIdx >= 0) customProfiles[existingIdx] = loadedProf;
                                else customProfiles.Add(loadedProf);
                            }
                        }
                        continue;
                    }

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0) continue;
                    string k = line.Substring(0, eqIdx).Trim();
                    string v = line.Substring(eqIdx + 1).Trim();

                    if (Osd.TryParseSetting(k, v)) continue;

                    byte regByte;
                    int intVal;
                    if (k == "ShowCpuTray") ShowCpuTray = ParseBool(v);
                    else if (k == "ShowGpuTray") ShowGpuTray = ParseBool(v);
                    else if (k == "ShowOverlay") ShowOverlay = ParseBool(v);
                    else if (k == "LockOverlay") LockOverlay = ParseBool(v);
                    else if (k == "Language")
                    {
                        try
                        {
                            AppLanguage parsedLang = (AppLanguage)Enum.Parse(typeof(AppLanguage), v, true);
                            Loc.SetLanguage(parsedLang);
                        }
                        catch (Exception ex)
                        {
                            WatchdogSupervisor.LogWatchdogEvent("Invalid Language setting '" + v + "': " + ex.Message);
                        }
                    }
                    else if (k == "RestoreAutoOnExit") RestoreAutoOnExit = ParseBool(v);
                    else if (k == "CloseToTray") CloseToTray = ParseBool(v);
                    else if (k == "UseFahrenheit") UseFahrenheit = ParseBool(v);
                    else if (k == "SilentSleepStandby") SilentSleepStandby = ParseBool(v);
                    else if (k == "AutoPauseForAntiCheat") AutoPauseForAntiCheat = ParseBool(v);
                    else if (k == "AutoSwitchWorkloads") AutoSwitchWorkloads = ParseBool(v);
                    else if (k == "WorkloadRule")
                    {
                        WorkloadRule rule = WorkloadRule.Parse(v);
                        if (rule != null)
                        {
                            int dupIdx = -1;
                            for (int ri = 0; ri < WorkloadRules.Count; ri++)
                            {
                                if (WorkloadRules[ri].ProcessPattern.Equals(rule.ProcessPattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    dupIdx = ri;
                                    break;
                                }
                            }
                            if (dupIdx >= 0) WorkloadRules[dupIdx] = rule;
                            else WorkloadRules.Add(rule);
                        }
                    }
                    else if (k == "AlwaysOnTop") AlwaysOnTop = ParseBool(v);
                    else if (k == "EnableHotkeys") EnableGlobalHotkeys = ParseBool(v);
                    else if (k == "HasDualFans") HasDualFans = ParseBool(v);
                    else if (k == "ShowSetupAssistant") ShowSetupAssistant = ParseBool(v);
                    else if (k == "HardwareProfile" || k == "ProfileId")
                    {
                        ProfileOverrideId = v;
                        if (!v.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            DeviceProfile explicitProf = DeviceProfile.FindProfile(v);
                            if (explicitProf != null)
                            {
                                prof = explicitProf;
                                profCloned = false;
                                if (sysInfo != null) sysInfo.MatchedProfile = explicitProf;
                                if (hw != null) hw.Profile = explicitProf;
                            }
                        }
                    }
                    else if (k == "RefreshIntervalMs" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) RefreshIntervalMs = Math.Max(100, Math.Min(10000, intVal));
                    else if (k == "ApuMinSpinPct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) FanCurveProfile.ApuMinSpinPct = Math.Max(14, Math.Min(45, intVal));
                    else if (k == "SysMinSpinPct" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) FanCurveProfile.SysMinSpinPct = Math.Max(14, Math.Min(45, intVal));
                    else if (k == "ApuMaxRpm" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) FanCurveProfile.ApuMaxRpm = Math.Max(500, Math.Min(6000, intVal));
                    else if (k == "SysMaxRpm" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal)) FanCurveProfile.SysMaxRpm = Math.Max(0, Math.Min(6000, intVal));
                    else if (k == "LastCalibratedDate") FanCurveProfile.LastCalibratedDate = v;
                    else if (k == "Theme" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal) && intVal >= 0 && intVal <= 4)
                    {
                        ThemePref = (ThemePreference)intVal;
                    }
                    else if (k == "ActiveCustom") LegacyCustomName = v;
                    else if (k == "ApuMode") ApuModeName = v;
                    else if (k == "SysMode") SysModeName = v;
                    else if (k == "BiosPowerMode" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal) && intVal >= -1 && intVal <= 2) BiosPowerMode = intVal;
                    else if (k == "RegModeStatus" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegModeStatus = regByte; }
                    else if (k == "RegModeCmd" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegModeCmd = regByte; }
                    else if (k == "RegFan1Ctrl" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan1Ctrl = regByte; }
                    else if (k == "RegFan2Ctrl" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan2Ctrl = regByte; }
                    else if (k == "RegFan1Hi" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan1Hi = regByte; }
                    else if (k == "RegFan1Lo" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan1Lo = regByte; }
                    else if (k == "RegFan2Hi" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan2Hi = regByte; }
                    else if (k == "RegFan2Lo" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegFan2Lo = regByte; }
                    else if (k == "RegCpuTemp" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegCpuTemp = regByte; }
                    else if (k == "RegGpuTemp" && TryParseHexOrDecByte(v, out regByte)) { ensureClonedProfile(); prof.RegGpuTemp = regByte; }
                    else if ((k == "EcAccess" || k == "AccessMethod") && hw != null)
                    {
                        if (v.Equals("PortIo", StringComparison.OrdinalIgnoreCase) || v.Equals("Port", StringComparison.OrdinalIgnoreCase) || v.Equals("0x62", StringComparison.OrdinalIgnoreCase))
                            hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo62;
                        else if (v.Equals("PortIo6C", StringComparison.OrdinalIgnoreCase) || v.Equals("0x6C", StringComparison.OrdinalIgnoreCase))
                            hw.AccessMethod = HardwareBackend.EcAccessMethod.PortIo6C;
                        else if (v.Equals("Mmio", StringComparison.OrdinalIgnoreCase))
                            hw.AccessMethod = HardwareBackend.EcAccessMethod.Mmio;
                    }
                }
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("LoadSettings failed: " + ex.Message);
            }
        }

        public static void WriteAllLinesAtomic(string path, string[] lines)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string tmpPath = path + ".tmp";
            string bakPath = path + ".bak";
            File.WriteAllLines(tmpPath, lines);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmpPath, path, bakPath, true);
                }
                catch
                {
                    File.Copy(tmpPath, path, true);
                    try { File.Delete(tmpPath); } catch { }
                }
            }
            else
            {
                File.Move(tmpPath, path);
            }
        }

        public void Save(FanCurveProfile apuProfile, FanCurveProfile sysProfile, List<FanCurveProfile> customProfiles)
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("ShowCpuTray=" + (ShowCpuTray ? "1" : "0"));
                lines.Add("ShowGpuTray=" + (ShowGpuTray ? "1" : "0"));
                lines.Add("ShowOverlay=" + (ShowOverlay ? "1" : "0"));
                lines.Add("LockOverlay=" + (LockOverlay ? "1" : "0"));
                Osd.AppendSettings(lines);
                lines.Add("RestoreAutoOnExit=" + (RestoreAutoOnExit ? "1" : "0"));
                lines.Add("CloseToTray=" + (CloseToTray ? "1" : "0"));
                lines.Add("UseFahrenheit=" + (UseFahrenheit ? "1" : "0"));
                lines.Add("ShowSetupAssistant=" + (ShowSetupAssistant ? "1" : "0"));
                lines.Add("SilentSleepStandby=" + (SilentSleepStandby ? "1" : "0"));
                lines.Add("AutoPauseForAntiCheat=" + (AutoPauseForAntiCheat ? "1" : "0"));
                lines.Add("AutoSwitchWorkloads=" + (AutoSwitchWorkloads ? "1" : "0"));
                for (int ri = 0; ri < WorkloadRules.Count; ri++)
                {
                    if (WorkloadRules[ri] != null && !string.IsNullOrWhiteSpace(WorkloadRules[ri].ProcessPattern))
                        lines.Add("WorkloadRule=" + WorkloadRules[ri].Serialize());
                }
                lines.Add("AlwaysOnTop=" + (AlwaysOnTop ? "1" : "0"));
                lines.Add("EnableHotkeys=" + (EnableGlobalHotkeys ? "1" : "0"));
                lines.Add("HasDualFans=" + (HasDualFans ? "1" : "0"));
                lines.Add("HardwareProfile=" + (ProfileOverrideId ?? "auto"));
                lines.Add("Language=" + Loc.CurrentLanguage.ToString());
                lines.Add("RefreshIntervalMs=" + RefreshIntervalMs.ToString(CultureInfo.InvariantCulture));
                lines.Add("ApuMinSpinPct=" + FanCurveProfile.ApuMinSpinPct.ToString(CultureInfo.InvariantCulture));
                lines.Add("SysMinSpinPct=" + FanCurveProfile.SysMinSpinPct.ToString(CultureInfo.InvariantCulture));
                lines.Add("ApuMaxRpm=" + FanCurveProfile.ApuMaxRpm.ToString(CultureInfo.InvariantCulture));
                lines.Add("SysMaxRpm=" + FanCurveProfile.SysMaxRpm.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(FanCurveProfile.LastCalibratedDate))
                    lines.Add("LastCalibratedDate=" + FanCurveProfile.LastCalibratedDate);
                lines.Add("Theme=" + ((int)ThemePref).ToString(CultureInfo.InvariantCulture));
                lines.Add("ApuMode=" + (apuProfile != null ? apuProfile.Name : "Balanced"));
                lines.Add("SysMode=" + (sysProfile != null ? sysProfile.Name : "Balanced"));
                if (BiosPowerMode >= 0 && BiosPowerMode <= 2)
                    lines.Add("BiosPowerMode=" + BiosPowerMode.ToString(CultureInfo.InvariantCulture));

                if (customProfiles != null)
                {
                    foreach (FanCurveProfile cp in customProfiles)
                    {
                        lines.Add("CustomProfile=" + cp.Name.Replace("|", "/") + "|" + FormatPointList(cp.ApuPoints) + "|" + FormatPointList(cp.SysPoints));
                    }
                }

                string path = GetSettingsPath();
                WriteAllLinesAtomic(path, lines.ToArray());
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("SaveSettings failed: " + ex.Message);
            }
        }
    }
}
