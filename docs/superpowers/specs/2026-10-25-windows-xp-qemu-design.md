# Windows XP VM on QEMU — Design

**Goal:** Add a Windows XP gaming VM to RetroBox that plays XP-era games with
sound and real hardware-accelerated OpenGL and Direct3D (Glide is a bonus),
uses the physical CD-ROM, has guest networking, and — as a stretch — works
with the NFC floppy drive.

**Status:** Draft. Phase 0 is a go/no-go spike on the reference hardware; the
PR plans for phases 1–5 are written after it.

## Why not 86Box

86Box emulates hardware cycle by cycle and its 3D cards end at the Voodoo
line. XP-era games need a hardware-virtualized VM: **QEMU with KVM**.

## Why qemu-3dfx

The reference build (i3-14100, Intel UHD 730 iGPU, 8 GB RAM) rules out the
usual ways of getting 3D into a guest:

| Option | Verdict |
| --- | --- |
| virtio-gpu / VirGL | No XP guest drivers. |
| iGPU passthrough | Intel ships no XP drivers for UHD 730. |
| qemu-3dfx (GPL-2.0) | Forwards guest OpenGL and Glide to host Mesa; supports Win2k/XP; no extra GPU. **Chosen.** |
| Second XP-capable GPU via VFIO | Native D3D9/GL, but needs new hardware, case space, and a second display output. **Fallback.** |
| Own paravirtual GPU + XP display driver | XPDM miniport plus DirectDraw/D3D DDI plus a device model; years of work. Rejected. |
| VMware SVGA3D device + VMware XP driver | Guest driver is proprietary and cannot be shipped. Rejected. |

Direct3D runs through **our own WineD3D build**: Wine's `wined3d`, `d3d8`,
`d3d9`, and `ddraw` cross-compiled as Windows DLLs with mingw (LGPL, so we
can redistribute them), running on top of qemu-3dfx's `OPENGL32.DLL`. XP
looks in the game's folder before system32, so they install per game.

Floppy and CD media changes use QEMU's stock QMP (`blockdev-change-medium`,
`eject`, `query-block`), so the fork carries no media-control patches.

## Target VM (`profiles/winxp`)

- `-machine pc` (i440fx), `-accel kvm -cpu host` (fallback `core2duo`),
  2 vCPUs, 2 GB RAM.
- 20 GB qcow2 on IDE (XP has no inbox AHCI driver).
- AC97 audio via `-audiodev alsa`, reusing the `.asoundrc` written by
  `retrobox-audio-setup`; the guest needs the Realtek AC97 driver.
- Networking: `rtl8139` (XP inbox driver) on user-mode NAT
  (`-netdev user`). The guest reaches the LAN/internet but accepts no
  inbound connections, which limits the exposure of an unpatched XP.
  Hosting LAN games needs `hostfwd` rules or a bridge (future work).
- Physical CD-ROM through `host_cdrom` on `ide-cd`; XP itself is installed
  from the real drive. CD audio (Redbook) tracks do not play through an
  emulated IDE drive; an `scsi-block` passthrough on an LSI controller is a
  later option.
- Second CD slot: the "RetroBox XP Tools" ISO (qemu-3dfx wrappers,
  `FXPTL.SYS`, WineD3D DLLs, install notes).
- 2D video card: decided in Phase 0 (`cirrus`, `vmware`, or `std` + VBEMP).
- No XP media, drivers, or games in the payload, same as the other profiles.

## Phases

Each phase from 1 to 5 is one PR and ships its own docs and ADR updates.
Gates: `mise run test`, `mise run format-check`; CI runs
`mise run publish-linux-x64`.

### Phase 0 — Hardware spike (no PR)

On the reference box running Debian 13:

1. Build QEMU 9.2.x with the qemu-3dfx patches and install XP under KVM.
2. Exercise one title per path: OpenGL (Quake III), Glide, D3D8/9
   (Max Payne / 3DMark2001), with sound.
3. Find the newest Wine release whose PE `wined3d`/`d3d9` loads on XP, or
   list the patches needed to make a newer one load.
4. Confirm fullscreen GL output on the console without X (SDL KMSDRM);
   otherwise use a minimal kiosk compositor (`cage`).
5. Pick the 2D video card; measure audio latency.

Record the results in `docs/qemu-xp-spike.md`. **Go:** commit to qemu-3dfx +
WineD3D. **No-go on D3D:** decide between patching Wine for XP and the VFIO
second-GPU fallback.

### Phase 1 — QEMU fork and guest tools (`ci`, `chore(appliance)`)

- `nakioman/qemu`: qemu-3dfx rebased on a pinned QEMU, with release
  tarballs, the same way the 86Box fork is maintained.
- CI that builds the host QEMU and the guest wrappers from the same commit
  (the wrappers' signature has to match the host build) plus the mingw WineD3D
  DLLs, and packages the XP Tools ISO.
- `appliance/qemu.env` pins versions and SHA-256 sums; the installer unpacks
  everything to `/opt/qemu`.

### Phase 2 — Multi-engine boot (`feat(core)`, `feat(cli)`)

- `vms.yaml` gains `engine: 86box | qemu`, default `86box`; validated in
  `RetroBoxCatalogValidation`.
- `RetroBoxBoot` splits into `RetroBox86BoxLauncher` and
  `RetroBoxQemuLauncher`. The selector, F12 window, and splash are unchanged.
- A QEMU profile's `qemu.args` (one argument per line) is its hardware source
  of truth; the launcher appends only host-specific arguments (KVM, CD
  device, QMP socket, audiodev).
- ADR 0008 extends ADR 0003: each engine's native profile is the hardware
  source of truth.
- Tests first: catalog parsing, engine selection, argument building.

### Phase 3 — XP profile, audio, networking (`feat(appliance)`)

- `profiles/winxp` as described above, with a blank qcow2 created at install
  time (same idea as `retrobox-hdd-creation`).
- `vms.yaml` entry with `engine: qemu`.
- `docs/vm-profiles.md` and `docs/qemu-xp.md` (installing XP and the XP Tools,
  copying wrappers into each game's folder).

### Phase 4 — CD-ROM and installer host setup (`feat(appliance)`)

- `lib/hardware-detect.sh` writes the detected drive into the QEMU profile
  alongside today's `ioctl://` 86Box setup.
- `retrobox` joins the `kvm` group; the installer checks that VT-x is enabled
  and reports it in `install-report.txt`.
- `docs/cdrom-passthrough.md` covers the QEMU path.

### Phase 5 — NFC floppy over QMP (`feat(daemon)`)

Same behavior as 86Box today: inserting an NFC floppy mounts the matching
`floppies.yaml` image, removing it ejects the image. Only floppy images use
NFC; CDs are always real discs in the physical drive, never images.

- `RetroBoxQmpFloppyControlClient` implements `IRetroBoxFloppyControlClient`
  on `/run/retrobox/qemu-qmp.sock` (`blockdev-change-medium` on `floppy0`
  honoring `ro`/`rw`, `eject`, `query-block`).
- The catalog, NFC tag format, `nfc: true` gating, and
  `RetroBoxFloppyEventHandler` are unchanged; only the transport differs.
- The daemon picks the client for whichever socket is live, so the existing
  re-sync-on-VM-start behavior carries over.

### Follow-up (not scheduled)

- Bridged networking / `hostfwd` for hosting LAN games.
- `scsi-block` CD passthrough for Redbook audio.
