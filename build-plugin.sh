#!/bin/bash
set -euo pipefail

PLUGIN_NAME="DotNetAnalysisAndRefactoringMcp"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
STAGING_DIR="$SCRIPT_DIR/.build-staging"

# Puts the tools build in <plugin>/tools/<build>, which the host serves when no newer build for it is published
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
    if [ -d "/Applications/Rider.app/Contents/jbr/Contents/Home" ]; then
        export JAVA_HOME="/Applications/Rider.app/Contents/jbr/Contents/Home"
    fi
fi

echo "Building backend..."
# The tools project references the host's, so this builds both.
dotnet build "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp.Tools/DotNetAnalysisAndRefactoringMcp.Tools.csproj" -c Release -v quiet

echo "Building frontend..."
(cd "$SCRIPT_DIR/rider-plugin" && ./gradlew jar --quiet)

echo "Assembling plugin ZIP..."
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/$PLUGIN_NAME/lib"
mkdir -p "$STAGING_DIR/$PLUGIN_NAME/dotnet"
cp "$SCRIPT_DIR/rider-plugin/build/libs/$PLUGIN_NAME.jar" "$STAGING_DIR/$PLUGIN_NAME/lib/"
cp "$SCRIPT_DIR/src/DotNetAnalysisAndRefactoringMcp/bin/Release/net472/$PLUGIN_NAME.dll" "$STAGING_DIR/$PLUGIN_NAME/dotnet/"
copy_tools_build "$STAGING_DIR/$PLUGIN_NAME"

cd "$STAGING_DIR"
rm -f "$SCRIPT_DIR/$PLUGIN_NAME.zip"
zip -r "$SCRIPT_DIR/$PLUGIN_NAME.zip" "$PLUGIN_NAME/" -q

# Cleanup
rm -rf "$STAGING_DIR"

echo ""
echo "Done! Plugin ZIP created: $SCRIPT_DIR/$PLUGIN_NAME.zip"
echo "Upload it at: https://plugins.jetbrains.com/plugin/add"
