# Arqon – Design

Arqon is a Windows tool that uses several internet connections at once. It keeps games smooth while downloads run, and speeds up downloads in every application. Open source (MIT), written in C#.

## Why

The owner (Uzbekistan, Wi-Fi + unlimited USB-tethered phone) sees two problems: games freeze while anything downloads, and the two connections cannot be used together. The reference project Plexo (Electron, MIT) only speeds up its own downloads, and its README admits Windows pitfalls: unsigned installer, Windows Connection Manager turning the second network off, and Windows routing traffic out the wrong adapter. Arqon's goal is to beat it on quality, comfort and Windows behaviour, and to work for every application, not only its own downloader.

## Goals

1. **Game protection.** Latency-sensitive traffic (games) uses the best link by latency/jitter/loss. Background downloads never saturate that link.
2. **Combine links.** Many-connection applications (Chrome, Telegram, Steam) spread across all links.
3. **Per-application control.** Each application gets: Auto, Only link X, or Both; optional speed cap. Games are auto-detected from a built-in catalogue.
4. **Built-in download manager.** Link or magnet in, chunks fetched in parallel over every link, resumable. (v1: HTTP/HTTPS links; torrents come after v1.)
5. **Windows fixes done for the user.** One-click fix for the Windows Connection Manager policy that disables the second network.
6. **Great UI/UX.** Dark theme, three main pages (Panel, Apps, Downloads) plus Settings. Live charts. Uzbek first, English second. Approved mockup: `mockups/arqon-v1-mockup.html`.

## Non-goals (v1)

- No relay server and no single-flow bonding of one TCP connection (planned later; leave an `IRelay` seam out of v1, do not build it).
- No torrent engine in v1.
- No macOS/Linux.
- No cloud accounts, telemetry or ads.

## Anti-goals

- Arqon must never leave the user without internet. Any steering failure falls back to Windows default routing within 2 seconds.
- No silent system changes. Every registry/firewall change is shown and reversible.

## Constraints

- .NET 10 (`net10.0` core, `net10.0-windows` WPF app). SDK present: 10.0.401.
- No third-party UI/chart libraries. Charts are drawn in WPF.
- Packet steering uses WinDivert 2.2.x (LGPLv3, signed driver), loaded dynamically, needs administrator rights. It is experimental and off by default until proven on a machine with two live links.
- Only one live link (Wi-Fi) exists on the build machine, so multi-link behaviour is verified with fakes and a local test server; real two-link verification is done by the owner with the phone attached.

## Decided by the owner

Name Arqon, language C#, WPF-style dark UI as in the mockup, server-less first, built-in download manager as an addition to system-wide operation.

## Builder's calls (not yet confirmed by the owner)

WPF over WinUI 3; steering via WinDivert NAT-style rewrite; Uzbek default UI language; MIT licence; JSON settings in `%AppData%\Arqon\settings.json`.
