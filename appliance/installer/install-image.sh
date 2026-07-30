#!/usr/bin/env bash
set -euo pipefail

die() {
    printf 'retrobox installer: %s\n' "$*" >&2
    exit 1
}

[[ $(id -u) -eq 0 ]] || die 'must run as root'

installer_media=''
for candidate in /run/live/medium/retropc /cdrom/retropc; do
    if [[ -d "$candidate" ]]; then
        installer_media=$candidate
        break
    fi
done
[[ -n "$installer_media" ]] || die 'could not find the installer media'
image_file="$installer_media/retrobox-system.raw.zst"
[[ -r "$image_file" ]] || die "system image is missing: $image_file"

partition_path() {
    local disk=$1 number=$2
    if [[ "$disk" =~ (nvme|mmcblk) ]]; then
        printf '%sp%s\n' "$disk" "$number"
    else
        printf '%s%s\n' "$disk" "$number"
    fi
}

installer_source=$(findmnt -no SOURCE /run/live/medium 2>/dev/null || true)
installer_parent=$(lsblk -no PKNAME "$installer_source" 2>/dev/null | tail -n 1 || true)
installer_disk=${installer_parent:+/dev/$installer_parent}
disk_list=()
while read -r disk size model type; do
    [[ "$type" == disk ]] || continue
    [[ -b "$disk" ]] || continue
    [[ -z "$installer_disk" || "$disk" != "$installer_disk" ]] || continue
    disk_list+=("$disk" "$size ${model:-unnamed}")
done < <(lsblk -dpno NAME,SIZE,MODEL,TYPE)
((${#disk_list[@]} >= 2)) || die 'no target disks were found'

if [[ -n "${RETROBOX_TEST_TARGET_DISK:-}" ]]; then
    target_disk=$RETROBOX_TEST_TARGET_DISK
else
    if command -v dialog > /dev/null 2>&1; then
        exec 3>&1
        target_disk=$(dialog --clear --stdout --title 'Retro PC disk selection' \
            --menu 'Select the disk to erase and install Retro PC onto:' 15 78 6 "${disk_list[@]}" 2>&1 1>&3)
        dialog_status=$?
        exec 3>&-
        [[ $dialog_status -eq 0 ]] || exit 1
    else
        printf 'Available disks:\n' >&2
        lsblk -dpno NAME,SIZE,MODEL >&2
        read -r -p 'Disk to erase: ' target_disk
    fi
fi
[[ -b "$target_disk" ]] || die "selected disk does not exist: $target_disk"
target_disk=$(readlink -f "$target_disk")
target_size=$(blockdev --getsize64 "$target_disk")
((target_size >= 8 * 1024 * 1024 * 1024)) || die 'selected disk must be at least 8 GiB'

if [[ -z "${RETROBOX_TEST_CONFIRM:-}" ]]; then
    if command -v dialog > /dev/null 2>&1; then
        dialog --title 'DANGER: destructive operation' --yesno \
            "EVERYTHING on $target_disk will be erased. Continue?" 8 68 || exit 1
    else
        read -r -p "Type ERASE to erase $target_disk: " confirmation
        [[ "$confirmation" == ERASE ]] || exit 1
    fi
fi

password=${RETROBOX_TEST_PASSWORD:-}
if [[ -z "$password" ]]; then
    if command -v dialog > /dev/null 2>&1; then
        exec 3>&1
        password=$(dialog --clear --stdout --passwordbox 'Password for user retrobox:' 8 60 2>&1 1>&3)
        password_confirm=$(dialog --clear --stdout --passwordbox 'Repeat the password:' 8 60 2>&1 1>&3)
        exec 3>&-
        [[ "$password" == "$password_confirm" ]] || die 'passwords do not match'
    else
        read -r -s -p 'Password for user retrobox: ' password; printf '\n' >&2
    fi
fi
[[ -n "$password" ]] || die 'password cannot be empty'

hostname=${RETROBOX_TEST_HOSTNAME:-}
if [[ -z "$hostname" ]]; then
    if command -v dialog > /dev/null 2>&1; then
        exec 3>&1
        hostname=$(dialog --clear --stdout --inputbox 'Hostname:' 8 60 retrobox 2>&1 1>&3)
        exec 3>&-
    else
        read -r -p 'Hostname [retrobox]: ' hostname
        hostname=${hostname:-retrobox}
    fi
fi
[[ "$hostname" =~ ^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?$ ]] \
    || die 'hostname contains invalid characters'

timezone=${RETROBOX_TEST_TIMEZONE:-}
if [[ -z "$timezone" ]]; then
    if command -v dialog > /dev/null 2>&1; then
        exec 3>&1
        timezone=$(dialog --clear --stdout --inputbox 'Timezone:' 8 60 Etc/UTC 2>&1 1>&3)
        exec 3>&-
    else
        read -r -p 'Timezone [Etc/UTC]: ' timezone
        timezone=${timezone:-Etc/UTC}
    fi
fi
[[ -f "/usr/share/zoneinfo/$timezone" ]] || die "unknown timezone: $timezone"

while read -r mounted_partition; do
    [[ "$mounted_partition" == "$target_disk" ]] && continue
    umount "$mounted_partition" 2>/dev/null || true
done < <(lsblk -lnpo NAME "$target_disk")
wipefs -a "$target_disk"
zstd --decompress --stdout "$image_file" | dd of="$target_disk" bs=16M conv=fsync status=progress
sync
partprobe "$target_disk" 2>/dev/null || true
root_partition=$(partition_path "$target_disk" 1)
data_partition=$(partition_path "$target_disk" 2)
for _ in {1..20}; do
    if [[ -b "$root_partition" && -b "$data_partition" ]]; then break; fi
    sleep 0.25
done
[[ -b "$root_partition" && -b "$data_partition" ]] || die 'installed image partitions did not appear'

if command -v growpart > /dev/null 2>&1; then
    grow_status=0
    growpart "$target_disk" 2 || grow_status=$?
    [[ "$grow_status" == 0 || "$grow_status" == 1 ]] \
        || die "could not expand the /data partition (growpart exit $grow_status)"
else
    die 'growpart is required by the installer environment'
fi
resize2fs "$data_partition"

mount_root=$(mktemp -d)
cleanup() {
    set +e
    umount -R "$mount_root" 2>/dev/null || true
    rmdir "$mount_root" 2>/dev/null || true
}
trap cleanup EXIT
mount "$root_partition" "$mount_root"
mount_data="$mount_root/data"
mkdir -p "$mount_data"
mount "$data_partition" "$mount_data"
mount --bind "$mount_data/system-state/samba" "$mount_root/var/lib/samba"

printf '%s\n' "$hostname" > "$mount_root/etc/hostname"
cat > "$mount_root/etc/hosts" <<EOF
127.0.0.1 localhost
127.0.1.1 $hostname
::1 localhost ip6-localhost ip6-loopback
EOF
ln -sf "/usr/share/zoneinfo/$timezone" "$mount_root/etc/localtime"
printf '%s\n' "$timezone" > "$mount_root/etc/timezone"
printf 'retrobox:%s\n' "$password" | chroot "$mount_root" chpasswd
chroot "$mount_root" passwd --lock root
printf '%s\n%s\n' "$password" "$password" | chroot "$mount_root" smbpasswd -a -s retrobox

root_uuid=$(blkid -s UUID -o value "$root_partition")
data_uuid=$(blkid -s UUID -o value "$data_partition")
cat > "$mount_root/etc/fstab" <<EOF
UUID=$root_uuid / ext4 ro,errors=remount-ro 0 1
UUID=$data_uuid /data ext4 rw,nosuid,nodev 0 2
/data/swapfile none swap sw 0 0
EOF
install -d -m 0750 "$mount_root/etc/retrobox-appliance"
cat > "$mount_root/etc/retrobox-appliance/install-report.txt" <<EOF
INSTALLER_LABEL=preinstalled-image
TARGET_DISK=$target_disk
HOSTNAME=$hostname
TIMEZONE=$timezone
USER=retrobox
ROOT_ACCOUNT=locked
DATA_EXPANDED=yes
EOF

sync
printf 'Installation complete. Remove the USB and reboot.\n'
