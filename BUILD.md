# FastHasher Build Guide

<p align="center">
  <b>English</b> | <a href="BUILD.zh-CN.md">简体中文</a>
</p>

## Overview

Building is driven by two scripts that accept identical arguments:

| Platform | Script | Default target |
|---|---|---|
| Windows | `build.ps1` | `win-x64` |
| Linux / macOS | `build.sh` | `linux-x64` |

Both call `dotnet publish` to produce a self-contained single-file program, then rename it to `FastHasher-v<version>` (with an `.exe` suffix on Windows) using `<Version>` from the csproj. The version number has two sources that must stay in sync: `src/FastHasher/VersionInfo.cs` and `FastHasher.csproj`.

> The RID and self-containment are no longer hard-coded in the csproj; the scripts pass them via `-r`, so one source tree produces both Windows and Linux artifacts.

## Requirements

| Item | Requirement |
|---|---|
| OS | Any (Windows can cross-compile the Linux artifact) |
| Windows build | PowerShell 5.1 or later |
| Linux build | bash 4 or later |
| SDK | .NET 8.0 SDK or later |
| Network | First build restores NuGet packages and the runtime pack for the target RID |
| Linux runtime | glibc ≥ 2.28, OpenSSL 1.1.1+ or 3.x (`InvariantGlobalization` is enabled, so ICU is not required) |

## Build Modes

Both scripts accept the same arguments:

| Mode | Windows | Linux |
|---|---|---|
| Standard | `.\build.ps1` | `./build.sh` |
| Trimmed | `.\build.ps1 small` | `./build.sh small` |
| Trimmed + compressed | `.\build.ps1 tiny` | `./build.sh tiny` |
| Clean then build | `.\build.ps1 clean` | `./build.sh clean` |
| Explicit RID | `.\build.ps1 linux-x64` | `./build.sh linux-arm64` |

### 1. Standard

```powershell
.\build.ps1        # Windows
```

```bash
./build.sh         # Linux
```

- Full feature set with no extra trimming/compression flags (trimming and R2R are controlled by the csproj)
- Windows ~19.3MB (measured 20,276,535 bytes on v2.2.1); Linux ~20.1MB (21,039,790 bytes)
- Recommended for everyday use and distribution

**Output**: `dist/FastHasher-v<version>.exe` (Windows) or `dist/FastHasher-v<version>` (Linux)

### 2. Small

```powershell
.\build.ps1 small
```

- Adds `-p:PublishTrimmed=true -p:TrimMode=link`
- Smaller than the standard build
- Good for distribution

**Output**: same file name as the standard build

### 3. Tiny

```powershell
.\build.ps1 tiny
```

- Adds `-p:EnableCompressionInSingleFile=true` on top of the small build
- Smallest binary; decompression at startup makes the first launch slower
- Good for network distribution

**Output**: same file name as the standard build

> ⚠️ All three modes write the same file name into the same `dist` directory for a given RID and overwrite each other. Artifacts for different RIDs have different names (`.exe` vs. no extension) and can coexist.

### 4. Clean and rebuild

```powershell
.\build.ps1 clean
```

```bash
./build.sh clean
```

Deletes `src/FastHasher/bin`, `src/FastHasher/obj` and `dist`, then rebuilds in standard mode. Use it first when a build misbehaves, to rule out incremental-compilation artifacts.

### Combining arguments

`clean` can be combined with a mode argument in any order:

```powershell
.\build.ps1 clean tiny    # clean, then build the tiny variant
.\build.ps1 tiny clean    # same effect
```

## Build Pipeline

1. **Parse arguments**: recognise `clean` / `small` / `tiny` and an optional RID (`linux-x64`, `linux-arm64`, `win-x64`, …); defaults to standard mode with the platform's default RID.
2. **Clean** (optional): delete `src/FastHasher/bin`, `src/FastHasher/obj` and `dist`.
3. **Publish**: `dotnet publish -r <RID> --self-contained`, appending trimming/compression flags per mode.
4. **Resolve version**: read `<Version>` from `src/FastHasher/FastHasher.csproj` (**never execute the artifact** — cross-compiled binaries cannot run locally).
5. **Rename**: `win-*` artifacts become `FastHasher-v<version>.exe`; other RIDs become `FastHasher-v<version>`.
6. **Smoke test**: print the byte size; if the artifact can run on the current platform, also run `--version`, otherwise print a note to verify on the target platform.

## Build Artifacts

```
dist/
├── FastHasher-v2.2.1.exe   # Windows x64: single file, self-contained
└── FastHasher-v2.2.1       # Linux x64: ELF, needs chmod +x
```

`dist/` is excluded by `.gitignore`; artifacts never enter the repository. Releases are distributed as GitHub / Gitee Release attachments.

## Cross-compiling the Linux Artifact

This works on Windows — no Linux environment or WSL required:

```powershell
.\build.ps1 linux-x64          # standard
.\build.ps1 clean tiny linux-x64
```

The equivalent raw command:

```powershell
dotnet publish src\FastHasher\FastHasher.csproj `
  -c Release -r linux-x64 --self-contained true -o dist `
  -p:PublishSingleFile=true -p:DebugType=none
```

Notes:

- The first run downloads the `linux-x64` runtime pack (100MB+); later builds can reuse it offline.
- The artifact is an ELF executable (magic `7F 45 4C 46`) and **cannot run on Windows**; the script skips the smoke test and prints a note.
- Built with the official runtime pack, the artifact requires glibc ≥ 2.28 and OpenSSL 1.1.1+/3.x on the target machine (baseline planned as Ubuntu 22.04 / glibc 2.35).
- Tested on: Rocky Linux 10.2 (glibc 2.39, OpenSSL 3.5.5, x86_64) — 18 regression checks plus hash comparisons (`md5sum` / `sha1sum` / `sha256sum` / `sha512sum`) all passed, and SIGINT cancellation exits with code `130`.
- Non-regular file detection uses `fstat` (the symbol is provided by glibc ≥ 2.33): on older glibc the check degrades to "no check" without crashing, but a FIFO in the directory may still block.
- If the copied file is not executable: `chmod +x FastHasher-v2.2.1`. Distributing via `tar.gz` preserves the permission bit; zip loses it.

## Publish Parameters

The equivalent command executed internally by both scripts (Windows example):

```powershell
dotnet publish src\FastHasher\FastHasher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained `
  --output .\dist `
  -p:PublishSingleFile=true `
  -p:DebugType=none
```

Extra parameters per mode:

| Mode | Extra parameters |
|---|---|
| small | `-p:PublishTrimmed=true -p:TrimMode=link` |
| tiny | `-p:PublishTrimmed=true -p:TrimMode=link -p:EnableCompressionInSingleFile=true` |

## Key csproj Settings

| Setting | Value | Purpose |
|---|---|---|
| `PublishSingleFile` | `true` | Bundle everything into one executable |
| `SelfContained` | passed by the script via `--self-contained` | Bundle the runtime so the target needs no .NET install |
| `RuntimeIdentifier` | passed by the script via `-r` | Determines the target platform; hard-coding it prevents building Linux packages |
| `PlatformTarget` | not set | Left to the RID to avoid conflicts with targets such as `linux-arm64` |
| `ApplicationIcon` | set only for `win-x64` | The icon is Windows-only; conditioning it keeps Linux artifacts unaffected |
| `PublishTrimmed` / `TrimMode` | `true` / `link` | Trim unused assemblies |
| `PublishReadyToRun` | `true` | Pre-compile to reduce cold-start time |
| `IncludeNativeLibrariesForSelfExtract` | `true` | Embed native libraries and extract at runtime |
| `InvariantGlobalization` | `true` | Drop ICU data to reduce size (basic behaviour such as case-insensitive comparison is unaffected) |
| `JsonSerializerIsReflectionEnabledByDefault` | `true` | Keep reflection-based System.Text.Json serialization working |
| `TrimmerRootAssembly` | `System.Text.Json`, `System.IO.Hashing`, `Blake3` | Prevent trimming from removing members needed at runtime |
| `TrimmerRootDescriptor` | `Linker.xml` | Explicitly preserve serialization-related types |
| `DebugType` | `none` | Do not emit pdb files, reducing size |

**Dependencies**: `System.IO.Hashing 8.0.0`, `Blake3 3.0.2` (both fully managed, no native DLLs, nothing extra to extract for single-file publishing).

## Trimming Risk and Regression Checklist

Trimming (`link`) removes members based on static reachability, so reflection, serialization and P/Invoke code are the most likely casualties. This project preserves the critical types via `TrimmerRootAssembly` and `Linker.xml`, but **regression is still required after any change to dependencies or serialization shape**:

```powershell
# Windows
# 1. Basic run
.\dist\FastHasher-v2.2.1.exe --version
.\dist\FastHasher-v2.2.1.exe -h

# 2. All algorithms (exercises BLAKE3 and the hashing paths)
.\dist\FastHasher-v2.2.1.exe .\LICENSE -a all -o - --no-log | ConvertFrom-Json

# 3. Large-file streaming path (>10MB)
.\dist\FastHasher-v2.2.1.exe big.bin -a blake3,md5 -o - --no-log | ConvertFrom-Json

# 4. Hash + verify round trip
.\dist\FastHasher-v2.2.1.exe . -j 2 -o base.json --no-log
.\dist\FastHasher-v2.2.1.exe verify base.json -o - --format text --no-log
```

```bash
# Linux
chmod +x dist/FastHasher-v2.2.1

# 1. Basic run
./dist/FastHasher-v2.2.1 --version
./dist/FastHasher-v2.2.1 -h

# 2. All algorithms
./dist/FastHasher-v2.2.1 ./LICENSE -a all -o - --no-log

# 3. Large-file streaming path
./dist/FastHasher-v2.2.1 big.bin -a blake3,md5 -o - --no-log

# 4. Hash + verify round trip (verify once more from another cwd to exercise path fallback)
./dist/FastHasher-v2.2.1 . -j 2 -o base.json --no-log
./dist/FastHasher-v2.2.1 verify base.json -o - --format text --no-log
cd /tmp && <absolute-artifact-path> verify <absolute-base.json-path> -o - --format text --no-log

# 5. Non-regular files and exit codes
mkfifo /tmp/fifo-test && timeout 5 ./dist/FastHasher-v2.2.1 /tmp -a md5 -q --no-log; echo "a directory containing a FIFO must return normally, not hang"
./dist/FastHasher-v2.2.1 /nonexistent -a md5 -q --no-log; echo "a missing path must return 1"
```

## Version Management

The version must be updated in two places and kept consistent:

1. `src/FastHasher/VersionInfo.cs`: `Version` / `InformationalVersion` (do not split `Version` into separate Major/Minor/Patch constants — they drift from the string)
2. `src/FastHasher/FastHasher.csproj`: `Version` / `FileVersion` / `AssemblyVersion` / `InformationalVersion`

Both build scripts read `<Version>` from the csproj for artifact naming, so the scripts need no maintenance. The runtime `--version` output comes from `VersionInfo`, hence both places must change together. The scripts no longer execute the artifact to obtain the version (cross-compiled binaries cannot run locally).

## Release Process (GitHub + Gitee mirrors)

```powershell
# 1. Bump the version (VersionInfo.cs + csproj) and update the docs
# 2. Build and run the regression on both platforms
.\build.ps1 clean            # Windows
.\build.ps1 clean linux-x64  # Linux
# 3. Commit and push to both remotes
git add .
git commit -m "v2.2.1: ..."
git push all main
# 4. Tag and push the tag
git tag v2.2.1
git push all v2.2.1
```

Then create a release on both platforms and upload `dist/FastHasher-v2.2.1.exe` and `dist/FastHasher-v2.2.1` (for the Linux artifact, `chmod +x` first and wrap it in a `tar.gz` to preserve the executable bit). `.gitignore` already excludes `dist/`, so artifacts never enter the repository.

## FAQ

### Q1: The build script cannot find dotnet

**A**: Install the .NET 8.0 SDK and make sure `dotnet --version` works; reopen the terminal after installing.

### Q2: Is `build.bat` still available?

**A**: No. The batch script was removed; the entry points are `build.ps1` (Windows) and `build.sh` (Linux). If the PowerShell execution policy blocks the script, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

### Q3: The build succeeds but the exe crashes at runtime

**A**: Suspect trimming first. Compare against `.\build.ps1` (standard mode); if standard works and small/tiny fail, look for new reflection calls not covered by `TrimmerRootAssembly` / `Linker.xml`.

### Q4: Why is the binary close to 20MB?

**A**: Self-contained publishing embeds the .NET runtime, which is the main contributor. `small` trimming and `tiny` compression reduce it further, at the cost of startup decompression and potential trimming incompatibilities.

### Q5: The build is slow

**A**: `PublishReadyToRun` and trimming/linking are inherently slow; the first build for a RID also downloads the runtime pack. Incremental builds (without `clean`) are much faster.

### Q6: After editing `build.ps1`, Chinese text is garbled or statements silently fail

**A**: PowerShell 5.1 interprets a **BOM-less** UTF-8 script using the system ANSI code page; trailing bytes of a Chinese comment can swallow the newline, merging the next statement into the comment. Save `build.ps1` as "UTF-8 with BOM". `build.sh` is the opposite: it must stay UTF-8 **without BOM**, otherwise the shebang breaks and execution fails with `bad interpreter`.

### Q7: On Linux I get `GLIBC_2.xx not found` or `Permission denied`

**A**:
- `Permission denied`: the artifact lost its executable bit — run `chmod +x FastHasher-v2.2.1`. Distributing via zip loses the bit; prefer `tar.gz`.
- `GLIBC_2.xx not found`: the target glibc is older than the artifact requires (~2.28). CentOS 7, Debian 9 and similar are unsupported; rebuild in an older environment or run in a container.
- `libssl` related errors: the target OpenSSL is too old; 1.1.1+ or 3.x is required.

## Version Information

- **Current version**: 2.2.1
- **Build toolchain**: .NET 8.0 SDK
- **Targets**: Windows x64, Linux x64 (cross-compilable from Windows)
- **Distribution**: single-file, self-contained executable
- **Version source**: `VersionInfo` class plus csproj, kept in sync
