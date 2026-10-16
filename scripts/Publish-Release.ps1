param(
  [Parameter(Mandatory=$true)][string]$Repository,
  [Parameter(Mandatory=$true)][string]$Version,
  [switch]$Upload
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if ($Repository -notmatch '^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$') { throw 'Repository should look like OWNER/REPO.' }
$projectVersion = ([xml](Get-Content '.\FloatShot.csproj')).Project.PropertyGroup.Version
if ($Version -ne $projectVersion) { throw "Version $Version does not match csproj version $projectVersion." }
& dotnet publish '.\FloatShot.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o '.\dist'
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Release was not generated.' }
Copy-Item '.\scripts\Uninstall-FloatShot.ps1' '.\dist\Uninstall-FloatShot.ps1' -Force
New-Item -ItemType Directory -Force -Path '.\release' | Out-Null
$zip = Join-Path $root 'release\FloatShot-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path '.\dist\*' -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$tag = 'v' + $Version
$manifest = [ordered]@{
  version = $Version
  assetUrl = "https://github.com/$Repository/releases/download/$tag/FloatShot-win-x64.zip"
  sha256 = $hash
}
$manifestPath = Join-Path $root 'release\floatshot-update.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('Created: ' + $zip)
Write-Host ('Created: ' + $manifestPath)
Write-Host ('SHA256: ' + $hash)
if ($Upload) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) is required for uploading.' }
  & gh auth status
  if ($LASTEXITCODE -ne 0) { throw 'Sign in to GitHub CLI before uploading.' }
  & gh release create $tag $zip $manifestPath --repo $Repository --title $tag --notes "FloatShot $tag for Windows x64"
  if ($LASTEXITCODE -ne 0) { throw 'GitHub Release upload failed.' }
  Write-Host 'Release published. Installed clients using this repository can update automatically.'
} else {
  Write-Host 'Release prepared locally. To publish it, rerun with -Upload (requires GitHub CLI).'
}
