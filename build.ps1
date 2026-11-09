# FastHasher Build Script
$ErrorActionPreference = "Stop"

Write-Host "FastHasher Build Script" -ForegroundColor Cyan
Write-Host ""

# Parse arguments
$mode = "Standard"
$clean = $false
$rid = "win-x64"

foreach ($a in $args) {
    switch -Wildcard ($a) {
        "clean"   { $clean = $true }
        "small"   { $mode = "Small" }
        "tiny"    { $mode = "Tiny" }
        "*-x64"   { $rid = $a }
        "*-arm64" { $rid = $a }
        "*-musl*" { $rid = $a }
    }
}

# Clean if requested
if ($clean) {
    Write-Host "Cleaning..." -ForegroundColor Yellow
    Remove-Item -Path "src\FastHasher\bin", "src\FastHasher\obj", "dist" -Recurse -Force -ErrorAction SilentlyContinue
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
              "--runtime", $rid, 
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

# 版本号从 csproj 读取：交叉编译产物无法在本机执行，不能靠运行产物取版本
$suffix = if ($rid -like "win-*") { ".exe" } else { "" }
$binPath = "dist\FastHasher$suffix"

if (Test-Path $binPath) {
    $csproj = [xml](Get-Content "src\FastHasher\FastHasher.csproj" -Raw)
    $simpleVersion = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)

    if (-not $simpleVersion) {
        Write-Host "无法从 csproj 解析版本号" -ForegroundColor Red
        exit 1
    }

    Write-Host "Version: $simpleVersion" -ForegroundColor Cyan

    $newExeName = "FastHasher-v$simpleVersion$suffix"
    $exePath = "dist\$newExeName"

    # Windows 上 Rename-Item 不会覆盖已存在的目标，重复构建需先删除
    if (Test-Path $exePath) { Remove-Item $exePath -Force }
    Rename-Item -Path $binPath -NewName $newExeName

    # Display file info
    $fileInfo = Get-Item $exePath
    Write-Host "Size: $($fileInfo.Length) bytes" -ForegroundColor Cyan
    Write-Host "Output: $exePath" -ForegroundColor Cyan
    Write-Host ""

    if ($suffix -eq ".exe") {
        # Test the exe
        Write-Host "Testing..." -ForegroundColor Yellow
        & $exePath --version
        Write-Host ""
    }
    else {
        Write-Host "提示: 交叉编译产物无法在 Windows 上运行，请复制到目标平台验证。" -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host "Usage:" -ForegroundColor Cyan
    Write-Host "  $newExeName <file|directory> [options]"
    Write-Host "  $newExeName -h"
    Write-Host ""
    Write-Host "Build Modes:" -ForegroundColor Cyan
    Write-Host "  build.ps1             - Standard (win-x64)"
    Write-Host "  build.ps1 small       - Small"
    Write-Host "  build.ps1 tiny        - Tiny"
    Write-Host "  build.ps1 clean       - Clean first"
    Write-Host "  build.ps1 linux-x64   - 交叉编译 Linux x64"
}

Write-Host ""
exit 0
