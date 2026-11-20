@echo off
rem Read-only probe of paired devices and HID++ features (no settings are changed).
chcp 65001 >nul
cd /d "%~dp0"
.venv\Scripts\python.exe tools\probe.py %*
pause
