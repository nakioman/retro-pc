#!/usr/bin/env bash
set -euo pipefail

minimum_appimage_bytes=1024

usage() {
    cat <<'EOF'
Usage: build-installer.sh [--output PATH]

Build a BIOS-preserving Debian 13 installer ISO with the Retro PC appliance
payload. The default output is build/retro-pc-installer.iso.

Debian and 86Box release values are read from install-retropc.conf.
EOF
}

die() {
    printf 'build-installer: %s\n' "$*" >&2
    exit 1
}

output_file=''

while (($#)); do
    case "$1" in
        --output)
            (($# >= 2)) || die '--output requires a path'
            output_file=$2
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

missing_commands=false
for required_command in curl xorriso sha256sum mise git; do
    if ! command -v "$required_command" > /dev/null 2>&1; then
        printf "build-installer: required command '%s' was not found; install it and retry\n" "$required_command" >&2
        missing_commands=true
    fi
done
if [[ "$missing_commands" == true ]]; then
    exit 1
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
repo_root=$(cd -- "$script_dir/../.." && pwd -P)
config_file="$script_dir/install-retropc.conf"
parser_file="$script_dir/read-install-retropc-conf.sh"
output_file=${output_file:-"$repo_root/build/retro-pc-installer.iso"}

[[ -r "$config_file" ]] || die "installer configuration is not readable: $config_file"
[[ -r "$parser_file" ]] || die "installer configuration parser is not readable: $parser_file"
# shellcheck source=read-install-retropc-conf.sh
source "$parser_file"
load_retrobox_config "$config_file"

box86_repository=${RETROBOX_86BOX_REPOSITORY:-}
box86_version=${RETROBOX_86BOX_VERSION:-}
box86_asset=${RETROBOX_86BOX_ASSET:-}
debian_version=${RETROBOX_DEBIAN_VERSION:-}
debian_netinst_url="https://cdimage.debian.org/debian-cd/${debian_version}/amd64/iso-cd/debian-${debian_version}-amd64-netinst.iso"
[[ "$box86_repository" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] \
    || die '86Box repository metadata is invalid'
[[ "$box86_version" =~ ^v[A-Za-z0-9._-]+$ ]] \
    || die '86Box version metadata is invalid'
[[ "$box86_asset" =~ ^[A-Za-z0-9._-]+\.AppImage$ ]] \
    || die '86Box asset metadata is invalid'
[[ "$debian_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
    || die 'Debian version metadata is invalid'

output_directory=$(dirname -- "$output_file")
mkdir -p "$output_directory"
output_file=$(cd -- "$output_directory" 2>/dev/null && pwd -P)/$(basename -- "$output_file") \
    || die "could not resolve output directory for: $output_file"

work_dir=$(mktemp -d)
cleanup() {
    rm -rf "$work_dir"
}
trap cleanup EXIT

payload_dir="$work_dir/retropc"
published_binary=${RETROBOX_PUBLISH_BINARY:-"$repo_root/src/RetroBox.Cli/bin/Release/net10.0/linux-x64/publish/retrobox"}
box86_url="https://github.com/${box86_repository}/releases/download/${box86_version}/${box86_asset}"
box86_download="$work_dir/$box86_asset"
debian_iso="$work_dir/debian-${debian_version}-amd64-netinst.iso"

(cd -- "$repo_root" && mise run publish-linux-x64)
[[ -x "$published_binary" ]] || die "published retrobox binary is missing or not executable: $published_binary"

mkdir -p "$payload_dir/systemd" "$payload_dir/samba"
install -m 0755 "$published_binary" "$payload_dir/retrobox"
install -m 0644 "$config_file" "$payload_dir/install-retropc.conf"
install -m 0644 "$parser_file" "$payload_dir/read-install-retropc-conf.sh"
install -m 0755 "$script_dir/install-retropc.sh" "$payload_dir/install-retropc.sh"
install -m 0644 "$script_dir/read-only-root.conf" "$payload_dir/read-only-root.conf"
install -m 0644 "$script_dir/systemd/retrobox-boot.service" "$payload_dir/systemd/retrobox-boot.service"
install -m 0644 "$script_dir/samba/smb.conf" "$payload_dir/samba/smb.conf"
cp -a "$repo_root/profiles" "$payload_dir/profiles"

curl --fail --location --retry 3 --output "$box86_download" "$box86_url"
[[ -s "$box86_download" ]] || die "86Box release asset download is empty: $box86_url"
box86_size=$(wc -c < "$box86_download")
((box86_size >= minimum_appimage_bytes)) \
    || die "86Box release asset is unexpectedly small (${box86_size} bytes): $box86_url"
install -m 0755 "$box86_download" "$payload_dir/$box86_asset"
ln -s "$box86_asset" "$payload_dir/86Box.AppImage"

curl --fail --location --retry 3 --output "$debian_iso" "$debian_netinst_url"
[[ -s "$debian_iso" ]] || die "Debian netinst download is empty: $debian_netinst_url"

iso_root="$work_dir/iso-root"
xorriso -osirrox on -indev "$debian_iso" -extract / "$iso_root"
chmod -R u+w "$iso_root"
isolinux_dir="$iso_root/isolinux"
[[ -f "$isolinux_dir/isolinux.bin" ]] \
    || die 'Debian netinst image is missing the BIOS isolinux boot image'
isohybrid_mbr="$isolinux_dir/isohdpfx.bin"
if [[ ! -f "$isohybrid_mbr" ]]; then
    for candidate in /usr/lib/ISOLINUX/isohdpfx.bin /usr/lib/syslinux/isohdpfx.bin; do
        if [[ -f "$candidate" ]]; then
            isohybrid_mbr=$candidate
            break
        fi
    done
fi
[[ -f "$isohybrid_mbr" ]] \
    || die 'could not find a BIOS isohybrid MBR from the Debian image or ISOLINUX'
boot_menu_count=0
while IFS= read -r -d '' boot_menu; do
    updated_boot_menu="$boot_menu.updated"
    if ! awk '
        /^[[:space:]]*append[[:space:]]/ {
            if ($0 ~ /---/ && $0 !~ /preseed\/file=\/cdrom\/preseed\.cfg/) {
                sub(/[[:space:]]+---/, " preseed/file=/cdrom/preseed.cfg ---")
                changed = 1
            }
        }
        { print }
        END { exit !changed }
    ' "$boot_menu" > "$updated_boot_menu"; then
        rm -f "$updated_boot_menu"
        continue
    fi
    grep -Fq 'preseed/file=/cdrom/preseed.cfg' "$updated_boot_menu" \
        || die "could not verify the preseed argument in BIOS boot menu: $boot_menu"
    mv "$updated_boot_menu" "$boot_menu"
    ((boot_menu_count += 1))
done < <(find "$isolinux_dir" -type f -name '*.cfg' -print0)
((boot_menu_count > 0)) || die 'could not add the preseed argument to an isolinux BIOS boot menu'

rm -f "$output_file" "$output_file.sha256" "$output_file.json"
install -m 0644 "$script_dir/preseed.cfg" "$iso_root/preseed.cfg"
cp -a "$payload_dir" "$iso_root/retropc"
xorriso -as mkisofs \
    -o "$output_file" \
    -r \
    -J \
    -joliet-long \
    -V 'Retro PC Installer' \
    -isohybrid-mbr "$isohybrid_mbr" \
    -b isolinux/isolinux.bin \
    -c isolinux/boot.cat \
    -no-emul-boot \
    -boot-load-size 4 \
    -boot-info-table \
    "$iso_root"

verify_iso_path() {
    local iso_path=$1 inspection
    inspection=$(xorriso -indev "$output_file" -find "$iso_path" -exec lsdl -- 2>&1) \
        || die "could not inspect generated ISO path: $iso_path"
    grep -Fq -- "$iso_path" <<< "$inspection" \
        || die "generated ISO is missing required payload: $iso_path"
}

for iso_path in \
    /preseed.cfg \
    /retropc/install-retropc.sh \
    /retropc/86Box.AppImage \
    /retropc/profiles \
    /retropc/retrobox; do
    verify_iso_path "$iso_path"
done

boot_report=$(xorriso -indev "$output_file" -report_el_torito plain 2>&1) \
    || die 'could not inspect generated ISO boot records'
grep -Eiq '(BIOS|Platform Id[[:space:]]*:[[:space:]]*0x00)' <<< "$boot_report" \
    || die 'generated ISO does not retain a BIOS El Torito boot entry'
if grep -Eiq '(EFI|UEFI|Platform Id[[:space:]]*:[[:space:]]*(0x)?[Ee][Ff])' <<< "$boot_report"; then
    die 'generated ISO El Torito report must not contain EFI or UEFI boot entries'
fi

sha256sum "$output_file" > "$output_file.sha256"
iso_sha256=$(awk '{print $1}' "$output_file.sha256")
git_commit=$(cd -- "$repo_root" && git rev-parse HEAD)
cat > "$output_file.json" <<EOF
{
  "debian_netinst_url": "$debian_netinst_url",
  "86box_repository": "$box86_repository",
  "86box_version": "$box86_version",
  "86box_asset": "$box86_asset",
  "git_commit": "$git_commit",
  "sha256": "$iso_sha256"
}
EOF

printf 'build-installer: created %s\n' "$output_file"
