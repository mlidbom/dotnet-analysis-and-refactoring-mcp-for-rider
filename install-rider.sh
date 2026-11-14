#!/bin/bash
set -euo pipefail

PLUGIN_NAME="DotNetAnalysisAndRefactoringMcp"
RIDER_VERSION="${1:-}"

# Auto-detect Rider version if not specified
if [ -z "$RIDER_VERSION" ]; then
    # Try macOS path first, then Linux
    RIDER_DIR=$(ls -d "$HOME/Library/Application Support/JetBrains/Rider"* 2>/dev/null | sort -V | tail -1)
    if [ -z "$RIDER_DIR" ]; then
        RIDER_DIR=$(ls -d ~/.local/share/JetBrains/Rider* 2>/dev/null | sort -V | tail -1)
    fi
    if [ -z "$RIDER_DIR" ]; then
        echo "Error: Could not find Rider plugin directory."
        echo "Usage: $0 [Rider2025.3]"
        exit 1
    fi
else
    # Check macOS path first, then Linux
    if [ -d "$HOME/Library/Application Support/JetBrains/$RIDER_VERSION" ]; then
        RIDER_DIR="$HOME/Library/Application Support/JetBrains/$RIDER_VERSION"
    else
        RIDER_DIR="$HOME/.local/share/JetBrains/$RIDER_VERSION"
    fi
fi

PLUGIN_DIR="$RIDER_DIR/plugins/$PLUGIN_NAME"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Puts the tools build in <plugin>/tools/<build>, which the host serves when no newer build for it is deployed
# elsewhere: the tools assembly, its symbols, and host.sha256, the SHA-256 of the host assembly it goes with.
copy_tools_build() {
    local tools_dir="$1/tools/$(date -u +%Y-%m-%d_%H-%M-%S-0000000)"
    local out="$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp.Tools/bin/Release/net472"
    mkdir -p "$tools_dir"
    cp "$out/DotNetAnalysisAndRefactoringMcp.Tools.dll" "$out/DotNetAnalysisAndRefactoringMcp.Tools.pdb" "$tools_dir/"
    sha256sum "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp/bin/Release/net472/$PLUGIN_NAME.dll" | cut -d' ' -f1 > "$tools_dir/host.sha256"
}

# Ensure JAVA_HOME is set for Gradle. If not already set, try to find Rider's bundled JBR.
if [ -z "${JAVA_HOME:-}" ]; then
    # macOS: Rider.app bundles a JBR
    if [ -d "/Applications/Rider.app/Contents/jbr/Contents/Home" ]; then
        export JAVA_HOME="/Applications/Rider.app/Contents/jbr/Contents/Home"
    fi
fi

echo "Building backend..."
# The tools project references the host's, so this builds both.
dotnet build "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp.Tools/DotNetAnalysisAndRefactoringMcp.Tools.csproj" -c Release -v quiet

echo "Building frontend..."
(cd "$SCRIPT_DIR/rider-plugin" && ./gradlew jar --quiet)

echo "Installing to: $PLUGIN_DIR"
mkdir -p "$PLUGIN_DIR/dotnet"
mkdir -p "$PLUGIN_DIR/lib"

# A running Rider memory-maps the DLL and compiles methods from it lazily. Copying over it in place rewrites
# the mapped bytes, and the next method it compiles reads garbage ("Bad IL range" crashed the backend).
# Removing first gives the new file its own inode; Rider keeps the old one until it restarts.
rm -f "$PLUGIN_DIR/dotnet/$PLUGIN_NAME.dll" "$PLUGIN_DIR/dotnet/$PLUGIN_NAME.pdb" "$PLUGIN_DIR/lib/$PLUGIN_NAME.jar"
cp "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp/bin/Release/net472/$PLUGIN_NAME.dll" "$PLUGIN_DIR/dotnet/"
cp "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp/bin/Release/net472/$PLUGIN_NAME.pdb" "$PLUGIN_DIR/dotnet/" 2>/dev/null || true
cp "$SCRIPT_DIR/rider-plugin/build/libs/$PLUGIN_NAME.jar" "$PLUGIN_DIR/lib/"
# The host loads tools builds from their bytes, so they are never locked: the old ones can go.
rm -rf "$PLUGIN_DIR/tools"
copy_tools_build "$PLUGIN_DIR"

echo ""
echo "Done! Plugin installed to $PLUGIN_DIR"
echo "  lib/  -> $PLUGIN_NAME.jar (frontend: Kotlin classes + plugin descriptor)"
echo "  dotnet/ -> $PLUGIN_NAME.dll (the host: backend components)"
echo "  tools/ -> the tools build the host serves"
echo ""
echo "Restart Rider for the plugin to take effect."
echo "The MCP server will start on http://127.0.0.1:23751/ when you open a solution."
echo "Set DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT env var to use a different port."
