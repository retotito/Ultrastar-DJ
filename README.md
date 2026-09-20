# Ultrastar DJ

Desktop karaoke DJ app for parties. The DJ runs the show from a laptop; singers see lyrics, note bars
and scores on one or two projectors. Songs come from local UltraStar folders, USDB and YouTube.

**Stack:** C# / .NET 10 · Avalonia 12 · libmpv · PortAudio · SQLite · ASP.NET Core (guest songbook).
**Platforms:** macOS (Apple Silicon + Intel), Windows x64.

This is the second implementation. The first (Tauri + Svelte, [retotito/UltraStarDJ](https://github.com/retotito/UltraStarDJ))
is a working prototype whose browser-based media stack could not route YouTube audio to a chosen device nor
share one video stream across windows. v2 decodes media in-process and owns audio routing.

## Documentation

| Doc | Content |
|---|---|
| [docs/00-vision.md](docs/00-vision.md) | What the app does, user flow, media cases, full feature inventory, why the rewrite |
| [docs/01-architecture.md](docs/01-architecture.md) | Stack, projects, layers, dependency rule, runtime picture, state ownership |
| [docs/02-media-engine.md](docs/02-media-engine.md) | libmpv wrapper, roles/channels, media plan for all cases, clock & sync, frame bus, device routing |
| [docs/03-game-engine.md](docs/03-game-engine.md) | UltraStar timing, playback state machine, mic pipeline, scoring, beamer rendering data model |
| [docs/04-ui.md](docs/04-ui.md) | Windows, DJ layout, MVVM rules, design tokens, controls, Avalonia pitfalls |
| [docs/05-conventions.md](docs/05-conventions.md) | Coding standards, units, threading, logging, comments, tests, git, definition of done |
| [docs/06-build-release.md](docs/06-build-release.md) | Onboarding, native dependencies, publish & packaging per platform |
| [docs/07-roadmap.md](docs/07-roadmap.md) | Sprints with acceptance criteria |

AI agents: start with [.github/copilot-instructions.md](.github/copilot-instructions.md).

## Getting started (developer)

```sh
# 1. Install the .NET 10 SDK (version pinned in global.json) — macOS: brew install --cask dotnet-sdk
# 2. Fetch native binaries for this machine (libmpv, yt-dlp, ffmpeg) — never committed
zsh scripts/fetch-natives.sh          # macOS
pwsh scripts/fetch-natives.ps1        # Windows
# 3. Run
dotnet run --project src/UltrastarDJ.App
```

See [docs/06-build-release.md](docs/06-build-release.md) for release builds.

## Status

Sprints 0–3 done on macOS: skeleton, media engine (libmpv, YouTube once → N windows, per-channel output device), Core port with tests, SQLite library, audio input (PortAudio, 4 mics on 2 SingStar dongles, YIN, monitoring, latency calibration). Sprint 4 (game on the beamer) next — see the roadmap.

## License

MIT
