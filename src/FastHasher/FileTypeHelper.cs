using System.Runtime.InteropServices;

namespace FastHasher
{
    /// <summary>
    /// 文件类型判断。
    /// Linux 上目录项可能是 FIFO、字符/块设备或套接字，读取语义与普通文件不同：
    /// 例如无写端的 FIFO 在 open 时会永久阻塞。这类路径必须在枚举阶段跳过。
    /// </summary>
    internal static class FileTypeHelper
    {
        private const int O_RDONLY = 0;
        // Linux <fcntl.h>：O_NONBLOCK = 0x800（x86_64 / aarch64 一致）
        private const int O_NONBLOCK = 0x800;

        // struct stat.st_mode：S_IFMT 为类型掩码，S_IFREG 为普通文件
        private const uint S_IFMT = 0xF000;
        private const uint S_IFREG = 0x8000;

        // glibc x86_64 / aarch64 的 struct stat 前四个字段：
        // st_dev(8) st_ino(8) st_nlink(8) st_mode(4)
        private const int StModeOffset = 24;
        private const int StatBufferSize = 256;

        [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int SysOpen(string pathname, int flags);

        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        private static extern int SysFstat(int fd, byte[] statbuf);

        [DllImport("libc", EntryPoint = "close")]
        private static extern int SysClose(int fd);

        // glibc < 2.33 没有 fstat 符号（只有 __fxstat），探测失败后不再重试
        private static bool statUnavailable;

        /// <summary>
        /// 是否为可安全读取的普通文件。
        /// 非 Linux 平台与探测失败场景一律返回 true，交由后续流程处理，行为不劣化。
        /// </summary>
        public static bool IsRegularFile(string path)
        {
            if (!OperatingSystem.IsLinux() || statUnavailable) return true;

            int fd = -1;
            try
            {
                // O_NONBLOCK 让 FIFO 打开时不阻塞
                fd = SysOpen(path, O_RDONLY | O_NONBLOCK);
                if (fd < 0) return true;

                byte[] buf = new byte[StatBufferSize];
                if (SysFstat(fd, buf) != 0) return true;

                uint mode = BitConverter.ToUInt32(buf, StModeOffset);
                return (mode & S_IFMT) == S_IFREG;
            }
            catch (EntryPointNotFoundException)
            {
                statUnavailable = true;
                return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                if (fd >= 0)
                {
                    try { SysClose(fd); } catch { }
                }
            }
        }

        /// <summary>
        /// 返回文件类型的中文描述，用于日志。
        /// </summary>
        public static string Describe(string path)
        {
            if (Directory.Exists(path)) return "目录";
            return IsRegularFile(path) ? "普通文件" : "非常规文件（FIFO/设备/套接字）";
        }
    }

    /// <summary>
    /// 路径可移植处理：JSON 中的路径存相对形式，便于报告与文件一起迁移。
    /// </summary>
    internal static class PortablePath
    {
        /// <summary>
        /// 转成相对当前工作目录的路径；跨盘符或计算失败时退回绝对路径。
        /// </summary>
        public static string ToPortable(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), full);
                // 跨盘符时 GetRelativePath 会返回绝对路径，保持原样即可
                return Path.IsPathRooted(relative) ? full : relative;
            }
            catch
            {
                return path;
            }
        }
    }
}
