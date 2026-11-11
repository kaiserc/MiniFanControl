# Changelog

All notable changes to **MiniFanControl** are documented in this file.
The project adheres to [Semantic Versioning](https://semver.org/).
For in-depth architectural deep dives, see [RELEASE_NOTES.md](RELEASE_NOTES.md).

## [2.7.0] - 2026-11-11

### Added
- **Dedicated FPS & Frametime Live Graph**: Real-time visualization with live FPS curve, frametime (ms) curve, average FPS dashed reference line, and 0.1% low percentile line.
- **Combined OSD Live Trend**: Multi-series charting combining thermal inputs (CPU/iGPU/Power/Fans) with gaming framerate trends.
- **Configurable OSD Graph Modes**: 4 graph display options (`Both`, `Combined Graph`, `Mini Sparklines`, `Text Only`) with 4 scaling tiers (`100% Std`, `125% Lg`, `150% XL`, `185% 4K`).
- **Native NVMe SSD & DDR5 Memory Telemetry**: Storage SMART composite temperature (`IOCTL_STORAGE_QUERY_PROPERTY`) and memory pressure tracking (`GlobalMemoryStatusEx`) with zero raw PCI contention.
- **Automated 40-Point Test Suite**: Rigorous offline verification covering motor stall bounds, inverted Super I/O PWM polarity, IPC watchdog heartbeat, and fail-closed security.
- **Hardware Profile Request Template**: Structured GitHub issue template for reporting unlisted Ryzen Mini PCs with `minifan-helper.exe dump`.

### Changed
- **OSD Row Layout Cleanup**: 
  - `0.1% Low` now displays strictly the percentile low metric without misplaced trailing averages.
  - Session Average FPS moved to the `FPS` row alongside frametime.
  - Average FPS prioritized from active session telemetry and mathematically clamped ($\text{MinFps} \le \text{AvgFps} \le \text{MaxFps}$).
- **Repository Hygiene**: Modularized C# domain structure with `.csproj` multi-targeting (.NET 4.8 & .NET 10), archived extraneous root assets into `assets/` and `docs/`.

### Fixed
- **DWM 100% Opacity Rendering**: Explicit `SetLayeredWindowAttributes` call prevents transparent HUD invisibility under Windows DWM.
- **Ampersand Mnemonic Bug**: Fixed chart title rendering by disabling Windows Forms mnemonic acceleration (`TextFormatFlags.NoPrefix`).
- **Background UWP Noise**: Filtered out Windows system apps (`SecHealthUI.exe`, `dwm.exe`, `explorer.exe`) from RTSS shared memory reader.

---

## [2.6.0] - 2026-11-05

### Added
- **Out-of-Process Watchdog Supervisor**: Independent background daemon (`minifan-helper.exe watchdog`) monitoring main thread heartbeats with automated fail-closed recovery to BIOS Auto.
- **Fail-Closed Hardware Protections**: Refusal of hardware register writes on unverified/unknown systems (`unknown-safe` profile).
- **Driver Port Whitelist**: Strict user-mode bounding for ACPI EC (`0x62/0x66`) and Super I/O ports in `NativeInpOut.cs`.

### Changed
- **Multi-Tool Mutex Semantics**: Enhanced `Global\Access_EC` and `Global\Access_ISABUS.HTP` least-privilege synchronization for concurrent HWiNFO64 and CapFrameX operation.

---

## [2.0.0] - 2026-10-15

### Added
- Initial modular release of MiniFanControl supporting GMKtec NucBox K12, K8 Plus, and ADREAMER AriesBox 10.
- 3-pass automated fan motor stall calibration engine.
- CapFrameX-style in-game OSD HUD with click-through transparency.
