# 0008. QEMU with vmvga, Xorg, and GTK on the appliance

Date: 2026-10-30
Status: Accepted

## Context

RetroBox needs a Windows XP VM that plays XP-era games with hardware-accelerated
Direct3D and OpenGL. 86Box emulates hardware cycle by cycle and its 3D cards
end at the Voodoo line, so XP needs a hardware-virtualized VM: QEMU with KVM.
The reference box (i3-14100, Intel UHD 730) has no XP-capable GPU to pass
through and Intel ships no XP driver for it.

The design first chose qemu-3dfx (guest OpenGL/Glide forwarded to host Mesa)
plus a WineD3D build for Direct3D, shown through QEMU's SDL display on KMS/DRM
with no X, like 86Box. The Phase 0 spike found a better path: the VMware SVGA
II device from [qemu-vmvga](https://github.com/qemus/qemu-vmvga), whose SVGA3D
commands are rendered by dxvk-native on the host's Vulkan driver. XP then uses
VMware's own SVGA II driver (2D, Direct3D 9, OpenGL ICD), which the user
supplies like the XP media. Direct3D 8/9 ran fast in the spike.

SDL on KMS/DRM did not survive the spike. SDL2's kmsdrm backend needed four
patches (frame re-present, opaque pixel formats, no resize, UI-thread-only 2D
work) and still conflicted with vmvga: DXVK's headless WSI refuses to load
next to an already loaded SDL2, so 3D stayed off. QEMU's GTK display in a bare
Xorg avoids all of it, and with `gl=on` it scales the guest on the GPU like
86Box does (with `gl=off` GTK scaled every frame on the CPU and D3D9
stuttered).

The full reasoning and the measurements are in
[`docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`](../superpowers/specs/2026-10-25-windows-xp-qemu-design.md).

## Decision

- QEMU is built from our fork [`nakioman/qemu-vmvga`](https://github.com/nakioman/qemu-vmvga)
  (branch `retrobox`): QEMU 11.1.1, `i386-softmmu`, GTK + OpenGL, no SDL, with
  dxvk-native in `lib/`. The fork's CI releases a tarball for `retrobox-*`
  tags. `appliance/86box.env` pins it (`QEMU_*`, URL + SHA-256) next to the
  86Box artifacts, and the installer unpacks it to `/opt/qemu-retrobox`,
  exactly like the 86Box AppImage and ROMs. QEMU dlopens DXVK, so it runs with
  `LD_LIBRARY_PATH=/opt/qemu-retrobox/lib`.
- The installer unpacks only what the x86 VM uses (`qemu-system-i386`,
  `qemu-img`, DXVK, `share/qemu` without the other architectures' EDK2
  images): 145 MB instead of 489 MB. The tarball itself is installed
  unmodified so its checksum stays verifiable.
- The host gains a bare Xorg (`xserver-xorg-core` with its built-in
  modesetting driver, `xserver-xorg-input-libinput`, `xinit`) and
  `matchbox-window-manager`, a kiosk WM that honours fullscreen requests. No
  display manager or desktop.
- The appliance has no logind sessions, so Xorg starts through the setuid
  `Xorg.wrap` from `xserver-xorg-legacy`. GTK's dconf gets its session bus
  from `dbus-x11`. `dbus-user-session` is not installed: it pulls in
  `libpam-systemd`, which would start logind sessions and change how
  `retrobox-boot` and 86Box own the console and DRM. The build fails if
  `pam_systemd.so` appears in the target rootfs.
- Vulkan comes from `libvulkan1` + `mesa-vulkan-drivers`, trimmed to Intel's
  ANV driver by dpkg `path-exclude` filters
  (`appliance/debian/dpkg-vulkan-intel-only.cfg`, passed to mmdebstrap).
- KVM loads with `enable_vmware_backdoor=1`
  (`/etc/modprobe.d/retrobox-kvm.conf`); without it the guest driver disables
  Direct3D. `retrobox` joins the `kvm` and `render` groups.
- The target rootfs is built without Recommends, and the root partition grows
  from 2 GiB to 3 GiB for fresh installs, so most of the disk stays on `/data`.

## Consequences

- Root filesystem cost, measured on Debian 13 amd64 without Recommends: Xorg
  ~27 MB, GTK 3 + matchbox + dbus-x11 ~90 MB, trimmed Vulkan ~21 MB (79 MB
  untrimmed), QEMU ~145 MB under `/opt`: about +283 MB in total.
- Reinstalls that preserve `/data` keep their existing partition table and so
  their 2 GiB root; the new stack still fits there.
- The appliance now has two display stacks: 86Box on SDL/KMS and QEMU on
  Xorg/GTK. Phase 2 starts Xorg only while a QEMU VM runs (`xinit`, with
  `/etc/X11/Xwrapper.config` allowing the service user).
- Only Intel GPUs get Vulkan. Another vendor's iGPU needs a filter change in
  `dpkg-vulkan-intel-only.cfg`.
- The guest driver is proprietary and never shipped; without it XP has no 3D.
- qemu-3dfx + WineD3D (`nakioman/qemu-3dfx`) stays as plan B. It requires the
  SDL display, so choosing it later would revisit this ADR.
