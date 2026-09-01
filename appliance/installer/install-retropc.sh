#!/usr/bin/env bash
# RetroBox appliance installer — runs on the booted USB (auto-started on tty1)
# and installs the Debian appliance onto an internal disk.
#
# SAFETY: this partitions and formats a disk. It lists candidate disks, excludes
# the USB installer device, and requires an explicit typed confirmation before
# writing anything (lib/disk-select.sh).
#
# Env overrides (mainly for testing):
#   RETROPC_MEDIUM              live medium mount (default /run/live/medium)
#   RETROPC_ROOT_GIB           root partition size in GiB (default 10)
#   RETROPC_HOSTNAME           target hostname (default retrobox)
#   RETROPC_RETROBOX_PASSWORD  set retrobox password non-interactively
#   RETROPC_UNATTENDED=1       never prompt / never auto-pick a destructive target

# -E so the ERR trap fires inside sourced functions, not just top-level.
set -Eeuo pipefail

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIB_DIR="$SELF_DIR/lib"
PAYLOAD_DIR="$SELF_DIR/payload"

# shellcheck source=lib/common.sh
. "$LIB_DIR/common.sh"
# shellcheck source=lib/disk-select.sh
. "$LIB_DIR/disk-select.sh"
# shellcheck source=lib/partition.sh
. "$LIB_DIR/partition.sh"
# shellcheck source=lib/rootfs-extract.sh
. "$LIB_DIR/rootfs-extract.sh"
# shellcheck source=lib/fstab.sh
. "$LIB_DIR/fstab.sh"
# shellcheck source=lib/swap.sh
. "$LIB_DIR/swap.sh"
# shellcheck source=lib/users.sh
. "$LIB_DIR/users.sh"
# shellcheck source=lib/hardware-detect.sh
. "$LIB_DIR/hardware-detect.sh"
# shellcheck source=lib/services.sh
. "$LIB_DIR/services.sh"
# shellcheck source=lib/grub-install.sh
. "$LIB_DIR/grub-install.sh"

cleanup() {
    umount_target_binds
    # umount_target unmounts in reverse-mount order (ESP -> /data -> /), so the
    # bind mounts are torn down before the underlying filesystems go away.
    umount_target 2>/dev/null || {
        umount -l "$TARGET_MNT/boot/efi" 2>/dev/null || true
        umount -l "$TARGET_MNT/data"      2>/dev/null || true
        umount -l "$TARGET_MNT"           2>/dev/null || true
    }
}
trap cleanup EXIT
# On any unhandled failure, freeze in a recovery shell instead of exiting (which
# would loop back to the banner). Leaves the target mounted for inspection.
trap 'fail $LINENO' ERR

banner() {
    cat >&2 <<'EOF'

  ██████  ███████ ████████ ██████   ██████  ██████   ██████  ██   ██
  ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
  ██████  █████      ██    ██████  ██    ██ ██████  ██    ██   ███
  ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
  ██   ██ ███████    ██    ██   ██  ██████  ██████   ██████  ██   ██

                         Appliance installer

EOF
}

require_root() {
    [ "$(id -u)" = "0" ] || die "The installer must run as root."
}

main() {
    # Tee everything to a log so a failure is readable even after it scrolls.
    : > "$LOGFILE" 2>/dev/null || LOGFILE="/tmp/retropc-install.log"
    exec > >(tee -a "$LOGFILE") 2>&1

    banner
    require_root
    [ -d "$MEDIUM" ] || die "Live medium not found at $MEDIUM (set RETROPC_MEDIUM)."

    # 1. Choose + confirm the destructive target (safety gate).
    select_target_disk

    # 2. Partition, format, mount.
    partition_disk "$TARGET_DISK"
    mount_target

    # 3. Lay down the OS image + mutable /data, stage runtime and profiles.
    extract_rootfs
    create_data_tree
    stage_binaries

    # 4. Host-specific config generated into the target.
    write_fstab
    setup_swap
    mount_target_binds
    create_accounts
    detect_and_record_hardware
    install_services
    install_bootloader
    umount_target_binds

    # 5. Flush and report.
    sync
    printf '\n' >&2
    ok "Installation complete."
    log "Install report saved to /data/retrobox/install-report.txt on the target disk."
    printf '\n' >&2    

    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        return 0
    fi
    read -r -p "Press Enter to reboot now (USB still inserted)... " _ < /dev/tty
    cleanup
    trap - EXIT ERR
    reboot
}

main "$@"
