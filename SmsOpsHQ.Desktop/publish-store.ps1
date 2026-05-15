# Build a self-contained single-file Desktop + bundled API for the store PC (or same PC).
# Prerequisites: .NET 8 SDK (this script can install it via winget if missing).
# Output: SmsOpsHQ.Desktop\bin\Publish\Store\  (Desktop exe + api\SmsOpsHQ.Api.exe) and  ...\SmsOpsHQ-Desktop-StoreTest.zip
# Run from either the repo root (next to SmsOpsHQ.Desktop\) or from inside SmsOpsHQ.Desktop\.
#
# Store / same PC: extract the ZIP, run SmsOpsHQ.Desktop.exe; the API starts in the background (no console)
# when LocalApi:AutoStart is true and ApiBaseUrl is localhost/127.0.0.1. This script sets that on the published appsettings.
# For dev (Visual Studio): keep LocalApi:AutoStart false in source appsettings.json so port 5000 is not double-bound.

param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [switch] $SkipPrerequisiteCheck
)

$ErrorActionPreference = "Stop"

function Refresh-SessionPath {
    $machine = [System.Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Test-DotNet8Sdk {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) { return $false }
    $sdks = & dotnet --list-sdks 2>$null
    return $null -ne ($sdks | Where-Object { $_ -match '^\s*8\.' })
}

function Install-DotNet8Sdk {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Host ""
        Write-Host "winget is not available. Install manually:"
        Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0  (SDK x64)"
        Write-Host "Then open a new PowerShell window and run this script again."
        exit 1
    }

    Write-Host ""
    Write-Host "Installing Microsoft .NET SDK 8 via winget (UAC may appear)..."
    & winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
        Write-Host "winget exit code: $LASTEXITCODE (0 often means success; continuing if dotnet works)"
    }

    Refresh-SessionPath
}

if (-not $SkipPrerequisiteCheck) {
    if (-not (Test-DotNet8Sdk)) {
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            Write-Host ".NET 8 SDK not detected (run: dotnet --list-sdks)."
        } else {
            Write-Host "dotnet CLI not found on PATH."
        }
        Install-DotNet8Sdk
    }

    if (-not (Test-DotNet8Sdk)) {
        Write-Host ""
        Write-Host "dotnet still missing or no 8.x SDK after install."
        Write-Host "1) Close this window, open a new PowerShell (refreshes PATH), run again."
        Write-Host "2) Or install SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }

    Write-Host "Prerequisites OK: $(dotnet --version)"
    Write-Host ""
}

$scriptDir = $PSScriptRoot
$projInDesktop = Join-Path $scriptDir "SmsOpsHQ.Desktop.csproj"
$projUnderRepo = Join-Path $scriptDir "SmsOpsHQ.Desktop\SmsOpsHQ.Desktop.csproj"

if (Test-Path $projInDesktop) {
    $root = $scriptDir
    $proj = $projInDesktop
    $repoRoot = Split-Path $root -Parent
} elseif (Test-Path $projUnderRepo) {
    $root = Join-Path $scriptDir "SmsOpsHQ.Desktop"
    $proj = $projUnderRepo
    $repoRoot = $scriptDir
} else {
    throw @"
Could not find SmsOpsHQ.Desktop.csproj.
Expected one of:
  $projInDesktop
  $projUnderRepo
Copy the whole repo (or at least SmsOpsHQ.Desktop + SmsOpsHQ.Api + dependencies), not only this script.
"@
}

$apiProj = Join-Path $repoRoot "SmsOpsHQ.Api\SmsOpsHQ.Api.csproj"
$outDir = Join-Path $root "bin\Publish\Store"
$apiOutDir = Join-Path $outDir "api"
$zipPath = Join-Path $root "bin\Publish\SmsOpsHQ-Desktop-StoreTest.zip"
if (-not (Test-Path $apiProj)) {
    throw "API project not found: $apiProj"
}

Write-Host "Publishing Desktop to $outDir ..."
dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $outDir

Write-Host "Publishing API to $apiOutDir (silent WinExe, self-contained folder) ..."
dotnet publish $apiProj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $apiOutDir

$desktopSettings = Join-Path $outDir "appsettings.json"
if (Test-Path $desktopSettings) {
    $cfg = Get-Content -LiteralPath $desktopSettings -Raw -Encoding UTF8 | ConvertFrom-Json
    $cfg.ApiBaseUrl = "http://127.0.0.1:5000"
    if ($cfg.PSObject.Properties.Name -notcontains 'LocalApi') {
        $cfg | Add-Member -NotePropertyName LocalApi -NotePropertyValue ([pscustomobject]@{
            AutoStart = $true
            ExecutableRelativePath = "api\SmsOpsHQ.Api.exe"
        })
    } else {
        $cfg.LocalApi.AutoStart = $true
        $cfg.LocalApi.ExecutableRelativePath = "api\SmsOpsHQ.Api.exe"
    }
    ($cfg | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $desktopSettings -Encoding UTF8
    Write-Host ("Patched {0} - LocalApi AutoStart=true, ApiBaseUrl=http://127.0.0.1:5000" -f $desktopSettings)
} else {
    Write-Warning "appsettings.json not found under publish output; skip LocalApi patch."
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path (Split-Path $zipPath) -Force | Out-Null
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Done."
Write-Host "  Folder: $outDir"
Write-Host "  ZIP:    $zipPath"
Write-Host "Run SmsOpsHQ.Desktop.exe - local API starts hidden if not already listening on that URL."
