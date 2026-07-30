#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
config_file="$repo_root/appliance/installer/install-retropc.conf"
parser_file="$repo_root/appliance/installer/read-install-retropc-conf.sh"
preseed_file="$repo_root/appliance/installer/preseed.cfg"
installer_file="$repo_root/appliance/installer/install-retropc.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$config_file" ]] || fail "installer configuration is missing: $config_file"
[[ -f "$parser_file" ]] || fail "installer configuration parser is missing: $parser_file"
[[ -f "$preseed_file" ]] || fail "installer preseed is missing: $preseed_file"
[[ -f "$installer_file" ]] || fail "target-side installer is missing: $installer_file"

# shellcheck source=/dev/null
source "$parser_file"
load_retrobox_config "$config_file"

[[ "${RETROBOX_86BOX_REPOSITORY:-}" == "nakioman/86box" ]] || fail "parser must expose RETROBOX_86BOX_REPOSITORY"
[[ "${RETROBOX_86BOX_VERSION:-}" == "v7.0.0-master.46" ]] || fail "parser must expose RETROBOX_86BOX_VERSION"
[[ "${RETROBOX_86BOX_ASSET:-}" == *x86_64* ]] || fail "parser must expose an x86_64 RETROBOX_86BOX_ASSET"
[[ "${RETROBOX_CONFIG_ROOT:-}" == "/data/retrobox" ]] || fail "parser must expose RETROBOX_CONFIG_ROOT"
[[ "${RETROBOX_DATA_ROOT:-}" == "/data" ]] || fail "parser must expose RETROBOX_DATA_ROOT"
[[ "${RETROBOX_INSTALLER_LABEL:-}" == "BIOS-only" ]] || fail "parser must expose RETROBOX_INSTALLER_LABEL"

if grep -Eiq '(password|passwd|secret|token|credential|private[_-]?key)[[:space:]]*=' "$config_file"; then
    fail "installer configuration must not contain credentials"
fi

if awk '
    $1 == "d-i" && $2 == "partman-auto/disk" &&
        !($3 == "seen" && $4 == "false" && NF == 4) { found = 1 }
    END { exit !found }
' "$preseed_file"; then
    fail "preseed must leave target disk selection interactive"
fi

grep -Eq '^[[:space:]]*d-i[[:space:]]+partman-auto/disk[[:space:]]+seen[[:space:]]+false[[:space:]]*$' "$preseed_file" \
    || fail "preseed must show the target disk selection prompt"

if grep -Eq '^[[:space:]]*d-i[[:space:]]+(partman/confirm|partman/confirm_nooverwrite|partman-partitioning/confirm_write_new_label)[[:space:]]+' "$preseed_file"; then
    fail "preseed must leave destructive partition confirmation interactive"
fi

if grep -Eq '^[[:space:]]*d-i[[:space:]]+partman/choose_partition[[:space:]]+select[[:space:]]+finish[[:space:]]*$' "$preseed_file"; then
    fail "preseed must not finish partitioning without confirmation"
fi

if grep -Eiq '^[[:space:]]*d-i[[:space:]]+(passwd/|user-setup/.*password)' "$preseed_file"; then
    fail "preseed must not embed user credentials"
fi

if grep -Eiq '^[[:space:]]*d-i[[:space:]]+[^[:space:]]*(efi|uefi)' "$preseed_file"; then
    fail "preseed must not configure UEFI boot entries"
fi

grep -Eq '^[[:space:]]*d-i[[:space:]]+preseed/late_command[[:space:]]+string[[:space:]].*/cdrom/retropc/install-retropc\.sh' "$preseed_file" \
    || fail "preseed late command must invoke /cdrom/retropc/install-retropc.sh"

grep -Eq '^[[:space:]]*d-i[[:space:]]+partman-auto/expert_recipe[[:space:]]+string' "$preseed_file" \
    || fail "preseed must define an automatic partition recipe"

grep -Eq '^[[:space:]]*d-i[[:space:]]+partman-auto/choose_recipe[[:space:]]+select[[:space:]]+retrobox[[:space:]]*$' "$preseed_file" \
    || fail "preseed must select the retrobox partition recipe"

grep -Eq 'mountpoint\{[[:space:]]*/[[:space:]]*\}' "$preseed_file" \
    || fail "preseed partition recipe must mount root"

grep -Eq 'mountpoint\{[[:space:]]*/data[[:space:]]*\}' "$preseed_file" \
    || fail "preseed partition recipe must mount /data"

grep -Eq '^[[:space:]]*d-i[[:space:]]+partman/mount_style[[:space:]]+select[[:space:]]+uuid[[:space:]]*$' "$preseed_file" \
    || fail "preseed must generate UUID-based fstab entries"

test_root=$(mktemp -d)
test_payload=$(mktemp -d)
test_bin=$(mktemp -d)
canonical_test_root=$(cd "$test_root" && pwd -P)
cleanup() {
    rm -rf "$test_root" "$test_payload" "$test_bin"
}
trap cleanup EXIT

mkdir -p \
    "$test_root/etc" \
    "$test_root/dev/disk/by-id" \
    "$test_payload/systemd" \
    "$test_payload/samba" \
    "$test_payload/profiles/pentium100/shaders" \
    "$test_payload/profiles/386sx16/shaders"

cp "$config_file" "$test_payload/install-retropc.conf"
cp "$parser_file" "$test_payload/read-install-retropc-conf.sh"
cp "$repo_root/appliance/installer/systemd/retrobox-boot.service" "$test_payload/systemd/retrobox-boot.service"
cp "$repo_root/appliance/installer/samba/smb.conf" "$test_payload/samba/smb.conf"
cp "$repo_root/appliance/installer/read-only-root.conf" "$test_payload/read-only-root.conf"
cp "$repo_root/profiles/pentium100/86box.cfg" "$test_payload/profiles/pentium100/86box.cfg"
cp "$repo_root/profiles/pentium100/shaders/syncmaster3.glsl" "$test_payload/profiles/pentium100/shaders/"
cp "$repo_root/profiles/386sx16/86box.cfg" "$test_payload/profiles/386sx16/86box.cfg"
cp "$repo_root/profiles/386sx16/shaders/syncmaster3.glsl" "$test_payload/profiles/386sx16/shaders/"
touch "$test_payload/$RETROBOX_86BOX_ASSET" "$test_payload/profiles/pentium100/HDD.vhd" "$test_root/dev/disk/by-id/ata-SONY_DVD_RW"
touch "$test_root/etc/fstab"
printf 'administrator sudo policy\n' > "$test_root/etc/sudoers"

make_stub() {
    local name=$1
    cat > "$test_bin/$name"
    chmod +x "$test_bin/$name"
}

make_stub id <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" == "-u" ]]; then
    printf '%s\n' "${RETROBOX_TEST_UID:-0}"
fi
EOF
make_stub groupadd <<'EOF'
#!/usr/bin/env bash
printf 'groupadd %s\n' "$*" >> "$RETROBOX_TEST_COMMAND_LOG"
EOF
make_stub useradd <<'EOF'
#!/usr/bin/env bash
printf 'useradd %s\n' "$*" >> "$RETROBOX_TEST_COMMAND_LOG"
EOF
make_stub chown <<'EOF'
#!/usr/bin/env bash
printf 'chown %s\n' "$*" >> "$RETROBOX_TEST_COMMAND_LOG"
EOF
make_stub systemctl <<'EOF'
#!/usr/bin/env bash
printf 'systemctl %s\n' "$*" >> "$RETROBOX_TEST_COMMAND_LOG"
EOF
make_stub findmnt <<'EOF'
#!/usr/bin/env bash
if [[ "$*" == *OPTIONS* ]]; then
    printf '%s\n' "${RETROBOX_TEST_ROOT_OPTIONS:-rw,relatime}"
fi
EOF
make_stub mount <<'EOF'
#!/usr/bin/env bash
printf 'mount %s\n' "$*" >> "$RETROBOX_TEST_COMMAND_LOG"
EOF

command_log="$test_root/installer-commands.log"
PATH="$test_bin:$PATH" \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf"

for relative_path in \
    data/retrobox \
    data/vms \
    data/floppies/scratch \
    data/floppies/cataloged \
    data/snapshots; do
    [[ -d "$test_root/$relative_path" ]] || fail "installer must create /$relative_path"
done

[[ -x "$test_root/opt/retrobox/86Box.AppImage" ]] || fail "installer must install an executable 86Box AppImage"
[[ -x "$test_root/usr/local/sbin/install-retropc.sh" ]] \
    || fail "installer must install the post-reboot maintenance command"
[[ -f "$test_root/data/vms/pentium100/HDD.vhd" ]] \
    || fail "installer must place the active Pentium VHD in writable /data"
[[ -f "$test_root/data/vms/pentium100/shaders/syncmaster3.glsl" ]] \
    || fail "installer must place Pentium shaders beside the writable VM profile"
[[ -f "$test_root/opt/retrobox/profiles/386sx16/shaders/syncmaster3.glsl" ]] \
    || fail "installer must copy 386 shaders"
[[ ! -e "$test_root/opt/retrobox/profiles/pentium100" ]] \
    || fail "installer must keep mutable Pentium state out of immutable /opt"
grep -Fqx 'cdrom_01_host_drive = /dev/disk/by-id/ata-SONY_DVD_RW' "$test_root/data/vms/pentium100/86box.cfg" \
    || fail "installer must prefer a stable CD-ROM by-id path"
grep -Fqx 'floppy_control_socket_enabled = 0' "$test_root/data/vms/pentium100/86box.cfg" \
    || fail "installer must disable the deferred floppy control socket"
grep -Fqx 'hdd_01_fn = HDD.vhd' "$test_root/data/vms/pentium100/86box.cfg" \
    || fail "installer must preserve Pentium profile-relative VHD paths"
grep -Fqx '86BOX_VERSION=v7.0.0-master.46' "$test_root/etc/retrobox-appliance/install-report.txt" \
    || fail "installer must record the pinned 86Box version"
grep -Fqx 'CDROM_DEVICE=/dev/disk/by-id/ata-SONY_DVD_RW' "$test_root/etc/retrobox-appliance/install-report.txt" \
    || fail "installer must report the selected CD-ROM device"
grep -Fqx 'UUID=root-test-uuid / ext4 ro,errors=remount-ro 0 1' "$test_root/etc/fstab" \
    || fail "installer must write a UUID-rooted read-only fstab entry"
grep -Fqx 'UUID=data-test-uuid /data ext4 rw,nosuid,nodev 0 2' "$test_root/etc/fstab" \
    || fail "installer must write a UUID-rooted writable data fstab entry"
grep -Fqx '/data/system-state/samba /var/lib/samba none bind 0 0' "$test_root/etc/fstab" \
    || fail "installer must preserve Samba state below writable /data"
grep -Fqx '/data/system-state/network-manager /var/lib/NetworkManager none bind 0 0' "$test_root/etc/fstab" \
    || fail "installer must preserve network manager state below writable /data"
[[ -d "$test_root/data/system-state/samba" && -d "$test_root/data/system-state/network-manager" ]] \
    || fail "installer must create persistent writable service-state directories"
cmp -s <(printf 'administrator sudo policy\n') "$test_root/etc/sudoers" \
    || fail "installer must preserve the administrator sudo policy"
grep -Fq 'systemctl --root=' "$command_log" \
    || fail "installer must enable only its boot service in the target root"
if grep -Eiq 'floppy.*daemon|retrobox-daemon' "$command_log"; then
    fail "installer must never enable a floppy daemon"
fi

grep -Fq 'TTYPath=/dev/tty1' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must launch on tty1"
grep -Fq 'Restart=on-failure' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must restart after an emulator failure"
grep -Fq -- '--vmpath /data/vms/pentium100' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must launch the Pentium profile"
grep -Fq 'RETROBOX_MAINTENANCE=1' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must document a fullscreen maintenance override"
grep -Fq 'Conflicts=getty@tty1.service' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must reserve tty1 from getty"
grep -Fq 'Before=getty@tty1.service' "$test_root/etc/systemd/system/retrobox-boot.service" \
    || fail "boot service must order before the tty1 getty"
grep -Fq '/data/system-state/samba' "$test_root/etc/retrobox-appliance/read-only-root.conf" \
    || fail "read-only support must document persistent Samba state"
grep -Fq '/data/system-state/network-manager' "$test_root/etc/retrobox-appliance/read-only-root.conf" \
    || fail "read-only support must document persistent network state"

printf 'persisted VHD state\n' > "$test_root/data/vms/pentium100/HDD.vhd"
printf 'persisted NVR state\n' > "$test_root/data/vms/pentium100/86box.nvr"

PATH="$test_bin:$PATH" \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf"
[[ $(grep -Fc 'UUID=root-test-uuid / ext4 ro,errors=remount-ro 0 1' "$test_root/etc/fstab") -eq 1 ]] \
    || fail "installer must keep fstab root provisioning idempotent"
[[ $(grep -Fc 'UUID=data-test-uuid /data ext4 rw,nosuid,nodev 0 2' "$test_root/etc/fstab") -eq 1 ]] \
    || fail "installer must keep fstab data provisioning idempotent"
grep -Fqx 'persisted VHD state' "$test_root/data/vms/pentium100/HDD.vhd" \
    || fail "installer must preserve the active Pentium VHD on repeat provisioning"
grep -Fqx 'persisted NVR state' "$test_root/data/vms/pentium100/86box.nvr" \
    || fail "installer must preserve generated Pentium NVR state on repeat provisioning"

rm "$test_root/dev/disk/by-id/ata-SONY_DVD_RW"
touch "$test_root/dev/sr0"
PATH="$test_bin:$PATH" \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf"
grep -Fqx 'CDROM_DEVICE=/dev/sr0' "$test_root/etc/retrobox-appliance/install-report.txt" \
    || fail "installer must fall back to /dev/sr0 for CD-ROM passthrough"

rm "$test_root/dev/sr0"
PATH="$test_bin:$PATH" \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf"
grep -Fqx 'CDROM_STATE=missing' "$test_root/etc/retrobox-appliance/install-report.txt" \
    || fail "installer must explicitly report a missing CD-ROM device"
grep -Fqx 'CDROM_DEVICE=none' "$test_root/etc/retrobox-appliance/install-report.txt" \
    || fail "installer must report no CD-ROM device when none is available"

if PATH="$test_bin:$PATH" RETROBOX_TEST_UID=1000 bash "$installer_file" --target-root "$test_root" >/dev/null 2>&1; then
    fail "installer must refuse a non-root invocation"
fi

if bash "$installer_file" --target-root >/dev/null 2>&1; then
    fail "installer must reject a target-root argument without a path"
fi
if bash "$installer_file" --unknown-option >/dev/null 2>&1; then
    fail "installer must reject unknown arguments"
fi

readonly_error="$test_root/readonly-error.log"
if PATH="$test_bin:$PATH" \
    RETROBOX_TEST_ROOT_OPTIONS=ro,relatime \
    RETROBOX_PAYLOAD_ROOT="$test_payload" \
    RETROBOX_ROOT_UUID="root-test-uuid" \
    RETROBOX_DATA_UUID="data-test-uuid" \
    RETROBOX_TEST_COMMAND_LOG="$command_log" \
    bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf" > /dev/null 2> "$readonly_error"; then
    fail "installer must refuse default provisioning on a read-only root"
fi
grep -Fq -- '--maintenance' "$readonly_error" \
    || fail "read-only provisioning failure must explain the maintenance command"

PATH="$test_bin:$PATH" \
RETROBOX_TEST_ROOT_OPTIONS=ro,relatime \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$test_root/usr/local/sbin/install-retropc.sh" --maintenance --target-root "$test_root"
grep -Fq "mount -o remount,rw $canonical_test_root" "$command_log" \
    || fail "maintenance mode must remount the selected root read-write"

PATH="$test_bin:$PATH" \
RETROBOX_TEST_ROOT_OPTIONS=rw,errors=remount-ro \
RETROBOX_PAYLOAD_ROOT="$test_payload" \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf"

PATH="$test_bin:$PATH" \
RETROBOX_TEST_ROOT_OPTIONS=rw,errors=remount-ro \
RETROBOX_ROOT_UUID="root-test-uuid" \
RETROBOX_DATA_UUID="data-test-uuid" \
RETROBOX_TEST_COMMAND_LOG="$command_log" \
bash "$test_root/usr/local/sbin/install-retropc.sh" --target-root "$test_root"
grep -Fqx 'persisted VHD state' "$test_root/data/vms/pentium100/HDD.vhd" \
    || fail "installed provisioning command must preserve the active Pentium VHD"
grep -Fqx 'persisted NVR state' "$test_root/data/vms/pentium100/86box.nvr" \
    || fail "installed provisioning command must preserve generated Pentium NVR state"
[[ $(grep -Fc 'UUID=root-test-uuid / ext4 ro,errors=remount-ro 0 1' "$test_root/etc/fstab") -eq 1 ]] \
    || fail "installed provisioning command must keep root provisioning idempotent"

grep -Fq '[retro-floppy-scratch]' "$test_root/etc/samba/smb.conf" \
    || fail "installer must install the restricted Samba scratch share"
grep -Fq 'guest ok = no' "$test_root/etc/samba/smb.conf" \
    || fail "Samba scratch share must disable guest access"
if grep -Eq '^\[[^]]*(cataloged|vms|snapshots)' "$test_root/etc/samba/smb.conf"; then
    fail "Samba must not expose cataloged media, VM disks, or snapshots"
fi
grep -Fq 'systemctl edit retrobox-boot.service' "$test_root/etc/retrobox-appliance/read-only-root.conf" \
    || fail "read-only support must document the maintenance boot override"
grep -Fq 'mount -o remount,ro /' "$test_root/etc/retrobox-appliance/read-only-root.conf" \
    || fail "read-only support must document returning root to read-only mode"
grep -Fq '/usr/local/sbin/install-retropc.sh --maintenance' "$test_root/etc/retrobox-appliance/read-only-root.conf" \
    || fail "read-only support must document the installed maintenance command"

touch "$test_payload/86Box.AppImage"
rm "$test_payload/$RETROBOX_86BOX_ASSET"
if PATH="$test_bin:$PATH" \
    RETROBOX_PAYLOAD_ROOT="$test_payload" \
    RETROBOX_ROOT_UUID="root-test-uuid" \
    RETROBOX_DATA_UUID="data-test-uuid" \
    RETROBOX_TEST_COMMAND_LOG="$command_log" \
    bash "$installer_file" --target-root "$test_root" --config "$test_payload/install-retropc.conf" > /dev/null 2>&1; then
    fail "installer must reject a generic AppImage when the pinned asset is absent"
fi

printf 'PASS: installer contract\n'
