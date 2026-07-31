#!/usr/bin/env bash
# Swap for the low-RAM (~2GB, shared with the GPU) appliance:
#   - zram: compressed RAM swap, preferred (fast, no disk wear).
#   - /data swapfile: modest disk backstop, used only after zram fills.
#
# Env: RETROPC_SWAP_GIB (disk swapfile size in GiB, default 2; 0 disables it).

: "${RETROPC_SWAP_GIB:=2}"

setup_swap() {
    _configure_zram
    _configure_swapfile
}

_configure_zram() {
    log "Configuring zram swap (systemd-zram-generator)"
    mkdir -p "$TARGET_MNT/etc/systemd"
    cat > "$TARGET_MNT/etc/systemd/zram-generator.conf" <<'EOF'
# Compressed RAM swap for the low-memory appliance. Higher priority than the
# /data swapfile, so the kernel prefers zram and only spills to disk under real
# memory pressure.
[zram0]
zram-size = ram
compression-algorithm = zstd
swap-priority = 100
EOF
}

_configure_swapfile() {
    if [ "$RETROPC_SWAP_GIB" = "0" ]; then
        log "Disk swapfile disabled (RETROPC_SWAP_GIB=0); zram only"
        return
    fi
    local sf="$TARGET_MNT/data/swapfile"
    log "Creating ${RETROPC_SWAP_GIB} GiB /data swapfile (OOM backstop)"
    rm -f "$sf"
    if ! fallocate -l "${RETROPC_SWAP_GIB}G" "$sf" 2>/dev/null; then
        dd if=/dev/zero of="$sf" bs=1M count=$((RETROPC_SWAP_GIB * 1024)) status=none
    fi
    chmod 600 "$sf"
    mkswap "$sf" >/dev/null
    # Lower priority than zram; ordered after the /data mount; nofail so a swap
    # problem never blocks boot.
    printf '/data/swapfile none swap sw,pri=10,nofail,x-systemd.requires-mounts-for=/data 0 0\n' \
        >> "$TARGET_MNT/etc/fstab"
}
