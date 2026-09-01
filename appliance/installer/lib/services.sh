#!/usr/bin/env bash
# Install and enable the appliance services, SSH, Samba, networking, and the
# boot splash into the target. The installer is the authoritative source of
# these artifacts until the standalone child issues (#28/#29) land.
#
# Consumes globals: PAYLOAD_DIR, RETROPC_HOSTNAME (optional).

: "${RETROPC_HOSTNAME:=retrobox}"

install_services() {
    _install_systemd_units
    _configure_ssh
    _configure_samba
    _configure_network
    _configure_splash
    _configure_locale
    _configure_identity
    _finalize_target_image
    ok "Services, SSH, Samba, networking, and splash configured"
}

_install_systemd_units() {
    log "Installing systemd units"
    mkdir -p "$TARGET_MNT/etc/systemd/system" "$TARGET_MNT/etc/tmpfiles.d" \
        "$TARGET_MNT/etc/sudoers.d" "$TARGET_MNT/usr/local/sbin"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-daemon.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-daemon.service"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-boot.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-boot.service"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-wifi-firstboot.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-wifi-firstboot.service"
    install -m 0644 "$PAYLOAD_DIR/units/hdmi-fix.service" \
        "$TARGET_MNT/etc/systemd/system/hdmi-fix.service"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-tmpfiles.conf" \
        "$TARGET_MNT/etc/tmpfiles.d/retrobox.conf"

    install -m 0440 "$PAYLOAD_DIR/sudoers/retrobox" "$TARGET_MNT/etc/sudoers.d/retrobox"
    in_target visudo -cf /etc/sudoers.d/retrobox >/dev/null

    install -m 0755 "$PAYLOAD_DIR/scripts/retrobox-wifi-firstboot" \
        "$TARGET_MNT/usr/local/sbin/retrobox-wifi-firstboot"
    install -m 0755 "$PAYLOAD_DIR/scripts/retrobox-audio-setup" \
        "$TARGET_MNT/usr/local/sbin/retrobox-audio-setup"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-audio-setup.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-audio-setup.service"

    enable_unit retrobox-daemon.service
    enable_unit retrobox-boot.service
    enable_unit retrobox-wifi-firstboot.service
    enable_unit hdmi-fix.service
    enable_unit retrobox-audio-setup.service
}

_configure_ssh() {
    log "Configuring SSH (root login disabled)"
    mkdir -p "$TARGET_MNT/etc/ssh/sshd_config.d"
    cat > "$TARGET_MNT/etc/ssh/sshd_config.d/retropc.conf" <<'EOF'
# RetroBox appliance SSH policy.
PermitRootLogin no
PasswordAuthentication yes
# Do not accept a locale from the maintenance client that is not installed in
# the minimal image; this keeps apt, Perl, and system tools warning-free.
SetEnv LANG=C.UTF-8 LC_ALL=C.UTF-8
EOF
    # Keep host identity in /data so reinstalling the system does not make SSH
    # clients see a changed host key.
    local persistent_keys="$TARGET_MNT/data/system/ssh"
    mkdir -p "$persistent_keys"
    if find "$persistent_keys" -maxdepth 1 -type f -name 'ssh_host_*' -print -quit | grep -q .; then
        cp -a "$persistent_keys"/ssh_host_* "$TARGET_MNT/etc/ssh/"
        log "Restored persistent SSH host keys"
    else
        in_target ssh-keygen -A >/dev/null || warn "ssh-keygen -A failed; host keys may be missing"
        cp -a "$TARGET_MNT/etc/ssh"/ssh_host_* "$persistent_keys/" \
            || warn "Could not persist SSH host keys"
        chmod 600 "$persistent_keys"/*_key 2>/dev/null || true
        log "Persisted SSH host keys under /data/system/ssh"
    fi
    enable_unit ssh.service
}

_configure_samba() {
    log "Configuring Samba scratch share"
    mkdir -p "$TARGET_MNT/etc/samba"
    install -m 0644 "$PAYLOAD_DIR/samba/retropc-scratch.conf" "$TARGET_MNT/etc/samba/smb.conf"
    enable_unit smbd.service
    enable_unit nmbd.service
}

_configure_network() {
    log "Configuring DHCP networking (systemd-networkd + resolved)"
    mkdir -p "$TARGET_MNT/etc/systemd/network"
    cat > "$TARGET_MNT/etc/systemd/network/20-wired.network" <<'EOF'
[Match]
Name=en* eth*

[Network]
DHCP=yes
EOF
    enable_unit systemd-networkd.service
    enable_unit systemd-resolved.service
    # Don't stall boot up to ~2 min waiting for a DHCP lease on a possibly
    # network-less appliance.
    in_target systemctl mask systemd-networkd-wait-online.service >/dev/null 2>&1 \
        || warn "could not mask systemd-networkd-wait-online.service"
    # resolv.conf lives on the writable /run so a read-only /etc is fine.
    ln -sf /run/systemd/resolve/stub-resolv.conf "$TARGET_MNT/etc/resolv.conf"
}

_configure_splash() {
    log "Configuring Plymouth boot splash"
    mkdir -p "$TARGET_MNT/etc/plymouth"
    install -m 0644 "$PAYLOAD_DIR/plymouth/plymouthd.conf" "$TARGET_MNT/etc/plymouth/plymouthd.conf"
    in_target plymouth-set-default-theme spinner >/dev/null 2>&1 || true
    # Bring up the framebuffer/KMS early in the initramfs so Plymouth paints as
    # soon as possible instead of leaving text visible first.
    mkdir -p "$TARGET_MNT/etc/initramfs-tools/conf.d"
    printf 'FRAMEBUFFER=y\n' > "$TARGET_MNT/etc/initramfs-tools/conf.d/retropc-splash"
}

_configure_locale() {
    log "Configuring UTF-8 locale"
    # C.UTF-8 is provided by glibc and does not require the locales package or
    # generating a locale database in the minimal appliance image.
    mkdir -p "$TARGET_MNT/etc/default"
    printf 'LANG=C.UTF-8\n' > "$TARGET_MNT/etc/default/locale"
}

_configure_identity() {
    log "Setting hostname ($RETROPC_HOSTNAME) and machine-id"
    printf '%s\n' "$RETROPC_HOSTNAME" > "$TARGET_MNT/etc/hostname"
    cat > "$TARGET_MNT/etc/hosts" <<EOF
127.0.0.1   localhost
127.0.1.1   $RETROPC_HOSTNAME
::1         localhost ip6-localhost ip6-loopback
EOF
    # Seed a fixed machine-id into the read-only image.
    in_target systemd-machine-id-setup >/dev/null 2>&1 || true
}

_finalize_target_image() {
    log "Regenerating initramfs (plymouth + overlay) in target"
    in_target update-initramfs -u >/dev/null 2>&1 || \
        warn "update-initramfs reported issues; check the boot splash on first boot"
}
