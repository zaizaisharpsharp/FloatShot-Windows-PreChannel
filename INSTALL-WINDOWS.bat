@echo off
setlocal
cd /d "%~dp0"
echo.
echo ==============================================================
echo   FloatShot - Install once, then update automatically
 echo ==============================================================
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 8 SDK first:
  echo https://dotnet.microsoft.com/en-us/download/dotnet/8.0
  pause
  exit /b 1
)
echo Building FloatShot for your PC...
dotnet publish FloatShot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o "%~dp0dist"
if errorlevel 1 (
  echo Build failed. Share the error output for correction.
  pause
  exit /b 1
)
copy /y "%~dp0scripts\Uninstall-FloatShot.ps1" "%~dp0dist\Uninstall-FloatShot.ps1" >nul
if errorlevel 1 (
  echo Failed to package uninstall script.
  pause
  exit /b 1
)
echo Installing to your user account (no administrator rights required)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-FloatShot.ps1" -PublishDir "%~dp0dist"
if errorlevel 1 (
  echo Install did not complete. Share this error output.
  pause
  exit /b 1
)
echo.
echo Install complete. FloatShot will run at Windows sign-in.
echo Updates are configured automatically for the official FloatShot release channel.
pause