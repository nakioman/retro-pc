# Debian Appliance Filesystem Layout

## Mount and mutability model

The appliance is designed around a read-only root filesystem. The operating
system, installed packages, the `retrobox` binary, and 86Box's AppImage are
part of the deployed system image and are not application state.

`/data` is the persistent writable filesystem for Retro PC state:

```text
/data/
  retrobox/
    config.yaml
    vms.yaml
    floppies.yaml
    games.yaml
    install-report.txt
  vms/
    386sx16/
    pentium100/
  floppies/
    scratch/
    cataloged/
  snapshots/
    386sx16/
    pentium100/
  system/
    var/          # overlay upperdir for /var
    .var.work/    # overlay workdir for /var
```

`/data/retrobox/install-report.txt` is written by the USB installer and records
the detected target disk, CD-ROM, and ESP8266 serial device (or explicit
placeholders when a device is absent).

`/data/system/` holds the writable overlay upperdirs that make a read-only root
usable — see [Read-only root exceptions](#read-only-root-exceptions). It is
system state produced by the installer and the running OS, not user-facing
application data, and network shares never expose it.

The current `retrobox` code uses these paths directly:

- `/data/retrobox` is the YAML catalog root.
- `/data/floppies/scratch` is the import source directory.
- `/data/floppies/cataloged` stores imported floppy images.
- VM paths are under `/data/vms`.

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
/run/
/tmp/
/var/log/
/var/lib/
```

The USB installer implements this contract as follows:

- `/` is mounted `ro,errors=remount-ro`.
- `/tmp` is `tmpfs` (volatile).
- `/var` is an `overlay` mount whose writable upperdir lives under
  `/data/system/var` (see the `/data` tree above), so logs, Samba state, DHCP
  leases, and ALSA state persist across reboots without a writable root.
- `/etc` stays **read-only**. The few files that must exist per machine — the
  SSH host keys and the machine-id — are generated into the image at install
  time. Maintenance edits use `sudo mount -o remount,rw /`.

`/etc` is deliberately **not** an fstab overlay: `/etc` is read during early
boot, before an fstab-mounted overlay could apply, which would split-brain the
config (early boot sees the read-only layer, later reads see the overlay). Doing
an `/etc` (or whole-root) overlay correctly means assembling it in the initramfs
before `switch_root` — the immutable squashfs-root approach tracked in #43 (and
the read-only-root prototype #30). Until then the appliance must always keep
`/data` writable and the OS image root effectively read-only.

The following system areas remain read-only after deployment unless a future
maintenance workflow deliberately remounts or replaces the system image:

```text
/
/usr/
/bin/
/sbin/
/lib/
/opt/
```

`/etc` contains installed system configuration and should be treated as image
configuration for this stage. Any future changes required while the root is
read-only must be represented by the image build or an explicitly documented
overlay; they must not be silently written into `/data` without defining the
corresponding contract.

## Runtime ownership and access

The eventual services should run with the least privilege compatible with the
hardware:

- `retrobox` owns and edits its YAML catalogs under `/data/retrobox`.
- `retrobox` reads VM and cataloged floppy assets under `/data`.
- `retrobox` writes imported assets to `/data/floppies/cataloged` only through
  the import workflow.
- administrators use SSH and `sudo` for maintenance and diagnostics.
- Samba provides a restricted drop folder at
  `/data/floppies/scratch`, not a general `/data` share.
- the 86Box control socket is created at
  `/run/retrobox/86box-floppy.sock` by the eventual runtime service.

Device-group membership for audio, input, serial/USB, CD-ROM, and graphics is
intentionally not finalized here. It depends on the physical hardware and the
deferred graphics integration work.

## Backup boundary

Back up `/data` as application state. At minimum this includes the YAML files,
VM disks, cataloged floppy images, and snapshots. The immutable root image,
package manifest, and AppImage are deployment artifacts and should be
reproducible or copied separately from the mutable data backup.
