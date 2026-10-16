@echo off
setlocal
cd /d "%~dp0"
echo.
echo ================================================
echo    FloatShot - Windows x64 Build (v6)
echo ================================================
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK not found. Install the .NET 8 SDK from Microsoft:
  echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
  pause
  exit /b 1
)
echo Publishing a self-contained Windows app...
dotnet publish FloatShot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o "%~dp0dist"
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Please copy the errors and share them.
  pause
  exit /b 1
)
copy /y "%~dp0scripts\Uninstall-FloatShot.ps1" "%~dp0dist\Uninstall-FloatShot.ps1" >nul
if errorlevel 1 (
  echo Unable to include uninstaller.
  pause
  exit /b 1
)
echo.
echo Build successful. Opening output folder...
explorer "%~dp0dist"
pause
