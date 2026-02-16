
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




