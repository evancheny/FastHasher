#!/usr/bin/env bash
# FastHasher Linux 构建脚本（与 build.ps1 等价）
#
# 用法:
#   ./build.sh                        # 标准构建，目标 linux-x64
#   ./build.sh small                  # 裁剪构建
#   ./build.sh tiny                   # 裁剪 + 单文件压缩
#   ./build.sh clean                  # 先清理 bin/obj/dist 再标准构建
#   ./build.sh linux-arm64            # 指定其它 RID
#   ./build.sh clean tiny linux-x64   # 组合使用，顺序无关
set -euo pipefail

RID="linux-x64"
MODE="standard"
CLEAN=0
PROJECT="src/FastHasher/FastHasher.csproj"

usage() {
    cat <<'EOF'
用法: ./build.sh [clean] [small|tiny] [RID]

  clean            构建前删除 src/FastHasher/bin、src/FastHasher/obj、dist
  small            追加程序集裁剪
  tiny             裁剪 + 单文件压缩（体积最小，启动略慢）
  RID              目标运行时，默认 linux-x64
                   例: linux-x64 / linux-arm64 / linux-musl-x64 / win-x64

示例:
  ./build.sh
  ./build.sh clean
  ./build.sh tiny linux-x64
EOF
}

for arg in "$@"; do
    case "$arg" in
        clean) CLEAN=1 ;;
        small) MODE="small" ;;
        tiny) MODE="tiny" ;;
        linux-* | win-* | osx-*) RID="$arg" ;;
        -h | --help) usage; exit 0 ;;
        *)
            echo "未知参数: $arg" >&2
            usage >&2
            exit 2
            ;;
    esac
done

echo "FastHasher Build Script"
echo ""

if [ "$CLEAN" -eq 1 ]; then
    echo "Cleaning..."
    rm -rf src/FastHasher/bin src/FastHasher/obj dist
fi

ARGS=(-c Release -r "$RID" --self-contained true -o dist
      -p:PublishSingleFile=true -p:DebugType=none)

case "$MODE" in
    small)
        echo "Mode: Small - Reduced size"
        ARGS+=(-p:PublishTrimmed=true -p:TrimMode=link)
        ;;
    tiny)
        echo "Mode: Tiny - Maximum size reduction"
        ARGS+=(-p:PublishTrimmed=true -p:TrimMode=link -p:EnableCompressionInSingleFile=true)
        ;;
    *)
        echo "Mode: Standard - Full features"
        ;;
esac

echo "RID: $RID"
echo ""
echo "Building..."

dotnet publish "$PROJECT" "${ARGS[@]}"

echo ""
echo "Build success!"
echo ""

# 版本号从 csproj 读取，避免依赖执行产物（交叉编译产物无法在本机运行）
VERSION=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)
if [ -z "$VERSION" ]; then
    echo "无法从 $PROJECT 解析版本号" >&2
    exit 1
fi

case "$RID" in
    win-*) SUFFIX=".exe" ;;
    *) SUFFIX="" ;;
esac

BIN="dist/FastHasher$SUFFIX"
TARGET="dist/FastHasher-v$VERSION$SUFFIX"

if [ ! -f "$BIN" ]; then
    echo "未找到构建产物: $BIN" >&2
    exit 1
fi

mv "$BIN" "$TARGET"

# 单文件发布在 Linux 上需要可执行位（打包成 zip 会丢失，tar.gz 会保留）
if [ -z "$SUFFIX" ]; then
    chmod +x "$TARGET"
fi

echo "Version: $VERSION"
echo "Size: $(wc -c < "$TARGET" | tr -d ' ') bytes"
echo "Output: $TARGET"
echo ""

case "$RID" in
    win-*)
        echo "提示: 交叉编译产物无法在当前平台运行，请复制到 Windows 验证。"
        ;;
    *)
        echo "Testing..."
        if "./$TARGET" --version; then
            echo ""
            echo "Usage:"
            echo "  ./FastHasher-v$VERSION <file|directory> [options]"
            echo "  ./FastHasher-v$VERSION -h"
            echo ""
            echo "Build Modes:"
            echo "  ./build.sh         - Standard"
            echo "  ./build.sh small   - Small"
            echo "  ./build.sh tiny    - Tiny"
            echo "  ./build.sh clean   - Clean first"
        else
            # 在 Git Bash / WSL 之外交叉编译时无法执行 ELF，属正常情况
            echo "提示: 产物无法在当前平台运行（交叉编译场景），请复制到 Linux 验证。"
        fi
        ;;
esac

echo ""
