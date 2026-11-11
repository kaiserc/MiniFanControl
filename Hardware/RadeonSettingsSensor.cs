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
        public string Summary;             // e.g. "Anti-Lag • AFMF 2 • RSR • RIS"

        public static string BuildSummary(bool freeSync, bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool ris, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            List<string> active = new List<string>(10);
            if (freeSync) active.Add("FreeSync");
            if (antiLag) active.Add("Anti-Lag");
            if (afmf) active.Add("AFMF 2");
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

        public static string BuildSummary(bool freeSync, bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            return BuildSummary(freeSync, antiLag, afmf, boost, chill, rsr, false, enhSync, sam, frtc, frtcFps);
        }

        public static string BuildSummary(bool antiLag, bool afmf, bool boost, bool chill, bool rsr, bool enhSync, bool sam, bool frtc, int frtcFps)
        {
            return BuildSummary(false, antiLag, afmf, boost, chill, rsr, false, enhSync, sam, frtc, frtcFps);
        }

        public List<string> GetActiveTags()
        {
            List<string> tags = new List<string>(7);
            if (FreeSync) tags.Add("FreeSync");
            if (AntiLag) tags.Add("Anti-Lag");
            if (Afmf) tags.Add("AFMF");
            if (Chill) tags.Add("Chill");
            if (RadeonBoost) tags.Add("Boost");
            if (Rsr) tags.Add("RSR");
            if (Ris) tags.Add("RIS");
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
        private static readonly object _lock = new object();
        private static string _discoveredAdapterKey = null;

        public static RadeonSettingsTelemetry Sample()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastSampleTime).TotalMilliseconds < 2000 && _cached.IsAvailable)
                {
                    return _cached;
                }
                _lastSampleTime = now;

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
                            t.SoftwareVersion = kmdKey.GetValue("RadeonSoftwareVersion") as string;

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

                            t.AntiLag = (deLag != 0);
                            t.Chill = (chill != 0);
                            t.RadeonBoost = (boost != 0 || boostVrs != 0);
                            t.Frtc = (frt != 0);
                            t.FrtcFps = maxFps;
                            t.SmartAccessMemory = (rebar != 0);
                            t.Rsr = (rsrEnable != 0);
                            t.Ris = (risEnable != 0 || gmxRis != 0);
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

                            // Also check Windows 11 Variable Refresh Rate (VRR / FreeSync)
                            try
                            {
                                using (RegistryKey vrrKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\GraphicsDrivers", false))
                                {
                                    if (vrrKey != null)
                                    {
                                        int vrr = GetDword(vrrKey, "VariableRefreshRate");
                                        if (vrr != 0) t.FreeSync = true;
                                    }
                                }
                            }
                            catch { }

                            // Detect active AMD FreeSync / VRR capability from connected display EDID (OUI 00-00-1A)
                            if (!t.FreeSync)
                            {
                                t.FreeSync = CheckDisplayFreeSyncSupport();
                            }

                            // Corroborate with live Adrenalin profile & gaming experience databases if accessible
                            TryCorroborateWithAdrenalinDatabase(ref t);

                            t.Summary = RadeonSettingsTelemetry.BuildSummary(
                                t.FreeSync, t.AntiLag, t.Afmf, t.RadeonBoost, t.Chill, t.Rsr, t.Ris, t.EnhancedSync, t.SmartAccessMemory, t.Frtc, t.FrtcFps);
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

        private static bool IsStringFlagEnabled(object val)
        {
            if (val == null) return false;
            if (val is int) return ((int)val) != 0;
            string s = val.ToString().Trim();
            if (s.Length == 0) return false;
            char c = s[0];
            return (c >= '1' && c <= '9');
        }

        private static void TryCorroborateWithAdrenalinDatabase(ref RadeonSettingsTelemetry t)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData)) return;

                // 1. Inspect AMD Game Manager Database (gmdb.blb) for AFMF (Fluid Motion Frames) & RSR Upscaling
                string gmPath = Path.Combine(localAppData, @"AMD\CN\gmdb.blb");
                if (File.Exists(gmPath))
                {
                    string gm = File.ReadAllText(gmPath);
                    if (!string.IsNullOrEmpty(gm))
                    {
                        if (Regex.IsMatch(gm, @"""framegen_enabled"":\s*[1-9]"))
                        {
                            t.Afmf = true;
                        }
                        if (Regex.IsMatch(gm, @"""upscaling_enabled"":\s*(?:""TRUE""|1)", RegexOptions.IgnoreCase))
                        {
                            t.Rsr = true;
                        }
                        if (Regex.IsMatch(gm, @"""sharpening_enabled"":\s*(?:""TRUE""|1)", RegexOptions.IgnoreCase))
                        {
                            t.Ris = true;
                        }
                    }
                }

                // 2. Inspect AMD Gaming Experience Database (GExDb.blb) for active feature toggles
                string gexPath = Path.Combine(localAppData, @"AMD\CN\GExDb.blb");
                if (File.Exists(gexPath))
                {
                    string gex = File.ReadAllText(gexPath);
                    if (!string.IsNullOrEmpty(gex))
                    {
                        bool? gexChill = null;
                        bool? gexAntiLag = null;
                        bool? gexBoost = null;
                        bool? gexRsr = null;
                        bool? gexRis = null;
                        bool? gexVrr = null;

                        MatchCollection mc = Regex.Matches(gex, @"""FeatureID"":\s*(\d+),\s*""FeatureValue"":\s*""([^""]+)""");
                        foreach (Match m in mc)
                        {
                            int fid;
                            if (int.TryParse(m.Groups[1].Value, out fid))
                            {
                                string val = m.Groups[2].Value.Trim();
                                bool isEnabled = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase));

                                if (fid == 35) gexChill = (gexChill == true) ? true : isEnabled;        // Radeon Chill
                                if (fid == 33) gexAntiLag = (gexAntiLag == true) ? true : isEnabled;    // Radeon Anti-Lag
                                if (fid == 30) gexBoost = (gexBoost == true) ? true : isEnabled;        // Radeon Boost
                                if (fid == 13) gexRsr = (gexRsr == true) ? true : isEnabled;            // Radeon Super Resolution
                                if (fid == 15) gexRis = (gexRis == true) ? true : isEnabled;            // Radeon Image Sharpening
                                if (fid == 27) gexVrr = (gexVrr == true) ? true : isEnabled;            // Variable Refresh Rate (VRR / FreeSync)
                            }
                        }

                        if (gexChill.HasValue) t.Chill = gexChill.Value;
                        if (gexAntiLag.HasValue) t.AntiLag = gexAntiLag.Value;
                        if (gexBoost.HasValue) t.RadeonBoost = gexBoost.Value;
                        if (gexRsr.HasValue && gexRsr.Value) t.Rsr = true;
                        if (gexRis.HasValue && gexRis.Value) t.Ris = true;
                        if (gexVrr.HasValue) t.FreeSync = gexVrr.Value;
                    }
                }
            }
            catch
            {
                // File access restrictions or fallback to driver registry values
            }
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

        private static string FindAmdDisplayAdapterKey()
        {
            try
            {
                const string classRoot = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(classRoot, false))
                {
                    if (root == null) return null;
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
                                        return classRoot + @"\" + sub;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
