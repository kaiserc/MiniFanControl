# Security Policy

## Supported Versions

Security updates and hardware-safety fixes are provided for the current release line of **MiniFanControl**:

| Version | Supported |
| :--- | :---: |
| `1.2.x` (Latest) | :white_check_mark: |
| `< 1.2.0` | :x: |

---

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

If you discover a security issue—especially one involving kernel port I/O (`inpoutx64.dll`), privilege boundaries, EC/Super I/O register write safety, IPC/watchdog handling, or installer/binary integrity—please report it privately:

1. Use **GitHub Private Vulnerability Reporting** via the **Security** tab of this repository:  
   `https://github.com/kaiserc/MiniFanControl/security/advisories/new`
2. Include as much detail as possible:
   - MiniFanControl version (`VERSION` / release tag) and Windows version
   - Hardware model / DMI platform (if hardware-specific)
   - Steps to reproduce or proof-of-concept
   - Potential impact (e.g., unintended register write, privilege escalation, thermal/fan stall bypass, or driver/service abuse)

You should receive an acknowledgment within **72 hours**, and we will work with you to validate and address confirmed issues before public disclosure.

---

## Security & Hardware-Safety Architecture

MiniFanControl operates close to hardware and follows a **fail-closed, least-privilege** design:

- **Fail-Closed Hardware Writes:** If hardware detection, `EcBusMutex` acquisition (`Global\Access_EC`, `Global\Access_ISABUS.HTP.Method`, `Global\Access_SMBUS.HTP.Method`), port validation, or register read-back fails, writes are aborted and the system falls back to safe read-only monitoring or BIOS Auto.
- **Whitelisted Platforms & Ports:** Writes to unverified platforms, unknown/blocklisted motherboard DMI strings, or unwhitelisted I/O ports are rejected directly inside `NativeInpOut.IsPortWhitelisted()`. Custom profiles (`custom-profile.json`) are bound to the source machine's DMI fingerprint and default fail-closed to read-only if unbound or incomplete.
- **EV-Signed PawnIO Priority-0 & SHA256-Pinned Fallbacks:** Priority 0 uses official HVCI-compatible **PawnIO** (`PawnIOLib.dll`, preferring `%ProgramFiles%\PawnIO` and enforcing Windows Authenticode verification via `WinVerifyTrust` under a `FileShare.Read` lock) with signed `LpcIO` / `LpcACPIEC` modules. When PawnIO is unavailable, `NativeInpOut.PreloadDriverDll()` verifies the SHA256 digest of `inpoutx64.dll` (or optional `WinRing0x64.dll`) while holding a `FileShare.Read` handle to prevent DLL planting / TOCTOU swaps. On Windows 11 systems with Memory Integrity (HVCI) enabled and no PawnIO installed, MiniFanControl automatically operates in safe read-only telemetry mode via standard Windows APIs, native DXGI/DxgKrnl ETW, and WHQL AMD ADL (`atiadlxx.dll`) with all hardware writes locked. Release checksums are published in `SHA256SUMS.txt`.
- **Out-of-Process Watchdog:** `WatchdogSupervisor` monitors heartbeat health and restores BIOS fan control if the main UI/controller exits unexpectedly.
