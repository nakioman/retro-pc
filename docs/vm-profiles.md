# VM Profiles

Retro PC VM profiles are native emulator profiles. For 86Box VMs, the 86Box
configuration file is the hardware source of truth. RetroBox YAML catalogs may
refer to a profile for display or selection, but YAML is metadata only and is
not required by 86Box.

The appliance installs the catalog at `/data/retrobox/vms.yaml` and profiles at
`/data/vms/<id>`. The catalog intentionally has no `defaultVm`; `retrobox boot`
selects a VM when needed. 86Box is launched with the selected profile as its
working directory, so relative disk paths remain valid; shader paths use the
absolute `/opt/86Box/shaders/crt/crt-easymode.glslp` form and monitor EDID (when
used) uses `/data/vms/<id>/syncmaster3.edid`.

## 386SX-16

Path: `profiles/386sx16`

This is the RTM modest DOS VM profile: a 386SX-16 class machine with a blank
54 MB hard disk and no installed operating system or bundled media.

Profile files:

- `86box.cfg`: native 86Box configuration.
- `hdd.raw`: blank disk (created dynamically from `hdd_01_parameters`).
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
- `hdd.raw`: blank disk (created dynamically from `hdd_01_parameters`).
- `README.md`: setup notes, hardware summary, and media policy.

## Pentium II 350

Path: `profiles/pentium2-350`

This is the Windows 98 SE VM profile: a Pentium II Deschutes 350 MHz (3.5x,
dynarec `new`) with 128 MB RAM, Voodoo3 3000 AGP with SyncMaster EDID, Sound
Blaster AWE64 Gold, and a blank ~20 GB disk (63, 255, 2434). Uses the same
`crt-easymode.glslp` shader as the other profiles.

Profile files:

- `86box.cfg`: native 86Box configuration and hardware source of truth.
- `hdd.raw`: blank disk (created dynamically from `hdd_01_parameters`).
- `syncmaster3.edid`: 128-byte binary EDID for the CRT shader setup.
- `README.md`: setup notes, hardware summary, and media policy.
