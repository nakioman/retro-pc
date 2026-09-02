#!/usr/bin/env bash
# Account setup: lock root, create the single `retrobox` account (service user +
# maintenance login with sudo), and prompt for its password.

# Hardware groups the appliance user should be in when they exist in the image.
_retrobox_groups() {
    local want=(sudo audio video input dialout cdrom plugdev gpio) have=()
    local g
    for g in "${want[@]}"; do
        if in_target getent group "$g" >/dev/null 2>&1; then
            have+=("$g")
        fi
    done
    local IFS=,
    printf '%s\n' "${have[*]}"
}

create_accounts() {
    log "Locking root account (no interactive root login)"
    in_target passwd -l root >/dev/null 2>&1 || true

    if in_target getent group gpio >/dev/null 2>&1; then
        log "GPIO group already exists"
    else
        log "Creating GPIO group"
        in_target groupadd gpio
    fi

    if in_target getent passwd "$RETROBOX_USER" >/dev/null 2>&1; then
        log "User $RETROBOX_USER already exists; ensuring home + groups"
    else
        log "Creating $RETROBOX_USER account (home on /data, shell /bin/bash)"
        in_target useradd \
            --create-home \
            --home-dir "/data/home/$RETROBOX_USER" \
            --shell /bin/bash \
            "$RETROBOX_USER"
    fi

    local groups
    groups="$(_retrobox_groups)"
    [ -n "$groups" ] && in_target usermod -aG "$groups" "$RETROBOX_USER"

    # Own the mutable application state; leave /data/system (overlay dirs) to root.
    in_target chown -R "$RETROBOX_USER:$RETROBOX_GROUP" \
        "/data/retrobox" "/data/floppies" "/data/vms" "/data/snapshots" \
        "/data/home/$RETROBOX_USER"
    in_target chown "$RETROBOX_USER:$RETROBOX_GROUP" "$RETROBOX_OPT" "$BOX86_OPT" || true

    set_retrobox_password
    ok "Accounts configured (root locked, $RETROBOX_USER ready)"
}

set_retrobox_password() {
    if [ -n "${RETROPC_RETROBOX_PASSWORD:-}" ]; then
        printf '%s:%s\n' "$RETROBOX_USER" "$RETROPC_RETROBOX_PASSWORD" | in_target chpasswd
        log "Set $RETROBOX_USER password from RETROPC_RETROBOX_PASSWORD"
        return
    fi
    if [ "$RETROPC_UNATTENDED" = "1" ]; then
        warn "Unattended: leaving $RETROBOX_USER password locked (set one later)."
        in_target passwd -l "$RETROBOX_USER" >/dev/null 2>&1 || true
        return
    fi

    local p1 p2
    while :; do
        read -r -s -p "Set a password for '$RETROBOX_USER' (SSH + sudo): " p1 < /dev/tty
        printf '\n' >&2
        read -r -s -p "Confirm password: " p2 < /dev/tty
        printf '\n' >&2
        if [ -z "$p1" ]; then warn "Empty password not allowed."; continue; fi
        if [ "$p1" != "$p2" ]; then warn "Passwords did not match."; continue; fi
        break
    done
    printf '%s:%s\n' "$RETROBOX_USER" "$p1" | in_target chpasswd
    unset p1 p2
}
