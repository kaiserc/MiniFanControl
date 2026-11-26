<#
.SYNOPSIS
    Single-source version synchronizer for MiniFanControl.
    Reads the canonical version from VERSION and updates app.manifest,
    installer.iss, MiniFanControl.csproj, and Ui/MainForm.cs (APP_VERSION).
.PARAMETER Check
    Verify all files match VERSION without modifying anything (exits non-zero on drift).
#>
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$verPath = Join-Path $root "VERSION"
if (-not (Test-Path $verPath)) { throw "VERSION file not found at $verPath" }

$ver = (Get-Content $verPath -Raw).Trim()
if ($ver -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid semver in VERSION: '$ver' (expected Major.Minor.Patch)"
}

$targets = @(
    @{
        Path        = Join-Path $root "app.manifest"
        Pattern     = '(<assemblyIdentity\s+version=")\d+\.\d+\.\d+\.0("\s+name="MiniFanControl")'
        Replacement = ('${{1}}{0}.0${{2}}' -f $ver)
        Label       = "app.manifest"
    },
    @{
        Path        = Join-Path $root "installer.iss"
        Pattern     = '#define MyAppVersion "\d+\.\d+\.\d+"'
        Replacement = ('#define MyAppVersion "{0}"' -f $ver)
        Label       = "installer.iss"
    },
    @{
        Path        = Join-Path $root "MiniFanControl.csproj"
        Pattern     = '<Version>\d+\.\d+\.\d+</Version>'
        Replacement = ('<Version>{0}</Version>' -f $ver)
        Label       = "MiniFanControl.csproj (Version)"
    },
    @{
        Path        = Join-Path $root "MiniFanControl.csproj"
        Pattern     = '<AssemblyVersion>\d+\.\d+\.\d+\.0</AssemblyVersion>'
        Replacement = ('<AssemblyVersion>{0}.0</AssemblyVersion>' -f $ver)
        Label       = "MiniFanControl.csproj (AssemblyVersion)"
    },
    @{
        Path        = Join-Path $root "MiniFanControl.csproj"
        Pattern     = '<FileVersion>\d+\.\d+\.\d+\.0</FileVersion>'
        Replacement = ('<FileVersion>{0}.0</FileVersion>' -f $ver)
        Label       = "MiniFanControl.csproj (FileVersion)"
    },
    @{
        Path        = Join-Path $root "Ui\MainForm.cs"
        Pattern     = '(APP_VERSION\s*=\s*")\d+\.\d+\.\d+(")'
        Replacement = ('${{1}}{0}${{2}}' -f $ver)
        Label       = "Ui/MainForm.cs (APP_VERSION)"
    }
)

$drifted = @()
foreach ($t in $targets) {
    $raw = Get-Content $t.Path -Raw
    $rx = New-Object System.Text.RegularExpressions.Regex($t.Pattern)
    if (-not $rx.IsMatch($raw)) {
        throw "Expected version pattern not found in $($t.Label)"
    }
    $updated = $rx.Replace($raw, $t.Replacement, 1)
    if ($updated -ne $raw) {
        if ($Check) {
            $drifted += $t.Label
        } else {
            Set-Content -Path $t.Path -Value $updated -NoNewline -Encoding UTF8
            Write-Host "[SYNC] Updated $($t.Label) -> $ver"
        }
    }
}

if ($Check) {
    if ($drifted.Count -gt 0) {
        Write-Error ("Version drift detected against VERSION ($ver): " + ($drifted -join ", "))
        exit 1
    }
    Write-Host "[OK] All versioned files match VERSION ($ver)."
} else {
    Write-Host "[OK] Single-source version sync complete ($ver)."
}
