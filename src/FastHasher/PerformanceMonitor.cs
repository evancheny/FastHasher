﻿// PerformanceMonitor.cs - 性能监控器
// 提供系统性能监控功能，包括内存使用情况和线程池状态
using System.Diagnostics;

namespace FastHasher
{
    /// <summary>
    /// 性能监控器类
    /// 提供系统资源和线程池的实时监控信息
    /// </summary>
    public static class PerformanceMonitor
    {
        /// <summary>
        /// 系统总可用内存（字节）
        /// 在静态构造时初始化，避免重复查询
        /// </summary>
        private static readonly long totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        /// <summary>
        /// 获取当前进程的内存使用信息
        /// </summary>
        /// <returns>格式化的内存使用字符串，格式：内存: 当前使用MB / 总内存MB</returns>
        public static string GetMemoryInfo()
        {
            var process = Process.GetCurrentProcess();
            return string.Format(L.T("内存: {0}MB / {1}MB"),
                process.WorkingSet64 / 1024 / 1024,  // 当前进程使用的工作集内存
                totalMemory / 1024 / 1024);          // 系统总可用内存
        }

        /// <summary>
        /// 获取线程池状态信息
        /// </summary>
        /// <returns>格式化的线程池状态字符串，包含工作线程和IO线程的可用/最大数量</returns>
        public static string GetThreadInfo()
        {
            // 获取线程池中可用的线程数
            ThreadPool.GetAvailableThreads(out int worker, out int io);
            // 获取线程池的最大线程数
            ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);

            return string.Format(L.T("线程池: {0}/{1} (工作), {2}/{3} (IO) | 逻辑线程数: {4}"),
                worker, maxWorker,          // 工作线程：可用/最大
                io, maxIo,                  // IO线程：可用/最大
                Environment.ProcessorCount); // 系统逻辑处理器数量
        }
    }
}

