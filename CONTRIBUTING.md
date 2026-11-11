# Contributing to MiniFanControl

Thank you for your interest in contributing to **MiniFanControl**! 

MiniFanControl is a community-driven, hardware-safe Embedded Controller (EC) fan control and telemetry monitoring utility for AMD Ryzen Mini PCs. Because direct hardware manipulation carries physical safety implications, all contributions must uphold our **fail-closed hardware safety architecture**.

---

## 🛡️ Core Safety Principles

Any contribution that modifies hardware access, fan curves, or register logic must strictly follow these rules:

1. **Fail-Closed by Default**: If hardware detection, mutex acquisition, port validation, or register read-back fails, the application must abort writes and fall back to safe read-only monitoring or BIOS Auto.
2. **Never Blind Write**: Writes to unverified platforms, unknown motherboard DMI strings, or unwhitelisted ports are strictly rejected.
3. **Always Synchronize**: All ACPI EC (`0x62/0x66`, `0x6C/0x6E`) and Super I/O (`0x2E/0x4E`) access must be scoped inside `EcBusMutex` (`Global\Access_EC` and `Global\Access_ISABUS.HTP`).
4. **Zero Unsigned Third-Party DLLs**: Keep the executable self-contained with zero third-party dependencies outside standard Windows APIs, AMD ADL (`atiadlxx.dll`), and the cross-signed `inpoutx64.dll` port driver.

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
This invokes the native Windows C# compiler (`csc.exe`), compiles `MiniFanControl.exe` and `minifan-helper.exe`, rotates live running `.exe` instances atomically, and automatically executes the 40-point automated safety test suite.

### Option B: Modern .NET SDK Multi-Target Build (.NET 4.8 & .NET 10.0-windows)
If you have the .NET SDK installed:

```powershell
# Build both targets
dotnet build MiniFanControl.csproj -c Release

# Run automated tests
dotnet run --project MiniFanControl.csproj -c Release -- test
```

---

## 🧪 Automated Testing Requirements

Before opening a pull request, verify that all **40 automated regression and safety tests** pass cleanly:

```powershell
.\minifan-helper.exe test
```

If you add new hardware features or curve math, please add corresponding unit test assertions in [`Tests/SelfTestSuite.cs`](Tests/SelfTestSuite.cs).

### Hardware Validation Matrix Check
Verify that all 6 existing device profiles and inverted polarity configurations remain valid:

```powershell
.\minifan-helper.exe hw-matrix
```

---

## 📦 Packaging a Release

To test release archive staging and SHA-256 verification checksum generation:

```cmd
package-release.bat
```
Output archives and `SHA256SUMS.txt` are created in the `dist/` directory.
