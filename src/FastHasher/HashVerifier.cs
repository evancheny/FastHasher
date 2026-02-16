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

