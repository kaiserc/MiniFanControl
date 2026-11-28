# MiniFanControl Public Roadmap

This roadmap tracks completed milestones, active work for the upcoming release, and longer-term priorities for **MiniFanControl**. Hardware safety (fail-closed defaults, HVCI compatibility, and zero unverified writes) remains the non-negotiable baseline for every item below.

---

## Shipped Foundations (`v1.0.0` → `v1.3.1`)

- **Core Fan Control & Safety:** Dual independent APU + Sys fan curves, 3-pass stall-floor calibration with ≥88°C abort, 4°C asymmetric cooldown hysteresis, ≥90°C emergency tripwire, out-of-process watchdog supervisor, and `Global\Access_EC` bus locking.
- **HVCI-First Security & Verifiable Releases:** EV-signed PawnIO Priority-0 backend (`LpcIO.bin`, `LpcACPIEC.bin`, `AMDFamily17.bin`) with per-module SHA256 pinning, hard-gated legacy `WinRing0` (`CVE-2020-14979`), SLSA Build Provenance Attestations, and `.NET 4.8` + `.NET 10.0-windows` CI builds.
- **Deep Telemetry & Hook-Free OSD:** Direct AMD SMN `Tctl/Tdie`, WHQL ADL2 iGPU/PPT/NPU, NVIDIA NVML desktop GPU, dual DDR5 SODIMM temps, multi-NVMe SMART + I/O, native Windows DXGI/DxgKrnl ETW Display Latency, Digital Foundry stutter graphs, and 6 themes (including Xmas Edition).
- **Zero-UI Background Service & S0ix Recovery:** ~3.5 MB RAM `MiniFanControlService` with GUI ↔ Service handoff, shared `%ProgramData%` config hot-reload, Modern Standby (S0ix) wake self-healing, external `profiles/*.json` registry, and `minifan-helper.exe status --stream` JSONL output.

---

## In Progress — Next Release (`v1.4.0`)

- [x] **Modernized Win11 Visual Polish:** Custom `ModernSlider` with visible Anti-Stall Floor dead-zone rail, dark-mode native scrollbars/checkboxes/comboboxes, unified `ThemePalette` dialogs (`FanCalibrationDialog`, `KernelDriverDialog`, `ThemedInfoDialog`), and thermal hero temperature ramps.
- [x] **OSD Studio Workflow & Bidirectional Sync:** 4 one-click OSD presets (`Minimal FPS`, `Compact Gaming`, `Digital Foundry`, `Full Audit`), 5 category filter pills, live-synced OSD right-click menu, and Live Trend `All`/`Temps`/`Cool` + Shift-click Solo filtering + Fan Curve `Ctrl+Z` undo.
- [x] **In-Game OSD Readability Polish:** Single-line `TREND` header with clamped 8-badge right-edge stack, stronger dark graph backing over bright game HUDs, cleaner DF corner inset `0` tick clearance, flush `NPU` alignment, alternating row striping, and padded Radeon/Ryzen pills.
- [x] **True BIOS Fan Curve Restoration (`Reset Auto`):** Snapshots untouched IT8613E SmartGuardian BIOS fan curves and initial EC BIOS modes before the first override, restoring them faithfully on `Reset Auto` and entering a true hands-off **`✓ BIOS Auto`** state (with ≥90°C emergency tripwire still armed).
- [x] **Privacy-First Update Checker:** On-demand **`↻ Check for Updates...`** in `Tools & Drivers ▾` querying GitHub Releases with strict SemVer parsing and official repository URL sanitization (zero background phone-home).
- [x] **Winget Packaging Automation:** Repeatable `scripts/generate-winget-manifest.ps1` generator for `kaiserc.MiniFanControl`.
- [x] **127 Automated Self-Tests:** Expanded catalog including hidden-OSD lock guard, ETW kernel event-ID filter ABI, update checker verification, and true BIOS `Reset Auto` restoration.

---

## Near-Term (`v1.4.x` – `v1.5.0`)

- [ ] **Official `winget` Package (`winget install kaiserc.MiniFanControl`):** Submit initial manifests to `microsoft/winget-pkgs` and keep releases synced via `scripts/generate-winget-manifest.ps1`.
- [ ] **OSS Authenticode Code Signing:** Apply for **SignPath Foundation** open-source code signing (or Azure Trusted Signing) for `MiniFanControl-Setup-*.exe` and portable binaries to eliminate Windows SmartScreen prompts alongside existing SLSA provenance attestations.
- [ ] **Expanded Verified Ryzen Mini PC Profiles:** Incorporate community-submitted `ec-probe-report.txt` / JSON profiles for additional GMKtec, Minisforum, Beelink, Aoostar, and TOPC Zen 4 / Zen 5 systems in `profiles/*.json`.
- [ ] **CI Coverage Reporting (`.NET 10`):** Add lightweight coverage reporting on `Tests/MiniFanControl.Tests.csproj` without dropping the zero-dependency `.NET 4.8` `csc.exe` build path.

---

## Longer-Term Exploration

- [ ] **Additional Super I/O Families:** Verified read/write profiles for Nuvoton `NCT67xx` and `IT8689E` desktop/Mini-ITX boards where safe dual-channel tachometer latching can be confirmed.
- [ ] **Per-Game OSD Presets:** Automatically switch OSD layout presets (e.g., `Minimal FPS` vs. `Digital Foundry`) alongside the existing workload profile switcher.
- [ ] **Community Localizations:** Expand `lang/*.json` translations verified via `minifan-helper.exe validate-lang`.

---

## Want to Help?

- **Have an unsupported Ryzen Mini PC?** Run **`Tools & Drivers ▾ → Submit Hardware Profile to GitHub (1-Click)`** (or `minifan-helper.exe submit-report`) and open a [Hardware Profile Request](https://github.com/kaiserc/MiniFanControl/issues/new/choose).
- **Found a bug or have a feature idea?** See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).
