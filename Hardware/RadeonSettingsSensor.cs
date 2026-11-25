using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MiniFanControl
{
    public struct RadeonSettingsTelemetry
    {
        public bool IsAvailable;
        public bool AdrenalinInstalled;    // True when Adrenalin UI / CN databases are present (false on Driver-Only installs)
        public string GpuDesc;             // e.g. "AMD Radeon 780M Graphics"
        public string SoftwareEdition;     // e.g. "AMD Software: Adrenalin Edition"
        public string SoftwareVersion;     // e.g. "26.10.1"
        public bool FreeSync;              // FreeSync / VRR active on display
        public bool AntiLag;               // KMD_DeLagEnabled = 1 or Adrenalin Delag: On
        public bool Afmf;                  // DrvFrameGenAlgorithm/Enabled != 0 or Adrenalin FrameGen: On
        public bool RadeonBoost;           // KMD_RadeonBoostEnabled = 1
        public bool Chill;                 // KMD_ChillEnabled = 1
        public bool EnhancedSync;          // TurboSync = 1
        public bool Rsr;                   // Gmx_RSREnabled = 1 (Radeon Super Resolution)
        public bool Ris;                   // KMD_ImageSharpeningEnabled = 1 (Radeon Image Sharpening)
        public bool Frtc;                  // KMD_FRTEnabled = 1
        public int FrtcFps;                // KMD_MaxFrameRateRequested
        public bool SmartAccessMemory;     // KMD_RebarControlMode != 0
        public bool Fsr;                   // FeatureID 58 / KMD_FSREnabled (AMD FSR Upscaling)
        public string Summary;             // e.g. "Anti-Lag • AFMF 2 • FSR • Boost"

        public static string BuildSummary(bool freeSync, bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool ris, bool fsr, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            List<string> active = new List<string>(11);
            if (freeSync) active.Add("FreeSync");
            if (antiLag) active.Add("Anti-Lag");
            if (afmf) active.Add("AFMF 2");
            if (fsr) active.Add("FSR");
            if (boost) active.Add("Boost");
            if (chill) active.Add("Chill");
            if (rsr) active.Add("RSR");
            if (ris) active.Add("RIS");
            if (enhSync) active.Add("EnhSync");
            if (sam) active.Add("SAM");
            if (frtc && frtcFps > 0) active.Add(string.Format("FRTC({0})", frtcFps));

            if (active.Count == 0) return "Default";
            return string.Join(" • ", active.ToArray());
        }

        public static string BuildSummary(bool freeSync, bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool ris, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            return BuildSummary(freeSync, antiLag, afmf, boost, chill, rsr, ris, false, enhSync, sam, frtc, frtcFps);
        }

        public static string BuildSummary(bool freeSync, bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            return BuildSummary(freeSync, antiLag, afmf, boost, chill, rsr, false, false, enhSync, sam, frtc, frtcFps);
        }

        public static string BuildSummary(bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            return BuildSummary(false, antiLag, afmf, boost, chill, rsr, false, false, enhSync, sam, frtc, frtcFps);
        }

        public List<string> GetActiveTags()
        {
            List<string> tags = new List<string>(11);
            if (FreeSync) tags.Add("FreeSync");
            if (AntiLag) tags.Add("Anti-Lag");
            if (Afmf) tags.Add("AFMF");
            if (Fsr) tags.Add("FSR");
            if (Chill) tags.Add("Chill");
            if (RadeonBoost) tags.Add("Boost");
            if (Rsr) tags.Add("RSR");
            if (Ris) tags.Add("RIS");
            if (EnhancedSync) tags.Add("EnhSync");
            if (SmartAccessMemory) tags.Add("SAM");
            if (Frtc) tags.Add(FrtcFps > 0 ? string.Format("FRTC {0}", FrtcFps) : "FRTC");
            return tags;
        }
    }

    /// <summary>
    /// Reads active AMD Radeon Adrenalin Driver 3D settings and gaming features.
    /// Safely inspects the display driver registry interfaces (KMD & UMD) and live Adrenalin UI state
    /// without external DLL dependencies or game process hooking.
    /// </summary>
    public static class RadeonSettingsSensor
    {
        private static RadeonSettingsTelemetry _cached;
        private static DateTime _lastSampleTime = DateTime.MinValue;
        private static string _cachedProcessName = null;
        private static readonly object _lock = new object();
        private static string _discoveredAdapterKey = null;

        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _lastSampleTime = DateTime.MinValue;
            }
        }

        public static RadeonSettingsTelemetry Sample()
        {
            return Sample(null);
        }

        public static RadeonSettingsTelemetry Sample(string activeProcessName)
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                bool sameProc = string.Equals(_cachedProcessName ?? "", activeProcessName ?? "", StringComparison.OrdinalIgnoreCase);
                if (sameProc && (now - _lastSampleTime).TotalMilliseconds < 2000 && _cached.IsAvailable)
                {
                    return _cached;
                }
                _lastSampleTime = now;
                _cachedProcessName = activeProcessName;

                RadeonSettingsTelemetry t = new RadeonSettingsTelemetry();
                try
                {
                    if (_discoveredAdapterKey == null)
                    {
                        _discoveredAdapterKey = FindAmdDisplayAdapterKey();
                    }

                    if (string.IsNullOrEmpty(_discoveredAdapterKey))
                    {
                        _cached = t;
                        return t;
                    }

                    using (RegistryKey kmdKey = Registry.LocalMachine.OpenSubKey(_discoveredAdapterKey, false))
                    {
                        if (kmdKey != null)
                        {
                            t.IsAvailable = true;
                            t.GpuDesc = kmdKey.GetValue("DriverDesc") as string;
                            t.SoftwareEdition = kmdKey.GetValue("RadeonSoftwareEdition") as string;
                            t.AdrenalinInstalled = !string.IsNullOrEmpty(t.SoftwareEdition);
                            t.SoftwareVersion = kmdKey.GetValue("RadeonSoftwareVersion") as string;
                            if (string.IsNullOrEmpty(t.SoftwareVersion))
                                t.SoftwareVersion = kmdKey.GetValue("Catalyst_Version") as string;
                            if (string.IsNullOrEmpty(t.SoftwareVersion))
                                t.SoftwareVersion = kmdKey.GetValue("DriverVersion") as string;
                            if (string.IsNullOrEmpty(t.SoftwareVersion))
                                t.SoftwareVersion = kmdKey.GetValue("ReleaseVersion") as string;

                            int deLag = GetDword(kmdKey, "KMD_DeLagEnabled");
                            int chill = GetDword(kmdKey, "KMD_ChillEnabled");
                            int boost = GetDword(kmdKey, "KMD_RadeonBoostEnabled");
                            int boostVrs = GetDword(kmdKey, "KMD_BoostAdaptiveVrsEnabled");
                            int frt = GetDword(kmdKey, "KMD_FRTEnabled");
                            int maxFps = GetDword(kmdKey, "KMD_MaxFrameRateRequested");
                            int rebar = GetDword(kmdKey, "KMD_RebarControlMode");
                            int fsEnable = GetDword(kmdKey, "KMD_FreeSyncEnabled");
                            int rsrEnable = GetDword(kmdKey, "Gmx_RSREnabled");
                            int risEnable = GetDword(kmdKey, "KMD_ImageSharpeningEnabled");
                            int gmxRis = GetDword(kmdKey, "Gmx_ImageSharpeningEnabled");
                            int fsrEnable = GetDword(kmdKey, "KMD_FSREnabled");
                            if (fsrEnable == 0) fsrEnable = GetDword(kmdKey, "Gmx_FSREnabled");
                            if (fsrEnable == 0) fsrEnable = GetDword(kmdKey, "KMD_FsrOverride");
                            if (fsrEnable == 0) fsrEnable = GetDword(kmdKey, "FsrEnabled");

                            t.AntiLag = (deLag != 0);
                            t.Chill = (chill != 0);
                            t.RadeonBoost = (boost != 0 || boostVrs != 0);
                            t.Frtc = (frt != 0);
                            t.FrtcFps = maxFps;
                            t.SmartAccessMemory = (rebar != 0);
                            t.Rsr = (rsrEnable != 0);
                            t.Ris = (risEnable != 0 || gmxRis != 0);
                            if (fsrEnable != 0) t.Fsr = true;
                            if (fsEnable != 0) t.FreeSync = true;

                            // Inspect UMD (User-Mode Driver) for AFMF, EnhancedSync, and Sharpening
                            using (RegistryKey umdKey = kmdKey.OpenSubKey("UMD", false))
                            {
                                if (umdKey != null)
                                {
                                    int turboSync = GetDword(umdKey, "TurboSync");
                                    if (turboSync != 0) t.EnhancedSync = true;

                                    if (IsStringFlagEnabled(umdKey.GetValue("DrvFrameGenEnabled")) ||
                                        IsStringFlagEnabled(umdKey.GetValue("DrvFrameGenAlgorithm")))
                                    {
                                        t.Afmf = true;
                                    }

                                    int cas = GetDword(umdKey, "CAS_Enable");
                                    int sharp = GetDword(umdKey, "Sharpness_Enable");
                                    if (cas != 0 || sharp != 0) t.Ris = true;

                                    // Live Adrenalin 26.x toggle for "AMD FSR Upscaling" (ML-based FSR upgrade):
                                    // UMD\FsrOverride flips cleanly between "0" (Disabled) and "1" (Enabled).
                                    if (IsStringFlagEnabled(umdKey.GetValue("FsrOverride")))
                                    {
                                        t.Fsr = true;
                                    }
                                    int fsrActive = GetDword(umdKey, "FsrEnabled");
                                    if (fsrActive == 0) fsrActive = GetDword(umdKey, "ML_FSR_Enable");
                                    if (fsrActive == 0) fsrActive = GetDword(umdKey, "FsrUpgrade");
                                    if (fsrActive != 0) t.Fsr = true;
                                }
                            }

                            // Inspect user-level Adrenalin DVR / CN settings in HKCU if accessible
                            try
                            {
                                using (RegistryKey hkcuAmd = Registry.CurrentUser.OpenSubKey(@"Software\AMD", false))
                                {
                                    if (hkcuAmd != null)
                                    {
                                        using (RegistryKey fsKey = hkcuAmd.OpenSubKey(@"CN\FreeSync", false))
                                        {
                                            if (fsKey != null)
                                            {
                                                int en = GetDword(fsKey, "FreeSyncEnabled");
                                                if (en != 0) t.FreeSync = true;
                                            }
                                        }
                                        using (RegistryKey fsrKey = hkcuAmd.OpenSubKey(@"CN\FSR", false))
                                        {
                                            if (fsrKey != null)
                                            {
                                                int en = GetDword(fsrKey, "FSREnabled");
                                                if (en == 0) en = GetDword(fsrKey, "Enabled");
                                                if (en != 0) t.Fsr = true;
                                            }
                                        }
                                        using (RegistryKey dvrKey = hkcuAmd.OpenSubKey("DVR", false))
                                        {
                                            if (dvrKey != null)
                                            {
                                                int fg = GetDword(dvrKey, "ToggleFrameGenEnabled");
                                                if (fg != 0) t.Afmf = true;
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }

                            // Corroborate with live Adrenalin profile & gaming experience databases if accessible
                            TryCorroborateWithAdrenalinDatabase(ref t, activeProcessName);

                            // Authoritative: the driver's per-display VRR switch (Radeon Software > Display > VRR)
                            // lives in DAL3_DATA\freesync_v1\EDID_*\userenable (0 = off, non-zero = on).
                            // When present it overrides every heuristic above so toggling VRR is reflected live,
                            // and monitor capability (EDID) is never mistaken for "FreeSync enabled".
                            bool? vrrUser = ReadDriverVrrUserEnable(kmdKey);
                            if (vrrUser.HasValue) t.FreeSync = vrrUser.Value;

                            t.Summary = RadeonSettingsTelemetry.BuildSummary(
                                t.FreeSync, t.AntiLag, t.Afmf, t.RadeonBoost, t.Chill, t.Rsr, t.Ris, t.Fsr, t.EnhancedSync, t.SmartAccessMemory, t.Frtc, t.FrtcFps);
                        }
                    }
                }
                catch (Exception)
                {
                    // Fail-safe: Non-Radeon platforms or restricted registry access
                }

                _cached = t;
                return t;
            }
        }

        internal static bool IsStringFlagEnabled(object val)
        {
            if (val == null) return false;
            if (val is int) return ((int)val) != 0;
            string s = val.ToString().Trim();
            if (s.Length == 0) return false;
            char c = s[0];
            return (c >= '1' && c <= '9');
        }

        private static string SafeReadAllText(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static void TryCorroborateWithAdrenalinDatabase(ref RadeonSettingsTelemetry t, string activeProcessName)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData)) return;

                string gm = SafeReadAllText(Path.Combine(localAppData, @"AMD\CN\gmdb.blb"));
                string gex = SafeReadAllText(Path.Combine(localAppData, @"AMD\CN\GExDb.blb"));
                if (!string.IsNullOrEmpty(gm) || !string.IsNullOrEmpty(gex))
                    t.AdrenalinInstalled = true;
                TryCorroborateFromAdrenalinJson(ref t, gm, gex, activeProcessName);
            }
            catch
            {
                // File access restrictions or fallback to driver registry values
            }
        }

        internal static void TryCorroborateFromAdrenalinJson(ref RadeonSettingsTelemetry t, string gm, string gex, string activeProcessName)
        {
            string activeGameGuid = null;

            // 1. Inspect AMD Game Manager Database (gmdb.blb) ONLY for the currently running game (never leak idle per-game entries or capability flags like FSRUpgradeAvailable)
            if (!string.IsNullOrEmpty(gm) && !string.IsNullOrEmpty(activeProcessName))
            {
                string gameBlock = FindGmdbGameBlock(gm, activeProcessName);
                if (!string.IsNullOrEmpty(gameBlock))
                {
                    Match guidM = Regex.Match(gameBlock, @"""guid""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (guidM.Success)
                        activeGameGuid = guidM.Groups[1].Value;

                    if (Regex.IsMatch(gameBlock, @"""framegen_enabled""\s*:\s*[1-9]"))
                        t.Afmf = true;
                    if (Regex.IsMatch(gameBlock, @"""upscaling_enabled""\s*:\s*(?:""TRUE""|1)", RegexOptions.IgnoreCase))
                        t.Rsr = true;
                    if (Regex.IsMatch(gameBlock, @"""sharpening_enabled""\s*:\s*(?:""TRUE""|1)", RegexOptions.IgnoreCase))
                        t.Ris = true;
                    if (Regex.IsMatch(gameBlock, @"""(?:fsr_enabled|fsr|fsr_upgrade|ml_fsr|amd_fsr)""\s*:\s*""?(?:TRUE|1|on)""?", RegexOptions.IgnoreCase))
                        t.Fsr = true;
                }
            }

            // 2. Inspect AMD Gaming Experience Database (GExDb.blb) scoped strictly to Global ("") + running game's GUID
            if (!string.IsNullOrEmpty(gex))
            {
                string globalBlock = ExtractTopLevelJsonObject(gex, "");
                if (!string.IsNullOrEmpty(globalBlock))
                    ApplyGexFeatureBlock(ref t, globalBlock);

                if (!string.IsNullOrEmpty(activeGameGuid))
                {
                    string gameGexBlock = ExtractTopLevelJsonObject(gex, activeGameGuid);
                    if (!string.IsNullOrEmpty(gameGexBlock))
                        ApplyGexFeatureBlock(ref t, gameGexBlock);
                }
            }
        }

        private static void ApplyGexFeatureBlock(ref RadeonSettingsTelemetry t, string block)
        {
            bool? gexChill = null;
            bool? gexAntiLag = null;
            bool? gexBoost = null;
            bool? gexRsr = null;
            bool? gexRis = null;
            bool? gexFsr = null;
            bool? gexVrr = null;

            MatchCollection mc = Regex.Matches(block, @"""FeatureID""\s*:\s*(\d+)\s*,\s*""FeatureValue""\s*:\s*""?([^"",}]+)""?");
            foreach (Match m in mc)
            {
                int fid;
                if (int.TryParse(m.Groups[1].Value, out fid))
                {
                    string val = m.Groups[2].Value.Trim().Trim('"');
                    bool isEnabled = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase));

                    if (fid == 35) gexChill = isEnabled;        // Radeon Chill
                    if (fid == 33) gexAntiLag = isEnabled;      // Radeon Anti-Lag
                    if (fid == 30) gexBoost = isEnabled;        // Radeon Boost
                    if (fid == 13) gexRsr = isEnabled;          // Radeon Super Resolution
                    if (fid == 15 || fid == 27) gexRis = isEnabled; // Radeon Image Sharpening (Adrenalin 26.x uses FeatureID 27)
                    if (fid == 58 || fid == 66) gexFsr = isEnabled; // AMD FSR Upscaling (ML-based FSR Upgrade)
                }
            }

            if (gexChill.HasValue) t.Chill = gexChill.Value;
            if (gexAntiLag.HasValue) t.AntiLag = gexAntiLag.Value;
            if (gexBoost.HasValue) t.RadeonBoost = gexBoost.Value;
            if (gexRsr.HasValue && gexRsr.Value) t.Rsr = true;
            if (gexRis.HasValue && gexRis.Value) t.Ris = true;
            if (gexFsr.HasValue) t.Fsr = gexFsr.Value;
            if (gexVrr.HasValue) t.FreeSync = gexVrr.Value;
        }

        private static string FindGmdbGameBlock(string gm, string activeProcessName)
        {
            string targetExe;
            try { targetExe = Path.GetFileName(activeProcessName).Trim(); }
            catch { targetExe = activeProcessName.Trim(); }
            if (string.IsNullOrEmpty(targetExe)) return null;
            string targetBase = Path.GetFileNameWithoutExtension(targetExe);

            MatchCollection exeMatches = Regex.Matches(gm, @"""exe_path""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (Match m in exeMatches)
            {
                string rawPath = m.Groups[1].Value.Replace(@"\\", @"\");
                string exeName;
                try { exeName = Path.GetFileName(rawPath); }
                catch { exeName = rawPath; }
                string exeBase = Path.GetFileNameWithoutExtension(exeName);

                if (string.Equals(exeName, targetExe, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(targetBase) && string.Equals(exeBase, targetBase, StringComparison.OrdinalIgnoreCase)))
                {
                    // Walk backwards from before the opening '"' of "exe_path" to the enclosing '{'
                    int openIdx = -1;
                    int depth = 0;
                    bool inStr = false;
                    for (int i = m.Index - 1; i >= 0; i--)
                    {
                        char c = gm[i];
                        if (c == '"' && (i == 0 || gm[i - 1] != '\\')) inStr = !inStr;
                        if (inStr) continue;
                        if (c == '}') depth++;
                        else if (c == '{')
                        {
                            if (depth == 0) { openIdx = i; break; }
                            depth--;
                        }
                    }
                    if (openIdx < 0) continue;

                    int closeIdx = FindMatchingBrace(gm, openIdx);
                    if (closeIdx > openIdx)
                        return gm.Substring(openIdx, closeIdx - openIdx + 1);
                }
            }
            return null;
        }

        private static string ExtractTopLevelJsonObject(string json, string topKey)
        {
            string pattern = "\"" + Regex.Escape(topKey) + "\"\\s*:\\s*\\{";
            MatchCollection matches = Regex.Matches(json, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                int braceIdx = m.Index + m.Length - 1;
                // Verify this key is at top-level (brace depth == 1 right before '{')
                int depth = 0;
                bool inStr = false;
                for (int i = 0; i < braceIdx; i++)
                {
                    char c = json[i];
                    if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                    if (inStr) continue;
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                if (depth != 1) continue;

                int closeIdx = FindMatchingBrace(json, braceIdx);
                if (closeIdx > braceIdx)
                    return json.Substring(braceIdx, closeIdx - braceIdx + 1);
            }
            return null;
        }

        private static int FindMatchingBrace(string s, int openIdx)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Reads the Adrenalin per-display VRR toggle. Returns null when the driver exposes no such key
        /// (older drivers), true when any display has VRR enabled, false when all are disabled.
        /// </summary>
        private static bool? ReadDriverVrrUserEnable(RegistryKey adapterKey)
        {
            try
            {
                using (RegistryKey fs = adapterKey.OpenSubKey(@"DAL3_DATA\freesync_v1", false))
                {
                    if (fs == null) return null;
                    bool any = false;
                    bool found = false;
                    foreach (string name in fs.GetSubKeyNames())
                    {
                        using (RegistryKey disp = fs.OpenSubKey(name, false))
                        {
                            if (disp == null) continue;
                            object v = disp.GetValue("userenable");
                            if (v == null) continue;
                            found = true;
                            byte[] b = v as byte[];
                            if (b != null)
                            {
                                foreach (byte x in b) { if (x != 0) { any = true; break; } }
                            }
                            else if (GetDword(disp, "userenable") != 0)
                            {
                                any = true;
                            }
                        }
                    }
                    return found ? (bool?)any : null;
                }
            }
            catch { }
            return null;
        }

        private static bool CheckDisplayFreeSyncSupport()
        {
            try
            {
                using (RegistryKey displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
                {
                    if (displayKey == null) return false;
                    foreach (string monName in displayKey.GetSubKeyNames())
                    {
                        using (RegistryKey mon = displayKey.OpenSubKey(monName))
                        {
                            if (mon == null) continue;
                            foreach (string instName in mon.GetSubKeyNames())
                            {
                                using (RegistryKey devParams = mon.OpenSubKey(instName + @"\Device Parameters"))
                                {
                                    if (devParams == null) continue;
                                    byte[] edid = devParams.GetValue("EDID") as byte[];
                                    if (edid != null && edid.Length >= 128)
                                    {
                                        // Check for AMD FreeSync Vendor-Specific Data Block (IEEE OUI 00-00-1A)
                                        for (int i = 0; i < edid.Length - 3; i++)
                                        {
                                            if (edid[i] == 0x1A && edid[i + 1] == 0x00 && edid[i + 2] == 0x00)
                                            {
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static int GetDword(RegistryKey key, string name)
        {
            try
            {
                object v = key.GetValue(name);
                if (v is int) return (int)v;
                int parsed;
                if (v != null && int.TryParse(v.ToString(), out parsed)) return parsed;
            }
            catch { }
            return 0;
        }

        public static bool IsDiscreteGpuDescription(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return false;
            string d = desc.Trim();

            // Explicit integrated GPU patterns (Ryzen Mobile/Desktop iGPUs & Vega APUs)
            if (d.IndexOf("780M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("890M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("880M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("860M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("840M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("760M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("740M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("680M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("660M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("610M", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Vega 8", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Vega 7", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Vega 6", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Vega 3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // Explicit discrete GPU patterns (AMD Radeon RX / PRO / VII, NVIDIA GeForce / RTX, Intel Arc)
            if (d.IndexOf("RX ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Radeon PRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Radeon VII", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Fury", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("R9 ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("R7 ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("GTX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Quadro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Arc A", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Arc B", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.IndexOf("Arc Pro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string FindAmdDisplayAdapterKey()
        {
            try
            {
                const string classRoot = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(classRoot, false))
                {
                    if (root == null) return null;
                    string firstAmdKey = null;
                    string[] subKeys = root.GetSubKeyNames();
                    foreach (string sub in subKeys)
                    {
                        if (sub.Length == 4 && char.IsDigit(sub[0]))
                        {
                            using (RegistryKey adapter = root.OpenSubKey(sub, false))
                            {
                                if (adapter != null)
                                {
                                    string provider = adapter.GetValue("ProviderName") as string;
                                    string desc = adapter.GetValue("DriverDesc") as string;
                                    if ((provider != null && provider.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (desc != null && (desc.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)))
                                    {
                                        string fullKey = classRoot + @"\" + sub;
                                        if (IsDiscreteGpuDescription(desc))
                                        {
                                            // Prefer discrete Radeon GPU over integrated Ryzen iGPU on desktop dual-GPU systems
                                            return fullKey;
                                        }
                                        if (firstAmdKey == null)
                                        {
                                            firstAmdKey = fullKey;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return firstAmdKey;
                }
            }
            catch { }
            return null;
        }
    }
}
