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

