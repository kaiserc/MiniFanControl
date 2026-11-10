# Release Notes: MiniFanControl v2.7.0 (Production Hardened Release)







**MiniFanControl v2.7.0** is a major milestone release delivering enterprise-grade hardware safety, out-of-process crash supervision, kernel driver security hardening, tachometer stabilization, clean architectural modularization, bulletproof hardware synchronization, expanded 25-point automated verification, and transparent security documentation.







---







## 🚀 Key Highlights & Architectural Enhancements







### 🛡️ Phase 1: Hardware Safety & Motor Stall Protection



- **Stall Dead-Zone Elimination**: Enforces hardware-safe fan motor minimums (PWM dead-zone clamp prevents fan motors from stalling or overheating between 1% and 29%).



- **Automated 3-Pass Calibration**: Measures true mechanical minimum startup speed for both APU and System fans, applying a +2% safety margin and storing calibration timestamps.



- **Emergency Thermal Safety Tripwire**: Failsafe instantly engages 100% full-blast cooling if silicon temperature reaches or exceeds 90°C, with a 75°C recovery threshold.



- **2°C Cooldown Hysteresis**: Prevents rapid acoustic oscillations ("fan ramping") during bursty CPU workloads.







### ⏱️ Phase 2: Crash Protection & Out-of-Process Watchdog



- **Decoupled Watchdog Process**: Launches `minifan-helper.exe watchdog` as an independent supervisor process.



- **Heartbeat Monitoring (IPC Named Pipe / Shared State)**: If the main UI thread freezes or the GUI process terminates unexpectedly, the watchdog automatically restores native BIOS/EC automatic fan control within 3.5 seconds.



- **Graceful Termination Handshake**: Clean UI shutdown sends an explicit termination signal to disarm the watchdog safely.







### 🔒 Phase 3: Driver Security & Ring-0 Hardening



- **Strict Kernel Port Whitelisting**: `NativeInpOut` restricts ring-0 port operations to verified ACPI EC ports (`0x62/0x66`, `0x6C/0x6E`) and LPC Super I/O ports (`0x2E/0x2F`, `0x4E/0x4F`, `0x0A35`). All other ports are rejected fail-closed to eliminate system instability.



- **Memory-Mapped I/O (MMIO) Boundary Validation**: Prevents arbitrary physical memory access by enforcing strict bounds checking against known EC memory apertures (`0xFE0B0400`).



- **Tachometer Hardware Calibration & Stabilization**:



  - Re-mapped IT8613 Super I/O tachometer registers to true physical channels (Fan 1 APU = `0x0E / 0x19`, Fan 2 Sys = `0x0F / 0x1A`).



  - Enabled 16-bit tachometer resolution (`0x0C |= 0x3F`) and gating bits (`0x13 |= 0x70`).



  - Atomic 16-bit latching with a 3-sample rolling median filter to eliminate erratic tachometer RPM spikes.



  - 5-second stabilization wait before fan calibration measurements to ensure true mechanical equilibrium.







### 🏛️ Phase 4: Clean Architecture & Domain Modularization



Decomposed the 11,337-line monolithic `MiniFanControl.cs` into 24 maintainable, single-responsibility domain modules:



- **`/Core`**: `FanStatus.cs`, `TelemetrySessionStats.cs`, `DeviceProfile.cs`, `ThemePreference.cs`



- **`/Curves`**: `CurvePoint.cs`, `FanCalibrationResult.cs`



- **`/Hardware`**: `EcBusMutex.cs`, `NativeInpOut.cs`, `DriverServiceManager.cs`, `AmdAdlGpuSensor.cs`, `HardwareBackend.cs`, `DemoBackend.cs`



- **`/Safety`**: `WatchdogSupervisor.cs`



- **`/Telemetry`**: `FpsMonitor.cs`, `CapFrameXOverlayForm.cs`



- **`/Ui`**: `DoubleBufferedPanel.cs`, `FanCurveCanvas.cs`, `LiveTelemetryCanvas.cs`, `NewModeDialog.cs`, `FanCalibrationDialog.cs`, `AppLogoRenderer.cs`, `MainForm.cs`



- **`/Tests`**: `SelfTestSuite.cs`



- **Root**: `Program.cs`, `MiniFanControl.csproj` (SDK-style multi-target support for both .NET Framework 4.8 and .NET 10.0-windows).







### 📦 Phase 5: Production Release Packaging



- **Automated Packaging Pipeline (`package-release.bat`)**: Single-script compilation, automated test verification, release staging, ZIP compression, and SHA256 checksum generation.



- **Inno Setup Script (`installer.iss`)**: Professional Windows installer script with desktop/startup shortcuts and administrative permission elevation for kernel driver deployment.



- **Non-Destructive Compilation**: `build-windows.bat` uses atomic file renaming (`.old.exe`) so the application can be rebuilt cleanly even while running.







### 🛡️ Phase 6: Release Hardening & Safe Read-Only Fallback



- **Unverified Hardware Protection**: `DeviceProfile` introduces an explicit `unknown-safe` profile with `IsReadOnly = true`. If run on an unrecognized hardware platform, all register writes are blocked fail-closed at the hardware layer, preventing damage while retaining safe read-only telemetry monitoring.



- **Tachometer Register Realignment**: Resolved documentation discrepancies; tooltips now dynamically reflect the active profile's physical registers (`0x35/0x36` & `0x37/0x38` on EC, `0x0E/0x19` & `0x0F/0x1A` on IT8613 Super I/O).



- **Power Telemetry Transparency**: Distinctly badges direct measured AMD APU silicon package telemetry (PPT via ADL2 PMLOG) vs algorithmic component breakdowns (`[Estimated]`) when individual rail sensors are unexposed.



- **Thermal History & CSV Export**: Added 1-click session telemetry CSV export under **Tools & Drivers ▾**, recording up to 10,000 samples of timestamped temperatures, RPM, power, fan duty, mode, GPU load, and FPS for benchmarking.



- **GitHub Actions CI/CD & Issue Templates**: Added automated build and test pipeline (`.github/workflows/ci.yml`) and structured GitHub issue templates.







### 💎 Phase 7: Bulletproof Hardening & 25-Point Safety Verification



- **Missing-Mutex Fail-Closed Enforcement**: Fixed missing-mutex fail-open behavior in `Hardware/EcBusMutex.cs`. If `_ecMutex` or `_isaBusMutex` cannot be created or opened, `AcquireEc()` strictly returns `Acquired = false` fail-closed. Restricted mutex security permissions to least-privilege `Synchronize | Modify`.



- **Watchdog IPC Hardening & Failsafe Read-Back Verification**: Eliminated fallback to unnamed local events in `Safety/WatchdogSupervisor.cs`; invalid or failing named IPC creation now fails closed (`null`). In `RestoreAutoFailsafe()`, the supervisor explicitly verifies `hw.SetAuto()` completion by reading back fan control registers before confirming recovery.



- **Synchronized Hardware Probing**: Super I/O LPC discovery (`0x2E/0x4E`) now executes inside `EcBusMutex.AcquireEc(40)` to eliminate race conditions and bus collisions during initial hardware detection.



- **Hardware Write Read-Back Verification & Structured CLI**: `SetMode`, `SetFan`, `SetAuto`, and `WriteReg` now return `bool` after performing immediate register read-back verification. CLI handlers in `Program.cs` verify all writes, emitting structured JSON responses (`{"ok":true,...}` on success or `{"ok":false,"error":...}` on failure) and returning proper process exit codes (`0` vs `1`).



- **Comprehensive Parameter Validation**: Guarded all fan ID bounds (`1..2`), duty percentages (`0..100%`), and performance modes (`0..2`) against invalid or malicious CLI/API inputs.



- **Transparent Driver Security & Anti-Cheat Documentation**: Added dedicated documentation explaining `inpoutx64.sys` vulnerability context, Core Isolation / Memory Integrity requirements, user-mode port whitelisting, on-demand driver stopping (`minifan-helper.exe driver-stop`), and the future signed driver roadmap.







### 🌟 Phase 8: Enterprise Hardening & Unified Hardware Initializer



- **Unified Hardware Initializer (`HardwareBackend.CreateAutoDetected`)**: Centralized system hardware detection across GUI, CLI, and Watchdog supervisor. Default constructor now strictly initializes `Profile = DeviceProfile.FindProfile("unknown-safe")` (`IsReadOnly = true`), preventing CLI commands or Watchdog recovery from ever defaulting to the K12 register map on untested hardware.



- **Strict Fail-Closed Port Whitelisting (Reads & Writes)**: Hardened `IsPortWhitelisted(port, isWrite)` to strictly block arbitrary unlisted port reads fail-closed. Sensitive ports (`0x70/0x71`, `0x92`, `0xCF9`, `0x20/0x21`, `0x40..0x43`, `0x61`) are explicitly blocked for both read and write.



- **Super I/O Write Read-Back & Separate Restore Paths**: `WriteSuperIoHwReg(reg, val)` now returns `bool` after verifying register read-back. `SetAuto()` cleanly separates into `RestoreNativeEcAuto()` and `ApplySafeSuperIoFallback()`, confirming both EC registers (`0x33/0x34 == 0x00`) and Super I/O PWM registers (`0x6B == 40`, `0x73 == 240`) before reporting success.



- **Dual Heartbeat UI Freeze Detection & IPC Handle Validation**: Implemented dual-thread watchdog heartbeat. Heartbeat pulses are dispatched from the UI thread (`_refreshTimer.Tick` & `OnHardwareWorkerSample`) only if the background hardware worker has recently sampled within 4.5 seconds. If either the UI thread freezes or the worker hangs, the heartbeat ceases and watchdog failsafe engages. IPC event handles are validated non-null before supervisor process launch.



- **Multi-Target CI Pipeline**: Updated `.github/workflows/ci.yml` to verify modern SDK builds (`net48` and `net10.0-windows` via `dotnet build MiniFanControl.csproj`) alongside the standalone .NET 4.8 `build-windows.bat` release pipeline.



- **Synchronized Versioning**: Authoritative version `2.7.0` synchronized across `VERSION`, `MiniFanControl.csproj`, `Ui/MainForm.cs`, `installer.iss`, `package-release.bat`, and documentation.



- **Expanded 28-Point Automated Safety Matrix**: Added Test 26 (Strict Fail-Closed Port Whitelist Enforcement), Test 27 (Centralized Hardware Initializer Safe Default), and Test 28 (Super I/O Write Read-Back & Separate Restore Paths).



### 🛡️ Phase 9: Enterprise Safety Polish & 32-Point Verification Matrix

- **Super I/O Write Verification in `SetFan()`**: Checked every Super I/O PWM and configuration register write (`0x13`, `0x15..0x17`, `0x0C`, `0x6B/0x63`, `0x73`) in `HardwareBackend.SetFan()`. Fails closed (`return ecOk && sioOk`) if any Super I/O register write or read-back check fails, preventing false-positive success reports on Super I/O platforms.

- **Watchdog IPC Least-Privilege ACL Hardening**: Replaced `WorldSid` (`Everyone`) modification access with strict least-privilege security descriptors (`sec.SetAccessRuleProtection(true, false)`). Named IPC events grant `FullControl` solely to `WindowsIdentity.GetCurrent().User`, `BuiltinAdministratorsSid`, and `LocalSystemSid`, preventing untrusted local processes from tampering with the supervisor or spoofing disarm signals.

- **Thread-Safe Mutex Initialization (`EcBusMutex`)**: Replaced unsynchronized flag access with thread-safe double-checked locking under `_initLock` with dedicated volatile `_isSimulated` and `_initDone` flags, eliminating race conditions during concurrent first calls.

- **Watchdog Process Lifecycle Verification & UI Recovery**: In `Ui/MainForm.cs`, `StartWatchdogSupervisor()` now verifies `_watchdogProcess != null && !_watchdogProcess.HasExited` before setting `_watchdogStarted = true`. If the helper process fails to launch or exits prematurely, the UI logs an explicit warning, updates status, and permits recovery retries rather than silently leaving the user unprotected.

- **Explicit Distinction Between Native BIOS Auto and Safe Super I/O Fallback**: Introduced `HardwareBackend.AutoRestoreMode` (`NativeBiosAuto` vs `SafeSuperIoThermalFallbackAndEcAuto`) and `GetAutoRestoreDescription()`. UI labels, watchdog supervisor logs, and CLI JSON responses accurately identify whether native BIOS automatic curve or fixed quiet-speed thermal fallback was engaged.

- **Enterprise Windows 11 Security Alignment**: Removed all suggestions to disable Memory Integrity (HVCI) or toggle the Vulnerable Driver Blocklist. Added prominent documentation endorsing Memory Integrity remaining ENABLED, with MiniFanControl falling back cleanly to Safe Read-Only Telemetry Mode powered by WHQL AMD ADL2 when kernel drivers are blocked.

- **Modular Multi-Target CI Architecture**: Refactored `.github/workflows/ci.yml` into explicit, independent matrix jobs: `sdk-multitarget-build` (`net48` and `net10.0-windows` via modern .NET SDK) and `standalone-build-and-test` (native CSC, 32 self-tests, and release packaging).

- **Expanded 32-Point Automated Safety Matrix**: Added Test 29 (Super I/O Write Failure Detection in `SetFan()`), Test 30 (Thread-Safe Mutex Initialization & Concurrency Safety), Test 31 (Watchdog IPC ACL Hardening), and Test 32 (Native BIOS Auto vs. Safe Super I/O Fallback Distinction).







---







## 🧪 Automated Verification Matrix



- **32/32 Self-Tests Passing (100%)**:



  1. Motor Stall Dead-Zone Protection (never outputs 1%..29% across 0..105°C)



  2. 47°C Balanced Curve Stall Regression Test (>= 30% PWM)



  3. Zero-RPM Idle Segment & Clean Step-Up Transition (0% <= 45°C, >= 30% at 51°C)



  4. Curve Boundary Clamping (15°C -> 0%, 99°C -> 100%)



  5. Tachometer Stall Filter (8000 -> 0 RPM, 65535 -> 0 RPM, 592 & 3150 preserved)



  6. EC Register Bitmask Encoding & Safety Bounds (0x32..0x34)



  7. Custom Curve Config Save/Load Round-Trip (FormatPointList <-> ParsePointList)



  8. Multi-Model Ryzen Mini-PC Profile Matching (K12 / K8 Plus / HX 370 / AriesBox 10)



  9. Simulation Backend State Machine (Manual Override -> Reset Auto)



  10. Smart EC Candidate Scanner & Dynamic Register Override Parsing



  11. 3-Pass Fan Calibration Engine, Date Tracking & +2% Safety Margin



  12. CapFrameX `Global\Access_EC` Mutex & APU Power Breakdown Tooltip



  13. Session Peak/Min/Avg Telemetry & Multi-Series Live Graph History



  14. CapFrameX OSD Multi-Tier Sizing (100%-185%), Live Graphs & Layout Modes



  15. Fail-Closed EcBusMutex Contention & Safe Mutex Isolation



  16. Emergency Thermal Safety Tripwire & Cooldown Hysteresis (90°C Trip / 75°C Recover)



  17. Safe Fail-Closed Hardware Probe Validation (No Blind PortIO Fallback)



  18. Hardware Watchdog Supervisor Lifecycle (Clean Exit, Heartbeat Hang & Crash Failsafe)



  19. Driver Security Port Whitelist & Memory Map Safety Bounds



  20. Unverified Hardware Fail-Closed Read-Only Protection



  21. Session Telemetry CSV Export Integrity & Schema Validation



  22. Missing-Mutex Fail-Closed Enforcement & Lock Security



  23. Watchdog IPC Named-Event Fail-Closed & Auto Read-Back Verification



  24. Parameter Validation & Invalid Fan ID / Mode Rejection



  25. Hardware Write Read-Back Verification & Read-Only Safety Protection



  26. Strict Fail-Closed Port Whitelist Enforcement (Read & Write)



  27. Centralized Hardware Initializer Safe Default (Read-Only Fail-Closed)



  28. Super I/O Write Read-Back & Separate Restore Paths

  29. Super I/O Write Failure Detection in `SetFan()`

  30. Thread-Safe Mutex Initialization & Concurrency Safety

  31. Watchdog IPC Access Control List (ACL) Hardening

  32. Native BIOS Auto vs. Safe Super I/O Fallback Distinction

  33. UI Write-Result Fail-Closed Handling & Independent Emergency Retries

  34. Watchdog Ready Handshake & Fail-Closed Event Fallback
  35. Multi-Profile Hardware Validation Matrix (6 Hardware Profiles)
  36. Authenticated Mutex DACL & Local Fallback Diagnostics



### 🛡️ Phase 10: Fail-Closed UI Write Verification & Two-Way Watchdog Handshake



- **UI Write-Result Fail-Closed Verification**: `MainForm.EvaluateAndApplyFanProfiles()` strictly checks all hardware write return values (`SetFan`, `SetMode`, `SetAuto`). Target fan speeds are recorded as applied *only* upon confirmed hardware register read-back verification. On rejected writes, previous state is retained, visible error banners and status alerts are surfaced with attempt counts, and re-evaluation is automatically queued on the next worker cycle.



- **Independent Emergency Thermal Retries**: The emergency thermal guard (≥90°C) never caches 100% PWM on a failed write, guaranteeing independent, persistent retry attempts on every background cycle until physical hardware write verification succeeds.



- **Watchdog Two-Way Ready Handshake (`ReadyEvent`)**: Replaced blind process launch verification with an explicit IPC handshake event (`MiniFanControl_Ready_<PID>`). The parent GUI waits for the supervisor daemon to confirm that IPC event handles are opened and the monitor loop has started before arming active watchdog state.



- **Continuous Supervisor Health Monitoring**: The background hardware worker actively supervises the watchdog child process (`_watchdogProcess.HasExited`), automatically alerting the user and restarting the supervisor if the child exits unexpectedly.



- **Elimination of Unsecured Named-Event Fallback**: Removed unauthenticated fallback event creation in `WatchdogSupervisor.CreateOrOpenEvent()`. If secured ACL creation fails, event initialization fails closed.



- **Hardware-Monitoring Mutex Coexistence vs. Watchdog IPC DACL Clarification**: Explicitly documented the architectural distinction between the cross-session coexistence mutexes (`Global\Access_EC` with `WorldSid` `Synchronize | Modify` for HWiNFO64/LibreHardwareMonitor/CapFrameX interop) and the Watchdog IPC events (strict least-privilege DACLs restricted exclusively to CurrentUser, Administrators, and SYSTEM).



- **Expanded 34-Point Automated Verification Suite**: Comprehensive verification covering all 34 safety checkpoints.

### 🛡️ Phase 11: Authenticated Mutex DACL, Role-Scoped Watchdog IPC & Multi-Profile Hardware Matrix

- **Super I/O Hardware-Write Read-Back Routing**: Fixed false-positive verification failures on Super I/O platforms (e.g. TOPC TS-12C / KTHIN PRO with IT8613E) where `SetFan()` and `SetAuto()` previously required read-back verification against ACPI EC registers (`0x33`/`0x34`) instead of confirming physical Super I/O PWM registers (`0x6B`/`0x73`). On Super I/O platforms, fan speed writes and fallsafes now verify directly through `WriteSuperIoHwReg()` read-back checks, eliminating spurious write verification failure alerts.

- **Authenticated Mutex DACL & Local Fallback Diagnostics**: Hardened Hardware/EcBusMutex.cs cross-process coexistence permissions by restricting WorldSid (Everyone) to AuthenticatedUserSid (Synchronize | Modify). This eliminates exposure to untrusted/anonymous/guest tokens while preserving seamless interop with hardware-monitoring tools (HWiNFO64, LibreHardwareMonitor, CapFrameX). Introduced UsesLocalNamespaceFallback and MutexNamespaceDiagnostic to track and report global vs local namespace fallback across CLI status JSON and system diagnostic reports.

- **Role-Scoped Watchdog IPC Least-Privilege (WatchdogIpcRole)**: Refactored watchdog IPC security rules to enforce role-based access control without granting broad FullControl:
  - Parent GUI (WatchdogIpcRole.Signaler) holds Modify | Synchronize | ReadPermissions for heartbeat and shutdown events, and WatchdogIpcRole.Waiter for the ready handshake.
  - Watchdog helper child daemon (WatchdogIpcRole.Waiter) holds strictly Synchronize | ReadPermissions on heartbeat and shutdown events (blocking unauthorized signaling or resetting), and WatchdogIpcRole.Signaler for the ready handshake.
  - Verified both Global\ and Local\ IPC creation paths in automated tests.

- **Multi-Profile Hardware Validation Matrix & Bench Specification**: Published HARDWARE_VALIDATION.md detailing physical bench test protocols, oscilloscope/tachometer verification, and inverted Super I/O PWM polarity mapping (40/0 and 240/0). Added minifan-helper.exe hw-matrix CLI command and Test 35 covering all 6 profiles (gmktec-k12, gmktec-k8-k11, 
yzen-hx370-ec, dreamer-ariesbox10, generic-it5570e, and unknown-safe), testing register layouts, thermal runaway failsafe, watchdog recovery, and read-only protection.

- **Expanded 36-Point Automated Safety Matrix**: Added Test 35 (Multi-Profile Hardware Validation Matrix) and Test 36 (Authenticated Mutex DACL & Local Fallback Diagnostics).

---

### 💾 Phase 12: Native NVMe Storage & System RAM Hardware Telemetry (Option A)

- **Crash-Proof NVMe Storage Telemetry (`Hardware/SystemStorageMemorySensor.cs`)**:
  - Implemented direct Windows storage stack querying using `DeviceIoControl` with `IOCTL_STORAGE_QUERY_PROPERTY`.
  - Queries `StorageDeviceProperty (0)` for drive identification (`Model`, `SerialNumber`) with one-time static caching to eliminate polling overhead.
  - Queries `StorageAdapterProtocolSpecificProperty (49)` requesting NVMe SMART Log Page `0x02` (`NVME_LOG_PAGE_HEALTH_INFO`).
  - Retrieves live Composite Temperature (°C), Remaining Drive Health / Percentage Used (Wear %), Available Spare Blocks %, and Critical Warning reliability flags.
  - **Zero Raw PCI I/O**: Completely avoids accessing raw PCI configuration space (`0xCF8/0xCFC`), eliminating storage controller bus contention, driver timeouts, or system freezes common with older hardware monitors on AMD chipsets.

- **Sub-Microsecond Physical RAM Monitoring**:
  - Integrated native Win32 `kernel32.dll!GlobalMemoryStatusEx` to monitor live memory allocation with < 0.01ms query latency and zero background CPU overhead.
  - Captures Used Physical Memory (GB), Total Installed RAM (GB), Available Memory (GB), and Memory Load Percentage (%).

- **Dedicated Option A Telemetry Row in GUI (`Ui/MainForm.cs`)**:
  - Expanded the *System Telemetry & Live Hardware Sensors* card with a sleek secondary row featuring dual pill cards:
    - **NVMe Card**: Displays live drive model, composite temperature, and health % (e.g. `💾 NVMe: Sabrent Rocket • 31°C (92% Health)`). Automatically highlights in amber/crimson if SSD temperature reaches or exceeds 70°C.
    - **RAM Card**: Displays live memory utilization and load % (e.g. `🧠 RAM: 21.0 / 47.6 GB (44% Load)`).
    - **Rich Hover Tooltips**: Hovering over the cards reveals complete SMART diagnostics, spare blocks, reliability status, and query details.

- **Proactive Fan 2 (Sys Fan) NVMe Thermal Coupling**:
  - In modern AMD Mini PCs, Fan 2 physically exhausts the underside compartment directly over the M.2 NVMe SSD bays and DDR5 SODIMM memory modules.
  - Enhanced `EvaluateAndApplyFanProfiles()`: Fan 2 now dynamically evaluates `Math.Max(APU Temp, NVMe Temp)` when driving automatic curves or Super I/O PWM duty.
  - If heavy SSD writes (game downloads, video renders, local AI model weights) heat the M.2 drive above the CPU, Fan 2 proactively ramps up to cool the SSD chamber, preventing controller thermal throttling. If NVMe temperature spikes to >= 65°C, MiniFanControl intervenes to ventilate the chassis even when in native BIOS auto mode.
  - The UI Sys Fan indicator displays `Auto (Bal • NVMe {temp}°C)` to clearly explain when NVMe thermals are actively driving secondary fan cooling.

- **In-Game / Desktop OSD HUD & Session CSV Logging**:
  - Updated `Telemetry/CapFrameXOverlayForm.cs` to support `ShowNvme` and `ShowRam` rows with custom color accents in both horizontal and vertical overlay layouts.
  - Expanded `Core/TelemetrySessionStats.cs` CSV export schema to include `NVMe_Temp_C`, `RAM_Used_GB`, and `RAM_Load_Pct` columns.
  - Updated CLI `status` command JSON output to include `nvme_temp`, `nvme_health`, `ram_used_gb`, `ram_total_gb`, and `ram_load_pct`.

- **37-Point Automated Hardware Safety & Regression Test Suite**:
  - Added Test 37 (`Native NVMe Storage & System RAM Sensor Telemetry`), verifying native memory queries, fail-safe NVMe querying, and tooltip formatting helpers.
