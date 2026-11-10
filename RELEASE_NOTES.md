# Release Notes: MiniFanControl v2.6.0 (Production Release)

**MiniFanControl v2.6.0** is a major milestone release delivering enterprise-grade hardware safety, out-of-process crash supervision, kernel driver security hardening, tachometer stabilization, clean architectural modularization, and unified Windows distribution packaging.

---

## 🚀 Key Highlights & Architectural Enhancements

### 🛡️ Phase 1: Hardware Safety & Motor Stall Protection
- **Stall Dead-Zone Elimination**: Enforces hardware-safe fan motor minimums (PWM dead-zone clamp prevents fan motors from stalling or overheating between 1% and 29%).
- **Automated 3-Pass Calibration**: Measures true mechanical minimum startup speed for both APU and System fans, applying a +2% safety margin and storing calibration timestamps.
- **Emergency Thermal Safety Tripwire**: Failsafe instantly engages 100% full-blast cooling if silicon temperature reaches or exceeds 90°C, with a 75°C recovery threshold.
- **2°C Cooldown Hysteresis**: Prevents rapid acoustic oscillations ("fan ramping") during bursty CPU workloads.

### ⏱️ Phase 2: Crash Protection & Out-of-Process Watchdog
- **Decoupled Watchdog Process**: Launches `minifan-helper.exe watchdog` as an independent supervisor process.
- **Heartbeat Monitoring (IPC Named Pipe / Shared State)**: If the main UI thread freezes or the GUI process terminates unexpectedly, the watchdog automatically restores native BIOS/EC automatic fan control within 3 seconds.
- **Graceful Termination Handshake**: Clean UI shutdown sends an explicit termination signal to disarm the watchdog safely.

### 🔒 Phase 3: Driver Security & Ring-0 Hardening
- **Strict Kernel Port Whitelisting**: `NativeInpOut` restricts ring-0 port operations to verified ACPI EC ports (`0x62/0x66`, `0x6C/0x6E`) and LPC Super I/O ports (`0x2E/0x2F`, `0x4E/0x4F`). All other ports are rejected fail-closed to eliminate system instability.
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

### 🛡️ Phase 6: Release Hardening & Safety Verification
- **Unverified Hardware Protection**: `DeviceProfile` introduces an explicit `unknown-safe` profile with `IsReadOnly = true`. If run on an unrecognized hardware platform, all register writes are blocked fail-closed at the hardware layer, preventing damage while retaining safe read-only telemetry monitoring.
- **Tachometer Register Realignment**: Resolved documentation discrepancies; tooltips now dynamically reflect the active profile's physical registers (`0x35/0x36` & `0x37/0x38` on EC, `0x0E/0x19` & `0x0F/0x1A` on IT8613 Super I/O).
- **Power Telemetry Transparency**: Distinctly badges direct measured AMD APU silicon package telemetry (PPT via ADL2 PMLOG) vs algorithmic component breakdowns (`[Estimated]`) when individual rail sensors are unexposed.
- **Thermal History & CSV Export**: Added 1-click session telemetry CSV export under **Tools & Drivers ▾**, recording up to 10,000 samples of timestamped temperatures, RPM, power, fan duty, mode, GPU load, and FPS for benchmarking.
- **GitHub Actions CI/CD & Issue Templates**: Added automated build and test pipeline (`.github/workflows/ci.yml`) and structured GitHub issue templates.

---

## 🧪 Automated Verification
- **21/21 Self-Tests Passing**:
  1. Motor Stall Dead-Zone Protection
  2. 47°C Balanced Curve Stall Regression Test
  3. Zero-RPM Idle Segment & Clean Step-Up Transition
  4. Curve Boundary Clamping
  5. Tachometer Stall Filter
  6. EC Register Bitmask Encoding & Safety Bounds
  7. Custom Curve Config Save/Load Round-Trip
  8. Multi-Model Ryzen Mini-PC Profile Matching
  9. Simulation Backend State Machine
  10. Smart EC Candidate Scanner & Dynamic Register Override Parsing
  11. 3-Pass Fan Calibration Engine & Safety Margin
  12. CapFrameX `Global\Access_EC` Mutex & APU Power Breakdown
  13. Session Peak/Min/Avg Telemetry & Multi-Series Live Graph History
  14. CapFrameX OSD Multi-Tier Sizing, Live Graphs & Layout Modes
  15. Fail-Closed EcBusMutex Contention & Safe Mutex Isolation
  16. Emergency Thermal Safety Tripwire & Cooldown Hysteresis
  17. Safe Fail-Closed Hardware Probe Validation
  18. Hardware Watchdog Supervisor Lifecycle
  19. Driver Security Port Whitelist & Memory Map Safety Bounds
  20. Unverified Hardware Fail-Closed Read-Only Protection
  21. Session Telemetry CSV Export Integrity & Schema Validation
