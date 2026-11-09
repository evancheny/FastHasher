# FastHasher Build Script
$ErrorActionPreference = "Stop"

Write-Host "FastHasher Build Script" -ForegroundColor Cyan
Write-Host ""

# Parse arguments
$mode = "Standard"
$clean = $false

if ($args -contains "clean") {
    $clean = $true
}

if ($args -contains "small") {
    $mode = "Small"
} elseif ($args -contains "tiny") {
    $mode = "Tiny"
}

# Clean if requested
if ($clean) {
    Write-Host "Cleaning..." -ForegroundColor Yellow
    Remove-Item -Path "bin", "obj", "dist" -Recurse -Force -ErrorAction SilentlyContinue
}

# Set build arguments
$buildArgs = @()
switch ($mode) {
    "Small" {
        Write-Host "Mode: Small - Reduced size" -ForegroundColor Yellow
        $buildArgs += "-p:PublishTrimmed=true", "-p:TrimMode=link"
    }
    "Tiny" {
        Write-Host "Mode: Tiny - Maximum size reduction" -ForegroundColor Yellow
        $buildArgs += "-p:PublishTrimmed=true", "-p:TrimMode=link", "-p:EnableCompressionInSingleFile=true"
    }
    Default {
        Write-Host "Mode: Standard - Full features" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Building..." -ForegroundColor Green

# Build the project
$buildArgs += "publish", "src\FastHasher\FastHasher.csproj", 
              "--configuration", "Release", 
              "--runtime", "win-x64", 
              "--self-contained", 
              "--output", ".\dist",
              "-p:PublishSingleFile=true",
              "-p:DebugType=none"

& dotnet $buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build success!" -ForegroundColor Green
Write-Host ""

# Get version and rename exe
if (Test-Path "dist\FastHasher.exe") {
    # Get version from exe
    $versionOutput = & "dist\FastHasher.exe" --version 2>&1 | Out-String

    # Extract version number after "v" - support both 1.2.0 and 1.2.0.0 formats
    $match = $versionOutput | Select-String "v(\d+\.\d+\.\d+(?:\.\d+)?)"
    if ($match) {
        $version = $match.Matches[0].Groups[1].Value
        # Convert 1.2.0.0 to 1.2.0
        $versionParts = $version -split '\.'
        if ($versionParts.Length -gt 3) {
            $simpleVersion = $versionParts[0..2] -join '.'
        } else {
            $simpleVersion = $version
        }
        Write-Host "Version: $simpleVersion" -ForegroundColor Cyan

        # Rename exe with version
        $newExeName = "FastHasher-v$simpleVersion.exe"
        Rename-Item -Path "dist\FastHasher.exe" -NewName $newExeName

        $exePath = "dist\$newExeName"
        
        # Display file info
        $fileInfo = Get-Item $exePath
        Write-Host "Size: $($fileInfo.Length) bytes" -ForegroundColor Cyan
        Write-Host "Output: $exePath" -ForegroundColor Cyan
        Write-Host ""
        
        # Test the exe
        Write-Host "Testing..." -ForegroundColor Yellow
        & $exePath --version
        Write-Host ""
        
        Write-Host "Usage:" -ForegroundColor Cyan
        Write-Host "  $newExeName <file|directory> [options]"
        Write-Host "  $newExeName -h"
        Write-Host ""
        Write-Host "Build Modes:" -ForegroundColor Cyan
        Write-Host "  build.ps1         - Standard"
        Write-Host "  build.ps1 small   - Small"
        Write-Host "  build.ps1 tiny    - Tiny"
        Write-Host "  build.ps1 clean   - Clean first"
    }
}

Write-Host ""
exit 0
