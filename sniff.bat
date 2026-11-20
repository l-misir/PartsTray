@echo off
rem Listen-only HID++ logger (sends nothing). Press Ctrl+C to stop.
chcp 65001 >nul
cd /d "%~dp0"
.venv\Scripts\python.exe tools\sniff.py %*
pause
