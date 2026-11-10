@echo off
setlocal
cd /d "%~dp0"

echo ======================================================================
echo   MiniFanControl - Automated Release Packaging Pipeline
echo ======================================================================

set /p VERSION=<VERSION
set "VERSION=%VERSION: =%"
if "%VERSION%"=="" set "VERSION=2.6.0"
echo Target Release Version: v%VERSION%
echo.

echo [1/5] Building binaries and running automated self-tests...
call "%~dp0build-windows.bat"
if errorlevel 1 (
    echo [ERROR] Build or test verification failed. Aborting release package.
    pause
    exit /b 1
)

set "DIST_DIR=%~dp0dist"
set "STAGE_DIR=%DIST_DIR%\MiniFanControl-v%VERSION%-win64"
set "ZIP_PATH=%DIST_DIR%\MiniFanControl-v%VERSION%-win64.zip"

echo.
echo [2/5] Staging release files into %STAGE_DIR%...
if exist "%STAGE_DIR%" rd /s /q "%STAGE_DIR%"
if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"
mkdir "%STAGE_DIR%" 2>nul

copy /y "%~dp0MiniFanControl.exe" "%STAGE_DIR%\" >nul
copy /y "%~dp0minifan-helper.exe" "%STAGE_DIR%\" >nul
copy /y "%~dp0inpoutx64.dll" "%STAGE_DIR%\" >nul
copy /y "%~dp0app.ico" "%STAGE_DIR%\" >nul
copy /y "%~dp0logo.png" "%STAGE_DIR%\" >nul
copy /y "%~dp0README.md" "%STAGE_DIR%\" >nul
copy /y "%~dp0GUIDE.md" "%STAGE_DIR%\" >nul
copy /y "%~dp0LICENSE" "%STAGE_DIR%\" >nul
copy /y "%~dp0VERSION" "%STAGE_DIR%\" >nul
copy /y "%~dp0run-windows.bat" "%STAGE_DIR%\" >nul
if exist "%~dp0RELEASE_NOTES.md" copy /y "%~dp0RELEASE_NOTES.md" "%STAGE_DIR%\" >nul

echo [3/5] Generating portable release archive .zip...
powershell -NoProfile -Command "$ProgressPreference = 'SilentlyContinue'; Compress-Archive -Path '%STAGE_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force"
if errorlevel 1 (
    echo [ERROR] Failed to create release zip archive.
    pause
    exit /b 1
)
echo [OK] Created: %ZIP_PATH%

echo.
echo [4/5] Checking for Inno Setup compiler...
set "ISCC="
for /f "delims=" %%I in ('powershell -NoProfile -Command "(Get-Command iscc.exe -ErrorAction SilentlyContinue).Source"') do set "ISCC=%%I"
if "%ISCC%"=="" if exist "C:\Program Files (x86)\Inno Setup 6\iscc.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\iscc.exe"
if "%ISCC%"=="" if exist "C:\Program Files\Inno Setup 6\iscc.exe" set "ISCC=C:\Program Files\Inno Setup 6\iscc.exe"

if "%ISCC%"=="" (
    echo [INFO] Inno Setup compiler ISCC.exe not found on PATH or standard install dirs.
    echo        Skipping .exe installer generation. Install Inno Setup 6 to enable.
) else (
    echo [INFO] Found Inno Setup: "%ISCC%"
    echo [INFO] Compiling Windows installer...
    "%ISCC%" /Q "%~dp0installer.iss"
    if errorlevel 1 (
        echo [WARNING] Inno Setup compilation failed.
    ) else (
        echo [OK] Created installer in: %DIST_DIR%\MiniFanControl-Setup-v%VERSION%.exe
    )
)

echo.
echo [5/5] Generating SHA256 verification checksums...
powershell -NoProfile -Command "Get-ChildItem -Path '%DIST_DIR%' -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object { (Get-FileHash -Algorithm SHA256 $_.FullName).Hash.ToLower() + '  ' + $_.Name } | Set-Content -Path '%DIST_DIR%\SHA256SUMS.txt' -Encoding Ascii"
type "%DIST_DIR%\SHA256SUMS.txt"

echo.
echo ======================================================================
echo   Release Packaging Complete!
echo   Output folder: %DIST_DIR%
echo ======================================================================
