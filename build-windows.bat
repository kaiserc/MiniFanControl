@echo off
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ::error title=CSC Missing::Built-in .NET C# compiler not found at: %CSC%
    echo [ERROR] Built-in .NET C# compiler not found at: %CSC%
    if "%CI%"=="" pause
    exit /b 1
)

echo Compiling MiniFanControl.exe (GUI) and minifan-helper.exe (CLI)...

rem Attempt to delete any unlocked old temporary exes
for %%f in (*.old.exe) do (
    del /f /q "%%f" >nul 2>&1
)

rem Safely free the filenames even if processes are currently running
if exist MiniFanControl.exe (
    ren MiniFanControl.exe "MiniFanControl.%RANDOM%%RANDOM%.old.exe" >nul 2>&1
)
if exist minifan-helper.exe (
    ren minifan-helper.exe "minifan-helper.%RANDOM%%RANDOM%.old.exe" >nul 2>&1
)

"%CSC%" /nologo /platform:x64 /target:exe /out:minifan-helper.exe /recurse:*.cs > "%TEMP%\csc_helper.log" 2>&1
set CSC_ERR=%ERRORLEVEL%
type "%TEMP%\csc_helper.log"
if %CSC_ERR% neq 0 (
    echo [ERROR] Failed to compile minifan-helper.exe
    echo ::error title=CSC Helper Error::minifan-helper.exe compilation failed
    for /f "delims=" %%L in ('type "%TEMP%\csc_helper.log"') do (
        echo ::error file=minifan-helper.exe::%%L
    )
    if "%CI%"=="" pause
    exit /b 1
)

"%~dp0minifan-helper.exe" gen-icon
if errorlevel 1 (
    echo [WARNING] Icon generation step returned non-zero, continuing build...
)

if exist app.ico (
    "%CSC%" /nologo /platform:x64 /target:winexe /win32icon:app.ico /out:MiniFanControl.exe /recurse:*.cs > "%TEMP%\csc_gui.log" 2>&1
) else (
    "%CSC%" /nologo /platform:x64 /target:winexe /out:MiniFanControl.exe /recurse:*.cs > "%TEMP%\csc_gui.log" 2>&1
)
set CSC_GUI_ERR=%ERRORLEVEL%
type "%TEMP%\csc_gui.log"
if %CSC_GUI_ERR% neq 0 (
    echo [ERROR] Failed to compile MiniFanControl.exe
    echo ::error title=CSC GUI Error::MiniFanControl.exe compilation failed
    for /f "delims=" %%L in ('type "%TEMP%\csc_gui.log"') do (
        echo ::error file=MiniFanControl.exe::%%L
    )
    if "%CI%"=="" pause
    exit /b 1
)

rem Clean up any old exes that may have closed
for %%f in (*.old.exe) do (
    del /f /q "%%f" >nul 2>&1
)

echo [OK] Build succeeded: MiniFanControl.exe and minifan-helper.exe
echo.
"%~dp0minifan-helper.exe" test > "%TEMP%\tests.log" 2>&1
set TEST_ERR=%ERRORLEVEL%
type "%TEMP%\tests.log"
if %TEST_ERR% neq 0 (
    echo [ERROR] Automated self-tests failed!
    echo ::error title=SelfTest Failure::Automated test suite exited with error code %TEST_ERR%
    for /f "delims=" %%L in ('type "%TEMP%\tests.log"') do (
        echo ::error file=Tests/SelfTestSuite.cs::%%L
    )
    if "%CI%"=="" pause
    exit /b 1
)
