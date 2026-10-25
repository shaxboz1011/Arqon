# Changelog

## 0.3.0

**Windows**

- **Problem report:** *Settings → Problem report → Copy report* puts the version, Windows, connections, system mode and the last crashes on the clipboard for a bug report. The user name, full addresses and folders are left out. Every unhandled error (also on background threads) is written to `crash.log`, which trims itself.
- **winget:** every release carries the three winget manifests, and with the `WINGET_TOKEN` secret the release workflow submits them to winget-pkgs (`winget install shaxboz1011.Arqon` once accepted).
- **Browser extension** (Chrome, Edge, Firefox) in `browser-extension/`: *Download with Arqon* on every link, and optionally every download. It talks to Arqon through `arqon://add?url=…` links, which accept only web and magnet links.
- **Download schedule:** queued downloads start only in chosen hours. Running downloads keep going, and *Start now* always works.
- **Save data:** ticked connections are used by downloads and torrents only when no other link is up; optionally all phone links once the daily limit is reached.
- **Torrents:** UPnP / NAT-PMP port mapping can be switched on (off by default).
- **Local API** for scripts on 127.0.0.1 with a token (off by default): connections, downloads, add a download. See `docs/api.md`.
- **Relay groundwork:** signed frames, a reorder buffer and a link scheduler for single-connection bonding, with a design in `docs/relay.md`. No relay server yet.
- [ROADMAP.md](ROADMAP.md) with the plan up to 1.0.

## 0.2.0

**Windows**

- The taskbar meter no longer lands on the weather widget or on taskbar buttons: it takes the free space nearest the notification area that is big enough (shrinking a little if needed), and hides instead of covering anything. You can also drag it along the bar (the place is remembered; right-click → *Reset position*), and right-click → *Copy taskbar layout* puts what it sees on the clipboard for bug reports.
- Speed test on the dashboard: each connection alone, all together, and the measured gain.
- Update check (GitHub, twice a day, can be switched off) with a notice and a download button in Settings.
- Daily data limit: a notice at 80% and when it is reached.
- *Usage → Export CSV*.
- The sidebar and Settings show the real version; the tray icon's tip shows the live speed.

- **One-click system mode:** *Settings → System mode → Set up system mode* adds the driver folder to Windows Defender's exclusions (Defender wrongly removes the WinDivert driver, the usual reason system mode did not work on other computers), downloads the official WinDivert 2.2.2 driver, checks its SHA-256 and signature, and turns system mode on. The driver now lives in `%APPDATA%\Arqon\driver`; files next to `Arqon.exe` still work.
- **Update in the app:** when GitHub has a newer version a card "Update available" appears in the sidebar; one click downloads `Arqon.exe`, checks it against the release's SHA-256, replaces the running copy and restarts Arqon.

**Android (preview, in `android/`)**

- Game mode: the chosen games go through a VPN tunnel over the best connection while downloads keep off that connection.
- Data usage screen (today, this month, last 7 days, per app), speed meter notification with the speed on the status-bar icon, signal and network type, daily mobile data limit, shortcuts to the 5G / 4G network-mode screens.
- Battery setting that keeps downloads running on Samsung and other phones that put apps to sleep.
- Anonymous statistics, off unless you agree and not available in the open-source build (see `docs/privacy.md`).

## 0.1.3

- Taskbar meter no longer covers the weather widget (or other things) that Windows 11 keeps next to the notification area: it steps to the left of them.
- Seeding continues after a restart: finished torrents are shared again when their files are still on disk (never starts a download).

## 0.1.2

- Russian interface language (Settings → Language).

## 0.1.1

- Optional seeding of finished torrents (Settings): stops at a chosen share ratio or by hand, held back while the game guard is active.
- Removing an unfinished torrent deletes its files also after a restart.
- Choice of files inside a multi-file torrent.

## 0.1.0

First public release.

- Live monitor of every connection (Wi-Fi, Ethernet, phone): speed, ping, jitter, loss.
- Multi-link download manager with resume, queue, speed limit.
- Game ping guard and safe game mode.
- Torrents and magnet links over every link; choice of download folder and of files; open from Windows.
- System mode (WinDivert) to spread the connections of every app.
- Per-app rules, usage statistics, tray, taskbar speed meter, Uzbek and English interface.
