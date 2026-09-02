# 0002. Read-only root with persistent /data

Date: 2026-07-28
Status: Accepted

## Context

The appliance should behave like an embedded console, not a general-purpose
Linux box. Users must not be able to break the base OS while playing, and
install-time configuration (SSH, Samba, users) should be reproducible across
reinstalls.

## Decision

The installed system uses a **read-only root filesystem**. The OS, packages,
`/opt/retrobox/retrobox`, and 86Box are immutable. All mutable application state
lives under a persistent `/data` partition:

```text
/data/retrobox/    YAML catalogs (config.yaml, vms.yaml, floppies.yaml)
/data/vms/<id>/    86Box profiles (86box.cfg, hdd.raw, syncmaster3.edid when used)
/data/floppies/    scratch/ + cataloged/ images
/data/snapshots/   VM snapshots
```

Runtime-only state (sockets, logs, PIDs) belongs under `/run` and `/var` via
systemd tmpfiles, never in the immutable application tree. Reinstalling keeps
`/data` intact (ADR `0002` is preserved by the installer's reinstall path).

## Consequences

- The system is crash- and tamper-resistant; recovery is a GRUB entry that
  appends `retropc.norun=1` to get a tty1 login.
- `/data` becomes the backup/reinstall boundary.
- Services must not write to the immutable tree; new persistent files require a
  deliberate `filesystem-layout.md` update.
