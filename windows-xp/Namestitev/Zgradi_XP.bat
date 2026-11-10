@echo off
setlocal
cd /d "%~dp0"
echo ============================================
echo   CDS TapeLab - XP izdaja (.NET 4.0, 32-bit)
echo ============================================
echo.
set OUT=%~dp0..\Izdaja\CDS TapeLab XP
if exist "%OUT%" rmdir /s /q "%OUT%"
echo [1/2] Izdelava programa ...
dotnet publish "%~dp0..\CDSMeasure\CDSMeasure.csproj" -c Release -o "%OUT%" -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 goto fail
copy /y "%~dp0README_XP.txt" "%OUT%\README_XP.txt" >nul
copy /y "%~dp0README_XP_HU.txt" "%OUT%\README_XP_HU.txt" >nul
echo.
echo [2/2] Pakiranje v ZIP ...
set ZIP=%~dp0..\Izdaja\CDSTapeLab-XP-0.6.0.zip
if exist "%ZIP%" del /q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
echo.
echo Narejeno: %OUT%
echo ZIP za prijatelja: %ZIP%
pause
exit /b 0

:fail
echo.
echo NAPAKA. Poslji mi zgornje besedilo.
pause
exit /b 1
