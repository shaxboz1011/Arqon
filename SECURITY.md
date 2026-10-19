# Security policy

Arqon changes how Windows routes network traffic (and, in system mode, loads a packet driver), so security reports are taken seriously.

## Reporting a vulnerability

Please **do not** open a public issue for a vulnerability. Use GitHub's private reporting instead:
**Security → Report a vulnerability** on this repository. Describe what you found, the Windows version, and how to reproduce it. You will get an answer as soon as possible, and a fix is released before details are made public.

## What counts

- Anything that lets another program or user take over Arqon, its settings, or the packet driver it loads.
- Bugs in system mode that can leave a machine without internet, or route traffic where it should not go.
- Unsafe handling of downloaded files, torrent data, or the command line / inbox hand-over between two copies of Arqon.

## Supported versions

Only the latest release is supported.

## Verifying a download

Every release has `Arqon.exe.sha256`. `Arqon.exe` is not code-signed yet, so Windows SmartScreen warns; compare the hash, or build it yourself with `tools\build.ps1`.
