# FastHasher – ⚡ 极速哈希计算与验证工具

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20x64-blue?style=flat-square" alt="Windows | Linux x64" />
  <img src="https://img.shields.io/badge/license-Apache--2.0-green?style=flat-square" alt="Apache-2.0" />
  <img src="https://img.shields.io/badge/version-2.2.0-orange?style=flat-square" alt="v2.2.0" />
</p>

<p align="center">
  <b>FastHasher</b> 是一款为高性能而生的文件哈希计算与验证工具，支持 MD5、SHA1、SHA256、SHA512、BLAKE3。<br>
  它能批量处理成千上万个文件，一键校验完整性，并输出可被程序直接解析的 JSON / NDJSON 报告。<br>
  单文件程序（Windows exe / Linux ELF），无需安装运行时，命令行与交互式向导双入口。
</p>

---

## ✨ 特性亮点

### 🚀 性能
- **并行计算**：默认按 CPU 逻辑线程数并行，`-j` 可指定线程数，`-j 1` 退化为顺序处理。
- **动态缓冲区**：≤10MB 的文件一次性读入内存计算；更大的文件走流式分块，缓冲区按文件大小在 1–10MB 间自动选择。
- **多算法单次读取**：一次遍历文件即同时算出全部所选算法，不重复读盘。
- **ReadyToRun 发布**：默认启用 R2R 预编译，冷启动更快。

### 🧭 易用
- **交互式向导**：无参数运行（Windows 双击、Linux 直接执行）或加 `-i` 进入菜单，逐项问答，执行前打印等价命令行，可直接复制成脚本。
- **双入口**：命令行适合批处理与自动化，向导适合偶尔使用、记不住参数。
- **长参数不再是问题**：`--file-list` 从文件读路径（`-f -` 读标准输入），`@args.rsp` 从文件读参数。
- **智能输出**：默认生成 `hashes_<时间戳>.json`；`-o -` 输出到标准输出，支持 `json` / `text` / `ndjson` 三种格式。

### 🔍 可靠
- **输出与提示分流**：stdout 只写结果，进度条、提示、日志一律走 stderr，可直接管道给其他程序。
- **验证模式**：`verify` 子命令自动识别 JSON 中的算法，逐文件重算比对，区分成功/失败/缺失/错误四态。
- **退出码可用**：`0` 通过 / `1` 有问题项 / `2` 参数错误 / `130` 用户取消，便于 CI 判断。
- **UTF-8 全链路**：中文文件名、路径、内容不乱码，JSON 输出无 BOM、转义正确。
- **完整元数据**：算法、并发、过滤条件、耗时、体积等配置完整写入 JSON，验证时可还原原始环境。

---

## 🚀 快速开始

### 下载

从 Releases 页面下载对应平台的单文件程序：

| 平台 | 产物 | 体积 |
|---|---|---|
| Windows x64 | `FastHasher-v2.2.0.exe` | 约 19.3MB |
| Linux x64 | `FastHasher-v2.2.0`（无扩展名） | 约 20.1MB |

- GitHub：[github.com/evancheny/FastHasher/releases](https://github.com/evancheny/FastHasher/releases)
- Gitee：[gitee.com/evancheny/fast-hasher/releases](https://gitee.com/evancheny/fast-hasher/releases)

无需安装 .NET 运行时。Windows 双击 exe、Linux `chmod +x` 后直接运行，不带参数即进入交互式向导。

### 本地构建

```powershell
# GitHub
git clone https://github.com/evancheny/FastHasher.git
# 或 Gitee
git clone https://gitee.com/evancheny/fast-hasher.git

cd FastHasher

# Windows
.\build.ps1          # 标准构建（推荐）
.\build.ps1 small    # 小体积构建（额外裁剪）
.\build.ps1 tiny     # 超小体积构建（裁剪 + 单文件压缩）
.\build.ps1 clean    # 先清理 bin/obj/dist 再标准构建

# 在 Windows 上交叉编译 Linux 产物（无需 Linux 环境）
.\build.ps1 linux-x64
```

```bash
# Linux（需 .NET 8 SDK）
chmod +x build.sh
./build.sh           # 标准构建，默认目标 linux-x64
./build.sh small
./build.sh tiny
./build.sh clean
./build.sh linux-arm64   # 指定其它 RID
```

**系统要求**

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows x64；Linux x64（glibc ≥ 2.28，OpenSSL 1.1.1+/3.x） |
| 构建环境 | Windows 用 PowerShell 5.1+；Linux 用 bash |
| SDK | .NET 8.0 SDK 或更高 |

**构建产物**

文件名由版本号与目标平台决定，每次构建只产出一个文件：

| 目标 | 文件名 | 体积 |
|---|---|---|
| Windows x64 | `dist/FastHasher-v<版本>.exe` | 约 19.3MB（v2.2.0 实测） |
| Linux x64 | `dist/FastHasher-v<版本>` | 约 20.1MB（v2.2.0 实测） |

| 模式 | 差异 |
|---|---|
| 标准 | 功能完整，日常使用 |
| small | 显式启用裁剪链接，体积更小 |
| tiny | 裁剪 + 单文件压缩，体积最小，启动略慢 |

> 同一平台下三种模式写入同一个 `dist` 目录且文件名相同，连续构建会相互覆盖；需要同时保留时请构建后改名。Windows 与 Linux 产物文件名不同，可以共存于 `dist`。

### Linux 使用说明

```bash
# 赋予可执行位（zip 解压会丢失，tar.gz 会保留）
chmod +x FastHasher-v2.2.0

./FastHasher-v2.2.0 --version
./FastHasher-v2.2.0 /data -a blake3 -j 8 -o result.json
./FastHasher-v2.2.0 /data -a all -o - --format ndjson -q --no-log > out.ndjson
```

- **自包含**：产物内含 .NET 运行时，无需安装 .NET；但依赖系统 `glibc`（≥ 2.28）与 `OpenSSL`（1.1.1+ 或 3.x）。已启用 `InvariantGlobalization`，不依赖 ICU。
- **当前状态**：Linux 产物已完成交叉编译与 ELF 校验，尚未在 Linux 实机回归；首次使用建议先跑 `--version`、`-a all` 与 `verify` 三项自测。
- **Alpine / musl** 无法直接运行 glibc 产物，需另行构建：`./build.sh linux-musl-x64`。
- 与 Windows 的行为差异：
  - 路径**严格区分大小写**：`A.txt` 与 `a.txt` 是两个不同文件（Windows 下视为同一个）。
  - 以 `.` 开头的隐藏文件默认跳过，与 Windows 的「隐藏文件」策略保持一致。
  - 通配符由程序自身展开，shell 已展开时同样可用。
  - 交互式向导、颜色、进度条在标准终端下正常；输出被重定向时进度条自动退化为单行刷新。

### 交互式模式

不带任何参数运行（双击可执行文件），或显式加 `-i` / `--interactive`：

```powershell
.\FastHasher-v2.2.0.exe          # 双击即进入向导
.\FastHasher-v2.2.0.exe -i
.\FastHasher-v2.2.0.exe interactive
```

```
==================================================
  FastHasher v2.2.0  交互式模式
  输入 5 或 q 退出；Ctrl+C 可中止正在执行的任务
==================================================

请选择操作:
  1. 计算哈希
  2. 验证哈希
  3. 基准测试
  4. 查看帮助
  5. 退出
```

- **计算向导**：路径（支持空格分隔多个路径）→ 算法 → 递归 → 线程 → 大小过滤 → 输出格式 → 输出路径 → 是否显示统计。
- **验证向导**：JSON 路径（直接回车列出当前目录最近 10 个 JSON 供选序号）→ 路径过滤 → 输出格式 → 输出路径。
- 执行前打印**等价命令行**，方便确认或复用；执行完返回菜单，可连续操作。
- 输入被重定向（CI、管道）时不会进入向导，而是打印帮助，避免卡住。

### 常用命令

```powershell
# 计算单个文件（默认生成 hashes_<时间戳>.json）
.\FastHasher-v2.2.0.exe 我的文件.txt

# 递归处理当前目录所有 txt，并显示统计与图表
.\FastHasher-v2.2.0.exe *.txt -r --stats

# 8 线程，多算法，按大小过滤
.\FastHasher-v2.2.0.exe E:\test -a md5,sha256 -j 8 --min-size 1MB

# 全算法（md5/sha1/sha256/sha512/blake3），结果以 NDJSON 输出到 stdout
.\FastHasher-v2.2.0.exe *.iso -a all -o - --format ndjson -q

# 纯顺序处理
.\FastHasher-v2.2.0.exe large-files\*.dat -j 1

# 从文件列表读路径；文件列表为 - 时读标准输入
.\FastHasher-v2.2.0.exe --file-list files.txt -j auto
Get-ChildItem *.iso | ForEach-Object FullName | .\FastHasher-v2.2.0.exe -f -

# 响应文件传参（每行一个参数）
.\FastHasher-v2.2.0.exe @args.rsp

# 验证：默认生成 <原名>_verify_<时间戳>.json
.\FastHasher-v2.2.0.exe verify hashes_20260101_120000.json

# 验证并输出文本报告到指定文件
.\FastHasher-v2.2.0.exe verify hashes.json --format text -o report.txt

# 只验证路径中含 backup 的记录
.\FastHasher-v2.2.0.exe verify hashes.json backup

# 帮助 / 版本
.\FastHasher-v2.2.0.exe -h
.\FastHasher-v2.2.0.exe -v
```

---

## 📖 命令行参数

### 用法

```
FastHasher                            交互式向导（无参数即进入，输入被重定向时打印帮助）
FastHasher -i                         显式进入交互式向导
FastHasher [hash] <文件|目录|通配符>... [选项]
FastHasher verify <哈希.json> [路径过滤...] [选项]
FastHasher bench
FastHasher help | version
```

> 第一个位置参数若恰为 `hash` 会被当作子命令忽略，实际计算直接写路径即可。

### 输入与算法

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--algorithms <列表>` | `-a` | 哈希算法，逗号/分号/空格分隔，或 `all` 展开全部。可用 `md5` `sha1` `sha256` `sha512` `blake3`，默认 `md5` | `-a md5,blake3` |
| `--recursive` | `-r` | 递归搜索子目录 | `-r` |
| `--min-size <大小>` | - | 最小文件大小，支持 `B/KB/KiB/MB/MiB/GB/GiB/TB/TiB` 与小数 | `--min-size 1KB` |
| `--max-size <大小>` | - | 最大文件大小 | `--max-size 100MB` |
| `--file-list <文件>` | `-f` | 从文件读路径列表，每行一个，`#` 开头为注释；`-` 表示标准输入 | `-f files.txt` |
| `@<响应文件>` | - | 从文件读命令行参数，每行一个 | `@args.rsp` |
| `--` | - | 终止选项解析，其后全部按路径处理（用于以 `-` 开头的文件名，须放在所有选项之后） | `-q -o - -- -weird.txt` |

### 并发

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--threads <n \| auto>` | `-j` | 并行线程数。默认 `auto`（= CPU 逻辑线程数）；`1` 为顺序处理；上限为 CPU 逻辑线程数 × 2，超出会提示并截断 | `-j 8` |

### 输出

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--output <文件 \| ->` | `-o` | 输出路径；`-` 表示标准输出；缺省时自动生成带时间戳的文件 | `-o result.json` |
| `--format <格式>` | - | `json`（默认，单对象）、`text`（人类可读）、`ndjson`（每行一个对象） | `--format ndjson` |

### 显示与日志

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--stats` | - | 显示统计信息与图表（大小分布、性能散点） | `--stats` |
| `--progress <模式>` | - | 进度条：`auto`（默认，非重定向且非静默时显示）、`always`、`never` | `--progress never` |
| `--quiet` | `-q` | 只输出错误与警告 | `-q` |
| `--verbose` | - | 输出详细日志到 stderr | `--verbose` |
| `--no-color` | - | 禁用彩色输出 | `--no-color` |
| `--log <文件>` | - | 指定日志文件路径 | `--log run.log` |
| `--no-log` | - | 不写日志文件 | `--no-log` |

### 其他

| 参数 | 简写 | 说明 | 示例 |
|------|------|------|------|
| `--interactive` | `-i` | 交互式向导模式（`interactive` / `ui` 子命令等效） | `-i` |
| `--help` | `-h` | 显示帮助 | `-h` |
| `--version` | `-v` | 显示版本与 CPU 逻辑线程数 | `-v` |

### 退出码

| 退出码 | 含义 |
|--------|------|
| `0` | 全部成功 |
| `1` | 存在失败文件、缺失文件或哈希不匹配（验证不通过） |
| `2` | 参数错误、文件不存在、验证异常 |
| `130` | 用户按 Ctrl+C 取消 |

### 已废弃参数

旧写法会立即报错（退出码 2）并提示迁移方式，不再静默忽略：

| 旧参数 | 新写法 |
|---|---|
| `-p/--parallel [n]` | `-j <n>`（`-j 1` 即顺序） |
| `-seq/--sequential` | `-j 1` |
| `-c/--console-output` | `-o -` |
| `--json-file <json>` | `verify <json>` |
| `--show-time` | `--stats` |
| `--benchmark` | `bench` 子命令 |
| `--batch-size` | 已移除，无替代 |

---

## 📝 输出格式

`-o -` 输出到标准输出时，**stdout 只有结果数据**，所有提示、进度条、日志均写入 stderr，因此 `... -o - -q --no-log | ConvertFrom-Json` 可直接解析。

### 默认文件命名

| 场景 | 默认路径 |
|---|---|
| 计算（json / text / ndjson） | `./hashes_<yyyyMMdd_HHmmss>.json` / `.txt` / `.ndjson` |
| 验证报告 | 与待验证 JSON 同目录，`<原名>_verify_<yyyyMMdd_HHmmss>.json` / `.txt` / `.ndjson` |
| 计算日志 | 与输出文件同目录，`FastHasher_<yyyyMMdd_HHmmss>.log` |
| 验证日志 | 与输出文件同目录，`FastHasher_Verify_<yyyyMMdd_HHmmss>.log` |

`--no-log` 可完全关闭日志落盘。

### JSON：计算模式

```json
{
  "metadata": {
    "generated": "2026-11-09 10:30:12",
    "version": "2.2.0",
    "algorithms": ["MD5", "BLAKE3"],
    "parallel": true,
    "maxDegreeOfParallelism": 12,
    "recursive": false,
    "filePatterns": ["*.iso"],
    "minFileSize": 0,
    "maxFileSize": 9223372036854775807,
    "totalFiles": 100,
    "totalBytes": 104857600,
    "processingTimeSeconds": 5.23
  },
  "files": [
    {
      "file": "C:\\test\\example.iso",
      "filename": "example.iso",
      "size": "1024",
      "md5": "d41d8cd98f00b204e9800998ecf8427e",
      "blake3": "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262",
      "time_ms": "15.23",
      "success": "true"
    }
  ]
}
```

**metadata 字段：**`generated` 生成时间、`version` 版本号、`algorithms` 算法列表、`parallel` 是否并行、`maxDegreeOfParallelism` 线程数、`recursive` 是否递归、`filePatterns` 输入模式、`minFileSize`/`maxFileSize` 大小范围、`totalFiles` 文件数、`totalBytes` 总字节、`processingTimeSeconds` 总耗时。

失败文件不会中断整体流程，对应条目为 `"success": "false"` 并带 `error`、`error_type`。

### text：人类可读

```
FastHasher 2.2.0 | 生成时间: 2026-11-09 10:30:12
算法: MD5, BLAKE3 | 文件数: 2 | 总大小: 1.5 MB | 用时: 0.42 秒

C:\test\a.iso
  大小: 1.0 MB | 用时: 120 ms
  MD5     : d41d8cd98f00b204e9800998ecf8427e
  BLAKE3  : af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262
```

验证的 text 报告会额外列出「问题文件」段，逐项给出失败类型、期望值与实际值。

### ndjson：每行一个对象

计算模式每行是一条文件记录，验证模式每行是一条验证结果。适合 `jq`、日志管道、流式处理大结果集。

### JSON：验证模式

```json
{
  "metadata": { "generated": "2026-11-09 10:35:00" },
  "summary": {
    "total_stored_files": 100,
    "verified_files": 98,
    "success": 95,
    "failed": 2,
    "missing": 1,
    "error": 0,
    "success_rate": "96.94"
  },
  "results": [ ]
}
```

---

## 🔍 验证模式详解

`verify` 子命令把当前文件与之前生成的哈希 JSON 对比：

1. 读取 JSON 的 `metadata`，还原当时的算法、并发与过滤配置；
2. 从 `files` 每条记录中自动识别算法（支持 MD5 / SHA1 / SHA256 / SHA512 / BLAKE3），未出现在记录中的算法不参与比对；
3. 重新计算并逐项比对，得到四种状态：`success` / `failed`（哈希不匹配）/ `missing`（文件缺失）/ `error`（读取或计算失败）；
4. 同时比对文件大小，不一致时该记录直接判为 `failed`，错误信息为「文件大小不匹配」；
5. 按 `--format` 输出 JSON / text / ndjson 报告，退出码反映结果：有任一失败、缺失或错误即为 `1`。

**路径过滤**：`verify hashes.json backup D:\data` —— 多个过滤词为「或」关系，匹配 `file` 或 `full_path` 字段（忽略大小写），适合只复验某个子目录。

---

## ⚙️ 性能与并行

- **默认并发**：`-j auto`，取 CPU 逻辑线程数。
- **上限保护**：指定值超过 CPU 逻辑线程数 × 2 时会被截断并提示，避免线程爆炸。
- **策略切换**：文件 ≤10MB 一次性读入内存，用 `HashData` 系列零分配计算；>10MB 走 `TransformBlock` 流式分块。
- **缓冲区**：按文件大小选择 1/2/4/8/10MB。
- **进度开销**：进度刷新限流到 200ms 一次，避免频繁写终端拖慢计算。

调参建议：SSD 上 `-j` 超过物理核数后收益递减；机械盘与网络盘受 IO 限制，建议 `-j 2`~`-j 4`，线程过多反而因寻道与带宽争抢变慢。

---

## ❓ 常见问题

### Q：中文文件名或路径会乱码吗？
**A**：不会。源码、文件读写、控制台输出统一 UTF-8；输出文件不带 BOM。

### Q：命令行太长怎么办？
**A**：`--file-list files.txt`（每行一个路径，`-f -` 读标准输入），或 `@args.rsp` 把参数写进文件（每行一个参数）。

### Q：为什么 `-o -` 的输出里混进了提示信息？
**A**：v2.0.0 起已彻底分离：结果只走 stdout，提示/进度/日志只走 stderr。若仍看到混入，说明用的是旧版本。

### Q：BLAKE3 需要额外的原生库吗？
**A**：不需要。使用的是纯托管 `Blake3` 包，单文件 exe 内直接包含，无 DLL 释放、无平台依赖，输出为 32 字节（64 位十六进制）。

### Q：支持 Linux/macOS 吗？
**A**：Linux x64 已支持，提供自包含单文件产物与 `build.sh`（约 20.1MB，要求 glibc ≥ 2.28、OpenSSL 1.1.1+/3.x）。macOS 未验证：`dotnet publish -r osx-x64` 理论上可产出，但未做回归测试，且图标、进度条等细节未适配。

### Q：三种构建模式怎么选？
**A**：默认用标准模式；要分发给他人、传输体积敏感时用 `tiny`。裁剪模式对反射序列化有风险，本项目的 `Linker.xml` 与 `TrimmerRootAssembly` 已为 `System.Text.Json`、`Blake3`、`System.IO.Hashing` 做了保留，但仍建议发布后跑一次计算 + 验证回归。

### Q：JSON 里的 metadata 有什么用？
**A**：记录计算当时的版本、算法、并发、过滤条件与耗时。验证时据此还原算法集合，避免用错算法导致误判。v1.2.1 曾修复 metadata 字段丢失问题，v2.x 全部字段均可正常往返。

---

## 📄 许可证

本项目采用 **Apache License 2.0**，详见 [LICENSE](LICENSE)。

## 🚀 版本信息

### 当前版本：v2.2.0

- **新增 BLAKE3**：`-a blake3` / `-a all`，纯托管实现，大小文件两条路径均已验证官方向量。
- **交互式向导**：无参数或 `-i` 进入，计算/验证/基准三套问答流程，执行前预览等价命令行。
- **参数体系重构（2.0.0）**：统一为 `-j` 并发、`-o` 输出、`--format` 格式化、`verify` 子命令；旧参数报错并提示迁移；新增 `-q/--verbose`、`--progress`、`--log/--no-log`、`--no-color`。
- **输出可靠性**：stdout 与 stderr 彻底分流，UTF-8 无 BOM，统一退出码 `0/1/2/130`。
- **Linux 支持**：新增 `build.sh`（标准 / small / tiny / clean，可指定 RID），交叉编译自包含单文件产物；路径比较按平台区分大小写，OS 信息改用 `OSDescription`。

完整历史见 [CHANGELOG.md](CHANGELOG.md)。

## 📚 相关文档

- [CHANGELOG.md](CHANGELOG.md) —— 版本更新日志
- [BUILD.md](BUILD.md) —— 构建指南、发布参数与技术细节

## 🙏 致谢

此项目本来是自用工具，用于解决海量文件的管理问题。希望能帮助到有类似需求的用户。如果你也有校验下载文件、备份对比的需求，欢迎试试看！

作者水平有限，代码简陋，功能初成，请大家多提意见！
