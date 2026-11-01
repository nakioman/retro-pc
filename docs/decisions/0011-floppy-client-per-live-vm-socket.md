# 0011. The daemon routes floppy commands to whichever VM socket answers

Date: 2026-11-01
Status: Accepted

## Context

The NFC floppy drive must work for QEMU VMs exactly as for 86Box. The daemon
(`retrobox daemon`) is a long-lived process separate from `retrobox boot`, so
it does not know which engine the running VM uses. 86Box VMs expose the floppy
control socket (`/run/retrobox/86box-floppy.sock`, JSON Lines); QEMU VMs
expose QMP (`/run/retrobox/qemu-qmp.sock`), where the A: drive is the
`floppy0` BlockBackend. Only one VM runs at a time. QEMU's QMP serves one
client at a time, and the udev-triggered CD refresh (ADR 0010) connects to it
too. The daemon already finds out when a VM starts by probing its floppy
socket once a second, and re-syncs the physical floppy then.

## Decision

- `RetroBoxQmpFloppyControlClient` implements `IRetroBoxFloppyControlClient`
  over QMP: `blockdev-change-medium` (raw, catalog `ro`/`rw`), forced `eject`,
  and `query-block` for status. Each command opens its own connection
  (greeting, `qmp_capabilities`, command, `query-block`, close) under a 5 s
  timeout; nothing holds the monitor between commands.
- `RetroBoxLiveVmFloppyControlClient` wraps the 86Box and QMP clients behind
  the same interface. Each command goes to the engine that answered last; if
  that socket is unavailable it tries the other. A VM that answers and
  refuses a command (missing image, no such drive) is the running VM, and
  the error is returned without trying the other engine.
- "Unavailable" is a subtype, `RetroBoxFloppyControlUnavailableException`,
  of the existing exception, with the same `internal_failure` code and
  message, so existing callers and messages are unchanged.
- The probe, event handler, re-sync, and web panel are unchanged: they get
  the router instead of the 86Box client.
- `RetroBoxQmpClient.ConnectAsync` retries a connect that fails with `EAGAIN`
  (QEMU is not accepting while another client holds the monitor, and its
  listen backlog is full) for up to 2 s.

## Consequences

- No coordination between `retrobox boot` and the daemon (no state file
  naming the running engine): whichever socket answers is the running VM, and
  a crashed VM's stale socket refuses connections like a missing one.
- While a QEMU VM runs, the once-a-second probe opens a QMP connection once a
  second for a few milliseconds. The CD refresh and the daemon can collide;
  both retry a busy socket, and a connect queued in the backlog only waits
  for the other client's few commands.
- While no VM runs, each probe tries both sockets; both fail at once
  (missing file or refused).
- Re-sync triggers on the "no VM" to "VM" transition. Going straight from one
  engine to the other without a probe seeing "no VM" would skip it; in
  practice the selector always runs between two VMs.
- `--echo` still prints 86Box requests only.
