# Windows XP VM on QEMU — Design

**Goal:** Add a Windows XP gaming VM to RetroBox that plays XP-era games with
sound and real hardware-accelerated OpenGL and Direct3D (Glide is a bonus),
uses the physical CD-ROM, has guest networking, and — as a stretch — works
with the NFC floppy drive.

**Status:** Draft. Phase 0 (the hardware spike) is in progress; Direct3D works
on the vmvga path described below. Phase 1 (QEMU package on the appliance) and
Phase 2 (multi-engine boot, declared disks) and Phase 3 (the `core2duo` XP
profile) are implemented; see their sections. The PR plans for phases 1–5 are written
after it, and Phase 1's fork content changes accordingly.

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

## Spike update (2026-10-29): the vmvga path

The spike found a better path than qemu-3dfx and now leads with it:
[qemus/qemu-vmvga](https://github.com/qemus/qemu-vmvga) (GPL-2.0+), a full
VMware SVGA II device for QEMU 11.1 with SVGA3D rendered through
**dxvk-native → Vulkan**. XP uses VMware's own SVGA II driver (2D, Direct3D
9, OpenGL ICD), which the user supplies like the XP media; RetroBox does not
ship it.

Working stack on the reference box (2D at 32-bit, mode switches, pointer,
dxdiag D3D8/D3D9 tests fast):

```text
XP + VMware SVGA II driver
  -> qemu 11.1.1 + vmvga (-device vmvga,hwcursor=off,gbobjects=off,vgpu=9)
  -> dxvk-native 3.1.1 -> Vulkan (Mesa ANV, UHD 730)
  -> QEMU GTK display, gl=on (GPU scaling, 4:3 patch)
  -> bare Xorg + matchbox-window-manager (kiosk), started with xinit
```

Findings that shaped it:

- **SDL on KMS/DRM was dropped.** SDL2's kmsdrm backend needed four patches
  (frame re-present, opaque pixel formats, no-resize, UI-thread-only 2D work)
  and still conflicted with vmvga: DXVK's headless WSI refuses to load next to
  an already loaded SDL2, so 3D stayed off. QEMU's GTK display in a bare Xorg
  avoids all of it. qemu-3dfx requires the SDL display, so this also retires
  the qemu-3dfx + WineD3D plan; it stays as plan B in the fork.
- **GTK must use `gl=on`.** With `gl=off` GTK scales every frame with cairo on
  the CPU (1024x768 -> 2048x1536) and D3D9 stuttered; with `gl=on` the GPU
  scales, like 86Box.
- **`hwcursor=off`** (fork patch): VMware drivers only report hardware cursor
  positions with an absolute pointer (vmmouse). With the relative PS/2 mouse
  games need, the reported position stayed at 0,0. Without the capability XP
  draws its own pointer.
- **`gbobjects=off`** (fork patch): with guest-backed objects the driver uses
  screen targets, and vmvga showed a stale buffer after every mode switch.
  The classic framebuffer and screen-object model switches modes correctly.
- **No logind on the appliance**, so Xorg runs through the setuid
  `Xorg.wrap` (`xserver-xorg-legacy`). GTK's `dconf` must use `dbus-x11`;
  `dbus-user-session` would pull in `libpam-systemd` and start logind sessions.
- Root filesystem cost on top of the base appliance: Xorg ~58 MB, GTK3 +
  matchbox ~79 MB, `mesa-vulkan-drivers` ~81 MB (all drivers; could be trimmed
  to Intel's), plus QEMU and DXVK under `/opt`.
- KVM must allow the VMware backdoor for 3D: `kvm.enable_vmware_backdoor=1`.

Open on this path:

- **16-bit modes:** the driver only lists 32-bit modes; DirectX 7's
  640x480x16 test fails. Many games of the era use 16-bit fullscreen. Needs
  vmvga to accept 16 bpp (fork patch).
- **Guest vCPU spins at 100% during 3D:** the XP driver polls `SVGA_REG_BUSY`
  instead of sleeping on fence interrupts.
- 1024x768 monitor cap: the VMware card has no EDID; XP shows "Default
  Monitor". Modes beyond 1024x768 are still offered.
- OpenGL games through VMware's `vmwogl32` ICD, real games (D3D7-9), audio,
  and longer stability runs are untested.

## Target VM (`profiles/winxp`, shipped as `profiles/core2duo`)

Written before the spike. The shipped profile follows "the vmvga path" above
and Phase 3 below: VMware SVGA II instead of std VGA + VBEMP, and no XP Tools
ISO (the user supplies VMware's driver).

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

### Phase 1 — QEMU package on the appliance (`ci`, `chore(appliance)`)

- QEMU comes from [`nakioman/qemu-vmvga`](https://github.com/nakioman/qemu-vmvga),
  branch `retrobox` (`master` mirrors qemus/qemu-vmvga). It carries the vmvga
  fixes as commits (`hwcursor`/`gbobjects` options, 15/16 bpp, the vgpu9
  performance work in `retrobox/PERFORMANCE.md`) and QEMU display patches in
  `retrobox/patches/` (4:3 fullscreen for gl=off and gl=on, input capture).
  CI builds `qemu-retrobox-<qemu>-<rev>-linux-x86_64.tar.gz` (QEMU 11.1.1 with
  GTK + OpenGL, no SDL, plus dxvk-native) and releases it for `retrobox-*`
  tags. `nakioman/qemu-3dfx` stays as plan B.
- `appliance/86box.env` pins the release and its SHA-256 (`QEMU_*`, next to
  the 86Box artifacts, rather than a separate `qemu.env`); the installer
  unpacks it to `/opt/qemu-retrobox`.
- Appliance packages: a bare Xorg (`xserver-xorg-core`,
  `xserver-xorg-input-libinput`, `xserver-xorg-legacy`, `xinit`),
  `matchbox-window-manager`, GTK 3 with `dbus-x11` (not `dbus-user-session`,
  which pulls in `libpam-systemd`), `libvulkan1`, and Intel's Vulkan driver
  (`mesa-vulkan-drivers`, trimmed to the Intel ICD if possible).
- KVM loads with `enable_vmware_backdoor=1` (modprobe option), otherwise the
  guest driver disables Direct3D.
- No guest tools are shipped: the user supplies VMware's SVGA II driver for
  XP, like the XP media.

**Status (2026-10-30): implemented.** Plan:
[`2026-10-30-windows-xp-qemu-phase-1.md`](../plans/2026-10-30-windows-xp-qemu-phase-1.md);
decision record: ADR 0008.

- First release: `retrobox-11.1.1-1` (fork commit `c894e78`), asset
  `qemu-retrobox-11.1.1-c894e78-linux-x86_64.tar.gz`.
- `ldd` on Debian 13 found one missing library, `libslirp0` (user-mode NAT),
  now in `packages.txt`.
- The package is a full install prefix (489 MB unpacked, ~290 MB of it other
  architectures' EDK2 images). The installer unpacks only `qemu-system-i386`,
  `qemu-img`, dxvk-native, and `share/qemu` without those images: 145 MB.
  Follow-up: drop them (and strip the binary) in the fork's package.
- `mesa-vulkan-drivers` is trimmed to Intel's ICD with dpkg `path-exclude`
  filters (79 MB -> 21 MB).
- Measured root cost without Recommends: +283 MB (Xorg 27, GTK 3 + matchbox
  + dbus-x11 90, Vulkan 21, QEMU 145). Fresh installs get a 3 GiB root
  partition (was 2 GiB); most of the disk stays on `/data`.
- `retrobox` joins `kvm` and `render` here, ahead of Phase 4.
- Left for Phase 2: `/etc/X11/Xwrapper.config` for starting Xorg from the
  boot service, the `xinit` session, and
  `LD_LIBRARY_PATH=/opt/qemu-retrobox/lib` on the QEMU process.

### Phase 2 — Multi-engine boot (`feat(core)`, `feat(cli)`)

- `vms.yaml` gains `engine: 86box | qemu`, default `86box`; validated in
  `RetroBoxCatalogValidation`.
- `RetroBoxBoot` splits into `RetroBox86BoxLauncher` and
  `RetroBoxQemuLauncher`. The selector, F12 window, and splash are unchanged.
- A QEMU profile's `qemu.args` (one argument per line) is its hardware source
  of truth; the launcher appends only host-specific arguments (KVM, CD
  device, QMP socket, audiodev).
- ADR 0009 extends ADR 0003: each engine's native profile is the hardware
  source of truth.
- Tests first: catalog parsing, engine selection, argument building.

**Implemented** (plan: `docs/superpowers/plans/2026-10-30-xp-phase-2-multi-engine-boot.md`):

- `RetroBoxBoot` became `RetroBox86BoxLauncher`; `RetroBoxQemuLauncher` reads
  `qemu.args` and appends only the QMP socket and the installer-detected
  optical drive (`CDROM_DEVICE` in `/etc/retrobox/hardware.env`, now with
  `CDROM_STATUS`) on IDE index 2, when detected and still present. KVM and
  the audiodev stay in the profile (they are the VM's hardware, like the
  spike's command line).
- QEMU starts with `xinit <retrobox> qemu-session --binary ... --args-file
  /run/retrobox/qemu-session.args --vm-path ... -- :0 vt<N> -keeptty -nolisten
  tcp`. The argv goes through a file because xinit splits client and server
  arguments at `--`. `qemu-session` (hidden) runs `xset s off -dpms`, starts
  matchbox, and runs QEMU with `NO_AT_BRIDGE=1` and
  `LD_LIBRARY_PATH=/opt/qemu-retrobox/lib`. The splash is covered before
  xinit and quit before the selector, as for 86Box.
- `/etc/X11/Xwrapper.config` (payload `x11/Xwrapper.config`):
  `allowed_users=anybody`, `needs_root_rights=yes`, so the boot service can
  start Xorg without a logind session.
- Disks moved into `vms.yaml` for every engine (`disks: [{file, size,
  format}]`); `retrobox vm create-disks` replaces `retrobox-hdd-creation` in
  the installer. 86Box sizes come from the `.cfg` geometry (ADR 0009).

### Phase 3 — XP profile, audio, networking (`feat(appliance)`)

- `profiles/winxp` as described above (`qemu.args`), with its 20 GB qcow2
  declared under `disks:` in `vms.yaml` so `retrobox vm create-disks`
  creates it at install time.
- Boots like a real PC: SeaBIOS boot menu (`-boot order=cd,menu=on`, ESC at
  power-on to pick disk, CD, or floppy), optionally with a RetroBox POST
  logo (`-boot splash=`).
- `vms.yaml` entry with `engine: qemu`.
- `docs/vm-profiles.md` and `docs/qemu-xp.md` (installing XP and the XP Tools,
  copying wrappers into each game's folder).

**Implemented** (plan: `docs/superpowers/plans/2026-10-30-xp-phase-3-core2duo-profile.md`):

- Named after its hardware like the other profiles: id `core2duo`, label
  "Core 2 Duo", `profiles/core2duo/qemu.args` with the spike's hardware
  (`pc,accel=kvm,vmport=on`, `core2duo`, 2 vCPUs, 3 GB, vmvga
  `hwcursor=off,gbobjects=off,vgpu=9`, IDE disk, empty floppy, empty CD on IDE
  index 3, AC97, rtl8139 on user NAT) and `hdd.qcow2` (120G qcow2; the pre-spike plan said 20 GB) in
  `vms.yaml`.
- The kiosk display (`-display gtk,gl=on,full-screen=on,...`) and `-monitor
  none -serial none -parallel none` moved from the profile into
  `RetroBoxQemuLauncher`: they are how the appliance presents QEMU, not VM
  hardware (ADR 0009 updated).
- User guide: `docs/windows-xp.md` (XP SP3 from the real CD, user-supplied
  Realtek AC97 and VMware `SVGA2_X86_WINXP` drivers, display modes, the VMware
  backdoor, Ctrl+Alt+G, known issues) instead of `docs/qemu-xp.md`; no XP
  Tools ISO on the vmvga path.
- Follow-up: a RetroBox POST logo (`-boot splash=`). The repository has no
  logo asset fit for a 640x480 BIOS splash yet.

### Phase 4 — CD-ROM and installer host setup (`feat(appliance)`)

- Done in Phase 2: `lib/hardware-detect.sh` records the detected drive and
  its status in `/etc/retrobox/hardware.env`, and the QEMU launcher attaches
  it (alongside today's `ioctl://` 86Box setup); QEMU profiles stay portable.
- The installer checks that VT-x is enabled and reports it in
  `install-report.txt` (`retrobox` already joined `kvm` in Phase 1).
- `docs/cdrom-passthrough.md` covers the QEMU path.
- QEMU does not notice a disc swap in the host drive: the guest keeps the old
  disc until the drive is reopened (QMP `blockdev-change-medium` on
  `ide1-cd0` with `/dev/sr0`, which the guest sees as eject + insert). The
  daemon watches udev media-change events (`DISK_MEDIA_CHANGE`) for the
  optical drive and issues that command while a QEMU VM runs.

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
