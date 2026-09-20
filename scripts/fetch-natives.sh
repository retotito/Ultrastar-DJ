#!/usr/bin/env zsh
# Download native dependencies for THIS machine into natives/<rid>/.
# Idempotent: existing files are skipped. Run once after cloning, and again after `brew upgrade mpv`.
#
#   zsh scripts/fetch-natives.sh
#
# In-process libraries (libmpv + its dependencies) must match the CPU architecture and are
# collected from Homebrew's mpv with dylibbundler. Sidecar processes (yt-dlp, ffmpeg) are
# downloaded as prebuilt binaries.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
ARCH=$(uname -m)
case "$ARCH" in
  arm64)  RID=osx-arm64; BREW_PREFIX=/opt/homebrew ;;
  x86_64) RID=osx-x64;   BREW_PREFIX=/usr/local ;;
  *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac
DEST="$ROOT/natives/$RID"
mkdir -p "$DEST"
echo "→ natives for $RID → $DEST"

# ── yt-dlp (universal binary) ────────────────────────────────────────────────
if [[ -x "$DEST/yt-dlp" ]]; then
  echo "✓ yt-dlp present ($("$DEST/yt-dlp" --version))"
else
  echo "→ downloading yt-dlp…"
  curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos" -o "$DEST/yt-dlp"
  chmod +x "$DEST/yt-dlp"
  echo "✓ yt-dlp $("$DEST/yt-dlp" --version)"
fi

# ── ffmpeg (static; evermeet serves a build that runs on both arches) ────────
if [[ -x "$DEST/ffmpeg" ]]; then
  echo "✓ ffmpeg present"
else
  echo "→ downloading ffmpeg…"
  TMP=$(mktemp -d)
  curl -fsSL "https://evermeet.cx/ffmpeg/getrelease/ffmpeg/zip" -o "$TMP/ffmpeg.zip"
  unzip -q "$TMP/ffmpeg.zip" -d "$TMP"
  mv "$TMP/ffmpeg" "$DEST/ffmpeg"
  chmod +x "$DEST/ffmpeg"
  rm -rf "$TMP"
  echo "✓ ffmpeg"
fi

# ── libmpv + dependency dylibs ───────────────────────────────────────────────
if [[ -f "$DEST/libmpv.2.dylib" ]]; then
  echo "✓ libmpv present"
else
  if ! command -v brew >/dev/null; then
    echo "✗ Homebrew not found — install it, then: brew install mpv dylibbundler"; exit 1
  fi
  for f in mpv dylibbundler; do
    brew list --versions "$f" >/dev/null 2>&1 || { echo "→ brew install $f"; brew install "$f"; }
  done
  SRC=$(brew --prefix mpv)/lib/libmpv.2.dylib
  [[ -f "$SRC" ]] || { echo "✗ $SRC not found"; exit 1; }
  echo "→ collecting libmpv and dependencies with dylibbundler…"
  # dylibbundler -od deletes the dest folder, so work in a staging dir and merge afterwards.
  STAGE=$(mktemp -d)
  cp "$SRC" "$STAGE/libmpv.2.dylib"
  chmod u+w "$STAGE/libmpv.2.dylib"
  # -x: binary to fix; -d: where deps go; -p: new install-name prefix; -b: bundle deps; -cd: create dest
  dylibbundler -cd -b -x "$STAGE/libmpv.2.dylib" -d "$STAGE/libs" -p "@loader_path/" 2>&1 | grep -v "install_name_tool: warning" >/dev/null
  mv "$STAGE/libs"/*.dylib "$DEST/"
  mv "$STAGE/libmpv.2.dylib" "$DEST/"
  rm -rf "$STAGE"
  # Homebrew dylibs are ad-hoc signed; rewriting load commands invalidates that → re-sign.
  for lib in "$DEST"/*.dylib; do codesign --force -s - "$lib" 2>/dev/null; done
  echo "✓ libmpv + $(ls "$DEST"/*.dylib | wc -l | tr -d ' ') dylibs"
fi

echo "✓ done — $(du -sh "$DEST" | cut -f1) in natives/$RID"
