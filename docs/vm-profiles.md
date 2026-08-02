# VM Profiles

Retro PC VM profiles are native emulator profiles. For 86Box VMs, the 86Box
configuration file is the hardware source of truth. RetroBox YAML catalogs may
refer to a profile for display or selection, but YAML is metadata only and is
not required by 86Box.

The appliance installs the catalog at `/data/retrobox/vms.yaml` and profiles at
`/data/vms/<id>`. The catalog intentionally has no `defaultVm`; `retrobox boot`
selects a VM when needed. 86Box is launched with the selected profile as its
working directory, so relative disk paths remain valid; shader paths use the
absolute `/data/vms/<id>/shaders/syncmaster3.glsl` form required by 86Box.

## 386SX-16

Path: `profiles/386sx16`

This is the RTM modest DOS VM profile: a 386SX-16 class machine with a blank
54 MB hard disk and no installed operating system or bundled media.

Profile files:

- `86box.cfg`: native 86Box configuration.
- `HDD.vhd`: blank 54 MB dynamic VHD used by the profile.
- `README.md`: setup notes and hardware summary.

## Pentium 100

Path: `profiles/pentium100`

This is the RTM mid-1990s gaming VM profile: a Pentium 100 with 8 MB of RAM,
Trident TGUI9440AGi PCI video, Sound Blaster 16, a blank approximately 2.1 GB
disk, and an ATAPI CD-ROM slot. The portable payload does not contain a host
device path. During appliance installation, a detected physical drive configures
only the first active optical slot in the installed copy with
`ioctl://<detected-device>`; no detected drive leaves profiles unchanged. See
[`cdrom-passthrough.md`](cdrom-passthrough.md) for target validation.

Profile files:

- `86box.cfg`: native 86Box configuration and hardware source of truth.
- `HDD.vhd`: blank dynamic VHD referenced by the config.
- `README.md`: setup notes, hardware summary, and media policy.
