# 386SX-16 86Box Profile

This directory contains the native 86Box profile for the Retro PC 386SX-16 VM.
The 86Box config file is the source of truth for hardware selection.

## Hardware

- Machine: `awardsx`
- CPU: `i386sx`, 16 MHz, dynarec disabled
- RAM: 2 MB
- Video: Trident TVGA 8900 ISA VGA, 512 KB
- Storage controller: ISA IDE
- Hard disk: `hdd.raw`, 54 MB blank disk with geometry 39, 4, 762
- Floppy: one 3.5-inch 1.44 MB drive with `alps_df354h148f_80t` audio
- Sound card: none; PC speaker only
- CD-ROM: none
- Expected OS: DOS, installed manually later

## Launch

Copy or import this directory as an ordinary 86Box VM profile. Keep
`86box.cfg` and `hdd.raw` in the same directory because the hard disk path in
the config is relative:

```text
hdd_01_fn = hdd.raw
```

The SDL graphics configuration enables the bundled CRT shader:

```text
shader0 = /opt/86Box/shaders/crt/crt-easymode.glslp
```

with per-profile tuning:

```text
[GL3 Shaders - crt-easymode.glslp]
SCANLINE_CUTOFF = 900
SCANLINE_BRIGHT_MIN = 0.30
SCANLINE_BRIGHT_MAX = 0.65
MASK_SIZE = 3
MASK_STRENGTH = 0.35
MASK_STAGGER = 3
MASK_DOT_HEIGHT = 2
SHARPNESS_H = 0.7
GAMMA_INPUT = 2.4
BRIGHT_BOOST = 1.3
```

Launching the profile should reach BIOS and then an empty/non-system disk
state. Install DOS manually later if the VM needs to become bootable.

## Blank Disk

`hdd.raw` is intentionally blank and is not committed to Git. The appliance
installer (and `appliance/installer/payload/scripts/retrobox-hdd-creation`)
creates it on first install from the geometry declared in the 86Box profile:

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
