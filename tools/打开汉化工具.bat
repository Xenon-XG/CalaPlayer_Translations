@echo off
chcp 65001 >nul
cd /d "%~dp0"
python server.py
if errorlevel 9009 goto nopy
goto end
:nopy
echo.
echo [!] Python not found. Install Python 3 from https://www.python.org and retry.
pause
:end
