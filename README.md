# FastHasher – ⚡ 极速哈希计算与验证工具

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-Windows%20x64-blue?style=flat-square" alt="Windows x64" />
  <img src="https://img.shields.io/badge/MIT-green?logo=open-source-initiative" alt="MIT License" />
</p>


<p align="center">
  <b>FastHasher</b> 是一款为高性能而生的文件哈希计算与验证工具，支持 MD5、SHA1、SHA256、SHA512 等主流算法。<br>
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

### 📊 **智能分析与验证**
- **验证模式** (`--verify`)：对比先前生成的哈希 JSON，输出详细报告（成功/失败/缺失/错误），精准定位问题文件。
- **图表展示** (`--show-time`)：自动生成文件大小分布图、性能散点图，直观分析处理效率。
- **单位大小过滤**：支持 `1KB`、`10MB`、`0.5GB` 等灵活参数，精确控制处理范围。

### 🖥️ **简单易用**
- **单文件发布**：基于 .NET 8 编译为独立 exe，无需安装任何运行时，即下即用。
- **实时进度条**：清晰显示当前文件、处理速度、剩余时间，让等待不再焦虑。
- **智能输出**：默认保存为带时间戳的 JSON 文件，也可直接输出到控制台 (`-c`)。

---

## 🚀 快速开始

### 下载与安装
从 [Releases](https://gitee.com/evancheny/fast-hasher/releases/) 页面下载最新版本的 `FastHasher.exe`（单文件），或通过以下命令自行构建：

```powershell
git clone https://gitee.com/evancheny/fast-hasher.git

# 运行构建脚本（需要 PowerShell）
# 只构建，不测试
.\build.ps1

# 构建+测试+运行基准测试
.\build.ps1 -Test -Benchmark

# 构建并运行基准测试
.\build.ps1 -Benchmark

# 清理、构建并运行所有测试
.\build.ps1 -Clean -Test

```
### 基本用法示例

```powershell
# 计算单个文件的哈希值（自动保存到 hashes_时间戳.json）
.\FastHasher.exe 我的文件.txt

# 计算当前目录所有 txt 文件（递归子目录），显示进度条
.\FastHasher.exe *.txt --recursive

# 指定4线程并行，显示详细统计与图表
.\FastHasher.exe *.iso -p 4 --show-time

# 计算模式最常用的参数组合 
.\FastHasher.exe E:\test  -a MD5,SHA256 --recursive  --show-time -s 

# 验证之前生成的哈希文件，并保存详细报告
.\FastHasher.exe --verify hashes_20240101.json

# 显示帮助
.\FastHasher.exe -h

```
## 📖 命令行参数详解

`--algorithms <列表>	-a`	指定要计算的哈希算法，逗号分隔（默认：MD5）
示例：-a MD5,SHA256
`--recursive	-r`	递归搜索子目录
`--parallel [线程数]	-p`	启用并行处理，可指定线程数（默认：CPU逻辑线程数）
示例：`-p 8`
`--sequential	-seq`	禁用并行，顺序处理
`--show-time`	处理完成后显示详细统计信息及图表
`--stats	-s`	显示系统性能统计（CPU、内存、线程池）
`--min-size <大小>`	最小文件大小（支持单位：B, KB, MB, GB, TB）
示例：`--min-size 1KB`
`--max-size <大小>`		最大文件大小
`--batch-size <数量>`		每批处理的文件数量（默认：100），用于大文件列表
`--file-list <文件>	-f`	从文件读取要处理的文件列表（每行一个路径）
`@<响应文件>`	使用响应文件传递参数（避免命令行过长）
`--output <路径>	-o`	指定输出文件路径（默认自动生成带时间戳的 JSON）
`--console-output	-c`	在控制台直接输出结果（不保存到文件）
`--verify <json-file>`	验证模式，检查文件是否与之前计算的哈希一致
`--benchmark	-b`	运行基准测试，比较顺序与并行性能
`--help	-h`	显示帮助信息
`--version	-v`	显示版本信息及当前 CPU 逻辑线程数

## 📝 输出格式
### 计算模式（JSON 数组）
```json
[
  {
    "file": "C:\\test\\example.txt",
    "filename": "example.txt",
    "size": "1024",
    "md5": "d41d8cd98f00b204e9800998ecf8427e",
    "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "time_ms": "15.23",
    "success": "true"
  }
]```
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
`--verify` 模式允许你将当前文件与之前生成的哈希 JSON 进行对比。它会：

自动检测 JSON 中包含的哈希算法（MD5、SHA1 等）

重新计算每个文件的哈希值

逐项比对，输出成功、失败、缺失、错误四种状态

生成详细的验证报告（JSON），包含不匹配的算法和具体哈希值

适用于数据完整性检查、备份验证等场景。

## ⚙️ 性能与并行
FastHasher 的并行策略经过精心设计：

默认使用 CPU 逻辑线程数，避免过度并行导致资源争抢。

可手动指定线程数（如 -p 4），最大不超过 CPU 线程数的 2 倍，防止性能下降。

实时显示当前并行线程数，进度条中可见。

大文件流式处理 + 动态缓冲区：根据文件大小自动选择最优缓冲区（1MB~10MB），兼顾速度和内存。
## ❓ 常见问题
Q：中文文件名或路径会乱码吗？
A：不会。所有内部处理均使用 UTF-8 编码，控制台输出也已设置为 UTF-8。

Q：命令行参数太长怎么办？
A：可以使用 --file-list 将文件列表写入文件，或使用响应文件 @files.rsp 传递参数。

Q：如何只计算文件大小，不计算哈希？
A：目前没有直接参数，但可以通过指定空算法列表（暂不支持）或修改源代码实现。

Q：支持 Linux/macOS 吗？
A：项目基于 .NET 8，理论上可跨平台。但当前构建脚本仅针对 Windows，欢迎贡献跨平台支持。

## 📄 许可证
本项目采用 MIT 许可证。


## 写在最后
   此项目本来是自用，用于解决海量文件的管理问题，今天除夕顺手开源在 Gitee 上，如果你也有校验下载文件、备份对比的需求，欢迎试试看。 作者水平有限，代码简陋，功能初成，请大家多提意见！
   新的一年，愿代码少 bug，生活多顺畅。大家除夕快乐！🧧
