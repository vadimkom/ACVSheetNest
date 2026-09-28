# Builds the full SheetNest 1.1.10 MSI (publish + wix). Run from the repo root.
$ErrorActionPreference = "Stop"
Set-Location "C:\Users\rosam\source\repos\DeepNestSharp"

Write-Output "=== 1/2 dotnet publish (self-contained win-x64) ==="
Get-Process SheetNest -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish DeepNestSharp/DeepNestSharp.csproj -c Release -r win-x64 --self-contained -o publish-1.1.10 -v quiet --nologo
if (-not (Test-Path "publish-1.1.10\SheetNest.exe")) { throw "publish failed: no SheetNest.exe" }

