using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public class DeviceProfile
    {
        public string Id;
        public string DisplayName;
        public string ChipsetNotes;
        public ulong EcBaseAddress = 0xFE0B0400UL;
        public uint EcSize = 0x100;
        public byte RegModeStatus = 0x31;
        public byte RegModeCmd    = 0x32;
        public byte RegFan1Ctrl   = 0x33;
        public byte RegFan2Ctrl   = 0x34;
        public byte RegFan1Hi     = 0x35;
        public byte RegFan1Lo     = 0x36;
        public byte RegFan2Hi     = 0x37;
        public byte RegFan2Lo     = 0x38;
        public byte RegCpuTemp    = 0x70;
        public byte RegGpuTemp    = 0x71;
        public byte RegVrmTemp    = 0x72; // Candidate VRM MOSFET power stage thermistor (0x00 if unmapped)
        public byte RegAmbientTemp = 0x74; // Candidate chassis/board ambient thermistor (0x00 if unmapped)
        public byte RegDdrTemp    = 0x75; // Candidate DDR5 thermistor (0x00 if unmapped)
        public byte RegDcVoltsAdc = 0xE0; // Candidate DC barrel jack voltage ADC rail (0x00 if unmapped)
        public bool HasDualFans   = true;
        public bool IsReadOnly    = false;
        public bool IsVerified    = true;
        public string BoundManufacturer = "";
        public string BoundProductName  = "";
        public string BoundBaseBoard    = "";

        public static readonly DeviceProfile[] KnownProfiles = new DeviceProfile[]
        {
            new DeviceProfile
            {
                Id = "gmktec-k12",
                DisplayName = "GMKtec NucBox K12 (Ryzen 7 H 255 / Radeon 780M)",
                ChipsetNotes = "ITE IT5570E EC @ 0xFE0B0400 • Dual Fans (APU + RAM/NVMe)"
            },
            new DeviceProfile
            {
                Id = "gmktec-k8-k11",
                DisplayName = "GMKtec NucBox K8 Plus / K11 (Ryzen 8845HS / 8945HS)",
                ChipsetNotes = "ITE IT5570E EC @ 0xFE0B0400 • Dual Fans (APU + System)"
            },
            new DeviceProfile
            {
                Id = "ryzen-hx370-ec",
                DisplayName = "Ryzen AI 9 HX 370 Mini PC (GMKtec EVO-X1 / TOPC TS-12C)",
                ChipsetNotes = "ITE IT8613E Super I/O @ 0x2E/0x0A30 + EC • Single/Dual Blower • Radeon 890M via ADL2 / PMLOG",
                HasDualFans = true
            },
            new DeviceProfile
            {
                Id = "adreamer-ariesbox10",
                DisplayName = "ADREAMER AriesBox 10 / PB07 (Ryzen 7 7840HS / Radeon 780M)",
                ChipsetNotes = "Dual Fans (APU + RAM/System) • Radeon 780M via ADL2 • AMD Zen 4 Phoenix"
            },
            new DeviceProfile
            {
                Id = "generic-it5570e",
                DisplayName = "Generic AMD Ryzen Mini PC (ITE IT5570E / AMI Aptio V)",
                ChipsetNotes = "Standard AMD LPC/eSPI EC window @ 0xFE0B0400"
            },
            new DeviceProfile
            {
                Id = "unknown-safe",
                DisplayName = "Unverified Hardware (Safe Read-Only Monitoring Mode)",
                ChipsetNotes = "Hardware registers unverified. Fan speed writes are disabled to prevent hardware damage.",
                IsReadOnly = true,
                IsVerified = false,
                HasDualFans = false
            }
        };

        public static DeviceProfile CustomProfile = null;
        public static readonly string CUSTOM_PROFILE_FILENAME = "custom-profile.json";

        public static List<DeviceProfile> GetAllProfiles()
        {
            List<DeviceProfile> list = new List<DeviceProfile>();
            if (CustomProfile != null)
            {
                list.Add(CustomProfile);
            }
            list.AddRange(KnownProfiles);
            return list;
        }

        public static DeviceProfile FindProfile(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                if (CustomProfile != null && (CustomProfile.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || id.Equals("custom", StringComparison.OrdinalIgnoreCase)))
                {
                    return CustomProfile;
                }
                foreach (DeviceProfile p in KnownProfiles)
                {
                    if (p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            foreach (DeviceProfile p in KnownProfiles)
            {
                if (p.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return KnownProfiles[KnownProfiles.Length - 1];
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine(string.Format("  \"id\": \"{0}\",", EscapeJson(Id)));
            sb.AppendLine(string.Format("  \"displayName\": \"{0}\",", EscapeJson(DisplayName)));
            sb.AppendLine(string.Format("  \"chipsetNotes\": \"{0}\",", EscapeJson(ChipsetNotes)));
            sb.AppendLine(string.Format("  \"ecBaseAddress\": \"0x{0:X8}\",", EcBaseAddress));
            sb.AppendLine(string.Format("  \"ecSize\": \"0x{0:X}\",", EcSize));
            sb.AppendLine(string.Format("  \"regModeStatus\": \"0x{0:X2}\",", RegModeStatus));
            sb.AppendLine(string.Format("  \"regModeCmd\": \"0x{0:X2}\",", RegModeCmd));
            sb.AppendLine(string.Format("  \"regFan1Ctrl\": \"0x{0:X2}\",", RegFan1Ctrl));
            sb.AppendLine(string.Format("  \"regFan2Ctrl\": \"0x{0:X2}\",", RegFan2Ctrl));
            sb.AppendLine(string.Format("  \"regFan1Hi\": \"0x{0:X2}\",", RegFan1Hi));
            sb.AppendLine(string.Format("  \"regFan1Lo\": \"0x{0:X2}\",", RegFan1Lo));
            sb.AppendLine(string.Format("  \"regFan2Hi\": \"0x{0:X2}\",", RegFan2Hi));
            sb.AppendLine(string.Format("  \"regFan2Lo\": \"0x{0:X2}\",", RegFan2Lo));
            sb.AppendLine(string.Format("  \"regCpuTemp\": \"0x{0:X2}\",", RegCpuTemp));
            sb.AppendLine(string.Format("  \"regGpuTemp\": \"0x{0:X2}\",", RegGpuTemp));
            sb.AppendLine(string.Format("  \"regVrmTemp\": \"0x{0:X2}\",", RegVrmTemp));
            sb.AppendLine(string.Format("  \"regAmbientTemp\": \"0x{0:X2}\",", RegAmbientTemp));
            sb.AppendLine(string.Format("  \"regDdrTemp\": \"0x{0:X2}\",", RegDdrTemp));
            sb.AppendLine(string.Format("  \"regDcVoltsAdc\": \"0x{0:X2}\",", RegDcVoltsAdc));
            sb.AppendLine(string.Format("  \"hasDualFans\": {0},", HasDualFans ? "true" : "false"));
            sb.AppendLine(string.Format("  \"isReadOnly\": {0},", IsReadOnly ? "true" : "false"));
            sb.AppendLine(string.Format("  \"isVerified\": {0},", IsVerified ? "true" : "false"));
            sb.AppendLine(string.Format("  \"boundManufacturer\": \"{0}\",", EscapeJson(BoundManufacturer)));
            sb.AppendLine(string.Format("  \"boundProductName\": \"{0}\",", EscapeJson(BoundProductName)));
            sb.AppendLine(string.Format("  \"boundBaseBoard\": \"{0}\"", EscapeJson(BoundBaseBoard)));
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string ExportKnownProfilesJson(string appVersion)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"source\": \"kaiserc/MiniFanControl\",");
            sb.AppendLine(string.Format("  \"appVersion\": \"{0}\",", EscapeJson(appVersion ?? "")));
            sb.AppendLine("  \"profiles\": [");

            List<DeviceProfile> profiles = GetAllProfiles();
            for (int i = 0; i < profiles.Count; i++)
            {
                DeviceProfile p = profiles[i];
                sb.AppendLine("    {");
                sb.AppendLine(string.Format("      \"id\": \"{0}\",", EscapeJson(p.Id)));
                sb.AppendLine(string.Format("      \"displayName\": \"{0}\",", EscapeJson(p.DisplayName)));
                sb.AppendLine(string.Format("      \"chipsetNotes\": \"{0}\",", EscapeJson(p.ChipsetNotes)));
                sb.AppendLine(string.Format("      \"ecBaseAddress\": \"0x{0:X8}\",", p.EcBaseAddress));
                sb.AppendLine(string.Format("      \"hasDualFans\": {0},", p.HasDualFans ? "true" : "false"));
                sb.AppendLine(string.Format("      \"isReadOnly\": {0},", p.IsReadOnly ? "true" : "false"));
                sb.AppendLine(string.Format("      \"isVerified\": {0},", p.IsVerified ? "true" : "false"));
                sb.AppendLine("      \"registers\": {");
                sb.AppendLine(string.Format("        \"modeStatus\": \"0x{0:X2}\",", p.RegModeStatus));
                sb.AppendLine(string.Format("        \"modeCmd\": \"0x{0:X2}\",", p.RegModeCmd));
                sb.AppendLine(string.Format("        \"fan1Ctrl\": \"0x{0:X2}\",", p.RegFan1Ctrl));
                sb.AppendLine(string.Format("        \"fan2Ctrl\": \"0x{0:X2}\",", p.RegFan2Ctrl));
                sb.AppendLine(string.Format("        \"fan1Hi\": \"0x{0:X2}\",", p.RegFan1Hi));
                sb.AppendLine(string.Format("        \"fan1Lo\": \"0x{0:X2}\",", p.RegFan1Lo));
                sb.AppendLine(string.Format("        \"fan2Hi\": \"0x{0:X2}\",", p.RegFan2Hi));
                sb.AppendLine(string.Format("        \"fan2Lo\": \"0x{0:X2}\",", p.RegFan2Lo));
                sb.AppendLine(string.Format("        \"cpuTemp\": \"0x{0:X2}\",", p.RegCpuTemp));
                sb.AppendLine(string.Format("        \"gpuTemp\": \"0x{0:X2}\"", p.RegGpuTemp));
                sb.AppendLine("      }");
                sb.AppendLine(i < profiles.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"probedSuperIoFamilies\": {");
            sb.AppendLine("    \"iteIdPrefixes\": [\"0x86xx\", \"0x87xx\", \"0x55xx\"],");
            sb.AppendLine("    \"nuvotonIdPrefixes\": [\"0xC3xx\", \"0xC5xx\", \"0xC8xx\", \"0xD4xx\", \"0xD8xx\", \"0xB473\", \"0xB571\"]");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string ExportKnownProfilesTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MiniFanControl Hardware Profile Registry:");
            sb.AppendLine("----------------------------------------------------------------------");
            foreach (DeviceProfile p in GetAllProfiles())
            {
                sb.AppendLine(string.Format("  {0,-22} | {1} | Dual={2} | {3}",
                    p.Id,
                    p.IsVerified ? "Verified" : "ReadOnly",
                    p.HasDualFans ? "Yes" : "No ",
                    p.DisplayName));
                if (!string.IsNullOrEmpty(p.ChipsetNotes))
                {
                    sb.AppendLine(string.Format("    Notes: {0}", p.ChipsetNotes));
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");
            sb.AppendLine("Probed Super I/O Chip ID Prefixes (LPC 0x2E / 0x4E):");
            sb.AppendLine("  ITE     : 0x86xx, 0x87xx, 0x55xx");
            sb.AppendLine("  Nuvoton : 0xC3xx, 0xC5xx, 0xC8xx, 0xD4xx, 0xD8xx, 0xB473, 0xB571");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            return SimpleJson.Escape(s);
        }

        private static bool IsMeaningfulDmiToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string t = value.Trim();
            return !t.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                && !t.Equals("Default string", StringComparison.OrdinalIgnoreCase)
                && !t.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase);
        }

        public bool HasValidRegisterMap()
        {
            return EcBaseAddress >= NativeInpOut.MIN_SAFE_PHYSICAL_ADDR
                && EcSize > 0
                && EcSize <= 0x10000
                && RegCpuTemp != 0
                && RegFan1Ctrl != 0;
        }

        public bool MatchesDmi(string productName, string baseBoard, string manufacturer = null)
        {
            bool hasProd = IsMeaningfulDmiToken(BoundProductName);
            bool hasBoard = IsMeaningfulDmiToken(BoundBaseBoard);
            bool hasMfg = IsMeaningfulDmiToken(BoundManufacturer);

            // Fail-closed: Unbound custom profiles must never auto-match arbitrary hardware
            if (!hasProd && !hasBoard && !hasMfg)
                return false;

            int matchedFields = 0;

            if (hasProd)
            {
                if (string.IsNullOrWhiteSpace(productName) ||
                    !BoundProductName.Trim().Equals(productName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                matchedFields++;
            }

            if (hasBoard)
            {
                if (string.IsNullOrWhiteSpace(baseBoard) ||
                    !BoundBaseBoard.Trim().Equals(baseBoard.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                matchedFields++;
            }

            if (hasMfg)
            {
                if (manufacturer != null)
                {
                    if (string.IsNullOrWhiteSpace(manufacturer) ||
                        !BoundManufacturer.Trim().Equals(manufacturer.Trim(), StringComparison.OrdinalIgnoreCase))
                        return false;
                    matchedFields++;
                }
                else if (!hasProd && !hasBoard)
                {
                    return false;
                }
            }

            return matchedFields > 0;
        }

        public static DeviceProfile FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (json.IndexOf('{') < 0 || json.IndexOf('}') < 0) return null;

            DeviceProfile p = new DeviceProfile();
            p.Id = "custom-profile";
            p.DisplayName = "Custom Hardware Profile";
            // Fail-closed defaults: stray or truncated JSON never silently unlocks hardware writes
            p.IsReadOnly = true;
            p.IsVerified = false;
            p.BoundManufacturer = "";
            p.BoundProductName = "";
            p.BoundBaseBoard = "";

            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int parsedFieldCount = SimpleJson.ParseFlatObject(json, fields, stringsOnly: false);
            if (parsedFieldCount == 0) return null;

            bool explicitReadOnlyFalse = false;
            bool explicitVerifiedTrue = false;

            foreach (KeyValuePair<string, string> kv in fields)
            {
                string key = kv.Key;
                string val = kv.Value ?? "";

                if (key.Equals("id", StringComparison.OrdinalIgnoreCase)) p.Id = val;
                else if (key.Equals("displayName", StringComparison.OrdinalIgnoreCase)) p.DisplayName = val;
                else if (key.Equals("chipsetNotes", StringComparison.OrdinalIgnoreCase)) p.ChipsetNotes = val;
                else if (key.Equals("ecBaseAddress", StringComparison.OrdinalIgnoreCase)) p.EcBaseAddress = ParseUlong(val, 0xFE0B0400UL);
                else if (key.Equals("ecSize", StringComparison.OrdinalIgnoreCase)) p.EcSize = (uint)ParseUlong(val, 0x100);
                else if (key.Equals("regModeStatus", StringComparison.OrdinalIgnoreCase)) p.RegModeStatus = ParseByte(val, 0x31);
                else if (key.Equals("regModeCmd", StringComparison.OrdinalIgnoreCase)) p.RegModeCmd = ParseByte(val, 0x32);
                else if (key.Equals("regFan1Ctrl", StringComparison.OrdinalIgnoreCase)) p.RegFan1Ctrl = ParseByte(val, 0x33);
                else if (key.Equals("regFan2Ctrl", StringComparison.OrdinalIgnoreCase)) p.RegFan2Ctrl = ParseByte(val, 0x00);
                else if (key.Equals("regFan1Hi", StringComparison.OrdinalIgnoreCase)) p.RegFan1Hi = ParseByte(val, 0x35);
                else if (key.Equals("regFan1Lo", StringComparison.OrdinalIgnoreCase)) p.RegFan1Lo = ParseByte(val, 0x36);
                else if (key.Equals("regFan2Hi", StringComparison.OrdinalIgnoreCase)) p.RegFan2Hi = ParseByte(val, 0x37);
                else if (key.Equals("regFan2Lo", StringComparison.OrdinalIgnoreCase)) p.RegFan2Lo = ParseByte(val, 0x38);
                else if (key.Equals("regCpuTemp", StringComparison.OrdinalIgnoreCase)) p.RegCpuTemp = ParseByte(val, 0x70);
                else if (key.Equals("regGpuTemp", StringComparison.OrdinalIgnoreCase)) p.RegGpuTemp = ParseByte(val, 0x71);
                else if (key.Equals("regVrmTemp", StringComparison.OrdinalIgnoreCase)) p.RegVrmTemp = ParseByte(val, 0x00);
                else if (key.Equals("regAmbientTemp", StringComparison.OrdinalIgnoreCase)) p.RegAmbientTemp = ParseByte(val, 0x00);
                else if (key.Equals("regDdrTemp", StringComparison.OrdinalIgnoreCase)) p.RegDdrTemp = ParseByte(val, 0x00);
                else if (key.Equals("regDcVoltsAdc", StringComparison.OrdinalIgnoreCase)) p.RegDcVoltsAdc = ParseByte(val, 0x00);
                else if (key.Equals("hasDualFans", StringComparison.OrdinalIgnoreCase)) p.HasDualFans = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (key.Equals("isReadOnly", StringComparison.OrdinalIgnoreCase))
                {
                    bool ro = !val.Equals("false", StringComparison.OrdinalIgnoreCase);
                    p.IsReadOnly = ro;
                    explicitReadOnlyFalse = !ro;
                }
                else if (key.Equals("isVerified", StringComparison.OrdinalIgnoreCase))
                {
                    bool ver = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    p.IsVerified = ver;
                    explicitVerifiedTrue = ver;
                }
                else if (key.Equals("boundManufacturer", StringComparison.OrdinalIgnoreCase)) p.BoundManufacturer = val;
                else if (key.Equals("boundProductName", StringComparison.OrdinalIgnoreCase)) p.BoundProductName = val;
                else if (key.Equals("boundBaseBoard", StringComparison.OrdinalIgnoreCase)) p.BoundBaseBoard = val;
            }

            // Re-verify invariants on load: an invalid register map or unbound profile cannot unlock writes
            bool hasDmiBinding = IsMeaningfulDmiToken(p.BoundProductName)
                || IsMeaningfulDmiToken(p.BoundBaseBoard)
                || IsMeaningfulDmiToken(p.BoundManufacturer);

            if (!p.HasValidRegisterMap() || !hasDmiBinding || !explicitVerifiedTrue || !explicitReadOnlyFalse)
            {
                p.IsReadOnly = true;
                if (!p.HasValidRegisterMap() || !hasDmiBinding)
                    p.IsVerified = false;
            }

            return p;
        }

        private static byte ParseByte(string s, byte def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                byte b;
                if (byte.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    return b;
            }
            else
            {
                byte b;
                if (byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                    return b;
            }
            return def;
        }

        private static ulong ParseUlong(string s, ulong def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong u;
                if (ulong.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u))
                    return u;
            }
            else
            {
                ulong u;
                if (ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out u))
                    return u;
            }
            return def;
        }

        public static void SaveCustomProfile(DeviceProfile prof, string path = null)
        {
            if (prof == null) return;
            string targetPath = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CUSTOM_PROFILE_FILENAME);
            File.WriteAllText(targetPath, prof.ToJson(), Encoding.UTF8);
            CustomProfile = prof;
        }

        public static DeviceProfile LoadCustomProfile(string path = null)
        {
            try
            {
                string targetPath = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CUSTOM_PROFILE_FILENAME);
                if (File.Exists(targetPath))
                {
                    string json = File.ReadAllText(targetPath, Encoding.UTF8);
                    DeviceProfile p = FromJson(json);
                    if (p != null)
                    {
                        CustomProfile = p;
                        return p;
                    }
                }
            }
            catch { }
            return null;
        }

        public static DeviceProfile CreateFromDiffReport(EcDiffReport report, SystemHardwareInfo sysInfo)
        {
            DeviceProfile p = new DeviceProfile();
            string prodName = (sysInfo != null && !string.IsNullOrEmpty(sysInfo.ProductName) && !sysInfo.ProductName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                ? sysInfo.ProductName : "Custom Mini PC";
            string board = (sysInfo != null && !string.IsNullOrEmpty(sysInfo.BaseBoard) && !sysInfo.BaseBoard.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                ? sysInfo.BaseBoard : "Discovered Board";

            p.Id = "custom-profile";
            p.DisplayName = string.Format("Custom: {0} ({1})", prodName, board);
            p.ChipsetNotes = "Auto-generated from EC Differential Telemetry Scanner on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            p.BoundManufacturer = (sysInfo != null && IsMeaningfulDmiToken(sysInfo.Manufacturer)) ? sysInfo.Manufacturer.Trim() : "";
            p.BoundProductName = (sysInfo != null && IsMeaningfulDmiToken(sysInfo.ProductName)) ? sysInfo.ProductName.Trim() : "";
            p.BoundBaseBoard = (sysInfo != null && IsMeaningfulDmiToken(sysInfo.BaseBoard)) ? sysInfo.BaseBoard.Trim() : "";
            bool hasBinding = IsMeaningfulDmiToken(p.BoundProductName) || IsMeaningfulDmiToken(p.BoundBaseBoard) || IsMeaningfulDmiToken(p.BoundManufacturer);
            p.IsReadOnly = !hasBinding;
            p.IsVerified = hasBinding;

            // Default fallback registers
            p.RegCpuTemp = 0x70;
            p.RegGpuTemp = 0x71;
            p.RegFan1Ctrl = 0x33;
            p.RegFan2Ctrl = 0x00;
            p.RegFan1Hi = 0x35;
            p.RegFan1Lo = 0x36;
            p.RegFan2Hi = 0x00;
            p.RegFan2Lo = 0x00;
            p.HasDualFans = false;

            if (report != null)
            {
                if (report.ThermalCandidates.Count > 0)
                {
                    p.RegCpuTemp = report.ThermalCandidates[0].Register;
                    if (report.ThermalCandidates.Count > 1)
                    {
                        p.RegGpuTemp = report.ThermalCandidates[1].Register;
                    }
                    foreach (var tc in report.ThermalCandidates)
                    {
                        if (tc.Register >= 0x72 && tc.Register <= 0x73 && p.RegVrmTemp == 0x00)
                            p.RegVrmTemp = tc.Register;
                        else if (tc.Register == 0x74 && p.RegAmbientTemp == 0x00)
                            p.RegAmbientTemp = tc.Register;
                        else if (tc.Register == 0x75 && p.RegDdrTemp == 0x00)
                            p.RegDdrTemp = tc.Register;
                    }
                }

                if (report.FanPwmCandidates.Count > 0)
                {
                    p.RegFan1Ctrl = report.FanPwmCandidates[0].Register;
                    if (report.FanPwmCandidates.Count > 1)
                    {
                        p.RegFan2Ctrl = report.FanPwmCandidates[1].Register;
                        p.HasDualFans = true;
                    }
                }

                if (report.TachometerCandidates.Count > 0)
                {
                    byte rHi = report.TachometerCandidates[0].Register;
                    p.RegFan1Hi = rHi;
                    p.RegFan1Lo = (byte)(rHi + 1);

                    for (int i = 1; i < report.TachometerCandidates.Count; i++)
                    {
                        byte cand = report.TachometerCandidates[i].Register;
                        if (cand != rHi && cand != rHi + 1)
                        {
                            p.RegFan2Hi = cand;
                            p.RegFan2Lo = (byte)(cand + 1);
                            p.HasDualFans = true;
                            break;
                        }
                    }
                }

                if (report.VoltageCandidates.Count > 0)
                {
                    p.RegDcVoltsAdc = report.VoltageCandidates[0].Register;
                }
            }

            return p;
        }
    }

    public class SystemHardwareInfo
    {
        public string Manufacturer = "Unknown";
        public string ProductName = "Unknown";
        public string BaseBoard = "Unknown";
        public string BiosVersion = "Unknown";
        public string CpuName = "AMD Ryzen APU";
        public DeviceProfile MatchedProfile = DeviceProfile.FindProfile("unknown-safe");

        public static SystemHardwareInfo Detect()
        {
            SystemHardwareInfo info = new SystemHardwareInfo();
            try
            {
                using (RegistryKey biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (biosKey != null)
                    {
                        info.Manufacturer = ReadRegStr(biosKey, "SystemManufacturer", info.Manufacturer);
                        info.ProductName  = ReadRegStr(biosKey, "SystemProductName", info.ProductName);
                        info.BaseBoard    = ReadRegStr(biosKey, "BaseBoardProduct", info.BaseBoard);
                        info.BiosVersion  = ReadRegStr(biosKey, "BIOSVersion", info.BiosVersion);
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (cpuKey != null)
                    {
                        info.CpuName = ReadRegStr(cpuKey, "ProcessorNameString", info.CpuName);
                    }
                }
            }
            catch { }

            info.MatchedProfile = MatchProfile(info.ProductName, info.BaseBoard, info.CpuName, info.Manufacturer);

            return info;
        }

        public static DeviceProfile MatchProfile(string productName, string baseBoard, string cpuName)
        {
            return MatchProfile(productName, baseBoard, cpuName, null);
        }

        public static DeviceProfile MatchProfile(string productName, string baseBoard, string cpuName, string manufacturer)
        {
            string combined = ((manufacturer ?? "") + " " + (productName ?? "") + " " + (baseBoard ?? "") + " " + (cpuName ?? "")).ToUpperInvariant();
            // Safety blocklist ALWAYS takes precedence before any custom profile lookup
            if (combined.Contains("RECOREQICH") || combined.Contains("KTHIN") || combined.Contains("HX PRO 370"))
                return DeviceProfile.FindProfile("unknown-safe");

            if (DeviceProfile.CustomProfile == null)
            {
                DeviceProfile.LoadCustomProfile();
            }
            if (DeviceProfile.CustomProfile != null
                && DeviceProfile.CustomProfile.HasValidRegisterMap()
                && DeviceProfile.CustomProfile.MatchesDmi(productName, baseBoard, manufacturer))
            {
                return DeviceProfile.CustomProfile;
            }
            if (combined.Contains("EVO-X1") || combined.Contains("TS-12C"))
                return DeviceProfile.FindProfile("ryzen-hx370-ec");
            if (combined.Contains("ADREAMER") || combined.Contains("ARIESBOX") || combined.Contains("PB07"))
                return DeviceProfile.FindProfile("adreamer-ariesbox10");
            if (combined.Contains("NUCBOX K8") || combined.Contains("NUCBOX_K8")
                || combined.Contains("K8 PLUS") || combined.Contains("K8_PLUS")
                || combined.Contains("NUCBOX K11") || combined.Contains("NUCBOX_K11")
                || (combined.Contains("GMKTEC") && (combined.Contains("K8") || combined.Contains("K11") || combined.Contains("8845") || combined.Contains("8945"))))
                return DeviceProfile.FindProfile("gmktec-k8-k11");
            if (combined.Contains("NUCBOX K12") || combined.Contains("NUCBOX_K12")
                || (combined.Contains("GMKTEC") && (combined.Contains("K12") || combined.Contains("H 255") || combined.Contains("H255"))))
                return DeviceProfile.FindProfile("gmktec-k12");
            return DeviceProfile.FindProfile("unknown-safe");
        }

        private static string ReadRegStr(RegistryKey key, string valueName, string fallback)
        {
            object val = key.GetValue(valueName);
            if (val is string)
            {
                string s = ((string)val).Trim();
                if (!string.IsNullOrEmpty(s)) return s;
            }
            else if (val is string[])
            {
                string[] arr = (string[])val;
                if (arr.Length > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0].Trim();
            }
            return fallback;
        }

        public string GetCompactSummary()
        {
            string prod = (ProductName ?? "").Replace("_", " ").Trim();
            if (string.IsNullOrEmpty(prod) || prod.Equals("Default string", StringComparison.OrdinalIgnoreCase) || prod.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                prod = (BaseBoard ?? "").Replace("_", " ").Trim();

            string shortCpu = CpuName
                .Replace("AMD ", "")
                .Replace("Processor", "")
                .Replace("w/ Radeon 780M Graphics", "")
                .Replace("w/ Radeon 890M Graphics", "")
                .Replace("with Radeon Graphics", "")
                .Trim();

            while (shortCpu.Contains("  "))
                shortCpu = shortCpu.Replace("  ", " ");

            if (!string.IsNullOrEmpty(prod) && !prod.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                return string.Format("{0} • {1}", prod, shortCpu);
            return shortCpu;
        }

        public static string DescribeSuperIoChip(ushort chipId)
        {
            if (chipId == 0 || chipId == 0xFFFF) return "";
            if (chipId == 0x5570) return "ITE IT5570E";
            if ((chipId & 0xFF00) == 0x5500) return "ITE IT55xx EC";
            if (chipId == 0x8613) return "ITE IT8613E";
            if ((chipId & 0xFF00) == 0x8600) return "ITE IT86xx";
            if ((chipId & 0xFF00) == 0x8700) return "ITE IT87xx";
            byte hi = (byte)(chipId >> 8);
            if (chipId == 0xB473 || chipId == 0xB571
                || hi == 0xC3 || hi == 0xC5 || hi == 0xC8 || hi == 0xD4 || hi == 0xD8)
                return "Nuvoton NCT67xx";
            return "";
        }

        private static string EscapeJsonStr(string s)
        {
            return SimpleJson.Escape(s);
        }

        public string BuildProbeReportJson(HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            bool allowWrites = (MatchedProfile != null && !MatchedProfile.IsReadOnly && MatchedProfile.Id == "ryzen-hx370-ec");
            bool s2eOk = false, s4eOk = false;
            ushort s2eChip = 0, s2eBase = 0, s4eChip = 0, s4eBase = 0;

            if (!demoMode)
            {
                try
                {
                    s2eOk = HardwareBackend.ProbeSuperIo(0x2E, out s2eChip, out s2eBase, allowWrites);
                    s4eOk = HardwareBackend.ProbeSuperIo(0x4E, out s4eChip, out s4eBase, allowWrites);
                }
                catch { }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"source\": \"kaiserc/MiniFanControl\",");
            sb.AppendLine(string.Format("  \"demoMode\": {0},", demoMode ? "true" : "false"));
            sb.AppendLine(string.Format("  \"manufacturer\": \"{0}\",", EscapeJsonStr(Manufacturer)));
            sb.AppendLine(string.Format("  \"productName\": \"{0}\",", EscapeJsonStr(ProductName)));
            sb.AppendLine(string.Format("  \"baseBoard\": \"{0}\",", EscapeJsonStr(BaseBoard)));
            sb.AppendLine(string.Format("  \"biosVersion\": \"{0}\",", EscapeJsonStr(BiosVersion)));
            sb.AppendLine(string.Format("  \"cpuName\": \"{0}\",", EscapeJsonStr(CpuName)));
            sb.AppendLine("  \"matchedProfile\": {");
            sb.AppendLine(string.Format("    \"id\": \"{0}\",", EscapeJsonStr(MatchedProfile != null ? MatchedProfile.Id : "unknown-safe")));
            sb.AppendLine(string.Format("    \"displayName\": \"{0}\",", EscapeJsonStr(MatchedProfile != null ? MatchedProfile.DisplayName : "")));
            sb.AppendLine(string.Format("    \"isVerified\": {0},", (MatchedProfile != null && MatchedProfile.IsVerified) ? "true" : "false"));
            sb.AppendLine(string.Format("    \"isReadOnly\": {0},", (MatchedProfile == null || MatchedProfile.IsReadOnly) ? "true" : "false"));
            sb.AppendLine(string.Format("    \"hasDualFans\": {0}", (MatchedProfile != null && MatchedProfile.HasDualFans) ? "true" : "false"));
            sb.AppendLine("  },");
            sb.AppendLine("  \"superIo\": {");
            sb.AppendLine("    \"port0x2E\": {");
            sb.AppendLine(string.Format("      \"detected\": {0},", (s2eOk || s2eChip != 0) ? "true" : "false"));
            sb.AppendLine(string.Format("      \"hwMonVerified\": {0},", s2eOk ? "true" : "false"));
            sb.AppendLine(string.Format("      \"chipId\": \"0x{0:X4}\",", s2eChip));
            sb.AppendLine(string.Format("      \"chipName\": \"{0}\",", EscapeJsonStr(DescribeSuperIoChip(s2eChip))));
            sb.AppendLine(string.Format("      \"hwBase\": \"0x{0:X4}\"", s2eBase));
            sb.AppendLine("    },");
            sb.AppendLine("    \"port0x4E\": {");
            sb.AppendLine(string.Format("      \"detected\": {0},", (s4eOk || s4eChip != 0) ? "true" : "false"));
            sb.AppendLine(string.Format("      \"hwMonVerified\": {0},", s4eOk ? "true" : "false"));
            sb.AppendLine(string.Format("      \"chipId\": \"0x{0:X4}\",", s4eChip));
            sb.AppendLine(string.Format("      \"chipName\": \"{0}\",", EscapeJsonStr(DescribeSuperIoChip(s4eChip))));
            sb.AppendLine(string.Format("      \"hwBase\": \"0x{0:X4}\"", s4eBase));
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string BuildProbeReport(HardwareBackend hw, DemoBackend demo, bool demoMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendLine("  MINIFANCONTROL • RYZEN MINI-PC HARDWARE & EC PROBE REPORT");
            sb.AppendLine("======================================================================");
            sb.AppendLine("Timestamp        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Manufacturer     : " + Manufacturer);
            sb.AppendLine("Product Name     : " + ProductName);
            sb.AppendLine("BaseBoard        : " + BaseBoard);
            sb.AppendLine("BIOS Version     : " + BiosVersion);
            sb.AppendLine("CPU / APU        : " + CpuName);
            sb.AppendLine("Active Profile   : " + MatchedProfile.DisplayName);
            sb.AppendLine("Kernel Driver    : " + NativeInpOut.ActiveDriverName);
            sb.AppendLine("EC Communication : " + (hw != null ? hw.AccessMethodName : (demoMode ? "Simulation/Demo" : "ACPI EC Port I/O (0x62/0x66)")));
            sb.AppendLine("EC Physical Base : 0x" + MatchedProfile.EcBaseAddress.ToString("X8") + " (Size: 0x" + MatchedProfile.EcSize.ToString("X2") + ")");
            sb.AppendLine("EC Mutex Sync    : " + EcBusMutex.MutexNamespaceDiagnostic);
            if (!demoMode)
            {
                sb.AppendLine("----------------------------------------------------------------------");
                sb.AppendLine("LOW-LEVEL LPC & ACPI PORT I/O DIAGNOSTICS:");
                try
                {
                    byte sc66 = HardwareBackend.ReadPortByte(0x66);
                    byte dt62 = HardwareBackend.ReadPortByte(0x62);
                    byte sc6E = HardwareBackend.ReadPortByte(0x6E);
                    byte dt6C = HardwareBackend.ReadPortByte(0x6C);
                    sb.AppendFormat("  ACPI EC Primary   (0x62/0x66) : Status=0x{0:X2}, Data=0x{1:X2}\r\n", sc66, dt62);
                    sb.AppendFormat("  ACPI EC Secondary (0x6C/0x6E) : Status=0x{0:X2}, Data=0x{1:X2}\r\n", sc6E, dt6C);

                    ushort s2eChip, s2eBase;
                    bool allowWrites = (MatchedProfile != null && !MatchedProfile.IsReadOnly && MatchedProfile.Id == "ryzen-hx370-ec");
                    bool s2eOk = HardwareBackend.ProbeSuperIo(0x2E, out s2eChip, out s2eBase, allowWrites);
                    string s2eName = DescribeSuperIoChip(s2eChip);
                    string s2eStatus = s2eOk ? "FOUND" : (s2eChip != 0 ? "CHIP DETECTED (NO LDN4)" : "Not Detected");
                    sb.AppendFormat("  Super I/O Port 0x2E Probe     : {0} (Chip ID: 0x{1:X4}{2}, Base: 0x{3:X4})\r\n",
                        s2eStatus, s2eChip, string.IsNullOrEmpty(s2eName) ? "" : " [" + s2eName + "]", s2eBase);

                    ushort s4eChip, s4eBase;
                    bool s4eOk = HardwareBackend.ProbeSuperIo(0x4E, out s4eChip, out s4eBase, allowWrites);
                    string s4eName = DescribeSuperIoChip(s4eChip);
                    string s4eStatus = s4eOk ? "FOUND" : (s4eChip != 0 ? "CHIP DETECTED (NO LDN4)" : "Not Detected");
                    sb.AppendFormat("  Super I/O Port 0x4E Probe     : {0} (Chip ID: 0x{1:X4}{2}, Base: 0x{3:X4})\r\n",
                        s4eStatus, s4eChip, string.IsNullOrEmpty(s4eName) ? "" : " [" + s4eName + "]", s4eBase);
                }
                catch (Exception pEx)
                {
                    sb.AppendLine("  Port Probe Exception: " + pEx.Message);
                }
            }
            sb.AppendLine("----------------------------------------------------------------------");

            try
            {
                FanStatus st = demoMode ? demo.GetStatus() : hw.GetStatus();
                sb.AppendLine("LIVE SENSOR & FAN READINGS:");
                sb.AppendLine(string.Format("  CPU Temperature (0x70) : {0} °C", st.CpuTemp));
                sb.AppendLine(string.Format("  iGPU Temperature (ADL) : {0} °C", st.GpuTemp));
                if (!string.IsNullOrEmpty(st.RadeonSoftwareVer))
                {
                    sb.AppendLine(string.Format("  Radeon Software Ver    : {0}", st.RadeonSoftwareVer));
                }
                sb.AppendLine(string.Format("  APU Power Usage        : {0} W", st.ApuPowerWatts));
                sb.AppendLine(string.Format("  Performance Mode (0x31): {0} (code={1})", st.Mode, st.ModeCode));
                sb.AppendLine(string.Format("  APU Fan (Fan 1, 0x35)  : {0} RPM | Ctrl(0x33)={1}% ({2})",
                    st.Fan1Rpm, st.Fan1Pct, st.Fan1Manual != 0 ? "Manual" : "Auto"));
                sb.AppendLine(string.Format("  Sys Fan (Fan 2, 0x37)  : {0} RPM | Ctrl(0x34)={1}% ({2})",
                    st.Fan2Rpm, st.Fan2Pct, st.Fan2Manual != 0 ? "Manual" : "Auto"));
                sb.AppendLine("----------------------------------------------------------------------");

                if (!demoMode)
                {
                    byte[] regs = hw.ReadAllRegisters();
                    sb.AppendLine(string.Format("EC REGISTER HEX DUMP (0x00 - 0xFE via {0}):", hw != null ? hw.AccessMethodName : "EC"));
                    sb.AppendLine("      00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F");
                    sb.AppendLine("      -----------------------------------------------");
                    for (int row = 0; row < 16; row++)
                    {
                        sb.AppendFormat("0x{0:X1}0: ", row);
                        for (int col = 0; col < 16; col++)
                        {
                            int idx = row * 16 + col;
                            if (idx < regs.Length)
                                sb.AppendFormat("{0:X2} ", regs[idx]);
                            else
                                sb.Append("-- ");
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("----------------------------------------------------------------------");
                    sb.AppendLine("KEY REGISTER ANNOTATIONS (Active Profile Layout):");
                    sb.AppendFormat("  [0x{0:X2}] Mode Status : 0x{1:X2} (0=Balanced, 1=Performance, 2=Silent)\r\n", MatchedProfile.RegModeStatus, regs[MatchedProfile.RegModeStatus]);
                    sb.AppendFormat("  [0x{0:X2}] Mode Cmd    : 0x{1:X2} (0x80=Balanced, 0x81=Performance, 0x82=Silent)\r\n", MatchedProfile.RegModeCmd, regs[MatchedProfile.RegModeCmd]);
                    sb.AppendFormat("  [0x{0:X2}] Fan 1 Ctrl  : 0x{1:X2} (0x00=Auto, 0x80|pct=Manual)\r\n", MatchedProfile.RegFan1Ctrl, regs[MatchedProfile.RegFan1Ctrl]);
                    sb.AppendFormat("  [0x{0:X2}] Fan 2 Ctrl  : 0x{1:X2} (0x00=Auto, 0x80|pct=Manual)\r\n", MatchedProfile.RegFan2Ctrl, regs[MatchedProfile.RegFan2Ctrl]);
                    sb.AppendFormat("  [0x{0:X2}..{1:X2}] Fan1 RPM: 0x{2:X2}{3:X2} ({4} raw)\r\n", MatchedProfile.RegFan1Hi, MatchedProfile.RegFan1Lo, regs[MatchedProfile.RegFan1Hi], regs[MatchedProfile.RegFan1Lo], (regs[MatchedProfile.RegFan1Hi] << 8) | regs[MatchedProfile.RegFan1Lo]);
                    sb.AppendFormat("  [0x{0:X2}..{1:X2}] Fan2 RPM: 0x{2:X2}{3:X2} ({4} raw)\r\n", MatchedProfile.RegFan2Hi, MatchedProfile.RegFan2Lo, regs[MatchedProfile.RegFan2Hi], regs[MatchedProfile.RegFan2Lo], (regs[MatchedProfile.RegFan2Hi] << 8) | regs[MatchedProfile.RegFan2Lo]);
                    sb.AppendFormat("  [0x{0:X2}] CPU Temp    : 0x{1:X2} ({1} °C)\r\n", MatchedProfile.RegCpuTemp, regs[MatchedProfile.RegCpuTemp]);
                    sb.AppendFormat("  [0x{0:X2}] dGPU Temp   : 0x{1:X2} ({1} °C)\r\n", MatchedProfile.RegGpuTemp, regs[MatchedProfile.RegGpuTemp]);
                    sb.AppendLine("----------------------------------------------------------------------");
                    sb.Append(AnalyzeEcCandidates(regs));
                }
                else
                {
                    sb.AppendLine("NOTE: Application is currently in Simulation/Demo Mode.");
                    sb.AppendLine("Uncheck Demo Mode to capture live physical EC registers.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR READING EC HARDWARE: " + ex.Message);
            }

            sb.AppendLine("======================================================================");
            return sb.ToString();
        }

        public static string AnalyzeEcCandidates(byte[] regs)
        {
            if (regs == null || regs.Length < 0x80)
                return "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("AUTO-DISCOVERY & CANDIDATE REGISTER SCAN (FOR NEW MINI PCs):");

            int stdTemp = regs[0x70];
            int stdF1 = (regs[0x35] << 8) | regs[0x36];
            int stdF2 = (regs[0x37] << 8) | regs[0x38];
            byte stdMode = regs[0x31];

            bool stdLayoutValid = (stdTemp >= 25 && stdTemp <= 105) &&
                                  (stdMode <= 2) &&
                                  ((stdF1 >= 300 && stdF1 <= 5000) || stdF1 == 8000 || stdF1 == 0);

            if (stdLayoutValid)
            {
                sb.AppendLine("  [OK] Standard GMKtec / ITE IT5570E layout (0x31..0x38, 0x70) is VALID on this PC!");
            }
            else
            {
                sb.AppendLine("  [!] Standard offsets (0x31..0x38, 0x70) look non-standard on this board.");
                sb.AppendLine("      Check the candidate registers below and add overrides to minifancontrol.cfg:");
            }

            List<string> tempCands = new List<string>();
            for (int r = 0x10; r < regs.Length; r++)
            {
                if (r >= 0x35 && r <= 0x38) continue;
                byte v = regs[r];
                if (v >= 28 && v <= 92)
                {
                    tempCands.Add(string.Format("0x{0:X2}={1}°C", r, v));
                }
            }
            sb.AppendLine("  • Candidate Temp Registers (28..92°C) : " + (tempCands.Count > 0 ? string.Join(", ", tempCands.ToArray()) : "None"));

            List<string> rpmCands = new List<string>();
            for (int r = 0x10; r < regs.Length - 1; r++)
            {
                int be = (regs[r] << 8) | regs[r + 1];
                if ((be >= 400 && be <= 4500) || be == 8000)
                {
                    rpmCands.Add(string.Format("0x{0:X2}..{1:X2}={2}{3}", r, r + 1, be, be == 8000 ? "(Idle)" : " RPM"));
                }
            }
            sb.AppendLine("  • Candidate 16-bit RPM Pairs (BE)     : " + (rpmCands.Count > 0 ? string.Join(", ", rpmCands.ToArray()) : "None"));
            sb.AppendLine("  • Custom Override Keys (minifancontrol.cfg):");
            sb.AppendLine("    RegModeStatus=0x31  RegModeCmd=0x32  RegFan1Ctrl=0x33  RegFan2Ctrl=0x34");
            sb.AppendLine("    RegFan1Hi=0x35      RegFan1Lo=0x36   RegFan2Hi=0x37    RegFan2Lo=0x38    RegCpuTemp=0x70");
            return sb.ToString();
        }
    }

}
