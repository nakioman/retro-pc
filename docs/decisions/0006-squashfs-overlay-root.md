# 0006. Immutable squashfs root + overlayfs for the installed appliance

Date: 2026-08-18
Status: Accepted
Supersedes: the runtime model described in [ADR 0002](0002-read-only-root-and-data.md)
(the read-only principle stands; the *mechanism* changes from ext4-ro+`/var`-overlay
to squashfs+whole-root overlay).

## Context

The installed appliance was shipping with an ext4 root mounted `ro`, plus an
fstab-driven overlayfs mount on `/var` whose upperdir lived under
`/data/system/var`. That kept `/var` (logs, Samba state, DHCP leases, ALSA)
writable while the root stayed read-only, but it left a class of papercuts:

- `/etc` was effectively read-only, so SSH host keys and `machine-id` were
  generated at install time as a workaround.
- `grub-common.service` ("Record successful boot for GRUB") failed writing
  `grubenv` on the ro root and had to be masked alongside
  `GRUB_RECORDFAIL_TIMEOUT=0`.
- Maintenance edits to `/etc` required `mount -o remount,rw /`.
- The root was only *conventionally* read-only — it could be remounted rw.

Tracked as [issue #43](https://github.com/nakioman/retro-pc/issues/43). The
proposed mechanism is an **immutable squashfs root** with **overlayfs** as the
writable layer, backed by `/data`, assembled by `live-boot` persistence at boot.

## Decision

The installed appliance boots from an immutable squashfs root via overlayfs.
The disk layout is two MBR partitions:

```text
p1  ext4  ~512 MiB  /boot   kernel, initrd, GRUB, root-<ver>.squashfs
                            (rw at runtime so a new image can be dropped in)
p2  ext4  rest      /data   overlay upperdir + persistent application state
```

The boot sequence:

```text
GRUB (BIOS/MBR, p1) -> vmlinuz + initrd.img (p1)
  -> live-boot reads cmdline: boot=live persistence persistence-label=retropc-data union=overlay
  -> mounts p1 -> /boot
  -> mounts p2 -> /data
  -> assembles overlay: lowerdir=root-<ver>.squashfs (mounted from /boot),
                        upperdir=/data/system/upper,
                        workdir=/data/system/.overlay.work
  -> switch_root into the overlay
```

Key consequences of the chosen mechanism:

- The squashfs is **truly immutable**: there is no `mount -o remount,rw /` path.
  Maintenance edits live entirely on the overlay.
- We **reuse `live-boot` persistence** (already bundled on the live medium and
  understood by the USB installer) instead of writing a custom
  initramfs-tools hook. The installed system is, in effect, the live system
  made persistent.
- Per-machine configuration that previously happened in the installer chroot
  — SSH host keys, `machine-id` — moves to a **first-boot provisioning
  service** (`retrobox-firstboot.service`) that runs once on the overlay,
  gated by `/data/system/first-boot-done`. The retrobox password stays at
  install time so SSH is usable on first boot.
- `/data/system/` is now populated by `live-boot` on the very first boot
  (`upper/`, `.overlay.work/`, `var/`, `.var.work/`). The installer no longer
  pre-creates `/data/system/var` and `/data/system/.var.work`.
- The fstab on the target only contains `/boot`, `/data`, `/tmp`. There is no
  `/var` overlay line — `live-boot` mounts `/` itself inside the initramfs.

OS update path: drop a new `root-<newver>.squashfs` (and matching kernel +
initrd) into `/boot` via SSH/`scp`, then update the GRUB default entry. A/B
slot automation is **out of scope** for this change; the documented manual
swap is the contract until a follow-up lands.

## Consequences

- All `grub-common.service` workarounds go away: `GRUB_RECORDFAIL_TIMEOUT=0`
  is dropped from `/etc/default/grub`, and the `systemctl mask
  grub-common.service` line is removed from `lib/grub-install.sh`.
- `ssh-keygen -A` and `systemd-machine-id-setup` move out of the installer
  chroot and into `retrobox-firstboot.service` on the running overlay. The
  installer no longer mutates `/etc` to work around its own ro mount.
- `update-initramfs` in the target chroot is no longer required: the
  initramfs the system boots lives in p1 alongside the kernel that produced
  it; both are part of the immutable image.
- `RETROPC_ROOT_GIB` is replaced by `RETROPC_BOOT_MIB` (default 512). The
  p1 partition must be large enough for kernel + initrd + squashfs + a future
  A/B squashfs copy; 512 MiB fits the current image with margin.
- Reinstall data preservation now keeps **both** p1 and p2 partitions by
  default. The installer only rewrites the squashfs + kernel/initrd on p1
  and overwrites `/boot/grub/grub.cfg`. The typed `ERASE <disk>` confirmation
  is still required.
- `live-boot` is a known quantity on the live medium; using it as the
  installed OS initramfs is an established pattern (Debian's persistence
  model), but it does mean the kernel/initrd pair shipped on p1 must match
  the version compiled into the target squashfs — handled by copying kernel
  and initrd out of the target rootfs in `build-usb-installer.sh`.
- Recovery changes: there is no `mount -o remount,rw /` — recovery means
  booting the USB installer and dropping a new squashfs on p1, or rewriting
  `/boot/grub/grub.cfg` over SSH to point at a previous kernel/squashfs.

## Alternatives considered

- Custom `initramfs-tools` hook (dracut-style) instead of `live-boot`:
  rejected as the default because `live-boot` already handles persistence,
  overlay assembly, and `switch_root` for us; the work to reproduce it is
  large and offers little upside. The custom hook remains an escape hatch if
  `live-boot` proves awkward as the installed initramfs.
- Keep ext4-ro + extend `/var` overlay to cover `/etc` as well: rejected
  because `/etc` is read during early boot, before an fstab-mounted overlay
  can apply. Doing an `/etc` overlay correctly requires assembling it in the
  initramfs — which is exactly what squashfs+overlay whole-root gives us.
- A/B dual-slot updates with a userspace bootloader: out of scope; the manual
  GRUB-default swap is the contract for this change.