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
    _configure_identity
    _finalize_target_image
    ok "Services, SSH, Samba, networking, and splash configured"
}

_install_systemd_units() {
    log "Installing systemd units"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-daemon.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-daemon.service"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-boot.service" \
        "$TARGET_MNT/etc/systemd/system/retrobox-boot.service"
    install -m 0644 "$PAYLOAD_DIR/units/retrobox-tmpfiles.conf" \
        "$TARGET_MNT/etc/tmpfiles.d/retrobox.conf"

    install -m 0440 "$PAYLOAD_DIR/sudoers/retrobox" "$TARGET_MNT/etc/sudoers.d/retrobox"
    in_target visudo -cf /etc/sudoers.d/retrobox >/dev/null

    in_target systemctl enable retrobox-daemon.service >/dev/null 2>&1
    in_target systemctl enable retrobox-boot.service   >/dev/null 2>&1
}

_configure_ssh() {
    log "Configuring SSH (root login disabled)"
    mkdir -p "$TARGET_MNT/etc/ssh/sshd_config.d"
    cat > "$TARGET_MNT/etc/ssh/sshd_config.d/retropc.conf" <<'EOF'
# RetroBox appliance SSH policy.
PermitRootLogin no
PasswordAuthentication yes
EOF
    # Generate host keys now: /etc is read-only at runtime.
    in_target ssh-keygen -A >/dev/null
    in_target systemctl enable ssh.service >/dev/null 2>&1
}

_configure_samba() {
    log "Configuring Samba scratch share"
    install -m 0644 "$PAYLOAD_DIR/samba/retropc-scratch.conf" "$TARGET_MNT/etc/samba/smb.conf"
    in_target systemctl enable smbd.service >/dev/null 2>&1 || true
    in_target systemctl enable nmbd.service >/dev/null 2>&1 || true
}

_configure_network() {
    log "Configuring DHCP networking (systemd-networkd + resolved)"
    cat > "$TARGET_MNT/etc/systemd/network/20-wired.network" <<'EOF'
[Match]
Name=en* eth*

[Network]
DHCP=yes
EOF
    in_target systemctl enable systemd-networkd.service >/dev/null 2>&1
    in_target systemctl enable systemd-resolved.service >/dev/null 2>&1
    # resolv.conf lives on the writable /run so a read-only /etc is fine.
    ln -sf /run/systemd/resolve/stub-resolv.conf "$TARGET_MNT/etc/resolv.conf"
}

_configure_splash() {
    log "Configuring Plymouth boot splash"
    mkdir -p "$TARGET_MNT/etc/plymouth"
    install -m 0644 "$PAYLOAD_DIR/plymouth/plymouthd.conf" "$TARGET_MNT/etc/plymouth/plymouthd.conf"
    in_target plymouth-set-default-theme spinner >/dev/null 2>&1 || true
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
