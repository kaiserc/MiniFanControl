# Contributing to MiniFanControl

Thank you for your interest in contributing to **MiniFanControl**! 

MiniFanControl is a community-driven, hardware-safe Embedded Controller (EC) fan control and telemetry monitoring utility for AMD Ryzen Mini PCs. Because direct hardware manipulation carries physical safety implications, all contributions must uphold our **fail-closed hardware safety architecture**.

---

## 🛡️ Core Safety Principles

Any contribution that modifies hardware access, fan curves, or register logic must strictly follow these rules:

1. **Fail-Closed by Default**: If hardware detection, mutex acquisition, port validation, or register read-back fails, the application must abort writes and fall back to safe read-only monitoring or BIOS Auto.
2. **Never Blind Write**: Writes to unverified platforms, unknown motherboard DMI strings, or unwhitelisted ports are strictly rejected.
3. **Always Synchronize**: All ACPI EC (`0x62/0x66`, `0x6C/0x6E`), Super I/O (`0x2E/0x4E`), and SMBus access must be scoped inside `EcBusMutex` (`Global\Access_EC`, `Global\Access_ISABUS.HTP.Method`, and `Global\Access_SMBUS.HTP.Method`).
4. **Zero Unsigned Third-Party DLLs**: Keep the executable self-contained with zero third-party dependencies outside standard Windows APIs, WHQL AMD ADL (`atiadlxx.dll`) / NVIDIA NVML (`nvml.dll`), and EV-signed **PawnIO** (`PawnIOLib.dll` + SHA256-pinned bundled `.bin` modules).

---

## ⚠️ C# 5 Language Constraint (Important!)

`build-windows.bat` compiles `MiniFanControl.exe` and `minifan-helper.exe` using Windows' inbox `.NET Framework 4.8` compiler (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) with `/warn:4`. That compiler supports **C# 5 only**.

Even though `MiniFanControl.csproj` also builds on `.NET 10` (`LangVersion = latest`), **all shared `.cs` files must remain strictly C# 5 compatible** unless guarded inside `#if NET8_0_OR_GREATER`:

- **Do NOT use:** string interpolation (`$"..."`), null-conditional (`?.`), null-coalescing assignment (`??=`), expression-bodied members (`=>`), `nameof(...)`, `out var`, target-typed `new()`, or switch expressions.
- **Always use:** `string.Format(...)`, explicit null checks, classic `out` declarations, and explicit types.
- **Verify locally:** Always run `build-windows.bat` before opening a PR!

---

## 🤝 How to Submit a New Mini PC Hardware Profile

We actively welcome community hardware qualifications for new AMD Zen 4 / Zen 5 Mini PC models (e.g. GMKtec, Minisforum, Beelink, ADREAMER, Morefine, KTHIN):

### Step 1: Capture the 256-Byte EC Register Map
Open PowerShell or Command Prompt as Administrator and run:

```powershell
.\minifan-helper.exe dump > my_minipc_dump.txt
```

### Step 2: Observe Active Fan & Thermal Registers
Run the live register watcher for 15 seconds while toggling performance modes in BIOS or running a brief CPU benchmark (e.g., CPU-Z or Cinebench):

```powershell
.\minifan-helper.exe watch
```

### Step 3: Open a Profile Request Issue
Submit your findings via GitHub using our structured [**Unsupported Hardware Profile Request**](https://github.com/kaiserc/MiniFanControl/issues/new?template=unsupported_hardware.md) template. Attach `my_minipc_dump.txt` and note:
- System manufacturer & marketing model name
- Processor (e.g., AMD Ryzen 7 8845HS, Ryzen AI 9 HX 370)
- Number of physical fans (1 vs 2)
- Any active control offsets observed during `watch`

---

## 🛠️ Local Development & Build Workflow

You can build and test MiniFanControl using either the native Windows compiler or the modern .NET SDK:

### Option A: Zero-Tooling Windows Build (.NET 4.8 Framework)
No Visual Studio installation required! Run the included batch script:

```cmd
build-windows.bat
```
This invokes the native Windows C# compiler (`csc.exe` with `/optimize+ /debug- /warn:4`), compiles a lean test-free `MiniFanControl.exe` and a test-enabled `minifan-helper.exe`, rotates live running `.exe` instances atomically, and automatically executes the full automated safety test suite.

### Option B: Modern .NET SDK Multi-Target Build (.NET 4.8 & .NET 10.0-windows)
If you have the .NET SDK installed:

```powershell
# Build both targets (shipping binary excludes Tests/)
dotnet build MiniFanControl.csproj -c Release

# Run the dedicated test assembly
dotnet run --project Tests\MiniFanControl.Tests.csproj -c Release -f net10.0-windows
```

---

## 🧪 Automated Testing Requirements

Before opening a pull request, verify that all **128 automated regression and safety tests** pass cleanly:

```powershell
.\minifan-helper.exe test
```

If you add new hardware features or curve math, please add corresponding unit test assertions in [`Tests/SelfTestSuite.Roadmap.cs`](Tests/SelfTestSuite.Roadmap.cs).

### Single-Source Version Bumps
Update only the root `VERSION` file and run the sync script before tagging:

```powershell
.\scripts\sync-version.ps1        # Sync VERSION -> app.manifest, installer.iss, csproj, MainForm.cs
.\scripts\sync-version.ps1 -Check # Verify zero version drift
```

### Hardware Validation Matrix Check
Verify that all 6 existing device profiles and inverted polarity configurations remain valid:

```powershell
# Validate hardware profiles and export supported EC/Super I/O IDs
.\minifan-helper.exe hw-matrix
.\minifan-helper.exe export-profiles --json
```

---

## 📦 Packaging a Release

To test release archive staging and SHA-256 verification checksum generation:

```cmd
package-release.bat
```
Output archives and `SHA256SUMS.txt` are created in the `dist/` directory.
