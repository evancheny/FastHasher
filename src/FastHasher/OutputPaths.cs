namespace FastHasher
{
    /// <summary>
    /// 输出文件与日志文件路径的统一解析规则。
    /// 计算模式与验证模式仅在默认文件名的前缀上不同，其余逻辑共用此处。
    /// </summary>
    internal static class OutputPaths
    {
        /// <summary>
        /// 当前时间戳，用于默认文件名（格式 yyyyMMdd_HHmmss）。
        /// </summary>
        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        /// <summary>
        /// 按输出格式选择文件扩展名。
        /// </summary>
        public static string GetExtension(OutputFormat format)
        {
            return format switch
            {
                OutputFormat.Text => ".txt",
                OutputFormat.Ndjson => ".ndjson",
                _ => ".json"
            };
        }

        /// <summary>
        /// 解析输出文件路径：指定 -o 时使用用户路径，否则在指定目录下生成「前缀_时间戳.扩展名」。
        /// </summary>
        /// <param name="userPath">用户通过 -o 指定的路径，为空表示自动生成</param>
        /// <param name="format">输出格式</param>
        /// <param name="baseDirectory">自动生成时的目标目录</param>
        /// <param name="baseName">自动生成时的文件名前缀</param>
        public static string ResolveOutput(string? userPath, OutputFormat format, string baseDirectory, string baseName)
        {
            if (!string.IsNullOrEmpty(userPath))
            {
                return Path.GetFullPath(userPath!);
            }

            return Path.Combine(baseDirectory,
                string.Format("{0}_{1}{2}", baseName, Timestamp(), GetExtension(format)));
        }

        /// <summary>
        /// 解析日志文件路径：--no-log 返回 null；--log 使用用户路径；否则与输出文件同目录，避免污染工作目录。
        /// </summary>
        /// <param name="options">命令行选项</param>
        /// <param name="outputPath">已解析的输出路径（"-" 表示标准输出）</param>
        /// <param name="logPrefix">日志文件名前缀</param>
        public static string? ResolveLog(CliOptions options, string? outputPath, string logPrefix)
        {
            if (options.NoLog) return null;
            if (!string.IsNullOrEmpty(options.LogFile)) return Path.GetFullPath(options.LogFile!);

            string directory = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(outputPath) && outputPath != "-")
            {
                string? outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory)) directory = outputDirectory;
            }

            return Path.Combine(directory, string.Format("{0}_{1}.log", logPrefix, Timestamp()));
        }
    }
}
