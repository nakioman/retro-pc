#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: install-retropc.sh [--target-root PATH] [--config PATH]

When invoked by Debian Installer from /cdrom/retropc/install-retropc.sh, the
implicit target root is /target. When invoked on the installed appliance, the
implicit target root is /. Pass --target-root only for an already-mounted
target system or a controlled test target.
EOF
}

die() {
    printf 'install-retropc: %s\n' "$*" >&2
    exit 1
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
payload_root=${RETROBOX_PAYLOAD_ROOT:-$script_dir}
target_root=''
config_file=''

while (($#)); do
    case "$1" in
        --target-root)
            (($# >= 2)) || die '--target-root requires a path'
            target_root=$2
            shift 2
            ;;
        --config)
            (($# >= 2)) || die '--config requires a path'
            config_file=$2
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            die "unknown argument: $1"
            ;;
    esac
done

if [[ -z "$target_root" ]]; then
    if [[ "$script_dir" == /cdrom/retropc && -d /target ]]; then
        target_root=/target
    else
        target_root=/
    fi
fi

[[ "$target_root" == /* ]] || die '--target-root must be an absolute path'
[[ -d "$target_root" ]] || die "target root does not exist: $target_root"
target_root=$(cd -- "$target_root" && pwd -P)
[[ -d "$target_root/etc" ]] || die "target root is not an installed system: $target_root"

[[ $(id -u) -eq 0 ]] || die 'must be run as root'

config_file=${config_file:-"$payload_root/install-retropc.conf"}
parser_file="$payload_root/read-install-retropc-conf.sh"
[[ -r "$parser_file" ]] || die "configuration parser is not readable: $parser_file"
# shellcheck source=read-install-retropc-conf.sh
source "$parser_file"
load_retrobox_config "$config_file"

[[ "${RETROBOX_86BOX_REPOSITORY:-}" == */* ]] || die '86Box repository metadata is invalid'
[[ "${RETROBOX_86BOX_VERSION:-}" == v* ]] || die '86Box version metadata is invalid'
[[ "${RETROBOX_86BOX_ASSET:-}" == *x86_64*.AppImage ]] || die '86Box asset metadata is invalid'
[[ "${RETROBOX_CONFIG_ROOT:-}" == /* ]] || die 'RetroBox configuration root must be absolute'
[[ "${RETROBOX_DATA_ROOT:-}" == /* ]] || die 'RetroBox data root must be absolute'
case "/${RETROBOX_CONFIG_ROOT#/}/" in
    *'/../'*|*'/./'*) die 'RetroBox configuration root must not contain relative path segments' ;;
esac
case "/${RETROBOX_DATA_ROOT#/}/" in
    *'/../'*|*'/./'*) die 'RetroBox data root must not contain relative path segments' ;;
esac

target_path() {
    local absolute_path=$1
    printf '%s%s\n' "${target_root%/}" "$absolute_path"
}

target_command() {
    if [[ "$target_root" == / ]]; then
        "$@"
    else
        "$1" --root "$target_root" "${@:2}"
    fi
}

ensure_system_group() {
    local group=$1
    if ! grep -q "^${group}:" "$(target_path /etc/group)" 2>/dev/null; then
        target_command groupadd --system "$group"
    fi
}

ensure_system_user() {
    local user=$1 group=$2
    if ! grep -q "^${user}:" "$(target_path /etc/passwd)" 2>/dev/null; then
        target_command useradd --system --gid "$group" --home-dir /nonexistent --shell /usr/sbin/nologin --no-create-home "$user"
    fi
}

copy_payload_file() {
    local source=$1 destination=$2 mode=$3
    [[ -r "$source" ]] || die "payload file is missing: $source"
    install -d -m 0755 "$(dirname -- "$destination")"
    install -m "$mode" "$source" "$destination"
}

upsert_profile_key() {
    local profile=$1 key=$2 value=$3 temporary_file
    temporary_file=$(mktemp "${profile}.XXXXXX")
    awk -v key="$key" -v value="$value" '
        $0 ~ "^" key "[[:space:]]*=" {
            if (!replaced) {
                print key " = " value
                replaced = 1
            }
            next
        }
        { print }
        END {
            if (!replaced) {
                print key " = " value
            }
        }
    ' "$profile" > "$temporary_file"
    mv "$temporary_file" "$profile"
}

find_existing_fstab_uuid() {
    local mount_point=$1
    awk -v mount_point="$mount_point" '$1 ~ /^UUID=/ && $2 == mount_point { sub(/^UUID=/, "", $1); print $1; exit }' "$(target_path /etc/fstab)" 2>/dev/null || true
}

find_mount_uuid() {
    local mount_point=$1 configured_uuid=$2 mounted_path
    if [[ -n "$configured_uuid" ]]; then
        printf '%s\n' "$configured_uuid"
        return
    fi

    configured_uuid=$(find_existing_fstab_uuid "$mount_point")
    if [[ -n "$configured_uuid" ]]; then
        printf '%s\n' "$configured_uuid"
        return
    fi

    mounted_path=$(target_path "$mount_point")
    findmnt -no UUID -T "$mounted_path" 2>/dev/null || true
}

write_fstab_entry() {
    local uuid=$1 mount_point=$2 filesystem_type=$3 options=$4 pass_number=$5 fstab temporary_file
    fstab=$(target_path /etc/fstab)
    temporary_file=$(mktemp "${fstab}.XXXXXX")
    awk -v mount_point="$mount_point" '$2 != mount_point { print }' "$fstab" > "$temporary_file"
    printf 'UUID=%s %s %s %s 0 %s\n' "$uuid" "$mount_point" "$filesystem_type" "$options" "$pass_number" >> "$temporary_file"
    mv "$temporary_file" "$fstab"
}

write_tmpfs_entry() {
    local mount_point=$1 options=$2 fstab temporary_file
    fstab=$(target_path /etc/fstab)
    temporary_file=$(mktemp "${fstab}.XXXXXX")
    awk -v mount_point="$mount_point" '$2 != mount_point { print }' "$fstab" > "$temporary_file"
    printf 'tmpfs %s tmpfs %s 0 0\n' "$mount_point" "$options" >> "$temporary_file"
    mv "$temporary_file" "$fstab"
}

detect_cdrom_device() {
    local candidate device_root target_candidate
    device_root=$target_root
    if [[ "$target_root" == /target && -d /dev ]]; then
        # d-i exposes hardware devices from its own /dev; the selected path is
        # still written as an installed-system path without the /target prefix.
        device_root=/
    fi

    shopt -s nullglob
    for candidate in "${device_root%/}/dev/disk/by-id"/*; do
        case "${candidate##*/}" in
            *[Cc][Dd]*|*[Dd][Vv][Dd]*)
                target_candidate=${candidate#"${device_root%/}"}
                printf '%s\n' "$target_candidate"
                return
                ;;
        esac
    done
    shopt -u nullglob

    if [[ -e "${device_root%/}/dev/sr0" || -b "${device_root%/}/dev/sr0" ]]; then
        printf '/dev/sr0\n'
    fi
}

appimage_source="$payload_root/86Box.AppImage"
if [[ ! -r "$appimage_source" ]]; then
    appimage_source="$payload_root/$RETROBOX_86BOX_ASSET"
fi
[[ -r "$appimage_source" ]] || die "pinned 86Box AppImage payload is missing: $RETROBOX_86BOX_ASSET"

ensure_system_group retrobox
ensure_system_group retrobox-samba
ensure_system_user retrobox retrobox

config_root=$(target_path "$RETROBOX_CONFIG_ROOT")
data_root=$(target_path "$RETROBOX_DATA_ROOT")
install -d -m 0750 "$config_root" "$data_root/vms" "$data_root/floppies/cataloged" "$data_root/snapshots"
install -d -m 0770 "$data_root/floppies/scratch"
chown -R retrobox:retrobox "$config_root" "$data_root/vms" "$data_root/floppies/cataloged" "$data_root/snapshots"
chown root:retrobox-samba "$data_root/floppies/scratch"

install_root=$(target_path /opt/retrobox)
install -d -m 0755 "$install_root/profiles"
install -m 0755 "$appimage_source" "$install_root/86Box.AppImage"
for profile in pentium100 386sx16; do
    [[ -d "$payload_root/profiles/$profile" ]] || die "profile payload is missing: $profile"
    rm -rf "$install_root/profiles/$profile"
    cp -R "$payload_root/profiles/$profile" "$install_root/profiles/$profile"
    upsert_profile_key "$install_root/profiles/$profile/86box.cfg" floppy_control_socket_enabled 0
done

pentium_profile="$install_root/profiles/pentium100/86box.cfg"
cdrom_device=$(detect_cdrom_device)
if [[ -n "$cdrom_device" ]]; then
    upsert_profile_key "$pentium_profile" cdrom_01_host_drive "$cdrom_device"
    cdrom_state=present
else
    upsert_profile_key "$pentium_profile" cdrom_01_host_drive ''
    cdrom_state=missing
fi

copy_payload_file "$payload_root/systemd/retrobox-boot.service" "$(target_path /etc/systemd/system/retrobox-boot.service)" 0644
copy_payload_file "$payload_root/samba/smb.conf" "$(target_path /etc/samba/smb.conf)" 0644
copy_payload_file "$payload_root/read-only-root.conf" "$(target_path /etc/retrobox-appliance/read-only-root.conf)" 0644
touch "$(target_path /etc/fstab)"

if [[ "$target_root" == / ]]; then
    systemctl daemon-reload
    systemctl enable retrobox-boot.service
else
    systemctl --root="$target_root" enable retrobox-boot.service
fi

root_uuid=$(find_mount_uuid / "${RETROBOX_ROOT_UUID:-}")
data_uuid=$(find_mount_uuid /data "${RETROBOX_DATA_UUID:-}")
[[ -n "$root_uuid" ]] || die 'could not determine the root filesystem UUID'
[[ -n "$data_uuid" ]] || die 'could not determine the /data filesystem UUID'

# The boot service and all target service files are installed before this final
# root read-only switch is written.
write_fstab_entry "$root_uuid" / ext4 ro,errors=remount-ro 1
write_fstab_entry "$data_uuid" /data ext4 rw,nosuid,nodev 2
install -d -m 1777 "$(target_path /tmp)"
install -d -m 0755 "$(target_path /var/log)" "$(target_path /var/lib/retrobox)"
write_tmpfs_entry /tmp mode=1777,nosuid,nodev
write_tmpfs_entry /var/log mode=0755,nosuid,nodev
write_tmpfs_entry /var/lib/retrobox mode=0755,nosuid,nodev

report_file=$(target_path /etc/retrobox-appliance/install-report.txt)
cat > "$report_file" <<EOF
INSTALLER_LABEL=$RETROBOX_INSTALLER_LABEL
86BOX_REPOSITORY=$RETROBOX_86BOX_REPOSITORY
86BOX_VERSION=$RETROBOX_86BOX_VERSION
86BOX_ASSET=$RETROBOX_86BOX_ASSET
CDROM_STATE=$cdrom_state
CDROM_DEVICE=${cdrom_device:-none}
ROOT_UUID=$root_uuid
DATA_UUID=$data_uuid
EOF

printf 'install-retropc: provisioned target root %s\n' "$target_root"
