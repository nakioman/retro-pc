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
expected to be delivered as an AppImage, while `retrobox` is deployed as the
self-contained Linux x64 binary produced by the repository's publish task.

This issue defines the base layout only. It does not create a bootable image,
systemd unit files, read-only-root enforcement, or the graphics stack needed
by 86Box. Those pieces must be integrated and tested against the real hardware
separately.

## Installer configuration

The installer configuration contract is defined in
[`installer/install-retropc.conf`](installer/install-retropc.conf). Changing
The Debian and 86Box release values in this file are the source of truth; the
installer must continue to use the explicitly configured x86_64 asset and must
not embed credentials in this file. Installer scripts load the file through
[`read-install-retropc-conf.sh`](installer/read-install-retropc-conf.sh), which
parses the editable keys without sourcing them as shell code and exposes safe
variables such as `RETROBOX_86BOX_VERSION`.

## Accounts and permissions

Create a system user and matching system group named `retrobox`. The account
owns the application state under `/data/retrobox` and should be granted only
the device and service permissions needed by the eventual systemd units.

The account is not a remote login account by default. Maintenance access is
provided through SSH for an explicitly authorized administrator account, using
`sudo` for commands that require elevated privileges. Do not enable password
login for the `retrobox` service account merely to make maintenance easier.

Samba exposes only the floppy import drop directory:

```text
/data/floppies/scratch/
```

The Samba share must not expose the complete `/data` tree. Imported images are
moved by `retrobox` into `/data/floppies/cataloged/`; cataloged images,
configuration, VM disks, and snapshots are not general-purpose network shares.

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
3. Run the published `retrobox` CLI for catalog/import maintenance.
4. Inspect VM directories and snapshots without changing the base OS.
5. Collect service and hardware diagnostics once the systemd units exist.

No DOS, Windows, drivers, games, floppy images, or other copyrighted media are
included by this base layout.

## Installer operation and verification

The bootable installer and the exact artifact, USB-writing, BIOS boot,
first-boot, and recovery procedures are documented in
[`installer/README.md`](installer/README.md). Its ISO is BIOS/Legacy-only: it
does not contain a UEFI boot path. The operator selects the target disk and
confirms its destructive erasure interactively; no device path is embedded in
the installer or documentation as an installation target.

The installed contract is a read-only `/` filesystem plus a writable `/data`
filesystem. Verify the physical prototype only with a disposable disk, then
confirm the hostname, administrator sudo access, timezone, SSH, restricted
Samba scratch share, `/data` persistence, detected physical CD-ROM path, and
fullscreen 86Box launch. Generated ISO artifacts have deterministic ISO,
`.sha256`, and `.json` names, and GitHub Actions publishes the same set as a
commit-named artifact.

Recovery is intentionally available through a systemd maintenance override,
SSH or local console, and `/usr/local/sbin/install-retropc.sh --maintenance`
to remount root read-write for updates. The normal root read-only mode must be
restored before rebooting.

The installer deliberately does not integrate floppy control. It installs the
restricted Samba scratch directory only as storage and permissions groundwork;
it does not configure the ESP8266 controller, NFC tags, floppy daemon, or
automatic floppy-image insertion.
