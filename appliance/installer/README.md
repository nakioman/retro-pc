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

GitHub Actions builds the same deliverables on Ubuntu and uploads the ISO,
checksum, and metadata JSON as workflow artifacts. Download all three files
from the workflow run and verify the checksum before using the image.

## Write and install

The image is BIOS/Legacy-only and deliberately has no UEFI boot path. Write it
to a USB drive with a raw-image writer suitable for your operating system.
On Linux, a typical command is `sudo dd if=build/retro-pc-installer.iso
of=/dev/sdX bs=4M conv=fsync status=progress`. **Confirm that `/dev/sdX` is the
USB drive before running it: writing the image overwrites that entire device.**
Do not select UEFI-only boot mode on the appliance; select Legacy/BIOS boot.

At boot, Debian Installer loads the bundled preseed and then remains
interactive for the target disk and destructive-write confirmation, hostname,
first maintenance user and password, and timezone. Select the intended target
disk carefully; the selected disk is partitioned for a BIOS/MBR bootloader,
read-only root, and writable `/data`.

## First boot checks

After installation completes and the USB drive is removed, verify that:

- the installed disk boots in BIOS/Legacy mode without the USB drive;
- the configured hostname, maintenance user, sudo access, timezone, and SSH
  work;
- `/data` is writable and the root filesystem can be remounted read-only;
- the restricted Samba scratch share is reachable; and
- 86Box starts fullscreen with the Pentium 100 profile and sees the physical
  CD-ROM when one is installed.

## Current scope

This first installer has no floppy-control integration. It does not configure
the ESP8266 controller, NFC tags, the floppy daemon, or automatic floppy image
insertion. The restricted Samba scratch directory is installed only as a
forward-compatible storage and permissions contract for later floppy work.
