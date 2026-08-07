# RetroBox

Retro PC appliance control tooling: a Debian-based appliance that boots into a
fullscreen 86Box virtual machine like a real DOS-era computer, with physical
hardware integration — a modified floppy drive with NFC-labeled disks, a real
CD-ROM, and a boot-time machine selector.

RetroBox is built from four parts that live in this repository:

| Area | Path | Description |
| --- | --- | --- |
| CLI | [`src/RetroBox.Cli`](src/RetroBox.Cli) | `retrobox` command-line entry point (System.CommandLine). |
| Core | [`src/RetroBox.Core`](src/RetroBox.Core) | Domain logic: YAML catalogs, boot selection, floppy import/control, NFC, serial protocol. |
| Daemon | [`src/RetroBox.Daemon`](src/RetroBox.Daemon) | Long-lived floppy/NFC event loop that drives the 86Box floppy socket. |
| Firmware | [`firmware/retrofloppy-esp8266`](firmware/retrofloppy-esp8266/README.md) | ESP8266 (NodeMCU) firmware that reads/writes NFC tags in floppy shells. |
| Appliance | [`appliance/`](appliance/README.md) | Debian 13 base layout, read-only root, and the bootable USB installer. |
| Tests | [`tests/RetroBox.Tests`](tests/RetroBox.Tests) | xUnit test suite for Core, Daemon, and CLI. |

## What it does

- **Boot a VM like a console.** On power-on the appliance boots straight into
  the default 86Box VM fullscreen. Pressing F12 during the boot window opens a
  plain-text machine selector. See [`appliance/README.md`](appliance/README.md)
  and [`docs/vm-profiles.md`](docs/vm-profiles.md).
- **NFC-labeled floppy disks.** A physical floppy carries an NFC tag encoding
  `<catalog-id>,<mode>`. Inserting it makes the daemon mount the matching image
  in 86Box; ejecting unmounts it. The firmware and serial protocol are documented
  in [`firmware/retrofloppy-esp8266/README.md`](firmware/retrofloppy-esp8266/README.md)
  and [`docs/floppy-controller-wiring.md`](docs/floppy-controller-wiring.md).
- **Physical CD-ROM passthrough.** The installer detects the host optical drive
  and wires the first active slot in each VM profile to it. See
  [`docs/cdrom-passthrough.md`](docs/cdrom-passthrough.md).
- **Read-only-root appliance.** The installed system is a minimal Debian 13 with
  immutable root and persistent state under `/data`. See
  [`appliance/filesystem-layout.md`](appliance/filesystem-layout.md).

## Prerequisites

- [mise](https://mise.jdx.dev/) — pins the .NET SDK (10) and `arduino-cli`.
  All project commands go through `mise run`, never bare `dotnet`.

## Quickstart

```bash
mise install          # install pinned tools (dotnet, arduino-cli)
mise run restore      # restore .NET dependencies
mise run test         # run the xUnit suite
mise run format-check # verify dotnet format compliance
mise run cli -- --help
```

Publish the Linux x64 Native AOT binary:

```bash
mise run publish-linux-x64
```

Build and flash the firmware (see the firmware README for ports):

```bash
mise run firmware-compile
mise run firmware-upload -- /dev/cu.usbserial-XXXX
```

Build the bootable USB installer image (see
[`appliance/installer/README.md`](appliance/installer/README.md)):

```bash
docker build --platform linux/amd64 -t retropc-builder appliance/installer
docker run --rm --platform linux/amd64 --privileged -v "$PWD:/work" \
    retropc-builder /work/appliance/installer/build-usb-installer.sh
```

## CLI overview

```text
retrobox boot    Start the configured VM; F12 opens the selector.
retrobox daemon  Run the floppy/NFC hardware integration daemon.
retrobox vm      List VMs and show/change the default.
retrobox floppy  Manage cataloged floppy images.
retrobox import  Import a floppy image from scratch into the catalog.
retrobox nfc     Read or write NFC-backed floppy labels.
```

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — system overview and data flow.
- [`docs/vm-profiles.md`](docs/vm-profiles.md) — 386SX-16 and Pentium 100 profiles.
- [`docs/86box-floppy-control-socket-contract.md`](docs/86box-floppy-control-socket-contract.md) — the 86Box floppy control socket protocol.
- [`docs/86box-floppy-control-integration-verification.md`](docs/86box-floppy-control-integration-verification.md) — end-to-end verification guide.
- [`docs/floppy-controller-wiring.md`](docs/floppy-controller-wiring.md) — physical floppy drive build.
- [`docs/cdrom-passthrough.md`](docs/cdrom-passthrough.md) — physical CD-ROM validation.
- [`docs/decisions/`](docs/decisions/) — architecture decision records.
- [`appliance/README.md`](appliance/README.md) — appliance base layout and runtime behavior.
- [`appliance/installer/README.md`](appliance/installer/README.md) — the USB installer.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the workflow, command conventions,
and code style. AI agents should start with [`AGENTS.md`](AGENTS.md).

## License

MIT — see [`LICENSE`](LICENSE). Copyright (c) 2026 Ignacio Glinsek.
