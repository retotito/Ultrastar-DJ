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

# libmpv and its dylibs stay in MacOS/natives/ (code; DllImport finds them next to the executable). The tools
# (yt-dlp's folder, ffmpeg, cloudflared) move to Resources/natives/: codesign treats every file in MacOS/ as code,
# and yt-dlp's folder (text files, a flattened Python.framework) cannot be signed that way. SidecarLocator
# looks in Resources/natives/ too.
chmod +x "$APP/Contents/MacOS/$EXECUTABLE"
mkdir -p "$APP/Contents/Resources/natives"
for tool in yt-dlp ffmpeg cloudflared; do
  [[ -e "$APP/Contents/MacOS/natives/$tool" ]] && mv "$APP/Contents/MacOS/natives/$tool" "$APP/Contents/Resources/natives/"
done
chmod +x "$APP/Contents/Resources/natives/"{yt-dlp/yt-dlp,ffmpeg,cloudflared} 2>/dev/null || true

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
  <key>LSMinimumSystemVersion</key>     <string>14.0</string>
  <key>NSHighResolutionCapable</key>    <true/>
  <key>NSMicrophoneUsageDescription</key>
  <string>Ultrastar DJ needs microphone access to detect pitch and score your singing.</string>
</dict>
</plist>
PLIST

echo "→ ad-hoc signing"
# Not --deep: it takes folders like yt-dlp's "*.dist-info" for bundles. codesign treats every file in
# Contents/MacOS as code, so each is signed on its own (.NET's .dll included — the signature goes into extended
# attributes), then ffmpeg and cloudflared (Apple Silicon runs no unsigned arm64 binary), then the bundle, which
# seals Resources/ (yt-dlp keeps the signature it ships with).
find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/$EXECUTABLE" -print0 |
  while IFS= read -r -d '' f; do
    codesign --force --sign - "$f" 2>/dev/null || echo "  ! could not sign ${f#$APP/}"
  done
for tool in ffmpeg cloudflared; do
  [[ -f "$APP/Contents/Resources/natives/$tool" ]] && codesign --force --sign - "$APP/Contents/Resources/natives/$tool"
done
codesign --force --sign - "$APP"
codesign --verify --strict "$APP" && echo "✓ signature valid"

echo "→ building dmg"
STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -quiet -volname "$APP_NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

echo "✓ $DMG ($(du -sh "$DMG" | cut -f1))"
echo "  First launch on another Mac: System Settings → Privacy & Security → Open Anyway"
