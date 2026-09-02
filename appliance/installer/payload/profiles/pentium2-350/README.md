# Pentium II 350 86Box Profile

This directory contains the native 86Box profile for the Retro PC Pentium II 350 VM.
The 86Box config file is the source of truth for hardware selection.

## Hardware

- Machine: `p2bls`
- CPU: `pentium2_deschutes`, 350 MHz (3.5x multiplier), dynarec `new`, internal FPU
- RAM: 128 MB
- Video: 3dfx Voodoo3 3000 AGP (`voodoo3_3k_agp`) with SyncMaster EDID (`syncmaster3.edid`)
- Sound: Sound Blaster AWE64 Gold (`sbawe64_gold`)
- Input: PS/2 keyboard + PS/2 mouse (4 buttons)
- GPIO: HDD activity on `/dev/gpiochip0` pin 360 (active-low)
- Storage: IDE, hard disk `hdd.raw` with ~20 GB geometry (63, 255, 2434), 5400 RPM (`1998_5400rpm`)
- Floppy: one 3.5-inch 1.44 MB drive with `alps_df354h148f_80t` audio
- CD-ROM: one ATAPI 72x DVD slot; its portable template has no host path. When an appliance detects a physical drive, its installed copy receives an `ioctl://<detected-device>` path for this first active slot
- Network: none (all slots disabled)
- Expected OS: Windows 98 SE, installed manually later

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
SCANLINE_CUTOFF = 700
SCANLINE_BRIGHT_MIN = 0.55
SCANLINE_BRIGHT_MAX = 0.75
MASK_SIZE = 2
MASK_STRENGTH = 0.30
MASK_STAGGER = 3
MASK_DOT_HEIGHT = 2
SHARPNESS_H = 1.0
GAMMA_INPUT = 2.4
BRIGHT_BOOST = 1.25
```

The monitor EDID is loaded from the installed path:

```text
monitor_edid_path = /data/vms/pentium2-350/syncmaster3.edid
```

Launching the template should reach BIOS and then an empty/non-system disk
state. Install Windows 98 SE manually later if the VM needs to become bootable.
The appliance installer, not this portable template, configures a detected
physical drive. Validate installed passthrough with
[`docs/cdrom-passthrough.md`](../../../../docs/cdrom-passthrough.md).

## Blank Disk

`hdd.raw` is intentionally blank and is not committed to Git. The appliance
installer (and `appliance/installer/payload/scripts/retrobox-hdd-creation`)
creates it on first install from the geometry declared in the 86Box profile:

```text
hdd_01_parameters = 63, 255, 2434, 0, ide
```

This is approximately 20 GB (18.6 GiB). Do not replace this file with an
installed OS image in the repository.

## Generated State

86Box may create an `nvr/` directory after the VM boots. That BIOS/NVR state is
runtime-generated and is not part of the profile template.

## Media Policy

This profile includes no DOS, Windows, drivers, games, floppy images, CD-ROM
images, or other copyrighted media.
