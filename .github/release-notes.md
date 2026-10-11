**Ultrastar DJ {VERSION}** — a beta. Already installed? The app offers this version itself when it starts.

### Download

| Your computer | File |
|---|---|
| Mac with Apple Silicon (M1 or newer) | `Ultrastar-DJ-{VERSION}-osx-arm64.dmg` |
| Mac with Intel | `Ultrastar-DJ-{VERSION}-osx-x64.dmg` |
| Windows 10 / 11 | `UltrastarDJ-win-Setup.exe` |
| Linux (x64) | `UltrastarDJ.AppImage` |

Not sure which Mac you have? Apple menu → About This Mac → "Chip" (Apple M…) or "Processor" (Intel). macOS 14 (Sonoma) or newer is needed.

The other files (`.nupkg`, `.json`, `RELEASES`) are for the app's own updates — no need to download them.

### First start

The app is not signed by Apple or Microsoft (that costs a yearly fee), so your computer asks once:

- **Mac:** open the dmg, drag Ultrastar DJ into Applications, open it. macOS says it cannot check the app → **System Settings → Privacy & Security → Open Anyway**.
- **Windows:** "Windows protected your PC" → **More info → Run anyway**. It installs and starts by itself.
- **Linux:** install mpv once (`sudo apt install libmpv2`, on older systems `libmpv1`), then right-click the AppImage → Properties → Permissions → **Allow executing file as program**, and double-click it.

Updates after that install themselves — no more questions.
