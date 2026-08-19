# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Conventional Commits](https://www.conventionalcommits.org/).

Releases on GitHub are tagged `appliance-YYYYMMDD-<run>` and their notes are
generated from this history; see
[`CONTRIBUTING.md`](CONTRIBUTING.md#releases).

## [Unreleased]

### Changed

- Appliance boots from an immutable squashfs root assembled by `live-boot`
  persistence into an overlayfs whole-root mount. The disk layout is now
  `p1=/boot` (ext4, kernel + initrd + `root-<ver>.squashfs`, rw at runtime so
  a new image can be dropped in) and `p2=/data` (ext4, overlay upperdir +
  application state). The previous ext4-ro + `/var`-overlay mechanism
  (shipped in `appliance-20260802-37`) is superseded. See
  [`appliance/read-only-root.md`](appliance/read-only-root.md) and
  [ADR 0006](docs/decisions/0006-squashfs-overlay-root.md).
- SSH host keys and `machine-id` are generated on first boot by the new
  `retrobox-firstboot.service` instead of being baked into the image at
  install time.

### Removed

- `grub-common.service` mask and `GRUB_RECORDFAIL_TIMEOUT=0` workarounds
  (no longer needed on the truly immutable squashfs root).
- The `overlay /var overlay ...` fstab line (replaced by the whole-root
  overlay assembled in the initramfs).
- The `FRAMEBUFFER=y` initramfs hint (initramfs ships inside the squashfs;
  chroot regeneration no longer happens at install time).

### Added

- Repository documentation: root README, contributing guide, license (MIT),
  architecture overview, and architecture decision records under `docs/decisions/`.
- GitHub issue and pull request templates.
- First-boot WiFi configuration: detects a `wl*` interface, prompts for SSID
  and password with `dialog`, and persists credentials under `/data/system/`.
  Bundles `firmware-realtek` (from `non-free-firmware`) for Realtek USB
  dongles, and uses `wpa_supplicant` + systemd-networkd (DHCP) for the
  connection.

### Removed

- `openspec/` and `.gga` agent-tooling configuration; context now lives in
  `AGENTS.md`, `docs/architecture.md`, and `docs/decisions/`.
- Legacy planning docs under `docs/superpowers/` and `docs/plans/`.

## [2026-08-07] — appliance-20260807-49

### Added

- Appliance installer preserves `/data` across reinstalls, keeping VM `.vhd`
  files and YAML catalogs.
- Daemon service is gated on the detected serial device and passes
  `--serial-port` to the daemon.

### Fixed

- Boot selector returns to the selector when a VM exits; Plymouth splash
  handoff and floppy re-sync on VM start are reliable.

## [2026-08-06] — appliance-20260806-38

### Added

- `retrobox nfc read/write` and catalog status commands.
- Daemon opens the floppy controller serial port directly and can echo the
  86Box socket payload without connecting.

## [2026-08-05]

### Changed

- Applied `dotnet format` across the solution and added `[tasks.format]` /
  `[tasks.format-check]` to `mise.toml`; CI now enforces formatting.

## [2026-08-02] — appliance-20260802-37

### Added

- First bootable USB appliance installer builds.
- Debian 13 read-only-root base layout and `/data` persistent state contract.
- Physical CD-ROM passthrough configuration during install.

## [2026-08-01] — appliance-20260801-35

### Added

- `retrobox vm` selection commands and `retrobox boot` with the F12 machine
  selector, published as a Native AOT Linux x64 binary.

## [2026-07-29]

### Added

- Pentium 100 86Box profile.
- SDL fullscreen CRT shader profiles.

## [2026-07-28]

### Added

- ESP8266 floppy controller firmware skeleton and PN532 NFC read/write over I2C.
- Floppy control socket client and daemon event wiring against the 86Box fork.
- Scratch floppy import with catalog validation.

## [2026-07-27]

### Added

- Retro PC appliance design specification.
- RetroBox .NET solution scaffold.
- YAML catalog configuration (`config.yaml`, `vms.yaml`, `floppies.yaml`) with
  validation and transactional saves.
- 86Box floppy control socket contract.
