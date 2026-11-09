// FastHashCalculator.cs - 高性能哈希计算器（优化版）
// 提供多种哈希算法的高性能计算功能，支持并行处理和流式处理
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace FastHasher
{
    /// <summary>
    /// 高性能哈希计算器 - 优化版
    /// 支持MD5、SHA1、SHA256、SHA512等多种哈希算法
    /// 采用动态缓冲区策略和并行处理优化性能
    /// </summary>
    public static class FastHashCalculator
    {
        /// <summary>
        /// 大文件阈值（10MB），超过此大小使用流式处理
        /// 流式处理可以减少内存占用，适合大文件处理
        /// </summary>
        private const long LargeFileThreshold = 10 * 1024 * 1024;

        /// <summary>
        /// 缓冲区大小阈值（MB）
        /// 用于根据文件大小动态选择最佳缓冲区大小
        /// </summary>
        private static readonly long[] BufferSizeThresholds = { 10, 100, 500, 2048, long.MaxValue };

        /// <summary>
        /// 对应的缓冲区大小（MB）
        /// 与BufferSizeThresholds数组一一对应
        /// </summary>
        private static readonly int[] BufferSizes = { 1, 2, 4, 8, 10 };

        /// <summary>
        /// 获取最佳缓冲区大小（根据文件大小动态调整）- 优化版
        /// </summary>
        /// <param name="fileSize">文件大小（字节）</param>
        /// <returns>最佳缓冲区大小（字节）</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetOptimalBufferSize(long fileSize)
        {
            long fileSizeMB = fileSize / (1024 * 1024);
            for (int i = 0; i < BufferSizeThresholds.Length; i++)
            {
                if (fileSizeMB <= BufferSizeThresholds[i])
                {
                    return BufferSizes[i] * 1024 * 1024;
                }
            }
            return 10 * 1024 * 1024;
        }

        /// <summary>
        /// 高性能十六进制转换 - 使用 Span 优化
        /// 将字节数组转换为十六进制字符串表示
        /// </summary>
        /// <param name="bytes">要转换的字节数组</param>
        /// <returns>十六进制字符串</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var hexChars = new char[bytes.Length * 2];
            var hexTable = "0123456789abcdef".AsSpan();

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                hexChars[i * 2] = hexTable[b >> 4];
                hexChars[i * 2 + 1] = hexTable[b & 0x0F];
            }

            return new string(hexChars);
        }

        /// <summary>
        /// 用于小文件的快速哈希计算 - 使用 ValueTask 减少异步开销
        /// 支持MD5、SHA1、SHA256、SHA512等多种哈希算法
        /// </summary>
        /// <param name="algorithm">哈希算法名称（如"MD5"、"SHA256"等）</param>
        /// <param name="data">要计算哈希的数据</param>
        /// <returns>包含算法名称、哈希值和错误信息的元组</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ValueTask<(string algorithm, string hash, string error)> ComputeHashForAlgorithmAsync(
            string algorithm, ReadOnlySpan<byte> data)
        {
            try
            {
                byte[] hash;
                string algoUpper = algorithm.ToUpper().Replace("-", ""); // 标准化算法名称

                switch (algoUpper)
                {
                    case "MD5":
                        hash = MD5.HashData(data);
                        break;
                    case "SHA1":
                        hash = SHA1.HashData(data);
                        break;
                    case "SHA256":
                        hash = SHA256.HashData(data);
                        break;
                    case "SHA512":
                        hash = SHA512.HashData(data);
                        break;
                    case "BLAKE3":
                        hash = Blake3.Hasher.Hash(data).AsSpan().ToArray();
                        break;
                    default:
                        return ValueTask.FromResult<(string, string, string)>(
                            (algorithm, string.Empty, $"不支持的算法: {algorithm}"));
                }

                return ValueTask.FromResult<(string, string, string)>(
                    (algorithm, BytesToHex(hash) ?? string.Empty, string.Empty));
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult<(string, string, string)>(
                    (algorithm, string.Empty, ex.Message));
            }
        }
        
        public static async Task<Dictionary<string, string>> ComputeHashesAsync(
            string filePath,
            string[] algorithms,
            bool computeSizeOnly = false,
            CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, string>
            {
                // 存相对路径：报告可与文件一起搬到其它机器/目录后继续 verify
                { "file", PortablePath.ToPortable(filePath) },
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

                // FIFO / 字符设备 / 块设备 / 套接字不具备普通文件的读取语义，
                // 无写端的 FIFO 会永久阻塞，直接跳过
                if (!FileTypeHelper.IsRegularFile(filePath))
                {
                    results["success"] = "false";
                    results["error"] = "非常规文件（FIFO/设备/套接字），已跳过";
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
                ProgressDisplay.WriteFileInfo(shortName, $"开始计算哈希 | 大小: {ProgressDisplay.FormatSize(fileSize)}");

                // 根据文件大小选择处理策略
                if (fileSize <= LargeFileThreshold)
                {
                    // 小文件：一次性读取到内存 - 优化版
                    await ComputeSmallFileHashesOptimized(filePath, algorithms, results, cancellationToken);
                }
                else
                {
                    // 大文件：流式处理，使用动态缓冲区 - 优化版
                    int bufferSize = GetOptimalBufferSize(fileSize);
                    ProgressDisplay.WriteFileInfo(shortName, $"使用动态缓冲区: {ProgressDisplay.FormatSize(bufferSize)}");
                    await ComputeLargeFileHashesOptimized(filePath, algorithms, results, bufferSize, cancellationToken);
                }

                stopwatch.Stop();
                results["time_ms"] = stopwatch.ElapsedMilliseconds.ToString("F2");
                results["success"] = "true";

                // 添加性能数据用于图表
                results["size_mb"] = (fileSize / (1024.0 * 1024.0)).ToString("F2");
                results["size_mb_double"] = (fileSize / (1024.0 * 1024.0)).ToString("F8");
                results["time_ms_double"] = stopwatch.ElapsedMilliseconds.ToString("F4");

                // 显示处理完成信息
                ProgressDisplay.WriteFileInfo(shortName, $"哈希计算完成 | 用时: {stopwatch.ElapsedMilliseconds:F2}ms");
            }
            catch (Exception ex)
            {
                results["success"] = "false";
                results["error"] = ex.Message;
                results["error_type"] = ex.GetType().Name;

                // 记录错误
                ProgressDisplay.WriteError($"文件 {Path.GetFileName(filePath)} 处理失败: {ex.Message}");
            }

            return results;
        }
        
        // 优化版小文件哈希计算 - 使用内存池和并行计算
        private static async Task ComputeSmallFileHashesOptimized(
            string filePath,
            string[] algorithms,
            Dictionary<string, string> results,
            CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            string shortName = fileName.Length > 30 ? fileName.Substring(0, 27) + "..." : fileName;

            // 使用内存池分配内存，减少GC压力
            byte[] fileData;
            int bytesRead = 0;
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                fileData = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                try
                {
                    int totalBytes = (int)stream.Length;

                    while (bytesRead < totalBytes)
                    {
                        int bytesToRead = Math.Min(1024 * 1024, totalBytes - bytesRead);
                        int read = await stream.ReadAsync(fileData, bytesRead, bytesToRead, cancellationToken);
                        if (read == 0) break;
                        bytesRead += read;
                    }
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(fileData);
                    throw;
                }
            }

            ProgressDisplay.WriteFileInfo(shortName, $"文件读取完成 | 大小: {ProgressDisplay.FormatSize(bytesRead)}");

            // 优化版哈希计算 - 使用并行和Span，只使用实际读取的字节数
            await ComputeHashesForDataOptimized(fileData.AsSpan(0, bytesRead).ToArray(), algorithms, results, shortName);

            // 归还内存池
            ArrayPool<byte>.Shared.Return(fileData);
        }
        
        // 优化版大文件哈希计算 - 使用内存池和异步优化
        private static async Task ComputeLargeFileHashesOptimized(
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

            ProgressDisplay.WriteFileInfo(shortName, $"流式处理大文件 | 大小: {ProgressDisplay.FormatSize(totalBytes)} | 缓冲区: {ProgressDisplay.FormatSize(bufferSize)}");

            // 初始化所有哈希算法
            var hashAlgorithms = new List<(string name, HashAlgorithm algo)>();
            foreach (string algorithm in algorithms)
            {
                var hashAlgo = CreateHashAlgorithm(algorithm);
                if (hashAlgo != null)
                {
                    hashAlgorithms.Add((algorithm, hashAlgo));
                }
            }

            // 使用内存池分配缓冲区
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                // 流式处理文件 - 使用异步优化
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    long processedBytes = 0;
                    var lastUpdateTime = DateTime.UtcNow;

                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (bytesRead == 0) break;

                        // 使用缓冲区计算哈希
                        foreach (var hashAlgo in hashAlgorithms)
                        {
                            hashAlgo.algo.TransformBlock(buffer, 0, bytesRead, null, 0);
                        }

                        processedBytes += bytesRead;

                        // 降低进度更新频率以提升性能
                        var now = DateTime.UtcNow;
                        if (now - lastUpdateTime > TimeSpan.FromMilliseconds(200))
                        {
                            double percentage = (double)processedBytes / totalBytes;
                            string progressBar = ProgressDisplay.GetProgressBar(percentage, 15);
                            string spinner = ProgressDisplay.GetSpinner();
                            string speed = ProgressDisplay.FormatSpeed(processedBytes, TimeSpan.FromMilliseconds(200));

                            ProgressDisplay.UpdateProgressLine($"{spinner} {shortName} [{progressBar}] {percentage:P1} | {ProgressDisplay.FormatSize(processedBytes)}/{ProgressDisplay.FormatSize(totalBytes)} | {speed} | 缓冲区: {ProgressDisplay.FormatSize(bufferSize)}");

                            lastUpdateTime = now;
                        }

                        if (cancellationToken.IsCancellationRequested)
                            break;
                    }

                    // 完成计算
                    foreach (var hashAlgo in hashAlgorithms)
                    {
                        hashAlgo.algo.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        results[hashAlgo.name.ToLower()] = BytesToHex(hashAlgo.algo.Hash ?? Array.Empty<byte>());
                        ProgressDisplay.WriteFileInfo(shortName, $"算法 {hashAlgo.name} 计算完成");
                    }
                }
            }
            finally
            {
                // 归还内存池
                ArrayPool<byte>.Shared.Return(buffer);

                // 清理资源
                foreach (var hashAlgo in hashAlgorithms)
                {
                    hashAlgo.algo.Dispose();
                }
            }

            ProgressDisplay.WriteProgressComplete($"大文件处理完成: {shortName}");
        }
        
        // 优化版哈希计算 - 使用并行和Span优化
        private static async Task ComputeHashesForDataOptimized(
            byte[] data,
            string[] algorithms,
            Dictionary<string, string> results,
            string fileName)
        {
            ProgressDisplay.WriteFileInfo(fileName, $"开始计算 {algorithms.Length} 种哈希算法");

            // 使用并行计算哈希 - 优化版
            var tasks = new List<ValueTask<(string algorithm, string hash, string error)>>();

            foreach (string algorithm in algorithms)
            {
                tasks.Add(ComputeHashForAlgorithmAsync(algorithm, data));
            }

            // 等待所有任务完成
            var hashResults = new (string algorithm, string hash, string error)[tasks.Count];
            for (int i = 0; i < tasks.Count; i++)
            {
                hashResults[i] = await tasks[i];
            }

            // 收集结果
            foreach (var result in hashResults)
            {
                if (!string.IsNullOrEmpty(result.error))
                {
                    results[$"error_{result.algorithm.ToLower()}"] = result.error;
                    ProgressDisplay.WriteWarning($"文件 {fileName} | 算法 {result.algorithm} 计算失败: {result.error}");
                }
                else
                {
                    results[result.algorithm.ToLower()] = result.hash;
                    string hashPreview = result.hash.Length > 16 ? result.hash.Substring(0, 16) : result.hash;
                    ProgressDisplay.WriteFileInfo(fileName, $"算法 {result.algorithm} 计算完成 | 哈希: {hashPreview}...");
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
                case "BLAKE3":
                    return new Blake3.Blake3HashAlgorithm();
                default:
                    throw new ArgumentException($"不支持的算法: {algorithm}");
            }
        }
    }
}




