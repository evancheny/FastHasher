# FastHasher 构建指南

## 概述

构建由两个参数等价的脚本驱动：

| 平台 | 脚本 | 默认目标 |
|---|---|---|
| Windows | `build.ps1` | `win-x64` |
| Linux / macOS | `build.sh` | `linux-x64` |

两者都调用 `dotnet publish` 生成自包含单文件程序，再按 csproj 中的 `<Version>` 重命名为 `FastHasher-v<版本>`（Windows 带 `.exe` 后缀）。版本号的唯一来源是 `src/FastHasher/VersionInfo.cs` 与 `FastHasher.csproj`，两处需保持一致。

> RID 与自包含不再写死在 csproj 中，统一由脚本通过 `-r` 传入，因此同一份源码可产出 Windows 与 Linux 产物。

## 系统要求

| 项目 | 要求 |
|---|---|
| 操作系统 | 任意（Windows 可直接交叉编译 Linux 产物） |
| Windows 构建 | PowerShell 5.1 或更高 |
| Linux 构建 | bash 4 或更高 |
| SDK | .NET 8.0 SDK 或更高 |
| 网络 | 首次构建需还原 NuGet 包与目标 RID 的 runtime pack |
| Linux 运行环境 | glibc ≥ 2.28，OpenSSL 1.1.1+ 或 3.x（已启用 `InvariantGlobalization`，不依赖 ICU） |

## 构建模式

两个脚本参数一致：

| 模式 | Windows | Linux |
|---|---|---|
| 标准 | `.\build.ps1` | `./build.sh` |
| 裁剪 | `.\build.ps1 small` | `./build.sh small` |
| 裁剪 + 压缩 | `.\build.ps1 tiny` | `./build.sh tiny` |
| 清理后构建 | `.\build.ps1 clean` | `./build.sh clean` |
| 指定 RID | `.\build.ps1 linux-x64` | `./build.sh linux-arm64` |

### 1. 标准模式

```powershell
.\build.ps1        # Windows
```

```bash
./build.sh         # Linux
```

- 完整功能，未额外加裁剪/压缩参数（裁剪与 R2R 由 csproj 决定）
- Windows 约 19.3MB（v2.2.1 实测 20,280,631 字节）；Linux 约 20.1MB（21,045,422 字节）
- 适合日常使用与分发

**输出**：`dist/FastHasher-v<版本>.exe`（Windows）或 `dist/FastHasher-v<版本>`（Linux）

### 2. 小体积模式

```powershell
.\build.ps1 small
```

- 显式追加 `-p:PublishTrimmed=true -p:TrimMode=link`
- 体积小于标准版
- 适合分发部署

**输出**：与标准模式同名

### 3. 超小体积模式

```powershell
.\build.ps1 tiny
```

- 在小体积模式基础上追加 `-p:EnableCompressionInSingleFile=true`
- 体积最小，启动时需解压，首次启动略慢
- 适合网络分发

**输出**：与标准模式同名

> ⚠️ 同一 RID 下三种模式写入同一个 `dist` 目录且文件名相同，互相覆盖。不同 RID 的产物名不同（`.exe` / 无后缀），可以共存于 `dist`。

### 4. 清理并重新构建

```powershell
.\build.ps1 clean
```

```bash
./build.sh clean
```

删除 `src/FastHasher/bin`、`src/FastHasher/obj`、`dist` 后按标准模式重建。构建出现异常时优先用它排除增量编译干扰。

### 参数组合

`clean` 可与模式参数组合，顺序无关：

```powershell
.\build.ps1 clean tiny    # 清理后构建超小体积版本
.\build.ps1 tiny clean    # 效果相同
```

## 构建流程

1. **解析参数**：识别 `clean` / `small` / `tiny` 与可选的 RID（`linux-x64`、`linux-arm64`、`win-x64` 等），缺省为标准模式 + 平台默认 RID。
2. **清理**（可选）：删除 `src/FastHasher/bin`、`src/FastHasher/obj`、`dist`。
3. **发布**：`dotnet publish -r <RID> --self-contained`，按模式追加裁剪/压缩参数。
4. **解析版本**：从 `src/FastHasher/FastHasher.csproj` 的 `<Version>` 读取版本号（**不执行产物**，否则交叉编译时无法读取）。
5. **重命名**：`win-*` 产物改名为 `FastHasher-v<版本>.exe`，其它 RID 为 `FastHasher-v<版本>`。
6. **冒烟测试**：打印字节数；若产物能在当前平台执行，再跑一次 `--version`，否则提示到目标平台验证。

## 构建产物

```
dist/
├── FastHasher-v2.2.1.exe   # Windows x64：单文件、自包含
└── FastHasher-v2.2.1       # Linux x64：ELF、需 chmod +x
```

`dist/` 已被 `.gitignore` 排除，产物不进版本库；发版通过 GitHub / Gitee 的 Release 附件分发。

## 交叉编译 Linux 产物

在 Windows 上即可完成，无需 Linux 环境或 WSL：

```powershell
.\build.ps1 linux-x64          # 标准
.\build.ps1 clean tiny linux-x64
```

等价的原始命令：

```powershell
dotnet publish src\FastHasher\FastHasher.csproj `
  -c Release -r linux-x64 --self-contained true -o dist `
  -p:PublishSingleFile=true -p:DebugType=none
```

注意事项：

- 首次会下载 `linux-x64` 的 runtime pack（约 100MB+），之后可离线复用。
- 产物是 ELF 可执行文件（头部 `7F 45 4C 46`），**无法在 Windows 上运行**，脚本会跳过冒烟测试并提示。
- 用官方 runtime pack 交叉编译，产物要求目标机 glibc ≥ 2.28、OpenSSL 1.1.1+/3.x（规划基线为 Ubuntu 22.04 / glibc 2.35）。
- 实测环境：Rocky Linux 10.2（glibc 2.39、OpenSSL 3.5.5、x86_64），18 项回归 + 哈希对拍（`md5sum` / `sha1sum` / `sha256sum` / `sha512sum`）全部通过，SIGINT 取消退出码 `130`。
- 非常规文件判定使用 `fstat`（glibc ≥ 2.33 提供该符号）：更低的 glibc 上会自动退化为「不检查」，不会崩溃，但目录中的 FIFO 仍可能阻塞。
- 复制到目标机后如无执行权限：`chmod +x FastHasher-v2.2.1`。用 `tar.gz` 分发可保留权限，zip 会丢失。

## 发布参数

两个脚本内部执行的等价命令（Windows 示例）：

```powershell
dotnet publish src\FastHasher\FastHasher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained `
  --output .\dist `
  -p:PublishSingleFile=true `
  -p:DebugType=none
```

模式追加参数：

| 模式 | 追加参数 |
|---|---|
| small | `-p:PublishTrimmed=true -p:TrimMode=link` |
| tiny | `-p:PublishTrimmed=true -p:TrimMode=link -p:EnableCompressionInSingleFile=true` |

## csproj 关键配置

| 配置 | 值 | 作用 |
|---|---|---|
| `PublishSingleFile` | `true` | 合并为单个可执行文件 |
| `SelfContained` | 由脚本 `--self-contained` 传入 | 自带运行时，目标机无需装 .NET |
| `RuntimeIdentifier` | 由脚本 `-r` 传入 | 决定目标平台；csproj 不再写死，否则无法产出 Linux 包 |
| `PlatformTarget` | 未设置 | 交由 RID 决定，避免与 `linux-arm64` 等目标冲突 |
| `ApplicationIcon` | 仅 `win-x64` 时设置 | 图标为 Windows 专用，条件化后不影响 Linux 产物 |
| `PublishTrimmed` / `TrimMode` | `true` / `link` | 裁剪未使用程序集 |
| `PublishReadyToRun` | `true` | 预编译，降低冷启动耗时 |
| `IncludeNativeLibrariesForSelfExtract` | `true` | 原生库内嵌并在运行时解压 |
| `InvariantGlobalization` | `true` | 去掉 ICU 数据，减小体积（不影响大小写不敏感比较等基础行为） |
| `JsonSerializerIsReflectionEnabledByDefault` | `true` | 保证 System.Text.Json 反射序列化可用 |
| `TrimmerRootAssembly` | `System.Text.Json`、`System.IO.Hashing`、`Blake3` | 防止裁剪误删运行时所需成员 |
| `TrimmerRootDescriptor` | `Linker.xml` | 显式保留序列化相关类型 |
| `DebugType` | `none` | 不生成 pdb，减小体积 |

**依赖包**：`System.IO.Hashing 8.0.0`、`Blake3 3.0.2`（纯托管实现，无原生 DLL，单文件发布无需额外释放文件）。

## 裁剪风险与回归清单

裁剪（`link`）会按静态可达性移除成员，反射、序列化、P/Invoke 相关代码最容易受影响。本项目已通过 `TrimmerRootAssembly` 与 `Linker.xml` 保留关键类型，但**每次改动依赖或序列化结构后仍应回归**：

```powershell
# Windows
# 1. 基本运行
.\dist\FastHasher-v2.2.1.exe --version
.\dist\FastHasher-v2.2.1.exe -h

# 2. 全算法计算（验证 BLAKE3 与哈希路径）
.\dist\FastHasher-v2.2.1.exe .\LICENSE -a all -o - --no-log | ConvertFrom-Json

# 3. 大文件流式路径（>10MB）
.\dist\FastHasher-v2.2.1.exe big.bin -a blake3,md5 -o - --no-log | ConvertFrom-Json

# 4. 计算 + 验证闭环
.\dist\FastHasher-v2.2.1.exe . -j 2 -o base.json --no-log
.\dist\FastHasher-v2.2.1.exe verify base.json -o - --format text --no-log
```

```bash
# Linux
chmod +x dist/FastHasher-v2.2.1

# 1. 基本运行
./dist/FastHasher-v2.2.1 --version
./dist/FastHasher-v2.2.1 -h

# 2. 全算法计算
./dist/FastHasher-v2.2.1 ./LICENSE -a all -o - --no-log

# 3. 大文件流式路径
./dist/FastHasher-v2.2.1 big.bin -a blake3,md5 -o - --no-log

# 4. 计算 + 验证闭环（并在换 cwd 后再验证一次，检查路径兜底解析）
./dist/FastHasher-v2.2.1 . -j 2 -o base.json --no-log
./dist/FastHasher-v2.2.1 verify base.json -o - --format text --no-log
cd /tmp && <产物绝对路径> verify <base.json 绝对路径> -o - --format text --no-log

# 5. 非常规文件与退出码
mkfifo /tmp/fifo-test && timeout 5 ./dist/FastHasher-v2.2.1 /tmp -a md5 -q --no-log; echo "含 FIFO 的目录应正常返回，不挂死"
./dist/FastHasher-v2.2.1 /nonexistent -a md5 -q --no-log; echo "路径不存在应返回 1"
```

## 版本号管理

版本号需同时修改两处，保持一致：

1. `src/FastHasher/VersionInfo.cs`：`Major` / `Minor` / `Patch` / `Version` / `AssemblyVersion` / `InformationalVersion`
2. `src/FastHasher/FastHasher.csproj`：`Version` / `FileVersion` / `AssemblyVersion` / `InformationalVersion`

两个构建脚本都从 csproj 的 `<Version>` 读取版本号用于重命名产物，无需在脚本里维护；而 `--version` 的运行时输出来自 `VersionInfo`，因此两处必须同步修改。脚本不再执行产物取版本（交叉编译时产物无法在本机运行）。

## 发版流程（GitHub + Gitee 双仓库）

```powershell
# 1. 改版本号（VersionInfo.cs + csproj）与文档
# 2. 构建并回归验证（两个平台各一次）
.\build.ps1 clean            # Windows
.\build.ps1 clean linux-x64  # Linux
# 3. 提交并双推
git add .
git commit -m "v2.2.1: ..."
git push all main
# 4. 打 tag 并推送
git tag v2.2.1
git push all v2.2.1
```

随后在两个平台的 Release 页面创建版本，上传 `dist/FastHasher-v2.2.1.exe` 与 `dist/FastHasher-v2.2.1`（Linux 产物建议先 `chmod +x` 再打成 `tar.gz`，以保留可执行位）。`.gitignore` 已排除 `dist/`，产物不进版本库。

## 常见问题

### Q1：构建脚本提示找不到 dotnet？

**A**：安装 .NET 8.0 SDK 并确认 `dotnet --version` 可执行；安装后需重开终端。

### Q2：`build.bat` 还能用吗？

**A**：不能。批量脚本已移除，构建入口是 `build.ps1`（Windows）与 `build.sh`（Linux）。若 PowerShell 执行策略拦截，可用：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

### Q3：构建成功但 exe 运行报错？

**A**：优先怀疑裁剪。先用 `.\build.ps1`（标准模式）对比，若标准模式正常而 small/tiny 异常，检查是否新增了未被 `TrimmerRootAssembly` / `Linker.xml` 覆盖的反射调用。

### Q4：为什么体积接近 20MB？

**A**：自包含发布把 .NET 运行时打进单文件，这是主要体积来源。`small` 裁剪与 `tiny` 压缩可进一步减小，代价是启动解压与潜在的裁剪兼容风险。

### Q5：构建很慢？

**A**：`PublishReadyToRun` 与裁剪链接本身耗时较长，属正常现象；首次为某个 RID 构建还需下载 runtime pack。增量构建（不加 `clean`）会快很多。

### Q6：改了 `build.ps1` 后中文乱码、部分语句莫名失效？

**A**：PowerShell 5.1 会把**无 BOM** 的 UTF-8 脚本按系统 ANSI 代码页解析，中文注释末尾的字节可能吞掉换行，使下一条语句并入注释。请将 `build.ps1` 保存为「UTF-8 with BOM」。`build.sh` 相反，必须保持 UTF-8 **无 BOM**，否则 shebang 失效、执行报 `bad interpreter`。

### Q7：Linux 上提示 `GLIBC_2.xx not found` 或 `Permission denied`？

**A**：
- `Permission denied`：产物缺少可执行位，执行 `chmod +x FastHasher-v2.2.1`。用 zip 分发会丢权限，建议 `tar.gz`。
- `GLIBC_2.xx not found`：目标系统 glibc 低于产物要求（约 2.28）。CentOS 7、Debian 9 等老系统不支持，需换构建环境重发或在容器中运行。
- `libssl` 相关报错：目标机 OpenSSL 版本过低，需 1.1.1+ 或 3.x。

## 版本信息

- **当前版本**：2.2.1
- **构建工具**：.NET 8.0 SDK
- **目标平台**：Windows x64、Linux x64（可在 Windows 上交叉编译）
- **发布方式**：单文件、自包含 exe
- **版本管理**：`VersionInfo` 类 + csproj 双处统一
