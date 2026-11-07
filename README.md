# MiniFanControl — Native EC Fan Control & Thermal Monitor for AMD Ryzen Mini PCs

<p align="center">
  <img src="assets/banner.png" alt="MiniFanControl Banner" width="100%" />
</p>

[![Platform: Windows 10 / 11](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(64--bit)-blue.svg)](#-quick-start-windows)
[![Language: C# / .NET](https://img.shields.io/badge/Language-C%23%20%2F%20.NET%20Framework%204.8-green.svg)](#-quick-start-windows)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-Zero%20(Native%20Windows)-brightgreen.svg)](#-quick-start-windows)
[![Mutex: Global\Access_EC](https://img.shields.io/badge/EC%20Mutex-Global%5CAccess__EC%20Synchronized-purple.svg)](#-capframex--hwinfo-coexistence-globalaccess_ec)
[![Themes: Dark / Light / Radeon](https://img.shields.io/badge/Themes-Dark%20%7C%20Light%20%7C%20AMD%20Radeon-crimson.svg)](#-3-high-contrast-themes)

**MiniFanControl** is a lightweight, zero-dependency **Windows (C#/.NET)** Embedded Controller (EC) fan control, custom fan curve, and hardware telemetry monitoring utility built specifically for **AMD Ryzen Mini PCs**—starting with the **GMKtec NucBox K12 (Ryzen 7 H 255 / 8845HS / Radeon 780M)** and expanding to other systems using the **ITE IT5570E** EC (`0xFE0B0400`) such as the **GMKtec K8 Plus / K11 / EVO-X1** and **Ryzen AI 9 HX 370 / 890M** platforms.

> 📖 **Need a complete step-by-step walkthrough or slide deck?**  
> - [**Complete User & Hardware Tuning Guide (GUIDE.md)**](GUIDE.md): In-depth instructions for fan calibration, motor stall prevention, OSD Studio customization, multi-monitor gaming, and EC register probing.  
> - [**Visual Overview Slide Deck (MiniFanControl-Overview.pptx)**](MiniFanControl-Overview.pptx): 7-slide visual presentation illustrating the architecture, dual-fan curves, OSD Studio, and `Global\Access_EC` mutex.

---

## 💡 Why MiniFanControl?

Standard desktop fan utilities (such as [Fan Control](https://getfancontrol.com/) or motherboard vendor tools) look for desktop Super I/O chips over standard LPC I/O ports. AMD Ryzen Mini PCs are engineered like modern gaming laptops: they rely on a proprietary **ITE IT5570E Embedded Controller (EC)** mapped at `0xFE0B0400`. 

Because desktop utilities cannot detect this controller, Mini PC users were historically limited to fixed BIOS profiles or high noise levels. **MiniFanControl** communicates directly with the Embedded Controller registers via kernel port I/O, providing independent dual-fan curves, live sparkline telemetry, an interactive CapFrameX-style in-game OSD, and taskbar temperature badges.

---

## ✨ Key Features

### 🖥️ Redesigned Modern Interface (680×860)
- **Dedicated Hardware Badge Header**: Displays detected product model and APU (e.g., `NucBox K12 • Ryzen 7 8845HS`), active EC profile map, system mutex state, animated glowing fan activity badge, quick **Tools & Drivers ▾** menu, and theme switcher.
- **5 Wide Telemetry Boxes with Real-Time Sparklines**:
  - **CPU Temp**: Reads core temperature directly from EC `0x70` with session peak tracking.
  - **iGPU Temp**: Reads integrated Radeon graphics telemetry via AMD Display Library (`atiadlxx.dll`).
  - **APU Power (W)**: Total package power (PPT) with detailed core, GFX, and SoC power breakdown.
  - **APU Fan RPM**: Live tachometer speed of the primary APU cooler.
  - **Sys Fan RPM**: Live tachometer speed of the secondary DDR5 RAM and NVMe SSD chassis fan.
  - **Live Sparkline Waveforms**: 118×21 px real-time mini charts inside each box. Click any box to spotlight that metric in the live telemetry graph!

### 📈 Multi-Series Live Telemetry Graph & Custom Curves
- **Combined Live Trend**: Visualizes the last ~180 seconds of CPU temp, iGPU temp, APU power, APU fan RPM, and Sys fan RPM on dual synchronized vertical axes.
- **Interactive Spotlighting**: Click any legend pill or status box to highlight that metric while dimming others.
- **Independent Dual-Fan Curves (`APU Fan` + `Sys Fan`)**: Assign built-in modes (`Balanced`, `Performance`, `Silent`) or custom user-defined curves to each fan independently.
- **Hardware-Safe 2°C Hysteresis**: Cooldown hysteresis buffer eliminates fan speed oscillation during bursty workloads.

### 🎮 CapFrameX-Style OSD Overlay & Dedicated OSD Studio
- **On-Screen Display HUD**: Floating, translucent, always-on-top overlay for gaming and benchmarking.
- **Dedicated OSD Studio Tab**: Configure all overlay parameters directly inside the main window:
  - **4 Live Graph Display Modes**: `✦ Both (Lines + Graph)`, `📈 Combined Graph`, `〰 Mini Sparklines`, or `Text Only`.
  - **4 Scale Tiers**: `100% Std`, `125% Lg`, `150% XL` (ideal for 1440p), and `185% 4K` (large bold readouts for 4K TV couch gaming).
  - **Layout Directions**: `▬ Horiz Bar` or `▮ Vert Stack`.
  - **Adjustable Graph Heights**: 45px Compact, 65px Medium, 95px Large, 130px Extra Large.
  - **Quick Corner Snaps**: One-click snap to `↖ TL`, `↗ TR`, `↙ BL`, or `↘ BR`.
  - **Click-Through Lock (`WS_EX_TRANSPARENT`)**: Mouse clicks pass straight through to full-screen games without stealing focus.
  - **Low-Impact FPS & iGPU Load**: Passively reads in-game FPS from RTSS/CapFrameX shared memory with zero DLL injection and zero anti-cheat risk; automatically displays live Radeon 3D engine load % and clock MHz directly from AMD ADL2.
- **Global Keyboard Hotkeys**: Toggle the overlay (`Ctrl+Alt+F9`) or switch thermal modes (`Ctrl+Alt+F10..F12`) anytime without leaving full-screen games.

### 🛡️ Motor Stall Protection & Automated 3-Pass Calibration
- **Physical Stall Dead-Zone Prevention**: Brushless blower fans stall if driven below their physical spin-up threshold. MiniFanControl's automated calibration engine measures each motor's exact minimum spin-up % (`ApuMinSpinPct` / `SysMinSpinPct`) and enforces a safe minimum PWM floor outside 0% idle.
- **1-Fan vs 2-Fan Auto Detection**: Detects single-fan vs dual-fan Mini PCs during calibration, automatically tailoring the UI and OSD layout.
- **Fail-Safe Exit Handler**: Automatically restores fan control registers to native BIOS EC Auto (`0x00`) on process exit or crash.

### 🔄 Multi-Tool Coexistence (`Global\Access_EC` Mutex)
- Acquires the system-wide `Global\Access_EC` Windows Mutex before every register read or write.
- Allows **MiniFanControl**, **HWiNFO64**, **CapFrameX**, and **LibreHardwareMonitor** to run simultaneously with zero EC bus collisions or register corruption.

### 🎨 3 High-Contrast Themes
- **Dark Theme**: High-contrast slate and midnight blue for low-light environments.
- **Light Theme**: Crisp alabaster with bold slate accents.
- **AMD Radeon Theme**: Ruby red / crimson theme inspired by AMD Adrenalin software.
- The OSD overlay can match the active application theme or use classic CapFrameX Cyan/Amber.

---

## 🚀 Quick Start (Windows)

1. **Prerequisite**: Ensure `inpoutx64.dll` is located in the application folder (next to `MiniFanControl.exe`).
2. **First-Time Launch**:
   - Right-click `MiniFanControl.exe` and select **Run as Administrator** once so `inpoutx64.dll` can register its kernel service (`inpoutx64.sys`).
   - Subsequent runs can be launched normally without elevation.
   - *Windows 11 Note*: If Windows blocks the driver, temporarily toggle **Memory Integrity** under *Windows Security → Device Security → Core Isolation*.
3. **Run the App**: Double-click `MiniFanControl.exe` (or run `run-windows.bat`).
4. **Compile from Source (Zero Tools Required)**:
   - Double-click `build-windows.bat`. Compiles `MiniFanControl.exe` and `minifan-helper.exe` using the native `.NET` compiler included with Windows 10/11.

---

## 🛠️ CLI Usage (`minifan-helper.exe`)

The included command-line tool provides full scripting, automation, and diagnostic capabilities:

```powershell
# Read live temperatures, fan speeds, and mode as JSON
.\minifan-helper.exe status

# Set EC performance mode (0 = Balanced, 1 = Performance, 2 = Silent)
.\minifan-helper.exe mode 0

# Set manual fan PWM % (1-100 = Manual %, 0 = Return to Auto)
.\minifan-helper.exe fan1 60
.\minifan-helper.exe fan2 45

# Reset both fans and mode to native BIOS automatic control
.\minifan-helper.exe auto

# Run 3-pass automated fan calibration (measures Min Spin %, Max RPM, and detects 1 vs 2 fans)
.\minifan-helper.exe calibrate

# Export 256-byte EC register hex dump & system diagnostic info (for new Mini PCs like HX 370)
.\minifan-helper.exe dump

# Live-watch all 256 EC registers for 15 seconds to identify active control registers
.\minifan-helper.exe watch

# Execute the 14-point automated hardware safety & curve test suite
.\minifan-helper.exe test
```

---

## 📊 Embedded Controller (EC) Register Map (`ITE IT5570E @ 0xFE0B0400`)

| Offset | Access | Description | Values / Formula |
|---|---|---|---|
| `0x31` | Read | Active Performance Mode | `0` = Balanced, `1` = Performance, `2` = Silent |
| `0x32` | Write | Set Performance Mode | `0x80` = Balanced, `0x81` = Performance, `0x82` = Silent |
| `0x33` | R/W | **APU Fan (`Fan 1`)** Control | `0x00` = Auto, `0x80 \| pct` = Manual (`1–100%`) |
| `0x34` | R/W | **Sys Fan (`Fan 2`, RAM/NVMe)** Control | `0x00` = Auto, `0x80 \| pct` = Manual (`1–100%`) |
| `0x35–0x36` | Read | **APU Fan (`Fan 1`)** RPM | `(reg[0x35] << 8) \| reg[0x36]` (Max ~3150 RPM; `8000` = motor stopped/idle) |
| `0x37–0x38` | Read | **Sys Fan (`Fan 2`)** RPM | `(reg[0x37] << 8) \| reg[0x38]` (Max ~1940 RPM; `8000` = motor stopped/idle) |
| `0x70` | Read | CPU Temperature | °C |
| `0x71` | Read | dGPU Temperature | `0` on APU models (Radeon iGPU temp is read via AMD `atiadlxx.dll`) |

---

## 🔗 Official Drivers & Manufacturer Resources

Accessible directly inside the app via **`Tools & Drivers ▾`**:

- **GMKtec Official Drivers & Manuals (K12 & NucBox Series)**:  
  https://www.gmktec.com/pages/drivers-and-software
- **GMKtec BIOS & Firmware Updates**:  
  https://www.gmktec.com/pages/firmware-update
- **AMD Software: Adrenalin Edition & Chipset Drivers (Ryzen 7 H 255 / 780M & HX 370 / 890M)**:  
  https://www.amd.com/en/support/download/drivers.html  
  *(Installing the official AMD Adrenalin driver enables native Radeon iGPU temperature & APU power telemetry via `atiadlxx.dll`.)*

---

## ⚠️ Hardware Safety Disclaimer

Direct hardware communication with Embedded Controller physical registers (`0xFE0B0400`) is performed at your own risk. MiniFanControl enforces minimum spinning PWM thresholds, 2°C cooldown hysteresis, safe shutdown handlers, and the `Global\Access_EC` mutex to protect your hardware, but users should always ensure adequate airflow when configuring custom fan curves.
