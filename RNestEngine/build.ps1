# Builds the RNest engine and puts rnest-sparrow.exe next to sparrow.exe, where
# DeepNestSharp.csproj picks it up. Needs Rust (rustup, stable toolchain).
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    cargo build --release -p nest-sparrow
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }
    Copy-Item target\release\rnest-sparrow.exe ..\SparrowEngine\rnest-sparrow.exe -Force
    Write-Host "rnest-sparrow.exe -> SparrowEngine"
} finally {
    Pop-Location
}
