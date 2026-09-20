#!/usr/bin/env zsh
# Build a release .app + .dmg for macOS.
#
#   zsh scripts/publish-macos.sh              # RID from this machine's CPU
#   zsh scripts/publish-macos.sh osx-x64      # explicit RID
#
# Output: out/<rid>/  (publish dir),  out/Ultrastar DJ.app,  out/Ultrastar-DJ-<version>-<rid>.dmg
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
RID=${1:-$([[ $(uname -m) == arm64 ]] && echo osx-arm64 || echo osx-x64)}
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)
[[ -n "$VERSION" ]] || { echo "✗ <Version> not found in Directory.Build.props"; exit 1; }
[[ -d "$ROOT/natives/$RID" ]] || { echo "✗ natives/$RID missing — run scripts/fetch-natives.sh first"; exit 1; }

OUT="$ROOT/out/$RID"
rm -rf "$OUT"
echo "→ dotnet publish $RID v$VERSION"
dotnet publish "$ROOT/src/UltrastarDJ.App" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -nologo -v q -o "$OUT"

zsh "$ROOT/scripts/bundle-macos.sh" "$OUT" "$VERSION" "$RID"
