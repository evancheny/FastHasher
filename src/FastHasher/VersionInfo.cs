// VersionInfo.cs - 版本号统一管理
namespace FastHasher
{
    /// <summary>
    /// 版本号信息统一管理类
    /// 所有版本号应从此类获取，确保版本号一致性
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// 当前主版本号 (Major)
        /// </summary>
        public const int Major = 2;

        /// <summary>
        /// 当前次版本号 (Minor)
        /// </summary>
        public const int Minor = 2;

        /// <summary>
        /// 当前修订号 (Patch)
        /// </summary>
        public const int Patch = 0;

        /// <summary>
        /// 完整版本号字符串 (格式: Major.Minor.Patch)
        /// </summary>
        public const string Version = "2.2.0";

        /// <summary>
        /// 程序集版本号字符串 (格式: Major.Minor.Patch.0)
        /// </summary>
        public const string AssemblyVersion = "2.2.0.0";

        /// <summary>
        /// 版本描述信息
        /// </summary>
        public const string InformationalVersion = "2.2.0 - 新增 Linux x64 支持";

        /// <summary>
        /// 获取完整版本号字符串
        /// </summary>
        /// <returns>版本号字符串</returns>
        public static string GetVersion()
        {
            return Version;
        }

        /// <summary>
        /// 获取程序集版本号字符串
        /// </summary>
        /// <returns>程序集版本号字符串</returns>
        public static string GetAssemblyVersion()
        {
            return AssemblyVersion;
        }

        /// <summary>
        /// 获取版本描述信息
        /// </summary>
        /// <returns>版本描述信息</returns>
        public static string GetInformationalVersion()
        {
            return InformationalVersion;
        }
    }
}
