@echo off
setlocal
cd /d "%~dp0"
echo ============================================
echo   CDS TapeLab - izdelava namestitvenega programa
echo ============================================
echo.
set OUT=%~dp0..\Izdaja\CDS TapeLab
if exist "%OUT%" rmdir /s /q "%OUT%"
echo [1/2] Izdelava programa ...
dotnet publish "%~dp0..\CDSMeasure\CDSMeasure.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "%OUT%"
if errorlevel 1 goto fail

set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo.
  echo Inno Setup ni namescen. Prenesi ga - brezplacno: https://jrsoftware.org/isdl.php
  echo Nato ta skripto zazeni znova.
  pause
  exit /b 1
)
echo.
echo [2/2] Izdelava namestitve ...
"%ISCC%" "%~dp0CDSTapeLab.iss"
if errorlevel 1 goto fail
echo.
echo Narejeno: %~dp0..\Izdaja\CDSTapeLab-Setup-0.6.0.exe
pause
exit /b 0

:fail
echo.
echo NAPAKA. Poslji mi zgornje besedilo.
pause
exit /b 1
