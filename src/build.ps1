param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$mirrorRoot = Split-Path $projectRoot -Parent
$localDotnet = Join-Path $mirrorRoot 'tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnetExe = $localDotnet
    $env:DOTNET_ROOT = Split-Path $localDotnet -Parent
    $env:DOTNET_CLI_HOME = Join-Path $mirrorRoot 'tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $mirrorRoot 'tools/nuget-packages'
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetExe = (Get-Command dotnet).Source
} else { throw 'Please install the .NET 10 SDK, or use the supplied Windows executable.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$feed = Join-Path $mirrorRoot 'tools/nuget-feed'
if (Test-Path -LiteralPath $feed) {
    & $dotnetExe restore (Join-Path $projectRoot 'Civ6EnglishDemo.csproj') --source $feed -p:NuGetAudit=false
} else {
    & $dotnetExe restore (Join-Path $projectRoot 'Civ6EnglishDemo.csproj')
}
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
& $dotnetExe build (Join-Path $projectRoot 'Civ6EnglishDemo.csproj') --no-restore -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($SelfTest) {
    $dll = Join-Path $projectRoot 'bin/Release/net10.0-windows/Civ6EnglishDemo.dll'
    & $dotnetExe $dll --self-test
    if ($env:CIV6_DEMO_DATA) { $report = Join-Path $env:CIV6_DEMO_DATA 'self-test.txt' }
    else { $report = Join-Path $env:LOCALAPPDATA 'Civ6EnglishDemo/self-test.txt' }
    if ($LASTEXITCODE -ne 0) { throw "Self-test failed. See $report" }
    Get-Content $report -Encoding UTF8
}
