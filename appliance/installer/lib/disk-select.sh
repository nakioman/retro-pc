#!/usr/bin/env bash
# Target-disk selection. SAFETY-CRITICAL: this is the gate that stops the
# installer from silently overwriting the wrong disk (issue #38).
#
# Sets the global TARGET_DISK (e.g. /dev/sda) on success.

# Resolve the whole-disk device that the live USB medium was booted from, so we
# can exclude it from the candidate list by default. Echoes a bare kernel name
# (e.g. "sdb"), or nothing if it cannot be determined.
live_usb_disk() {
    local src pk
    src="$(findmnt -n -o SOURCE --target "$MEDIUM" 2>/dev/null || true)"
    [ -n "$src" ] || return 0
    # SOURCE is usually a partition (…/sdb1); climb to its parent disk.
    pk="$(lsblk -no PKNAME "$src" 2>/dev/null | head -n1 || true)"
    if [ -n "$pk" ]; then
        printf '%s\n' "$pk"
    else
        # SOURCE was already a whole disk.
        basename "$src"
    fi
}

# Populate TARGET_DISK by listing candidate disks and requiring an explicit,
# typed confirmation before returning.
select_target_disk() {
    local exclude candidates=() name type size model tran line
    exclude="$(live_usb_disk)"

    while IFS= read -r line; do
        # Columns: NAME TYPE SIZE MODEL TRAN
        read -r name type size _ <<< "$line"
        [ "$type" = "disk" ] || continue
        [ "$name" = "$exclude" ] && continue
        candidates+=("$name")
    done < <(lsblk -dn -o NAME,TYPE,SIZE,MODEL,TRAN)

    if [ "${#candidates[@]}" -eq 0 ]; then
        die "No installable target disks found (only the USB installer device is present)."
    fi

    printf '\n' >&2
    log "Detected target disks (the USB installer device is already excluded):"
    printf '\n' >&2
    local i=1
    for name in "${candidates[@]}"; do
        # Re-read details for a readable line.
        read -r _ _ size model tran < <(lsblk -dn -o NAME,TYPE,SIZE,MODEL,TRAN "/dev/$name")
        printf '   %d) /dev/%-8s %8s  %s  [%s]\n' \
            "$i" "$name" "${size:-?}" "${model:-unknown model}" "${tran:-?}" >&2
        i=$((i + 1))
    done
    printf '\n' >&2

    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        die "Unattended mode: refusing to auto-select a destructive install target."
    fi

    local choice
    while :; do
        read -r -p "Select the disk to INSTALL ONTO (1-${#candidates[@]}, or q to abort): " choice < /dev/tty
        case "$choice" in
            q | Q) die "Aborted by user." ;;
            '' ) continue ;;
            *[!0-9]* ) warn "Enter a number." ; continue ;;
        esac
        if [ "$choice" -ge 1 ] && [ "$choice" -le "${#candidates[@]}" ]; then
            break
        fi
        warn "Out of range."
    done

    local disk="/dev/${candidates[$((choice - 1))]}"

    local existing_data
    existing_data="$(existing_data_partition "$disk" || true)"
    if [ -n "$existing_data" ]; then
        printf '\n' >&2
        warn "Existing RetroBox install detected (/data at $existing_data)."
        warn "Reinstall will offer to preserve /data — set RETROPC_WIPE_DATA=1 for a full wipe."
        printf '\n' >&2
    fi

    printf '\n' >&2
    warn "About to ERASE and repartition: $disk"
    lsblk -o NAME,SIZE,TYPE,FSTYPE,MOUNTPOINT,MODEL "$disk" >&2 || true
    printf '\n' >&2
    warn "ALL DATA ON $disk WILL BE PERMANENTLY DESTROYED."
    if ! confirm_token "ERASE $disk" \
        "Type exactly '${_c_red}ERASE $disk${_c_reset}' to continue: "; then
        die "Confirmation did not match. Nothing was written."
    fi

    TARGET_DISK="$disk"
    ok "Target disk confirmed: $TARGET_DISK"
}
