#!/usr/bin/env bash
# Extract the prebuilt appliance root filesystem onto the target disk and lay
# out the mutable /data tree. Offline: the OS comes from the squashfs carried on
# the USB, not from the network.
#
# Sets globals RETROBOX_STATUS and BOX86_STATUS ("installed" | "PLACEHOLDER").

extract_rootfs() {
    [ -f "$TARGET_SQUASHFS" ] || die "Target rootfs not found on medium: $TARGET_SQUASHFS"
    log "Extracting appliance root filesystem (this takes a minute)"
    # -f: extract into the already-mounted target dir (root partition).
    unsquashfs -f -d "$TARGET_MNT" "$TARGET_SQUASHFS" >/dev/null
    ok "Root filesystem extracted"
}

create_data_tree() {
    log "Creating /data tree"
    local base="$TARGET_MNT/data"
    mkdir -p \
        "$base/retrobox" \
        "$base/vms" \
        "$base/floppies/scratch" \
        "$base/floppies/cataloged" \
        "$base/snapshots" \
        "$base/home" \
        "$base/system/var" "$base/system/.var.work"
    # Ownership is applied in users.sh once the retrobox uid/gid exist.
}

# Copy the RetroBox binary and 86Box AppImage from the medium into the target.
# The build stages a real file when RETROBOX_BIN / BOX86_APPIMAGE were provided,
# otherwise a ".placeholder" marker so CI can build with no large/copyright blobs.
stage_binaries() {
    mkdir -p "$TARGET_MNT$RETROBOX_OPT" "$TARGET_MNT$BOX86_OPT"

    if [ -f "$RETROBOX_SRC" ]; then
        install -m 0755 "$RETROBOX_SRC" "$TARGET_MNT$RETROBOX_OPT/retrobox"
        RETROBOX_STATUS="installed"
        ok "Staged retrobox binary -> $RETROBOX_OPT/retrobox"
    else
        cp "$RETROBOX_SRC.placeholder" "$TARGET_MNT$RETROBOX_OPT/retrobox.placeholder" 2>/dev/null || true
        RETROBOX_STATUS="PLACEHOLDER"
        warn "retrobox binary not on medium; installed a placeholder marker only"
    fi

    if [ -f "$BOX86_SRC" ]; then
        install -m 0755 "$BOX86_SRC" "$TARGET_MNT$BOX86_OPT/86box.AppImage"
        BOX86_STATUS="installed"
        ok "Staged 86Box AppImage -> $BOX86_OPT/86box.AppImage"
    else
        cp "$BOX86_SRC.placeholder" "$TARGET_MNT$BOX86_OPT/86box.AppImage.placeholder" 2>/dev/null || true
        BOX86_STATUS="PLACEHOLDER"
        warn "86Box AppImage not on medium; installed a placeholder marker only"
    fi
}
