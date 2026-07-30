# Debian Appliance Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a BIOS/Legacy-only Debian Installer ISO in GitHub Actions that interactively installs the Retro PC appliance without floppy/NFC integration.

**Architecture:** Remaster the official Debian 13 netinst ISO with a preseed file and an appliance payload. Debian Installer handles disk selection, confirmation, user credentials, hostname, timezone, and base installation; `preseed/late_command` invokes a target-side installer script that configures `/data`, Samba, 86Box, physical CD-ROM passthrough, systemd boot, and read-only-root support. GitHub Actions pins and downloads the 86Box x86_64 AppImage, builds `retrobox`, assembles the ISO, validates it, and uploads the ISO plus checksum.

**Tech Stack:** Debian 13 (Trixie) Installer, BIOS/MBR, shell scripts, `xorriso`, `curl`, `sha256sum`, GitHub Actions, .NET 10 via `mise`, 86Box AppImage release assets.

## Global Constraints

- Build and boot mode is BIOS/Legacy only; do not add UEFI requirements.
- The installer must show the target disk and require explicit confirmation before destructive partitioning.
- The installer must not embed a user password or other credentials.
- Root is configured read-only after installation; `/data` remains writable.
- The initial image excludes ESP8266, floppy daemon, NFC, and floppy import behavior.
- The initial 86Box version is `v7.0.0-master.46`, configured through one editable `86BOX_VERSION` value.
- The 86Box asset is `86Box-SDL-x86_64-46.AppImage`; a missing tag or asset must fail the build.
- Copyrighted DOS, Windows, drivers, games, BIOS ROMs, and floppy/CD media are excluded.
- Normal .NET workflows use `mise` tasks, including `mise run test` and `mise run publish-linux-x64`.

---

## File Map

- Create `appliance/installer/preseed.cfg`: Debian Installer defaults, interactive question flags, package selection, partition recipe, and late command.
- Create `appliance/installer/install-retropc.sh`: target-side post-install orchestration; accepts `/target` only when called by d-i and runs in the installed system when invoked directly for testing.
- Create `appliance/installer/install-retropc.conf`: editable appliance build values, including `86BOX_VERSION`, release repository, and installation paths.
- Create `appliance/installer/systemd/retrobox-boot.service`: tty1 fullscreen launcher and restart/recovery behavior.
- Create `appliance/installer/samba/smb.conf`: restricted scratch share configuration.
- Create `appliance/installer/read-only-root.conf`: tmpfiles/fstab/systemd settings needed for read-only root and writable `/data`.
- Create `appliance/installer/build-installer.sh`: downloads the pinned Debian installer and 86Box artifact, publishes `retrobox`, assembles the BIOS ISO, and emits metadata/checksum.
- Create `appliance/installer/README.md`: local build, USB writing, installation warnings, first boot, recovery, and physical verification procedure.
- Create `.github/workflows/build-appliance-installer.yml`: Ubuntu workflow that validates, builds, inspects, and uploads artifacts.
- Create `tests/installer/installer-contract.sh`: shell-level contract tests for required values, paths, destructive confirmation, BIOS-only flags, and artifact names.
- Modify `appliance/README.md`: link the installer workflow and explain the current no-floppy scope.
- Modify `profiles/pentium100/README.md`: document generated physical CD-ROM configuration and the installer override boundary.

## Task 1: Establish the installer configuration contract

**Files:**
- Create: `appliance/installer/install-retropc.conf`
- Create: `tests/installer/installer-contract.sh`
- Modify: `appliance/README.md`

**Interfaces:**
- Produces shell variables consumed by `build-installer.sh` and `install-retropc.sh`: `86BOX_REPOSITORY`, `86BOX_VERSION`, `86BOX_ASSET`, `RETROBOX_CONFIG_ROOT`, `RETROBOX_DATA_ROOT`, `INSTALLER_LABEL`.
- Produces contract checks runnable without a Debian target system.

- [ ] **Step 1: Write failing contract checks** for the pinned release, x86_64 asset, BIOS-only label, `/data` root, and absence of credentials.
- [ ] **Step 2: Run the checks** with `bash tests/installer/installer-contract.sh`; verify they fail because the configuration does not exist.
- [ ] **Step 3: Add the configuration file** with `86BOX_VERSION="v7.0.0-master.46"` and explicit paths.
- [ ] **Step 4: Add documentation** stating that changing `86BOX_VERSION` is the supported release update mechanism.
- [ ] **Step 5: Run the checks** again and verify they pass.
- [ ] **Step 6: Commit** with `git add appliance/installer/install-retropc.conf appliance/README.md tests/installer/installer-contract.sh && git commit -m "feat(appliance): define installer configuration contract"`.

## Task 2: Define Debian Installer interaction and disk layout

**Files:**
- Create: `appliance/installer/preseed.cfg`
- Modify: `tests/installer/installer-contract.sh`

**Interfaces:**
- `preseed.cfg` invokes `/cdrom/retropc/install-retropc.sh` through `d-i preseed/late_command`.
- The installer leaves target disk choice, destructive confirmation, hostname, regular-user credentials, and timezone interactive.
- The partition recipe creates BIOS-compatible root and writable `/data` filesystems without hardcoding `/dev/sda`.

- [ ] **Step 1: Add contract checks** that reject `partman-auto/disk`, embedded passwords, UEFI boot entries, and a late command missing the target script.
- [ ] **Step 2: Add the preseed defaults** for Debian 13, BIOS/MBR GRUB, SSH server, sudo, Samba, audio utilities, networking, and no desktop environment.
- [ ] **Step 3: Keep required questions interactive** by leaving credential/disk values unset and resetting `seen` flags only where a default must remain visible.
- [ ] **Step 4: Add the partition recipe** with root and `/data` mount points and UUID-based fstab generation.
- [ ] **Step 5: Run `bash tests/installer/installer-contract.sh`** and validate the preseed syntax with `debconf-set-selections -c appliance/installer/preseed.cfg` when the Debian package is available.
- [ ] **Step 6: Commit** with `git add appliance/installer/preseed.cfg tests/installer/installer-contract.sh && git commit -m "feat(appliance): add interactive Debian installer preseed"`.

## Task 3: Implement target-side appliance provisioning

**Files:**
- Create: `appliance/installer/install-retropc.sh`
- Create: `appliance/installer/systemd/retrobox-boot.service`
- Create: `appliance/installer/samba/smb.conf`
- Create: `appliance/installer/read-only-root.conf`
- Modify: `tests/installer/installer-contract.sh`

**Interfaces:**
- `install-retropc.sh [--target-root PATH] [--config PATH]` is idempotent and defaults to `/` when run on the installed system.
- It reads `86BOX_VERSION` and asset metadata from `install-retropc.conf`.
- It writes `/etc/retrobox-appliance/install-report.txt`, `/etc/systemd/system/retrobox-boot.service`, `/etc/samba/smb.conf`, and `/etc/fstab` entries using filesystem UUIDs supplied by the installed system.

- [ ] **Step 1: Add shell tests** for argument parsing, refusing a non-root target, creating `/data/retrobox`, `/data/vms`, `/data/floppies/scratch`, `/data/floppies/cataloged`, and `/data/snapshots`, and never enabling a floppy daemon.
- [ ] **Step 2: Implement account and directory provisioning** with `retrobox` system user/group, administrator sudo preserved, Samba scratch ownership, and no network write access to cataloged data or VM disks.
- [ ] **Step 3: Implement 86Box installation** from the payload AppImage, copy both profiles/shaders, set executable permissions, and verify the expected pinned release metadata.
- [ ] **Step 4: Implement CD-ROM detection** preferring `/dev/disk/by-id/*` and falling back to `/dev/sr0`; write the selected path to the Pentium profile and record missing-device state explicitly in the install report.
- [ ] **Step 5: Implement `retrobox-boot.service`** to launch the configured Pentium profile on tty1, restart on failure, and provide an documented maintenance override that prevents fullscreen launch.
- [ ] **Step 6: Install the Samba config** with only `retro-floppy-scratch` mapped to `/data/floppies/scratch` and guest access disabled.
- [ ] **Step 7: Configure read-only support** for root, tmpfs/runtime paths, `/data`, and a documented recovery remount; do not enable the final read-only switch until the service files are installed.
- [ ] **Step 8: Run shell syntax and contract tests** with `bash -n` and `bash tests/installer/installer-contract.sh`.
- [ ] **Step 9: Commit** with `git add appliance/installer tests/installer/installer-contract.sh && git commit -m "feat(appliance): provision Debian runtime without floppy integration"`.

## Task 4: Build the bootable BIOS installer ISO locally

**Files:**
- Create: `appliance/installer/build-installer.sh`
- Modify: `tests/installer/installer-contract.sh`
- Modify: `appliance/installer/README.md`

**Interfaces:**
- `./appliance/installer/build-installer.sh --output build/retro-pc-installer.iso` creates the ISO and sibling `.sha256` and `.json` metadata files.
- The script accepts `86BOX_VERSION` from `install-retropc.conf` or the environment and fails on a missing release asset.

- [ ] **Step 1: Add build contract checks** for required command checks, pinned release URL construction, ISO output, SHA-256 output, and BIOS boot preservation.
- [ ] **Step 2: Implement dependency checks** for `curl`, `xorriso`, `sha256sum`, `mise`, and `git`; print an actionable error for each missing command.
- [ ] **Step 3: Publish `retrobox`** with `mise run publish-linux-x64` and copy the resulting `retrobox` binary into the installer payload.
- [ ] **Step 4: Download and verify 86Box** from the exact GitHub release URL and stage it as `86Box.AppImage`; use HTTP failure and asset-size checks to reject a bad download.
- [ ] **Step 5: Download the pinned Debian 13 amd64 netinst ISO**, preserve its BIOS boot catalog with `xorriso`, and add `preseed.cfg`, scripts, configuration, profiles, shaders, and published `retrobox` under `/retropc`.
- [ ] **Step 6: Add the `preseed/file=/cdrom/preseed.cfg` BIOS boot argument** while retaining the installer’s BIOS boot image; do not add an EFI partition or UEFI boot path.
- [ ] **Step 7: Inspect the generated ISO** with `xorriso -indev`, verify `/preseed.cfg`, `/retropc/install-retropc.sh`, the 86Box asset, profiles, and BIOS boot entries, then write checksum and metadata.
- [ ] **Step 8: Run the local build and contract checks** on an Ubuntu/Debian host or CI-equivalent environment.
- [ ] **Step 9: Commit** with `git add appliance/installer tests/installer/installer-contract.sh && git commit -m "feat(appliance): build BIOS Debian installer ISO"`.

## Task 5: Automate the build in GitHub Actions

**Files:**
- Create: `.github/workflows/build-appliance-installer.yml`
- Modify: `appliance/installer/README.md`

**Interfaces:**
- Workflow name: `Build Debian appliance installer`.
- Triggers: `workflow_dispatch` and pushes affecting `appliance/**`, `profiles/**`, `src/**`, `mise.toml`, and the workflow itself.
- Artifact name: `retro-pc-debian-installer-${{ github.sha }}`.
- Artifact contents: BIOS ISO, SHA-256 checksum, and JSON build metadata.

- [ ] **Step 1: Add a workflow contract check** for `ubuntu-latest`, checkout, build invocation, test invocation, and `actions/upload-artifact`.
- [ ] **Step 2: Define the workflow** with read-only repository permissions, `actions/checkout`, setup for the repository’s `mise` toolchain, and required Ubuntu packages.
- [ ] **Step 3: Run tests and build** using `mise run test` followed by `./appliance/installer/build-installer.sh`.
- [ ] **Step 4: Upload artifacts** with `actions/upload-artifact@v4`, `if-no-files-found: error`, and a seven-day retention for prototype iterations.
- [ ] **Step 5: Publish the selected `86BOX_VERSION` and checksum** in the workflow summary without exposing credentials.
- [ ] **Step 6: Validate the workflow YAML** with a local parser or GitHub Actions lint tool available on the runner.
- [ ] **Step 7: Commit** with `git add .github/workflows/build-appliance-installer.yml appliance/installer/README.md && git commit -m "ci(appliance): build installer image artifact"`.

## Task 6: Document and execute verification

**Files:**
- Modify: `appliance/installer/README.md`
- Modify: `appliance/README.md`
- Modify: `profiles/pentium100/README.md`

**Interfaces:**
- Documentation provides exact USB-writing commands, the destructive-install warning, the BIOS boot procedure, first-boot checks, SSH/Samba tests, CD-ROM verification, recovery, and known no-floppy scope.

- [ ] **Step 1: Document artifact download and USB writing** using an explicit device path and a warning to verify the USB device before `dd`.
- [ ] **Step 2: Document the interactive installer answers** and the expected root `/data` layout.
- [ ] **Step 3: Document first boot verification** for hostname, sudo, timezone, SSH, Samba, `/data`, root read-only mode, CD-ROM path, and 86Box fullscreen.
- [ ] **Step 4: Document recovery** through the maintenance boot override, SSH, and remounting root writable for updates.
- [ ] **Step 5: Update the Pentium profile docs** to explain that the installer writes the physical CD-ROM path and that no CD media is included.
- [ ] **Step 6: Run the full verification commands**: `mise run test`, `bash -n appliance/installer/*.sh`, contract tests, local ISO inspection, and a GitHub Actions artifact build.
- [ ] **Step 7: Commit** with `git add appliance/README.md appliance/installer/README.md profiles/pentium100/README.md && git commit -m "docs(appliance): document installer verification"`.

## Self-review checklist

- The plan covers all design requirements: BIOS-only boot, interactive disk confirmation, credentials, timezone, root read-only, writable `/data`, Samba, physical CD-ROM, fullscreen 86Box, pinned version, GitHub Actions artifact, checksum, recovery, and no floppy integration.
- No task hardcodes `/dev/sda`; disk selection remains an installer interaction.
- No password is placed in `preseed.cfg`, the repository, or the workflow.
- The 86Box release is configurable in one file and the build fails on missing assets.
- Every generated artifact has a deterministic name and checksum.
- The first physical test uses a disposable disk and does not claim final RTM hardware validation.
