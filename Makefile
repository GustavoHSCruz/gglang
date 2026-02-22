# ggLang Compiler - Makefile
# Build, install, and manage the ggLang compiler
#
# Usage:
#   make              Build the compiler
#   make install      Build and install to /usr/local/bin/gg
#   make uninstall    Remove installed files
#   make test         Run all tests
#   make examples     Build and run all examples
#   make clean        Clean build artifacts
#   make help         Show this help

# Configuration
PREFIX       ?= /usr/local
BINDIR       := $(PREFIX)/bin
LIBDIR       := $(PREFIX)/lib/gglang
RUNTIME_DIR  := $(LIBDIR)/runtime
LIBS_DIR     := $(LIBDIR)/libs
BUILD_DIR    := build
PUBLISH_DIR  := $(BUILD_DIR)/publish
CLI_PROJECT  := src/ggLang.CLI/ggLang.CLI.csproj
RID          := linux-x64
DOTNET       := dotnet
GG           := $(BUILD_DIR)/gg

.PHONY: all build publish install uninstall test examples clean help

# Default target
all: build

# ============================================================
# BUILD
# ============================================================

## Build the compiler (debug)
build:
	@echo "=== Building ggLang Compiler ==="
	@$(DOTNET) build --nologo -v q
	@echo "[ggLang] build complete"

## Publish as self-contained single-file binary
publish:
	@echo "=== Publishing ggLang Compiler ==="
	@$(DOTNET) publish $(CLI_PROJECT) \
		-c Release \
		--self-contained \
		-r $(RID) \
		-p:PublishSingleFile=true \
		-p:PublishTrimmed=false \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-o $(PUBLISH_DIR) \
		--nologo -v q
	@cp $(PUBLISH_DIR)/ggLang.CLI $(GG)
	@chmod +x $(GG)
	@echo "[ggLang] published: $(GG) ($$(du -h $(GG) | cut -f1))"

# ============================================================
# INSTALL / UNINSTALL
# ============================================================

## Install gg compiler to $(PREFIX) (requires sudo)
install: publish
	@echo ""
	@echo "=== Installing ggLang v0.9.2 ==="
	@echo "  Binary:  $(BINDIR)/gg"
	@echo "  Runtime: $(RUNTIME_DIR)/"
	@echo "  Libs:    $(LIBS_DIR)/"
	@echo ""
	@sudo mkdir -p $(BINDIR) $(RUNTIME_DIR) $(LIBS_DIR)
	@sudo cp $(GG) $(BINDIR)/gg
	@sudo chmod +x $(BINDIR)/gg
	@sudo cp runtime/gg_runtime.c $(RUNTIME_DIR)/
	@sudo cp runtime/gg_runtime.h $(RUNTIME_DIR)/
	@sudo cp libs/*.lib.gg $(LIBS_DIR)/ 2>/dev/null || true
	@sudo chmod 444 $(LIBS_DIR)/*.lib.gg 2>/dev/null || true
	@sudo sh -c 'if command -v chattr >/dev/null 2>&1; then chattr +i $(LIBS_DIR)/*.lib.gg 2>/dev/null || true; elif command -v chflags >/dev/null 2>&1; then chflags uchg $(LIBS_DIR)/*.lib.gg 2>/dev/null || true; fi'
	@echo ""
	@echo "[ggLang] installation complete!"
	@echo ""
	@echo "  Try it:"
	@echo "    gg version"
	@echo "    gg run examples/hello.gg"
	@echo "    gg init my_project"
	@echo ""

## Remove installed files
uninstall:
	@echo "=== Uninstalling ggLang ==="
	@sudo rm -f $(BINDIR)/gg
	@sudo sh -c 'if [ -d "$(LIBS_DIR)" ]; then if command -v chattr >/dev/null 2>&1; then chattr -i $(LIBS_DIR)/*.lib.gg 2>/dev/null || true; elif command -v chflags >/dev/null 2>&1; then chflags nouchg $(LIBS_DIR)/*.lib.gg 2>/dev/null || true; fi; fi'
	@sudo rm -rf $(LIBDIR)
	@echo "[ggLang] uninstalled"

# ============================================================
# TEST & EXAMPLES
# ============================================================

## Run all unit tests
test:
	@echo "=== Running Tests ==="
	@$(DOTNET) test --nologo -v q

## Build and run all examples
examples: build
	@echo "=== Running Examples ==="
	@echo ""
	@echo "--- hello.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/hello.gg
	@echo ""
	@echo "--- classes.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/classes.gg
	@echo ""
	@echo "--- calculator.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/calculator.gg
	@echo ""
	@echo "--- interfaces.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/interfaces.gg
	@echo ""
	@echo "--- enums.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/enums.gg
	@echo ""
	@echo "--- libraries.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/libraries.gg
	@echo ""
	@echo "--- extensions.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/extensions.gg
	@echo ""
	@echo "--- imports.gg ---"
	@$(DOTNET) run --project $(CLI_PROJECT) --no-build -- run examples/imports.gg

# ============================================================
# CLEAN
# ============================================================

## Clean all build artifacts
clean:
	@echo "=== Cleaning ==="
	@$(DOTNET) clean --nologo -v q 2>/dev/null || true
	@rm -rf $(BUILD_DIR)
	@rm -f examples/*.c
	@echo "[ggLang] clean"

# ============================================================
# HELP
# ============================================================

## Show available targets
help:
	@echo ""
	@echo "ggLang Compiler - Build System"
	@echo "=============================="
	@echo ""
	@echo "  make              Build the compiler (debug)"
	@echo "  make publish      Publish as single-file native binary"
	@echo "  make install      Build + install to $(PREFIX)/bin/gg"
	@echo "  make uninstall    Remove installed gg compiler"
	@echo "  make test         Run unit tests"
	@echo "  make examples     Build and run all examples"
	@echo "  make clean        Remove build artifacts"
	@echo "  make help         Show this help"
	@echo ""
	@echo "  PREFIX=$(PREFIX)  (override with: make install PREFIX=/opt/gglang)"
	@echo ""
