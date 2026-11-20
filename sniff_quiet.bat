@echo off
rem Listen-only HID++ logger, quiet mode: shows only timing milestones. Leave it running.
chcp 65001 >nul
cd /d "%~dp0"
.venv\Scripts\python.exe tools\sniff.py --quiet %*
pause
