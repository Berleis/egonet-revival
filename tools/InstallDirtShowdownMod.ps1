param(
    [string]$Server = "142.93.206.37",
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown"
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Relaunch-AsAdministrator {
    $scriptPath = $PSCommandPath
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$scriptPath`"",
        "-Server", "`"$Server`"",
        "-GamePath", "`"$GamePath`""
    )

    Start-Process powershell -Verb RunAs -ArgumentList $arguments
}

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Update-HostsFile {
    $hostsPath = "$env:WINDIR\System32\drivers\etc\hosts"
    $hostnames = @(
        "prod.egonet.codemasters.com",
        "egonet.codemasters.com",
        "racenet.codemasters.com",
        "api.racenet.codemasters.com",
        "showdown.racenet.codemasters.com",
        "racenet.com",
        "www.racenet.com",
        "api.racenet.com"
    )

    $existing = Get-Content -LiteralPath $hostsPath -ErrorAction SilentlyContinue
    $kept = $existing | Where-Object {
        $line = $_
        -not ($hostnames | Where-Object {
            $line -match "^\s*(\d{1,3}\.){3}\d{1,3}\s+$([regex]::Escape($_))(\s|$)" -or
            $line -match "^\s*::1\s+$([regex]::Escape($_))(\s|$)"
        })
    }

    $newLines = $hostnames | ForEach-Object { "$Server $_" }
    $content = @($kept; ""; "# EgoNet Revival - DiRT Showdown"; $newLines)
    Set-Content -LiteralPath $hostsPath -Value $content -Encoding ASCII
}

function Install-RootCertificate {
    param([string]$CertificatePath)

    & certutil.exe -addstore -f Root $CertificatePath | Out-Host
}

function Test-HealthEndpoint {
    $url = "https://prod.egonet.codemasters.com/health"
    & curl.exe -fsS $url | Out-Host
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Administrator permission is required to update hosts, install the certificate, and patch the game."
    Write-Host "Opening an elevated window..."
    Relaunch-AsAdministrator
    exit 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$certificateDirectory = Join-Path $repoRoot "certs"
if (Test-Path -LiteralPath (Join-Path $repoRoot "src\RaceNetShowdown.Server")) {
    $certificateDirectory = Join-Path $repoRoot "src\RaceNetShowdown.Server\certs"
}
$certificatePath = Join-Path $certificateDirectory "codemasters-local-root-ca.cer"
$bundledPatcher = Join-Path $PSScriptRoot "RaceNetShowdown.Patcher\RaceNetShowdown.Patcher.exe"
$sourcePatcherProject = Join-Path $repoRoot "src\RaceNetShowdown.Patcher"

Write-Host "EgoNet Revival DiRT Showdown installer" -ForegroundColor Green
Write-Host "Server: $Server"
Write-Host "Game path: $GamePath"

if (-not (Test-Path -LiteralPath $GamePath)) {
    throw "Game folder not found: $GamePath"
}

Write-Step "Closing DiRT Showdown if it is running"
Get-Process showdown, showdown_avx -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Step "Updating Windows hosts file"
Update-HostsFile

Write-Step "Flushing DNS cache"
& ipconfig.exe /flushdns | Out-Host

Write-Step "Downloading server root certificate"
New-Item -ItemType Directory -Force -Path $certificateDirectory | Out-Null
Invoke-WebRequest -Uri "http://$Server/racenet-root-ca.cer" -OutFile $certificatePath
& certutil.exe -hashfile $certificatePath SHA256 | Out-Host

Write-Step "Installing root certificate"
Install-RootCertificate -CertificatePath $certificatePath

Write-Step "Patching game executables"
if (Test-Path -LiteralPath $bundledPatcher) {
    & $bundledPatcher patch $GamePath $certificatePath
} elseif (Test-Path -LiteralPath $sourcePatcherProject) {
    dotnet run --project $sourcePatcherProject -- patch $GamePath $certificatePath
} else {
    throw "Patcher executable not found. Expected: $bundledPatcher"
}

Write-Step "Verifying HTTPS connection"
Test-HealthEndpoint

Write-Host ""
Write-Host "Done. Open DiRT Showdown and enter RaceNet." -ForegroundColor Green
