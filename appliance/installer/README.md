# Retro PC BIOS Installer

This builder creates the Retro PC Debian 13 installer image. The image is
BIOS/Legacy-only: it has a BIOS El Torito boot entry and no EFI or UEFI boot
path.

## Build locally

From the repository root on a Debian or Ubuntu host with `curl`, `xorriso`,
`sha256sum`, `mise`, and `git` installed, run:

```text
./appliance/installer/build-installer.sh --output build/retro-pc-installer.iso
```

The command publishes `retrobox`, downloads the pinned Debian and 86Box
assets, and writes the ISO plus `build/retro-pc-installer.iso.sha256` and
`build/retro-pc-installer.iso.json`.

GitHub Actions runs the same builder on Ubuntu whenever installer-related
sources change, or manually through the **Build Debian appliance installer**
workflow. Download its `retro-pc-debian-installer-<commit>` artifact, which
contains the ISO, checksum, and metadata JSON. Prototype artifacts are retained
for seven days. From the extracted artifact directory, verify it before using
it:

```text
sha256sum -c retro-pc-installer.iso.sha256
```

## Write and install

The image is BIOS/Legacy-only and deliberately has no UEFI boot path. Write it
to a USB drive with a raw-image writer suitable for your operating system. On
Linux, first identify the whole USB device (for example, `/dev/sdX`, not a
partition such as `/dev/sdX1`), then use it explicitly:

```text
USB_DEVICE=/dev/sdX
sudo dd if=retro-pc-installer.iso of="$USB_DEVICE" bs=4M conv=fsync status=progress
sync
```

**Before running `dd`, verify that `USB_DEVICE` is the removable USB drive.
This command overwrites the entire selected device.** Do not substitute a
target appliance disk and do not hardcode a device name from another machine.
When using a locally built image, either run the command from `build/` or set
`if=` to `build/retro-pc-installer.iso`.

Boot the appliance from that USB drive in Legacy/BIOS mode; do not select
UEFI-only boot. The first physical installation must use a disposable target
disk. It is a prototype check, not final RTM hardware validation.

At boot, Debian Installer loads the bundled preseed and then asks the operator
to confirm the following answers:

- Select the intended target disk and explicitly confirm the destructive
  partitioning write. The installer never chooses a disk for you.
- Provide the hostname (the displayed `retrobox` default may be changed).
- Create the first maintenance user and password. Those credentials are
  entered only in Debian Installer; they are not in `preseed.cfg`, the image
  builder, workflow, or repository.
- Confirm the timezone (the displayed `Etc/UTC` default may be changed).

The selected disk is partitioned for BIOS/MBR boot with an ext4 `/` filesystem
mounted read-only after provisioning and a separate writable ext4 `/data`
filesystem. Persistent appliance state is placed under `/data`, including
`/data/retrobox`, `/data/vms`, `/data/floppies/scratch`,
`/data/floppies/cataloged`, and `/data/snapshots`.

## First boot checks

After installation completes, remove the USB drive and boot the installed disk
in BIOS/Legacy mode. Log in as the maintenance user locally or by SSH, then
check the installed contract:

```text
hostnamectl
sudo -v
timedatectl
findmnt -no TARGET,OPTIONS /
findmnt -no TARGET,OPTIONS /data
touch /data/.retro-pc-write-check && rm /data/.retro-pc-write-check
grep '^cdrom_01_host_drive' /data/vms/pentium100/86box.cfg
cat /etc/retrobox-appliance/install-report.txt
```

`/` must show `ro` in its mount options and `/data` must be writable. The
CD-ROM configuration should be a detected physical path (normally a stable
`/dev/disk/by-id/...` path, with `/dev/sr0` as the fallback) when a drive was
present during installation; the install report records `CDROM_STATE` and
`CDROM_DEVICE`. A missing drive is reported as `missing`/`none` rather than
being fabricated.

From another machine on the network, verify remote maintenance and the
restricted Samba scratch share with an authorized Samba user:

```text
ssh <maintenance-user>@<appliance-hostname>
smbclient //'<appliance-hostname>'/retro-floppy-scratch -U <authorized-samba-user>
```

The Samba user must be an existing local user authorized through the
`retrobox-samba` group and Samba's password database; the share exposes only
`/data/floppies/scratch`, not VM disks, snapshots, or cataloged media. Finally,
confirm visually that the normal console boots directly into fullscreen 86Box
using `/data/vms/pentium100` and that its CD-ROM device is available when the
physical drive was detected.

## Recovery and updates

For a maintenance boot that leaves tty1 free instead of starting fullscreen
86Box, create a systemd override as an administrator:

```text
sudo systemctl edit retrobox-boot.service
```

Add the following content, save it, then reboot and connect over SSH (or use a
local console):

```ini
[Service]
Environment=RETROBOX_MAINTENANCE=1
```

The appliance root is intentionally read-only. Before applying OS updates or
making system changes, remount it through the installed maintenance command:

```text
sudo /usr/local/sbin/install-retropc.sh --maintenance
sudo apt update
sudo apt upgrade
sudo mount -o remount,ro /
sudo systemctl revert retrobox-boot.service
sudo reboot
```

`--maintenance` only remounts the selected root filesystem read-write; it does
not reprovision the appliance. Remove the override after maintenance so the
next normal boot returns to fullscreen 86Box.

## Current scope

This first installer has no floppy-control integration. It does not configure
the ESP8266 controller, NFC tags, the floppy daemon, or automatic floppy image
insertion. The restricted Samba scratch directory is installed only as a
forward-compatible storage and permissions contract for later floppy work.
