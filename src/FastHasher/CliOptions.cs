// CliOptions.cs - 命令行参数模型与解析器
// 统一管理 FastHasher 的全部命令行参数：解析、校验、迁移提示
// 所有功能模块只依赖 CliOptions，不再各自解析字符串参数
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FastHasher
{
    /// <summary>
    /// 可用的子命令
    /// </summary>
    public enum CliCommand
    {
        /// <summary>计算哈希（默认命令）</summary>
        Hash,
        /// <summary>验证哈希</summary>
        Verify,
        /// <summary>基准测试</summary>
        Bench,
        /// <summary>交互式向导</summary>
        Interactive,
        /// <summary>显示帮助</summary>
        Help,
        /// <summary>显示版本</summary>
        Version
    }

    /// <summary>
    /// 结果输出格式
    /// </summary>
    public enum OutputFormat
    {
        /// <summary>单个 JSON 对象（metadata + files）</summary>
        Json,
        /// <summary>人类可读的文本</summary>
        Text,
        /// <summary>每行一个 JSON 对象</summary>
        Ndjson
    }

    /// <summary>
    /// 进度显示策略
    /// </summary>
    public enum ProgressMode
    {
        /// <summary>输出被重定向时自动关闭</summary>
        Auto,
        /// <summary>始终显示</summary>
        Always,
        /// <summary>从不显示</summary>
        Never
    }

    /// <summary>
    /// 控制台输出详细程度
    /// </summary>
    public enum VerbosityLevel
    {
        /// <summary>只输出错误与警告</summary>
        Quiet,
        /// <summary>默认：进度、统计、逐文件结果</summary>
        Normal,
        /// <summary>额外输出调试日志</summary>
        Verbose
    }

    /// <summary>
    /// 解析后的命令行参数集合
    /// </summary>
    public sealed class CliOptions
    {
        /// <summary>当前子命令</summary>
        public CliCommand Command { get; set; } = CliCommand.Hash;

        /// <summary>位置参数：hash 模式为文件/模式列表，verify 模式为路径过滤条件</summary>
        public List<string> Inputs { get; } = new List<string>();

        /// <summary>要计算的哈希算法（已标准化为大写）</summary>
        public List<string> Algorithms { get; set; } = new List<string> { "MD5" };

        /// <summary>是否递归搜索子目录</summary>
        public bool Recursive { get; set; }

        /// <summary>最小文件大小（字节）</summary>
        public long MinSize { get; set; }

        /// <summary>最大文件大小（字节）</summary>
        public long MaxSize { get; set; } = long.MaxValue;

        /// <summary>并行线程数，1 表示顺序处理</summary>
        public int Threads { get; set; } = Environment.ProcessorCount;

        /// <summary>输出路径，null 表示默认文件名，"-" 表示标准输出</summary>
        public string? OutputPath { get; set; }

        /// <summary>输出格式</summary>
        public OutputFormat Format { get; set; } = OutputFormat.Json;

        /// <summary>是否显示统计信息与图表</summary>
        public bool Stats { get; set; }

        /// <summary>控制台输出详细程度</summary>
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.Normal;

        /// <summary>是否禁用彩色输出</summary>
        public bool NoColor { get; set; }

        /// <summary>进度显示策略</summary>
        public ProgressMode Progress { get; set; } = ProgressMode.Auto;

        /// <summary>日志文件路径，null 表示自动生成</summary>
        public string? LogFile { get; set; }

        /// <summary>是否禁用日志文件</summary>
        public bool NoLog { get; set; }

        /// <summary>verify 模式要验证的 JSON 文件</summary>
        public string? VerifyJson { get; set; }

        /// <summary>是否输出到标准输出</summary>
        public bool OutputToStdout => OutputPath == "-";

        /// <summary>是否启用并行处理（线程数大于 1）</summary>
        public bool IsParallel => Threads > 1;
    }

    /// <summary>
    /// 命令行解析结果
    /// </summary>
    public sealed class CliParseResult
    {
        /// <summary>解析成功时的参数对象</summary>
        public CliOptions? Options { get; private set; }

        /// <summary>解析失败时的错误信息</summary>
        public string? Error { get; private set; }

        /// <summary>解析过程中产生的提示（例如线程数被限制）</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>解析是否成功</summary>
        public bool Success => Error == null;

        /// <summary>参数错误统一使用退出码 2</summary>
        public int ExitCode => Success ? 0 : 2;

        internal static CliParseResult Ok(CliOptions options, List<string> warnings)
        {
            var result = new CliParseResult { Options = options };
            result.Warnings.AddRange(warnings);
            return result;
        }

        internal static CliParseResult Fail(string error) => new CliParseResult { Error = error };
    }

    /// <summary>
    /// 命令行解析器
    /// 负责：响应文件展开、子命令识别、选项解析、参数校验、废弃参数提示
    /// </summary>
    public static class CliParser
    {
        /// <summary>支持的哈希算法</summary>
        public static readonly string[] SupportedAlgorithms = { "MD5", "SHA1", "SHA256", "SHA512", "BLAKE3" };

        /// <summary>线程数上限倍数（相对 CPU 逻辑线程数）</summary>
        public const int ThreadLimitFactor = 2;

        /// <summary>
        /// 已废弃参数及其替代写法。替代写法为空表示该参数已无作用，直接删除即可。
        /// </summary>
        private static readonly (string Name, string Replacement)[] DeprecatedOptions =
        {
            ("-p", "-j <n>"),
            ("--parallel", "-j <n>"),
            ("-seq", "-j 1"),
            ("--sequential", "-j 1"),
            ("-c", "-o -"),
            ("--console-output", "-o -"),
            ("--json-file", "verify <json>"),
            ("--show-time", "--stats"),
            ("-b", "bench"),
            ("--benchmark", "bench"),
            ("--batch-size", ""),
        };

        /// <summary>用于拼写建议的已知选项</summary>
        private static readonly string[] KnownOptions =
        {
            "--algorithms", "--recursive", "--min-size", "--max-size", "--file-list",
            "--threads", "--output", "--format", "--stats", "--quiet", "--verbose",
            "--no-color", "--progress", "--log", "--no-log", "--interactive", "--help", "--version"
        };

        /// <summary>
        /// 大小单位表，按后缀长度降序排列以优先匹配 KiB 之类的双字符单位
        /// </summary>
        private static readonly (string Suffix, long Factor)[] SizeUnits =
        {
            ("KIB", 1024L),
            ("MIB", 1024L * 1024),
            ("GIB", 1024L * 1024 * 1024),
            ("TIB", 1024L * 1024 * 1024 * 1024),
            ("PIB", 1024L * 1024 * 1024 * 1024 * 1024),
            ("KB", 1024L),
            ("MB", 1024L * 1024),
            ("GB", 1024L * 1024 * 1024),
            ("TB", 1024L * 1024 * 1024 * 1024),
            ("PB", 1024L * 1024 * 1024 * 1024 * 1024),
            ("K", 1024L),
            ("M", 1024L * 1024),
            ("G", 1024L * 1024 * 1024),
            ("T", 1024L * 1024 * 1024 * 1024),
            ("P", 1024L * 1024 * 1024 * 1024 * 1024),
            ("B", 1L),
        };

        /// <summary>
        /// 解析命令行参数
        /// </summary>
        /// <param name="rawArgs">原始命令行参数</param>
        /// <returns>解析结果，失败时 Error 非空且退出码为 2</returns>
        public static CliParseResult Parse(string[] rawArgs)
        {
            if (!TryExpandResponseFiles(rawArgs, out var args, out var expandError))
            {
                return CliParseResult.Fail(expandError!);
            }

            var options = new CliOptions();
            var warnings = new List<string>();
            var command = CliCommand.Hash;
            bool endOfOptions = false;
            int index = 0;

            // 子命令：仅当第一个 token 精确等于命令名时才视为命令
            if (args.Count > 0 && !args[0].StartsWith("-"))
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "hash": command = CliCommand.Hash; index = 1; break;
                    case "verify": command = CliCommand.Verify; index = 1; break;
                    case "bench": command = CliCommand.Bench; index = 1; break;
                    case "interactive":
                    case "ui": command = CliCommand.Interactive; index = 1; break;
                    case "help": command = CliCommand.Help; index = 1; break;
                    case "version": command = CliCommand.Version; index = 1; break;
                }
            }

            for (; index < args.Count; index++)
            {
                string token = args[index];

                // 位置参数（含 "--" 之后的全部内容）
                if (endOfOptions || !token.StartsWith("-") || token == "-")
                {
                    options.Inputs.Add(token);
                    continue;
                }

                // "--" 终止选项解析
                if (token == "--")
                {
                    endOfOptions = true;
                    continue;
                }

                // 拆分 --name=value 形式
                string name = token;
                string? inlineValue = null;
                int equalIndex = token.IndexOf('=');
                if (token.StartsWith("--") && equalIndex > 2)
                {
                    name = token.Substring(0, equalIndex);
                    inlineValue = token.Substring(equalIndex + 1);
                }

                string lowerName = name.ToLowerInvariant();

                // 废弃参数：给出明确迁移提示后退出
                if (TryGetDeprecatedReplacement(lowerName, out string replacement))
                {
                    return CliParseResult.Fail(BuildDeprecatedMessage(name, replacement));
                }

                // 读取选项值（支持 --name=value 与 --name value）
                string? ReadValue()
                {
                    if (inlineValue != null) return inlineValue;
                    if (index + 1 >= args.Count) return null;
                    index++;
                    return args[index];
                }

                switch (lowerName)
                {
                    case "-h":
                    case "--help":
                        command = CliCommand.Help;
                        break;

                    case "-v":
                    case "--version":
                        command = CliCommand.Version;
                        break;

                    case "-i":
                    case "--interactive":
                        command = CliCommand.Interactive;
                        break;

                    case "-a":
                    case "--algorithms":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个算法列表，例如: --algorithms md5,sha256");
                        if (!TryParseAlgorithms(value, out var algorithms, out string? algoError))
                        {
                            return CliParseResult.Fail(algoError!);
                        }
                        options.Algorithms = algorithms;
                        break;
                    }

                    case "-r":
                    case "--recursive":
                        options.Recursive = true;
                        break;

                    case "--min-size":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个文件大小，例如: --min-size 1MB");
                        if (!TryParseSize(value, out long bytes))
                        {
                            return CliParseResult.Fail($"无法解析文件大小: {value}（示例: 1KB, 10MB, 0.5GiB）");
                        }
                        options.MinSize = bytes;
                        break;
                    }

                    case "--max-size":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个文件大小，例如: --max-size 100MB");
                        if (!TryParseSize(value, out long bytes))
                        {
                            return CliParseResult.Fail($"无法解析文件大小: {value}（示例: 1KB, 10MB, 0.5GiB）");
                        }
                        options.MaxSize = bytes;
                        break;
                    }

                    case "-f":
                    case "--file-list":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个文件路径，使用 - 表示从标准输入读取");
                        if (!TryLoadFileList(value, options, out string? listError))
                        {
                            return CliParseResult.Fail(listError!);
                        }
                        break;
                    }

                    case "-j":
                    case "--threads":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个线程数，例如: --threads 8、--threads auto、--threads 1");
                        if (!TryParseThreads(value, out int threads, out string? threadError, out string? threadWarning))
                        {
                            return CliParseResult.Fail(threadError!);
                        }
                        if (threadWarning != null) warnings.Add(threadWarning);
                        options.Threads = threads;
                        break;
                    }

                    case "-o":
                    case "--output":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个输出路径，使用 - 表示输出到标准输出");
                        options.OutputPath = value;
                        break;
                    }

                    case "--format":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个格式: json、text 或 ndjson");
                        switch (value.ToLowerInvariant())
                        {
                            case "json": options.Format = OutputFormat.Json; break;
                            case "text": options.Format = OutputFormat.Text; break;
                            case "ndjson": options.Format = OutputFormat.Ndjson; break;
                            default: return CliParseResult.Fail($"不支持的输出格式: {value}（可用: json, text, ndjson）");
                        }
                        break;
                    }

                    case "--stats":
                        options.Stats = true;
                        break;

                    case "-q":
                    case "--quiet":
                        options.Verbosity = VerbosityLevel.Quiet;
                        break;

                    case "--verbose":
                        options.Verbosity = VerbosityLevel.Verbose;
                        break;

                    case "--no-color":
                        options.NoColor = true;
                        break;

                    case "--progress":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个值: auto、always 或 never");
                        switch (value.ToLowerInvariant())
                        {
                            case "auto": options.Progress = ProgressMode.Auto; break;
                            case "always": options.Progress = ProgressMode.Always; break;
                            case "never": options.Progress = ProgressMode.Never; break;
                            default: return CliParseResult.Fail($"不支持的值: {value}（可用: auto, always, never）");
                        }
                        break;
                    }

                    case "--log":
                    {
                        string? value = ReadValue();
                        if (value == null) return CliParseResult.Fail($"{name} 需要一个日志文件路径");
                        options.LogFile = value;
                        options.NoLog = false;
                        break;
                    }

                    case "--no-log":
                        options.NoLog = true;
                        options.LogFile = null;
                        break;

                    default:
                    {
                        string message = $"未知参数: {name}";
                        string? suggestion = SuggestOption(lowerName);
                        if (suggestion != null) message += $"，是否想用: {suggestion}？";
                        message += "\n使用 FastHasher -h 查看全部参数";
                        return CliParseResult.Fail(message);
                    }
                }
            }

            // 统一的语义校验
            if (options.MinSize > options.MaxSize)
            {
                return CliParseResult.Fail($"--min-size ({options.MinSize} 字节) 不能大于 --max-size ({options.MaxSize} 字节)");
            }

            if (command == CliCommand.Verify)
            {
                if (options.Inputs.Count == 0)
                {
                    return CliParseResult.Fail("verify 需要指定要验证的 JSON 文件\n用法: FastHasher verify <hash.json> [路径过滤...]");
                }

                options.VerifyJson = options.Inputs[0];
                options.Inputs.RemoveAt(0);

                if (!File.Exists(options.VerifyJson))
                {
                    return CliParseResult.Fail($"验证文件不存在: {options.VerifyJson}");
                }
            }
            else if (command == CliCommand.Hash && options.Inputs.Count == 0)
            {
                return CliParseResult.Fail("缺少输入路径\n用法: FastHasher [hash] <文件|目录|通配符>... [选项]");
            }

            options.Command = command;
            return CliParseResult.Ok(options, warnings);
        }

        /// <summary>
        /// 展开 @响应文件：把文件中的每一行作为独立参数
        /// </summary>
        private static bool TryExpandResponseFiles(string[] rawArgs, out List<string> expanded, out string? error)
        {
            expanded = new List<string>();
            error = null;

            foreach (string arg in rawArgs)
            {
                if (arg.Length > 1 && arg[0] == '@')
                {
                    string path = arg.Substring(1);
                    if (!File.Exists(path))
                    {
                        error = $"响应文件不存在: {path}";
                        return false;
                    }

                    try
                    {
                        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                        {
                            string item = line.Trim();
                            if (item.Length == 0 || item.StartsWith("#")) continue;
                            expanded.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        error = $"读取响应文件失败 {path}: {ex.Message}";
                        return false;
                    }
                }
                else
                {
                    expanded.Add(arg);
                }
            }

            return true;
        }

        /// <summary>
        /// 从文件（或标准输入）读取文件列表，追加到位置参数
        /// </summary>
        private static bool TryLoadFileList(string path, CliOptions options, out string? error)
        {
            error = null;

            try
            {
                if (path == "-")
                {
                    foreach (string line in Console.In.ReadToEnd().Split('\n'))
                    {
                        string item = line.Trim();
                        if (item.Length > 0 && !item.StartsWith("#")) options.Inputs.Add(item);
                    }
                    return true;
                }

                if (!File.Exists(path))
                {
                    error = $"文件列表不存在: {path}";
                    return false;
                }

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string item = line.Trim();
                    if (item.Length > 0 && !item.StartsWith("#")) options.Inputs.Add(item);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"读取文件列表失败 {path}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 解析算法列表，支持 all 展开（交互式模式复用同一套校验）
        /// </summary>
        public static bool TryParseAlgorithms(string raw, out List<string> algorithms, out string? error)
        {
            algorithms = new List<string>();
            error = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = part.Trim().ToUpperInvariant().Replace("-", string.Empty);

                if (normalized == "ALL")
                {
                    foreach (string algo in SupportedAlgorithms)
                    {
                        if (seen.Add(algo)) algorithms.Add(algo);
                    }
                    continue;
                }

                if (Array.IndexOf(SupportedAlgorithms, normalized) < 0)
                {
                    error = $"不支持的算法: {part}（可用: {string.Join(", ", SupportedAlgorithms)} 或 all）";
                    return false;
                }

                if (seen.Add(normalized)) algorithms.Add(normalized);
            }

            if (algorithms.Count == 0)
            {
                error = "算法列表不能为空";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解析带单位的大小，支持 B/KB/KiB/MB/MiB/GB/GiB/TB/TiB 与小数（交互式模式复用同一套校验）
        /// </summary>
        public static bool TryParseSize(string raw, out long bytes)
        {
            bytes = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string text = raw.Trim().ToUpperInvariant().Replace(" ", string.Empty);

            // 纯数字视为字节
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long plain))
            {
                if (plain < 0) return false;
                bytes = plain;
                return true;
            }

            foreach (var unit in SizeUnits)
            {
                if (!text.EndsWith(unit.Suffix, StringComparison.Ordinal)) continue;

                string numberPart = text.Substring(0, text.Length - unit.Suffix.Length);
                if (!decimal.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
                {
                    continue;
                }

                if (number < 0) return false;

                decimal total = number * unit.Factor;
                if (total > long.MaxValue) return false;

                bytes = (long)total;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 解析线程数：正整数或 auto，上限为 CPU 逻辑线程数 × 2
        /// </summary>
        private static bool TryParseThreads(string raw, out int threads, out string? error, out string? warning)
        {
            threads = 0;
            error = null;
            warning = null;

            if (raw.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                threads = Environment.ProcessorCount;
                return true;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                error = $"线程数必须是正整数或 auto: {raw}";
                return false;
            }

            int upper = Environment.ProcessorCount * ThreadLimitFactor;
            if (value > upper)
            {
                warning = $"线程数 {value} 超过上限，已调整为 {upper}（CPU 逻辑线程数 × {ThreadLimitFactor}）";
                threads = upper;
                return true;
            }

            threads = value;
            return true;
        }

        private static bool TryGetDeprecatedReplacement(string lowerName, out string replacement)
        {
            foreach (var item in DeprecatedOptions)
            {
                if (item.Name == lowerName)
                {
                    replacement = item.Replacement;
                    return true;
                }
            }

            replacement = string.Empty;
            return false;
        }

        private static string BuildDeprecatedMessage(string name, string replacement)
        {
            if (string.IsNullOrEmpty(replacement))
            {
                return $"参数 {name} 已移除（该参数从未生效），请直接删除。\n使用 FastHasher -h 查看全部参数";
            }

            return $"参数 {name} 已废弃，请改用: {replacement}\n使用 FastHasher -h 查看全部参数";
        }

        /// <summary>
        /// 为未知选项给出拼写建议
        /// </summary>
        private static string? SuggestOption(string name)
        {
            string? best = null;
            int bestDistance = int.MaxValue;

            foreach (string candidate in KnownOptions)
            {
                int distance = LevenshteinDistance(name, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return bestDistance <= 3 ? best : null;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            int[] previous = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }
    }
}
