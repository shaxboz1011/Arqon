# Local API

Scripts and other programs on the same computer can talk to a running Arqon over HTTP. The API is **off by default**: switch it on under *Settings → Local API for scripts*.

- Address: `http://127.0.0.1:47011`. It listens on the loopback address only, so other computers cannot reach it.
- Every request needs the header `Authorization: Bearer <token>`. *Settings → Copy token* copies the token. It is stored in `%APPDATA%\Arqon\api-token`; delete that file to get a new one the next time the API starts.
- Answers are JSON. A request without the right token gets `401` and nothing happens.

A web page cannot use the API. A browser does not send an `Authorization` header to another origin without asking the server first, and Arqon never answers that question.

## Endpoints

| Method and path | What it does |
|---|---|
| `GET /api/status` | Version, combined speed and every connection: `{ version, downBps, upBps, links: [{ id, name, kind, pingMs, downBps, upBps }] }`. Speeds are in bytes per second. |
| `GET /api/downloads` | Every download: `[{ name, state, percent, speedBps }]`. `state` is `Downloading`, `Queued`, `Paused`, `Done` or `Failed`. |
| `POST /api/downloads` | Adds a download. The body is `{ "url": "https://…" }`, and a magnet link works too. Answers `202`. Paths on this computer and other schemes are refused with `400`. |

## Examples

PowerShell:

```powershell
$h = @{ Authorization = "Bearer $(Get-Content $env:APPDATA\Arqon\api-token)" }
Invoke-RestMethod http://127.0.0.1:47011/api/status -Headers $h
Invoke-RestMethod http://127.0.0.1:47011/api/downloads -Method Post -Headers $h -ContentType application/json `
    -Body '{"url":"https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/SHA256SUMS"}'
```

curl:

```sh
curl -H "Authorization: Bearer $(cat "$APPDATA/Arqon/api-token")" http://127.0.0.1:47011/api/downloads
```

## From the command line without the API

`Arqon.exe "<link>"` adds a link to the running Arqon (or starts it). This works for web links, magnet links, `.torrent` files and `arqon://add?url=…` links.
