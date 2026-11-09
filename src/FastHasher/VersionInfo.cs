// VersionInfo.cs - 版本号统一管理
namespace FastHasher
{
    /// <summary>
    /// 版本号信息统一管理类。
    /// 版本号在 FastHasher.csproj 与本文件中各维护一份，发版时需同步修改。
    /// </summary>
    public static class VersionInfo
    {
        /// <summary>
        /// 完整版本号字符串 (格式: Major.Minor.Patch)
        /// </summary>
        public const string Version = "2.2.1";

        /// <summary>
        /// 版本描述信息
        /// </summary>
        public const string InformationalVersion = "2.2.1 - 修复非常规文件阻塞与路径可移植性";

        /// <summary>
        /// 获取完整版本号字符串
        /// </summary>
        /// <returns>版本号字符串</returns>
        public static string GetVersion()
        {
            return Version;
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
