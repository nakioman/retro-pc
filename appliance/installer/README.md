# RetroBox USB appliance installer

This directory builds a **bootable USB installer** that installs the RetroBox
Debian appliance onto the target machine's internal HDD/SSD.

The installed system is a **read-only-root** Debian 13 appliance with a mutable
`/data` partition, GRUB in BIOS/legacy MBR mode, SSH maintenance access, and a
Samba scratch share — see [`../README.md`](../README.md) and
[`../filesystem-layout.md`](../filesystem-layout.md).

> **First functional slice.** This implements the end-to-end build → boot →
> safe install → read-only-root path. A few things are intentionally deferred
> and recorded rather than blocking the install — see
> [Scope & deferrals](#scope--deferrals).

## How it works

The build produces **two** root filesystems packed into one hybrid ISO:

1. A small **live installer** rootfs (`live/filesystem.squashfs`) that boots from
   USB and auto-runs [`install-retropc.sh`](install-retropc.sh) on tty1.
2. The **target appliance** rootfs (`install/target-rootfs.squashfs`), built from
   [`../debian/packages.txt`](../debian/packages.txt), carried on the USB and
   extracted onto the internal disk. The install is **offline** — no network is
   needed on the retro PC.

```text
Build USB installer image        (build-usb-installer.sh)
Write image to a USB stick       (dd)
Boot the retro PC from USB       (BIOS/legacy)
Installer auto-starts on tty1    (install-retropc.sh)
Pick the internal disk           (USB device excluded; typed confirmation)
Partition + format               (fresh: MBR p1 /boot, p2 /data)
Preserve existing /data          (reinstall: keep VMs/floppies, rewrite /boot only)
Stage kernel+initrd+squashfs     (offline, from the USB, onto /boot)
Write UUID fstab, users, GRUB    (root locked; retrobox password prompted)
Remove USB and reboot            (target boots the installed appliance)
```

## Build

### CI (recommended)

`.github/workflows/build-usb-installer.yml` runs shellcheck, calls the
`build-retrobox.yml` workflow as a reusable workflow to build and publish the
RetroBox Linux x64 binary, then builds the image on a native Linux runner and
uploads `retropc-installer.iso` as an artifact. Because the installer workflow
invokes the .NET build itself (instead of reusing an older published artifact),
the ISO always embeds the exact code at the tested commit.
Download it from the workflow run, then flash it (below).

### Local (macOS / Docker)

The Debian tooling needs Linux, so build in the privileged builder container.
The image is amd64 (the appliance target); on Apple Silicon add
`--platform linux/amd64` so mmdebstrap builds natively under emulation:

```bash
docker build --platform linux/amd64 -t retropc-builder appliance/installer
docker run --rm --platform linux/amd64 --privileged -v "$PWD:/work" \
    retropc-builder /work/appliance/installer/build-usb-installer.sh
# -> appliance/installer/out/retropc-installer.iso
```

On an amd64 host you can drop `--platform linux/amd64`.

### Local (native Linux)

```bash
sudo apt-get install -y mmdebstrap squashfs-tools xorriso \
    isolinux syslinux-common dosfstools zstd e2fsprogs
sudo bash appliance/installer/build-usb-installer.sh
```

### Embedding the runtime

The build always embeds the published RetroBox binary and downloads the pinned
86Box AppImage and ROM tarball from `appliance/86box.env`, validating both
SHA256 values. To build locally:

```bash
mise run publish-linux-x64   # produces the retrobox linux-x64 single-file binary
RETROBOX_BIN=path/to/retrobox BOX86_APPIMAGE=path/to/86box.AppImage \
    sudo bash appliance/installer/build-usb-installer.sh
```

The publish output also contains `libSystem.IO.Ports.Native.so` (NativeAOT cannot
statically link `System.IO.Ports`); the build stages it next to the binary and
installs it to `/opt/retrobox/` when the publish output provides it. The publish
workflow asserts it exists; an installer built from a stale runtime artifact
warns instead of failing.

The installer copies the runtime to `/opt`, `vms.yaml` to
`/data/retrobox/vms.yaml`, and both complete profiles to `/data/vms`. It does
not create `config.yaml`; that file remains optional runtime state.

## Flash to USB

```bash
# macOS: find the disk with `diskutil list`, unmount, then (BE SURE of the disk):
sudo dd if=appliance/installer/out/retropc-installer.iso of=/dev/rdiskN bs=4m
# Linux:
sudo dd if=appliance/installer/out/retropc-installer.iso of=/dev/sdX bs=4M status=progress
```

## Install onto the retro PC

1. Set the BIOS to boot from USB (legacy/BIOS mode, not UEFI).
2. Boot; the installer starts on the primary display.
3. Choose the internal disk. **The USB installer device is excluded by default**,
   and you must type the exact `ERASE /dev/sdX` confirmation before anything is
   written.
4. Set the `retrobox` password when prompted (used for SSH and `sudo`).
5. Press Enter to reboot **with the USB still inserted** (the live installer runs
   from it). Remove the USB while the machine restarts — at the BIOS/logo screen
   — so it boots from the internal disk.

## Reinstall & data preservation

Re-running the installer over an existing appliance keeps your data by default:

- The `/data` partition is **not** reformatted. Only the `/boot` partition is
  rewritten (still gated by the typed `ERASE /dev/sdX` confirm); the staged
  squashfs image is always overwritten with the one carried on the USB.
- Everything on `/data` survives: VMs (`.vhd`), the VM catalog (`vms.yaml`),
  floppies, snapshots, and Samba scratch.
- The first-boot provisioning marker (`/data/system/first-boot-done`) is
  preserved, so SSH host keys and `machine-id` survive a reinstall.
- OS-managed profile files (`86box.cfg`, shaders) **are** refreshed; `.vhd` and
  `.yaml` files are never overwritten.
- Set `RETROPC_WIPE_DATA=1` to force a full wipe of `/data` on reinstall
  (or answer `n` to the "Preserve /data?" prompt during the install).

## Accounts & maintenance

- `root` is **locked**. `retrobox` is the sole account — the service runtime user
  and the SSH maintenance login, with `sudo`.
- Maintenance over SSH: `ssh retrobox@<ip>` (DHCP; `PermitRootLogin no`).
- The root filesystem is **immutable** (squashfs lower). There is **no**
  `mount -o remount,rw /` path. System files live on the overlayfs upperdir
  under `/data/system/upper/`; treat configuration changes as image changes —
  rebuild and stage a new `root-<ver>.squashfs` on `/boot` to persist them
  across reinstalls. `/data` is always writable.

## Machine selector

Press F12 during the boot window to open the text selector on the appliance
console. Press a VM's displayed number to start it. Press `D`, then a VM number,
to save it as the default and start it immediately. `Esc` cancels; if a default
is already configured, cancellation starts that VM.

Closing a VM returns to the selector so another VM can be started without a
reboot. `Esc` on that returned selector ends the session and returns to the
tty1 login; it does not restart the VM that just closed.

The Plymouth splash stays up until 86Box's first frame: `retrobox boot`
retains it before launching a VM and quits it before the selector, so boot and
86Box loading text never flashes on the terminal.

The floppy daemon re-syncs the drive when a VM starts: once the 86Box
floppy-control socket is ready it sends `STATUS` to the floppy controller and
applies the reported physical floppy, so swaps made while the VM was off are
loaded on power-on.

## Recovery

- Hold **Shift** (or press **Esc**) during boot to reveal the hidden GRUB menu.
- Choose **"RetroBox — recovery (maintenance, no fullscreen VM)"**. It appends
  `retropc.norun=1`, so `retrobox-boot.service` skips the fullscreen VM path and
  leaves a normal login on tty1 (SSH still works).
- The root is truly immutable: there is no `mount -o remount,rw /`. Recovery
  from a broken OS image means booting the USB installer and dropping a
  known-good `root-<ver>.squashfs` + kernel + initrd onto `/boot`, or
  rewriting `/boot/grub/grub.cfg` over SSH to point at a previous kernel.
- SSH host keys and `machine-id` are produced on first boot by
  `retrobox-firstboot.service`; they are regenerated automatically after a
  full wipe of `/data` because the marker `/data/system/first-boot-done`
  disappears with the partition.

## OS update (manual A/B swap)

Drop a new image onto `/boot` over SSH and switch the GRUB default entry.
A/B automation is out of scope for this release; the manual swap is the
contract. See [`../read-only-root.md`](../read-only-root.md) for the detailed
procedure and recovery options.

## Verification

Automated (CI / local):

- `shellcheck -x` on all installer scripts.
- The build asserts the ISO is isohybrid (MBR boot sector), has an El Torito
  boot catalog, that `target-rootfs.squashfs` is valid, and that the expected
  package binaries (`sshd`, `smbd`, `plymouth`, `grub-install`) are present.

Manual (on real hardware / a spare disk):

- Installer auto-starts on tty1; the target list excludes the USB device.
- Installed disk boots without the USB; `ssh retrobox@<ip>` works.
- `/data` is writable; `/etc/fstab` uses UUIDs; root is read-only.
- CD-ROM and ESP8266 device choices are recorded in
  `/data/retrobox/install-report.txt`.
- With a detected CD-ROM drive, the installer configures the first active
  optical slot in each applicable installed profile with its `ioctl://` path;
  templates remain portable. See [`../../docs/cdrom-passthrough.md`](../../docs/cdrom-passthrough.md)
  for target-hardware validation.

## Scope & deferrals

Fully implemented: two-rootfs build + hybrid ISO, safe disk selection,
partition/format, offline kernel+initrd+squashfs staging onto `/boot`,
UUID-based fstab, immutable squashfs root assembled by `live-boot` persistence
into an overlayfs whole-root mount (lower = squashfs, upper + work on
`/data/system/`), `retrobox` account (root locked) with prompted password, SSH,
Samba scratch share, DHCP networking, Plymouth boot splash, GRUB BIOS install
with hidden 1280x960 menu + recovery entry, zram + `/data` swapfile backstop for
the low-RAM machine, the `retrobox-daemon` / `retrobox-boot` /
`retrobox-wifi-firstboot` / `retrobox-firstboot` systemd units, first-boot
SSH host-key + `machine-id` provisioning, and reinstall data preservation
(existing `/data`, `.vhd`, and `.yaml` are kept unless `RETROPC_WIPE_DATA=1`).

Deferred and recorded in `install-report.txt` rather than failing the install:

- **RetroBox binary / 86Box AppImage / ROMs** — required at build time; the
  AppImage and ROM tarball are downloaded and checksum-verified from
  `appliance/86box.env`.
- **ESP8266 serial** — detected when present; otherwise the daemon config
  keeps a documented placeholder path. Electronics validation is tracked in
  #22 / #35.
- **Fullscreen 86Box boot path** — `retrobox boot` is wired via
  `retrobox-boot.service` but the graphics stack lands with #26; until then tty1
  shows the placeholder boot service (use SSH or the recovery entry for a shell).
- **UEFI boot** — BIOS/legacy only for now; the build script leaves a seam for a
  GRUB EFI El-Torito image.
- **Network beyond DHCP** — static addressing / DNS tuning is out of scope.
