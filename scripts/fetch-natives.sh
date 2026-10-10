#!/usr/bin/env zsh
# Download native dependencies for THIS machine into natives/<rid>/.
# Idempotent: existing files are skipped. Run once after cloning.
#
#   zsh scripts/fetch-natives.sh
#
# In-process libraries (libmpv + its dependencies) come from IINA's app (universal, macOS 11+).
# Sidecar processes (yt-dlp, ffmpeg, cloudflared) are downloaded as prebuilt binaries.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
ARCH=$(uname -m)
case "$ARCH" in
  arm64)  RID=osx-arm64 ;;
  x86_64) RID=osx-x64 ;;
  *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac
DEST="$ROOT/natives/$RID"
mkdir -p "$DEST"
echo "→ natives for $RID → $DEST"

# ── yt-dlp (universal folder build) ─────────────────────────────────────────
# The folder build (yt-dlp_macos.zip) starts in ~0.2 s. The single-file yt-dlp_macos unpacks a
# Python runtime on every run (~9 s), and mpv runs yt-dlp for every YouTube load.
if [[ -x "$DEST/yt-dlp/yt-dlp" ]]; then
  echo "✓ yt-dlp present ($("$DEST/yt-dlp/yt-dlp" --version))"
else
  echo "→ downloading yt-dlp…"
  TMP=$(mktemp -d)
  curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos.zip" -o "$TMP/yt-dlp.zip"
  unzip -q "$TMP/yt-dlp.zip" -d "$TMP/yt-dlp"
  mv "$TMP/yt-dlp/yt-dlp_macos" "$TMP/yt-dlp/yt-dlp"
  chmod +x "$TMP/yt-dlp/yt-dlp"
  rm -rf "$DEST/yt-dlp"   # also replaces an older single-file build
  mv "$TMP/yt-dlp" "$DEST/yt-dlp"
  rm -rf "$TMP"
  # The first run of a new binary is scanned by macOS (~9 s); pay that here, not on the first song.
  echo "✓ yt-dlp $("$DEST/yt-dlp/yt-dlp" --version)"
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

# ── cloudflared (songbook public link: Cloudflare Quick Tunnel) ──────────────
if [[ -x "$DEST/cloudflared" ]]; then
  echo "✓ cloudflared present ($("$DEST/cloudflared" --version | head -1))"
else
  echo "→ downloading cloudflared…"
  case "$ARCH" in arm64) CF=cloudflared-darwin-arm64.tgz ;; *) CF=cloudflared-darwin-amd64.tgz ;; esac
  TMP=$(mktemp -d)
  curl -fsSL "https://github.com/cloudflare/cloudflared/releases/latest/download/$CF" -o "$TMP/cf.tgz"
  tar -xzf "$TMP/cf.tgz" -C "$TMP"
  mv "$TMP/cloudflared" "$DEST/cloudflared"
  chmod +x "$DEST/cloudflared"
  rm -rf "$TMP"
  echo "✓ cloudflared $("$DEST/cloudflared" --version | head -1)"
fi

# ── libmpv + dependency dylibs (from IINA) ──────────────────────────────────
# Homebrew builds for the macOS it runs on (15 on GitHub's Intel runner), so its dylibs refuse to load on older
# Macs. IINA ships a universal libmpv (x86_64 + arm64, macOS 11+) with LuaJIT, which mpv's YouTube support
# (ytdl_hook) needs. Its dylibs name each other via @rpath; IINA's executable supplies that rpath, ours does not,
# so every dylib gets @loader_path as rpath.
IINA_VERSION=1.5.0
if [[ -f "$DEST/libmpv.2.dylib" ]]; then
  echo "✓ libmpv present"
else
  echo "→ downloading IINA $IINA_VERSION for its libmpv…"
  TMP=$(mktemp -d)
  curl -fsSL "https://github.com/iina/iina/releases/download/v$IINA_VERSION/IINA.v$IINA_VERSION.dmg" -o "$TMP/iina.dmg"
  hdiutil attach -quiet -nobrowse -readonly -mountpoint "$TMP/mnt" "$TMP/iina.dmg"
  FW="$TMP/mnt/IINA.app/Contents/Frameworks"
  # libmpv and everything it loads, nothing else (IINA's Swift libraries are not needed).
  typeset -A seen
  queue=(libmpv.2.dylib)
  while (( ${#queue} )); do
    lib=${queue[1]}; queue=(${queue[2,-1]})
    [[ -n ${seen[$lib]:-} ]] && continue
    seen[$lib]=1
    cp "$FW/$lib" "$DEST/$lib"
    for dep in $(otool -L "$FW/$lib" | awk 'NR>1 && $1 ~ /^@rpath\// { sub("@rpath/", "", $1); print $1 }'); do
      [[ -z ${seen[$dep]:-} ]] && queue+=($dep)
    done
  done
  hdiutil detach -quiet "$TMP/mnt"
  rm -rf "$TMP"
  for lib in "$DEST"/*.dylib; do
    chmod u+w "$lib"
    install_name_tool -add_rpath @loader_path "$lib" 2>/dev/null
    codesign --force -s - "$lib" 2>/dev/null   # IINA's signature breaks with the new rpath
  done
  echo "✓ libmpv + $(ls "$DEST"/*.dylib | wc -l | tr -d ' ') dylibs (IINA $IINA_VERSION, macOS 11+)"
fi

echo "✓ done — $(du -sh "$DEST" | cut -f1) in natives/$RID"
