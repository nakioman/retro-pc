# Core 2 Duo QEMU Profile

This directory contains the native QEMU profile for the Retro PC Core 2 Duo VM,
the Windows XP machine. `qemu.args` is the source of truth for hardware
selection (ADR 0009). Installing and setting up XP is covered in
[`docs/windows-xp.md`](../../../../docs/windows-xp.md).

## Hardware

- Machine: `pc` (i440FX + PIIX), KVM, VMware backdoor port (`vmport=on`)
- CPU: `core2duo`, 2 vCPUs (KVM runs them at host speed)
- RAM: 3 GB
- Video: VMware SVGA II (`vmvga,hwcursor=off,gbobjects=off,vgpu=9`), 3D
  through dxvk-native and the host's Vulkan driver
- Sound: AC97 on the host's ALSA output
- Network: Realtek RTL8139 on user-mode NAT (outbound only)
- Input: PS/2 keyboard and mouse
- Storage: IDE (XP has no inbox AHCI driver), hard disk `hdd.qcow2`, 120 GB
  qcow2
- Floppy: one empty 1.44 MB drive
- CD-ROM: the host's physical drive on IDE index 2 (secondary master), added
  by the launcher when the installer detected one, plus an empty CD drive on
  index 3 for ISOs
- BIOS: SeaBIOS; boots the disk first, then the CD; ESC during the 3-second
  prompt opens the boot menu
- Expected OS: Windows XP SP3, installed manually from the real CD

## Launch

`retrobox boot` runs `/opt/qemu-retrobox/bin/qemu-system-i386` with this
directory as its working directory, so the relative disk path works:

```text
-drive
file=hdd.qcow2,if=ide,index=0,media=disk,format=qcow2
```

The launcher appends only host-specific arguments: the fullscreen GTK display
(`-display gtk,gl=on,full-screen=on,...`, `-monitor none -serial none
-parallel none`), the QMP socket, and the detected optical drive on IDE index
2. Keep index 2 free in `qemu.args`. See
[`docs/vm-profiles.md`](../../../../docs/vm-profiles.md).

Direct3D needs KVM's VMware backdoor (`kvm.enable_vmware_backdoor=1`), which
the appliance sets in `/etc/modprobe.d/retrobox-kvm.conf`.

## Blank Disk

`hdd.qcow2` is intentionally blank and is not committed to Git. The disk is
declared in `vms.yaml`:

```yaml
disks:
  - file: hdd.qcow2
    size: 120G
    format: qcow2
```

and the installer creates it with `retrobox vm create-disks`, which runs
`/opt/qemu-retrobox/bin/qemu-img`. The qcow2 file is preallocated (`preallocation=falloc`), so the full 120 GB
is reserved on `/data` up front.
Do not replace this file with an installed OS image in the repository.

## Generated State

QEMU keeps no state next to the profile besides the disk: SeaBIOS settings and
the clock (`-rtc base=localtime`) are not persisted.

## Media Policy

This profile includes no Windows media, drivers (Realtek AC97, VMware SVGA II),
games, floppy images, CD-ROM images, or other copyrighted media.
