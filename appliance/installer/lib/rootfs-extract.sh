#!/usr/bin/env bash
# Stage the prebuilt appliance image onto the target /boot partition and lay out
# the mutable /data tree. Offline: the OS comes from the squashfs carried on the
# USB, not from the network.
#
# Sets globals BOOT_KVER, RETROBOX_STATUS and BOX86_STATUS ("installed").
# RETROBOX_STATUS/BOX86_STATUS are consumed by hardware-detect.sh.
# shellcheck disable=SC2034

stage_image() {
    [ -f "$TARGET_SQUASHFS" ] || die "Target squashfs not found on medium: $TARGET_SQUASHFS"

    # Kernel version: prefer the one recorded by build-usb-installer.sh; fall
    # back to extracting it from the squashfs listing.
    if [ -f "$INSTALL_SRC/boot-kver" ]; then
        BOOT_KVER="$(cat "$INSTALL_SRC/boot-kver")"
    else
        BOOT_KVER="$(unsquashfs -l "$TARGET_SQUASHFS" 2>/dev/null \
            | awk '/\/boot\/vmlinuz-/ { sub(".*/boot/vmlinuz-",""); print; exit }')"
    fi
    [ -n "$BOOT_KVER" ] || die "Could not determine kernel version from $TARGET_SQUASHFS"

    [ -f "$INSTALL_SRC/vmlinuz-$BOOT_KVER" ] \
        || die "Staged kernel not found: $INSTALL_SRC/vmlinuz-$BOOT_KVER"
    [ -f "$INSTALL_SRC/initrd.img-$BOOT_KVER" ] \
        || die "Staged initrd not found: $INSTALL_SRC/initrd.img-$BOOT_KVER"

    mkdir -p "$TARGET_MNT/boot"
    log "Staging kernel $BOOT_KVER to /boot"
    install -m 0644 "$INSTALL_SRC/vmlinuz-$BOOT_KVER" "$TARGET_MNT/boot/vmlinuz-$BOOT_KVER"
    install -m 0644 "$INSTALL_SRC/initrd.img-$BOOT_KVER" "$TARGET_MNT/boot/initrd.img-$BOOT_KVER"

    log "Staging target squashfs to /boot/root-$BOOT_KVER.squashfs"
    install -m 0644 "$TARGET_SQUASHFS" "$TARGET_MNT/boot/root-$BOOT_KVER.squashfs"
    ok "Image staged: kernel $BOOT_KVER + root-$BOOT_KVER.squashfs"
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
        "$base/system"
    # Ownership is applied in users.sh once the retrobox uid/gid exist.
    # live-boot creates /data/system/{upper,.overlay.work} at runtime.
}

# Copy the immutable runtime, ROMs, catalog, and VM profiles from the medium.
stage_binaries() {
    local profile_root profile vm required found_profile=0

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
        for required in 86box.cfg HDD.vhd shaders/syncmaster3.glsl; do
            [ -f "$profile/$required" ] || die "VM profile $vm is missing $required"
        done
        mkdir -p "$TARGET_MNT/data/vms/$vm"
        if [ "$PRESERVE_DATA" = "1" ] && [ -d "$TARGET_MNT/data/vms/$vm" ]; then
            # Reinstall: refresh the OS-managed files but never clobber the
            # user's VM disks (.vhd) or catalog (.yaml).
            rsync -a --exclude='*.vhd' --exclude='*.yaml' "$profile/." "$TARGET_MNT/data/vms/$vm/"
        else
            cp -a "$profile/." "$TARGET_MNT/data/vms/$vm/"
        fi
        for required in 86box.cfg HDD.vhd shaders/syncmaster3.glsl; do
            [ -f "$TARGET_MNT/data/vms/$vm/$required" ] \
                || die "Installed VM profile $vm is missing $required"
        done
    done
    [ "$found_profile" = "1" ] || die "VM profiles payload contains no profiles"
    ok "Staged VM catalog and profiles -> /data"
}
