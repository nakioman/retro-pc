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

# _disk_partitions DISK -> one partition device path per line, for $DISK only.
# TYPE=part is the load-bearing filter: `lsblk -o PATH` also prints the disk
# node itself as its first line, so an unfiltered read hands callers /dev/sda
# where they expect /dev/sda1 — and callers here run mkfs on what they get.
_disk_partitions() {
    lsblk -n -o PATH,TYPE "$1" 2>/dev/null | awk '$2 == "part" { print $1 }'
}

# _partition_by_label DISK LABEL -> the partition of DISK carrying LABEL.
# Scoped to DISK on purpose: `blkid -L` searches every block device attached,
# including the installer USB and any second disk.
_partition_by_label() {
    local disk="$1" want="$2" part label
    while IFS= read -r part; do
        [ -b "$part" ] || continue
        label="$(blkid -s LABEL -o value "$part" 2>/dev/null || true)"
        [ "$label" = "$want" ] && { printf '%s\n' "$part"; return 0; }
    done < <(_disk_partitions "$disk")
    return 1
}

# Echo the first partition on $disk that carries the retropc-data label — i.e. a
# previous RetroBox appliance install — or nothing when this is a fresh disk.
# Works for both MBR and GPT tables (lookup is by label).
existing_data_partition() {
    _partition_by_label "$1" retropc-data
}

# _detect_table_type DISK -> gpt or msdos. Defaults to msdos when parted cannot
# read the table, so an unreadable/legacy disk is treated as a BIOS install.
_detect_table_type() {
    local disk="$1" label
    label="$(target_parted_disk_label "$disk")"
    [ -n "$label" ] || label="msdos"
    printf '%s\n' "$label"
}

# _mkfs_fat -> sets MKFS_FAT to the available FAT mkfs command, or dies.
# Sets a global rather than echoing: die() drops into an interactive recovery
# shell, which cannot work inside a $(...) subshell whose stdout is captured.
MKFS_FAT=""
_mkfs_fat() {
    if command -v mkfs.fat >/dev/null 2>&1; then
        MKFS_FAT=mkfs.fat
    elif command -v mkfs.vfat >/dev/null 2>&1; then
        MKFS_FAT=mkfs.vfat
    else
        die "Neither mkfs.fat nor mkfs.vfat found (dosfstools missing)."
    fi
}

# Partition and format the target disk. On a reinstall over an existing
# appliance, /data (VMs, catalog, floppies, snapshots) is preserved by default:
# only the root filesystem is rewritten. RETROPC_WIPE_DATA=1 forces a full wipe.
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
    read -r -p "Existing RetroBox install found. Preserve /data (VMs, floppies)? [y/N] " reply < /dev/tty
    case "$reply" in
        n | N | no | NO) return 1 ;;
        y | Y | yes | YES) return 0 ;;
        *) return 1 ;;
    esac
}

# Fresh GPT install: ESP (p1) + root (p2) + /data (p3).
_fresh_install_gpt() {
    local disk="$1"
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

    _mkfs_fat
    log "Formatting $ESP_PART as FAT32 (label retropc-esp)"
    "$MKFS_FAT" -F32 -n retropc-esp "$ESP_PART"
    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    log "Formatting $DATA_PART as ext4 (label retropc-data)"
    mkfs.ext4 -q -F -L retropc-data "$DATA_PART"

    ok "Partitioned and formatted $disk (GPT)"
}

# Reinstall over an existing GPT install: keep the current partition table and
# the /data partition with its contents; only root and ESP are reformatted.
# Partitions are located by label (retropc-root / retropc-data / retropc-esp),
# scoped to $disk — a bare `blkid -L` searches every block device in the system,
# which on a machine with a second RetroBox disk attached would happily format
# the wrong one.
_reinstall_preserving_data() {
    local disk="$1" root_part data_part esp_part part
    root_part="$(_partition_by_label "$disk" retropc-root || true)"
    data_part="$(_partition_by_label "$disk" retropc-data || true)"
    esp_part="$(_partition_by_label "$disk" retropc-esp || true)"

    [ -n "$root_part" ] || die "Reinstall: root partition (retropc-root) not found on $disk."
    [ -n "$data_part" ] || die "Reinstall: data partition (retropc-data) not found on $disk."
    [ "$root_part" != "$data_part" ] || die "Reinstall: root and data partition are the same ($data_part)."

    if [ -z "$esp_part" ]; then
        # Fall back to the only *partition* that is neither root nor data. This
        # must never consider the disk node itself: mkfs.fat on /dev/sda would
        # destroy the partition table (and with it the /data we promised to
        # keep), so _disk_partitions filters on TYPE=part.
        while IFS= read -r part; do
            [ "$part" = "$root_part" ] && continue
            [ "$part" = "$data_part" ] && continue
            esp_part="$part"
            break
        done < <(_disk_partitions "$disk")
    fi
    [ -n "$esp_part" ] && [ -b "$esp_part" ] \
        || die "Reinstall: ESP partition not found on $disk."
    [ "$esp_part" != "$disk" ] \
        || die "Reinstall: refusing to format the whole disk $disk as the ESP."

    ROOT_PART="$root_part"
    DATA_PART="$data_part"
    ESP_PART="$esp_part"
    PRESERVE_DATA=1

    partprobe "$disk" 2>/dev/null || true
    udevadm settle 2>/dev/null || true

    log "Reinstall: preserving /data ($DATA_PART) — VMs, catalog, floppies kept"
    _mkfs_fat
    log "Formatting $ESP_PART as FAT32 (label retropc-esp)"
    "$MKFS_FAT" -F32 -n retropc-esp "$ESP_PART"
    log "Formatting $ROOT_PART as ext4 (label retropc-root)"
    mkfs.ext4 -q -F -L retropc-root "$ROOT_PART"
    ok "Root + ESP reformatted; /data preserved at $DATA_PART"
}

# _migrate_staging_dir NEEDED_BYTES -> sets MIGRATE_STAGING to a directory with
# room to hold a copy of /data, or dies. (A global, not an echo: die() opens a
# recovery shell and cannot run inside a captured $(...) subshell.)
#
# The default lives under /var/tmp, which on this live-boot installer is RAM —
# live-boot puts the writable layer in a tmpfs. Staging tens of GiB of VMs there
# fills memory and kills the box *after* the disk has been repartitioned, with
# the only copy of /data in the RAM that just died. So refuse RAM-backed staging
# unless explicitly overridden, and check free space before anything
# destructive runs.
MIGRATE_STAGING=""
_migrate_staging_dir() {
    local needed="$1" dir fstype avail
    dir="${RETROPC_MIGRATE_STAGING_DIR:-/var/tmp/retropc-migrate}"
    mkdir -p "$dir" || die "Migration: cannot create staging directory $dir."

    fstype="$(findmnt -no FSTYPE -T "$dir" 2>/dev/null || true)"
    case "$fstype" in
        tmpfs|ramfs)
            [ "${RETROPC_MIGRATE_ALLOW_RAM_STAGING:-0}" = "1" ] || die \
"Migration: staging directory $dir is on $fstype (RAM). Copying /data there would exhaust memory mid-migration and lose it. Attach external storage and re-run with RETROPC_MIGRATE_STAGING_DIR=/path/on/that/disk, or set RETROPC_MIGRATE_ALLOW_RAM_STAGING=1 if /data is known to fit in RAM. Nothing has been written to $TARGET_DISK."
            warn "Staging /data in RAM ($dir) — RETROPC_MIGRATE_ALLOW_RAM_STAGING=1."
            ;;
    esac

    avail="$(df -B1 --output=avail "$dir" 2>/dev/null | tail -n1 | tr -d ' ')"
    [ -n "$avail" ] || die "Migration: cannot determine free space on $dir."
    [ "$avail" -ge "$needed" ] || die \
"Migration: staging $dir has $((avail / 1024 / 1024)) MiB free but /data needs $((needed / 1024 / 1024)) MiB. Free space or point RETROPC_MIGRATE_STAGING_DIR at a disk with room. Nothing has been written to $TARGET_DISK."

    MIGRATE_STAGING="$dir"
}

# BIOS->UEFI migration: stage /data off the legacy MBR install, repartition the
# disk as GPT, and restore /data onto the new p3. Requires an explicit
# RETROPC_MIGRATE_BIOS_TO_UEFI=1 or a typed MIGRATE confirmation — never
# auto-converts, so a declined migration aborts without touching the BIOS disk.
_migrate_bios_to_uefi() {
    local disk="$1" data_part staging old_mnt new_mnt used needed
    if [ "${RETROPC_MIGRATE_BIOS_TO_UEFI:-0}" != "1" ]; then
        if ! confirm_token "MIGRATE" \
            "BIOS->UEFI migration: /data will be staged and the disk repartitioned as GPT. Type 'MIGRATE' to continue: "; then
            die "Migration not confirmed. Back up /data and re-run with RETROPC_MIGRATE_BIOS_TO_UEFI=1."
        fi
    fi

    data_part="$(existing_data_partition "$disk" || true)"
    [ -n "$data_part" ] || die "Migration: no retropc-data partition found on $disk."

    umount_target 2>/dev/null || true

    old_mnt="/mnt/olddata"
    new_mnt="/mnt/newdata"
    mkdir -p "$old_mnt" "$new_mnt"
    mount -o ro "$data_part" "$old_mnt" \
        || die "Migration: cannot mount $data_part read-only."

    # Size the staging area from actual usage, not partition size, plus 5% for
    # rsync metadata and filesystem overhead. Sizing and the staging check both
    # run while the source is still mounted and before _fresh_install_gpt
    # touches the partition table, so any failure here leaves the legacy
    # install exactly as it was.
    used="$(df -B1 --output=used "$old_mnt" 2>/dev/null | tail -n1 | tr -d ' ')"
    [ -n "$used" ] || { umount "$old_mnt"; die "Migration: cannot size /data on $data_part."; }
    needed=$(( used + used / 20 + 64 * 1024 * 1024 ))

    _migrate_staging_dir "$needed"
    staging="$MIGRATE_STAGING"

    log "BIOS->UEFI migration: staging $((used / 1024 / 1024)) MiB of /data from $data_part to $staging"
    rm -rf "${staging:?}/data"
    rsync -aHAX "$old_mnt/" "$staging/data/" \
        || { umount "$old_mnt"; die "Migration: staging copy failed; $disk untouched."; }
    umount "$old_mnt"
    rmdir "$old_mnt" 2>/dev/null || true

    _fresh_install_gpt "$disk"

    # Past this point the legacy layout is gone and $staging is the only copy of
    # /data, so every failure keeps the staged copy and says where it is.
    mount "$DATA_PART" "$new_mnt" \
        || die "Migration: cannot mount new $DATA_PART. Staged /data kept at $staging/data."
    rsync -aHAX "$staging/data/" "$new_mnt/" \
        || die "Migration: restore to $DATA_PART failed. Staged /data kept at $staging/data."
    sync
    umount "$new_mnt"
    rmdir "$new_mnt" 2>/dev/null || true
    rm -rf "${staging:?}/data"
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
