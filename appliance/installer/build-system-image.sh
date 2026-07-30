#!/usr/bin/env bash
set -euo pipefail

die() { printf 'build-system-image: %s\n' "$*" >&2; exit 1; }
usage() { printf '%s\n' 'Usage: build-system-image.sh --output PATH --payload PATH --config PATH'; }

output_file=''
payload_dir=''
config_file=''
while (($#)); do
    case "$1" in
        --output) output_file=${2:?}; shift 2 ;;
        --payload) payload_dir=${2:?}; shift 2 ;;
        --config) config_file=${2:?}; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) die "unknown argument: $1" ;;
    esac
done
[[ -n "$output_file" && -n "$payload_dir" && -n "$config_file" ]] || die 'all arguments are required'
[[ $(id -u) -eq 0 ]] || die 'must run as root'

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
packages_file="$repo_root/appliance/debian/packages.txt"
source "$script_dir/read-install-retropc-conf.sh"
load_retrobox_config "$config_file"

for command_name in debootstrap sfdisk losetup mkfs.ext4 mount umount chroot blkid mkswap fallocate grub-install; do
    command -v "$command_name" > /dev/null 2>&1 || die "required command '$command_name' was not found"
done

suite=${RETROBOX_DEBIAN_SUITE:-}
image_size_gib=${RETROBOX_IMAGE_SIZE_GIB:-}
root_size_gib=${RETROBOX_ROOT_SIZE_GIB:-}
swap_size_gib=${RETROBOX_SWAPFILE_SIZE_GIB:-}
[[ "$suite" == trixie ]] || die 'only the Debian trixie suite is supported'
[[ "$image_size_gib" =~ ^[0-9]+$ && "$root_size_gib" =~ ^[0-9]+$ && "$swap_size_gib" =~ ^[0-9]+$ ]] \
    || die 'image, root, and swap sizes must be integers'
((image_size_gib >= 8)) || die 'image size must be at least 8 GiB'
((root_size_gib >= 2 && root_size_gib < image_size_gib)) || die 'root size is outside image bounds'
((swap_size_gib >= 1 && swap_size_gib < image_size_gib - root_size_gib)) \
    || die 'swap size leaves no usable initial /data space'
[[ -r "$packages_file" && -d "$payload_dir" ]] || die 'package manifest or payload is missing'

mkdir -p "$(dirname -- "$output_file")"
output_file=$(cd -- "$(dirname -- "$output_file")" && pwd -P)/$(basename -- "$output_file")
rm -f -- "$output_file"
work_dir=$(mktemp -d)
loop_device=''
root_mount="$work_dir/root"
cleanup() {
    set +e
    umount -R "$root_mount" 2>/dev/null || true
    [[ -z "$loop_device" ]] || losetup --detach "$loop_device" 2>/dev/null || true
    rm -rf -- "$work_dir"
}
trap cleanup EXIT

truncate -s "${image_size_gib}G" "$output_file"
image_sectors=$((image_size_gib * 1024 * 1024 * 1024 / 512))
root_sectors=$((root_size_gib * 1024 * 1024 * 1024 / 512))
root_start=2048
data_start=$((root_start + root_sectors))
data_size=$((image_sectors - data_start - 2048))
sfdisk "$output_file" <<EOF
label: dos
unit: sectors
${root_start},${root_sectors},83,*
${data_start},${data_size},83
EOF

loop_device=$(losetup --find --show --partscan "$output_file")
root_partition="${loop_device}p1"
data_partition="${loop_device}p2"
for _ in {1..20}; do
    if [[ -b "$root_partition" && -b "$data_partition" ]]; then break; fi
    sleep 0.25
done
[[ -b "$root_partition" && -b "$data_partition" ]] || die 'loop partitions did not appear'
mkfs.ext4 -F -L RETROBOX_ROOT "$root_partition" > /dev/null
mkfs.ext4 -F -L RETROBOX_DATA "$data_partition" > /dev/null
mkdir -p "$root_mount"
mount "$root_partition" "$root_mount"
mkdir -p "$root_mount/data"
mount "$data_partition" "$root_mount/data"

packages=$(awk '!/^[[:space:]]*#/ && NF { printf "%s%s", separator, $1; separator="," }' "$packages_file")
DEBIAN_FRONTEND=noninteractive debootstrap --arch=amd64 --variant=minbase --include="$packages" "$suite" "$root_mount" https://deb.debian.org/debian

install -d -m 0755 "$root_mount/opt/retrobox" "$root_mount/usr/local/lib/retrobox-installer" \
    "$root_mount/etc/retrobox-appliance"
install -d -m 0755 "$root_mount/var/lib/samba" "$root_mount/var/lib/NetworkManager"
install -m 0755 "$payload_dir/retrobox" "$root_mount/usr/local/bin/retrobox"
install -m 0755 "$payload_dir/$RETROBOX_86BOX_ASSET" "$root_mount/opt/retrobox/86Box.AppImage"
cp -a "$payload_dir/profiles" "$root_mount/opt/retrobox/profiles"
install -m 0644 "$payload_dir/install-retropc.conf" "$root_mount/usr/local/lib/retrobox-installer/install-retropc.conf"
install -m 0644 "$payload_dir/read-install-retropc-conf.sh" "$root_mount/usr/local/lib/retrobox-installer/read-install-retropc-conf.sh"
install -m 0755 "$payload_dir/install-retropc.sh" "$root_mount/usr/local/sbin/install-retropc.sh"
install -m 0644 "$payload_dir/systemd/retrobox-boot.service" "$root_mount/etc/systemd/system/retrobox-boot.service"
install -m 0644 "$payload_dir/samba/smb.conf" "$root_mount/etc/samba/smb.conf"
install -m 0644 "$payload_dir/read-only-root.conf" "$root_mount/etc/retrobox-appliance/read-only-root.conf"

chroot "$root_mount" groupadd --system retrobox
chroot "$root_mount" groupadd --system retrobox-samba
chroot "$root_mount" useradd --create-home --gid retrobox --shell /bin/bash --groups sudo retrobox
chroot "$root_mount" usermod --append --groups retrobox-samba retrobox
chroot "$root_mount" passwd --lock root
retrobox_uid=$(chroot "$root_mount" id -u retrobox)
retrobox_gid=$(chroot "$root_mount" id -g retrobox)
retrobox_samba_gid=$(chroot "$root_mount" getent group retrobox-samba | cut -d: -f3)
install -d -m 0750 "$root_mount/home/retrobox" "$root_mount/data/retrobox" "$root_mount/data/vms" \
    "$root_mount/data/floppies/scratch" "$root_mount/data/floppies/cataloged" "$root_mount/data/snapshots" \
    "$root_mount/data/system-state/samba" "$root_mount/data/system-state/network-manager"
chown -R "$retrobox_uid:$retrobox_gid" "$root_mount/home/retrobox" "$root_mount/data/retrobox" "$root_mount/data/vms" \
    "$root_mount/data/floppies/cataloged" "$root_mount/data/snapshots"
chown "root:$retrobox_samba_gid" "$root_mount/data/floppies/scratch"
chmod 0770 "$root_mount/data/floppies/scratch"
install -d -m 0755 "$root_mount/etc/sudoers.d" "$root_mount/etc/ssh/sshd_config.d"
printf 'retrobox ALL=(ALL:ALL) ALL\n' > "$root_mount/etc/sudoers.d/retrobox"
chmod 0440 "$root_mount/etc/sudoers.d/retrobox"
cat > "$root_mount/etc/ssh/sshd_config.d/retrobox-appliance.conf" <<'EOF'
PermitRootLogin no
PasswordAuthentication yes
EOF

printf 'retrobox\n' > "$root_mount/etc/hostname"
cat > "$root_mount/etc/hosts" <<'EOF'
127.0.0.1 localhost
127.0.1.1 retrobox
::1 localhost ip6-localhost ip6-loopback
EOF
ln -sf /usr/share/zoneinfo/Etc/UTC "$root_mount/etc/localtime"
printf 'Etc/UTC\n' > "$root_mount/etc/timezone"
rm -f "$root_mount/etc/machine-id"

root_uuid=$(blkid -s UUID -o value "$root_partition")
data_uuid=$(blkid -s UUID -o value "$data_partition")
cat > "$root_mount/etc/fstab" <<EOF
UUID=$root_uuid / ext4 ro,errors=remount-ro 0 1
UUID=$data_uuid /data ext4 rw,nosuid,nodev 0 2
/data/swapfile none swap sw 0 0
tmpfs /tmp tmpfs mode=1777,nosuid,nodev 0 0
tmpfs /var/log tmpfs mode=0755,nosuid,nodev 0 0
/data/system-state/samba /var/lib/samba none bind 0 0
/data/system-state/network-manager /var/lib/NetworkManager none bind 0 0
EOF
kernel_image=$(find "$root_mount/boot" -maxdepth 1 -type f -name 'vmlinuz-*' -print -quit)
initrd_image=$(find "$root_mount/boot" -maxdepth 1 -type f -name 'initrd.img-*' -print -quit)
[[ -n "$kernel_image" && -n "$initrd_image" ]] || die 'Debian kernel or initrd is missing'
kernel_name=${kernel_image##*/}
initrd_name=${initrd_image##*/}
install -d -m 0755 "$root_mount/boot/grub"
cat > "$root_mount/boot/grub/grub.cfg" <<EOF
set timeout=0
set default=0
search --no-floppy --fs-uuid --set=root $root_uuid
menuentry 'Retro PC' {
    linux /boot/$kernel_name root=UUID=$root_uuid ro quiet
    initrd /boot/$initrd_name
}
EOF
grub-install --target=i386-pc --boot-directory="$root_mount/boot" --no-floppy "$loop_device" > /dev/null
fallocate -l "${swap_size_gib}G" "$root_mount/data/swapfile"
chmod 0600 "$root_mount/data/swapfile"
mkswap "$root_mount/data/swapfile" > /dev/null
install -d -m 0755 "$root_mount/etc/systemd/system/multi-user.target.wants"
ln -sf /lib/systemd/system/retrobox-boot.service "$root_mount/etc/systemd/system/multi-user.target.wants/retrobox-boot.service"

printf 'build-system-image: created %s\n' "$output_file"
