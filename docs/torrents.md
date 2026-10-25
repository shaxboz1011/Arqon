# Torrents

Arqon downloads torrents with [MonoTorrent](https://github.com/alanmcgovern/monotorrent) (MIT, NuGet `MonoTorrent` 3.0.2). What Arqon adds is the multi-link part and the integration with the rest of the app.

## Adding a torrent

In **Downloads**, paste into the box and press Enter, or use the "Torrent file" button:

- a magnet link (`magnet:?xt=urn:btih:...`),
- a link to a `.torrent` file (it is fetched first),
- the path of a `.torrent` file on this computer (quotes from "Copy as path" are fine),
- or pick one or more `.torrent` files with the button.

A magnet link on the clipboard is offered like a file link. The same torrent is never added twice (compared by info hash). A `.torrent` file is copied to `%APPDATA%\Arqon\torrents\<hash>.torrent`, so the torrent still resumes after the original is moved or deleted.

### From Windows: double click, browser links, drag and drop

- **Double click a `.torrent` file / click a magnet link**: Windows starts `Arqon.App.exe "<file or link>"`. A running Arqon receives it from the second copy (through `%APPDATA%\Arqon\inbox`) and adds it; if Arqon is not running it starts and adds it itself. Before this was added, Arqon ignored the argument and only came to the front, so a torrent opened that way never downloaded.
- **Settings, "Open torrents and magnet links with Arqon"** registers Arqon for `.torrent` files and `magnet:` links in the current user's registry (no administrator rights; the toggle removes it again). Windows 10 keeps the choice of the default program for the user, so confirm it under Settings, Default apps (a button opens it). Another program's default is never taken over: if a torrent client already owns `magnet:`, Arqon is only offered in "Open with" and Default apps, and the settings page says which program owns it.
- **Drag and drop**: drop `.torrent` files, magnet links or web links on the window.
- If Arqon runs elevated (system mode needs it) and Explorer is not, Windows cannot hand the request over and a second, normal copy starts.

## Finding peers

- A magnet link usually names trackers; the public ones in `MagnetTrackers` are added to every magnet link after its own. Without them a bare magnet could not get its metadata on the test network (DHT only: no metadata in 90 s; with trackers: 7 s).
- The DHT needs bootstrap nodes. MonoTorrent's built-in hosts (`router.bittorrent.com`, `router.utorrent.com`) did not answer on the test network, so Arqon gives its own DHT engine nodes that do (`dht.transmissionbt.com`, `dht.libtorrent.org`, ...) until it reports ready.

## Where it is saved

One setting for all downloads: the folder you pick on the Downloads page (pencil button). By default it is automatic, the **Downloads folder of Windows** (asked from Windows, so a moved Downloads folder works). The reset button next to the pencil goes back to automatic. A multi-file torrent creates its own folder inside it.

## Over several links

Every outgoing peer connection is bound to the address of the next link (Wi-Fi, phone, ...) in turn, so a torrent's many connections leave through both adapters. This needs no system mode and no driver. A link that looks down (all probes failed) is skipped while another works. IPv6 peers are left to Windows. The numbers next to a torrent (`Wi-Fi 53 · Phone 54`) count connection attempts per link, not live peers.

Incoming connections cannot be steered: they arrive on whichever adapter the peer reaches.

## Behaviour worth knowing

- Torrents take part in the download queue ("downloads at the same time" in Settings).
- Before a download starts, the free space of the drive is checked against what is still to download (plus a 512 MB reserve for Windows); if it does not fit the download stops with a message instead of failing half way. This applies to plain downloads too. A finished download or torrent raises a tray notification.
- **Choosing files**: a torrent with several files has a file-list button (while it is in the engine, i.e. started in this session) that opens a window with a check box per file. Only the checked files are downloaded, counted in the size and progress, and the torrent is finished when they are there. The choice is saved with the download and applied again after a restart; if everything is checked it is stored as "all". The torrent starts with all files and the choice takes effect when you confirm it, so a few pieces of an unwanted file may already have been fetched; files already on disk are not deleted. At least one file must stay checked.
- **Seeding** is off by default. Switch on *Keep seeding finished torrents* in Settings and a finished torrent stays running, shown as "Seeding · ratio 0.42" with the upload speed, until it has uploaded the chosen share ratio (1x, 2x, 3x or no limit; uploaded divided by downloaded, counted while Arqon is open) or you press the stop button. A seeding torrent does not hold a place in the download queue. Uploading still happens while a torrent downloads (that is how the protocol works), whatever the setting.
- **Seeding after a restart:** with seeding on, finished torrents are shared again a few seconds after Arqon starts (and when you switch seeding on), one at a time. A torrent is only started when every wanted file is still on disk in full, so this can never turn into a download: if you deleted or moved a file, the item simply stays "Finished" and nothing is fetched. Checking the files of a big torrent takes a while (the engine's saved resume data makes it quick when nothing changed). The share ratio counts from the start of the session. The engine, and with it the listening port and the firewall question, is started for this only when seeding is on; if no finished torrent is in the list it is created later, when the first torrent is added.
- While the game protection holds a link back for a running game, all torrents together are held to that same speed, **downloading and uploading (seeding)**, so a torrent cannot undo the protection.
- The engine is created when the first torrent is added, not at start-up. It opens a listening port, so Windows may show a **firewall question the first time**.
- UPnP / NAT-PMP port mapping is **off by default**: it changes the router's configuration. Switch it on under *Settings → Open the torrent port on the router (UPnP)*; otherwise incoming connections work only where the network allows them.
- Resume data and the metadata of magnet links live in `%APPDATA%\Arqon\torrent-cache`.
- Removing an unfinished torrent deletes its downloaded files, also after a restart: the torrent is added to the engine once more and removed again, so only files that belong to it go and the rest of the folder is left alone. (A magnet link whose metadata was never saved has no known files, so nothing is deleted for it.)
- `wss://` (WebTorrent) trackers are not supported by MonoTorrent; a torrent that has only those finds no peers unless DHT helps.

## Tested

- Unit tests: input detection, identity by info hash, folder choice, link rotation, the socket connector (each connection really leaves from the next link's address).
- Integration test: a seeder and a downloader engine on loopback; the file arrives intact and the downloader's connection went through a link chosen by Arqon.
- Real run (2026-10-18, Wi-Fi + USB phone, official Debian netinst torrent, speed capped at 1.5 MB/s): 40 peers, 35 seeds; both adapters carried the data (Wi-Fi 24 MB, phone 35 MB in 45 s).
- Integration test: a three-file torrent from a local seeder with one file unchecked: the other two arrive intact, the size counts only them, the third is not downloaded.
- Integration test: a downloader that finished and was left running serves a second downloader after the original seeder is gone (the data arrives intact and the first one counts what it uploaded).
- Integration tests for seeding after a restart: a finished torrent in a new engine is opened and serves a downloader; a torrent whose file was deleted is not opened and nothing is created or downloaded; a multi-file torrent needs only its chosen files to be present.
- Not tested: a full multi-gigabyte download, DHT-only magnet links, the screen driven with real clicks (this includes the file-choice window and the seeding state in the list: they compile and their logic is covered through the engine and unit tests, but they were not operated by hand).
