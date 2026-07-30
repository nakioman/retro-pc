#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: install-retropc.sh [--target-root PATH] [--config PATH] [--maintenance]

When invoked on the installed appliance, the implicit target root is /. Pass
--target-root only for an already-mounted target system or a controlled test
target. The image installer uses install-image.sh for first-time deployment;
this script remains the maintenance/provisioning utility.

Use --maintenance after booting a read-only appliance to remount the selected
root read-write. It does not provision or modify appliance files.
EOF
}

die() {
    printf 'install-retropc: %s\n' "$*" >&2
    exit 1
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
default_payload_root=$script_dir
if [[ -d "$script_dir/../lib/retrobox-installer" ]]; then
    default_payload_root=$(cd -- "$script_dir/../lib/retrobox-installer" && pwd -P)
fi
payload_root=${RETROBOX_PAYLOAD_ROOT:-$default_payload_root}
target_root=''
config_file=''
maintenance_mode=false

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
        --maintenance)
            maintenance_mode=true
            shift
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

target_path() {
    local absolute_path=$1
    printf '%s%s\n' "${target_root%/}" "$absolute_path"
}

root_mount_options=$(findmnt -no OPTIONS -T "$(target_path /)" 2>/dev/null || true)
if [[ ",$root_mount_options," == *,ro,* ]]; then
    if "$maintenance_mode"; then
        mount -o remount,rw "$target_root" \
            || die "could not remount $target_root read-write; boot with systemd.unit=multi-user.target and retry --maintenance"
        printf 'install-retropc: remounted %s read-write for maintenance\n' "$target_root"
        exit 0
    fi
    die "root filesystem is read-only; run $0 --maintenance before provisioning"
fi

if "$maintenance_mode"; then
    printf 'install-retropc: %s is already read-write; no remount was needed\n' "$target_root"
    exit 0
fi

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
    if [[ -e "$destination" && "$source" -ef "$destination" ]]; then
        chmod "$mode" "$destination"
        return
    fi
    install -m "$mode" "$source" "$destination"
}

seed_payload_tree() {
    local source_root=$1 destination_root=$2 source_path relative_path destination_path
    while IFS= read -r -d '' source_path; do
        relative_path=${source_path#"$source_root"/}
        destination_path="$destination_root/$relative_path"
        if [[ -d "$source_path" ]]; then
            install -d -m 0750 "$destination_path"
        elif [[ ! -e "$destination_path" ]]; then
            install -d -m 0750 "$(dirname -- "$destination_path")"
            cp "$source_path" "$destination_path"
        fi
    done < <(find "$source_root" -mindepth 1 -print0)
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

write_bind_entry() {
    local source_path=$1 mount_point=$2 fstab temporary_file
    fstab=$(target_path /etc/fstab)
    temporary_file=$(mktemp "${fstab}.XXXXXX")
    awk -v mount_point="$mount_point" '$2 != mount_point { print }' "$fstab" > "$temporary_file"
    printf '%s %s none bind 0 0\n' "$source_path" "$mount_point" >> "$temporary_file"
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

appimage_source="$payload_root/$RETROBOX_86BOX_ASSET"
[[ -r "$appimage_source" ]] || die "pinned 86Box AppImage payload is missing: $RETROBOX_86BOX_ASSET"

persistent_payload_root=$(target_path /usr/local/lib/retrobox-installer)
copy_payload_file "$payload_root/install-retropc.conf" "$persistent_payload_root/install-retropc.conf" 0644
copy_payload_file "$payload_root/read-install-retropc-conf.sh" "$persistent_payload_root/read-install-retropc-conf.sh" 0644
copy_payload_file "$appimage_source" "$persistent_payload_root/$RETROBOX_86BOX_ASSET" 0755
copy_payload_file "$payload_root/systemd/retrobox-boot.service" "$persistent_payload_root/systemd/retrobox-boot.service" 0644
copy_payload_file "$payload_root/samba/smb.conf" "$persistent_payload_root/samba/smb.conf" 0644
copy_payload_file "$payload_root/read-only-root.conf" "$persistent_payload_root/read-only-root.conf" 0644

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
[[ -d "$payload_root/profiles/386sx16" ]] || die 'profile payload is missing: 386sx16'
seed_payload_tree "$payload_root/profiles/386sx16" "$persistent_payload_root/profiles/386sx16"
rm -rf "$install_root/profiles/386sx16"
cp -R "$payload_root/profiles/386sx16" "$install_root/profiles/386sx16"
upsert_profile_key "$install_root/profiles/386sx16/86box.cfg" floppy_control_socket_enabled 0

[[ -d "$payload_root/profiles/pentium100" ]] || die 'profile payload is missing: pentium100'
seed_payload_tree "$payload_root/profiles/pentium100" "$persistent_payload_root/profiles/pentium100"
pentium_vm_root="$data_root/vms/pentium100"
install -d -m 0750 "$pentium_vm_root"
seed_payload_tree "$payload_root/profiles/pentium100" "$pentium_vm_root"
rm -rf "$install_root/profiles/pentium100"
chown -R retrobox:retrobox "$pentium_vm_root"

pentium_profile="$pentium_vm_root/86box.cfg"
upsert_profile_key "$pentium_profile" floppy_control_socket_enabled 0
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
copy_payload_file "${BASH_SOURCE[0]}" "$(target_path /usr/local/sbin/install-retropc.sh)" 0755
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
install -d -m 0755 \
    "$(target_path /var/log)" \
    "$(target_path /var/lib/samba)" \
    "$(target_path /var/lib/NetworkManager)" \
    "$data_root/system-state/samba" \
    "$data_root/system-state/network-manager"
write_tmpfs_entry /tmp mode=1777,nosuid,nodev
write_tmpfs_entry /var/log mode=0755,nosuid,nodev
write_bind_entry "$RETROBOX_DATA_ROOT/system-state/samba" /var/lib/samba
write_bind_entry "$RETROBOX_DATA_ROOT/system-state/network-manager" /var/lib/NetworkManager

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
