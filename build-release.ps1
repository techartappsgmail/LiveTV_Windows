# Build Release Script for Live TV
# This script builds self-contained x64 and ARM64 releases

param(
    [ValidateSet("all", "win-x64", "win-arm64")]
    [string]$Runtime = "all"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "IPTV-Player.csproj"
$publishRoot = Join-Path $PSScriptRoot "publish"
$runtimeIdentifiers = if ($Runtime -eq "all") { @("win-x64", "win-arm64") } else { @($Runtime) }
$releaseSummaries = @()

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Live TV - Release Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean $projectPath -c Release --nologo -v q
foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    $runtimeOutput = Join-Path $publishRoot $runtimeIdentifier
    if (Test-Path $runtimeOutput) {
        Remove-Item -Path $runtimeOutput -Recurse -Force
    }
}
Write-Host "  Done." -ForegroundColor Green

foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    $runtimeOutput = Join-Path $publishRoot $runtimeIdentifier
    $selfContainedOutput = Join-Path $runtimeOutput "self-contained"
    $singleFileOutput = Join-Path $runtimeOutput "single-file"
    $executableName = if ($runtimeIdentifier -eq "win-arm64") { "LiveTV-arm64.exe" } else { "LiveTV.exe" }

    Write-Host "Restoring packages for $runtimeIdentifier..." -ForegroundColor Yellow
    dotnet restore $projectPath -r $runtimeIdentifier --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Restore failed for $runtimeIdentifier!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Done." -ForegroundColor Green

    Write-Host "Publishing $runtimeIdentifier self-contained folder..." -ForegroundColor Yellow
    dotnet publish $projectPath -c Release -r $runtimeIdentifier --self-contained true -p:PublishSingleFile=false -o $selfContainedOutput --no-restore --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Self-contained publish failed for $runtimeIdentifier!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Done." -ForegroundColor Green

    # Native VLC libraries and plugins are extracted at run time.
    Write-Host "Publishing $runtimeIdentifier self-contained single file..." -ForegroundColor Yellow
    dotnet publish $projectPath -c Release -r $runtimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $singleFileOutput --no-restore --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Single-file publish failed for $runtimeIdentifier!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Done." -ForegroundColor Green

    $releaseSummaries += [pscustomobject]@{
        Runtime = $runtimeIdentifier
        ExecutableName = $executableName
        FolderSize = (Get-ChildItem -Path $selfContainedOutput -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
        SingleFileSize = (Get-Item (Join-Path $singleFileOutput $executableName)).Length / 1MB
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output locations:" -ForegroundColor White

foreach ($summary in $releaseSummaries) {
    Write-Host "  $($summary.Runtime) folder:      publish/$($summary.Runtime)/self-contained/ ($([math]::Round($summary.FolderSize, 2)) MB)" -ForegroundColor Yellow
    Write-Host "  $($summary.Runtime) single file: publish/$($summary.Runtime)/single-file/$($summary.ExecutableName) ($([math]::Round($summary.SingleFileSize, 2)) MB)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "All releases include the .NET runtime and do not require .NET to be installed." -ForegroundColor Gray
Write-Host "The single-file release extracts native VLC components when it starts." -ForegroundColor Gray
Write-Host "Use win-x64 on Intel/AMD PCs and win-arm64 on ARM64 PCs." -ForegroundColor DarkGray
