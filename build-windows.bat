@echo off
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] Built-in .NET C# compiler not found at:
    echo         %CSC%
    pause
    exit /b 1
)

echo Compiling MiniFanControl.exe (GUI) and minifan-helper.exe (CLI)...
del /f /q MiniFanControl.old.exe >nul 2>&1
if exist MiniFanControl.exe ren MiniFanControl.exe MiniFanControl.old.exe >nul 2>&1

"%CSC%" /nologo /platform:x64 /target:exe /out:minifan-helper.exe MiniFanControl.cs
if errorlevel 1 (
    echo [ERROR] Failed to compile minifan-helper.exe
    pause
    exit /b 1
)

"%~dp0minifan-helper.exe" gen-icon
if errorlevel 1 (
    echo [WARNING] Icon generation step returned non-zero, continuing build...
)

if exist app.ico (
    "%CSC%" /nologo /platform:x64 /target:winexe /win32icon:app.ico /out:MiniFanControl.exe MiniFanControl.cs
) else (
    "%CSC%" /nologo /platform:x64 /target:winexe /out:MiniFanControl.exe MiniFanControl.cs
)
if errorlevel 1 (
    echo [ERROR] Failed to compile MiniFanControl.exe
    pause
    exit /b 1
)

del /f /q MiniFanControl.old.exe >nul 2>&1
echo [OK] Build succeeded: MiniFanControl.exe and minifan-helper.exe
echo.
"%~dp0minifan-helper.exe" test
if errorlevel 1 (
    echo [ERROR] Automated self-tests failed!
    pause
    exit /b 1
)
