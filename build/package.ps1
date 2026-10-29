$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
$publish = Join-Path $root 'publish\NEXMC'

if (-not (Test-Path $dotnet)) {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $systemDotnet) { throw '.NET 10 SDK not found. Install it or place a portable SDK in tools\dotnet.' }
    $dotnet = $systemDotnet.Source
}
if (-not (Test-Path (Join-Path $root 'node_modules\electron-builder'))) { throw 'Run bun install before packaging.' }

& $dotnet publish (Join-Path $root 'src\NEXMC\NEXMC.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Push-Location $root
try {
    & (Join-Path $root 'node_modules\.bin\electron-builder.exe') --config build\electron-builder.yml --prepackaged $publish --win nsis
    if ($LASTEXITCODE -ne 0) { throw "electron-builder failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
Write-Host 'Package complete.'
