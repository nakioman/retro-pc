# 0010. QEMU disc changes are refreshed by a udev-triggered systemd unit

Date: 2026-10-30
Status: Accepted

## Context

QEMU VMs get the installer-detected optical drive as a host drive
(`-drive file=<CDROM_DEVICE>,if=ide,index=2,media=cdrom,format=raw`, QOM id
`ide1-cd0`; ADR 0009). QEMU does not notice a disc swap in the host drive: the
guest keeps the old disc until the drive is reopened. Reopening it over QMP
(`blockdev-change-medium` on `ide1-cd0`, read-only, raw) makes the guest see
an eject and an insert; with an empty drive that command fails and `eject`
is used instead. Linux reports the swap as a udev `change` event with
`DISK_MEDIA_CHANGE=1` on the `sr` device, but only while the kernel polls the
drive, which is off by default and the appliance runs no udisks.

The design first placed the watcher in the daemon. The daemon is the floppy
controller's process (serial, web panel), and watching uevents there needs
netlink parsing in the AOT binary.

## Decision

- `/etc/udev/rules.d/90-retrobox-cdrom.rules` enables polling
  (`events_poll_msecs=2000`) on `sr*` and, on `DISK_MEDIA_CHANGE=1`, runs
  only `systemctl --no-block restart retrobox-cdrom-refresh@%k.service`.
- `retrobox-cdrom-refresh@.service` is a oneshot running as `retrobox`, with
  `ConditionPathExists=/run/retrobox/qemu-qmp.sock`. It runs
  `retrobox cdrom refresh --device /dev/%I`, which ignores drives other than
  the installer's, waits up to 15 s while the drive reports "not ready"
  (spin-up), then connects to QMP and changes the medium (disc present) or
  force-ejects it (empty or open tray).
- The QMP client (`RetroBoxQmpClient`) lives in Core with an injectable
  stream, so the Phase 5 floppy control reuses it.

## Consequences

- udev's RUN action returns at once; the spin-up wait happens outside udev's
  sandbox and event timeout.
- QEMU runs as `retrobox` and owns the QMP socket; the unit runs as the same
  user, so the socket keeps its permissions and root never talks to a VM.
- `restart` instead of `start`: a oneshot that is still running would merge a
  second `start` into the running job and lose an insert that quickly
  follows an eject. A restart cancels the stale run, and the new run reads the
  drive as it is now, so the last event wins.
- Nothing runs while an 86Box VM (or no VM) is up: the unit is skipped. A
  stale socket left by a crashed QEMU refuses connections, which the command
  treats as "no QEMU VM".
- QEMU serves one QMP client at a time. The refresh holds the connection only
  for its commands (the spin-up wait comes before connecting); Phase 5's
  daemon must still tolerate a short wait for the socket.
- The 2-second poll is a TEST UNIT READY on an idle drive; acceptable on an
  appliance that already spins the drive for 86Box guests.
