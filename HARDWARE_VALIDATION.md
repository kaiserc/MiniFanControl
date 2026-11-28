# MiniFanControl — Physical Hardware Validation Matrix & Device Test Protocol

This specification establishes the physical hardware verification methodology, register mapping validation, and safety qualification matrix for all AMD Ryzen Mini PC hardware profiles supported by **MiniFanControl**.

---

## 🏛️ Supported Hardware Profiles Matrix

| Profile ID | Target Mini PC Models | Silicon / APU | Architecture & Controller | EC Registers | Super I/O Base & Polarity | Thermal Auto Failsafe | Validation Status |
|---|---|---|---|---|---|---|---|
| **`gmktec-k12`** | GMKtec NucBox K12 | AMD Ryzen 7 H 255 / 8845HS | Native ITE IT5570E EC (`0xFE0B0400`) | `0x31`–`0x38`, `0x70` | Native EC (`SuperIoBase = 0`) | Native BIOS EC Auto (`0x00`) | **Verified on Hardware** |
| **`gmktec-k8-k11`** | GMKtec NucBox K8 Plus / K11 | AMD Ryzen 7 8845HS / 8945HS | Native ITE IT5570E EC (`0xFE0B0400`) | `0x31`–`0x38`, `0x70` | Native EC (`SuperIoBase = 0`) | Native BIOS EC Auto (`0x00`) | **Verified on Hardware** |
| **`ryzen-hx370-ec`** | GMKtec EVO-X1, TOPC TS-12C | AMD Ryzen AI 9 HX 370 | Dual: ITE IT8613E Super I/O + IT5570E EC | `0x31`–`0x38`, `0x70` | Super I/O @ `0x0A30` (Inverted PWM `255 - (pct * 255) / 100`) | Safe Super I/O Fallback + EC Auto | **Verified on Hardware** |
| **`adreamer-ariesbox10`** | ADREAMER AriesBox 10 / PB07 | AMD Ryzen 7 7840HS | Dual: ITE IT8613E Super I/O + IT5570E EC | `0x31`–`0x38`, `0x70` | Super I/O @ `0x0A30` / `0x0290` (Inverted) | Safe Super I/O Fallback + EC Auto | **Verified on Hardware** |
| **`generic-it5570e`** | Generic Zen 4 / Zen 5 Mini PC | AMD Ryzen APU (Phoenix/Hawk/Strix) | Standard AMI Aptio V EC (`0xFE0B0400`) | `0x31`–`0x38`, `0x70` | Native EC (`SuperIoBase = 0`) | Native BIOS EC Auto (`0x00`) | **Manual Opt-In Profile** |
| **`unknown-safe`** | Unrecognized / New Motherboards | Any x86 APU / CPU | Read-Only Safe Fail-Closed Default | Read-Only (`0x70`) | Writes Disabled (`IsReadOnly = true`) | Non-Interference (Read-Only) | **Enforced Fail-Closed** |

---

## 🔬 Physical Hardware Bench Verification Protocol

MiniFanControl enforces four mandatory bench tests before certifying hardware support for any Mini PC motherboard:

### 1. Register Read-Back & Inverted PWM Polarity Verification
- **IT8613E Super I/O PWM Inversion**: Unlike standard desktop boards where 0 = Off and 255 = 100%, AMD Zen 4/5 Mini PCs using IT8613E Super I/O (such as TOPC TS-12C / GMKtec EVO-X1) utilize **inverted PWM polarity** (configured by register `0x14 = 0xC7`):
  - **Inverted Duty Mapping**: `duty = (pct <= 0) ? 255 : 255 - ((pct * 255) / 100)`
    - `0% PWM` translates to hardware duty `255` (0xFF, motor stopped / zero-RPM quiet idle).
    - `100% PWM` translates to hardware duty `0` (0x00, full-throttle emergency cooling).
    - `25% PWM` translates to hardware duty `191` (~1000 RPM quiet cooling curve floor).
  - **APU Fan (PWM 2 @ 0x6B & PWM 1 @ 0x63)**: 8-bit extended duty register and 7-bit manual control register (`0x16`).
  - **System Fan (PWM 3 @ 0x73)**: 8-bit extended duty register and 7-bit manual control register (`0x17`).
- **Physical Tachometer Signal**: Fan tachometer outputs (16-bit big-endian registers `0x35..0x36` and `0x37..0x38`) are validated with an oscilloscope / optical tachometer to ensure reported RPMs align within ±2% of physical impeller rotation.

### 2. Emergency Thermal Safety Tripwire (Silicon Runaway Prevention)
- **Trip Condition**: When APU temperature reaches or exceeds 90°C (`EMERGENCY_TEMP_TRIP_C`), the safety guard instantly overrides manual sliders and software fan curves to force 100% full-blast cooling.
- **Fail-Closed Retry Guarantee**: The controller verifies physical register read-back upon writing 100%. If the hardware write fails or is rejected, the UI **retains previous state** and independently retries 100% on every worker cycle until confirmed.
- **Hysteresis Recovery**: 100% cooling holds continuously until the silicon temperature drops below 75°C (`EMERGENCY_TEMP_RECOVER_C`).

### 3. Out-of-Process Watchdog Recovery Bench Test
- **Execution**: The GUI is terminated abruptly via ungraceful process kill (`taskkill /f /im MiniFanControl.exe`) while running at a forced custom fan speed.
- **Validation**:
  - The independent supervisor daemon (`minifan-helper.exe watchdog`) detects parent process termination within 250ms.
  - The daemon immediately invokes `RestoreAutoFailsafe()`.
  - On native EC profiles: restores `0x00` to registers `0x32`, `0x33`, `0x34` (confirmed via register read-back).
  - On Super I/O profiles: sets safe quiet fallback duty `166` (~35% PWM) to `0x63`, `0x6B`, and `0x73`, and restores native EC Auto.

### 4. Cross-Process Mutex Coexistence
- **Cooperating Tools**: **HWiNFO64**, **CapFrameX**, and **LibreHardwareMonitor**.
- **Shared Mutexes**:
  - `Global\Access_EC` (Embedded Controller register synchronization)
  - `Global\Access_ISABUS.HTP.Method` (Super I/O port I/O synchronization)
  - `Global\Access_PCI` (AMD SMN/SMU aperture 0xCF8/0xCFC synchronization)
  - `Global\Access_SMBUS.HTP.Method` (AMD FCH SMBus / DDR5 JEDEC SPD5118 hub synchronization)
- **Bench Test**: MiniFanControl runs continuously for 2 hours under high sampling rate (10 Hz / 100ms) with HWiNFO64 sensor polling active simultaneously. Zero mutex deadlocks, zero bus collision read corruptions, and 100% clean lock releases are required.

---

## 🛠️ Automated Hardware Matrix Verification

You can execute the hardware validation matrix directly from the command line:

```powershell
# Run the complete 6-profile hardware validation matrix
.\minifan-helper.exe hw-matrix

# Export supported hardware profiles & probed Super I/O ID prefixes (JSON or table)
.\minifan-helper.exe export-profiles --json

# Execute the 128-point automated hardware safety & regression test suite
.\minifan-helper.exe test

# Dump hardware registers & diagnostic report for new Mini PC qualification
.\minifan-helper.exe dump
```
