#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
config_file="$repo_root/appliance/installer/install-retropc.conf"
parser_file="$repo_root/appliance/installer/read-install-retropc-conf.sh"
preseed_file="$repo_root/appliance/installer/preseed.cfg"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$config_file" ]] || fail "installer configuration is missing: $config_file"
[[ -f "$parser_file" ]] || fail "installer configuration parser is missing: $parser_file"
[[ -f "$preseed_file" ]] || fail "installer preseed is missing: $preseed_file"

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

printf 'PASS: installer configuration contract\n'
