# RetroBox Architecture

## Overview

RetroBox is a retro PC appliance: a minimal Debian 13 host that boots into a
fullscreen 86Box virtual machine so it feels like a DOS-era console. Physical
hardware integrates through two channels:

- A **modified floppy drive** — a NodeMCU (ESP8266) reads/writes an NFC tag
  glued inside each floppy shell and reports insert/eject to the host over USB
  serial.
- A **physical CD-ROM** — passed through to the guest via `ioctl://` device
  paths configured at install time.

Everything else (Linux, systemd, the emulator) is an implementation detail the
user should never notice.

## Runtime flow

```text
power on
  -> Plymouth splash
  -> retrobox boot
       F12 pressed? -> terminal VM selector (start / set default / cancel)
       otherwise    -> default VM
  -> 86Box fullscreen (SDL, CRT shader)
       VM exit -> selector returns, so another VM can start without rebooting
  -> 86Box exits -> tty1 login
```

On the first boot with a USB WiFi NIC attached, `retrobox-wifi-firstboot`
prompts for the network credentials on tty1 before `retrobox boot` takes over;
every boot it materializes the wpa_supplicant config and a slim networkd
`/run/systemd/network/30-wifi.network` from `/data/system/wifi.conf`. The
supplicant does the WPA2-PSK association (systemd-networkd has no native
support); `firmware-realtek` (from the `non-free-firmware` component) is
bundled so Realtek USB dongles work without extra packages (see `0005`).

A long-lived daemon (`retrobox daemon`) supervises floppy hardware during the
session:

```text
NodeMCU/PN532 (USB serial, 115200)
  -> INSERT <id>,<mode> | EJECT | ERROR <msg>
  -> RetroBoxDaemon (RetroBox.Daemon)
  -> RetroBoxFloppyControlClient (RetroBox.Core)
  -> 86Box floppy control socket (JSON Lines over Unix socket)
```

When a VM starts, the daemon re-syncs the drive: it asks the firmware for the
current physical floppy (`STATUS` over serial) and applies it as soon as the
86Box socket is ready, so swaps made while the VM was off are loaded on power-on.

## Components

### src/RetroBox.Core (domain)

Flat, file-per-concern `RetroBox*` classes. No external dependencies except
YamlDotNet.

- `RetroBoxConfigStore` — reads/writes `config.yaml`, `vms.yaml`,
  `floppies.yaml` under the catalog root (default `/data/retrobox`), with
  validation and backup-then-restore on failed saves.
- `RetroBoxCatalogModels` / `RetroBoxCatalogValidation` / `RetroBoxCatalogRules`
  — YAML data shapes and ID/mode/size validation rules.
- `RetroBoxBoot` / `RetroBoxBootSelector` / `RetroBoxBootHotkey` — resolve and
  launch the target VM; detect the F12 boot-window hotkey.
- `RetroBoxFloppyControlClient` / `RetroBoxEchoTransportStream` — client for the
  86Box floppy control socket; echo transport for `--echo`.
- `RetroBoxFloppyImporter` — moves scratch images into the catalog and updates
  `floppies.yaml`.
- `RetroBoxArduinoSerialProtocol` — parses `INSERT`/`EJECT`/`ERROR`/`INIT` and
  builds host commands (`WRITE`, `TAGID`, `PING`, `STATUS`); parses controller
  replies (`OK`, `PONG`, `Tag ID: <uid>`, `ERROR <msg>`).
- `RetroBoxNfcClient` / `RetroBoxNfcWriter` — `PING`/`PONG` connectivity and
  tag writing for a cataloged floppy.
- `RetroBoxVmSelection` — list VMs, get/set the default VM.

### src/RetroBox.Cli

System.CommandLine root command (`Program.cs` → `CliCommandFactory.cs`).
Subcommands: `boot`, `daemon`, `vm`, `floppy`, `import`, `nfc`. The factory
accepts injected runners/UIs so tests can drive commands in-process.

### src/RetroBox.Daemon

`RetroBoxDaemon` runs the event loop: it opens the serial device
(`RetroBoxSerialDeviceRunner`), watches the floppy controller, probes for the
86Box socket (`RetroBoxVmSocketProbe`), and applies floppy events through
`RetroBoxFloppyEventHandler`. Since a command reply and a floppy event can
arrive on the same serial line in either order, `RetroBoxSerialLineRouter`
splits the two apart; `RetroBoxSerialNfcCommandChannel` sits on top of it to
serialize controller commands (`WRITE`/`TAGID`/`STATUS`) behind a timeout, so
every caller on the one physical line — today the socket-poll loop, later a
web layer writing tags — shares one gate instead of racing each other on the
wire. `RetroBoxDriveStateTracker` tracks what is currently in the drive from
the events it observes.

### firmware/retrofloppy-esp8266

ESP8266 (NodeMCU v2) Arduino firmware. Talks to a PN532 over I2C (address
`0x24`), detects disk presence by polling the PN532 for the tag itself (no
mechanical switch), and reports events over 115200 baud serial. NFC tags carry raw `<id>,<mode>` bytes in pages
4–11 (32 bytes max), deliberately not NDEF. Vendored PN532 libraries are pinned
in `sketch.yaml`. See the
[firmware README](../firmware/retrofloppy-esp8266/README.md) and
[floppy controller wiring](floppy-controller-wiring.md).

### appliance/

Debian 13 base layout: read-only root, persistent `/data` for VMs, floppies,
catalogs, and snapshots; a single `retrobox` account (root locked, `sudo`
available); a restricted Samba scratch share. The bootable USB installer builds
a live installer rootfs plus the target appliance rootfs and installs offline.
See [`appliance/README.md`](../appliance/README.md) and
[`appliance/installer/README.md`](../appliance/installer/README.md).

## Key contracts

| Contract | Where | Notes |
| --- | --- | --- |
| Arduino serial protocol | `RetroBoxArduinoSerialProtocol` | `INIT`, `INSERT <id>,<mode>`, `EJECT`, `ERROR`; host `WRITE`/`TAGID`/`STATUS`/`PING`. |
| 86Box floppy control socket | `docs/86box-floppy-control-socket-contract.md` | JSON Lines over Unix socket; `floppy.insert/eject/status`. |
| VM hardware source of truth | `docs/vm-profiles.md` | The 86Box `.cfg` defines hardware; YAML is metadata only. |
| CD-ROM passthrough | `docs/cdrom-passthrough.md` | First active optical slot bound to `ioctl://<device>` at install. |

## Data model

The catalog is YAML metadata; it never duplicates emulator hardware state.

```yaml
# config.yaml
defaultVm: pentium100
floppyControlSocketPath: /run/retrobox/86box-floppy.sock
serialPort: /dev/ttyUSB0
serialBaud: 115200
```

```yaml
# vms.yaml
vms:
  pentium100:
    label: "Pentium 100"
    path: "/data/vms/pentium100"
```

```yaml
# floppies.yaml
floppies:
  monkey1-disk1:
    label: "Monkey Island 1 (disk 1)"
    image: /data/floppies/cataloged/monkey1-disk1.img
    mode: ro
    size: 3.5-1.44M
    nfc: true
```

The `nfc` field tracks whether a physical NFC tag has been assigned to this floppy. When `true`, the daemon accepts inserts for this floppy ID. When `false`, the daemon refuses any insert attempt (without touching the 86Box socket) because no tag has been written for this catalog entry yet. Use `retrobox nfc write <id> --port <serial-port>` to write a tag to a cataloged floppy.

## Deployment model

- The `retrobox` binary is published as a **Native AOT Linux x64** single file
  (`mise run publish-linux-x64`) and installed at `/opt/retrobox/retrobox`.
- 86Box runs as `/opt/86Box/86box.AppImage` with ROMs in `/opt/86Box/roms`.
- Systemd units under `appliance/installer/payload/units/` supervise boot and
  the daemon; the daemon unit is gated on the serial device existing.
- Root filesystem is read-only; `/data` is the persistent, writable partition.

## Releases

The USB installer workflow tags releases `appliance-YYYYMMDD-<run>` and
generates release notes from `git log --no-merges <previous-tag>..HEAD`, grouped
by conventional-commit type. See `CONTRIBUTING.md` and the release job in
`.github/workflows/build-usb-installer.yml`.

## Architectural decisions

Recorded in [`docs/decisions/`](decisions/README.md). Notable entries:

- `0001` — publish the CLI as a single Native AOT binary.
- `0002` — appliance uses a read-only root with persistent `/data`.
- `0003` — the 86Box `.cfg` is the hardware source of truth; YAML is metadata.
- `0004` — NFC tags carry raw bytes, not NDEF.
- `0005` — appliance supports WiFi via `wpa_supplicant` + systemd-networkd (DHCP) and a first-boot dialog.
