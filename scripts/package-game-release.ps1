param(
    [Parameter(Mandatory = $true)]
    [string]$Game,

    [string]$OutputRoot = "artifacts/releases"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "games/games.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$gameInfo = $manifest.games | Where-Object { $_.id -eq $Game } | Select-Object -First 1

if ($null -eq $gameInfo) {
    throw "Unknown game package '$Game'. Add it to games/games.json first."
}

$packagePath = Join-Path $repoRoot $gameInfo.packagePath
$installerPath = Join-Path $packagePath $gameInfo.installerAsset

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer asset not found: $installerPath"
}

$outputPath = Join-Path (Join-Path $repoRoot $OutputRoot) $gameInfo.id
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$installerOutputPath = Join-Path $outputPath $gameInfo.installerAsset
Copy-Item -LiteralPath $installerPath -Destination $installerOutputPath -Force

$readmePath = Join-Path $packagePath "README.md"
if (Test-Path -LiteralPath $readmePath -PathType Leaf) {
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $outputPath "README.md") -Force
}

$releaseNotesPath = Join-Path $packagePath "RELEASE_NOTES.md"
if (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf) {
    Copy-Item -LiteralPath $releaseNotesPath -Destination (Join-Path $outputPath "RELEASE_NOTES.md") -Force
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $installerOutputPath
"$($hash.Hash.ToLowerInvariant())  $($gameInfo.installerAsset)" |
    Set-Content -LiteralPath (Join-Path $outputPath "$($gameInfo.installerAsset).sha256") -Encoding ASCII

Write-Host "Packaged $($gameInfo.name) release assets in $outputPath"
