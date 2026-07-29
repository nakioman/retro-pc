# VM Profiles

Retro PC VM profiles are native emulator profiles. For 86Box VMs, the 86Box
configuration file is the hardware source of truth. RetroBox YAML catalogs may
refer to a profile for display or selection, but YAML is metadata only and is
not required by 86Box.

## 386SX-16

Path: `profiles/386sx16`

This is the RTM modest DOS VM profile: a 386SX-16 class machine with a blank
54 MB hard disk and no installed operating system or bundled media.

Profile files:

- `86box.cfg`: native 86Box configuration.
- `HDD.vhd`: blank 54 MB dynamic VHD used by the profile.
- `README.md`: setup notes and hardware summary.
