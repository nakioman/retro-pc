# Task 6 report: installer documentation and verification

## Delivered documentation

- `appliance/installer/README.md` now documents downloading the commit-named
  GitHub Actions artifact, verifying its SHA-256 sidecar, writing the ISO to a
  deliberately chosen whole USB device, the destructive-write warning,
  Legacy/BIOS boot, and disposable-disk prototype limitation.
- It documents every remaining interactive Debian Installer answer: disk
  selection and destructive confirmation, hostname, first maintenance-user
  credentials, and timezone. It records the read-only `/` plus writable
  `/data` partition contract and its persistent paths.
- It supplies first-boot checks for hostname, sudo, timezone, SSH, Samba,
  `/data`, root mount options, CD-ROM path/install report, and visual
  fullscreen 86Box behavior. It also documents the maintenance systemd
  override, SSH/local-console recovery, `--maintenance`, updates, and restoring
  the read-only root state.
- `appliance/README.md` links the operational procedure and summarizes the
  BIOS-only, no-disk-hardcoding, recovery, generated-artifact, disposable-disk,
  and no-floppy boundaries.
- `profiles/pentium100/README.md` now distinguishes the portable profile
  template from the installed runtime profile: installation detects a physical
  optical drive, prefers `/dev/disk/by-id/...`, falls back to `/dev/sr0`, and
  writes `cdrom_01_host_drive` to `/data/vms/pentium100/86box.cfg`. It also
  explicitly states that no physical CD media is included.

## Concrete contract correction

The documented local command and the GitHub Actions workflow both execute
`./appliance/installer/build-installer.sh`. A real Linux container build showed
the tracked script was mode `100644`, so that invocation failed with
`Permission denied` after the publish task completed. The script mode is now
`100755`; no installer logic changed.

## Verification

| Check | Result |
| --- | --- |
| `mise run test` | Passed: 91 tests, 0 failed, 0 skipped. |
| `bash -n appliance/installer/*.sh` | Passed. |
| `bash tests/installer/installer-contract.sh` | Passed. It exercises the builder’s stubbed local ISO creation, BIOS-only El Torito checks, deterministic checksum/metadata output, payload inspection, no-floppy behavior, maintenance, CD-ROM detection, and workflow contract. |
| `git diff --check` | Passed. |
| Real local ISO build and inspection | Blocked by transient container network/package availability after a first run completed the Linux-x64 publish and exposed the executable-bit issue. The GitHub Actions artifact build below is the full hosted verification. |
| `./appliance/installer/build-installer.sh --help` | Passed after the executable-bit correction. |
| GitHub Actions artifact build | The task branch was pushed. Explicit `workflow_dispatch` returned `404` because this workflow is not present on the repository default branch, and the follow-up run query was blocked by transient GitHub API connection refusal. The workflow's `push` trigger includes `appliance/**`, so the pushed commit is eligible; hosted artifact completion could not be confirmed in this session. |

## Scope review

- BIOS-only scope remains intact; no UEFI boot path was added.
- Disk selection and destructive partition confirmation remain interactive;
  documentation uses `/dev/sdX` only as an operator-supplied placeholder and
  never selects `/dev/sda`.
- No credentials were introduced to `preseed.cfg`, installer configuration,
  workflow, or documentation examples.
- `86BOX_VERSION` remains configured in one file, and the builder retains its
  missing-asset failure behavior.
- Artifact naming and SHA-256 sidecars remain deterministic.
- No floppy controller, daemon, media insertion, NFC, or ESP8266 integration
  was added. Physical validation remains explicitly limited to a disposable
  disk prototype.

## Follow-up: release asset correction

The hosted artifact build failed with HTTP 404 after publishing `retrobox`.
The configured tag `v7.0.0-master.46` is valid, but its actual x86_64 asset is
`86Box-SDL-x86_64-46.AppImage`, not `86Box-Linux-x86_64.AppImage`.

The single editable `86BOX_ASSET` value in
`appliance/installer/install-retropc.conf` and both exact URL expectations in
`tests/installer/installer-contract.sh` now use the published asset name. The
workflow does not contain an asset-name literal: it supplies only the same
configured release version to the builder, so no workflow edit was required.

Test-first evidence: after changing the contract fixture to accept only the
published asset while retaining the stale configuration, the installer contract
failed with exit 22 and logged the old `86Box-Linux-x86_64.AppImage` URL. After
the one-line configuration correction, verification passed:

| Check | Result |
| --- | --- |
| `bash tests/installer/installer-contract.sh` | Passed. |
| `mise run test` | Passed: 91 tests, 0 failed, 0 skipped. |
| `bash -n appliance/installer/*.sh` | Passed. |
| `git diff --check` | Passed. |
| Repository search for the old asset string | No remaining matches outside Git metadata. |

## Final global-review fixes

- The workflow no longer overrides `86BOX_VERSION`. Its summary reads the
  `86box_version` field from the builder-generated ISO metadata.
- CI installs `debconf-utils` and runs
  `debconf-set-selections -c appliance/installer/preseed.cfg`; the source
  contract asserts the command and metadata-based summary behavior.
- The Debian Installer package selection now includes `fuse3`, matching the
  appliance package manifest required by the 86Box AppImage.

| Check | Result |
| --- | --- |
| `bash tests/installer/installer-contract.sh` | Passed. |
| `mise run test` | Passed: 91 tests, 0 failed, 0 skipped. |
| `bash -n appliance/installer/*.sh tests/installer/installer-contract.sh` | Passed. |
| YAML parse of `.github/workflows/build-appliance-installer.yml` | Passed. |
| `git diff --check` | Passed. |

`debconf-set-selections` is supplied by `debconf-utils` on the Ubuntu runner;
the local macOS environment does not provide that Debian command.

## Final global-review fixes

- Removed the workflow-level `86BOX_VERSION` override. The builder emits the
  selected configuration value in `build/retro-pc-installer.iso.json`, and the
  GitHub Actions summary reads the `86box_version` field from that metadata.
- CI installs `debconf-utils` and runs
  `debconf-set-selections -c appliance/installer/preseed.cfg` before the build.
  The source contract asserts that validation command, the absence of a
  workflow version override, and metadata-based summary output.
- Added `fuse3` to `d-i pkgsel/include`, matching the appliance package
  manifest and allowing the 86Box AppImage to mount at runtime.

| Check | Result |
| --- | --- |
| `bash tests/installer/installer-contract.sh` | Passed. |
| `mise run test` | Passed: 91 tests, 0 failed, 0 skipped. |
| `bash -n appliance/installer/*.sh tests/installer/installer-contract.sh` | Passed. |
| YAML parse of `.github/workflows/build-appliance-installer.yml` | Passed. |
| `git diff --check` | Passed. |

The local macOS environment does not provide `debconf-set-selections`; the
workflow installs `debconf-utils` and performs the actual Debian syntax check
on its Ubuntu runner.
