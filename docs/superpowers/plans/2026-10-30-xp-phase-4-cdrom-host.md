# Windows XP Phase 4 — CD-ROM and Installer Host Checks Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking.
> Work test-first: RED (failing test), GREEN (minimal code), REFACTOR.

**Goal:** QEMU VMs use the installer-detected physical CD-ROM and see disc
swaps without a reboot; the installer reports whether the host can run QEMU
VMs with KVM.

**Spec:** `docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`
("Phase 4").

**Tech stack:** .NET 10 / C# 13 (Native AOT), System.CommandLine, xUnit,
bash (installer), udev, systemd.

## Global constraints

- `mise` tasks only; never raw `dotnet`.
- Native AOT: no reflection-based JSON; QMP messages are written with
  `Utf8JsonWriter` and read with `JsonDocument`.
- The XP profile is Phase 3's; nothing here creates `profiles/winxp`.
- No device name is hard-coded: QEMU only ever gets the drive the installer
  recorded in `/etc/retrobox/hardware.env`.
- Installer host checks warn, never fail (86Box VMs need none of it).
- Each task ends with `mise run test` and `mise run format-check`; installer
  tasks also run CI's `shellcheck -x -P SCRIPTDIR -S style` invocation.

## What Phase 2 already did (item 1)

`RetroBoxQemuLauncher` appends `-drive file=<CDROM_DEVICE>,if=ide,index=2,media=cdrom`
when `hardware.env` says `CDROM_STATUS=DETECTED` and the device still exists
(`RetroBoxHostHardware.ReadOpticalDrive`). `hardware.env` and
`install-report.txt` are written from the same `detect_cdrom` result, so the
detection is already shared with the 86Box `ioctl://` path. Alignment left:

- Add `format=raw` to the drive so QEMU does not probe the disc's format
  (it would guess raw and warn) and so the drive matches what the refresh
  passes to `blockdev-change-medium`.
- Name the drive's QOM id once (`ide1-cd0`: IDE index 2 is bus 1, unit 0) as
  a launcher constant, so the refresh cannot drift from the launch line.
- Record in `install-report.txt` how the drive is used
  (`cdrom.qemu=attached|none`) next to `cdrom.device`/`cdrom.status`.

## Disc-change refresh (item 2)

### Mechanism: udev rule -> templated systemd oneshot -> `retrobox cdrom refresh`

```text
kernel media poll -> uevent change DISK_MEDIA_CHANGE=1 on srN
  -> 90-retrobox-cdrom.rules: systemctl --no-block restart retrobox-cdrom-refresh@srN.service
     -> ConditionPathExists=/run/retrobox/qemu-qmp.sock (skipped when no QEMU VM runs)
     -> User=retrobox: retrobox cdrom refresh --device /dev/srN
        -> device is the installer's drive? else exit 0
        -> wait while the drive reports "not ready" (spin-up), max 15 s
        -> QMP: disc present -> blockdev-change-medium ide1-cd0 <drive> raw read-only
                tray empty   -> eject ide1-cd0 (force)
        -> connect refused / no socket -> exit 0 (no QEMU VM)
```

Why this and not the daemon:

- **udev RUN stays tiny.** `systemctl --no-block` only queues a job; the
  refresh, which may wait seconds for a disc to spin up, runs outside udev's
  sandbox and event timeout.
- **Socket ownership is respected.** QEMU runs as `retrobox` (boot service)
  and creates the QMP socket with its owner's permissions. The unit runs as
  `retrobox` too, so nothing is chmod-ed and root never talks to the VM.
- **`restart`, not `start`.** A oneshot that is still running merges a second
  `start` into the running job, which would lose an insert that follows an
  eject quickly. `restart` cancels the stale run and starts a new one, which
  reads the drive's state at that moment, so the last event always wins.
- **The daemon is the floppy controller's process** (serial + web panel) and
  would need raw netlink uevent parsing in the AOT binary; udev already does
  the filtering. The QMP client lives in Core, so the Phase 5 daemon can use
  it unchanged.
- **Kernel polling.** The kernel only reports optical media changes when the
  block device is polled (`block.events_dfl_poll_msecs` defaults to 0 and
  the appliance has no udisks). The same rule sets
  `events_poll_msecs=2000` on `sr*` when the device is added.

### Code

- `RetroBoxQmpClient` (Core): JSON-lines QMP over a Unix socket with an
  injectable stream factory, like `RetroBoxFloppyControlClient`.
  `ConnectAsync` reads the greeting and negotiates `qmp_capabilities`,
  returning a `RetroBoxQmpConnection` (`ExecuteAsync`, `ChangeMediumAsync`,
  `EjectAsync`) that skips asynchronous `event` lines while waiting for a
  `return`/`error`. Connect failures throw `RetroBoxQmpUnavailableException`;
  QMP errors throw `RetroBoxQmpException` (QMP `class` + `desc`). Phase 5
  reuses it for `floppy0`.
- `IRetroBoxOpticalDriveProbe` + `RetroBoxLinuxOpticalDriveProbe`:
  `CDROM_DRIVE_STATUS` ioctl (`open(O_RDONLY|O_NONBLOCK)`) ->
  `Present | Absent | TrayOpen | NotReady`.
- `RetroBoxQemuCdromRefresher`: the flow above, with injected probe, QMP
  client and delay; returns an outcome (`NoQemuVm`, `Changed`, `Ejected`).
  The CLI handles "no drive", "other drive" and "no socket" before it.
- `RetroBoxHostHardware.IsSameDevice`: compares two paths after resolving
  symlinks (`/dev/srN` against a `/dev/disk/by-id/*` drive).
- CLI: `retrobox cdrom refresh [--device] [--qmp-socket] [--hardware-env]`.

### Tasks

- [x] QMP client tests (greeting + capabilities handshake, command line
  format, events skipped, error -> `RetroBoxQmpException`, closed stream,
  connect failure -> unavailable); implement.
- [x] `IsSameDevice` tests (same path, symlink to device, different device);
  implement.
- [x] Refresher tests (present -> change with the drive path, raw,
  read-only; absent -> forced eject; not-ready waits then acts; other device
  -> no QMP; unavailable socket -> `NoQemuVm`; QMP error propagates);
  implement.
- [x] Launcher test: drive has `format=raw`; constant `OpticalDriveId`.
- [x] CLI test: `cdrom refresh` with no drive in `hardware.env` exits 0 and
  says so; help smoke test lists `cdrom`.
- [x] Payload: `udev/90-retrobox-cdrom.rules`,
  `units/retrobox-cdrom-refresh@.service`; `services.sh` installs both.
  Payload test: the unit calls the CLI command that exists and runs as
  `retrobox`; the rule restarts that unit.

## Installer host checks (item 3)

New `lib/host-checks.sh`, `check_qemu_host` runs after `install_services`
(the modprobe option is installed there) and appends to
`install-report.txt`:

```text
kvm.cpu_flag=vmx            # vmx | svm | NOT_FOUND
kvm.device=PRESENT          # PRESENT | MISSING (/dev/kvm on the live system)
kvm.status=USABLE           # USABLE | UNAVAILABLE
kvm.vmware_backdoor=CONFIGURED   # CONFIGURED | MISSING (target modprobe.d)
retrobox.group.kvm=MEMBER   # MEMBER | MISSING
retrobox.group.render=MEMBER
```

Each non-OK value logs a warning naming the consequence (QEMU VMs will not
start / no 3D); the install continues.

- [x] Write `lib/host-checks.sh`; source it from `install-retropc.sh`; call
  after `install_services`.
- [x] `cdrom.qemu` line in `write_install_report`.
- [x] Exercise the functions against a fake target tree in the scratchpad
  (vmx/no vmx, groups present/missing, option present/missing); shellcheck.

## Docs

- [x] `docs/cdrom-passthrough.md`: QEMU path, the refresh, validation steps.
- [x] `appliance/installer/hardware-detect.md`: report keys, KVM checks.
- [x] `appliance/installer/README.md`: host checks, the udev rule/unit.
- [x] Spec "Phase 4" status (the daemon bullet becomes the udev/systemd
  mechanism, with the reason); ADR 0010 for the refresh mechanism.

## Hardware validation (on the reference box, after merge)

- Swap discs while XP runs: guest sees the new disc without action.
- Eject with no replacement: guest drive shows empty.
- Insert while the drive spins up: the refresh waits and inserts once.
- No refresh loop: `journalctl -u 'retrobox-cdrom-refresh@*'` shows one run
  per physical change.
- 86Box VM running: the unit is skipped (`ConditionPathExists`).
