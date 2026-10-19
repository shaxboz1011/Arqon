<div align="center">

# Arqon

**Use Wi-Fi, Ethernet and your phone's internet at the same time on Windows.**
Faster downloads. Smooth games while you download. Free and open source.

[**English**](README.md) · [**O'zbekcha**](README.uz.md) · [**Русский**](README.ru.md)

[![Build](https://github.com/OWNER/Arqon/actions/workflows/build.yml/badge.svg)](https://github.com/OWNER/Arqon/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/OWNER/Arqon?include_prereleases)](https://github.com/OWNER/Arqon/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/OWNER/Arqon/total)](https://github.com/OWNER/Arqon/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

<img src="docs/img/en-panel.png" alt="Arqon dashboard: Wi-Fi and a USB phone combined into one connection" width="820">

</div>

> *Arqon* means "rope" in Uzbek: several thin strands, one strong line.

## Why Arqon

Most people have more than one way online: home Wi-Fi, a cable, a phone with a data plan. Windows uses only one at a time. So a big download crawls, and while it runs your game lags.

Arqon uses **all of them together**:

- **Faster downloads.** A file is split into pieces and every piece is fetched over a different connection at once. In a real test, 140 MB over Wi-Fi plus a USB phone took about 10 seconds.
- **Games stay smooth.** Arqon sees a running game, watches its ping, and slows downloads only on the game's link, only as much as needed, then gives the speed back.
- **Every app can benefit.** *System mode* spreads the connections of Telegram, Chrome, Steam and others over your links. In a first real test, 40 parallel downloads finished in 5.3 s instead of 11.3 s.
- **Torrents too.** Peer connections are spread over every link. Paste a magnet link or open a `.torrent` file, and choose which files to download.
- **Nothing hidden.** No account, no ads, no telemetry. MIT licence.

> Arqon only adds bandwidth when each link has its **own** way to the internet (for example Wi-Fi from one router plus a phone's mobile data). Two adapters behind the same router share one line and give no gain.

## Screenshots

| Downloads | Apps | Usage |
|---|---|---|
| <img src="docs/img/en-downloads.png" width="270"> | <img src="docs/img/en-apps.png" width="270"> | <img src="docs/img/en-usage.png" width="270"> |

The interface is in **Uzbek and English** (switch in Settings); this guide is also in Russian. Screenshots are taken in demo mode.

## Install

1. Download **`Arqon.exe`** from the [latest release](https://github.com/OWNER/Arqon/releases/latest). It is one file: nothing to install, .NET is included.
2. Double-click it. Windows SmartScreen may warn that the app is unrecognised, because the file is not code-signed yet (see [Security](#security)). Click **More info → Run anyway**.
3. Connect your second internet, for example a phone with **USB tethering** (Android: Settings → Network → Hotspot & tethering → USB tethering).
4. Open **Dashboard**: both connections should be listed. If a yellow notice appears, press **Fix** (Windows may otherwise switch off the second network).

To check the download, compare its SHA-256 with the `Arqon.exe.sha256` file in the release:

```powershell
(Get-FileHash .\Arqon.exe -Algorithm SHA256).Hash
```

### Quick tour

| Page | What you do there |
|---|---|
| **Dashboard** | See every connection live: speed, ping, jitter, loss. One-click fix for Windows switching off the second network. |
| **Downloads** | Paste a file link or a magnet link, or pick a `.torrent` file. Choose the folder. Pick files inside a multi-file torrent. |
| **Apps** | Set rules per app: automatic, only Wi-Fi, only phone, both, or a speed cap. Games are detected automatically. |
| **Usage** | How much data each connection used per day and per month (handy for a phone data plan). |
| **Settings** | Language, speed unit (Mbit/s or MB/s), game protection, system mode, torrents, start with Windows. |

## System mode (every app, not only Arqon)

System mode steers new connections of all programs over your links. It needs the signed [WinDivert](https://reqrypt.org/windivert.html) packet driver and **administrator rights**, so it is off by default.

```powershell
# 1. Download the helper script (it checks the driver's signature before installing)
Invoke-WebRequest https://raw.githubusercontent.com/OWNER/Arqon/main/tools/get-windivert.ps1 -OutFile get-windivert.ps1

# 2. Put the driver next to Arqon.exe (change the folder to where your Arqon.exe is)
powershell -ExecutionPolicy Bypass -File .\get-windivert.ps1 -Destination "C:\Tools\Arqon"
```

Then start `Arqon.exe` **as administrator** and switch on *System mode* in Settings. If anything goes wrong, Windows routes normally again at once. While a game from the catalogue is running, Arqon switches system mode off and unloads the driver (anti-cheat safety). Details: [docs/steering.md](docs/steering.md).

## Command line

Everything is in the window, but these options exist:

| Command | What it does |
|---|---|
| `Arqon.exe` | Start normally. |
| `Arqon.exe "C:\path\file.torrent"` | Add a torrent file (this is what a double-click on a `.torrent` does). |
| `Arqon.exe "magnet:?xt=urn:btih:..."` | Add a magnet link. A running Arqon receives it and adds it. |
| `Arqon.exe --tray` | Start hidden in the tray (used by *Start with Windows*). |
| `Arqon.exe --demo` | Two pretend connections, no phone needed. Good for trying the interface. |
| `Arqon.exe --screenshots <folder>` | Save a picture of every page into the folder. |

In Settings, **Open torrents and magnet links with Arqon** registers it for `.torrent` files and `magnet:` links (current user only, no administrator rights, and it never takes over another program's default).

## Build from source

You need Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/OWNER/Arqon.git
cd Arqon

dotnet run --project src/Arqon.App            # run with your real adapters
dotnet run --project src/Arqon.App -- --demo  # run with two pretend connections
dotnet test                                    # 379 tests

# one self-contained Arqon.exe (about 75 MB) in publish\win-x64
powershell -ExecutionPolicy Bypass -File tools\build.ps1
```

## Features and status

| Feature | Status |
|---|---|
| Link discovery (Wi-Fi / Ethernet / phone) and live monitor: speed, ping, jitter, loss | Done |
| Multi-link download manager: chunked HTTP ranges over every link, resume, endgame racing, speed limit, respects 429 / Retry-After | Done |
| Download queue (at most N at once, "Start now" jumps the line) | Done |
| Game ping guard: caps downloads on the game's link only, then gives bandwidth back gradually | Done |
| Per-app rules, built-in game catalogue, JSON settings | Done |
| One-click fix for the Windows policy that switches off the second network | Done |
| Usage statistics per link, per day and month | Done |
| Tray icon, notifications, clipboard offer, single instance, start with Windows | Done |
| Taskbar speed meter with hover totals | Done (tested on Windows 10, bottom taskbar) |
| Safe game mode (system mode off and driver unloaded while a game runs) | Done (tested with a fake game, not with a real anti-cheat) |
| Speed unit switch (Mbit/s or MB/s) | Done |
| Interface in Uzbek and English | Done (more languages welcome) |
| **Torrents and magnet links** over every link; choose download folder; choose files; open from Windows | Done (real Debian torrent over Wi-Fi + phone: both adapters carried data) |
| **System mode** (speed up Telegram, Chrome, every app) | Works in a first real test (Wi-Fi + USB phone). Not yet tested with a game or over a long session. Needs WinDivert and administrator rights |
| Relay server (one connection split across links, duplicated game packets) | Planned |
| Installer and code signing | Planned |

Honest limits: torrents stop when finished (no seeding yet), UPnP port mapping is off, and system mode was not yet checked with the driver when running from the single-file `Arqon.exe`.

## Security

- **No telemetry, no account, no update pings.** Arqon connects only to what you ask for, plus: the link monitor opens a connection to `1.1.1.1` / `8.8.8.8` (port 443) to measure ping, and torrents contact public trackers and the DHT, as every torrent client does.
- Changes to Windows (registry, driver) are explained first and reversible. System mode is off by default and fails open.
- `Arqon.exe` is **not code-signed yet**, so SmartScreen warns. You can verify the SHA-256 or build it yourself from this source. Free code signing for open-source projects is planned.
- Found a vulnerability? Please read [SECURITY.md](SECURITY.md).

## Compared with similar tools

[Plexo](https://github.com/anmolkapil/plexo) speeds up its own downloads (and torrents) with ranged requests over several networks. Arqon uses the same idea for its downloader and goes further: protecting game latency, per-app rules, steering the connections of other programs, and handling the Windows pitfalls (the Connection Manager policy) for you.

## Contributing

Bug reports, ideas, translations and pull requests are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md). Good first steps: try Arqon on your own two connections and tell us the result in an [issue](https://github.com/OWNER/Arqon/issues/new/choose), or add a language to the interface (`src/Arqon.App/Resources/Strings.*.xaml`: Russian is the first wanted one).

If Arqon is useful to you, a ⭐ on GitHub helps other people find it.

## Licence

MIT, see [LICENSE](LICENSE). System mode uses [WinDivert](https://github.com/basil00/WinDivert) (LGPLv3), which is downloaded separately and is not part of this repository. Torrents use [MonoTorrent](https://github.com/alanmcgovern/monotorrent) (MIT).
