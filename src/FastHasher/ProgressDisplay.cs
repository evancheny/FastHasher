
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

