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
- [x] yt-dlp self-update action; error dialogs for blocked/unavailable videos — done in Sprint 13
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
- An exception escaping a PortAudio callback aborts the whole process (it runs on CoreAudio's IO thread). `PaStream.OnCallback` therefore catches, plays silence from then on and logs on `Dispose`. Crash seen: the mic monitor read index −512 when it started 2 ms after the mics (song replay) — it now waits for its 512-sample lead.
- Never re-colour a FormattedText (or change any of its properties) while drawing: Avalonia lays it out again, and a font weight the font lacks creates a new, uncached synthetic font face each time that HarfBuzz keeps alive — a leak of ~200 KB per frame. Build one text per colour and reuse it.
- PortAudio on macOS sets the device's IO buffer size from an output stream's suggested latency, and that size is per process: a low-latency PortAudio output on the same device as mpv makes mpv crackle. Output streams use a fixed 512-frame block.
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

## Sprint 6l — Error handling and connectivity  *(done)*

Errors appeared in different places (red text in preview / Now Playing, panel status lines, only the log); bugs had
no handler at all. The prototype had one error dialog and mic toasts.

- [x] Three levels in `NotificationService`: toasts (info / success / warning, ✕, 5 s / 8 s), error dialog (reasons + "Show details"), bug dialog (Copy details, Open log folder). Dialogs queue.
- [x] Global handlers (`Dispatcher.UIThread.UnhandledException`, `TaskScheduler.UnobservedTaskException`, `AppDomain`): log with stack trace, bug dialog, keep running. Debug: Shift+F9 test exception.
- [x] YouTube failures in plain language: `MpvPlayer` keeps yt-dlp's error line, `PlaybackError.Explain` maps it (age restriction, private, removed, country, no connection, blocked, yt-dlp missing), with tests.
- [x] Inline errors replaced: preview, Now Playing, playback stopped mid-song (now a dialog), folder scan, songbook start → error dialog; USDB offline at start → warning toast.
- [x] `ConnectivityService`: offline / back-online toasts while running (probe at start, on network change, every 15 s); USDB greys out offline and reconnects when back. USDB toast only when the internet works but USDB does not.
- [x] Toast shadow was cut square by its list's wrappers (same cause as the Now Playing card): clipping off. Debug: Shift+F8 test toasts.
- [x] Verified live

## Sprint 6m — Mic plug / unplug, mic test, UI consistency  *(done)*

Decided with the user (after trying a "keep and mark" version): a player only has a mic while it is plugged in.
Details: `03-game-engine.md` "Mic plug / unplug".

- [x] `Core.Players.MicPresence` (missing players, device diff, description) with tests.
- [x] `AudioInputService`: presence set, 3 s device poll while idle, `PortAudioBackend` opens streams under the refresh lock.
- [x] Unplugged (idle, during a test, missing at start) → mic unassigned + toast; player leaves its beamer (`DisplayService` rule, also for a manual "no microphone"); plugged in → toast, lists update by themselves.
- [x] Mid-song loss → song stops (rewound), beamers close, warning toast.
- [x] Mic test per player card (Test / Stop) instead of one global test; unplugging stops that card's test; closing the Audio Input panel stops all tests.
- [x] Card: note / "gated" shown only while that mic is tested (gate state reset on stop); tooltips for GAIN, GATE, MIX, mic delay, Calibrate.
- [x] Settings → "Show tooltips" toggle for every tooltip in the app (inherited `ToolTip.ServiceEnabled` on all windows).
- [x] Game volume: Now Playing slider and Audio Output → Game are one setting (via `OutputsService`, persisted), both follow each other live; Audio Output also follows the preview player's volume live.
- [x] Disabled standard: `OpacityDisabled` 0.4 + arrow cursor for every disabled control; tooltips also on disabled controls (Displays: player without mic explains why). Beamer idle hint fixed (Esc no longer closes).
- [x] Interaction state tokens (`BrushState*`, Fluent list colours on the OS accent): library rows, queue rows, sidebar + icon buttons, toggle buttons, active Test button.
- [x] Verified live
---

## Sprint 7 — Gameplay: the Game Player  *(done)*

- [x] "Now Playing" card renamed **Game Player** (UI text only; `NowPlaying*` stays in code); right column gets a **PREVIEW PLAYER** section title like QUEUE; empty preview shows "No song loaded".
- [x] Game Player transport reduced to four buttons (prototype): Home (start view, song stays loaded — replaces Clear and Leave score screen), Get ready, Play/Pause (one button; disabled during the countdown), Stop (score screen; Play replays from the start). Rules in `Core.Playback.PlaybackRules` with tests.
- [x] Game Player picture box always shown (fixed size, note when empty), loader while loading, then the video's first frame or the cover / background — same as the preview player (`SongImages` shared). Same height token (`PlayerPictureHeight`). YouTube 16:9 thumbnails cached by `ThumbnailService` (`cache/thumbs`). `VideoSurface` clears when its `FrameBus` gets a new song and stays transparent until its first frame, so the picture lies under the video.
- [x] One picture rule for all displays (6 media cases): `SongPicture` picture (cover → YouTube thumbnail → background) and backdrop (background → cover); `StageView` decides beamer background and Game Player box per state (get ready / score blurred, countdown picture, song = video or sharp dimmed backdrop). Beamer get-ready no longer black for YouTube / USDB songs. Tested.
- [x] Beamer text (countdown, PAUSED, get ready, score) gets a soft drop shadow — readable on bright frames.
- [x] A beamer closed while a song runs stops the song (score screen) with a warning toast.
- [x] All load-into-game entry points (library ⋮ and right-click menus, preview Load, queue Load next and row Load) disable while a song runs or loads (`NowPlayingViewModel.CanLoadSong`), with a tooltip saying why; menu items join the disabled look.
- [x] Transport colours: play solid accent blue, pause light blue (`BrushStateSelected`), stop red (`BrushError`); round 48 px (`Button.transport`). No beamer open → one "Select displays" button (opens the Displays panel) instead of the four buttons and the status hint.
- [x] Verified live

---

## Sprint 8 — Beamers  *(done)*

- [x] A beamer without assigned players still shows the lyrics bar (lead-in, syllable sweep in the lead-in blue) and the progress line during a song — karaoke without scoring. Lyrics follow track 1.
- [x] Elapsed / remaining time and progress bar under the lyrics (prototype's SongProgress) from `Core.Timing.SongTimeline` — #START, #END, #GAP, #VIDEOGAP and media length, tested; replaces the thin top progress line; auto-stop uses the same end.
- [x] Smooth fill: USDX-style synced clock (never backwards, tested), per-player sung beat (`GameSession.SungBeatAt`, tested), fill edge glides to the sung beat, redraw per display refresh.
- [x] Note bars in the prototype's look: sizes per player count, Note bar style setting (White / Black), golden shimmer, rap dashed + hit glow + badge, freestyle dotted + light-up, correct pulse, PERFECT fade.
- [x] Fix: crash on song replay — mic monitor read a negative ring index in the audio callback (regression tests); audio callbacks can no longer take the app down.
- [x] Audio Input: an input another player uses (same side, or Mono on either — `MicBinding.ConflictsWith`, tested) is disabled in the other cards' drop-downs ("used by …"); labels use the device id so identical names stay apart ("(2)").
- [x] Two identical Let's Sing mics: only one appeared on the USB bus — a hardware fault of that mic (a replacement shows up as a separate device, "(2)"). Not an app issue.
- [x] Crackling during the game (all sources): beamer drawing made nearly allocation-free (cached texts, brushes, pens, segment lists); SustainedLowLatency GC during a song; GC counts logged per song. Monitor stream shares CoreAudio's IO thread with mpv, so a GC pause stalled both.
- [x] Real cause of the crackling: PortAudio (macOS) sets the device IO buffer from an output stream's latency, per process — the low-latency monitor stream shrank the MacBook speakers' buffer to its minimum, and mpv's song output in our process crackled (preview on the headphones did not). Output streams now open with a fixed 512-frame block (`PortAudioBackend.OutputBlockFrames`).
- [x] Crackling verified gone. (Recalibrate mic delays once: the calibration beep now uses 512-frame blocks.)
- [x] Pitch analysis delay: measured ≈ 60 ms (`PitchPathDelayTests`); now part of the calibration (Sprint 9).
- [x] Verified live (played on the beamers through Sprints 9 and 10)

---

## Sprint 9 — Latency and sync  *(done)*

- [x] `Core.Timing.LatencyModel` (tested): display = clock − output latency; mic delay = calibrated total − latency of the calibration output (default 140 ms); flash timing for Test sync.
- [x] Pitch analysis delay measured: ≈ 60 ms from tone onset to a stable note (`PitchPathDelayTests`).
- [x] Calibrate: A4 tone on the **game** output, detected through the game's pitch analysis with the player's gain; stores total + output; card shows the resulting mic delay ("default" until calibrated).
- [x] Output latency per game output (device + pair) in Audio Output → Game → LATENCY (0–800 ms); beamers and scoring use it.
- [x] Test sync: click every second on the game output (`SyncClicks`, tested), disc flash on the open beamers when each click should be heard; needs an open beamer, stops with the panel, a song, or the last beamer.
- [x] Settings → Lyrics offset removed; an existing value moves into the game output's latency once.
- [x] Verified live: Calibrate gives a steady ~110 ms per mic (tone → recognised note; old beep method 36 ms); Test sync on the MacBook speakers lands on 0 ms, as expected for wired speakers.

---

## Sprint 10 — Scoring effects  *(done)*

- [x] Rap notes: no sung fill (any voice counts, so the fill painted the whole bar) — the hit shows on the border only (solid, orange glow, white R). Freestyle: no fill either (not scored).
- [x] Rap scoring checked against USDX (`UMusic.pas` ScoreFactor: freestyle 0, normal 1, golden 2, rap 1, golden rap 2) — ours matches (100 / 200 per beat).
- [x] Fix: Play again after Stop kept the previous run's fill — the beamer reused its scene; a new game session now gets a new scene (resume after Pause keeps it).
- [x] Fix: freestyle notes written "F:" (colon after the type letter, e.g. "Ben Harper - Golden Rapnotes") were dropped by the parser (test first); also "R:" / "G:".
- [x] Freestyle look: thin white dots (1 px); lights up (solid border in the player colour + glow) once the player is heard on it, not just when reached.
- [x] Phrase rating after USDX (`Core.Game.PhraseRating`, tested): share of the phrase's points × 8, rounded; only GREAT! (6), AWESOME! (7) and PERFECT! (8, from ~94 %) are shown, centred in the lane with USDX's popup timing (pop 0.35 s, rise 0.55 s, fade 0.2 s). Rated when the mic has delivered the phrase's last beat. Replaces the old every-beat PERFECT!.
- [x] Perfect note (every beat right, `PlayerScorer.IsNotePerfect`, tested): three twinkling stars at the bar's top-right (left of the R badge) once it is sung through.
- [x] Golden notes: gold sparkles on the sung part, a white-blue twinkle at the fill edge while it is hit.
- [x] 100 % phrase: a star burst in the player's colour over the lane.
- [x] The 50 % border flare stays (feedback while the note is still sung).
- [x] Rating words 30 % smaller (11 % of the lane height, min 20 px).
- [x] Score screen: once the count-up has finished, stars in the winner's colour twinkle over the screen and drift upwards for 5 s, fading out over the last second (`Controls.StarShower`); none when nobody scored (no trophy then either).
- [x] Verified live: rap and freestyle looks, empty bars on replay, rating words, note stars, golden sparkles, star burst, score-screen stars.

---

## Sprint 11 — Fixes  *(done)*

- [x] Library filters: Language and Genre list each value once, never "French, English" (`Core.Songs.ValueList`, tested, as usdb_syncer: languages split on `, ; / |`, genres on commas; "(romanized)" stays its own entry). Choosing a value finds every song that has it among its entries ("French" → "French", "English, French", "Chinese (romanized), French"). USDB catalog: 148 → 85 languages, 946 → 700 genres. The table column keeps the original text.
- [x] Filter drop-downs have a fixed width — that of their widest entry (`Controls.FitWidestItem`); they no longer jump while scrolling through genres.
- [x] Layout panel (was a placeholder): toggles for every library column — Artist, Year, Language, Genre, Edition, Creator, BPM, Rating, Source, Media (Title always shown); persisted in the app settings; defaults as before plus Genre. New columns sortable (`SongSort` Genre/Edition/Creator/Bpm, tested).
- [x] Library table: fixed column widths (Language 170 px fits "(romanized)"), Title and Artist share the rest; when the visible columns need more than the window, the table scrolls horizontally instead of squeezing them.
- [x] Song menus (right-click and ⋮): icons as in the prototype (Preview ▶, Add to queue, Load into game), divider, **Details**.
- [x] Details popup (centre of the window; ✕, Esc or a click outside closes): picture, title, artist, source; tags (year … END, USDB id/views); notes summary (`Core.Songs.SongSummary`, tested — singing ends at, solo/duet with names, phrases/notes, golden share, rap/freestyle, pitch range e.g. G3 – G4); files with ✓/✗ and the folder (Show in Finder); problems (`Core.Songs.SongCheck`, tested — validator errors, missing files, #VIDEOGAP without video, no #LANGUAGE, overlapping or zero-length notes, #START/#END outside the notes); the .txt with Copy; Open on USDB / YouTube; Preview, Add to queue, Load into game. USDB songs fetch their text like Preview (cached).
- [x] Menu icons 24 px (Fluent's 16 px icon box widened for all menus).
- [x] Verified live: filters, Layout panel, table scrolling, menus, Details popup.
- [x] Settings → Logs & data: Log folder (today's log selected in Finder), Today's log (opens in Console), crash reports (only when macOS wrote one for the app; newest selected), App data folder. `Services.FileReveal` (Finder / Explorer / xdg-open), also used by Details → Show in Finder.
- [x] Log size: at most 10 MB per file (then a new file), 7 files kept — ≤ 70 MB worst case. UI inspector dumps (debug builds) older than 7 days are deleted at startup.
- [x] Beamer mic meter: five bars in the player's colour after "Name  score", as tall as its capitals — the singer sees their mic works. Driven by loudness above the player's noise gate (`Core.Players.MicActivity`, tested; 0 at the gate, full at +30 dB), each bar wobbling at its own pace, fast rise and smooth fall; dots when silent. No FFT: two volatile reads and five rectangles per player per frame.
- [x] Verified live: Logs & data buttons, mic meter.
- [x] Game Player box shows the countdown (3 – 2 – 1) and PAUSED like the beamers: same text style (`TextBlock.beamer`, now app-wide), scaled to the box; the count starts on the same state change as the beamers'.
- [x] Game Player box: elapsed / remaining pills and the blue progress line at its bottom, as on the beamer (`SongTimeline`; tokens `BrushStageProgress/Track/Pill`); replaces the small position text.
- [x] YouTube load retry: a refused stream (HTTP 403 / 5xx, seen 3× in one evening — first load failed, the manual retry worked) is loaded again automatically with a fresh yt-dlp address, up to 2 retries, before the error shows (`Media.StreamRetry`, tested). Game and preview channels, video and audio roles.
- [x] Songs that cannot be loaded are marked: red ⚠ before the title with reason and date (tooltip), and at the top of Details → Problems. Only the song's own problems (no YouTube link, video removed/private/age-restricted/blocked, broken or missing local files — `SongLoadException.SongProblem`, `PlaybackError.SongProblem`, tested); never connection trouble. Rows stay usable. The mark goes away when the song loads (Preview or Game Player) or changed since (`Core.Songs.LoadFailure` fingerprint: USDB change time / local .txt modification time, tested). Stored in library.db (`load_failures`, tested).
- [x] Fix: cached USDB song texts were never refreshed — a text older than the song's last change on USDB is fetched again (cached text used if that fails).
- [x] Score screen: thicker (36 px), longer bars and the percentage in each player's colour, counting up with the score. Share of the points possible: the whole song when sung through; after an early stop, of what was possible until then (`PlayerScorer.MaxScoreUntil`, `GameSession.PossibleScoreAt` — up to each player's sung beat; tested) with a note "Stopped at m:ss — …".
- [x] Fix: Play again after a song ran to the very end of its file did nothing (elapsed stuck at the end, 0 s to go) — mpv closes a finished file (keep-open=no), so the rewind seek failed. Play / Get ready / Home now load the song again in that case (loader in the Game Player); after a stop mid-song they still just rewind.
- [x] Fix: the library's horizontal scrolling was unusable — the scrollbar auto-hid, and the song list's own ScrollViewer swallowed sideways swipes. The horizontal bar now stays visible when the table is wider than the window; sideways swipes and Shift + wheel scroll it (tunnel handler in `LibraryView`), up / down stays with the list.
- [x] Displays popover sizes to its content (at least the usual 420 px): all four player buttons on one line, wider for longer names; wraps only when the window runs out of room. Hints keep the usual text width (`PopoverTextWidth`).
- [x] USB drives (prototype: 5 s poll, songs removed, rescan on return, no toasts). Here: same 5 s poll; toasts "Drive removed: …" / "Drive connected: … — N songs available again" and at start "Not connected: …"; a returning source is rescanned (only that one). Songs stay greyed instead of removed; loading one says "<source> is not connected — plug in the drive" (checked live, not only every 5 s) and never marks it ⚠; a drive pulled mid-song stops with "The drive with this song was removed". `Core.Songs.SourceAvailability` (tested).
- [x] Song Sources: on/off switch per folder (prototype) — off leaves its songs out of the library and the source filter; rows show the song count, or greyed "Not connected" with an icon. Source filter: unplugged folders greyed "(not connected)" (still selectable), switched-off ones left out; the chosen filter survives a drive coming or going.
- [x] Greyed songs (drive unplugged, USDB offline): Preview, Add to queue and Load into game disabled in both menus, the Details popup and on double-click / Enter, with the reason as tooltip; Details stays.
- [x] Fix: memory leak — macOS killed the app at 23 GB after ~17 min. The phrase rating popup, the bar syllables and the lyrics sweep re-coloured their FormattedText every frame (`SetForegroundBrush`), so Avalonia laid them out again; the rating's FontWeight.Black (not in the font) built a new synthetic font face each time, which HarfBuzz kept alive (~55/s). Found with a memory log line, `dotnet-gcdump` (4,407 orphan GlyphTypefaces rooted by HarfBuzz handles) and `dotnet-trace` (stacks into `ScoreEffects.DrawRating`). Now one cached text per colour, Inter Bold; .NET heap stays flat (116 MB through songs). `Diagnostics.MemoryLog` writes process / heap / allocation rate every 30 s.
- [x] Fix: switching a source on or off left the Language / Genre filters showing an empty selection — rebuilding their lists made the ComboBoxes write null (and refresh mid-rebuild). Guarded like the Source filter; the choice is restored, or "no filter" if that value left with the source.
- [x] Filter counts (faceted search): every entry of Rating, Language, Genre and Source shows how many songs it would show under the search and the other filters — "French (1,812)", the first entry the total; recomputed on every search keystroke and filter change (`Core.Songs.SongFacets`, one pass, tested — counts match what the filter shows); entries with 0 greyed, still selectable. The closed box shows only the name and is as narrow as the longest name; the open list is wider by the room for the numbers (`FitWidestItem.Suffix`).
- [x] Search placeholder shortened to "Search title or artist". Settings: Theme drop-down (Light / Dark) instead of the light-theme switch; a first start is light (a saved choice is kept).
- [x] Settings → Difficulty locked while a song runs (countdown / playing / paused), with the reason as tooltip: the scorers take the tolerance at song start, so a change mid-song only applied to the next song. Theme and Note bar style stay live.
- [x] Song Sources → USDB on/off switch: off leaves USDB songs out of the library, the source filter and the songbook; stays logged in, sync still works; persisted.
- [x] Verified live

---

## Sprint 12 — Songbook  *(done)*

- [x] Phone test over Wi-Fi: works (browse, search, request).
- [x] Pears (P2P, Hyperswarm) considered instead of Wi-Fi: needs an installed app on every phone (browsers cannot hole-punch) and a Bare sidecar — not for a party. Browser + Wi-Fi stays; a tunnel link is the option for guests on mobile data.
- [x] QR code on the beamer while the songbook runs: card bottom-right on the start view, get ready and score (never during countdown / song) — "Scan to request a song", the address, the PIN if set. `Controls.QrCode` (QRCoder modules drawn as squares, ECC M); the address is the Wi-Fi one phones can reach (`Core.Songbook.SongbookAddress`, tested: 192.168 on Wi-Fi first, never link-local / Tailscale).
- [x] Verified live: QR scanned from the phone
- [x] Phones search and filter locally: the library comes once per guest (`/api/library`, compact arrays, gzip ≈ 620 KB for 31,500 songs, cached on the phone per library version; `Core.Songbook.SongbookCatalog`, tested), so the Mac answers no request per keystroke. Only playable songs (no unplugged drives, no USDB offline); a new catalog when the library changes (version in `/api/state`). On the phone: instant search, one filter at a time (Language, Genre, Decade, Popularity — with counts), first 100 + "Show more", browsing without a search.
- [x] Song rows use the full width (title, then artist · year · language) with ⋯ on the right; tapping the row or ⋯ opens a centred dialog: details (year, language, genre, popularity, source), "Watch on YouTube" and "Request song". The YouTube link opens YouTube (the app on phones) — embedded players were refused (uploaders disable embedding; YouTube rejects players on a plain http://192.168… page). USDB songs get their YouTube id from the Mac when the dialog opens (`/api/youtube`, cached).
- [x] Requests: name field in the song dialog (remembered on the phone, editable); "My requests" at the top of the page with status (Waiting for the DJ, In the queue #n / Up next, On stage now, Sung, Not this time) and Cancel while waiting; markers in the list ("Requested", "In the queue #3", "On stage"); the dialog's button turns into "Cancel my request" or shows why a song can't be requested. Rules in `Core.Songbook.GuestRequests` (tested): no song requested twice or while queued / on stage, at most 3 open requests per guest, cancel only own + waiting, status read from the queue. Each phone has a random id (its requests survive a renamed guest).
- [x] The DJ's queue shows "🎤 Name" under songs requested on the songbook — who to call to the mic.
- [x] State, requests and cancels run on the UI thread (the queue and the request book live there; the old status call read the queue from Kestrel threads).
- [x] Game Player shows "🎤 Name" under the artist for a requested song (as in the queue).
- [x] Requests stand out: light green background (`BrushRequest`, per theme) for the REQUESTS list and the request toasts (new toast kind with a mic icon); the phone's "My requests" in the same green.
- [x] Phone: "↑" back-to-top button after scrolling down.
- [x] Fix: a request stayed "On stage now" after the DJ loaded the next song from the library (the queue's active entry does not move then). On stage = loaded in the Game Player; `GuestRequests` takes the loaded song (test first).
- [x] Loading a song from the queue removes it from the queue (it is in the Game Player; the queue is what is still to come).
- [x] Songbook panel → "Allow requests" (default on, persisted): off = guests browse only; the phone hides the name field and says "The DJ isn't taking requests right now"; the server refuses requests too.
- [x] Phone: no "now playing" card; back-to-top in a quarter second (eased) instead of the browser's slow smooth scroll.
- [x] Phone toasts in the middle of the screen, 3.2 s; "Requested" (and "Request cancelled") in the requests' green, other messages neutral.
- [x] Phone: when "My requests" is scrolled away, a green tab under the search shows the number of requests; tapping it opens them right there (Cancel works), the next scroll closes them.
- [x] Phone: the requests panel opened from the tab has its title ("My requests (n)") too.
- [x] Public link (second way in, next to Wi-Fi): Songbook panel → "Public link" starts a Cloudflare Quick Tunnel (`cloudflared` sidecar, fetched by scripts/fetch-natives; free, no account) and shows its `https://….trycloudflare.com` address; the beamer's QR code then uses it (works on Wi-Fi and mobile data); the party PIN is switched on; up to 3 reconnects 5 s apart if cloudflared dies, then a warning, the QR falls back to Wi-Fi. `Songbook.CloudflareTunnel` (address parsing tested — not cloudflared's own api.trycloudflare.com from its error lines). Chosen over bore (the prototype's: one-person server, odd port, plain http, no reconnect).
- [x] One way in at a time: Songbook panel → "Guests connect via" Wi-Fi (default, no internet needed) or Public link. In public-link mode the server listens on this Mac only (127.0.0.1) so the Wi-Fi address is really closed, and the QR code shows only the public address (hidden while connecting / after the link is lost). Switching while running stops the one and starts the other automatically. (Phones keep "My requests" and the name per address, so they start fresh after a switch; the requests stay on the Mac.)
- [x] Beamer songbook card: just the QR code and "Songbook" (plus the PIN when set — guests need it to get in), on the start view and get ready only — not on the score screen. The briefly added typed Wi-Fi name, the address and the explanations were dropped again.
- [x] Verified live: phone over Wi-Fi and the public link, requests, queue, beamer card

---

## Sprint 13 — yt-dlp update  *(done)*

- [x] Settings → YouTube: running yt-dlp version, newest release (GitHub API), "Check for update" / "Update to …". The update downloads the official folder build (`yt-dlp_macos.zip` / `yt-dlp_win.zip`) into the app data folder (`sidecars/yt-dlp/`, searched first by `SidecarLocator` — a signed bundle may not change itself), runs it once, swaps it in; the next YouTube song uses it (mpv's yt-dlp path is changeable), no restart. `Infrastructure.YtDlpUpdater`, `App.Services.YtDlpService`; versions compared by `Core.Sidecars.YtDlpVersion` (tested).
- [x] Quiet check at start (when online): a toast if a newer yt-dlp exists — never an automatic update, nothing changes mid-party.
- [x] Hint on YouTube errors an update usually fixes (refused stream, "not a bot", format / page not readable, unexplained YouTube failures): the error dialog adds "A newer yt-dlp is available (…) — update it in Settings → YouTube", or "Your yt-dlp is N days old — check for an update…" / "If this keeps happening…". Not for removed / private / age-restricted / blocked videos or no internet (`PlaybackError.YtDlpMayHelp`, tested).
- [x] Verified live: version, "Up to date" and the check. The update itself waits for a release newer than 2026.08.19.

---

## Sprint 14 — Fixes  *(done)*

- [x] Fix: with a song loaded, switching the game output (e.g. to headphones) only took effect with the next song. The new device did reach the loaded player (`MediaChannel` test), but mpv keeps an already open audio output on the old device; `MpvPlayer.AudioDevice` now rebuilds it (`ao-reload`) when a song is loaded. Preview channel likewise.
- [x] Verified live: output switch reaches the loaded song
- [x] Beamer grid lines as a stave (like USDX): a line through the middle of every second semitone row instead of on row edges, so notes sit on a line or between two; bars unchanged. Settings → Grid lines (on/off, live, default on)
- [x] Verified live: grid lines
- [x] Rap and freestyle notes readable on bright video, in hues no player has: rap magenta, freestyle teal (was white dots, hard to see); both dashed with a faint tint and a dark underlay under the dashes; R/F badges in the same colours
- [x] Verified live: rap/freestyle colours
- [x] Buttons: one look per job — normal (light blue tint, outline, soft shadow), start/open filled green, stop/close filled red; start/stop pairs are one button (display Open/Close merged); Game Player and Preview Player transport in the same colours; text next to an icon centred (was a few px high)
- [x] Follow-ups: display row no longer shifts on Open/Close (tv icon hidden, not collapsed); disabled icon buttons (queue: load next, clear) have no grey background, just dimmed; tooltips on green/red buttons had white text (the button text style reached the tooltip) — tooltips keep Fluent's text colour
- [x] Verified live: buttons
- [x] Toggle switches: "on" in the sidebar's selected blue (accent 40 % light / 60 % dark, hover and pressed stronger), knob in the text colour — was Fluent's solid accent
- [x] Verified live: toggle switches

---

## Sprint 15 — Volume slider with built-in meter  *(done)*

One control, `MeterFader`, instead of a slider with a separate thin meter underneath (as in the Tauri HorizontalFader).
Drawn directly (meter rate, allocation-free), not a templated Slider.

- [x] Track = segmented meter (~30 segments green → yellow → red, unlit segments as dark tints); knob 12×28 on top
      (selected blue, or the player colour on mic rows), grows while dragged, ring on hover
- [x] Behaviour as `Fader`: only the knob moves (track clicks do nothing), double-click resets, grab / grabbing cursor; arrow keys
- [x] Meter scale per element, settable from XAML or a binding: floor and top in dB, where yellow and red start
      (e.g. song −40…0 dB, mic −70…−20 dB); the dB mapping is a tested pure function
- [x] Peak hold: thin line at the last peak, falling back slowly
- [x] Level shown after the fader (like a mixing desk)
- [x] `MeterFader` control + `Core.Playback.MeterScale` (dB range per fader, tested) and `VolumeCurve` (mpv's volume is cubic: the meter after the fader = level × volume³, tested)
- [x] Trial in the Preview Player first → verified live
- [x] Audio Input cards: GAIN + GATE in one `MeterFader` (`ShowGate`: shaded range below the gate, handle under the track, double-click −50 dB) and MIX (meter = gated level × mix); the separate meter and Gate slider are gone
- [x] Mic gain knob in dB (−40…+20, half-dB steps, double-click 0 dB; `Core.Players.InputGainScale`, tested), stored linear as before (0.7× → −3.1 dB). A linear 0–10× knob left SingStar / Let's Sing mics (~0.1×) in its first 3 %. Mic meters: red from −6 dB (the meter is RMS, voice peaks are 10–15 dB higher — red = distortion in the speakers); target: loudest singing just reaches the yellow
- [x] Verified live: Audio Input (tooltips just name the knobs)
- [x] Game Player: song fader (−40…0 dB, level × volume³) and mic mix rows (−70…0 dB, gated level × mix, empty when muted — the fader dims) as `MeterFader`; polled at 20 Hz; tooltips name the knob
- [x] Mix shown in % (Audio Input MIX, Game Player mic rows — next to the mute button), like the song and preview faders; gain stays in dB
- [x] Audio Input: MONITOR output always shown (defaults to the game output); the ear toggle is gone — the monitor is on while at least one mic is tested
- [x] Fix: tested voice sometimes metallic / robotic — the monitor mixer's fixed 512-sample lead ran dry with USB mic blocks and clock drift; adaptive lead + ±0.3 % speed nudge (tests first: live-timed callbacks, 512/1024 blocks, ±0.02 % drift)
- [x] Verified live: Game Player, monitor, mix %
- [x] All meters: yellow from 80 %, red from 90 % of their range (song −40…0: −8 / −4; mic −70…0: −14 / −7), so song and mic meters show the same segments (were 5 vs 2 yellow)
- [x] Mix 0–100 % (was 0–200 %): mix only turns a mic down, the input gain boosts; older settings above 100 % read as 100 %
- [x] Game Player 50 px wider (`GamePlayerWidth` 390, picture box scaled to keep its shape); the mute button moved from the right of each mic row to the left — the mic in the player's colour, before the name — so the faders are longer; a gap under the transport buttons
- [x] Verified live: Game Player layout
- [x] Song speaker icon centred over the mic buttons (it sat ~4 px left); name column 104 px so "Player 2" fits (fader 20 px shorter)
- [x] Verified live: alignment, names
- [x] Fix: the Game Player's mic mute also silenced the Audio Input mic test — mute is for the song only (`PlayerConfig.MutedInMonitor`, test first)
- [x] Verified live: mute vs mic test
- [x] Game Player: the song fader is invisible and inert while no display is open (keeps its space: the card keeps its height)
- [x] Verified live: song fader without display
- [x] Audio Output: VOLUME fader and meter removed from both cards — duplicates of the Game Player's song fader and the preview player's; the panel keeps device, latency and Test sync
- [x] Verified live: Audio Output
- [x] Audio Output (game): not needed — its fader is gone (see above)
- [x] Mic inputs (Tauri PlayerCard), two per input: **1. Gain + Gate** in one — knob = input gain, meter = mic after gain,
      dimmed zone below the gate with a drag handle under the track (replaces the separate Gate slider and meter);
      **2. Output mix** — knob = monitor mix, meter = what the speakers get (level × mix, empty when muted)
- [x] Meter faders in the toggles' selected blue (plain sliders — seek bars, latency — moved to Later)
- [x] Verified live

---

## Sprint 16 — Adjustments & fixes  *(in progress)*

- [x] Plain sliders (latency) in the toggles' selected blue, like the meter faders
- [x] Preview Player picture box like the Game Player's: time pills (elapsed / remaining) and the progress line at the
      bottom of the picture — but seekable: the line thickens on hover (with a knob), click or drag jumps
- [x] Loading: a spinner in the middle of the picture instead of the blue bar at its bottom (Preview and Game Player alike)
- [x] Preview play / pause centred under the picture; the old slider row goes
- [x] Preview: one row under the picture — play / pause, Queue, Load (short labels; Queue has a tooltip); the separate button row at the bottom goes
- [x] Fix: startup crash — the spinner's style animation on RenderTransform has no animator in Avalonia; now a `Spinner` control that turns itself while visible
- [x] Popovers: 16 px between the content and the scrollbar (Fluent's scrollbar floats over the content); `PopoverWidth` 420 → 432 so the content keeps its width
- [x] Song details popup: the same 16 px scrollbar room (width 860 → 876)
- [x] Fix: picking a value in a drop-down sometimes closed the panel — a press in any popup (drop-down list, menu) is never "outside"; a panel closed by an outside press is logged with the element pressed
- [x] Fix: with a drop-down open, a press anywhere closed the whole panel — that press lands on Avalonia's LightDismissOverlayLayer (found via the outside-press log line) and now only closes the list
- [x] One name for the singers' screens: **Display 1 / 2** (window title, idle screen, panel, toasts, tooltips — "beamer" only in code); the panel is **Game Displays**; Audio Output's game subtitle "Main speakers"
- [x] Game Displays: status pill per display (Closed / Open / Fullscreen) and the screen it is on (name · resolution, "same screen as Ultrastar DJ" until dragged away)
- [x] Game Displays: player buttons have a 3 px border (as the Tauri chips) — the player's colour while the player sings on that display, invisible otherwise (always there, so nothing moves on a click)
- [x] Library bar: the search ✕ floats over the box and shows whenever there is text (Fluent's only while focused — two clicks); the clear icon (no text) is always in place, dimmed when nothing is set, and also empties the search
- [x] Fix: library table left an empty strip (~95 px) after the ⋮ column in wide windows — Avalonia's star columns came out short (measured with a width probe and screenshots); Title / Artist widths are now computed in pixels from the table width
- [x] DJ window opens where it was when the app closed: same screen, position, size and maximised / fullscreen; centred when that screen is gone (`WindowPlacement.Fit`, tested); first start 1420 × 860 (was 1400)
- [x] Fix: leaving fullscreen on a display went back to the small first window, not to maximised / the size it had — it now returns to what it was before fullscreen
- [x] Displays open where they were last closed: screen (by name, then position — survives a changed monitor arrangement), position, size, maximised / fullscreen; screen not connected → next to the DJ window (`WindowPlacement.Fit`, tested)
- [x] Fix: after quitting the app, displays reopened on the main screen — a window closing with the app reports position (0, 0); placement is now taken from what was tracked while the window lived (verified with a probe run on a second monitor: normal and fullscreen, quit, restart, open)
- [ ] Verified live

---

## Sprint 17 — Song marks & backup  *(done)*

- [x] Marks per song: **Favourite** and **Broken** (with an optional note), stored in `settings/marks.json`; local songs
      also remember artist + title, so a mark finds its song again after a folder moved (the id contains the path)
- [x] Set them in the song details (next to the source chip) and in the song menu (right-click / ⋮)
- [x] Library: ♥ and ⚠ in the row (tooltip: the note); rating filter gets "Favourites" (with its count);
      Layout → "Show broken songs" (default off)
- [x] Songbook: broken songs and songs that could not be loaded are not offered to guests
- [x] Backup: Settings → Backup → "Back up…" saves one zip (settings, players, outputs, sources, songbook, marks) anywhere;
      "Restore…" picks a zip — staged, applied at the next start before anything reads its settings ("Quit now" button).
      `usdb.json` is left out: it holds the USDB password in plain text (`Infrastructure.Settings.SettingsBackup`, tested:
      only plain *.json names, never outside the settings folder)
- [x] Verified live: marks, filters, songbook (backup not yet)
- [x] Game Player: ♥ and broken icon buttons right of title / artist for the loaded song
- [x] Rating filter "⚠ Broken" (with its count) while Layout → Show broken songs is on
- [x] Fix: text in toggle buttons with an icon (Details: Favourite / Broken) sat high — the centring rule now covers every button kind
- [x] Verified live: Game Player marks, Broken filter, backup (restore after deleting marks.json)

---

## Sprint 18 — Duets  *(in progress)*

UltraStar duets have two voices (`P1` / `P2` in the notes; `#P1:` / `#P2:` or `#DUETSINGERP1/2` name the singers;
USDB marks them "[DUET]" in the title). The format has no third voice.

- [x] Lanes: a phrase appears 3 s before it is sung; a voice with nothing to sing for longer has an empty lane (in
      "Shallow" the second lane showed Lady Gaga's first phrase — same melody — while Bradley Cooper sang).
      `NoteLaneGeometry.ActiveLine` lead-in, tested. Also in solo songs: empty lane during long instrumentals.
- [x] Who sings a duet (`Core.Game.DuetVoices.Singers`, tested): exactly two — the DJ's pick, else the first two set up
      (players 1 and 3 → 1 voice 1, 3 voice 2); the others sit out (no lane, no score, no mic in the mix)
- [x] A single singer gets both voices merged into one track (where both sing at once, voice 1) (`DuetVoices.Merge`, tested)
- [x] Lyrics line per display (its singers' voices; no singer → every voice, neutral): one voice on (singing or within the
      3 s lead-in) → its line and preview in its singer's colour; both on → two lines, voice 1 above voice 2, each in its
      colour, a bit smaller, no preview (the strip keeps its height); none → the voice that comes next. Separate displays
      already showed their own voice. Allocation-free per frame
- [x] Popup: a selected voice can be clicked off again (`Controls.ToggleRadioButton`) — one voice left → that player sings both
- [x] Loading a duet with 2+ players set up opens "Duet: <title> — who sings which voice?": per open display with players,
      its players (with a mic), each Voice 1 / Voice 2 / sits out (radio buttons; column heads with the singers' names
      from `#P1`/`#P2`, `#DUETSINGERP1/2` or USDB's `p1=`/`p2=` in `#VIDEO`). Each voice has one singer — giving it to
      someone takes it from whoever had it; OK needs both voices; Swap; nobody is moved between displays; Esc / backdrop
      keeps the pick. No open display with players → no popup. One player: a note "… is a duet — <name> sings both voices"
- [x] Popup look: no "sits out" column (neither voice = sits out); the two voice columns on their own tints with a divider,
      each display a block under a full-width line (checked with screenshots)
- [x] Fix: moving players between displays (same singers) or adding one did not ask again — the confirmed setup now is
      players per display + singers, not only the singers
- [x] Fix: a display opened on the score screen showed the last song's scores — it did not take part; it shows the start view
- [x] Popup: OK with only one voice picked — that player sings both voices (`DuetChoice.Solo`, tested); the popup says so
      ("Player 1 sings both voices."). Only one player set up: the same popup in the middle ("Only one player is set up —
      … sings both voices. To sing it as a duet, assign a second player in Game Displays.") instead of an easy-to-miss toast
- [x] Crackling once in a duet (two mics on two devices, two displays) — not reproduced; no memory leak (heap flat at
      ~101 MB). Diagnostics added: monitor jumps per mic and PortAudio drop-outs per device, logged when a stream stops
- [x] Fix (found with those diagnostics): the monitor mixer ran dry 6–43× per song — almost one jump per gen1 GC (6/6,
      28/33, 43/53): a pause holds the mic's callback up, the output asks first. Each jump is a click while singing. The
      lead now covers the largest output block seen and grows ~10 ms each time it runs dry (≤ 50 ms). Tests first:
      varying output blocks 1 → 0 clicks, GC-like mic pauses 4 → 1 click in 60 s
- [x] Game Player: "Duet: Reto → Bradley Cooper · Anna → Lady Gaga" under the artist, "change" reopens the pick (not while it runs)
- [x] Singers changed after the pick (a player added / removed on a display, a mic set to none or unplugged): the Game
      Player line shows ⚠, and the popup comes back when Game Displays / Audio Input closes — at the latest at Play, where
      OK then starts the song. Same singers as confirmed (e.g. only the mic device swapped) → no popup
- [x] Verified live: duet with players 1 + 2 (popup, lanes, Game Player line)
- [x] ~~Bug: players 1 + 4 — player 4 gets no note bars~~ — not a bug: sung into the wrong mic
- [ ] Verified live
- Next: duet marker in the library (filter) and on the songbook; swapping voices

---

## Later / ideas

- Flaky test: `SqliteLoadFailureStoreTests.Save_RoundTrips_UpdatesAndRemoves` failed once in a full `dotnet test` run, passes alone —
  probably another test class's `SqliteConnection.ClearAllPools()` running in parallel
- Cap mpv read-ahead (~60 s) so YouTube streams like a player instead of pulling the whole song into RAM
- OpenGL frame path (shared texture) if CPU copy shows up in profiles
- Global hotkeys, keyboard-only DJ operation
- Duet rendering (two tracks per lane pair), medley, `#PREVIEWSTART`
- Linux AppImage
- Code-signing certificates (Windows EV / Apple Developer) — optional, no architectural impact
