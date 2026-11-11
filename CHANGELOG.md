# Changelog

All notable changes to **MiniFanControl** are documented in this file.
The project adheres to [Semantic Versioning](https://semver.org/).
For in-depth architectural deep dives, see [RELEASE_NOTES.md](RELEASE_NOTES.md).

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
