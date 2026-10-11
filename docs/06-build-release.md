# 06 — Build, run, release

Targets: **macOS (arm64 + x64, macOS 14+)**, **Windows (x64)** and **Linux (x64 AppImage, system libmpv)**.

---

## Prerequisites per machine

1. **.NET SDK 10** (version pinned in `global.json`). Nothing else is compiled natively, so no Xcode / Visual
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
| `libmpv` (`libmpv.2.dylib` / `libmpv-2.dll`) | media decode/playback | in-process (`DllImport`) — **must match CPU arch** | mac: IINA's universal libmpv + dylibs (LuaJIT for YouTube; Homebrew builds need the newest macOS); win: shinchiro/zhongfly `mpv-dev` builds |
| PortAudio | audio I/O | via `PortAudioSharp2` NuGet (ships native libs) | NuGet |
| `yt-dlp` | YouTube resolution (called by mpv's ytdl hook) | separate process, **folder build** in `natives/<rid>/yt-dlp/` | GitHub releases `yt-dlp_macos.zip` (universal) / `yt-dlp_win.zip`. Not the single-file `yt-dlp_macos`/`yt-dlp.exe`: those unpack a Python runtime on every run (~9 s on macOS vs 0.2 s) |
| `ffmpeg` | used by yt-dlp when muxing is needed | separate process | mac: evermeet.cx static; win: gyan.dev / BtbN builds |

`fetch-natives.*` detects OS + arch (`uname -m` / `$env:PROCESSOR_ARCHITECTURE`), downloads into
`natives/osx-arm64`, `natives/osx-x64`, `natives/win-x64`, and is idempotent (skips existing files).
`UltrastarDJ.App.csproj` copies `natives/$(RuntimeIdentifier)/**` next to the executable on build/publish.

Rule of thumb: **in-process libraries** (libmpv, PortAudio) must be the exact architecture of the app;
**sidecar processes** (yt-dlp, ffmpeg, cloudflared) may be universal or even x64 under Rosetta.

`SidecarLocator` resolves sidecars in this order: next to the executable → `natives/<rid>/` (dev) → `PATH`.

---

## Development

```
dotnet build                          # whole solution
dotnet test                           # all test projects
dotnet run --project src/UltrastarDJ.App
```

Settings and logs live in the per-user app data folder (`~/Library/Application Support/UltrastarDJ`,
`%APPDATA%\UltrastarDJ`). Delete the folder for a clean start. Inside: `logs/ultrastardj-YYYYMMDD.log` (Serilog, one per
day, a new file after 10 MB, 7 files kept), `settings/*.json`, `library.db`, `cache/`. Native crashes (audio threads) end up in macOS's
`~/Library/Logs/DiagnosticReports/UltrastarDJ-*.ips`. Settings → Logs & data opens all of these for users.

---

## Release build

### macOS (`scripts/publish-macos.sh`)

```
dotnet publish src/UltrastarDJ.App -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=false -p:DebugType=none -o out/osx-arm64
scripts/bundle-macos.sh out/osx-arm64 0.2.0 osx-arm64    # creates and signs out/Ultrastar DJ.app
vpk pack … -p "out/Ultrastar DJ.app" --channel osx-arm64 --signAppIdentity - --signDisableDeep \
  --signEntitlements scripts/macos.entitlements --noInst      # adds the updater, re-signs the bundle
# dmg from the app Velopack made → out/Releases/Ultrastar-DJ-<version>-osx-arm64.dmg
```

Velopack adds its updater (`UpdateMac`) to the bundle, so it signs: `-` is the same ad-hoc identity,
`--signDisableDeep` keeps our per-file signatures (its `--deep` would fail on yt-dlp's folder), and it signs with
the **hardened runtime** — `scripts/macos.entitlements` lists what .NET needs plus
`com.apple.security.device.audio-input` (without it the microphone delivers silence). The dmg is built from
Velopack's app, so an app installed from the dmg updates itself; Velopack's portable zip of the same app is
dropped. Without `vpk` on the PATH the script still builds a dmg, of an app that does not update.

`bundle-macos.sh`:
1. Creates `Ultrastar DJ.app/Contents/{MacOS,Resources}`.
2. Copies publish output into `MacOS/`, `icon.icns` into `Resources/`.
3. Writes `Info.plist` (bundle id `com.retokupfer.ultrastardj`, version, `NSMicrophoneUsageDescription`,
   `LSMinimumSystemVersion`, `NSHighResolutionCapable`).
4. Fixes dylib load paths if needed (`install_name_tool` → `@executable_path`).
5. **Ad-hoc signs** without a certificate or Apple account (required on Apple Silicon). Not `--deep`: it takes
   yt-dlp's `*.dist-info` folders for bundles. codesign treats every file in `Contents/MacOS` as code, so the
   tools (yt-dlp's folder build, ffmpeg, cloudflared) move to `Contents/Resources/natives/` (`SidecarLocator`
   looks there too); libmpv and its dylibs stay in `Contents/MacOS/natives/`. Each file in `Contents/MacOS` is
   signed on its own (.NET's `.dll` too — the signature goes into extended attributes), then ffmpeg and
   cloudflared, then the bundle; yt-dlp keeps the signature it ships with (its flattened `Python.framework`
   cannot be re-signed).
6. Builds the dmg with `hdiutil` (or `create-dmg`).

**Supported: macOS 14 (Sonoma) or newer**, Apple Silicon and Intel (2018+). Older Macs were tried (macOS 12, Intel): libmpv
did not load. Info.plist says 14.0, so macOS refuses the app on older systems with its own clear message instead of a
failure at the first song.


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

### Linux (`scripts/publish-linux.sh`, on Linux)

`dotnet publish -r linux-x64` → `vpk pack` → `UltrastarDJ.AppImage` + update packages. libmpv is not in the
AppImage: the app uses the system's (`libmpv.so.2`, or `.so.1` on Ubuntu 22.04 / Mint 21) and says what to install
when it is missing. Built on Ubuntu 22.04 so it runs there and on everything newer.

### Versioning

`<Version>` in `Directory.Build.props` (SemVer, e.g. `0.2.0-beta.1`) for local builds. `$VERSION` (CI: the tag
without its `v`) wins over it; the scripts pass it to `dotnet publish` (`BuildInfo.Version`, About, Settings) and to
`vpk`. Bump `<Version>` together with the tag.

## Releases and updates

**Publishing a version:** bump `<Version>`, commit, then `git tag v0.2.0-beta.2 && git push origin v0.2.0-beta.2`.
The workflow builds all four packages and its `release` job creates the GitHub Release (title "Ultrastar DJ
<version>", text from `.github/release-notes.md` with the download table and the first-start steps). A version with
`-` is a pre-release. Before packing, each job fetches the previous release (`vpk download github`), so Velopack
also builds a small delta package; the old full package itself is not re-uploaded.

Velopack channels keep the systems apart in one release: `osx-arm64`, `osx-x64`, `win`, `linux`
(`releases.<channel>.json`, `*-full.nupkg`, `*-delta.nupkg`). Release assets for people: the two dmgs,
`UltrastarDJ-win-Setup.exe`, `UltrastarDJ.AppImage`.

**In the app** (`UpdateService`): 5 s after the start an installed app asks GitHub (pre-releases included) for a
newer version of its channel; the developer build never asks. Found → "Update available — Ultrastar DJ x.y.z is
available · Later / Update now", shown only while no song is on (idle or loaded, not from "Get ready" to the score).
Update now downloads with progress and restarts into the new version (`ApplyUpdatesAndRestart`); settings and songs
are in the user's data folder and stay. Later: asked again at the next start. A failed download keeps the offer with
the reason. Offline / GitHub unreachable: logged, nothing shown.

---

## CI (`.github/workflows/build.yml`)

GitHub Actions (free for this public repository): `macos-14` (Apple Silicon), `macos-15-intel` (Intel),
`windows-latest`, `ubuntu-22.04`. Each job: setup-dotnet (from `global.json`) → fetch-natives → `dotnet test` →
publish + Velopack pack (`vpk`, pinned to the SDK's version) → upload `out/Releases/*` as an artifact. Runs by hand
(Actions → Build → Run workflow, or `gh workflow run build.yml`; artifacts are downloadable when signed in) and on a
`v*` tag, which also publishes the Release (above). `VelopackApp.Build().Run()` is the first line of `Main`
(install / update hooks).

---

## Runtime permissions

| Platform | Microphone | Notes |
|---|---|---|
| macOS | TCC prompt on first PortAudio input open; requires `.app` bundle with `NSMicrophoneUsageDescription` | A bare `dotnet run` executable also prompts (via the terminal's identity) — fine for dev |
| Windows | Settings → Privacy → Microphone → "Allow desktop apps" | Show a hint dialog if device open fails with access denied |

Full-screen beamer windows on a second display: no special permission on either platform.
