# Arqon

**Combine your internet connections on Windows: smooth games while downloading, faster downloads.**
**Windows'da internet ulanishlaringizni birlashtiring: yuklashda ham o'yin qotmaydi, yuklash tezroq.**

Arqon ("rope" in Uzbek) uses Wi-Fi, Ethernet and a USB-tethered phone at the same time.

## Status

| Feature | Status |
|---|---|
| Link discovery (Wi-Fi / Ethernet / phone) and live monitor: speed, ping, jitter, loss | Done |
| Multi-link download manager: chunked HTTP ranges over every link, resume, endgame racing, speed limit. Respects server limits (429 / Retry-After), and a link with no route to the server no longer blocks the start | Done (140 MB over Wi-Fi + phone in about 10 s) |
| **Download queue**: at most N downloads run at once (Settings, default 3), the rest wait in order and start by themselves; "Start now" jumps the line | Done (queue logic tested; the screen was checked in demo mode, not clicked through with real downloads) |
| **Game ping guard**: detects running games, and when ping or loss rises on the game's link it caps downloads on that link only, then gives bandwidth back gradually | Done |
| Game link choice (lowest latency, jitter, loss, with hysteresis) | Done |
| Per-app rules, built-in game catalogue, JSON settings | Done |
| One-click fix for the Windows policy that turns the second network off | Done |
| Usage statistics: bytes per link per day, 14-day chart, monthly totals, persisted | Done |
| Tray icon, close-to-tray, balloon notifications (link connected/lost, game found, cap engaged), single instance, start hidden with Windows | Done |
| Clipboard offer: copy a file link anywhere and Downloads offers to fetch it | Done |
| **Taskbar speed meter**: live download and upload speed next to the tray; hover for today's and this month's traffic, per link. Hides itself while a game runs full screen | Done (Windows 10 tested; bottom taskbar, not auto-hidden) |
| **Safe game mode**: while a game runs, system mode is stopped and the WinDivert driver unloaded (anti-cheat safety); it comes back when the game closes | Done (tested with a fake game, not with a real anti-cheat) |
| Speed unit switch: Mbit/s or MB/s, everywhere (meter, Dashboard, charts, Downloads, app speed caps) | Done |
| Dashboard, Apps, Downloads, Usage, Settings UI (dark theme, live charts, Uzbek/English) | Done |
| **System mode** (speed up Telegram, Chrome, every app) | **Implemented and unit-tested; first test with Wi-Fi plus a USB phone: 40 parallel downloads finished in 5.3 s instead of 11.3 s.** Not yet tested with a game or over a long session. Needs the WinDivert driver and administrator rights. See [docs/steering.md](docs/steering.md) |
| Torrents / magnet links | Planned |
| Optional relay server (single-connection bonding, duplicated game packets) | Planned |
| Installer and code signing | Planned |

Arqon only adds bandwidth when each link really has its own internet path. Two adapters behind the same router share one uplink and give no gain.

## Run it

Requires Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet run --project src/Arqon.App            # real adapters
dotnet run --project src/Arqon.App -- --demo  # two pretend links, no phone needed
dotnet test                                    # 302 tests
```

System mode additionally needs the packet driver, see [docs/steering.md](docs/steering.md):

```powershell
powershell -ExecutionPolicy Bypass -File tools\get-windivert.ps1 -Destination src\Arqon.App\bin\Debug\net10.0-windows
```

## Layout

- `src/Arqon.Core`: links, monitoring, scoring, download engine, game guard, rules, settings, usage, steering (no UI)
- `src/Arqon.App`: WPF app
- `tests/Arqon.Core.Tests`: xUnit tests, including a local range-capable HTTP server and hand-built packets
- `tools`: icon generator, WinDivert installer script
- `docs`: steering design, design and implementation plans
- `mockups`: the approved UI mockup

## Compared with Plexo

[Plexo](https://github.com/anmolkapil/plexo) speeds up its own downloads (and torrents) with chunked range requests over several networks. Arqon uses the same idea for its download manager and aims further: protecting game latency, per-app rules, steering other apps' connections, and handling the Windows pitfalls Plexo documents (the Connection Manager policy) for you.

## Licence

MIT. System mode uses WinDivert (LGPLv3), which is downloaded separately and not part of this repository.
