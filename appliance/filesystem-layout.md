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
  vms/
    386sx16/
    pentium100/
  floppies/
    scratch/
    cataloged/
  snapshots/
    386sx16/
    pentium100/
```

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

The final read-only-root implementation must provide the necessary tmpfs,
overlay, or explicitly persistent mounts for services that write there. The
base package layout does not choose or implement that mount strategy.

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
