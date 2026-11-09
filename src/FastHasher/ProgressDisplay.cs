// ProgressDisplay.cs - 专业进度显示类（优化配色版）
// 提供美观的命令行界面，包括进度条、状态显示、彩色输出等
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace FastHasher
{
    /// <summary>
    /// 专业进度显示类
    /// 提供美观的命令行界面，包括进度条、状态显示、彩色输出等
    /// </summary>
    public static class ProgressDisplay
    {
        /// <summary>
        /// 旋转动画字符数组
        /// 用于显示处理中的动画效果
        /// </summary>
        private static readonly string[] spinnerChars = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        /// <summary>
        /// 进度条字符数组
        /// 用于绘制平滑的进度条
        /// </summary>
        private static readonly string[] barChars = { "█", "▉", "▊", "▋", "▌", "▍", "▎", "▏", " ", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };

        /// <summary>
        /// 当前旋转动画索引
        /// </summary>
        private static int spinnerIndex = 0;

        /// <summary>
        /// 当前进度条索引
        /// </summary>
        private static int barIndex = 0;

        /// <summary>
        /// 控制台输出锁对象
        /// 用于多线程环境下的线程安全输出
        /// </summary>
        private static readonly object consoleLock = new object();

        /// <summary>
        /// 日志文件写入锁对象
        /// 用于多线程环境下的线程安全日志写入
        /// </summary>
        private static readonly object logLock = new object();

        /// <summary>
        /// 日志文件路径
        /// </summary>
        private static string logFilePath = "";

        /// <summary>
        /// 是否启用彩色输出
        /// </summary>
        private static bool enableColor = true;

        /// <summary>
        /// 是否启用彩色输出（由 --no-color 或输出重定向关闭）
        /// </summary>
        public static bool EnableColor
        {
            get => enableColor;
            set => enableColor = value;
        }

        /// <summary>
        /// 控制台输出详细程度（由 --quiet / --verbose 设置）
        /// </summary>
        public static VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>
        /// 是否允许输出动态进度行（由 --progress 设置）
        /// </summary>
        public static bool ProgressEnabled { get; set; } = true;

        /// <summary>
        /// 是否处于安静模式：只输出错误与警告
        /// </summary>
        private static bool IsQuiet => Verbosity == VerbosityLevel.Quiet;

        /// <summary>
        /// 简洁统一配色方案 - 极简风格
        /// </summary>
        private static readonly ConsoleColor ColorSuccess = ConsoleColor.Green;       // 成功 - 绿色
        private static readonly ConsoleColor ColorInfo = ConsoleColor.Cyan;          // 信息 - 青色
        private static readonly ConsoleColor ColorWarning = ConsoleColor.Yellow;     // 警告 - 黄色
        private static readonly ConsoleColor ColorError = ConsoleColor.Red;          // 错误 - 红色
        private static readonly ConsoleColor ColorFile = ConsoleColor.Cyan;          // 文件名 - 青色
        private static readonly ConsoleColor ColorValue = ConsoleColor.Cyan;         // 数值 - 青色
        private static readonly ConsoleColor ColorSection = ConsoleColor.Cyan;       // 章节 - 青色
        private static readonly ConsoleColor ColorProgress = ConsoleColor.Green;     // 进度 - 绿色
        private static readonly ConsoleColor ColorSecondary = ConsoleColor.Gray;     // 次要信息 - 灰色
        private static readonly ConsoleColor ColorMuted = ConsoleColor.Gray;         // 弱强调 - 灰色
        private static readonly ConsoleColor ColorBorder = ConsoleColor.Gray;        // 边框 - 灰色
        
        public static void InitializeLog(string path)
        {
            logFilePath = path;
            try
            {
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
                    }
                }
            }
            
            // 控制台镜像仅在 verbose 模式下输出，避免刷屏
            if (toConsole && Verbosity == VerbosityLevel.Verbose)
            {
                lock (consoleLock)
                {
                    try
                    {
                        Console.Error.WriteLine(logMessage);
                    }
                    catch
                    {
                    }
                }
            }
        }
        
        public static void WriteSection(string title)
        {
            if (IsQuiet) return;

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
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    if (enableColor) Console.ForegroundColor = ColorBorder;
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
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    if (enableColor) Console.ForegroundColor = ColorMuted;
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
            
            filled = Math.Max(0, filled);
            empty = Math.Max(0, empty);
            
            if (filled == 0)
            {
                return "[" + new string('░', empty) + "]";
            }
            
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
            // 进度行始终写 stderr，保证 stdout 只包含结果数据
            if (!ProgressEnabled) return;

            lock (consoleLock)
            {
                try
                {
                    int top = Console.CursorTop;

                    Console.SetCursorPosition(0, top);

                    Console.Error.Write(new string(' ', Console.WindowWidth - 1));

                    Console.SetCursorPosition(0, top);

                    if (text.Length > Console.WindowWidth - 1)
                    {
                        if (enableColor) Console.ForegroundColor = ColorProgress;
                        Console.Error.Write(text.Substring(0, Console.WindowWidth - 1));
                    }
                    else
                    {
                        if (enableColor) Console.ForegroundColor = ColorProgress;
                        Console.Error.Write(text);
                    }

                    if (enableColor) Console.ResetColor();
                }
                catch (Exception)
                {
                    Console.Error.Write(string.Format("\r{0}", text));
                }
            }
        }
        
        public static void ClearProgressLine()
        {
            if (!ProgressEnabled) return;

            lock (consoleLock)
            {
                try
                {
                    int currentLineCursor = Console.CursorTop;
                    Console.SetCursorPosition(0, currentLineCursor);
                    Console.Error.Write(new string(' ', Console.WindowWidth - 1));
                    Console.SetCursorPosition(0, currentLineCursor);
                }
                catch
                {
                    Console.Error.WriteLine();
                }
            }
        }
        
        public static void WriteProgressComplete(string message)
        {
            if (!ProgressEnabled) return;

            lock (consoleLock)
            {
                ClearProgressLine();
                if (enableColor) Console.ForegroundColor = ColorSuccess;
                Console.Error.WriteLine("✅ " + message);
                if (enableColor) Console.ResetColor();
                
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
                
                WriteToLog("错误: " + message, false);
            }
        }
        
        public static void WriteInfo(string message)
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorInfo;
                Console.Error.WriteLine("ℹ️  " + message);
                if (enableColor) Console.ResetColor();
                
                WriteToLog("信息: " + message, false);
            }
        }
        
        public static void WriteSuccess(string message)
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorSuccess;
                Console.Error.WriteLine("✅ " + message);
                if (enableColor) Console.ResetColor();
                
                WriteToLog("成功: " + message, false);
            }
        }
        
        public static void WriteFileInfo(string fileName, string message)
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                if (enableColor) Console.ForegroundColor = ColorFile;
                Console.Error.Write("📄 ");
                if (enableColor) Console.ForegroundColor = ColorMuted;
                Console.Error.Write(fileName);
                if (enableColor) Console.ForegroundColor = ColorSecondary;
                Console.Error.Write(": ");
                if (enableColor) Console.ResetColor();
                Console.Error.WriteLine(message);

                string logMessage = string.Format("文件 {0}: {1}", fileName, message);
                WriteToLog(logMessage, false);
            }
        }
        
        public static void WriteBanner(string title, string subtitle = "")
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    int width = Math.Max(title.Length, subtitle.Length) + 6;
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine("╔" + new string('═', width));
                    Console.Error.Write("║");
                    Console.Error.Write(new string(' ', (width - title.Length) / 2));
                    if (enableColor) Console.ForegroundColor = ColorInfo;
                    Console.Error.Write(title);
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.Write(new string(' ', width - title.Length - (width - title.Length) / 2));
                    Console.Error.WriteLine("║");

                    if (!string.IsNullOrEmpty(subtitle))
                    {
                        Console.Error.Write("║");
                        Console.Error.Write(new string(' ', (width - subtitle.Length) / 2));
                        if (enableColor) Console.ForegroundColor = ColorSecondary;
                        Console.Error.Write(subtitle);
                        if (enableColor) Console.ForegroundColor = ColorSection;
                        Console.Error.Write(new string(' ', width - subtitle.Length - (width - subtitle.Length) / 2));
                        Console.Error.WriteLine("║");
                    }
                    
                    Console.Error.WriteLine("╚" + new string('═', width));
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine();
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
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    int tableWidth = 60;
                    
                    var adjustedItems = items.Select(item => {
                        string key = item.key.Length > 20 ? item.key.Substring(0, 17) + "..." : item.key;
                        string value = item.value.Length > 25 ? item.value.Substring(0, 22) + "..." : item.value;
                        return (key, value, item.color);
                    }).ToArray();
                    
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine("┌" + new string('─', tableWidth));
                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ColorInfo;
                    Console.Error.Write(title);
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("├" + new string('─', tableWidth));
                    
                    foreach (var item in adjustedItems)
                    {
                        Console.Error.Write("│ ");
                        if (enableColor) Console.ForegroundColor = ColorMuted;
                        Console.Error.Write(item.key.PadRight(20));
                        Console.Error.Write(" : ");
                        if (enableColor) Console.ForegroundColor = item.color;
                        Console.Error.WriteLine(item.value);
                    }
                    
                    Console.Error.WriteLine("└" + new string('─', tableWidth));
                    if (enableColor) Console.ResetColor();
                }
                catch
                {
                    Console.Error.WriteLine("=== " + title + " ===");
                    foreach (var item in items)
                    {
                        Console.Error.WriteLine("  " + item.key + " : " + item.value);
                    }
                }
            }
        }
        
        public static void WriteHorizontalBarChart(string title, params (string label, double value, ConsoleColor color)[] items)
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    if (items == null || items.Length == 0) return;
                    
                    const int chartWidth = 64;
                    const int contentWidth = chartWidth - 2;
                    
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine("┌" + new string('─', chartWidth));

                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ColorInfo;
                    Console.Error.Write(title.PadRight(contentWidth));
                    Console.Error.WriteLine();

                    Console.Error.WriteLine("├" + new string('─', chartWidth));
                    
                    double maxValue = items.Max(x => x.value);
                    if (maxValue <= 0) maxValue = 1;
                    
                    foreach (var item in items)
                    {
                        Console.Error.Write("│ ");
                        
                        if (enableColor) Console.ForegroundColor = ColorMuted;
                        string label = item.label.Length > 15 ? item.label.Substring(0, 12) + "..." : item.label;
                        Console.Error.Write(label.PadRight(15));
                        Console.Error.Write(" ");
                        
                        int barLength = (int)((item.value / maxValue) * (contentWidth - 25));
                        barLength = Math.Max(0, Math.Min(contentWidth - 25, barLength));
                        
                        if (enableColor) Console.ForegroundColor = item.color;
                        Console.Error.Write(new string('█', barLength));
                        
                        if (enableColor) Console.ForegroundColor = ColorValue;
                        Console.Error.Write(" ");
                        Console.Error.Write(item.value.ToString("F2").PadLeft(8));
                        
                        Console.Error.WriteLine();
                    }
                    
                    Console.Error.WriteLine("└" + new string('─', chartWidth));
                    if (enableColor) Console.ResetColor();
                }
                catch (Exception)
                {
                    Console.Error.WriteLine(string.Format("=== {0} ===", title));
                    foreach (var item in items)
                    {
                        Console.Error.WriteLine(string.Format("  {0}: {1:F2}", item.label, item.value));
                    }
                }
            }
        }
        
        public static void WriteSizeDistributionChart(List<long> fileSizes, string title = "文件大小分布")
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    if (fileSizes == null || fileSizes.Count == 0) return;
                    
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
                    
                    var chartItems = new List<(string label, double value, ConsoleColor color)>();
                    for (int i = 0; i < sizeRanges.Length; i++)
                    {
                        if (counts[i] > 0)
                        {
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
        
        public static void WriteScatterPlot(string title, List<(double x, double y, string label)> points, 
                                          string xLabel = "文件大小(MB)", string yLabel = "处理时间(ms)")
        {
            if (IsQuiet) return;

            lock (consoleLock)
            {
                try
                {
                    if (points == null || points.Count == 0) return;
                    
                    Console.Error.WriteLine();
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine("┌" + new string('─', 64));
                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ColorInfo;
                    Console.Error.Write(title.PadRight(62));
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine();
                    
                    int gridWidth = 60;
                    int gridHeight = 20;
                    char[,] grid = new char[gridHeight, gridWidth];
                    
                    for (int y = 0; y < gridHeight; y++)
                    {
                        for (int x = 0; x < gridWidth; x++)
                        {
                            grid[y, x] = ' ';
                        }
                    }
                    
                    for (int y = 0; y < gridHeight; y++)
                    {
                        grid[y, 0] = '│';
                    }
                    for (int x = 0; x < gridWidth; x++)
                    {
                        grid[gridHeight - 1, x] = '─';
                    }
                    grid[gridHeight - 1, 0] = '└';
                    
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
                    
                    for (int y = 0; y < gridHeight; y++)
                    {
                        Console.Error.Write("│ ");
                        for (int x = 0; x < gridWidth; x++)
                        {
                            if (grid[y, x] == '●')
                            {
                                if (enableColor) Console.ForegroundColor = ColorProgress;
                                Console.Error.Write(grid[y, x]);
                            }
                            else
                            {
                                if (enableColor) Console.ForegroundColor = ColorMuted;
                                Console.Error.Write(grid[y, x]);
                            }
                        }
                        if (enableColor) Console.ForegroundColor = ColorSection;
                        Console.Error.WriteLine();
                    }

                    Console.Error.Write("│ ");
                    if (enableColor) Console.ForegroundColor = ColorMuted;
                    Console.Error.Write(string.Format("← {0}", xLabel).PadRight(32));
                    Console.Error.Write(string.Format("↑ {1}", yLabel).PadRight(32));
                    if (enableColor) Console.ForegroundColor = ColorSection;
                    Console.Error.WriteLine();
                    
                    Console.Error.WriteLine("└" + new string('─', 64));
                    if (enableColor) Console.ResetColor();
                }
                catch (Exception)
                {
                    Console.Error.WriteLine(string.Format("=== {0} ===", title));
                    Console.Error.WriteLine(string.Format("{0} 与 {1} 关系:", xLabel, yLabel));
                    foreach (var point in points.Take(10))
                    {
                        Console.Error.WriteLine(string.Format("  大小: {0:F1}MB, 时间: {1:F1}ms", point.x, point.y));
                    }
                }
            }
        }
    }
}
