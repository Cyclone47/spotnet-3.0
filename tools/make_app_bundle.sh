#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MAC_PROJ="$REPO_ROOT/src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj"
APP_DIR="$REPO_ROOT/artifacts/Spotnet.app"
CONTENTS="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"

echo "=== Building Spotnet 3.0 macOS App Bundle ==="

# 1. Prepare directories
rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

# 2. Build .icns icon
ICONSET_DIR="/tmp/Spotnet.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

SRC_ICON="$REPO_ROOT/tools/branding/spotnet_icon.png"
if [ -f "$SRC_ICON" ]; then
    echo "Creating macOS AppIcon.icns..."
    sips -z 16 16     "$SRC_ICON" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null 2>&1
    sips -z 32 32     "$SRC_ICON" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null 2>&1
    sips -z 32 32     "$SRC_ICON" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null 2>&1
    sips -z 64 64     "$SRC_ICON" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null 2>&1
    sips -z 128 128   "$SRC_ICON" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null 2>&1
    sips -z 256 256   "$SRC_ICON" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null 2>&1
    sips -z 256 256   "$SRC_ICON" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null 2>&1
    sips -z 512 512   "$SRC_ICON" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null 2>&1
    sips -z 512 512   "$SRC_ICON" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null 2>&1
    sips -z 1024 1024 "$SRC_ICON" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null 2>&1
    iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/AppIcon.icns"
    rm -rf "$ICONSET_DIR"
fi

# Detect or accept target architecture (x64, arm64, auto)
TARGET_ARCH="${1:-auto}"
if [ "$TARGET_ARCH" = "auto" ]; then
    HOST_ARCH="$(uname -m)"
    if [ "$HOST_ARCH" = "x86_64" ]; then
        TARGET_RID="osx-x64"
    else
        TARGET_RID="osx-arm64"
    fi
elif [ "$TARGET_ARCH" = "x64" ] || [ "$TARGET_ARCH" = "intel" ]; then
    TARGET_RID="osx-x64"
elif [ "$TARGET_ARCH" = "arm64" ] || [ "$TARGET_ARCH" = "apple-silicon" ]; then
    TARGET_RID="osx-arm64"
else
    echo "Unknown architecture: $TARGET_ARCH. Use 'x64', 'arm64', or 'auto'."
    exit 1
fi

echo "Target architecture: $TARGET_RID"

DOTNET_BIN="$HOME/.dotnet/dotnet"
if ! command -v "$DOTNET_BIN" &>/dev/null; then
    DOTNET_BIN="dotnet"
fi

echo "Publishing self-contained binary for $TARGET_RID..."
"$DOTNET_BIN" publish "$MAC_PROJ" \
    -c Release \
    -r "$TARGET_RID" \
    --self-contained true \
    -p:PublishTrimmed=false \
    -o "$MACOS_DIR"

# 4. Create Info.plist
cat << 'PLIST' > "$CONTENTS/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>nl</string>
    <key>CFBundleExecutable</key>
    <string>Spotnet.Mac</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>nl.spotnet.desktop</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>Spotnet</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>3.0.0.2</string>
    <key>CFBundleVersion</key>
    <string>3.0.0.2</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

echo "Ad-hoc signing Spotnet.app..."
codesign --force --deep -s - "$APP_DIR"

echo "=== Successfully created $APP_DIR ==="
