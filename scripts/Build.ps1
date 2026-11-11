param([switch]$Preview, [string]$OutputDirectory = 'dist/current')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
$previousAppData = $env:APPDATA
try {
    # Keep build tooling within the project, including in restricted workspaces.
    $env:APPDATA = Join-Path $projectRoot '.buildprofile'
    dotnet restore Dashboard.csproj --configfile NuGet.Config --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet publish Dashboard.csproj -c Release --no-restore -o $OutputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Preview) {
        $app = Join-Path (Join-Path $projectRoot $OutputDirectory) 'DevOpsPulse.exe'
        $process = Start-Process -FilePath $app -ArgumentList '--preview' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Visual preview failed.' }
    }
    $releaseRelative = Resolve-Path -LiteralPath $OutputDirectory -Relative
    $launcher = '@echo off' + "`r`n" + 'start "" "%~dp0..\' + $releaseRelative + '\DevOpsPulse.exe"' + "`r`n"
    [IO.File]::WriteAllText((Join-Path $projectRoot 'dist/Launch-Pulse.cmd'), $launcher, [Text.Encoding]::ASCII)
    Write-Host "Ready: $OutputDirectory/DevOpsPulse.exe (Start-Pulse.cmd updated)"
}
finally { $env:APPDATA = $previousAppData; Pop-Location }
