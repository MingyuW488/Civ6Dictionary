$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/Civ6EnglishDemo.dll'
$localDotnet = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) { & $localDotnet $dll } else { & dotnet $dll }
