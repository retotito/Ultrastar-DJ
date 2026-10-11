#!/usr/bin/env zsh
# Build a release .app + .dmg for macOS, and (if Velopack's `vpk` is installed) the update packages.
#
#   zsh scripts/publish-macos.sh              # RID from this machine's CPU
#   zsh scripts/publish-macos.sh osx-x64      # explicit RID
#   VERSION=0.2.0-beta.1 zsh scripts/publish-macos.sh   # version other than Directory.Build.props (CI: the tag)
#
# Install vpk once:  dotnet tool install -g vpk
# Output: out/<rid>/ (publish dir), out/Ultrastar DJ.app, out/Releases/ (update packages, channel <rid>),
#         out/Releases/Ultrastar-DJ-<version>-<rid>.dmg
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
RID=${1:-$([[ $(uname -m) == arm64 ]] && echo osx-arm64 || echo osx-x64)}
VERSION=${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)}
[[ -n "$VERSION" ]] || { echo "✗ <Version> not found in Directory.Build.props"; exit 1; }
[[ -d "$ROOT/natives/$RID" ]] || { echo "✗ natives/$RID missing — run scripts/fetch-natives.sh first"; exit 1; }

OUT="$ROOT/out/$RID"
RELEASES="$ROOT/out/Releases"
APP="$ROOT/out/Ultrastar DJ.app"
rm -rf "$OUT"
mkdir -p "$RELEASES"
echo "→ dotnet publish $RID v$VERSION"
dotnet publish "$ROOT/src/UltrastarDJ.App" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -p:Version="$VERSION" -nologo -v q -o "$OUT"

zsh "$ROOT/scripts/bundle-macos.sh" "$OUT" "$VERSION" "$RID"

# Velopack adds its updater to the .app and signs that and the bundle (ad-hoc "-", hardened runtime with our
# entitlements); everything else is already signed by bundle-macos.sh (--signDisableDeep). The dmg then carries the
# app Velopack made, so an app installed from it updates itself.
if command -v vpk >/dev/null; then
  echo "→ vpk pack (channel $RID)"
  vpk pack -u UltrastarDJ -v "$VERSION" -p "$APP" -e UltrastarDJ --channel "$RID" \
    --packTitle 'Ultrastar DJ' --packAuthors 'Reto Küpfer' --icon "$ROOT/src/UltrastarDJ.App/Assets/icon.icns" \
    --signAppIdentity - --signDisableDeep --signEntitlements "$ROOT/scripts/macos.entitlements" --noInst -o "$RELEASES"
  rm -rf "$APP"
  ditto -x -k "$RELEASES/UltrastarDJ-$RID-Portable.zip" "$ROOT/out"
  # The portable zip is the same app as the dmg; one download per Mac is enough on the Releases page.
  rm "$RELEASES/UltrastarDJ-$RID-Portable.zip"
  codesign --verify --strict "$APP" && echo "✓ signature valid after Velopack"
else
  echo "  (no vpk: the app will not update itself — dotnet tool install -g vpk)"
fi

echo "→ building dmg"
DMG="$RELEASES/Ultrastar-DJ-$VERSION-$RID.dmg"
STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -quiet -volname "Ultrastar DJ" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"
echo "✓ $DMG ($(du -sh "$DMG" | cut -f1))"
echo "  First launch on another Mac: System Settings → Privacy & Security → Open Anyway"
