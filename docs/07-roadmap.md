# 07 — Roadmap

Risk first: prove the media engine before porting anything else. Each sprint ends with a demo an agent or
human can run. "Done" per task = `05-conventions.md` → Definition of done.

---

## Sprint 0 — Skeleton & pipeline

Goal: an empty but *shippable* app on both platforms.

- [x] Solution + projects per `01-architecture.md`, `Directory.Build.props`, `global.json`, `.editorconfig`
- [x] DI composition root, Serilog, `JsonSettingsStore`
- [x] `Styles/Tokens.axaml` (dark + light), Fluent base, `Icon` control with Material Symbols font, Inter font
- [x] `DjWindow` with sidebar + empty panels; `DisplayService` opening a fullscreen `BeamerWindow` on a chosen screen (and `--beamer-debug`)
- [x] `scripts/fetch-natives.{sh,ps1}`, `publish-macos.sh` + `bundle-macos.sh`, `publish-windows.ps1`
- [x] Builds, runs, and produces an installable artifact on macOS arm64 (dmg verified) — Windows x64 scripts written, **not yet run on a Windows machine**

**Demo:** launch app, open beamer on second screen showing "Ultrastar DJ" idle screen; dmg/Setup installs.

---

## Sprint 1 — Media spike (go / no-go)

Goal: the two problems that motivated the rewrite are demonstrably solved.

- [x] `MpvPlayer` P/Invoke wrapper (create, options, commands, property observe, events, SW render context)
- [x] `FrameBus` + `VideoSurface` control; frames in DJ monitor + 2 beamers from one player
- [x] `MediaChannel` Game/Preview with `audio-device` selection, gain, `astats` metering
- [x] `MediaSourceResolver` for all six cases (unit-tested) — playback verified for cases 2, 4, 6 (3 = same path as 2 with a YouTube visual)
- [x] `MediaGameClock` + `ClockFollower` (drift log: +138 ms at start → single digits within 5 s)
- [ ] Channel-pair routing via `pan` on a multichannel device (test with the MOTU) — **deferred to Sprint 5** (needs the interface at hand)
- [x] Readiness signal (buffering) for local and YouTube
- [x] Acceptance list in `02-media-engine.md` passes on macOS — **Windows still to run** (first clone on the Windows machine)

**Result: GO.** YouTube plays once per role with audio on a chosen CoreAudio device and the same frames on
the DJ monitor and two beamers; preview plays a local file on a different device at the same time.
Headless check: `dotnet run --project tools/MediaSmoke -- <file|youtubeId> [audioFile|-] [device]`.
Spike UI: sidebar → Media Lab (remove in Sprint 5 when the real preview/Now Playing UI lands).
Known: YouTube resolution via yt-dlp takes 10–15 s per load — pre-resolve when a song enters the queue (Sprint 5).

**Go/no-go:** if YouTube-to-device or frame fan-out fails fundamentally, stop and revisit the engine choice
(fallback: FFmpeg-decoded audio through PortAudio, mpv for video) **before** Sprint 2.

---

## Sprint 2 — Core port

- [x] `Song`/`Note` model, `UltraStarParser`, `BeatMath`, `SongValidator`, `Playlist` (named so because analyzers reject `*Queue`)
- [x] `PlayerScorer` (matching, joker, points, line bonus, per-note state), `PitchRingBuffer`, `NoteLaneGeometry.PitchToRow`
- [x] Edge-case tests from `03-game-engine.md` (64 Core tests) + `RealCorpusTests` (set `ULTRASTAR_SONGS=<folder>` to parse a real library)
- [x] `LocalFolderScanner` → `SqliteSongRepository`; Sources panel adds folders, library list in the DJ window, persists across restarts

**Demo:** `dotnet test` green; a folder of songs appears in a plain list in the DJ window.

---

## Sprint 3 — Audio input

- [x] `IAudioBackend` (PortAudio): enumerate, input by device+channel, output by device+offset; refresh only while idle (PortAudio must re-init) — hot-plug *loss* is detected via the stream going inactive, *arrival* needs a manual refresh
- [x] `MicPipeline`: gain → gate → level → YIN → ring buffer; 16 tests (YIN < ⅓ semitone E2–C6, harmonics → fundamental, L/R demux, gate, gain)
- [x] `MonitorMixer` (linear resampler between mic and output rates); per-player mix gain / mute
- [x] `LatencyTest` — Goertzel tone detector at 1 kHz, two-block sustain, plausibility window 12–800 ms; measured 44–48 ms MacBook speakers → SingStar mic
- [x] Audio Input panel (4 cards, device/channel pick, gain/gate/mix, dB meter, live note, test mode, monitor output, calibrate)

Hardware note: SingStar USB mics deliver ≈ −55 dBFS; defaults are gain 1×/gate 0.01 and the gain slider goes to 10×.
Headless check: `dotnet run --project tools/MicSmoke -- mics|monitor <out>|latency <out> <in> [L|R]`.

**Demo (done):** four mics on two SingStar dongles tracked independently; monitoring audible; latency calibrated.

---

## Sprint 4 — Game on the beamer

- [ ] `PlaybackService` state machine, `GameSession`, `GameTicker` (60 Hz), messages
- [ ] `GameOverlayControl`: note lanes (geometry, styles, syllables), sung fill, glow, PERFECT, playhead, lyrics with sweep + lead-in, progress
- [ ] Beamer states: idle, preview, countdown (reports `CountdownDone`), playing/paused, score (animated)
- [ ] Per-beamer player assignment; 2 beamers with different players
- [ ] Mic delay applied; `#VIDEOGAP` modes A/B verified with test songs; `#END`

**Demo:** full song with 2 players on 2 beamers, scores at the end; 60 fps on a 1080p beamer.

---

## Sprint 5 — DJ workflow

- [ ] Virtualized song table (search, filters, sort, badges, availability)
- [ ] Preview player (all cases, own device, fader/meter), Add to queue, Load
- [ ] Queue widget; Now Playing card (status, faders, mic mix rows, transport); popup locking while active
- [ ] Audio Output panel (game/preview device pairs, faders), Displays panel, Settings (theme, difficulty, lyrics offset)
- [ ] Validation dialog, offline handling, device-gone toasts

**Demo:** the full "typical evening" flow from `00-vision.md` without touching a config file.

---

## Sprint 6 — Sources, guests, polish

- [ ] `UsdbClient` (login, catalog sync with progress/abort, incremental, song txt), SQLite catalog, badges, auto-login
- [ ] Songbook server (Kestrel) + mobile page + PIN; optional tunnel sidecar
- [ ] yt-dlp self-update action; error dialogs for blocked/unavailable videos
- [ ] Packaging polish: icons, version display, README download instructions (Gatekeeper/SmartScreen)
- [ ] Remove debug logs, close TODOs, tag `v0.2.0-beta.1`

---

## Later / ideas

- OpenGL frame path (shared texture) if CPU copy shows up in profiles
- Global hotkeys, keyboard-only DJ operation
- Duet rendering (two tracks per lane pair), medley, `#PREVIEWSTART`
- Linux AppImage
- Code-signing certificates (Windows EV / Apple Developer) — optional, no architectural impact
