#!/usr/bin/env bash
# Shared helpers for the RetroBox USB installer.
# Sourced by install-retropc.sh and the other lib/*.sh scripts.

# --- Layout constants -------------------------------------------------------

# Where live-boot mounts the USB installer medium. Override with RETROPC_MEDIUM
# for testing outside a live-boot environment.
: "${RETROPC_MEDIUM:=/run/live/medium}"
MEDIUM="$RETROPC_MEDIUM"

# Payload staged onto the medium by build-usb-installer.sh.
INSTALL_SRC="$MEDIUM/install"
TARGET_SQUASHFS="$INSTALL_SRC/target-rootfs.squashfs"
RETROBOX_SRC="$INSTALL_SRC/retrobox"
BOX86_SRC="$INSTALL_SRC/86box.AppImage"

# Where the target root filesystem is assembled during install.
TARGET_MNT="/mnt/target"

# Target-side paths (inside TARGET_MNT).
RETROBOX_OPT="/opt/retrobox"
BOX86_OPT="/opt/86box"
DATA_DIR="/data"

# Runtime user/group for the appliance.
RETROBOX_USER="retrobox"
RETROBOX_GROUP="retrobox"

# Non-interactive mode for smoke tests / CI (never partitions a real disk).
: "${RETROPC_UNATTENDED:=0}"

# --- Logging ----------------------------------------------------------------

_c_reset=$'\033[0m'; _c_blue=$'\033[1;34m'; _c_yellow=$'\033[1;33m'
_c_red=$'\033[1;31m'; _c_green=$'\033[1;32m'

log()  { printf '%s[*]%s %s\n' "$_c_blue"  "$_c_reset" "$*" >&2; }
ok()   { printf '%s[+]%s %s\n' "$_c_green" "$_c_reset" "$*" >&2; }
warn() { printf '%s[!]%s %s\n' "$_c_yellow" "$_c_reset" "$*" >&2; }
err()  { printf '%s[x]%s %s\n' "$_c_red"   "$_c_reset" "$*" >&2; }
die()  { err "$*"; exit 1; }

# --- Prompts ----------------------------------------------------------------

# confirm PROMPT -> returns 0 on yes, 1 on no. Defaults to no.
confirm() {
    local prompt="$1" reply
    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        return 1
    fi
    read -r -p "$prompt [y/N] " reply < /dev/tty
    case "$reply" in
        y | Y | yes | YES) return 0 ;;
        *) return 1 ;;
    esac
}

# confirm_token EXPECTED PROMPT -> 0 only if the user types EXPECTED exactly.
# Used to gate destructive disk writes: no default, no fuzzy match.
confirm_token() {
    local expected="$1" prompt="$2" reply
    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        return 1
    fi
    read -r -p "$prompt" reply < /dev/tty
    [ "$reply" = "$expected" ]
}

# --- Block-device helpers ---------------------------------------------------

# part_dev DISK N -> partition device path (sda -> sda1, nvme0n1 -> nvme0n1p1).
part_dev() {
    local disk="$1" n="$2"
    case "$disk" in
        *[0-9]) printf '%sp%s\n' "$disk" "$n" ;;
        *)      printf '%s%s\n'  "$disk" "$n" ;;
    esac
}

# fs_uuid DEVICE -> filesystem UUID (via blkid).
fs_uuid() {
    blkid -s UUID -o value "$1"
}

# --- Target chroot bind mounts ---------------------------------------------

mount_target_binds() {
    for d in dev dev/pts proc sys run; do
        mkdir -p "$TARGET_MNT/$d"
    done
    mount --bind /dev      "$TARGET_MNT/dev"
    mount --bind /dev/pts  "$TARGET_MNT/dev/pts"
    mount -t proc  proc    "$TARGET_MNT/proc"
    mount -t sysfs sysfs   "$TARGET_MNT/sys"
    mount -t tmpfs tmpfs   "$TARGET_MNT/run"
}

umount_target_binds() {
    for d in run sys proc dev/pts dev; do
        umount -l "$TARGET_MNT/$d" 2>/dev/null || true
    done
}

# in_target CMD... -> run a command inside the target chroot.
in_target() {
    chroot "$TARGET_MNT" /usr/bin/env -i \
        PATH=/usr/sbin:/usr/bin:/sbin:/bin \
        DEBIAN_FRONTEND=noninteractive \
        "$@"
}
