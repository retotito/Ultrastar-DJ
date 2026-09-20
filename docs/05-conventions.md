# 05 — Conventions

These are binding for humans and agents. When in doubt: boring, explicit, tested.

---

## Language & compiler

- C# latest, `.NET 10` (SDK pinned in `global.json`). Versions of all packages live in `Directory.Packages.props` (central package management) — never in a `.csproj`.
- `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` for `Core`, `Media`, `Audio`; warnings allowed but
  visible in `App` during UI iteration.
- Analyzers: built-in .NET analyzers at `AnalysisLevel=latest-recommended`; `.editorconfig` enforces style
  (4 spaces, file-scoped namespaces, `var` when type is apparent, braces always).

## Naming

| Thing | Rule | Example |
|---|---|---|
| Types, methods, properties | PascalCase | `MediaChannel`, `LoadAsync` |
| Private fields | `_camelCase` | `_player` |
| Interfaces | `I` + noun/adjective | `IMediaPlayer`, `IGameClock` |
| Async methods | `Async` suffix, return `Task`/`ValueTask` | `LoadAsync` |
| Events | past tense or `…Changed` | `StateChanged`, `FrameReady` |
| Messages (messenger) | records, noun phrase | `record GameTick(double PositionSec, double Beat)` |
| Bool members | `Is`/`Has`/`Can` | `IsRunning`, `CanPlay` |
| Units in names when not `TimeSpan` | suffix | `gapMs`, `videoGapSec`, `lengthBeats`, `sampleRateHz` |

**Units rule (mandatory):** any numeric time/pitch/gain value that is not a `TimeSpan` carries its unit in the
name. Public APIs prefer `TimeSpan`. UltraStar "pitch" (C4 = 0) and MIDI (C4 = 60) are different scales — name
them `usPitch` and `midiNote`, never just `pitch`.

## Types

- `record` for immutable domain data (`Song`, `Note`, messages). `sealed class` by default for behaviour.
- No public mutable collections on records; expose `IReadOnlyList<T>`.
- Enums over string constants (`NoteType`, `MediaState`, `PlaybackState`).
- Nullable annotations are truth: no `!` operators except at boundaries with a comment why.

## Structure & design

- Interfaces only at layer boundaries (`01-architecture.md`). No `IFooService` for every class.
- Constructor injection; no static service access; no singletons except `static readonly` pure helpers.
- Factories only for runtime-parameterised creation (`IMediaPlayerFactory`).
- No "Manager", "Helper", "Util", "Common" names. Name the responsibility.
- One class per file, file name = type name.
- Small methods; guard clauses; early return. No regions.
- Do not add configuration/feature flags "for later". Add when needed.

## Realtime & threading

Applies to PortAudio callbacks, mpv callbacks, `GameTicker`, `FrameBus`:

- **No allocations, no locks, no logging, no exceptions** inside audio callbacks. Preallocate buffers,
  use `Interlocked`/`Volatile` or a lock-free ring buffer to hand data to worker threads.
- Never call Avalonia from these threads. Hand off via `Dispatcher.UIThread.Post` or the messenger
  (messenger handlers run on the sender's thread — marshal inside the handler if it touches UI).
- `async void` is forbidden except Avalonia event handlers, which must `try/catch` and log.
- Every long-lived loop takes a `CancellationToken` and disposes cleanly. `IAsyncDisposable` for anything
  owning native handles.
- Determinism: the scoring path must be a pure function of its input sequence. Non-determinism bugs are
  debugged by **controlling variables first** (same start position twice → same result?) before changing code.

## Error handling

- Validate at boundaries (files, network, native calls, user input). Inside the domain, trust the types.
- Native/library failures become typed exceptions (`MpvException`, `AudioBackendException`) with the native
  message; callers decide UX.
- User-facing errors go through `IDialogService`/toasts with the actual cause. Never swallow silently;
  `catch { }` needs a comment saying why.

## Logging

- `ILogger<T>` injected; Serilog sinks to console (Debug) and rolling file in app data (all builds).
- Levels: `Trace` per-tick noise (off by default), `Debug` state transitions, `Information` user actions and
  device changes, `Warning` recoverable (device gone, drift seek), `Error` failures.
- Structured properties, not string concatenation: `_log.LogDebug("Drift {DriftMs} ms, seeking", driftMs)`.
- Remove temporary debug logs before merging.

## Comments & documentation

- `///` XML docs on every **public** type and member in `Core`, `Media`, `Audio`, `Infrastructure`. Say what
  it is for, the contract (units, ranges, thread affinity, ownership of buffers), not how it works.
- Inline comments explain **why** (a non-obvious constraint, a spec quirk, a tuned constant), one short line.
  Never restate the code, never leave changelog notes ("changed X to Y") or reviewer notes in code.
- Tuned constants get a comment with the reason and reference:
  `const double DriftSeekThresholdSec = 0.35; // above this, nudging speed is too slow to converge`
- Architecture-level knowledge lives in `docs/`, not in comments. Update the doc in the same PR.

## Tests

- xUnit. Name: `Method_Scenario_Expectation` (`Matches_OctaveAbove_IsTrue`).
- `Core` is 100 % testable without mocks. `Media`/`Audio` logic (clock, follower, ring buffer, YIN, gate,
  scoring integration) tested with fake `IMediaPlayer`/synthetic PCM (generated sine sweeps).
- Every bug fix adds a test that failed before the fix.
- `dotnet test` must pass before any commit that touches `Core`, `Media`, `Audio`.

## Git

- `main` is always buildable. Feature branches, small PRs, squash-merge.
- Conventional commits: `feat(media): …`, `fix(audio): …`, `docs: …`, `chore: …`, `test: …`.
- Never commit `natives/`, `bin/`, `obj/`, logs, or user settings. `.gitignore` is authoritative.
- Sidecar binaries are downloaded by scripts, never vendored.

## Definition of done (per task)

1. Builds on macOS and Windows (`dotnet build -warnaserror` for Core/Media/Audio).
2. Tests added/updated and green.
3. Docs updated if behaviour or architecture changed.
4. No new WebView, no layer-rule violation, no hardcoded colours/sizes in XAML, no units-less numbers in public APIs.
5. Manual check on the real feature if it touches media/audio (device switch, YouTube case, two beamers).
