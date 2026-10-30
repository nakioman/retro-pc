# 0008. Each engine's native profile is the hardware source of truth

Date: 2026-10-30
Status: Accepted (amends 0003)

## Context

ADR 0003 made the 86Box `.cfg` the authoritative hardware description and kept
`vms.yaml` to metadata. The Windows XP VM runs on QEMU (see
`docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`), which has no
`.cfg`: its hardware is a command line. Disks were also created by
`retrobox-hdd-creation`, a bash script that parsed `86box.cfg`; that does not
extend to QEMU's qcow2 disks.

## Decision

- `vms.yaml` gains `engine: 86box | qemu` (default `86box`). Each engine's
  **native profile file** in the VM directory is the hardware source of truth:
  `86box.cfg` for 86Box, `qemu.args` for QEMU (one argument per line, `#`
  comments and blank lines ignored, no shell quoting). YAML still never
  describes hardware.
- The QEMU launcher appends only host-specific arguments to `qemu.args`: the
  QMP socket (`/run/retrobox/qemu-qmp.sock`) and the host optical drive
  (`/dev/sr0` on IDE index 2) when present. QEMU runs fullscreen in a bare
  Xorg started by `xinit`, with `retrobox qemu-session` as the X client.
- `vms.yaml` declares each VM's disks (`disks: [{file, size, format}]`), for
  every engine. `retrobox vm create-disks` creates the missing ones (raw:
  sparse file; qcow2: `qemu-img`) and never touches an existing path. The
  installer runs it instead of `retrobox-hdd-creation`, which is removed.
- For 86Box disks the `.cfg` geometry (`hdd_NN_parameters`, S×H×C×512) stays
  authoritative, because 86Box must agree with the file: `size` may be
  omitted and is then derived; when given it must match exactly, or disk
  creation fails. 86Box disks are raw. QEMU disks must declare a size.

## Consequences

- Adding an engine means a launcher and a profile file name; the catalog,
  selector, F12 window and splash are shared.
- A disk declaration is the only hardware-adjacent data in YAML: it says
  which files RetroBox must create, not how the emulator uses them (the
  profile still references the file).
- Catalogs written before this ADR keep loading (`engine` defaults to
  `86box`, `disks` is optional), but their disks are only created when
  declared.
- Disk/geometry consistency is checked when disks are planned, not on every
  catalog load, so the web panel and daemon never read profile files.
