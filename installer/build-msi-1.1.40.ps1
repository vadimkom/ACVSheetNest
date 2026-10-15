# Builds the SheetNest 1.1.40 MSI (publish + wix). Run from the repo root or the installer/ folder.
$ErrorActionPreference = "Stop"
if ($PSScriptRoot) {
  Set-Location (Split-Path $PSScriptRoot -Parent)
}

Write-Output "=== 1/2 dotnet publish (self-contained win-x64) ==="
Get-Process SheetNest -ErrorAction SilentlyContinue | Stop-Process -Force
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet publish DeepNestSharp/DeepNestSharp.csproj -c Release -r win-x64 --self-contained -o publish-1.1.40 -v quiet --nologo
if (-not (Test-Path "publish-1.1.40\SheetNest.exe")) { throw "publish failed: no SheetNest.exe" }

# A publish folder reused from an older build can still hold the retired FreeCAD payload; wix would ship it.
if (Test-Path "publish-1.1.40\freecad") {
  Remove-Item "publish-1.1.40\freecad" -Recurse -Force
}

Write-Output "=== 2/2 wix build ==="
# PublishDir MUST be absolute: wix resolves relative -d paths against the .wxs location
# (installer\), which silently harvests nothing and produces an empty MSI.
$repo = (Get-Location).Path
$wixCmd = if (Get-Command wix -ErrorAction SilentlyContinue) { "wix" } elseif (Test-Path "$env:USERPROFILE\.dotnet\tools\wix.exe") { "$env:USERPROFILE\.dotnet\tools\wix.exe" } else { "wix" }

& $wixCmd build installer\SheetNest.wxs `
  -d PublishDir="$repo\publish-1.1.40" `
  -d Version=1.1.40 `
  -d IconPath="$repo\DeepNestSharp\Branding\sheetnest.ico" `
  -ext WixToolset.UI.wixext `
  -arch x64 -o SheetNest-1.1.40-win-x64.msi
$mb = [math]::Round((Get-Item "SheetNest-1.1.40-win-x64.msi").Length / 1048576)
Write-Output "MSI listo: SheetNest-1.1.40-win-x64.msi ($mb MB)"
