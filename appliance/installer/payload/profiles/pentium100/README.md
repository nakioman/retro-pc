# Pentium 100 86Box Profile

This directory contains the native 86Box profile for the Retro PC Pentium 100
VM. The 86Box configuration is the source of truth for hardware selection.

## Hardware

- Machine: `8500tuc`
- CPU: `pentium_p54c`, 100 MHz (1.5x multiplier), dynarec enabled
- RAM: 8 MB
- Video: Trident TGUI9440AGi PCI, 1 MB
- Sound: Sound Blaster 16
- Hard disk: `hdd.raw` with approximately 2.1 GB geometry (63, 16, 4092)
- Floppy: one 3.5-inch 1.44 MB drive with `alps_df354h148f_80t` audio
- CD-ROM: one ATAPI 4x slot; its portable template has no host path. When an
  appliance detects a physical drive, its installed copy receives an
  `ioctl://<detected-device>` path for this first active slot
- Expected OS: DOS and Windows 3.1, installed manually later

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
SCANLINE_CUTOFF = 540 (scanlines en 640×480, apagadas de 800×600 para arriba)
SCANLINE_BRIGHT_MIN = 0.45
SCANLINE_BRIGHT_MAX = 0.70
MASK_SIZE = 2
MASK_STRENGTH = 0.35
MASK_STAGGER = 3
MASK_DOT_HEIGHT = 2
SHARPNESS_H = 0.9
GAMMA_INPUT = 2.4
BRIGHT_BOOST = 1.3
```

Launching the template should reach BIOS and then an empty/non-system disk
state. Install DOS and Windows 3.1 manually later if the VM needs to become
bootable. The appliance installer, not this portable template, configures a
detected physical drive. Validate installed passthrough with
[`docs/cdrom-passthrough.md`](../../../../docs/cdrom-passthrough.md).

## Blank Disk

`hdd.raw` is intentionally blank and is not committed to Git. The appliance
installer (and `appliance/installer/payload/scripts/retrobox-hdd-creation`)
creates it on first install from the geometry declared in the 86Box profile:

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
images, or other copyrighted media.
