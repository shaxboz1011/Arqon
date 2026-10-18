# System mode (steering)

**Status: implemented and unit-tested; not yet verified on real hardware with two live links.** It is off by default, needs the WinDivert driver and administrator rights, and fails open: if anything goes wrong, Windows routes normally again.

## What it does

With system mode on, every application benefits, not only Arqon's own download manager:

- Each new TCP connection or UDP flow is given to one link, in proportion to the links' measured speeds. A browser or Telegram that opens many connections ends up using both networks.
- Games (the built-in catalogue plus apps you mark as games) stay on the steadiest link, with 20 % hysteresis so they do not flip between links.
- "Only X" rules pin an app to one link and fall back to the best link when X is missing.
- A connection never moves once it has started, because moving it would break it.

One TCP connection cannot be split across two links without a relay on the other side. Per-connection steering helps applications with several connections; a relay server (single-connection bonding) is a planned later step.

## How it works

```
app ── packet (src = default address A) ──► WinDivert (network layer, outbound)
                                              │
                                  NatSteeringEngine.ProcessOutbound
                                  new flow? ─► process lookup ─► SteeringCoordinator.PickLink
                                              │
                          chosen link B ≠ A ─► rewrite src to B, fix checksums, send
                                              ▼
                                         link B's network
reply ── dst = B ──► WinDivert (inbound) ─► ProcessInbound ─► rewrite dst back to A ─► app
```

| Part | Role | Tested without the driver |
|---|---|---|
| `PacketView` | Parses IPv4 TCP/UDP, rewrites addresses, recomputes checksums. Fragments and malformed data are passed through untouched. | yes |
| `NatSteeringEngine` | Per-flow decision, NAT tables, bypass rules, statistics, quarantine of a link that does not answer. | yes |
| `SteeringCoordinator` | Which link a new flow gets (game, pinned, weighted round-robin). | yes |
| `WindowsProcessResolver` | Local port to process (IP Helper tables), and "is this address:port already taken". | yes |
| `SteeringService` | Lifecycle and state for the UI; turns every failure into a state. | yes |
| `WinDivertFilter` | Builds the driver filter so only the needed packets reach user mode. | yes |
| `WinDivertSteerer` + `WinDivertNative` | The actual driver calls. Thin on purpose. | **no (needs the driver)** |

### What is never touched

- Destinations that are private, loopback, link-local, CGNAT, multicast or broadcast.
- IPv6 (passes through unchanged; a dual-stack site may still use IPv6 on the default link).
- TCP connections that were already running when system mode was switched on.
- Arqon's own traffic (the download manager binds to links itself).
- Packets whose source is not the default address.

### Safety

- The driver diverts packets only while Arqon's handle is open. Closing Arqon, switching the mode off, a crash or killing the process all return traffic to normal at once.
- Any driver error, 200 consecutive send failures or 100 engine exceptions stop steering and show the reason in the app.
- If a link does not answer connections that were steered to it (4 or more unanswered in 4 s, none answered recently), it is taken out of rotation for 60 s and its stuck connections are decided again. This is also the safety net if Windows does not route the rewritten packets the way we expect: the phone is simply paused and nothing breaks.
- A connection is not moved to a link whose address already has a socket on the same local port (that would mix its replies with that socket's traffic).

## Enabling it

1. Install the driver files next to `Arqon.exe` (they are not in this repository; WinDivert is LGPLv3 and the driver is signed by its author):

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\get-windivert.ps1 -Destination src\Arqon.App\bin\Debug\net10.0-windows
   ```

   The script downloads the official release, prints its SHA-256, and refuses to install unless Windows reports a valid signature on the driver.
2. Start Arqon as administrator (Settings has a "Restart as administrator" button).
3. Switch on **System mode** in Settings. The Dashboard then shows how many connections each link received.

If something looks wrong, switch the mode off or close Arqon. If the network still misbehaves (it should not), restart the `WinDivert` service or reboot.

## How to test it (needs Wi-Fi plus a USB-tethered phone)

1. Start Arqon, check the Dashboard shows both links with sensible ping.
2. Enable system mode. Open a few sites, start a speed test in the browser, and watch the Dashboard: both links should show traffic, and the "System mode" card should count connections for each.
3. Play a game or run a ping to a game server: it should stay on the better link, and downloads on that link should be capped when ping rises (Dashboard, game card).
4. Pull the phone's cable: after a few seconds everything should continue on Wi-Fi.

If the phone never receives traffic, or the Dashboard shows it as "paused: no answers", Windows probably does not route the rewritten packets out of the phone's interface. Things to try, all unverified: `netsh interface ipv4 set interface "<phone adapter>" weakhostsend=enabled weakhostreceive=enabled`, then the same with `weakhostsend=disabled` on every adapter. Please open an issue with the Dashboard numbers and the output of `route print`.

## First hardware test (2026-10-18, Wi-Fi + USB phone)

- Found and fixed: new flows were weighted by each link's *current* speed, so an idle link was never chosen and therefore never measured. Weights now use a fading peak speed plus a 25 % exploration floor; links whose probes all fail get no new flows.
- Verified on the wire (pktmon): the driver installs, system mode starts, and steered connections complete normally.
- **Not verified:** sending a connection out of the *non-default* link. During the test the phone had no internet (Windows reported `LocalNetwork`), so no rewritten connection could complete; Windows had also made Wi-Fi the default address because of that. The quarantine caught it: stalled connections were retried on the other link after about 4 s.
- Needs a repeat with a phone that really has internet, ideally once with each link as the default.

## Known limits

- Per connection, not per packet (see above).
- Inbound packets for every non-default link pass through user mode, including Arqon's own downloads on that link.
- A port clash is checked when a connection is created; a socket opened later on the target link with the same port and the same remote endpoint could still be confused with it (very unlikely).
- Not verified on real hardware yet.
