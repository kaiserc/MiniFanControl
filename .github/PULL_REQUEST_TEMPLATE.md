## Summary

<!-- Briefly describe what this PR changes and why. -->

Fixes #<!-- issue number, if applicable -->

---

## Type of Change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature or UI/localization improvement
- [ ] New Mini PC hardware profile / EC or Super I/O register update
- [ ] Hardware safety, watchdog, or curve math change
- [ ] Documentation or CI/packaging update

---

## Safety & Verification Checklist

Please confirm the following before requesting review:

- [ ] **Automated Self-Tests:** All 101 tests pass (`.\minifan-helper.exe test` or `dotnet run --project MiniFanControl.csproj -c Release -- test`).
- [ ] **Build Check:** Compiles cleanly via `build-windows.bat` and/or `dotnet build MiniFanControl.csproj -c Release`.
- [ ] **Hardware Safety Principles** (per [CONTRIBUTING.md](CONTRIBUTING.md)):
  - [ ] Fail-closed behavior preserved (aborts writes on detection, mutex, or read-back failure).
  - [ ] No blind writes to unverified platforms or unwhitelisted I/O ports.
  - [ ] All EC / Super I/O / SMBus access is synchronized with `EcBusMutex`.
- [ ] **Hardware Profile / Register Changes (if applicable):**
  - [ ] Validated `.\minifan-helper.exe hw-matrix` and `.\minifan-helper.exe export-profiles --json`.
  - [ ] Tested on physical hardware per [HARDWARE_VALIDATION.md](HARDWARE_VALIDATION.md) (specify model/BIOS version below).

---

## Hardware Tested On *(if applicable)*

- **System / Mini PC Model:**
- **CPU / APU:**
- **BIOS / EC Version:**
- **Notes / Logs:**
