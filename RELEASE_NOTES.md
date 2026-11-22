# Release Notes: MiniFanControl v1.2.1 (High-DPI GDI Scaling & Clean Desktop Read-Only Mode)

**MiniFanControl v1.2.1** fixes high-DPI text/layout clipping on 125%–250% (1440p/4K) Windows displays and polishes the **Safe Read-Only Monitoring** experience on unverified desktop motherboards, expanding the automated test suite to **98/98 passing**.

## ✨ Highlights in v1.2.1
- **Crisp High-DPI Scaling on 4K & Scaled Desktop Monitors**: Enabled Windows 10/11 System Enhanced GDI Scaling (`app.manifest` + `HighDpiMode.DpiUnawareGdiScaled`) with fixed section-card geometry and scrollbar-aware initial sizing, fixing clipped/overlapping text in telemetry cards, buttons, live graphs, `SetupAssistantDialog`, and the OSD overlay (Test #98).
- **Zero Failsafe Spam on Desktop / Unverified Hardware (`unknown-safe`)**: `FanControllerService` now cleanly skips EC/fan writes on inherently read-only unverified profiles instead of accumulating write failures and triggering an `Escalated failsafe` banner (Test #97).
- **Clean Read-Only Monitoring UX**: Suppresses the automatic Mini PC calibration wizard popup on read-only hardware, hides unusable EC `BIOS:` header buttons, disables manual fan write controls, and accurately reports `Read-Only` across the header, fan tiles, tabs, OSD, and status bar.

---

# Release Notes: MiniFanControl v1.2.0 (PawnIO HVCI Driver, Windows 11 Visual Polish, Anti-Cheat Auto-Pause & Headless Daemon)

**MiniFanControl v1.2.0** is a major feature and visual polish release introducing native **PawnIO (`PawnIOLib`)** HVCI-compatible driver support, **Windows 11 DWM** dark/themed titlebars and sleek rounded card chrome, opt-in **Anti-Cheat Safe Auto-Pause** (EAC / BattlEye + Vanguard detection), a zero-UI **Headless Background Daemon** (`minifan-helper.exe daemon`), upgraded **1-Click Sanitized GitHub Hardware Profile Submission**, smooth **`Live Perf` FPS curve parity** with the Digital Foundry overlay, and **96/96 passing tests** on both `.NET 4.8` and `.NET 10.0-windows`.

## ✨ Highlights in v1.2.0
- **Native Windows 11 DWM Visual Polish & Smooth `Live Perf` Parity**: Zero-dependency `dwmapi.dll` theming matches the Windows 11 titlebar, text, rounded corners, and window border to Dark, Light, AMD Radeon, and Afterburner themes alongside sensor-tinted telemetry card sheens, crisp hairline card/button borders, hover/press feedback, and smooth Gaussian-averaged FPS curves on the `Live Perf` tab matching the DF Corner overlay (Tests #95–#96).
- **PawnIO (`PawnIOLib`) HVCI-Compatible Driver Backend**: Detects and loads EV-signed `PawnIOLib.dll` + signed `LpcIO.bin` / `LpcACPIEC.bin` modules as Priority 0 so Windows 11 users can keep Core Isolation / Memory Integrity enabled, while preserving fail-closed port whitelisting and seamless fallback to SHA256-pinned `InpOut64` / `WinRing0` (Test #91).
- **Opt-In Anti-Cheat Safe Auto-Pause**: New `🎮 Auto-Pause for Anti-Cheat (EAC / BattlEye)` toggle in `Tools & Drivers` strictly restores BIOS Auto / Safe Super I/O Fallback *before* unloading legacy kernel drivers while an anti-cheat is active (never unloading EV-signed PawnIO), and seamlessly resumes fan curves once the game closes (Test #92).
- **Headless Background Daemon (`minifan-helper.exe daemon`)**: Run full curve regulation, anti-stall clamping, 90°C thermal tripwire, and sleep/resume protection with zero GUI/GDI+ footprint for 24/7 servers and local LLM hosts (Test #93).
- **Upgraded 1-Click Hardware Profile Submission**: Generates a PII-sanitized JSON payload + pre-filled GitHub New Issue URL directly from `Tools & Drivers` or `minifan-helper.exe submit-report` (Test #94).

---

# Release Notes: MiniFanControl v1.1.3 (Super I/O Dynamic PWM Polarity, Native Windows Power Mode Bar & Calibration Fix)

**MiniFanControl v1.1.3** brings major reliability and control improvements for **ITE IT8613E Super I/O** Ryzen Mini PCs (such as the TOPC TS-12C / Ryzen AI 9 HX 370): live `Reg 0x14` Bit 7 (`PWM_POL`) polarity tracking across cold-boot and Modern Standby sleep/resume transitions, a native Windows 11 **`Power:`** mode bar (`Silent` / `Balanced` / `Perf`) synced via `powrprof.dll`, reliable 3-pass calibration for non-zero floor blowers, and **90/90 passing tests** on both `.NET 4.8` and `.NET 10.0-windows`.

## ✨ Highlights in v1.1.3
- **Dynamic IT8613E `Reg 0x14` PWM Polarity Tracking**: On IT8613E boards where cold BIOS POST sets `Reg 0x14 = 0xC7` (Active-High) while Modern Standby leaves `Reg 0x14 = 0x40` (Active-Low), MiniFanControl now reads Bit 7 dynamically on every duty encode/decode and 35% safe fallback—eliminating reversed fan speeds after sleep/resume—backed by a Pass 1 calibration auto-flip safety net (Test #89).
- **Native Windows Power Mode Bar on Super I/O Platforms**: Super I/O boards now get a safe **`Power: [🌙 Silent] [⚖ Balanced] [⚡ Perf]`** header switcher bound 1:1 to Windows 11 `Best Power Efficiency`, `Balanced`, and `Best Performance` power overlays (`powrprof.dll`), keeping EC **`BIOS:`** switching intact on IT5570E boards and automatically adapting `✨ Auto-Tuned` curves (Test #90).
- **Super I/O Calibration & Non-Zero Floor Blower Support**: Calibration now uses `0%` stop duty on Super I/O boards, latches verified tachometer channels so stopped secondary fans never mirror Fan 1's RPM, and recognizes settled non-zero hardware blower floors (e.g., ~320 RPM) cleanly without hitting thermal safety aborts (Test #88).
- **90/90 Automated Tests & 6/6 Hardware Matrix**: Expanded the suite to **90/90 passing** across `.NET 4.8` and `.NET 10.0-windows` alongside `6/6` Hardware Validation Matrix coverage.

---

# Release Notes: MiniFanControl v1.1.2 (Unified Manual Floor Clamp, Physical Memory Allowlist & SHA-Pinned CI)

**MiniFanControl v1.1.2** closes the final defense-in-depth hardening items: unified GUI + CLI anti-stall clamping inside `FanControllerService`, a strict physical-memory allowlist with an EC-only write barrier in `NativeInpOut`, hardened `LoadLibraryEx` search flags, SHA-pinned GitHub Actions, a 5-file modular split of `CapFrameXOverlayForm`, and **87/87 passing tests** on both `.NET 4.8` and `.NET 10.0-windows`.

## 🔒 Highlights in v1.1.2
- **Unified GUI + CLI Anti-Stall Floor Clamp**: `FanControllerService.ApplyManualFan()` now enforces the calibrated minimum spin floor identically across both GUI sliders and CLI `fan`/`fan1`/`fan2` (with an explicit override flag for `--force`), snapping GUI sliders cleanly to the floor when clamped (Test #86).
- **Physical Memory Allowlist & EC-Only Write Barrier**: `NativeInpOut.IsPhysicalMemoryRangeAllowed()` restricts `MapPhysToLinSafe()`, `ReadPhysicalByteSafe()`, and `WritePhysicalByteSafe()` exclusively to verified EC MMIO (≤ 4 KB), read-only PCIe ECAM Bus `0:0:0` (4 KB), and runtime-registered read-only AMD SMU PM-table buffers (≤ 16 KB), blocking arbitrary physical RAM access and all non-EC physical writes fail-closed (Test #87).
- **Hardened DLL Load Flags & SHA-Pinned CI**: Replaced `LOAD_WITH_ALTERED_SEARCH_PATH` with `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`, and pinned every GitHub Action in `.github/workflows/ci.yml` by commit SHA.
- **Modular Overlay Refactor & Doc Cleanup**: Split `CapFrameXOverlayForm` into 5 cohesive partial files (`.cs`, `.ContextMenu.cs`, `.Formatting.cs`, `.Graphs.cs`, `.Paint.cs`), corrected the final `2°C` → `4°C` hysteresis mention in `README.md`, and verified **87/87 passing** across `.NET 4.8` and `.NET 10.0-windows`.

---

# Release Notes: MiniFanControl v1.1.1 (Security Hardening, `.NET 10` Fix & Modular Refactor)

**MiniFanControl v1.1.1** is a security, hardware-safety, and reliability release that implements runtime SHA256 driver pinning, binds custom profiles to source machine DMI fingerprints, moves the port whitelist directly into `NativeInpOut`, hardens CLI/failsafe edge cases, fixes `.NET 10` `ContextMenuStrip` runtime compatibility, fixes GitHub Release `Setup.exe` packaging, and expands the automated safety suite to **85/85 passing on both `.NET 4.8` and `.NET 10.0-windows`**.

## 🔒 Highlights in v1.1.1
- **Runtime SHA256 Driver Pinning & Single-Choke-Point Port Whitelist**: `NativeInpOut` now verifies the SHA256 digest of `inpoutx64.dll` (and optional `WinRing0x64.dll`) while holding a `FileShare.Read` lock across `LoadLibraryEx`, and enforces `IsPortWhitelisted()` directly inside `DlPortReadPortUchar`/`DlPortWritePortUchar` (`Inp32`/`Out32`).
- **DMI-Bound Custom Profiles & Fail-Closed Parsing**: `custom-profile.json` is bound to source machine DMI (`boundManufacturer`/`boundProductName`/`boundBaseBoard`), defaults fail-closed to read-only/unverified on stray or truncated files, never bypasses hardware safety blocklists, and now parses single-line JSON via the unified `Core/SimpleJson.cs` tokenizer.
- **Thermal Failsafe, CLI Anti-Stall & Locale Fixes**: The ≥90°C emergency thermal tripwire now executes *before* the read-only latch return with periodic `SetAuto()` retries while latched; CLI `fan`/`fan1`/`fan2` clamps sub-floor manual values to the calibrated spin floor unless `--force` is passed; CLI `status` JSON now formats decimals with `InvariantCulture` (fixing `de-DE`/`fr-FR`/`es-ES` locales); `EvaluateCurve()` defensively enforces sorted monotonic curves; and tach spike filtering bypasses at `≥95%` duty.
- **`.NET 10` Compatibility, Installer Packaging & Modular Architecture**: Migrated legacy `ContextMenu` calls to `ContextMenuStrip` so `.NET 10.0-windows` runs cleanly without `PlatformNotSupportedException`, fixed the `dist/*-Setup-*.exe` CI release glob, split `MainForm` (6 partials) and `HardwareBackend` (4 partials), untracked compiled `.exe` binaries from git, and verified **85/85 passing tests** across both `.NET 4.8` and `.NET 10.0-windows`.

---

# Release Notes: MiniFanControl v1.1.0 (BIOS Power Bar, Digital Foundry OSD & Universal Calibration)

**MiniFanControl v1.1.0** is a major feature release adding an independent **`BIOS:` Power Mode** switcher in the header, an expanded 2×4 telemetry dashboard with a scrollbar-free OSD Studio, Digital Foundry **`DF Zoom`** / **`DF Corner`** / **`Graphs Only`** overlay modes, universal `≥ 88°C` calibration thermal safety with full `14%` low-PWM floor support, and **77/77 passing** automated safety tests.

## ✨ Highlights in v1.1.0
- **Independent `BIOS:` Power Mode Bar & Dual-Fan Mode-Aware `✨ Auto-Tune`**: Switch live factory BIOS TDP limits (`Silent ~35W` / `Balanced ~48W` / `Perf ~60W+`) independently of Custom Fan Curves, with `✨ Auto-Tune` automatically updating both APU & Sys fans and adapting its knee when switching BIOS Power modes.
- **Expanded 2×4 Telemetry Dashboard & Scrollbar-Free OSD Studio**: Larger `257×108 px` sensor cards with `▼Low ~Avg ▲High` ranges, a `72×72` top-left logo, live `Source Temp • Duty % • RPM` callout badges on the Fan Curve graph, and a taller OSD Studio that displays all 7 metric sections with zero inner scrollbars.
- **Digital Foundry Views & In-Game OSD Overhaul**: Added `🔍 DF Zoom`, `◰ DF Corner` / **`DF Corner Inset`** (Vertical & Horizontal), **`Graphs Only`** mode, `1%`/`0.1%` lows + shaded floor band on the overlay FPS chart, right-click **`Sampling Rate`** submenu, sample-rate-synced timestamps, and clean **In-Game OSD** branding.
- **Universal Calibration & Tachometer Stabilization**: Added a live `≥ 88°C` thermal safety abort during calibration, lowered the sweep start to `12%` (`14%` safe floor honored end-to-end in `EvaluateCurve()`), added 1-second rolling steady-state RPM settling, and added high-load zero-tach debounce + `+2000 RPM` spike rejection (**77/77 passing**).

---

# Release Notes: MiniFanControl v1.0.6 (Fail-Closed Profile Matching & JSON Probe Export)

**MiniFanControl v1.0.6** hardens hardware auto-detection against false-positive profile matches on unverified Ryzen Mini PCs, adds structured JSON output to `minifan-helper.exe dump`, improves Super I/O / EC chip identification in diagnostic reports, and expands the automated safety test suite to **76/76 passing**.

## ✨ Highlights in v1.0.6
- **Vendor-Scoped Fail-Closed Profile Matching (`Core/DeviceProfile.cs`)**: `SystemHardwareInfo.MatchProfile` now incorporates `SystemManufacturer`, scopes CPU-number fallbacks (`8845`/`8945`/`H 255`) to verified `GMKtec` hardware, narrows `ryzen-hx370-ec` to `EVO-X1` and `TS-12C`, and ensures unrecognized or third-party Ryzen systems remain safely locked in `unknown-safe` read-only mode.
- **`minifan-helper.exe dump --json` & Chip Naming (`Program.cs`, `Core/DeviceProfile.cs`)**: Exports structured DMI (`manufacturer`, `productName`, `baseBoard`, `biosVersion`, `cpuName`), `matchedProfile`, and `0x2E`/`0x4E` Super I/O chip metadata (`ITE IT5570E`, `ITE IT8613E`, `Nuvoton NCT67xx`) for easy hardware reporting and profile submissions (Test #76).
- **Documentation & Profile Naming Alignment**: Corrected `ryzen-hx370-ec` naming across `DeviceProfile.cs`, `README.md`, `GUIDE.md`, and `HARDWARE_VALIDATION.md` to `GMKtec EVO-X1 / TOPC TS-12C`, and updated `unsupported_hardware.md` and `ci.yml` for the 76-point test suite.

---

# Release Notes: MiniFanControl v1.0.5 (CLI Profile Export & CI Wide-Layout Fix)

**MiniFanControl v1.0.5** adds a machine-readable hardware profile export command to `minifan-helper.exe`, includes all `v1.0.4` Wide 2-Column Mode and Adrenalin/NPU enhancements, and resolves the headless CI runner screen-clamping issue in Test #59 (verified by **75/75** automated self-tests).

## ✨ Highlights in v1.0.5
- **`minifan-helper.exe export-profiles [--json]`**: Exports verified hardware profiles, EC/Super I/O register mappings, and probed ITE/Nuvoton Super I/O chip ID prefixes as a formatted table or JSON (`DeviceProfile.ExportKnownProfilesJson` / `ExportKnownProfilesTable`), covered by Test #75.
- **CI Wide-Layout Test Fix**: Decoupled Wide 2-Column Mode (`>= 1450px`) verification in `SelfTestSuite` Test #59 from the CI VM's physical display bounds via `MainForm.TestApplyWideLayoutSize(int, int)`.

---

# Release Notes: MiniFanControl v1.0.4 (Wide 2-Column Mode, Curve Hysteresis & Accurate Radeon/NPU Telemetry)

**MiniFanControl v1.0.4** introduces a responsive 2-column Wide Mode for maximized/widescreen tuning, eliminates APU fan hunting on Auto-Tuned curves, fixes AMD Adrenalin 26.x RIS/FSR/Anti-Lag detection, auto-hides NPU telemetry on non-NPU APUs (Ryzen 7 H 255), and aligns OSD/Live Perf readouts (verified by 74/74 automated self-tests).

## ✨ Highlights in v1.0.4
- **Wide 2-Column Mode (`>= 1450px`)**: Maximizing or widening the window docks **Live Trend** / **Live Perf** side-by-side on the right while keeping **Fan Curves** / **OSD Studio** on the left.
- **Anti-Hunting Auto-Tune & 4°C Asymmetric Hysteresis**: Auto-Tune now anchors the APU fan at `ApuMinSpinPct` instead of `0%`, updates automatically after recalibration, and uses `4°C` cooldown hysteresis (`0°C` up).
- **Accurate Adrenalin 26.x & Running-Game Detection**: Scopes `GExDb.blb` / `gmdb.blb` to Global at idle and the active game `.exe` during gameplay, removes `UMD\FsrOverride` / `FSRUpgradeAvailable` false positives, maps `FeatureID 27` to RIS, and removes the obsolete KMD registry toggle submenu.
- **No-NPU & Driver-Only Clarity**: Detects AMD XDNA NPU PCI hardware (`HasNpuHardware`) to hide NPU rows/menus on non-NPU APUs, and adds in-app + `README.md` diagnostics for Driver-Only and missing-driver setups.
- **OSD & Live Perf Polish**: Fixed-width CPU/iGPU/FCLK OSD formatting without `@` jitter, and rolling live-window `Avg` FPS in `Live Perf`.

---

# Release Notes: MiniFanControl v1.0.3 (Architecture Refactor, Reliability & Accessibility Release)

**MiniFanControl v1.0.3** decouples core state, settings persistence, OSD configuration, and hardware fan-control loops out of `MainForm.cs` into dedicated `Core/` services while fixing parser/installer-path bugs and adding full keyboard navigation, screen-reader accessibility, High Contrast support, and DPI/scrolling improvements.

## 🏗️ Architecture & Reliability Highlights (v1.0.3)
- **Modular `Core/` Services**:
  - `Core/OsdOverlayConfig.cs`: Encapsulates all CapFrameX OSD metrics, graph settings, defaults, and `CultureInfo.InvariantCulture` serialization.
  - `Core/AppSettings.cs`: Encapsulates `minifancontrol.cfg` loading/saving and curve point serialization with watchdog exception logging.
  - `Core/FanControllerService.cs`: Encapsulates thread-safe hardware polling, curve evaluation, manual/auto transitions, thermal tripwire, write-error failsafe latching, and sleep/resume lifecycle with deterministic disposal.
- **Parser & Path Reliability**: Fixed `DeviceProfile.FromJson` colon splitting in string values, upgraded `Loc` JSON parsing to a full tokenizing parser supporting single-line JSON, backslash escapes, and `\uXXXX` Unicode escapes, and added `%LocalAppData%\MiniFanControl` fallback for `sio-diag.txt` under read-only installs (`Program Files`).
- **Input, Lifetime & Display Fixes**: Preserved the single-instance wake listener if watchdog spawning fails, disposed prior `ContextMenu` instances in `RebuildDriversMenu()`, prevented `Tab` key releases on sliders from triggering manual fan writes, fixed Fahrenheit formatting in the window title and NVMe telemetry tags, and aligned version `1.0.3` across all manifests and packaging scripts.
- **Accessibility & High-DPI Support**: Added keyboard point selection/editing on `FanCurveCanvas`, chart keyboard shortcuts (`R` reset peaks, `V` split view), keyboard activation (`Enter`/`Space`) and live-value UIA `AccessibleName` updates across all 8 telemetry metric boxes, `AcceptButton`/`CancelButton` on `SetupAssistantDialog`, `AutoScaleMode.Dpi` + resizable/`AutoScroll` support, and `SystemInformation.HighContrast` theme adaptation (verified by 74/74 automated self-tests).

---

# Release Notes: MiniFanControl v1.0.1 (Calibration & Tachometer Stability Patch)

**MiniFanControl v1.0.1** is a targeted maintenance release fixing multi-pass fan calibration coast-down timing and 16-bit EC tachometer rollover spikes observed on real GMKtec K12 hardware.

## 🔧 What's Fixed in v1.0.1
- **Full 0 RPM Coast-Down Window (`35.0s`)**: Extended the `1% PWM` baseline spin-down timeout from `10.0s` to `35.0s` with `2s` keep-alive commands and settled-floor detection (`>= 15s`, `<= 15 RPM` delta over 8 samples), ensuring Passes 1, 2, and 3 start from a true settled baseline rather than mid-deceleration from `100% PWM`.
- **Adaptive Step Stabilization & Trimmed-Mean Filtering**: Calibration steps now extend up to `+2.5s` when rotors are still accelerating/decelerating (`> 30 RPM` delta), discard single-sample outliers via `ComputeRobustStepRpm`, and guard against false spin-up detection while a fan is still coasting down.
- **Atomic Tachometer Latching & Rollover Spike Rejection**: Added MSB/LSB double-read latching on EC tachometer registers in `HardwareBackend.GetStatus()` and rejected `>= 7500 RPM` rollover spikes in `DecodeTachometerRpm()` (verified by 69/69 passing automated tests).

---

# Release Notes: MiniFanControl v1.0.0 (Production Release & Architectural Milestone)

**MiniFanControl v1.0.0** is the production milestone release delivering native EC & Super I/O fan curve control, out-of-process watchdog crash supervision, Windows 11 Modern Standby (S0ix) silent sleep protection, CapFrameX-style in-game HUD overlay with Digital Foundry twin graphs, deep silicon telemetry (Zen 5 SMN junction Tctl/Tdie, ADL2 PMLOG, SMU/FCLK, dual stacked SODIMM DDR5 thermals), fail-closed multi-tool coexistence (`Global\Access_EC`), comprehensive UI layout polish, and 69 passing automated hardware safety & regression tests.

---

## 🚀 Key Highlights & Architectural Enhancements (v1.0.0)

### 🎨 UI Polish, Geometry Anti-Clipping & Documentation Visuals
- **Calibration Status Badge Isolation**: Resolved test suite mutation of `FanCurveProfile.LastCalibratedDate` in automated tests by wrapping mock evaluations in `try ... finally`, restoring the user's authentic calibration date and ensuring the badge renders in green: `✓ Calibrated 2026-11-16 (Min 22%/22%)` (Test #11).
- **Options Card Geometry Integration**: Moved `Sampling:` label and dropdown inside `optCard.Controls` at `Location = Point(660, 11)` and `Point(728, 7)`, cleanly occupying Row 1, Column 3 inside the Options panel and eliminating border straddling.
- **OSD Studio TrackBar Clearance & Button Anti-Clipping**: Added an 8px vertical air gap below the Win32 Opacity TrackBar (`Y=188` / `Y=184`), completely eliminating text clipping on the `Visible Telemetry Metrics` section header. Expanded Quick Snap button widths across the canvas (`Center-Bottom` to 104px, `Bottom-Right` to 98px) to eliminate label truncation under Segoe UI Bold (Test #44).
- **Header Button Click-Surface Protection**: Bounded `_hwSubLabel` and `_hwBadgeLabel` to `Width = 580` (`X=68..648`) with `AutoEllipsis = true` and called `BringToFront()` on `Tools & Drivers`, eliminating a 20px overlap that previously intercepted clicks intended for the menu button.
- **Documentation Image Population**: Enriched `README.md` and `GUIDE.md` with 5 real hardware screenshots from the TOPC TS-12C Mini PC (`ui-live-telemetry.png`, `ui-fan-curve-editor.png`, `ui-digital-foundry-perf.png`, `ui-osd-studio.png`, `ingame-osd-overlay.jpg`).

### 💤 Sleep & Modern Standby (S0ix) Lifecycle Protection
- **Windows 11 Modern Standby (S0ix) Support via `RegisterPowerSettingNotification`**: Desktop Win32 applications do not receive legacy `PBT_APMSUSPEND` when entering modern low-power idle. MiniFanControl registers for `PBT_POWERSETTINGCHANGE` notifications (`GUID_CONSOLE_DISPLAY_STATE`, `GUID_MONITOR_POWER_ON`, and `GUID_LOW_POWER_EPOCH`). When the display turns off or low-power epoch begins, the app immediately pauses the hardware worker, stops the watchdog supervisor gracefully, and coasts fans down silently, eliminating false watchdog timeouts and cyclic fan blasting while the machine is sleeping (Test #69).
- **Default Silent Sleep Standby (Zero Spin-Up Burst)**: `SilentSleepStandby` defaults to `true`. When sleep is entered, fans gently coast to 0% duty with zero RPM kickstart burst. When the display turns back on or user wakes the machine, hardware registers and Super I/O are re-enumerated, the watchdog supervisor is re-launched, and custom curves resume seamlessly.
- **Eliminated Super I/O Runaway Fan Blasts in Fallback**: Corrected `ApplySafeSuperIoFallback()` to configure registers `0x15..0x17` to `0x00` (Extended PWM Mode) with safe duty `166` (quiet ~35% / 1100 RPM baseline), matching the factory BIOS. Setting Bit 7 = 1 (`0xFF` SmartGuardian Auto) previously caused the IT8613 hardware chip to command 100% full throttle whenever thermal diode clocks stopped during sleep.
- **Floating Sensor Diode Filter (`0x7F` = 127°C)**: In `CheckEmergencyThermalTripwire`, temperatures are bounded to `< 115°C` and explicitly reject `127°C` (`0x7F`), preventing floating uninitialized bus reads during sleep transitions from tripping the emergency 100% thermal override.

### 🛡️ Out-of-Process Watchdog IPC AutoReset Crash Loop Elimination
- **AutoReset Waiter Rights (`Modify | Synchronize`)**: In Windows NT kernel security, calling `WaitOne` on an `AutoReset` event performs an atomic state reset upon wait satisfaction, which requires `EVENT_MODIFY_STATE` (`Modify`). Opening the handle as a pure waiter with only `Synchronize` triggered `UnauthorizedAccessException` on the child supervisor's first poll. Resolved by granting `EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize` to Waiters on AutoReset events, eliminating crash loops (Test #34, Test #68).
- **Auto-Rearming Watchdog Failsafe**: In `WatchdogSupervisor.ExecuteLoop()`, `triggeredFailsafe = true` is now cleared and logged when heartbeat pulses resume from the parent process, re-arming the supervisor if the UI recovers from a transient hang.
- **IPC Access Control List (ACL) Hardening**: Removed `S-1-15-2-1` (`ALL APPLICATION PACKAGES`) from IPC event DACLs, ensuring AppContainer sandboxes cannot dismiss watchdogs or pulse fake heartbeats. Event security is strictly bounded to the creator, Administrators, SYSTEM, and Authenticated Users (Test #31).
- **Cross-Process Lifecycle Handshake**: Integrated Test #68, which spawns an actual independent `minifan-helper.exe watchdog` child process, verifies bi-directional ready handshake, tests AutoReset heartbeat pulse consumption without security exceptions, and confirms clean graceful shutdown with exit code 0.

### 🔒 Hardware Layer Safety & Mutex Fail-Closed Rigor
- **Fail-Closed SMBus Mutex**: `EcBusMutex.WaitSmbus` now fails closed (`return false`) when `_smBusMutex` is missing or uninitialized, ensuring complete alignment with the fail-closed doctrine across EC, ISA, and PCI mutexes (Test #22).
- **IT87 SmartGuardian Automatic Thermal Mode**: In `HardwareBackend.ApplySafeSuperIoFallback()`, registers `0x15..0x17` explicitly have Bit 7 set (`0x80`), engaging motherboard BIOS SmartGuardian thermal automatic control so fans ramp dynamically with temperature under load after an emergency restore, backed by the 166 baseline floor.
- **Persistent Hardware Write Escalation**: In `MainForm.cs`, consecutive write verification failures now escalate after 10 retries: `SetAuto()` is forced and manual control is latched read-only (`_readOnlyFailsafeLatched = true`) to prevent hardware damage from runaway states (Test #33).
- **Passive Super I/O Probe Gating**: `ProbeSuperIo` gates candidate configuration writes behind `allowConfigWrites = false` during passive hardware scanning.
- **Sensor Telemetry Transparency**: Added `IsVrmEstimated`, `IsAmbientEstimated`, `IsDdrEstimated`, `IsApuPowerEstimated` to `FanStatus`. Modeled temperatures and power are clearly flagged `[Est]` across UI labels, tooltips, CapFrameX OSD, and CSV logs, and are prevented from driving fan curves. Fixed `GetDdrStickCount` to probe SMBus SPD first rather than assuming ≥30 GB is dual-stick.

---

# Release Notes: MiniFanControl v1.1.0 (Advanced Hardware Telemetry & Thermal Coupling Release)

**MiniFanControl v1.1.0** expands hardware monitoring and proactive thermal protection for AMD Ryzen Mini PCs, adding DDR Dual Data Rate speed calculation, dual stacked SODIMM temperature tracking with JEDEC SPD5118 hub reading, dynamic RAM and NVMe power usage monitoring, multi-NVMe storage throughput, AMD FSR detection, Super I/O IT8613 Channel 6 Chassis Ambient thermistor mapping, inverted PWM duty curve fixes, and automated test suite expansion to 66 passing tests.

---

## 🚀 Key Highlights & Architectural Enhancements (v1.1.0)

### 🧠 DDR Dual Data Rate & Dual Stacked SODIMM Thermal Architecture
- **Dual Data Rate ($2\times \text{MCLK}$)**: Computes and displays true DDR effective transfer speed (e.g. `5600 MT/s` / `5600MHz` when base MCLK is 2800 MHz) across the OSD overlay, Main Form, Live Trend, and RAM tooltips.
- **Physical SODIMM Stacking Telemetry**: Mini PCs physically stack SODIMM slots vertically. Slot 0 (Bottom) sits near the cooler/chassis (`~41.5°C`), while Slot 1 (Top) is sandwiched on top with restricted airflow, absorbing radiant heat from below (`~47.8°C`, reaching up to `75.8°C` under sustained load). MiniFanControl monitors both physical sticks individually (`DdrTemp1` and `DdrTemp2`), reads JEDEC SPD5118 on-die thermal sensors via AMD SMBus (`0x50` and `0x51`), tracks individual session peaks, and formats dual temps (`41°/48°C`).
- **Single-Stick System Accommodation**: Automatically detects physical module count (`DdrStickCount = 1` vs `2`), cleanly falling back to single-temperature format (`42°C`) and omitting ghost modules when only one stick is installed.
- **Proactive Fan 2 Coupling to Hotter Module**: Locked composite `DdrTemp` to $\max(\text{DIMM 0}, \text{DIMM 1})$ so the secondary blower ramps up to protect the sandwiched top stick.
- **SMBus Mutex Synchronization**: All SPD5118 queries synchronize through `Global\Access_SMBUS.HTP.Method` alongside HWiNFO64 and LibreHardwareMonitor to ensure zero bus collisions.

### 🌡️ Super I/O IT8613 Channel 6 Chassis Ambient Thermistor
- **Channel 6 (Register 0x2E) Mapping**: Mapped Super I/O Temperature 6 directly to Chassis Ambient (`41°C`–`49°C`), matching HWiNFO64 Temperature 6.
- **Enabled Out-of-the-Box**: `ShowAmbient` and `_osdShowAmbient` default to `true`. Displayed continuously across the Vertical OSD (`Chassis 41°C ▲49°`), Horizontal OSD (`CHAS 41°C`), Main Window secondary row (`• 🌡️ Amb: 41°C`), and Live Trend (`● Chas 41°C`).

### 🌀 Super I/O IT8613 Inverted Hardware PWM Duty Fix & 3-Pass Calibration
- **Hardware-Inverted Polarity Correction**: Corrected inverted duty calculation on IT8613 (`0xC7` in register `0x14` on TS-12C / KTHIN PRO): `duty = (pct <= 0) ? 255 : 255 - ((pct * 255) / 100)`. Resolves fan speed runaways and allows Sys Fan to throttle down to custom low-noise curves (~20-25% PWM).
- **3-Pass Fan Calibration Engine**: Enhanced 3-pass calibration with independent spin-up delay compensation for both blowers.

### 💾 Multi-NVMe Storage & Real-Time Read/Write Throughput
- **Dual NVMe Support**: Monitors primary and secondary M.2 NVMe SSDs (`PhysicalDrive0` and `PhysicalDrive1`).
- **Real-Time I/O Throughput**: Displays live Read and Write rates in MB/s (`IOCTL_DISK_PERFORMANCE`), active queue depth, remaining health %, and composite temperatures.
- **Dynamic NVMe Power Envelope (`NvmePowerWatts`)**: Integrates real-time NVMe storage power estimation (`~0.9W` idle up to `~5.5W` active per drive) dynamically coupled to live read/write activity across tooltips, status sub-header, CapFrameX OSD, and Live Trend legend pills.

### ⚡ RAM & NVMe Power Usage Telemetry Restored & Unified
- **RAM Power Usage**: Restored DDR5 memory power wattage (`~4.8W`) alongside dual SODIMM temperatures (`37°/43°C (Δ6°)`) and clocks (`5600MHz`) across the CapFrameX OSD (Vertical Stack and Horizontal Bar), Live Trend legend pills, and hover HUD.
- **NVMe Power Usage**: Added `NvmePowerWatts` dynamic power envelope display (`~0.9W`–`~1.2W` idle, ramping up during active disk writes) alongside composite temperature and I/O rates.
- **Unified Tooltip & Status Bar Envelopes**: Both RAM and NVMe tooltips and Main Form status labels now provide full visibility into memory controller and PCIe Gen4 power states (Test #66).

### 🎮 AMD FSR Upscaling Detection & Silicon Tctl Decoding
- **AMD FSR Upscaling Detection**: Enumerates FSR state from Radeon Software runtime / registry, surfaced as a dedicated "FSR" pill in active Radeon features.
- **AMD Zen 5 Silicon Junction Tctl/Tdie SMN Decoding**: Isolated AMD SMN register `0x00059800` (`THM_TCON_CUR_TMP`) on Zen 5 Strix Point (Ryzen AI 9 HX 370), matching HWiNFO64 Tctl/Tdie to within $\pm 0.1^\circ\text{C}$.
- **Right-Aligned Exact MHz Clocks & VRM Peak**: Standardized clock speeds to exact MHz (`1600MHz`, `@ 1848MHz`) with clean right-aligned baseline stability, and added session peak VRM temperature tracking (`MaxVrmTemp`).

### ⚡ CapFrameX-Inspired Bus Throttling & Zero-Impact Overlay Optimizations
- **Phased `RefreshThrottle` Bus Optimization**: Inspired by CapFrameX commit `3d8996c7`, slow-moving hardware sensors (NVMe SMART health/temperatures and DDR5 SPD5118 hub reads) are decoupled from high-frequency CPU/iGPU polling. Operates on a deterministic, jitter-immune timer grid with staggered phase offsets (+250ms) between dual SODIMM modules, completely eliminating bus collisions and mutex contention on `Global\Access_SMBUS.HTP.Method` (Test #64).
- **Hook-Free Transparent Overlay Dirty-Check**: Inspired by CapFrameX's hook-free partial repaint measurements (`Overlay_Game_Impact_DevPlan.md`), MiniFanControl computes an allocation-free 64-bit integer hash of visible telemetry, skipping redundant GDI+ bitmap rasterization and Win32 `UpdateLayeredWindow` blits when telemetry is static. Eliminates GPU compositor frame slips in high-FPS games with active frame generation.
- **AMD Ryzen Processor Tier Badges**: Seamlessly detects and renders official AMD Ryzen tier badges (`Ryzen AI 9`, `Ryzen 9`, `Ryzen 7`) with signature orange styling (`#EE6B24`) in the OSD HUD (Test #65).
- **Automated Self-Test Expansion**: 66 passing automated tests (100% pass rate).

---

# Release Notes: MiniFanControl v1.0.0 (Initial Production Release)

**MiniFanControl v1.0.0** is the initial public production milestone release delivering enterprise-grade hardware safety, out-of-process crash supervision, kernel driver security hardening, tachometer stabilization, clean architectural modularization, bulletproof hardware synchronization, expanded 40-point automated hardware safety & regression verification, and transparent security documentation.







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



- **Synchronized Versioning**: Authoritative version `1.0.0` synchronized across `VERSION`, `MiniFanControl.csproj`, `Ui/MainForm.cs`, `installer.iss`, `package-release.bat`, and documentation.



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

- **38-Point Automated Hardware Safety & Regression Test Suite**:
  - Added Test 37 (`Native NVMe Storage & System RAM Sensor Telemetry`), verifying native memory queries, fail-safe NVMe querying, and tooltip formatting helpers.
  - Added Test 38 (`Session Telemetry NVMe Tracking & Sparkline Generation`), verifying session min/avg/peak calculation, 120-sample rolling ring buffer history, and custom sparkline rendering for NVMe thermals.

---

### 🚀 Phase 12.1: Unified Thermal Telemetry Dashboard & OSD Sequence Realignment

- **6-Block Primary Telemetry Grid with Native NVMe Block**:
  - Realigned the top dashboard into 6 symmetrically balanced 101px metric blocks: `CPU` | `iGPU` | `NVMe` | `PWR` | `APU FAN` | `SYS FAN`.
  - Upgraded NVMe to a first-class metric block with full parity:
    - **Live & Peak Readouts**: Displays live temperature (`35°C`) and peak tracking (`NVMe • MAX 42°`).
    - **Real-Time Sparkline History**: Bottom-anchored 120-sample sparkline rendered in electric cyan (`#22D3EE`).
    - **Interactive Spotlighting**: Left-click spotlights NVMe on the Combined Live Telemetry Graph with dedicated axis scaling and timeline trace.
    - **Instant Peak Reset**: Right-click resets session peaks across all metrics.
    - **Rich Tooltip & Hover Graph**: Displays drive health %, wear %, spare blocks %, SMART status, session Min/Avg/Peak stats, and a high-resolution 90s trend mini-chart.
- **Secondary Unified Memory & Storage Status Bar**:
  - Row 2 spans the full width, displaying live physical memory allocation and installed NVMe drive health (e.g. `🧠 RAM: 20.8 / 47.6 GB (43% Load) • 💾 SSD: Sabrent Rocket 4.0 Plus (92% Health)`).
- **CapFrameX OSD Sequence Reordered**:
  - Realigned both Horizontal and Vertical in-game/desktop overlay layouts so that temperature metrics are grouped together: `CPU` -> `iGPU` -> `NVMe` -> `Power` -> `GPU Load` -> `APU Fan` -> `Sys Fan` -> `RAM` -> `Mode`.
  - Added NVMe peak tracking (`▲{peak}°`) and optional sparkline trace directly in the OSD.
- **RAM Power Telemetry Analysis & JEDEC Power Modeling**:
  - In consumer Mini PCs and laptops, DDR5 SO-DIMM power is supplied via motherboard 5V rails to on-DIMM PMICs without public Win32 shunt sensors, and AMD client APUs do not expose a raw DRAM RAPL domain.
  - Integrated JEDEC DDR5 dynamic load modeling (~1.3W idle per module, ~3.5W–5.0W active) alongside live AMD ADL2 `PMLOG_SOC_POWER` memory controller & Infinity Fabric power tracking in the RAM tooltip.

---

### ⚡ Phase 12.2: 7-Metric Dashboard Grid, Dedicated RAM Power Box, Symmetrical Wide UI & Realigned OSD

- **7-Metric Symmetrical Dashboard Grid**:
  - Re-laid out top dashboard with 7 distinct 110px metric boxes: `CPU` -> `iGPU` -> `NVMe` -> `PWR` (APU) -> `RAM` (RAM Power) -> `APU FAN` -> `SYS FAN`.
  - Widened the application window (`ClientSize.Width = 870px`) providing generous breathing room for long RPM and status text strings (e.g. `2518 RPM •` and `APU • MAX 2626`), eliminating text truncation/clipping.
  - Dedicated **RAM Power Metric Box** (`_ramPowerBox`):
    - Real-time estimated DDR5/LPDDR5 memory wattage readout (`~{power}W`, e.g. `~3.8W`).
    - Peak session tracking (`RAM • MAX {peak}W`).
    - Dedicated 120-sample rolling sparkline history rendered in periwinkle indigo (`#818CF8`).
    - Interactive graph spotlighting (Metric 7), highlighting RAM Power on the large Combined Live Telemetry Graph with dedicated auto-scaling.
    - Right-click resets session peaks across all 7 telemetry metrics.
    - Rich hover tooltip with live Min/Avg/Peak session tracking, 90-second trend sparkline, SO-DIMM count, and AMD VDD_SOC memory controller fabric power.
- **CapFrameX OSD Reordering**:
  - Re-aligned OSD metric order to follow the new dashboard layout: `CPU` -> `iGPU` -> `NVMe` -> `Power` -> `RAM` -> `GPU Load` -> `APU Fan` -> `Sys Fan` -> `Mode`.
  - Added live RAM estimated wattage readout (`~{power}W`), peak tracking (`▲{peak}W`), and dedicated sparkline directly in both Horizontal and Vertical OSD layouts.
- **Telemetry & CSV Logging**:
  - Expanded `Core/TelemetrySessionStats.cs` CSV export schema to include `RAM_Power_W` at the end while preserving full backward compatibility with existing parsers.
  - Test 38 updated to verify both NVMe and RAM power tracking, session min/max/avg statistics, and sparkline generation.

---

### 🎨 Phase 12.3: OSD Studio Makeover, Full Granular Metric Toggles (NVMe & RAM), and Layout De-Squashing

- **Granular OSD Metric Visibility Controls (NVMe & RAM Added)**:
  - Added dedicated toggle checkboxes for **NVMe SSD Temp** (`OsdShowNvme`) and **RAM Load & Power** (`OsdShowRam`) directly in the OSD Overlay Configuration Studio.
  - Added full config file persistence in `minifancontrol.cfg` for `OsdShowNvme` and `OsdShowRam`.
  - Added metric visibility toggle items inside the OSD overlay right-click context menu for instant in-game configuration.
  - Added real-time rendering of NVMe temperature and RAM power curves in the OSD Combined Live Trend Graph.
  - Added `Select All` and `Defaults` quick action buttons for instant telemetry preset management.
  - Added single-fan awareness: `Sys Fan RPM` is gracefully grayed out as `Sys Fan RPM (N/A)` on single-fan mini-PCs.
- **Studio Layout Makeover & Anti-Squash Redesign**:
  - Resolved root cause of `"Visible Tel"` label truncation: eliminated TrackBar control overlap with the metrics section.
  - Restructured panel into a clean 2-column side-by-side design: Graph Mode + Graph Height on Row 2, Scale Tier + Layout Direction on Row 3, Opacity Slider + Quick Snap on Row 4.
  - Created a spacious 4-column metric checkbox grid (196–202px per column) with `UseMnemonic = false` to correctly display `RAM Load & Power`.
  - Replaced unsupported unicode emoji characters that produced missing glyph boxes (`[]`) with clean, universally supported text and standard geometric bullets (`●`).
- **Automated Verification**:
  - Added automated test 39 in `Tests/SelfTestSuite.cs` validating granular metric toggling (`ShowNvme` & `ShowRam`) and visibility counts.
  - All 39 tests passing with 0 errors.

---

### 🎮 Phase 12.4: CapFrameX High-Resolution Frametime & Lows Precision, Dedicated Graphs & 40-Point Suite

- **Dedicated FPS & Frametime Live Graph**:
  - Added dedicated multi-series chart rendering underneath the Combined Live Trend graph.
  - Plots 4 synchronized curves:
    1. Solid bright cyan curve for live in-game FPS.
    2. Solid amber gold curve for high-resolution frametime in milliseconds (`ftLo` to `ftHi`).
    3. Dashed cyan guide line for active session Average FPS.
    4. Dashed rose/coral percentile line with translucent fill for 0.1% Low framerates.
  - Header displays live dynamic range: `FPS & FRAMETIME (min–max FPS • frametime ms)`.
- **0.1% Low & Average FPS Layout Alignment**:
  - Removed misplaced trailing `Avg:` metric from the `0.1% Low` line, rendering `0.1% Low` cleanly as `42 FPS` with zero trailing clutter.
  - Repositioned session Average FPS to the `FPS` row alongside live frametime: `FPS 59 (16.9ms) Avg: 58`.
  - Prioritized active session telemetry (`Stats.AvgFps`) over stale RTSS ring buffers, eliminating impossible average readings that exceeded peak FPS.
  - Enforced strict mathematical boundary clamping: MinFps <= AvgFps <= MaxFps.
- **DWM Transparency & Windows Forms Mnemonic Fixes**:
  - Added explicit Win32 `SetLayeredWindowAttributes` P/Invoke call ensuring transparent layered OSD rendering at 100% opacity.
  - Disabled ampersand mnemonic character acceleration via `TextFormatFlags.NoPrefix` on all chart titles, eliminating underscore rendering bugs.
  - Filtered out background Windows UWP shell processes (`SecHealthUI.exe`, `dwm.exe`, `explorer.exe`) from RTSS shared memory polling.
- **Automated 40-Point Regression & Safety Test Suite**:
  - Added Test 39: High-Resolution RTSS Frametime, Average FPS & 0.1% Lows Telemetry (percentile computation from 1,000 synthetic frame samples, session FPS bounds, frametime sparklines, and double-buffered OSD bitmap rendering).
  - Added Test 40: Hardware Validation Matrix Auto-Discovery & Polarity Testing (verifying all 6 device profile configurations, EC register mapping bounds, inverted Super I/O PWM polarity 40/0 and 240/0, and fail-closed read-only protection).
  - All 40 automated tests passing with 0 errors (100%).

---

### 📈 Phase 12.5: Interactive Graph Line Traces, Dynamic Scale Decoupling & Scrubbing HUD

- **Dynamic Thermal & Power Scale Decoupling**:
  - Automatically isolates `LIVE TREND` to purely silicon thermals, storage, power, and fan RPM when the dedicated `FPS & FRAMETIME` chart is active (`bool plotFpsHere = PlotFpsOnTrend && !ShowFpsGraph`).
  - Eliminates thermal scale squashing: prevents high framerate spikes (e.g. 500+ FPS in game menus like *Hades*) from crushing temperatures and power consumption into the bottom 5% of the chart.
- **Line Traces & Anti-Collided Endpoint Value Badges**:
  - Added right-aligned endpoint badges anchored directly to each curve's rightmost sample across `LIVE TREND`, `FPS & FRAMETIME`, and the main dashboard canvas.
  - Multi-pass vertical anti-collision algorithm spreads badges smoothly when values are close while connecting tick lines trace each badge back to its exact curve endpoint.
  - Badges render with translucent dark backgrounds, colored borders, and exact unit readings: `70°`, `57°`, `45°`, `33W`, `1.9k RPM`, `1.7k RPM`, `169 FPS`, `5.9ms`, `Avg: 452`, `Low: 27`.
- **Color-Coded Header Legends**:
  - Added color-coded dot badges on both graph headers:
    - `LIVE TREND`: `● CPU`, `● iGPU`, `● NVMe`, `● PWR`, `● APU`, `● SYS`.
    - `FPS & FRAMETIME`: `● FPS`, `● FT`, `● Avg`, `● 0.1%`.
  - Header text and legends render with zero clipping across all scale tiers.
- **Interactive Time-Scrubbing Crosshair & Tooltip HUD Cards**:
  - Hovering the mouse over unlocked OSD charts or the main dashboard renders a vertical dotted crosshair synchronized with that exact point in time.
  - Displays a floating HUD tooltip box inspecting all active metrics and exact time-offset (`-3s | CPU 70° • iGPU 57° • NVMe 45° • PWR 33W • APU 1928 RPM`).
  - Dynamic positioning prevents the tooltip from obscuring the mouse cursor or graph curves.
- **Digital Foundry (DF) Style Twin Performance Graphs**:
  - Replicated Digital Foundry's visual benchmarking layout:
    - **`FRAME-TIME (MS)` Box**: Crisp white outline with vertical center divider, right-axis labels `0 / 50 / 100`, inverted scale (0ms top ceiling, stutters spike downward), and electric cyan jitter line.
    - **`FRAME-RATE (FPS)` Strip**: Horizontal reference grid lines at `120 / 80 / 40 / 0` (or dynamic tiers `240`, `300`, `480`, `600`, `720+` for high-FPS titles), right-axis labels, neon lime green live framerate curve, and dashed smooth session average line.
  - Configurable with persistence via `OsdDfGraphStyle` in `minifancontrol.cfg`, OSD Studio checkbox, and in-game right-click menu.
- **Framerate Precision & Comb Oscillation Elimination**:
  - Replaced single-frame instantaneous `dwFrameTime` ($10^6 / \mu s$) with aggregate window measurement `1000.0 * dwFrames / (dwTime1 - dwTime0)` as primary framerate source, eliminating Nyquist sampling aliasing where 1-frame micro-stutters plummeted 500 FPS game readings into jagged 80 FPS comb oscillations.
  - Added foreground window PID prioritization via Win32 `GetForegroundWindow` and `GetWindowThreadProcessId`, ensuring the active game process is always tracked without interference from background launchers (`steamwebhelper.exe`, `gameoverlayui.exe`, `amdow.exe`, `discord.exe`).
  - Added 3-point rolling moving average curve smoothing for the DF framerate plot while maintaining instant live leading-edge response.
  - Added dynamic ceiling scaling (`fpsCeiling` automatically scaling to 480, 600, 720+ FPS) preventing high-framerate titles (e.g. Hades at 480+ FPS) from slamming into a static 240 ceiling.
