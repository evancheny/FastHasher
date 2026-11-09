// Localization.cs - 极简双语文案层（中文 / English）
//
// 设计要点（重要，勿改动，避免重蹈覆辙）：
//
// 1. 以【中文字符串本身】作为词典 key：T("中文") 在中文模式下原样返回；
//    英文模式下查 En 词典，查不到时回退原中文。
//    好处：新增文案时即使漏翻译也不会崩或显示空白，只会暂时显示中文；
//    坏处：key 与调用点字面量必须逐字一致（含空格与标点），改动文案时两处都要改。
//    因此有一个配套审计脚本要求：所有 L.T("...") 的 key 必须能在 En 词典中找到
//    （zh 模式下虽不查表，但审计仍要求 key 在词典中，防止英文模式下回退成中文）。
//
// 2. 不要尝试用 CultureInfo / CurrentUICulture 自动检测系统语言：
//    csproj 开启了 InvariantGlobalization（为减小体积移除了 ICU 数据），
//    文化信息不可靠。语言只有一个来源：--lang 参数 > 配置文件 > 默认 zh。
//
// 3. 配置文件 settings.json 存放用户在交互式菜单里选择的语言：
//    - Windows: %APPDATA%\FastHasher\settings.json
//    - Linux:   ~/.config/FastHasher/settings.json
//    （.NET 中 SpecialFolder.ApplicationData 在 Linux 上即 ~/.config，一条代码路径即可）
//    文件损坏/不存在时静默忽略，回到默认中文。
//
// 4. 含 {0} 占位符的文案必须【先 T() 再 string.Format】：
//    string.Format(L.T("处理进度: {0}/{1}"), a, b)
//    顺序反了会导致格式化后的字符串无法命中词典。
//    内插字符串 $"...{expr}..." 无法查表，必须改写成 string.Format 形式。
//
// 5. VersionInfo.InformationalVersion 是程序集元数据（也出现在 --version 输出），
//    保持中文不翻译，保证跨语言/跨版本的元数据一致。
//
// 6. T() 必须极廉价：进度行每 200ms 刷新一次也会调用。当前实现为一次
//    字符串相等比较 + 词典查找，可放心在热路径使用。

using System.Text.Json;

namespace FastHasher
{
    /// <summary>
    /// 双语文案访问入口。所有用户可见文案统一通过 L.T() 取出。
    /// </summary>
    internal static class L
    {
        /// <summary>当前是否英文模式</summary>
        public static bool IsEnglish => current == "en";

        private static string current = "zh";

        /// <summary>
        /// 翻译一条中文文案。中文模式下原样返回；英文模式下查词典，缺失时回退中文。
        /// </summary>
        public static string T(string zh)
        {
            if (!IsEnglish) return zh;
            return En.TryGetValue(zh, out string? en) ? en : zh;
        }

        /// <summary>
        /// 程序启动时调用：--lang 参数优先，其次配置文件，最后默认中文。
        /// 必须在任何控制台输出之前调用，否则参数错误提示会用错语言。
        /// </summary>
        /// <param name="cliLang">命令行 --lang 解析结果（"zh"/"en"），null 表示未指定</param>
        public static void Initialize(string? cliLang)
        {
            if (cliLang != null)
            {
                current = cliLang;
                return;
            }

            string? saved = LoadConfiguredLanguage();
            if (saved != null) current = saved;
        }

        /// <summary>
        /// 切换语言并持久化到 settings.json（交互式菜单的「切换语言」项调用）。
        /// 写配置失败（只读环境等）不影响本次会话，仅下次启动回退。
        /// </summary>
        public static void ToggleAndPersist()
        {
            current = IsEnglish ? "zh" : "en";

            try
            {
                string path = GetConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // 重要：这里必须用 Dictionary<string,string>，不能用匿名类型 new { language = ... }。
                // 匿名类型依赖反射序列化，在 R2R/裁剪发布下会抛
                // "The deserialization constructor ... contains parameters with null names"
                // （构造函数参数名被 ILLink 裁剪），导致配置静默写不出去。
                // 同类坑：任何「运行时动态类型 + JsonSerializer」都要走源生成序列化或字典。
                var payload = new Dictionary<string, string> { ["language"] = current };
                string json = JsonSerializer.Serialize(payload,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // 配置目录不可写等情况：本次会话仍生效，但要让用户知道没有持久化
                Console.Error.WriteLine(L.T("警告: 保存语言设置失败，仅本次会话生效: ") + ex.Message);
            }
        }

        /// <summary>
        /// 归一化语言取值：zh/zh-* 视为中文，en/en-* 视为英文，其余非法返回 null。
        /// </summary>
        public static string? NormalizeLanguage(string value)
        {
            string v = value.Trim().ToLowerInvariant();
            if (v.StartsWith("zh")) return "zh";
            if (v.StartsWith("en")) return "en";
            return null;
        }

        private static string GetConfigPath()
        {
            // Linux 上 ApplicationData 即 ~/.config（XDG 约定），与 Windows 的 %APPDATA% 对应。
            // 坑：某些非交互环境（如 ssh -tt + 管道输入的会话）GetFolderPath 会返回空串，
            // Path.Combine(空, ...) 会得到相对路径，配置文件就落到当前目录去了。
            // 因此必须判空并回退到 $HOME，再不行用当前目录，保证路径永远是绝对路径。
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(baseDir))
            {
                string? home = Environment.GetEnvironmentVariable("HOME");
                baseDir = string.IsNullOrEmpty(home)
                    ? Environment.CurrentDirectory
                    : Path.Combine(home, ".config");
            }

            return Path.Combine(baseDir, "FastHasher", "settings.json");
        }

        private static string? LoadConfiguredLanguage()
        {
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path)) return null;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("language", out JsonElement lang))
                {
                    return NormalizeLanguage(lang.GetString() ?? string.Empty);
                }
            }
            catch
            {
                // 配置文件损坏/不可读时按无配置处理
            }

            return null;
        }

        /// <summary>
        /// 中文 → 英文词典。key 必须与调用点的中文字面量逐字一致（含空格、标点、{0} 占位符）。
        /// 本文件是唯一允许出现「成对中英文文案」的地方；其余代码文件只写中文原文。
        /// 审计脚本会校验：所有 L.T("...") key 与翻译边界参数字面量都必须出现在本词典中。
        /// </summary>
        private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
        {
            // ===== 交互式向导 =====
            ["  1. 计算哈希"] = "  1. Hash files",
            ["  2. 验证哈希"] = "  2. Verify hashes",
            ["  3. 基准测试"] = "  3. Benchmark",
            ["  4. 查看帮助"] = "  4. Help",
            ["  6. 退出"] = "  6. Quit",
            ["请选择操作:"] = "Select an action:",
            ["请选择"] = "Select",
            ["请输入 1-6 之间的数字，或输入 q 退出"] = "Enter a number 1-6, or q to quit",
            ["  FastHasher v{0}  交互式模式"] = "  FastHasher v{0}  Interactive Mode",
            ["  输入 6 或 q 退出；Ctrl+C 可中止正在执行的任务"] = "  Enter 6 or q to quit; Ctrl+C aborts the running task",
            ["再见。"] = "Goodbye.",
            ["按回车返回主菜单..."] = "Press Enter to return to the menu...",
            ["等价命令行:"] = "Equivalent command line:",
            ["已取消"] = "Cancelled",
            ["执行完成：全部成功"] = "Done: all succeeded",
            ["执行已取消"] = "Cancelled by the user",
            ["执行完成：存在问题项（退出码 {0}），详见输出报告"] = "Done with problems (exit code {0}); see the output report",

            // ===== 向导问答 =====
            ["路径（文件/目录/通配符，多个用空格分隔）"] = "Path(s) (file/dir/glob, space separated)",
            ["路径不能为空"] = "Path cannot be empty",
            ["算法（md5/sha1/sha256/sha512/blake3/all，逗号分隔）"] = "Algorithms (md5/sha1/sha256/sha512/blake3/all, comma separated)",
            ["递归搜索子目录"] = "Recurse into subdirectories",
            ["并行线程数（1 表示顺序处理）"] = "Worker threads (1 = sequential)",
            ["最小文件大小（回车跳过，例如 1KB）"] = "Minimum file size (Enter to skip, e.g. 1KB)",
            ["最大文件大小（回车跳过）"] = "Maximum file size (Enter to skip)",
            ["最小文件大小不能大于最大文件大小"] = "Minimum size cannot be greater than maximum size",
            ["输出路径（回车自动生成，- 表示输出到屏幕）"] = "Output path (Enter to auto-generate, - for screen)",
            ["显示统计与图表"] = "Show statistics and charts",
            ["开始计算"] = "Start hashing",
            ["开始验证"] = "Start verification",
            ["要验证的 JSON 文件（回车从当前目录列表中选择）"] = "JSON file to verify (Enter to pick from the list in the current directory)",
            ["路径过滤（回车表示验证全部记录，多个用空格分隔）"] = "Path filters (Enter = verify all records, space separated)",
            ["输入序号或文件名"] = "Enter an index or file name",
            ["选择格式"] = "Choose format",
            ["输出格式:  1) json（默认）  2) text  3) ndjson"] = "Output format:  1) json (default)  2) text  3) ndjson",
            ["当前目录下的 JSON 文件:"] = "JSON files in the current directory:",
            ["当前目录下没有 JSON 文件，请直接输入完整路径"] = "No JSON files in the current directory; enter a full path",
            ["无效的选择: {0}"] = "Invalid choice: {0}",
            ["文件不存在: {0}"] = "File does not exist: {0}",
            ["线程数必须是正整数: {0}"] = "Thread count must be a positive integer: {0}",
            ["线程数 {0} 超过上限，已调整为 {1}"] = "Thread count {0} exceeds the limit; adjusted to {1}",
            ["无法解析文件大小: {0}（示例: 1KB、10MB、0.5GiB）"] = "Cannot parse file size: {0} (examples: 1KB, 10MB, 0.5GiB)",
            ["无法解析文件大小: {0}（示例: 100MB、2GiB）"] = "Cannot parse file size: {0} (examples: 100MB, 2GiB)",

            // ===== 命令行解析错误 =====
            ["{0} 需要一个算法列表，例如: --algorithms md5,sha256"] = "{0} requires an algorithm list, e.g. --algorithms md5,sha256",
            ["{0} 需要一个文件大小，例如: --min-size 1MB"] = "{0} requires a file size, e.g. --min-size 1MB",
            ["{0} 需要一个文件大小，例如: --max-size 100MB"] = "{0} requires a file size, e.g. --max-size 100MB",
            ["{0} 需要一个文件路径，使用 - 表示从标准输入读取"] = "{0} requires a file path; - means stdin",
            ["{0} 需要一个线程数，例如: --threads 8、--threads auto、--threads 1"] = "{0} requires a thread count, e.g. --threads 8, --threads auto, --threads 1",
            ["{0} 需要一个输出路径，使用 - 表示输出到标准输出"] = "{0} requires an output path; - means stdout",
            ["{0} 需要一个格式: json、text 或 ndjson"] = "{0} requires a format: json, text or ndjson",
            ["{0} 需要一个值: auto、always 或 never"] = "{0} requires a value: auto, always or never",
            ["{0} 需要一个日志文件路径"] = "{0} requires a log file path",
            ["{0} 需要一个语言: zh 或 en"] = "{0} requires a language: zh or en",
            ["不支持的输出格式: {0}（可用: json, text, ndjson）"] = "Unsupported output format: {0} (available: json, text, ndjson)",
            ["不支持的算法: {0}"] = "Unsupported algorithm: {0}",
            ["不支持的算法: {0}（可用: {1} 或 all）"] = "Unsupported algorithm: {0} (available: {1} or all)",
            ["不支持的语言: {0}（可用: zh, en）"] = "Unsupported language: {0} (available: zh, en)",
            ["不支持的值: {0}（可用: auto, always, never）"] = "Unsupported value: {0} (available: auto, always, never)",
            ["未知参数: {0}"] = "Unknown option: {0}",
            ["，是否想用: {0}？"] = "; did you mean {0}?",
            ["使用 FastHasher -h 查看全部参数"] = "Run FastHasher -h to see all options",
            ["参数 {0} 已废弃，请改用: {1}\n使用 FastHasher -h 查看全部参数"] = "Option {0} is deprecated; use {1} instead.\nRun FastHasher -h to see all options",
            ["参数 {0} 已移除（该参数从未生效），请直接删除。\n使用 FastHasher -h 查看全部参数"] = "Option {0} has been removed (it never had any effect); just delete it.\nRun FastHasher -h to see all options",
            ["无法解析文件大小: {0}（示例: 1KB, 10MB, 0.5GiB）"] = "Cannot parse file size: {0} (examples: 1KB, 10MB, 0.5GiB)",
            ["线程数必须是正整数或 auto: {0}"] = "Thread count must be a positive integer or auto: {0}",
            ["线程数 {0} 超过上限，已调整为 {1}（CPU 逻辑线程数 × {2}）"] = "Thread count {0} exceeds the limit; adjusted to {1} (logical CPU count x {2})",
            ["--min-size ({0} 字节) 不能大于 --max-size ({1} 字节)"] = "--min-size ({0} bytes) cannot be greater than --max-size ({1} bytes)",
            ["verify 需要指定要验证的 JSON 文件\n用法: FastHasher verify <hash.json> [路径过滤...]"] = "verify requires the JSON file to verify\nUsage: FastHasher verify <hash.json> [path filters...]",
            ["缺少输入路径\n用法: FastHasher [hash] <文件|目录|通配符>... [选项]"] = "Missing input path\nUsage: FastHasher [hash] <file|dir|glob>... [options]",
            ["验证文件不存在: {0}"] = "Verification file does not exist: {0}",
            ["响应文件不存在: {0}"] = "Response file does not exist: {0}",
            ["读取响应文件失败 {0}: {1}"] = "Failed to read response file {0}: {1}",
            ["文件列表不存在: {0}"] = "File list does not exist: {0}",
            ["读取文件列表失败 {0}: {1}"] = "Failed to read file list {0}: {1}",
            ["算法列表不能为空"] = "Algorithm list cannot be empty",

            // ===== 前缀与统计标签 =====
            ["错误: "] = "Error: ",
            ["警告: 保存语言设置失败，仅本次会话生效: "] = "Warning: failed to save language setting (session only): ",
            ["提示: "] = "Note: ",
            ["警告: "] = "Warning: ",
            ["信息: "] = "Info: ",
            ["成功: "] = "Success: ",
            ["完成: "] = "Done: ",
            ["验证失败: "] = "Verification failed: ",
            ["算法"] = "Algorithms",
            ["输入"] = "Input",
            ["递归"] = "Recursive",
            ["线程"] = "Threads",
            ["输出"] = "Output",
            ["格式"] = "Format",
            ["验证文件"] = "Verify file",
            ["路径过滤"] = "Path filters",
            ["并行线程"] = "Threads",
            ["文件数量"] = "File count",
            ["总大小"] = "Total size",
            ["模式"] = "Mode",
            ["并行"] = "Parallel",
            ["顺序"] = "Sequential",
            ["是"] = "Yes",
            ["否"] = "No",
            ["无"] = "None",
            ["未知"] = "Unknown",
            ["默认文件名"] = "default file name",
            ["1 (顺序)"] = "1 (sequential)",
            ["{0} (并行)"] = "{0} (parallel)",
            ["{0} 个模式"] = "{0} patterns",
            ["并行线程数"] = "Thread count",
            ["并行线程数: {0}"] = "Thread count: {0}",
            ["并行线程数: {0} {1}"] = "Thread count: {0} {1}",
            ["递归搜索: {0}"] = "Recursive: {0}",
            ["文件数量: {0}"] = "File count: {0}",
            ["文件数量: {0} | 总大小: {1}"] = "File count: {0} | Total size: {1}",
            ["文件总数"] = "Total files",
            ["文件总数: {0}"] = "Total files: {0}",
            ["文件数"] = "Files",
            ["文件平均"] = "Per file",
            ["总时间"] = "Total time",
            ["总用时"] = "Total time",
            ["计算时间"] = "Time",
            ["成功率"] = "Success rate",
            ["存储文件总数"] = "Stored files",
            ["验证文件数"] = "Verified files",
            ["文件大小过滤: {0} - {1}"] = "Size filter: {0} - {1}",
            ["并行模式"] = "Parallel mode",
            ["验证结果"] = "Verification results",

            // ===== 计算流程（FileProcessor / FastHashCalculator）=====
            ["FastHasher 开始运行，时间: {0}"] = "FastHasher started at {0}",
            ["计算模式"] = "Hash mode",
            ["验证模式"] = "Verify mode",
            ["计算哈希"] = "Hash files",
            ["验证哈希"] = "Verify hashes",
            ["并行处理: {0}"] = "Parallel: {0}",
            ["处理模式 [{0}/{1}]: {2}"] = "Pattern [{0}/{1}]: {2}",
            ["文件模式列表:"] = "Pattern list:",
            ["无法获取文件大小: {0} - {1}"] = "Cannot get file size: {0} - {1}",
            ["无限制"] = "unlimited",
            ["递归搜索"] = "Recursive",
            ["路径过滤: {0}"] = "Path filters: {0}",
            ["输出格式: {0}"] = "Format: {0}",
            ["验证文件: {0}"] = "Verify file: {0}",
            ["开始处理文件..."] = "Starting file processing...",
            ["开始并行处理文件..."] = "Starting parallel processing...",
            ["开始顺序处理文件..."] = "Starting sequential processing...",
            ["开始处理文件模式，模式数量: {0}"] = "Expanding patterns, count: {0}",
            ["正在展开文件模式..."] = "Expanding file patterns...",
            ["文件模式展开完成，找到 {0} 个文件"] = "Pattern expansion finished, found {0} files",
            ["  目录 {0} 中找到 {1} 个文件"] = "  Found {1} files in directory {0}",
            ["  通配符 {0} 匹配到 {1} 个文件"] = "  Glob {0} matched {1} files",
            ["  添加文件: {0}"] = "  Added file: {0}",
            ["  目录是否存在: {0}"] = "  Directory exists: {0}",
            ["  解析后的目录: {0}"] = "  Resolved directory: {0}",
            ["  解析后的文件模式: {0}"] = "  Resolved pattern: {0}",
            ["  原始模式: {0}"] = "  Raw pattern: {0}",
            ["  绝对路径目录: {0}"] = "  Absolute directory: {0}",
            ["无法枚举目录 '{0}': {1}"] = "Cannot enumerate directory '{0}': {1}",
            ["无法处理模式 '{0}': {1}"] = "Cannot process pattern '{0}': {1}",
            ["无法获取文件信息: {0}"] = "Cannot get file info: {0}",
            ["文件 {0} 大小 {1} 不符合过滤条件 ({2} - {3})"] = "File {0} size {1} does not match the filter ({2} - {3})",
            ["文件大小过滤完成，剩余 {0} 个文件"] = "Size filter finished, {0} files remain",
            ["跳过非常规文件: {0}"] = "Skipped non-regular file: {0}",
            ["已跳过 {0} 个非常规文件（FIFO/设备/套接字）"] = "Skipped {0} non-regular files (FIFO/device/socket)",
            ["非常规文件（FIFO/设备/套接字），已跳过"] = "Non-regular file (FIFO/device/socket), skipped",
            ["没有找到匹配的文件"] = "No matching files found",
            ["警告: 没有找到匹配的文件"] = "Warning: no matching files found",
            ["找到 {0} 个文件需要处理 | 总大小: {1}"] = "Found {0} files to process | Total size: {1}",
            ["文件统计"] = "File statistics",
            ["输出方式: 标准输出 (stdout)"] = "Output: stdout",
            ["输出文件: {0}"] = "Output file: {0}",
            ["处理开始时间: {0}"] = "Started at {0}",
            ["处理结束时间: {0}"] = "Finished at {0}",
            ["开始处理文件: {0} (大小: {1})"] = "Processing file: {0} (size: {1})",
            ["开始处理文件: {0} (无法获取大小)"] = "Processing file: {0} (size unknown)",
            ["开始处理文件 [{0}/{1}]: {2} (大小: {3})"] = "Processing file [{0}/{1}]: {2} (size: {3})",
            ["(CPU默认)"] = "(CPU default)",
            ["(手动指定)"] = "(manually set)",
            ["(未知文件)"] = "(unknown file)",
            ["文件处理开始"] = "File processing started",
            ["处理被取消"] = "Processing cancelled",
            ["处理被用户取消"] = "Processing cancelled by the user",
            ["处理完成！"] = "Processing finished!",
            ["处理完成！日志已保存到: {0}"] = "Processing finished! Log saved to: {0}",
            ["验证完成！"] = "Verification finished!",
            ["验证完成！日志已保存到: {0}"] = "Verification finished! Log saved to: {0}",
            ["处理完成，但没有可用的输出路径"] = "Processing finished, but no output path is available",
            ["{0} 正在初始化并行处理，文件总数: {1}..."] = "{0} Initializing parallel processing, total files: {1}...",
            ["{0} 正在初始化哈希计算，文件总数: {1}..."] = "{0} Initializing hashing, total files: {1}...",
            ["处理进度: {0}/{1} ({2:P1}) | 大小: {3} | 速度: {4}"] = "Progress: {0}/{1} ({2:P1}) | Size: {3} | Speed: {4}",
            ["进度报告: 已处理 {0}/{1} 文件 ({2:P1})"] = "Progress: processed {0}/{1} files ({2:P1})",
            ["{0} 处理中: {1}/{2} 文件 ({3:P1}) | 当前: {4}"] = "{0} processing: {1}/{2} files ({3:P1}) | Current: {4}",
            ["{0} {1} {2:P1} | {3}/{4} 文件 | {5}"] = "{0} {1} {2:P1} | {3}/{4} files | {5}",
            ["{0} {1} {2:P1} | {3}/{4} 文件 | {5} | 当前: {6}"] = "{0} {1} {2:P1} | {3}/{4} files | {5} | Current: {6}",
            ["{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6}{7} | 当前: {8}"] = "{0} {1} {2:P1} | {3}/{4} files | {5} | {6}{7} | Current: {8}",
            ["{0} {1} {2:P1} | {3}/{4} 文件 | {5} | {6} | {7}{8} | 当前: {9}"] = "{0} {1} {2:P1} | {3}/{4} files | {5} | {6} | {7}{8} | Current: {9}",
            ["{0} [{1}] {2:P1} | {3}/{4} 文件 | {5} | 正在计算: {6}"] = "{0} [{1}] {2:P1} | {3}/{4} files | {5} | Hashing: {6}",
            [" | {0}线程"] = " | {0} threads",
            ["文件处理完成 [{0}/{1}]: {2} (用时: {3}ms)"] = "File processed [{0}/{1}]: {2} (took {3}ms)",
            ["文件处理完成: {0} (用时: {1}ms)"] = "File processed: {0} (took {1}ms)",
            ["文件处理失败 [{0}/{1}]: {2} - {3}"] = "File failed [{0}/{1}]: {2} - {3}",
            ["文件处理失败: {0} - {1}"] = "File failed: {0} - {1}",
            ["并行处理完成: {0} 个文件"] = "Parallel processing finished: {0} files",
            ["顺序处理完成: {0} 个文件"] = "Sequential processing finished: {0} files",
            ["并行处理完成，共处理 {0} 个文件"] = "Parallel processing finished, {0} files processed",
            ["顺序处理完成，共处理 {0} 个文件"] = "Sequential processing finished, {0} files processed",
            ["使用并行处理模式"] = "Using parallel processing",
            ["使用顺序处理模式"] = "Using sequential processing",
            ["处理过程中出现异常: {0}"] = "Exception during processing: {0}",
            ["处理出错: {0}"] = "Processing error: {0}",
            ["异常堆栈: {0}"] = "Exception stack: {0}",
            ["异常详情: {0} - {1}"] = "Exception details: {0} - {1}",
            ["堆栈跟踪: {0}"] = "Stack trace: {0}",
            ["处理统计信息"] = "Processing statistics",
            ["性能统计"] = "Performance statistics",
            ["线程信息"] = "Thread info",
            ["内存信息"] = "Memory info",
            ["内存: {0}MB / {1}MB"] = "Memory: {0}MB / {1}MB",
            ["线程池: {0}/{1} (工作), {2}/{3} (IO) | 逻辑线程数: {4}"] = "Thread pool: {0}/{1} (worker), {2}/{3} (IO) | Logical threads: {4}",
            ["总处理时间: {0:F2}秒"] = "Total processing time: {0:F2}s",
            ["平均每个文件: {0:F2}毫秒"] = "Average per file: {0:F2}ms",
            ["平均速度"] = "Average speed",
            ["平均速度: {0}"] = "Average speed: {0}",
            ["文件大小分布"] = "File size distribution",
            ["文件大小分布图表"] = "File size distribution chart",
            ["性能分析图表"] = "Performance analysis chart",
            ["不同大小文件的平均处理时间"] = "Average processing time by file size",
            ["文件大小 vs 处理时间关系图"] = "File size vs processing time",
            ["文件大小(MB)"] = "File size (MB)",
            ["处理时间(ms)"] = "Processing time (ms)",
            ["  大小: {0:F1}MB, 时间: {1:F1}ms"] = "  size: {0:F1}MB, time: {1:F1}ms",
            ["大小:{0:F1}MB"] = "size: {0:F1}MB",
            ["  大小: {0} | 用时: {1} ms"] = "  size: {0} | time: {1} ms",
            ["{0} 与 {1} 关系:"] = "{0} vs {1}:",
            ["无法绘制图表: {0}"] = "Cannot render chart: {0}",

            // ===== 输出 =====
            ["没有结果需要输出"] = "No results to output",
            ["开始输出结果，结果数量: {0}"] = "Outputting results, count: {0}",
            ["输出内容长度: {0} 字符"] = "Output length: {0} chars",
            ["结果已输出到标准输出"] = "Results written to stdout",
            ["哈希计算结果已保存到: {0}"] = "Hash results saved to: {0}",
            ["结果文件大小: {0}"] = "Result file size: {0}",
            ["保存结果到文件失败: {0}"] = "Failed to save results to file: {0}",
            ["结果未写出，请检查输出路径权限"] = "Results not written; check output path permissions",
            ["JSON 序列化完成，长度: {0}"] = "JSON serialization finished, length: {0}",
            ["HashOutput 对象创建完成"] = "HashOutput object created",
            ["Metadata 是否为 null: {0}"] = "Metadata is null: {0}",
            ["Files 是否为 null: {0}"] = "Files is null: {0}",
            ["FastHasher {0} | 生成时间: {1}"] = "FastHasher {0} | Generated at {1}",
            ["算法: {0}"] = "Algorithms: {0}",
            ["算法: {0} | 文件数: {1} | 总大小: {2} | 用时: {3:F2} 秒"] = "Algorithms: {0} | Files: {1} | Total size: {2} | Elapsed: {3:F2} s",
            ["用时: {0:F2} 秒"] = "Elapsed: {0:F2} s",
            ["{0:F2}秒"] = "{0:F2}s",
            ["验证结果已保存到: {0}"] = "Verification results saved to: {0}",
            ["验证结果已输出到标准输出"] = "Verification results written to stdout",
            ["保存验证结果到文件失败: {0}"] = "Failed to save verification results: {0}",
            ["FastHasher {0} 验证报告 | 生成时间: {1}"] = "FastHasher {0} verification report | Generated at {1}",

            // ===== FastHashCalculator =====
            ["开始计算 {0} 种哈希算法"] = "Computing {0} hash algorithms",
            ["开始计算哈希 | 大小: {0}"] = "Starting hashing | Size: {0}",
            ["文件读取完成 | 大小: {0}"] = "File read finished | Size: {0}",
            ["使用动态缓冲区: {0}"] = "Dynamic buffer: {0}",
            ["流式处理大文件 | 大小: {0} | 缓冲区: {1}"] = "Streaming large file | Size: {0} | Buffer: {1}",
            ["{0} {1} [{2}] {3:P1} | {4}/{5} | {6} | 缓冲区: {7}"] = "{0} {1} [{2}] {3:P1} | {4}/{5} | {6} | Buffer: {7}",
            ["所有算法计算完成"] = "All algorithms finished",
            ["算法 {0} 计算完成"] = "Algorithm {0} finished",
            ["算法 {0} 计算完成 | 哈希: {1}..."] = "Algorithm {0} finished | Hash: {1}...",
            ["哈希计算完成 | 用时: {0:F2}ms"] = "Hashing finished | Took {0:F2}ms",
            ["大文件处理完成: {0}"] = "Large file processed: {0}",
            ["文件 {0} | 算法 {1} 计算失败: {2}"] = "File {0} | Algorithm {1} failed: {2}",
            ["文件 {0} 处理失败: {1}"] = "File {0} failed: {1}",
            ["哈希计算完成 ✓"] = "Hashing finished ✓",
            ["哈希计算完成，共计算 {0} 个文件"] = "Hashing finished, {0} files computed",
            ["哈希计算完成: {0} 个文件"] = "Hashing finished: {0} files",
            ["计算过程中出错: {0}"] = "Error during computation: {0}",
            ["计算过程中出现异常: {0}"] = "Exception during computation: {0}",
            ["计算出错: {0}"] = "Computation error: {0}",
            ["计算哈希失败: {0}"] = "Hashing failed: {0}",
            ["计算错误"] = "Computation error",
            ["文件不存在"] = "File does not exist",
            ["文件不存在或无法访问"] = "File does not exist or is inaccessible",

            // ===== 验证流程（HashVerifier）=====
            ["FastHasher 验证模式开始运行，时间: {0}"] = "FastHasher verification started at {0}",
            ["验证模式缺少要验证的 JSON 文件"] = "Verify mode is missing the JSON file to verify",
            ["没有找到匹配的验证文件"] = "No matching verification files found",
            ["过滤后需要验证 {0} 个文件"] = "{0} files to verify after filtering",
            ["开始读取验证文件: {0}"] = "Reading verification file: {0}",
            ["正在读取验证文件..."] = "Reading verification file...",
            ["正在读取文件..."] = "Reading file...",
            ["文件读取完成，大小: {0}"] = "File read finished, size: {0}",
            ["读取JSON文件失败: {0}"] = "Failed to read JSON file: {0}",
            ["JSON内容前500字符: {0}"] = "First 500 chars of JSON: {0}",
            ["JSON根元素属性: {0}"] = "JSON root properties: {0}",
            ["检测到旧格式JSON（不包含metadata）"] = "Detected legacy JSON (no metadata)",
            ["检测到新格式JSON（包含metadata）"] = "Detected new-format JSON (with metadata)",
            ["旧格式JSON解析完成，找到 {0} 条记录"] = "Legacy JSON parsed, found {0} records",
            ["新格式JSON解析完成，找到 {0} 条记录"] = "New-format JSON parsed, found {0} records",
            ["新格式JSON中未找到files数组"] = "files array not found in new-format JSON",
            ["验证文件读取完成，共 {0} 条记录"] = "Verification file read finished, {0} records",
            ["验证文件中没有找到哈希记录"] = "No hash records found in the verification file",
            ["使用算法: {0}"] = "Using algorithms: {0}",
            ["并行模式: {0}, 线程数: {1}"] = "Parallel: {0}, threads: {1}",
            ["原始计算配置"] = "Original computation config",
            ["验证配置"] = "Verification config",
            ["验证模式配置"] = "Verify mode config",
            ["计算参数信息"] = "Computation parameters",
            ["验证开始时间: {0}"] = "Verification started at {0}",
            ["验证结束时间: {0}"] = "Verification finished at {0}",
            ["开始重新计算哈希..."] = "Recomputing hashes...",
            ["开始重新计算 {0} 个文件的哈希..."] = "Recomputing hashes for {0} files...",
            ["验证被取消"] = "Verification cancelled",
            ["计算被用户取消"] = "Computation cancelled by the user",
            ["哈希计算完成: {0}/{1} 文件"] = "Hashing finished: {0}/{1} files",
            ["[线程 {0}] 开始计算: {1}"] = "[Thread {0}] computing: {1}",
            ["[线程 {0}] 文件 {1} 异常: {2}"] = "[Thread {0}] file {1} exception: {2}",
            ["开始构建验证报告，结果数量: {0}"] = "Building verification report, result count: {0}",
            ["验证报告构建完成，报告长度: {0}"] = "Verification report built, length: {0}",
            ["生成验证报告失败: {0}"] = "Failed to build verification report: {0}",
            ["验证统计"] = "Verification statistics",
            ["验证统计: {0} 成功, {1} 失败, {2} 缺失, {3} 错误"] = "Verification: {0} success, {1} failed, {2} missing, {3} error",
            ["成功: {0} | 失败: {1} | 缺失: {2} | 错误: {3} | 成功率: {4:F2}%"] = "Success: {0} | Failed: {1} | Missing: {2} | Error: {3} | Success rate: {4:F2}%",
            ["问题文件:"] = "Problem files:",
            ["存储文件总数: {0} | 验证文件数: {1}"] = "Stored files: {0} | Verified files: {1}",
            ["总验证时间: {0:F2}秒"] = "Total verification time: {0:F2}s",
            ["总用时: {0:F2}秒"] = "Total time: {0:F2}s",
            ["验证成功"] = "Verified",
            ["验证失败"] = "Verification failed",
            ["文件缺失"] = "File missing",
            ["哈希不匹配"] = "Hash mismatch",
            ["{0} 哈希不匹配"] = "{0} hash mismatch",
            ["文件大小不匹配"] = "File size mismatch",
            ["计算失败"] = "Computation failed",
            ["未知错误"] = "Unknown error",
            ["未知文件"] = "Unknown file",
            ["        {0} 期望: {1}"] = "        {0} expected: {1}",
            ["        {0} 实际: {1}"] = "        {0} actual: {1}",
            ["  失败: {0}"] = "  Failed: {0}",
            ["文件 {0}: {1}"] = "File {0}: {1}",

            // ===== 基准测试 =====
            ["运行基准测试..."] = "Running benchmark...",
            ["顺序处理测试:"] = "Sequential test:",
            ["并行处理测试（所有算法同时计算）:"] = "Parallel test (all algorithms at once):",
            ["基准测试完成"] = "Benchmark finished",
            ["测试文件: {0}MB"] = "Test file: {0}MB",
            ["  总计: {0}ms"] = "  Total: {0}ms",
            ["加速比: {0:F2}x"] = "Speedup: {0:F2}x",
            ["并行效率: {0:F1}%"] = "Parallel efficiency: {0:F1}%",
            ["CPU 逻辑线程数: {0}"] = "Logical CPU threads: {0}",
            ["按任意键退出..."] = "Press any key to exit...",
            ["正在取消当前任务，请稍候..."] = "Cancelling the current task, please wait...",

            // ===== 时间与进度工具 =====
            ["计算中..."] = "computing...",
            ["{0:F0}秒"] = "{0:F0}s",
            ["{0:F1}分"] = "{0:F1}m",
            ["{0:F1}时"] = "{0:F1}h",
            ["{0:F2}秒"] = "{0:F2}s",
        };

        // ===== 帮助文本（整段替换而非逐行翻译，保证排版对齐）=====

        /// <summary>帮助文本标题行（含版本号占位符）</summary>
        public static string HelpHeader =>
            IsEnglish
                ? "FastHasher v{0} - file hashing and verification tool"
                : "FastHasher v{0} - 文件哈希计算与验证工具";

        /// <summary>帮助正文。两种语言各自维护整段文本，选项对齐方式互不迁就。</summary>
        public static string HelpBody =>
            IsEnglish ? EnglishHelp : ChineseHelp;

        private const string ChineseHelp = @"用法:
  FastHasher                    交互式向导模式（无参数即可进入）
  FastHasher -i                 显式进入交互式向导
  FastHasher [hash] <文件|目录|通配符>... [选项]
  FastHasher verify <哈希.json> [路径过滤...] [选项]
  FastHasher bench
  FastHasher help | version

选项:
  -a, --algorithms <列表>   哈希算法，逗号分隔或 all
                            可用: md5, sha1, sha256, sha512, blake3（默认 md5）
  -r, --recursive           递归搜索子目录
      --min-size <大小>     最小文件大小，例如 1KB、10MB、0.5GiB
      --max-size <大小>     最大文件大小
  -f, --file-list <文件>    从文件读取路径列表，每行一个；- 表示标准输入
  @<响应文件>               从文件读取命令行参数，每行一个

  -j, --threads <n|auto>    并行线程数，默认 auto（CPU 逻辑线程数）
                            auto 或 1 = 顺序处理；上限为 CPU 逻辑线程数 x 2

  -o, --output <文件|->     输出路径，- 表示标准输出
                            默认生成带时间戳的文件
      --format <格式>       输出格式: json（默认）、text、ndjson

      --lang <zh|en>        界面语言；优先级高于配置文件 settings.json
      --stats               显示统计信息与图表
  -q, --quiet               只输出错误与警告
      --verbose             输出详细日志
      --no-color            禁用彩色输出
      --progress <模式>     进度显示: auto（默认）、always、never
      --log <文件>          指定日志文件路径
      --no-log              不写日志文件

  -i, --interactive         交互式向导模式
  -h, --help                显示帮助
  -v, --version             显示版本信息

退出码:
  0    全部成功
  1    存在失败、缺失或哈希不匹配
  2    参数错误
  130  用户取消

示例:
  FastHasher *.txt -a md5,sha256 --recursive
  FastHasher D:\data --min-size 1MB --stats -j 8
  FastHasher verify hashes_20260101_120000.json
  FastHasher verify hashes.json --format text -o report.txt
  FastHasher verify hashes.json -q -o -
  FastHasher --file-list files.txt -j auto
";

        private const string EnglishHelp = @"Usage:
  FastHasher                    interactive wizard (no arguments)
  FastHasher -i                 enter the interactive wizard explicitly
  FastHasher [hash] <file|dir|glob>... [options]
  FastHasher verify <hashes.json> [path filters...] [options]
  FastHasher bench
  FastHasher help | version

Options:
  -a, --algorithms <list>   hash algorithms, comma separated or all
                            available: md5, sha1, sha256, sha512, blake3 (default md5)
  -r, --recursive           recurse into subdirectories
      --min-size <size>     minimum file size, e.g. 1KB, 10MB, 0.5GiB
      --max-size <size>     maximum file size
  -f, --file-list <file>    read a path list from a file, one per line; - means stdin
  @<response file>          read command-line arguments from a file, one per line

  -j, --threads <n|auto>    worker threads, default auto (logical CPU count)
                            auto or 1 = sequential; capped at logical CPU count x 2

  -o, --output <file|->     output path, - means stdout
                            a timestamped file is generated by default
      --format <format>     output format: json (default), text, ndjson

      --lang <zh|en>        UI language; overrides settings.json
      --stats               show statistics and charts
  -q, --quiet               only errors and warnings
      --verbose             verbose logging
      --no-color            disable coloured output
      --progress <mode>     progress display: auto (default), always, never
      --log <file>          explicit log file path
      --no-log              do not write a log file

  -i, --interactive         interactive wizard mode
  -h, --help                show help
  -v, --version             show version information

Exit codes:
  0    everything succeeded
  1    failures, missing files or hash mismatches
  2    invalid arguments
  130  cancelled by the user

Examples:
  FastHasher *.txt -a md5,sha256 --recursive
  FastHasher D:\data --min-size 1MB --stats -j 8
  FastHasher verify hashes_20260101_120000.json
  FastHasher verify hashes.json --format text -o report.txt
  FastHasher verify hashes.json -q -o -
  FastHasher --file-list files.txt -j auto
";
    }
}
