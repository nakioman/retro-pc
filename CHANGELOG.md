# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Conventional Commits](https://www.conventionalcommits.org/).

Releases on GitHub are tagged `appliance-YYYYMMDD-<run>` and their notes are
generated from this history; see
[`CONTRIBUTING.md`](CONTRIBUTING.md#releases).

## [Unreleased]

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

### Fixed

- USB installer release notes: the workflow used `gh release list --limit 2
  --jq '.[1].tagName'` to detect the previous release tag, which always
  skipped the most recent prior release (and returned `null` when there was
  only one), so every release was published with an "Initial release"
  changelog. Now uses `.[0]` with `--limit 1`, and the git-tag fallback only
  matches `appliance-*` tags (skipping orphan tags from failed runs) and
  reads the first line of the sorted list.

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
