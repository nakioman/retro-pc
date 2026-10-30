# Windows XP Phase 2 — Multi-Engine Boot and VM Disks Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking.
> Work test-first: RED (failing test), GREEN (minimal code), REFACTOR.

**Goal:** Let a VM in `vms.yaml` run on 86Box or QEMU, and let `vms.yaml`
declare the VM's hard disks so `retrobox` (not a bash script) creates the
missing ones for any engine.

**Spec:** `docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`
("Spike update (2026-10-29): the vmvga path" and "Phase 2").

**Tech stack:** .NET 10 / C# 13, YamlDotNet static context (Native AOT),
System.CommandLine, xUnit.

## Global constraints

- `mise` tasks only; never raw `dotnet`.
- Native AOT: every new YAML record is registered in `RetroBoxYamlContext`;
  no reflection.
- Existing catalogs keep working: `engine` defaults to `86box`, `disks` is
  optional.
- 86Box launch behaviour is unchanged (same arguments, working directory,
  stdout discarded).
- No packaging work (Phase 1 owns `/opt/qemu-retrobox`); assume
  `/opt/qemu-retrobox/bin/qemu-system-i386`, `/opt/qemu-retrobox/bin/qemu-img`
  and `/opt/qemu-retrobox/lib`.
- Out of scope: the XP profile (Phase 3), CD media refresh (Phase 4), floppy
  over QMP (Phase 5).
- Each task ends with `mise run test` and `mise run format-check`.

## Design decisions

### Catalog shape

```yaml
vms:
  winxp:
    label: "Windows XP"
    path: "/data/vms/winxp"
    engine: qemu            # 86box (default) | qemu
    disks:
      - file: xp.qcow2      # relative to path
        size: 20G           # bytes, or K/M/G/T (binary multiples)
        format: qcow2       # raw (default) | qcow2
```

Load-time validation (`RetroBoxVmCatalogRules`, called from
`RetroBoxConfigStore.Validate`), pure and filesystem-free:

- `engine` is `86box` or `qemu`.
- `disks[].file` is required, relative, without `..` segments, unique per VM.
- `disks[].format` is `raw` or `qcow2`; 86Box disks must be `raw` (86Box's
  `hdd.raw` images are flat).
- `disks[].size`, when present, parses to a positive byte count; it is
  required for QEMU disks.

### Disk sizes for 86Box: the `.cfg` is authoritative

86Box needs the geometry (`hdd_NN_parameters = S, H, C, ...`) and the disk
file must match it exactly, so the `.cfg` stays the authority (ADR 0003 and
the new ADR 0009). For an 86Box disk:

- the declared `file` must match an `hdd_NN_fn` in the profile's `86box.cfg`
  (paths resolved against the profile directory);
- `size` is optional: when omitted it is derived as `S*H*C*512`; when present
  it must equal that value, or disk creation fails with both numbers.

The payload `vms.yaml` declares `hdd.raw` (raw, no size) for the three
86Box profiles, so a fresh install creates the same three sparse files of
60,862,464 / 2,111,864,832 / 20,020,331,520 bytes as the old script.

This check reads the profile and runs in disk planning (`vm create-disks`),
not in `RetroBoxConfigStore.Load`, which the web panel and daemon call on
every change and which must not depend on profile files.

### Disk creation (`retrobox vm create-disks [id] [--config-root] [--qemu-img]`)

- `RetroBoxVmDiskPlanner` turns a catalog VM into `RetroBoxVmDiskPlan`
  entries (absolute path, bytes, format), applying the 86Box rules above.
- `RetroBoxVmDiskCreator` creates each missing disk and never opens an
  existing one: `raw` → `FileStream.SetLength` (sparse on ext4); `qcow2` →
  `qemu-img create -f qcow2 <path> <bytes>` through an injected process
  runner, with `LD_LIBRARY_PATH` pointing at the bundle's `lib/`. A missing
  `qemu-img` fails that disk with a message naming the path and the QEMU
  package.
- Every VM is processed; any failure makes the command exit 1 after all
  output is written.
- The installer runs it in the target
  (`in_target /opt/retrobox/retrobox vm create-disks --config-root /data/retrobox`)
  in place of `retrobox-hdd-creation`, which is deleted.

### Launchers

- `RetroBox86BoxLauncher` is today's `RetroBoxBoot`, renamed.
- `RetroBoxQemuProfile` reads `<profile>/qemu.args`: one argument per line,
  blank lines and `#` comments ignored, surrounding whitespace trimmed, no
  shell quoting (`-name` / `Windows XP` are two lines).
- `RetroBoxQemuLauncher`:
  1. builds QEMU's argv = profile arguments + host arguments
     (`-qmp unix:<run dir>/qemu-qmp.sock,server=on,wait=off`, plus
     `-drive file=<drive>,if=ide,index=2,media=cdrom` with the drive the
     installer detected into `/etc/retrobox/hardware.env`, when its
     `CDROM_STATUS` is `DETECTED` and the device exists);
  2. writes it to `<run dir>/qemu-session.args` (same one-per-line format; it
     doubles as a record of the last launch);
  3. runs `xinit <retrobox> qemu-session --binary <qemu> --args-file <file>
     --vm-path <profile> -- :0 vt<N> -keeptty -nolisten tcp` and waits.
     Passing the argv through a file avoids xinit's `--` separator and any
     quoting.
- `retrobox qemu-session` (hidden command, the xinit client) runs
  `RetroBoxQemuSession`: `xset s off -dpms`, starts
  `matchbox-window-manager -use_titlebar no -use_cursor no` in the
  background, runs QEMU with `NO_AT_BRIDGE=1` and
  `LD_LIBRARY_PATH=<bundle>/lib` in the profile directory with stdout
  discarded, then stops the window manager. When it returns, xinit stops X
  and `retrobox boot` shows the selector, like after 86Box.
- `<N>` is the VT of the boot console, read from `/proc/self/fd/0`
  (`/dev/ttyN`), falling back to 1 (the unit's `TTYPath=/dev/tty1`).
- Logs: QEMU, Xorg and the session inherit stderr, which
  `retrobox-boot.service` sends to the journal, as for 86Box.
- All process launches go through `IRetroBoxProcessRunner`
  (`Run` and `Start`), so launcher and session are unit-tested with a
  recording runner.
- The boot command checks the engine's native profile file (`86box.cfg` or
  `qemu.args`), adds `Engine` and `QemuBinaryPath` to
  `RetroBoxBootCommandRequest`, and dispatches by engine. Splash cover/quit
  and the selector loop are untouched.

---

### Task 1: Engine and disks in the catalog

**Files:** `RetroBoxCatalogModels.cs`, `RetroBoxYamlContext.cs`,
`RetroBoxConfigStore.cs`, new `RetroBoxVmCatalogRules.cs`; tests in
`RetroBoxConfigStoreTests.cs`, new `RetroBoxVmCatalogRulesTests.cs`.

- [x] Tests: missing `engine` loads as `86box`; `qemu` loads; unknown engine,
  absolute/escaping/duplicate disk file, bad format, `qcow2` on 86Box,
  unparsable size, and QEMU disk without size are rejected; disks round-trip
  through `Save`; size parsing (`20G`, `512M`, `1024`, rejects `0`, `1.5G`,
  `G`).
- [x] Add `Engine`, `Disks`, `RetroBoxVmDisk`, register it, validate.
- [x] Commit `feat(core): add VM engine and disk declarations to vms.yaml`.

### Task 2: Disk planning and creation

**Files:** new `RetroBox86BoxConfig.cs` (hdd geometry reader),
`RetroBoxVmDisks.cs` (planner + creator), `RetroBoxProcessRunner.cs`
(`IRetroBoxProcessRunner`, `RetroBoxProcessSpec`, default implementation);
tests `RetroBox86BoxConfigTests.cs`, `RetroBoxVmDisksTests.cs`.

- [x] Tests: geometry parsing of the three payload configs' lines; size
  derived from the `.cfg`; explicit matching size accepted; mismatch and
  undeclared-in-cfg rejected; raw creation is exact size; existing file
  untouched (content and length); qcow2 runs `qemu-img` with the expected
  argv and `LD_LIBRARY_PATH`; missing `qemu-img` reports clearly; failures
  on one VM don't stop others.
- [x] Implement.
- [x] Commit `feat(core): create declared VM disks`.

### Task 3: `retrobox vm create-disks`

**Files:** `CliCommandFactory.cs`; tests `CliVmTests.cs`.

- [x] Tests: creates missing disks under a temp layout and prints
  `Created`/`Keeping` lines; optional VM id limits the run; unknown id and
  disk errors exit 1.
- [x] Implement.
- [x] Commit `feat(cli): add vm create-disks command`.

### Task 4: Split the launchers and add QEMU

**Files:** rename `RetroBoxBoot.cs` → `RetroBox86BoxLauncher.cs`; new
`RetroBoxQemuProfile.cs`, `RetroBoxQemuLauncher.cs`, `RetroBoxQemuSession.cs`;
tests `RetroBox86BoxLauncherTests.cs` (renamed), `RetroBoxQemuLauncherTests.cs`.

- [x] Tests: `qemu.args` parsing (comments, blanks, trimming, missing file);
  the spike's XP arguments as a fixture produce the expected QEMU argv with
  QMP and, only with a detected drive, that device; xinit argv and args file content;
  session order (xset → WM start → QEMU with env/working directory → WM
  stop) and QEMU's exit code returned; VT detection fallback.
- [x] Implement; 86Box launcher test unchanged apart from the name.
- [x] Commit `feat(core): add QEMU launcher behind xinit`.

### Task 5: Boot command dispatch and `qemu-session`

**Files:** `CliCommandFactory.cs`; tests `CliBootSessionTests.cs`.

- [x] Tests: QEMU VM boots through the runner with `Engine == "qemu"`; QEMU
  VM without `qemu.args` and 86Box VM without `86box.cfg` fail with the
  splash quit; the selector loop and splash order hold for a mixed catalog;
  `qemu-session` is hidden from help.
- [x] Implement.
- [x] Commit `feat(cli): boot QEMU VMs`.

### Task 6: Appliance migration

**Files:** `appliance/installer/payload/retrobox/vms.yaml`,
`appliance/installer/lib/rootfs-extract.sh`, delete
`appliance/installer/payload/scripts/retrobox-hdd-creation`, profile READMEs.

- [x] Declare `hdd.raw` for the three profiles; add a test that loads the
  payload catalog and plans exactly the three historical sizes.
- [x] Installer: run `retrobox vm create-disks`; accept profiles with
  `86box.cfg` or `qemu.args`; keep `*.qcow2` on reinstall.
- [x] Commit `feat(appliance): create VM disks from vms.yaml`.

### Task 7: Docs and ADR

- [x] ADR 0009 (each engine's native profile is the hardware source of
  truth; disk declarations in `vms.yaml`, `.cfg` authoritative for 86Box
  geometry); mark ADR 0003 amended; index.
- [x] `docs/vm-profiles.md`, `docs/architecture.md`, `AGENTS.md` domain facts,
  `appliance/installer/README.md`, `appliance/filesystem-layout.md`, design
  doc Phase 2/3 notes.
- [x] Commit `docs: record multi-engine VMs and declared disks`.
