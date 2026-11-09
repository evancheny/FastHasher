// InteractiveShell.cs - 交互式向导模式
// 通过菜单与问答逐项收集参数，最终仍复用 CliOptions + FileProcessor / HashVerifier 执行
// 目的：双击 exe 或输入 FastHasher -i 时，无需记忆任何参数即可完成全部操作
using System.Text;

namespace FastHasher
{
    /// <summary>
    /// 交互式运行模式
    /// 所有输入校验都复用 CliParser 的实现，本类只负责提问与展示
    /// </summary>
    internal static class InteractiveShell
    {
        /// <summary>输入流是否已结束（用于逐层退出向导）</summary>
        private static bool endOfInput;

        /// <summary>
        /// 启动交互式会话
        /// </summary>
        /// <returns>进程退出码，交互式会话正常结束固定为 0</returns>
        internal static async Task<int> RunAsync()
        {
            PrintHeader();

            while (!endOfInput)
            {
                PrintMenu();

                if (!TryReadInput("请选择", null, out string choice))
                {
                    break;
                }

                switch (choice.ToLowerInvariant())
                {
                    case "1":
                        await RunHashWizardAsync();
                        break;

                    case "2":
                        await RunVerifyWizardAsync();
                        break;

                    case "3":
                        await Program.RunBenchmarkAsync();
                        Pause();
                        break;

                    case "4":
                        Program.ShowHelp();
                        Pause();
                        break;

                    case "5":
                        ToggleLanguage();
                        break;

                    case "6":
                    case "q":
                    case "quit":
                    case "exit":
                        Console.WriteLine(L.T("再见。"));
                        return 0;

                    default:
                        WriteError(L.T("请输入 1-6 之间的数字，或输入 q 退出"));
                        break;
                }
            }

            Console.WriteLine();
            Console.WriteLine(L.T("再见。"));
            return 0;
        }

        /// <summary>
        /// 计算哈希向导
        /// </summary>
        private static async Task RunHashWizardAsync()
        {
            WriteStep(L.T("计算哈希"));

            if (!TryReadInput("路径（文件/目录/通配符，多个用空格分隔）", ".", out string rawPaths))
            {
                return;
            }

            var inputs = SplitArguments(rawPaths);
            if (inputs.Count == 0)
            {
                WriteError(L.T("路径不能为空"));
                return;
            }

            if (!TryReadInput("算法（md5/sha1/sha256/sha512/blake3/all，逗号分隔）", "md5", out string rawAlgorithms))
            {
                return;
            }

            if (!CliParser.TryParseAlgorithms(rawAlgorithms, out List<string> algorithms, out string? algorithmError))
            {
                WriteError(algorithmError!);
                return;
            }

            bool recursive = Ask("递归搜索子目录", inputs.Count == 1 && Directory.Exists(inputs[0]));

            if (!TryReadInput("并行线程数（1 表示顺序处理）", Environment.ProcessorCount.ToString(), out string rawThreads))
            {
                return;
            }

            if (!int.TryParse(rawThreads, out int threads) || threads <= 0)
            {
                WriteError(string.Format(L.T("线程数必须是正整数: {0}"), rawThreads));
                return;
            }

            int threadLimit = Environment.ProcessorCount * CliParser.ThreadLimitFactor;
            if (threads > threadLimit)
            {
                WriteInfo(string.Format(L.T("线程数 {0} 超过上限，已调整为 {1}"), threads, threadLimit));
                threads = threadLimit;
            }

            if (!TryReadInput("最小文件大小（回车跳过，例如 1KB）", null, out string rawMin))
            {
                return;
            }

            long minSize = 0;
            if (rawMin.Length > 0 && !CliParser.TryParseSize(rawMin, out minSize))
            {
                WriteError(string.Format(L.T("无法解析文件大小: {0}（示例: 1KB、10MB、0.5GiB）"), rawMin));
                return;
            }

            if (!TryReadInput("最大文件大小（回车跳过）", null, out string rawMax))
            {
                return;
            }

            long maxSize = long.MaxValue;
            if (rawMax.Length > 0 && !CliParser.TryParseSize(rawMax, out maxSize))
            {
                WriteError(string.Format(L.T("无法解析文件大小: {0}（示例: 100MB、2GiB）"), rawMax));
                return;
            }

            if (minSize > maxSize)
            {
                WriteError(L.T("最小文件大小不能大于最大文件大小"));
                return;
            }

            OutputFormat format = AskFormat();

            if (!TryReadInput("输出路径（回车自动生成，- 表示输出到屏幕）", null, out string outputPath))
            {
                return;
            }

            bool stats = Ask("显示统计与图表", false);

            var options = new CliOptions
            {
                Command = CliCommand.Hash,
                Algorithms = algorithms,
                Recursive = recursive,
                Threads = threads,
                MinSize = minSize,
                MaxSize = maxSize,
                Format = format,
                Stats = stats
            };

            options.Inputs.AddRange(inputs);
            if (outputPath.Length > 0) options.OutputPath = outputPath;

            ShowPreview(BuildHashCommandLine(options));

            if (!Ask("开始计算", true))
            {
                WriteInfo(L.T("已取消"));
                return;
            }

            Program.ApplyUi(options);
            int exitCode = await Program.RunHashAsync(options);
            ShowResult(exitCode);
        }

        /// <summary>
        /// 验证哈希向导
        /// </summary>
        private static async Task RunVerifyWizardAsync()
        {
            WriteStep(L.T("验证哈希"));

            string jsonPath = string.Empty;

            while (jsonPath.Length == 0)
            {
                if (!TryReadInput("要验证的 JSON 文件（回车从当前目录列表中选择）", null, out string input))
                {
                    return;
                }

                if (input.Length == 0)
                {
                    string? picked = PickFromLocalJsonFiles();
                    if (picked == null)
                    {
                        return;
                    }

                    if (picked.Length == 0)
                    {
                        continue;
                    }

                    jsonPath = picked;
                    break;
                }

                if (!File.Exists(input))
                {
                    WriteError(string.Format(L.T("文件不存在: {0}"), input));
                    continue;
                }

                jsonPath = Path.GetFullPath(input);
            }

            if (!TryReadInput("路径过滤（回车表示验证全部记录，多个用空格分隔）", null, out string rawFilters))
            {
                return;
            }

            OutputFormat format = AskFormat();

            if (!TryReadInput("输出路径（回车自动生成，- 表示输出到屏幕）", null, out string outputPath))
            {
                return;
            }

            var options = new CliOptions
            {
                Command = CliCommand.Verify,
                VerifyJson = jsonPath,
                Format = format
            };

            foreach (string filter in SplitArguments(rawFilters))
            {
                options.Inputs.Add(filter);
            }

            if (outputPath.Length > 0) options.OutputPath = outputPath;

            ShowPreview(BuildVerifyCommandLine(options));

            if (!Ask("开始验证", true))
            {
                WriteInfo(L.T("已取消"));
                return;
            }

            Program.ApplyUi(options);
            int exitCode = await Program.RunVerifyAsync(options);
            ShowResult(exitCode);
        }

        /// <summary>
        /// 从当前目录的 JSON 文件中选择一个
        /// </summary>
        /// <returns>选中路径；返回空字符串表示重新输入；返回 null 表示结束会话</returns>
        private static string? PickFromLocalJsonFiles()
        {
            List<string> candidates = FindCandidateJsonFiles();

            if (candidates.Count == 0)
            {
                WriteError(L.T("当前目录下没有 JSON 文件，请直接输入完整路径"));
                return string.Empty;
            }

            Console.WriteLine(L.T("当前目录下的 JSON 文件:"));
            for (int i = 0; i < candidates.Count; i++)
            {
                Console.WriteLine(string.Format("  {0}. {1}  ({2})",
                    i + 1,
                    Path.GetFileName(candidates[i]),
                    File.GetLastWriteTime(candidates[i]).ToString("yyyy-MM-dd HH:mm")));
            }

            if (!TryReadInput("输入序号或文件名", "1", out string pick))
            {
                return null;
            }

            if (int.TryParse(pick, out int index) && index >= 1 && index <= candidates.Count)
            {
                return candidates[index - 1];
            }

            if (File.Exists(pick))
            {
                return Path.GetFullPath(pick);
            }

            WriteError(string.Format(L.T("无效的选择: {0}"), pick));
            return string.Empty;
        }

        /// <summary>
        /// 当前目录下最近的 JSON 文件
        /// </summary>
        private static List<string> FindCandidateJsonFiles()
        {
            try
            {
                return Directory.EnumerateFiles(Environment.CurrentDirectory, "*.json")
                    .OrderByDescending(File.GetLastWriteTime)
                    .Take(10)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// 输出格式选择
        /// </summary>
        private static OutputFormat AskFormat()
        {
            Console.WriteLine(L.T("输出格式:  1) json（默认）  2) text  3) ndjson"));

            if (!TryReadInput("选择格式", "1", out string choice))
            {
                return OutputFormat.Json;
            }

            switch (choice.ToLowerInvariant())
            {
                case "2":
                case "text":
                    return OutputFormat.Text;
                case "3":
                case "ndjson":
                    return OutputFormat.Ndjson;
                default:
                    return OutputFormat.Json;
            }
        }

        /// <summary>
        /// 打印会话头部
        /// </summary>
        private static void PrintHeader()
        {
            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine(string.Format(L.T("  FastHasher v{0}  交互式模式"), VersionInfo.GetVersion()));
            Console.WriteLine(L.T("  输入 6 或 q 退出；Ctrl+C 可中止正在执行的任务"));
            Console.WriteLine("==================================================");
        }

        /// <summary>
        /// 打印主菜单。
        /// 「切换语言」一项在两种语言下都保留中文提示，保证用户切到看不懂的语言后仍能找回入口。
        /// </summary>
        private static void PrintMenu()
        {
            Console.WriteLine();
            Console.WriteLine(L.T("请选择操作:"));
            Console.WriteLine(L.T("  1. 计算哈希"));
            Console.WriteLine(L.T("  2. 验证哈希"));
            Console.WriteLine(L.T("  3. 基准测试"));
            Console.WriteLine(L.T("  4. 查看帮助"));
            Console.WriteLine(L.IsEnglish ? "  5. 切换语言 / Switch language" : "  5. 切换语言 / Language");
            Console.WriteLine(L.T("  6. 退出"));
        }

        /// <summary>
        /// 切换中文/英文并写入 settings.json，下次启动保持所选语言。
        /// 切换后立即生效（后续所有 T() 走新词典），无需重启。
        /// </summary>
        private static void ToggleLanguage()
        {
            L.ToggleAndPersist();
            Console.WriteLine();
            Console.WriteLine(L.IsEnglish
                ? "Language switched to English. Saved to settings.json (choose 5 again to switch back)."
                : "语言已切换为中文。已保存到 settings.json（再次选择 5 可切回英文）。");
        }

        /// <summary>
        /// 打印步骤标题（标题在边界统一翻译）
        /// </summary>
        private static void WriteStep(string rawTitle)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format("--- {0} ---", L.T(rawTitle)));
        }

        /// <summary>
        /// 打印等价命令行
        /// </summary>
        private static void ShowPreview(string commandLine)
        {
            Console.WriteLine();
            Console.WriteLine(L.T("等价命令行:"));
            Console.WriteLine("  " + commandLine);
            Console.WriteLine();
        }

        /// <summary>
        /// 打印执行结果
        /// </summary>
        private static void ShowResult(int exitCode)
        {
            Console.WriteLine();

            switch (exitCode)
            {
                case 0:
                    WriteInfo(L.T("执行完成：全部成功"));
                    break;
                case 130:
                    WriteInfo(L.T("执行已取消"));
                    break;
                default:
                    WriteInfo(string.Format(L.T("执行完成：存在问题项（退出码 {0}），详见输出报告"), exitCode));
                    break;
            }

            Pause();
        }

        /// <summary>
        /// 暂停并等待回车，返回主菜单
        /// </summary>
        private static void Pause()
        {
            if (endOfInput) return;

            Console.WriteLine();
            Console.Write(L.T("按回车返回主菜单..."));
            if (Console.ReadLine() == null)
            {
                endOfInput = true;
            }
        }

        /// <summary>
        /// 读取一行输入
        /// </summary>
        /// <param name="prompt">提示文字（中文原文，在此统一翻译，调用点不必包 L.T）</param>
        /// <param name="defaultValue">回车时采用的默认值，null 表示无默认值</param>
        /// <param name="value">读取结果</param>
        /// <returns>返回 false 表示输入流结束</returns>
        private static bool TryReadInput(string prompt, string? defaultValue, out string value)
        {
            value = string.Empty;

            if (endOfInput) return false;

            Console.Write(L.T(prompt));
            if (!string.IsNullOrEmpty(defaultValue)) Console.Write(string.Format(" [{0}]", defaultValue));
            Console.Write(": ");

            string? line = Console.ReadLine();
            if (line == null)
            {
                endOfInput = true;
                Console.WriteLine();
                return false;
            }

            // 脚本重定向输入时首行可能带 BOM，需先剔除
            line = line.TrimStart('\uFEFF').Trim();
            value = line.Length == 0 ? (defaultValue ?? string.Empty) : line;
            return true;
        }

        /// <summary>
        /// 询问是/否（问题文字在此统一翻译；中英文模式都接受 y/yes/是）
        /// </summary>
        private static bool Ask(string question, bool defaultValue)
        {
            if (endOfInput) return false;

            Console.Write(string.Format("{0} [{1}]: ", L.T(question), defaultValue ? "Y/n" : "y/N"));

            string? line = Console.ReadLine();
            if (line == null)
            {
                endOfInput = true;
                Console.WriteLine();
                return false;
            }

            line = line.TrimStart('\uFEFF').Trim().ToLowerInvariant();
            if (line.Length == 0) return defaultValue;

            return line == "y" || line == "yes" || line == "是" || line == "1";
        }

        /// <summary>
        /// 按空白拆分输入，支持用双引号包裹含空格的路径（拖放文件到窗口时常见）
        /// </summary>
        private static List<string> SplitArguments(string raw)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (char ch in raw)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        /// <summary>
        /// 生成计算模式的等价命令行
        /// </summary>
        private static string BuildHashCommandLine(CliOptions options)
        {
            var sb = new StringBuilder("FastHasher");

            foreach (string input in options.Inputs)
            {
                sb.Append(' ').Append(Quote(input));
            }

            sb.Append(" -a ").Append(string.Join(",", options.Algorithms.Select(a => a.ToLowerInvariant())));

            if (options.Recursive) sb.Append(" -r");

            if (options.Threads == 1)
            {
                sb.Append(" -j 1");
            }
            else if (options.Threads != Environment.ProcessorCount)
            {
                sb.Append(" -j ").Append(options.Threads);
            }

            if (options.MinSize > 0) sb.Append(" --min-size ").Append(options.MinSize);
            if (options.MaxSize < long.MaxValue) sb.Append(" --max-size ").Append(options.MaxSize);

            if (options.Format != OutputFormat.Json)
            {
                sb.Append(" --format ").Append(options.Format.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrEmpty(options.OutputPath))
            {
                sb.Append(" -o ").Append(Quote(options.OutputPath!));
            }

            if (options.Stats) sb.Append(" --stats");

            return sb.ToString();
        }

        /// <summary>
        /// 生成验证模式的等价命令行
        /// </summary>
        private static string BuildVerifyCommandLine(CliOptions options)
        {
            var sb = new StringBuilder("FastHasher verify ");
            sb.Append(Quote(options.VerifyJson ?? string.Empty));

            foreach (string filter in options.Inputs)
            {
                sb.Append(' ').Append(Quote(filter));
            }

            if (options.Format != OutputFormat.Json)
            {
                sb.Append(" --format ").Append(options.Format.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrEmpty(options.OutputPath))
            {
                sb.Append(" -o ").Append(Quote(options.OutputPath!));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 需要时给参数加引号
        /// </summary>
        private static string Quote(string value)
        {
            if (value.Length == 0) return "\"\"";
            return value.IndexOf(' ') >= 0 || value.IndexOf('"') >= 0
                ? "\"" + value + "\""
                : value;
        }

        private static void WriteInfo(string message)
        {
            Console.WriteLine(message);
        }

        private static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(L.T("错误: ") + message);
            Console.ResetColor();
        }
    }
}
