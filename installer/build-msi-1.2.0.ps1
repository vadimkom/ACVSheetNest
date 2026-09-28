# Builds the full SheetNest 1.2.0 MSI (publish + wix). Run from the repo root.
$ErrorActionPreference = "Stop"
Set-Location "C:\Users\rosam\source\repos\DeepNestSharp"

Write-Output "=== 1/2 dotnet publish (self-contained win-x64) ==="
Get-Process SheetNest -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish DeepNestSharp/DeepNestSharp.csproj -c Release -r win-x64 --self-contained -o publish-1.2.0 -v quiet --nologo
if (-not (Test-Path "publish-1.2.0\SheetNest.exe")) { throw "publish failed: no SheetNest.exe" }

Write-Output "=== 2/2 wix build ==="
# PublishDir MUST be absolute: wix resolves relative -d paths against the .wxs location
# (installer\), which silently harvests nothing and produces an empty MSI.
$repo = (Get-Location).Path
wix build installer\SheetNest.wxs `
  -d PublishDir="$repo\publish-1.2.0" `
  -d Version=1.2.0 `
  -d IconPath="$repo\DeepNestSharp\Branding\sheetnest.ico" `
  -ext WixToolset.UI.wixext `
  -arch x64 -o SheetNest-1.2.0-win-x64.msi
$mb = [math]::Round((Get-Item "SheetNest-1.2.0-win-x64.msi").Length / 1048576)
Write-Output "MSI listo: SheetNest-1.2.0-win-x64.msi ($mb MB)"
