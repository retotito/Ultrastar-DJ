# Commands

The commands used day to day, for copying into a terminal. Run them in the project folder (`Ultrastar-DJ`).
Details behind them: [06-build-release.md](06-build-release.md).

## Every day

| What | Command |
|---|---|
| Get the newest code (always first) | `git pull` |
| Start the app | `dotnet run --project src/UltrastarDJ.App` |
| Stop the app | close its window, or `Ctrl+C` in the terminal |
| Build only (check that it compiles) | `dotnet build` |
| Run all tests | `dotnet test` |

## Git

| What | Command |
|---|---|
| What changed? | `git status` |
| The last commits | `git log --oneline -10` |
| Send commits to GitHub (other machines get them with `git pull`) | `git push` |
| Commits not yet on GitHub | `git log origin/main..HEAD --oneline` |

## Setting up a machine (once)

Install the .NET SDK (version in `global.json`) first, then fetch the native parts (libmpv, yt-dlp, ffmpeg,
cloudflared — never in git):

| System | Command |
|---|---|
| Mac | `zsh scripts/fetch-natives.sh` |
| Windows (PowerShell) | `powershell -ExecutionPolicy Bypass -File scripts\fetch-natives.ps1` |
| Linux (Ubuntu / Mint) | `sudo apt install git libmpv2` (Mint 21 / Ubuntu 22.04: `libmpv1`), then `bash scripts/fetch-natives-linux.sh` |

Then `dotnet build`, `dotnet test`, `dotnet run --project src/UltrastarDJ.App`.

## Logs

Today's log file is `logs/ultrastardj-YYYYMMDD.log` in the app's data folder. The developer build and an installed
copy write to the same file.

| System | Last 200 lines of today's log |
|---|---|
| Mac | `tail -200 ~/Library/Application\ Support/UltrastarDJ/logs/ultrastardj-$(date +%Y%m%d).log` |
| Windows (PowerShell) | `Get-Content "$env:APPDATA\UltrastarDJ\logs\ultrastardj-$(Get-Date -f yyyyMMdd).log" -Tail 200` |
| Linux | `tail -200 ~/.config/UltrastarDJ/logs/ultrastardj-$(date +%Y%m%d).log` |

Follow the log live (Mac / Linux): replace `tail -200` with `tail -f`.

## Releases

Releases are built by GitHub Actions, not on a machine.

1. Set the new version in `Directory.Build.props` (`<Version>0.9.0-beta.2</Version>`), commit, `git push`.
2. Tag it and push the tag — this starts the build and publishes the release:
   ```
   git tag v0.9.0-beta.2
   git push origin v0.9.0-beta.2
   ```
3. Watch the build: `gh run list --workflow build.yml --limit 3` (or the Actions tab on GitHub).
4. Add a "What's new" list to the release text (GitHub → Releases → the version → Edit, or
   `gh release edit v0.9.0-beta.2 --notes-file notes.md`) — what users notice, in plain words.
5. Test the update: open the installed app on each machine; it offers the new version after a few seconds.
6. Then update the version and the download links in `website/index.html`, commit, `git push`.

Download counts per file: `gh api repos/retotito/Ultrastar-DJ/releases --jq '.[0].assets[] | "\(.name) \(.download_count)"'`

## Website

| What | Command |
|---|---|
| Look at it locally | `python3 -m http.server -d website`, then open http://localhost:8000 |
| Publish | commit the change in `website/` and `git push` — online about a minute later |

## GitHub

| What | Command |
|---|---|
| Latest checks (tests, CodeQL, website) | `gh run list --limit 5` |
| Open feedback / issues | `gh issue list` |
| Open pull requests (Dependabot) | `gh pr list` |
