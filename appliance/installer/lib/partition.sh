#!/usr/bin/env bash
# Partition and format the target disk, then mount it under TARGET_MNT.
#
# Layout (MBR/msdos, BIOS/legacy — matches the Biostar H55HD / i3-540 target):
#   p1  ext4  /       ~10 GiB   (read-only root at runtime)
#   p2  ext4  /data   rest      (mutable application + system-overlay state)
#
# Sets globals ROOT_PART and DATA_PART.

: "${RETROPC_ROOT_GIB:=10}"

partition_disk() {
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

mount_target() {
    log "Mounting target filesystems under $TARGET_MNT"
    mkdir -p "$TARGET_MNT"
    mount "$ROOT_PART" "$TARGET_MNT"
    mkdir -p "$TARGET_MNT/data"
    mount "$DATA_PART" "$TARGET_MNT/data"
}
