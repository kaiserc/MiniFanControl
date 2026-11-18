# Security Policy

## Supported Versions

Security updates and hardware-safety fixes are provided for the current release line of **MiniFanControl**:

| Version | Supported |
| :--- | :---: |
| `1.0.x` (Latest) | :white_check_mark: |
| `< 1.0.0` | :x: |

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
- **Whitelisted Platforms & Ports:** Writes to unverified platforms, unknown motherboard DMI strings, or unwhitelisted I/O ports are rejected.
- **No Unsigned Third-Party Drivers:** Releases rely only on standard Windows APIs, AMD ADL (`atiadlxx.dll`), and the cross-signed `inpoutx64.dll` port driver, with release checksums published in `SHA256SUMS.txt`.
- **Out-of-Process Watchdog:** `WatchdogSupervisor` monitors heartbeat health and restores BIOS fan control if the main UI/controller exits unexpectedly.
