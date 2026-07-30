#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
config_file="$repo_root/appliance/installer/install-retropc.conf"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$config_file" ]] || fail "installer configuration is missing: $config_file"

read_config_value() {
    local key=$1

    sed -n -E "s/^${key}=\"([^\"]*)\"$/\1/p" "$config_file"
}

[[ "$(read_config_value 86BOX_REPOSITORY)" == "nakioman/86box" ]] || fail "86BOX_REPOSITORY must be nakioman/86box"
[[ "$(read_config_value 86BOX_VERSION)" == "v7.0.0-master.46" ]] || fail "86BOX_VERSION must be v7.0.0-master.46"
[[ "$(read_config_value 86BOX_ASSET)" == *x86_64* ]] || fail "86BOX_ASSET must identify an x86_64 asset"
[[ "$(read_config_value RETROBOX_CONFIG_ROOT)" == "/data/retrobox" ]] || fail "RETROBOX_CONFIG_ROOT must be /data/retrobox"
[[ "$(read_config_value RETROBOX_DATA_ROOT)" == "/data" ]] || fail "RETROBOX_DATA_ROOT must be /data"
[[ "$(read_config_value INSTALLER_LABEL)" == "BIOS-only" ]] || fail "INSTALLER_LABEL must be BIOS-only"

if grep -Eiq '(password|passwd|secret|token|credential|private[_-]?key)[[:space:]]*=' "$config_file"; then
    fail "installer configuration must not contain credentials"
fi

printf 'PASS: installer configuration contract\n'
