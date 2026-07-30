# Pentium 100 86Box Profile

This directory contains the native 86Box profile for the Retro PC Pentium 100
VM. The 86Box configuration is the source of truth for hardware selection.

## Hardware

- Machine: `8500tuc`
- CPU: `pentium_p54c`, 100 MHz (1.5x multiplier), dynarec enabled
- RAM: 8 MB
- Video: Trident TGUI9440AGi PCI, 1 MB
- Sound: Sound Blaster 16
- Hard disk: `HDD.vhd`, blank dynamic VHD with approximately 2.1 GB geometry
- Floppy: one 3.5-inch 1.44 MB drive with `alps_df354h148f_80t` audio
- CD-ROM: one ATAPI 4x slot; the Debian appliance installer detects the host's
  physical optical drive and writes its path into the installed runtime profile
- Expected OS: DOS and Windows 3.1, installed manually later

## Launch

Copy or import this directory as an ordinary 86Box VM profile. Keep
`86box.cfg` and `HDD.vhd` in the same directory because the hard disk path in
the config is relative:

```text
hdd_01_fn = HDD.vhd
```

The SDL graphics configuration enables the bundled CRT shader:

```text
shader0 = shaders/syncmaster3.glsl
```

Keep the `shaders/` directory beside `86box.cfg` when copying the profile.

Launching the profile should reach BIOS and then an empty/non-system disk
state. Install DOS and Windows 3.1 manually later if the VM needs to become
bootable. The repository template intentionally has no host-specific CD-ROM
path. During Debian appliance installation, `install-retropc.sh` detects the
physical optical drive and writes `cdrom_01_host_drive` into the writable
runtime profile at `/data/vms/pentium100/86box.cfg`, preferring a stable
`/dev/disk/by-id/...` path and falling back to `/dev/sr0`. If no drive is
present, it records that absence rather than inventing a path.

## Blank Disk

`HDD.vhd` is intentionally blank. It declares the approximately 2.1 GB disk
geometry used by the 86Box profile:

```text
hdd_01_parameters = 63, 16, 4092, 0, ide
```

This is in the spirit of a Quantum Bigfoot-era disk. Do not replace this file
with an installed OS image in the repository.

## Generated State

86Box may create an `nvr/` directory after the VM boots. That BIOS/NVR state is
runtime-generated and is not part of the profile template.

## Media Policy

This profile includes no DOS, Windows, drivers, games, floppy images, CD-ROM
images, physical CD media, or other copyrighted media. A physical disc is
supplied and managed by the appliance operator outside this repository.
