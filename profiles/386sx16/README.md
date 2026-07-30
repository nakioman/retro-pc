# 386SX-16 86Box Profile

This directory contains the native 86Box profile for the Retro PC 386SX-16 VM.
The 86Box config file is the source of truth for hardware selection.

## Hardware

- Machine: `awardsx`
- CPU: `i386sx`, 16 MHz, dynarec disabled
- RAM: 2 MB
- Video: Trident TVGA 8900 ISA VGA, 512 KB
- Storage controller: ISA IDE
- Hard disk: `HDD.vhd`, 54 MB blank dynamic VHD
- Floppy: one 3.5-inch 1.44 MB drive with `alps_df354h148f_80t` audio
- Sound card: none; PC speaker only
- CD-ROM: none
- Expected OS: DOS, installed manually later

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
state. Install DOS manually later if the VM needs to become bootable.

## Blank Disk

`HDD.vhd` is intentionally blank. It is a tiny dynamic VHD file in Git, but it
declares the 54 MB disk geometry used by the 86Box profile:

```text
hdd_01_parameters = 39, 4, 762, 0, ide
```

Do not replace this file with an installed OS image in the repository.

## Generated State

86Box may create an `nvr/` directory after the VM boots. That BIOS/NVR state is
runtime-generated and is not part of the profile template.

## Media Policy

This profile includes no DOS, Windows, drivers, games, floppy images, CD-ROM
images, or other copyrighted media.
