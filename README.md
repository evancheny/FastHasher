# FastHasher – ⚡ Fast File Hashing and Verification

<p align="center">
  <b>English</b> | <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20x64-blue?style=flat-square" alt="Windows | Linux x64" />
  <img src="https://img.shields.io/badge/license-Apache--2.0-green?style=flat-square" alt="Apache-2.0" />
  <img src="https://img.shields.io/badge/version-2.2.1-orange?style=flat-square" alt="v2.2.1" />
</p>

<p align="center">
  <b>FastHasher</b> is a high-performance file hashing and verification tool supporting MD5, SHA1, SHA256, SHA512 and BLAKE3.<br>
  It processes thousands of files in one run, verifies integrity in a single command, and emits JSON / NDJSON reports that other programs can parse directly.<br>
  Distributed as a single self-contained executable (Windows exe / Linux ELF) with both a CLI and an interactive wizard.
</p>

---

## ✨ Highlights

### 🚀 Performance
- **Parallel hashing**: runs on all logical CPU cores by default; use `-j` to pick a thread count, and `-j 1` to fall back to sequential processing.
- **Dynamic buffering**: files ≤10MB are read into memory at once; larger files are streamed in chunks with a 1–10MB buffer chosen by file size.
- **Single read, multiple algorithms**: every selected algorithm is computed during one pass over the file — no repeated disk reads.
- **ReadyToRun publishing**: R2R pre-compilation is enabled by default for faster cold start.

### 🧭 Usability
- **Interactive wizard**: run with no arguments (double-click on Windows, execute on Linux) or pass `-i` to get a menu-driven Q&A flow that prints the equivalent command line before running, ready to copy into a script.
- **Two entry points**: the CLI for batch jobs and automation, the wizard for occasional use when you cannot remember the flags.
- **No more command-line length limits**: `--file-list` reads paths from a file (`-f -` reads stdin), and `@args.rsp` reads arguments from a file.
- **Sensible output**: `hashes_<timestamp>.json` by default; `-o -` writes to stdout in `json` / `text` / `ndjson`.

### 🔍 Reliability
- **Separated streams**: stdout carries results only; progress bars, notices and logs always go to stderr, so output can be piped safely.
- **Verification mode**: the `verify` subcommand detects algorithms from the JSON report, recomputes each file and classifies results as success / failed / missing / error.
- **Scriptable exit codes**: `0` success / `1` problems or no matching files / `2` invalid arguments / `130` cancelled, suitable for CI.
- **UTF-8 end to end**: Chinese file names, paths and contents work correctly; JSON output has no BOM and is escaped properly.
- **Complete metadata**: algorithms, concurrency, filters and timings are recorded in the JSON so verification can reproduce the original run.

---

## 🚀 Quick Start

### Download

Grab the single-file executable for your platform from the Releases page:

| Platform | Artifact | Size |
|---|---|---|
| Windows x64 | `FastHasher-v2.2.1.exe` | ~19.3MB |
| Linux x64 | `FastHasher-v2.2.1` (no extension) | ~20.1MB |

- GitHub: [github.com/evancheny/FastHasher/releases](https://github.com/evancheny/FastHasher/releases)
- Gitee: [gitee.com/evancheny/fast-hasher/releases](https://gitee.com/evancheny/fast-hasher/releases)

No .NET runtime installation is required. Double-click the exe on Windows, or `chmod +x` the binary on Linux. Running without arguments opens the interactive wizard.

### Build from source

```powershell
# GitHub
git clone https://github.com/evancheny/FastHasher.git
# or Gitee
git clone https://gitee.com/evancheny/fast-hasher.git

cd FastHasher

# Windows
.\build.ps1          # standard build (recommended)
.\build.ps1 small    # smaller build (extra trimming)
.\build.ps1 tiny     # smallest build (trimming + single-file compression)
.\build.ps1 clean    # clean bin/obj/dist first, then standard build

# Cross-compile the Linux binary from Windows (no Linux needed)
.\build.ps1 linux-x64
```

```bash
# Linux (requires the .NET 8 SDK)
chmod +x build.sh
./build.sh           # standard build, defaults to linux-x64
./build.sh small
./build.sh tiny
./build.sh clean
./build.sh linux-arm64   # any other RID
```

**Requirements**

| Item | Requirement |
|---|---|
| OS | Windows x64; Linux x64 (glibc ≥ 2.28, OpenSSL 1.1.1+/3.x) |
| Build environment | PowerShell 5.1+ on Windows; bash on Linux |
| SDK | .NET 8.0 SDK or later |

**Build output**

The file name is derived from the version and target platform; each build produces exactly one file:

| Target | File name | Size |
|---|---|---|
| Windows x64 | `dist/FastHasher-v<version>.exe` | ~19.3MB (measured on v2.2.1) |
| Linux x64 | `dist/FastHasher-v<version>` | ~20.1MB (measured on v2.2.1) |

| Mode | Difference |
|---|---|
| standard | Full feature set, for everyday use |
| small | Explicit trimming enabled, smaller binary |
| tiny | Trimming + single-file compression, smallest binary, slightly slower start |

> All three modes write the same file name into the same `dist` directory, so consecutive builds overwrite each other. Rename the artifact if you need to keep several. Windows and Linux artifacts have different names and can coexist.

### Linux notes

```bash
# Set the executable bit (lost when extracting a zip, preserved by tar.gz)
chmod +x FastHasher-v2.2.1

./FastHasher-v2.2.1 --version
./FastHasher-v2.2.1 /data -a blake3 -j 8 -o result.json
./FastHasher-v2.2.1 /data -a all -o - --format ndjson -q --no-log > out.ndjson
```

- **Self-contained**: the .NET runtime is bundled, so no .NET install is needed; the binary still depends on system `glibc` (≥ 2.28) and `OpenSSL` (1.1.1+ or 3.x). `InvariantGlobalization` is enabled, so ICU is not required.
- **Tested on**: Rocky Linux 10.2 (glibc 2.39, OpenSSL 3.5.5, x86_64) — all 18 regression checks and hash comparisons against `md5sum`/`sha*sum` passed. For a first run, try `--version`, `-a all` and `verify`.
- **Alpine / musl** cannot run the glibc build; build separately with `./build.sh linux-musl-x64`.
- Behaviour differences from Windows:
  - Paths are **case-sensitive**: `A.txt` and `a.txt` are two different files (Windows treats them as one).
  - Hidden files starting with `.` are skipped by default, matching the Windows hidden-file policy.
  - Wildcards are expanded by the program itself; pre-expanded shell globs work too.
  - The wizard, colours and progress bar work in a normal terminal; when output is redirected the progress bar degrades to a single-line refresh.

### Interactive mode

Run with no arguments (double-click the executable), or pass `-i` / `--interactive`:

```powershell
.\FastHasher-v2.2.1.exe          # double-click opens the wizard
.\FastHasher-v2.2.1.exe -i
.\FastHasher-v2.2.1.exe interactive
```

```
==================================================
  FastHasher v2.2.1  interactive mode
  Enter 6 or q to quit; Ctrl+C aborts the running task
==================================================

Select an action:
  1. Hash files
  2. Verify hashes
  3. Benchmark
  4. Help
  5. 切换语言 / Switch language
  6. Quit
```

- **Hash wizard**: paths (multiple paths separated by spaces) → algorithms → recursion → threads → size filter → output format → output path → show statistics.
- **Verify wizard**: JSON path (press Enter to list the 10 most recent JSON files in the current directory and pick one by index) → path filter → output format → output path.
- **Language switch (option 5)**: toggles Chinese/English on the spot and persists the choice to `settings.json`, so the next launch keeps it. The item always shows a Chinese label in both languages so you can always find your way back.
- The **equivalent command line** is printed before execution so you can confirm or reuse it; afterwards you return to the menu and can keep working.
- When stdin is redirected (CI, pipes) the wizard is skipped and help is printed instead, so nothing hangs.

### Common commands

```powershell
# Hash a single file (writes hashes_<timestamp>.json by default)
.\FastHasher-v2.2.1.exe myfile.txt

# Recursively hash every txt file in the current directory, with statistics and charts
.\FastHasher-v2.2.1.exe *.txt -r --stats

# 8 threads, two algorithms, minimum size filter
.\FastHasher-v2.2.1.exe E:\test -a md5,sha256 -j 8 --min-size 1MB

# All algorithms (md5/sha1/sha256/sha512/blake3), NDJSON on stdout
.\FastHasher-v2.2.1.exe *.iso -a all -o - --format ndjson -q

# Strictly sequential processing
.\FastHasher-v2.2.1.exe large-files\*.dat -j 1

# Read paths from a file list; use - to read stdin
.\FastHasher-v2.2.1.exe --file-list files.txt -j auto
Get-ChildItem *.iso | ForEach-Object FullName | .\FastHasher-v2.2.1.exe -f -

# Arguments from a response file (one per line)
.\FastHasher-v2.2.1.exe @args.rsp

# Verify: writes <name>_verify_<timestamp>.json by default
.\FastHasher-v2.2.1.exe verify hashes_20260101_120000.json

# Verify and write a text report to a specific file
.\FastHasher-v2.2.1.exe verify hashes.json --format text -o report.txt

# Only verify records whose path contains "backup"
.\FastHasher-v2.2.1.exe verify hashes.json backup

# Help / version
.\FastHasher-v2.2.1.exe -h
.\FastHasher-v2.2.1.exe -v
```

---

## 📖 Command-Line Reference

### Usage

```
FastHasher                            interactive wizard (no arguments; prints help when stdin is redirected)
FastHasher -i                         enter the interactive wizard explicitly
FastHasher [hash] <file|dir|glob>... [options]
FastHasher verify <hashes.json> [path filters...] [options]
FastHasher bench
FastHasher help | version
```

> If the first positional argument happens to be `hash`, it is treated as the subcommand name and ignored; just pass the path to hash.

### Input and algorithms

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--algorithms <list>` | `-a` | Hash algorithms, separated by comma/semicolon/space, or `all`. Accepts `md5` `sha1` `sha256` `sha512` `blake3`; default `md5` | `-a md5,blake3` |
| `--recursive` | `-r` | Recurse into subdirectories | `-r` |
| `--min-size <size>` | - | Minimum file size; supports `B/KB/KiB/MB/MiB/GB/GiB/TB/TiB` and decimals | `--min-size 1KB` |
| `--max-size <size>` | - | Maximum file size | `--max-size 100MB` |
| `--file-list <file>` | `-f` | Read a path list from a file, one per line, `#` starts a comment; `-` means stdin | `-f files.txt` |
| `@<response file>` | - | Read command-line arguments from a file, one per line | `@args.rsp` |
| `--` | - | Stop option parsing; everything after is treated as a path (for file names starting with `-`; must come after all options) | `-q -o - -- -weird.txt` |

### Concurrency

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--threads <n \| auto>` | `-j` | Worker threads. Default `auto` (= logical CPU count); `1` means sequential; capped at logical CPU count × 2 with a warning and truncation | `-j 8` |

### Output

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--output <file \| ->` | `-o` | Output path; `-` means stdout; when omitted a timestamped file is generated | `-o result.json` |
| `--format <format>` | - | `json` (default, single object), `text` (human readable), `ndjson` (one object per line) | `--format ndjson` |

### Display and logging

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--stats` | - | Show statistics and charts (size distribution, performance scatter) | `--stats` |
| `--progress <mode>` | - | Progress bar: `auto` (default; shown when not redirected and not quiet), `always`, `never` | `--progress never` |
| `--quiet` | `-q` | Only errors and warnings | `-q` |
| `--verbose` | - | Verbose log output on stderr | `--verbose` |
| `--no-color` | - | Disable coloured output | `--no-color` |
| `--lang <zh\|en>` | - | UI language; overrides `settings.json` (`%APPDATA%\FastHasher\settings.json` on Windows, `~/.config/FastHasher/settings.json` on Linux) | `--lang en` |
| `--log <file>` | - | Explicit log file path | `--log run.log` |
| `--no-log` | - | Do not write a log file | `--no-log` |

### Miscellaneous

| Option | Short | Description | Example |
|--------|-------|-------------|---------|
| `--interactive` | `-i` | Interactive wizard (`interactive` / `ui` subcommands are equivalent) | `-i` |
| `--help` | `-h` | Show help | `-h` |
| `--version` | `-v` | Show version and logical CPU count | `-v` |

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Everything succeeded |
| `1` | Failed files, missing files, hash mismatches, or **no files/records to process** (wrong path, empty directory) |
| `2` | Invalid arguments, verification error |
| `130` | Cancelled by the user (Ctrl+C) |

### Deprecated options

Legacy forms fail immediately (exit code 2) with a migration hint instead of being silently ignored:

| Legacy option | Replacement |
|---|---|
| `-p/--parallel [n]` | `-j <n>` (`-j 1` means sequential) |
| `-seq/--sequential` | `-j 1` |
| `-c/--console-output` | `-o -` |
| `--json-file <json>` | `verify <json>` |
| `--show-time` | `--stats` |
| `--benchmark` | `bench` subcommand |
| `--batch-size` | Removed, no replacement |

---

## 📝 Output Formats

With `-o -`, **stdout contains result data only**; all notices, progress bars and logs go to stderr, so `... -o - -q --no-log | ConvertFrom-Json` parses directly.

### Default file names

| Scenario | Default path |
|---|---|
| Hash (json / text / ndjson) | `./hashes_<yyyyMMdd_HHmmss>.json` / `.txt` / `.ndjson` |
| Verification report | Same directory as the input JSON, `<name>_verify_<yyyyMMdd_HHmmss>.json` / `.txt` / `.ndjson` |
| Hash run log | Same directory as the output file, `FastHasher_<yyyyMMdd_HHmmss>.log` |
| Verify run log | Same directory as the output file, `FastHasher_Verify_<yyyyMMdd_HHmmss>.log` |

`--no-log` disables log files entirely.

### JSON: hash mode

```json
{
  "metadata": {
    "generated": "2026-11-09 10:30:12",
    "version": "2.2.1",
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
      "file": "test\\example.iso",
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

**metadata fields:** `generated` creation time, `version` tool version, `algorithms` algorithm list, `parallel` whether parallel, `maxDegreeOfParallelism` thread count, `recursive` recursion flag, `filePatterns` input patterns, `minFileSize`/`maxFileSize` size range, `totalFiles` file count, `totalBytes` total bytes, `processingTimeSeconds` elapsed time.

A failing file does not abort the run; its entry carries `"success": "false"` plus `error` and `error_type`.

> `file` is **relative to the current working directory** (falling back to an absolute path when a relative path cannot be formed, e.g. across Windows drives), while `filename` is the bare file name. This lets a report travel with its files to another directory or machine and still be verifiable. When the recorded path does not exist, `verify` falls back to: relative to the current directory → relative to the report's directory → matching the trailing path segments.

### text: human readable

```
FastHasher 2.2.1 | generated: 2026-11-09 10:30:12
algorithms: MD5, BLAKE3 | files: 2 | total size: 1.5 MB | elapsed: 0.42 s

C:\test\a.iso
  size: 1.0 MB | time: 120 ms
  MD5     : d41d8cd98f00b204e9800998ecf8427e
  BLAKE3  : af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262
```

The verification text report adds a "problem files" section listing the failure type along with expected and actual values.

### ndjson: one object per line

In hash mode each line is a file record; in verify mode each line is a verification result. Ideal for `jq`, log pipelines and streaming large result sets.

### JSON: verify mode

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

## 🔍 Verification in Detail

The `verify` subcommand compares current files against a previously generated hash JSON:

1. Reads `metadata` from the JSON to restore the original algorithms, concurrency and filters;
2. Detects algorithms per record (MD5 / SHA1 / SHA256 / SHA512 / BLAKE3); algorithms absent from a record are not compared;
3. Recomputes and compares each value, producing four states: `success` / `failed` (hash mismatch) / `missing` (file gone) / `error` (read or compute failure);
4. Also compares file size; a mismatch marks the record `failed` with the message "file size mismatch";
5. Emits a JSON / text / ndjson report according to `--format`, and the exit code reflects the result: any failure, missing entry or error yields `1`.

**Path filters**: `verify hashes.json backup D:\data` — multiple filters are OR-ed and matched against the `file` or `full_path` field (case-insensitive), handy for re-verifying a single subdirectory.

---

## ⚙️ Performance and Parallelism

- **Default concurrency**: `-j auto`, i.e. the logical CPU count.
- **Upper bound**: values above logical CPU count × 2 are truncated with a warning to avoid thread explosion.
- **Strategy switching**: files ≤10MB are read into memory and hashed with the zero-allocation `HashData` APIs; files >10MB use streaming `TransformBlock` chunks.
- **Buffer size**: 1/2/4/8/10MB chosen by file size.
- **Progress overhead**: progress is throttled to one refresh every 200ms to avoid slowing down hashing.

Tuning advice: on SSDs, raising `-j` beyond the physical core count yields diminishing returns; mechanical and network drives are IO-bound, so `-j 2` to `-j 4` is usually best — too many threads hurt due to seek and bandwidth contention.

---

## ❓ FAQ

### Q: Will Chinese file names or paths be garbled?
**A**: No. Source code, file IO and console output are all UTF-8, and output files have no BOM.

### Q: What if the command line gets too long?
**A**: Use `--file-list files.txt` (one path per line, `-f -` reads stdin), or put arguments into a file with `@args.rsp` (one argument per line).

### Q: Why did notices appear in my `-o -` output?
**A**: Since v2.0.0 the streams are fully separated: results go to stdout, notices/progress/logs go to stderr. If you still see mixing, you are on an old version.

### Q: Does BLAKE3 need an extra native library?
**A**: No. It uses the fully managed `Blake3` package, embedded in the single-file executable — no DLL extraction, no platform dependency, producing 32 bytes (64 hex characters).

### Q: Is Linux/macOS supported?
**A**: Linux x64 is supported, with a self-contained single-file artifact and `build.sh` (~20.1MB, requires glibc ≥ 2.28 and OpenSSL 1.1.1+/3.x). macOS is untested: `dotnet publish -r osx-x64` should produce a binary, but there is no regression coverage and details such as the icon and progress bar are not adapted.

### Q: Which of the three build modes should I use?
**A**: Use the standard mode by default; use `tiny` when distributing to others and transfer size matters. Trimming carries reflection/serialization risk — `Linker.xml` and `TrimmerRootAssembly` already preserve `System.Text.Json`, `Blake3` and `System.IO.Hashing`, but still run a hash + verify regression after publishing.

### Q: What is `metadata` in the JSON for?
**A**: It records the version, algorithms, concurrency, filters and timings of the original run. Verification uses it to restore the algorithm set and avoid false mismatches from comparing with the wrong algorithms. v1.2.1 fixed a metadata field loss bug; all fields round-trip correctly since v2.x.

---

## 📄 License

Licensed under the **Apache License 2.0**. See [LICENSE](LICENSE).

## 🚀 Version

### Current version: v2.2.1

- **BLAKE3 support**: `-a blake3` / `-a all`, fully managed, both the small-file and large-file paths are verified against the official vectors.
- **Interactive wizard**: open it with no arguments or `-i`; separate flows for hashing, verification and benchmarking, with a command-line preview before execution.
- **Argument system rewrite (2.0.0)**: unified `-j` concurrency, `-o` output, `--format`, and the `verify` subcommand; legacy options now fail with migration hints; added `-q/--verbose`, `--progress`, `--log/--no-log`, `--no-color`.
- **Output reliability**: stdout and stderr fully separated, UTF-8 without BOM, unified exit codes `0/1/2/130`.
- **Linux support**: new `build.sh` (standard / small / tiny / clean, any RID) and cross-compilation of a self-contained single-file artifact; path comparison respects platform case sensitivity and OS info uses `OSDescription`.
- **Robustness (2.2.1)**: FIFOs, devices and sockets are skipped (a directory containing a named pipe used to hang the process); zero matches now return exit code `1`; the JSON `file` field stores a relative path so reports remain verifiable after being moved.
- **Bilingual UI (unreleased)**: every console message (including the help text) is available in Chinese and English, switchable via `--lang zh|en` or the interactive menu; the choice is persisted to `settings.json`.

See [CHANGELOG.md](CHANGELOG.md) for the full history.

## 📚 Related documents

- [CHANGELOG.md](CHANGELOG.md) — release history
- [BUILD.md](BUILD.md) — build guide, publish parameters and technical details

## 🙏 Acknowledgements

This started as a personal tool for managing large numbers of files. Hopefully it helps anyone with similar needs — verifying downloads, comparing backups, and so on.

Feedback and suggestions are very welcome.
