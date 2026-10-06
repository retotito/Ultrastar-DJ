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

- [x] `PlaybackService` state machine (Idle → Loaded → Preview → Countdown → Playing ⇄ Paused → Score → Loaded), `GameSession` (Core, tested), 60 Hz tick loop; beamers observe the service directly (in-process events instead of a messenger)
- [x] `GameOverlayControl`: note lanes (geometry, styles, syllables), sung fill (correct in colour / wrong at sung row), glow, PERFECT, playhead, lyrics with sweep + lead-in, progress
- [x] Beamer states: idle, preview, countdown (first beamer to finish starts the song), playing/paused, score (animated count-up, winner)
- [x] Per-beamer player assignment (Displays panel chips; a player is on one screen only)
- [x] Per-player mic delay applied; `#VIDEOGAP` modes handled by the media plan; `#END` via mpv `end`, plus a 4 s tail after the last note
- [x] DJ: Now Playing card (transport, game gain/meter, monitor), library → double-click previews, right-click / ▶ loads into the game

Deferred to Sprint 5: difficulty setting UI (Medium hardcoded in `PlaybackService.Difficulty`), 2-beamer/4-player stress test on a 1080p projector, duet lyrics per track.

**Demo (done):** full song with four SingStar mics on one beamer, live fills and scores, score screen at the end.

---

## Sprint 5 — DJ workflow

- [x] Virtualized song table (search, language/genre filters, sortable headers, SOURCE badge, greyed rows when a source is unavailable — 5 s poll)
- [x] Preview player (all cases, own device, fader/meter, seek), row ⋮/context menu: Preview / Add to queue / Load into game; double-click previews
- [x] Queue widget (reorder, remove, load next, active row); Now Playing card (status, transport, game monitor); Audio Input/Output/Displays locked while Countdown/Playing/Paused
- [x] Audio Output panel (game/preview device + stereo pair, faders, meters; persisted and applied at startup — **MOTU channel pairs still untested**), Displays panel, Settings (light theme, difficulty, lyrics offset ±500 ms); Media Lab removed
- [x] Validation dialog on load/preview failure, device-gone toasts (mic disconnected)
- [x] Shutdown tears services down off the UI thread with a 5 s deadline (a stalled preview player used to hang the app on close)
- [x] **5b:** Now Playing is a floating, draggable card (sidebar toggle, position persisted) with song fader/meter and one mic-mix row per player on an open beamer (fader, mute, live meter → game output device incl. stereo pair; linked with the Audio Input panel); beamer idle screen shows its assigned players live

**Demo (done):** the full "typical evening" flow from `00-vision.md` without touching a config file.
Still open from earlier sprints: pre-resolving YouTube on queue-add, duet lyrics per track, 2-beamer stress test, Windows build.

---

## Sprint 6 — Sources, guests, polish  *(in progress)*

- [x] `UsdbClient` (login, streamed full catalog paging, incremental sync from the mtime watermark, song txt) + `UsdbHtml` scraper (AngleSharp); `SqliteUsdbCatalog` table in `library.db`; `UsdbService` (credentials persisted in `settings/usdb.json` — plain text, like the prototype; auto-connect at start; full/incremental sync with abort — a stopped full sync keeps what it fetched; disconnect removes the songs); green USDB badge, rows greyed while offline. **Verified live: 28.7k songs, preview and game from YouTube.**
- [x] `SongResolver` (App): one load path for game and preview — USDB txt from disk cache (`cache/usdb/<id>.txt`) or network → BPM/GAP/YouTube id/notes → `SongValidator`. USDB writes `#VIDEO:a=<ytid>,co=…,bg=…`; `YouTubeId.TryExtract` understands that form.
- [x] Songbook: `SongbookServer` (Kestrel, Infrastructure) serves the embedded `songbook.html` + `/api/songs|state|request|verify-pin`, optional 4-digit PIN; `SongbookService` (App) with persisted port/PIN/autostart, LAN URLs, guest requests → toast + REQUESTS section above the queue; Songbook sidebar panel. **Not yet tested from a phone.** Tunnel sidecar (bore) dropped for now — LAN only.
- [ ] yt-dlp self-update action; error dialogs for blocked/unavailable videos
- [ ] Packaging polish: icons, version display, README download instructions (Gatekeeper/SmartScreen)
- [ ] Remove debug logs, close TODOs, tag `v0.2.0-beta.1`

### Where to resume

1. *(Moved to later — see Sprint 6b.)* Phone test of the songbook (Songbook panel → Start → open the shown `http://192.168.x.x:4747` on a phone; request a song; try the PIN).
2. yt-dlp self-update (`yt-dlp -U` against the sidecar in `natives/<rid>/`, button in Settings) and a dialog when mpv reports a blocked/unavailable video (`MediaChannel.ErrorOccurred`).
3. Packaging polish, debug-log cleanup, roadmap/README update, tag.

Still open from earlier sprints: Windows build test, MOTU channel-pair verification, pre-resolving YouTube on queue-add, duet lyrics per track, 2-beamer stress test. The user also has small UI details to report after Sprint 6.

### Lessons that are not obvious from the code

- Never dispose services synchronously on the UI thread at `Exit` — the preview player teardown deadlocked and the app hung on close. `App.OnShutdownRequested` cancels, disposes on a worker with a 5 s deadline, then `Shutdown()` + `Environment.Exit`.
- A `ComboBox` whose `ItemsSource` is bound through `$parent[...]` resolves after `SelectedItem` when a panel view is recreated → the selection is written back as null. Expose the option list on the row view model and bind directly.
- Avalonia "was not able to start the RenderTimer (-6661)" at startup means the display is asleep / screen locked, not a code bug.
- Dapper `MatchNamesWithUnderscores` must be set before the first `Query<T>` (static ctor of the repository).
- mpv shutdown order: render context free → `quit` → join event thread → `terminate_destroy`.
- Never swap files in `natives/` under a running app: `MediaService` resolves the yt-dlp path once at start, so the app keeps the stale path ("youtube-dl failed: not found"). Quit, fetch, restart.
- Analyzers (warnings-as-errors) reject `*Queue`/`*Stream` type names, `params` as a parameter name, and XML docs on positional record parameters.

---

## Sprint 6b — Faster YouTube start  *(done)*

Found in first real use: opening a YouTube song in preview or game took ~12 s before sound. Not a download
(mpv streams into RAM) — 8.7 s of it was yt-dlp's single-file build unpacking itself on every run.

- [x] yt-dlp folder build (`natives/<rid>/yt-dlp/`, `SidecarLocator` prefers it); fetch scripts migrate the old single file; warm-up run at app start. **Measured: yt-dlp start 8.7 s → 0.19 s. Verified live: YouTube preview and game start in 2–3 s.** Windows fetch script not yet run on Windows.

Not done here: pre-resolving stream URLs (already a leftover above), error dialog for blocked/age-restricted
videos (Sprint 6 list), read-ahead cap (Later). Guest songbook phone test moved to later.

---

## Sprint 6c — UI inspector  *(done)*

- [x] Debug-only `UiInspector` (Avalonia's Developer Tools are paid): F12 overlay with type/name/classes/axaml file/DataContext/layout of the hovered control, Shift+F12 dumps the visual tree to `logs/`. See [04-ui.md](04-ui.md#ui-inspector-debug-builds). **Verified live in the DJ window.** Not in Release builds.

---

## Sprint 6d — Library: filter and sort by source  *(done)*

- [x] Source filter in the library header: All sources · All local folders · each folder · USDB. SOURCE column sorts by source name (ties: artist, title). Search/filter/sort moved from `LibraryViewModel` into `Core.Songs.SongQuery`, with tests. **Verified live.**
- [x] "LIBRARY" title removed from the header; shown/total song count moved to a footer at the bottom left (as in the prototype). **Verified live.**

---

## Sprint 6e — Sidebar panels: light dismiss and header  *(done)*

- [x] A press anywhere outside the open panel (and outside the sidebar buttons) closes it; the press still reaches its target. Now Playing is not affected. **Verified live.**
- [x] Shared panel header in `DjWindow`: icon, title, ✕, divider; body scrolls under it. Panel views lost their own titles; their actions moved to a right-aligned row. **Verified live.**

---

## Sprint 6f — Library: rating filter, filter names, clear  *(done)*

- [x] Rating = USDB popularity from views (★ 100+, ★★ 500+, ★★★ 1000+, ★★★★ 2000+), as in the prototype: `Song.Stars`, `SongQuery.Stars`, sort by views; RATING column; filter matches the exact star count (the prototype used "at least" — changed after live test), hides local songs. USDB's own rating column is not usable — it renders as images, the scraper stores 0 for every song.
- [x] Rating, Language and Genre filters show their name instead of "All" when unset.
- [x] "Clear" text button at the end of the filter row while rating/language/genre/source is set (as in the prototype; search is not reset); ✕ inside the search box clears the search.
- [x] Verified live

---

## Sprint 6g — One look for floating panels  *(done)*

- [x] Now Playing card uses the popover frame (subtle border, rounded shadow without spread, same padding) and the popover header (icon, title, drag hint, ✕, divider). Shared style in `Controls.axaml`. The square shadow was clipping by the card's container (`ClipToBounds`), not the shadow itself. **Verified live (screenshot).**
- [x] Context menus, ⋮ menus and filter drop-downs get the same shadow (`ShadowOverlay` token, `ShadowRoom` margin on the frame inside their popup windows — a margin on the menu control itself did not work). **Verified live.**

---

## Sprint 6h — UI fixes: sizes, rows, cursors, faders  *(done)*

- [x] Sidebar navigation icons 36px (`IconNav`) with 20px gaps, as in the prototype; other icon buttons unchanged.
- [x] Body text 14 → 16px app-wide (`Window` font size + Fluent's `ControlContentThemeFontSize`); explicit sizes (labels, titles, small notes) unchanged. The prototype's CSS said 14px but its screenshots render ~16px.
- [x] Settings panel anchors to the window's bottom (its button is at the sidebar's bottom); others stay at the top.
- [x] Cursors: hand on buttons, drop-downs, menu items, toggles; Now Playing header open hand, closed fist while dragging (prototype's grab/grabbing). Windows: check what `DragMove` looks like.
- [x] `Fader` for every volume/gain/gate/offset slider: knob only (track clicks ignored), open hand / fist on the knob, double-click resets (100%, gate −50 dB, offset 0). Song position slider keeps click-to-jump.
- [x] Song list: zebra stripes (`ColorTableRowAlt`, on-surface 4% as in the prototype, `:nth-child(2n)`), rows 53 → 43px.
- [x] Verified live

---

## Sprint 6i — Preview player logic  *(done)*

- [x] Stop button removed: pause covers silence, loading another song replaces the current one, the position slider rewinds; stop only threw away the song (and disabled Add to queue / Load). Load into the game does not pause the preview: loading does not start the song, and preview (headphones) and game use separate outputs.
- [x] Verified live

---

## Sprint 6j — Beamer windows you can move  *(done)*

Found in use: a beamer opened on the DJ's screen covered the DJ window for good — it was an owned window (always in
front of its owner), borderless and fullscreen on a screen picked in code.

- [x] Beamers are independent, normal windows: 960×540 on the DJ window's screen (beamer 2 cascaded), title bar, resizable; the DJ drags them to the projector. No screen picker (removed `ScreenInfo`, `DisplayConfig.ScreenName`, `--beamer-debug`).
- [x] Fullscreen on the window's current screen: Displays panel button, double-click or F; Esc leaves fullscreen and no longer closes; pointer hides after 2 s in fullscreen.
- [x] Closing the DJ window closes all beamers first; closing one beamer keeps the other and updates the Displays panel.
- [x] Fix: picking an entry in a drop-down inside a sidebar panel (e.g. mic selection) closed the panel. The list is drawn outside the panel but is its logical descendant; light dismiss now checks the logical tree too.
- [x] Now Playing card stays inside the DJ window: clamped on every window resize and when the card itself grows (saved position not overwritten — only dragging saves).
- [x] Verified live on macOS
- [ ] Windows/Linux: check fullscreen and that the DJ window comes to the front (with the Windows build test)

---

## Sprint 6k — Inspector in its own window  *(done)*

- [x] F12 opens a separate always-on-top UI Inspector window instead of the tooltip box; the overlay only outlines. More detail (pseudo-classes, alignment, visibility/enabled/opacity, font family/weight, corners, full path), selectable text, Copy and Dump tree buttons. Alt/Option+click pins a control (orange) without clicking it; the next Alt+click anywhere or the Unpin button releases it.
- [x] Verified live

---

## Later / ideas

- Cap mpv read-ahead (~60 s) so YouTube streams like a player instead of pulling the whole song into RAM
- OpenGL frame path (shared texture) if CPU copy shows up in profiles
- Global hotkeys, keyboard-only DJ operation
- Duet rendering (two tracks per lane pair), medley, `#PREVIEWSTART`
- Linux AppImage
- Code-signing certificates (Windows EV / Apple Developer) — optional, no architectural impact
