
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


