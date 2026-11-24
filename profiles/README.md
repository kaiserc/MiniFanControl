# External Hardware Profile Registry (`profiles/`)

Drop community or custom JSON hardware profiles into `profiles/` (next to `MiniFanControl.exe` or in `%LocalAppData%\MiniFanControl\profiles\`) to add support for new AMD Ryzen Mini PCs without recompiling MiniFanControl.

## Safety Rules (Enforced Fail-Closed)

1. **DMI Binding Required:** Every external profile must specify `boundProductName` and/or `boundBaseBoard` (generic placeholders like `"Default string"` or `"To be filled by O.E.M."` are rejected).
2. **Valid Register Map Required:** `ecBaseAddress` must be in safe MMIO space (`>= 0x000E0000`) and both `regCpuTemp` and `regFan1Ctrl` must be non-zero.
3. **Blocklist Precedence:** Known-unsafe / unverified hardware blocklists always override external profiles.
4. **Read-Only Default:** New submissions should start with `"isReadOnly": true, "isVerified": false` until live read-back verification confirms safe fan writes.

## Validating Profiles

Run:

```bat
minifan-helper.exe validate-profiles
```

Use [template.profile.json](template.profile.json) as a starting point (files starting with `template` are skipped at runtime).
