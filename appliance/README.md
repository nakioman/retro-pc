# Retro PC Debian Appliance Base

This directory documents the base operating-system layout for the Retro PC
appliance. The target is Debian 13 (trixie), installed as a minimal physical
system and extended with the runtime packages listed in
[`debian/packages.txt`](debian/packages.txt).

The official `debian:13-slim` image is used as a reference for keeping the
package set small. It is not the appliance image: a container does not model
the physical machine's systemd boot, audio, USB/serial, CD-ROM, input, or
graphics devices. The final deployment is a normal Debian installation on the
appliance hardware.

## Runtime shape

The normal user experience is 86Box in fullscreen. Linux remains an
implementation detail and does not provide a desktop environment. 86Box is
deployed as `/opt/86Box/86box.AppImage` with ROMs in `/opt/86Box/roms`, while
`/opt/retrobox/retrobox` is the self-contained Native AOT Linux x64 binary
produced by the repository's publish task. Versions and checksums are pinned in
[`86box.env`](86box.env).
This document defines the base layout. The bootable USB installer that turns it
into an installed, read-only-root system on the appliance disk lives under
[`installer/`](installer/README.md); the installer is the authoritative source
for the systemd units, Samba share, read-only-root enforcement, and account
setup described below until the standalone child issues (#28, #29, #30) land.

## Machine selector

Press F12 during the boot window to open the plain-text machine selector. Press
the VM's displayed number to start it, or press `D` and then a number to set
that VM as the default and start it immediately. `Esc` cancels; when a default
already exists, cancellation starts that default.

When a VM is closed, the selector returns so another VM can be started without
rebooting the appliance. `Esc` on that returned selector ends the session and
returns to the tty1 login; it does not restart the previous VM.

The Plymouth boot splash stays on screen until 86Box's first frame: `retrobox
boot` retains it right before launching a VM (so no boot or 86Box loading text
is visible) and quits it whenever the terminal selector is shown.

The floppy daemon re-syncs the drive whenever a VM starts: once the 86Box
floppy-control socket is ready it asks the floppy controller for the current
physical floppy (`STATUS` over serial) and applies it, so floppy swaps made
while the VM was off are loaded when it powers on.

## Web panel

The appliance hosts an unauthenticated floppy management panel on the LAN at
`http://<appliance>:8080`. The panel lists, uploads, renames, re-modes, and
deletes cataloged floppies; it runs whether or not the floppy controller is
attached. Disable it by setting `WEB_PORT=0` in `/etc/retrobox/daemon.env`.

## Accounts and permissions

The appliance uses a single account, `retrobox`.

`root` is locked (`passwd -l root`): there is no interactive root login on the
console or over SSH. `retrobox` is both the service runtime user (it owns the
application state under `/data/retrobox`) and the maintenance login. It is a
member of the `sudo` and `gpio` groups. The installer creates the `gpio` group
and installs a udev rule granting `retrobox` access to GPIO chip devices.
There is no separate administrator account.

The `retrobox` password is set during installation (the installer prompts for
it) and is required for SSH login and `sudo`. Maintenance is performed over SSH
as `retrobox`; `PermitRootLogin` is disabled.

Samba exposes only the floppy import drop directory:

```text
/data/floppies/scratch/
```

The Samba share must not expose the complete `/data` tree. Imported images are
moved by `retrobox` into `/data/floppies/cataloged/`; cataloged images,
configuration, VM disks, and snapshots are not general-purpose network shares.

## WiFi

The appliance detects a `wl*` interface at boot (`retrobox-wifi-firstboot`).
With no WiFi NIC, wired DHCP is the only network path. When a WiFi NIC is
present the first boot prompts for the SSID and password on tty1 with
`dialog`, persists them to `/data/system/wifi.conf` (root:root, 0600), and
materializes wpa_supplicant + systemd-networkd config into `/run` so the
read-only root is never written at runtime. The prompt runs once; later
boots reconnect from the saved credentials.

Reconfiguring WiFi is manual: edit `/data/system/wifi.conf` and remove
`/data/system/wifi-configured`, then reboot. `firmware-realtek` is bundled
(from the `non-free-firmware` component), so Realtek USB dongles — including
the TP-Link 2.4GHz and similar rtw88/rtl8188/rtl8192 devices — work out of
the box without extra packages. `wpasupplicant` is the association backend
because systemd-networkd has no native `[WiFi]` support.

## Persistent and immutable state

The intended appliance model is a read-only root filesystem with persistent
application state below `/data`. This document records the target contract; it
does not implement the mounts or the required tmpfs/overlay configuration.

Persistent application data lives in the layout described in
[`filesystem-layout.md`](filesystem-layout.md). Runtime-generated state such as
`/run/retrobox/86box-floppy.sock`, logs, PID files, and temporary files must be
handled by the eventual systemd/read-only-root design rather than written into
the immutable application tree.

## Maintenance checklist

An administrator connecting over SSH should be able to:

1. Inspect `retrobox` YAML catalogs under `/data/retrobox`.
2. Copy floppy images into `/data/floppies/scratch` through the restricted
   Samba share or an approved SSH transfer.
3. Run the published `/opt/retrobox/retrobox` CLI for catalog/import maintenance.
4. Inspect VM directories and snapshots without changing the base OS.
5. Collect service and hardware diagnostics once the systemd units exist.

No DOS, Windows, drivers, games, floppy images, or other copyrighted media are
included by this base layout.
