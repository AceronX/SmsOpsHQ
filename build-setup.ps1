# Build SmsOps HQ for store deployment:
#   1) Publish self-contained Desktop + API (publish-store.ps1)
#   2) Compile Windows setup.exe with Inno Setup 6 (if installed)
#
# Output:
#   SmsOpsHQ.Desktop\bin\Publish\Store\              — portable folder (ZIP also created)
#   SmsOpsHQ.Desktop\bin\Publish\SmsOpsHQ-Setup-1.0.0-x64.exe — installer (when Inno Setup is present)
#
# Usage (from repo root):
#   .\build-setup.ps1
#   .\build-setup.ps1 -SkipPublish          # reuse existing Publish\Store
#   .\build-setup.ps1 -AppVersion "1.2.0"

param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $AppVersion = "1.0.0",
    [switch] $SkipPublish,
    [switch] $SkipPrerequisiteCheck
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$publishScript = Join-Path $repoRoot "SmsOpsHQ.Desktop\publish-store.ps1"
$publishDir = Join-Path $repoRoot "SmsOpsHQ.Desktop\bin\Publish\Store"
$issPath = Join-Path $repoRoot "installer\SmsOpsHQ.iss"
$publishOutDir = Join-Path $repoRoot "SmsOpsHQ.Desktop\bin\Publish"

if (-not $SkipPublish) {
    if (-not (Test-Path $publishScript)) {
        throw "Missing publish script: $publishScript"
    }
    Write-Host "=== Step 1: Publish Desktop + API ===" -ForegroundColor Cyan
    $publishArgs = @{
        Configuration = $Configuration
        Runtime       = $Runtime
    }
    if ($SkipPrerequisiteCheck) { $publishArgs.SkipPrerequisiteCheck = $true }
    & $publishScript @publishArgs
}

if (-not (Test-Path (Join-Path $publishDir "SmsOpsHQ.Desktop.exe"))) {
    throw "Publish output not found. Run without -SkipPublish or fix publish-store.ps1. Expected: $publishDir"
}

Write-Host ""
Write-Host "=== Step 2: Build Windows installer (Inno Setup) ===" -ForegroundColor Cyan

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 is not installed - setup.exe was NOT built." -ForegroundColor Yellow
    Write-Host "Install: https://jrsoftware.org/isdl.php  (default path, include ISCC.exe)"
    Write-Host "Then run this script again."
    Write-Host ""
    Write-Host "You can still deploy the portable build:" -ForegroundColor Green
    Write-Host "  Folder: $publishDir"
    $zip = Join-Path $publishOutDir "SmsOpsHQ-Desktop-StoreTest.zip"
    if (Test-Path $zip) { Write-Host "  ZIP:    $zip" }
    Write-Host ""
    Write-Host "Or compile the installer manually:"
    Write-Host "  `"$($isccCandidates[0])`" `"$issPath`" /DPublishDir=`"$publishDir`" /DMyAppVersion=`"$AppVersion`""
    exit 0
}

$publishDirForIss = $publishDir.Replace('\', '\\')
& $iscc $issPath "/DPublishDir=$publishDir" "/DMyAppVersion=$AppVersion"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed (exit $LASTEXITCODE)"
}

$setupExe = Join-Path $publishOutDir "SmsOpsHQ-Setup-$AppVersion-x64.exe"
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Portable: $publishDir"
Write-Host "  Installer: $setupExe"
if (Test-Path $setupExe) {
    $sizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
    Write-Host ('  Size:     {0} MB' -f $sizeMb)
}
