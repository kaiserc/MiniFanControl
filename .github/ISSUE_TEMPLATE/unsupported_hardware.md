---
name: Unsupported Hardware / Profile Request
about: Request support for a new AMD Ryzen Mini PC or submit an EC register dump
title: '[Hardware]: Support for <Manufacturer> <Model> (<CPU>)'
labels: ['hardware-support', 'enhancement']
assignees: ''
---

### System Information
- **Manufacturer**: (e.g. GMKtec, Minisforum, Beelink, ADREAMER, Morefine, KTHIN)
- **Model Name / Number**: (e.g. NucBox K12, AriesBox 10, EVO-X1, TS-12C)
- **Processor (CPU / APU)**: (e.g. AMD Ryzen 7 8845HS, Ryzen AI 9 HX 370, Ryzen 7 7840HS)
- **Integrated GPU**: (e.g. Radeon 780M, Radeon 890M)
- **Number of Physical Fans**: [ ] 1 Fan  [ ] 2 Fans (Dual Fan)
- **Operating System**: Windows 10 / 11 64-bit (Build number)

### Embedded Controller (EC) Register Dump
Please run the following command in PowerShell / Command Prompt as Administrator and paste the output below:

```powershell
.\minifan-helper.exe dump
```

<details>
<summary>Click to expand EC Register Dump output</summary>

```
<!-- Paste output of 'minifan-helper.exe dump' here -->
```

</details>

### Dynamic Register Watch (Optional but Recommended)
Run the register watcher for 15 seconds while switching performance modes in BIOS or running a brief CPU stress test (e.g. CPU-Z bench):

```powershell
.\minifan-helper.exe watch
```

<details>
<summary>Click to expand active changing registers</summary>

```
<!-- Paste output of 'minifan-helper.exe watch' here -->
```

</details>

### Additional Details
Any additional context (BIOS version, known motherboard DMI strings, fan behavior under heavy load).
