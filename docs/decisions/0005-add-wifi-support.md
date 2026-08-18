# 0005. Add WiFi support

Date: 2026-08-16
Status: Accepted

## Context

The appliance currently has wired DHCP only. A TP-Link 2.4GHz USB WiFi dongle
(Realtek chipset) is the only practical network path in the target location and
is unsupported out of the box: the minimal image ships no WiFi firmware and no
supplicant. This is greenfield — nothing depends on an existing WiFi stack.

## Decision

WiFi over WPA2-PSK is handled by **`wpa_supplicant`** (the `nl80211` driver)
plus **`systemd-networkd`** for DHCP. systemd-networkd's `[WiFi]` section and
`PreSharedKey=` key do **not** exist; the only WiFi-related keys in a
`.network` file are *match* options (`[Match] SSID=`, `BSSID=`,
`WLANInterfaceType=`) used to select a config for an already-connected
interface. The chosen architecture:

- `wpa_supplicant` does the association (the only thing systemd-networkd
  cannot do). It is supervised by a transient unit regenerated every boot
  from `/data/system/wifi.conf`.
- `firmware-realtek` (from the `non-free-firmware` component) is installed so
  Realtek dongles — including the TP-Link 2.4GHz USB with the in-tree rtw88
  driver — work out of the box. The narrower `firmware-realtek` keeps the
  squashfs slim compared to the Ubuntu `linux-firmware` mega-bundle, which
  has no direct Debian equivalent (Debian's source package `firmware-nonfree`
  builds per-vendor binaries separately).
- A first-boot `dialog` prompt (SSID + password with confirmation) collects
  the credentials, gated on a `wl*` interface existing and a marker file
  being absent. Later boots skip the prompt.
- `/data/system/wifi.conf` (root:root, 0600) is the source of truth. Every
  boot the unit materializes three files into `/run`:
  - `/run/wpa_supplicant/wifi.conf` — supplicant config (network block +
    `key_mgmt=WPA-PSK`).
  - `/run/systemd/system/wpa-wifi.service` — transient unit that runs
    `wpa_supplicant -D nl80211 -i <iface> -c /run/wpa_supplicant/wifi.conf`.
  - `/run/systemd/network/30-wifi.network` — slim networkd config
    (`[Match] Name=wl*` + `[Network] DHCP=yes`); no `[WiFi]` section.
- The service unit owns `TTYPath=/dev/tty1` and runs `plymouth quit` (no
  `--retain-splash`) so the dialog can draw on the primary display after
  Plymouth releases the framebuffer. It also runs `Before=plymouth-quit.service`
  to keep Plymouth alive for the rest of the boot.
- No `retrobox wifi` CLI subcommand. Re-configuration is a manual edit of
  `wifi.conf` plus removal of the `wifi-configured` marker, then a reboot.

## Consequences

- The PSK is stored in plain text on `/data/system/wifi.conf` (no FDE today;
  consistent with the current posture of `/data`).
- `PRESERVE_DATA` already retains `/data` across reinstalls, so credentials
  survive a reinstall.
- `firmware-realtek` is small (the Realtek blobs only) so the squashfs stays
  modest; adding `firmware-iwlwifi` / `firmware-atheros` later is a one-line
  change in `packages.txt` plus the `non-free-firmware` component already
  enabled in `build-usb-installer.sh`.
- `wpasupplicant` adds the association backend systemd-networkd lacks. The
  alternative (iwd) was rejected because its profile store lives at
  `/var/lib/iwd`, which is on the read-only root and would not survive the
  "materialize into `/run`, never write `/etc`" design.
- There is no bash test suite in the repo (no bats-core); the script's packaging
  is verifiable through the installer smoke test.

## Alternatives considered

- iwd: rejected because its profile store lives at /var/lib/iwd (read-only root).
- NetworkManager: heavyweight and breaks the current networkd-only posture.
- First-boot prompt with no re-configuration path: rejected as too fragile.
