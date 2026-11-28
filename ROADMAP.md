# MiniFanControl Public Roadmap

This roadmap tracks completed milestones, active work for the upcoming release, and longer-term priorities for **MiniFanControl**. Hardware safety (fail-closed defaults, HVCI compatibility, and zero unverified writes) remains the non-negotiable baseline for every item below.

---

## Shipped Foundations (`v1.0.0` → `v1.4.0`)

- **Core Fan Control, True BIOS Restoration & Safety:** Dual independent APU + Sys fan curves, 3-pass stall-floor calibration with ≥88°C abort, 4°C asymmetric cooldown hysteresis, ≥90°C emergency tripwire, out-of-process watchdog supervisor, `Global\Access_EC` bus locking, and faithful **True BIOS `Reset Auto`** restoration (snapshotting untouched IT8613E SmartGuardian curves & EC initial modes before the first override).
- **HVCI-First Security & Verifiable Releases:** EV-signed PawnIO Priority-0 backend (`LpcIO.bin`, `LpcACPIEC.bin`, `AMDFamily17.bin`) with per-module SHA256 pinning, hard-gated legacy `WinRing0` (`CVE-2020-14979`), SLSA Build Provenance Attestations, on-demand privacy-first `UpdateChecker`, `winget` manifest automation, and `.NET 4.8` + `.NET 10.0-windows` CI builds with **128/128 automated self-tests**.
- **Modern Win11 UI, Deep Telemetry & Hook-Free OSD:** Custom `ModernSlider` with anti-stall floor zone, unified `ThemePalette` dialogs, 4 OSD Studio presets & bidirectional context-menu sync, direct AMD SMN `Tctl/Tdie`, WHQL ADL2 iGPU/PPT/NPU, NVIDIA NVML desktop GPU, dual DDR5 SODIMM temps, multi-NVMe SMART + I/O, kernel-filtered Windows DXGI/DxgKrnl ETW Display Latency, Digital Foundry stutter graphs, and 6 themes (including Xmas Edition).
- **Zero-UI Background Service, S0ix Recovery & Modular Architecture:** ~3.5 MB RAM `MiniFanControlService` with GUI ↔ Service handoff, shared `%ProgramData%` config hot-reload, Modern Standby (S0ix) wake self-healing, external `profiles/*.json` registry, `minifan-helper.exe status --stream` JSONL output, and cleanly extracted `CliCommandRunner`, `OsdStudioControl`, `TelemetryTileControl`, `Ddr5SpdSensor`, `BiosFanStateSnapshot`, and `DfGraphMath` modules.

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
