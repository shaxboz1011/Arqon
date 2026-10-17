# Arqon

**Combine your internet connections on Windows: smooth games while downloading, faster downloads.**
**Windows'da internet ulanishlaringizni birlashtiring: yuklashda ham o'yin qotmaydi, yuklash tezroq.**

Arqon ("rope" in Uzbek) uses Wi-Fi, Ethernet and a USB-tethered phone at the same time.

## Status

| Feature | Status |
|---|---|
| Link discovery (Wi-Fi / Ethernet / phone) and live monitor: speed, ping, jitter, loss | Done |
| Game protection logic: pick the best link for games, avoid flip-flopping | Done (decision logic; applied to traffic only in system mode) |
| Multi-link download manager: chunked HTTP ranges over every link, resume, endgame racing, speed limit | Done |
| Per-app rules, built-in game catalogue, JSON settings | Done |
| One-click fix for the Windows policy that turns the second network off | Done |
| Dashboard, Apps, Downloads, Settings UI (dark theme, live charts, Uzbek/English) | Done |
| **System mode** (speed up Telegram, Chrome, every app) | **Decision logic done; packet driver not connected.** See [docs/steering.md](docs/steering.md) |
| Torrents / magnet links | Planned |
| Optional relay server (single-connection bonding, duplicated game packets) | Planned |
| Installer and code signing | Planned |

Arqon only adds bandwidth when each link really has its own internet path. Two adapters behind the same router share one uplink and give no gain.

## Run it

Requires Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet run --project src/Arqon.App            # real adapters
dotnet run --project src/Arqon.App -- --demo  # two pretend links, no phone needed
dotnet test                                    # 59 tests
```

## Layout

- `src/Arqon.Core`: links, monitoring, scoring, download engine, rules, settings, steering decisions (no UI)
- `src/Arqon.App`: WPF app
- `tests/Arqon.Core.Tests`: xUnit tests, including a local range-capable HTTP server
- `docs/superpowers`: design and implementation plan
- `mockups`: the approved UI mockup

## Compared with Plexo

[Plexo](https://github.com/anmolkapil/plexo) speeds up its own downloads (and torrents) with chunked range requests over several networks. Arqon uses the same idea for its download manager and aims further: protecting game latency, per-app rules, and handling the Windows pitfalls Plexo documents (the Connection Manager policy) for you.

## Licence

MIT
