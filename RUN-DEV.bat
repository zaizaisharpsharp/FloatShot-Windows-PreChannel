@echo off
cd /d "%~dp0"
dotnet run --project FloatShot.csproj
if errorlevel 1 pause
