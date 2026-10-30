# Windows XP on the Core 2 Duo VM

The Core 2 Duo VM (`core2duo`, profile in
[`appliance/installer/payload/profiles/core2duo`](../appliance/installer/payload/profiles/core2duo/README.md))
is RetroBox's Windows XP machine. Unlike the 86Box profiles it runs on QEMU
with KVM, so XP-era games run at host speed with Direct3D through the VMware
SVGA II device. This guide covers installing XP and its drivers. The profile
and launcher are described in [`vm-profiles.md`](vm-profiles.md); the design
is in [`superpowers/specs/2026-10-25-windows-xp-qemu-design.md`](superpowers/specs/2026-10-25-windows-xp-qemu-design.md).

RetroBox ships no Windows media and no guest drivers. You need:

- a Windows XP SP3 (32-bit) CD and its product key;
- the Realtek AC97 audio driver for Windows XP;
- the VMware SVGA II display driver for Windows XP: the `SVGA2_X86_WINXP`
  folder from a VMware Tools release that still supports XP;
- Intel VT-x enabled in the motherboard firmware (KVM refuses to start the VM
  without it).

## Getting files into the guest

The VM has the physical CD-ROM drive (when the installer detected one), an
empty floppy drive, and outbound networking. The simplest way to bring the
drivers in is a CD-R with both driver folders: insert it **before** starting
the VM (see [Known issues](#known-issues) about swapping discs). Once the
network works you can also download files or copy them from a LAN share
inside XP.

## Installing XP SP3

1. Put the XP CD in the physical drive.
2. Start the Core 2 Duo VM from the selector (press F12 at boot).
3. SeaBIOS tries the hard disk first, then the CD. The new disk is blank, so
   the CD boots and XP setup starts. To pick a device by hand, press ESC
   during the 3-second prompt at power-on.
4. In text-mode setup, install to the 120 GB disk (`hdd.qcow2`, created by
   the installer) and format it NTFS. Setup sees a plain IDE disk; no F6
   storage driver is needed.
5. After setup reboots, SeaBIOS boots the hard disk, so the CD's "Press any
   key to boot from CD" never takes over again. Leave the CD in the drive:
   the graphical part of setup still reads it.
6. Network: the Realtek RTL8139 works with XP's own driver and gets an
   address by DHCP from QEMU's user-mode NAT (`10.0.2.x`). XP reaches the LAN
   and the internet; nothing outside can connect to it, which suits an
   unpatched XP.

To return to the selector, shut XP down: QEMU exits and the selector comes
back, as with 86Box.

## Drivers

### Display: VMware SVGA II

Until this driver is installed XP runs on its generic VGA driver at 640x480
and 16 colors, without Direct3D.

1. Open Device Manager and find the display adapter under "Display adapters"
   or "Other devices" ("Video Controller (VGA Compatible)").
2. Update Driver > "No, not this time" > "Install from a list or specific
   location" > "Don't search. I will choose the driver to install" >
   **Have Disk**.
3. Browse to the `SVGA2_X86_WINXP` folder and pick its `.inf`. Accept the
   unsigned driver warning.
4. Reboot, then set the resolution in Display Properties.
5. Check `dxdiag`: on the Display tab, DirectDraw and Direct3D acceleration
   must be enabled and the D3D8/D3D9 tests must pass.

### Audio: Realtek AC97

Install the Realtek AC97 driver for XP (setup program or Device Manager >
"Multimedia Audio Controller" > Update Driver). Sound plays through the
host's ALSA output, the same one 86Box uses.

## Display modes and output

- **Color depth:** the desktop runs at 32-bit color; the VMware driver lists
  32-bit modes. RetroBox's QEMU build adds 15/16 bpp support to the vmvga
  device for games that switch to 16-bit fullscreen modes; this is still being
  validated (in the spike, DirectX 7's 640x480x16 test failed). If a game
  fails to start in 16-bit color, look for a 32-bit option in its settings.
- **Resolution:** the virtual card has no monitor EDID, so XP shows "Default
  Monitor". Modes beyond 1024x768 are offered and work, but 1024x768,
  800x600 and 640x480 match the screen best.
- **4:3 output:** QEMU runs fullscreen and scales the guest on the GPU
  (`gl=on`), always to a 4:3 area, like a CRT on the panel. Pick 4:3 modes
  for square pixels; wide modes are squeezed.

## Direct3D and the KVM VMware backdoor

The VMware driver talks to the host through VMware's backdoor I/O port. The
profile enables the port (`-machine ...,vmport=on`), and KVM must let the
guest use it: the appliance loads KVM with `enable_vmware_backdoor=1`
(`/etc/modprobe.d/retrobox-kvm.conf`). Without it the driver still shows a
desktop but disables Direct3D. To check on the host:

```sh
cat /sys/module/kvm/parameters/enable_vmware_backdoor   # must print Y
```

OpenGL games use VMware's OpenGL driver, installed with the display driver.
Glide is not supported.

## Input

QEMU grabs the mouse and keyboard while the pointer is over its window, which
is always in fullscreen. **Ctrl+Alt+G** releases the grab; click in the VM to
grab again. The mouse is a relative PS/2 mouse, as games of the era expect,
so XP draws its own pointer.

## Adding the VM to an existing appliance

A reinstall that preserves `/data` keeps your `vms.yaml`, so the Core 2 Duo
profile is staged in `/data/vms/core2duo` but not listed in the selector.
Add the entry to `/data/retrobox/vms.yaml`:

```yaml
  core2duo:
    label: "Core 2 Duo"
    path: "/data/vms/core2duo"
    engine: qemu
    disks:
      - file: hdd.qcow2
        size: 120G
        format: qcow2
```

and create its disk as the `retrobox` user:

```sh
/opt/retrobox/retrobox vm create-disks core2duo
```

## Known issues

- **Disc swaps:** QEMU does not notice a disc swapped in the physical drive;
  the guest keeps seeing the old disc (or none) until the VM restarts. To
  change discs, shut XP down, swap, and start the VM again. Automatic refresh
  is planned (Phase 4 of the design).
- **3DMark03 "Mother Nature"** flickers.
- The guest vCPU spins at 100% during 3D: the XP driver polls the device
  instead of waiting for interrupts.
- CD audio (Redbook) tracks do not play through the emulated IDE drive.
- The second CD drive (IDE index 3) and the floppy drive start empty; loading
  images into them (and NFC floppies) comes with later phases.
- Hosting LAN games needs port forwarding or a bridge, which the profile does
  not set up.
