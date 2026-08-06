@echo off
setlocal
set "EGONET_INSTALLER=%~f0"
set "EGONET_ARG_SERVER=%~1"
set "EGONET_ARG_GAMEPATH=%~2"

%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $path=$env:EGONET_INSTALLER; $raw=[IO.File]::ReadAllText($path); $marker='### POWERSHELL ###'; $index=$raw.LastIndexOf($marker); if($index -lt 0){ throw 'PowerShell installer marker not found.' }; $code=$raw.Substring($index + $marker.Length); & ([scriptblock]::Create($code))"
set "EGONET_EXIT=%ERRORLEVEL%"
pause
exit /b %EGONET_EXIT%

### POWERSHELL ###
$ErrorActionPreference = "Stop"

$DefaultServer = "142.93.206.37"
$DefaultGamePath = "C:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown"
$ExpectedRootCertificateLength = 1003

$Server = $env:EGONET_ARG_SERVER
if ([string]::IsNullOrWhiteSpace($Server)) {
    $Server = $DefaultServer
}

$GamePath = $env:EGONET_ARG_GAMEPATH
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    $GamePath = $DefaultGamePath
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting Administrator permission..."
    $scriptPath = $env:EGONET_INSTALLER
    $args = @("/c", "`"$scriptPath`"", $Server, "`"$GamePath`"")
    Start-Process -FilePath $env:ComSpec -ArgumentList $args -Verb RunAs
    exit 0
}

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Test-BytesEqual([byte[]]$Left, [byte[]]$Right) {
    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }

    return $true
}

function Get-DerSequenceLength([byte[]]$Bytes, [int]$Offset) {
    if ($Bytes[$Offset] -ne 0x30 -or $Offset + 1 -ge $Bytes.Length) {
        return $null
    }

    $marker = [int]$Bytes[$Offset + 1]
    if (($marker -band 0x80) -eq 0) {
        return 2 + $marker
    }

    $lengthBytes = $marker -band 0x7f
    if ($lengthBytes -le 0 -or $lengthBytes -gt 4 -or $Offset + 2 + $lengthBytes -gt $Bytes.Length) {
        return $null
    }

    $length = 0
    for ($index = 0; $index -lt $lengthBytes; $index++) {
        $length = ($length -shl 8) -bor [int]$Bytes[$Offset + 2 + $index]
    }

    return 2 + $lengthBytes + $length
}

function Test-ContainsAscii([byte[]]$Bytes, [string]$Text) {
    $pattern = [Text.Encoding]::ASCII.GetBytes($Text)
    if ($Bytes.Length -lt $pattern.Length) {
        return $false
    }

    for ($offset = 0; $offset -le $Bytes.Length - $pattern.Length; $offset++) {
        $matched = $true
        for ($index = 0; $index -lt $pattern.Length; $index++) {
            if ($Bytes[$offset + $index] -ne $pattern[$index]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            return $true
        }
    }

    return $false
}

function Find-CodemastersRootCertificates([byte[]]$Bytes) {
    $results = New-Object System.Collections.Generic.List[object]

    for ($offset = 0; $offset -lt $Bytes.Length - 8; $offset++) {
        if ($Bytes[$offset] -ne 0x30) {
            continue
        }

        $length = Get-DerSequenceLength -Bytes $Bytes -Offset $offset
        if ($null -eq $length -or $length -lt 256 -or $length -gt 8192 -or $offset + $length -gt $Bytes.Length) {
            continue
        }

        $candidateBytes = New-Object byte[] $length
        [Array]::Copy($Bytes, $offset, $candidateBytes, 0, $length)

        if (-not (Test-ContainsAscii -Bytes $candidateBytes -Text "Codemasters")) {
            continue
        }

        try {
            $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(,$candidateBytes)
        }
        catch {
            continue
        }

        if ($certificate.Subject.IndexOf("CN=Codemasters Online Root CA", [StringComparison]::OrdinalIgnoreCase) -ge 0 `
            -and $certificate.Subject.IndexOf("OU=Codemasters Online", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $results.Add([pscustomobject]@{
                Offset = $offset
                Bytes = $candidateBytes
                Certificate = $certificate
            }) | Out-Null

            $offset += $length - 1
        }
    }

    return $results
}

function Patch-Executable([string]$ExecutablePath, [byte[]]$RootBytes) {
    if (-not (Test-Path -LiteralPath $ExecutablePath)) {
        Write-Host "Skipping missing file: $ExecutablePath"
        return
    }

    Write-Host "Patching $([IO.Path]::GetFileName($ExecutablePath))..."
    $bytes = [IO.File]::ReadAllBytes($ExecutablePath)
    $candidates = Find-CodemastersRootCertificates -Bytes $bytes

    if ($candidates.Count -eq 0) {
        Write-Host "$([IO.Path]::GetFileName($ExecutablePath)): Codemasters root CA not found."
        return
    }

    $changed = $false
    foreach ($candidate in $candidates) {
        if (Test-BytesEqual -Left $candidate.Bytes -Right $RootBytes) {
            Write-Host "$([IO.Path]::GetFileName($ExecutablePath)): already patched at 0x$($candidate.Offset.ToString('x'))."
            continue
        }

        if ($candidate.Bytes.Length -ne $RootBytes.Length) {
            Fail "$([IO.Path]::GetFileName($ExecutablePath)): embedded certificate length is $($candidate.Bytes.Length), but server root certificate is $($RootBytes.Length)."
        }

        $backupPath = "$ExecutablePath.racenet-original.bak"
        if (-not (Test-Path -LiteralPath $backupPath)) {
            Copy-Item -LiteralPath $ExecutablePath -Destination $backupPath
            Write-Host "$([IO.Path]::GetFileName($ExecutablePath)): backup written."
        }

        [Array]::Copy($RootBytes, 0, $bytes, $candidate.Offset, $RootBytes.Length)
        $changed = $true
        Write-Host "$([IO.Path]::GetFileName($ExecutablePath)): patched at 0x$($candidate.Offset.ToString('x'))."
    }

    if ($changed) {
        [IO.File]::WriteAllBytes($ExecutablePath, $bytes)
    }
}

Write-Host "EgoNet Revival - DiRT Showdown installer"
Write-Host "Server: $Server"
Write-Host "Game path: $GamePath"

if (-not (Test-Path -LiteralPath $GamePath -PathType Container)) {
    Fail "Game folder not found: $GamePath"
}

$runningGame = Get-Process -Name "showdown", "showdown_avx" -ErrorAction SilentlyContinue
if ($runningGame) {
    Write-Host "Closing DiRT Showdown before patching..."
    $runningGame | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$hostNames = @(
    "prod.egonet.codemasters.com",
    "egonet.codemasters.com",
    "racenet.codemasters.com",
    "api.racenet.codemasters.com",
    "showdown.racenet.codemasters.com",
    "racenet.com",
    "www.racenet.com",
    "api.racenet.com"
)

$hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
$hostsItem = Get-Item -LiteralPath $hostsPath -ErrorAction Stop
if (($hostsItem.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
    $hostsItem.Attributes = $hostsItem.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)
}

$existingHosts = [IO.File]::ReadAllLines($hostsPath)
$cleanHosts = New-Object System.Collections.Generic.List[string]

foreach ($line in $existingHosts) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "# EgoNet Revival DiRT Showdown") {
        continue
    }

    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
        $cleanHosts.Add($line) | Out-Null
        continue
    }

    $parts = $trimmed -split "\s+"
    if ($parts.Length -ge 2 -and $hostNames -contains $parts[1].ToLowerInvariant()) {
        continue
    }

    $cleanHosts.Add($line) | Out-Null
}

$cleanHosts.Add("") | Out-Null
$cleanHosts.Add("# EgoNet Revival DiRT Showdown") | Out-Null
foreach ($hostName in $hostNames) {
    $cleanHosts.Add("$Server $hostName") | Out-Null
}

[IO.File]::WriteAllLines($hostsPath, [string[]]$cleanHosts.ToArray(), [Text.Encoding]::ASCII)
Write-Host "Windows hosts file updated."

ipconfig /flushdns | Out-Null
Write-Host "DNS cache flushed."

$downloadDirectory = Join-Path $env:TEMP "egonet-revival"
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
$certificatePath = Join-Path $downloadDirectory "codemasters-local-root-ca.cer"
$certificateUrl = "http://$Server/racenet-root-ca.cer"

Write-Host "Downloading root certificate from $certificateUrl"
Invoke-WebRequest -Uri $certificateUrl -OutFile $certificatePath -UseBasicParsing

$rootBytes = [IO.File]::ReadAllBytes($certificatePath)
if ($rootBytes.Length -ne $ExpectedRootCertificateLength) {
    Fail "Server root certificate must be $ExpectedRootCertificateLength bytes for in-place patching, but it is $($rootBytes.Length) bytes."
}

certutil -addstore -f Root $certificatePath | Out-Host
certutil -urlcache * delete | Out-Null
Write-Host "Root certificate installed."

Write-Host "Patching game executables..."
Patch-Executable -ExecutablePath (Join-Path $GamePath "showdown.exe") -RootBytes $rootBytes
Patch-Executable -ExecutablePath (Join-Path $GamePath "showdown_avx.exe") -RootBytes $rootBytes

Write-Host "Testing HTTPS health endpoint..."
& curl.exe -fsS --ssl-no-revoke "https://prod.egonet.codemasters.com/health" | Out-Host

Write-Host ""
Write-Host "Done. Open DiRT Showdown and enter RaceNet."
