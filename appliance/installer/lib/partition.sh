#!/usr/bin/env bash
# Partition and format the target disk, then mount it under TARGET_MNT.
#
# Layout (MBR/msdos, BIOS/legacy — matches the Biostar H55HD / i3-540 target):
#   p1  ext4  /       ~10 GiB   (read-only root at runtime)
#   p2  ext4  /data   rest      (mutable application + system-overlay state)
#
# Sets globals ROOT_PART and DATA_PART.

: "${RETROPC_ROOT_GIB:=10}"
: "${RETROPC_WIPE_DATA:=0}"
: "${PRESERVE_DATA:=0}"

# Echo the first partition on $disk that carries the retropc-data label — i.e. a
# previous RetroBox appliance install — or nothing when this is a fresh disk.
existing_data_partition() {
    local disk="$1" part label
    while IFS= read -r part; do
        [ -b "$part" ] || continue
        label="$(blkid -s LABEL -o value "$part" 2>/dev/null || true)"
        [ "$label" = "retropc-data" ] && { printf '%s\n' "$part"; return 0; }
    done < <(lsblk -n -o PATH "$disk" 2>/dev/null || true)
    return 1
}

# Partition and format the target disk. On a reinstall over an existing
# appliance, /data (VMs, catalog, floppies, snapshots) is preserved by default:
# only the root filesystem is rewritten. RETROPC_WIPE_DATA=1 forces a full wipe.
partition_disk() {
    local disk="$1" existing_data
    existing_data="$(existing_data_partition "$disk" || true)"

    if [ -n "$existing_data" ] && [ "$RETROPC_WIPE_DATA" != "1" ]; then
        if _confirm_preserve_data; then
            _reinstall_preserving_data "$disk" "$existing_data"
            return 0
        fi
        warn "Full wipe selected: existing /data ($existing_data) will be destroyed."
    elif [ -n "$existing_data" ]; then
        warn "RETROPC_WIPE_DATA=1: wiping existing install; /data will be destroyed."
    fi

    _fresh_install "$disk"
}

_confirm_preserve_data() {
    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        log "Unattended: preserving existing /data"
        return 0
    fi
    local reply
    read -r -p "Existing RetroBox install found. Preserve /data (VMs, floppies)? [Y/n] " reply < /dev/tty
    case "$reply" in
        n | N | no | NO) return 1 ;;
        *) return 0 ;;
    esac
}

_fresh_install() {
    local disk="$1"
    log "Wiping existing signatures on $disk"
    wipefs -a "$disk" >/dev/null

    log "Creating MBR partition table (root ${RETROPC_ROOT_GIB} GiB + /data)"
    parted -s "$disk" mklabel msdos
    parted -s "$disk" mkpart primary ext4 1MiB "${RETROPC_ROOT_GIB}GiB"
    parted -s "$disk" mkpart primary ext4 "${RETROPC_ROOT_GIB}GiB" 100%
    parted -s "$disk" set 1 boot on

    # Make sure the kernel picks up the new partition nodes.
    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    ROOT_PART="$(part_dev "$disk" 1)"
    DATA_PART="$(part_dev "$disk" 2)"
    [ -b "$ROOT_PART" ] || die "Root partition $ROOT_PART did not appear."
    [ -b "$DATA_PART" ] || die "Data partition $DATA_PART did not appear."

    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    log "Formatting $DATA_PART as ext4 (label retropc-data)"
    mkfs.ext4 -q -F -L retropc-data "$DATA_PART"

    ok "Partitioned and formatted $disk"
}

# Reinstall over an existing install: keep the current partition table and the
# /data partition with its contents; only the root filesystem is reformatted.
# Assumes the standard layout p1=root, p2=/data.
_reinstall_preserving_data() {
    local disk="$1" data_part="$2" root_part
    root_part="$(part_dev "$disk" 1)"
    [ -b "$root_part" ] || die "Reinstall: root partition $root_part not found."
    [ "$root_part" != "$data_part" ] || die "Reinstall: root and data partition are the same ($data_part)."

    ROOT_PART="$root_part"
    DATA_PART="$data_part"
    PRESERVE_DATA=1

    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    log "Reinstall: preserving /data ($DATA_PART) — VMs, catalog, floppies kept"
    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    ok "Root reformatted; /data preserved at $DATA_PART"
}

mount_target() {
    log "Mounting target filesystems under $TARGET_MNT"
    mkdir -p "$TARGET_MNT"
    mount "$ROOT_PART" "$TARGET_MNT"
    mkdir -p "$TARGET_MNT/data"
    mount "$DATA_PART" "$TARGET_MNT/data"
}
