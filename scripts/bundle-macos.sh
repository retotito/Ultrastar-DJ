#!/usr/bin/env zsh
# Wrap a `dotnet publish` output folder into "Ultrastar DJ.app", ad-hoc sign it, and build a dmg.
# Called by publish-macos.sh; usable standalone:
#
#   zsh scripts/bundle-macos.sh out/osx-arm64 0.2.0 osx-arm64
#
# No Apple developer account is used. Ad-hoc signing (-s -) is mandatory on Apple Silicon.
set -euo pipefail

PUBLISH_DIR=$(cd "$1" && pwd)
VERSION=$2
RID=$3
ROOT=$(cd "$(dirname "$0")/.." && pwd)

APP_NAME="Ultrastar DJ"
BUNDLE_ID="com.retokupfer.ultrastardj"
EXECUTABLE="UltrastarDJ"
OUT_DIR="$ROOT/out"
APP="$OUT_DIR/$APP_NAME.app"
DMG="$OUT_DIR/Ultrastar-DJ-$VERSION-$RID.dmg"

echo "→ bundling $APP"
rm -rf "$APP" "$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
cp "$ROOT/src/UltrastarDJ.App/Assets/icon.icns" "$APP/Contents/Resources/icon.icns"

# Sidecars/dylibs are published under MacOS/natives/; dyld finds libmpv via DllImport search paths
# configured in the app (natives/ next to the executable), so no install_name_tool pass is needed.
chmod +x "$APP/Contents/MacOS/$EXECUTABLE"
[[ -d "$APP/Contents/MacOS/natives" ]] && chmod +x "$APP/Contents/MacOS/natives/"{yt-dlp,ffmpeg} 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>               <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>        <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>         <string>$BUNDLE_ID</string>
  <key>CFBundleVersion</key>            <string>$VERSION</string>
  <key>CFBundleShortVersionString</key> <string>$VERSION</string>
  <key>CFBundleExecutable</key>         <string>$EXECUTABLE</string>
  <key>CFBundleIconFile</key>           <string>icon.icns</string>
  <key>CFBundlePackageType</key>        <string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
  <key>LSMinimumSystemVersion</key>     <string>12.0</string>
  <key>NSHighResolutionCapable</key>    <true/>
  <key>NSMicrophoneUsageDescription</key>
  <string>Ultrastar DJ needs microphone access to detect pitch and score your singing.</string>
</dict>
</plist>
PLIST

echo "→ ad-hoc signing"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP" && echo "✓ signature valid"

echo "→ building dmg"
STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -quiet -volname "$APP_NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

echo "✓ $DMG ($(du -sh "$DMG" | cut -f1))"
echo "  First launch on another Mac: System Settings → Privacy & Security → Open Anyway"
