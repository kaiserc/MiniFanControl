<#
.SYNOPSIS
    Generates Microsoft winget-pkgs manifests (version, installer, defaultLocale)
    for kaiserc.MiniFanControl from VERSION and the built Inno Setup installer.
.EXAMPLE
    ./scripts/generate-winget-manifest.ps1
.EXAMPLE
    ./scripts/generate-winget-manifest.ps1 -Version 1.3.1 -InstallerSha256 "<64-hex-sha256>"
#>
param(
    [string]$Version,
    [string]$InstallerSha256,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $verPath = Join-Path $root "VERSION"
    if (-not (Test-Path $verPath)) { throw "VERSION file not found at $verPath" }
    $Version = (Get-Content $verPath -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid semver version: '$Version' (expected Major.Minor.Patch)"
}

$installerFileName = "MiniFanControl-Setup-v$Version-x64.exe"
$installerUrl = "https://github.com/kaiserc/MiniFanControl/releases/download/v$Version/$installerFileName"
$releaseNotesUrl = "https://github.com/kaiserc/MiniFanControl/releases/tag/v$Version"

if ([string]::IsNullOrWhiteSpace($InstallerSha256)) {
    $distInstaller = Join-Path $root (Join-Path "dist" $installerFileName)
    $shaSumsPath = Join-Path $root (Join-Path "dist" "SHA256SUMS.txt")
    if (Test-Path $distInstaller) {
        $InstallerSha256 = (Get-FileHash -Path $distInstaller -Algorithm SHA256).Hash.ToUpperInvariant()
    } elseif (Test-Path $shaSumsPath) {
        $match = Select-String -Path $shaSumsPath -Pattern ([regex]::Escape($installerFileName)) | Select-Object -First 1
        if ($match -and $match.Line -match '([A-Fa-f0-9]{64})') {
            $InstallerSha256 = $Matches[1].ToUpperInvariant()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($InstallerSha256)) {
    throw "Installer SHA256 not provided and '$installerFileName' was not found under dist/. Run package-release.bat first or pass -InstallerSha256 <64-hex>."
}
if ($InstallerSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
    throw "Invalid SHA256 format: '$InstallerSha256' (expected 64 hex characters)"
}
$InstallerSha256 = $InstallerSha256.ToUpperInvariant()

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root "dist\winget\manifests\k\kaiserc\MiniFanControl\$Version"
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: kaiserc.MiniFanControl
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: kaiserc.MiniFanControl
PackageVersion: $Version
InstallerLocale: en-US
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
Scope: machine
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART
UpgradeBehavior: install
ElevationRequirement: elevationRequired
Installers:
- Architecture: x64
  InstallerUrl: $installerUrl
  InstallerSha256: $InstallerSha256
ManifestType: installer
ManifestVersion: 1.6.0
"@

$localeYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: kaiserc.MiniFanControl
PackageVersion: $Version
PackageLocale: en-US
Publisher: Christian Kaiser
PublisherUrl: https://github.com/kaiserc
PublisherSupportUrl: https://github.com/kaiserc/MiniFanControl/issues
Author: Christian Kaiser
PackageName: MiniFanControl
PackageUrl: https://github.com/kaiserc/MiniFanControl
License: MIT
LicenseUrl: https://github.com/kaiserc/MiniFanControl/blob/main/LICENSE
ShortDescription: Native EC and Super I/O dual-fan curve controller, stall protection, and hook-free telemetry OSD for AMD Ryzen Mini PCs.
Description: |-
  MiniFanControl is a lightweight, fail-closed Windows fan controller, custom dual-fan curve editor,
  low-RAM Windows Service, and hook-free hardware telemetry / Digital Foundry-style in-game OSD engineered
  for AMD Ryzen Mini PCs (ITE IT5570E EC and IT8613E Super I/O) with HVCI-safe EV-signed PawnIO support.
Moniker: minifancontrol
Tags:
- amd
- fan-control
- hardware-monitor
- mini-pc
- osd
- pawnio
- ryzen
- telemetry
ReleaseNotesUrl: $releaseNotesUrl
Documentations:
- DocumentLabel: User & Hardware Tuning Guide
  DocumentUrl: https://github.com/kaiserc/MiniFanControl/blob/main/GUIDE.md
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $OutputRoot "kaiserc.MiniFanControl.yaml"), $versionYaml.Replace("`r`n", "`n"), $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $OutputRoot "kaiserc.MiniFanControl.installer.yaml"), $installerYaml.Replace("`r`n", "`n"), $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $OutputRoot "kaiserc.MiniFanControl.locale.en-US.yaml"), $localeYaml.Replace("`r`n", "`n"), $utf8NoBom)

Write-Host "[OK] Generated winget-pkgs manifests in: $OutputRoot"
Write-Host "     Next step: validate with 'winget validate --manifest `"$OutputRoot`"' and submit a PR to microsoft/winget-pkgs."
