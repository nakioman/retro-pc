# Windows XP Phase 5 — NFC Floppy over QMP Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking.
> Work test-first: RED (failing test), GREEN (minimal code), REFACTOR.

**Goal:** the NFC floppy drive works for QEMU VMs exactly as it does for
86Box: inserting a tagged floppy mounts its `floppies.yaml` image in the VM's
A: drive, removing it ejects the image, and a VM that starts with a floppy
already in the drive gets it (re-sync).

**Spec:** `docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`
("Phase 5").

**Tech stack:** .NET 10 / C# 13 (Native AOT), System.CommandLine, xUnit.

## Global constraints

- `mise` tasks only; never raw `dotnet`.
- Native AOT: QMP messages are written with `Utf8JsonWriter` and read with
  `JsonDocument` (Phase 4's `RetroBoxQmpClient`).
- Unchanged: the catalog, the tag format, `nfc: true` gating,
  `RetroBoxFloppyEventHandler`, the re-sync (`STATUS` over serial, then apply),
  the web panel drive endpoints, and the 86Box client and its messages while an
  86Box VM runs. Only floppy images use NFC; CDs stay real discs.
- Each task ends with `mise run test` and `mise run format-check`.

## Verified QMP commands (reference box, running XP VM)

The core2duo profile has `-drive if=floppy,index=0`, so the BlockBackend is
`floppy0`:

```text
insert: {"execute":"blockdev-change-medium","arguments":{"device":"floppy0",
         "filename":<image>,"format":"raw","read-only-mode":"read-only"|"read-write"}}
eject:  {"execute":"eject","arguments":{"device":"floppy0","force":true}}
status: {"execute":"query-block"} -> entry "device":"floppy0",
         "inserted":{"file":<image>,"ro":<bool>} (absent when empty)
```

## Design

```text
RetroBoxDaemon / RetroBoxFloppyEventHandler / RetroBoxFloppyControlSocketProbe (unchanged)
  -> RetroBoxLiveVmFloppyControlClient       (new, Core)
       -> RetroBoxFloppyControlClient          86Box JSON Lines socket
       -> RetroBoxQmpFloppyControlClient       QEMU QMP socket, floppy0 (new, Core)
```

- **Unavailable vs failed.** A new `RetroBoxFloppyControlUnavailableException`
  (subclass of `RetroBoxFloppyControlException`, same `internal_failure` code
  and message as today) marks "no VM behind this socket". Existing catch
  blocks keep working; only the router looks at the subtype. A real command
  failure (missing image, invalid drive) is never retried on the other engine.
- **Routing.** `RetroBoxLiveVmFloppyControlClient` sends each command to the
  engine that last answered, and on *unavailable* tries the other one. The
  first success makes that engine current. When no engine answers it throws
  an unavailable exception naming both sockets. Only one VM runs at a time,
  so at most one socket answers.
- **Probe and re-sync.** `RetroBoxFloppyControlSocketProbe` is reused as-is:
  its `StatusAsync(0)` goes through the router, so "socket ready" means "the
  86Box socket or QMP answers". The down-to-up transition (the selector always
  sits between two VMs) triggers `STATUS` as before.
- **QMP sessions.** QEMU serves one QMP client at a time, and the udev CD
  refresh connects too. `RetroBoxQmpFloppyControlClient` connects per command
  (greeting, `qmp_capabilities`, command, `query-block`, close) under a 5 s
  timeout. `RetroBoxQmpClient.ConnectAsync` retries for up to 2 s when the
  connect fails with `EAGAIN` (QEMU's listen backlog is full because another
  client holds the monitor), which also protects the CD refresh from the
  daemon's once-a-second probe. A connect that lands in the backlog waits for
  the greeting, bounded by the command timeout.
- **Error mapping.** Drive other than 0 -> `invalid_drive` (no connect).
  `blockdev-change-medium` `GenericError` "Could not open ..." ->
  `missing_image`. `DeviceNotFound` / no `floppy0` in `query-block` ->
  `invalid_drive`. Other QMP errors -> `internal_failure`. Unavailable socket,
  `Disconnected`, or timeout -> unavailable.
- **Status mapping.** `Inserted` = `inserted` present, `Path` = `inserted.file`,
  `ReadOnly` = `inserted.ro`, `Busy` = false (QMP has no busy flag),
  `Changed` = true after insert/eject, false for status.
- **Config.** The launcher always puts QMP at
  `RetroBoxQemuLauncher.DefaultQmpSocketPath`, so the daemon gets a
  `--qmp-socket` option with that default (like `cdrom refresh`), and no
  `config.yaml` key or `daemon.env` variable. `--echo` stays 86Box-only.

## Tasks

### Task 1: busy-socket retry in `RetroBoxQmpClient`

- [x] RED: `ConnectAsync` retries a `WouldBlock` connect and succeeds;
  gives up as unavailable after the busy timeout; does not retry
  `ConnectionRefused`.
- [x] GREEN: internal constructor takes an optional delay; retry loop on
  `SocketError.WouldBlock`/`TryAgain`, 100 ms interval, 2 s budget.

### Task 2: `RetroBoxFloppyControlUnavailableException`

- [x] RED: 86Box client socket failures throw the subtype (code and message
  unchanged); error responses and malformed responses do not.
- [x] GREEN: unseal the base exception, add the subtype, throw it from the
  socket catch.

### Task 3: `RetroBoxQmpFloppyControlClient`

- [x] RED (ScriptedQmpSocket per command): insert ro/rw request shape and
  status from the following `query-block`; eject request shape; status
  inserted / empty; drive 1 -> `invalid_drive` without connecting; insert
  `GenericError` -> `missing_image`; missing `floppy0` -> `invalid_drive`;
  unavailable socket -> unavailable subtype; each command opens its own
  connection.
- [x] GREEN: implement on `RetroBoxQmpConnection.ChangeMediumAsync` /
  `EjectAsync` / `ExecuteAsync("query-block")`.

### Task 4: `RetroBoxLiveVmFloppyControlClient`

- [x] RED: routes to 86Box when it answers; falls through to QMP when 86Box is
  unavailable and then sticks to QMP; switches back; a non-unavailable error
  is rethrown without trying the other engine; all unavailable -> unavailable
  naming both.
- [x] GREEN: implement.

### Task 5: daemon wiring

- [x] RED: CLI test — `daemon --qmp-socket` is parsed into the request
  (default `RetroBoxQemuLauncher.DefaultQmpSocketPath`); daemon test — probe
  over the router triggers `STATUS` when only QMP answers.
- [x] GREEN: add the option/record field; build the router in
  `CliCommandFactory`.

### Task 6: docs

- [x] `docs/architecture.md` floppy flow for both engines and components.
- [x] Design doc Phase 5 status.
- [x] `docs/windows-xp.md` and the core2duo profile README: floppy usage.
- [x] ADR 0011: floppy client chosen per live VM socket.
- [x] `mise run test`, `mise run format-check`.
