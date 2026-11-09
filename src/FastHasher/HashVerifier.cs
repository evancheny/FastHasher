﻿﻿﻿// HashVerifier.cs - 专业文件哈希验证器（优化版）
// 提供哈希验证功能，对比计算结果与原始哈希值
// 支持批量验证、详细报告、错误定位等
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;

namespace FastHasher
{
    /// <summary>
    /// 专业文件哈希验证器
    /// 提供哈希验证功能，对比计算结果与原始哈希值
    /// 支持批量验证、详细报告、错误定位等
    /// </summary>
    public class HashVerifier
    {
        /// <summary>
        /// Metadata 数据类
        /// 用于存储和解析哈希文件的元数据信息
        /// </summary>
        public class HashMetadata
        {
            /// <summary>
            /// 基本信息类
            /// </summary>
            public string generated { get; set; } = "";
            public string version { get; set; } = "";

            /// <summary>
            /// 算法配置
            /// </summary>
            public List<string> algorithms { get; set; } = new();

            /// <summary>
            /// 并行处理配置
            /// </summary>
            public bool parallel { get; set; }
            public int maxDegreeOfParallelism { get; set; }

            // 文件搜索配置
            public bool recursive { get; set; }
            public List<string> filePatterns { get; set; } = new();

            // 文件大小过滤
            public long minFileSize { get; set; }
            public long maxFileSize { get; set; }

            // 统计信息
            public int totalFiles { get; set; }
            public long totalBytes { get; set; }
            public int successFiles { get; set; }
            public int failedFiles { get; set; }

            // 性能信息
            public double processingTimeSeconds { get; set; }
            public double averageSpeedMBps { get; set; }
            public double averageFileProcessingTimeMs { get; set; }

            // 系统信息
            public string machine { get; set; } = "";
            public int processorCount { get; set; }
            public string osVersion { get; set; } = "";
            public string workingDirectory { get; set; } = "";
            public string userName { get; set; } = "";
        }

        private readonly CliOptions options;
        private readonly string jsonFilePath;
        private readonly List<string> pathFilters;
        private readonly int maxDegreeOfParallelism;
        private readonly string? outputFilePath; // "-" 表示输出到标准输出
        private readonly Stopwatch totalStopwatch = new Stopwatch();
        private readonly string? logFilePath;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private int failedCount = 0;
        private int totalTargets = 0;
        private bool canceled = false;

        /// <summary>
        /// 进程退出码：0 全部通过，1 存在失败/缺失/错误或没有可验证记录，130 用户取消
        /// </summary>
        public int ExitCode => canceled ? 130
            : failedCount > 0 ? 1
            : totalTargets == 0 ? 1
            : 0;

        public HashVerifier(CliOptions options)
        {
            this.options = options;
            this.jsonFilePath = options.VerifyJson ?? throw new ArgumentException(L.T("验证模式缺少要验证的 JSON 文件"));
            this.pathFilters = options.Inputs;
            this.maxDegreeOfParallelism = Math.Max(1, options.Threads);
            this.outputFilePath = options.OutputToStdout ? "-" : ResolveOutputPath();

            // 线程数已在解析阶段规范化（正整数、上限 CPU×2）

            // 设置日志文件路径
            this.logFilePath = ResolveLogPath();

            // 初始化日志系统（--no-log 时为空，表示不落盘）
            ProgressDisplay.InitializeLog(this.logFilePath ?? string.Empty);

            // 记录启动信息
            ProgressDisplay.WriteToLog(string.Format(L.T("FastHasher 验证模式开始运行，时间: {0}"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format(L.T("验证文件: {0}"), jsonFilePath));
            ProgressDisplay.WriteToLog(string.Format(L.T("路径过滤: {0}"), pathFilters.Count == 0 ? "无" : string.Join("; ", pathFilters)));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行线程数: {0}"), this.maxDegreeOfParallelism));
        }

        /// <summary>
        /// 取消验证（由 Ctrl+C 触发）
        /// </summary>
        public void Cancel()
        {
            canceled = true;
            cts.Cancel();
            ProgressDisplay.WriteToLog(L.T("验证被取消"));
        }

        public async Task VerifyAsync()
        {
            if (!File.Exists(jsonFilePath)) {
                throw new FileNotFoundException(string.Format(L.T("验证文件不存在: {0}"), jsonFilePath));
            }

            totalStopwatch.Start();

            // 读取JSON文件
            ProgressDisplay.WriteInfo(L.T("正在读取验证文件..."));
            ProgressDisplay.WriteToLog(string.Format(L.T("开始读取验证文件: {0}"), jsonFilePath));

            var (metadata, storedFiles) = await ReadHashJsonFileWithMetadata(jsonFilePath);

            if (storedFiles.Count == 0) {
                ProgressDisplay.WriteWarning(L.T("验证文件中没有找到哈希记录"));
                ProgressDisplay.WriteToLog(L.T("验证文件中没有找到哈希记录"));
                return;
            }

            ProgressDisplay.WriteSuccess(string.Format(L.T("验证文件读取完成，共 {0} 条记录"), storedFiles.Count));
            ProgressDisplay.WriteToLog(string.Format(L.T("验证文件读取完成，共 {0} 条记录"), storedFiles.Count));

            // 显示metadata信息
            ProgressDisplay.WriteSection("计算参数信息");
            ProgressDisplay.WriteStatistics(L.T("原始计算配置"),
                ("计算时间", metadata.generated, ConsoleColor.White),
                ("算法", string.Join(", ", metadata.algorithms), ConsoleColor.Cyan),
                ("并行模式", metadata.parallel ? "是" : "否", ConsoleColor.Green),
                ("并行线程数", metadata.maxDegreeOfParallelism.ToString(), ConsoleColor.Yellow),
                ("递归搜索", metadata.recursive ? "是" : "否", ConsoleColor.Gray),
                ("文件数量", metadata.totalFiles.ToString(), ConsoleColor.Yellow),
                ("总大小", ProgressDisplay.FormatSize(metadata.totalBytes), ConsoleColor.White)
            );
            ProgressDisplay.WriteSeparator();

            // 应用路径过滤
            var filesToVerify = FilterFiles(storedFiles, pathFilters);
            if (filesToVerify.Count == 0) {
                ProgressDisplay.WriteWarning(L.T("没有找到匹配的验证文件"));
                ProgressDisplay.WriteToLog(L.T("没有找到匹配的验证文件"));
                return;
            }

            ProgressDisplay.WriteInfo(string.Format(L.T("过滤后需要验证 {0} 个文件"), filesToVerify.Count));
            ProgressDisplay.WriteToLog(string.Format(L.T("过滤后需要验证 {0} 个文件"), filesToVerify.Count));

            // 输出路径已在构造阶段确定
            string? finalOutputPath = outputFilePath;

            ProgressDisplay.WriteSection("验证模式配置");
            ProgressDisplay.WriteStatistics(L.T("验证配置"),
                ("验证文件", Path.GetFileName(jsonFilePath), ConsoleColor.White),
                ("文件总数", filesToVerify.Count.ToString(), ConsoleColor.Yellow),
                ("并行线程", maxDegreeOfParallelism.ToString(), ConsoleColor.Green),
                ("路径过滤", pathFilters.Count == 0 ? "无" : string.Join("; ", pathFilters), ConsoleColor.Gray)
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

            // 记录验证开始时间
            ProgressDisplay.WriteToLog(L.T("开始重新计算哈希..."));
            var startTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format(L.T("验证开始时间: {0}"), startTime.ToString("yyyy-MM-dd HH:mm:ss")));

            // 重新计算所有文件的哈希
            totalTargets = storedFiles.Count;
            var recalculationResults = await RecalculateHashesByMetadata(metadata, filesToVerify);

            // 记录验证结束时间
            var endTime = DateTime.Now;
            ProgressDisplay.WriteToLog(string.Format(L.T("验证结束时间: {0}"), endTime.ToString("yyyy-MM-dd HH:mm:ss")));
            ProgressDisplay.WriteToLog(string.Format(L.T("总验证时间: {0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds));

            // 对比两个JSON文件
            var verificationResults = CompareJsonResults(storedFiles, recalculationResults);

            // 输出验证结果
            OutputVerificationResults(verificationResults, storedFiles.Count);

            totalStopwatch.Stop();

            // 显示日志文件位置
            if (!string.IsNullOrEmpty(logFilePath))
            {
                ProgressDisplay.WriteSuccess(string.Format(L.T("验证完成！日志已保存到: {0}"), logFilePath));
            }
            else
            {
                ProgressDisplay.WriteSuccess(L.T("验证完成！"));
            }
        }

        /// <summary>
        /// 计算验证报告的输出路径：未指定时与待验证文件同目录
        /// </summary>
        private string ResolveOutputPath()
        {
            string fullJsonPath = Path.GetFullPath(jsonFilePath);
            string directory = Path.GetDirectoryName(fullJsonPath) ?? Environment.CurrentDirectory;
            string baseName = Path.GetFileNameWithoutExtension(fullJsonPath) + "_verify";

            return OutputPaths.ResolveOutput(options.OutputPath, options.Format, directory, baseName);
        }

        /// <summary>
        /// 计算日志文件路径：默认与输出文件同目录，避免污染工作目录
        /// </summary>
        private string? ResolveLogPath()
        {
            return OutputPaths.ResolveLog(options, outputFilePath, "FastHasher_Verify");
        }

        private List<Dictionary<string, string>> FilterFiles(List<Dictionary<string, string>> files, List<string> filters)
        {
            if (filters.Count == 0)
            {
                return files;
            }

            return files.Where(file =>
            {
                string path = file.TryGetValue("file", out var direct) ? direct
                    : file.TryGetValue("full_path", out var fullPath) ? fullPath : string.Empty;

                return filters.Any(filter => path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }).ToList();
        }

        /// <summary>
        /// 解析 JSON 中记录的文件路径。
        /// 记录的是相对路径时，若与当前工作目录不匹配（报告与文件一起搬到别处），
        /// 依次尝试：相对当前目录 → 相对 JSON 所在目录 → 按文件名在 JSON 目录下查找。
        /// </summary>
        private string? ResolveExistingPath(string? recordedPath, string fileName)
        {
            if (string.IsNullOrEmpty(recordedPath)) return recordedPath;

            try
            {
                if (File.Exists(recordedPath)) return recordedPath;

                string absolute = Path.GetFullPath(recordedPath);
                if (File.Exists(absolute)) return absolute;

                string jsonDir = Path.GetDirectoryName(Path.GetFullPath(jsonFilePath)) ?? ".";
                string nearJson = Path.Combine(jsonDir, recordedPath);
                if (File.Exists(nearJson)) return nearJson;

                // 录制的是相对路径时，报告搬移后前缀不再匹配：
                // 从最长尾部片段开始，逐个尝试拼接在 JSON 目录下
                string normalized = recordedPath
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);

                if (!Path.IsPathRooted(normalized))
                {
                    string[] parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string candidate = Path.Combine(jsonDir, Path.Combine(parts[i..]));
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch
            {
                // 非法路径等异常保持原值，交由后续流程报 missing
            }

            return recordedPath;
        }

        private void OutputVerificationResults(List<VerificationResult> results, int totalStoredFiles)
        {
            // 统计
            int success = results.Count(r => r.Status == "success");
            int failed = results.Count(r => r.Status == "failed");
            int missing = results.Count(r => r.Status == "missing");
            int error = results.Count(r => r.Status == "error");
            failedCount = failed + missing + error;

            ProgressDisplay.WriteToLog(string.Format(L.T("开始构建验证报告，结果数量: {0}"), results.Count));

            // 按输出格式构建报告
            string report = options.Format switch
            {
                OutputFormat.Text => BuildTextReport(results, totalStoredFiles, success, failed, missing, error),
                OutputFormat.Ndjson => BuildNdjsonReport(results),
                _ => BuildVerificationReport(results, totalStoredFiles, success, failed, missing, error)
            };

            ProgressDisplay.WriteToLog(string.Format(L.T("验证报告构建完成，报告长度: {0}"), report.Length));

            if (outputFilePath == "-")
            {
                // 标准输出：只写报告本身，保证可被管道解析
                Console.Out.WriteLine(report);
                ProgressDisplay.WriteToLog(L.T("验证结果已输出到标准输出"));
            }
            else if (!string.IsNullOrEmpty(outputFilePath))
            {
                // 保存到文件
                try
                {
                    // 确保目录存在
                    string? directory = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(outputFilePath, report, new UTF8Encoding(false));

                    ProgressDisplay.WriteSection("验证结果");

                    ProgressDisplay.WriteStatistics(L.T("验证统计"), 
                        ("存储文件总数", totalStoredFiles.ToString(), ConsoleColor.Gray),
                        ("验证文件数", results.Count.ToString(), ConsoleColor.Yellow),
                        ("验证成功", string.Format("{0} ✓", success), ConsoleColor.Green),
                        ("验证失败", string.Format("{0} ✗", failed), ConsoleColor.Red),
                        ("文件缺失", string.Format("{0} ⚠", missing), ConsoleColor.Yellow),
                        ("计算错误", string.Format("{0} ⚠", error), ConsoleColor.Red),
                        ("成功率", string.Format("{0}%", results.Count > 0 ? (success * 100.0 / results.Count).ToString("F2") : "0.00"), 
                            results.Count > 0 && (success * 100.0 / results.Count) >= 90 ? ConsoleColor.Green : 
                            results.Count > 0 && (success * 100.0 / results.Count) >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red),
                        ("总用时", string.Format(L.T("{0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds), ConsoleColor.White)
                    );

                    ProgressDisplay.WriteSuccess(string.Format(L.T("验证结果已保存到: {0}"), outputFilePath));

                    // 记录到日志
                    ProgressDisplay.WriteToLog(string.Format(L.T("验证结果已保存到: {0}"), outputFilePath));
                    ProgressDisplay.WriteToLog(string.Format(L.T("验证统计: {0} 成功, {1} 失败, {2} 缺失, {3} 错误"), success, failed, missing, error));
                    ProgressDisplay.WriteToLog(string.Format(L.T("总用时: {0:F2}秒"), totalStopwatch.Elapsed.TotalSeconds));
                }
                catch (Exception ex)
                {
                    ProgressDisplay.WriteError(string.Format(L.T("保存验证结果到文件失败: {0}"), ex.Message));

                    // 记录错误到日志
                    ProgressDisplay.WriteError(string.Format(L.T("保存验证结果到文件失败: {0}"), ex.Message));
                }
            }
        }

        // 优化版验证报告生成 - 使用 System.Text.Json
        private string BuildVerificationReport(List<VerificationResult> results, int total, int success,
                                             int failed, int missing, int error)
        {
            try
            {
                var report = new VerificationReport
                {
                    Metadata = new VerificationMetadata
                    {
                        Generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        VerificationFile = jsonFilePath,
                        PathFilter = pathFilters.Count == 0 ? string.Empty : string.Join("; ", pathFilters),
                        TotalTimeMs = totalStopwatch.ElapsedMilliseconds,
                        ParallelThreads = maxDegreeOfParallelism
                    },
                    Summary = new VerificationSummary
                    {
                        TotalStoredFiles = total,
                        VerifiedFiles = results.Count,
                        Success = success,
                        Failed = failed,
                        Missing = missing,
                        Error = error,
                        SuccessRate = results.Count > 0 ? (success * 100.0 / results.Count).ToString("F2") : "0.00"
                    },
                    IssuesSummary = new VerificationIssuesSummary
                    {
                        TotalIssues = failed + missing + error,
                        HashMismatch = failed,
                        FilesMissing = missing,
                        CalculationErrors = error
                    },
                    ProblemFiles = new VerificationProblemFiles
                    {
                        HashMismatch = results.Where(r => r.Status == "failed").Select(r => new HashMismatchFile
                        {
                            File = r.FilePath,
                            Filename = r.FileName,
                            Status = r.Status,
                            SizeMatch = r.SizeMatch,
                            MismatchedAlgorithms = r.AlgorithmResults?.Where(a => !a.Value.Match)
                                .ToDictionary(a => a.Key, a => new AlgorithmMismatchDetail
                                {
                                    Expected = a.Value.Expected ?? string.Empty,
                                    Actual = a.Value.Actual ?? string.Empty
                                }) ?? new Dictionary<string, AlgorithmMismatchDetail>()
                        }).ToList(),
                        FilesMissing = results.Where(r => r.Status == "missing").Select(r => new MissingFile
                        {
                            File = r.FilePath,
                            Filename = r.FileName,
                            Status = r.Status,
                            Error = L.T("文件不存在或无法访问")
                        }).ToList(),
                        CalculationErrors = results.Where(r => r.Status == "error").Select(r => new ErrorFile
                        {
                            File = r.FilePath,
                            Filename = r.FileName,
                            Status = r.Status,
                            Error = r.Error
                        }).ToList()
                    },
                    Results = results.Select(r => new VerificationResultItem
                    {
                        File = r.FilePath,
                        Filename = r.FileName,
                        Status = r.Status,
                        Error = r.Error,
                        SizeMatch = r.SizeMatch,
                        Algorithms = r.AlgorithmResults
                    }).ToList()
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                return JsonSerializer.Serialize(report, jsonOptions);
            }
            catch (Exception ex)
            {
                ProgressDisplay.WriteError(string.Format(L.T("生成验证报告失败: {0}"), ex.Message));
                ProgressDisplay.WriteError(string.Format(L.T("堆栈跟踪: {0}"), ex.StackTrace));
                return "{}";
            }
        }

        /// <summary>
        /// 生成人类可读的验证报告
        /// </summary>
        private string BuildTextReport(List<VerificationResult> results, int totalStoredFiles,
                                      int success, int failed, int missing, int error)
        {
            var sb = new StringBuilder();
            double successRate = results.Count > 0 ? success * 100.0 / results.Count : 0;

            sb.AppendLine(string.Format(L.T("FastHasher {0} 验证报告 | 生成时间: {1}"),
                VersionInfo.GetVersion(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine(string.Format(L.T("验证文件: {0}"), jsonFilePath));
            sb.AppendLine(string.Format(L.T("存储文件总数: {0} | 验证文件数: {1}"), totalStoredFiles, results.Count));
            sb.AppendLine(string.Format(L.T("成功: {0} | 失败: {1} | 缺失: {2} | 错误: {3} | 成功率: {4:F2}%"),
                success, failed, missing, error, successRate));
            sb.AppendLine(string.Format(L.T("用时: {0:F2} 秒"), totalStopwatch.Elapsed.TotalSeconds));

            var issues = results.Where(r => r.Status != "success").ToList();
            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(L.T("问题文件:"));

                foreach (var issue in issues)
                {
                    string status = issue.Status switch
                    {
                        "failed" => L.T("哈希不匹配"),
                        "missing" => L.T("文件缺失"),
                        _ => L.T("计算错误")
                    };

                    sb.AppendLine(string.Format("  [{0}] {1}", status, issue.FilePath));

                    if (!string.IsNullOrEmpty(issue.Error))
                    {
                        sb.AppendLine(string.Format("        {0}", issue.Error));
                    }

                    if (issue.AlgorithmResults != null)
                    {
                        foreach (var algorithm in issue.AlgorithmResults.Where(a => !a.Value.Match))
                        {
                            sb.AppendLine(string.Format(L.T("        {0} 期望: {1}"), algorithm.Key, algorithm.Value.Expected));
                            sb.AppendLine(string.Format(L.T("        {0} 实际: {1}"), algorithm.Key, algorithm.Value.Actual));
                        }
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成每行一个 JSON 对象的 ndjson 验证报告
        /// </summary>
        private string BuildNdjsonReport(List<VerificationResult> results)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var sb = new StringBuilder();
            foreach (var result in results)
            {
                var item = new VerificationResultItem
                {
                    File = result.FilePath,
                    Filename = result.FileName,
                    Status = result.Status,
                    Error = result.Error,
                    SizeMatch = result.SizeMatch,
                    Algorithms = result.AlgorithmResults
                };

                sb.AppendLine(JsonSerializer.Serialize(item, jsonOptions));
            }

            return sb.ToString().TrimEnd();
        }

        // 新方法：读取JSON文件并提取metadata和文件列表
        private async Task<(HashMetadata metadata, List<Dictionary<string, string>> files)> ReadHashJsonFileWithMetadata(string filePath)
        {
            try
            {
                ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), "正在读取文件...");
                string jsonContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), string.Format(L.T("文件读取完成，大小: {0}"), ProgressDisplay.FormatSize(jsonContent.Length)));

                ProgressDisplay.WriteToLog(string.Format(L.T("JSON内容前500字符: {0}"), jsonContent.Substring(0, Math.Min(500, jsonContent.Length))));

                // 尝试解析新的格式（包含metadata）
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;

                    ProgressDisplay.WriteToLog(string.Format(L.T("JSON根元素属性: {0}"), string.Join(", ", root.EnumerateObject().Select(p => p.Name))));

                    // 检查是否包含metadata（新格式）
                    if (root.TryGetProperty("metadata", out var metadataElement) || 
                        root.TryGetProperty("Metadata", out metadataElement))
                    {
                        ProgressDisplay.WriteToLog(L.T("检测到新格式JSON（包含metadata）"));

                        var metadata = new HashMetadata
                        {
                            generated = metadataElement.TryGetProperty("generated", out var genElem) ? genElem.GetString() ?? L.T("未知") : L.T("未知"),
                            version = metadataElement.TryGetProperty("version", out var verElem) ? verElem.GetString() ?? VersionInfo.GetVersion() : VersionInfo.GetVersion(),
                            algorithms = metadataElement.TryGetProperty("algorithms", out var algElem) ? JsonSerializer.Deserialize<List<string>>(algElem.GetRawText()) ?? new List<string>() : new List<string>(),
                            parallel = metadataElement.TryGetProperty("parallel", out var parElem) && parElem.GetBoolean(),
                            maxDegreeOfParallelism = metadataElement.TryGetProperty("maxDegreeOfParallelism", out var maxDegElem) ? maxDegElem.GetInt32() : Environment.ProcessorCount,
                            recursive = metadataElement.TryGetProperty("recursive", out var recElem) && recElem.GetBoolean(),
                            minFileSize = metadataElement.TryGetProperty("minFileSize", out var minElem) ? minElem.GetInt64() : 0,
                            maxFileSize = metadataElement.TryGetProperty("maxFileSize", out var maxElem) ? maxElem.GetInt64() : long.MaxValue,
                            totalFiles = metadataElement.TryGetProperty("totalFiles", out var totFilesElem) ? totFilesElem.GetInt32() : 0,
                            totalBytes = metadataElement.TryGetProperty("totalBytes", out var totBytesElem) ? totBytesElem.GetInt64() : 0,
                            machine = metadataElement.TryGetProperty("machine", out var machElem) ? machElem.GetString() ?? L.T("未知") : L.T("未知"),
                            processorCount = metadataElement.TryGetProperty("processorCount", out var procElem) ? procElem.GetInt32() : Environment.ProcessorCount
                        };

                        // 尝试获取files数组
                        JsonElement filesElement;
                        if (root.TryGetProperty("files", out filesElement) || 
                            root.TryGetProperty("Files", out filesElement))
                        {
                            var files = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(
                                filesElement.GetRawText(),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), string.Format(L.T("新格式JSON解析完成，找到 {0} 条记录"), files?.Count ?? 0));
                            return (metadata, files ?? new List<Dictionary<string, string>>());
                        }
                        else
                        {
                            ProgressDisplay.WriteError(L.T("新格式JSON中未找到files数组"));
                            return (new HashMetadata(), new List<Dictionary<string, string>>());
                        }
                    }
                    else
                    {
                        ProgressDisplay.WriteToLog(L.T("检测到旧格式JSON（不包含metadata）"));

                        // 旧格式（没有metadata），直接作为文件列表
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var files = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(jsonContent, options) ?? new List<Dictionary<string, string>>();

                        // 创建默认metadata
                        var defaultMetadata = new HashMetadata
                        {
                            generated = L.T("未知"),
                            version = VersionInfo.GetVersion(),
                            algorithms = DetectAlgorithmsFromFiles(files),
                            parallel = false,
                            maxDegreeOfParallelism = 1,
                            recursive = false,
                            minFileSize = 0,
                            maxFileSize = long.MaxValue,
                            totalFiles = files.Count,
                            totalBytes = files.Sum(f => f.ContainsKey("size") ? long.Parse(f["size"]) : 0),
                            machine = L.T("未知"),
                            processorCount = Environment.ProcessorCount
                        };

                        ProgressDisplay.WriteFileInfo(Path.GetFileName(filePath), string.Format(L.T("旧格式JSON解析完成，找到 {0} 条记录"), files.Count));
                        return (defaultMetadata, files);
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressDisplay.WriteError(string.Format(L.T("读取JSON文件失败: {0}"), ex.Message));
                ProgressDisplay.WriteToLog(string.Format(L.T("异常详情: {0} - {1}"), ex.GetType().Name, ex.Message));
                ProgressDisplay.WriteToLog(string.Format(L.T("异常堆栈: {0}"), ex.StackTrace));
                return (new HashMetadata(), new List<Dictionary<string, string>>());
            }
        }

        // 新方法：根据metadata重新计算哈希
        private async Task<List<Dictionary<string, string>>> RecalculateHashesByMetadata(HashMetadata metadata, List<Dictionary<string, string>> files)
        {
            var results = new ConcurrentBag<Dictionary<string, string>>();
            int processed = 0;
            int total = files.Count;
            var startTime = DateTime.UtcNow;
            var lastUpdateTime = DateTime.UtcNow;
            var updateLock = new object();

            // 使用 SemaphoreSlim 限制文件读取的并发度
            var fileAccessSemaphore = new SemaphoreSlim(Environment.ProcessorCount);

            ProgressDisplay.WriteInfo(string.Format(L.T("开始重新计算 {0} 个文件的哈希..."), total));
            ProgressDisplay.WriteToLog(string.Format(L.T("使用算法: {0}"), string.Join(", ", metadata.algorithms)));
            ProgressDisplay.WriteToLog(string.Format(L.T("并行模式: {0}, 线程数: {1}"), metadata.parallel ? L.T("是") : L.T("否"), metadata.maxDegreeOfParallelism));

            try
            {
                ProgressDisplay.UpdateProgressLine(string.Format(L.T("{0} 正在初始化哈希计算，文件总数: {1}..."), ProgressDisplay.GetSpinner(), total));

                await Parallel.ForEachAsync(files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, metadata.maxDegreeOfParallelism),
                        CancellationToken = cts.Token
                    },
                    async (fileRecord, cancellationToken) =>
                    {
                        string? filePath = fileRecord.ContainsKey("file") ? fileRecord["file"] :
                                        fileRecord.ContainsKey("full_path") ? fileRecord["full_path"] : null;

                        string fileName = string.IsNullOrEmpty(filePath) ? "未知文件" : Path.GetFileName(filePath);

                        // 记录的是相对路径，报告被搬移时按 JSON 所在目录兜底解析
                        filePath = ResolveExistingPath(filePath, fileName);

                        await fileAccessSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            ProgressDisplay.WriteToLog(string.Format(L.T("[线程 {0}] 开始计算: {1}"), Thread.CurrentThread.ManagedThreadId, fileName));

                            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                            {
                                ProgressDisplay.WriteFileInfo(fileName, "文件不存在");
                                results.Add(new Dictionary<string, string>
                                {
                                    { "file", filePath ?? "未知文件" },
                                    { "filename", fileName },
                                    { "status", "missing" },
                                    { "error", L.T("文件不存在") }
                                });
                            }
                            else
                            {
                                // 计算哈希
                                var currentHashes = await FastHashCalculator.ComputeHashesAsync(
                                    filePath,
                                    metadata.algorithms.ToArray(),
                                    cancellationToken: cancellationToken);

                                if (currentHashes["success"] != "true")
                                {
                                    string errorMsg = currentHashes.ContainsKey("error") ? currentHashes["error"] : L.T("未知错误");
                                    ProgressDisplay.WriteFileInfo(fileName, string.Format(L.T("计算哈希失败: {0}"), errorMsg));
                                    results.Add(new Dictionary<string, string>
                                    {
                                        { "file", filePath },
                                        { "filename", fileName },
                                        { "status", "error" },
                                        { "error", errorMsg }
                                    });
                                }
                                else
                                {
                                    ProgressDisplay.WriteFileInfo(fileName, "哈希计算完成 ✓");
                                    results.Add(currentHashes);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ProgressDisplay.WriteFileInfo(fileName, string.Format(L.T("计算过程中出错: {0}"), ex.Message));
                            ProgressDisplay.WriteToLog(string.Format(L.T("[线程 {0}] 文件 {1} 异常: {2}"), Thread.CurrentThread.ManagedThreadId, fileName, ex.Message));
                            results.Add(new Dictionary<string, string>
                            {
                                { "file", filePath ?? "未知文件" },
                                { "filename", fileName },
                                { "status", "error" },
                                { "error", ex.Message }
                            });
                        }
                        finally
                        {
                            fileAccessSemaphore.Release();

                            int currentProcessed = Interlocked.Increment(ref processed);

                            var now = DateTime.UtcNow;
                            if (now - lastUpdateTime > TimeSpan.FromMilliseconds(200))
                            {
                                lock (updateLock)
                                {
                                    if (now - lastUpdateTime > TimeSpan.FromMilliseconds(200))
                                    {
                                        double percentage = (double)currentProcessed / total;
                                        var elapsed = DateTime.UtcNow - startTime;
                                        string eta = ProgressDisplay.GetETA(elapsed, currentProcessed, total);
                                        string progressBar = ProgressDisplay.GetProgressBar(percentage);
                                        string spinner = ProgressDisplay.GetSpinner();

                                        string progressText = string.Format(L.T("{0} [{1}] {2:P1} | {3}/{4} 文件 | {5} | 正在计算: {6}"),
                                            spinner, progressBar, percentage,
                                            currentProcessed, total, eta,
                                            ProgressDisplay.Truncate(fileName, 25));

                                        ProgressDisplay.UpdateProgressLine(progressText);
                                        lastUpdateTime = now;
                                    }
                                }
                            }
                        }
                    });

                ProgressDisplay.WriteProgressComplete(string.Format(L.T("哈希计算完成: {0} 个文件"), total));
                ProgressDisplay.WriteToLog(string.Format(L.T("哈希计算完成，共计算 {0} 个文件"), total));
            }
            catch (OperationCanceledException)
            {
                ProgressDisplay.WriteToLog(L.T("计算被用户取消"));
                ProgressDisplay.WriteProgressComplete(L.T("计算被用户取消"));
            }
            catch (Exception ex)
            {
                ProgressDisplay.WriteToLog(string.Format(L.T("计算过程中出现异常: {0}"), ex.Message));
                ProgressDisplay.WriteToLog(string.Format(L.T("异常堆栈: {0}"), ex.StackTrace));
                ProgressDisplay.WriteProgressComplete(string.Format(L.T("计算出错: {0}"), ex.Message));
            }
            finally
            {
                fileAccessSemaphore.Dispose();
            }

            ProgressDisplay.WriteSuccess(string.Format(L.T("哈希计算完成: {0}/{1} 文件"), processed, total));
            return results.OrderBy(r => r.ContainsKey("file") ? r["file"] : "").ToList();
        }

        // 新方法：对比两个JSON结果
        private List<VerificationResult> CompareJsonResults(List<Dictionary<string, string>> stored, List<Dictionary<string, string>> recalculated)
        {
            var results = new List<VerificationResult>();

            // 以「解析后的绝对路径」为键：报告与文件一起搬移后相对路径的基准目录会变，
            // 直接比对原始字符串会把所有文件都判成缺失
            string KeyOf(Dictionary<string, string> record)
            {
                string raw = record.ContainsKey("file") ? record["file"]
                    : record.ContainsKey("full_path") ? record["full_path"] : "";
                if (string.IsNullOrEmpty(raw)) return "";

                string name = record.ContainsKey("filename") ? record["filename"] : Path.GetFileName(raw);
                string? resolved = ResolveExistingPath(raw, name);
                try { return Path.GetFullPath(resolved ?? raw); } catch { return raw; }
            }

            var storedDict = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var record in stored) storedDict[KeyOf(record)] = record;

            var recalculatedDict = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var record in recalculated) recalculatedDict[KeyOf(record)] = record;

            // 检查所有存储的文件
            foreach (var storedFile in stored)
            {
                string filePath = storedFile.ContainsKey("file") ? storedFile["file"] : storedFile["full_path"];
                string fileName = storedFile.ContainsKey("filename") ? storedFile["filename"] : Path.GetFileName(filePath);
                string recordKey = KeyOf(storedFile);

                var result = new VerificationResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Status = "success",
                    AlgorithmResults = new Dictionary<string, AlgorithmComparison>()
                };

                // 检查文件是否存在
                if (!recalculatedDict.ContainsKey(recordKey))
                {
                    result.Status = "missing";
                    result.Error = L.T("文件不存在");
                    results.Add(result);
                    continue;
                }

                var recalculatedFile = recalculatedDict[recordKey];

                // 检查计算是否成功
                if (recalculatedFile.ContainsKey("status") && recalculatedFile["status"] != "success")
                {
                    result.Status = "error";
                    result.Error = recalculatedFile.ContainsKey("error") ? recalculatedFile["error"] : L.T("计算失败");
                    results.Add(result);
                    continue;
                }

                // 检查文件大小
                if (storedFile.ContainsKey("size") && recalculatedFile.ContainsKey("size"))
                {
                    long storedSize = long.Parse(storedFile["size"]);
                    long currentSize = long.Parse(recalculatedFile["size"]);
                    result.SizeMatch = storedSize == currentSize;

                    if (!result.SizeMatch)
                    {
                        result.Status = "failed";
                        result.Error = L.T("文件大小不匹配");
                    }
                }

                // 检查各个哈希算法
                var algorithms = new[] { "md5", "sha1", "sha256", "sha512" };

                foreach (var algo in algorithms)
                {
                    if (storedFile.ContainsKey(algo) && recalculatedFile.ContainsKey(algo))
                    {
                        bool match = storedFile[algo].Equals(recalculatedFile[algo], StringComparison.OrdinalIgnoreCase);
                        result.AlgorithmResults[algo.ToUpper()] = new AlgorithmComparison
                        {
                            Expected = storedFile[algo],
                            Actual = recalculatedFile[algo],
                            Match = match
                        };

                        if (!match)
                        {
                            result.Status = "failed";
                            if (string.IsNullOrEmpty(result.Error))
                            {
                                result.Error = string.Format(L.T("{0} 哈希不匹配"), algo.ToUpper());
                            }
                        }
                    }
                }

                results.Add(result);
            }

            // 检查是否有新文件（只在重新计算中存在）
            foreach (var recalculatedFile in recalculated)
            {
                if (!storedDict.ContainsKey(KeyOf(recalculatedFile)))
                {
                    // 这个文件在存储列表中不存在，跳过
                    continue;
                }
            }

            return results;
        }

        // 辅助方法：从文件列表中检测算法
        private List<string> DetectAlgorithmsFromFiles(List<Dictionary<string, string>> files)
        {
            var algorithmSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (file.ContainsKey("md5")) algorithmSet.Add("MD5");
                if (file.ContainsKey("sha1")) algorithmSet.Add("SHA1");
                if (file.ContainsKey("sha256")) algorithmSet.Add("SHA256");
                if (file.ContainsKey("sha512")) algorithmSet.Add("SHA512");
            }

            return algorithmSet.ToList();
        }
    }

    // VerificationResult和AlgorithmComparison类保持不变
    public class VerificationResult
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Status { get; set; } = ""; // success, failed, missing, error
        public string Error { get; set; } = "";
        public bool SizeMatch { get; set; } = true;
        public Dictionary<string, AlgorithmComparison> AlgorithmResults { get; set; } = new();
    }

    public class AlgorithmComparison
    {
        public string Expected { get; set; } = "";
        public string Actual { get; set; } = "";
        public bool Match { get; set; }
    }

    // 验证报告相关类
    public class VerificationReport
    {
        public VerificationMetadata Metadata { get; set; } = new();
        public VerificationSummary Summary { get; set; } = new();
        public VerificationIssuesSummary IssuesSummary { get; set; } = new();
        public VerificationProblemFiles ProblemFiles { get; set; } = new();
        public List<VerificationResultItem> Results { get; set; } = new();
    }

    public class VerificationMetadata
    {
        public string Generated { get; set; } = "";
        public string VerificationFile { get; set; } = "";
        public string PathFilter { get; set; } = "";
        public long TotalTimeMs { get; set; }
        public int ParallelThreads { get; set; }
    }

    public class VerificationSummary
    {
        public int TotalStoredFiles { get; set; }
        public int VerifiedFiles { get; set; }
        public int Success { get; set; }
        public int Failed { get; set; }
        public int Missing { get; set; }
        public int Error { get; set; }
        public string SuccessRate { get; set; } = "";
    }

    public class VerificationIssuesSummary
    {
        public int TotalIssues { get; set; }
        public int HashMismatch { get; set; }
        public int FilesMissing { get; set; }
        public int CalculationErrors { get; set; }
    }

    public class VerificationProblemFiles
    {
        public List<HashMismatchFile> HashMismatch { get; set; } = new();
        public List<MissingFile> FilesMissing { get; set; } = new();
        public List<ErrorFile> CalculationErrors { get; set; } = new();
    }

    public class HashMismatchFile
    {
        public string File { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Status { get; set; } = "";
        public bool SizeMatch { get; set; }
        public Dictionary<string, AlgorithmMismatchDetail> MismatchedAlgorithms { get; set; } = new();
    }

    public class AlgorithmMismatchDetail
    {
        public string Expected { get; set; } = "";
        public string Actual { get; set; } = "";
    }

    public class MissingFile
    {
        public string File { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class ErrorFile
    {
        public string File { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class VerificationResultItem
    {
        public string File { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
        public bool SizeMatch { get; set; }
        public Dictionary<string, AlgorithmComparison> Algorithms { get; set; } = new();
    }
}

