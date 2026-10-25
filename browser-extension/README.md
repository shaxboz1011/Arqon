# Arqon browser extension

Adds **Download with Arqon** to the right-click menu of every link, and can optionally send every download the browser starts to Arqon. Arqon then fetches it over all your connections at once.

It works with Chrome, Edge, Brave and other Chromium browsers, and Firefox 128 or newer.

## How it works

The extension opens an `arqon://add?url=…` link. Windows starts Arqon with it (or hands it to the Arqon that is already running), the same way it opens a magnet link. Arqon accepts only web (`http`, `https`) and magnet links this way, never a file on your computer. The first time, the browser asks whether to open Arqon; tick *Always allow* to skip that question.

Before the first use, switch on **Settings → Open torrents and magnet links with Arqon** in Arqon. That registers the `arqon://` scheme for the current user (no administrator rights), and switching it off removes it again.

## Install

The extension is not in the stores yet. Load it from this folder (or from `Arqon-browser-extension.zip` on the release page, unpacked):

- **Chrome / Edge / Brave:** open `chrome://extensions` (`edge://extensions`), turn on *Developer mode*, click *Load unpacked* and pick this folder.
- **Firefox:** open `about:debugging#/runtime/this-firefox`, click *Load Temporary Add-on…* and pick `manifest.json` (it stays until Firefox restarts, until the add-on is signed by Mozilla).

## Options

*Send every download to Arqon* (off by default) cancels the browser's own download and gives the link to Arqon. Sites that only serve a file to a logged-in browser may refuse Arqon; use the right-click menu for those or leave the option off. `blob:` and `data:` downloads always stay in the browser.

Interface languages: English, Uzbek, Russian (`_locales/`).
