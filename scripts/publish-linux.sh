#!/usr/bin/env bash
# Build a self-contained Linux x64 release and (if Velopack's `vpk` is installed) an AppImage. Run on Linux.
#
#   bash scripts/publish-linux.sh
#
# Install vpk once:  dotnet tool install -g vpk
# Output: out/linux-x64/  and  out/Releases/  (UltrastarDJ.AppImage + update packages)
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
RID=linux-x64
# $VERSION (CI: the tag) wins over Directory.Build.props.
VERSION=${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)}
[[ -n "$VERSION" ]] || { echo "✗ <Version> not found in Directory.Build.props"; exit 1; }
[[ -d "$ROOT/natives/$RID" ]] || { echo "✗ natives/$RID missing — run scripts/fetch-natives-linux.sh first"; exit 1; }

OUT="$ROOT/out/$RID"
rm -rf "$OUT"
echo "→ dotnet publish $RID v$VERSION"
dotnet publish "$ROOT/src/UltrastarDJ.App" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -p:Version="$VERSION" -nologo -v q -o "$OUT"
# The copy into the publish folder drops the executable bit of the tools.
chmod +x "$OUT/UltrastarDJ" "$OUT/natives/"{yt-dlp/yt-dlp,ffmpeg,cloudflared}

if ! command -v vpk >/dev/null; then
  echo "✓ published to $OUT (install Velopack for an AppImage: dotnet tool install -g vpk)"
  exit 0
fi

echo "→ vpk pack"
vpk pack -u UltrastarDJ -v "$VERSION" -p "$OUT" -e UltrastarDJ \
  --packTitle 'Ultrastar DJ' --packAuthors 'Reto Küpfer' \
  --icon "$ROOT/src/UltrastarDJ.App/Assets/icon.png" -o "$ROOT/out/Releases"
echo "✓ out/Releases/$(cd "$ROOT/out/Releases" && ls *.AppImage)"
