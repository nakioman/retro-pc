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

- `-machine pc` (i440fx), `-accel kvm -cpu core2duo`, 2 vCPUs, 3 GB RAM
  (32-bit XP cannot use much more). With KVM the model only sets the CPUID
  the guest sees; games run at host speed.
- Direct3D and OpenGL only. Glide is out of scope (no host Glide library or
  guest Glide DLLs are shipped).
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
- 2D video card: std VGA with a period monitor EDID,
  `-vga none -device VGA,edid=on,xres=1024,yres=768,xmax=1024,ymax=768,vgamem_mb=16`;
  the fork caps the VESA mode list at the EDID maximum, and the guest uses the
  VBEMP VESA driver for 32-bit modes (XP's Cirrus driver has none).
- Fullscreen output is stretched to 4:3 like a CRT (fork patch).
- No XP media, drivers, or games in the payload, same as the other profiles.

## Display findings and open issues

Found during the Phase 0 spike on the reference box (UHD 730, 2048x1536 HDMI
panel, SDL2 `kmsdrm`, no X). Each fix lives in `retrobox/patches/` of the
fork.

- QEMU's text consoles (monitor, serial, parallel) each open an SDL window
  and the last one covers the VM: run with `-monitor none -serial none
  -parallel none`.
- A frame presented once never reaches the panel; it only shows after later
  presents. QEMU presented only on guest updates, so static screens stayed
  black. Fixed by re-presenting the 2D frame every refresh tick.
- **The scanout honours alpha.** SDL2 `kmsdrm` scans out an ARGB8888
  buffer and pixels with alpha 0 show as black, even with blending off.
  QEMU mapped `x8r8g8b8` framebuffers (x byte = 0) to `ARGB8888`; the fork
  maps every x-channel format to SDL's X formats.
- **Open, must be checked when 3D is tested:** qemu-3dfx creates the game's
  GL context with an 8-bit alpha channel (`GL_CONTEXTALPHA 1` on Linux,
  `SDL_GL_ALPHA_SIZE 8`). Any pixel a game leaves with alpha < 1 will show
  black or see-through on this scanout. Candidate fixes: drop the alpha
  channel from the window context, or force alpha to 1.0 before each swap
  (`glColorMask(0, 0, 0, 1)` + clear to alpha 1). Verify with OpenGL
  (Quake III) and WineD3D titles.
- Tests must run from the foreground VT (tty1/tty2 on the box). Launching
  QEMU over SSH while a text console is active shows black even with a
  working build.

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
4. Upstream's Linux mesa backend creates its GL context through GLX and needs
   Xorg. Test both it and qemu-3dfx's unbuilt SDL backend
   (`mglcntx_sdlgl.c`) running on KMS/DRM with no X, like 86Box.
5. Pick the 2D video card; measure audio latency.

The spike tooling is throwaway and stays out of the repository; its results
are recorded in this document. **Go:** commit to qemu-3dfx +
WineD3D. **No-go on D3D:** decide between patching Wine for XP and the VFIO
second-GPU fallback.

### Phase 1 — QEMU fork and guest tools (`ci`, `chore(appliance)`)

- [`nakioman/qemu-3dfx`](https://github.com/nakioman/qemu-3dfx), branch
  `retrobox`: a GitHub fork of qemu-3dfx (`master` mirrors upstream) with
  RetroBox changes as commits and `retrobox/patches/` for QEMU-tree fixes,
  maintained like the 86Box fork. `retrobox/build.sh` downloads the pinned
  QEMU, applies everything, and produces the host tarball and the XP tools
  ISO; CI releases them for `retrobox-*` tags.
- CI that builds the host QEMU and the guest wrappers from the same commit
  (the wrappers' signature has to match the host build) plus the mingw WineD3D
  DLLs, and packages the XP Tools ISO.
- Build the SDL GL context backend so QEMU runs on KMS/DRM without X, and
  replace its GLX pbuffer path with FBOs or EGL pbuffers. Host Glide
  (OpenGLide) gets the same treatment, or stays out of scope if no XP-era
  Glide title matters.
- Replace the prebuilt `FXPTL.SYS` (a binary-only build of the NT DDK
  `MAPMEM` sample) with our own source-built driver: the same `\\.\MAPMEM`
  IOCTL interface `fxlibnt.c` uses, compiled with mingw-w64's DDK headers.
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
- Boots like a real PC: SeaBIOS boot menu (`-boot order=cd,menu=on`, ESC at
  power-on to pick disk, CD, or floppy), optionally with a RetroBox POST
  logo (`-boot splash=`).
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
