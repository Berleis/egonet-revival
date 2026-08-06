param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $repoRoot "artifacts\dirt-showdown-mod"
$patcherOutput = Join-Path $packageRoot "tools\RaceNetShowdown.Patcher"
$zipPath = Join-Path $repoRoot "artifacts\EgoNetRevival-DiRTShowdown-Mod.zip"

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $patcherOutput | Out-Null

dotnet publish (Join-Path $repoRoot "src\RaceNetShowdown.Patcher\RaceNetShowdown.Patcher.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    --output $patcherOutput

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath (Join-Path $patcherOutput "RaceNetShowdown.Patcher.exe"))) {
    throw "Published patcher executable was not found."
}

Copy-Item -LiteralPath (Join-Path $repoRoot "install-dirt-showdown-mod.cmd") -Destination $packageRoot
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot "tools") | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "tools\InstallDirtShowdownMod.ps1") -Destination (Join-Path $packageRoot "tools")

@"
# EgoNet Revival - DiRT Showdown Mod

1. Close DiRT Showdown.
2. Run install-dirt-showdown-mod.cmd as Administrator.
3. Open DiRT Showdown and enter RaceNet.

Default server: 142.93.206.37

Advanced:
install-dirt-showdown-mod.cmd -Server 142.93.206.37
"@ | Set-Content -LiteralPath (Join-Path $packageRoot "README.txt") -Encoding ASCII

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath

Write-Host "Package written to: $zipPath" -ForegroundColor Green
