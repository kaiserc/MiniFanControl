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
        public bool HasDualFans   = true;
        public bool IsReadOnly    = false;
        public bool IsVerified    = true;

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
                DisplayName = "Ryzen AI 9 HX 370 Mini PC (KTHIN PRO / EVO-X1 / TOPC TS-12C)",
                ChipsetNotes = "ITE IT8613E Super I/O @ 0x2E/0x0A30 + EC • Dual Fans • Radeon 890M via ADL2 / PMLOG"
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

        public static DeviceProfile FindProfile(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
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

            string combined = (info.ProductName + " " + info.BaseBoard + " " + info.CpuName).ToUpperInvariant();
            info.MatchedProfile = MatchProfile(info.ProductName, info.BaseBoard, info.CpuName);

            return info;
        }

        public static DeviceProfile MatchProfile(string productName, string baseBoard, string cpuName)
        {
            string combined = ((productName ?? "") + " " + (baseBoard ?? "") + " " + (cpuName ?? "")).ToUpperInvariant();
            if (combined.Contains("HX 370") || combined.Contains("HX370") || combined.Contains("EVO-X1") || combined.Contains("KTHIN") || combined.Contains("TS-12C") || combined.Contains("STRIX"))
                return DeviceProfile.FindProfile("ryzen-hx370-ec");
            if (combined.Contains("ADREAMER") || combined.Contains("ARIESBOX") || combined.Contains("PB07") || combined.Contains("7840") || combined.Contains("7940"))
                return DeviceProfile.FindProfile("adreamer-ariesbox10");
            if (combined.Contains("K8") || combined.Contains("K11") || combined.Contains("8845") || combined.Contains("8945"))
                return DeviceProfile.FindProfile("gmktec-k8-k11");
            if (combined.Contains("K12") || combined.Contains("H 255") || combined.Contains("H255"))
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
            sb.AppendLine("EC Communication : " + (hw != null ? hw.AccessMethodName : (demoMode ? "Simulation/Demo" : "ACPI EC Port I/O (0x62/0x66)")));
            sb.AppendLine("EC Physical Base : 0x" + MatchedProfile.EcBaseAddress.ToString("X8") + " (Size: 0x" + MatchedProfile.EcSize.ToString("X2") + ")");
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
                    bool s2eOk = HardwareBackend.ProbeSuperIo(0x2E, out s2eChip, out s2eBase);
                    sb.AppendFormat("  Super I/O Port 0x2E Probe     : {0} (Chip ID: 0x{1:X4}, Base: 0x{2:X4})\r\n", s2eOk ? "FOUND" : "Not Detected", s2eChip, s2eBase);

                    ushort s4eChip, s4eBase;
                    bool s4eOk = HardwareBackend.ProbeSuperIo(0x4E, out s4eChip, out s4eBase);
                    sb.AppendFormat("  Super I/O Port 0x4E Probe     : {0} (Chip ID: 0x{1:X4}, Base: 0x{2:X4})\r\n", s4eOk ? "FOUND" : "Not Detected", s4eChip, s4eBase);
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
