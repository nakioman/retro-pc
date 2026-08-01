#!/usr/bin/env bash
# Extract the prebuilt appliance root filesystem onto the target disk and lay
# out the mutable /data tree. Offline: the OS comes from the squashfs carried on
# the USB, not from the network.
#
# Sets globals RETROBOX_STATUS and BOX86_STATUS ("installed").

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

# Copy the immutable runtime, ROMs, catalog, and VM profiles from the medium.
stage_binaries() {
    mkdir -p "$TARGET_MNT$RETROBOX_OPT" "$TARGET_MNT$BOX86_OPT" \
        "$TARGET_MNT/data/retrobox" "$TARGET_MNT/data/vms"

    [ -x "$RETROBOX_SRC" ] || die "Executable RetroBox binary not on medium: $RETROBOX_SRC"
    install -m 0755 "$RETROBOX_SRC" "$TARGET_MNT$RETROBOX_OPT/retrobox"
    RETROBOX_STATUS="installed"
    ok "Staged retrobox binary -> $RETROBOX_OPT/retrobox"

    [ -f "$BOX86_SRC" ] || die "86Box AppImage not on medium: $BOX86_SRC"
    install -m 0755 "$BOX86_SRC" "$TARGET_MNT$BOX86_OPT/86box.AppImage"
    BOX86_STATUS="installed"
    ok "Staged 86Box AppImage -> $BOX86_OPT/86box.AppImage"

    [ -d "$BOX86_ROMS_SRC" ] || die "86Box ROMs not on medium: $BOX86_ROMS_SRC"
    mkdir -p "$TARGET_MNT$BOX86_OPT/roms"
    cp -a "$BOX86_ROMS_SRC/." "$TARGET_MNT$BOX86_OPT/roms/"
    ok "Staged 86Box ROMs -> $BOX86_OPT/roms"

    [ -f "$PAYLOAD_DIR/retrobox/vms.yaml" ] || die "VM catalog payload is missing"
    install -m 0644 "$PAYLOAD_DIR/retrobox/vms.yaml" "$TARGET_MNT/data/retrobox/vms.yaml"
    for vm in 386sx16 pentium100; do
        local profile="$PAYLOAD_DIR/profiles/$vm"
        for required in 86box.cfg HDD.vhd shaders/syncmaster3.glsl; do
            [ -f "$profile/$required" ] || die "VM profile $vm is missing $required"
        done
        mkdir -p "$TARGET_MNT/data/vms/$vm"
        cp -a "$profile/." "$TARGET_MNT/data/vms/$vm/"
        for required in 86box.cfg HDD.vhd shaders/syncmaster3.glsl; do
            [ -f "$TARGET_MNT/data/vms/$vm/$required" ] \
                || die "Installed VM profile $vm is missing $required"
        done
    done
    ok "Staged VM catalog and profiles -> /data"
}
