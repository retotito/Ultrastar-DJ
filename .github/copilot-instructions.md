# Ultrastar DJ — AI Agent Instructions

You are working on **Ultrastar DJ**, a desktop karaoke DJ app: C# / .NET 8, Avalonia 11, libmpv, PortAudio.
Code identifier: `UltrastarDJ` (namespaces, projects). Display name: "Ultrastar DJ".

## Read first (in this order)

1. `docs/01-architecture.md` — layers, projects, dependency rule, runtime picture
2. `docs/05-conventions.md` — naming, units, threading, comments, tests, definition of done
3. The doc for the area you touch: `02-media-engine.md`, `03-game-engine.md`, `04-ui.md`, `06-build-release.md`
4. `docs/00-vision.md` when you need to know *what* a feature must do; `docs/07-roadmap.md` for the current sprint

## Non-negotiable rules

- **No WebView. Ever.** Not for YouTube, not for anything. The previous app died on this.
- **Dependency direction:** `Core` ← `Media` / `Audio` / `Infrastructure` ← `App`. Never the other way, never sideways.
- **Realtime code** (PortAudio callbacks, mpv callbacks, `GameTicker`, `FrameBus`) allocates nothing, locks nothing, logs nothing, and never touches Avalonia. Marshal with `Dispatcher.UIThread.Post` or the messenger.
- **Units in names** for any non-`TimeSpan` number: `gapMs`, `videoGapSec`, `lengthBeats`, `usPitch` vs `midiNote`. UltraStar `#BPM` is quadrupled.
- **Design tokens only** in XAML: `{DynamicResource ColorX}`, `{StaticResource SpaceN}`. No hex colours, no magic sizes. New tokens go in `Styles/Tokens.axaml` (+ light variant).
- **MVVM:** ViewModels have no Avalonia types; ≥20 Hz data drives custom controls directly, not bound properties.
- **Interfaces only at layer boundaries.** No factories/abstractions for single-use code. No `Manager`/`Helper`/`Util` classes.
- **Tests** for everything in `Core` and for clock/sync/pitch/scoring logic. Bug fix ⇒ failing test first.
- **Comments** say *why* and the contract; never restate code, never leave changelog/reviewer notes.
- **Everything mpv-specific stays inside `MpvPlayer`.** Nothing else knows option names.

## Before you finish a task

```
dotnet build
dotnet test
```

Both must pass. Update the relevant `docs/*.md` in the same change if behaviour or architecture moved.
Do not create new markdown files to describe your change; put knowledge in the existing docs.

## Avalonia pitfalls (agents get these wrong)

- If an API is not in the Avalonia docs it does not exist (no WPF `Freezable`, no WPF-style `VisualBrush` tricks).
- `x:Class` must exactly match the code-behind class; resource keys are case-sensitive.
- Use `Dispatcher.UIThread.Post`, never block the UI thread waiting for mpv/PortAudio.
- `WriteableBitmap.Lock()` → copy inside → dispose → `InvalidateVisual()`.
- Virtualized lists need `VirtualizingStackPanel` + fixed-height items.
- Each `Window` has its own `DataContext`; never share one ViewModel between two beamers.

## Project map

```
src/UltrastarDJ.Core            domain, parser, timing, scoring, queue   (no dependencies)
src/UltrastarDJ.Media           MpvPlayer, MediaChannel, FrameBus, clock, sync
src/UltrastarDJ.Audio           PortAudio backend, MicPipeline, YIN, MonitorMixer, LatencyTest
src/UltrastarDJ.Infrastructure  SQLite repos, settings, USDB client, songbook server, sidecars
src/UltrastarDJ.App             Avalonia windows/views/viewmodels/controls, DI root
tests/*.Tests                   xUnit
natives/<rid>/                  downloaded per machine (git-ignored) — libmpv, yt-dlp, ffmpeg
scripts/                        fetch-natives, publish, bundle-macos
docs/                           the source of truth for architecture and behaviour
```
