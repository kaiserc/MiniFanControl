# Accessibility Statement

**MiniFanControl** aims to be usable by as many people as possible, including users who rely on keyboard navigation, high-contrast color schemes, or assistive technologies on Windows.

---

## Supported Environments

- **Operating System:** Windows 10 and Windows 11 (x64)
- **UI Framework:** Windows Forms (.NET Framework 4.8 and .NET 10.0-windows)
- **Visual Themes:** Built-in **Dark**, **Light**, and **AMD Radeon** high-contrast themes

---

## Accessibility Features

- **Contrast & Theming:** Multiple high-contrast UI themes (`ThemePreference.cs`) to support readability in different lighting conditions and color preferences.
- **Accessible Controls & Labels:** Standard Windows Forms controls with descriptive labels, tooltips, and localization support (`Localization.cs`).
- **CLI & Headless Support:** Core diagnostics, self-tests, profile exports, and fan/hardware queries can be run from the command line via `minifan-helper.exe` without requiring the graphical interface.

---

## Known Limitations

- **Custom-Drawn Canvases:** Interactive graphical controls such as the fan curve editor (`FanCurveCanvas`), live telemetry sparklines (`LiveTelemetryCanvas`), and performance graphs (`LivePerformanceCanvas`) are custom-drawn GDI+ surfaces and may have limited screen-reader or keyboard-only granularity compared to standard form controls.
- **In-Game OSD HUD:** The transparent CapFrameX-style overlay (`CapFrameXOverlayForm`) is designed as a click-through visual overlay and is not intended for screen-reader interaction.

---

## Reporting Accessibility Barriers

If you encounter an accessibility barrier—such as missing keyboard focus, unreadable contrast, missing accessible names/labels, or screen-reader issues—please open an issue using our [Bug Report template](https://github.com/kaiserc/MiniFanControl/issues/new?template=bug_report.md) and include:

- Your Windows version and display scaling (DPI) settings
- Any assistive technology used (e.g., Narrator, NVDA, Magnifier, Windows High Contrast)
- The screen or dialog where the issue occurred
