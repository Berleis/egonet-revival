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

$releaseAssets = New-Object System.Collections.Generic.List[string]

$installerOutputPath = Join-Path $outputPath $gameInfo.installerAsset
Copy-Item -LiteralPath $installerPath -Destination $installerOutputPath -Force
$releaseAssets.Add($installerOutputPath) | Out-Null

$installerProject = $null
if ($gameInfo.PSObject.Properties.Name -contains "installerProject") {
    $installerProject = $gameInfo.installerProject
}

$installerExecutableAsset = $null
if ($gameInfo.PSObject.Properties.Name -contains "installerExecutableAsset") {
    $installerExecutableAsset = $gameInfo.installerExecutableAsset
}

if (-not [string]::IsNullOrWhiteSpace($installerProject) -and -not [string]::IsNullOrWhiteSpace($installerExecutableAsset)) {
    $installerProjectPath = Join-Path $repoRoot $installerProject
    if (-not (Test-Path -LiteralPath $installerProjectPath -PathType Leaf)) {
        throw "Installer project not found: $installerProjectPath"
    }

    $publishPath = Join-Path $outputPath "_installer-publish"
    if (Test-Path -LiteralPath $publishPath) {
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }

    dotnet publish $installerProjectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:EnableCompressionInSingleFile=true `
        --output $publishPath

    $publishedExecutable = Get-ChildItem -LiteralPath $publishPath -Filter "*.exe" |
        Select-Object -First 1
    if ($null -eq $publishedExecutable) {
        throw "Installer publish did not produce an executable in $publishPath"
    }

    $installerExecutableOutputPath = Join-Path $outputPath $installerExecutableAsset
    Copy-Item -LiteralPath $publishedExecutable.FullName -Destination $installerExecutableOutputPath -Force
    $releaseAssets.Add($installerExecutableOutputPath) | Out-Null

    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

$readmePath = Join-Path $packagePath "README.md"
if (Test-Path -LiteralPath $readmePath -PathType Leaf) {
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $outputPath "README.md") -Force
}

$releaseNotesPath = Join-Path $packagePath "RELEASE_NOTES.md"
if (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf) {
    Copy-Item -LiteralPath $releaseNotesPath -Destination (Join-Path $outputPath "RELEASE_NOTES.md") -Force
}

foreach ($assetPath in $releaseAssets) {
    $assetName = Split-Path -Leaf $assetPath
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath
    "$($hash.Hash.ToLowerInvariant())  $assetName" |
        Set-Content -LiteralPath (Join-Path $outputPath "$assetName.sha256") -Encoding ASCII
}

Write-Host "Packaged $($gameInfo.name) release assets in $outputPath"
