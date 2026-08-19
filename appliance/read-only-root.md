# Read-only root for the installed appliance

This document is the deliverable for issue #30 (prototype) and the
implementation reference for issue #43 (immutable squashfs root + overlayfs).
The runtime model of the installed RetroBox appliance is:

```text
GRUB (BIOS/MBR, on /boot, partition 1)
  -> vmlinuz + initrd.img (on /boot)
  -> live-boot: boot=live persistence persistence-label=retropc-data union=overlay
  -> mount /boot  (p1, ext4, rw — needed to drop a new OS image)
  -> mount /data  (p2, ext4, rw — persistence partition)
  -> assemble overlay root:
       lower = /boot/root-<ver>.squashfs
       upper = /data/system/upper
       work  = /data/system/.overlay.work
  -> switch_root into the overlay
```

After boot the running system looks like this:

```text
$ mount | grep ' / '
overlay on / type overlay (lowerdir=...,upperdir=/data/system/upper,workdir=/data/system/.overlay.work)

$ mount | grep -E '/boot|/data '
/dev/sda1 on /boot type ext4 (rw,nosuid,nodev,noatime,errors=remount-ro)
/dev/sda2 on /data type ext4 (rw,nosuid,nodev,noatime)
```

The root is **truly immutable**: the lower directory is a squashfs inside
`/boot` and cannot be remounted read-write. `/etc`, `/var`, and the rest of
the root are layered over that squashfs by the overlay.

## Why squashfs + overlayfs

The previous model (ext4 root mounted `ro` + an fstab-driven overlayfs mount
on `/var`) kept the root *conventionally* read-only. That left several
papercuts: `/etc` was effectively read-only (host keys and `machine-id` had
to be baked at install time), `grub-common.service` had to be masked
because writing `grubenv` failed on the ro root, and `mount -o remount,rw /`
was a real escape hatch.

Squashfs gives a **truly immutable lower** — there is nothing to remount.
Overlayfs gives a **persistent writable layer** that covers `/etc`, `/var`,
and `/opt` uniformly, so the whole class of ro-root papercuts disappears.
As a side effect, the OS image is compressed (~2–3× smaller than an
uncompressed ext4 root) and corruption on flaky storage only damages the
overlay.

## Partition layout

Two MBR partitions, BIOS/legacy boot:

| Part | FS | Size | Mount at runtime | Purpose |
|------|----|------|------------------|---------|
| p1 | ext4 | `RETROPC_BOOT_MIB` MiB (default 512) | `/boot` (rw) | GRUB, kernel, initrd, squashfs image; rw so a new image can be dropped in |
| p2 | ext4 | rest of the disk | `/data` (rw) | overlay upperdir + persistent application state |

The installer (`appliance/installer/install-retropc.sh`) creates this layout
in `appliance/installer/lib/partition.sh`. `RETROPC_BOOT_MIB=512` is enough
for the current kernel, initrd, squashfs, and one A/B squashfs copy with
margin to spare.

Labels:

- p1: `retropc-boot` (used by `search --no-floppy --fs-uuid --set=root` in
  GRUB; the UUID is what GRUB actually depends on, the label is a hint).
- p2: `retropc-data` (also the `persistence-label` value in the GRUB
  cmdline, so `live-boot` knows where to find the upper/work dirs).

`live-boot` creates the overlay upper/work directories on first boot:

```text
/data/system/
  upper/            # overlay upperdir (live-boot creates this)
  .overlay.work/    # overlay workdir (live-boot creates this)
  first-boot-done   # marker written by retrobox-firstboot.service
  swapfile          # disk swap backstop
  retrobox/         # persistent retrobox state (catalogs, install report)
  vms/              # VM profiles + disks
  floppies/         # scratch + cataloged
  snapshots/        # VM snapshots
  home/             # retrobox $HOME
  wifi.conf         # plain-text SSID/PSK (0600, root:root)
  wifi-configured   # marker set by retrobox-wifi-firstboot
```

## GRUB cmdline

The installed GRUB entry (and the recovery entry) uses:

```text
root=UUID=<p1-uuid> boot=live components persistence persistence-label=retropc-data union=overlay quiet video=1280x960@60
```

- `boot=live` enables the live-boot scripts inside the initramfs.
- `persistence` activates the persistence hook (scan labelled ext4
  partitions, mount them, and apply their overlay config).
- `persistence-label=retropc-data` is the partition `live-boot` looks for.
- `union=overlay` selects overlayfs as the union filesystem (the default
  since Debian bookworm; declared explicitly to avoid surprises on trixie).
- `components` matches the live-boot behavior already used by the USB
  installer.

The recovery entry is the same cmdline with `retropc.norun=1` appended, so
`retrobox-boot.service` skips the fullscreen VM path and a normal login
remains on tty1.

## First-boot provisioning

Three things used to be generated in the installer chroot and are now done
on first boot, on the overlay:

1. SSH host keys (`ssh-keygen -A`).
2. `machine-id` (`systemd-machine-id-setup`).
3. Anything else that depends on a per-machine, persistent identity.

Implemented by `appliance/installer/payload/first-boot/retrobox-firstboot.service`
(oneshot) + `retrobox-firstboot.sh`. The service is gated by
`ConditionPathExists=!/data/system/first-boot-done` and writes that marker
once it finishes. `enable_unit` is called by `lib/services.sh`; nothing
else wires it up.

The `retrobox` password stays an install-time concern: SSH must be usable
on the first boot, and there is no interactive first-boot prompt that would
let the user set it later without SSH.

## What this obsoletes

These workarounds from the ext4-ro model are gone:

- `systemctl mask grub-common.service` (grub-common can now write `grubenv`).
- `GRUB_RECORDFAIL_TIMEOUT=0` in `/etc/default/grub`.
- `ssh-keygen -A` and `systemd-machine-id-setup` in the installer chroot.
- The `overlay /var overlay ...` line in the generated `/etc/fstab`.
- The `FRAMEBUFFER=y` initramfs hint (Plymouth runs from the initramfs that
  ships inside the squashfs; this hint was for the chroot-generated
  initramfs and is no longer needed).
- The "edit `/etc` via `mount -o remount,rw /`" recovery path.

## OS update / A/B swap (manual, out of scope for automation)

To update the OS image:

1. Download the new `target-rootfs.squashfs`, `vmlinuz-<ver>`, and
   `initrd.img-<ver>` (the build artifact bundles these).
2. Copy them into `/boot` via SSH:
   ```bash
   ssh retrobox@<ip>
   sudo cp root-<newver>.squashfs /boot/
   sudo cp vmlinuz-<newver> initrd.img-<newver> /boot/
   ```
3. Either edit `/boot/grub/grub.cfg` to point the default menu entry at
   the new kernel/initrd (keeping the old entry as the fallback), or use
   `grub-set-default` if a `grubenv` chain is configured.
4. Reboot. The overlay upper is preserved across the swap, so all
   `/data`-rooted state survives.

If the new image fails to boot, hold **Shift** (or press **Esc**) at boot
to reveal the GRUB menu and pick the previous kernel/initrd.

## Recovery

- **GRUB menu**: hold **Shift** (or press **Esc**) during boot.
- **Recovery entry**: appends `retropc.norun=1`; fullscreen VM path is
  skipped, login remains on tty1.
- **Cannot `mount -o remount,rw /`**: by design. `/etc` lives on the
  overlay; correct a broken configuration by rebooting into the recovery
  entry, editing via SSH, or — if `/etc` is unrecoverable — replacing the
  squashfs on `/boot` with a known-good one.
- **USB installer rescue**: boot the USB installer, choose the same disk,
  confirm `ERASE`, and reinstall. The default preserves `/data`; set
  `RETROPC_WIPE_DATA=1` for a full wipe.

## Acceptance checklist

- [ ] `mount | grep ' / '` shows an overlay whose lower is the squashfs.
- [ ] `/data` is writable; the overlay upper persists across reboots.
- [ ] `systemctl is-active grub-common.service` returns `active` (no longer
      masked).
- [ ] `/etc/fstab` contains entries only for `/boot`, `/data`, `/tmp`.
- [ ] No failed units on a normal boot.
- [ ] SSH, Samba scratch, and `retrobox` `/data` access all work.
- [ ] OS update by replacing the `.squashfs` is documented.
- [ ] Recovery/remount procedure documented.

See [ADR 0006](../decisions/0006-squashfs-overlay-root.md) for the design
record and rationale.