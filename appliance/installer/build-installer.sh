#!/usr/bin/env bash
set -euo pipefail

die() { printf 'build-installer: %s\n' "$*" >&2; exit 1; }
usage() { printf '%s\n' 'Usage: build-installer.sh [--output PATH]'; }

output_file=''
while (($#)); do
    case "$1" in
        --output) output_file=${2:?}; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) die "unknown argument: $1" ;;
    esac
done
[[ $(id -u) -eq 0 ]] || die 'must run as root to build the preinstalled disk image'

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
config_file="$script_dir/install-retropc.conf"
parser_file="$script_dir/read-install-retropc-conf.sh"
output_file=${output_file:-"$repo_root/build/retro-pc-installer.iso"}
mkdir -p "$(dirname -- "$output_file")"
output_dir=$(cd -- "$(dirname -- "$output_file")" && pwd -P)
output_file="$output_dir/$(basename -- "$output_file")"

for required_command in curl xorriso sha256sum mise git debootstrap sfdisk losetup unsquashfs mksquashfs zstd; do
    command -v "$required_command" > /dev/null 2>&1 \
        || die "required command '$required_command' was not found"
done

source "$parser_file"
load_retrobox_config "$config_file"
debian_version=${RETROBOX_DEBIAN_VERSION:-}
live_variant=${RETROBOX_DEBIAN_LIVE_VARIANT:-standard}
debian_live_url="https://cdimage.debian.org/debian-cd/${debian_version}-live/amd64/iso-hybrid/debian-live-${debian_version}-amd64-${live_variant}.iso"
[[ "$debian_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die 'Debian version metadata is invalid'

work_dir=$(mktemp -d)
cleanup() {
    set +e
    umount -R "$work_dir/live-root" 2>/dev/null || true
    rm -rf -- "$work_dir"
}
trap cleanup EXIT

payload_dir="$work_dir/retropc"
mkdir -p "$payload_dir/systemd" "$payload_dir/samba"
(cd "$repo_root" && mise run publish-linux-x64)
published_binary=${RETROBOX_PUBLISH_BINARY:-"$repo_root/src/RetroBox.Cli/bin/Release/net10.0/linux-x64/publish/retrobox"}
[[ -x "$published_binary" ]] || die "published retrobox binary is missing: $published_binary"
install -m 0755 "$published_binary" "$payload_dir/retrobox"
install -m 0644 "$config_file" "$payload_dir/install-retropc.conf"
install -m 0644 "$parser_file" "$payload_dir/read-install-retropc-conf.sh"
install -m 0755 "$script_dir/install-retropc.sh" "$payload_dir/install-retropc.sh"
install -m 0755 "$script_dir/install-image.sh" "$payload_dir/install-image.sh"
install -m 0644 "$script_dir/systemd/retrobox-boot.service" "$payload_dir/systemd/retrobox-boot.service"
install -m 0644 "$script_dir/samba/smb.conf" "$payload_dir/samba/smb.conf"
install -m 0644 "$script_dir/read-only-root.conf" "$payload_dir/read-only-root.conf"
cp -a "$repo_root/profiles" "$payload_dir/profiles"

box86_url="https://github.com/${RETROBOX_86BOX_REPOSITORY}/releases/download/${RETROBOX_86BOX_VERSION}/${RETROBOX_86BOX_ASSET}"
curl --fail --location --retry 3 --output "$payload_dir/$RETROBOX_86BOX_ASSET" "$box86_url"
[[ -s "$payload_dir/$RETROBOX_86BOX_ASSET" ]] || die '86Box download is empty'
chmod 0755 "$payload_dir/$RETROBOX_86BOX_ASSET"

curl --fail --location --retry 3 --output "$work_dir/debian-live.iso" "$debian_live_url"
[[ -s "$work_dir/debian-live.iso" ]] || die 'Debian Live download is empty'

system_image="$work_dir/retrobox-system.raw"
"$script_dir/build-system-image.sh" --output "$system_image" --payload "$payload_dir" --config "$config_file"
zstd --threads=0 --ultra -19 --force --output "$payload_dir/retrobox-system.raw.zst" "$system_image"

iso_root="$work_dir/iso-root"
xorriso -osirrox on -indev "$work_dir/debian-live.iso" -extract / "$iso_root"
chmod -R u+w "$iso_root"
live_squashfs="$iso_root/live/filesystem.squashfs"
[[ -f "$live_squashfs" ]] || die 'Debian Live image has no filesystem squashfs'
live_root="$work_dir/live-root"
unsquashfs -no-progress -d "$live_root" "$live_squashfs" > /dev/null
mount --rbind /dev "$live_root/dev"
mount --make-rslave "$live_root/dev"
mount -t proc proc "$live_root/proc"
mount -t sysfs sysfs "$live_root/sys"
rm -f "$live_root/etc/resolv.conf"
cp -L /etc/resolv.conf "$live_root/etc/resolv.conf"
rm -f "$live_root/etc/apt/sources.list.d/debian.sources" "$live_root/etc/apt/sources.list"
cat > "$live_root/etc/apt/sources.list" <<'EOF'
deb http://deb.debian.org/debian trixie main
deb http://deb.debian.org/debian trixie-updates main
deb http://security.debian.org/debian-security trixie-security main
EOF
chroot "$live_root" apt-get update
chroot "$live_root" env DEBIAN_FRONTEND=noninteractive apt-get install --yes --no-install-recommends \
    cloud-guest-utils dialog e2fsprogs zstd
install -d -m 0755 "$live_root/usr/local/sbin" "$live_root/etc/systemd/system/multi-user.target.wants"
install -m 0755 "$script_dir/install-image.sh" "$live_root/usr/local/sbin/retrobox-install-image"
install -m 0644 "$script_dir/live/retrobox-installer.service" "$live_root/etc/systemd/system/retrobox-installer.service"
ln -sf /etc/systemd/system/retrobox-installer.service \
    "$live_root/etc/systemd/system/multi-user.target.wants/retrobox-installer.service"
mksquashfs "$live_root" "$live_squashfs" -comp xz -noappend -no-progress > /dev/null

install -d -m 0755 "$iso_root/retropc"
cp -a "$payload_dir/retrobox-system.raw.zst" "$iso_root/retropc/retrobox-system.raw.zst"
rm -rf "$iso_root/EFI" "$iso_root/boot/grub" "$iso_root/isolinux/efiboot.img"

while IFS= read -r -d '' boot_menu; do
    temporary_menu="$boot_menu.updated"
    awk '
        /^[[:space:]]*append[[:space:]]/ && $0 !~ /systemd\.unit=retrobox-installer\.service/ {
            print $0 " systemd.unit=retrobox-installer.service"
            next
        }
        { print }
    ' "$boot_menu" > "$temporary_menu"
    mv "$temporary_menu" "$boot_menu"
done < <(find "$iso_root/isolinux" -type f -name '*.cfg' -print0)

isohybrid_mbr=''
for candidate in "$iso_root/isolinux/isohdpfx.bin" /usr/lib/ISOLINUX/isohdpfx.bin /usr/lib/syslinux/isohdpfx.bin; do
    if [[ -f "$candidate" ]]; then isohybrid_mbr=$candidate; break; fi
done
[[ -n "$isohybrid_mbr" ]] || die 'could not find a BIOS isohybrid MBR'

rm -f "$output_file" "$output_file.sha256" "$output_file.json"
xorriso -as mkisofs \
    -o "$output_file" \
    -r -J -joliet-long -V 'Retro PC Installer' \
    -isohybrid-mbr "$isohybrid_mbr" \
    -b isolinux/isolinux.bin -c isolinux/boot.cat \
    -no-emul-boot -boot-load-size 4 -boot-info-table \
    "$iso_root"

for iso_path in /retropc/retrobox-system.raw.zst /live/filesystem.squashfs; do
    inspection=$(xorriso -indev "$output_file" -find "$iso_path" -exec lsdl -- 2>&1) \
        || die "could not inspect generated ISO path: $iso_path"
    grep -Fq -- "$iso_path" <<< "$inspection" || die "generated ISO is missing $iso_path"
done
boot_report=$(xorriso -indev "$output_file" -report_el_torito plain 2>&1) \
    || die 'could not inspect generated ISO boot records'
grep -Eiq '(BIOS|Platform Id[[:space:]]*:[[:space:]]*0x00)' <<< "$boot_report" \
    || die 'generated ISO has no BIOS boot entry'
if grep -Eiq '(EFI|UEFI|Platform Id[[:space:]]*:[[:space:]]*(0x)?[Ee][Ff])' <<< "$boot_report"; then
    die 'generated ISO must not contain EFI or UEFI boot entries'
fi

install -m 0644 "$payload_dir/retrobox-system.raw.zst" "$output_dir/retrobox-system.raw.zst"
sha256sum "$output_file" > "$output_file.sha256"
sha256sum "$output_dir/retrobox-system.raw.zst" > "$output_dir/retrobox-system.raw.zst.sha256"
iso_sha256=$(awk '{print $1}' "$output_file.sha256")
image_sha256=$(awk '{print $1}' "$output_dir/retrobox-system.raw.zst.sha256")
git_commit=$(cd "$repo_root" && git rev-parse HEAD)
cat > "$output_file.json" <<EOF
{
  "debian_live_url": "$debian_live_url",
  "image_size_gib": "$RETROBOX_IMAGE_SIZE_GIB",
  "root_size_gib": "$RETROBOX_ROOT_SIZE_GIB",
  "swapfile_size_gib": "$RETROBOX_SWAPFILE_SIZE_GIB",
  "raw_image_sha256": "$image_sha256",
  "86box_repository": "$RETROBOX_86BOX_REPOSITORY",
  "86box_version": "$RETROBOX_86BOX_VERSION",
  "86box_asset": "$RETROBOX_86BOX_ASSET",
  "git_commit": "$git_commit",
  "sha256": "$iso_sha256"
}
EOF
printf 'build-installer: created %s\n' "$output_file"
