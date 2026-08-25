#!/usr/bin/env bash
# Build the bootable RetroBox USB appliance installer image.
#
# Produces a BIOS/UEFI hybrid (isohybrid with MBR + GPT + EFI El Torito) ISO that
# can be written to a USB stick with `dd` and boots on the target hardware,
# auto-starting the installer on tty1.
#
# Runs on a Debian/Ubuntu Linux host with root (CI: native runner; macOS: inside
# the privileged builder container from Dockerfile). Requires: mmdebstrap,
# mksquashfs, xorriso, isolinux + syslinux modules, grub-efi-amd64-bin.
#
# Env:
#   SUITE            Debian suite (default: trixie)
#   MIRROR           Debian mirror (default: https://deb.debian.org/debian)
#   OUT_DIR          output dir (default: appliance/installer/out)
#   RETROBOX_BIN     path to the published retrobox linux-x64 binary
#   BOX86_APPIMAGE   optional local override for the pinned 86Box AppImage
#   BOX86_ROMS_ARCHIVE optional local override for the pinned ROM tarball

set -euo pipefail

SUITE="${SUITE:-trixie}"
MIRROR="${MIRROR:-https://deb.debian.org/debian}"

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SELF_DIR/../.." && pwd)"
PKG_LIST="$REPO_ROOT/appliance/debian/packages.txt"
ARTIFACT_ENV="$REPO_ROOT/appliance/86box.env"
OUT_DIR="${OUT_DIR:-$SELF_DIR/out}"
ISOLINUX_LIB="/usr/lib/ISOLINUX"
SYSLINUX_MOD="/usr/lib/syslinux/modules/bios"

log()  { printf '\033[1;34m[build]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[build:warning]\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[build:error]\033[0m %s\n' "$*" >&2; exit 1; }
# latest GLOB... -> highest version-sorted path from the expanded glob args.
latest() { printf '%s\n' "$@" | sort -V | tail -n1; }

[ "$(id -u)" = "0" ] || die "Must run as root (mmdebstrap needs it). Use sudo or the privileged builder container."
[ -f "$PKG_LIST" ] || die "Package manifest not found: $PKG_LIST"
command -v mmdebstrap >/dev/null || die "mmdebstrap not installed."
command -v mksquashfs >/dev/null || die "squashfs-tools not installed."
command -v xorriso    >/dev/null || die "xorriso not installed."
command -v grub-mkimage >/dev/null || die "grub-mkimage not installed (grub-efi-amd64-bin)."
command -v curl       >/dev/null || die "curl not installed."
command -v sha256sum  >/dev/null || die "sha256sum not installed."
command -v tar         >/dev/null || die "tar not installed."
[ -f "$ISOLINUX_LIB/isolinux.bin" ] || die "isolinux not installed ($ISOLINUX_LIB/isolinux.bin missing)."
[ -f "$ARTIFACT_ENV" ] || die "Artifact manifest not found: $ARTIFACT_ENV"
# shellcheck disable=SC1090
. "$ARTIFACT_ENV"

[[ "$BOX86_APPIMAGE_SHA256" =~ ^[[:xdigit:]]{64}$ ]] || die "Invalid AppImage SHA256 in $ARTIFACT_ENV"
[[ "$BOX86_ROMS_SHA256" =~ ^[[:xdigit:]]{64}$ ]] || die "Invalid ROMs SHA256 in $ARTIFACT_ENV"

download_and_verify() {
    local source="$1" url="$2" expected="$3" label="$4"
    if [ ! -f "$source" ]; then
        log "Downloading $label"
        curl --fail --location --retry 3 --retry-delay 2 --silent --show-error \
            "$url" -o "$source"
    fi
    printf '%s  %s\n' "$expected" "$source" | sha256sum -c - >/dev/null \
        || die "$label checksum mismatch"
}

WORK="$(mktemp -d)"
LIVE="$WORK/live-root"
TGT="$WORK/tgt-root"
ISO="$WORK/iso"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

mkdir -p "$ISO/live" "$ISO/install" "$ISO/isolinux" "$ISO/install/roms" "$OUT_DIR"

# --- 1. Target appliance rootfs (the deployed OS) --------------------------
log "Building target appliance rootfs from $(basename "$PKG_LIST")"
TARGET_PKGS="$(grep -vE '^[[:space:]]*#|^[[:space:]]*$' "$PKG_LIST" | tr '\n' ',' | sed 's/,$//')"
mmdebstrap \
    --variant=minbase \
    --arch=amd64 \
    --components=main,non-free-firmware \
    --include="$TARGET_PKGS,linux-image-amd64,ca-certificates" \
    "$SUITE" "$TGT" "$MIRROR"

log "Compressing target rootfs -> install/target-rootfs.squashfs"
mksquashfs "$TGT" "$ISO/install/target-rootfs.squashfs" -comp zstd -noappend -no-progress

# --- 2. Live installer rootfs (boots from USB, runs the installer) ---------
log "Building live installer rootfs"
LIVE_PKGS="linux-image-amd64,live-boot,systemd-sysv,parted,gdisk,e2fsprogs,dosfstools,rsync,squashfs-tools,grub-efi-amd64-bin,grub-common,util-linux,pciutils,usbutils,kmod,dialog,bash,ncurses-term,less"
# The customize-hook is single-quoted on purpose: $1 must reach mmdebstrap
# literally (it is mmdebstrap's target dir inside the hook), not expand here.
# shellcheck disable=SC2016
mmdebstrap \
    --variant=minbase \
    --arch=amd64 \
    --include="$LIVE_PKGS" \
    --customize-hook='chroot "$1" update-initramfs -u || true' \
    "$SUITE" "$LIVE" "$MIRROR"

# --- 3. Inject the installer payload + tty1 auto-start into the live root ---
log "Injecting installer + tty1 autologin into the live root"
mkdir -p "$LIVE/opt/retropc-installer"
cp -a "$SELF_DIR/install-retropc.sh" "$SELF_DIR/lib" "$SELF_DIR/payload" \
    "$LIVE/opt/retropc-installer/"
chmod +x "$LIVE/opt/retropc-installer/install-retropc.sh"

mkdir -p "$LIVE/etc/systemd/system/getty@tty1.service.d"
cat > "$LIVE/etc/systemd/system/getty@tty1.service.d/autologin.conf" <<'EOF'
[Service]
ExecStart=
ExecStart=-/sbin/agetty --autologin root --noclear %I 38400 linux
EOF

cat > "$LIVE/root/.bash_profile" <<'EOF'
# Auto-start the RetroBox installer on the primary console only.
if [ "$(tty)" = "/dev/tty1" ]; then
    exec /opt/retropc-installer/install-retropc.sh
fi
EOF

# --- 4. Live squashfs + kernel/initrd --------------------------------------
log "Compressing live rootfs -> live/filesystem.squashfs"
mksquashfs "$LIVE" "$ISO/live/filesystem.squashfs" -comp zstd -noappend -no-progress -e boot
cp "$(latest "$LIVE"/boot/vmlinuz-*)" "$ISO/live/vmlinuz"
cp "$(latest "$LIVE"/boot/initrd.img-*)" "$ISO/live/initrd.img"

# --- 5a. GRUB-EFI image + EFI System Partition directory --------------------
log "Staging GRUB-EFI boot image"
mkdir -p "$ISO/EFI/BOOT" "$ISO/boot/grub"
grub-mkimage -O x86_64-efi -o "$ISO/EFI/BOOT/BOOTX64.EFI" -p /boot/grub part_gpt part_msdos fat ext2 normal search search_fs_uuid search_label configfile linux echo all_video gfxterm font
cat > "$ISO/boot/grub/grub.cfg" <<'EOF'
set timeout=5
set default=0
menuentry "RetroBox Appliance Installer" {
    linux /live/vmlinuz boot=live components quiet video=1280x960@60
    initrd /live/initrd.img
}
EOF
log "Staged GRUB-EFI image at EFI/BOOT/BOOTX64.EFI"

# --- 5. Stage runtime, ROMs, catalog, and VM profiles -----------------------
if [ -z "${RETROBOX_BIN:-}" ] || [ ! -f "${RETROBOX_BIN:-}" ]; then
    die "RETROBOX_BIN must point to the published Linux x64 binary"
fi
install -m 0755 "$RETROBOX_BIN" "$ISO/install/retrobox"

# NativeAOT cannot statically link libSystem.IO.Ports.Native (the runtime only
# ships it as a shared .so), so the daemon/NFC serial P/Invoke resolves it from
# the executable's directory. The publish workflow asserts it in the smoke test
# and uploads it; warn (don't fail) on a stale artifact that predates that.
if [ -f "$(dirname "$RETROBOX_BIN")/libSystem.IO.Ports.Native.so" ]; then
    install -m 0755 "$(dirname "$RETROBOX_BIN")/libSystem.IO.Ports.Native.so" \
        "$ISO/install/libSystem.IO.Ports.Native.so"
else
    warn "libSystem.IO.Ports.Native.so not found next to $RETROBOX_BIN; serial/NFC will be unavailable"
fi

APPIMAGE_CACHE="${BOX86_APPIMAGE:-$WORK/$BOX86_APPIMAGE_NAME}"
ROMS_CACHE="${BOX86_ROMS_ARCHIVE:-$WORK/86box-roms-${BOX86_ROMS_VERSION}.tar.gz}"
download_and_verify "$APPIMAGE_CACHE" "$BOX86_APPIMAGE_URL" "$BOX86_APPIMAGE_SHA256" "86Box AppImage"
download_and_verify "$ROMS_CACHE" "$BOX86_ROMS_URL" "$BOX86_ROMS_SHA256" "86Box ROM tarball"
install -m 0755 "$APPIMAGE_CACHE" "$ISO/install/86box.AppImage"
tar -xzf "$ROMS_CACHE" --strip-components=1 -C "$ISO/install/roms"
[ -n "$(find "$ISO/install/roms" -type f -print -quit)" ] \
    || die "86Box ROM tarball extracted no files"
cp -a "$SELF_DIR/payload/retrobox/vms.yaml" "$ISO/install/vms.yaml"
cp -a "$SELF_DIR/payload/profiles" "$ISO/install/"
found_profile=0
for profile in "$ISO/install"/profiles/*/; do
    [ -d "$profile" ] || continue
    found_profile=1
    vm="${profile%/}"
    vm="${vm##*/}"
    for required in 86box.cfg HDD.vhd shaders/syncmaster3.glsl; do
        [ -f "$profile/$required" ] \
            || die "ISO payload profile $vm is missing $required"
    done
done
[ "$found_profile" = "1" ] || die "ISO payload contains no VM profiles"
log "Staged runtime, ROMs, VM catalog, and profiles"

# --- 6. ISOLINUX BIOS boot files + config ----------------------------------
log "Staging ISOLINUX BIOS boot files"
cp "$ISOLINUX_LIB/isolinux.bin" "$ISO/isolinux/"
for m in ldlinux.c32 libcom32.c32 libutil.c32 menu.c32; do
    cp "$SYSLINUX_MOD/$m" "$ISO/isolinux/"
done
cat > "$ISO/isolinux/isolinux.cfg" <<'EOF'
UI menu.c32
PROMPT 0
TIMEOUT 30
DEFAULT retropc

MENU TITLE RetroBox Appliance Installer

LABEL retropc
  MENU LABEL Install RetroBox Appliance
  KERNEL /live/vmlinuz
  APPEND initrd=/live/initrd.img boot=live components quiet video=1280x960@60
EOF

# --- 7. Hybrid ISO (BIOS + EFI El Torito, isohybrid MBR + GPT, dd-able to USB) ---
OUT_ISO="$OUT_DIR/retropc-installer.iso"
log "Building hybrid ISO -> $OUT_ISO"
xorriso -as mkisofs \
    -iso-level 3 -full-iso9660-filenames \
    -volid RETROPC_INSTALL \
    -isohybrid-mbr "$ISOLINUX_LIB/isohdpfx.bin" \
    -b isolinux/isolinux.bin -c isolinux/boot.cat \
    -no-emul-boot -boot-load-size 4 -boot-info-table \
    -eltorito-alt-boot \
    -e EFI/BOOT/BOOTX64.EFI \
    -no-emul-boot \
    -isohybrid-gpt-basdat \
    -o "$OUT_ISO" "$ISO"

# --- 8. Post-build assertions ----------------------------------------------
log "Verifying image"
file "$OUT_ISO" | grep -qi 'DOS/MBR boot sector' \
    || die "ISO is not isohybrid (no MBR boot sector) — it will not boot from USB."
# El Torito catalog must list both boot entries (BIOS isolinux + EFI BOOTX64).
# report_el_torito plain prints one "El Torito boot img" line per entry; assert
# >= 2 and that the EFI image is present (its path appears in the report).
xorriso -indev "$OUT_ISO" -report_el_torito plain 2>/dev/null > "$WORK/el_torito.txt" \
    || die "xorriso could not read the ISO's El Torito catalog."
[ "$(grep -c 'El Torito boot img' "$WORK/el_torito.txt")" -ge 2 ] \
    || die "ISO El Torito catalog has fewer than 2 boot entries (need BIOS + EFI)."
grep -q 'EFI/BOOT/BOOTX64.EFI' "$WORK/el_torito.txt" \
    || die "ISO El Torito catalog is missing the EFI boot image."
unsquashfs -s "$ISO/install/target-rootfs.squashfs" >/dev/null \
    || die "target-rootfs.squashfs is not a valid squashfs."
# List once to a file and grep the file (no pipe -> no pipefail/SIGPIPE surprise).
unsquashfs -l "$ISO/install/target-rootfs.squashfs" > "$WORK/target.list"
# dialog covers the first-boot prompt; wpa_supplicant + iw are the actual WiFi
# association backend (systemd-networkd has no native WPA2-PSK); the rtw88 blob
# confirms firmware-realtek was bundled.
for bin in usr/sbin/sshd usr/sbin/smbd usr/bin/plymouth usr/sbin/grub-install \
           usr/bin/grub-mkimage usr/bin/dialog usr/sbin/wpa_supplicant usr/sbin/iw \
           usr/lib/firmware/rtw88/rtw8822c_fw.bin; do
    grep -q "/$bin$" "$WORK/target.list" \
        || die "Expected package binary missing from target rootfs: /$bin"
done
[ -f "$ISO/install/retrobox" ] || die "ISO is missing /install/retrobox"
[ -f "$ISO/install/libSystem.IO.Ports.Native.so" ] \
    || warn "ISO is missing /install/libSystem.IO.Ports.Native.so (stale runtime artifact)"
[ -f "$ISO/install/86box.AppImage" ] || die "ISO is missing /install/86box.AppImage"
[ -f "$ISO/install/vms.yaml" ] || die "ISO is missing vms.yaml"
[ -d "$ISO/install/profiles" ] || die "ISO is missing VM profiles"

log "Done: $OUT_ISO"
ls -lh "$OUT_ISO"
