# 00 — Vision & Feature Inventory

**Ultrastar DJ** is a desktop karaoke app for parties. One person (the DJ) runs the show from a
laptop; singers see lyrics, note bars and scores on one or two projectors ("beamers"). Songs come
from local UltraStar folders, the USDB community database, and YouTube.

This is the second implementation. The first (Tauri 2 + Svelte, repo `retotito/UltraStarDJ`) is a
working prototype whose architecture could not solve two problems — see [Why a rewrite](#why-a-rewrite).
Everything that was learned there is preserved in these docs; the *behaviour* described here is
the behaviour to reproduce.

---

## Roles and windows

| Window | Who looks at it | Purpose |
|---|---|---|
| **DJ window** | The DJ | Library, queue, preview player, player/mic setup, output routing, display setup, transport |
| **Beamer 1** | Singers / audience | Idle screen, "get ready" preview, countdown, game (video + note lanes + lyrics), score screen |
| **Beamer 2** (optional) | Singers on a second screen | Same as beamer 1, but only the players assigned to it |

All windows belong to **one process**. There is no IPC between windows.

---

## User flow (a typical evening)

1. **Load song sources** — add local folders, connect USDB (27k songs), library fills.
2. **Assign microphones to players** — up to 4 players, each with a mic device + channel (L/R/mono), input gain, noise gate, mic delay.
3. **Select audio outputs** — game audio → main speakers (any device / channel pair); preview → DJ headphones (any device / channel pair).
4. **Assign players to screens** — open beamer 1 (and 2), choose which players show on which screen.
5. **Load a song into the player** — search library, preview in headphones, add to queue, load.
6. **Play the game** — preview screen → play → 3-2-1 countdown → singing with live pitch feedback → score screen.

Reference screenshots of the prototype are in `docs/reference/screenshots/`.

---

## Media cases (must all work)

UltraStar `.txt` files reference media in different combinations. The engine must handle all six.

| # | Audio source | Visual on beamer | Notes |
|---|---|---|---|
| 1 | `#MP3` | `#BACKGROUND` image | static image |
| 2 | `#MP3` | `#VIDEO` local file (muted) | most common local case |
| 3 | `#MP3` | YouTube video (muted) | `#VIDEO:` URL or `#YOUTUBE:` |
| 4 | YouTube (audio track of the video) | same YouTube video | **USDB songs** — no local files at all |
| 5 | `#MP3` | `#COVER` image | fallback when no bg/video |
| 6 | audio track of `#VIDEO` local file | same local video | no `#MP3` |

Rules that apply across cases:
- `#GAP` (ms): audio offset before beat 0. `#BPM` is quadrupled (`#BPM 120` = 480 beats/min).
- `#VIDEOGAP` (seconds): where in the video the song content begins. Two modes — see `03-game-engine.md`.
- `#START` / `#END` (seconds / ms as in file): playback range.
- Unsupported local video containers (MPG, AVI, MKV, WMV, FLV) are handled by the media engine directly (libmpv decodes them) — no pre-transcode needed anymore.

---

## Feature inventory

Everything below exists in the prototype and must exist in v2 unless marked *(later)*.

### Library
- Sources: local folders (recursive scan for `.txt`), USDB (login, full + incremental catalog sync, progress, abort, disconnect, auto-login on start). *(later)*: custom/plugin sources.
- Song table with virtual scrolling (27k+ rows), search (title/artist), filters (language, genre), column sort, source badge (local / USDB), availability watcher (source folder unplugged → songs greyed).
- Lazy song validation before preview/queue/load: required tags, ≥1 note line, at least one playable audio source on disk or YouTube; patched copy with missing optional files nulled; error dialog listing problems.
- USDB songs: `.txt` fetched on demand, YouTube ID extracted, requires internet (offline badge + block).

### Preview player (DJ headphones)
- Plays the selected song's media (audio, video, YouTube) with transport + progress.
- Own volume fader with level meter. Own output device / channel pair. **Must work for YouTube** (this failed in the prototype).
- Add to queue / Load into game.

### Queue
- Ordered list, add/remove/reorder, load next into game.

### Players & microphones
- 4 fixed player slots: active, name, colour (blue/red/green/yellow), mic assignment (device + channel L/R/mono).
- Per player: input gain 0–2, noise gate threshold 0–0.5, mix gain 0–2, mute, mic delay ms (default 40, cap 250 in UI).
- Live level meters; mic test mode; hot-plug detection (disconnected / reconnected toasts, auto-clear at startup if device gone).
- Mic delay calibration dialog: beep → echo detection, 5 trials, median, apply per player.
- Mic monitoring: mic audio mixed into the game output during the song (mix fader + mute per player in the Now Playing card).

### Audio outputs
- Two channels: **game** and **preview**. Each: device selection (including channel pairs on multichannel interfaces, e.g. "MOTU — Ch 3–4"), volume fader, level meter, persisted.
- Hidden virtual/loopback devices (BlackHole, Loopback, screen recorders).
- Hot-plug: device list refresh; reset to default if selected device disappears.

### Displays
- Beamer 1 and 2: open/close on a chosen monitor (fullscreen), assign player IDs (a player is on at most one display). Display 2 offered when ≥3 players active.

### Transport (Now Playing card, floating/draggable in DJ window)
- States: idle → loaded → preview → countdown → playing ⇄ paused → score/stopped.
- Buttons: home screen (clear beamers), preview (title screen), play, pause, stop.
- Song volume fader (game channel) with meter, player badges, status text (buffering, no display, …).
- Play disabled until every open beamer is ready and media is buffered.
- Audio config popups locked while a song is active.

### Game (beamer)
- Background: video / YouTube / image / cover, priority in that order.
- Countdown 3-2-1, then media starts and mics start.
- Per assigned player: **note lane** — 16 pitch rows (12 for 3–4 players), note bars positioned by beat and pitch with octave wrapping, syllable text inside bars, styles for normal / golden / rap / freestyle, sung-fill that grows beat by beat in player colour (correct) or dimmed at the sung row (wrong), fully-correct note glow, PERFECT phrase flash, playhead line.
- Lyrics bar at bottom: current phrase with per-syllable sweep, next phrase dimmed, 3-second lead-in bar.
- Song progress bar.
- Live score per player; animated score screen with winner at the end (all players on all beamers).

### Scoring
- YIN pitch detection per mic, median ring buffer (5), octave-invariant matching, difficulty easy/medium/hard (±2 / ±1 / ±0.5 semitones), points per beat (normal 1×, golden 2×, rap = any sound, freestyle = 0), max 10 000 per song, phrase bonus.

### Songbook for guests
- Embedded HTTP server serving a mobile web page: guests browse/search the library and request songs. Optional 4-digit party PIN. Public URL via tunnel (bore) for guests not on the LAN. *(v2: keep server; tunnel optional)*.

### Settings
- Theme dark/light, difficulty, lyrics offset ms, sources, persisted players/displays/outputs.

### Sidecars
- `yt-dlp` (YouTube stream resolution — used by libmpv), `ffmpeg` (used by yt-dlp for muxing when needed; no longer needed for transcoding).

---

## Why a rewrite

The prototype embeds YouTube with the official IFrame player inside WebViews. Two limits follow directly:

1. **Preview audio device** — audio from a cross-origin iframe cannot be captured by Web Audio (`createMediaElementSource` yields silence) and WKWebView has no `setSinkId`. Preview YouTube audio could only ever go to the system default output.
2. **One stream per window** — each Tauri window is an isolated WebView. A YouTube-only song opened 3–4 independent YouTube streams (preview, hidden game player, one per beamer), synchronised by periodic `seekTo`.

Both are properties of *browser-hosted media*, not of Tauri or Rust. v2 therefore moves media into the process: **libmpv decodes once; the app owns the audio routing and hands the same video frames to every window.** See `02-media-engine.md`.

---

## Non-goals (for now)

- Linux packaging (architecture supports it; not tested or shipped).
- App Store distribution / notarisation (ad-hoc signed like UltraStar Deluxe).
- Song editing, recording, online multiplayer.
- Mobile apps (the songbook web page covers guests' phones).
