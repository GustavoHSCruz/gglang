#!/bin/bash
# ggLang Compiler - Build & Install Script
# Usage:
#   ./build.sh          Build only
#   ./build.sh install  Build and install (gg command available globally)
#   ./build.sh test     Build and run tests

set -e

VERSION="0.5.0-beta"
PREFIX="${GG_PREFIX:-/usr/local}"
BINDIR="$PREFIX/bin"
LIBDIR="$PREFIX/lib/gglang"
RUNTIMEDIR="$LIBDIR/runtime"
LIBSDIR="$LIBDIR/libs"
BUILD_DIR="build"
CLI_PROJECT="src/ggLang.CLI/ggLang.CLI.csproj"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

info()  { echo -e "${CYAN}[ggLang]${NC} $1"; }
ok()    { echo -e "${GREEN}[ggLang]${NC} $1"; }
warn()  { echo -e "${YELLOW}[ggLang]${NC} $1"; }
err()   { echo -e "${RED}[ggLang]${NC} $1"; }

# ============================================================
# PRE-FLIGHT CHECKS
# ============================================================

check_deps() {
    info "checking dependencies..."

    # .NET SDK
    if ! command -v dotnet &>/dev/null; then
        err "dotnet SDK not found. Install .NET 10+ from https://dot.net"
        exit 1
    fi
    local dotnet_ver
    dotnet_ver=$(dotnet --version 2>/dev/null || echo "unknown")
    info "  dotnet: $dotnet_ver"

    # GCC
    if ! command -v gcc &>/dev/null; then
        err "gcc not found. Install gcc (e.g., sudo pacman -S gcc)"
        exit 1
    fi
    local gcc_ver
    gcc_ver=$(gcc --version | head -1)
    info "  gcc: $gcc_ver"

    ok "dependencies OK"
    echo ""
}

# ============================================================
# BUILD
# ============================================================

do_build() {
    echo -e "${BOLD}=== Building ggLang Compiler v${VERSION} ===${NC}"
    echo ""
    check_deps

    info "building solution..."
    dotnet build --nologo -v q
    ok "build complete"
    echo ""
}

# ============================================================
# TEST
# ============================================================

do_test() {
    do_build

    echo -e "${BOLD}=== Running Tests ===${NC}"
    dotnet test --nologo
    echo ""
}

# ============================================================
# PUBLISH (self-contained single binary)
# ============================================================

do_publish() {
    info "publishing self-contained binary..."
    mkdir -p "$BUILD_DIR"

    dotnet publish "$CLI_PROJECT" \
        -c Release \
        --self-contained \
        -r linux-x64 \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$BUILD_DIR/publish" \
        --nologo -v q

    cp "$BUILD_DIR/publish/ggLang.CLI" "$BUILD_DIR/gg"
    chmod +x "$BUILD_DIR/gg"

    local size
    size=$(du -h "$BUILD_DIR/gg" | cut -f1)
    ok "published: $BUILD_DIR/gg ($size)"
}

# ============================================================
# INSTALL
# ============================================================

do_install() {
    echo -e "${BOLD}=== Installing ggLang v${VERSION} ===${NC}"
    echo ""
    check_deps

    info "building solution..."
    dotnet build --nologo -v q
    ok "build complete"
    echo ""

    do_publish
    echo ""

    info "installing to $PREFIX ..."
    echo "  binary:  $BINDIR/gg"
    echo "  runtime: $RUNTIMEDIR/"
    echo "  libs:    $LIBSDIR/"
    echo ""

    sudo mkdir -p "$BINDIR" "$RUNTIMEDIR" "$LIBSDIR"
    sudo cp "$BUILD_DIR/gg" "$BINDIR/gg"
    sudo chmod +x "$BINDIR/gg"
    sudo cp runtime/gg_runtime.c "$RUNTIMEDIR/"
    sudo cp runtime/gg_runtime.h "$RUNTIMEDIR/"
    sudo cp libs/*.lib.gg "$LIBSDIR/" 2>/dev/null || true
    sudo chmod 444 "$LIBSDIR"/*.lib.gg 2>/dev/null || true
    if command -v chattr &>/dev/null; then
        sudo chattr +i "$LIBSDIR"/*.lib.gg 2>/dev/null || true
    elif command -v chflags &>/dev/null; then
        sudo chflags uchg "$LIBSDIR"/*.lib.gg 2>/dev/null || true
    fi

    echo ""
    ok "installation complete!"
    echo ""
    echo -e "  ${BOLD}Try it:${NC}"
    echo "    gg version"
    echo "    gg run examples/hello.gg"
    echo ""
}

# ============================================================
# UNINSTALL
# ============================================================

do_uninstall() {
    echo -e "${BOLD}=== Uninstalling ggLang ===${NC}"
    echo ""

    if [[ -f "$BINDIR/gg" ]]; then
        info "removing $BINDIR/gg"
        sudo rm -f "$BINDIR/gg"
    fi

    if [[ -d "$LIBDIR" ]]; then
        info "removing $LIBDIR/"
        if [[ -d "$LIBSDIR" ]]; then
            if command -v chattr &>/dev/null; then
                sudo chattr -i "$LIBSDIR"/*.lib.gg 2>/dev/null || true
            elif command -v chflags &>/dev/null; then
                sudo chflags nouchg "$LIBSDIR"/*.lib.gg 2>/dev/null || true
            fi
        fi
        sudo rm -rf "$LIBDIR"
    fi

    ok "ggLang uninstalled"
    echo ""
}

# ============================================================
# CLEAN
# ============================================================

do_clean() {
    info "cleaning build artifacts..."
    dotnet clean --nologo -v q 2>/dev/null || true
    rm -rf "$BUILD_DIR"
    rm -f examples/*.c
    ok "clean"
}

# ============================================================
# HELP
# ============================================================

do_help() {
    echo ""
    echo -e "${BOLD}ggLang Compiler v${VERSION} - Build Script${NC}"
    echo "========================================="
    echo ""
    echo "  ./build.sh              Build the compiler"
    echo "  ./build.sh install      Build and install (gg available globally)"
    echo "  ./build.sh uninstall    Remove installed gg compiler"
    echo "  ./build.sh test         Build and run tests"
    echo "  ./build.sh clean        Remove build artifacts"
    echo "  ./build.sh help         Show this help"
    echo ""
    echo "  Override install prefix: GG_PREFIX=/opt/gg ./build.sh install"
    echo ""
}

# ============================================================
# MAIN
# ============================================================

cd "$(dirname "$0")"

case "${1:-build}" in
    build)      do_build ;;
    install)    do_install ;;
    uninstall)  do_uninstall ;;
    test)       do_test ;;
    clean)      do_clean ;;
    publish)    do_build && do_publish ;;
    help|--help|-h) do_help ;;
    *)
        err "unknown command: $1"
        do_help
        exit 1
        ;;
esac
