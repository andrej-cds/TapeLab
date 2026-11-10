@echo off
cd /d "%~dp0"
echo Gradim ... (traja 1-3 minute, ne zapri okna)
call "%~dp0Zgradi_XP.bat" < nul > "%~dp0zgradi_log.txt" 2>&1
echo.
type "%~dp0zgradi_log.txt"
echo.
echo === KONEC (zapis je tudi v zgradi_log.txt) ===
pause
