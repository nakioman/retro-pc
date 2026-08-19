# Debian Appliance Filesystem Layout

## Mount and mutability model

The appliance boots from an **immutable squashfs root** assembled by
`live-boot` persistence into an overlayfs whole-root mount. `/boot` holds the
kernel, initrd, and the squashfs image itself; `/data` is the persistent
writable partition that backs the overlay upperdir and holds application
state. The root is truly immutable: there is no `mount -o remount,rw /` path.

```text
/                    overlayfs (lower = squashfs, upper + work on /data)
/boot    /dev/sda1   ext4  rw — kernel, initrd, GRUB, root-<ver>.squashfs
/data    /dev/sda2   ext4  rw — overlay upperdir + application state
/tmp                 tmpfs (volatile)
```

The operating system, installed packages, `/opt/retrobox/retrobox`, 86Box's
AppImage, and `/opt/86Box/roms` are part of the deployed system image. The
overlay makes `/etc` and `/var` writable on top of the squashfs without
exposing the lower to accidental mutation.

`/data` is the persistent writable filesystem for Retro PC state:

```text
/data/
  retrobox/
    vms.yaml
    install-report.txt
  vms/
    386sx16/
      86box.cfg
      HDD.vhd
      shaders/syncmaster3.glsl
    pentium100/
      86box.cfg
      HDD.vhd
      shaders/syncmaster3.glsl
  floppies/
    scratch/
    cataloged/
  snapshots/
    386sx16/
    pentium100/
  system/                    # created by live-boot at first boot
    upper/                   # overlayfs upperdir (live-boot)
    .overlay.work/           # overlayfs workdir (live-boot)
    first-boot-done          # marker set by retrobox-firstboot.service
    swapfile                 # disk swap backstop (see below)
    wifi.conf                # SSID/PSK from WiFi first-boot (root:root 0600)
    wifi-configured          # marker set after the first successful WiFi prompt
```

`/data/retrobox/install-report.txt` is written by the USB installer and records
the detected target disk, CD-ROM, and ESP8266 serial device (or explicit
diagnostic placeholders when a device is absent). When a CD-ROM is detected,
the installer writes its `ioctl://` path only to the first active optical slot
of each applicable installed `/data/vms/*/86box.cfg`; it leaves profiles
unchanged when no drive is found.

`/data/system/` is created by `live-boot` on the first boot (the `upper/` and
`.overlay.work/` overlay directories) and by the running services
(`wifi.conf`, `first-boot-done`, etc.). It is system state, not user-facing
application data, and network shares never expose it.

### WiFi credentials

`/data/system/wifi.conf` — plain-text `SSID=`/`PSK=` lines (root:root, 0600),
written by `retrobox-wifi-firstboot.service` on the first boot that detects a
`wl*` interface. `/data/system/wifi-configured` (0644) marks that the prompt has
run; removing it re-arms the prompt. The same unit materializes the
wpa_supplicant config, the transient `wpa-wifi.service`, and the networkd
config into `/run` every boot, so the overlay is only used to persist the
plain-text credentials, not to materialize runtime config.

### First-boot provisioning

`retrobox-firstboot.service` runs once on first boot to generate the
per-machine identity that the installer used to bake into the image:

- SSH host keys (`ssh-keygen -A`).
- `machine-id` (`systemd-machine-id-setup`).

The service writes `/data/system/first-boot-done` and never runs again.

The `retrobox` account password stays an install-time concern — SSH must be
usable on the first boot, and there is no interactive first-boot prompt
without SSH.

The current `retrobox` code uses these paths directly:

- `/data/retrobox` is the YAML catalog root.
- `/data/floppies/scratch` is the import source directory.
- `/data/floppies/cataloged` stores imported floppy images.
- VM paths are under `/data/vms`.
- The immutable runtime is `/opt/retrobox/retrobox`, `/opt/86Box/86box.AppImage`,
  and `/opt/86Box/roms`.
- `config.yaml` is optional runtime state. The installer seeds only `vms.yaml`
  and does not choose a default VM.

The directories may be created during appliance provisioning. The `retrobox`
system user/group should own `/data/retrobox` and the cataloged application
state. Samba write access should be restricted to the `scratch` directory;
network users must not be granted write access to catalogs, VM disks, or
snapshots.

## Read-only root exceptions

The following paths are runtime locations and must not be treated as persistent
application storage:

```text
/run/retrobox/86box-floppy.sock
/run/systemd/network/30-wifi.network
/run/
/tmp/
```

The USB installer implements this contract as follows:

- The root `/` is an overlayfs mount assembled by `live-boot` in the initramfs.
  The lower directory is the squashfs on `/boot`; the upper and work
  directories live under `/data/system/` and are created by `live-boot` on
  the first boot.
- `/boot` is an ext4 partition labeled `retropc-boot` (rw, `errors=remount-ro`).
  It is rw at runtime only so a new OS image can be dropped in; the squashfs
  itself is read-only.
- `/data` is an ext4 partition labeled `retropc-data` (rw, nosuid, nodev,
  noatime). It is the only persistent writable partition and carries the
  overlay upper/work dirs plus application state.
- `/tmp` is `tmpfs` (volatile).
- `/etc` is **writable on the overlay**. There is no longer a ro-`/etc`
  workaround: SSH host keys and `machine-id` are produced on first boot by
  `retrobox-firstboot.service` (see [First-boot provisioning](#first-boot-provisioning)).
- Swap for the low-RAM machine: **zram** (compressed RAM swap, preferred) plus a
  modest **`/data/swapfile`** as a lower-priority OOM backstop. The root stays
  read-only; the swapfile lives on writable `/data`.

The following system areas remain read-only at runtime (carried by the
squashfs lower):

```text
/usr/
/bin/
/sbin/
/lib/
/opt/
```

Mounting any of these read-write is **not supported**. To change the OS,
replace the squashfs on `/boot` — see
[`appliance/read-only-root.md`](read-only-root.md) for the documented manual
A/B swap.

## Runtime ownership and access

The eventual services should run with the least privilege compatible with the
hardware:

- `retrobox` owns and edits its YAML catalogs under `/data/retrobox`.
- `retrobox` reads VM and cataloged floppy assets under `/data` and belongs to
  Linux's `cdrom` group for physical optical-drive access.
- `retrobox` writes imported assets to `/data/floppies/cataloged` only through
  the import workflow.
- administrators use SSH and `sudo` for maintenance and diagnostics.
- Samba provides a restricted drop folder at
  `/data/floppies/scratch`, not a general `/data` share.
- the 86Box control socket is created at
  `/run/retrobox/86box-floppy.sock` by the eventual runtime service.

Audio, input, serial/USB, and graphics device access still depends on the
physical hardware and runtime integration. `cdrom` group membership is the
baseline for physical optical-drive access; add a device-specific rule only if
target validation proves it necessary.

## Backup boundary

Back up `/data` as application state. At minimum this includes the YAML files,
VM disks, cataloged floppy images, and snapshots. The immutable root image,
package manifest, and AppImage are deployment artifacts and should be
reproducible or copied separately from the mutable data backup. `/boot` only
needs backing up if you want to preserve a known-good kernel/initrd/squashfs
pair off-machine; otherwise it can be rebuilt from the build artifact.
