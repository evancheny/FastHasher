# FastHasher – ⚡ 极速哈希计算与验证工具

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-Windows%20x64-blue?style=flat-square" alt="Windows x64" />
  <img src="https://img.shields.io/badge/MIT-green?logo=open-source-initiative" alt="MIT License" />
</p>


<p align="center">
  <b>FastHasher</b> 是一款为高性能而生的文件哈希计算与验证工具，支持 MD5、SHA1、SHA256、SHA512、BLAKE3 等算法。<br>
  它不仅能<em>极速处理</em>成千上万个文件，还能一键验证完整性，并输出专业 JSON 报告与可视化图表。<br>
  告别乱码、告别参数过长、告别等待 —— 让哈希计算成为享受！
</p>

---

## ✨ 特性亮点

### 🚀 **极致性能**
- **默认并行处理**：自动利用所有 CPU 逻辑线程，多核满载，加速比显著。
- **动态缓冲区**：小文件内存计算，大文件流式处理，平衡速度与内存占用。
- **批处理模式**：内置批处理机制，避免一次性加载过多文件，轻松应对数十万文件。

### 🔧 **解决常见痛点**
- **完美中文支持**：文件名、路径、内容均采用 UTF-8 编码，控制台不乱码。
- **参数过长？** 支持响应文件 (`@files.rsp`) 和文件列表 (`--file-list`)，彻底摆脱命令行长度限制。
- **哈希一致性**：修复了旧版中哈希结果不稳定的问题，结果可靠，可重复验证。
- **JSON 输出**：正确的转义与序列化，输出立即可被其他程序解析。
- **完整记录**：所有计算配置完整记录，验证时准确还原原始环境。

### 📊 **智能分析与验证**
- **验证模式**（`verify` 子命令）：对比先前生成的哈希 JSON，输出详细报告（成功/失败/缺失/错误），精准定位问题文件。
- **图表展示**（`--stats`）：自动生成文件大小分布图、性能散点图，直观分析处理效率。
- **单位大小过滤**：支持 `1KB`、`10MB`、`0.5GiB` 等灵活参数，精确控制处理范围。
- **完整记录**：所有计算配置（并行模式、递归搜索、文件过滤、性能指标等）完整记录到JSON，验证时准确还原。

### 🖥️ **简单易用**
- **单文件发布**：基于 .NET 8 编译为独立 exe，无需安装任何运行时，即下即用。
- **实时进度条**：清晰显示当前文件、处理速度、剩余时间，让等待不再焦虑。
- **智能输出**：默认保存为带时间戳的 JSON 文件，也可输出到标准输出 (`-o -`)，支持 `json`、`text`、`ndjson` 三种格式。
- **帮助信息**：使用 `-h` 参数或无参数运行时，帮助信息首行显示当前版本号，方便用户了解版本信息。

---

## 🚀 快速开始

### 下载与安装
从 [Releases](https://gitee.com/evancheny/fast-hasher/releases/) 页面下载最新版本的 `FastHasher-v2.1.0.exe`（单文件，约19MB），或通过以下命令自行构建：

#### 本地构建
```powershell
git clone https://gitee.com/evancheny/fast-hasher.git
cd FastHasher

# 标准构建（推荐）
.\build.ps1

# 小体积构建
.\build.ps1 small

# 超小体积构建
.\build.ps1 tiny

# 清理后构建
.\build.ps1 clean
```

**系统要求**：
- Windows 操作系统
- PowerShell 5.1 或更高版本
- .NET 8.0 SDK 或更高版本

**构建产物**：
- `dist/FastHasher-v2.1.0.exe` - 标准版本（约19MB）
- `dist/FastHasher-v2.1.0.exe` - 小体积版本（约17MB）
- `dist/FastHasher-v2.1.0.exe` - 超小体积版本（约15MB）

### 交互式模式

不带任何参数运行（双击 exe），或显式加 `-i` / `--interactive`，会进入向导式菜单，全程无需记忆参数：

```powershell
# 双击 exe 等价于
.\FastHasher-v2.1.0.exe

# 显式进入交互式向导
.\FastHasher-v2.1.0.exe -i
.\FastHasher-v2.1.0.exe interactive
```

```
==================================================
  FastHasher v2.1.0  交互式模式
  输入 5 或 q 退出；Ctrl+C 可中止正在执行的任务
==================================================

请选择操作:
  1. 计算哈希
  2. 验证哈希
  3. 基准测试
  4. 查看帮助
  5. 退出
```

向导会逐项询问路径、算法、递归、线程、大小过滤、输出格式与输出路径，并在执行前打印**等价的命令行**，方便确认或直接复制成脚本复用。一次操作结束后返回菜单，可继续下一次任务。

> 输入被重定向（CI、管道）时不会进入交互式，而是直接打印帮助，避免等待输入。

### 基本用法示例

```powershell
# 计算单个文件的哈希（默认保存为 hashes_时间戳.json）
.\FastHasher-v2.1.0.exe 我的文件.txt

# 递归计算当前目录所有 txt 文件，并显示统计与图表
.\FastHasher-v2.1.0.exe *.txt -r --stats

# 8 线程并行，多算法，按大小过滤
.\FastHasher-v2.1.0.exe E:\test -a md5,sha256 -j 8 --min-size 1MB

# 结果输出到标准输出，供其他程序解析
.\FastHasher-v2.1.0.exe *.iso -a all -o - --format ndjson -q

# 纯顺序处理（单线程）
.\FastHasher-v2.1.0.exe large-files\*.dat -j 1

# 从文件列表读取路径（每行一个），避免命令行过长
.\FastHasher-v2.1.0.exe --file-list files.txt -j auto

# 使用响应文件传参（每行一个参数）
.\FastHasher-v2.1.0.exe @args.rsp

# 验证之前生成的哈希文件
.\FastHasher-v2.1.0.exe verify hashes_20260101_120000.json

# 验证并输出文本报告到指定文件
.\FastHasher-v2.1.0.exe verify hashes.json --format text -o report.txt

# 只验证路径中包含 backup 的记录
.\FastHasher-v2.1.0.exe verify hashes.json backup

# 显示帮助 / 版本
.\FastHasher-v2.1.0.exe -h
.\FastHasher-v2.1.0.exe -v
```
## 📖 命令行参数详解

```
FastHasher                    交互式向导模式（无参数即可进入）
FastHasher -i                 显式进入交互式向导
FastHasher [hash] <文件|目录|通配符>... [选项]
FastHasher verify <哈希.json> [路径过滤...] [选项]
FastHasher bench
FastHasher help | version
```

### 输入与算法

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--algorithms <列表>` | `-a` | 哈希算法，逗号分隔或 `all`（可用 `md5` `sha1` `sha256` `sha512` `blake3`，默认 `md5`） | `-a md5,sha256` |
| `--recursive` | `-r` | 递归搜索子目录 | `-r` |
| `--min-size <大小>` | - | 最小文件大小，支持 `B/KB/KiB/MB/MiB/GB/GiB/TB` 与小数 | `--min-size 1KB` |
| `--max-size <大小>` | - | 最大文件大小 | `--max-size 100MB` |
| `--file-list <文件>` | `-f` | 从文件读取路径列表，每行一个；`-` 表示标准输入 | `-f files.txt` |
| `@<响应文件>` | - | 从文件读取命令行参数，每行一个 | `@args.rsp` |

### 并发

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--threads <n \| auto>` | `-j` | 并行线程数，默认 `auto`（CPU 逻辑线程数）；`1` 表示顺序处理，上限为 CPU 逻辑线程数 × 2 | `-j 8` |

### 输出

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--output <文件 \| ->` | `-o` | 输出路径，`-` 表示标准输出；默认生成带时间戳的文件 | `-o result.json` |
| `--format <格式>` | - | 输出格式：`json`（默认）、`text`、`ndjson` | `--format ndjson` |

### 显示与日志

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--stats` | - | 显示统计信息与图表 | `--stats` |
| `--quiet` | `-q` | 只输出错误与警告 | `-q` |
| `--verbose` | - | 输出详细日志到控制台 | `--verbose` |
| `--no-color` | - | 禁用彩色输出 | `--no-color` |
| `--progress <模式>` | - | 进度显示：`auto`（默认）、`always`、`never` | `--progress never` |
| `--log <文件>` | - | 指定日志文件路径 | `--log run.log` |
| `--no-log` | - | 不写日志文件 | `--no-log` |

### 其他

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--interactive` | `-i` | 交互式向导模式 | `-i` |
| `--help` | `-h` | 显示帮助信息 | `-h` |
| `--version` | `-v` | 显示版本信息及当前 CPU 逻辑线程数 | `-v` |

### 退出码

| 退出码 | 含义 |
|--------|------|
| `0` | 全部成功 |
| `1` | 存在失败文件、缺失文件或哈希不匹配 |
| `2` | 参数错误 |
| `130` | 用户取消（Ctrl+C） |

> 已移除的旧参数：`-p/--parallel` → `-j <n>`、`-seq/--sequential` → `-j 1`、`-c/--console-output` → `-o -`、`--json-file` → `verify <json>`、`--show-time` → `--stats`、`--benchmark` → `bench`、`--batch-size` 已无作用。使用旧写法会立即报错并提示迁移方式。

## 📝 输出格式

通过 `--format` 选择输出格式（`json` 默认、`text`、`ndjson`）。使用 `-o -` 输出到标准输出时，stdout 只包含结果数据，提示、进度与日志一律写入 stderr，可直接管道给其他程序。

### 计算模式（JSON 对象）
FastHasher使用统一的JSON输出格式，包含完整的元数据和文件哈希结果：

```json
{
  "metadata": {
    "generated": "2026-03-06 13:35:54",
    "version": "2.0.0",
    "algorithms": ["MD5", "SHA256"],
    "parallel": true,
    "maxDegreeOfParallelism": 12,
    "recursive": false,
    "filePatterns": ["*.txt"],
    "minFileSize": 0,
    "maxFileSize": 9223372036854775807,
    "totalFiles": 100,
    "totalBytes": 104857600,
    "processingTimeSeconds": 5.23
  },
  "files": [
    {
      "file": "C:\\test\\example.txt",
      "filename": "example.txt",
      "size": "1024",
      "md5": "d41d8cd98f00b204e9800998ecf8427e",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "time_ms": "15.23",
      "success": "true"
    }
  ]
}
```

**元数据字段说明：**
- `generated`: 计算时间戳
- `version`: FastHasher版本号
- `algorithms`: 使用的哈希算法列表
- `parallel`: 是否使用并行处理
- `maxDegreeOfParallelism`: 并行线程数
- `recursive`: 是否递归搜索
- `filePatterns`: 文件匹配模式
- `minFileSize/maxFileSize`: 文件大小过滤范围
- `totalFiles`: 处理的文件总数
- `totalBytes`: 文件总大小
- `processingTimeSeconds`: 总处理时间
### 验证模式（JSON 对象）
```json
{
  "metadata": { "generated": "2024-01-01 12:00:00" },
  "summary": {
    "total_stored_files": 100,
    "verified_files": 98,
    "success": 95,
    "failed": 2,
    "missing": 1,
    "error": 0,
    "success_rate": "96.94"
  },
  "results": [ ... ]
}
```
## 🔍 验证模式详解
`verify` 子命令允许你将当前文件与之前生成的哈希 JSON 进行对比。它会：

自动检测 JSON 中包含的哈希算法（MD5、SHA1 等）

重新计算每个文件的哈希值

逐项比对，输出成功、失败、缺失、错误四种状态

生成详细的验证报告（JSON），包含不匹配的算法和具体哈希值

适用于数据完整性检查、备份验证等场景。

## ⚙️ 性能与并行
FastHasher 的并行策略经过精心设计：

默认使用 CPU 逻辑线程数，避免过度并行导致资源争抢。

可手动指定线程数（如 `-j 4`），上限为 CPU 逻辑线程数的 2 倍，防止过度并行导致性能下降；使用 `-j 1` 即顺序处理。

实时显示当前并行线程数，进度条中可见。

大文件流式处理 + 动态缓冲区：根据文件大小自动选择最优缓冲区（1MB~10MB），兼顾速度和内存。
## ❓ 常见问题

### Q：中文文件名或路径会乱码吗？
**A**：不会。所有内部处理均使用 UTF-8 编码，控制台输出也已设置为 UTF-8，完美支持中文文件名和路径。

### Q：命令行参数太长怎么办？
**A**：可以使用 `--file-list` 将文件列表写入文件，或使用响应文件 `@files.rsp` 传递参数，彻底摆脱命令行长度限制。

### Q：支持 Linux/macOS 吗？
**A**：项目基于 .NET 8，理论上可跨平台。但当前构建脚本仅针对 Windows，欢迎贡献跨平台支持。

### Q：如何选择合适的并行线程数？
**A**：默认使用 CPU 逻辑线程数是最优选择。如果需要手动调整，建议不超过 CPU 线程数的 2 倍，以避免过度并行导致资源争抢。

### Q：JSON输出中的元数据有什么用？
**A**：元数据记录了计算时的完整配置信息，包括：
- 计算时间和版本号
- 使用的哈希算法
- 并行处理配置
- 文件过滤条件
- 性能指标

这些信息在验证模式下非常重要，可以确保验证环境与原始计算环境一致，提高验证的准确性。

### Q：验证模式显示的原始配置不准确怎么办？
**A**：该问题在 v1.2.1 中已修复，v2.0.0 起所有 23 个 metadata 字段都能正确序列化到 JSON 文件中。如果使用的是旧版本，建议升级到 v2.0.0 或更高版本。

### Q：不同构建模式的区别？
**A**：
- **标准模式**：约 19MB，功能完整，启动快，适合日常使用
- **小体积模式**：约 17MB，启用程序集裁剪，适合分发部署
- **超小体积模式**：约 15MB，最大程度裁剪和压缩，适合网络分发，启动略慢

## 📄 许可证
本项目采用 MIT 许可证。

## 🚀 版本信息

### 当前版本：v2.1.0
- **发布日期**：2026-03-06
- **主要更新**：
  - 🐛 修复了JSON序列化metadata字段丢失的关键问题
  - ✨ 增强了JSON序列化，所有23个metadata字段都能正确记录
  - 🔧 消除了所有编译警告（从86个到0个）
  - 🛠️ 优化了构建脚本，统一使用PowerShell
  - 📝 完善了代码注释，提升了代码可读性
  - 🎨 改进了帮助信息显示，现在包含版本号


## 📚 相关文档
- [CHANGELOG.md](CHANGELOG.md) - 版本更新日志
- [BUILD.md](BUILD.md) - 构建指南和构建模式说明

## 🙏 致谢
此项目本来是自用工具，用于解决海量文件的管理问题。在除夕之际开源，希望能帮助到有类似需求的用户。如果你也有校验下载文件、备份对比的需求，欢迎试试看！

作者水平有限，代码简陋，功能初成，请大家多提意见！
