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

QEMU profiles are not edited: `/etc/retrobox/hardware.env` records
`CDROM_DEVICE` and `CDROM_STATUS`, and `retrobox boot` attaches the device to a
QEMU VM (`-drive file=<device>,if=ide,index=2,media=cdrom,format=raw`) only
when the status is `DETECTED` and the device still exists at launch; the
report's `cdrom.qemu` says whether it will (`ATTACHED` or `NONE`). Disc
changes reach a running QEMU VM through `90-retrobox-cdrom.rules` and
`retrobox-cdrom-refresh@.service`, which act only on this drive (see
[the CD-ROM doc](../../docs/cdrom-passthrough.md#disc-changes)).

When no drive is found, installed profiles are unchanged. `/dev/sr0` remains
only the diagnostic placeholder in `hardware.env` (with
`CDROM_STATUS=NOT_DETECTED`) and the install report; it is never written into a
profile or attached to a VM. Payload profile templates are likewise
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

Audio is configured at boot by `retrobox-audio-setup.service`, after the
runtime ALSA devices and connected HDMI endpoints are available. The installer
does not detect or configure audio.

## QEMU host support (KVM)

Implemented by [`lib/host-checks.sh`](lib/host-checks.sh), after the services
are installed. Each check is reported and warned about, never fatal: 86Box VMs
need none of them.

- **VT-x/AMD-V**: `vmx` (or `svm`) in the live system's `/proc/cpuinfo`. The
  installer runs on the appliance's own hardware, and the kernel hides the
  flag when the firmware has virtualization disabled.
- **`/dev/kvm`** exists on the live system (the KVM module loaded).
- **VMware backdoor**: the target's `/etc/modprobe.d/retrobox-kvm.conf` sets
  `options kvm enable_vmware_backdoor=1`; without it the XP guest's VMware
  SVGA driver disables Direct3D.
- **Groups**: `retrobox` is in `kvm` (opens `/dev/kvm`) and `render` (GPU for
  Vulkan/3D).

`kvm.status=UNAVAILABLE` usually means VT-x is off in the firmware setup;
enable it and the installed system picks it up on the next boot (the report is
not regenerated).

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
cdrom.qemu=ATTACHED
serial.device=/dev/serial/by-id/usb-1a86_USB_Serial-if00-port0
serial.baud=115200
serial.status=DETECTED
retrobox.binary=installed
box86.appimage=installed
qemu.package=installed
kvm.cpu_flag=vmx
kvm.device=PRESENT
kvm.status=USABLE
kvm.vmware_backdoor=CONFIGURED
retrobox.group.kvm=MEMBER
retrobox.group.render=MEMBER
```

| Key | Values |
| --- | --- |
| `cdrom.qemu` | `ATTACHED` (QEMU VMs get the drive) or `NONE` |
| `kvm.cpu_flag` | `vmx`, `svm`, or `NOT_FOUND` |
| `kvm.device` | `PRESENT` or `MISSING` (`/dev/kvm`) |
| `kvm.status` | `USABLE` (flag and device) or `UNAVAILABLE` |
| `kvm.vmware_backdoor` | `CONFIGURED` or `MISSING` |
| `retrobox.group.kvm`, `retrobox.group.render` | `MEMBER` or `MISSING` |
