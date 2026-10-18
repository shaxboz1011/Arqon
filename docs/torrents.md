# Torrents

Arqon downloads torrents with [MonoTorrent](https://github.com/alanmcgovern/monotorrent) (MIT, NuGet `MonoTorrent` 3.0.2). What Arqon adds is the multi-link part and the integration with the rest of the app.

## Adding a torrent

In **Downloads**, paste into the box and press Enter, or use the "Torrent file" button:

- a magnet link (`magnet:?xt=urn:btih:...`),
- a link to a `.torrent` file (it is fetched first),
- the path of a `.torrent` file on this computer (quotes from "Copy as path" are fine),
- or pick one or more `.torrent` files with the button.

A magnet link on the clipboard is offered like a file link. The same torrent is never added twice (compared by info hash). A `.torrent` file is copied to `%APPDATA%\Arqon\torrents\<hash>.torrent`, so the torrent still resumes after the original is moved or deleted.

## Where it is saved

One setting for all downloads: the folder you pick on the Downloads page (pencil button). By default it is automatic, the **Downloads folder of Windows** (asked from Windows, so a moved Downloads folder works). The reset button next to the pencil goes back to automatic. A multi-file torrent creates its own folder inside it.

## Over several links

Every outgoing peer connection is bound to the address of the next link (Wi-Fi, phone, ...) in turn, so a torrent's many connections leave through both adapters. This needs no system mode and no driver. A link that looks down (all probes failed) is skipped while another works. IPv6 peers are left to Windows. The numbers next to a torrent (`Wi-Fi 53 · Phone 54`) count connection attempts per link, not live peers.

Incoming connections cannot be steered: they arrive on whichever adapter the peer reaches.

## Behaviour worth knowing

- Torrents take part in the download queue ("downloads at the same time" in Settings).
- A finished torrent is stopped: Arqon does not stay on to seed. Uploading still happens while it downloads (that is how the protocol works).
- While the game protection holds a link back for a running game, all torrents together are held to that same speed, so a torrent cannot undo the protection.
- The engine is created when the first torrent is added, not at start-up. It opens a listening port, so Windows may show a **firewall question the first time**.
- UPnP / NAT-PMP port mapping is **off**: it changes the router's configuration. Incoming connections therefore work only where the network allows them.
- Resume data and the metadata of magnet links live in `%APPDATA%\Arqon\torrent-cache`.
- Removing an unfinished torrent deletes its downloaded files, but only for a torrent started in the current session (the engine then knows exactly which files are its own). After a restart, the files of an unfinished torrent that you remove stay on disk.
- `wss://` (WebTorrent) trackers are not supported by MonoTorrent; a torrent that has only those finds no peers unless DHT helps.

## Tested

- Unit tests: input detection, identity by info hash, folder choice, link rotation, the socket connector (each connection really leaves from the next link's address).
- Integration test: a seeder and a downloader engine on loopback; the file arrives intact and the downloader's connection went through a link chosen by Arqon.
- Real run (2026-10-18, Wi-Fi + USB phone, official Debian netinst torrent, speed capped at 1.5 MB/s): 40 peers, 35 seeds; both adapters carried the data (Wi-Fi 24 MB, phone 35 MB in 45 s).
- Not tested: a full multi-gigabyte download, DHT-only magnet links, the screen driven with real clicks.
