# Ultrastar DJ — working notes for Claude Code

Architecture, code rules and the project map (shared with other coding agents):

@.github/copilot-instructions.md

## How we work

- **Small sprints.** Each issue or small group of fixes is a sprint section in `docs/07-roadmap.md` with ticked
  items. Build + test, update the doc that describes the behaviour, then smoke-start the app yourself (below).
- **The owner verifies live before anything is committed.** Ask them to restart the app
  (`dotnet run --project src/UltrastarDJ.App`) and name the exact steps to check. Commit only when they confirm,
  push only when they say so. One descriptive conventional commit per sprint (`fix(audio): …`, `feat(ui): …`).
- **Never stop the owner's running app.** Start your own instance and kill only its PID — never `pkill`/`taskkill`
  by name, that closes theirs too. Do not replace files in `natives/` while the app runs.
- **Texts:** every UI text goes through `src/UltrastarDJ.App/Languages/*.json` (13 languages, keys sorted, same
  format as the files — two-space indent, no `\u` escapes). Add a new key to **all** languages.
- **Explain in plain words.** The owner is not a developer of this stack; say what happened and what to do next.

## Several machines (Mac, Windows, Linux)

Everyone works on `main`; nothing is platform-specific in git.

- **Before starting:** `git pull`. **After a confirmed fix:** commit and push, so the other machines get it.
- One machine at a time. If two must work in parallel, use a short branch per fix (`fix/windows-crash`) and merge
  it back the same day — no long-lived platform branches.
- Releases are built by GitHub Actions only (`docs/06-build-release.md` → "Releases and updates"); day-to-day
  testing is `dotnet run` on the machine itself.

## Setting up a machine (once)

| | Mac | Windows | Linux (Ubuntu / Mint) |
|---|---|---|---|
| Tools | Command Line Tools (`xcode-select --install`, or the dmg from developer.apple.com — free Apple ID) | Git for Windows, 7-Zip (`winget install Git.Git 7zip.7zip`) | `sudo apt install git libmpv2` (Mint 21 / Ubuntu 22.04: `libmpv1`) |
| .NET | .NET 10 SDK (version in `global.json`) | same | same |
| Natives | `zsh scripts/fetch-natives.sh` | `powershell -ExecutionPolicy Bypass -File scripts\fetch-natives.ps1` (Windows blocks scripts by default) | `bash scripts/fetch-natives-linux.sh` |

Then:

```
dotnet build
dotnet test
dotnet run --project src/UltrastarDJ.App
```

`natives/<rid>/` (libmpv, yt-dlp, ffmpeg, cloudflared) is git-ignored and fetched per machine.

## Smoke start (your own instance)

macOS / Linux:

```
src/UltrastarDJ.App/bin/Debug/net10.0/UltrastarDJ > /tmp/usdj.log 2>&1 &  PID=$!
sleep 20; kill -0 $PID && grep -iE "FTL|ERR|exception" /tmp/usdj.log; kill $PID
```

Windows (PowerShell):

```
$p = Start-Process src\UltrastarDJ.App\bin\Debug\net10.0\UltrastarDJ.exe -PassThru
Start-Sleep 20; Stop-Process -Id $p.Id
```

XAML style errors only show at runtime — always start the app once after UI changes.

## Logs and data

Settings, library database, cache and logs live in one folder per user — shared by the developer build and an
installed copy, so both write to the same log file (lines do not say which instance wrote them).

| | Data and logs (`logs/ultrastardj-YYYYMMDD.log`) | Installed app |
|---|---|---|
| macOS | `~/Library/Application Support/UltrastarDJ/` | `/Applications/Ultrastar DJ.app` |
| Windows | `%APPDATA%\UltrastarDJ\` | `%LOCALAPPDATA%\UltrastarDJ\current\` |
| Linux | `~/.config/UltrastarDJ/` | wherever the AppImage was saved |

Last lines of today's log on Windows: `Get-Content "$env:APPDATA\UltrastarDJ\logs\ultrastardj-$(Get-Date -f yyyyMMdd).log" -Tail 200`.

Before tests that change settings (fresh-user tests, probes), copy the data folder aside and restore it afterwards.
