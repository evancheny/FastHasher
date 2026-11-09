# FastHasher 构建指南

## 概述

FastHasher 使用 PowerShell 脚本作为主要的构建工具。该脚本功能强大、易于维护，支持多种构建模式和智能版本号管理。

## 系统要求

- Windows 操作系统
- PowerShell 5.1 或更高版本
- .NET 8.0 SDK 或更高版本

## 构建方式

### PowerShell 脚本构建（推荐）

#### 1. 标准模式

```powershell
.\build.ps1
```

**特点：**
- 完整功能
- 文件大小：约 18-20 MB
- 包含所有优化功能
- 自动生成带版本号的 exe 文件
- 适合日常使用

**输出：** `dist/FastHasher-v1.2.0.exe`

#### 2. 小体积模式

```powershell
.\build.ps1 small
```

**特点：**
- 启用程序集裁剪
- 文件大小：约 16-18 MB
- 功能完整
- 自动生成带版本号的 exe 文件
- 适合分发和部署

**输出：** `dist/FastHasher-v2.1.0.exe`

#### 3. 超小体积模式

```powershell
.\build.ps1 tiny
```

**特点：**
- 最大程度裁剪
- 启用单文件压缩
- 文件大小：约 14-16 MB
- 启动速度略慢
- 自动生成带版本号的 exe 文件
- 适合网络分发

**输出：** `dist/FastHasher-v2.1.0.exe`

#### 4. 清理并重新构建

```powershell
.\build.ps1 clean
```

**特点：**
- 清理所有旧的构建文件
- 使用标准模式重新构建
- 适合解决构建问题

#### PowerShell 脚本优势

- ✅ 自动检测版本号
- ✅ 自动重命名 exe 文件（带版本号）
- ✅ 显示详细的构建信息
- ✅ 自动测试生成的程序
- ✅ 提供清晰的使用说明
- ✅ 彩色输出，更易读
- ✅ 更好的错误处理
- ✅ 现代化的脚本语法

## 构建输出

构建成功后，输出文件位于 `dist` 目录：

```
dist/
└── FastHasher-v2.1.0.exe   # 主程序（带版本号）
```

## 构建脚本功能

`build.ps1` 提供以下功能：

1. ✅ 自动清理旧构建文件（clean 模式）
2. ✅ 支持多种构建模式（Standard、Small、Tiny）
3. ✅ 显示构建进度和结果
4. ✅ 自动检测版本号并重命名 exe
5. ✅ 显示文件大小信息
6. ✅ 自动测试生成的程序
7. ✅ 提供清晰的使用说明
8. ✅ 彩色输出提升用户体验

## 构建流程

1. **参数解析阶段**
   - 解析命令行参数（clean、small、tiny）
   - 确定构建模式

2. **清理阶段**（可选）
   - 删除 `bin`、`obj`、`dist` 目录（clean 模式）

3. **构建阶段**
   - 使用 `dotnet publish` 命令编译项目
   - 应用相应的优化参数

4. **版本检测阶段**
   - 运行生成的 exe 获取版本号
   - 自动重命名 exe 文件（带版本号）

5. **验证阶段**
   - 检查生成的 exe 文件
   - 显示文件大小
   - 运行版本测试

6. **完成阶段**
   - 显示构建成功信息
   - 提供使用说明

## 常见问题

### Q1: 构建失败怎么办？

**A:** 尝试以下步骤：
```bash
build.bat clean
```
如果仍然失败，检查：
- 是否安装了 .NET 8.0 SDK
- 源代码文件是否完整
- 是否有足够的磁盘空间

### Q2: 如何查看构建的详细信息？

**A:** 构建脚本会显示详细的构建信息，包括：
- 构建模式
- dotnet 命令输出
- 文件大小
- 测试结果

### Q3: 不同模式的性能差异？

**A:** 各模式的性能对比：

| 模式 | 文件大小 | 启动速度 | 运行速度 | 适用场景 |
|------|---------|---------|---------|---------|
| 标准 | ~18 MB | 快 | 快 | 日常开发 |
| 小体积 | ~16 MB | 快 | 快 | 分发部署 |
| 超小体积 | ~14 MB | 略慢 | 相同 | 网络分发 |

### Q4: 如何自定义构建参数？

**A:** 编辑 `build.bat` 文件，修改以下部分：
```batch
set ARGS=-p:PublishTrimmed=true -p:TrimMode=link
```

可以添加或修改 MSBuild 参数。

## 技术细节

### 使用的优化参数

**小体积模式：**
```batch
-p:PublishTrimmed=true -p:TrimMode=link
```

**超小体积模式：**
```batch
-p:PublishTrimmed=true -p:TrimMode=link -p:EnableCompressionInSingleFile=true
```

### 发布配置

```batch
dotnet publish src\FastHasher\FastHasher.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained ^
  --output .\dist ^
  -p:PublishSingleFile=true ^
  -p:DebugType=none
```

## 版本信息

- **当前版本**: 2.1.0
- **构建工具**: .NET 8.0 SDK
- **目标平台**: Windows x64
- **发布方式**: 单文件独立 exe
- **版本管理**: 统一版本号管理（VersionInfo 类）
