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
`86BOX_VERSION` is the supported release update mechanism; the installer must
continue to use the explicitly configured x86_64 asset and must not embed
credentials in this file.

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
