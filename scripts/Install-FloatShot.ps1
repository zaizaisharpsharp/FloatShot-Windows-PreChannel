param([Parameter(Mandatory=$true)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FloatShot'
$source = Join-Path $PublishDir 'FloatShot.exe'
if (-not (Test-Path -LiteralPath $source)) { throw 'Published FloatShot.exe was not found. Build again.' }
$running = Get-Process -Name FloatShot -ErrorAction SilentlyContinue
if ($running) {
    throw 'FloatShot is running. Right-click its tray icon, select Quit FloatShot, then double-click INSTALL-WINDOWS.bat again.'
}
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
# Create the screenshot library immediately, even before the app first launches.
$captureDir = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'FloatShot Captures'
New-Item -ItemType Directory -Force -Path $captureDir | Out-Null
Get-ChildItem -LiteralPath $PublishDir -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $installDir -Recurse -Force
}
Set-Content -LiteralPath (Join-Path $installDir 'installed.marker') -Value 'FloatShot per-user installation' -Encoding UTF8
$target = Join-Path $installDir 'FloatShot.exe'
$icon = Join-Path $installDir 'assets\floatshot.ico'
$shell = New-Object -ComObject WScript.Shell
function New-FloatShotShortcut([string]$path) {
    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $target
    $link.WorkingDirectory = $installDir
    if (Test-Path -LiteralPath $icon) { $link.IconLocation = $icon }
    $link.Description = 'FloatShot screenshot capture'
    $link.Save()
}
New-FloatShotShortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'FloatShot.lnk')
New-FloatShotShortcut (Join-Path ([Environment]::GetFolderPath('Startup')) 'FloatShot.lnk')
New-FloatShotShortcut (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'FloatShot.lnk')

# Register an uninstaller for Settings > Installed Apps (per-user, no elevation).
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FloatShot'
New-Item -Path $key -Force | Out-Null
$version = (Get-Item -LiteralPath $target).VersionInfo.ProductVersion
Set-ItemProperty -Path $key -Name DisplayName -Value 'FloatShot'
Set-ItemProperty -Path $key -Name DisplayVersion -Value $version
Set-ItemProperty -Path $key -Name Publisher -Value 'FloatShot (local build)'
Set-ItemProperty -Path $key -Name InstallLocation -Value $installDir
Set-ItemProperty -Path $key -Name DisplayIcon -Value $target
$uninstall = Join-Path $installDir 'Uninstall-FloatShot.ps1'
Set-ItemProperty -Path $key -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $uninstall + '"')

Write-Host ('Installed FloatShot to: ' + $installDir)
Start-Process -FilePath $target -WorkingDirectory $installDir
