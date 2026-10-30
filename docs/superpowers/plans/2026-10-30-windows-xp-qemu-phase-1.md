# Windows XP on QEMU — Phase 1: QEMU Package on the Appliance

> **For agentic workers:** Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Ship our QEMU build (vmvga + dxvk-native) and the host stack it needs
(bare Xorg, a kiosk window manager, GTK 3, Vulkan, KVM's VMware backdoor) on
the appliance, exactly the way 86Box is shipped today. Nothing launches QEMU
yet; that is Phase 2.

**Architecture:** The QEMU release tarball is pinned (URL + SHA-256) in
`appliance/86box.env` next to the 86Box artifacts. `build-usb-installer.sh`
downloads and verifies it (or takes a local override), checks that it contains
`bin/qemu-system-i386`, and stages it on the ISO as a tarball. The installer
unpacks it to `/opt/qemu-retrobox` on the target and reports `qemu.package` in
`install-report.txt`. The host packages go into `appliance/debian/packages.txt`
(installed by mmdebstrap without Recommends); a dpkg `path-exclude` file trims
`mesa-vulkan-drivers` to Intel's ICD. A modprobe options file enables
`kvm.enable_vmware_backdoor=1`, and `retrobox` joins `kvm` and `render`.

**Tech Stack:** Bash (installer), mmdebstrap, Debian 13 packages, GitHub
Actions on the fork (`nakioman/qemu-vmvga`).

**Spec:** [`docs/superpowers/specs/2026-10-25-windows-xp-qemu-design.md`](../specs/2026-10-25-windows-xp-qemu-design.md)
(sections "Spike update (2026-10-29): the vmvga path" and "Phase 1").

## Global Constraints

- Follow the 86Box artifact pattern: pin, validate the SHA-256 format, download
  and verify, allow a local override, stage, install under `/opt`, report.
- QEMU pins live in `appliance/86box.env` (`QEMU_*` mirroring `BOX86_*`), not
  a separate `qemu.env` as the spec first proposed: one artifact manifest.
- Keep the root small (most of the disk belongs to `/data`): no Recommends,
  measure the size impact. Fresh installs grow the root from 2 to 3 GiB.
- No `dbus-user-session` / `libpam-systemd`: the appliance has no logind
  sessions and must keep it that way for `retrobox-boot` and 86Box.
- No guest drivers in the payload (the user supplies VMware's SVGA II driver).
- Gates: `mise run test`, `mise run format-check`, and
  `shellcheck -x -P SCRIPTDIR -S style` on `build-usb-installer.sh` and
  `install-retropc.sh` (as CI runs it).
- Conventional Commits: `chore(appliance):`, `docs:`.

---

## Task 1 — Publish the first QEMU release

- [x] Tag the fork's `retrobox` HEAD as `retrobox-11.1.1-1` (`gh api` refs).
- [x] Wait for `.github/workflows/retrobox.yml`; confirm the release carries
      `qemu-retrobox-11.1.1-<rev>-linux-x86_64.tar.gz`.
- [x] Download it, compute the SHA-256, list its layout (prefix-relative
      `bin/`, `lib/`, `share/qemu/`).
- [x] `ldd` `bin/qemu-system-i386` and `lib/*.so*` in a Debian 13 container
      with the base manifest + new packages installed; any `not found` library
      becomes a package in Task 3.

## Task 2 — Pin and fetch the artifact (`chore(appliance)`)

Files: `appliance/86box.env`, `appliance/installer/build-usb-installer.sh`.

- [x] Add `QEMU_VERSION`, `QEMU_RELEASE`, `QEMU_ARCHIVE_NAME`, `QEMU_URL`,
      `QEMU_SHA256`, `QEMU_PATH=/opt/qemu-retrobox`.
- [x] Validate `QEMU_SHA256` like the other pins.
- [x] `QEMU_ARCHIVE` env override (documented in the header), then
      `download_and_verify`.
- [x] Assert the tarball contains `bin/qemu-system-i386` and stage it as
      `install/qemu-retrobox.tar.gz` (a tarball keeps symlinks and modes,
      which the ISO tree does not guarantee).
- [x] Post-build assertion: the ISO carries `install/qemu-retrobox.tar.gz`.

## Task 3 — Host packages (`chore(appliance)`)

Files: `appliance/debian/packages.txt`,
`appliance/debian/dpkg-vulkan-intel-only.cfg`,
`appliance/installer/build-usb-installer.sh`.

- [x] Add, with comments: `xserver-xorg-core`, `xserver-xorg-input-libinput`,
      `xserver-xorg-legacy`, `xinit`, `matchbox-window-manager`,
      `libgtk-3-0t64`, `dbus-x11`, `libvulkan1`, `mesa-vulkan-drivers`, plus
      any library Task 1's `ldd` found missing.
- [x] Rewrite the "Deliberately excluded" footer (no desktop environment; the
      kiosk Xorg is the one exception, for QEMU only).
- [x] Target mmdebstrap: `--aptopt='Apt::Install-Recommends "false"'` (make
      the no-Recommends policy explicit) and
      `--dpkgopt=<dpkg-vulkan-intel-only.cfg>` (keep only
      `libvulkan_intel.so` + `intel_icd.json`).
- [x] Post-build assertions on the target rootfs: `Xorg`, `Xorg.wrap`,
      `xinit`, `matchbox-window-manager`, the Intel ICD present; the radeon
      ICD and `pam_systemd.so` absent.

## Task 4 — Install on the target (`chore(appliance)`)

Files: `lib/common.sh`, `lib/rootfs-extract.sh`, `lib/hardware-detect.sh`,
`lib/users.sh`, `lib/services.sh`, `payload/modprobe/retrobox-kvm.conf`,
`hardware-detect.md`.

- [x] `RETROPC_ROOT_GIB` default 2 -> 3 (`lib/partition.sh`); reinstalls that
      preserve `/data` keep their partition sizes.
- [x] `QEMU_SRC="$INSTALL_SRC/qemu-retrobox.tar.gz"`, `QEMU_OPT=/opt/qemu-retrobox`.
- [x] `stage_qemu` (from `stage_binaries`) unpacks the x86 subset
      (`qemu-system-i386`, `qemu-img`, `lib/libdxvk_*`, `share/qemu` minus
      non-x86 EDK2 images; 145 MB instead of 489 MB) with `--no-same-owner`,
      checks
      `bin/qemu-system-i386` is executable, sets `QEMU_STATUS=installed`.
- [x] `install-report.txt` gains `qemu.package=$QEMU_STATUS`.
- [x] `_retrobox_groups` adds `kvm` and `render`.
- [x] `services.sh` installs `/etc/modprobe.d/retrobox-kvm.conf`
      (`options kvm enable_vmware_backdoor=1`) before the initramfs rebuild.

## Task 5 — Docs and ADR (`docs`)

- [x] ADR `0008-qemu-xorg-gtk-on-the-appliance.md` (why QEMU, why vmvga +
      DXVK over qemu-3dfx, why GTK in bare Xorg instead of SDL on KMS, why
      `xserver-xorg-legacy` + `dbus-x11`). The spec reserves "ADR 0008" for
      Phase 2's engine ADR; that one becomes 0009. Index it.
- [x] `appliance/README.md`: runtime shape, QEMU under `/opt/qemu-retrobox`,
      `LD_LIBRARY_PATH`, groups, KVM option, rootfs size.
- [x] `appliance/installer/README.md`: download/override, report key.
- [x] Spec: Phase 1 status, `86box.env` instead of `qemu.env`, ADR number.

## Task 6 — Verify

- [x] `shellcheck -x -P SCRIPTDIR -S style` on both entry scripts.
- [x] `mise run test`, `mise run format-check`.
- [x] Report the measured rootfs delta.

## Deferred to Phase 2

- Starting Xorg from `retrobox-boot`: `/etc/X11/Xwrapper.config`
  (`allowed_users`, `needs_root_rights`), the `xinit` client script, and
  `LD_LIBRARY_PATH=/opt/qemu-retrobox/lib` for the QEMU process.
