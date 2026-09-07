#Requires -Version 5.1
<#
.SYNOPSIS
    Builds SynToolkit and creates an installer.
.DESCRIPTION
    This script builds the SynToolkit application in Release mode and creates
    an installer using Inno Setup.
.PARAMETER SkipBuild
    Skip the build step and only create the installer (assumes build already exists).
.PARAMETER SkipInstaller
    Skip creating the installer (only build the application).
#>
param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  SynToolkit Build and Installer Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "SynToolkit"
$ProjectFile = Join-Path $ProjectDir "SynToolkit.csproj"
$PublishDir = Join-Path $ScriptDir "artifacts\publish"
$OutputDir = Join-Path $ScriptDir "Installer\Output"
$InstallerDir = Join-Path $ScriptDir "Installer"
$InstallerScript = Join-Path $InstallerDir "setup.iss"

# Check if dotnet is available
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetPath) {
    Write-Host "ERROR: .NET SDK not found. Please install .NET 8 SDK." -ForegroundColor Red
    Write-Host "Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found .NET SDK: $(dotnet --version)" -ForegroundColor Green
Write-Host ""

if (-not $SkipBuild) {
    # Clean previous builds
    Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
    $releaseDir = Join-Path $ProjectDir "bin\x64\Release"
    $objReleaseDir = Join-Path $ProjectDir "obj\x64\Release"
    
    if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
    if (Test-Path $objReleaseDir) { Remove-Item $objReleaseDir -Recurse -Force }
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Done." -ForegroundColor Green
    Write-Host ""

    # Restore packages
    Write-Host "[2/4] Restoring NuGet packages..." -ForegroundColor Yellow
    dotnet restore $ProjectFile -r win-x64
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to restore packages." -ForegroundColor Red
        exit 1
    }
    Write-Host "Done." -ForegroundColor Green
    Write-Host ""

    # Build and publish
    Write-Host "[3/4] Building and publishing SynToolkit (Release, x64)..." -ForegroundColor Yellow
    dotnet publish $ProjectFile `
        -c Release `
        -r win-x64 `
        -p:Platform=x64 `
        -p:PublishSingleFile=false `
        -p:SelfContained=true `
        -p:PublishReadyToRun=true `
        --output $PublishDir
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "Done." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1-3/4] Skipping build (using existing build)..." -ForegroundColor Yellow
    Write-Host ""
}

if (-not $SkipInstaller) {
    # Find Inno Setup
    Write-Host "[4/4] Creating installer..." -ForegroundColor Yellow
    
    $innoSearchPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    
    $innoPath = $null
    foreach ($path in $innoSearchPaths) {
        if (Test-Path $path) {
            $innoPath = $path
            break
        }
    }
    
    if ($innoPath) {
        Write-Host "Found Inno Setup at: $innoPath" -ForegroundColor Green
        & $innoPath $InstallerScript
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "============================================" -ForegroundColor Green
            Write-Host "  BUILD COMPLETE!" -ForegroundColor Green
            Write-Host "============================================" -ForegroundColor Green
            Write-Host ""
            Write-Host "Installer created at:" -ForegroundColor Cyan
            Write-Host "  $OutputDir\SynToolkit-Setup-1.7.0.exe" -ForegroundColor White
            Write-Host ""
            Write-Host "Published files at:" -ForegroundColor Cyan
            Write-Host "  $PublishDir" -ForegroundColor White
        } else {
            Write-Host "WARNING: Installer creation failed." -ForegroundColor Yellow
        }
    } else {
        Write-Host ""
        Write-Host "WARNING: Inno Setup not found." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To create the installer:" -ForegroundColor Cyan
        Write-Host "  1. Download Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor White
        Write-Host "  2. Install it" -ForegroundColor White
        Write-Host "  3. Run this script again, OR" -ForegroundColor White
        Write-Host "  4. Open Installer\setup.iss in Inno Setup and compile" -ForegroundColor White
        Write-Host ""
        Write-Host "============================================" -ForegroundColor Green
        Write-Host "  BUILD COMPLETE (without installer)" -ForegroundColor Green
        Write-Host "============================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Published files at:" -ForegroundColor Cyan
        Write-Host "  $PublishDir" -ForegroundColor White
        Write-Host ""
        Write-Host "You can run SynToolkit.exe directly from that folder." -ForegroundColor White
    }
} else {
    Write-Host "[4/4] Skipping installer creation..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  BUILD COMPLETE (without installer)" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Published files at:" -ForegroundColor Cyan
    Write-Host "  $PublishDir" -ForegroundColor White
}

Write-Host ""
