# Debian Appliance Installer Design

> Superseded by the preinstalled raw-image installer implemented in this
> branch. The current design uses a Debian Live BIOS environment to copy a
> prepared Debian disk image and configure `retrobox`.

## Goal

Build a BIOS/Legacy-only bootable Debian Installer USB image for the Retro PC
appliance. The USB installs Debian minimal, asks the operator for the target
disk and basic machine credentials, then configures the machine to boot 86Box
fullscreen with the Pentium 100 profile and physical CD-ROM passthrough.

The first appliance image deliberately excludes the ESP8266, floppy daemon,
NFC, and floppy import integration.

## Installer experience

The image is based on the official Debian Installer with its ncurses interface.
The installer remains interactive for:

- target disk selection and destructive-write confirmation;
- hostname;
- first maintenance user's name and password;
- timezone.

Preseed values provide safe defaults without embedding credentials in the ISO.
The target disk is not hardcoded in the workflow. Debian's partitioner shows
the disks and the operator confirms the disk that may be erased.

## Disk layout

The installer uses a fixed recipe after the operator selects the disk:

- BIOS/MBR bootloader on the selected disk;
- an ext4 root filesystem with a bounded appliance-system size;
- an ext4 `/data` filesystem using the remaining space;
- optional swap according to the recipe.

The root filesystem is configured for read-only operation after the initial
installation is validated. `/data` remains writable and contains VM disks,
RetroBox YAML, Samba scratch files, and persistent application state. Runtime
locations such as `/run`, `/tmp`, and required log/state directories receive
the tmpfs or persistent handling needed by systemd services.

## Post-install configuration

Debian Installer invokes a repository-provided post-install script through
`preseed/late_command` while `/target` is available. The script will:

1. install the appliance package manifest, SSH, Samba, sudo, and required
   runtime dependencies;
2. create the `retrobox` service account and `/data` layout;
3. configure the restricted `retro-floppy-scratch` Samba share for future
   floppy support;
4. install the pinned 86Box AppImage, profiles, and CRT shaders;
5. detect a physical CD-ROM device and write the Pentium profile's passthrough
   setting;
6. install the fullscreen boot service and emergency maintenance path;
7. configure the read-only root mode and document its recovery procedure;
8. write an install report under `/data/retrobox`.

The first version may leave the floppy share unused, but it should establish
the directory and permissions contract so later floppy work does not require
repartitioning or reinstalling the appliance.

## 86Box version pinning

The workflow will expose one easily editable version value, `86BOX_VERSION`,
and derive the x86_64 AppImage URL from the tagged release. The initial value
is `v7.0.0-master.46`, the current latest release of `nakioman/86box` at design
time. The workflow must fail if the requested release or asset is missing,
rather than silently selecting a different version.

The installer contains no DOS, Windows, drivers, games, BIOS ROMs, or other
copyrighted media.

## GitHub Actions artifact

GitHub Actions runs the reproducible ISO build on an Ubuntu runner. The
workflow will:

1. check out the repository;
2. validate the preseed and shell scripts;
3. download the exact 86Box release asset;
4. assemble/remaster the BIOS Debian Installer ISO;
5. inspect the ISO for expected boot files and payloads;
6. create a SHA-256 checksum;
7. upload the ISO, checksum, and build metadata as workflow artifacts.

The workflow does not contain a user password. Release publishing can be
added later; the first validation cycle uses downloadable workflow artifacts.

## First verification milestone

On the physical Retro PC, install to a spare or disposable target disk and
verify:

- the USB boots in BIOS/Legacy mode;
- the installer asks for and erases only the selected disk;
- the installed disk boots without the USB;
- the configured hostname, user, sudo, timezone, and SSH work;
- `/data` is writable and root can be remounted read-only;
- the Samba scratch share is reachable and writable;
- the Pentium 100 profile sees the physical CD-ROM;
- 86Box starts fullscreen automatically;
- the maintenance/recovery path remains available.

Floppy/NFC behavior is explicitly deferred to a later iteration.
