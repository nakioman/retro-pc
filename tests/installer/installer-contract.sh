#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
config_file="$repo_root/appliance/installer/install-retropc.conf"
parser_file="$repo_root/appliance/installer/read-install-retropc-conf.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$config_file" ]] || fail "installer configuration is missing: $config_file"
[[ -f "$parser_file" ]] || fail "installer configuration parser is missing: $parser_file"

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

printf 'PASS: installer configuration contract\n'
