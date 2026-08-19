#!/usr/bin/env bash
# Partition and format the target disk, then mount it under TARGET_MNT.
#
# Layout (MBR/msdos, BIOS/legacy — matches the Biostar H55HD / i3-540 target):
#   p1  ext4  /boot   RETROPC_BOOT_MIB MiB   (kernel, initrd, GRUB, squashfs)
#   p2  ext4  /data   rest                   (mutable application + overlay state)
#
# Sets globals BOOT_PART and DATA_PART.

: "${RETROPC_BOOT_MIB:=512}"
: "${RETROPC_WIPE_DATA:=0}"
: "${PRESERVE_DATA:=0}"

# Echo the first partition on $disk that carries the given ext4 label — i.e. a
# previous RetroBox appliance install — or nothing when this is a fresh disk.
existing_partition() {
    local disk="$1" want_label="$2" part label
    while IFS= read -r part; do
        [ -b "$part" ] || continue
        label="$(blkid -s LABEL -o value "$part" 2>/dev/null || true)"
        [ "$label" = "$want_label" ] && { printf '%s\n' "$part"; return 0; }
    done < <(lsblk -n -o PATH "$disk" 2>/dev/null || true)
    return 1
}

# Partition and format the target disk. On a reinstall over an existing
# appliance, both /boot and /data (VMs, catalog, floppies, snapshots) are
# preserved by default: only the boot filesystem is rewritten so a fresh
# squashfs image can be staged. RETROPC_WIPE_DATA=1 forces a full wipe.
partition_disk() {
    local disk="$1" existing_boot existing_data
    existing_boot="$(existing_partition "$disk" retropc-boot || true)"
    existing_data="$(existing_partition "$disk" retropc-data || true)"

    if [ -n "$existing_boot" ] && [ -n "$existing_data" ] && [ "$RETROPC_WIPE_DATA" != "1" ]; then
        if _confirm_preserve_data; then
            _reinstall_preserving_data "$disk" "$existing_boot" "$existing_data"
            return 0
        fi
        warn "Full wipe selected: existing install ($existing_boot, $existing_data) will be destroyed."
    elif [ -n "$existing_boot" ] || [ -n "$existing_data" ]; then
        warn "RETROPC_WIPE_DATA=1: wiping existing install; /data will be destroyed."
    fi

    _fresh_install "$disk"
}

_confirm_preserve_data() {
    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        log "Unattended: preserving existing /boot and /data"
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

    log "Creating MBR partition table (boot ${RETROPC_BOOT_MIB} MiB + /data)"
    parted -s "$disk" mklabel msdos
    parted -s "$disk" mkpart primary ext4 1MiB "$((RETROPC_BOOT_MIB))MiB"
    parted -s "$disk" mkpart primary ext4 "$((RETROPC_BOOT_MIB))MiB" 100%
    parted -s "$disk" set 1 boot on

    # Make sure the kernel picks up the new partition nodes.
    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    BOOT_PART="$(part_dev "$disk" 1)"
    DATA_PART="$(part_dev "$disk" 2)"
    [ -b "$BOOT_PART" ] || die "Boot partition $BOOT_PART did not appear."
    [ -b "$DATA_PART" ] || die "Data partition $DATA_PART did not appear."

    log "Formatting $BOOT_PART as ext4 (label retropc-boot)"
    mkfs.ext4 -q -F -L retropc-boot "$BOOT_PART"
    log "Formatting $DATA_PART as ext4 (label retropc-data)"
    mkfs.ext4 -q -F -L retropc-data "$DATA_PART"

    ok "Partitioned and formatted $disk"
}

# Reinstall over an existing install: keep the current partition table and both
# the /boot and /data partitions with their contents; only the boot filesystem
# is reformatted so a fresh squashfs image can be staged. Assumes the standard
# layout p1=boot, p2=data (matches by label, not by partition number).
_reinstall_preserving_data() {
    local disk="$1" boot_part="$2" data_part="$3"
    [ -b "$boot_part" ] || die "Reinstall: boot partition $boot_part not found."
    [ "$boot_part" != "$data_part" ] || die "Reinstall: boot and data partition are the same ($data_part)."

    BOOT_PART="$boot_part"
    DATA_PART="$data_part"
    PRESERVE_DATA=1

    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    log "Reinstall: preserving /boot ($BOOT_PART) and /data ($DATA_PART) — VMs, catalog, floppies kept"
    log "Formatting $BOOT_PART as ext4 (label retropc-boot)"
    mkfs.ext4 -q -F -L retropc-boot "$BOOT_PART"
    ok "Boot reformatted; /data preserved at $DATA_PART"
}

mount_target() {
    log "Mounting target filesystems under $TARGET_MNT"
    mkdir -p "$TARGET_MNT"
    mkdir -p "$TARGET_MNT/boot"
    mount "$BOOT_PART" "$TARGET_MNT/boot"
    mkdir -p "$TARGET_MNT/data"
    mount "$DATA_PART" "$TARGET_MNT/data"
}
