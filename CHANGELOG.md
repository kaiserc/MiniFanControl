# Changelog

All notable changes to **MiniFanControl** are documented in this file.
The project adheres to [Semantic Versioning](https://semver.org/).
For in-depth architectural deep dives, see [RELEASE_NOTES.md](RELEASE_NOTES.md).

## [Unreleased]

### 🎨 Modern Win11 UI Polish, OSD Studio Workflow & In-Game OSD Refinements
- **Win11 Visual Modernization (`Ui/ModernSlider.cs`, `Ui/ThemePalette.cs`, `Ui/ThemedInfoDialog.cs`, `Ui/Win11VisualStyle.cs`, `Ui/MainForm*.cs`, `Ui/FanCalibrationDialog.cs`, `Ui/KernelDriverDialog.cs`)**:
  - Replaced legacy Win32 `TrackBar` sliders with a custom double-buffered `ModernSlider` displaying an amber Anti-Stall Floor rail zone & threshold notch with live drag value pill.
  - Enabled native Windows dark-mode scrollbars, themed comboboxes/checkboxes, thermal hero temperature color ramps, and unified `FanCalibrationDialog`, `KernelDriverDialog`, and `ThemedInfoDialog` (replacing plain `MessageBox.Show` for About & Hardware Compatibility) onto `ThemePalette`.
- **OSD Studio Presets, Bidirectional Menu Sync & Canvas Productivity (`Ui/MainForm.OsdStudio.cs`, `Core/OsdOverlayConfig.cs`, `Telemetry/CapFrameXOverlayForm.ContextMenu.cs`, `Ui/LiveTelemetryCanvas.cs`, `Ui/FanCurveCanvas.cs`)**:
  - Added 4 one-click OSD presets (`Minimal FPS`, `Compact Gaming`, `Digital Foundry`, `Full Audit`) and 5 category filter pills in OSD Studio, plus full bidirectional sync with the dark-themed overlay right-click context menu (including `ShowLatency`, `MatchTheme`, and preventing empty `Graphs Only` overlays).
  - Added `All` / `Temps` / `Cool` quick-filter pills, Shift+Click Solo mode, and a `TIMELINE PAUSED` banner in `LiveTelemetryCanvas`, plus an inline curve legend and `Ctrl+Z` undo in `FanCurveCanvas`.
- **In-Game OSD Readability Polish (`Telemetry/CapFrameXOverlayForm.Graphs.cs`, `Telemetry/CapFrameXOverlayForm.Paint.cs`, `Telemetry/CapFrameXOverlayForm.Formatting.cs`)**:
  - Kept the `TREND` header strictly single-line with compact colored dots and `+N` overflow indicator, dynamically clamped stacked right-edge endpoint badges cleanly inside graph bounds, deepened dark chart backing over bright game HUDs, increased DF corner frametime inset `0` tick clearance, inset the live `FPS` pill clear of right-axis ticks, aligned `NPU` flush with other labels, added subtle alternating row tinting, padded Radeon/Ryzen pills, and inset the left accent bar inside the outer frame.

### 🛡️ True BIOS Restoration (`Reset Auto`), ETW/Sensor Hardening & Distribution Readiness
- **True BIOS Fan Curve & Mode Restoration on `Reset Auto` (`Hardware/HardwareBackend.SuperIo.cs`, `Hardware/HardwareBackend.cs`, `Core/FanControllerService.cs`, `Ui/MainForm*.cs`)**:
  - Snapshots untouched IT8613E Super I/O fan registers (`0x15..0x17`, `0x63/0x6B/0x73`) and initial EC BIOS mode (`RegModeStatus`) before MiniFanControl's first fan override; `Reset Auto` faithfully restores custom BIOS SmartGuardian fan curves or initial EC modes and enters a true hands-off **`✓ BIOS Auto`** state (without overwriting BIOS control 1 second later with software Balanced, while keeping the ≥90°C emergency tripwire armed) (Test #127).
- **ETW Kernel Event-ID Filter, Hidden OSD Lock Guard & DDR5 SPD Backoff (`Telemetry/EtwLatencyMonitor.cs`, `Telemetry/FpsMonitor.cs`, `Hardware/HardwareBackend.SiliconSensors.cs`, `Core/WorkloadProfileSwitcher.cs`)**:
  - Added Win64 `ENABLE_TRACE_PARAMETERS` / `EVENT_FILTER_DESCRIPTOR` kernel event-ID filtering for lower ETW overhead, filtered WebView2 & AI host processes (e.g., Unsloth Studio) from active game detection, guarded hidden OSD click-through locks against resurrecting stale windows, and added SMBus DDR5 SPD backoff on missing DIMM slots (Tests #124–#125).
- **Privacy-First Update Checker, Public Roadmap & Winget Automation (`Core/UpdateChecker.cs`, `ROADMAP.md`, `scripts/generate-winget-manifest.ps1`, `Tests/README.md`, `README.md`)**:
  - Added on-demand **`↻ Check for Updates (GitHub Releases)...`** in `Tools & Drivers ▾` with strict SemVer comparison, depth-aware JSON parsing, and fail-closed official repo URL sanitization (zero background phone-home) (Test #126).
  - Added [ROADMAP.md](ROADMAP.md), automated `winget-pkgs` 1.6.0 manifest generation (`scripts/generate-winget-manifest.ps1`), `gh attestation verify` instructions in `README.md`, and expanded the verified test catalog to **127/127** tests.

## [1.3.1] - 2026-11-27

### 🛡️ Stabilization, Security Hardening & Verifiable Supply Chain
- **PawnIO `.bin` Module SHA256 Pinning & Legacy `WinRing0` Hard-Gate (`Hardware/PawnIoDriver.cs`, `Hardware/NativeInpOut.cs`, `Core/AppSettings.cs`)**:
  - Enforced exact per-module SHA256 allowlists (`LpcIO.bin`, `LpcACPIEC.bin`, `AMDFamily17.bin`) before loading any bytecode module into `PawnIO.sys`, blocking tampered payloads and cross-module swap attacks fail-closed (Test #122).
  - Hard-gated legacy `WinRing0` fallback (`CVE-2020-14979`) off by default (`AllowLegacyWinRing0Fallback = false`), requiring explicit opt-in via `AllowLegacyWinRing0=1` or `MINIFAN_ALLOW_WINRING0=1`, and removed tracked `inpoutx64.dll` from the repository root so `git clone` stays Defender-clean while keeping SHA256-pinned manual drop-in support.
- **Strict Fail-Closed Bus Synchronization & Physical Read Hygiene (`Hardware/EcBusMutex.cs`, `Hardware/HardwareBackend*.cs`, `Hardware/NativeInpOut.cs`, `Hardware/AmdSmuSiliconSensor.cs`, `Program.cs`)**:
  - Hard-blocked live EC/Super I/O writes (`SetMode`, `SetFan`, `WriteReg`) when `Global\Access_EC` degrades to `Local\` fallback and surfaced a top warning banner in `MainForm`.
  - Replaced `0xFF` failure-sentinel reads with `NativeInpOut.TryReadPhysicalByte(ulong, out byte)` with one-shot watchdog error logging, and hardened the single-instance GUI guard against fail-open races (`IsAnotherGuiInstanceRunning()`).
- **Verifiable Supply Chain & Optimized Lean Builds (`build-windows.bat`, `Tests/MiniFanControl.Tests.csproj`, `scripts/sync-version.ps1`, `.github/workflows/ci.yml`, `.github/dependabot.yml`)**:
  - Added `/optimize+ /debug-` to standalone `.NET 4.8` builds, excluded `Tests\*.cs` from shipping `MiniFanControl.exe` (~145 KB smaller) and `MiniFanControl.csproj` by default, and introduced a dedicated `Tests/MiniFanControl.Tests.csproj` runner verified on `.NET 10.0-windows`.
  - Added GitHub **Build Provenance Attestations** (`actions/attest-build-provenance@v4.2.2`), weekly grouped Dependabot PRs, GitHub CodeQL Default Setup scanning (`csharp` + `actions`), and single-source version synchronization via `scripts/sync-version.ps1`.
- **Strict Versioned JSON, Service Extraction, CLI JSONL & UX Polish (`Core/SimpleJson.cs`, `Core/DeviceProfile.cs`, `Core/AppSettings.cs`, `Ui/TrayIconRenderer.cs`, `Curves/CurveProfileCatalog.cs`, `Ui/MainForm*.cs`, `Ui/FanCalibrationDialog.cs`)**:
  - Added `SimpleJson.ParseFlatObjectStrict` (`System.Text.Json` on `.NET 8/10+`, strict tokenizer on `.NET 4.8`), schema-versioned `DeviceProfile.FromJsonStrict` (`schemaVersion: 1`) and `CustomProfileJson`, extracted `TrayIconRenderer` and `CurveProfileCatalog` from `MainForm`, and added `minifan-helper.exe status --stream` JSONL output (Test #123).
  - Added live-synced **Power / BIOS Power Mode** submenu with checkmarks to the system tray right-click menu, added hover tooltips across **Tools & Drivers ▾** clarifying Background Service Mode vs. Minimize to Tray vs. Exit Safety, fixed duplicate `%` signs & radio label clipping in the Fan Calibration dialog, slimmed `README.md` by 84% (`Tests/README.md` + `GUIDE.md` split + ⭐ Star History), and removed the 8.3 MB `osd-demo.mp4` blob from git.

## [1.3.0] - 2026-11-25

### ⚙️ Low-RAM GUI Background Service Mode (Closes [#2](https://github.com/kaiserc/MiniFanControl/issues/2) — thanks `@gemignani`!)
- **Zero-UI Windows Service Mode with ~3.5 MB Active RAM (`Core/WindowsServiceHost.cs`, `Core/HeadlessDaemon.cs`, `Hardware/HardwareBackend.cs`, `Core/AppPaths.cs`, `Core/AppSettings.cs`, `Ui/MainForm.cs`, `Ui/MainForm.MenusAndTray.cs`, `installer.iss`)**:
  - Added 1-click **`⚙ Enable Background Service Mode (Zero-UI / Boot Autostart)...`** in `Tools & Drivers` (plus an optional `servicemode` task and clean uninstall hook in `installer.iss`) so `MiniFanControlService` starts automatically at boot *before Windows login*—ideal for headless Mini PC servers, HTPCs, local LLM hosts, and set-and-forget desktop setups.
  - Added `HardwareBackend.LeanServiceMode` (skipping GUI-only RTSS/ETW/Radeon/SMU/SMBus polling and unnecessary GPU DLL loads when EC/SMN temps are available), eliminated the hidden `SystemEvents` thread under SCM, and added post-init/periodic `TrimProcessWorkingSet()` (also trimming GUI Working Set on `MinimizeToTray()`), dropping Task Manager active memory on live hardware to **~3.5 MB** (`0.00s` CPU).
  - Implemented **Shared Config Sync** (`%ProgramData%\MiniFanControl\minifancontrol.cfg` for Program Files installs, local folder for portable installs, passed via quoted `--config`), **live config hot-reload** inside `HeadlessDaemon`, and **seamless GUI ↔ Service handoff** (opening the GUI pauses the service automatically so curves/Auto-Tune/calibration never contend over EC registers; closing the GUI saves settings and restarts the service with zero fan blip) (Tests #116, #119).

### 🛡️ PawnIO HVCI-Native First-Run Onboarding, Bundled Signed Modules & PawnIO-First Packaging
- **Bundled Signed PawnIO Modules, `LpcIO` BAR Discovery & Direct Device Fallback (`Hardware/PawnIoDriver.cs`, `Hardware/HardwareBackend.SuperIo.cs`, `Core/DeviceProfile.cs`, `PawnIO/modules/*.bin`)**:
  - Bundled official EV-signed `LpcIO.bin`, `LpcACPIEC.bin`, and `AMDFamily17.bin` modules alongside a direct `\\?\GLOBALROOT\Device\PawnIO` IOCTL fallback (working even when `PawnIOLib.dll` isn't installed), Authenticode verification, and Priority-0 `AMDFamily17` SMN `Tctl/Tdie` silicon thermal reads (Tests #102, #112).
  - Implemented `TrySelectSuperIoSlot()` and `TryFindSuperIoBars()` (`ioctl_find_bars`) inside `ProbeSuperIo()` so PawnIO's `LpcIO.bin` BAR allowlist unlocks IT8613E/IT87xx Hardware Monitor ports (`0x0A30`) with Windows 11 Memory Integrity (HVCI) enabled (Test #118).
- **Guided Driver Setup Dialog & PawnIO-First Releases (`Ui/KernelDriverDialog.cs`, `Core/FanControllerService.cs`, `package-release.bat`, `installer.iss`)**:
  - Added an interactive startup onboarding modal (`KernelDriverDialog`) with live HVCI/PawnIO status, 1-click download (`pawnio.eu`), and zero-restart **`↻ Re-check Driver`** activation, gated live hardware writes while no kernel driver is open (eliminating false `Escalated failsafe` banners on virgin installs), and removed legacy `inpoutx64.dll` from default release packages to avoid AV/SmartScreen false positives while keeping SHA256-pinned manual drop-in support in `NativeInpOut` (Tests #117–#118).

### ❄️ White / Black / Red Xmas Edition Theme
- **Festive Nordic Winter Theme (`Core/ThemePreference.cs`, `Ui/MainForm.Theme.cs`, `Ui/MainForm.Layout.cs`, `Ui/MainForm.CurvesAndModes.cs`, `Ui/MainForm.OsdStudio.cs`, `Ui/FanCurveCanvas.cs`, `Ui/LiveTelemetryCanvas.cs`, `Ui/LivePerformanceCanvas.cs`, `Ui/AppLogoRenderer.cs`, `Ui/Win11VisualStyle.cs`, `Telemetry/CapFrameXOverlayForm*.cs`)**:
  - Added **`❄ Theme: Xmas`** (`ThemePreference.Xmas = 5`) featuring a crisp **Snow-White** background, **Jet Obsidian Black** typography/borders, **Festive Crimson Red** accents & Windows 11 DWM border, delicate vector crystalline snowflake watermarks, a bespoke Concept #6 **3D Snow-White & Crimson Xmas Mini-PC Logo**, matching dialog styling, and a full White/Black/Red **In-Game OSD** preset (Test #121).

### 🔧 Modern Standby (S0ix) Self-Healing, Adrenalin 26.x FSR Pill, NVML, External Profiles & Hardening
- **Modern Standby (S0ix) Super I/O Self-Healing (`Core/FanControllerService.cs`, `Hardware/HardwareBackend.SuperIo.cs`, `Hardware/HardwareBackend.cs`)**: Added wall-clock S0ix wake time-jump detection (`IsModernStandbyTimeJump`) and throttled `0xFF` Super I/O read-back self-healing so ITE IT8613E boards that reset LDN4 during Modern Standby automatically re-probe and recover without latching read-only failsafe (Test #120).
- **Live Adrenalin 26.x `AMD FSR Upscaling` OSD Pill (`Hardware/RadeonSettingsSensor.cs`)**: Wired live `UMD\FsrOverride` (`"0"`/`"1"`) detection so the **`FSR`** badge reflects Adrenalin 26.11+ `AMD FSR Upscaling` toggles in real time.
- **External `profiles/*.json` Registry, Auto Workload Switcher & Native NVIDIA NVML (`Core/ExternalProfileRegistry.cs`, `Core/WorkloadProfileSwitcher.cs`, `Hardware/NvmlGpuSensor.cs`)**: Added drop-in external hardware profiles (`minifan-helper.exe validate-profiles`), automatic per-game & AI/LLM/NPU profile switching with exit cooldown, and WHQL `nvml.dll` desktop GPU telemetry (Tests #113–#115).
- **Architecture, Safety & OSD Hardening (`Tests/SelfTestSuite*.cs`, `Core/AppPaths.cs`, `Core/AutomationCoordinator.cs`, `Telemetry/CapFrameXOverlayForm.Graphs.cs`)**: Split `SelfTestSuite` into 5 modular files, enforced `/warn:4`, restored compact top-left DF Corner frame-time inset over full-width FPS chart, fixed independent APU/Sys cooldown hysteresis, zero-alloc ETW ring wrap, atomic settings writes, and expanded the suite to **121/121 passing** (Tests #102–#121).

## [1.2.2] - 2026-11-22

### ⏱️ Native Hook-Free Windows ETW Display Latency & Flip Mode Monitoring
- **PresentMon-Style DXGI/DxgKrnl ETW Latency Engine (`Telemetry/EtwLatencyMonitor.cs`, `Hardware/HardwareBackend.cs`, `Hardware/DemoBackend.cs`, `Core/FanStatus.cs`, `Core/TelemetrySessionStats.cs`)**: Added zero-dependency real-time Windows ETW tracing (`Microsoft-Windows-DXGI` + `Microsoft-Windows-DxgKrnl` QPC events) measuring Present-to-Display scan-out latency (`DisplayLatencyMs`), Render latency (`RenderLatencyMs`), Flip Mode (`MPO Flip` / `Indep. Flip` / `Composed`), and automatic hook-free fallback FPS/Frametime/1%/0.1% Lows even when RTSS isn't running—with post-flip scan-out pairing to prevent premature secondary-monitor VSync under-reads (Test #100).
- **OSD, OSD Studio, `Live Perf` Ribbon & CLI Integration (`Telemetry/CapFrameXOverlayForm*.cs`, `Ui/LivePerformanceCanvas.cs`, `Ui/MainForm.OsdStudio.cs`, `Core/OsdOverlayConfig.cs`, `Program.cs`)**: Displays live `LAT: xx.xms` on the DF Corner / FPS graph header and OSD strip, adds the `Display Latency (ETW)` toggle in OSD Studio and the OSD context menu, adds color-coded `Lat: xx.xms (MPO Flip)` badges + hover HUD readouts to `Live Perf`, adds `minifan-helper.exe latency [--demo]`, and exposes latency fields in `status` JSON (Test #101).

### 🖥️ Desktop GPU / Read-Only OSD Polish, Upward Frametime Scale & Responsive Layout
- **Desktop GPU & Read-Only OSD Refinements (`Hardware/RadeonSettingsSensor.cs`, `Telemetry/CapFrameXOverlayForm*.cs`, `Telemetry/FpsMonitor.cs`, `Ui/LivePerformanceCanvas.cs`, `Ui/MainForm.Layout.cs`)**: Added discrete GPU detection (`GPU` vs `iGPU` labels), clean ASCII `(HS xx°)` hotspot formatting, automatic read-only OSD row trimming (hiding 0 RPM fans & read-only mode), consistent frametime resolution, 1% Low history tracking, upward frametime graph scaling, and responsive layout refinements (Test #99).
- **101/101 Automated Tests (`Tests/SelfTestSuite.cs`, `.github/workflows/ci.yml`, `README.md`, `GUIDE.md`, `HARDWARE_VALIDATION.md`, `CONTRIBUTING.md`)**: Surpassed **100+ automated tests** with **101/101 passing** across `.NET 4.8` and `.NET 10.0-windows`.

## [1.2.1] - 2026-11-22

### 🖥️ High-DPI Scaling & Safe Desktop Read-Only Monitoring
- **Windows High-DPI GDI Scaling (`app.manifest`, `Program.cs`, `Ui/MainForm.cs`, `Ui/MainForm.Layout.cs`, `Ui/SetupAssistantDialog.cs`)**: Enabled Windows 10/11 System Enhanced GDI Scaling (`dpiAware=false`, `gdiScaling=true`, and `HighDpiMode.DpiUnawareGdiScaled` on `.NET 10`) with `AutoScaleMode.None` and scrollbar-safe initial sizing, eliminating text clipping, wrapped buttons, and overlapping labels on 125%–250% HiDPI/4K monitors across the main dashboard, dialogs, canvases, and in-game OSD (Test #98).
- **Clean Safe Read-Only Mode on Unverified / Desktop Hardware (`Core/FanControllerService.cs`, `Ui/MainForm.cs`, `Ui/MainForm.CurvesAndModes.cs`, `Ui/MainForm.MenusAndTray.cs`, `Ui/SetupAssistantDialog.cs`)**: Added `IsInherentlyReadOnly` guards so unverified boards (`unknown-safe`, e.g., desktop motherboards) never attempt EC/fan writes or trigger spurious `Escalated failsafe (Failures: 10)` error banners, suppressed auto-opening the Mini PC Setup Assistant on startup, hid unusable EC `BIOS:` header buttons, disabled manual fan write controls, and surfaced accurate `Read-Only` status across the header, fan cards, tabs, OSD overlay, and status bar (Test #97).
- **98/98 Automated Tests (`Tests/SelfTestSuite.cs`, `.github/workflows/ci.yml`)**: Expanded automated test coverage to **98/98 passing**.

## [1.2.0] - 2026-11-22

### ✨ Windows 11 DWM Visual Polish & Smooth Live Performance Parity
- **Native Windows 11 DWM Chrome & Card Polish (`Ui/Win11VisualStyle.cs`, `Ui/MainForm.Theme.cs`, `Ui/MainForm.Layout.cs`, `Ui/MainForm.cs`)**: Added zero-dependency `dwmapi.dll` `DwmSetWindowAttribute` theming for dark titlebars, rounded corners, and theme-matched caption/text/border colors across Dark, Light, AMD Radeon, and Afterburner themes; upgraded section cards, telemetry boxes (sensor-tinted ambient top sheen + rounded borders), tooltips, and buttons with crisp 1px hairline borders and hover/pressed feedback while staying High Contrast safe (Tests #95–#96).
- **Smooth `Live Perf` FPS Curve Parity & Header Polish (`Ui/LivePerformanceCanvas.cs`, `Ui/MainForm.Layout.cs`)**: Matched the Digital Foundry Overlay's smooth rolling FPS curve on the `Live Perf` tab (5-point Gaussian weighted average with exact live leading edge, keeping the Frametime inset raw for true micro-stutter visibility), widened the top `Power:` header label to prevent DPI clipping, and updated the hero showcase screenshot (`assets/ui-main-osd.jpg`).

### 🛡️ PawnIO HVCI Backend, Anti-Cheat Safe Auto-Pause, Headless Daemon & 1-Click Submission
- **PawnIO (`PawnIOLib`) HVCI-Compatible Driver Backend (`Hardware/PawnIoDriver.cs`, `Hardware/NativeInpOut.cs`, `Program.cs`, `Ui/MainForm.MenusAndTray.cs`)**: Implemented the official `PawnIOLib.dll` C ABI (`pawnio_version`, `pawnio_open`, `pawnio_load`, `pawnio_execute`, `pawnio_close`) loading signed `LpcIO.bin` / `LpcACPIEC.bin` modules as Priority 0 ahead of SHA256-pinned `InpOut64` / `WinRing0`, while keeping every port read/write strictly fail-closed behind `NativeInpOut.IsPortWhitelisted()`. Added live PawnIO & HVCI telemetry to `driver-status` and the Security Status dialog (Test #91).
- **Opt-In Anti-Cheat Safe Auto-Pause (`Core/AntiCheatMonitor.cs`, `Core/AppSettings.cs`, `Ui/MainForm.cs`, `Ui/MainForm.MenusAndTray.cs`)**: Added an opt-in `🎮 Auto-Pause for Anti-Cheat (EAC / BattlEye)` toggle that strictly restores BIOS Auto / Safe Super I/O Fallback *before* unloading legacy kernel drivers (skipping unload when EV-signed PawnIO is active), pauses OSD cleanly, resumes after a 3s cooldown when the anti-cheat exits, and surfaces boot-time Riot Vanguard (`vgk`) detection in Security Status (Test #92).
- **Headless Background Daemon Mode (`Core/HeadlessDaemon.cs`, `Program.cs`)**: Added `minifan-helper.exe daemon [--interval <ms>] [--max-ticks <n>] [--quiet] [--demo]` to run `FanControllerService` (curves, anti-stall floor, 90°C tripwire, suspend/resume, and optional anti-cheat safe pause) with zero WinForms/GDI+ UI overhead for 24/7 servers and local LLM hosts (Test #93).
- **Upgraded 1-Click Hardware Profile Submission (`Core/HardwareProfileSubmission.cs`, `Ui/MainForm.MenusAndTray.cs`, `Program.cs`)**: Added PII-sanitized JSON generation (redacting `C:\Users\<name>` paths), pre-filled GitHub New Issue URLs, and `minifan-helper.exe submit-report` (Test #94).
- **96/96 Automated Tests (`Tests/SelfTestSuite.cs`, `.github/workflows/ci.yml`, `README.md`, `GUIDE.md`, `HARDWARE_VALIDATION.md`, `CONTRIBUTING.md`)**: Expanded the automated hardware-safety and regression suite to **96/96 passing** across `.NET 4.8` and `.NET 10.0-windows`.

## [1.1.3] - 2026-11-21

### 🎛️ Super I/O IT8613E Dynamic Polarity & Native Windows Power Mode Bar
- **Dynamic IT8613E `Reg 0x14` PWM Polarity Tracking (`Hardware/HardwareBackend.SuperIo.cs`, `Hardware/HardwareBackend.cs`, `Curves/FanCalibrationResult.cs`)**: Reads IT8613E `Reg 0x14` Bit 7 (`PWM_POL`) dynamically so both cold-boot Active-High (`0xC7`) and post-sleep/Modern Standby Active-Low (`0x40`/`0x47`) states map fan duty percentages and 35% safe fallback bytes accurately without mutating `Reg 0x14`. Includes a runtime Pass 1 calibration auto-flip safety net and diagnostic polarity logging (Test #89).
- **Native Windows Power Mode Header Bar on Super I/O Boards (`Core/WindowsPowerMode.cs`, `Ui/MainForm.Layout.cs`, `Ui/MainForm.CurvesAndModes.cs`, `Hardware/HardwareBackend.cs`)**: Displays **`Power: [🌙 Silent] [⚖ Balanced] [⚡ Perf]`** on Super I/O boards (such as the TOPC TS-12C / Ryzen AI 9 HX 370) synced 1:1 with Windows 11 Power Modes (`Best Power Efficiency` / `Balanced` / `Best Performance`) via native `powrprof.dll` `PowerSetActiveOverlayScheme` / `PowerGetActualOverlayScheme`—while preserving the EC **`BIOS:`** bar on IT5570E boards like the GMKtec K12 and automatically adapting `✨ Auto-Tuned` curves (Test #90).

### 🔧 Super I/O Calibration & Non-Zero Floor Blower Reliability
- **True `0%` Super I/O Stop Duty & Tach Channel Latching (`Curves/FanCalibrationResult.cs`, `Hardware/HardwareBackend.SuperIo.cs`, `Hardware/HardwareBackend.cs`, `Ui/FanCalibrationDialog.cs`)**: Uses `0%` stop duty on Super I/O boards during calibration (keeping `1%` on EC boards where `0%` restores BIOS Auto), latches verified Super I/O tachometer channels so a stopped Sys fan never mirrors Fan 1's RPM, and detects settled non-zero hardware blower floors (e.g., ~320 RPM) for accurate ramp-up discovery without tripping the 88°C thermal safety abort (Test #88).
- **90/90 Automated Self-Tests (`Tests/SelfTestSuite.cs`, `.github/workflows/ci.yml`, `README.md`)**: Expanded the automated hardware-safety and regression suite to **90/90 passing** across `.NET 4.8` and `.NET 10.0-windows`.

## [1.1.2] - 2026-11-21

### 🔒 Defense-in-Depth Hardening & GUI/CLI Parity
- **Unified GUI + CLI Anti-Stall Floor Enforcement (`Core/FanControllerService.cs`, `Ui/MainForm.cs`, `Program.cs`)**: Consolidated manual fan duty clamping into `FanControllerService.ClampManualFanDuty()` and enforced it inside `ApplyManualFan()` (with an explicit `allowBelowFloor` override). Both the GUI manual sliders and `minifan-helper.exe fan`/`fan1`/`fan2` now enforce identical anti-stall safety, snapping the GUI slider to the calibrated spin floor with a clear status callout when clamped (Test #86).
- **Strict Physical Memory Allowlist & EC-Only Write Barrier (`Hardware/NativeInpOut.cs`, `Hardware/HardwareBackend.cs`, `Hardware/AmdSmuSiliconSensor.cs`)**: Added `IsPhysicalMemoryRangeAllowed()` restricting `MapPhysToLinSafe()`, `ReadPhysicalByteSafe()`, and `WritePhysicalByteSafe()` to explicit hardware windows: verified AMD FCH/EC MMIO (`0xFE0B0400` or verified FCH MMIO ≥ `0xFE000000`, ≤ 4 KB), read-only PCIe ECAM Bus `0:0:0` (4 KB), and the runtime-registered read-only AMD SMU PM-table DRAM buffer (≤ 16 KB). Arbitrary physical RAM reads/maps and all non-EC physical writes are blocked fail-closed (Test #87).
- **Hardened `LoadLibraryEx` Search Flags (`Hardware/NativeInpOut.cs`)**: Replaced `LOAD_WITH_ALTERED_SEARCH_PATH` with `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32`.

### 🏗️ CI Supply-Chain Hardening, Modular OSD Split & 87/87 Suite
- **SHA-Pinned GitHub Actions (`.github/workflows/ci.yml`)**: Pinned all workflow actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact`, `softprops/action-gh-release`) to verified commit SHAs.
- **Modular Overlay Split (`Telemetry/CapFrameXOverlayForm*.cs`)**: Split the 3,960-line overlay form into 5 focused partial files (`CapFrameXOverlayForm.cs`, `.ContextMenu.cs`, `.Formatting.cs`, `.Graphs.cs`, `.Paint.cs`).
- **Documentation & 87/87 Automated Tests (`README.md`, `GUIDE.md`, `Program.cs`, `Tests/SelfTestSuite.cs`)**: Fixed the remaining `2°C` → `4°C asymmetric` hysteresis note in `README.md`, documented the physical memory allowlist, and expanded the automated suite to **87/87 passing** on both `.NET 4.8` and `.NET 10.0-windows`.

## [1.1.1] - 2026-11-21

### 🔒 Security, Integrity & Hardware-Safety Hardening
- **Runtime SHA256 Driver Pinning with TOCTOU Lock (`Hardware/NativeInpOut.cs`, `Ui/MainForm.MenusAndTray.cs`, `Program.cs`)**: Enforced fail-closed SHA256 verification before `LoadLibraryEx` for both `inpoutx64.dll` and optional `WinRing0x64.dll` while holding a `FileShare.Read` handle across hash + load to block DLL planting / TOCTOU swaps. The Security Status dialog and `minifan-helper.exe driver-status` now report live runtime hash verification and integrity alerts.
- **Single-Choke-Point Port Whitelist (`Hardware/NativeInpOut.cs`, `Hardware/HardwareBackend.PortsAndEc.cs`)**: Moved `IsPortWhitelisted()` into `NativeInpOut` and enforced it directly inside `DlPortReadPortUchar` / `DlPortWritePortUchar` (`Inp32`/`Out32`) so no caller can bypass port restrictions.
- **DMI-Bound Custom Profiles & Fail-Closed `FromJson()` (`Core/DeviceProfile.cs`)**: Added `boundManufacturer`/`boundProductName`/`boundBaseBoard` binding to `custom-profile.json`, defaulted `FromJson()` fail-closed to `IsReadOnly = true` / `IsVerified = false`, enforced hardware blocklists (`Recoreqich`/`KTHIN`/`HX PRO 370`) ahead of custom profile lookup, and prevented portable custom profiles from activating on mismatched hardware.
- **Read-Only Failsafe Latch Thermal Protection (`Core/FanControllerService.cs`)**: Evaluated the ≥90°C emergency thermal tripwire *before* the read-only failsafe latch return, added periodic `SetAuto()` retries while latched if escalation `SetAuto()` failed, and validated Super I/O support in `SetBiosPowerMode()` before mutating controller state.
- **True Read-Only Startup Probing & Tach Spike Bypass (`Hardware/HardwareBackend.cs`, `Hardware/HardwareBackend.SuperIo.cs`, `Hardware/HardwareBackend.PortsAndEc.cs`)**: Gated `VerifyIt87HwBase()` register `0x00` writes behind `allowConfigWrites`, skipped automatic Super I/O LPC config-mode probing in Safe Read-Only Mode, and bypassed the 20-poll upward tach spike hold when `≥95%` PWM is commanded.
- **CLI Anti-Stall Floor Clamping & Locale-Safe JSON (`Program.cs`, `Curves/CurvePoint.cs`)**: `fan`/`fan1`/`fan2` now clamps sub-floor manual values (`1..floor-1`) to the calibrated anti-stall floor unless `--force` is passed (`0%` remains BIOS Auto); `status` and CLI JSON outputs now use `InvariantCulture` and RFC-8259 string escaping (fixing comma-decimal locales like `de-DE`/`fr-FR`); `EvaluateCurve()` defensively sorts unsorted points and enforces non-decreasing PWM monotonicity.

### 🏗️ Architecture, `.NET 10` Runtime Compatibility & CI/Release Fixes
- **`.NET 10.0-windows` WinForms Compatibility (`Ui/MainForm.MenusAndTray.cs`, `Ui/LivePerformanceCanvas.cs`, `Ui/LiveTelemetryCanvas.cs`)**: Migrated remaining legacy `ContextMenu`/`MenuItem` usages to `ContextMenuStrip`/`ToolStripMenuItem` (resolving runtime `PlatformNotSupportedException` on `.NET 10`), and updated Test #68 watchdog helper launching for `dotnet.exe` hosting.
- **Unified `SimpleJson` Parser & Modular Partial Split (`Core/SimpleJson.cs`, `Hardware/HardwareBackend.*.cs`, `Ui/MainForm.*.cs`)**: Consolidated JSON tokenization and escaping across `Loc`, `DeviceProfile`, and CLI onto `SimpleJson` (adding single-line JSON support for `DeviceProfile.FromJson()`), split `HardwareBackend` into 4 partial files and `MainForm` into 6 partial files, and untracked compiled `.exe` binaries from git.
- **CI, Installer Packaging & 85/85 Test Suite (`.github/workflows/ci.yml`, `Tests/SelfTestSuite.cs`, `app.manifest`, `SECURITY.md`, `GUIDE.md`, `README.md`)**: Fixed the `dist/*-Setup-*.exe` release glob so the Inno Setup installer uploads to GitHub Releases, scoped `contents: write` strictly to the tag-only `publish-release` job, added automated version-consistency validation and `.NET 10` test execution in CI, synced docs/manifests, and added Tests #78–#85 (**85/85 passing** on both `.NET 4.8` and `.NET 10.0-windows`).

## [1.1.0] - 2026-11-21

### 🖥️ Dashboard, BIOS Power Mode & Fan Curve Polish
- **Expanded 2×4 Telemetry Dashboard & Scrollbar-Free OSD Studio (`Ui/MainForm.cs`)**: Upgraded the 8 hardware sensor cards to `257×108 px` with `▼Low ~Avg ▲High` session ranges, dedicated 2-line detail rows, full profile names, a prominent `72×72` top-left header logo, and an expanded main card height (`590px` / `720px`) so all 7 OSD Studio metric sections fit cleanly without an inner scrollbar.
- **Explicit Independent `BIOS:` Power Mode Bar (`Ui/MainForm.cs`, `Core/FanControllerService.cs`, `Core/AppSettings.cs`)**: Added an always-visible **`BIOS: [🌙 Silent] [⚖ Balanced] [⚡ Perf]`** switcher in the top header (EC `0x31`/`0x32`, persisted as `BiosPowerMode`) so users can switch live factory BIOS TDP limits independently without overwriting Custom Fan Curves.
- **Dual-Fan Mode-Aware `✨ Auto-Tune` (`Ui/MainForm.cs`, `Curves/CurvePoint.cs`)**: Running Auto-Tune now applies to **both** APU Fan and Sys Fan automatically on dual-fan systems, clamps bogus TDP readings to `15..120W`, and dynamically adapts its acoustic knee (`Silent 35W` / `Balanced 48W` / `Perf 60W+`) whenever you switch the top `BIOS:` power mode while `✨ Auto-Tuned` is active.
- **Live Curve Operating Callout Badge (`Ui/FanCurveCanvas.cs`, `Ui/MainForm.cs`)**: Fixed the curve operating point to use calibrated stall floors (`ApuMinSpinPct`/`SysMinSpinPct`) and replaced the bare dot with a live **`Source Temp • Duty % • RPM`** callout pill (e.g., `CPU 45°C • 26% • 972 RPM` or `NVMe 49°C • 26% • 539 RPM`).
- **Tachometer Glitch Stabilization & Emergency Display (`Hardware/HardwareBackend.cs`, `Ui/MainForm.cs`)**: Added high-load zero-tach debounce (up to 8 polls at `≥50%` PWM / `≥1500 RPM`) and implausible upward-spike rejection (`> +2000 RPM` single-poll jumps), plus `100% (Emergency)` badges on both fan cards during thermal override and rich EC/slew-rate info in the CPU card tooltip.
- **Un-Maximize Scrollbar Fix (`Ui/MainForm.cs`)**: Collapsing `_wideRightPanel` bounds and resetting `AutoScrollPosition` when restoring down from wide/full-screen mode eliminates lingering horizontal/vertical scrollbars.

### 🛡️ Universal Laptop / Handheld / Mini-PC Calibration Upgrades
- **Thermal Safety Abort (`Curves/FanCalibrationResult.cs`, `Ui/FanCalibrationDialog.cs`)**: Added a live `≥ 88°C` thermal tripwire during both coast-down and step sweeps that pulses 100% cooling, aborts safely, and restores BIOS Auto if background load heats the APU.
- **Adaptive 1-Second Steady-State Settle & `12%` Sweep Floor (`Curves/FanCalibrationResult.cs`, `Core/AppSettings.cs`, `Curves/CurvePoint.cs`)**: Lowered the calibration sweep start from `20%` to `12%` (safe floor clamp `14%`), fixed `EvaluateCurve()` to honor calibrated `14%` floors end-to-end instead of snapping back to `30%`, replaced single-tick movement detection with a 1-second rolling RPM window (`> 40 RPM/s`, up to `+6s` settle on `100%`), and reset tachometer debounce state at each pass start.

### 🎮 Digital Foundry Layouts & In-Game OSD Enhancements
- **`🔍 DF Zoom` & `◰ DF Corner` Views (`Ui/LivePerformanceCanvas.cs`, `Telemetry/CapFrameXOverlayForm.cs`, `Core/OsdOverlayConfig.cs`, `Ui/MainForm.cs`)**: Added a zoomed 30-sample Frame-Time mode (`DF Zoom`) and an authentic compact corner-inset layout (`DF Corner` / OSD **`DF Corner Inset`**) across both Vertical Stack and Horizontal Bar overlays.
- **`Graphs Only` OSD Mode, Per-Series Live Trend Toggles & `Sampling Rate` Right-Click Menu (`Telemetry/CapFrameXOverlayForm.cs`, `Ui/LiveTelemetryCanvas.cs`, `Ui/MainForm.cs`, `Core/OsdOverlayConfig.cs`)**: Added `Graphs Only` (`GraphMode = 4`) under Live Graph Display Mode, added synced per-series Live Trend toggles (left-click pills on the Live Trend tab or right-click **`Live Trend Series`** on the overlay, with `Show All` / `Temps Only` / `Power & Fans Only` presets), wrapped the overlay Live Trend legend onto 2 rows so `CHAS` / `APU` / `SYS` are never clipped, and added a `Sampling Rate` submenu directly to the overlay right-click menu.
- **`1%` & `0.1%` Lows Parity, Sample-Rate Timestamps & Cleanups (`Telemetry/CapFrameXOverlayForm.cs`, `Ui/LivePerformanceCanvas.cs`, `Ui/LiveTelemetryCanvas.cs`, `Ui/MainForm.cs`)**: Added `1%` & `0.1%` lows + shaded rose `0.1%` floor band to the overlay FPS chart, synchronized hover & timeline timestamps to the live `SampleIntervalMs`, removed FCLK from Live Trend charts, auto-disabled `iGPU Hotspot (N/A)` when unsupported, replaced user-facing `CapFrameX` labels with **In-Game OSD** / **MiniFanControl**, and expanded the automated test suite to **77/77 passing** (Test #77).

## [1.0.6] - 2026-11-19

### 🛡️ Fail-Closed Hardware Matching & Probe Diagnostics
- **Vendor-Scoped `DeviceProfile.MatchProfile` (`Core/DeviceProfile.cs`)**: Added `Manufacturer` to hardware profile matching, removed ambiguous bare CPU/model token matches (`8845`, `8945`, `7840`, `7940`, `H 255`, `HX 370`, `STRIX`, bare `K8`), and added explicit fail-closed guards so unverified third-party systems (e.g., `Recoreqich K8`, `SZQFTX ARB39-SC`, `TianBei MACO`, `Beelink SER9`, `TOPC PHX`) stay safely in `unknown-safe` read-only mode while verified `GMKtec` (`NucBox K8`/`K11`/`K12`/`EVO-X1`), `TOPC TS-12C`, and `ADREAMER AriesBox 10` systems remain supported.
- **Structured Probe JSON & Super I/O Chip Identification (`Program.cs`, `Core/DeviceProfile.cs`)**: Added `minifan-helper.exe dump --json` (`SystemHardwareInfo.BuildProbeReportJson`) and `DescribeSuperIoChip` (`ITE IT5570E`, `ITE IT8613E`, `Nuvoton NCT67xx`), and updated `dump` text output to report `CHIP DETECTED (NO LDN4)` when a Super I/O/EC chip ID responds without an IT87 hardware monitor base.
- **Documentation & 76-Point Test Suite (`README.md`, `GUIDE.md`, `HARDWARE_VALIDATION.md`, `Tests/SelfTestSuite.cs`)**: Aligned `GMKtec EVO-X1 / TOPC TS-12C` hardware docs, documented `dump --json`, and added Test #76 plus vendor-scoped fail-closed regression checks (**76/76 passing**).

## [1.0.5] - 2026-11-18

### 🛠️ CLI & CI Reliability
- **CLI Profile Registry Export (`export-profiles`)**: Added `minifan-helper.exe export-profiles [--json]` (backed by `DeviceProfile.ExportKnownProfilesJson` and `ExportKnownProfilesTable`) so contributors and hardware databases can export verified device profiles, EC/Super I/O register layouts, and probed Super I/O chip ID prefixes directly from the CLI (Test #75).
- **Resolution-Independent Wide Layout Test**: Added `MainForm.TestApplyWideLayoutSize(int, int)` for Test #59 so Wide 2-Column Mode (`>= 1450px`) layout verification passes reliably on headless CI runners (`1024×768` / `1280×1024`).

## [1.0.4] - 2026-11-18

### 🌀 Fan Curve & Hysteresis
- **Non-Zero Auto-Tune APU Idle Floor (`Curves/CurvePoint.cs`)**: `SynthesizeAutoTunedCurve` now anchors the APU fan idle point at `apuFloor` (`ApuMinSpinPct`) instead of `0%`, eliminating constant start/stop hunting during light desktop bursts (while preserving `🔒 Zero-RPM Desktop Idle` for explicit 0 RPM setups).
- **Recalibration Curve Sync (`Ui/MainForm.cs`)**: Added `RefreshAutoTunedCurveAfterCalibration()` so recalibrating fans automatically re-synthesizes any existing `✨ Auto-Tuned` custom curve with the newly calibrated minimum spin floors.
- **Asymmetric Cooldown Hysteresis (`Core/FanControllerService.cs`)**: Increased cooldown hysteresis from `2°C` to `4°C` down (`0°C` up) and removed the `regPct == 1` hysteresis bypass so fans ramp up immediately under load but resist hunting on brief temperature dips.

### 🖥️ Wide 2-Column Mode & Live Performance
- **Side-by-Side Wide Mode (`Ui/MainForm.cs`)**: When the window is widened or maximized (`ClientSize.Width >= 1450px`), a **Live Telemetry & Performance Monitor** companion panel docks on the right (`• Live Trend` / `• Live Perf` toggle) alongside the Fan Curve Editor / OSD Studio on the left.
- **Rolling Live Average FPS (`Ui/LivePerformanceCanvas.cs`, `Core/TelemetrySessionStats.cs`)**: `Live Perf` `Avg` and the dashed `AVG` graph line now prioritize RTSS's live rolling average (`RtssAvgFps`) or the visible 120-sample window instead of the cumulative all-session average.

### 🎮 AMD Radeon, NPU & OSD Polish
- **Accurate Adrenalin Feature Detection (`Hardware/RadeonSettingsSensor.cs`, `Hardware/HardwareBackend.cs`)**:
  - Removed false-positive triggers (`UMD\FsrOverride` capability flag and `FSRUpgradeAvailable` / `has_upscaling_profile` in `gmdb.blb`).
  - Scoped `GExDb.blb` parsing strictly to the Global (`""`) profile at idle plus the running game's `.exe` profile (via `rtss.ProcessName`) when gaming, and mapped Adrenalin 26.x `FeatureID 27` to **Radeon Image Sharpening (RIS)**.
  - Removed the obsolete `🎮 Radeon Driver Features (KMD)` registry toggle submenu from `Tools & Drivers`.
- **NPU Hardware Auto-Detection (`Hardware/AmdAdlGpuSensor.cs`, `Telemetry/CapFrameXOverlayForm.cs`, `Ui/MainForm.cs`)**: Added `HasNpuHardware` PCI device detection (`VEN_1022&DEV_1502/1508/17F0/17F8`) so APUs without an XDNA NPU (such as the Ryzen 7 H 255) hide `◆ NPU` from the OSD, hide `Ryzen AI NPU` from the overlay right-click menu, and show `Ryzen AI NPU (N/A)` in OSD Studio.
- **Fixed-Width OSD Alignment (`Telemetry/CapFrameXOverlayForm.cs`)**: Removed `@` from CPU and iGPU OSD lines, added fixed-width padding (`{temp,2}`, `{load,3}%`, `{clock,4}MHz`) to prevent horizontal jitter across 3/4-digit clocks and 1/2/3-digit loads, and unified `FCLK` to `{0,4}MHz`.
- **Driver & Adrenalin Diagnostics (`README.md`, `Ui/MainForm.cs`)**: Added `AdrenalinInstalled` detection, in-app status/warning banners for missing EC kernel drivers and minimal/missing AMD display drivers, and a Driver & Software Compatibility Matrix in `README.md` (verified by 74/74 automated self-tests).

## [1.0.3] - 2026-11-18

### 🏗️ Architecture Refactor (`MainForm` Decomposition)
- **`Core/OsdOverlayConfig.cs`**: Extracted all ~45 CapFrameX OSD layout/metric settings, `ResetDefaults`, `SelectAllMetrics`, `DeselectAllMetrics`, `ApplyToOverlay`, `ReadFromOverlay`, `TryParseSetting`, and `AppendSettings` (using `CultureInfo.InvariantCulture`).
- **`Core/AppSettings.cs`**: Extracted `minifancontrol.cfg` persistence (`Load`, `Save`, `ParsePointList`, `FormatPointList`, `TryParseHexOrDecByte`, `GetSettingsPath`) with `CultureInfo.InvariantCulture` formatting/parsing and watchdog event logging on I/O failures.
- **`Core/FanControllerService.cs`**: Extracted hardware polling, `EvaluateAndApplyFanProfiles`, manual/auto fan transitions, thermal tripwire, write-error escalation/failsafe latching, sleep/resume worker pause handling, and immutable snapshot events out of `Ui/MainForm.cs` with deterministic `Dispose()`.
- **Deterministic Resource Cleanup**: Added `CleanupManagedResources()` in `Ui/MainForm.cs` disposing `_controller`, `_uiToolTip`, `_driversMenu` (via `RebuildDriversMenu()`), and `_refreshTimer`.

### 🛡️ Bug & Robustness Fixes
- **JSON Parser Hardening (`DeviceProfile.FromJson` & `Loc.ParseJsonStrings`)**: Fixed colon-splitting bug in `DeviceProfile.FromJson` when string values contain colons (e.g., `displayName` or `chipsetNotes`), and replaced line-based `Loc.ParseJsonStrings` with a tokenizing parser supporting single-line JSON, embedded colons, backslash escapes (`\"`, `\\`, `\/`, `\n`, `\r`, `\t`), and `\uXXXX` Unicode escapes (plus `LoadCustomJson`/`ClearCustomStrings`).
- **Writable Diagnostic Path (`sio-diag.txt`)**: Updated `HardwareBackend.DumpSuperIoDiagnostic` via `GetWritableDiagnosticPath` to fall back to `%LocalAppData%\MiniFanControl` when installed under read-only folders like `Program Files`.
- **Single-Instance & Control State Fixes**: Stopped `StartWatchdogSupervisor()` from tearing down `StopSingleInstanceWakeListener()` on watchdog spawn failures and tied the listener loop condition to `_wakeListenerRunning`. Filtered slider `KeyUp` events via `IsSliderValueChangeKey` so `Tab` navigation never dispatches unintended fan PWM writes.
- **Fahrenheit (`°F`) & Version Synchronization**: Fixed missing Fahrenheit conversion in the main window title bar and NVMe telemetry tag, and synchronized version `1.0.3` across `VERSION`, `MiniFanControl.csproj`, `installer.iss`, `app.manifest`, `Program.cs`, `Ui/MainForm.cs`, and `package-release.bat`.

### ♿ Accessibility, High Contrast & DPI Improvements
- **Full Keyboard Navigation & UIA Metadata**: Added `ControlStyles.Selectable`, `TabStop = true`, UIA `AccessibleRole` (`Chart` / `PushButton`), focus indicators, and keyboard interaction across `FanCurveCanvas` (`PageUp`/`PageDown`/`[`/`]` point selection, Arrow keys with `Ctrl` fine-stepping), `LiveTelemetryCanvas` (`R` reset peaks), `LivePerformanceCanvas` (`V` split view toggle, `R` reset peaks), and all 8 telemetry metric boxes (`DoubleBufferedPanel.EnableKeyboardActivation`, `Enter`/`Space` click equivalent, and live-value `AccessibleName` updates in `ApplyStatusToUi`).
- **Dialog Keyboard Flow, DPI Scaling & High Contrast**: Added `AcceptButton`/`CancelButton` on `SetupAssistantDialog`, enabled `AutoScaleMode.Dpi`, resizable borders, and `AutoScroll = true` on `MainForm` and `_osdMetricsScrollPanel`, and added `SystemInformation.HighContrast` system color adaptation in `ApplyThemeToControls()`.
- **Expanded Regression Test Suite**: Added 5 regression checks in `Tests/SelfTestSuite.cs` (74/74 automated tests passing).

## [1.0.2] - 2026-11-18

### 🎨 UI Clarity, Per-Metric OSD Studio & Expanded Fan Curve Layout
- **Wider & Taller Main Window (`1100 × 1160`)**: Expanded the main form width and height so all 8 top telemetry boxes (`CPU`, `iGPU`, `RAM`, `NVMe`, `PWR`, `Chassis`, `APU`, `SYS`) sit cleanly side-by-side without overlap, and all OSD Studio sections fit without a scrollbar or clipped checkbox text at 125% DPI.
- **Per-Metric OSD Studio Sections**: Reorganized OSD Studio toggles into dedicated sections (**CPU**, **Power & VRM**, **iGPU & Radeon**, **RAM (DDR5)**, **NVMe Storage**, **Chassis & Silicon**, **FPS & Frametime**, **Fans**, and **Graphs, Theme & Global Overlay**).
- **Expanded Fan Curve Editor & Badge Alignment**: Increased `_curveCanvas` to `1056 × 496` (`modeCard` height `632px`) on the **APU Fan** and **Sys Fan** tabs to fill the space above `Manual Fan Control`, and aligned `_calibBadgeLabel` cleanly on the `Manual Fan Control` header row.
- **Chassis Naming & Live Trend Click Alignment**: Renamed the motherboard ambient box to `Chassis`, fixed `_fan1Box` placement in `ApplyFanCapabilities()`, and aligned `_ambientBox` click/hover selection to `LiveTelemetryCanvas` metric ID `9`.

### 🌡️ Fahrenheit (°F) Mode, Tracker Modes & Overlay Polish
- **Global Fahrenheit (`°F`) Option**: Added **Use Fahrenheit (°F)** in the `Options` card (`UseFahrenheit`), converting temperatures across telemetry boxes, status strip, tray tooltips, `LiveTelemetryCanvas`, `FanCurveCanvas`, and `CapFrameXOverlayForm`.
- **Tracker Modes & Granular Sub-Field Toggles**: Added `PeakTrackerMode` (`Peaks (▲)`, `Lows (▼)`, `Min/Max (▼▲)`, `Avg (~)`) and per-field OSD toggles (`CPU Load`, `iGPU Hotspot`, `APU Power Limit`, `NVMe I/O`, `NVMe Dual-Drive`, `FCLK Sync Badge`, `FPS Frametime`, `FPS Session Avg`, and RAM sub-fields).
- **Clean Inline Sparklines**: Removed the unwanted background-sparkline fallback on wide `RAM` and `NVMe` rows in `CapFrameXOverlayForm.DrawVertRow` when `Background Sparklines` is unchecked.

### 🎮 Extended Radeon OSD Tags & Driver Feature Toggles
- **SAM, EnhSync & FRTC OSD Pills**: Added `SAM`, `EnhSync`, and `FRTC` pills to `GetActiveRadeonTags()`, controllable via the **SAM, EnhSync & FRTC Tags** toggle in OSD Studio and the overlay context menu.
- **Radeon Driver Features (KMD) Menu**: Added a **🎮 Radeon Driver Features (KMD)** submenu under **Tools & Drivers** to toggle `Anti-Lag`, `Radeon Chill`, `Radeon Boost`, `RIS`, `RSR`, `Enhanced Sync`, and `FRTC` directly in the AMD driver registry.

## [1.0.1] - 2026-11-17

### 🔧 Fan Calibration & Tachometer Stability
- **Extended 0 RPM Coast-Down Window (35s)**: Increased `FanCalibrationResult.RunCalibration` baseline `1% PWM` spin-down timeout from `10.0s` to `35.0s` (with `2s` keep-alive re-assertion), allowing high-inertia blowers coming off `100% PWM` or BIOS Auto to fully coast down before measuring Pass 1, Pass 2, and Pass 3. Added a settled-floor early-exit guard (`>= 15s`, `<= 15 RPM` delta across 8 samples) for hardware with non-zero minimum fan floors.
- **Adaptive Step Stabilization & Anti-Coasting Spin-Up Guard**: Added up to `+2.5s` adaptive dwell when RPM is still changing by `> 30 RPM` between ticks, trimmed-mean outlier rejection (`ComputeRobustStepRpm`), and a deceleration guard (`f1CoastingDown` / `f2CoastingDown`) so fans still coasting down at `20% PWM` are not falsely flagged as spinning up.
- **16-Bit EC Tachometer Latching & Rollover Spike Filter**: Added atomic MSB/LSB double-read latching in `HardwareBackend.GetStatus()` and updated `DecodeTachometerRpm()` to reject `<= 0` and `>= 7500` RPM rollover glitches (Test #5).

## [1.0.0] - 2026-11-16

### 🎨 UI Polish, Geometry Anti-Clipping & Documentation Visuals
- **Interactive First-Time Setup Assistant & Auto-Tune Guide**: Added `Ui/SetupAssistantDialog.cs` providing a clean, step-by-step 3-card onboarding flow for new users (Step 1: Motor Stall Calibration, Step 2: Auto-Tune Fan Curve Synthesis, Step 3: In-Game Monitoring). The dialog pops up automatically on initial run when uncalibrated, and can be accessed anytime via `Tools & Drivers ▾ → ✨ First-Time Setup Assistant & Auto-Tune Guide...` and the system tray context menu.
- **Auto-Tune Technical Documentation**: Added exhaustive documentation in `GUIDE.md` and `README.md` explaining the dynamic 5-point curve synthesis algorithm, why running Auto-Tune after 3-pass calibration unlocks true ~750 RPM (~22% PWM) whisper-quiet operation compared to the conservative 30% generic fallback, and how cross-coupled Sys Fan monitoring protects NVMe SSDs and high-power DDR5 modules.
- **Calibration Status Badge Isolation**: Resolved test suite mutation of `FanCurveProfile.LastCalibratedDate` in automated tests by wrapping mock evaluations in `try ... finally`, restoring the user's authentic calibration date and ensuring the badge renders in green: `✓ Calibrated 2026-11-16 (Min 22%/22%)` (Test #11).
- **Options Card Geometry Integration**: Moved `Sampling:` label and dropdown inside `optCard.Controls` at `Location = Point(660, 11)` and `Point(728, 7)`, cleanly occupying Row 1, Column 3 inside the Options panel and eliminating border straddling.
- **OSD Studio TrackBar Clearance & Button Anti-Clipping**: Added an 8px vertical air gap below the Win32 Opacity TrackBar (`Y=188` / `Y=184`), completely eliminating text clipping on the `Visible Telemetry Metrics` section header. Expanded Quick Snap button widths across the canvas (`Center-Bottom` to 104px, `Bottom-Right` to 98px) to eliminate label truncation under Segoe UI Bold (Test #44).
- **Header Button Click-Surface Protection**: Bounded `_hwSubLabel` and `_hwBadgeLabel` to `Width = 580` (`X=68..648`) with `AutoEllipsis = true` and called `BringToFront()` on `Tools & Drivers`, eliminating a 20px overlap that previously intercepted clicks intended for the menu button.
- **Documentation Image Population**: Enriched [README.md](README.md) and [GUIDE.md](GUIDE.md) with 5 real hardware screenshots from the TOPC TS-12C Mini PC (`ui-live-telemetry.png`, `ui-fan-curve-editor.png`, `ui-digital-foundry-perf.png`, `ui-osd-studio.png`, `ingame-osd-overlay.jpg`).

### 🛡️ Hardware Safety & Watchdog Supervisor Hardening
- **Display Timeout vs System Sleep Decoupling**: Fixed monitor screen timeout (`GUID_CONSOLE_DISPLAY_STATE` / `GUID_MONITOR_POWER_ON` reporting 0) being falsely treated as system suspend. Display timeout now cleanly tracks screen visibility while keeping background worker, watchdog, and fan curves actively cooling high-load tasks (e.g. overnight renders or local LLM inference). Corrected `GUID_LOW_POWER_EPOCH` polarity (1 = enter Connected Standby/sleep, 0 = exit/resume) according to Windows specifications (Test #69).
- **Single-Fan Auto-Adapt False-Positive Hardening**: Prevented transient write errors or legitimate 0-RPM zero-idle states from falsely disabling dual-fan control. Auto-adaptation now requires 5+ consecutive write failures at non-zero commanded duty ($\ge 30\%$).
- **Strict Write Verification for Error Clearance**: Guarded `ClearHardwareWriteError()` to ensure dummy no-op writes on single-fan machines (`SetFan(2)`) cannot clear real write failures on Fan 1.
- **Private Watchdog IPC ACL Hardening**: Purged `AuthenticatedUserSid` from private heartbeat and shutdown events, granting access strictly to Current User, Local System, and Administrators. Added poll wait sleep on `WaitAny` exceptions to prevent busy-looping.
- **Setup Assistant Production Decoupling & Localization**: Replaced test seam calls (`TestRunGuiCalibration`, `TestRunAutoTuneCurve`) with public production methods (`RunGuiFanCalibration`, `RunAutoTuneCurve`) and hooked all dialog strings to `Loc` localization engine.
- **Watchdog IPC AutoReset Crash Loop Elimination**: Granted `EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize` to the child `Waiter` process on AutoReset heartbeat events. Under Windows NT security, `WaitOne` on AutoReset handles performs an implicit reset requiring `Modify` rights; this eliminates `UnauthorizedAccessException` crash-loops on startup (Test #34, Test #68).
- **Auto-Rearming Watchdog Failsafe**: Watchdog failsafe now automatically clears `triggeredFailsafe` and re-arms when parent process heartbeats resume after a temporary hang or recovery.
- **IPC Access Control List (ACL) Hardening**: Dropped `S-1-15-2-1` (`ALL APPLICATION PACKAGES`) from IPC event DACLs, restricting event creation and signaling to the creator, Administrators, SYSTEM, and Authenticated Users (Test #31).
- **Fail-Closed SMBus Mutex**: `EcBusMutex.WaitSmbus` is now strictly fail-closed (`return false` when missing or uninitialized), aligning with the ecosystem fail-closed doctrine across EC, ISA, and PCI mutexes (Test #22).
- **Windows 11 Modern Standby (S0ix) Detection via `RegisterPowerSettingNotification`**: Windows 11 Modern Standby does not broadcast legacy `PBT_APMSUSPEND`. MiniFanControl now registers for `PBT_POWERSETTINGCHANGE` notifications targeting `GUID_CONSOLE_DISPLAY_STATE`, `GUID_MONITOR_POWER_ON`, and `GUID_LOW_POWER_EPOCH`. When the display turns off or low-power epoch begins, the app immediately pauses the hardware worker and cleanly shuts down the child watchdog supervisor, eliminating false 3.5s timeout hangs and repeating failsafe loops while the system sleeps (Test #69).
- **Default Silent Sleep Standby (Zero Spin-Up Burst)**: `SilentSleepStandby` now defaults to enabled (`true`). Upon entering sleep/standby, fans coast down silently to 0% duty with zero RPM kickstart bursts. Upon waking, hardware is re-enumerated, watchdog restarted, and custom curves smoothly resumed.
- **Eliminated Super I/O Runaway Fan Blasts in Fallback**: Corrected `ApplySafeSuperIoFallback()` to configure registers `0x15..0x17` to `0x00` (Extended PWM Mode) with safe duty `166` (quiet ~35% / 1100 RPM baseline), matching the factory BIOS. Setting Bit 7 = 1 (`0xFF` SmartGuardian Auto) previously caused the IT8613 hardware chip to command 100% full throttle whenever thermal diode clocks stopped during sleep.
- **Floating Sensor Diode Filter (`0x7F` = 127°C)**: In `CheckEmergencyThermalTripwire`, temperatures are bounded to `< 115°C` and explicitly reject `127°C` (`0x7F`), preventing floating uninitialized bus reads during sleep transitions from tripping the emergency 100% thermal override.
- **RAM Dual-Stick & Spread Delta Display Fix**: Integrated Win32 SMBIOS Type 17 (`DetectPhysicalMemoryStickCountFromSmbios`) to accurately detect populated DIMM sockets despite APU 16GB UMA VRAM carve-outs, added SMBus Channel B (`0x52/0x53`) probing, applied realistic stacked SODIMM gradient model (+4°C differential), and removed suppression guards in `CapFrameXOverlayForm.cs`.
- **Automated Test Suite Expansion**: Expanded to **69 passing automated self-tests** (100% pass rate), including cross-process child watchdog verification and sleep/suspend lifecycle testing.

### 🚀 Advanced Hardware Telemetry & Thermal Coupling
- **DDR Dual Data Rate (MT/s) Effective Speed**: Calculates and formats memory transfer rate at true Double Data Rate ($2\times \text{MCLK}$, e.g. `5600 MT/s` / `5600MHz` when base MCLK is 2800 MHz) across all UI surfaces (Vertical Overlay, Horizontal Bar, Main Form, Live Trend, and Tooltips).
- **Dual Stacked SODIMM Thermal Monitoring**: In Mini PCs, SODIMM slots are physically stacked one on top of the other, causing the upper module (DIMM 1) to run substantially hotter than the lower module (DIMM 0) due to radiant heat and restricted airflow ($47.8^\circ\text{C}$ vs $41.5^\circ\text{C}$ idle, up to $75.8^\circ\text{C}$ load). MiniFanControl monitors both physical sticks individually (`DdrTemp1` and `DdrTemp2`), reads JEDEC SPD5118 on-die thermal sensors via AMD SMBus (`0x50` and `0x51`), tracks individual session peaks, formats dual temps (`41°/48°C`), and cleanly accommodates single-stick systems without ghost modules (Test #63).
- **Proactive Fan 2 (Sys Fan) Coupling to Hotter Stacked Module**: Locked composite `DdrTemp` to $\max(\text{DIMM 0}, \text{DIMM 1})$ to ensure the secondary cooling blower ramps proactively and prevents the top SODIMM from overheating.
- **SMBus Cross-Tool Coexistence (`Global\Access_SMBUS.HTP.Method`)**: Synchronized all SPD5118 I2C/SMBus sensor queries with HWiNFO64, LibreHardwareMonitor, and third-party tools to guarantee zero bus contention or sensor corruption.
- **Motherboard / Chassis Ambient Sensor via IT8613 Channel 6 (`0x2E`)**: Mapped Super I/O Temperature 6 directly to Chassis Ambient (`41°C`–`49°C`), matching HWiNFO64 Temperature 6, with thermal envelope fallback and enabled by default on the overlay (`Chassis 41°C ▲49°`), Horizontal Bar (`CHAS 41°C`), and Live Trend (`● Chas 41°C`).
- **Super I/O IT8613 Inverted Hardware PWM Duty Fix & 3-Pass Calibration**: Corrected hardware-inverted PWM duty polarity (`255 - ((pct * 255) / 100)`) on IT8613 (`0xC7` in register `0x14` on TS-12C / KTHIN PRO). Eliminates fan speed runaways and enables the Sys Fan to drop down smoothly to custom low-noise curves (~20-25% PWM). Updated 3-pass calibration engine with independent spin-up delay compensation for both blowers.
- **Multi-NVMe Drive Telemetry & Real-Time Throughput**: Native Windows IOCTL monitoring supporting primary and secondary M.2 NVMe SSDs (`PhysicalDrive0` and `PhysicalDrive1`), real-time Read and Write throughput (MB/s), active I/O queue depth, remaining health %, and composite temperatures.
- **AMD FSR Upscaling Detection**: Real-time detection of AMD FSR from Radeon runtime/registry, adding a dedicated "FSR" pill in active Radeon features alongside FreeSync, Anti-Lag, AFMF 2, Boost, Chill, and RSR (Test #62).
- **AMD Zen 5 Silicon Junction Tctl/Tdie SMN Decoding**: Isolated AMD SMN register `0x00059800` (`THM_TCON_CUR_TMP`) on Zen 5 Strix Point (Ryzen AI 9 HX 370), matching HWiNFO64 Tctl/Tdie to within $\pm 0.1^\circ\text{C}$ (Test #60).
- **CapFrameX-Inspired Phased `RefreshThrottle` Bus Optimization**: Decoupled high-speed CPU/GPU polling from slow storage/memory buses, implementing jitter-immune grid timing with staggered phase offsets between dual SODIMM modules to eliminate SMBus collisions (Test #64).
- **Hook-Free Overlay Zero-Impact Repaint Dirty-Check & Ryzen Processor Tier Badges**: Eliminated redundant GDI+ bitmap rasterization and Win32 blits when telemetry is static, completely removing GPU compositor frame slips in high-FPS games with frame generation. Added authentic AMD Ryzen processor tier badges (`Ryzen AI 9`, `Ryzen 9`, `Ryzen 7`) with signature orange styling in the OSD (Test #65).
- **RAM & NVMe Dynamic Power Readouts Restored & Enhanced**: Restored real-time DDR5 memory power wattage (`~4.8W`) alongside dual SODIMM temperatures (`37°/43°C (Δ6°)`) and clocks (`5600MHz`) across the CapFrameX OSD (Vertical Stack and Horizontal Bar), Live Trend legend pills, and hover HUD. Added native `NvmePowerWatts` dynamic power envelope monitoring (~0.9W idle up to ~5.5W active per drive) coupled to real-time I/O throughput across tooltips, status sub-header, OSD, and Live Trend pills (Test #66).

## [1.0.0] - 2026-11-14

### Highlights & Post-Launch Architecture Hardening
- **PawnIO EV-Signed Driver Support (HVCI Breakthrough)**: Out-of-the-box hardware fan control with Windows 11 Memory Integrity (HVCI) and Core Isolation enabled, eliminating security downgrades.
- **Deep Silicon Telemetry (Tier 2 & Tier 3)**: Direct AMD SMU/SMN mailbox queries, live VRM electrical amperage draw (TDC/EDC), Infinity Fabric (FCLK) ratio tracking, and differential EC candidate scanning.
- **Ryzen AI NPU Utilization & "AI Workload" Profile**: Direct silicon NPU monitoring (`ADL_PMLOG_CLK_NPUCLK` & `ADL_PMLOG_NPU_BUSY_AVG`) with proactive feed-forward thermal coupling to prevent localized NPU and memory bus heat soak during local LLM inference.
- **CapFrameX OSD Overhaul & MSI Afterburner Theme**: Classic green/cyan (`#00FF00` / `#00FFFF`) contrast theme, Digital Foundry dynamic scale ladder, unified GPU row with dual peaks, neural glyph (`◈ NPU`), and anti-poisoning 0.1% lows.
- **Community Sharing & Diagnostic Exports**: 1-click GitHub profile submission, fan curve clipboard copy/import, live graph PNG snapshot export, and 10,000-record CSV telemetry logging.
- **Zero-Dependency Multi-Language Localization**: Standalone engine with built-in translations across 6 languages (EN, ZH, DE, ES, FR, JA), community drop-in JSON file support (`lang/`), and CI schema validation.
- **Live Trend & Live Performance Primary Tab Sequence**: Reordered tab strip so `● Live Trend` is the first default tab (index 0) and `● Live Perf` is second (index 1), with dedicated fan tuning tabs (`● APU Fan`, `● Sys Fan`) positioned after them.
- **Dynamic Manual Fan Control Visibility & Canvas Expansion**: The entire "Manual Fan Control" section (header, sliders, auto buttons, calibration badge) dynamically hides when viewing telemetry, performance, or OSD studio tabs, expanding graph canvas height from 368px to 490px (+122px of vertical chart room).
- **1440p / High-DPI CapFrameX OSD Auto-Fit & Taskbar Working Area Clamping**: Full resolution-aware vertical clamping and 2-stage intelligent compression (compressing live graphs and scaling row heights & fonts) so the overlay never spills off the bottom of 1440p / 1080p displays or under the Windows taskbar, strictly respecting `Screen.WorkingArea` (Test #58).
- **Protected Reference Curves (`🔒`) & `✨ Auto-Tune` Synthesizer**: Added 4 undeletable reference curves (`🔒 Zero-RPM Desktop Idle`, `🔒 Gaming Sweet-Spot (Acoustic Knee)`, `🔒 24/7 Server / Local LLM Sustained`, `🔒 Max Cooling / Benchmark`) with 1-click factory `Reset` protection, plus an `✨ Auto-Tune` engine that synthesizes a 5-point APU + Sys curve from calibrated anti-stall floors, idle baseline, and TDP power envelope (Test #59).
- **Automated Test Suite Expansion**: Expanded to **59 passing automated self-tests** (100% pass rate).

### 🔒 Security & Kernel Driver Hardening
- **PawnIO Priority 1 Driver Integration**: Added dynamic unmanaged interfacing with modern EV-signed `PawnIo64.dll` / `pawnio.sys`, enabling ring-0 port and physical memory I/O without triggering Windows 11 vulnerable driver blocks.
- **Windows 11 Memory Integrity Detection**: Added `NativeInpOut.IsHvciEnabled()` querying Windows DeviceGuard registry keys, paired with an interactive `"🛡️ Kernel Driver & HVCI Security Status..."` inspection dialog in the GUI.
- **Purged System32 Probing**: Eliminated all `System32` directory searches across all driver engines, preventing DLL hijacking, insecure search-order traversal, and silent engine substitution by untrusted binaries.
- **Eliminated Phantom MMIO Heap Snapshots**: Replaced disconnected `AllocHGlobal` buffers with live per-access direct physical memory IOCTLs with silicon read-back verification.
- **Strict 32-Bit Dword Port Whitelisting**: Restricted dword port reads/writes strictly to PCI configuration apertures `0xCF8` and `0xCFC` (Test #47), rejecting arbitrary or dangerous ports fail-closed.
- **Single-Instance Mutex Guard**: Implemented `Global\MiniFanControl_SingleInstance_GUI` to prevent concurrent instances from competing for EC registers or causing hardware race conditions (Test #49).
- **Safe Read-Only Telemetry Fallback**: Ensured that unverified platforms or blocked kernel drivers gracefully fallback to native AMD WHQL ADL2 and Windows storage telemetry without throwing unhandled exceptions (Test #48).

### 🔬 Deep Silicon Telemetry & Tier 2/3 Hardware Expansion
- **Tier 2 EC Differential Sensor Scanner**: Added `minifan-helper scan-ec --diff` comparing 256-byte EC hex snapshots between idle and load states to discover candidate VRM thermal diodes, ambient thermistors, PWM channels, and ADC voltage rails (Test #52).
- **Tier 3 AMD SMU / SMN Register Access**: Added direct silicon mailbox queries via `0xCF8`/`0xCFC` aperture to interrogate Zen 4 / Zen 5 Power Management Firmware (PMFW) registers (Test #54).
- **Live VRM Current Draw**: Real-time continuous Thermal Design Current (TDC Amps) and peak Electrical Design Current (EDC Amps) for core `VDDCR_VDD` and `VDDCR_SOC` power planes.
- **Infinity Fabric Clock (FCLK)**: Real-time fabric frequency tracking and 1:1 memory bus synchronization ratio detection.
- **Ryzen AI NPU Telemetry**: Integrated `ADL_PMLOG_CLK_NPUCLK` and `ADL_PMLOG_NPU_BUSY_AVG` for live NPU load % and compute frequency.
- **iGPU Hotspot Junction Monitoring**: Exposed `GpuHotspotTemp` alongside edge temperature with dual-peak HUD tracking (`▲41° ▲9%`).
- **Memory Clock & Power Limits**: Added live DDR5/LPDDR5X memory controller clock rates and APU package power envelope limits (`XW/XW`) (Test #51).

### 🤖 Cooling Profiles & Proactive NPU Thermal Coupling
- **4th Built-In Fan Profile ("AI Workload")**: Tuned aggressive curves for both APU (Fan 1) and secondary System (RAM/NVMe) fan to manage sustained local AI compute workloads (LM Studio, Ollama, ONNX DirectML).
- **Feed-Forward NPU Thermal Coupling**: Dynamically elevates effective thermal targets (`boost = Math.Round(NpuActivityPct * 0.35)`) when NPU activity reaches ≥10%, aggressively evacuating localized tile heat before overall silicon thermal saturation (Test #55).
- **Dynamic DeviceProfile Synthesis**: Added on-the-fly hardware profile generation from scan diffs, JSON schema serialization, persistence, and safe unlocked manual control (Test #53).

### 🎮 CapFrameX In-Game OSD & Visual Customization
- **MSI Afterburner Theme Palette**: Added classic green/cyan (`#00FF00` / `#00FFFF`) contrast theme alongside Dark, Light, and AMD Radeon themes (Test #41).
- **Digital Foundry Dual Graphs Overhaul**: Dynamic scale ladder (`60`, `120`, `180`, `240` FPS), anti-clipping baseline padding, and 4 selectable heights (Compact 75px, Medium 105px, Large 140px, XL 180px) (Test #45).
- **Dynamic Column Auto-Fit**: Automatic recalculation of column `MinWidth` based on formatted text length with graceful compaction fallback, resolving multi-monitor DPI scaling edge cases.
- **Unified GPU Row**: Merged iGPU temperature, hotspot, activity %, and clock into a single high-efficiency row with dual peak indicators.
- **Neural Accelerator Glyph**: Distinct `◈ NPU` indicator in CapFrameX OSD horizontal and vertical modes.
- **Anti-Poisoning Frametime Sanitization**: Filtered out loading screen pauses (>200ms) and RTSS startup artifacts (1 FPS) from corrupting active gameplay 0.1% low framerate metrics (Test #50).
- **Smart Game Title Detection**: Restricted title detection strictly to active 3D presentation processes, filtering out Steam/Epic launchers.
- **Granular Metric Toggles & Center Snaps**: Added independent toggles for NVMe, RAM, VRM, FCLK, and NPU, plus Center-Top and Center-Bottom snap coordinates (Tests #42, #43).
- **UI Layout Anti-Clipping**: Verified non-overlapping geometry across all telemetry boxes, OSD Studio controls, and Options cards (Test #44).

### 📋 Community Workflow, Curve Sharing & Diagnostic Exports
- **1-Click Hardware Profile Submission**: Automated GitHub Issue pre-population with 256-byte EC hex dump and hardware identification for instant community support.
- **Fan Curve Clipboard Sharing**: Right-click copy and import fan curve profiles directly to/from clipboard formatted for forum and Reddit sharing.
- **Live Graph Snapshot PNG Export**: Right-click "Save Live Graph Snapshot as PNG" capturing the 120-sample multi-series graph.
- **Session Telemetry CSV Export**: Full session logger capturing up to 10,000 samples with comprehensive hardware metrics.

### 🌐 Zero-Dependency Localization Engine (i18n)
- **Standalone `Loc` Engine**: Created `Core/Localization.cs` with built-in translation dictionaries for **6 languages**: English, Chinese Simplified (简体中文), German (Deutsch), Spanish (Español), French (Français), and Japanese (日本語).
- **Drop-In Community Translations**: Supports loading external translation files from `lang/{code}.json` without requiring recompilation. Added `lang/template.json` for community contributors.
- **CLI & CI Schema Validation**: Added `minifan-helper.exe validate-lang` command and integrated automated validation into the GitHub Actions CI pipeline to verify UTF-8 encoding and prevent missing translation keys (Test #56).
- **Runtime Switching**: Added `"🌐 Language / 语言 / Sprache"` menu to dynamically switch languages with configuration persistence.

### 📦 Packaging & CI/CD Pipeline
- **Segregated Release Packages**: Release pipeline generates distinct packages:
  - `MiniFanControl-v1.0.0-win64-Portable.zip` (standalone portable distribution for power users).
  - `MiniFanControl-Setup-v1.0.0.exe` (Inno Setup 6 installer with Task Scheduler logon autostart).
- **Automated Checksums**: Automated SHA256 checksum generation (`SHA256SUMS.txt`).
- **CI Inno Setup Compilation**: Automated Inno Setup installation and installer compilation on GitHub Actions Windows runners.

### 🧪 Automated Test Suite Expansion (Jump to 58 Tests)
Expanded from 40 to **58 passing automated self-tests** (100% pass rate):
- Test #41: OSD Driver Version & Afterburner Theme Integration
- Test #42: OSD Center-Top & Center-Bottom Snap Location Geometry
- Test #43: OSD Granular Metric Toggle & Horizontal Auto-Width Expansion
- Test #44: UI Layout Anti-Clipping Geometry
- Test #45: Digital Foundry Dual Graphs Dynamic Scale Ladder & Baseline Anti-Clipping
- Test #46: Driver Engine Isolation & WinRing0 MMIO Heap-Mapping Prevention
- Test #47: Strict Fail-Closed Dword Port Whitelist Aperture Enforcement (0xCF8/0xCFC)
- Test #48: Safe Read-Only Telemetry Fallback When Kernel Driver Is Blocked
- Test #49: Single-Instance Mutex & Clean Driver Shutdown Lifecycle
- Test #50: Anti-Poisoning Gameplay 0.1% Lows & Startup Hitch Rejection
- Test #51: AMD Silicon & ADL Extended Telemetry Validation
- Test #52: Tier 2 EC Motherboard Differential Sensor Scanner & Telemetry
- Test #53: Dynamic Custom DeviceProfile Generation, JSON Serialization & Fan Control Lifecycle
- Test #54: Tier 3: AMD SMU / SMN Telemetry & Deep Silicon VRM / FCLK Bus Synchronization
- Test #55: NPU Telemetry Utilization, AI Workload Curve & Proactive Thermal Coupling
- Test #56: Localization Engine (Loc), Built-In Multi-Language Dictionaries & Community JSON Schema
- Test #57: Live Performance Tab & Digital Foundry Frametime / Framerate Engine Integration
- Test #58: Live Trend First Tab Sequence, Dynamic Manual Fan Visibility & 1440p Taskbar Auto-Fit

---

## [1.0.0] - 2026-11-11

### Initial Public Production Release
- **Production-Grade Hardware & EC Fan Control**: Full support for AMD Ryzen Mini PCs (GMKtec NucBox K12, K8 Plus, K11, KTHIN Ryzen AI 9 HX 370 PRO, ADREAMER AriesBox 10, TOPC TS-12C) with ITE IT8613E and IT5570E controllers.
- **Fail-Closed Hardware Safety Architecture**:
  - Independent out-of-process crash supervisor daemon (`minifan-helper.exe watchdog`).
  - Strict driver port whitelist bounding ACPI EC (`0x62/0x66`) and Super I/O ports.
  - Multi-tool `Global\Access_EC` and `Global\Access_ISABUS.HTP` mutex synchronization for seamless coexistence with HWiNFO64 and CapFrameX.
  - Read-back verification on all hardware register writes and 90°C emergency thermal tripwire.
  - Motor stall dead-zone protection preventing sub-30% stalling on brushless DC fan motors.
- **CapFrameX & Digital Foundry Style In-Game OSD Studio**:
  - Highly configurable transparent, click-through gaming HUD with 4 scaling tiers (100% to 185%).
  - Dedicated Digital Foundry twin performance graphs (`FRAME-TIME (MS)` and `FRAME-RATE (FPS)`).
  - Multi-series `LIVE TREND` chart tracking CPU, iGPU, NVMe, Power, and Fan RPMs.
  - Endpoint line traces, anti-collided value badges, and color-coded header legends.
  - Interactive time-scrubbing crosshair and floating HUD inspection cards on mouse hover.
- **AMD Radeon Adrenalin Feature Integration**:
  - Compact pill tags for active Radeon gaming features: `[FreeSync]`, `[Anti-Lag]`, `[AFMF]`, `[Chill]`, `[Boost]`, `[RSR]`, `[RIS]`.
  - Dual-layer query engine checking kernel driver interfaces (`0000\KMD_*`, `UMD\DrvFrameGen*`) and Adrenalin live analytics database.
- **Storage & Memory Telemetry**: Native NVMe SMART temperature via Win32 IOCTL and DDR5 physical memory pressure tracking.
- **Automated 40-Point Hardware Safety & Regression Test Suite**: 100% passing automated test suite covering safety bounds, percentile math, and UI rendering.
- **Modern .NET Multi-Targeting**: Simultaneous build support for .NET Framework 4.8 and .NET 10.

---

## [0.9.0] - 2026-11-05 (Pre-Release Beta)

### Added
- **Out-of-Process Watchdog Supervisor**: Independent background daemon (`minifan-helper.exe watchdog`) monitoring main thread heartbeats with automated fail-closed recovery to BIOS Auto.
- **Fail-Closed Hardware Protections**: Refusal of hardware register writes on unverified/unknown systems (`unknown-safe` profile).
- **Driver Port Whitelist**: Strict user-mode bounding for ACPI EC (`0x62/0x66`) and Super I/O ports in `NativeInpOut.cs`.

### Changed
- **Multi-Tool Mutex Semantics**: Enhanced `Global\Access_EC` and `Global\Access_ISABUS.HTP` least-privilege synchronization for concurrent HWiNFO64 and CapFrameX operation.

---

## [0.8.0] - 2026-10-15 (Pre-Release Alpha)

### Added
- Initial modular release of MiniFanControl supporting GMKtec NucBox K12, K8 Plus, and ADREAMER AriesBox 10.
- 3-pass automated fan motor stall calibration engine.
- CapFrameX-style in-game OSD HUD with click-through transparency.
