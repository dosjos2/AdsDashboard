@echo off
cd /d "%~dp0"
if exist "dist\Launch-Pulse.cmd" (
  call "dist\Launch-Pulse.cmd"
) else if exist "dist\current\DevOpsPulse.exe" (
  start "" "dist\current\DevOpsPulse.exe"
) else if exist "dist\DevOpsPulse.exe" (
  start "" "dist\DevOpsPulse.exe"
) else (
  dotnet run --project Dashboard.csproj
)
