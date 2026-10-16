$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FloatShot'
$running = Get-Process -Name FloatShot -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Please quit FloatShot from the tray icon first.'
    Read-Host 'Press Enter to close'
    exit 1
}
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'FloatShot.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'FloatShot.lnk'),
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'FloatShot.lnk')
)
foreach ($shortcut in $shortcuts) { Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue }
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FloatShot' -Recurse -Force -ErrorAction SilentlyContinue
# Do not delete Desktop\FloatShot Captures or update preferences/history.
# Since this script may itself run from the install directory, remove it after the script exits.
$backup = $installDir + '.previous'
$incoming = $installDir + '.incoming'
$command = 'ping 127.0.0.1 -n 3 >nul & rmdir /s /q "' + $installDir + '" & rmdir /s /q "' + $backup + '" & rmdir /s /q "' + $incoming + '"'
Start-Process -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', $command) -WindowStyle Hidden
Write-Host 'FloatShot was uninstalled. Saved screenshots were preserved.'
