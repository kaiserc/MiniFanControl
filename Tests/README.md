# MiniFanControl — Automated Hardware Safety & Curve Test Suite (122/122 Passing)

The built-in test suite (`minifan-helper.exe test`, run automatically by `build-windows.bat` and GitHub Actions CI on both `.NET 4.8` and `.NET 10.0-windows`) rigorously validates hardware safety mechanisms, driver integrity gates, curve math, mutex synchronization, and UI/OSD contracts without touching physical EC/Super I/O registers.

> **Binary Separation:** Starting with the `v1.3.1` stabilization line, `Tests/*.cs` is compiled exclusively into `minifan-helper.exe` (via `/define:INCLUDE_SELF_TESTS`) and the SDK test target, keeping the shipping `MiniFanControl.exe` GUI binary lean and free of test harnesses.

## Running the Suite

```powershell
# Run all 122 automated self-tests
.\minifan-helper.exe test

# Run the 6-profile hardware validation matrix
.\minifan-helper.exe hw-matrix

# Validate community language files and external hardware profiles
.\minifan-helper.exe validate-lang
.\minifan-helper.exe validate-profiles
```

---

## Full 122-Test Catalog

### Core Curves, Calibration & Hardware Safety (`#1–#36`)
1. **Motor Stall Dead-Zone Protection**: Verifies fan speeds 1%–29% are clamped to safe minimums across 0°C–105°C.
2. **47°C Balanced Curve Stall Regression Test**: Confirms output never dips into the motor stall region at 47°C.
3. **Zero-RPM Idle Segment & Clean Step-Up**: Ensures fans stay at 0% in idle (<45°C) and step up cleanly (≥30%) above 51°C.
4. **Curve Boundary Clamping**: Verifies extreme inputs (15°C → 0%, 99°C → 100%) remain strictly bounded.
5. **Tachometer Stall Filter**: Filters tachometer anomalies (8000 / 9489 / 65535 RPM mapped to 0, valid speeds preserved).
6. **EC Register Bitmask Encoding & Safety Bounds**: Validates `0x80` manual flag and clamped PWM bytes (`0x32`–`0x34`).
7. **Custom Curve Config Save/Load Round-Trip**: Verifies serialization and deserialization integrity across profiles.
8. **Multi-Model Ryzen Mini-PC Profile Matching**: Validates profile detection for K12, K8 Plus, HX 370, TS-12C, and AriesBox 10.
9. **Simulation Backend State Machine**: Tests manual fan overrides and failsafe return to BIOS Auto.
10. **Smart EC Candidate Scanner & Dynamic Register Override**: Validates runtime command-line register overrides.
11. **3-Pass Fan Calibration Engine, Date Tracking & Safety Margin**: Validates multi-pass calibration and `+2%` safety margin.
12. **CapFrameX `Global\Access_EC` Mutex & APU Power Breakdown**: Tests mutex acquisition and PPT breakdown tooltip.
13. **Session Peak/Min/Avg Telemetry & Multi-Series Live Graph History**: Tests telemetry rolling window and statistical tracking.
14. **CapFrameX OSD Multi-Tier Sizing, Live Graphs & Layout Modes**: Validates HUD geometry, scaling tiers (`100%`–`185%`), and rendering.
15. **OSD Granular Metric Toggle Verification (`ShowNvme` & `ShowRam`)**: Validates independent metric visibility toggles.
16. **OSD Horizontal Bar Auto-Width Expansion**: Validates dynamic horizontal width sizing across 11 merged metrics.
17. **OSD Driver Version & Afterburner Theme Integration**: Validates Afterburner palette contrast and Radeon driver version row.
18. **OSD Center-Top & Center-Bottom Snap Location Geometry**: Validates multi-monitor snap coordinate positioning.
19. **Fail-Closed `EcBusMutex` Contention & Safe Mutex Isolation**: Verifies fail-closed behavior under cross-process lock contention.
20. **Emergency Thermal Safety Tripwire & Cooldown Hysteresis**: Tests instant 100% full-blast tripwire at ≥90°C and 75°C recovery.
21. **Safe Fail-Closed Hardware Probe Validation**: Verifies probe failures cleanly reject blind port I/O fallback.
22. **Hardware Watchdog Supervisor Lifecycle**: Tests clean shutdown handshake, heartbeat timeout triggers, and process crash recovery.
23. **Driver Security Port Whitelist & Memory Map Safety Bounds**: Validates port whitelist security and physical memory bounds checking.
24. **Unverified Hardware Fail-Closed Read-Only Protection**: Confirms unrecognized hardware platforms are locked to read-only mode.
25. **Session Telemetry CSV Export Integrity & Schema Validation**: Validates CSV export format, headers, and values.
26. **Missing-Mutex Fail-Closed Enforcement & Lock Security**: Verifies missing EC, ISA, PCI, or SMBus mutex triggers fail-closed isolation.
27. **Watchdog IPC Named-Event Fail-Closed & Auto Read-Back Verification**: Validates unnamed event fallback rejection and read-back state confirmation.
28. **Parameter Validation & Invalid Fan ID / Mode Rejection**: Verifies out-of-bounds fan IDs, PWM percentages, and mode codes are rejected fail-closed.
29. **Hardware Write Read-Back Verification & Read-Only Safety Protection**: Verifies read-back mismatches, read-only profiles, and mutex contention block writes.
30. **Strict Fail-Closed Port Whitelist Enforcement**: Verifies unlisted and sensitive ports (`0x70/0x71`, `0x92`, `0xCF9`, `0x20/0x21`, `0x40..0x43`, `0x61`) are blocked fail-closed.
31. **Centralized Hardware Initializer Safe Default**: Verifies default initialization selects `unknown-safe` (`IsReadOnly = true`).
32. **Super I/O Write Read-Back & Separate Restore Paths**: Validates `WriteSuperIoHwReg()` read-back, `RestoreNativeEcAuto()`, and `ApplySafeSuperIoFallback()`.
33. **Super I/O Write Failure Detection in `SetFan()`**: Verifies failed Super I/O writes cause `SetFan()` to fail closed.
34. **Thread-Safe Mutex Initialization & Concurrency Safety**: Verifies race-free, multithreaded initialization of `EcBusMutex`.
35. **Watchdog IPC Access Control List (ACL) Hardening**: Verifies named IPC events purge `WorldSid` and AppContainer permissions.
36. **Native BIOS Auto vs. Safe Super I/O Fallback Distinction**: Validates status distinction between native BIOS Auto and safe Super I/O fallback.

### Multi-Profile Matrix, Sensors, OSD & Silicon Telemetry (`#37–#77`)
37. **UI Write-Result Fail-Closed Handling & Independent Emergency Retries**: Verifies UI caches speeds only on confirmed write/read-back and escalates after 10 failures.
38. **Watchdog Ready Handshake, Scoped Roles & Global/Local Path Verification**: Verifies two-way `ReadyEvent` handshake and timeout detection.
39. **Multi-Profile Hardware Validation Matrix (6 Hardware Profiles)**: Validates all 6 built-in profiles, register bounds, polarity, and failsafes.
40. **Authenticated Mutex DACL & Local Fallback Diagnostics**: Verifies `AuthenticatedUserSid` DACL and namespace diagnostics.
41. **Native NVMe Storage & System RAM Sensor Telemetry**: Verifies NVMe SMART log page `0x02` and `GlobalMemoryStatusEx` metrics.
42. **Session Telemetry NVMe & RAM Power Tracking & Sparklines**: Verifies rolling history and peak/avg calculations for NVMe and RAM power.
43. **High-Resolution RTSS Frametime, Lows & AMD Radeon Feature Telemetry**: Verifies 99th/99.9th percentile lows and OSD bitmap rendering.
44. **UI Layout Anti-Clipping Geometry**: Validates non-overlapping bounds across telemetry tiles, OSD Studio, and Options card.
45. **Digital Foundry Dual Graphs Dynamic Scale Ladder & Baseline Anti-Clipping**: Validates 60/120/180/240 FPS ladders and 4-tier heights.
46. **Driver Engine Isolation & WinRing0 MMIO Heap-Mapping Prevention**: Validates linear map isolation and minimum physical memory bounds.
47. **Strict Fail-Closed Dword Port Whitelist Aperture Enforcement**: Validates 32-bit dword I/O permits only `0xCF8` and `0xCFC`.
48. **Safe Read-Only Telemetry Fallback When Kernel Driver Is Blocked**: Validates graceful ADL/Storage telemetry fallback without exceptions.
49. **Single-Instance Mutex & Clean Driver Shutdown Lifecycle**: Validates single-instance guard and full driver resource teardown.
50. **Anti-Poisoning Gameplay 0.1% Lows & Startup Hitch Rejection**: Validates loading hitches never corrupt gameplay 0.1% lows.
51. **AMD Silicon & ADL Extended Telemetry Validation**: Validates CPU clocks, DDR5/LPDDR5X clocks, hotspot temps, and PPT limits.
52. **Tier 2 EC Motherboard Differential Sensor Scanner & Telemetry**: Validates ambient thermistors, VRM zones, and PWM discovery.
53. **Dynamic Custom `DeviceProfile` Generation & JSON Serialization**: Validates custom profile synthesis from scan diffs.
54. **Tier 3 AMD SMU / SMN Telemetry & Deep Silicon VRM / FCLK Bus Sync**: Validates VRM TDC/EDC currents and FCLK ratio detection.
55. **NPU Telemetry, "AI Workload" Fan Curve & Proactive Thermal Coupling**: Validates feed-forward NPU thermal coupling and overlay rendering.
56. **Localization Engine (`Loc`) & Community JSON Schema**: Validates all 6 built-in languages and drop-in JSON translations.
57. **Live Performance Tab & Digital Foundry Frametime Engine**: Validates inverted stutter headroom scaling and dynamic FPS ladders.
58. **Live Trend First Tab Sequence & 1440p Taskbar Auto-Fit**: Validates tab ordering and `Screen.WorkingArea` clamping.
59. **Protected Reference Curves (`🔒`) & `✨ Auto-Tune` Curve Synthesizer**: Validates reset-only templates and TDP-aware curve synthesis.
60. **AMD Silicon Junction SMN `Tctl/Tdie` Decoding**: Validates Zen 5 SMN `0x00059800` decode parity with HWiNFO64.
61. **Live Trend & Overlay Metric Ordering & Format Parity**: Validates canonical metric ordering across UI and OSD.
62. **AMD FSR Upscaling Detection, Multi-NVMe I/O & RAM Dual Peaks**: Validates FSR pill detection and multi-drive throughput tracking.
63. **DDR Dual Data Rate (`MT/s`) & Dual Stacked SODIMM Temps**: Validates `2x MCLK` speed and upper/lower SODIMM differential tracking.
64. **Phased `RefreshThrottle` Bus Optimization**: Validates staggered SMBus/storage polling offsets.
65. **Hook-Free Overlay Dirty-Check & Ryzen Tier Badges**: Validates hash-based dirty-checking to skip redundant GDI+ blits.
66. **RAM & NVMe Dynamic Power Readouts**: Validates DDR5 and NVMe dynamic power display across UI and OSD.
67. **Live Telemetry Auto-Zoom & Right-Axis RPM Decoupling**: Validates dual-axis autoscaling without clipping.
68. **Cross-Process Watchdog IPC Verification & Child Handshake**: Spawns live `minifan-helper.exe watchdog` child process and verifies handshake/exit.
69. **Sleep & Suspend Lifecycle Protection**: Validates S3 suspend worker pause, tripwire reset, and quiet standby fallback.
70. **`DeviceProfile.FromJson` Colon Preservation**: Ensures JSON values containing colons deserialize intact.
71. **Tokenizing `Loc` JSON Parser**: Validates escaped quotes and `\uXXXX` Unicode escape decoding.
72. **`OsdOverlayConfig` Defaults & Settings Round-Trip**: Verifies preset toggles and config persistence parity.
73. **Custom Controls Accessibility**: Confirms keyboard `TabStop` and UIA `AccessibleRole` metadata.
74. **`FanControllerService` Decoupled Control & Tripwire**: Verifies decoupled curve evaluation and emergency clamping.
75. **`DeviceProfile` CLI Export**: Verifies `export-profiles` JSON and summary table outputs.
76. **Hardware Probe JSON Export & Super I/O Chip Identification**: Verifies `dump --json` and `IT5570E` / `IT8613E` / `NCT67xx` IDs.
77. **Calibrated 14% Floor, BIOS-Mode Auto-Tune & OSD `Graphs Only` / `DF Corner` Config**: Verifies low-floor calibration and OSD config round-trips.

### Security Hardening, PawnIO HVCI, ETW Latency, Service Mode & Themes (`#78–#122`)
78. **`NativeInpOut` SHA256 Pin Verification & Single-Choke-Point Port Whitelist**: Verifies runtime SHA256 pin enforcement and port blocking.
79. **Custom `DeviceProfile` Fail-Closed `FromJson`, DMI Fingerprint Binding & Blocklist Precedence**: Verifies machine-bound custom JSON and blocklist priority.
80. **CLI Fan Duty Anti-Stall Floor Clamping & `--force` Override**: Verifies CLI floor clamping and explicit `--force` override.
81. **Read-Only Failsafe Latch Preserves Emergency Tripwire & Periodic `SetAuto()` Retry**: Verifies ≥90°C emergency cooling survives read-only latch.
82. **CLI `status` JSON `InvariantCulture` Formatting & String Escaping**: Verifies `de-DE` locale safety and JSON escaping.
83. **`EvaluateCurve` Defensive Sorting & Monotonic Thermal Safety**: Verifies unsorted curves are monotonically clamped.
84. **Tachometer Spike Filter Bypass on 100% Emergency Spin-Up**: Verifies immediate RPM reporting during emergency spin-up.
85. **`SetBiosPowerMode` Platform Validation Order**: Verifies Super I/O platforms reject EC mode mutations cleanly.
86. **Unified `FanControllerService.ApplyManualFan` Anti-Stall Clamping**: Verifies GUI and CLI share identical floor-clamping logic.
87. **`NativeInpOut` Physical Memory Allowlist & EC-Only Write Barrier**: Verifies EC MMIO / PCIe ECAM / SMU allowlist and write barrier.
88. **Super I/O `0%` Stop Duty, Tach Channel Latching & Non-Zero Floor Calibration**: Verifies clean Channel 1/2 tach isolation.
89. **Super I/O IT8613E Dynamic `Reg 0x14` Bit 7 PWM Polarity Tracking**: Verifies Direct (`0xC7`) and post-sleep Inverted (`0x47`) polarity math.
90. **Native Windows Power Mode Overlay Mapping (`WindowsPowerMode`)**: Verifies Windows 11 Efficiency / Balanced / Performance overlay mapping.
91. **PawnIO HVCI Driver Backend, Priority-0 Selection & Port Whitelist Gating**: Verifies Priority-0 PawnIO routing and fail-closed port gating.
92. **Anti-Cheat Safe Auto-Pause State Machine & Ordered Handoff (`AntiCheatMonitor`)**: Verifies EAC/BattlEye/Vanguard detection and BIOS-Auto-before-unload ordering.
93. **Headless Background Daemon Mode (`HeadlessDaemon`)**: Verifies zero-UI fan loop and BIOS Auto restore on exit.
94. **Sanitized Hardware Profile Submission (`HardwareProfileSubmission`)**: Verifies PII path redaction and pre-filled GitHub Issue URLs.
95. **Windows 11 DWM Visual Style (`Win11VisualStyle`)**: Verifies `COLORREF`, rounded cards, and null-form safety.
96. **Opt-In Anti-Cheat Persistence & `MainForm` Win11 DWM Handle Integration**: Verifies `AutoPauseForAntiCheat` opt-in default.
97. **Unverified Desktop Hardware (`unknown-safe`) Read-Only Guard**: Verifies zero failsafe spam on read-only desktops.
98. **High-DPI GDI Scaling Manifest & `MainForm` Layout Integrity**: Verifies `gdiScaling=true` and `AutoScaleMode.None`.
99. **Desktop PC OSD Polish & Consistent Frametime**: Verifies discrete GPU labels, ASCII hotspot, and 1% Low history.
100. **Native Windows ETW Display Latency Tracker & x64 Struct Alignment**: Verifies Win64 ETW struct sizes and MPO/FreeSync latency math.
101. **ETW Display Latency Stats, OSD Studio Persistence, Overlay/LivePerf Render & Status JSON**: Verifies end-to-end latency telemetry.
102. **PawnIO ProgramFiles Priority, Fail-Closed Authenticode Verification & `LpcACPIEC` Routing**: Verifies `WinVerifyTrust` gating and ACPI EC routing.
103. **HeadlessDaemon Anti-Cheat Pause & Automatic Resume While Worker Paused (Zero Deadlock)**: Verifies deadlock-free anti-cheat resume.
104. **Read-Only / Non-AMD Silicon Write Gate & Temperature-Aware Super I/O Safe Fallback**: Verifies temperature-scaled safe fallback.
105. **Horizontal OSD Brush-Safe Render (`FCLK`/`VRM`/`NPU`) & ETW Shutdown/AutoStart Guard**: Verifies GDI brush lifecycle safety.
106. **`WatchdogSupervisor` Log Rotation Rollover Cap & Monotonic Timing**: Verifies bounded log rollover and monotonic timing.
107. **Driver `ReaderWriterLock` Shutdown Safety & Deferred CLI Preload Policy**: Verifies concurrent read draining before unload.
108. **Atomic `AppSettings` Write, Profile Clone Non-Mutation & `CustomProfile` Deduplication**: Verifies atomic config save and clone isolation.
109. **Unverified Profile Read-Only Invariant (`IsVerified`) & `HardwareBackend.SetMode` Fail-Safe**: Verifies read-only invariants.
110. **Narrow `FpsMonitor` Crash Filter & Zero-Alloc `EtwPresentTracker` Ring Buffer Wrap**: Verifies RTSS exception isolation and ring-buffer wrap.
111. **Independent APU & Sys Fan Cooldown Hysteresis**: Verifies NVMe/RAM spikes are never masked by APU dips.
112. **PawnIO `AMDFamily17` SMN Module, Address Allowlist & Tier-0 `ReadAmdSmn` Integration**: Verifies HVCI-safe AMD SMN reads.
113. **External JSON Hardware Profile Registry (`profiles/*.json`), Validation & Blocklist Precedence**: Verifies external profile loading and blocklist priority.
114. **Automatic Per-Game / Per-Workload Profile Switcher (`ProfileSwitcher`)**: Verifies process/AI workload rules and exit cooldown.
115. **Native NVIDIA NVML Desktop GPU Telemetry & Trusted System32 Path Isolation**: Verifies NVML parsing and trusted path loading.
116. **Native Windows Service Host (`WindowsServiceHost`)**: Verifies quoted `ImagePath` and SCM stop/power hooks.
117. **Bundled Signed PawnIO Modules, Guided Driver Onboarding Dialog & Startup Prompt Persistence**: Verifies bundled modules and onboarding flow.
118. **PawnIO `LpcIO` Slot Selection, `ioctl_find_bars` BAR Whitelisting & No-Driver Failsafe Gating**: Verifies Super I/O BAR discovery on HVCI-ON Windows 11.
119. **Windows Service Mode (`--config` ImagePath, Shared Config Sync, `LeanServiceMode` & Live Hot-Reload)**: Verifies `%ProgramData%` shared config and hot-reload.
120. **Modern Standby (S0ix) Time-Jump Wake Detection & Super I/O Reset Protection**: Verifies S0ix gap detection and `0xFF` self-healing.
121. **Xmas Edition Theme (White/Black/Red Palette, Concept #6 Logo, Settings Persistence & OSD Sync)**: Verifies `ThemePreference.Xmas = 5` end-to-end.
122. **PawnIO `.bin` Module SHA256 Pinning, Legacy WinRing0 Hard-Gate, `Local\` Mutex Sync Check & CLI JSONL Streaming**: Verifies bundled `.bin` SHA256 pins accept official blobs and reject tampered payloads, confirms WinRing0 is blocked by default (`AllowLegacyWinRing0Fallback = false`), checks `EcBusMutex.IsCrossSessionSynchronized`, and verifies `FormatStatusJsonLine()` valid JSONL output with UTC timestamps.
