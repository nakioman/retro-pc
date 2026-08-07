# AGENTS.md

Guidance for AI coding agents working in this repository. Read this file first;
it is the single source of truth for commands, architecture, and conventions.
See [`docs/architecture.md`](docs/architecture.md) for the system overview and
[`docs/decisions/README.md`](docs/decisions/README.md) for ADRs.

## Project at a glance

RetroBox is a retro PC appliance: a Debian 13 host that boots into a fullscreen
86Box VM like a real DOS-era console, with physical hardware integration (an
ESP8266 floppy controller reading NFC-labeled disks, and a real CD-ROM).

Tech stack: .NET 10 (C# 13), solution `RetroBox.slnx`. Components:

- `src/RetroBox.Core` — domain: YAML catalog store, boot selection, floppy
  import/control client, Arduino serial protocol, NFC client/writer, VM
  selection. Flat file-per-concern classes with `RetroBox*` prefix.
- `src/RetroBox.Cli` — System.CommandLine entry point (`Program.cs` +
  `CliCommandFactory.cs`).
- `src/RetroBox.Daemon` — long-lived floppy/NFC event loop driving the 86Box
  floppy control socket.
- `tests/RetroBox.Tests` — xUnit suite for Core, Daemon, and CLI.
- `firmware/retrofloppy-esp8266` — ESP8266 (NodeMCU) Arduino firmware + vendored
  PN532 libraries, pinned via `sketch.yaml`.
- `appliance/` — Debian 13 read-only-root appliance layout and the bootable USB
  installer (`appliance/installer/`).

## Project Commands

Use `mise` tasks as the project command interface. Do not invoke `dotnet`
directly for normal project workflows.

- Restore dependencies: `mise run restore`
- Run tests: `mise run test`
- Apply formatting: `mise run format`
- Verify formatting: `mise run format-check`
- Run the CLI: `mise run cli -- <args>`
- Publish Linux x64: `mise run publish-linux-x64`
- Compile firmware: `mise run firmware-compile`
- Upload firmware: `mise run firmware-upload -- <port>`

`mise.toml` is the source of truth for the .NET tool version and project
commands. If a command needs to change, update `mise.toml` first and keep this
file aligned.

## Verification

Before claiming tests pass, run:

```bash
mise run test
```

If the command fails because the sandbox blocks local build IPC, rerun the same
`mise` task with the required permissions instead of switching to direct
`dotnet` commands.

Also run `mise run format-check` before finishing any change; CI enforces it.

## Testing conventions

- xUnit under `tests/RetroBox.Tests`; write tests alongside code (TDD:
  RED-GREEN-REFACTOR).
- Test projects reference Core/Daemon/Cli; `TestRetroBoxLayout.cs` drives
  temp-dir test layouts.
- Focused verification: `mise run test`. Build/publish verification:
  `mise run publish-linux-x64` (Native AOT; requires Linux toolchain — on macOS
  it fails at `llvm-objcopy`, which is expected and not a regression).

## Style and conventions

- C# 13, nullable enable, implicit usings; English identifiers, artifacts, and
  comments.
- Flat, file-per-concern `RetroBox*`-prefixed classes (e.g.
  `RetroBoxConfigStore`, `RetroBoxBootSelector`, `RetroBoxNfcWriter`).
- No comments unless they explain non-obvious decisions.
- Conventional Commits, scoped by area: `feat(cli):`, `fix(core):`,
  `feat(daemon):`, `feat(firmware):`, `feat(appliance):`, `chore:`.
- Follow `.editorconfig`; `mise run format-check` must pass.

## Key domain facts

- Catalog files under `configRoot` (default `/data/retrobox`): `config.yaml`,
  `vms.yaml`, `floppies.yaml`. The 86Box `.cfg` in each VM profile is the
  hardware source of truth; YAML is metadata only.
- Serial protocol (115200 baud, newline lines): `INIT <v>`, `INSERT <id>,<mode>`,
  `EJECT`, `ERROR <msg>`; host commands `WRITE <payload>`, `TAGID`, `STATUS`,
  `PING`/`PONG`. Parsing lives in `RetroBoxArduinoSerialProtocol`.
- 86Box floppy control socket: JSON Lines over Unix socket
  (`/run/retrobox/86box-floppy.sock`); commands `floppy.insert/eject/status`.
  Contract in `docs/86box-floppy-control-socket-contract.md`.
- NFC tags carry raw `<id>,<mode>` bytes (not NDEF) in pages 4–11, 32 bytes max.

## Documentation map

- `README.md` — user-facing overview and quickstart.
- `CONTRIBUTING.md` — contribution workflow, commit conventions, releases.
- `docs/architecture.md` — system overview and data flows.
- `docs/decisions/` — ADRs; record design changes there.
- `docs/vm-profiles.md`, `docs/cdrom-passthrough.md`,
  `docs/floppy-controller-wiring.md`, `docs/86box-*.md` — feature docs.
- `appliance/README.md`, `appliance/installer/README.md` — appliance and
  installer.
- `CHANGELOG.md` — notable changes; keep updated with meaningful changes.

## CI

- `.github/workflows/build-retrobox.yml` — format-check, test, Native AOT
  publish, smoke test, artifact upload.
- `.github/workflows/build-usb-installer.yml` — shellcheck, installer ISO build,
  release job that tags `appliance-YYYYMMDD-<run>` and generates grouped change
  notes from `git log` between tags.

## Process rules for agents

- Prefer `mise run <task>`; never raw `dotnet` for normal workflows.
- Keep scope small; document design decisions in `docs/decisions/` when they
  affect future maintainability.
- Update the changelog and relevant docs with user-visible or architectural
  changes.
