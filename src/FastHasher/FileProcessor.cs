﻿﻿﻿﻿// FileProcessor.cs - 专业文件处理器（优化版）
// 负责文件处理、哈希计算、JSON序列化等核心功能
// 支持并行处理、递归搜索、多种过滤条件
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastHasher
{
    /// <summary>
    /// JSON输出结构类
    /// 用于序列化哈希计算结果为JSON格式
    /// </summary>
    public class HashOutput
    {
        /// <summary>
        /// 元数据信息，包含计算配置、性能指标等
        /// </summary>
        [JsonPropertyName("metadata")]
        public HashMetadata Metadata { get; set; } = new();

        /// <summary>
        /// 文件哈希结果列表
        /// 每个元素包含文件路径和对应的哈希值
        /// </summary>
        [JsonPropertyName("files")]
        public List<Dictionary<string, string>> Files { get; set; } = new();
    }

    /// <summary>
    /// 哈希计算元数据类
    /// 记录计算时的配置信息、性能指标、系统信息等
    /// </summary>
    public class HashMetadata
    {
        /// <summary>
        /// 基本信息类
        [JsonPropertyName("generated")]
        public string generated { get; set; } = "";

        [JsonPropertyName("version")]
        public string version { get; set; } = "";

        // 算法配置
        [JsonPropertyName("algorithms")]
        public List<string> algorithms { get; set; } = new();

        // 并行处理配置
        [JsonPropertyName("parallel")]
        public bool parallel { get; set; }

        [JsonPropertyName("maxDegreeOfParallelism")]
        public int maxDegreeOfParallelism { get; set; }

        // 文件搜索配置
        [JsonPropertyName("recursive")]
        public bool recursive { get; set; }

        [JsonPropertyName("filePatterns")]
        public List<string> filePatterns { get; set; } = new();

        // 文件大小过滤
        [JsonPropertyName("minFileSize")]
        public long minFileSize { get; set; }

        [JsonPropertyName("maxFileSize")]
        public long maxFileSize { get; set; }

        // 统计信息
        [JsonPropertyName("totalFiles")]
        public int totalFiles { get; set; }

        [JsonPropertyName("totalBytes")]
        public long totalBytes { get; set; }

        [JsonPropertyName("successFiles")]
        public int successFiles { get; set; }

        [JsonPropertyName("failedFiles")]
        public int failedFiles { get; set; }

        // 性能信息
        [JsonPropertyName("processingTimeSeconds")]
        public double processingTimeSeconds { get; set; }

        [JsonPropertyName("averageSpeedMBps")]
        public double averageSpeedMBps { get; set; }

        [JsonPropertyName("averageFileProcessingTimeMs")]
        public double averageFileProcessingTimeMs { get; set; }

        // 系统信息
        [JsonPropertyName("machine")]
        public string machine { get; set; } = "";

        [JsonPropertyName("processorCount")]
        public int processorCount { get; set; }

        [JsonPropertyName("osVersion")]
        public string osVersion { get; set; } = "";

        [JsonPropertyName("workingDirectory")]
        public string workingDirectory { get; set; } = "";

        [JsonPropertyName("userName")]
        public string userName { get; set; } = "";
    }

    // 文件处理器 - 增强显示和日志记录
    public class FileProcessor
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly CliOptions options;
        private readonly List<string> algorithms;
        private readonly bool showTime;
        private readonly bool recursive;
        private readonly bool parallel;
        public readonly int maxDegreeOfParallelism;
        private readonly long minFileSize = 0;
        private readonly long maxFileSize = long.MaxValue;
        private readonly string? outputFilePath; // "-" 表示输出到标准输出
        private readonly Stopwatch totalStopwatch = new Stopwatch();
        private long totalBytes = 0;
        private readonly string? logFilePath;
        private List<string> filePatterns = new();
        private int successFiles = 0;
        private int failedFiles = 0;
        private bool canceled = false;

        // 图表数据收集
        private readonly List<long> fileSizes = new List<long>();
        private readonly List<(double sizeMB, double timeMs)> performanceData = new List<(double, double)>();

        /// <summary>
        /// 进程退出码：0 全部成功，1 存在失败文件或没有可处理的文件，130 用户取消。
        /// 零匹配（路径写错、空目录）也会返回 1，避免脚本静默通过。
        /// </summary>
        public int ExitCode => canceled ? 130
            : failedFiles > 0 ? 1
            : successFiles == 0 ? 1
            : 0;

        public FileProcessor(CliOptions options)
        {
            this.options = options;
            this.algorithms = options.Algorithms;
            this.showTime = options.Stats;
            this.recursive = options.Recursive;
            this.parallel = options.IsParallel;
            this.minFileSize = options.MinSize;
            this.maxFileSize = options.MaxSize;
            this.maxDegreeOfParallelism = Math.Max(1, options.Threads);

            // 解析输出路径（"-" 表示标准输出）
            this.outputFilePath = options.OutputToStdout ? "-" : ResolveOutputPath();

            // 初始化日志系统（--no-log 时为空，表示不落盘）
            this.logFilePath = ResolveLogPath();
            ProgressDisplay.InitializeLog(this.logFilePath ?? string.Empty);

            // 记录启动信息
            ProgressDisplay.WriteToLog(string.Format(L.T("FastHasher 开始运行，时间: {0}"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format(L.T("算法: {0}"), string.Join(", ", algorithms)));
            ProgressDisplay.WriteToLog(string.Format(L.T("递归搜索: {0}"), recursive ? L.T("是") : L.T("否")));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行处理: {0}"), parallel ? L.T("是") : L.T("否")));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行线程数: {0}"), this.maxDegreeOfParallelism));
            ProgressDisplay.WriteToLog(string.Format(L.T("输出格式: {0}"), options.Format));
            ProgressDisplay.WriteToLog(string.Format(L.T("文件大小过滤: {0} - {1}"),
                minFileSize > 0 ? ProgressDisplay.FormatSize(minFileSize) : L.T("无限制"),
                maxFileSize < long.MaxValue ? ProgressDisplay.FormatSize(maxFileSize) : L.T("无限制")));
        }

        /// <summary>
        /// 计算输出文件路径：未指定时按格式生成带时间戳的默认文件名
        /// </summary>
        private string ResolveOutputPath()
        {
            return OutputPaths.ResolveOutput(options.OutputPath, options.Format, Environment.CurrentDirectory, "hashes");
        }

        /// <summary>
        /// 计算日志文件路径：默认与输出文件同目录，避免污染工作目录
        /// </summary>
        private string? ResolveLogPath()
        {
            return OutputPaths.ResolveLog(options, outputFilePath, "FastHasher");
        }

        public async Task ProcessFilesAsync(List<string> patterns)
        {
            // 存储文件模式用于 Metadata
            this.filePatterns = patterns;

            // 重置统计计数器
            this.successFiles = 0;
            this.failedFiles = 0;

            ProgressDisplay.WriteToLog(string.Format(L.T("开始处理文件模式，模式数量: {0}"), patterns.Count));
            if (patterns.Count > 0)
            {
                ProgressDisplay.WriteToLog(L.T("文件模式列表:"));
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
                ProgressDisplay.WriteWarning(L.T("警告: 没有找到匹配的文件"));
                ProgressDisplay.WriteToLog(L.T("没有找到匹配的文件"));
                return;
            }

            // 一次遍历同时收集图表数据与总大小，避免对同一批文件重复 stat
            fileSizes.Clear();
            long totalSize = 0;
            foreach (var file in files)
            {
                try
                {
                    long size = new FileInfo(file).Length;
                    fileSizes.Add(size);
                    totalSize += size;
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteToLog(string.Format(L.T("无法获取文件大小: {0} - {1}"), file, ex.Message));
                }
            }

            totalBytes = totalSize;

            // 显示文件统计信息
            ProgressDisplay.WriteStatistics(L.T("文件统计"), 
                ("文件数量", files.Count.ToString("N0"), ConsoleColor.Yellow),
                ("总大小", ProgressDisplay.FormatSize(totalBytes), ConsoleColor.White),
                ("并行线程", maxDegreeOfParallelism.ToString(), maxDegreeOfParallelism == Environment.ProcessorCount ? ConsoleColor.Green : ConsoleColor.Yellow),
                ("模式", parallel ? "并行" : "顺序", parallel ? ConsoleColor.Green : ConsoleColor.Gray)
            );

            // 显示输出信息
            if (outputFilePath == "-")
            {
                ProgressDisplay.WriteInfo(L.T("输出方式: 标准输出 (stdout)"));
            }
            else if (!string.IsNullOrEmpty(outputFilePath))
            {
                ProgressDisplay.WriteInfo(string.Format(L.T("输出文件: {0}"), outputFilePath));
            }

            ProgressDisplay.WriteSeparator();

            // 记录到日志
            ProgressDisplay.WriteToLog(string.Format(L.T("找到 {0} 个文件需要处理 | 总大小: {1}"), files.Count, ProgressDisplay.FormatSize(totalBytes)));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行线程数: {0} {1}"), maxDegreeOfParallelism, maxDegreeOfParallelism == Environment.ProcessorCount ? L.T("(CPU默认)") : L.T("(手动指定)")));

            totalStopwatch.Start();
            var results = new List<Dictionary<string, string>>();

            // 记录开始时间
            ProgressDisplay.WriteToLog(L.T("开始处理文件..."));
            var startTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format(L.T("处理开始时间: {0}"), startTime.ToString("yyyy-MM-dd HH:mm:ss")));

            if (parallel)
            {
                ProgressDisplay.WriteInfo(L.T("使用并行处理模式"));
                results = await ProcessFilesParallelAsync(files);
            }
            else
            {
                ProgressDisplay.WriteInfo(L.T("使用顺序处理模式"));
                results = await ProcessFilesSequentialAsync(files);
            }

            totalStopwatch.Stop();

            // 统计成功和失败的文件数
            foreach (var result in results)
            {
                if (result.ContainsKey("success") && result["success"] == "true")
                {
                    successFiles++;
                }
                else
                {
                    failedFiles++;
                }
            }

            // 记录结束时间
            var endTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format(L.T("处理结束时间: {0}"), endTime.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format(L.T("总处理时间: {0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds));

            // 输出结果
            OutputResults(results);

            // 显示统计信息和图表
            if (showTime)
            {
                ProgressDisplay.WriteSection("处理统计信息");

                ProgressDisplay.WriteStatistics(L.T("性能统计"), 
                    ("总时间", string.Format(L.T("{0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds), ConsoleColor.Yellow),
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
                ProgressDisplay.WriteToLog(string.Format(L.T("总处理时间: {0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds));
                ProgressDisplay.WriteToLog(string.Format(L.T("文件数量: {0} | 总大小: {1}"), files.Count, ProgressDisplay.FormatSize(totalBytes)));
                ProgressDisplay.WriteToLog(string.Format(L.T("平均速度: {0}"), ProgressDisplay.FormatSpeed(totalBytes, totalStopwatch.Elapsed)));
                ProgressDisplay.WriteToLog(string.Format(L.T("平均每个文件: {0:F2}毫秒"), totalStopwatch.Elapsed.TotalMilliseconds / Math.Max(1, files.Count)));
                ProgressDisplay.WriteToLog(PerformanceMonitor.GetMemoryInfo());
                ProgressDisplay.WriteToLog(PerformanceMonitor.GetThreadInfo());
            }

            // 显示日志文件位置
            if (!string.IsNullOrEmpty(logFilePath))
            {
                ProgressDisplay.WriteSuccess(string.Format(L.T("处理完成！日志已保存到: {0}"), logFilePath));
            }
            else
            {
                ProgressDisplay.WriteSuccess(L.T("处理完成！"));
            }
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
                    .Select(d => (d.sizeMB, d.timeMs, string.Format(L.T("大小:{0:F1}MB"), d.sizeMB)))
                    .ToList();

                ProgressDisplay.WriteScatterPlot(L.T("文件大小 vs 处理时间关系图"), scatterData);

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

                    ProgressDisplay.WriteHorizontalBarChart(L.T("不同大小文件的平均处理时间"), groupedData);
                }
            }
        }

        // 优化版并行文件处理 - 移除预计算，使用 ConcurrentDictionary
        private async Task<List<Dictionary<string, string>>> ProcessFilesParallelAsync(List<string> files)
        {
            var results = new ConcurrentDictionary<int, Dictionary<string, string>>();
            var processedCount = 0;
            var processedBytes = 0L;
            var startTime = DateTime.UtcNow;
            var lastUpdateTime = DateTime.UtcNow;
            var updateLock = new object();

            // 开始显示进度
            ProgressDisplay.UpdateProgressLine(string.Format(L.T("{0} 正在初始化并行处理，文件总数: {1}..."), ProgressDisplay.GetSpinner(), files.Count));

            // 记录并行处理开始
            ProgressDisplay.WriteToLog(L.T("开始并行处理文件..."));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行线程数: {0}"), maxDegreeOfParallelism));

            try
            {
                await Parallel.ForEachAsync(files.Select((f, i) => (file: f, index: i)),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism,
                        CancellationToken = cts.Token
                    },
                    async (item, cancellationToken) =>
                    {
                        // 记录开始处理单个文件
                        string fileName = Path.GetFileName(item.file);
                        long fileSize = 0;

                        try
                        {
                            fileSize = new FileInfo(item.file).Length;
                            ProgressDisplay.WriteToLog(string.Format(L.T("开始处理文件: {0} (大小: {1})"), fileName, ProgressDisplay.FormatSize(fileSize)));
                        }
                        catch
                        {
                            ProgressDisplay.WriteToLog(string.Format(L.T("开始处理文件: {0} (无法获取大小)"), fileName));
                        }

                        var result = await FastHashCalculator.ComputeHashesAsync(
                            item.file,
                            algorithms.ToArray(),
                            cancellationToken: cancellationToken);

                        results[item.index] = result;
                        Interlocked.Increment(ref processedCount);
                        Interlocked.Add(ref processedBytes, fileSize);

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

                        // 记录文件处理完成
                        if (result["success"] == "true")
                        {
                            ProgressDisplay.WriteToLog(string.Format(L.T("文件处理完成: {0} (用时: {1}ms)"), fileName, result["time_ms"]));
                        }
                        else
                        {
                            ProgressDisplay.WriteError(string.Format(L.T("文件处理失败: {0} - {1}"), fileName, (result.ContainsKey("error") ? result["error"] : L.T("未知错误"))));
                        }

                        // 降低进度更新频率以提升性能
                        var now = DateTime.UtcNow;
                        if (now - lastUpdateTime > TimeSpan.FromMilliseconds(200))
                        {
                            lock (updateLock)
                            {
                                if (now - lastUpdateTime > TimeSpan.FromMilliseconds(200))
                                {
                                    UpdateProgress(processedCount, files.Count,
                                                   processedBytes, totalBytes,
                                                   startTime, item.file, parallel: true);
                                    lastUpdateTime = now;
                                }
                            }
                        }
                    });

                // 最终完成时显示100%
                ProgressDisplay.WriteProgressComplete(string.Format(L.T("并行处理完成: {0} 个文件"), files.Count));

                // 记录并行处理完成
                ProgressDisplay.WriteToLog(string.Format(L.T("并行处理完成，共处理 {0} 个文件"), files.Count));
            }
            catch (OperationCanceledException)
            {
                ProgressDisplay.WriteToLog(L.T("处理被用户取消"));
                ProgressDisplay.WriteProgressComplete(L.T("处理被用户取消"));
            }
            catch (Exception ex)
            {
                ProgressDisplay.WriteToLog(string.Format(L.T("处理过程中出现异常: {0}"), ex.Message));
                ProgressDisplay.WriteToLog(string.Format(L.T("异常堆栈: {0}"), ex.StackTrace));
                ProgressDisplay.WriteProgressComplete(string.Format(L.T("处理出错: {0}"), ex.Message));
            }

            // 按原始顺序排序
            return results.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }

        /// <summary>
        /// 输出单行进度。并行模式额外显示线程数并写入进度日志，顺序模式只刷新进度行。
        /// </summary>
        private void UpdateProgress(int processed, int total, long bytesProcessed, long totalBytes,
                                    DateTime startTime, string currentFile, bool parallel)
        {
            if (total == 0) return;

            double percentage = (double)processed / total;
            var elapsed = DateTime.UtcNow - startTime;

            string fileName = ProgressDisplay.Truncate(Path.GetFileName(currentFile), 20);
            string progressBar = ProgressDisplay.GetProgressBar(percentage);
            string spinner = ProgressDisplay.GetSpinner();
            string eta = ProgressDisplay.GetETA(elapsed, processed, total);
            string speed = ProgressDisplay.FormatSpeed(bytesProcessed, elapsed);
            string sizeInfo = string.Format("{0}/{1}",
                ProgressDisplay.FormatSize(bytesProcessed), ProgressDisplay.FormatSize(totalBytes));
            string threads = parallel ? string.Format(L.T(" | {0}线程"), maxDegreeOfParallelism) : string.Empty;

            string progressText;
            try
            {
                int consoleWidth = Console.WindowWidth - 2;

                if (consoleWidth >= 120)
                {
                    progressText = string.Format(L.T("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6} | {7}{8} | 当前: {9}"),
                        spinner, progressBar, percentage, processed, total, sizeInfo, speed, eta, threads, fileName);
                }
                else if (consoleWidth >= 100)
                {
                    progressText = string.Format(L.T("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6}{7} | 当前: {8}"),
                        spinner, progressBar, percentage, processed, total, sizeInfo, speed, threads, fileName);
                }
                else if (consoleWidth >= 80)
                {
                    progressText = string.Format(L.T("{0} {1} {2:P1} | {3}/{4} 文件 | {5} | 当前: {6}"),
                        spinner, progressBar, percentage, processed, total, speed, fileName);
                }
                else if (consoleWidth >= 60)
                {
                    progressText = string.Format(L.T("{0} {1} {2:P1} | {3}/{4} 文件 | {5}"),
                        spinner, progressBar, percentage, processed, total, fileName);
                }
                else
                {
                    progressText = string.Format("{0} {1:P1} | {2}/{3} | {4}",
                        spinner, percentage, processed, total, fileName);
                }

                ProgressDisplay.UpdateProgressLine(progressText);

                // 并行模式下按 10% 或每 100 个文件补一条日志，避免日志刷屏
                if (parallel && (processed % 100 == 0 || percentage % 0.1 < 0.01))
                {
                    ProgressDisplay.WriteToLog(string.Format(L.T("处理进度: {0}/{1} ({2:P1}) | 大小: {3} | 速度: {4}"),
                        processed, total, percentage, sizeInfo, speed), false);
                }
            }
            catch
            {
                progressText = string.Format(L.T("{0} 处理中: {1}/{2} 文件 ({3:P1}) | 当前: {4}"),
                    spinner, processed, total, percentage, fileName);
                Console.Error.Write(string.Format("\r{0}", progressText));
            }
        }

        private async Task<List<Dictionary<string, string>>> ProcessFilesSequentialAsync(List<string> files)
        {
            var results = new List<Dictionary<string, string>>();
            var processedBytes = 0L;
            var startTime = DateTime.UtcNow;

            ProgressDisplay.WriteToLog(L.T("开始顺序处理文件..."));
            ProgressDisplay.WriteToLog(string.Format(L.T("文件总数: {0}"), files.Count));

            for (int i = 0; i < files.Count; i++)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    ProgressDisplay.WriteToLog(L.T("处理被用户取消"));
                    ProgressDisplay.WriteProgressComplete(L.T("处理被用户取消"));
                    break;
                }

                var file = files[i];
                var fileSize = new FileInfo(file).Length;
                var fileName = Path.GetFileName(file);

                // 记录开始处理文件
                ProgressDisplay.WriteToLog(string.Format(L.T("开始处理文件 [{0}/{1}]: {2} (大小: {3})"), 
                    i + 1, files.Count, fileName, ProgressDisplay.FormatSize(fileSize)));

                // 显示当前文件处理进度
                UpdateProgress(i, files.Count, processedBytes, totalBytes,
                               startTime, fileName, parallel: false);

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
                    ProgressDisplay.WriteToLog(string.Format(L.T("文件处理完成 [{0}/{1}]: {2} (用时: {3}ms)"), 
                        i + 1, files.Count, fileName, result["time_ms"]));
                }
                else
                {
                    ProgressDisplay.WriteError(string.Format(L.T("文件处理失败 [{0}/{1}]: {2} - {3}"), 
                        i + 1, files.Count, fileName, result.ContainsKey("error") ? result["error"] : L.T("未知错误")));
                }

                // 文件处理完成后立即更新进度
                UpdateProgress(i + 1, files.Count, processedBytes, totalBytes,
                               startTime, fileName, parallel: false);

                // 每处理10个文件记录一次详细进度
                if ((i + 1) % 10 == 0)
                {
                    ProgressDisplay.WriteToLog(string.Format(L.T("进度报告: 已处理 {0}/{1} 文件 ({2:P1})"), 
                        i + 1, files.Count, (double)(i + 1) / files.Count), false);
                }
            }

            if (files.Count > 0)
            {
                ProgressDisplay.WriteProgressComplete(string.Format(L.T("顺序处理完成: {0} 个文件"), files.Count));
            }

            ProgressDisplay.WriteToLog(string.Format(L.T("顺序处理完成，共处理 {0} 个文件"), results.Count));

            return results;
        }

        private void OutputResults(List<Dictionary<string, string>> results)
        {
            if (results == null || results.Count == 0)
            {
                ProgressDisplay.WriteWarning(L.T("没有结果需要输出"));
                return;
            }

            string reportText = options.Format switch
            {
                OutputFormat.Text => BuildTextOutput(results),
                OutputFormat.Ndjson => BuildNdjsonOutput(results),
                _ => BuildJsonOutput(results)
            };

            ProgressDisplay.WriteToLog(string.Format(L.T("输出内容长度: {0} 字符"), reportText.Length));

            // 记录输出结果信息
            ProgressDisplay.WriteToLog(string.Format(L.T("开始输出结果，结果数量: {0}"), results.Count));

            if (outputFilePath == "-")
            {
                // 标准输出：只写结果本身，不附加任何提示，保证可被管道解析
                Console.Out.WriteLine(reportText);
                ProgressDisplay.WriteToLog(L.T("结果已输出到标准输出"));
            }
            else if (!string.IsNullOrEmpty(outputFilePath))
            {
                try
                {
                    // 确保目录存在
                    string? directory = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(outputFilePath, reportText, new UTF8Encoding(false));

                    string writtenSize = ProgressDisplay.FormatSize(new FileInfo(outputFilePath).Length);

                    ProgressDisplay.WriteSeparator();
                    ProgressDisplay.WriteSuccess(string.Format(L.T("哈希计算结果已保存到: {0}"), outputFilePath));
                    ProgressDisplay.WriteInfo(string.Format(L.T("文件数量: {0}"), results.Count));
                    ProgressDisplay.WriteInfo(string.Format(L.T("结果文件大小: {0}"), writtenSize));

                    ProgressDisplay.WriteToLog(string.Format(L.T("哈希计算结果已保存到: {0}"), outputFilePath));
                    ProgressDisplay.WriteToLog(string.Format(L.T("结果文件大小: {0}"), writtenSize));
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteError(string.Format(L.T("保存结果到文件失败: {0}"), ex.Message));
                    ProgressDisplay.WriteToLog(L.T("结果未写出，请检查输出路径权限"));
                }
            }
            else
            {
                ProgressDisplay.WriteWarning(L.T("处理完成，但没有可用的输出路径"));
            }
        }

        // 优化版 JSON 输出 - 使用 System.Text.Json
        private string BuildJsonOutput(List<Dictionary<string, string>> results)
        {
            // 计算性能指标
            double processingTimeSeconds = totalStopwatch.Elapsed.TotalSeconds;
            double averageSpeedMBps = processingTimeSeconds > 0 ? (totalBytes / (1024.0 * 1024.0)) / processingTimeSeconds : 0;
            double averageFileProcessingTimeMs = results.Count > 0 ? processingTimeSeconds * 1000.0 / results.Count : 0;

            // 获取操作系统信息：Unix 下 Environment.OSVersion 只给内核版本，OSDescription 更具可读性
            string osVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

            // 创建包含metadata的输出结构
            var output = new HashOutput
            {
                Metadata = new HashMetadata
                {
                    // 基本信息类
                    generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    version = VersionInfo.GetVersion(),

                    // 算法配置
                    algorithms = this.algorithms,

                    // 并行处理配置
                    parallel = this.parallel,
                    maxDegreeOfParallelism = this.maxDegreeOfParallelism,

                    // 文件搜索配置
                    recursive = this.recursive,
                    filePatterns = this.filePatterns,

                    // 文件大小过滤
                    minFileSize = this.minFileSize,
                    maxFileSize = this.maxFileSize,

                    // 统计信息
                    totalFiles = results.Count,
                    totalBytes = totalBytes,
                    successFiles = this.successFiles,
                    failedFiles = this.failedFiles,

                    // 性能信息
                    processingTimeSeconds = processingTimeSeconds,
                    averageSpeedMBps = averageSpeedMBps,
                    averageFileProcessingTimeMs = averageFileProcessingTimeMs,

                    // 系统信息
                    machine = Environment.MachineName,
                    processorCount = Environment.ProcessorCount,
                    osVersion = osVersion,
                    workingDirectory = Environment.CurrentDirectory,
                    userName = Environment.UserName
                },
                Files = results
            };

            // 添加调试日志
            ProgressDisplay.WriteToLog(string.Format(L.T("HashOutput 对象创建完成")));
            ProgressDisplay.WriteToLog(string.Format(L.T("Metadata 是否为 null: {0}"), output.Metadata == null));
            if (output.Metadata != null)
            {
                ProgressDisplay.WriteToLog(string.Format("Metadata.generated = {0}", output.Metadata.generated));
                ProgressDisplay.WriteToLog(string.Format("Metadata.algorithms.Count = {0}", output.Metadata.algorithms?.Count ?? 0));
            }
            ProgressDisplay.WriteToLog(string.Format(L.T("Files 是否为 null: {0}"), output.Files == null));

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };

            string jsonOutput = JsonSerializer.Serialize(output, options);
            ProgressDisplay.WriteToLog(string.Format(L.T("JSON 序列化完成，长度: {0}"), jsonOutput.Length));

            return jsonOutput;
        }

        /// <summary>
        /// 生成人类可读的文本输出
        /// </summary>
        private string BuildTextOutput(List<Dictionary<string, string>> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(L.T("FastHasher {0} | 生成时间: {1}"), VersionInfo.GetVersion(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine(string.Format(L.T("算法: {0} | 文件数: {1} | 总大小: {2} | 用时: {3:F2} 秒"),
                string.Join(", ", algorithms), results.Count,
                ProgressDisplay.FormatSize(totalBytes), totalStopwatch.Elapsed.TotalSeconds));
            sb.AppendLine();

            foreach (var result in results)
            {
                string path = result.TryGetValue("file", out var file) ? file : L.T("(未知文件)");
                bool success = result.TryGetValue("success", out var ok) && ok == "true";

                sb.AppendLine(path);

                if (!success)
                {
                    sb.AppendLine(string.Format(L.T("  失败: {0}"), result.TryGetValue("error", out var error) ? error : L.T("未知错误")));
                    sb.AppendLine();
                    continue;
                }

                long size = long.TryParse(result.GetValueOrDefault("size"), out long parsedSize) ? parsedSize : 0;
                sb.AppendLine(string.Format(L.T("  大小: {0} | 用时: {1} ms"),
                    ProgressDisplay.FormatSize(size), result.GetValueOrDefault("time_ms", "0")));

                foreach (string algorithm in algorithms)
                {
                    if (result.TryGetValue(algorithm.ToLowerInvariant(), out var hash) && !string.IsNullOrEmpty(hash))
                    {
                        sb.AppendLine(string.Format("  {0,-8}: {1}", algorithm, hash));
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成每行一个 JSON 对象的 ndjson 输出
        /// </summary>
        private string BuildNdjsonOutput(List<Dictionary<string, string>> results)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var sb = new StringBuilder();
            foreach (var result in results)
            {
                sb.AppendLine(JsonSerializer.Serialize(result, jsonOptions));
            }

            return sb.ToString().TrimEnd();
        }

        // 修复后的文件模式展开方法，使用 EnumerationOptions 忽略无权限访问的子目录
        private static List<string> ExpandFilePatterns(List<string> patterns, bool recursive, long minFileSize, long maxFileSize)
        {
            // Windows 文件系统大小写不敏感，Linux 严格区分大小写；按平台选择比较器，
            // 否则 Linux 上 A.txt 与 a.txt 会被当成同一文件而漏算
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            var files = new HashSet<string>(pathComparer);

            // 创建支持忽略访问错误的枚举选项
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,   // 是否递归
                IgnoreInaccessible = true,            // 忽略无法访问的目录
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System // 可按需调整
            };

            ProgressDisplay.WriteInfo(L.T("正在展开文件模式..."));
            int patternCount = 0;

            foreach (var pattern in patterns)
            {
                patternCount++;
                ProgressDisplay.WriteToLog(string.Format(L.T("处理模式 [{0}/{1}]: {2}"), patternCount, patterns.Count, pattern));

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
                        ProgressDisplay.WriteToLog(string.Format(L.T("  添加文件: {0}"), fullPath));
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
                            ProgressDisplay.WriteToLog(string.Format(L.T("  目录 {0} 中找到 {1} 个文件"), directory, count));
                        }
                        catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
                        {
                            ProgressDisplay.WriteWarning(string.Format(L.T("无法枚举目录 '{0}': {1}"), pattern, ex.Message));
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

                        // 添加调试日志
                        ProgressDisplay.WriteToLog(string.Format(L.T("  原始模式: {0}"), pattern));
                        ProgressDisplay.WriteToLog(string.Format(L.T("  解析后的目录: {0}"), directoryPart));
                        ProgressDisplay.WriteToLog(string.Format(L.T("  解析后的文件模式: {0}"), filePattern));

                        // 如果目录部分为空或不存在，使用当前目录
                        if (string.IsNullOrEmpty(directoryPart))
                        {
                            directoryPart = ".";
                        }

                        // 将相对路径转换为绝对路径，以便正确检查目录是否存在
                        string absoluteDirectory = Path.GetFullPath(directoryPart);
                        ProgressDisplay.WriteToLog(string.Format(L.T("  绝对路径目录: {0}"), absoluteDirectory));
                        ProgressDisplay.WriteToLog(string.Format(L.T("  目录是否存在: {0}"), Directory.Exists(absoluteDirectory)));

                        if (!Directory.Exists(absoluteDirectory))
                        {
                            directoryPart = ".";
                        }
                        else
                        {
                            // 使用绝对路径
                            directoryPart = absoluteDirectory;
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
                        ProgressDisplay.WriteToLog(string.Format(L.T("  通配符 {0} 匹配到 {1} 个文件"), pattern, count));
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
                    {
                        ProgressDisplay.WriteWarning(string.Format(L.T("无法处理模式 '{0}': {1}"), pattern, ex.Message));
                    }
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteWarning(string.Format(L.T("无法处理模式 '{0}': {1}"), pattern, ex.Message));
                }
            }

            ProgressDisplay.WriteInfo(string.Format(L.T("文件模式展开完成，找到 {0} 个文件"), files.Count));

            // 文件大小过滤（在文件收集之后进行）
            int skippedSpecial = 0;
            var filteredFiles = files.Where(f => {
                try
                {
                    var fileInfo = new FileInfo(f);
                    if (!fileInfo.Exists) return false;

                    // FIFO / 字符设备 / 块设备 / 套接字：读取可能永久阻塞（如无写端 FIFO），跳过
                    if (!FileTypeHelper.IsRegularFile(f))
                    {
                        skippedSpecial++;
                        ProgressDisplay.WriteToLog(string.Format(L.T("跳过非常规文件: {0}"), f));
                        return false;
                    }

                    var size = fileInfo.Length;
                    bool sizeOk = size >= minFileSize && size <= maxFileSize;

                    if (!sizeOk)
                    {
                        ProgressDisplay.WriteToLog(string.Format(L.T("文件 {0} 大小 {1} 不符合过滤条件 ({2} - {3})"),
                            Path.GetFileName(f), ProgressDisplay.FormatSize(size),
                            minFileSize > 0 ? ProgressDisplay.FormatSize(minFileSize) : L.T("无限制"),
                            maxFileSize < long.MaxValue ? ProgressDisplay.FormatSize(maxFileSize) : L.T("无限制")));
                    }

                    return sizeOk;
                }
                catch
                {
                    ProgressDisplay.WriteWarning(string.Format(L.T("无法获取文件信息: {0}"), f));
                    return false;
                }
            }).ToList();

            if (skippedSpecial > 0)
            {
                ProgressDisplay.WriteWarning(string.Format(L.T("已跳过 {0} 个非常规文件（FIFO/设备/套接字）"), skippedSpecial));
            }

            ProgressDisplay.WriteInfo(string.Format(L.T("文件大小过滤完成，剩余 {0} 个文件"), filteredFiles.Count));

            return filteredFiles.OrderBy(f => f).ToList();
        }

        public void Cancel()
        {
            canceled = true;
            cts.Cancel();
            ProgressDisplay.WriteToLog(L.T("处理被取消"));
        }
    }
}

