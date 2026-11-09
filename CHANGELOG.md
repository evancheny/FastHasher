# Changelog

<p align="center">
  <b>English</b> | <a href="CHANGELOG.zh-CN.md">简体中文</a>
</p>

Versions follow `major.minor.patch`. The version number lives in exactly two places, kept in sync: `src/FastHasher/VersionInfo.cs` and `FastHasher.csproj`.

## [Unreleased]

### Added

- **Bilingual UI (Chinese / English)**: all console messages — wizard, help text, progress, statistics, warnings and errors — are available in both languages.
  - `--lang zh|en` forces a language; it is pre-scanned before argument parsing so even parse errors honour it. Priority: `--lang` > `settings.json` > default Chinese.
  - The interactive wizard gains option 5 "切换语言 / Switch language"; the choice persists to `settings.json` (`%APPDATA%\FastHasher\settings.json` on Windows, `~/.config/FastHasher/settings.json` on Linux).
  - The help text is maintained as two full blocks (one per language) instead of line-by-line translation, so each layout stays properly aligned.

### Changed (internal cleanup, no change to commands or output formats)

- Removed the unreachable legacy verification path `VerifyFilesAsync` and its private helpers (~330 lines); `verify` now always uses the "recompute from metadata + compare" path.
- Progress rendering merged into a single `UpdateProgress` shared by parallel and sequential modes; `FormatSize` / `FormatSpeed` share one scaling implementation; name truncation unified as `ProgressDisplay.Truncate`.
- Output-path and log-path resolution extracted into `OutputPaths`, so the hash and verify modes no longer keep separate copies.
- Version constants reduced to `Version` and `InformationalVersion`; the unbound `Major`/`Minor`/`Patch`/`AssemblyVersion` constants were dropped.
- Removed `using` directives already covered by `ImplicitUsings`, along with debug logging and a duplicated error message.
- File-size statistics and chart data are now collected in a single pass instead of `stat`-ing the same files twice.

### Fixed

- `--no-color` and output redirection could be ignored: `ProgressDisplay.InitializeLog` unconditionally reset the colour switch to true, leaking ANSI escapes into notices. It now only sets the log path; colour is decided solely by `ApplyUi`.

## [2.2.1] - 2026-11-09

### Fixed

- **FIFO / device files caused a permanent hang**: file type is now checked with `fstat` during enumeration and before hashing, skipping FIFOs, character/block devices and sockets (new `FileTypeHelper`). Previously a single named pipe anywhere in the target directory would block the process forever.
- **Zero matches passed silently**: a mistyped path or an empty directory (no files found at all) used to return `0`, which CI read as success; it now returns `1`. `verify` likewise returns `1` when the report contains no verifiable records.
- **JSON paths were not portable**: the `file` field changed from an absolute path to a path relative to the current working directory (falling back to absolute when a relative path cannot be formed). Reports can now be moved together with their files and still be verified. `verify` gained path fallbacks: relative to the current directory → relative to the report's directory → matching trailing path segments.

### Changed

- Version bumped to 2.2.1; `InformationalVersion` is now "2.2.1 - 修复非常规文件阻塞与路径可移植性".

## [2.2.0] - 2026-11-09

### Added

- **Linux x64 support**: new `build.sh` (standard / small / tiny / clean, any RID) producing a self-contained single-file ELF; cross-compile from Windows with `.\build.ps1 linux-x64`.
- Version bumped from 2.1.0 to 2.2.0; `InformationalVersion` is now "2.2.0 - 新增 Linux x64 支持".
- Build scripts accept an explicit target RID and name artifacts per platform: `FastHasher-v<version>.exe` (Windows) and `FastHasher-v<version>` (Linux).

### Changed

- The csproj no longer hard-codes `RuntimeIdentifier`, `SelfContained` or `PlatformTarget`; the scripts pass them via `-r` and `--self-contained`. The application icon applies only when publishing for a Windows target.
- `build.ps1` reads the version from the csproj `<Version>` instead of executing the artifact (cross-compiled binaries cannot run locally).
- `build.ps1` is saved as UTF-8 with BOM to avoid PowerShell 5.1 parsing it as ANSI and swallowing lines after Chinese comments.
- Repeated builds delete the existing artifact before renaming, fixing the "file already exists" failure on the second build.

### Fixed

- On Linux, file names are case-sensitive; deduplicating with `OrdinalIgnoreCase` treated `A.txt` and `a.txt` as the same file and skipped one. The comparer is now chosen per platform.
- OS information in metadata only reported the kernel version on Unix; it now uses `RuntimeInformation.OSDescription`.
- When stdin is redirected, `-h` no longer prompts "press any key to exit".

## [2.1.0] - 2026-11-09

### Added

- **BLAKE3 algorithm**: `-a blake3`, automatically included by `-a all`. Uses the fully managed `Blake3 3.0.2` package with no native DLLs, adding no extra dependencies to the single-file build.
  - The small-file path uses `Hasher.Hash`; the large-file path (>10MB) uses streaming `Blake3HashAlgorithm` chunks. Both produce identical results.
  - Empty input and `"abc"` are verified against the official BLAKE3 test vectors.
- Help text, the `bench` benchmark and the interactive wizard all list BLAKE3.

### Changed

- Version bumped from 2.0.0 to 2.1.0; `InformationalVersion` is now "2.1.0 - 新增 BLAKE3 支持".

## [2.0.0] - 2026-11-09

Breaking rewrite: legacy options are no longer silently ignored — they fail with exit code 2 and a migration hint.

### Added

- **Interactive wizard** (`src/FastHasher/InteractiveShell.cs`): open it with no arguments, `-i`, `--interactive` or `interactive`. It offers hashing, verification and benchmarking flows, prints the equivalent command line before running, and returns to the menu afterwards.
- **Unified argument model** (`src/FastHasher/CliOptions.cs`): `CliOptions` + `CliParser` centralize response-file expansion, subcommand detection, unit parsing, algorithm validation, deprecation hints and misspelling suggestions for unknown options.
- `--format json|text|ndjson` for both hashing and verification.
- `-o -` writes to stdout, where stdout carries result data only.
- `-q/--quiet`, `--verbose`, `--no-color`, `--progress auto|always|never`, `--log <file>`, `--no-log`.
- Unified exit codes: `0` success / `1` failures or missing entries / `2` invalid arguments / `130` cancelled.
- Multi-term path filters: `verify hashes.json backup D:\data`.

### Changed

- Mode: `--verify` / `--json-file` → the `verify <json>` subcommand.
- Concurrency: `-p [n]` / `-seq` → `-j <n|auto>`; `-j 1` means sequential, capped at logical CPU count × 2.
- Output: `-c` → `-o -`; output and log paths are resolved consistently and default to the output file's directory.
- Statistics: `--show-time` and the old `-s` → `--stats`.
- Benchmark: `--benchmark` → the `bench` subcommand.
- All notices, progress bars and logs now go to stderr so stdout result data stays clean.
- The program entry point now only initializes the environment and dispatches subcommands.

### Removed

- `--batch-size`: it never took effect after parsing; removed.

### Fixed

- Setting `Console.InputEncoding` rebuilt the stdin reader and swallowed the first line when stdin was redirected; it is now set only for interactive terminals.
- A UTF-8 BOM on the first line of a script broke parsing of the first argument.
- Progress lines and degraded output were written to stdout, breaking the parsability of `-o -` JSON.

## [1.0.1] - Early versions

- Fixed reading from the root directory.
- Adjustments to the build scripts and file-processing logic.

## [1.0.0] - Early versions

- First release: MD5 / SHA1 / SHA256 / SHA512 hashing, parallel processing, JSON output and verification mode.
