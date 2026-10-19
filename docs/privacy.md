# Privacy

Arqon is built so that you do not have to trust it: it has no account, no ads and no tracking, and everything below can be checked in the source.

## Windows

- **Nothing about you is sent anywhere.** Arqon connects to what you ask it to download, plus:
  - `1.1.1.1` / `8.8.8.8` on port 443, only to measure the ping of each connection;
  - public torrent trackers and the DHT, when you use torrents (as every torrent client does);
  - **GitHub**, twice a day, with one request to `api.github.com/repos/shaxboz1011/Arqon/releases/latest`, to see whether a newer version exists. Only the standard request headers go with it. Switch it off in *Settings → Check for updates*.
- Usage statistics (how much each connection carried) are kept in `%APPDATA%\Arqon\usage.json` on your computer. *Usage → Export CSV* saves them to a file you choose. They are never uploaded.

## Android

- Data usage, the speed meter and the signal are read on the phone and stay on the phone.
- The **anonymous statistics** are off unless you say yes in the question the app asks once, and the open-source build of Arqon has **no server address**, so it cannot send anything at all (*Settings → Anonymous statistics* says so). A build made with `-PstatsUrl=https://…` can send one report a day.
- If it is on, the report is exactly this (the app shows it to you in *Settings → Anonymous statistics → Show what would be sent*):

  ```json
  {"id":"<random number made on first use>","app":"0.2.0","platform":"android","os":"15 (API 35)",
   "device":"samsung SM-S928B","lang":"uz","day":"2026-10-19",
   "connections":["sim:active","wifi:active"],
   "features":{"download_torrent":1,"game_mode":1,"speed_test":2}}
  ```

  - `id` is a random number that stands for one installation. It is not your advertising ID, phone number, SIM or account, and clearing the app's data makes a new one.
  - `connections` says which connections Android gave to apps (Wi-Fi, SIM, whether a SIM was refused), so we learn which phones can combine them.
  - `features` counts how often each feature was used that day.
  - **Never sent:** links, file or torrent names, websites, location, phone numbers, contacts, which apps you use, or anything you download.
- The report is built by one small function ([`StatsReport.kt`](../android/core/src/main/kotlin/app/arqon/core/stats/StatsReport.kt)) with a test that pins its exact fields.
- *Game mode* uses Android's VPN interface only to receive the chosen apps' packets and send them over the best network. The packets go straight to the game's server from your phone; there is no server of ours in between.

## Running your own statistics server

Set up any HTTPS endpoint that accepts the JSON above with `POST`, then build the app with `./gradlew :app:assembleRelease -PstatsUrl=https://your.server/report`. Keep the data you store to what is listed here, do not log the sender's IP address, and say so in your own privacy notice.
