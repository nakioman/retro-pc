#!/usr/bin/env bash
# Detect appliance hardware, write the RetroBox daemon/hardware config into the
# target, and record an install report. Detection never aborts the install: an
# absent device yields a clearly-marked placeholder. See hardware-detect.md.
#
# Consumes globals: TARGET_DISK, ROOT_UUID, DATA_UUID, RETROBOX_STATUS, BOX86_STATUS.

detect_cdrom() {
    local d
    for d in /dev/disk/by-id/*cd* /dev/disk/by-id/*CD* \
             /dev/disk/by-id/*dvd* /dev/disk/by-id/*DVD*; do
        [ -e "$d" ] || continue
        CDROM_DEVICE="$d"; CDROM_STATUS="DETECTED"; return
    done
    if [ -b /dev/sr0 ];   then CDROM_DEVICE="/dev/sr0";   CDROM_STATUS="DETECTED"; return; fi
    if [ -e /dev/cdrom ]; then CDROM_DEVICE="/dev/cdrom"; CDROM_STATUS="DETECTED"; return; fi
    CDROM_DEVICE="/dev/sr0"; CDROM_STATUS="NOT_DETECTED"
}

# Configure only the first active optical slot in each installed profile. The
# payload profiles stay portable; this is the point where the host-specific
# ioctl path is written to their copies under /data.
configure_cdrom_passthrough() {
    local config config_dir slot tmp image_path

    [ "$CDROM_STATUS" = "DETECTED" ] || return 0
    [ -d "$TARGET_MNT/data/vms" ] || return 0
    image_path="ioctl://$CDROM_DEVICE"

    while IFS= read -r -d '' config; do
        if [ ! -r "$config" ] || [ ! -w "$config" ]; then
            warn "Skipping unreadable CD-ROM profile config: $config"
            continue
        fi

        # Parameters are "enabled, bus". Pick the lowest numbered enabled
        # slot that has a real bus; a profile with no optical slot is valid.
        slot="$(awk '
            /^[[:space:]]*cdrom_[0-9][0-9]_parameters[[:space:]]*=/ {
                key = $1
                sub(/^cdrom_/, "", key)
                sub(/_parameters$/, "", key)
                value = $0
                sub(/^[^=]*=[[:space:]]*/, "", value)
                count = split(value, fields, ",")
                enabled = fields[1]
                bus = fields[2]
                gsub(/^[[:space:]]+|[[:space:]]+$/, "", enabled)
                gsub(/^[[:space:]]+|[[:space:]]+$/, "", bus)
                if (count < 2 || enabled !~ /^[0-9]+$/ || bus == "") {
                    malformed = 1
                    next
                }
                seen[key]++
                if (seen[key] > 1) {
                    malformed = 1
                    next
                }
                if (enabled != "0" && tolower(bus) != "none" &&
                    (!found || key + 0 < selected + 0)) {
                    selected = key
                    found = 1
                }
            }
            END {
                if (malformed) exit 2
                if (found) print selected
            }
        ' "$config")"
        case $? in
            0) ;;
            *)
                warn "Skipping malformed CD-ROM profile config: $config"
                continue
                ;;
        esac
        [ -n "$slot" ] || continue

        config_dir="${config%/*}"
        if [ ! -w "$config_dir" ]; then
            warn "Skipping unwritable CD-ROM profile directory: $config_dir"
            continue
        fi
        if ! tmp="$(mktemp "$config_dir/.86box.cfg.cdrom.XXXXXX")"; then
            warn "Could not create temporary CD-ROM profile config: $config"
            continue
        fi
        if ! cp -p "$config" "$tmp"; then
            warn "Could not preserve CD-ROM profile config metadata: $config"
            rm -f "$tmp"
            continue
        fi
        if ! awk -v slot="$slot" -v image_path="$image_path" '
            BEGIN { key = "cdrom_" slot "_image_path" }
            $0 ~ "^[[:space:]]*" key "[[:space:]]*=" {
                if (!written++) print key " = " image_path
                next
            }
            { print }
            END { if (!written) print key " = " image_path }
        ' "$config" > "$tmp"; then
            warn "Could not update CD-ROM profile config: $config"
            rm -f "$tmp"
            continue
        fi
        if cmp -s "$config" "$tmp"; then
            rm -f "$tmp"
            continue
        fi
        if ! mv "$tmp" "$config"; then
            warn "Could not install CD-ROM passthrough config: $config"
            rm -f "$tmp"
            continue
        fi
        log "Configured CD-ROM passthrough in $config (cdrom_$slot)"
    done < <(find "$TARGET_MNT/data/vms" -type f -name 86box.cfg -print0)
}

detect_serial() {
    SERIAL_BAUD="${RETROPC_SERIAL_BAUD:-115200}"
    local d
    for d in /dev/serial/by-id/*; do
        [ -e "$d" ] || continue
        SERIAL_DEVICE="$d"; SERIAL_STATUS="DETECTED"; return
    done
    for d in /dev/ttyUSB* /dev/ttyACM*; do
        [ -e "$d" ] || continue
        SERIAL_DEVICE="$d"; SERIAL_STATUS="DETECTED_FALLBACK"; return
    done
    SERIAL_DEVICE="/dev/ttyUSB0"; SERIAL_STATUS="NOT_DETECTED"
}

# Select a connected HDMI PCM without relying on the card's numeric index. ELD
# filenames identify an HDA codec pin, not an ALSA PCM device, so the PCM number
# must be read from the matching /proc/asound pcm id file.
detect_hdmi_pcm() {
    local eld card_dir device present card_id pcm_id

    for eld in /proc/asound/card*/eld#*; do
        [ -f "$eld" ] || continue
        present="$(awk '$1 == "monitor_present" { print $2; exit }' "$eld")"
        [ "$present" = "1" ] || continue

        card_dir="${eld%/eld#*}"
        card_id="$(cat "$card_dir/id" 2>/dev/null || true)"
        [ -n "$card_id" ] || continue

        for pcm_id in "$card_dir"/pcm*p/info; do
            [ -f "$pcm_id" ] || continue
            grep -qi '^id:.*HDMI' "$pcm_id" || continue
            device="${pcm_id##*/pcm}"
            device="${device%%p/info}"
            printf '%s|%s\n' "$card_id" "$device"
            return 0
        done
    done

    # Some kernels expose HDMI PCM metadata but no ELD file. Use the first
    # playback PCM explicitly named HDMI as a fallback.
    for pcm_id in /proc/asound/card*/pcm*p/info; do
        [ -f "$pcm_id" ] || continue
        grep -qi '^id:.*HDMI' "$pcm_id" || continue
        card_dir="${pcm_id%/pcm*}"
        device="${pcm_id##*/pcm}"
        device="${device%%p/info}"
        card_id="$(cat "$card_dir/id" 2>/dev/null || true)"
        [ -n "$card_id" ] || continue
        printf '%s|%s\n' "$card_id" "$device"
        return 0
    done

    # During installation an HDMI sink may not expose ELD data until the
    # display is fully initialized. Keep audio usable by selecting the first
    # playback PCM; this also covers analog-only machines.
    for pcm_id in /proc/asound/card*/pcm*p/info; do
        [ -f "$pcm_id" ] || continue
        card_dir="${pcm_id%/pcm*}"
        device="${pcm_id##*/pcm}"
        device="${device%%p/info}"
        card_id="$(cat "$card_dir/id" 2>/dev/null || true)"
        [ -n "$card_id" ] || continue
        printf '%s|%s\n' "$card_id" "$device"
        return 0
    done

    return 1
}

configure_audio_output() {
    local detected card_id device home asoundrc passwd_entry _login _passwd _uid _gid _gecos _shell

    if ! detected="$(detect_hdmi_pcm)"; then
        HDMI_AUDIO_DEVICE="unknown"
        HDMI_AUDIO_STATUS="NOT_DETECTED"
        warn "No connected HDMI audio PCM detected; keeping ALSA defaults"
        return 0
    fi

    IFS='|' read -r card_id device <<< "$detected"
    case "$card_id" in
        ''|*[!A-Za-z0-9_-]*)
            HDMI_AUDIO_DEVICE="unknown"
            HDMI_AUDIO_STATUS="INVALID_CARD_ID"
            warn "Detected HDMI audio card has an unsafe ALSA id: $card_id"
            return 0
            ;;
    esac

    passwd_entry="$(in_target getent passwd "$RETROBOX_USER" || true)"
    IFS=: read -r _login _passwd _uid _gid _gecos home _shell <<< "$passwd_entry"
    if [ -z "$home" ]; then
        HDMI_AUDIO_DEVICE="unknown"
        HDMI_AUDIO_STATUS="NO_USER_HOME"
        warn "Could not determine $RETROBOX_USER home; skipping ALSA config"
        return 0
    fi

    asoundrc="$TARGET_MNT$home/.asoundrc"
    mkdir -p "$(dirname "$asoundrc")"
    cat > "$asoundrc" <<EOF
# RetroBox appliance ALSA configuration — generated by install-retropc.sh.
# Selected from the connected HDMI ELD at install time; the card id avoids
# depending on the kernel's numeric card ordering.
pcm.!default {
    type plug
    slave.pcm "hw:CARD=$card_id,DEV=$device"
}
ctl.!default {
    type hw
    card $card_id
}
EOF
    in_target chown "$RETROBOX_USER:$RETROBOX_GROUP" "$home/.asoundrc"
    HDMI_AUDIO_DEVICE="plughw:CARD=$card_id,DEV=$device"
    HDMI_AUDIO_STATUS="DETECTED"
    log "Default ALSA output: $HDMI_AUDIO_DEVICE"
}

write_hardware_config() {
    mkdir -p "$TARGET_MNT/etc/retrobox"

    # Consumed by retrobox-daemon.service.
    cat > "$TARGET_MNT/etc/retrobox/daemon.env" <<EOF
# RetroBox daemon environment — generated by install-retropc.sh.
# ESP8266/NodeMCU serial controller ($SERIAL_STATUS). Edit if the device path
# changes; prefer a stable /dev/serial/by-id/* path.
SERIAL_DEVICE=$SERIAL_DEVICE
SERIAL_BAUD=$SERIAL_BAUD
FLOPPY_CONTROL_SOCKET=/run/retrobox/86box-floppy.sock
EOF

    # Consumed by the 86Box / RetroBox VM layer for physical CD-ROM passthrough.
    cat > "$TARGET_MNT/etc/retrobox/hardware.env" <<EOF
# Physical hardware paths — generated by install-retropc.sh.
# 86Box Pentium profile CD-ROM passthrough host device ($CDROM_STATUS).
CDROM_DEVICE=$CDROM_DEVICE
EOF
}

write_install_report() {
    local by_id="" link target report="$TARGET_MNT/data/retrobox/install-report.txt"
    # Find a stable by-id path that resolves to the target disk (avoid ls|awk,
    # which trips pipefail when the directory is absent).
    target="$(readlink -f "$TARGET_DISK" 2>/dev/null || printf '%s' "$TARGET_DISK")"
    if [ -d /dev/disk/by-id ]; then
        for link in /dev/disk/by-id/*; do
            [ -e "$link" ] || continue
            if [ "$(readlink -f "$link")" = "$target" ]; then by_id="$link"; break; fi
        done
    fi
    mkdir -p "$TARGET_MNT/data/retrobox"
    cat > "$report" <<EOF
generated=install-retropc.sh
target.disk=$TARGET_DISK
target.disk.by_id=${by_id:-unknown}
target.root.uuid=$ROOT_UUID
target.data.uuid=$DATA_UUID
cdrom.device=$CDROM_DEVICE
cdrom.status=$CDROM_STATUS
serial.device=$SERIAL_DEVICE
serial.baud=$SERIAL_BAUD
serial.status=$SERIAL_STATUS
retrobox.binary=$RETROBOX_STATUS
box86.appimage=$BOX86_STATUS
EOF
}

detect_and_record_hardware() {
    log "Detecting CD-ROM and ESP8266 serial devices"
    detect_cdrom
    detect_serial
    configure_cdrom_passthrough
    log "CD-ROM: $CDROM_DEVICE ($CDROM_STATUS)"
    log "Serial: $SERIAL_DEVICE @ ${SERIAL_BAUD} ($SERIAL_STATUS)"
    write_hardware_config
    write_install_report
    ok "Hardware config + install report written"
}
