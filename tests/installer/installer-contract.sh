#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)
installer_dir="$repo_root/appliance/installer"
config_file="$installer_dir/install-retropc.conf"
parser_file="$installer_dir/read-install-retropc-conf.sh"
builder_file="$installer_dir/build-installer.sh"
image_builder_file="$installer_dir/build-system-image.sh"
image_installer_file="$installer_dir/install-image.sh"
boot_service="$installer_dir/systemd/retrobox-boot.service"
live_service="$installer_dir/live/retrobox-installer.service"
packages_file="$repo_root/appliance/debian/packages.txt"

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

for required_file in "$config_file" "$parser_file" "$builder_file" "$image_builder_file" \
    "$image_installer_file" "$boot_service" "$live_service" "$packages_file"; do
    [[ -f "$required_file" ]] || fail "required installer file is missing: $required_file"
done
[[ ! -f "$installer_dir/preseed.cfg" ]] || fail 'legacy Debian preseed must not remain in the image installer'

source "$parser_file"
load_retrobox_config "$config_file"
[[ "$RETROBOX_86BOX_REPOSITORY" == nakioman/86box ]] || fail '86Box repository is wrong'
[[ "$RETROBOX_86BOX_VERSION" == v7.0.0-master.46 ]] || fail '86Box version is wrong'
[[ "$RETROBOX_86BOX_ASSET" == *x86_64*.AppImage ]] || fail '86Box asset is not x86_64'
[[ "$RETROBOX_DEBIAN_VERSION" == 13.6.0 ]] || fail 'Debian version is wrong'
[[ "$RETROBOX_DEBIAN_SUITE" == trixie ]] || fail 'Debian suite is wrong'
[[ "$RETROBOX_DEBIAN_LIVE_VARIANT" == standard ]] || fail 'Debian Live variant is wrong'
[[ "$RETROBOX_IMAGE_SIZE_GIB" == 8 ]] || fail 'image size must be 8 GiB'
[[ "$RETROBOX_ROOT_SIZE_GIB" == 4 ]] || fail 'root size must be 4 GiB'
[[ "$RETROBOX_SWAPFILE_SIZE_GIB" == 2 ]] || fail 'swapfile size must be 2 GiB'
[[ "$RETROBOX_CONFIG_ROOT" == /data/retrobox ]] || fail 'config root is wrong'
[[ "$RETROBOX_DATA_ROOT" == /data ]] || fail 'data root is wrong'
[[ "$RETROBOX_INSTALLER_LABEL" == BIOS-only ]] || fail 'installer label is wrong'

if grep -Eiq '(password|passwd|secret|token|credential|private[_-]?key)[[:space:]]*=' "$config_file"; then
    fail 'installer configuration must not contain credentials'
fi
if grep -Eiq 'debian-(13|trixie).*netinst|preseed/file|pkgsel/include' "$builder_file"; then
    fail 'image builder must not use the Debian Installer package-install path'
fi

for package in systemd systemd-sysv linux-image-amd64 grub-pc-bin openssh-server samba sudo network-manager alsa-utils fuse3 xserver-xorg xinit libgl1 libasound2t64 libpulse0 pulseaudio-utils; do
    grep -Eq "^[[:space:]]*$package([[:space:]]|$)" "$packages_file" \
        || fail "runtime package is missing: $package"
done
if awk '!/^[[:space:]]*#/ && NF { print $1 }' "$packages_file" | grep -Eiq '^(python|pip|desktop|gnome|kde)'; then
    fail 'runtime package manifest contains an explicitly unwanted large dependency'
fi

grep -Fq 'sfdisk' "$image_builder_file" || fail 'system image must define an MBR partition table'
grep -Fq 'IMAGE_SIZE_GIB' "$image_builder_file" || fail 'system image must use configured image size'
grep -Fq 'fallocate -l "${swap_size_gib}G"' "$image_builder_file" \
    || fail 'system image must create the configured swapfile'
grep -Fq 'growpart' "$image_installer_file" || fail 'installer must expand /data'
grep -Fq 'resize2fs' "$image_installer_file" || fail 'installer must resize the /data filesystem'
grep -Fq 'wipefs -a' "$image_installer_file" || fail 'installer must wipe only after disk confirmation'
grep -Fq 'chpasswd' "$image_installer_file" || fail 'installer must set retrobox password'
grep -Fq 'passwd --lock root' "$image_installer_file" || fail 'installer must lock root'
grep -Fq 'smbpasswd -a -s retrobox' "$image_installer_file" || fail 'installer must configure Samba for retrobox'
grep -Fq 'sudoers.d/retrobox' "$image_builder_file" || fail 'image must configure sudo user'
grep -Fq 'dialog' "$image_installer_file" || fail 'installer must provide a console UI'
grep -Fq 'installer_disk' "$image_installer_file" || fail 'installer must exclude its own USB disk'

grep -Fq 'After=local-fs.target' "$boot_service" || fail '86Box must wait for local filesystems'
if grep -Eq '^(After|Wants)=network(-online)?\.target$' "$boot_service"; then
    fail '86Box must not wait for network availability'
fi
grep -Fq 'ExecStart=/usr/local/sbin/retrobox-install-image' "$live_service" \
    || fail 'live image must auto-run installer service'
grep -Fq 'systemd.unit=retrobox-installer.service' "$builder_file" \
    || fail 'BIOS live boot must select the installer service'
if grep -Eiq -- '(^|[[:space:]])(-e|--efi-boot|--efi-boot-part|--efi-boot-image)([[:space:]]|$)' "$builder_file"; then
    fail 'image builder must not add an EFI boot path'
fi

printf 'PASS: preinstalled image installer contract\n'
