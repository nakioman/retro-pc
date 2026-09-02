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
    local profile_root profile vm found_profile=0

    mkdir -p "$TARGET_MNT$RETROBOX_OPT" "$TARGET_MNT$BOX86_OPT" \
        "$TARGET_MNT/data/retrobox" "$TARGET_MNT/data/vms"

    [ -x "$RETROBOX_SRC" ] || die "Executable RetroBox binary not on medium: $RETROBOX_SRC"
    install -m 0755 "$RETROBOX_SRC" "$TARGET_MNT$RETROBOX_OPT/retrobox"
    # System.IO.Ports P/Invoke native library: NativeAOT leaves it as a dynamic
    # dependency, so it must sit next to the binary. Warn (don't abort) so media
    # built from a stale runtime artifact can still install.
    if [ -f "$INSTALL_SRC/libSystem.IO.Ports.Native.so" ]; then
        install -m 0755 "$INSTALL_SRC/libSystem.IO.Ports.Native.so" \
            "$TARGET_MNT$RETROBOX_OPT/libSystem.IO.Ports.Native.so"
    else
        warn "libSystem.IO.Ports.Native.so not on medium; serial/NFC will be unavailable"
    fi
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

    [ -d "$BOX86_SHADERS_SRC" ] || die "GLSL shaders not on medium: $BOX86_SHADERS_SRC"
    mkdir -p "$TARGET_MNT$BOX86_OPT/shaders"
    cp -a "$BOX86_SHADERS_SRC/." "$TARGET_MNT$BOX86_OPT/shaders/"
    ok "Staged GLSL shaders -> $BOX86_OPT/shaders"

    [ -f "$PAYLOAD_DIR/retrobox/vms.yaml" ] || die "VM catalog payload is missing"
    # On a reinstall that preserves /data, keep the existing catalog so user
    # edits (and any VMs they added) survive; otherwise (re)write the payload.
    if [ "$PRESERVE_DATA" = "1" ] && [ -f "$TARGET_MNT/data/retrobox/vms.yaml" ]; then
        log "Preserving existing /data/retrobox/vms.yaml"
    else
        install -m 0644 "$PAYLOAD_DIR/retrobox/vms.yaml" "$TARGET_MNT/data/retrobox/vms.yaml"
    fi
    profile_root="$PAYLOAD_DIR/profiles"
    [ -d "$profile_root" ] || die "VM profiles payload is missing"
    for profile in "$profile_root"/*/; do
        [ -d "$profile" ] || continue
        found_profile=1
        profile="${profile%/}"
        vm="${profile##*/}"
        [ -f "$profile/86box.cfg" ] || die "VM profile $vm is missing 86box.cfg"
        mkdir -p "$TARGET_MNT/data/vms/$vm"
        if [ "$PRESERVE_DATA" = "1" ] && [ -d "$TARGET_MNT/data/vms/$vm" ]; then
            # Reinstall: refresh the OS-managed files but never clobber the
            # user's VM disks (.raw/.vhd) or catalog (.yaml).
            rsync -a --exclude='*.raw' --exclude='*.vhd' --exclude='*.yaml' "$profile/." "$TARGET_MNT/data/vms/$vm/"
        else
            cp -a "$profile/." "$TARGET_MNT/data/vms/$vm/"
        fi
        [ -f "$TARGET_MNT/data/vms/$vm/86box.cfg" ] \
            || die "Installed VM profile $vm is missing 86box.cfg"
    done
    [ "$found_profile" = "1" ] || die "VM profiles payload contains no profiles"
    install -m 0755 "$PAYLOAD_DIR/scripts/retrobox-hdd-creation" \
        "$TARGET_MNT/usr/local/sbin/retrobox-hdd-creation"
    in_target /usr/local/sbin/retrobox-hdd-creation /data/vms
    ok "Staged VM catalog and profiles -> /data"
}
