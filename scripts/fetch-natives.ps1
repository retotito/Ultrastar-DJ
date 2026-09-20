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

# ── yt-dlp ─────────────────────────────────────────────────────────────────
$ytdlp = Join-Path $Dest 'yt-dlp.exe'
if (Test-Path $ytdlp) { Write-Host "✓ yt-dlp present" }
else {
  Write-Host "→ downloading yt-dlp…"
  Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile $ytdlp
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

# ── libmpv-2.dll (shinchiro mpv-dev builds, published on SourceForge) ──────
$libmpv = Join-Path $Dest 'libmpv-2.dll'
if (Test-Path $libmpv) { Write-Host "✓ libmpv present" }
else {
  Write-Host "→ downloading libmpv…"
  $tmp = Join-Path ([IO.Path]::GetTempPath()) "mpv-$(Get-Random)"
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  # "latest" redirects to the newest mpv-dev-x86_64-*.7z; 7z is required to extract it.
  $url = 'https://sourceforge.net/projects/mpv-player-windows/files/libmpv/latest/download'
  Invoke-WebRequest $url -OutFile "$tmp\mpv-dev.7z" -UserAgent 'Mozilla/5.0'
  $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
  if (-not $sevenZip) { throw "7z not found. Install 7-Zip (winget install 7zip.7zip) and re-run." }
  & $sevenZip.Source x "$tmp\mpv-dev.7z" "-o$tmp\mpv" -y | Out-Null
  Copy-Item "$tmp\mpv\libmpv-2.dll" $libmpv
  Remove-Item $tmp -Recurse -Force
  Write-Host "✓ libmpv"
}

Write-Host "✓ done"
