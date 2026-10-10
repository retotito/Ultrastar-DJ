<#
.SYNOPSIS
  Download native dependencies for this Windows machine into natives\win-x64\.
  Idempotent: existing files are skipped.

  pwsh scripts\fetch-natives.ps1
#>
$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Dest = Join-Path $Root 'natives\win-x64'
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
Write-Host "→ natives for win-x64 → $Dest"

# ── yt-dlp (folder build) ──────────────────────────────────────────────────
# The folder build (yt-dlp_win.zip) starts fast; the single-file yt-dlp.exe unpacks a Python runtime
# on every run, and mpv runs yt-dlp for every YouTube load.
$ytdlpDir = Join-Path $Dest 'yt-dlp'
$ytdlp = Join-Path $ytdlpDir 'yt-dlp.exe'
if (Test-Path $ytdlp) { Write-Host "✓ yt-dlp present ($(& $ytdlp --version))" }
else {
  Write-Host "→ downloading yt-dlp…"
  $tmp = Join-Path ([IO.Path]::GetTempPath()) "yt-dlp-$(Get-Random)"
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_win.zip' -OutFile "$tmp\yt-dlp.zip"
  Expand-Archive "$tmp\yt-dlp.zip" -DestinationPath "$tmp\yt-dlp"
  # Also replaces an older single-file build.
  Remove-Item (Join-Path $Dest 'yt-dlp.exe') -Force -ErrorAction SilentlyContinue
  if (Test-Path $ytdlpDir) { Remove-Item $ytdlpDir -Recurse -Force }
  Copy-Item "$tmp\yt-dlp" $ytdlpDir -Recurse   # Move-Item cannot move folders across drives
  Remove-Item $tmp -Recurse -Force
  Write-Host "✓ yt-dlp $(& $ytdlp --version)"
}

# ── ffmpeg (gyan.dev essentials build) ─────────────────────────────────────
$ffmpeg = Join-Path $Dest 'ffmpeg.exe'
if (Test-Path $ffmpeg) { Write-Host "✓ ffmpeg present" }
else {
  Write-Host "→ downloading ffmpeg…"
  $tmp = Join-Path ([IO.Path]::GetTempPath()) "ffmpeg-$(Get-Random)"
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile "$tmp\ffmpeg.zip"
  Expand-Archive "$tmp\ffmpeg.zip" -DestinationPath $tmp
  Copy-Item (Get-ChildItem "$tmp" -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName $ffmpeg
  Remove-Item $tmp -Recurse -Force
  Write-Host "✓ ffmpeg"
}

# ── cloudflared (songbook public link: Cloudflare Quick Tunnel) ─────────────
$cloudflared = Join-Path $Dest 'cloudflared.exe'
if (Test-Path $cloudflared) { Write-Host "✓ cloudflared present" }
else {
  Write-Host "→ downloading cloudflared…"
  Invoke-WebRequest 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe' -OutFile $cloudflared
  Write-Host "✓ cloudflared"
}

# ── libmpv-2.dll (mpv-dev builds: zhongfly on GitHub, shinchiro on SourceForge as fallback) ──
$libmpv = Join-Path $Dest 'libmpv-2.dll'
if (Test-Path $libmpv) { Write-Host "✓ libmpv present" }
else {
  Write-Host "→ downloading libmpv…"
  $tmp = Join-Path ([IO.Path]::GetTempPath()) "mpv-$(Get-Random)"
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  # GitHub first: SourceForge's mirrors often refuse GitHub Actions machines. The plain x86_64 build (not "-v3",
  # which needs AVX2) runs on every 64-bit PC. A token (CI) avoids GitHub's rate limit for anonymous calls.
  $headers = @{ 'User-Agent' = 'UltrastarDJ-fetch-natives' }
  if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
  $url = $null
  try {
    $release = Invoke-RestMethod 'https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest' -Headers $headers
    $url = ($release.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d{8}-git-.+\.7z$' } | Select-Object -First 1).browser_download_url
  } catch { Write-Host "  GitHub not reachable: $($_.Exception.Message)" }
  if ($url) { Invoke-WebRequest $url -OutFile "$tmp\mpv-dev.7z" }
  else {
    # "latest" redirects to the newest mpv-dev-x86_64-*.7z.
    Invoke-WebRequest 'https://sourceforge.net/projects/mpv-player-windows/files/libmpv/latest/download' -OutFile "$tmp\mpv-dev.7z" -UserAgent 'Mozilla/5.0'
  }
  # 7z is required to extract it.
  $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
  if (-not $sevenZip) { throw "7z not found. Install 7-Zip (winget install 7zip.7zip) and re-run." }
  & $sevenZip.Source x "$tmp\mpv-dev.7z" "-o$tmp\mpv" -y | Out-Null
  Copy-Item "$tmp\mpv\libmpv-2.dll" $libmpv
  Remove-Item $tmp -Recurse -Force
  Write-Host "✓ libmpv"
}

Write-Host "✓ done"
