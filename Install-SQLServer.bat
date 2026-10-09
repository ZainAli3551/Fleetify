@echo off
echo ========================================================
echo   Launching Microsoft SQL Server Express Installer
echo ========================================================
echo.
echo Please click 'Yes' on the Windows Administrator prompt...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process '%USERPROFILE%\Downloads\SQL2025-SSEI-Expr.exe' -Verb RunAs"
