# Hardware detection contract

The installer detects appliance-specific hardware while installing to the target
disk and records what it found (or did not find) in the install report at
`/data/retrobox/install-report.txt`. Detection never aborts the install: when a
device is absent, the installer writes a clearly marked placeholder and a
`status=NOT_DETECTED` line so a technician can fix it later without reinstalling.

Implemented by [`lib/hardware-detect.sh`](lib/hardware-detect.sh).

## Target disks

- Enumerated with `lsblk -J -o NAME,TYPE,SIZE,MODEL,TRAN` (type `disk` only).
- The live USB installer device is **excluded by default** — its parent block
  device is resolved from the live medium mount (`/run/live/medium`) and removed
  from the candidate list. See [`lib/disk-select.sh`](lib/disk-select.sh).
- Removable/USB-transport disks are de-prioritised but still listed, so an
  install to a USB SSD is possible with explicit confirmation.
- The chosen disk is referenced by `/dev/disk/by-id/*` where available and by
  filesystem **UUID** in `/etc/fstab` — never by a transient `/dev/sdX` name.

## CD-ROM

Probed in this order; the first stable match wins:

1. `/dev/disk/by-id/*` entries whose name contains `cd`/`dvd` (preferred, stable).
2. `/dev/sr0`.
3. `/dev/cdrom` symlink.

When a drive is detected, the installer updates every installed
`/data/vms/**/86box.cfg` that has an active optical slot. It selects the
lowest-numbered enabled `cdrom_XX_parameters` entry whose bus is not `none`,
then writes `cdrom_XX_image_path = ioctl://<detected-device>`. Other optical
slots are left untouched. A malformed or unreadable profile is warned about and
skipped without failing installation.

When no drive is found, installed profiles are unchanged. `/dev/sr0` remains
only the diagnostic placeholder in `hardware.env` and the install report;
it is never written into a profile. Payload profile templates are likewise
host-neutral. Follow [the physical CD-ROM validation procedure](../../docs/cdrom-passthrough.md)
on the target appliance.

## ESP8266 / NodeMCU floppy controller (serial)

Probed in this order:

1. `/dev/serial/by-id/*` (preferred, stable across reboots and USB ports).
2. Fallback: `/dev/ttyUSB*` then `/dev/ttyACM*` — **only** used when no
   `by-id` link exists, and recorded as a fallback in the report because these
   names are not stable.

The resolved path and baud rate are written to the RetroBox daemon config. When
no serial device is present the daemon config keeps a placeholder
(`serial_device=/dev/ttyUSB0`, `baud=115200`) and the report records
`serial.status=NOT_DETECTED`. Electronics validation is out of scope (tracked in
#22 / #35); the installer only records/consumes the path and baud.

## HDMI audio

The installer checks ALSA's ELD data under `/proc/asound` for a connected HDMI
monitor and selects the corresponding HDMI PCM. It writes a per-user
`~/.asoundrc` for `retrobox` using the stable ALSA card id and detected PCM,
for example `plughw:CARD=MID,DEV=3`; it does not assume that the card is always
numbered `0` or that HDMI is always device `3`.

If no connected HDMI endpoint is visible during installation, the installer
leaves ALSA's normal default unchanged and records `audio.status=NOT_DETECTED`.
The target can then be reconfigured after connecting the display.

## Install report format

`/data/retrobox/install-report.txt` is a simple `key=value` text file, for
example:

```text
generated=install-retropc.sh
target.disk=/dev/sda
target.disk.by_id=/dev/disk/by-id/ata-CT500MX500SSD1_...
target.root.uuid=...
target.data.uuid=...
cdrom.device=/dev/sr0
cdrom.status=DETECTED
serial.device=/dev/serial/by-id/usb-1a86_USB_Serial-if00-port0
serial.baud=115200
serial.status=DETECTED
audio.device=plughw:CARD=MID,DEV=3
audio.status=DETECTED
retrobox.binary=installed
box86.appimage=installed
```
