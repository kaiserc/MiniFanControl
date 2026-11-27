<#
.SYNOPSIS
    Extracts the curated release-notes section for the current VERSION (or -Tag)
    from RELEASE_NOTES.md and appends SLSA build provenance verification commands
    for GitHub Releases (softprops/action-gh-release body_path).
.PARAMETER Tag
    Optional git tag name (e.g. 'v1.3.1'). If provided, must match VERSION.
.PARAMETER OutputPath
    Optional file path to write the extracted Markdown body (UTF-8 without BOM).
.PARAMETER Check
    Verify that RELEASE_NOTES.md contains a non-empty section matching VERSION
    without writing any files (exits non-zero on mismatch or missing section).
#>
param(
    [string]$Tag = "",
    [string]$OutputPath = "",
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$verPath = Join-Path $root "VERSION"
$notesPath = Join-Path $root "RELEASE_NOTES.md"

if (-not (Test-Path $verPath)) { throw "VERSION file not found at $verPath" }
if (-not (Test-Path $notesPath)) { throw "RELEASE_NOTES.md not found at $notesPath" }

$ver = (Get-Content $verPath -Raw).Trim()
if ($ver -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid semver in VERSION: '$ver' (expected Major.Minor.Patch)"
}

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $cleanTag = $Tag.Trim()
    if ($cleanTag.StartsWith("refs/tags/")) {
        $cleanTag = $cleanTag.Substring("refs/tags/".Length)
    }
    $expectedTag = "v$ver"
    if ($cleanTag -ne $expectedTag) {
        throw "Tag '$cleanTag' does not match VERSION '$ver' (expected '$expectedTag')."
    }
}

$lines = [System.IO.File]::ReadAllLines($notesPath, [System.Text.Encoding]::UTF8)
$headerPattern = '^#\s+Release Notes:\s+MiniFanControl\s+v' + [regex]::Escape($ver) + '(?:\s|\(|$)'

$startIndex = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match $headerPattern) {
        $startIndex = $i
        break
    }
}

if ($startIndex -lt 0) {
    Write-Error "Could not find a '# Release Notes: MiniFanControl v$ver' section in RELEASE_NOTES.md."
    exit 1
}

$sectionLines = New-Object System.Collections.Generic.List[string]
for ($i = $startIndex; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($i -gt $startIndex) {
        if ($line -match '^\s*---\s*$') { break }
        if ($line -match '^#\s+Release Notes:') { break }
    }
    $sectionLines.Add($line)
}

# Trim trailing blank lines
while ($sectionLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($sectionLines[$sectionLines.Count - 1])) {
    $sectionLines.RemoveAt($sectionLines.Count - 1)
}

if ($sectionLines.Count -lt 2) {
    Write-Error "Release notes section for v$ver in RELEASE_NOTES.md is unexpectedly empty."
    exit 1
}

$body = [string]::Join("`n", $sectionLines)

if ($body -notmatch 'gh attestation verify') {
    $verifyBlock = @"

### Verify Your Download (SLSA Build Provenance)
``````bash
gh attestation verify MiniFanControl-v$ver-win64-Portable.zip --repo kaiserc/MiniFanControl
gh attestation verify MiniFanControl-Setup-v$ver.exe --repo kaiserc/MiniFanControl
``````
"@
    $body = $body + "`n`n" + $verifyBlock.TrimStart("`r", "`n")
}

$body = $body.Replace("`r`n", "`n").TrimEnd() + "`n"

if ($Check) {
    Write-Host "[OK] RELEASE_NOTES.md contains valid release notes for v$ver ($($sectionLines.Count) lines)."
    exit 0
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOut = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $root $OutputPath }
    $outDir = Split-Path -Parent $fullOut
    if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($fullOut, $body, $utf8NoBom)
    Write-Host "[OK] Extracted v$ver release notes -> $OutputPath"
} else {
    Write-Output $body
}
