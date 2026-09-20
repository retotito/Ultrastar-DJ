# 06 — Build, run, release

Targets: **macOS (arm64 + x64)** and **Windows (x64)**. Linux is architecturally supported, not shipped.

---

## Prerequisites per machine

1. **.NET SDK 8** (version pinned in `global.json`). Nothing else is compiled natively, so no Xcode / Visual
   Studio Build Tools are required.
2. `git clone https://github.com/retotito/Ultrastar-DJ.git`
3. Fetch native binaries for *this* machine (never committed):
   - macOS: `zsh scripts/fetch-natives.sh`
   - Windows: `pwsh scripts/fetch-natives.ps1`
4. `dotnet run --project src/UltrastarDJ.App`

That is the entire onboarding — the same "clone, one script, build" flow as the prototype, minus the Rust and Node toolchains.

---

## Native dependencies (`natives/<rid>/`)

| Binary | Role | Loaded how | Source (verify at implementation time) |
|---|---|---|---|
| `libmpv` (`libmpv.2.dylib` / `libmpv-2.dll`) | media decode/playback | in-process (`DllImport`) — **must match CPU arch** | mac: Homebrew `mpv` + `dylibbundler` to collect dependencies, or a self-contained libmpv build; win: shinchiro/zhongfly `mpv-dev` builds |
| PortAudio | audio I/O | via `PortAudioSharp2` NuGet (ships native libs) | NuGet |
| `yt-dlp` | YouTube resolution (called by mpv's ytdl hook) | separate process | GitHub releases (`yt-dlp_macos` universal, `yt-dlp.exe`) |
| `ffmpeg` | used by yt-dlp when muxing is needed | separate process | mac: evermeet.cx static; win: gyan.dev / BtbN builds |

`fetch-natives.*` detects OS + arch (`uname -m` / `$env:PROCESSOR_ARCHITECTURE`), downloads into
`natives/osx-arm64`, `natives/osx-x64`, `natives/win-x64`, and is idempotent (skips existing files).
`UltrastarDJ.App.csproj` copies `natives/$(RuntimeIdentifier)/**` next to the executable on build/publish.

Rule of thumb: **in-process libraries** (libmpv, PortAudio) must be the exact architecture of the app;
**sidecar processes** (yt-dlp, ffmpeg) may be universal or even x64 under Rosetta.

`SidecarLocator` resolves sidecars in this order: next to the executable → `natives/<rid>/` (dev) → `PATH`.

---

## Development

```
dotnet build                          # whole solution
dotnet test                           # all test projects
dotnet run --project src/UltrastarDJ.App
dotnet run --project src/UltrastarDJ.App -- --beamer-debug   # opens beamer 1 as a normal window on the same screen
```

Settings and logs live in the per-user app data folder (`~/Library/Application Support/UltrastarDJ`,
`%APPDATA%\UltrastarDJ`). Delete the folder for a clean start.

---

## Release build

### macOS (`scripts/publish-macos.sh`)

```
dotnet publish src/UltrastarDJ.App -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -o out/osx-arm64
scripts/bundle-macos.sh out/osx-arm64 "Ultrastar DJ" 0.2.0     # creates out/Ultrastar DJ.app
```

`bundle-macos.sh`:
1. Creates `Ultrastar DJ.app/Contents/{MacOS,Resources}`.
2. Copies publish output into `MacOS/`, `icon.icns` into `Resources/`.
3. Writes `Info.plist` (bundle id `com.retokupfer.ultrastardj`, version, `NSMicrophoneUsageDescription`,
   `LSMinimumSystemVersion`, `NSHighResolutionCapable`).
4. Fixes dylib load paths if needed (`install_name_tool` → `@executable_path`).
5. **Ad-hoc signs** everything: `codesign --force --deep -s - "Ultrastar DJ.app"` — required on Apple
   Silicon; no certificate, no Apple account.
6. Builds the dmg with `hdiutil` (or `create-dmg`).

Repeat with `-r osx-x64` on (or for) Intel. No universal binary — two dmgs, as the prototype shipped.

**Gatekeeper:** unsigned-by-Apple apps show "cannot verify developer". Users: System Settings →
Privacy & Security → *Open Anyway* (macOS 15+), or `xattr -d com.apple.quarantine "Ultrastar DJ.app"`.
Document this in the README download section. This is the same situation as UltraStar Deluxe.

### Windows (`scripts/publish-windows.ps1`)

```
dotnet publish src/UltrastarDJ.App -c Release -r win-x64 --self-contained -o out/win-x64
vpk pack -u UltrastarDJ -v 0.2.0 -p out/win-x64 -e UltrastarDJ.App.exe --packTitle "Ultrastar DJ"
```

Velopack produces `Setup.exe` + update packages. Unsigned → SmartScreen warning on first run; acceptable
for now (a code-signing certificate can be added later without architectural change).

### Versioning

Single source: `<Version>` in `Directory.Build.props`. Scripts read it; tags are `v0.2.0`.

---

## CI (optional, recommended once Sprint 1 passes)

GitHub Actions matrix: `macos-14` (arm64), `macos-13` (x64), `windows-latest`. Steps: setup-dotnet →
fetch-natives → `dotnet test` → publish → bundle → upload artifacts; on tag, create a GitHub Release with the
dmg/Setup files. Native downloads are cached by hash.

---

## Runtime permissions

| Platform | Microphone | Notes |
|---|---|---|
| macOS | TCC prompt on first PortAudio input open; requires `.app` bundle with `NSMicrophoneUsageDescription` | A bare `dotnet run` executable also prompts (via the terminal's identity) — fine for dev |
| Windows | Settings → Privacy → Microphone → "Allow desktop apps" | Show a hint dialog if device open fails with access denied |

Full-screen beamer windows on a second display: no special permission on either platform.
