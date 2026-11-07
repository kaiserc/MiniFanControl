@echo off
setlocal
cd /d "%~dp0"

if not exist "MiniFanControl.exe" (
    call "%~dp0build-windows.bat"
)

start "" "%~dp0MiniFanControl.exe" %*
