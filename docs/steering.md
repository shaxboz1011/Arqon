# System mode (steering): status and design

**Status: decision logic done and tested; the packet driver is not connected yet. System mode stays unavailable in the app.**

## What exists

- `FlowKey` / `FlowTable`: remembers which link each connection got. A connection never changes link mid-way. Idle TCP flows expire after 60 s, UDP after 30 s.
- `SteeringCoordinator.PickLink(flow, exe)`: game apps go to the best link (lowest latency, jitter, loss, with hysteresis); "Only X" rules pin to X and fall back to the best link if X is missing; everything else is spread across links in proportion to measured speed (smooth weighted round-robin). No measurements means `null`, i.e. Windows routes as usual.
- `ISteerer`: the seam where a packet driver plugs in. Any failure raises `Faulted`, and the coordinator stops steering so Windows routing takes over again (fail-open).

## What is missing

A `WinDivertSteerer` that implements `ISteerer`:

1. Open WinDivert 2.2.x (LGPLv3, signed driver, administrator rights) on outbound TCP/UDP.
2. For each new flow, resolve the owning process (`GetExtendedTcpTable` / `GetExtendedUdpTable`) and call `PickLink`.
3. Rewrite the source address to the chosen link's address and inject the packet on that link's interface index; for inbound packets, look the flow up by destination and rewrite back.
4. Fail open on any error, and release everything on `Stop()`.

This could not be verified on the build machine (only one link). It must be tested with Wi-Fi plus a USB-tethered phone before being switched on. WinDivert binaries are not bundled; the licence and signing terms must be reviewed before shipping them.

## Why per-flow and not per-packet

One TCP connection cannot be split over two links without a relay on the other side. Per-flow steering therefore speeds up applications that open many connections (browsers, Telegram, Steam), and keeps game traffic on the best link. A relay server (single-connection bonding, duplicated game packets) is a planned later step.
