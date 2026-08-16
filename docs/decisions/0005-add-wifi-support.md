# 0005. Add WiFi support

Date: 2026-08-16
Status: Accepted

## Context

The appliance currently has wired DHCP only. A TP-Link 2.4GHz USB WiFi dongle
(Realtek chipset) is the only practical network path in the target location and
is unsupported out of the box: the minimal image ships no WiFi firmware and no
supplicant. This is greenfield — nothing depends on an existing WiFi stack.

## Decision

WiFi is handled by **systemd-networkd v256's native `[WiFi]` section** for
WPA2-PSK; there is no `wpa_supplicant`. The PSK is written as
`PreSharedKey=<plain>` into the materialized `.network` file.

- `linux-firmware` (the mega-bundle) is installed so Realtek, Atheros, and other
  chipsets work without per-vendor package selection.
- A first-boot `dialog` prompt (SSID + password with confirmation) collects the
  credentials, gated on a `wl*` interface existing and a marker file being
  absent. Later boots skip the prompt.
- `/data/system/wifi.conf` (root:root, 0600) is the source of truth. Every boot
  the unit materializes `/run/systemd/network/30-wifi.network` from it (tmpfs,
  regenerated each boot), dodging the read-only root — nothing is written to
  `/etc` at runtime.
- There is no `retrobox wifi` CLI subcommand. Re-configuration is a manual edit
  of `wifi.conf` plus removal of the `wifi-configured` marker, then a reboot.

## Consequences

- The PSK is stored in plain text on `/data/system/wifi.conf` (no FDE today;
  consistent with the current posture of `/data`).
- `PRESERVE_DATA` already retains `/data` across reinstalls, so credentials
  survive a reinstall.
- `linux-firmware` inflates the squashfs by roughly 600MB; splitting the bundle
  per chipset is future work.
- There is no bash test suite in the repo (no bats-core); the script's packaging
  is verifiable through the installer smoke test.

## Alternatives considered

- `wpa_supplicant`: more battle-tested, but an extra daemon and config layer
  when systemd-networkd v256 covers WPA2-PSK natively.
- NetworkManager: heavyweight and breaks the current networkd-only posture.
- First-boot prompt with no re-configuration path: rejected as too fragile.
