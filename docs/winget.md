# Arqon in winget

Arqon is one self-contained exe, so in [winget](https://learn.microsoft.com/windows/package-manager/) it is a **portable** package: winget downloads `Arqon.exe` from the GitHub release, checks its SHA-256 and puts an `arqon` command on the PATH.

```powershell
winget install shaxboz1011.Arqon     # once the package is accepted into microsoft/winget-pkgs
winget upgrade shaxboz1011.Arqon
```

## How a release reaches winget

1. A tag such as `v0.3.0` starts [`release.yml`](../.github/workflows/release.yml). It builds `Arqon.exe`, and [`tools/winget-manifest.ps1`](../tools/winget-manifest.ps1) writes the three manifests (`shaxboz1011.Arqon.yaml`, `.installer.yaml`, `.locale.en-US.yaml`) with the exe's SHA-256. They are attached to the release.
2. If the repository has the secret **`WINGET_TOKEN`**, the workflow runs `wingetcreate submit` and opens a pull request in [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Microsoft's bots validate it and a moderator merges it, usually within a few days.
3. Without the secret nothing fails: download the three `.yaml` files from the release and submit them by hand (below).

### The token

Create a **classic** personal access token with only the `public_repo` scope (GitHub → Settings → Developer settings → Personal access tokens). Save it in this repository as *Settings → Secrets and variables → Actions → New repository secret*, name `WINGET_TOKEN`. wingetcreate uses it to fork winget-pkgs under your account and open the pull request.

### By hand

```powershell
powershell -ExecutionPolicy Bypass -File tools\winget-manifest.ps1 -Exe publish\win-x64\Arqon.exe -Output publish\winget
winget validate publish\winget            # optional check
winget install --manifest publish\winget  # optional local test (needs: winget settings --enable LocalManifestFiles)
wingetcreate submit --token <token> publish\winget
```

## Notes

- The installer URL is `https://github.com/shaxboz1011/Arqon/releases/download/v<version>/Arqon.exe`, so the tag and the version in `Directory.Build.props` must match.
- Arqon's own *Update now* still works for a winget copy; winget then shows the old version until `winget upgrade` runs. Both end with the same file.
- The first submission is reviewed more closely. An unsigned exe is accepted, but a SmartScreen or antivirus warning can slow it down; code signing (see the [roadmap](../ROADMAP.md)) helps.
