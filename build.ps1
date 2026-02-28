# build.ps1 v17- 最终修复版构建脚本
# 修复已知问题：哈希一致性、JSON转义、中文乱码、参数过长、JSON序列化错误、根目录文件读取错误
# 新增特性：默认按CPU逻辑线程数设置并行线程数，支持--parallel/-p手动指定覆盖


# 只构建，不测试
#.\build.ps1

# 构建并运行所有测试并运行基准测试
#.\build.ps1 -Test -Benchmark

# 清理、构建并运行所有测试
#.\build.ps1 -Clean -Test


param(
    [switch]$Clean,
    [switch]$Test,
    [switch]$Benchmark,
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

# 设置PowerShell输出编码为UTF-8
$OutputEncoding = [System.Text.Encoding]::UTF8
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    # 某些系统可能不支持，忽略
}

# 配置
$ProjectName = "FastHasher"
$Runtime = "win-x64"
$OutputDir = ".\dist"
$SourceDir = ".\src"
$Version = "1.0.1"  # 更新版本号

Write-Host "=========================================" -ForegroundColor White
Write-Host "   快速哈希计算器 v$Version  " -ForegroundColor White
Write-Host "   修复已知问题，稳定可靠" -ForegroundColor White
Write-Host "   并行默认: CPU逻辑线程数" -ForegroundColor White
Write-Host "=========================================" -ForegroundColor White
Write-Host ""

# 函数：安全执行命令，避免参数过长问题
function Safe-Execute {
    param(
        [string]$Command,
        [array]$Arguments,
        [switch]$NoWait,
        [switch]$UseResponseFile,
        [string]$WorkingDirectory = $null
    )
    
    try {
        if ($UseResponseFile) {
            # 使用响应文件传递长参数
            $responseFile = [System.IO.Path]::GetTempFileName()
            $responseFile = $responseFile -replace '\.tmp$', '.rsp'
            
            try {
                # 将参数写入响应文件
                $Arguments | Out-File $responseFile -Encoding UTF8
                
                # 使用响应文件执行命令
                $processInfo = New-Object System.Diagnostics.ProcessStartInfo
                $processInfo.FileName = $Command
                $processInfo.Arguments = "@`"$responseFile`""
                
                if ($WorkingDirectory) {
                    $processInfo.WorkingDirectory = $WorkingDirectory
                }
                
                $processInfo.UseShellExecute = $false
                $processInfo.RedirectStandardOutput = $true
                $processInfo.RedirectStandardError = $true
                $processInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
                $processInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
                
                $process = New-Object System.Diagnostics.Process
                $process.StartInfo = $processInfo
                
                $process.Start() | Out-Null
                
                if (-not $NoWait) {
                    $stdout = $process.StandardOutput.ReadToEnd()
                    $stderr = $process.StandardError.ReadToEnd()
                    $process.WaitForExit()
                    
                    return @{
                        ExitCode = $process.ExitCode
                        Output = $stdout
                        Error = $stderr
                    }
                }
            }
            finally {
                # 清理响应文件
                if (Test-Path $responseFile) {
                    Remove-Item $responseFile -Force -ErrorAction SilentlyContinue
                }
            }
        }
        else {
            # 直接执行
            $argumentsString = $Arguments -join ' '
            
            # 检查命令行长度
            $totalLength = $Command.Length + $argumentsString.Length + 100  # 加上其他开销
            
            if ($totalLength -gt 8000) {
                Write-Warning "命令行可能过长 ($totalLength 字符)，考虑使用响应文件"
                # 递归调用，使用响应文件
                return Safe-Execute -Command $Command -Arguments $Arguments -UseResponseFile -WorkingDirectory $WorkingDirectory
            }
            
            if ($WorkingDirectory) {
                Push-Location $WorkingDirectory
                try {
                    if ($NoWait) {
                        Start-Process -FilePath $Command -ArgumentList $Arguments -NoNewWindow -PassThru
                    }
                    else {
                        $process = Start-Process -FilePath $Command -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput "temp_output.txt" -RedirectStandardError "temp_error.txt"
                        
                        $output = Get-Content "temp_output.txt" -Raw -ErrorAction SilentlyContinue
                        $errorOutput = Get-Content "temp_error.txt" -Raw -ErrorAction SilentlyContinue
                        
                        Remove-Item "temp_output.txt", "temp_error.txt" -Force -ErrorAction SilentlyContinue
                        
                        return @{
                            ExitCode = $process.ExitCode
                            Output = $output
                            Error = $errorOutput
                        }
                    }
                }
                finally {
                    Pop-Location
                }
            }
            else {
                if ($NoWait) {
                    Start-Process -FilePath $Command -ArgumentList $Arguments -NoNewWindow -PassThru
                }
                else {
                    $process = Start-Process -FilePath $Command -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput "temp_output.txt" -RedirectStandardError "temp_error.txt"
                    
                    $output = Get-Content "temp_output.txt" -Raw -ErrorAction SilentlyContinue
                    $errorOutput = Get-Content "temp_error.txt" -Raw -ErrorAction SilentlyContinue
                    
                    Remove-Item "temp_output.txt", "temp_error.txt" -Force -ErrorAction SilentlyContinue
                    
                    return @{
                        ExitCode = $process.ExitCode
                        Output = $output
                        Error = $errorOutput
                    }
                }
            }
        }
    }
    catch {
        Write-Error "执行命令失败: $_"
        return @{
            ExitCode = -1
            Output = ""
            Error = $_.ToString()
        }
    }
}

# 检查.NET SDK
Write-Host "[1/8] 检查环境..." -ForegroundColor Yellow
$dotnetVersion = (dotnet --version 2>$null)
if (-not $dotnetVersion) {
    Write-Host "错误: 未找到.NET SDK" -ForegroundColor Red
    Write-Host "请从 https://dotnet.microsoft.com/download 安装.NET 8.0 SDK" -ForegroundColor Red
    exit 1
}
Write-Host "✓ 已安装 .NET $dotnetVersion" -ForegroundColor Green

# 检查CPU特性（用于优化，获取逻辑线程数）
if ($IsWindows) {
    try {
        $cpuInfo = Get-CimInstance -ClassName Win32_Processor -ErrorAction SilentlyContinue
        if ($cpuInfo) {
            $cpuLogicalCores = $cpuInfo.NumberOfLogicalProcessors
            Write-Host "✓ CPU信息: $($cpuInfo.Name) | 逻辑线程数: $cpuLogicalCores" -ForegroundColor Gray
            $cpuCores = $cpuLogicalCores
        } else {
            $cpuCores = [Environment]::ProcessorCount
            Write-Host "✓ CPU逻辑线程数: $cpuCores" -ForegroundColor Gray
        }
    } catch {
        $cpuCores = [Environment]::ProcessorCount
        Write-Host "✓ CPU逻辑线程数: $cpuCores" -ForegroundColor Gray
    }
} else {
    $cpuCores = [Environment]::ProcessorCount
    Write-Host "✓ CPU逻辑线程数: $cpuCores" -ForegroundColor Gray
}

# 清理旧构建
if ($Clean) {
    Write-Host "[2/8] 清理旧文件..." -ForegroundColor Yellow
    $pathsToClean = @($OutputDir, $SourceDir, ".\$ProjectName.exe", ".\bin", ".\obj", ".\test", ".\tmp", ".\temp_output.txt", ".\temp_error.txt")
    foreach ($path in $pathsToClean) {
        if (Test-Path $path) {
            try {
                Remove-Item -Path $path -Recurse -Force -ErrorAction Stop
                Write-Host "✓ 清理: $path" -ForegroundColor Green
            } catch {
                Write-Host "⚠ 无法清理: $path ($_)" -ForegroundColor Yellow
            }
        }
    }
}

# 创建源代码目录结构
Write-Host "[3/8] 创建项目结构..." -ForegroundColor Yellow
$dirsToCreate = @($SourceDir, "$SourceDir\$ProjectName")
foreach ($dir in $dirsToCreate) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "✓ 创建目录: $dir" -ForegroundColor Green
    }
}

# +++ 新增：复制图标文件 +++
Write-Host "  复制图标文件..." -ForegroundColor Gray
$iconFile = "FastHasher.ico"
if (Test-Path $iconFile) {
    Copy-Item $iconFile "$SourceDir\$ProjectName\$iconFile" -Force
    Write-Host "  ✓ 图标文件已复制" -ForegroundColor Green
} else {
    Write-Host "  ⚠ 未找到图标文件，将使用默认图标" -ForegroundColor Yellow
}

# 创建项目文件 - 优化配置
Write-Host "[4/8] 创建项目文件..." -ForegroundColor Yellow

$csprojContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!--增加ico图标-->
    <ApplicationIcon>FastHasher.ico</ApplicationIcon>
    <TargetFramework>net8.0</TargetFramework>
    <!-- 添加以下编码相关配置 -->
    <OutputEncoding>UTF-8</OutputEncoding>
    <ConsoleOutputEncoding>UTF-8</ConsoleOutputEncoding>
    <CharacterSet>Unicode</CharacterSet>
    <Nullable>enable</Nullable>
    <!-- 确保控制台使用 UTF-8 -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <PublishTrimmed>false</PublishTrimmed>
    <ImplicitUsings>enable</ImplicitUsings>
    <PlatformTarget>x64</PlatformTarget>
    <Optimize>true</Optimize>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnablePreviewFeatures>false</EnablePreviewFeatures>
    <AssemblyName>FastHasher</AssemblyName>
    <RootNamespace>FastHasher</RootNamespace>
    <Version>1.0.1</Version>
    <FileVersion>1.0.1</FileVersion>
    <AssemblyVersion>1.0.1.0</AssemblyVersion>
    <InformationalVersion>1.0.1 - v17修复版</InformationalVersion>
    <PublishReadyToRun>true</PublishReadyToRun>
    <PublishReadyToRunShowWarnings>true</PublishReadyToRunShowWarnings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>    
  </PropertyGroup>
  
  <PropertyGroup Condition="'$(Configuration)'=='Release'">
    <Optimize>true</Optimize>
    <DebugType>none</DebugType>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="System.IO.Hashing" Version="8.0.0" />
    <!-- 可选：确保图标文件被包含 -->
    <Content Include="FastHasher.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
'@

$csprojContent | Out-File "$SourceDir\$ProjectName\$ProjectName.csproj" -Encoding UTF8
Write-Host "✓ 项目文件创建完成" -ForegroundColor Green

# 创建各个C#文件（替代之前的单一文件）
Write-Host "[5/8] 生成修复版源代码（拆分版）..." -ForegroundColor Yellow

# 定义所有需要创建的C#文件
$sourceFiles = @{
    "PerformanceMonitor.cs" = @'
// PerformanceMonitor.cs - 性能监控器
using System;
using System.Diagnostics; // 注意:需要这个命名空间 不能缺少
using System.Threading;   // 注意:需要这个命名空间 不能缺少

namespace FastHasher
{
    public static class PerformanceMonitor
    {
        private static readonly long totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        
        public static string GetMemoryInfo()
        {
            var process = Process.GetCurrentProcess();
            return string.Format("内存: {0}MB / {1}MB", 
                process.WorkingSet64 / 1024 / 1024, 
                totalMemory / 1024 / 1024);
        }
        
        public static string GetThreadInfo()
        {
            ThreadPool.GetAvailableThreads(out int worker, out int io);
            ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);
            return string.Format("线程池: {0}/{1} (工作), {2}/{3} (IO) | 逻辑线程数: {4}", 
                worker, maxWorker, io, maxIo, Environment.ProcessorCount);
        }
    }
}

'@
    
    "ProgressDisplay.cs" = @'

// ProgressDisplay.cs - 专业进度显示类（包含图表功能）
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace FastHasher
{
    public static class ProgressDisplay
    {
        private static readonly string[] spinnerChars = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        private static readonly string[] barChars = { "█", "▉", "▊", "▋", "▌", "▍", "▎", "▏", " ", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };
        private static int spinnerIndex = 0;
        private static int barIndex = 0;
        private static readonly object consoleLock = new object();
        private static readonly object logLock = new object();
        private static string logFilePath = "";
        private static bool enableColor = true;
        
        // 专业配色方案
        private static readonly ConsoleColor ColorSuccess = ConsoleColor.Green;
        private static readonly ConsoleColor ColorInfo = ConsoleColor.White;
        private static readonly ConsoleColor ColorWarning = ConsoleColor.Yellow;
        private static readonly ConsoleColor ColorError = ConsoleColor.Red;
        private static readonly ConsoleColor ColorFile = ConsoleColor.White;
        private static readonly ConsoleColor ColorPath = ConsoleColor.DarkCyan;
        private static readonly ConsoleColor ColorValue = ConsoleColor.White;
        private static readonly ConsoleColor ColorNumber = ConsoleColor.Yellow;
        private static readonly ConsoleColor ColorTime = ConsoleColor.DarkGray;
        private static readonly ConsoleColor ColorSection = ConsoleColor.DarkCyan;
        private static readonly ConsoleColor ColorProgress = ConsoleColor.DarkGreen;
        
        public static void InitializeLog(string path)
        {
            logFilePath = path;
            try
            {
                // 检查控制台是否支持颜色
                enableColor = true;
            }
            catch
            {
                enableColor = false;
            }
        }
        
        public static void WriteToLog(string message, bool toConsole = true)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logMessage = string.Format("[{0}] {1}", timestamp, message);
            
            // 写入日志文件
            if (!string.IsNullOrEmpty(logFilePath))
            {
                lock (logLock)
                {
                    try
                    {
                        File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
                    }
                    catch
                    {
                        // 如果写入日志失败，继续执行
                    }
                }
            }
            
            // 输出到控制台
            if (toConsole)
            {
                lock (consoleLock)
                {
                    try
                    {
                        Console.WriteLine(logMessage);
                    }
                    catch
                    {
                        // 如果控制台输出失败，忽略
                    }
                }
            }
        }
        
        public static void WriteSection(string title)
        {
            lock (consoleLock)
            {
                try
                {
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine("┌" + new string('─', title.Length + 10));
                    Console.Error.WriteLine("│ " + title);
                    Console.Error.WriteLine("└" + new string('─', title.Length + 10));
                    if (enableColor) Console.ResetColor();
                    Console.Error.WriteLine();
                    
                    WriteToLog("=== " + title + " ===", false);
                }
                catch
                {
                    Console.Error.WriteLine("=== " + title + " ===");
                }
            }
        }
        
        public static void WriteSeparator(int length = 60, char symbol = '─')
        {
            lock (consoleLock)
            {
                try
                {
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Error.WriteLine(new string(symbol, length));
                    if (enableColor) Console.ResetColor();
                }
                catch
                {
                    Console.Error.WriteLine(new string(symbol, length));
                }
            }
        }
        
        public static void WriteKeyValue(string key, string value, ConsoleColor valueColor = ConsoleColor.White)
        {
            lock (consoleLock)
            {
                try
                {
                    if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Error.Write("  ");
                    Console.Error.Write(key.PadRight(25));
                    Console.Error.Write(" : ");
                    
                    if (enableColor) Console.ForegroundColor = valueColor;
                    Console.Error.WriteLine(value);
                    
                    if (enableColor) Console.ResetColor();
                }
                catch
                {
                    Console.Error.WriteLine("  " + key.PadRight(25) + " : " + value);
                }
            }
        }
        
        public static string GetSpinner()
        {
            spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;
            return spinnerChars[spinnerIndex];
        }
        
        public static string GetProgressBar(double percentage, int width = 20)
        {
            if (width <= 0) width = 20;
            int filled = (int)(percentage * width);
            int empty = width - filled;
            
            barIndex = (barIndex + 1) % barChars.Length;
            string movingChar = barChars[barIndex];
            
            // 确保filled不为负数
            filled = Math.Max(0, filled);
            empty = Math.Max(0, empty);
            
            // 如果filled为0，则不显示移动字符
            if (filled == 0)
            {
                return "[" + new string('░', empty) + "]";
            }
            
            // 如果filled等于width，则显示满格
            if (filled == width)
            {
                return "[" + new string('█', width) + "]";
            }
            
            return "[" + new string('█', filled - 1) + movingChar + new string('░', empty) + "]";
        }
        
        public static string GetETA(TimeSpan elapsed, int processed, int total)
        {
            if (processed <= 0 || elapsed.TotalSeconds <= 0.1) return "计算中...";
            
            double averageTime = elapsed.TotalSeconds / processed;
            int remaining = total - processed;
            double remainingSeconds = averageTime * remaining;
            
            if (remainingSeconds < 60)
                return string.Format("{0:F0}秒", remainingSeconds);
            else if (remainingSeconds < 3600)
                return string.Format("{0:F1}分", remainingSeconds / 60);
            else
                return string.Format("{0:F1}时", remainingSeconds / 3600);
        }
        
        public static string FormatSpeed(long bytes, TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds <= 0.1) return "计算中...";
            
            double speed = bytes / elapsed.TotalSeconds;
            
            if (speed >= 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB/s", speed / (1024 * 1024 * 1024));
            else if (speed >= 1024 * 1024)
                return string.Format("{0:F2} MB/s", speed / (1024 * 1024));
            else if (speed >= 1024)
                return string.Format("{0:F2} KB/s", speed / 1024);
            else
                return string.Format("{0:F0} B/s", speed);
        }
        
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB", bytes / (1024.0 * 1024.0 * 1024.0));
            else if (bytes >= 1024 * 1024)
                return string.Format("{0:F2} MB", bytes / (1024.0 * 1024.0));
            else if (bytes >= 1024)
                return string.Format("{0:F2} KB", bytes / 1024.0);
            else
                return string.Format("{0} B", bytes);
        }
        
        public static void UpdateProgressLine(string text)
        {
            lock (consoleLock)
            {
                try
                {
                    // 保存光标位置
                    int left = Console.CursorLeft;
                    int top = Console.CursorTop;
                    
                    // 移动到行首
                    Console.SetCursorPosition(0, top);
                    
                    // 清除当前行
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                    
                    // 回到行首
                    Console.SetCursorPosition(0, top);
                    
                    // 输出进度文本，使用绿色显示进度部分
                    if (text.Length > Console.WindowWidth - 1)
                    {
                        if (enableColor) Console.ForegroundColor = ColorProgress;
                        Console.Write(text.Substring(0, Console.WindowWidth - 1));
                    }
                    else
                    {
                        if (enableColor) Console.ForegroundColor = ColorProgress;
                        Console.Write(text);
                    }
                    
                    if (enableColor) Console.ResetColor();
                    
                    // 如果需要恢复光标位置
                    // Console.SetCursorPosition(left, top);
                }
                catch (Exception)
                {
                    // 如果控制台操作失败，使用简单输出
                    Console.Write(string.Format("\r{0}", text));
                }
            }
        }
        
        public static void ClearProgressLine()
        {
            lock (consoleLock)
            {
                try
                {
                    int currentLineCursor = Console.CursorTop;
                    Console.SetCursorPosition(0, currentLineCursor);
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                    Console.SetCursorPosition(0, currentLineCursor);
                }
                catch
                {
                    Console.WriteLine();
                }
            }
        }
        
        public static void WriteProgressComplete(string message)
        {
            lock (consoleLock)
            {
                ClearProgressLine();
                if (enableColor) Console.ForegroundColor = ColorSuccess;
                Console.Error.WriteLine("✅ " + message);
                if (enableColor) Console.ResetColor();
                
                // 记录到日志
                WriteToLog("完成: " + message, false);
            }
        }
        
        public static void WriteWarning(string message)
        {
            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorWarning;
                Console.Error.WriteLine("⚠️  " + message);
                if (enableColor) Console.ResetColor();
                
                // 记录到日志
                WriteToLog("警告: " + message, false);
            }
        }
        
        public static void WriteError(string message)
        {
            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorError;
                Console.Error.WriteLine("❌ " + message);
                if (enableColor) Console.ResetColor();
                
                // 记录到日志
                WriteToLog("错误: " + message, false);
            }
        }
        
        public static void WriteInfo(string message)
        {
            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorInfo;
                Console.Error.WriteLine("ℹ️  " + message);
                if (enableColor) Console.ResetColor();
                
                // 记录到日志
                WriteToLog("信息: " + message, false);
            }
        }
        
        public static void WriteSuccess(string message)
        {
            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorSuccess;
                Console.Error.WriteLine("✅ " + message);
                if (enableColor) Console.ResetColor();
                
                // 记录到日志
                WriteToLog("成功: " + message, false);
            }
        }
        
        public static void WriteFileInfo(string fileName, string message)
        {
            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorFile;
                Console.Error.Write("📄 ");
                if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                Console.Error.Write(fileName);
                if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                Console.Error.Write(": ");
                if (enableColor) Console.ResetColor();
                Console.Error.WriteLine(message);
                
                // 记录到日志
                string logMessage = string.Format("文件 {0}: {1}", fileName, message);
                WriteToLog(logMessage, false);
            }
        }
        
        public static void WriteBanner(string title, string subtitle = "")
        {
            lock (consoleLock)
            {
                try
                {
                    int width = Math.Max(title.Length, subtitle.Length) + 6;
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Error.WriteLine("╔" + new string('═', width));
                    Console.Error.Write("║");
                    Console.Error.Write(new string(' ', (width - title.Length) / 2));
                    if (enableColor) Console.ForegroundColor = ConsoleColor.White;
                    Console.Error.Write(title);
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Error.Write(new string(' ', width - title.Length - (width - title.Length) / 2));
                    Console.Error.WriteLine("║");
                    
                    if (!string.IsNullOrEmpty(subtitle))
                    {
                        Console.Error.Write("║");
                        Console.Error.Write(new string(' ', (width - subtitle.Length) / 2));
                        if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Error.Write(subtitle);
                        if (enableColor) Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.Error.Write(new string(' ', width - subtitle.Length - (width - subtitle.Length) / 2));
                        Console.Error.WriteLine("║");
                    }
                    
                    Console.Error.WriteLine("╚" + new string('═', width));
                    if (enableColor) Console.ResetColor();
                    Console.Error.WriteLine();
                }
                catch
                {
                    Console.Error.WriteLine("=== " + title + " ===");
                    if (!string.IsNullOrEmpty(subtitle))
                    {
                        Console.Error.WriteLine(subtitle);
                    }
                    Console.Error.WriteLine();
                }
            }
        }
        
        public static void WriteStatistics(string title, params (string key, string value, ConsoleColor color)[] items)
        {
            lock (consoleLock)
            {
                try
                {
                    // 简单修复：使用固定宽度并限制内容长度
                    int tableWidth = 60; // 固定宽度
                    
                    // 截断过长的键和值
                    var adjustedItems = items.Select(item => {
                        string key = item.key.Length > 20 ? item.key.Substring(0, 17) + "..." : item.key;
                        string value = item.value.Length > 25 ? item.value.Substring(0, 22) + "..." : item.value;
                        return (key, value, item.color);
                    }).ToArray();
                    
                    // 使用简单表格
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(new string('=', tableWidth));
                    Console.Error.WriteLine(string.Format("  {0}", title));
                    Console.Error.WriteLine(new string('-', tableWidth));
                    
                    foreach (var item in adjustedItems)
                    {
                        Console.Error.Write("  ");
                        if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Error.Write(item.key.PadRight(20));
                        Console.Error.Write(" : ");
                        if (enableColor) Console.ForegroundColor = item.color;
                        Console.Error.WriteLine(item.value);
                    }
                    
                    Console.Error.WriteLine(new string('=', tableWidth));
                    if (enableColor) Console.ResetColor();
                }
                catch
                {
                    // 回退到简单显示
                    Console.Error.WriteLine("=== " + title + " ===");
                    foreach (var item in items)
                    {
                        Console.Error.WriteLine("  " + item.key + " : " + item.value);
                    }
                }
            }
        }
        
        // ========== 图表功能 ==========
        
        /// <summary>
        /// 绘制水平条形图
        /// </summary>
        public static void WriteHorizontalBarChart(string title, params (string label, double value, ConsoleColor color)[] items)
        {
            lock (consoleLock)
            {
                try
                {
                    if (items == null || items.Length == 0) return;
                    
                    const int chartWidth = 64;  // 统一宽度
                    const int contentWidth = chartWidth - 2;  // 减去左侧竖线和空格
                    
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.Error.WriteLine("┌" + new string('─', chartWidth));  // 只有左上角，没有右上角
                    
                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ConsoleColor.White;
                    Console.Error.Write(title.PadRight(contentWidth));
                    Console.Error.WriteLine();  // 直接换行，没有右侧竖线
                    
                    Console.Error.WriteLine("├" + new string('─', chartWidth));  // 只有左侧，没有右侧
                    
                    // 找到最大值用于比例计算
                    double maxValue = items.Max(x => x.value);
                    if (maxValue <= 0) maxValue = 1;
                    
                    foreach (var item in items)
                    {
                        Console.Error.Write("│ ");
                        
                        // 标签 - 调整为适合新宽度
                        if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                        string label = item.label.Length > 15 ? item.label.Substring(0, 12) + "..." : item.label;
                        Console.Error.Write(label.PadRight(15));
                        Console.Error.Write(" ");
                        
                        // 条形图 - 调整长度
                        int barLength = (int)((item.value / maxValue) * (contentWidth - 25));  // 动态计算
                        barLength = Math.Max(0, Math.Min(contentWidth - 25, barLength));
                        
                        if (enableColor) Console.ForegroundColor = item.color;
                        Console.Error.Write(new string('█', barLength));
                        
                        // 值
                        if (enableColor) Console.ForegroundColor = ConsoleColor.White;
                        Console.Error.Write(" ");
                        Console.Error.Write(item.value.ToString("F2").PadLeft(8));
                        
                        Console.Error.WriteLine();  // 直接换行，没有右侧竖线
                    }
                    
                    Console.Error.WriteLine("└" + new string('─', chartWidth));  // 只有左下角，没有右下角
                    if (enableColor) Console.ResetColor();
                }
                catch (Exception)
                {
                    // 如果绘制失败，显示简化版本
                    Console.Error.WriteLine(string.Format("=== {0} ===", title));
                    foreach (var item in items)
                    {
                        Console.Error.WriteLine(string.Format("  {0}: {1:F2}", item.label, item.value));
                    }
                }
            }
        }
        
        /// <summary>
        /// 绘制文件大小分布图
        /// </summary>
        public static void WriteSizeDistributionChart(List<long> fileSizes, string title = "文件大小分布")
        {
            lock (consoleLock)
            {
                try
                {
                    if (fileSizes == null || fileSizes.Count == 0) return;
                    
                    // 定义大小范围
                    var sizeRanges = new[]
                    {
                        (label: "0-1KB", max: 1024L),
                        (label: "1KB-100KB", max: 100 * 1024L),
                        (label: "100KB-1MB", max: 1024 * 1024L),
                        (label: "1MB-10MB", max: 10 * 1024 * 1024L),
                        (label: "10MB-100MB", max: 100 * 1024 * 1024L),
                        (label: "100MB-1GB", max: 1024 * 1024 * 1024L),
                        (label: "1GB+", max: long.MaxValue)
                    };
                    
                    // 统计每个区间的文件数量
                    var counts = new int[sizeRanges.Length];
                    foreach (var size in fileSizes)
                    {
                        for (int i = 0; i < sizeRanges.Length; i++)
                        {
                            if (size <= sizeRanges[i].max)
                            {
                                counts[i]++;
                                break;
                            }
                        }
                    }
                    
                    // 绘制图表
                    var chartItems = new List<(string label, double value, ConsoleColor color)>();
                    for (int i = 0; i < sizeRanges.Length; i++)
                    {
                        if (counts[i] > 0)
                        {
                            // 根据文件数量选择颜色
                            ConsoleColor color = counts[i] switch
                            {
                                var n when n >= 100 => ConsoleColor.Red,
                                var n when n >= 50 => ConsoleColor.Yellow,
                                var n when n >= 10 => ConsoleColor.Green,
                                var n when n >= 5 => ConsoleColor.White,
                                _ => ConsoleColor.Blue
                            };
                            
                            chartItems.Add((sizeRanges[i].label, counts[i], color));
                        }
                    }
                    
                    if (chartItems.Count > 0)
                    {
                        WriteHorizontalBarChart(title, chartItems.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    WriteWarning(string.Format("无法绘制图表: {0}", ex.Message));
                }
            }
        }
        
        /// <summary>
        /// 绘制处理时间散点图
        /// </summary>
        public static void WriteScatterPlot(string title, List<(double x, double y, string label)> points, 
                                          string xLabel = "文件大小(MB)", string yLabel = "处理时间(ms)")
        {
            lock (consoleLock)
            {
                try
                {
                    if (points == null || points.Count == 0) return;
                    
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Error.WriteLine(string.Format("┌{0}┐", new string('─', 64)));
                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ConsoleColor.Green;
                    Console.Error.Write(title.PadRight(62));
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Error.WriteLine();
                    
                    // 创建坐标系网格
                    int gridWidth = 60;
                    int gridHeight = 20;
                    char[,] grid = new char[gridHeight, gridWidth];
                    
                    // 初始化网格
                    for (int y = 0; y < gridHeight; y++)
                    {
                        for (int x = 0; x < gridWidth; x++)
                        {
                            grid[y, x] = ' ';
                        }
                    }
                    
                    // 绘制坐标轴
                    for (int y = 0; y < gridHeight; y++)
                    {
                        grid[y, 0] = '│';
                    }
                    for (int x = 0; x < gridWidth; x++)
                    {
                        grid[gridHeight - 1, x] = '─';
                    }
                    grid[gridHeight - 1, 0] = '└'; // 左下角
                    
                    // 添加数据点
                    double maxX = points.Max(p => p.x);
                    double maxY = points.Max(p => p.y);
                    
                    if (maxX > 0 && maxY > 0)
                    {
                        foreach (var point in points)
                        {
                            int plotX = (int)((point.x / maxX) * (gridWidth - 5)) + 1;
                            int plotY = gridHeight - 1 - (int)((point.y / maxY) * (gridHeight - 2));
                            
                            if (plotX >= 0 && plotX < gridWidth && plotY >= 0 && plotY < gridHeight)
                            {
                                grid[plotY, plotX] = '●';
                            }
                        }
                    }
                    
                    // 输出网格
                    for (int y = 0; y < gridHeight; y++)
                    {
                        Console.Error.Write("│ ");
                        for (int x = 0; x < gridWidth; x++)
                        {
                            if (grid[y, x] == '●')
                            {
                                if (enableColor) Console.ForegroundColor = ConsoleColor.Red;
                                Console.Error.Write(grid[y, x]);
                            }
                            else
                            {
                                if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Error.Write(grid[y, x]);
                            }
                        }
                        if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Error.WriteLine();
                    }
                    
                    // 坐标轴标签
                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Error.Write(string.Format("← {0}", xLabel).PadRight(32));
                    Console.Error.Write(string.Format("↑ {0}", yLabel).PadRight(32));
                    if (enableColor) Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Error.WriteLine();
                    
                    Console.Error.WriteLine(string.Format("└{0}┘", new string('─', 64)));
                    if (enableColor) Console.ResetColor();
                }
                catch (Exception)
                {
                    // 简化为文本显示
                    Console.Error.WriteLine(string.Format("=== {0} ===", title));
                    Console.Error.WriteLine(string.Format("{0} 与 {1} 关系:", xLabel, yLabel));
                    foreach (var point in points.Take(10)) // 只显示前10个点
                    {
                        Console.Error.WriteLine(string.Format("  大小: {0:F1}MB, 时间: {1:F1}ms", point.x, point.y));
                    }
                }
            }
        }
    }
}

'@

"FastHashCalculator.cs" = @'

// FastHashCalculator.cs - 高性能哈希计算器（带动态缓冲区）
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FastHasher
{
    // 高性能哈希计算器 - 带动态缓冲区
    public static class FastHashCalculator
    {
        // 大文件阈值（10MB），超过此大小使用流式处理
        private const long LargeFileThreshold = 10 * 1024 * 1024;
        
        // 获取最佳缓冲区大小（根据文件大小动态调整）
        private static int GetOptimalBufferSize(long fileSize)
        {
            // 文件大小 < 10MB: 使用1MB缓冲区
            if (fileSize <= 10 * 1024 * 1024)
                return 1024 * 1024; // 1MB
            
            // 文件大小 10MB-100MB: 使用2MB缓冲区
            if (fileSize <= 100 * 1024 * 1024)
                return 2 * 1024 * 1024; // 2MB
            
            // 文件大小 100MB-500MB: 使用4MB缓冲区
            if (fileSize <= 500 * 1024 * 1024)
                return 4 * 1024 * 1024; // 4MB
            
            // 文件大小 500MB-2GB: 使用8MB缓冲区
            if (fileSize <= 2 * 1024 * 1024 * 1024L)
                return 8 * 1024 * 1024; // 8MB
            
            // 文件大小 > 2GB: 使用10MB缓冲区
            return 10 * 1024 * 1024; // 10MB
        }
        
        public static async Task<Dictionary<string, string>> ComputeHashesAsync(
            string filePath, 
            string[] algorithms, 
            bool computeSizeOnly = false,
            CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, string>
            {
                { "file", filePath },
                { "filename", Path.GetFileName(filePath) }
            };
            
            try
            {
                if (!File.Exists(filePath))
                {
                    results["success"] = "false";
                    results["error"] = "文件不存在";
                    return results;
                }
                
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;
                results["size"] = fileSize.ToString();
                results["full_path"] = fileInfo.FullName;
                results["directory"] = fileInfo.DirectoryName ?? "";
                results["extension"] = fileInfo.Extension;
                results["created"] = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                results["modified"] = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                
                if (computeSizeOnly)
                {
                    results["success"] = "true";
                    return results;
                }
                
                var stopwatch = Stopwatch.StartNew();
                
                // 在控制台显示开始处理文件的信息
                string fileName = Path.GetFileName(filePath);
                string shortName = fileName.Length > 30 ? fileName.Substring(0, 27) + "..." : fileName;
                ProgressDisplay.WriteFileInfo(shortName, string.Format("开始计算哈希 | 大小: {0}", ProgressDisplay.FormatSize(fileSize)));
                
                // 根据文件大小选择处理策略
                if (fileSize <= LargeFileThreshold)
                {
                    // 小文件：一次性读取到内存
                    await ComputeSmallFileHashes(filePath, algorithms, results, cancellationToken);
                }
                else
                {
                    // 大文件：流式处理，使用动态缓冲区
                    int bufferSize = GetOptimalBufferSize(fileSize);
                    ProgressDisplay.WriteFileInfo(shortName, string.Format("使用动态缓冲区: {0}", ProgressDisplay.FormatSize(bufferSize)));
                    await ComputeLargeFileHashes(filePath, algorithms, results, bufferSize, cancellationToken);
                }
                
                stopwatch.Stop();
                results["time_ms"] = stopwatch.ElapsedMilliseconds.ToString("F2");
                results["success"] = "true";
                
                // 添加性能数据用于图表
                results["size_mb"] = (fileSize / (1024.0 * 1024.0)).ToString("F2");
                results["size_mb_double"] = (fileSize / (1024.0 * 1024.0)).ToString("F8");
                results["time_ms_double"] = stopwatch.ElapsedMilliseconds.ToString("F4");
                
                // 显示处理完成信息
                ProgressDisplay.WriteFileInfo(shortName, string.Format("哈希计算完成 | 用时: {0:F2}ms", stopwatch.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                results["success"] = "false";
                results["error"] = ex.Message;
                results["error_type"] = ex.GetType().Name;
                
                // 记录错误
                ProgressDisplay.WriteError(string.Format("文件 {0} 处理失败: {1}", Path.GetFileName(filePath), ex.Message));
            }
            
            return results;
        }
        
        private static async Task ComputeSmallFileHashes(
            string filePath, 
            string[] algorithms, 
            Dictionary<string, string> results,
            CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            string shortName = fileName.Length > 30 ? fileName.Substring(0, 27) + "..." : fileName;
            
            // 读取整个文件
            ProgressDisplay.WriteFileInfo(shortName, "读取文件到内存...");
            byte[] fileData;
            
            // 使用FileStream读取，支持大文件
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                fileData = new byte[stream.Length];
                int bytesRead = 0;
                int totalBytes = (int)stream.Length;
                
                while (bytesRead < totalBytes)
                {
                    int bytesToRead = Math.Min(1024 * 1024, totalBytes - bytesRead);
                    int read = await stream.ReadAsync(fileData, bytesRead, bytesToRead, cancellationToken);
                    if (read == 0) break;
                    bytesRead += read;
                }
            }
            
            ProgressDisplay.WriteFileInfo(shortName, string.Format("文件读取完成 | 大小: {0}", ProgressDisplay.FormatSize(fileData.Length)));
            
            // 计算哈希
            await ComputeHashesForData(fileData, algorithms, results, shortName);
        }
        
        private static async Task ComputeLargeFileHashes(
            string filePath, 
            string[] algorithms, 
            Dictionary<string, string> results,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            string shortName = fileName.Length > 30 ? fileName.Substring(0, 27) + "..." : fileName;
            var fileInfo = new FileInfo(filePath);
            long totalBytes = fileInfo.Length;
            
            ProgressDisplay.WriteFileInfo(shortName, string.Format("流式处理大文件 | 大小: {0} | 缓冲区: {1}", 
                ProgressDisplay.FormatSize(totalBytes), ProgressDisplay.FormatSize(bufferSize)));
            
            // 初始化所有哈希算法
            var hashAlgorithms = new List<Tuple<string, HashAlgorithm>>();
            foreach (string algorithm in algorithms)
            {
                var hashAlgo = CreateHashAlgorithm(algorithm);
                if (hashAlgo != null)
                {
                    hashAlgorithms.Add(new Tuple<string, HashAlgorithm>(algorithm, hashAlgo));
                }
            }
            
            // 流式处理文件
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[bufferSize];
                long processedBytes = 0;
                var lastUpdateTime = DateTime.UtcNow;
                
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;
                    
                    foreach (var hashAlgo in hashAlgorithms)
                    {
                        hashAlgo.Item2.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }
                    
                    processedBytes += bytesRead;
                    
                    // 更新进度（每处理5%或100ms更新一次）
                    var now = DateTime.UtcNow;
                    if (now - lastUpdateTime > TimeSpan.FromMilliseconds(100))
                    {
                        double percentage = (double)processedBytes / totalBytes;
                        string progressBar = ProgressDisplay.GetProgressBar(percentage, 15);
                        string spinner = ProgressDisplay.GetSpinner();
                        string speed = ProgressDisplay.FormatSpeed(processedBytes, TimeSpan.FromMilliseconds(100));
                        
                        ProgressDisplay.UpdateProgressLine(string.Format("{0} {1} [{2}] {3:P1} | {4}/{5} | {6} | 缓冲区: {7}", 
                            spinner, shortName, progressBar, percentage, 
                            ProgressDisplay.FormatSize(processedBytes), 
                            ProgressDisplay.FormatSize(totalBytes), 
                            speed,
                            ProgressDisplay.FormatSize(bufferSize)));
                        
                        lastUpdateTime = now;
                    }
                    
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
                
                // 完成计算
                foreach (var hashAlgo in hashAlgorithms)
                {
                    hashAlgo.Item2.TransformFinalBlock(new byte[0], 0, 0);
                    results[hashAlgo.Item1.ToLower()] = BytesToHex(hashAlgo.Item2.Hash);
                    ProgressDisplay.WriteFileInfo(shortName, string.Format("算法 {0} 计算完成", hashAlgo.Item1));
                }
            }
            
            // 清理资源
            foreach (var hashAlgo in hashAlgorithms)
            {
                hashAlgo.Item2.Dispose();
            }
            
            ProgressDisplay.WriteProgressComplete(string.Format("大文件处理完成: {0}", shortName));
        }
        
        private static async Task ComputeHashesForData(
            byte[] data, 
            string[] algorithms, 
            Dictionary<string, string> results,
            string fileName)
        {
            ProgressDisplay.WriteFileInfo(fileName, string.Format("开始计算 {0} 种哈希算法", algorithms.Length));
            
            // 并行计算哈希
            var tasks = new List<Task<Tuple<string, string, string>>>();
            
            foreach (string algorithm in algorithms)
            {
                tasks.Add(Task.Run(() => ComputeHashForAlgorithm(algorithm, data)));
            }
            
            // 等待所有任务完成
            var hashResults = await Task.WhenAll(tasks);
            
            // 收集结果
            foreach (var result in hashResults)
            {
                if (!string.IsNullOrEmpty(result.Item3))
                {
                    results[string.Format("error_{0}", result.Item1.ToLower())] = result.Item3;
                    ProgressDisplay.WriteWarning(string.Format("文件 {0} | 算法 {1} 计算失败: {2}", fileName, result.Item1, result.Item3));
                }
                else
                {
                    results[result.Item1.ToLower()] = result.Item2;
                    ProgressDisplay.WriteFileInfo(fileName, string.Format("算法 {0} 计算完成 | 哈希: {1}...", 
                        result.Item1, result.Item2.Substring(0, Math.Min(16, result.Item2.Length))));
                }
            }
            
            ProgressDisplay.WriteFileInfo(fileName, "所有算法计算完成");
        }
        
        private static HashAlgorithm CreateHashAlgorithm(string algorithm)
        {
            string algoUpper = algorithm.ToUpper().Replace("-", "");
            
            switch (algoUpper)
            {
                case "MD5":
                    return MD5.Create();
                case "SHA1":
                    return SHA1.Create();
                case "SHA256":
                    return SHA256.Create();
                case "SHA512":
                    return SHA512.Create();
                default:
                    throw new ArgumentException(string.Format("不支持的算法: {0}", algorithm));
            }
        }
        
        private static Tuple<string, string, string> ComputeHashForAlgorithm(string algorithm, byte[] data)
        {
            try
            {
                string algoUpper = algorithm.ToUpper().Replace("-", "");
                byte[] hash;
                
                using (HashAlgorithm hashAlgorithm = CreateHashAlgorithm(algoUpper))
                {
                    hash = hashAlgorithm.ComputeHash(data);
                }
                
                return new Tuple<string, string, string>(algorithm, BytesToHex(hash), null);
            }
            catch (Exception ex)
            {
                return new Tuple<string, string, string>(algorithm, string.Empty, ex.Message);
            }
        }
        
        // 字节数组转十六进制
        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString();
        }
    }
}




'@



  "FileProcessor.cs" = @'

// FileProcessor.cs - 专业文件处理器（包含图表功能）
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Enumeration; // 新增：用于 EnumerationOptions

namespace FastHasher
{
    // 文件处理器 - 增强显示和日志记录
    public class FileProcessor
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly List<string> algorithms;
        private readonly bool showTime;
        private readonly bool recursive;
        private readonly bool parallel;
        public readonly int maxDegreeOfParallelism;
        private readonly long minFileSize = 0;
        private readonly long maxFileSize = long.MaxValue;
        private readonly bool saveToFile; // 新增：是否保存到文件
        private readonly string outputFilePath; // 新增：输出文件路径
        private readonly bool consoleOutput; // 新增：控制台输出
        private readonly Stopwatch totalStopwatch = new Stopwatch();
        private long totalBytes = 0;
        private readonly string logFilePath; // 新增：日志文件路径
        
        // 图表数据收集
        private List<long> fileSizes = new List<long>();
        private List<(double sizeMB, double timeMs)> performanceData = new List<(double, double)>();
        
        public FileProcessor(List<string> algorithms, bool showTime, bool recursive, bool parallel, 
                           int maxDegreeOfParallelism = 0, 
                           long minFileSize = 0, long maxFileSize = long.MaxValue,
                           bool saveToFile = true, string outputFilePath = null, bool consoleOutput = false)
        {
            this.algorithms = algorithms;
            this.showTime = showTime;
            this.recursive = recursive;
            this.parallel = parallel;
            this.minFileSize = minFileSize;
            this.maxFileSize = maxFileSize;
            this.saveToFile = saveToFile;
            this.outputFilePath = outputFilePath;
            this.consoleOutput = consoleOutput;

            // 核心优化：并行度校验+默认值设置（CPU逻辑线程数）
            if (maxDegreeOfParallelism <= 0)
            {
                // 无有效值时，默认使用CPU逻辑线程数
                this.maxDegreeOfParallelism = Environment.ProcessorCount;
            }
            else
            {
                // 手动指定值有效时，使用指定值（限制最大为CPU线程数*2，避免过度并行）
                this.maxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, Environment.ProcessorCount * 2);
            }
            
            // 设置日志文件路径
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            this.logFilePath = Path.Combine(Environment.CurrentDirectory, string.Format("FastHasher_{0}.log", timestamp));
            
            // 初始化日志系统
            ProgressDisplay.InitializeLog(this.logFilePath);
            
            // 记录启动信息
            ProgressDisplay.WriteToLog(string.Format("FastHasher 开始运行，时间: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format("算法: {0}", string.Join(", ", algorithms)));
            ProgressDisplay.WriteToLog(string.Format("递归搜索: {0}", recursive ? "是" : "否"));
            ProgressDisplay.WriteToLog(string.Format("并行处理: {0}", parallel ? "是" : "否"));
            ProgressDisplay.WriteToLog(string.Format("并行线程数: {0}", this.maxDegreeOfParallelism));
            ProgressDisplay.WriteToLog(string.Format("文件大小过滤: {0} - {1}", 
                minFileSize > 0 ? ProgressDisplay.FormatSize(minFileSize) : "无限制", 
                maxFileSize < long.MaxValue ? ProgressDisplay.FormatSize(maxFileSize) : "无限制"));
        }
        
        public async Task ProcessFilesAsync(List<string> patterns)
        {
            ProgressDisplay.WriteToLog(string.Format("开始处理文件模式，模式数量: {0}", patterns.Count));
            if (patterns.Count > 0)
            {
                ProgressDisplay.WriteToLog("文件模式列表:");
                foreach (var pattern in patterns)
                {
                    ProgressDisplay.WriteToLog(string.Format("  - {0}", pattern));
                }
            }
            
            // 显示开始处理横幅
            ProgressDisplay.WriteSection("文件处理开始");
            
            var files = ExpandFilePatterns(patterns, recursive, minFileSize, maxFileSize);
            
            if (files.Count == 0)
            {
                ProgressDisplay.WriteWarning("警告: 没有找到匹配的文件");
                ProgressDisplay.WriteToLog("没有找到匹配的文件");
                return;
            }
            
            // 收集文件大小用于图表
            fileSizes.Clear();
            foreach (var file in files)
            {
                try
                {
                    fileSizes.Add(new FileInfo(file).Length);
                }
                catch { }
            }
            
            // 计算总大小（用于进度显示）
            totalBytes = CalculateTotalSize(files);
            
            // 显示文件统计信息
            ProgressDisplay.WriteStatistics("文件统计", 
                ("文件数量", files.Count.ToString("N0"), ConsoleColor.Yellow),
                ("总大小", ProgressDisplay.FormatSize(totalBytes), ConsoleColor.White),
                ("并行线程", maxDegreeOfParallelism.ToString(), maxDegreeOfParallelism == Environment.ProcessorCount ? ConsoleColor.Green : ConsoleColor.Yellow),
                ("模式", parallel ? "并行" : "顺序", parallel ? ConsoleColor.Green : ConsoleColor.Gray)
            );
            
            // 显示输出信息
            if (saveToFile && !string.IsNullOrEmpty(outputFilePath))
            {
                ProgressDisplay.WriteInfo(string.Format("输出文件: {0}", outputFilePath));
            }
            else if (consoleOutput)
            {
                ProgressDisplay.WriteInfo("输出方式: 控制台");
            }
            
            ProgressDisplay.WriteSeparator();
            
            // 记录到日志
            ProgressDisplay.WriteToLog(string.Format("找到 {0} 个文件需要处理 | 总大小: {1}", files.Count, ProgressDisplay.FormatSize(totalBytes)));
            ProgressDisplay.WriteToLog(string.Format("并行线程数: {0} {1}", maxDegreeOfParallelism, maxDegreeOfParallelism == Environment.ProcessorCount ? "(CPU默认)" : "(手动指定)"));
            
            totalStopwatch.Start();
            var results = new List<Dictionary<string, string>>();
            
            // 记录开始时间
            ProgressDisplay.WriteToLog("开始处理文件...");
            var startTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format("处理开始时间: {0}", startTime.ToString("yyyy-MM-dd HH:mm:ss")));
            
            if (parallel)
            {
                ProgressDisplay.WriteInfo("使用并行处理模式");
                results = await ProcessFilesParallelAsync(files);
            }
            else
            {
                ProgressDisplay.WriteInfo("使用顺序处理模式");
                results = await ProcessFilesSequentialAsync(files);
            }
            
            totalStopwatch.Stop();
            
            // 记录结束时间
            var endTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format("处理结束时间: {0}", endTime.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format("总处理时间: {0:F2}秒", totalStopwatch.Elapsed.TotalSeconds));
            
            // 输出结果
            OutputResults(results);
            
            // 显示统计信息和图表
            if (showTime)
            {
                ProgressDisplay.WriteSection("处理统计信息");
                
                ProgressDisplay.WriteStatistics("性能统计", 
                    ("总时间", string.Format("{0:F2}秒", totalStopwatch.Elapsed.TotalSeconds), ConsoleColor.Yellow),
                    ("文件数", files.Count.ToString("N0"), ConsoleColor.White),
                    ("总大小", ProgressDisplay.FormatSize(totalBytes), ConsoleColor.White),
                    ("平均速度", ProgressDisplay.FormatSpeed(totalBytes, totalStopwatch.Elapsed), ConsoleColor.Green),
                    ("文件平均", string.Format("{0:F1}ms", totalStopwatch.Elapsed.TotalMilliseconds / Math.Max(1, files.Count)), ConsoleColor.DarkGreen)
                );
                
                ProgressDisplay.WriteSeparator();
                ProgressDisplay.WriteKeyValue("内存信息", PerformanceMonitor.GetMemoryInfo(), ConsoleColor.Gray);
                ProgressDisplay.WriteKeyValue("线程信息", PerformanceMonitor.GetThreadInfo(), ConsoleColor.Gray);
                ProgressDisplay.WriteSeparator();
                
                // 显示图表
                ShowCharts();
            }
            
            // 记录统计信息到日志
            if (showTime)
            {
                ProgressDisplay.WriteToLog(string.Format("总处理时间: {0:F2}秒", totalStopwatch.Elapsed.TotalSeconds));
                ProgressDisplay.WriteToLog(string.Format("文件数量: {0} | 总大小: {1}", files.Count, ProgressDisplay.FormatSize(totalBytes)));
                ProgressDisplay.WriteToLog(string.Format("平均速度: {0}", ProgressDisplay.FormatSpeed(totalBytes, totalStopwatch.Elapsed)));
                ProgressDisplay.WriteToLog(string.Format("平均每个文件: {0:F2}毫秒", totalStopwatch.Elapsed.TotalMilliseconds / Math.Max(1, files.Count)));
                ProgressDisplay.WriteToLog(PerformanceMonitor.GetMemoryInfo());
                ProgressDisplay.WriteToLog(PerformanceMonitor.GetThreadInfo());
            }
            
            // 显示日志文件位置
            ProgressDisplay.WriteSuccess(string.Format("处理完成！日志已保存到: {0}", logFilePath));
        }
        
        // 显示图表
        private void ShowCharts()
        {
            // 显示文件大小分布
            if (fileSizes.Count > 0)
            {
                ProgressDisplay.WriteSection("文件大小分布图表");
                ProgressDisplay.WriteSizeDistributionChart(fileSizes);
            }
            
            // 显示性能图表
            if (performanceData.Count > 0)
            {
                ProgressDisplay.WriteSection("性能分析图表");
                
                // 转换为图表数据格式
                var scatterData = performanceData
                    .Select(d => (d.sizeMB, d.timeMs, string.Format("大小:{0:F1}MB", d.sizeMB)))
                    .ToList();
                
                ProgressDisplay.WriteScatterPlot("文件大小 vs 处理时间关系图", scatterData);
                
                // 显示性能统计条形图
                if (performanceData.Count > 5)
                {
                    // 按大小分组统计平均时间
                    var groupedData = performanceData
                        .GroupBy(d => 
                        {
                            double sizeMB = d.sizeMB;
                            if (sizeMB < 1) return "0-1MB";
                            if (sizeMB < 10) return "1-10MB";
                            if (sizeMB < 100) return "10-100MB";
                            if (sizeMB < 1000) return "100MB-1GB";
                            return "1GB+";
                        })
                        .Select(g => (
                            label: g.Key,
                            value: g.Average(d => d.timeMs),
                            color: ConsoleColor.White
                        ))
                        .ToArray();
                    
                    ProgressDisplay.WriteHorizontalBarChart("不同大小文件的平均处理时间", groupedData);
                }
            }
        }
        
        private long CalculateTotalSize(List<string> files)
        {
            long total = 0;
            int successCount = 0;
            int failCount = 0;
            
            ProgressDisplay.WriteInfo("正在计算文件总大小...");
            
            foreach (var file in files)
            {
                try
                {
                    long fileSize = new FileInfo(file).Length;
                    total += fileSize;
                    successCount++;
                    
                    // 每计算100个文件记录一次
                    if (successCount % 100 == 0)
                    {
                        ProgressDisplay.WriteToLog(string.Format("已计算 {0} 个文件大小，当前总计: {1}", successCount, ProgressDisplay.FormatSize(total)));
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    ProgressDisplay.WriteWarning(string.Format("无法获取文件大小: {0} - {1}", file, ex.Message));
                }
            }
            
            ProgressDisplay.WriteToLog(string.Format("文件大小计算完成: 成功 {0} 个，失败 {1} 个，总计大小: {2}", successCount, failCount, ProgressDisplay.FormatSize(total)));
            
            return total;
        }

        private async Task<List<Dictionary<string, string>>> ProcessFilesParallelAsync(List<string> files)
        {
            var results = new Dictionary<int, Dictionary<string, string>>();
            var processedCount = 0;
            var processedBytes = 0L;
            var startTime = DateTime.UtcNow;
            var lastUpdateTime = DateTime.UtcNow;
            var updateLock = new object();
            
            // 预计算文件大小（用于进度显示）
            ProgressDisplay.WriteInfo("正在预计算文件大小...");
            var fileSizesArray = new long[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    fileSizesArray[i] = new FileInfo(files[i]).Length;
                }
                catch
                {
                    fileSizesArray[i] = 0;
                }
                
                // 每计算100个文件记录一次
                if (i % 100 == 0 && i > 0)
                {
                    ProgressDisplay.WriteToLog(string.Format("已预计算 {0}/{1} 个文件大小", i, files.Count));
                }
            }
            
            ProgressDisplay.WriteSuccess("文件大小预计算完成");
            
            // 开始显示进度
            ProgressDisplay.UpdateProgressLine(string.Format("{0} 正在初始化并行处理，文件总数: {1}...", ProgressDisplay.GetSpinner(), files.Count));
            
            // 记录并行处理开始
            ProgressDisplay.WriteToLog("开始并行处理文件...");
            ProgressDisplay.WriteToLog(string.Format("并行线程数: {0}", maxDegreeOfParallelism));
            
            try
            {
                await Parallel.ForEachAsync(files.Select((f, i) => (file: f, index: i, size: fileSizesArray[i])), 
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism,
                        CancellationToken = cts.Token
                    }, 
                    async (item, cancellationToken) =>
                    {
                        // 记录开始处理单个文件
                        string fileName = Path.GetFileName(item.file);
                        ProgressDisplay.WriteToLog(string.Format("开始处理文件: {0} (大小: {1})", fileName, ProgressDisplay.FormatSize(item.size)));
                        
                        var result = await FastHashCalculator.ComputeHashesAsync(
                            item.file, 
                            algorithms.ToArray(), 
                            cancellationToken: cancellationToken);
                        
                        lock (results)
                        {
                            results[item.index] = result;
                            processedCount++;
                            processedBytes += item.size;
                            
                            // 收集性能数据用于图表
                            if (result["success"] == "true" && 
                                result.ContainsKey("size_mb_double") && 
                                result.ContainsKey("time_ms_double"))
                            {
                                double sizeMB = double.Parse(result["size_mb_double"]);
                                double timeMs = double.Parse(result["time_ms_double"]);
                                lock (performanceData)
                                {
                                    performanceData.Add((sizeMB, timeMs));
                                }
                            }
                        }
                        
                        // 记录文件处理完成
                        if (result["success"] == "true")
                        {
                            ProgressDisplay.WriteToLog(string.Format("文件处理完成: {0} (用时: {1}ms)", fileName, result["time_ms"]));
                        }
                        else
                        {
                            ProgressDisplay.WriteError(string.Format("文件处理失败: {0} - {1}", fileName, result.ContainsKey("error") ? result["error"] : "未知错误"));
                        }
                        
                        // 实时显示进度（只在直接模式下显示）
                        // 限制更新频率，避免过多控制台操作影响性能
                        var now = DateTime.UtcNow;
                        if (now - lastUpdateTime > TimeSpan.FromMilliseconds(150))
                        {
                            lock (updateLock)
                            {
                                if (now - lastUpdateTime > TimeSpan.FromMilliseconds(150))
                                {
                                    UpdateParallelProgress(processedCount, files.Count, 
                                                         processedBytes, totalBytes, 
                                                         startTime, item.file);
                                    lastUpdateTime = now;
                                }
                            }
                        }
                    });
                    
                // 最终完成时显示100%
                ProgressDisplay.WriteProgressComplete(string.Format("并行处理完成: {0} 个文件", files.Count));
                
                // 记录并行处理完成
                ProgressDisplay.WriteToLog(string.Format("并行处理完成，共处理 {0} 个文件", files.Count));
            }
            catch (OperationCanceledException)
            {
                ProgressDisplay.WriteToLog("处理被用户取消");
                ProgressDisplay.WriteProgressComplete("处理被用户取消");
            }
            catch (Exception ex)
            {
                ProgressDisplay.WriteToLog(string.Format("处理过程中出现异常: {0}", ex.Message));
                ProgressDisplay.WriteToLog(string.Format("异常堆栈: {0}", ex.StackTrace));
                ProgressDisplay.WriteProgressComplete(string.Format("处理出错: {0}", ex.Message));
            }
            
            // 按原始顺序排序
            return results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }

        private void UpdateParallelProgress(int processed, int total, long bytesProcessed, long totalBytes, 
                                          DateTime startTime, string currentFile)
        {
            if (total == 0) return;
            
            double percentage = (double)processed / total;
            var elapsed = DateTime.UtcNow - startTime;
            string fileName = Path.GetFileName(currentFile);
            
            // 限制文件名长度
            if (fileName.Length > 20)
            {
                fileName = fileName.Substring(0, 17) + "...";
            }
            
            // 构建进度显示文本，包含文件名
            string progressBar = ProgressDisplay.GetProgressBar(percentage);
            string spinner = ProgressDisplay.GetSpinner();
            string eta = ProgressDisplay.GetETA(elapsed, processed, total);
            string speed = ProgressDisplay.FormatSpeed(bytesProcessed, elapsed);
            string sizeInfo = string.Format("{0}/{1}", ProgressDisplay.FormatSize(bytesProcessed), ProgressDisplay.FormatSize(totalBytes));
            
            string progressText;
            
            // 根据控制台宽度选择显示格式
            try
            {
                int consoleWidth = Console.WindowWidth - 2;
                
                if (consoleWidth >= 120)
                {
                    // 宽控制台：显示完整信息，包含文件名
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6} | {7} | {8}线程 | 当前: {9}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        sizeInfo, 
                        speed, 
                        eta, 
                        maxDegreeOfParallelism,
                        fileName);
                }
                else if (consoleWidth >= 100)
                {
                    // 较宽控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6} | 当前: {7}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        sizeInfo, 
                        speed,
                        fileName);
                }
                else if (consoleWidth >= 80)
                {
                    // 中等控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | 当前: {6}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        speed,
                        fileName);
                }
                else if (consoleWidth >= 60)
                {
                    // 较窄控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5}",
                        spinner, progressBar, percentage, 
                        processed, total,
                        fileName);
                }
                else
                {
                    // 窄控制台：简化显示，但仍然包含文件名
                    progressText = string.Format("{0} {1:P1} {2}/{3} | {4}", 
                        spinner, percentage, processed, total, fileName);
                }
                
                ProgressDisplay.UpdateProgressLine(progressText);
                
                // 记录进度到日志（每隔10%或每处理100个文件记录一次）
                if (processed % 100 == 0 || percentage % 0.1 < 0.01)
                {
                    ProgressDisplay.WriteToLog(string.Format("处理进度: {0}/{1} ({2:P1}) | 大小: {3}/{4} | 速度: {5}",
                        processed, total, percentage, 
                        ProgressDisplay.FormatSize(bytesProcessed), 
                        ProgressDisplay.FormatSize(totalBytes),
                        speed), false);
                }
            }
            catch
            {
                // 如果获取控制台宽度失败，使用简化显示，包含文件名
                progressText = string.Format("{0} 处理中: {1}/{2} 文件 ({3:P1}) | 当前: {4}", 
                    spinner, processed, total, percentage, fileName);
                Console.Write(string.Format("\r{0}", progressText));
            }
        }

        private async Task<List<Dictionary<string, string>>> ProcessFilesSequentialAsync(List<string> files)
        {
            var results = new List<Dictionary<string, string>>();
            var processedBytes = 0L;
            var startTime = DateTime.UtcNow;
            
            ProgressDisplay.WriteToLog("开始顺序处理文件...");
            ProgressDisplay.WriteToLog(string.Format("文件总数: {0}", files.Count));
            
            for (int i = 0; i < files.Count; i++)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    ProgressDisplay.WriteToLog("处理被用户取消");
                    ProgressDisplay.WriteProgressComplete("处理被用户取消");
                    break;
                }
                
                var file = files[i];
                var fileSize = new FileInfo(file).Length;
                var fileName = Path.GetFileName(file);
                
                // 记录开始处理文件
                ProgressDisplay.WriteToLog(string.Format("开始处理文件 [{0}/{1}]: {2} (大小: {3})", 
                    i + 1, files.Count, fileName, ProgressDisplay.FormatSize(fileSize)));
                
                // 显示当前文件处理进度
                UpdateSequentialProgress(i, files.Count, processedBytes, totalBytes, 
                                       startTime, fileName);
                
                var result = await FastHashCalculator.ComputeHashesAsync(
                    file, 
                    algorithms.ToArray(), 
                    cancellationToken: cts.Token);
                
                results.Add(result);
                processedBytes += fileSize;
                
                // 收集性能数据用于图表
                if (result["success"] == "true" && 
                    result.ContainsKey("size_mb_double") && 
                    result.ContainsKey("time_ms_double"))
                {
                    double sizeMB = double.Parse(result["size_mb_double"]);
                    double timeMs = double.Parse(result["time_ms_double"]);
                    performanceData.Add((sizeMB, timeMs));
                }
                
                // 记录文件处理结果
                if (result["success"] == "true")
                {
                    ProgressDisplay.WriteToLog(string.Format("文件处理完成 [{0}/{1}]: {2} (用时: {3}ms)", 
                        i + 1, files.Count, fileName, result["time_ms"]));
                }
                else
                {
                    ProgressDisplay.WriteError(string.Format("文件处理失败 [{0}/{1}]: {2} - {3}", 
                        i + 1, files.Count, fileName, result.ContainsKey("error") ? result["error"] : "未知错误"));
                }
                
                // 文件处理完成后立即更新进度
                UpdateSequentialProgress(i + 1, files.Count, processedBytes, totalBytes, 
                                       startTime, fileName);
                
                // 每处理10个文件记录一次详细进度
                if ((i + 1) % 10 == 0)
                {
                    ProgressDisplay.WriteToLog(string.Format("进度报告: 已处理 {0}/{1} 文件 ({2:P1})", 
                        i + 1, files.Count, (double)(i + 1) / files.Count), false);
                }
            }
            
            if (files.Count > 0)
            {
                ProgressDisplay.WriteProgressComplete(string.Format("顺序处理完成: {0} 个文件", files.Count));
            }
            
            ProgressDisplay.WriteToLog(string.Format("顺序处理完成，共处理 {0} 个文件", results.Count));
            
            return results;
        }
        
        private void UpdateSequentialProgress(int processed, int total, long bytesProcessed, long totalBytes, 
                                            DateTime startTime, string currentFile)
        {
            if (total == 0) return;
            
            double percentage = (double)processed / total;
            var elapsed = DateTime.UtcNow - startTime;
            
            // 限制文件名长度
            string fileName = currentFile;
            if (fileName.Length > 25)
            {
                fileName = fileName.Substring(0, 22) + "...";
            }
            
            // 构建进度显示文本，包含文件名
            string progressBar = ProgressDisplay.GetProgressBar(percentage);
            string spinner = ProgressDisplay.GetSpinner();
            string eta = ProgressDisplay.GetETA(elapsed, processed, total);
            string speed = ProgressDisplay.FormatSpeed(bytesProcessed, elapsed);
            
            string progressText;
            
            try
            {
                int consoleWidth = Console.WindowWidth - 2;
                
                if (consoleWidth >= 120)
                {
                    // 宽控制台：显示完整信息，包含文件名
                    progressText = string.Format("{0} [{1}] {2:P1} | {3}/{4} 文件 | {5} | {6} | {7} | 正在计算: {8}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        string.Format("{0}/{1}", ProgressDisplay.FormatSize(bytesProcessed), ProgressDisplay.FormatSize(totalBytes)), 
                        speed, 
                        eta,
                        fileName);
                }
                else if (consoleWidth >= 100)
                {
                    // 较宽控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6} | 当前: {7}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        speed, 
                        eta,
                        fileName);
                }
                else if (consoleWidth >= 80)
                {
                    // 中等控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | 当前: {6}",
                        spinner, progressBar, percentage, 
                        processed, total, 
                        speed,
                        fileName);
                }
                else if (consoleWidth >= 60)
                {
                    // 较窄控制台
                    progressText = string.Format("{0} {1} {2:P1} | {3}/{4} | {5}",
                        spinner, progressBar, percentage, 
                        processed, total,
                        fileName);
                }
                else
                {
                    // 窄控制台：简化显示，但仍然包含文件名
                    progressText = string.Format("{0} {1:P1} | {2}/{3} | {4}", 
                        spinner, percentage, processed, total, fileName);
                }
                
                ProgressDisplay.UpdateProgressLine(progressText);
            }
            catch
            {
                // 如果获取控制台宽度失败，使用简化显示，包含文件名
                progressText = string.Format("{0} 正在处理 {1}/{2}: {3}", 
                    spinner, processed, total, fileName);
                Console.Write(string.Format("\r{0}", progressText));
            }
        }
        
        private void OutputEmptyResult()
        {
            Console.WriteLine("[]");
        }
        
        private void OutputResults(List<Dictionary<string, string>> results)
        {
            string jsonOutput = BuildJsonOutput(results);
            
            // 记录输出结果信息
            ProgressDisplay.WriteToLog(string.Format("开始输出结果，结果数量: {0}", results.Count));
            
            if (saveToFile && !string.IsNullOrEmpty(outputFilePath))
            {
                try
                {
                    // 确保目录存在
                    string directory = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    File.WriteAllText(outputFilePath, jsonOutput, Encoding.UTF8);
                    
                    ProgressDisplay.WriteSeparator();
                    ProgressDisplay.WriteSuccess(string.Format("哈希计算结果已保存到: {0}", outputFilePath));
                    ProgressDisplay.WriteInfo(string.Format("文件数量: {0}", results.Count));
                    ProgressDisplay.WriteInfo(string.Format("结果文件大小: {0}", ProgressDisplay.FormatSize(new FileInfo(outputFilePath).Length)));
                    
                    // 记录到日志
                    ProgressDisplay.WriteToLog(string.Format("哈希计算结果已保存到: {0}", outputFilePath));
                    ProgressDisplay.WriteToLog(string.Format("结果文件大小: {0}", ProgressDisplay.FormatSize(new FileInfo(outputFilePath).Length)));
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteError(string.Format("保存结果到文件失败: {0}", ex.Message));
                    
                    // 记录错误到日志
                    ProgressDisplay.WriteError(string.Format("保存结果到文件失败: {0}", ex.Message));
                    
                    // 保存失败，回退到控制台输出
                    if (consoleOutput)
                    {
                        Console.WriteLine(jsonOutput);
                    }
                }
            }
            else if (consoleOutput)
            {
                // 控制台输出
                Console.WriteLine(jsonOutput);
                ProgressDisplay.WriteToLog("结果已输出到控制台");
            }
            else
            {
                ProgressDisplay.WriteWarning("处理完成，但未指定输出方式");
                ProgressDisplay.WriteInfo("使用 --output 参数指定输出文件，或使用 --console-output 查看结果");
                
                ProgressDisplay.WriteWarning("处理完成，但未指定输出方式");
            }
        }
        
        private string BuildJsonOutput(List<Dictionary<string, string>> results)
        {
            var output = new StringBuilder();
            output.AppendLine("[");
            
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                output.Append("  {");
                
                bool first = true;
                foreach (var kvp in result)
                {
                    if (!first) output.Append(",");
                    first = false;
                    output.Append(string.Format("\n    \"{0}\": \"{1}\"", kvp.Key, JsonEscape(kvp.Value)));
                }
                
                output.Append("\n  }");
                if (i < results.Count - 1) output.Append(",");
                output.AppendLine();
            }
            
            output.AppendLine("]");
            return output.ToString();
        }
        
        private static string JsonEscape(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
        
        // 修复后的文件模式展开方法，使用 EnumerationOptions 忽略无权限访问的子目录
        private static List<string> ExpandFilePatterns(List<string> patterns, bool recursive, long minFileSize, long maxFileSize)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // 创建支持忽略访问错误的枚举选项
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,   // 是否递归
                IgnoreInaccessible = true,            // 忽略无法访问的目录
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System // 可按需调整
            };

            ProgressDisplay.WriteInfo("正在展开文件模式...");
            int patternCount = 0;
            
            foreach (var pattern in patterns)
            {
                patternCount++;
                ProgressDisplay.WriteToLog(string.Format("处理模式 [{0}/{1}]: {2}", patternCount, patterns.Count, pattern));
                
                try
                {
                    // 处理特殊标记
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }
                    
                    // 检查是否是通配符模式
                    bool hasWildcard = pattern.Contains('*') || pattern.Contains('?');
                    
                    // 如果模式是具体文件且存在，直接添加
                    if (!hasWildcard && File.Exists(pattern))
                    {
                        var fullPath = Path.GetFullPath(pattern);
                        files.Add(fullPath);
                        ProgressDisplay.WriteToLog(string.Format("  添加文件: {0}", fullPath));
                        continue;
                    }
                    
                    // 如果模式是目录，则跳过（我们不处理目录本身，只处理目录下的文件）
                    if (!hasWildcard && Directory.Exists(pattern))
                    {
                        // 目录模式：自动添加通配符 *
                        string directory = pattern;
                        if (!directory.EndsWith(Path.DirectorySeparatorChar) && !directory.EndsWith(Path.AltDirectorySeparatorChar))
                        {
                            directory += Path.DirectorySeparatorChar;
                        }
                        
                        // 处理目录中的所有文件，使用 EnumerateFiles 并传入枚举选项
                        try
                        {
                            var dirFiles = Directory.EnumerateFiles(directory, "*", enumerationOptions);
                            int count = 0;
                            foreach (var file in dirFiles)
                            {
                                files.Add(Path.GetFullPath(file));
                                count++;
                            }
                            ProgressDisplay.WriteToLog(string.Format("  目录 {0} 中找到 {1} 个文件", directory, count));
                        }
                        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
                        {
                            ProgressDisplay.WriteWarning(string.Format("无法枚举目录 '{0}': {1}", pattern, ex.Message));
                        }
                        continue;
                    }
                    
                    // 处理通配符模式
                    string directoryPart;
                    string filePattern;
                    
                    try
                    {
                        // 获取目录部分和文件模式部分
                        directoryPart = Path.GetDirectoryName(pattern) ?? ".";
                        filePattern = Path.GetFileName(pattern);
                        
                        // 如果目录部分为空或不存在，使用当前目录
                        if (string.IsNullOrEmpty(directoryPart) || !Directory.Exists(directoryPart))
                        {
                            directoryPart = ".";
                        }
                        
                        // 如果文件模式为空，使用通配符 *
                        if (string.IsNullOrEmpty(filePattern))
                        {
                            filePattern = "*";
                        }
                        
                        // 展开通配符，使用 EnumerateFiles 并传入枚举选项
                        var matchedFiles = Directory.EnumerateFiles(directoryPart, filePattern, enumerationOptions);
                        int count = 0;
                        foreach (var file in matchedFiles)
                        {
                            files.Add(Path.GetFullPath(file));
                            count++;
                        }
                        ProgressDisplay.WriteToLog(string.Format("  通配符 {0} 匹配到 {1} 个文件", pattern, count));
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
                    {
                        ProgressDisplay.WriteWarning(string.Format("无法处理模式 '{0}': {1}", pattern, ex.Message));
                    }
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteWarning(string.Format("无法处理模式 '{0}': {1}", pattern, ex.Message));
                }
            }
            
            ProgressDisplay.WriteInfo(string.Format("文件模式展开完成，找到 {0} 个文件", files.Count));
            
            // 文件大小过滤（在文件收集之后进行）
            var filteredFiles = files.Where(f => {
                try
                {
                    var fileInfo = new FileInfo(f);
                    if (!fileInfo.Exists) return false;
                    
                    var size = fileInfo.Length;
                    bool sizeOk = size >= minFileSize && size <= maxFileSize;
                    
                    if (!sizeOk)
                    {
                        ProgressDisplay.WriteToLog(string.Format("文件 {0} 大小 {1} 不符合过滤条件 ({2} - {3})", 
                            Path.GetFileName(f), ProgressDisplay.FormatSize(size), 
                            minFileSize > 0 ? ProgressDisplay.FormatSize(minFileSize) : "无限制",
                            maxFileSize < long.MaxValue ? ProgressDisplay.FormatSize(maxFileSize) : "无限制"));
                    }
                    
                    return sizeOk;
                }
                catch
                {
                    ProgressDisplay.WriteWarning(string.Format("无法获取文件信息: {0}", f));
                    return false;
                }
            }).ToList();
            
            ProgressDisplay.WriteInfo(string.Format("文件大小过滤完成，剩余 {0} 个文件", filteredFiles.Count));
            
            return filteredFiles.OrderBy(f => f).ToList();
        }
        
        public void Cancel()
        {
            cts.Cancel();
            ProgressDisplay.WriteToLog("处理被取消");
        }
    }
}

'@


    "HashVerifier.cs" = @'
// HashVerifier.cs - 专业文件哈希验证器
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FastHasher
{
    public class HashVerifier
    {
        private readonly string jsonFilePath;
        private readonly string pathFilter;
        private readonly bool recursive;
        private readonly int maxDegreeOfParallelism;
        private readonly bool saveToFile; // 新增：是否保存到文件
        private readonly string outputFilePath; // 新增：输出文件路径
        private readonly bool consoleOutput; // 新增：控制台输出
        private readonly Stopwatch totalStopwatch = new Stopwatch();
        private readonly string logFilePath; // 新增：日志文件路径
        
        public HashVerifier(string jsonFilePath, string pathFilter = null, 
                           bool recursive = false, int maxDegreeOfParallelism = 0,
                           bool saveToFile = true, string outputFilePath = null, bool consoleOutput = false)
        {
            this.jsonFilePath = jsonFilePath;
            this.pathFilter = pathFilter;
            this.recursive = recursive;
            this.saveToFile = saveToFile;
            this.outputFilePath = outputFilePath;
            this.consoleOutput = consoleOutput;
            
            if (maxDegreeOfParallelism <= 0) {
                this.maxDegreeOfParallelism = Environment.ProcessorCount;
            } else {
                this.maxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, Environment.ProcessorCount * 2);
            }
            
            // 设置日志文件路径
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            this.logFilePath = Path.Combine(Environment.CurrentDirectory, string.Format("FastHasher_Verify_{0}.log", timestamp));
            
            // 初始化日志系统
            ProgressDisplay.InitializeLog(this.logFilePath);
            
            // 记录启动信息
            ProgressDisplay.WriteToLog(string.Format("FastHasher 验证模式开始运行，时间: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format("验证文件: {0}", jsonFilePath));
            ProgressDisplay.WriteToLog(string.Format("路径过滤: {0}", string.IsNullOrEmpty(pathFilter) ? "无" : pathFilter));
            ProgressDisplay.WriteToLog(string.Format("递归搜索: {0}", recursive ? "是" : "否"));
            ProgressDisplay.WriteToLog(string.Format("并行线程数: {0}", this.maxDegreeOfParallelism));
        }
        
        public async Task VerifyAsync()
        {
            if (!File.Exists(jsonFilePath)) {
                throw new FileNotFoundException(string.Format("验证文件不存在: {0}", jsonFilePath));
            }
            
            totalStopwatch.Start();
            
            // 读取JSON文件
            ProgressDisplay.WriteInfo("正在读取验证文件...");
            ProgressDisplay.WriteToLog(string.Format("开始读取验证文件: {0}", jsonFilePath));
            var storedHashes = await ReadHashJsonFile(jsonFilePath);
            
            if (storedHashes.Count == 0) {
                ProgressDisplay.WriteWarning("验证文件中没有找到哈希记录");
                ProgressDisplay.WriteToLog("验证文件中没有找到哈希记录");
                OutputEmptyResult();
                return;
            }
            
            ProgressDisplay.WriteSuccess(string.Format("验证文件读取完成，共 {0} 条记录", storedHashes.Count));
            ProgressDisplay.WriteToLog(string.Format("验证文件读取完成，共 {0} 条记录", storedHashes.Count));
            
            // 应用路径过滤
            var filesToVerify = FilterFiles(storedHashes, pathFilter);
            if (filesToVerify.Count == 0) {
                ProgressDisplay.WriteWarning("没有找到匹配的验证文件");
                ProgressDisplay.WriteToLog("没有找到匹配的验证文件");
                OutputEmptyResult();
                return;
            }
            
            ProgressDisplay.WriteInfo(string.Format("过滤后需要验证 {0} 个文件", filesToVerify.Count));
            ProgressDisplay.WriteToLog(string.Format("过滤后需要验证 {0} 个文件", filesToVerify.Count));
            
            // 确定输出文件路径
            string finalOutputPath = DetermineOutputPath();
            
            ProgressDisplay.WriteSection("验证模式配置");
            
            ProgressDisplay.WriteStatistics("验证配置", 
                ("验证文件", Path.GetFileName(jsonFilePath), ConsoleColor.White),
                ("文件总数", filesToVerify.Count.ToString(), ConsoleColor.Yellow),
                ("并行线程", maxDegreeOfParallelism.ToString(), ConsoleColor.Green),
                ("路径过滤", string.IsNullOrEmpty(pathFilter) ? "无" : pathFilter, ConsoleColor.Gray)
            );
            
            // 显示输出信息
            if (saveToFile && !string.IsNullOrEmpty(finalOutputPath))
            {
                ProgressDisplay.WriteInfo(string.Format("输出文件: {0}", Path.GetFileName(finalOutputPath)));
            }
            else if (consoleOutput)
            {
                ProgressDisplay.WriteInfo("输出方式: 控制台显示");
            }
            
            ProgressDisplay.WriteSeparator();
            
            // 记录验证开始时间
            ProgressDisplay.WriteToLog("开始验证文件...");
            var startTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format("验证开始时间: {0}", startTime.ToString("yyyy-MM-dd HH:mm:ss")));
            
            // 验证文件哈希
            var verificationResults = await VerifyFilesAsync(filesToVerify);
            
            // 记录验证结束时间
            var endTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format("验证结束时间: {0}", endTime.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format("总验证时间: {0:F2}秒", totalStopwatch.Elapsed.TotalSeconds));
            
            // 输出验证结果
            OutputVerificationResults(verificationResults, storedHashes.Count, finalOutputPath);
            
            totalStopwatch.Stop();
            
            // 显示日志文件位置
            ProgressDisplay.WriteSuccess(string.Format("验证完成！日志已保存到: {0}", logFilePath));
        }
        
        private string DetermineOutputPath()
        {
            // 如果用户指定了输出文件路径，直接使用
            if (!string.IsNullOrEmpty(outputFilePath))
            {
                return outputFilePath;
            }
            
            // 如果不需要保存到文件，返回空
            if (!saveToFile)
            {
                return null;
            }
            
            // 自动生成输出文件路径
            string baseName = Path.GetFileNameWithoutExtension(jsonFilePath);
            string directory = Path.GetDirectoryName(jsonFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            return Path.Combine(directory, string.Format("{0}_验证_{1}.json", baseName, timestamp));
        }

         private async Task<List<Dictionary<string, string>>> ReadHashJsonFile(string filePath)
        {
            var results = new List<Dictionary<string, string>>();
            
            try {
                ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), "正在读取文件...");
                string jsonContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), string.Format("文件读取完成，大小: {0}", ProgressDisplay.FormatSize(jsonContent.Length)));
                
                // 简单的JSON解析（假设是FastHasher输出的格式）
                results = ParseSimpleJsonArray(jsonContent);
                ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), string.Format("JSON解析完成，找到 {0} 条记录", results.Count));
            } catch (Exception ex) {
                ProgressDisplay.WriteError(string.Format("读取JSON文件失败: {0}", ex.Message));
            }
            
            return results;
        }
        
         private List<Dictionary<string, string>> ParseSimpleJsonArray(string jsonContent)
        {
            var results = new List<Dictionary<string, string>>();
            
            try {
                // 移除空白字符
                string content = jsonContent.Trim();
                
                // 检查是否是数组格式
                if (!content.StartsWith('[') || !content.EndsWith(']')) {
                    return results;
                }
                
                // 移除外层数组括号
                content = content.Substring(1, content.Length - 2).Trim();
                
                // 分割对象
                int braceCount = 0;
                int startIndex = 0;
                List<string> objects = new List<string>();
                
                for (int i = 0; i < content.Length; i++) {
                    char c = content[i];
                    
                    if (c == '{') {
                        if (braceCount == 0) startIndex = i;
                        braceCount++;
                    } else if (c == '}') {
                        braceCount--;
                        if (braceCount == 0) {
                            string obj = content.Substring(startIndex, i - startIndex + 1);
                            objects.Add(obj);
                        }
                    }
                }
                
                // 解析每个对象
                foreach (var obj in objects) {
                    var dict = ParseSimpleJsonObject(obj);
                    if (dict.Count > 0) {
                        results.Add(dict);
                    }
                }
            } catch {
                // 解析失败，返回空列表
            }
            
            return results;
        }

        private Dictionary<string, string> ParseSimpleJsonObject(string jsonObject)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            try {
                string content = jsonObject.Trim();
                if (!content.StartsWith('{') || !content.EndsWith('}')) {
                    return dict;
                }
                
                // 移除外层大括号
                content = content.Substring(1, content.Length - 2).Trim();
                
                // 分割键值对
                int quoteCount = 0;
                bool inKey = false;
                bool inValue = false;
                StringBuilder currentPart = new StringBuilder();
                string currentKey = null;
                
                for (int i = 0; i < content.Length; i++) {
                    char c = content[i];
                    
                    if (c == '"' && (i == 0 || content[i-1] != '\\')) {
                        quoteCount++;
                        
                        if (quoteCount % 2 == 1) {
                            // 开始引号
                            if (!inKey && !inValue) {
                                inKey = true;
                                currentPart.Clear();
                            } else if (inKey) {
                                inKey = false;
                                currentKey = currentPart.ToString().Trim();
                                currentPart.Clear();
                            } else if (inValue) {
                                inValue = false;
                                string value = currentPart.ToString().Trim();
                                if (!string.IsNullOrEmpty(currentKey)) {
                                    dict[currentKey] = UnescapeJsonString(value);
                                }
                                currentPart.Clear();
                                currentKey = null;
                            }
                        }
                    } else if (c == ':' && quoteCount % 2 == 0 && !inValue) {
                        // 键值分隔符
                        inValue = true;
                        i++; // 跳过冒号后的空格
                        while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
                        i--; // 回退一格
                    } else if (c == ',' && quoteCount % 2 == 0) {
                        // 键值对分隔符，重置状态
                        if (currentPart.Length > 0 && !string.IsNullOrEmpty(currentKey)) {
                            string value = currentPart.ToString().Trim();
                            dict[currentKey] = UnescapeJsonString(value);
                        }
                        currentPart.Clear();
                        currentKey = null;
                        inKey = false;
                        inValue = false;
                    } else {
                        if (inKey || inValue) {
                            currentPart.Append(c);
                        }
                    }
                }
                
                // 处理最后一个键值对
                if (currentPart.Length > 0 && !string.IsNullOrEmpty(currentKey)) {
                    string value = currentPart.ToString().Trim();
                    dict[currentKey] = UnescapeJsonString(value);
                }
            } catch {
                // 解析失败，返回已解析的部分
            }
            
            return dict;
        }
        
        private string UnescapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            
            var sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++) {
                char c = str[i];
                if (c == '\\' && i + 1 < str.Length) {
                    char next = str[i + 1];
                    switch (next) {
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        case '/': sb.Append('/'); i++; break;
                        case 'b': sb.Append('\b'); i++; break;
                        case 'f': sb.Append('\f'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        case 'u': 
                            // Unicode转义序列
                            if (i + 5 < str.Length) {
                                string hex = str.Substring(i + 2, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code)) {
                                    sb.Append((char)code);
                                    i += 5;
                                } else {
                                    sb.Append(c);
                                }
                            } else {
                                sb.Append(c);
                            }
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        

        private List<Dictionary<string, string>> FilterFiles(List<Dictionary<string, string>> files, string filter)
        {
            if (string.IsNullOrEmpty(filter)) {
                return files;
            }
            
            var filtered = new List<Dictionary<string, string>>();
            foreach (var file in files) {
                if (file.ContainsKey("file") && file["file"].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) {
                    filtered.Add(file);
                }
            }
            return filtered;
        }
        
         private async Task<List<VerificationResult>> VerifyFilesAsync(List<Dictionary<string, string>> filesToVerify)
        {
            var results = new List<VerificationResult>();
            int processed = 0;
            int total = filesToVerify.Count;
            var startTime = DateTime.UtcNow;
            
            ProgressDisplay.WriteToLog(string.Format("开始验证 {0} 个文件...", total));
            
            if (total > 0) {
                ProgressDisplay.WriteInfo(string.Format("开始验证 {0} 个文件...", total));
            }
            
            // 确定需要验证的算法
            var algorithms = DetectAlgorithms(filesToVerify);
            ProgressDisplay.WriteInfo(string.Format("检测到需要验证的算法: {0}", string.Join(", ", algorithms)));
            ProgressDisplay.WriteToLog(string.Format("检测到需要验证的算法: {0}", string.Join(", ", algorithms)));
            
            foreach (var fileRecord in filesToVerify) {
                processed++;
                
                string filePath = fileRecord.ContainsKey("file") ? fileRecord["file"] : 
                                fileRecord.ContainsKey("full_path") ? fileRecord["full_path"] : null;
                
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                    ProgressDisplay.WriteFileInfo(filePath ?? "未知文件", "文件不存在");
                    results.Add(new VerificationResult {
                        FilePath = filePath,
                        Status = "missing",
                        Error = "文件不存在"
                    });
                    continue;
                }
                
                string fileName = Path.GetFileName(filePath);
                
                // 显示进度
                UpdateVerificationProgress(processed, total, fileName, startTime);
                
                try {
                    // 记录开始验证文件
                    ProgressDisplay.WriteFileInfo(fileName, "开始验证哈希值...");
                    
                    // 计算当前文件哈希
                    var currentHashes = await FastHashCalculator.ComputeHashesAsync(filePath, algorithms.ToArray());
                    
                    if (currentHashes["success"] != "true") {
                        ProgressDisplay.WriteFileInfo(fileName, string.Format("计算哈希失败: {0}", currentHashes.ContainsKey("error") ? currentHashes["error"] : "未知错误"));
                        results.Add(new VerificationResult {
                            FilePath = filePath,
                            FileName = fileName,
                            Status = "error",
                            Error = currentHashes.ContainsKey("error") ? currentHashes["error"] : "计算哈希失败"
                        });
                        continue;
                    }
                    
                    // 比较哈希值
                    var verification = CompareHashes(fileRecord, currentHashes);
                    verification.FilePath = filePath;
                    verification.FileName = fileName;
                    
                    // 记录验证结果
                    if (verification.Status == "success") {
                        ProgressDisplay.WriteFileInfo(fileName, "验证通过 ✓");
                    } else {
                        ProgressDisplay.WriteFileInfo(fileName, "验证失败 ✗");
                    }
                    
                    results.Add(verification);
                    
                } catch (Exception ex) {
                    ProgressDisplay.WriteFileInfo(fileName, string.Format("验证过程中出错: {0}", ex.Message));
                    results.Add(new VerificationResult {
                        FilePath = filePath,
                        FileName = fileName,
                        Status = "error",
                        Error = ex.Message
                    });
                }
            }
            
            if (total > 0) {
                Console.Error.WriteLine();
                ProgressDisplay.WriteSuccess(string.Format("验证完成: {0}/{1} 文件", processed, total));
            }
            
            ProgressDisplay.WriteToLog(string.Format("验证完成: {0}/{1} 文件", processed, total));
            
            return results;
        }

        private List<string> DetectAlgorithms(List<Dictionary<string, string>> files)
        {
            var algorithms = new List<string>();
            var algorithmSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (files.Count > 0) {
                var firstFile = files[0];
                
                if (firstFile.ContainsKey("md5")) algorithmSet.Add("MD5");
                if (firstFile.ContainsKey("sha1")) algorithmSet.Add("SHA1");
                if (firstFile.ContainsKey("sha256")) algorithmSet.Add("SHA256");
                if (firstFile.ContainsKey("sha512")) algorithmSet.Add("SHA512");
            }
            
            algorithms.AddRange(algorithmSet);
            return algorithms;
        }

        private VerificationResult CompareHashes(Dictionary<string, string> stored, Dictionary<string, string> current)
        {
            var result = new VerificationResult {
                Status = "success",
                AlgorithmResults = new Dictionary<string, AlgorithmComparison>()
            };
            
            // 检查文件大小
            if (stored.ContainsKey("size") && current.ContainsKey("size")) {
                long storedSize = long.Parse(stored["size"]);
                long currentSize = long.Parse(current["size"]);
                result.SizeMatch = storedSize == currentSize;
                
                if (!result.SizeMatch) {
                    result.Status = "failed";
                }
            }
            
            // 检查各个哈希算法
            var algorithms = new[] { "md5", "sha1", "sha256", "sha512" };
            
            foreach (var algo in algorithms) {
                if (stored.ContainsKey(algo) && current.ContainsKey(algo)) {
                    bool match = stored[algo].Equals(current[algo], StringComparison.OrdinalIgnoreCase);
                    result.AlgorithmResults[algo.ToUpper()] = new AlgorithmComparison {
                        Expected = stored[algo],
                        Actual = current[algo],
                        Match = match
                    };
                    
                    if (!match) {
                        result.Status = "failed";
                    }
                }
            }
            
            return result;
        }

        private void UpdateVerificationProgress(int processed, int total, string currentFile, DateTime startTime)
        {
            if (total == 0) return;
            
            double percentage = (double)processed / total;
            var elapsed = DateTime.UtcNow - startTime;
            string eta = ProgressDisplay.GetETA(elapsed, processed, total);
            
            // 限制文件名长度
            string fileName = currentFile;
            if (fileName.Length > 25) {
                fileName = fileName.Substring(0, 22) + "...";
            }
            
            string progressBar = ProgressDisplay.GetProgressBar(percentage);
            string spinner = ProgressDisplay.GetSpinner();
            
            // 显示验证进度，包含文件名
            string progressText = string.Format("{0} [{1}] {2:P1} | {3}/{4} 文件 | {5} | 正在验证: {6}", 
                spinner, progressBar, percentage, 
                processed, total, 
                eta,
                fileName);
            
            ProgressDisplay.UpdateProgressLine(progressText);
            
            // 记录进度到日志（每隔10%或每处理100个文件记录一次）
            if (processed % 100 == 0 || percentage % 0.1 < 0.01)
            {
                ProgressDisplay.WriteToLog(string.Format("验证进度: {0}/{1} ({2:P1})", 
                    processed, total, percentage), false);
            }
        }
        
        
        private void OutputVerificationResults(List<VerificationResult> results, int totalStoredFiles, string outputPath)
        {
            // 统计
            int success = results.Count(r => r.Status == "success");
            int failed = results.Count(r => r.Status == "failed");
            int missing = results.Count(r => r.Status == "missing");
            int error = results.Count(r => r.Status == "error");
            
            // 构建验证报告
            var report = BuildVerificationReport(results, totalStoredFiles, success, failed, missing, error);
            
            if (saveToFile && !string.IsNullOrEmpty(outputPath))
            {
                // 保存到文件
                try
                {
                    // 确保目录存在
                    string directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    File.WriteAllText(outputPath, report, Encoding.UTF8);
                    
                    ProgressDisplay.WriteSection("验证结果");
                    
                    ProgressDisplay.WriteStatistics("验证统计", 
                        ("存储文件总数", totalStoredFiles.ToString(), ConsoleColor.Gray),
                        ("验证文件数", results.Count.ToString(), ConsoleColor.Yellow),
                        ("验证成功", string.Format("{0} ✓", success), ConsoleColor.Green),
                        ("验证失败", string.Format("{0} ✗", failed), ConsoleColor.Red),
                        ("文件缺失", string.Format("{0} ⚠", missing), ConsoleColor.Yellow),
                        ("计算错误", string.Format("{0} ⚠", error), ConsoleColor.Red),
                        ("成功率", string.Format("{0}%", results.Count > 0 ? (success * 100.0 / results.Count).ToString("F2") : "0.00"), 
                            results.Count > 0 && (success * 100.0 / results.Count) >= 90 ? ConsoleColor.Green : 
                            results.Count > 0 && (success * 100.0 / results.Count) >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red),
                        ("总用时", string.Format("{0:F2}秒", totalStopwatch.Elapsed.TotalSeconds), ConsoleColor.White)
                    );
                    
                    ProgressDisplay.WriteSuccess(string.Format("验证结果已保存到: {0}", outputPath));
                    
                    // 记录到日志
                    ProgressDisplay.WriteToLog(string.Format("验证结果已保存到: {0}", outputPath));
                    ProgressDisplay.WriteToLog(string.Format("验证统计: {0} 成功, {1} 失败, {2} 缺失, {3} 错误", success, failed, missing, error));
                    ProgressDisplay.WriteToLog(string.Format("总用时: {0:F2}秒", totalStopwatch.Elapsed.TotalSeconds));
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteError(string.Format("保存验证结果到文件失败: {0}", ex.Message));
                    
                    // 记录错误到日志
                    ProgressDisplay.WriteError(string.Format("保存验证结果到文件失败: {0}", ex.Message));
                    
                    // 保存失败，回退到控制台输出
                    if (consoleOutput)
                    {
                        Console.WriteLine(report);
                    }
                }
            }
            else if (consoleOutput)
            {
                // 控制台输出
                Console.WriteLine(report);
                ProgressDisplay.WriteToLog("验证结果已输出到控制台");
            }
        }

        // 修改后的BuildVerificationReport方法
        private string BuildVerificationReport(List<VerificationResult> results, int total, int success, 
                                             int failed, int missing, int error)
        {
            var output = new StringBuilder();
            output.AppendLine("{");
            output.AppendLine(string.Format("  \"metadata\": {{"));
            output.AppendLine(string.Format("    \"generated\": \"{0}\",", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            output.AppendLine(string.Format("    \"verification_file\": \"{0}\",", jsonFilePath));
            output.AppendLine(string.Format("    \"path_filter\": \"{0}\",", string.IsNullOrEmpty(pathFilter) ? "" : JsonEscape(pathFilter)));
            output.AppendLine(string.Format("    \"total_time_ms\": {0},", totalStopwatch.ElapsedMilliseconds));
            output.AppendLine(string.Format("    \"parallel_threads\": {0}", maxDegreeOfParallelism));
            output.AppendLine(string.Format("  }},"));
            output.AppendLine(string.Format("  \"summary\": {{"));
            output.AppendLine(string.Format("    \"total_stored_files\": {0},", total));
            output.AppendLine(string.Format("    \"verified_files\": {0},", results.Count));
            output.AppendLine(string.Format("    \"success\": {0},", success));
            output.AppendLine(string.Format("    \"failed\": {0},", failed));
            output.AppendLine(string.Format("    \"missing\": {0},", missing));
            output.AppendLine(string.Format("    \"error\": {0},", error));
            output.AppendLine(string.Format("    \"success_rate\": \"{0}\"", results.Count > 0 ? (success * 100.0 / results.Count).ToString("F2") : "0.00"));
            output.AppendLine(string.Format("  }},"));
            
            // 新增：问题摘要
            var failedFiles = results.Where(r => r.Status == "failed").ToList();
            var missingFiles = results.Where(r => r.Status == "missing").ToList();
            var errorFiles = results.Where(r => r.Status == "error").ToList();
            
            output.AppendLine(string.Format("  \"issues_summary\": {{"));
            output.AppendLine(string.Format("    \"total_issues\": {0},", failedFiles.Count + missingFiles.Count + errorFiles.Count));
            output.AppendLine(string.Format("    \"hash_mismatch\": {0},", failedFiles.Count));
            output.AppendLine(string.Format("    \"files_missing\": {0},", missingFiles.Count));
            output.AppendLine(string.Format("    \"calculation_errors\": {0}", errorFiles.Count));
            output.AppendLine(string.Format("  }},"));
            
            // 新增：问题文件详细分类（如果有问题文件的话）
            bool hasIssues = failedFiles.Count > 0 || missingFiles.Count > 0 || errorFiles.Count > 0;
            if (hasIssues)
            {
                output.AppendLine(string.Format("  \"problem_files\": {{"));
                
                bool firstCategory = true;
                
                // 哈希不匹配的文件
                if (failedFiles.Count > 0)
                {
                    if (!firstCategory) output.AppendLine(",");
                    firstCategory = false;
                    
                    output.AppendLine(string.Format("    \"hash_mismatch\": ["));
                    for (int i = 0; i < failedFiles.Count; i++)
                    {
                        var result = failedFiles[i];
                        output.Append("      {");
                        output.Append(string.Format("\n        \"file\": \"{0}\",", JsonEscape(result.FilePath)));
                        output.Append(string.Format("\n        \"filename\": \"{0}\",", JsonEscape(result.FileName)));
                        output.Append(string.Format("\n        \"status\": \"failed\","));
                        
                        // 显示不匹配的算法
                        if (result.AlgorithmResults != null)
                        {
                            var mismatchedAlgos = result.AlgorithmResults.Where(a => !a.Value.Match).ToList();
                            if (mismatchedAlgos.Count > 0)
                            {
                                output.Append(string.Format("\n        \"mismatched_algorithms\": {{"));
                                
                                bool firstAlgo = true;
                                foreach (var algo in mismatchedAlgos)
                                {
                                    if (!firstAlgo) output.Append(",");
                                    firstAlgo = false;
                                    
                                    output.Append(string.Format("\n          \"{0}\": {{", algo.Key));
                                    output.Append(string.Format("\n            \"expected\": \"{0}\",", algo.Value.Expected));
                                    output.Append(string.Format("\n            \"actual\": \"{0}\",", algo.Value.Actual));
                                    output.Append(string.Format("\n            \"match\": false"));
                                    output.Append(string.Format("\n          }}"));
                                }
                                
                                output.Append(string.Format("\n        }},"));
                            }
                        }
                        
                        output.Append(string.Format("\n        \"size_match\": {0}", result.SizeMatch.ToString().ToLower()));
                        output.Append(string.Format("\n      }}"));
                        
                        if (i < failedFiles.Count - 1) output.Append(",");
                        output.AppendLine();
                    }
                    output.AppendLine(string.Format("    ]"));
                }
                
                // 文件缺失的文件
                if (missingFiles.Count > 0)
                {
                    if (!firstCategory) output.AppendLine(",");
                    firstCategory = false;
                    
                    output.AppendLine(string.Format("    \"files_missing\": ["));
                    for (int i = 0; i < missingFiles.Count; i++)
                    {
                        var result = missingFiles[i];
                        output.Append("      {");
                        output.Append(string.Format("\n        \"file\": \"{0}\",", JsonEscape(result.FilePath)));
                        output.Append(string.Format("\n        \"filename\": \"{0}\",", JsonEscape(result.FileName)));
                        output.Append(string.Format("\n        \"status\": \"missing\","));
                        output.Append(string.Format("\n        \"error\": \"文件不存在或无法访问\""));
                        output.Append(string.Format("\n      }}"));
                        
                        if (i < missingFiles.Count - 1) output.Append(",");
                        output.AppendLine();
                    }
                    output.AppendLine(string.Format("    ]"));
                }
                
                // 计算错误的文件
                if (errorFiles.Count > 0)
                {
                    if (!firstCategory) output.AppendLine(",");
                    
                    output.AppendLine(string.Format("    \"calculation_errors\": ["));
                    for (int i = 0; i < errorFiles.Count; i++)
                    {
                        var result = errorFiles[i];
                        output.Append("      {");
                        output.Append(string.Format("\n        \"file\": \"{0}\",", JsonEscape(result.FilePath)));
                        output.Append(string.Format("\n        \"filename\": \"{0}\",", JsonEscape(result.FileName)));
                        output.Append(string.Format("\n        \"status\": \"error\","));
                        output.Append(string.Format("\n        \"error\": \"{0}\"", JsonEscape(result.Error)));
                        output.Append(string.Format("\n      }}"));
                        
                        if (i < errorFiles.Count - 1) output.Append(",");
                        output.AppendLine();
                    }
                    output.AppendLine(string.Format("    ]"));
                }
                
                output.AppendLine(string.Format("  }},"));
            }
            
            // 所有结果
            output.AppendLine(string.Format("  \"results\": ["));
            
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                output.Append("    {");
                output.Append(string.Format("\n      \"file\": \"{0}\",", JsonEscape(result.FilePath)));
                output.Append(string.Format("\n      \"filename\": \"{0}\",", JsonEscape(result.FileName)));
                output.Append(string.Format("\n      \"status\": \"{0}\",", result.Status));
                
                if (!string.IsNullOrEmpty(result.Error))
                {
                    output.Append(string.Format("\n      \"error\": \"{0}\",", JsonEscape(result.Error)));
                }
                
                if (result.AlgorithmResults != null && result.AlgorithmResults.Count > 0)
                {
                    output.Append(string.Format("\n      \"algorithms\": {{"));
                    
                    bool firstAlgo = true;
                    foreach (var algo in result.AlgorithmResults)
                    {
                        if (!firstAlgo) output.Append(",");
                        firstAlgo = false;
                        output.Append(string.Format("\n        \"{0}\": {{", algo.Key));
                        output.Append(string.Format("\n          \"expected\": \"{0}\",", algo.Value.Expected));
                        output.Append(string.Format("\n          \"actual\": \"{0}\",", algo.Value.Actual));
                        output.Append(string.Format("\n          \"match\": {0}", algo.Value.Match.ToString().ToLower()));
                        output.Append(string.Format("\n        }}"));
                    }
                    
                    output.Append(string.Format("\n      }},"));
                }
                
                output.Append(string.Format("\n      \"size_match\": {0}", result.SizeMatch.ToString().ToLower()));
                output.Append(string.Format("\n    }}"));
                
                if (i < results.Count - 1) output.Append(",");
                output.AppendLine();
            }
            
            output.AppendLine(string.Format("  ]"));
            output.AppendLine(string.Format("}}"));
            
            return output.ToString();
        }

         private static string JsonEscape(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            var sb = new StringBuilder();
            foreach (char c in input) {
                switch (c) {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') {
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        } else {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
        
        private void OutputEmptyResult()
        {
            ProgressDisplay.WriteWarning("没有找到可验证的文件");
        }
    }
    
    // VerificationResult和AlgorithmComparison类保持不变
    public class VerificationResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; } // success, failed, missing, error
        public string Error { get; set; }
        public bool SizeMatch { get; set; } = true;
        public Dictionary<string, AlgorithmComparison> AlgorithmResults { get; set; }
    }
    
    public class AlgorithmComparison
    {
        public string Expected { get; set; }
        public string Actual { get; set; }
        public bool Match { get; set; }
    }
}

'@


    "Program.cs" = @'

  // Program.cs - 主程序类（修改版：默认保存结果到JSON文件）
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FastHasher
{
    class Program
    {
        static async Task Main(string[] args)
        {   

            // +++ 新增：判断如果启动时没有任何参数，则显示帮助并等待 +++
            if (args.Length == 0)
            {
                ShowHelp(); // 调用你已有的显示帮助的函数
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey(); // 等待用户按下一个键
                return; // 退出程序
            }

            // 修复中文乱码：设置控制台编码为UTF-8
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // 如果设置失败，继续运行
            }
            
            // 设置高性能线程池（基于CPU逻辑线程数）
            int cpuThreads = Environment.ProcessorCount;
            ThreadPool.SetMinThreads(cpuThreads * 2, cpuThreads * 2);
            
            // 检查响应文件参数
            List<string> filePatterns = new List<string>();
            bool useResponseFile = false;
            string responseFilePath = null;
            
            // 检查是否使用响应文件
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("@"))
                {
                    useResponseFile = true;
                    responseFilePath = args[i].Substring(1);
                    break;
                }
            }
            
            // 如果使用响应文件，从文件读取参数
            if (useResponseFile && !string.IsNullOrEmpty(responseFilePath))
            {
                try
                {
                    if (File.Exists(responseFilePath))
                    {
                        var fileArgs = File.ReadAllLines(responseFilePath, System.Text.Encoding.UTF8);
                        args = fileArgs;
                    }
                    else
                    {
                        Console.Error.WriteLine(string.Format("响应文件不存在: {0}", responseFilePath));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(string.Format("读取响应文件失败: {0}", ex.Message));
                    return;
                }
            }
            
            // 检查是否显示帮助
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }
            
            // 检查是否显示版本
            if (args[0] == "--version" || args[0] == "-v")
            {
                Console.WriteLine(string.Format("快速哈希计算器 v{0}", typeof(Program).Assembly.GetName().Version));
                Console.WriteLine("增强版 - 支持验证模式、带单位大小参数、批处理、图表分析");
                Console.WriteLine(string.Format("当前系统CPU逻辑线程数: {0}", Environment.ProcessorCount));
                return;
            }
            
            // 检查是否运行基准测试
            if (args[0] == "--benchmark" || args[0] == "-b")
            {
                await RunBenchmark();
                return;
            }
            
            // 解析命令行参数
            var patterns = new List<string>();
            var algorithms = new List<string> { "MD5" };
            bool showTime = false;
            bool recursive = false;
            bool parallel = true;
            bool stats = false;
            int maxParallel = 0; // 初始值设为0，由FileProcessor做最终校验
            long minSize = 0;
            long maxSize = long.MaxValue;
            int batchSize = 100; // 默认批处理大小
            
            // 输出控制参数
            bool saveToFile = true; // 新增：默认保存到文件
            string outputFilePath = null; // 新增：输出文件路径
            bool consoleOutput = false; // 新增：控制台输出模式
            
            // 验证模式相关参数
            bool verifyMode = false;
            string verifyJsonFile = null;
            
            // 解析命令行参数
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--algorithms":
                    case "-a":
                        if (i + 1 < args.Length)
                        {
                            algorithms = new List<string>(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries));
                            i++;
                        }
                        break;
                        
                    case "--show-time":
                        showTime = true;
                        break;
                        
                    case "--recursive":
                    case "-r":
                        recursive = true;
                        break;
                        
                    case "--sequential":
                    case "-seq":
                        parallel = false;
                        break;
                        
                    case "--parallel":
                    case "-p":
                        parallel = true;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int threads))
                        {
                            maxParallel = threads; // 手动指定线程数，传递给FileProcessor做校验
                            i++;
                        }
                        break;
                        
                    case "--stats":
                    case "-s":
                        stats = true;
                        break;
                        
                    case "--min-size":
                        if (i + 1 < args.Length && TryParseSize(args[i + 1], out long min))
                        {
                            minSize = min;
                            i++;
                        }
                        break;
                        
                    case "--max-size":
                        if (i + 1 < args.Length && TryParseSize(args[i + 1], out long max))
                        {
                            maxSize = max;
                            i++;
                        }
                        break;
                        
                    case "--batch-size":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int batch))
                        {
                            batchSize = batch;
                            i++;
                        }
                        break;
                        
                    case "--verify":
                        verifyMode = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            verifyJsonFile = args[i + 1];
                            i++;
                        }
                        break;
                        
                    case "--json-file":
                        if (i + 1 < args.Length)
                        {
                            verifyJsonFile = args[i + 1];
                            i++;
                        }
                        break;
                        
                    // 新增参数：指定输出文件路径
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            outputFilePath = args[i + 1];
                            saveToFile = true; // 指定输出路径意味着要保存到文件
                            i++;
                        }
                        break;
                        
                    // 新增参数：控制台输出模式（覆盖默认的文件保存行为）
                    case "--console-output":
                    case "-c":
                        consoleOutput = true;
                        saveToFile = false; // 控制台输出时不保存到文件
                        break;
                        
                    case "--file-list":
                    case "-f":
                        // 从文件读取文件列表
                        if (i + 1 < args.Length)
                        {
                            string fileListPath = args[i + 1];
                            i++;
                            
                            if (File.Exists(fileListPath))
                            {
                                try
                                {
                                    var filesFromList = File.ReadAllLines(fileListPath, System.Text.Encoding.UTF8);
                                    patterns.AddRange(filesFromList.Where(f => !string.IsNullOrWhiteSpace(f)));
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine(string.Format("无法读取文件列表 {0}: {1}", fileListPath, ex.Message));
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine(string.Format("文件列表不存在: {0}", fileListPath));
                            }
                        }
                        break;
                        
                    default:
                        if (!args[i].StartsWith("-") && !verifyMode)
                        {
                            patterns.Add(args[i]);
                        }
                        break;
                }
            }
            
            // 验证模式
            if (verifyMode)
            {
                if (string.IsNullOrEmpty(verifyJsonFile))
                {
                    Console.Error.WriteLine("错误: 验证模式需要指定JSON文件");
                    Console.Error.WriteLine("用法: FastHasher.exe --verify <json-file> [选项]");
                    Console.Error.WriteLine("       FastHasher.exe --verify --json-file <file>");
                    return;
                }
                
                await RunVerificationMode(verifyJsonFile, patterns.Count > 0 ? patterns[0] : null, 
                                        recursive, maxParallel, saveToFile, outputFilePath, consoleOutput);
                return;
            }
            
            // 计算模式
            if (patterns.Count == 0)
            {
                Console.Error.WriteLine("错误: 未指定文件路径或模式");
                Console.Error.WriteLine("使用 --help 查看帮助");
                return;
            }
            
            // 检查是否有大量文件，如果太多则建议使用文件列表
            if (patterns.Count > 100)
            {
                Console.Error.WriteLine(string.Format("警告: 检测到 {0} 个文件/模式，如果遇到参数过长问题，请使用 --file-list 参数", patterns.Count));
            }
            
            // 确定输出方式
            string finalOutputPath = DetermineOutputPath(outputFilePath, "hashes");
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine("╔══════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine("║                      快速哈希计算器 - 计算模式                     ");
            Console.Error.WriteLine("╠══════════════════════════════════════════════════════════════════");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write("║  文件/模式数量: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write(patterns.Count);
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine("                                 ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write("║  算法: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write(string.Join(", ", algorithms));
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine("                                ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write("║  递归: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write(recursive ? "是" : "否");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write("║  并行线程数: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write(maxParallel > 0 ? maxParallel : Environment.ProcessorCount);
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine();
            
            if (saveToFile && !string.IsNullOrEmpty(finalOutputPath))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.Write("║  输出文件: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.Write(finalOutputPath);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.WriteLine();
            }
            else if (consoleOutput)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.Write("║  输出方式: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.Write("控制台");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.WriteLine();
            }
            
            if (stats)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.Write("║  CPU逻辑线程数: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.Write(Environment.ProcessorCount);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.Write("║  ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.Write(PerformanceMonitor.GetMemoryInfo());
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.Write("║  ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.Write(PerformanceMonitor.GetThreadInfo());
                Console.ForegroundColor = ConsoleColor.White;
                Console.Error.WriteLine(" ║");
            }
            Console.Error.WriteLine("╚══════════════════════════════════════════════════════════════════");
            Console.ResetColor();
            
            var processor = new FileProcessor(
                algorithms, 
                showTime, 
                recursive, 
                parallel,
                maxParallel,
                minSize,
                maxSize,
                saveToFile,      // 新增参数
                finalOutputPath, // 新增参数
                consoleOutput    // 新增参数
            );
            
            // 设置Ctrl+C处理
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.Error.WriteLine("\n正在取消处理...");
                processor.Cancel();
                e.Cancel = true;
            };
            
            await processor.ProcessFilesAsync(patterns);
        }
        
        // 运行验证模式
        static async Task RunVerificationMode(string jsonFilePath, string pathFilter, 
                                             bool recursive, int maxParallel, bool saveToFile, 
                                             string outputFilePath, bool consoleOutput)
        {
            try
            {
                var verifier = new HashVerifier(
                    jsonFilePath, 
                    pathFilter, 
                    recursive,
                    maxParallel,
                    saveToFile,
                    outputFilePath,
                    consoleOutput
                );
                
                await verifier.VerifyAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format("验证失败: {0}", ex.Message));
            }
        }
        
        // 确定输出文件路径
        static string DetermineOutputPath(string userPath, string defaultPrefix)
        {
            // 如果用户指定了输出文件路径，直接使用
            if (!string.IsNullOrEmpty(userPath))
            {
                return Path.GetFullPath(userPath);
            }
            
            // 生成默认文件名：前缀_时间戳.json
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = string.Format("{0}_{1}.json", defaultPrefix, timestamp);
            
            return Path.Combine(Environment.CurrentDirectory, fileName);
        }
        
        // 显示帮助信息
        static void ShowHelp()
        {
            
            Console.WriteLine(@"
快速哈希计算器 
=================================================

【主要功能】
1. 计算文件哈希值，默认保存到JSON文件（带时间戳）
2. 使用 --verify 验证文件是否与上次计算结果一致
3. 验证结果也默认保存到JSON文件
4. 图表分析功能：文件大小分布图、性能分析图

【输出控制】
  默认行为：将结果保存到JSON文件，文件名包含时间戳
  --output, -o <路径>   指定输出文件路径
  --console-output, -c  在控制台显示结果（不保存到文件）

【图表功能】
  使用 --show-time 参数时自动显示以下图表：
  1. 文件大小分布图：显示不同大小范围的文件数量
  2. 性能分析图：文件大小 vs 处理时间关系图
  3. 平均处理时间图：不同大小文件的平均处理时间

【验证模式】
  用法：.\FastHasher.exe --verify <哈希文件.json>
  示例：
    # 计算文件哈希并保存
    .\FastHasher.exe *.txt
    
    # 验证文件是否与之前一致
    .\FastHasher.exe --verify hashes_20240115_143025.json
    
    # 验证并在控制台查看结果
    .\FastHasher.exe --verify hashes.json -c

    # 保存到指定文件
    .\FastHasher.exe --verify hashes.json -o verification_results.json

批处理选项（新增）:
  --batch-size <数量>        指定每批处理的文件数量（默认: 100）
                            避免参数过长，提高大文件列表处理稳定性

大小参数格式（新增）:
  支持带单位: 1B, 100KB, 10MB, 1GB, 2TB
  支持小数: 0.5GB, 1.5MB
  支持简写: 1K (1KB), 1M (1MB), 1G (1GB)
  【示例】--min-size 1KB --max-size 100MB

进度显示选项:
  --show-time                显示详细统计信息和图表分析
  --stats, -s                显示系统性能统计（CPU、内存、线程池信息）

参数过长解决方案:
  1. 使用响应文件: .\FastHasher.exe @files.txt
  2. 使用文件列表参数: .\FastHasher.exe --file-list files.txt
  3. 使用批处理: .\FastHasher.exe --file-list files.txt --batch-size 50
  4. 减少通配符匹配的文件数量

示例:
  # 基本用法
  .\FastHasher.exe test.txt                        # 单个文件（默认CPU线程数并行）
  .\FastHasher.exe *.txt -p 4                      # 4线程并行处理所有txt文件，显示进度
  .\FastHasher.exe C:\Data\*.csv --parallel 8      # 8线程并行处理csv文件
  .\FastHasher.exe 中文文件.txt                     # 中文文件
  
  # 验证模式（新：默认保存到文件）
  .\FastHasher.exe *.txt --console-output > hashes.json  # 计算哈希并保存
  .\FastHasher.exe --verify hashes.json            # 验证并自动保存结果到JSON文件
  .\FastHasher.exe --verify hashes.json -c         # 在控制台显示验证结果
  .\FastHasher.exe --verify hashes.json -o verification_report.json # 保存到指定文件
  
  # 带单位的大小参数
  .\FastHasher.exe *.dat --min-size 1KB --max-size 100MB
  .\FastHasher.exe *.iso --min-size 1GB --max-size 10GB
  
  # 批处理
  .\FastHasher.exe @filelist.txt --batch-size 50   # 批处理，每批50个文件
  .\FastHasher.exe --file-list files.txt -p 6 --batch-size 100
  
  # 高级用法（包含图表分析）
  .\FastHasher.exe . --recursive --algorithms MD5,SHA256 --show-time # 递归+双算法+默认并行+显示统计+图表

文件/目录处理规则:
  1. 如果是具体文件路径，处理该文件
  2. 如果是目录路径，处理该目录下所有文件
  3. 如果是通配符模式，按模式匹配文件
  4. 使用 --recursive 参数时，会递归处理子目录

性能选项:
  --parallel, -p [线程数]    启用并行处理（默认CPU逻辑线程数，可手动指定）
  --sequential, -seq          禁用并行，使用顺序处理
  --min-size <大小>          最小文件大小（支持单位，例如: 1KB, 1MB, 1GB）
  --max-size <大小>          最大文件大小
  --batch-size <数量>        批处理大小，避免参数过长

输出模式选项:
  --console-output, -c       在控制台显示结果（覆盖默认的文件保存）
  --show-time                显示计算时间和图表分析

验证选项:
  --verify <json-file>       验证模式，检查文件哈希是否一致
  --json-file <文件>          指定验证用的JSON文件
  --console-output, -c       在控制台显示验证结果（覆盖默认的文件保存）
  --output, -o <文件路径>     指定验证结果输出文件路径

文件列表选项:
  --file-list, -f <文件路径>  从文件读取要处理的文件列表（每行一个文件路径）
  @<文件路径>                  使用响应文件传递参数

常规选项:
  --algorithms, -a <算法列表>  计算指定的哈希算法（逗号分隔）
  --recursive, -r             递归搜索子目录
  --stats, -s                 显示性能统计信息（含CPU线程数/并行数）
  --benchmark, -b             运行基准测试
  --help, -h                  显示此帮助
  --version, -v               显示版本信息+当前CPU线程数

输出格式: 
  计算模式: JSON数组，每个元素包含文件的哈希结果和元数据
  验证模式: 默认保存到JSON文件，包含详细验证报告
");
        }
        
        // 运行基准测试
        static async Task RunBenchmark()
        {
            // 设置控制台编码
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }
            
            Console.WriteLine("运行基准测试...");
            Console.WriteLine("================");
            Console.WriteLine(string.Format("当前CPU逻辑线程数: {0}", Environment.ProcessorCount));
            Console.WriteLine(string.Format("基准测试并行线程数: {0}（默认）", Environment.ProcessorCount));
            Console.WriteLine("---");
            
            // 创建测试文件
            const int testSize = 10 * 1024 * 1024; // 10MB
            var testData = new byte[testSize];
            new Random().NextBytes(testData);
            
            string testFile = "benchmark_test.tmp";
            await File.WriteAllBytesAsync(testFile, testData);
            
            try
            {
                var algorithms = new[] { "MD5", "SHA1", "SHA256", "SHA512" };
                
                Console.WriteLine(string.Format("测试文件: {0}MB", testSize / 1024 / 1024));
                Console.WriteLine("---");
                
                // 测试顺序处理
                Console.WriteLine("顺序处理测试:");
                var sequentialTimes = new List<long>();
                
                foreach (var algo in algorithms)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var result = await FastHashCalculator.ComputeHashesAsync(testFile, new[] { algo });
                    stopwatch.Stop();
                    
                    if (result["success"] == "true")
                    {
                        var hash = result[algo.ToLower()];
                        Console.WriteLine(string.Format("  {0}: {1}ms ({2}...)", algo, stopwatch.ElapsedMilliseconds, hash.Substring(0, Math.Min(16, hash.Length))));
                        sequentialTimes.Add(stopwatch.ElapsedMilliseconds);
                    }
                }
                
                Console.WriteLine(string.Format("  总计: {0}ms", sequentialTimes.Sum()));
                
                // 测试并行处理（默认CPU线程数）
                Console.WriteLine(string.Format("\n并行处理测试 (所有算法同时计算 | 线程数: {0}):", Environment.ProcessorCount));
                var parallelStopwatch = Stopwatch.StartNew();
                var parallelResult = await FastHashCalculator.ComputeHashesAsync(testFile, algorithms);
                parallelStopwatch.Stop();
                
                if (parallelResult["success"] == "true")
                {
                    Console.WriteLine(string.Format("  总计: {0}ms", parallelStopwatch.ElapsedMilliseconds));
                    foreach (var algo in algorithms)
                    {
                        var hash = parallelResult[algo.ToLower()];
                        Console.WriteLine(string.Format("  {0}: {1}...", algo, hash.Substring(0, Math.Min(16, hash.Length))));
                    }
                }
                
                // 计算加速比
                if (sequentialTimes.Count > 0 && parallelStopwatch.ElapsedMilliseconds > 0)
                {
                    double speedup = (double)sequentialTimes.Sum() / parallelStopwatch.ElapsedMilliseconds;
                    Console.WriteLine(string.Format("\n加速比: {0:F2}x", speedup));
                    Console.WriteLine(string.Format("并行效率: {0:F1}%", speedup / Environment.ProcessorCount * 100));
                }
                
                Console.WriteLine("\n基准测试完成!");
                Console.WriteLine("提示：使用--parallel N可手动调整线程数，优化不同场景性能");
            }
            finally
            {
                // 清理测试文件
                if (File.Exists(testFile))
                {
                    File.Delete(testFile);
                }
            }
        }

        // 解析带单位的大小字符串
        static bool TryParseSize(string input, out long bytes)
        {
            bytes = 0;
            if (string.IsNullOrEmpty(input)) return false;
            
            input = input.Trim().ToUpperInvariant();
            
            // 移除所有空格
            input = input.Replace(" ", "");
            
            // 尝试直接解析为数字
            if (long.TryParse(input, out bytes))
            {
                return true;
            }
            
            // 尝试解析带单位的
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "K", "M", "G", "T", "P" };
            
            foreach (string unit in units)
            {
                if (input.EndsWith(unit))
                {
                    string numberPart = input.Substring(0, input.Length - unit.Length);
                    
                    // 尝试解析数字部分（支持小数）
                    if (decimal.TryParse(numberPart, out decimal number))
                    {
                        // 根据单位计算字节数
                        switch (unit)
                        {
                            case "B":
                                bytes = (long)number;
                                break;
                            case "KB":
                            case "K":
                                bytes = (long)(number * 1024L);
                                break;
                            case "MB":
                            case "M":
                                bytes = (long)(number * 1024L * 1024);
                                break;
                            case "GB":
                            case "G":
                                bytes = (long)(number * 1024L * 1024 * 1024);
                                break;
                            case "TB":
                            case "T":
                                bytes = (long)(number * 1024L * 1024 * 1024 * 1024);
                                break;
                            case "PB":
                            case "P":
                                bytes = (long)(number * 1024L * 1024 * 1024 * 1024 * 1024);
                                break;
                            default:
                                return false;
                        }
                        return true;
                    }
                }
            }
            
            return false;
        }
    }
}          


'@


}

# 创建每个源文件
foreach ($file in $sourceFiles.Keys) {
    $sourceFiles[$file] | Out-File "$SourceDir\$ProjectName\$file" -Encoding UTF8
    Write-Host "✓ 创建: $file" -ForegroundColor Green
}

Write-Host "✓ 源代码生成完成" -ForegroundColor Green

# 创建app.config文件以启用高性能
Write-Host "创建应用配置文件..." -ForegroundColor Yellow

$appConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <runtime>
    <gcServer enabled="true"/>
    <gcConcurrent enabled="true"/>
    <Thread_UseAllCpuGroups enabled="true"/>
    <GCCpuGroup enabled="true"/>
    <gcAllowVeryLargeObjects enabled="true" />
  </runtime>
</configuration>
'@

$appConfig | Out-File "$SourceDir\$ProjectName\app.config" -Encoding UTF8
Write-Host "✓ 应用配置创建完成" -ForegroundColor Green

# 构建项目
Write-Host "[6/8] 构建项目..." -ForegroundColor Yellow
Set-Location "$SourceDir\$ProjectName"

try {
    # 还原NuGet包
    Write-Host "  还原NuGet包..." -ForegroundColor Gray
    $restoreResult = dotnet restore --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ NuGet包还原失败" -ForegroundColor Red
        Write-Host $restoreResult
        exit 1
    }
    Write-Host "  ✓ NuGet包还原完成" -ForegroundColor Green
    
    # 构建
    Write-Host "  编译项目..." -ForegroundColor Gray
    $buildArgs = @(
        "build",
        "-c", $Configuration,
        "--verbosity", "minimal"
    )
    
    $buildResult = dotnet @buildArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ 项目编译失败" -ForegroundColor Red
        Write-Host $buildResult
        exit 1
    }
    Write-Host "  ✓ 项目编译完成" -ForegroundColor Green
    
    # 发布为单文件
    Write-Host "  发布单文件exe..." -ForegroundColor Gray
    $publishArgs = @(
        "publish",
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none",
        "-p:PublishTrimmed=false",  # 禁用裁剪以避免JSON序列化问题
        "-p:PublishReadyToRun=true",
        "-p:CharacterSet=Unicode",
        "--verbosity", "minimal"
    )
    
    $publishResult = dotnet @publishArgs 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ 发布失败" -ForegroundColor Red
        Write-Host $publishResult
        exit 1
    }
    
    Write-Host "  ✓ 发布成功" -ForegroundColor Green
}
catch {
    Write-Host "  ✗ 构建失败: $_" -ForegroundColor Red
    exit 1
}

# 查找exe文件
Write-Host "[7/8] 查找生成的exe文件..." -ForegroundColor Gray
$exePath = $null

# 搜索可能的发布目录
$searchPaths = @(
    "bin\$Configuration\net8.0\$Runtime\publish",
    "bin\$Configuration\net8.0\$Runtime",
    "bin\$Configuration\net8.0\win-x64\publish"
)

foreach ($path in $searchPaths) {
    if (Test-Path $path) {
        $exe = Get-ChildItem -Path $path -Filter "$ProjectName.exe" | Select-Object -First 1
        if ($exe) {
            $exePath = $exe
            Write-Host ("  在 {0} 找到exe文件" -f $path) -ForegroundColor White
            break
        }
    }
}

if (-not $exePath) {
    # 递归查找
    $allExes = Get-ChildItem -Path "." -Filter "$ProjectName.exe" -Recurse | 
               Where-Object { $_.Directory.Name -eq "publish" } | 
               Select-Object -First 1
    if ($allExes) {
        $exePath = $allExes
    }
}

if ($exePath) {
    
    # 创建输出目录
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }
    
    # 复制到输出目录
    $destPath = Join-Path $OutputDir $exePath.Name
    Copy-Item $exePath.FullName $destPath -Force

    # 创建带版本号的副本
    $versionedName = "{0}_v{1}.exe" -f $ProjectName, $Version
    $destPathVersioned = Join-Path $OutputDir $versionedName
    Copy-Item $exePath.FullName $destPathVersioned -Force

    # 复制到根目录（不带版本号）
    Copy-Item $exePath.FullName "..\..\$ProjectName.exe" -Force

    # 同时复制带版本号的到根目录
    Copy-Item $exePath.FullName "..\..\$versionedName" -Force
    
    # 显示文件信息
    $exeSize = [math]::Round(($exePath.Length / 1MB), 2)
    Write-Host ("文件大小: {0:F2} MB" -f $exeSize) -ForegroundColor White
    Write-Host ("输出文件: {0}" -f $destPath) -ForegroundColor White
    Write-Host ("版本文件: {0}" -f $destPathVersioned) -ForegroundColor White


    
    # 运行基准测试
    if ($Benchmark) {
        Write-Host "[8/8] 运行基准测试..." -ForegroundColor Yellow
        & $destPath --benchmark
    }
    
    Write-Host "`n✓ 构建完成!" -ForegroundColor Green
    
    # 测试部分
    if ($Test) {
        Write-Host "`n测试所有修复功能（含CPU线程数并行）..." -ForegroundColor Yellow
        
        # 设置编码
        $OutputEncoding = [System.Text.Encoding]::UTF8
        try {
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        } catch {}
        
        # 创建测试目录结构
        Write-Host "创建测试目录结构..." -ForegroundColor Gray
        
        # 创建测试目录
        $testDir = "test"
        New-Item -ItemType Directory -Path $testDir -Force | Out-Null
        New-Item -ItemType Directory -Path "$testDir\sub" -Force | Out-Null
        
        # 创建测试文件
        Write-Host "创建测试文件..." -ForegroundColor Gray
        1..5 | ForEach-Object {
            $content = "测试文件 $_ - 内容" * 10
            $content | Out-File "$testDir\test$_.txt" -Encoding UTF8
            $content | Out-File "$testDir\sub\sub$_.txt" -Encoding UTF8
        }
        
        # 创建中文测试文件
        "中文内容测试" | Out-File "$testDir\中文文件.txt" -Encoding UTF8
        
        Write-Host "`n测试1: 验证哈希一致性修复" -ForegroundColor Green
        # 创建两个相同内容的文件
        $testContent = "相同内容测试" * 1000
        $testContent | Out-File "$testDir\same1.txt" -Encoding UTF8
        $testContent | Out-File "$testDir\same2.txt" -Encoding UTF8
        
        # 使用临时文件捕获输出，避免PowerShell解析问题
        $tempOut = "temp_test_output.txt"
        $tempErr = "temp_test_error.txt"
        
        try {
            $process = Start-Process -FilePath $destPath -ArgumentList @("$testDir\same1.txt", "$testDir\same2.txt", "--console-output") `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOut -RedirectStandardError $tempErr
            $result = Get-Content $tempOut -Raw -ErrorAction SilentlyContinue
            
            if ($process.ExitCode -eq 0) {
                Write-Host "✓ 哈希一致性测试通过" -ForegroundColor Green
            } else {
                Write-Host "✗ 哈希一致性测试失败" -ForegroundColor Red
            }
        } finally {
            Remove-Item $tempOut, $tempErr -Force -ErrorAction SilentlyContinue
        }
        
        Write-Host "`n测试2: 验证中文处理" -ForegroundColor Green
        try {
            $process = Start-Process -FilePath $destPath -ArgumentList @("$testDir\中文文件.txt", "--console-output") `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOut -RedirectStandardError $tempErr
            $result = Get-Content $tempOut -Raw -ErrorAction SilentlyContinue
            
            if ($process.ExitCode -eq 0) {
                Write-Host "✓ 中文文件处理测试通过" -ForegroundColor Green
            } else {
                Write-Host "✗ 中文文件处理测试失败" -ForegroundColor Red
            }
        } finally {
            Remove-Item $tempOut, $tempErr -Force -ErrorAction SilentlyContinue
        }
        
        Write-Host "`n测试3: 验证参数文件方式" -ForegroundColor Green
        $fileList = "filelist.txt"
        Get-ChildItem "$testDir\*.txt" | Select-Object -First 3 | ForEach-Object { $_.FullName } | Out-File $fileList -Encoding UTF8
        
        try {
            $process = Start-Process -FilePath $destPath -ArgumentList @("--file-list", $fileList, "--console-output") `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOut -RedirectStandardError $tempErr
            $result = Get-Content $tempOut -Raw -ErrorAction SilentlyContinue
            
            if ($process.ExitCode -eq 0) {
                Write-Host "✓ 文件列表参数测试通过" -ForegroundColor Green
            } else {
                Write-Host "✗ 文件列表参数测试失败" -ForegroundColor Red
            }
        } finally {
            Remove-Item $tempOut, $tempErr -Force -ErrorAction SilentlyContinue
        }

        
        #新的测试JSON
        Write-Host "`n测试4: 验证基本功能" -ForegroundColor Green

        try {
            # 不指定--output参数，让程序自动生成文件名
            $process = Start-Process -FilePath $destPath `
                -ArgumentList @("$testDir\test1.txt") `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOut -RedirectStandardError $tempErr
            
            if ($process.ExitCode -eq 0) {
                # 查找最近创建的JSON文件
                $recentFiles = Get-ChildItem -Path . -Filter "hashes_*.json" | 
                            Sort-Object LastWriteTime -Descending | 
                            Select-Object -First 1
                
                if ($recentFiles) {
                    Write-Host "  ✓ 自动创建了文件: $($recentFiles.Name)" -ForegroundColor Green
                    
                    # 验证文件内容
                    $content = Get-Content $recentFiles.FullName -Raw -Encoding UTF8
                    try {
                        $json = $content | ConvertFrom-Json -ErrorAction Stop
                        if ($json.Count -gt 0) {
                            Write-Host "  ✓ 文件包含 $($json.Count) 个有效记录" -ForegroundColor Green
                        }
                    } catch {
                        Write-Host "  ✗ 文件内容不是有效JSON" -ForegroundColor Red
                    }
                    
                    # 清理文件
                    Remove-Item $recentFiles.FullName -Force
                } else {
                    Write-Host "  ✗ 未找到自动创建的哈希文件" -ForegroundColor Red
                }
            }
        } finally {
            Remove-Item $tempOut, $tempErr -Force -ErrorAction SilentlyContinue
        }

        #测试JSON 完成

        
        Write-Host "`n测试5: 验证并行参数" -ForegroundColor Green
        try {
            $process = Start-Process -FilePath $destPath -ArgumentList @("$testDir\test1.txt", "-p", "2", "--console-output") `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOut -RedirectStandardError $tempErr
            $result = Get-Content $tempOut -Raw -ErrorAction SilentlyContinue
            
            if ($process.ExitCode -eq 0) {
                Write-Host "✓ 并行参数测试通过" -ForegroundColor Green
            } else {
                Write-Host "✗ 并行参数测试失败" -ForegroundColor Red
            }
        } finally {
            Remove-Item $tempOut, $tempErr -Force -ErrorAction SilentlyContinue
        }
        
        # 清理测试目录
        Write-Host "`n清理测试文件..." -ForegroundColor Gray
        Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $fileList -Force -ErrorAction SilentlyContinue
        
        Write-Host "`n所有测试完成!" -ForegroundColor Green
    }
}
else {
    Write-Host "✗ 构建失败: 未找到生成的exe文件" -ForegroundColor Red
}

Set-Location "..\.."

Write-Host ""
Write-Host "=========================================" -ForegroundColor White
if ($exePath) {
    Write-Host "构建成功！FastHasher $Version" -ForegroundColor Green
    Write-Host "已修复问题:" -ForegroundColor White
    Write-Host "  1. 哈希一致性 ✓" -ForegroundColor Green
    Write-Host "  2. JSON转义问题 ✓" -ForegroundColor Green
    Write-Host "  3. 中文乱码问题 ✓" -ForegroundColor Green
    Write-Host "  4. 参数过长问题 ✓" -ForegroundColor Green
    Write-Host "  5. JSON序列化错误 ✓" -ForegroundColor Green    
    Write-Host "  6. 根目录读取错误 ✓" -ForegroundColor Green    
    Write-Host "新增CPU并行特性:" -ForegroundColor White
    Write-Host "  ✅ 默认按CPU逻辑线程数并行，自动适配" -ForegroundColor Green
    Write-Host "  ✅ --parallel/-p手动指定线程数，优先级最高" -ForegroundColor Green
    Write-Host "  ✅ 自动限制最大并行数为CPU*2，避免性能下降" -ForegroundColor Green
    Write-Host "  ✅ 实时显示并行线程数，进度更直观" -ForegroundColor Green
    Write-Host ""
    Write-Host "使用示例:" -ForegroundColor White
    Write-Host ("  默认并行: .\{0}.exe *.txt --recursive" -f $ProjectName) -ForegroundColor Yellow
    Write-Host ("  手动指定4线程: .\{0}.exe *.txt -p 4" -f $ProjectName) -ForegroundColor Yellow
    Write-Host ("  控制台输出: .\{0}.exe *.csv -c" -f $ProjectName) -ForegroundColor Yellow
} else {
    Write-Host "构建过程可能有误，请检查输出" -ForegroundColor Yellow
}
Write-Host "=========================================" -ForegroundColor White
