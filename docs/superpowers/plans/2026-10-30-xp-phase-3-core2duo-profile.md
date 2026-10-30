# Windows XP Phase 3 — Core 2 Duo Profile Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [x]`) syntax for tracking.
> Work test-first: RED (failing test), GREEN (minimal code), REFACTOR.

**Goal:** Ship the Windows XP VM as an ordinary RetroBox profile: id
`core2duo`, label "Core 2 Duo", engine `qemu`, with the hardware proven in the
Phase 0 spike, a 120G qcow2 disk created by `retrobox vm create-disks`, and a
user guide for installing XP on it.

**Spec:** `docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`
("Spike update (2026-10-29): the vmvga path" and "Phase 3"); ADR 0008,
ADR 0009.

**Tech stack:** QEMU 11.1.1 (`nakioman/qemu-vmvga`), .NET 10 / xUnit for the
launcher and payload tests.

## Global constraints

- `mise` tasks only; never raw `dotnet`.
- Profiles are named after their hardware, like `386sx16`, `pentium100`,
  `pentium2-350`. The spec's `profiles/winxp` becomes `profiles/core2duo`.
- `qemu.args` is the hardware source of truth (ADR 0009); the launcher adds
  only host-specific arguments.
- No XP media, drivers (Realtek AC97, VMware SVGA II), or games in the payload.
- Out of scope: CD media refresh and the installer VT-x check (Phase 4), NFC
  floppy over QMP (Phase 5).
- Each task ends with `mise run test` and `mise run format-check`.

## Design decisions

### Profile vs launcher arguments

The Phase 2 launcher appends the QMP socket and the installer-detected
optical drive (IDE index 2). The spike's command line also carried the kiosk
display (`-display gtk,gl=on,full-screen=on,...`) and `-monitor none -serial
none -parallel none`; the Phase 2 tests kept them in the profile. They are not
the VM's hardware: they describe how the appliance presents QEMU (fullscreen
GTK inside the launcher's own Xorg, no GTK text-console tabs covering the
VM). Phase 3 moves them into `RetroBoxQemuLauncher.BuildQemuArguments`, ahead
of the QMP socket, so every QEMU profile gets the same kiosk and a profile
cannot accidentally open a monitor console on the screen. Logs need no
argument: QEMU's stderr already goes to the `retrobox-boot` journal.

`qemu.args` keeps everything that describes the machine:

```text
-machine pc,accel=kvm,vmport=on     -cpu core2duo  -smp 2  -m 3072
-rtc base=localtime
-boot order=cd,menu=on,splash-time=3000
-vga none  -device vmvga,hwcursor=off,gbobjects=off,vgpu=9
-drive file=hdd.qcow2,if=ide,index=0,media=disk,format=qcow2
-drive if=floppy,index=0
-drive if=ide,index=3,media=cdrom
-audiodev alsa,id=snd0  -device AC97,audiodev=snd0
-netdev user,id=net0    -device rtl8139,netdev=net0
```

The disk is `hdd.qcow2`, matching the other profiles' `hdd.raw`. IDE index 2
stays free for the launcher's physical drive; index 3 is an empty CD drive
for ISOs (changed over QMP later). `-boot order=cd` is "disk (c), then CD
(d)": a blank disk falls through to the XP CD.

### POST logo

SeaBIOS can show a JPEG/BMP during POST (`-boot splash=<file>`). The
repository has no RetroBox logo asset suitable for a 640x480 BIOS splash
(only product photos), so the logo is a follow-up; `splash-time=3000` already
keeps the boot menu prompt up for three seconds.

### Existing installs

A reinstall that preserves `/data` keeps the user's `vms.yaml`, so the
profile files are staged but the VM is not in the catalog. The guide shows
the entry to add and `retrobox vm create-disks core2duo`.

## Task 1 — Launcher owns the kiosk display (`feat(core)`)

Files: `src/RetroBox.Core/RetroBoxQemuLauncher.cs`,
`tests/RetroBox.Tests/RetroBoxQemuLauncherTests.cs`.

- [x] RED: drop `-display`/`-monitor`/`-serial`/`-parallel` from the spike
      profile fixture; expect `BuildQemuArguments` to return profile args,
      then the display and console arguments, then `-qmp`.
- [x] GREEN: add them in `BuildQemuArguments` (public constants for the
      display value).
- [x] `mise run test`, `mise run format-check`.

## Task 2 — The `core2duo` payload profile (`feat(appliance)`)

Files: `appliance/installer/payload/profiles/core2duo/{qemu.args,README.md}`,
`appliance/installer/payload/retrobox/vms.yaml`,
`tests/RetroBox.Tests/RetroBoxPayloadCatalogTests.cs`.

- [x] RED: the payload test copies each profile's native file (`86box.cfg` or
      `qemu.args`) and expects `("core2duo", "hdd.qcow2", 128_849_018_880,
      "qcow2")`; a new test reads the payload `qemu.args` and checks it
      references every declared disk, leaves IDE index 2 free, and has none of
      the launcher's arguments (`-qmp`, `-display`, `-monitor`).
- [x] GREEN: add `qemu.args` (commented, one argument per line), the
      README, and the `vms.yaml` entry (`engine: qemu`, `hdd.qcow2`, `120G`,
      `qcow2`).
- [x] `mise run test`, `mise run format-check`.

## Task 3 — Docs (`docs`)

- [x] `docs/vm-profiles.md`: launcher arguments now include the kiosk display;
      new "Core 2 Duo" section.
- [x] `docs/windows-xp.md`: install XP SP3 from the real CD, user-supplied
      drivers (Realtek AC97; VMware SVGA II `SVGA2_X86_WINXP` via Device
      Manager > Have Disk), 32/16-bit modes and 4:3 output, the KVM VMware
      backdoor, input capture (Ctrl+Alt+G), known issues, adding the VM to a
      preserved catalog.
- [x] `README.md` profile table and docs list; `appliance/filesystem-layout.md`.
- [x] ADR 0009 and `docs/architecture.md`: the launcher's host arguments.
- [x] Design doc: Phase 3 status, `profiles/core2duo` naming, logo follow-up.
