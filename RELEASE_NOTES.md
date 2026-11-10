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
- **Root**: `Program.cs`, `MiniFanControl.csproj` (SDK-style multi-target support for both .NET Framework 4.8 and .NET 8.0-windows).

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
- **Multi-Target CI Pipeline**: Updated `.github/workflows/ci.yml` to verify modern SDK builds (`net48` and `net8.0-windows` via `dotnet build MiniFanControl.csproj`) alongside the standalone .NET 4.8 `build-windows.bat` release pipeline.
- **Synchronized Versioning**: Authoritative version `2.7.0` synchronized across `VERSION`, `MiniFanControl.csproj`, `Ui/MainForm.cs`, `installer.iss`, `package-release.bat`, and documentation.
- **Expanded 28-Point Automated Safety Matrix**: Added Test 26 (Strict Fail-Closed Port Whitelist Enforcement), Test 27 (Centralized Hardware Initializer Safe Default), and Test 28 (Super I/O Write Read-Back & Separate Restore Paths).

---

## 🧪 Automated Verification Matrix
- **28/28 Self-Tests Passing (100%)**:
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
