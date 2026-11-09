﻿// Program.cs - 主程序入口
// 负责初始化运行环境、解析命令行、分发子命令、打印帮助与版本信息
namespace FastHasher
{
    /// <summary>
    /// FastHasher 主程序类
    /// 参数解析全部交给 CliParser，本类只做环境初始化和子命令分发
    /// </summary>
    class Program
    {
        /// <summary>
        /// 当前正在执行的任务的取消入口，null 表示空闲
        /// </summary>
        private static Action? activeCancel;

        /// <summary>
        /// 程序主入口
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>进程退出码：0 成功，1 存在失败项，2 参数错误，130 用户取消</returns>
        static async Task<int> Main(string[] args)
        {
            SetupConsole();
            RegisterCancelHandler();

            // 语言必须最先确定（--lang > 配置文件 > 默认中文），
            // 否则后续参数解析错误、帮助文本都会用错语言
            L.Initialize(CliParser.PreScanLanguage(args));

            // 无参数：双击 exe 或直接运行都进入交互式向导；
            // 输入被重定向（脚本、管道）时退回帮助，避免等待用户输入
            if (args.Length == 0)
            {
                if (Console.IsInputRedirected)
                {
                    ShowHelp();
                    return 0;
                }

                return await InteractiveShell.RunAsync();
            }

            CliParseResult parsed = CliParser.Parse(args);

            if (!parsed.Success)
            {
                Console.Error.WriteLine(L.T("错误: ") + parsed.Error);
                return parsed.ExitCode;
            }

            CliOptions options = parsed.Options!;

            foreach (string warning in parsed.Warnings)
            {
                Console.Error.WriteLine(L.T("提示: ") + warning);
            }

            switch (options.Command)
            {
                case CliCommand.Help:
                    ShowHelp();
                    WaitForKey();
                    return 0;

                case CliCommand.Version:
                    ShowVersion();
                    return 0;

                case CliCommand.Interactive:
                    ApplyUi(options);
                    return await InteractiveShell.RunAsync();

                case CliCommand.Bench:
                    ApplyUi(options);
                    await RunBenchmarkAsync();
                    return 0;

                case CliCommand.Verify:
                    ApplyUi(options);
                    return await RunVerifyAsync(options);

                default:
                    ApplyUi(options);
                    return await RunHashAsync(options);
            }
        }

        /// <summary>
        /// 注册全局 Ctrl+C 处理：有任务时取消任务，空闲时保持系统默认行为
        /// </summary>
        static void RegisterCancelHandler()
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                Action? cancel = activeCancel;
                if (cancel == null)
                {
                    // 没有正在执行的任务，交给系统终止进程
                    return;
                }

                e.Cancel = true;
                Console.Error.WriteLine();
                Console.Error.WriteLine(L.T("正在取消当前任务，请稍候..."));
                cancel();
            };
        }

        /// <summary>
        /// 初始化控制台编码与线程池
        /// </summary>
        static void SetupConsole()
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;

                // 仅在交互终端设置输入编码：stdin 被重定向时重建 reader 会吞掉已缓冲的首行
                if (!Console.IsInputRedirected)
                {
                    Console.InputEncoding = System.Text.Encoding.UTF8;
                }
            }
            catch
            {
                // 输出被重定向时可能失败，忽略即可
            }

            int cpuThreads = Environment.ProcessorCount;
            ThreadPool.SetMinThreads(cpuThreads * 2, cpuThreads * 2);
        }

        /// <summary>
        /// 应用显示相关参数（颜色、详细程度、进度）
        /// </summary>
        internal static void ApplyUi(CliOptions options)
        {
            ProgressDisplay.Verbosity = options.Verbosity;
            ProgressDisplay.EnableColor = !options.NoColor && !Console.IsOutputRedirected;
            ProgressDisplay.ProgressEnabled = options.Progress switch
            {
                ProgressMode.Always => true,
                ProgressMode.Never => false,
                _ => options.Verbosity != VerbosityLevel.Quiet && !Console.IsErrorRedirected
            };
        }

        /// <summary>
        /// 计算模式
        /// </summary>
        internal static async Task<int> RunHashAsync(CliOptions options)
        {
            ProgressDisplay.WriteStatistics(L.T("计算模式"),
                ("算法", string.Join(", ", options.Algorithms), ConsoleColor.Cyan),
                ("输入", options.Inputs.Count == 1 ? options.Inputs[0] : string.Format(L.T("{0} 个模式"), options.Inputs.Count), ConsoleColor.White),
                ("递归", options.Recursive ? "是" : "否", ConsoleColor.Gray),
                ("线程", options.IsParallel ? string.Format(L.T("{0} (并行)"), options.Threads) : "1 (顺序)", ConsoleColor.Green),
                ("输出", options.OutputToStdout ? "stdout" : (options.OutputPath ?? "默认文件名"), ConsoleColor.Gray),
                ("格式", options.Format.ToString().ToLowerInvariant(), ConsoleColor.Gray)
            );

            var processor = new FileProcessor(options);
            activeCancel = processor.Cancel;

            try
            {
                await processor.ProcessFilesAsync(options.Inputs);
                return processor.ExitCode;
            }
            finally
            {
                activeCancel = null;
            }
        }

        /// <summary>
        /// 验证模式
        /// </summary>
        internal static async Task<int> RunVerifyAsync(CliOptions options)
        {
            ProgressDisplay.WriteStatistics(L.T("验证模式"),
                ("验证文件", options.VerifyJson ?? string.Empty, ConsoleColor.Cyan),
                ("路径过滤", options.Inputs.Count == 0 ? "无" : string.Join("; ", options.Inputs), ConsoleColor.Gray),
                ("线程", options.IsParallel ? string.Format(L.T("{0} (并行)"), options.Threads) : "1 (顺序)", ConsoleColor.Green),
                ("输出", options.OutputToStdout ? "stdout" : (options.OutputPath ?? "默认文件名"), ConsoleColor.Gray)
            );

            try
            {
                var verifier = new HashVerifier(options);
                activeCancel = verifier.Cancel;

                await verifier.VerifyAsync();
                return verifier.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(L.T("验证失败: ") + ex.Message);
                return 2;
            }
            finally
            {
                activeCancel = null;
            }
        }

        /// <summary>
        /// 显示版本信息
        /// </summary>
        static void ShowVersion()
        {
            Console.WriteLine(string.Format("FastHasher v{0}", VersionInfo.GetVersion()));
            Console.WriteLine(VersionInfo.GetInformationalVersion());
            Console.WriteLine(string.Format(L.T("CPU 逻辑线程数: {0}"), Environment.ProcessorCount));
        }

        /// <summary>
        /// 显示帮助信息。
        /// 帮助文本按整段维护（L.HelpHeader / L.HelpBody），不做逐行 T() 翻译：
        /// 两种语言的选项列对齐方式不同，整段维护排版质量最好。
        /// 新增命令行选项时必须同时更新 Localization.cs 的 ChineseHelp 与 EnglishHelp。
        /// </summary>
        internal static void ShowHelp()
        {
            Console.WriteLine(string.Format(L.HelpHeader, VersionInfo.GetVersion()));
            Console.WriteLine();
            Console.WriteLine(L.HelpBody);
        }

        /// <summary>
        /// 等待用户按键（输入被重定向时直接返回）
        /// </summary>
        static void WaitForKey()
        {
            // 输入被重定向（管道、CI、无终端）时不提示也不等待
            if (Console.IsInputRedirected) return;

            try
            {
                Console.WriteLine();
                Console.WriteLine(L.T("按任意键退出..."));
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                // 无可用控制台，忽略
            }
        }

        /// <summary>
        /// 运行基准测试
        /// </summary>
        internal static async Task RunBenchmarkAsync()
        {
            Console.WriteLine(L.T("运行基准测试..."));
            Console.WriteLine("================");
            Console.WriteLine(string.Format(L.T("CPU 逻辑线程数: {0}"), Environment.ProcessorCount));

            const int testSize = 10 * 1024 * 1024; // 10MB
            var testData = new byte[testSize];
            new Random().NextBytes(testData);

            string testFile = "benchmark_test.tmp";
            await File.WriteAllBytesAsync(testFile, testData);

            try
            {
                var algorithms = new[] { "MD5", "SHA1", "SHA256", "SHA512", "BLAKE3" };

                Console.WriteLine(string.Format(L.T("测试文件: {0}MB"), testSize / 1024 / 1024));
                Console.WriteLine("---");
                Console.WriteLine(L.T("顺序处理测试:"));

                long sequentialTotal = 0;

                foreach (string algorithm in algorithms)
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var result = await FastHashCalculator.ComputeHashesAsync(testFile, new[] { algorithm });
                    stopwatch.Stop();

                    if (result["success"] == "true")
                    {
                        string hash = result[algorithm.ToLowerInvariant()];
                        sequentialTotal += stopwatch.ElapsedMilliseconds;
                        Console.WriteLine(string.Format("  {0,-6}: {1}ms ({2}...)",
                            algorithm, stopwatch.ElapsedMilliseconds, hash.Substring(0, Math.Min(16, hash.Length))));
                    }
                }

                Console.WriteLine(string.Format(L.T("  总计: {0}ms"), sequentialTotal));
                Console.WriteLine();

                Console.WriteLine(L.T("并行处理测试（所有算法同时计算）:"));
                var parallelStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var parallelResult = await FastHashCalculator.ComputeHashesAsync(testFile, algorithms);
                parallelStopwatch.Stop();

                if (parallelResult["success"] == "true")
                {
                    Console.WriteLine(string.Format(L.T("  总计: {0}ms"), parallelStopwatch.ElapsedMilliseconds));
                    foreach (string algorithm in algorithms)
                    {
                        string hash = parallelResult[algorithm.ToLowerInvariant()];
                        Console.WriteLine(string.Format("  {0,-6}: {1}...", algorithm, hash.Substring(0, Math.Min(16, hash.Length))));
                    }

                    if (parallelStopwatch.ElapsedMilliseconds > 0)
                    {
                        double speedup = (double)sequentialTotal / parallelStopwatch.ElapsedMilliseconds;
                        Console.WriteLine();
                        Console.WriteLine(string.Format(L.T("加速比: {0:F2}x"), speedup));
                        Console.WriteLine(string.Format(L.T("并行效率: {0:F1}%"), speedup / Environment.ProcessorCount * 100));
                    }
                }

                Console.WriteLine();
                Console.WriteLine(L.T("基准测试完成"));
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    File.Delete(testFile);
                }
            }
        }
    }
}
