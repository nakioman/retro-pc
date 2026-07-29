# Pentium 100 VM Profile Design

## Goal

Add a portable native 86Box profile for the project's mid-1990s Pentium 100
gaming machine, with a blank approximately 2.1 GB hard disk and no bundled
copyrighted media.

## Design

The profile lives under `profiles/pentium100` and follows the existing
`profiles/386sx16` layout. `86box.cfg` is the hardware source of truth and
uses relative paths so the directory can be copied or imported as an ordinary
86Box VM profile. The configuration is based on the locally created 86Box VM,
but excludes installation-specific `host_cpu` and `uuid` values.

The profile contains a blank dynamic VHD copied from the local VM. Its native
86Box geometry is 63 sectors, 16 heads, and 4092 cylinders, approximately 2.1
GB, matching a Quantum Bigfoot-era disk. The README documents the hardware,
manual launch workflow, physical CD-ROM host dependency, generated NVRAM, and
media policy. The VM catalog documentation lists the profile as native 86Box
configuration rather than RetroBox YAML metadata.

## Verification

- Inspect the CFG for the required machine, CPU, memory, video, sound, CD-ROM,
  floppy, and relative HDD settings.
- Confirm the VHD has a valid dynamic VHD footer and matches the local template.
- Run `mise run test`.
- Manually launch the profile in 86Box to confirm BIOS detects the expected
  memory and disk; CD-ROM passthrough remains host-dependent and out of scope.
