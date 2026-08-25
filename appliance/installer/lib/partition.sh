#!/usr/bin/env bash
# Partition and format the target disk, then mount it under TARGET_MNT.
#
# Layout (GPT, UEFI):
#   p1  FAT32  /boot/efi  512 MiB  (EFI System Partition)
#   p2  ext4   /          ~10 GiB  (read-only root at runtime)
#   p3  ext4   /data      rest     (mutable application + system-overlay state)
#
# Sets globals ROOT_PART, DATA_PART, and ESP_PART.

: "${RETROPC_ROOT_GIB:=10}"
: "${ESP_SIZE_MIB:=512}"
: "${RETROPC_WIPE_DATA:=0}"
: "${PRESERVE_DATA:=0}"

# Echo the first partition on $disk that carries the retropc-data label — i.e. a
# previous RetroBox appliance install — or nothing when this is a fresh disk.
# Works for both MBR and GPT tables (lookup is by label).
existing_data_partition() {
    local disk="$1" part label
    while IFS= read -r part; do
        [ -b "$part" ] || continue
        label="$(blkid -s LABEL -o value "$part" 2>/dev/null || true)"
        [ "$label" = "retropc-data" ] && { printf '%s\n' "$part"; return 0; }
    done < <(lsblk -n -o PATH "$disk" 2>/dev/null || true)
    return 1
}

# _detect_table_type DISK -> gpt or msdos. Defaults to msdos when parted cannot
# read the table, so an unreadable/legacy disk is treated as a BIOS install.
_detect_table_type() {
    local disk="$1" label
    label="$(target_parted_disk_label "$disk")"
    [ -n "$label" ] || label="msdos"
    printf '%s\n' "$label"
}

# _mkfs_fat -> the available FAT mkfs command (mkfs.fat or mkfs.vfat), or die.
_mkfs_fat() {
    if command -v mkfs.fat >/dev/null 2>&1; then
        printf 'mkfs.fat\n'
    elif command -v mkfs.vfat >/dev/null 2>&1; then
        printf 'mkfs.vfat\n'
    else
        die "Neither mkfs.fat nor mkfs.vfat found (dosfstools missing)."
    fi
}

# Partition and format the target disk. On a reinstall over an existing
# appliance, /data (VMs, catalog, floppies, snapshots) is preserved by default:
# only the root filesystem is rewritten. RETROPC_WIPE_DATA=1 forces a full wipe.
# A legacy BIOS (MBR) install with /data is migrated to GPT/UEFI only after an
# explicit RETROPC_MIGRATE_BIOS_TO_UEFI=1 or a typed MIGRATE confirmation.
partition_disk() {
    local disk="$1" existing_data table_type
    existing_data="$(existing_data_partition "$disk" || true)"

    if [ -n "$existing_data" ] && [ "$RETROPC_WIPE_DATA" != "1" ]; then
        table_type="$(_detect_table_type "$disk")"
        if [ "$table_type" = "gpt" ]; then
            if _confirm_preserve_data; then
                _reinstall_preserving_data "$disk"
                return 0
            fi
            warn "Full wipe selected: existing /data ($existing_data) will be destroyed."
        else
            _migrate_bios_to_uefi "$disk"
            return 0
        fi
    elif [ -n "$existing_data" ]; then
        warn "RETROPC_WIPE_DATA=1: wiping existing install; /data will be destroyed."
    fi

    _fresh_install_gpt "$disk"
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

# Fresh GPT install: ESP (p1) + root (p2) + /data (p3).
_fresh_install_gpt() {
    local disk="$1" mkfs_fat
    log "Wiping existing signatures on $disk"
    wipefs -a "$disk" >/dev/null

    log "Creating GPT partition table (ESP ${ESP_SIZE_MIB} MiB + root ${RETROPC_ROOT_GIB} GiB + /data)"
    parted -s "$disk" mklabel gpt
    parted -s "$disk" mkpart ESP fat32 1MiB $((1 + ESP_SIZE_MIB))MiB
    parted -s "$disk" set 1 esp on
    parted -s "$disk" mkpart primary ext4 $((1 + ESP_SIZE_MIB))MiB $((1 + ESP_SIZE_MIB + RETROPC_ROOT_GIB * 1024))MiB
    parted -s "$disk" mkpart primary ext4 $((1 + ESP_SIZE_MIB + RETROPC_ROOT_GIB * 1024))MiB 100%

    # Make sure the kernel picks up the new partition nodes.
    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    ESP_PART="$(part_dev "$disk" 1)"
    ROOT_PART="$(part_dev "$disk" 2)"
    DATA_PART="$(part_dev "$disk" 3)"
    [ -b "$ESP_PART" ] || die "ESP partition $ESP_PART did not appear."
    [ -b "$ROOT_PART" ] || die "Root partition $ROOT_PART did not appear."
    [ -b "$DATA_PART" ] || die "Data partition $DATA_PART did not appear."

    mkfs_fat="$(_mkfs_fat)"
    log "Formatting $ESP_PART as FAT32 (label retropc-esp)"
    "$mkfs_fat" -F32 -n retropc-esp "$ESP_PART"
    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    log "Formatting $DATA_PART as ext4 (label retropc-data)"
    mkfs.ext4 -q -F -L retropc-data "$DATA_PART"

    ok "Partitioned and formatted $disk (GPT)"
}

# Reinstall over an existing GPT install: keep the current partition table and
# the /data partition with its contents; only root and ESP are reformatted.
# Partitions are located by label (retropc-root / retropc-data / retropc-esp).
_reinstall_preserving_data() {
    local disk="$1" root_part data_part esp_part part mkfs_fat
    root_part="$(blkid -L retropc-root 2>/dev/null || true)"
    data_part="$(blkid -L retropc-data 2>/dev/null || true)"
    esp_part="$(blkid -L retropc-esp 2>/dev/null || true)"

    [ -n "$root_part" ] && [ -b "$root_part" ] \
        || die "Reinstall: root partition (retropc-root) not found on $disk."
    [ -n "$data_part" ] && [ -b "$data_part" ] \
        || die "Reinstall: data partition (retropc-data) not found on $disk."
    [ "$root_part" != "$data_part" ] || die "Reinstall: root and data partition are the same ($data_part)."

    if [ -z "$esp_part" ] || [ ! -b "$esp_part" ]; then
        # Fall back to the only partition that is neither root nor data.
        while IFS= read -r part; do
            [ -b "$part" ] || continue
            [ "$part" = "$root_part" ] && continue
            [ "$part" = "$data_part" ] && continue
            esp_part="$part"
            break
        done < <(lsblk -n -o PATH "$disk" 2>/dev/null || true)
    fi
    [ -n "$esp_part" ] && [ -b "$esp_part" ] \
        || die "Reinstall: ESP partition not found on $disk."

    ROOT_PART="$root_part"
    DATA_PART="$data_part"
    ESP_PART="$esp_part"
    PRESERVE_DATA=1

    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    log "Reinstall: preserving /data ($DATA_PART) — VMs, catalog, floppies kept"
    mkfs_fat="$(_mkfs_fat)"
    log "Formatting $ESP_PART as FAT32 (label retropc-esp)"
    "$mkfs_fat" -F32 -n retropc-esp "$ESP_PART"
    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    ok "Root + ESP reformatted; /data preserved at $DATA_PART"
}

# BIOS->UEFI migration: stage /data off the legacy MBR install, repartition the
# disk as GPT, and restore /data onto the new p3. Requires an explicit
# RETROPC_MIGRATE_BIOS_TO_UEFI=1 or a typed MIGRATE confirmation — never
# auto-converts, so a declined migration aborts without touching the BIOS disk.
_migrate_bios_to_uefi() {
    local disk="$1" data_part staging old_mnt new_mnt
    if [ "${RETROPC_MIGRATE_BIOS_TO_UEFI:-0}" != "1" ]; then
        if ! confirm_token "MIGRATE" \
            "BIOS->UEFI migration: /data will be staged and the disk repartitioned as GPT. Type 'MIGRATE' to continue: "; then
            die "Migration not confirmed. Back up /data and re-run with RETROPC_MIGRATE_BIOS_TO_UEFI=1."
        fi
    fi

    data_part="$(existing_data_partition "$disk" || true)"
    [ -n "$data_part" ] || die "Migration: no retropc-data partition found on $disk."

    log "BIOS->UEFI migration: staging /data from $data_part"
    umount_target 2>/dev/null || true

    staging="/var/tmp/retropc-migrate"
    old_mnt="$staging/old"
    new_mnt="/mnt/newdata"
    rm -rf "$staging"
    mkdir -p "$staging" "$old_mnt" "$new_mnt"

    mount -o ro "$data_part" "$old_mnt"
    rsync -aHAX "$old_mnt/" "$staging/data/"
    umount "$old_mnt"

    _fresh_install_gpt "$disk"

    mount "$DATA_PART" "$new_mnt"
    rsync -aHAX "$staging/data/" "$new_mnt/"
    umount "$new_mnt"
    rmdir "$new_mnt" 2>/dev/null || true
    rm -rf "$staging"
    ok "Migration complete: /data restored onto new GPT layout"
}

mount_target() {
    log "Mounting target filesystems under $TARGET_MNT"
    mkdir -p "$TARGET_MNT"
    mount "$ROOT_PART" "$TARGET_MNT"
    mkdir -p "$TARGET_MNT/data" "$(esp_mount_point)"
    mount "$DATA_PART" "$TARGET_MNT/data"
    mount "$ESP_PART" "$(esp_mount_point)"
}

umount_target() {
    umount -l "$(esp_mount_point)" 2>/dev/null || true
    umount -l "$TARGET_MNT/data" 2>/dev/null || true
    umount -l "$TARGET_MNT" 2>/dev/null || true
}