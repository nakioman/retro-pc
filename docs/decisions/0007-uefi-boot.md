# 0007. UEFI boot with hybrid BIOS/UEFI installer image

Date: 2026-08-24
Status: Accepted

## Context

The appliance originally installed as a BIOS/legacy system: MBR partition table,
no EFI System Partition, GRUB installed with `--target=i386-pc` to the MBR.
The installer ISO was ISOLINUX + isohybrid MBR. A `docs/decisions/` deferral
note flagged the missing UEFI path: "the build script leaves a seam for a GRUB
EFI El-Torito image".

New target hardware no longer ships legacy BIOS boot, only UEFI. The legacy
BIOS support in the field still exists (older machines already running the
appliance) and re-flashing a working installation should not be required for
hardware that already boots.

## Decision

The appliance target install is **GPT with a dedicated EFI System Partition**:

```text
p1  FAT32  /boot/efi  512 MiB  (label retropc-esp)
p2  ext4   /          ~2 GiB   (label retropc-root, read-only at runtime)
p3  ext4   /data      rest     (label retropc-data)
```

GRUB is installed as **GRUB-EFI**:

```sh
grub-install --target=x86_64-efi --efi-directory=/boot/efi \
    --bootloader-id=RetroBox --removable --no-nvram --recheck
```

`--removable` is load-bearing rather than cosmetic. Without it GRUB installs to
`EFI/RetroBox/grubx64.efi` and depends on an NVRAM `Boot####` entry written by
`efibootmgr` — which the installer cannot write: the target chroot gets a fresh
`sysfs` that carries no `efivarfs` submount, and the installer USB may itself
have booted in legacy mode, where EFI variables do not exist at all.
`grub-install` only *warns* in that case, so the install would appear to succeed
and leave a disk no firmware can boot. `--removable` writes the UEFI fallback
path `EFI/BOOT/BOOTX64.EFI`, which every implementation boots without NVRAM;
`--no-nvram` makes the impossible variable write an explicit no-op instead of a
warning scrolling past on the install console. The installer still bind-mounts
`efivarfs` into the chroot when the live system booted via UEFI, so
firmware-side tooling works, but correctness does not depend on it. The recovery
entry is rewritten for `insmod part_gpt`.

The installer ISO is a **hybrid BIOS/UEFI image**: a single `dd`-able ISO with
two El-Torito boot entries. The first is ISOLINUX with the legacy isohybrid
MBR. The second is `boot/grub/efi.img` — a **FAT filesystem image** containing
`EFI/BOOT/BOOTX64.EFI` — attached with `-eltorito-alt-boot -e` and exposed to
UEFI firmware via `-isohybrid-gpt-basdat`, which maps that image's extent to a
GPT partition. The FAT wrapper is required, not optional: `-e` registers a
*filesystem* image, and `-isohybrid-gpt-basdat` publishes it as a partition the
firmware mounts to find `/EFI/BOOT/BOOTX64.EFI` inside. Pointing `-e` at the raw
PE binary yields an ISO that passes every structural check and still offers no
boot device on UEFI. A plain copy of the loader is also placed at
`EFI/BOOT/BOOTX64.EFI` in the ISO9660 tree for firmware that browses the
filesystem instead of the GPT.

The GRUB-EFI core image is built with an **embedded config**, because when
firmware loads `BOOTX64.EFI` GRUB's `$root` is the ESP, not the ISO9660 volume
that carries the kernel — the built-in prefix alone can never find
`/boot/grub/grub.cfg`. The embedded config locates the ISO by a file only it
has, repoints `$root`/`$prefix`, and hands over:

```sh
search --no-floppy --set=root --file /live/vmlinuz
set prefix=($root)/boot/grub
configfile ($root)/boot/grub/grub.cfg
```

The module set must therefore include `iso9660` (to read the kernel and initrd
off the ISO) and `search_fs_file` (to back `search --file`) alongside the
partition and FAT modules. The same USB works on either firmware.

A legacy BIOS install on the same disk is migrated explicitly: the installer
detects an MBR partition table with a `retropc-data` label, asks for a typed
`MIGRATE` confirmation (or `RETROPC_MIGRATE_BIOS_TO_UEFI=1`), stages `/data`
off the disk, repartitions as GPT, formats root + ESP, and restores `/data`
to the new p3. The migration is gated — it never auto-runs.

Staging is **sized and validated before anything destructive happens**. The
default staging path is under `/var/tmp`, which on the live-boot installer is
RAM: copying tens of GiB of VMs there exhausts memory and kills the machine
*after* the partition table has been rewritten, with the only copy of `/data` in
the RAM that just died. So the installer refuses RAM-backed staging unless
`RETROPC_MIGRATE_ALLOW_RAM_STAGING=1`, requires
`RETROPC_MIGRATE_STAGING_DIR=/path/on/external/storage` otherwise, and checks
free space against actual `/data` usage while the legacy partition is still
mounted. Past the point of no return every failure keeps the staged copy and
reports its location rather than deleting it.

The fstab gains a `/boot/efi` vfat entry keyed by UUID, with **fsck pass 0**:
the ESP is written only by `grub-install` at install time, and a nonzero pass
would require `fsck.vfat` inside the minimal image. SSH host keys and machine-id
are still generated into the image at install time (no new state-machine files).
The target package manifest swaps `grub-pc` for `grub-efi-amd64` and adds
`efibootmgr`. The live installer manifest swaps `grub-pc-bin` for
`grub-efi-amd64-bin`; the *build host* additionally needs `mtools` and
`dosfstools` to construct the FAT `efi.img`.

## Consequences

- The same USB image boots on legacy BIOS and modern UEFI hardware, so a
  technician carrying one stick can install either.
- The MBR→GPT migration path is destructive of the partition table but
  preserves `/data` (rsync stage-and-restore). VMs, floppies, and catalogs
  survive a BIOS→UEFI conversion; nothing else is touched.
- The migration needs somewhere to put `/data` that is not RAM, so on a
  single-disk machine it requires attached external storage
  (`RETROPC_MIGRATE_STAGING_DIR`). This is a real operational constraint, not a
  detail: an in-place MBR→GPT conversion was rejected because the backup GPT
  header lands in the last sectors of the disk, which the existing `/data`
  filesystem occupies.
- A failed or declined migration aborts the install rather than corrupting the
  legacy disk; a future boot on legacy firmware will still see the original
  BIOS install intact.
- The appliance boots in non-Secure-Boot UEFI mode. A signed shim is not
  staged; Secure-Boot-enforced firmware must either disable Secure Boot or
  gain a shim in a follow-up ADR.
- `boot/grub/grub.cfg` continues to live on the read-only root and is
  regenerated by `update-grub`; the GRUB-EFI binary in the ESP is
  re-installed by `grub-install` on every install but not on every boot.
- Reinstall over an existing UEFI install reuses all three partitions and
  reformats only root + ESP, preserving `/data` byte-for-byte (same contract
  as the legacy path; see ADR `0002`).
- The ISO build asserts what actually matters, not just structure: the El-Torito
  catalog must hold two entries, the referenced EFI image must be a FAT
  filesystem containing `EFI/BOOT/BOOTX64.EFI`, and the ISO must carry a GPT in
  its system area. A path registered in the catalog proves nothing about
  bootability, which is exactly how a broken hybrid ISO ships green.
