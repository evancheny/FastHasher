# 更新日志

本项目版本号格式为 `主版本.次版本.修订号`，版本来源统一为 `src/FastHasher/VersionInfo.cs` 与 `FastHasher.csproj`。

## [2.2.1] - 2026-11-09

### 修复

- **FIFO / 设备文件导致永久阻塞**：枚举与计算前用 `fstat` 判断文件类型，跳过 FIFO、字符/块设备与套接字（新增 `FileTypeHelper`）。此前目标目录中只要存在一个无名管道，进程就会挂死。
- **零匹配静默通过**：路径写错、空目录等「一个文件都没找到」的情况此前返回 `0`，CI 中会误判为成功；现返回 `1`。`verify` 在报告里没有可验证记录时同样返回 `1`。
- **JSON 路径不可移植**：`file` 字段由绝对路径改为相对当前工作目录（跨盘符时回退绝对路径），报告与文件一起搬到其它目录/机器后仍可 `verify`；`verify` 增加路径兜底：相对当前目录 → 相对报告所在目录 → 尾部路径片段匹配。

### 变更

- 版本号提升至 2.2.1，`InformationalVersion` 更新为「2.2.1 - 修复非常规文件阻塞与路径可移植性」。

## [2.2.0] - 2026-11-09

### 新增

- **Linux x64 支持**：新增 `build.sh`（标准 / small / tiny / clean，可指定 RID），产出自包含单文件 ELF；Windows 上可直接交叉编译：`.\build.ps1 linux-x64`。
- 版本号由 2.1.0 提升至 2.2.0，`InformationalVersion` 更新为「2.2.0 - 新增 Linux x64 支持」。
- 构建脚本支持指定目标 RID，产物按平台命名：`FastHasher-v<版本>.exe`（Windows）与 `FastHasher-v<版本>`（Linux）。

### 变更

- csproj 移除写死的 `RuntimeIdentifier`、`SelfContained`、`PlatformTarget`，改由脚本通过 `-r` 与 `--self-contained` 传入；程序图标仅在对 Windows 目标发布时生效。
- `build.ps1` 版本号改为从 csproj 的 `<Version>` 读取，不再执行产物（交叉编译产物无法在本机运行）。
- `build.ps1` 保存为 UTF-8 with BOM，避免 PowerShell 5.1 按 ANSI 解析导致中文注释吞行。
- 重复构建时先删除同名产物再重命名，修复二次构建报「文件已存在」。

### 修复

- Linux 文件名大小写敏感，文件去重统一按 `OrdinalIgnoreCase` 会把 `A.txt` 与 `a.txt` 当成同一文件而漏算；现按平台选择比较器。
- 元数据中的操作系统信息在 Unix 下只有内核版本，改用 `RuntimeInformation.OSDescription`。
- 输入被重定向时，`-h` 后不再提示「按任意键退出」。

## [2.1.0] - 2026-11-09

### 新增

- **BLAKE3 算法**：`-a blake3`，`-a all` 自动包含。采用纯托管 `Blake3 3.0.2` 包，无原生 DLL，单文件发布无额外依赖。
  - 小文件路径使用 `Hasher.Hash`，大文件（>10MB）使用 `Blake3HashAlgorithm` 流式分块，两条路径结果一致。
  - 空输入与 `"abc"` 均通过 BLAKE3 官方测试向量校验。
- 帮助文本、`bench` 基准测试、交互式向导的算法列表同步支持 BLAKE3。

### 变更

- 版本号由 2.0.0 提升至 2.1.0，`InformationalVersion` 更新为「2.1.0 - 新增 BLAKE3 支持」。

## [2.0.0] - 2026-11-09

破坏性重构，旧参数不再静默忽略，使用时会报错（退出码 2）并提示替代写法。

### 新增

- **交互式向导**（`src/FastHasher/InteractiveShell.cs`）：无参数运行或 `-i` / `--interactive` / `interactive` 进入。提供计算、验证、基准测试三套问答流程，执行前打印等价命令行，执行后返回菜单。
- **统一参数模型**（`src/FastHasher/CliOptions.cs`）：`CliOptions` + `CliParser`，集中处理响应文件展开、子命令识别、单位解析、算法校验、废弃参数迁移提示与未知参数拼写建议。
- `--format json|text|ndjson`，计算与验证均支持三种输出。
- `-o -` 输出到标准输出；此时 stdout 仅含结果数据。
- `-q/--quiet`、`--verbose`、`--no-color`、`--progress auto|always|never`、`--log <file>`、`--no-log`。
- 统一退出码：`0` 成功 / `1` 存在失败或缺失 / `2` 参数错误 / `130` 用户取消。
- 多路径过滤：`verify hashes.json backup D:\data` 支持多个过滤词。

### 变更

- 模式：`--verify` / `--json-file` → `verify <json>` 子命令。
- 并发：`-p [n]` / `-seq` → `-j <n|auto>`，`-j 1` 即顺序处理，上限为 CPU 逻辑线程数 × 2。
- 输出：`-c` → `-o -`；输出路径与日志路径统一解析，默认落在输出文件同目录。
- 统计：`--show-time` 与旧 `-s` → `--stats`。
- 基准：`--benchmark` → `bench` 子命令。
- 所有提示、进度条、日志改写入 stderr，避免污染 stdout 的结果数据。
- 程序入口改为只做环境初始化与子命令分发。

### 移除

- `--batch-size`：解析后从未生效，直接删除。

### 修复

- 设置 `Console.InputEncoding` 会重建 stdin reader，在输入被重定向时吞掉首行；现仅在交互终端下设置。
- 脚本输入首行的 UTF-8 BOM 导致首个参数解析失败。
- 进度行与降级输出写入 stdout，破坏 `-o -` 的 JSON 可解析性。

## [1.0.1] - 早期版本

- 修复根目录读取错误。
- 构建脚本与文件处理逻辑调整。

## [1.0.0] - 早期版本

- 首个发布版本：MD5 / SHA1 / SHA256 / SHA512 计算、并行处理、JSON 输出与验证模式。
