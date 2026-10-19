# Arqon for Android (preview)

Uses **Wi-Fi and the SIM cards at the same time** for downloads and torrents, keeps a game's ping steady while it downloads, shows
how much data you use, and has a browser that shows websites as a computer does (some sites hide download links from phones).

Written in Kotlin. `core/` is a plain JVM library with no Android code (tested on a computer): range downloader over several
links, bencode, torrent files, magnet links, trackers (HTTP and UDP), the BitTorrent peer protocol, a torrent engine, the packet
code of the game mode, and the data-usage maths. `app/` is the Android app (Jetpack Compose): it asks Android for each network and
gives the core one socket factory per network.

## Try it

Install `app-debug.apk` (build it with `./gradlew :app:assembleDebug`, or take the artifact of the `android` workflow), allow
"install unknown apps", open Arqon and look at the **Panel** tab: it lists every network Android gave to the app.

## What it does

| Tab | What |
|---|---|
| **Panel** | Every network Android gave to Arqon (Wi-Fi, each SIM) with speed and ping, and the **speed test**: each connection alone, all together, and the measured gain. |
| **Browser** | A browser in desktop mode. Downloads and `.torrent` / magnet links go to Arqon. |
| **Downloads** | HTTP files and torrents over every connection at once; choice of files; optional sharing after the download. |
| **Usage** | Speed now, signal and network type (5G / 4G / 3G / 2G), data used today and this month over Wi-Fi and over mobile data, the last 7 days, the apps that used the most (with "usage access"), a daily mobile data limit, and shortcuts to the phone's own network-mode screens. A **speed meter notification** shows the speed on the status-bar icon and today's use in the shade. |
| **Game** | The apps you tick go through a VPN tunnel over the best connection while Arqon's downloads keep off it. |
| **Settings** | Battery (so phones such as Samsung do not put downloads to sleep), notifications, the label over other apps, download folder, anonymous statistics (off unless you allow). |

## What is known and what is not

- Wi-Fi + mobile data together: uses Android's `requestNetwork`, the documented way. Works on the two phones tried (Samsung
  S24 Ultra, Infinix). On the Infinix the developer option *Mobile data always active* is needed for the SIM to stay up next to Wi-Fi.
- Both SIM cards at once: asks for each SIM with `TelephonyNetworkSpecifier` (Android 12+, needs the phone-state permission to
  list the SIMs). **Whether the second SIM answers depends on the phone**: many phones, including the S24 Ultra, can use only one
  SIM for data at a time. The Panel shows which ones answered and never pretends.
- The torrent engine was tested with local seeders and against a real swarm (a Debian torrent over Wi-Fi on a computer), and the
  app on two phones. The browser, the background service and the overlay have had little testing.
- **Game mode**: `core/vpn` re-creates every TCP connection and UDP conversation of the chosen apps as an ordinary socket on the
  best link (shortest answer time; mobile data only when it is clearly better), and Arqon's own downloads keep to the other links
  while a game sends data. This keeps a download from filling the connection a game plays on. It does **not** add speed to a game
  and there is no server of ours in the path. With only one link, downloads share it. The packet code is tested against real
  sockets; on a phone it has not been tried with real games yet. IPv4 only; pings (ICMP) are not carried.
- **Network mode**: Android does not let an app switch between 5G, 4G, 3G and 2G (only the phone's own screens can). Arqon shows
  the current type and opens those screens.
- **Data usage**: exact numbers and the per-app list come from Android's own counters and need "usage access" (a special
  permission the user gives in Android's settings). Without it Arqon counts what it sees itself while it runs.
- **Statistics**: see [../docs/privacy.md](../docs/privacy.md). The open-source build has no server and sends nothing.
- Not done yet: DHT, incoming connections, a language picker inside the app (it follows the phone's language: English, Uzbek,
  Russian), a signed release APK.

## Build

JDK 21 and the Android SDK (platform 35). `./gradlew :core:test` runs the tests, `./gradlew :app:assembleDebug` builds the APK.

## Releasing

A release APK must always be signed with the same key, or phones refuse to update it. Make the key once and keep it safe (lose it and
nobody can update):

```
keytool -genkeypair -v -keystore arqon.jks -alias arqon -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 arqon.jks            # the text to store as ARQON_KEYSTORE_BASE64
```

In the repository settings (Secrets and variables, Actions) add `ARQON_KEYSTORE_BASE64`, `ARQON_KEYSTORE_PASSWORD`, `ARQON_KEY_ALIAS`
and `ARQON_KEY_PASSWORD`. Then push a tag such as `android-v0.2.0`: the `android-release` workflow runs the tests, builds the signed
`Arqon.apk` and publishes it with its SHA-256 as a pre-release. A local `./gradlew :app:assembleRelease` without these variables gives an
unsigned APK.
