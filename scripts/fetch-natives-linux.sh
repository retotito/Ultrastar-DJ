#!/usr/bin/env bash
# Download native dependencies for Linux x64 into natives/linux-x64/.
# Idempotent: existing files are skipped.
#
#   bash scripts/fetch-natives-linux.sh
#
# libmpv is not shipped: the app uses the system's (sudo apt install libmpv2, or libmpv1 on Ubuntu 22.04 /
# Linux Mint 21) and says so when it is missing. The sidecar processes are prebuilt downloads.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
DEST="$ROOT/natives/linux-x64"
mkdir -p "$DEST"
echo "→ natives for linux-x64 → $DEST"

# ── yt-dlp (folder build: starts fast; the single file unpacks Python on every run) ──
if [[ -x "$DEST/yt-dlp/yt-dlp" ]]; then
  echo "✓ yt-dlp present ($("$DEST/yt-dlp/yt-dlp" --version))"
else
  echo "→ downloading yt-dlp…"
  TMP=$(mktemp -d)
  curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux.zip" -o "$TMP/yt-dlp.zip"
  unzip -q "$TMP/yt-dlp.zip" -d "$TMP/yt-dlp"
  mv "$TMP/yt-dlp/yt-dlp_linux" "$TMP/yt-dlp/yt-dlp"
  chmod +x "$TMP/yt-dlp/yt-dlp"
  rm -rf "$DEST/yt-dlp"
  mv "$TMP/yt-dlp" "$DEST/yt-dlp"
  rm -rf "$TMP"
  echo "✓ yt-dlp $("$DEST/yt-dlp/yt-dlp" --version)"
fi

# ── ffmpeg (static GPL build from BtbN on GitHub) ────────────────────────────
if [[ -x "$DEST/ffmpeg" ]]; then
  echo "✓ ffmpeg present"
else
  echo "→ downloading ffmpeg…"
  TMP=$(mktemp -d)
  curl -fsSL "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz" -o "$TMP/ffmpeg.tar.xz"
  tar -xJf "$TMP/ffmpeg.tar.xz" -C "$TMP" --wildcards '*/bin/ffmpeg'
  mv "$TMP"/*/bin/ffmpeg "$DEST/ffmpeg"
  chmod +x "$DEST/ffmpeg"
  rm -rf "$TMP"
  echo "✓ ffmpeg"
fi

# ── cloudflared (songbook public link: Cloudflare Quick Tunnel) ──────────────
if [[ -x "$DEST/cloudflared" ]]; then
  echo "✓ cloudflared present"
else
  echo "→ downloading cloudflared…"
  curl -fsSL "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64" -o "$DEST/cloudflared"
  chmod +x "$DEST/cloudflared"
  echo "✓ cloudflared $("$DEST/cloudflared" --version | head -1)"
fi

echo "✓ done — $(du -sh "$DEST" | cut -f1) in natives/linux-x64"
