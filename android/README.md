# Arqon for Android (work in progress)

Uses **Wi-Fi and both SIM cards at the same time** for downloads and torrents, and has a browser that shows websites as a
computer does (some sites hide download links from phones).

Written in Kotlin. `core/` is a plain JVM library with no Android code (tested on a computer): range downloader over several
links, bencode, torrent files, magnet links, trackers (HTTP and UDP), the BitTorrent peer protocol and a torrent engine.
`app/` is the Android app (Jetpack Compose): it asks Android for each network and gives the core one socket factory per network.

## Try it

Install `app-debug.apk` (build it with `./gradlew :app:assembleDebug`, or take the artifact of the `android` workflow), allow
"install unknown apps", open Arqon and look at the **Panel** tab: it lists every network Android gave to the app.

## What is known and what is not

- Wi-Fi + mobile data together: uses Android's `requestNetwork`, the documented way. Expected to work on Android 8+.
- Both SIM cards at once: asks for each SIM with `TelephonyNetworkSpecifier` (Android 12+, needs the phone-state permission to
  list the SIMs). **Whether the second SIM answers depends on the phone**: many phones can use only one SIM for data at a time.
  The Panel shows which ones answered and never pretends.
- The torrent engine was tested with local seeders and against a real swarm (a Debian torrent over Wi-Fi on a computer).
  Dual-SIM, the browser, the screens and the background service have **not yet been tested on a phone**.
- Speed test on the Panel: every connection alone, all together, and the measured gain.
- A notification (and an optional label over other apps, games included) says which connections are combined. It does not
  change a game's own traffic: that still uses one connection until the game mode below exists.
- Not done yet: game mode (steering game traffic over the best link needs a VpnService), DHT, incoming connections, a language
  picker inside the app (it follows the phone's language: English, Uzbek, Russian).

## Build

JDK 21 and the Android SDK (platform 35). `./gradlew :core:test` runs the tests, `./gradlew :app:assembleDebug` builds the APK.
