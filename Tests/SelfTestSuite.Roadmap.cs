using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MiniFanControl
{
    public static partial class SelfTestSuite
    {
        private static void RunRoadmapTests()
        {
            // 112. PawnIO AMDFamily17 SMN Module, Address Allowlist & Tier-0 ReadAmdSmn Integration
            bool pawnSmnOk = false;
            string pawnSmnDetail = "";
            try
            {
                bool allowOk = PawnIoDriver.IsSmnReadAddressAllowed(0x00059800U)
                    && PawnIoDriver.IsSmnReadAddressAllowed(0x00059808U)
                    && !PawnIoDriver.IsSmnReadAddressAllowed(0x03B10528U)
                    && !PawnIoDriver.IsSmnReadAddressAllowed(0x00000000U);

                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
                uint encoded72C = (uint)(72 * 8) << 21;
                int smnCalls = 0;
                PawnIoDriver.SimulatedAvailable = true;
                PawnIoDriver.SimulatedExecuteHook = delegate(string fn, ulong[] inp)
                {
                    if (fn == "ioctl_read_smn" && inp.Length >= 1 && inp[0] == 0x00059800UL)
                    {
                        smnCalls++;
                        return new ulong[] { encoded72C };
                    }
                    return null;
                };
                bool pre = NativeInpOut.PreloadDriverDll();
                bool hasMod = PawnIoDriver.HasAmdSmnModule && PawnIoDriver.LoadedModulesSummary.Contains("AMDFamily17");
                uint rawSmn = HardwareBackend.ReadAmdSmn(0x00059800U);
                int decodedTemp = HardwareBackend.DecodeAmdSmnTemp(rawSmn);

                uint blockedVal;
                bool blockedWriteAddr = !PawnIoDriver.TryReadAmdSmn(0x03B10528U, out blockedVal);

                pawnSmnOk = allowOk && pre && hasMod && (smnCalls >= 1) && (decodedTemp == 72) && blockedWriteAddr;
                pawnSmnDetail = string.Format("Allow={0}, Mod={1}, Calls={2}, Decoded={3}C, BlockedUnsafe={4}",
                    allowOk, hasMod, smnCalls, decodedTemp, blockedWriteAddr);
            }
            catch (Exception ex) { pawnSmnDetail = ex.Message; }
            finally
            {
                NativeInpOut.Shutdown();
                PawnIoDriver.Shutdown();
            }
            Check(pawnSmnOk,
                "PawnIO AMDFamily17 SMN Module, Address Allowlist & Tier-0 ReadAmdSmn Integration",
                pawnSmnDetail);

            // 113. External JSON Hardware Profile Registry (profiles/*.json), Validation & Blocklist Precedence
            bool extProfOk = false;
            string extProfDetail = "";
            string tempProfDir = null;
            try
            {
                tempProfDir = Path.Combine(Path.GetTempPath(), "mfc_ext_profiles_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempProfDir);

                File.WriteAllText(Path.Combine(tempProfDir, "template.profile.json"), "{ invalid template skipped }");
                File.WriteAllText(Path.Combine(tempProfDir, "beelink-ser8-test.json"),
                    "{\"id\":\"beelink-ser8-test\",\"displayName\":\"Beelink SER8 (Test)\",\"ecBaseAddress\":\"0xFE0B0400\",\"ecSize\":\"0x100\",\"regCpuTemp\":\"0x70\",\"regFan1Ctrl\":\"0x33\",\"hasDualFans\":true,\"isReadOnly\":false,\"isVerified\":true,\"boundManufacturer\":\"Beelink\",\"boundProductName\":\"SER8\",\"boundBaseBoard\":\"SER8-MB\"}");
                File.WriteAllText(Path.Combine(tempProfDir, "bad-unbound.json"),
                    "{\"id\":\"bad-unbound\",\"displayName\":\"Missing DMI\",\"regCpuTemp\":\"0x70\",\"regFan1Ctrl\":\"0x33\"}");

                List<string> loadErrors;
                List<DeviceProfile> loaded = ExternalProfileRegistry.LoadFromDirectory(tempProfDir, out loadErrors);
                bool parseCountOk = (loaded.Count == 1 && loaded[0].Id == "beelink-ser8-test" && loadErrors.Count == 1);

                ExternalProfileRegistry.ResetForTest(loaded);
                DeviceProfile matched = SystemHardwareInfo.MatchProfile("SER8", "SER8-MB", "AMD Ryzen 7 8845HS", "Beelink");
                DeviceProfile byId = DeviceProfile.FindProfile("beelink-ser8-test");
                DeviceProfile blocklisted = SystemHardwareInfo.MatchProfile("KTHIN", "SER8-MB", "AMD Ryzen AI 9 HX PRO 370", "Recoreqich");

                extProfOk = parseCountOk
                    && matched != null && matched.Id == "beelink-ser8-test"
                    && byId != null && byId.Id == "beelink-ser8-test"
                    && blocklisted != null && blocklisted.Id == "unknown-safe";
                extProfDetail = string.Format("Loaded={0}, Errs={1}, Matched={2}, Blocklisted={3}",
                    loaded.Count, loadErrors.Count, matched != null ? matched.Id : "null", blocklisted != null ? blocklisted.Id : "null");
            }
            catch (Exception ex) { extProfDetail = ex.Message; }
            finally
            {
                ExternalProfileRegistry.ResetForTest(null);
                if (!string.IsNullOrEmpty(tempProfDir)) { try { Directory.Delete(tempProfDir, true); } catch { } }
            }
            Check(extProfOk,
                "External JSON Hardware Profile Registry (profiles/*.json), Validation & Blocklist Precedence",
                extProfDetail);

            // 114. Automatic Per-Game / Per-Workload Profile Switcher (Rules, AI/NPU, Exit Cooldown & Settings)
            bool workloadOk = false;
            string workloadDetail = "";
            string tempWsCfg = null;
            try
            {
                long fakeClock = 1000;
                WorkloadProfileSwitcher sw = new WorkloadProfileSwitcher(delegate { return fakeClock; }, 5000);
                sw.Enabled = true;
                sw.Rules.Add(new WorkloadRule("Cyberpunk*", "My Quiet Curve", "Performance"));

                // Explicit game rule match saves baseline ("Balanced") and switches
                WorkloadSwitchAction a1 = sw.Evaluate("Cyberpunk2077.exe", 0, "Balanced", "Balanced", false);
                bool ruleMatched = (a1 == WorkloadSwitchAction.ApplyWorkloadProfile)
                    && sw.IsSwitched
                    && sw.TargetApuProfileName == "My Quiet Curve"
                    && sw.TargetSysProfileName == "Performance"
                    && sw.BaselineApuProfileName == "Balanced";

                // Manual override must not trigger switch/restore fight
                WorkloadSwitchAction aMan = sw.Evaluate(null, 0, "Balanced", "Balanced", true);
                bool manualRespected = (aMan == WorkloadSwitchAction.None && sw.IsSwitched);

                // Brief idle (2s < 5s cooldown) holds switched profile
                fakeClock = 2000;
                WorkloadSwitchAction aHold1 = sw.Evaluate(null, 0, "My Quiet Curve", "Performance", false);
                fakeClock = 4500;
                WorkloadSwitchAction aHold2 = sw.Evaluate(null, 0, "My Quiet Curve", "Performance", false);

                // Past 5s cooldown -> restores baseline
                fakeClock = 7500;
                WorkloadSwitchAction aRestore = sw.Evaluate(null, 0, "My Quiet Curve", "Performance", false);
                bool restoredOk = (aRestore == WorkloadSwitchAction.RestoreBaselineProfile && !sw.IsSwitched);

                // Built-in AI/LLM & NPU detection -> AI Workload
                WorkloadSwitchAction aAi = sw.Evaluate("ollama.exe", 0, "Silent", "Silent", false);
                bool aiMatched = (aAi == WorkloadSwitchAction.ApplyWorkloadProfile && sw.TargetApuProfileName == "AI Workload");

                // Settings round-trip for AutoSwitchWorkloads + WorkloadRule
                tempWsCfg = Path.Combine(Path.GetTempPath(), "mfc_ws_cfg_" + Guid.NewGuid().ToString("N") + ".cfg");
                AppSettings.WriteAllLinesAtomic(tempWsCfg, new string[]
                {
                    "AutoSwitchWorkloads=1",
                    "WorkloadRule=Cyberpunk*|My Quiet Curve|Performance"
                });
                AppSettings sLoad = new AppSettings();
                sLoad.LoadFromPath(tempWsCfg, null, null, new List<FanCurveProfile>());
                bool cfgOk = sLoad.AutoSwitchWorkloads && sLoad.WorkloadRules.Count == 1
                    && sLoad.WorkloadRules[0].ProcessPattern == "Cyberpunk*"
                    && sLoad.WorkloadRules[0].ApuProfileName == "My Quiet Curve";

                workloadOk = ruleMatched && manualRespected
                    && (aHold1 == WorkloadSwitchAction.None) && (aHold2 == WorkloadSwitchAction.None)
                    && restoredOk && aiMatched && cfgOk;
                workloadDetail = string.Format("Rule={0}, Hold={1}, Restore={2}, AI={3}, Cfg={4}",
                    ruleMatched, (aHold1 == WorkloadSwitchAction.None && aHold2 == WorkloadSwitchAction.None), restoredOk, aiMatched, cfgOk);
            }
            catch (Exception ex) { workloadDetail = ex.Message; }
            finally
            {
                if (!string.IsNullOrEmpty(tempWsCfg))
                {
                    try { if (File.Exists(tempWsCfg)) File.Delete(tempWsCfg); } catch { }
                    try { if (File.Exists(tempWsCfg + ".bak")) File.Delete(tempWsCfg + ".bak"); } catch { }
                }
            }
            Check(workloadOk,
                "Automatic Per-Game / Per-Workload Profile Switcher (Rules, AI/NPU, Exit Cooldown & Settings)",
                workloadDetail);

            // 115. Native NVIDIA NVML Desktop GPU Telemetry & Trusted System Path Isolation
            bool nvmlOk = false;
            string nvmlDetail = "";
            try
            {
                string[] paths = NvmlGpuSensor.GetTrustedNvmlPaths(@"C:\Windows\System32", @"C:\Program Files");
                bool pathsStrict = (paths.Length == 2
                    && paths[0].Equals(@"C:\Windows\System32\nvml.dll", StringComparison.OrdinalIgnoreCase)
                    && paths[1].Equals(@"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll", StringComparison.OrdinalIgnoreCase));

                NvmlGpuSensor.SimulatedTelemetryHook = delegate
                {
                    return new NvmlTelemetry
                    {
                        IsAvailable = true,
                        GpuName = "NVIDIA GeForce RTX 5080",
                        GpuTempC = 61,
                        GpuLoadPct = 88,
                        PowerWatts = 240,
                        PowerLimitWatts = 360,
                        GraphicsClockMhz = 2650,
                        MemoryClockMhz = 15000,
                        FanSpeedPct = 48
                    };
                };

                using (NvmlGpuSensor sensor = new NvmlGpuSensor())
                {
                    NvmlTelemetry t = sensor.ReadTelemetry();
                    bool simOk = t.IsAvailable && t.GpuName == "NVIDIA GeForce RTX 5080"
                        && t.GpuTempC == 61 && t.PowerWatts == 240 && t.PowerLimitWatts == 360
                        && t.GraphicsClockMhz == 2650 && t.GpuLoadPct == 88
                        && RadeonSettingsSensor.IsDiscreteGpuDescription(t.GpuName);
                    nvmlOk = pathsStrict && simOk;
                    nvmlDetail = string.Format("StrictPaths={0}, SimGpu={1}({2}C/{3}W/{4}%)",
                        pathsStrict, t.GpuName, t.GpuTempC, t.PowerWatts, t.GpuLoadPct);
                }
            }
            catch (Exception ex) { nvmlDetail = ex.Message; }
            finally
            {
                NvmlGpuSensor.SimulatedTelemetryHook = null;
            }
            Check(nvmlOk,
                "Native NVIDIA NVML Desktop GPU Telemetry & Trusted System32 Path Isolation",
                nvmlDetail);

            // 116. Native Windows Service Host (Quoted ImagePath & External Stop/Power Lifecycle Hooks)
            bool winSvcOk = false;
            string winSvcDetail = "";
            try
            {
                string binPath = WindowsServiceHost.BuildServiceBinaryPath(@"C:\Program Files\MiniFanControl\minifan-helper.exe");
                bool quotedOk = binPath.Equals("\"C:\\Program Files\\MiniFanControl\\minifan-helper.exe\" service", StringComparison.OrdinalIgnoreCase);

                bool cmdGateOk = !Program.ShouldPreloadDriverForCommand("service-install", false)
                    && !Program.ShouldPreloadDriverForCommand("service-uninstall", false)
                    && !Program.ShouldPreloadDriverForCommand("service-status", false)
                    && Program.ShouldPreloadDriverForCommand("service", false);

                ManualResetEvent extStop = new ManualResetEvent(false);
                ManualResetEvent hooksReady = new ManualResetEvent(false);
                Action suspCb = null;
                Action resCb = null;
                int exitCode = -1;

                Thread svcThread = new Thread(delegate()
                {
                    exitCode = HeadlessDaemon.Run(
                        true, 100, 0, true, extStop,
                        delegate(Action onS, Action onR)
                        {
                            suspCb = onS;
                            resCb = onR;
                            hooksReady.Set();
                        });
                });
                svcThread.IsBackground = true;
                svcThread.Start();

                bool gotHooks = hooksReady.WaitOne(2000) && suspCb != null && resCb != null;
                if (gotHooks)
                {
                    suspCb();
                    resCb();
                }
                extStop.Set();
                bool joined = svcThread.Join(2000);
                extStop.Close();
                hooksReady.Close();

                winSvcOk = quotedOk && cmdGateOk && gotHooks && joined && (exitCode == 0);
                winSvcDetail = string.Format("QuotedPath={0}, CmdGate={1}, PowerHooks={2}, CleanStop={3}(rc={4})",
                    quotedOk, cmdGateOk, gotHooks, joined, exitCode);
            }
            catch (Exception ex) { winSvcDetail = ex.Message; }
            Check(winSvcOk,
                "Native Windows Service Host (Quoted ImagePath & External Stop/Power Lifecycle Hooks)",
                winSvcDetail);
        }
    }
}
