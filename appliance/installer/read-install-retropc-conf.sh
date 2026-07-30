#!/usr/bin/env bash

# Load the editable installer configuration without sourcing it as shell code.
# Callers source this helper, then invoke load_retrobox_config CONFIG_FILE.
load_retrobox_config() {
    local config_file=${1:?configuration file is required}
    local line key value variable line_number=0
    local assignment_pattern='^([A-Za-z_][A-Za-z0-9_]*|[0-9][A-Za-z0-9_]*)="([^"]*)"$'

    [[ -r "$config_file" ]] || {
        printf 'installer configuration is not readable: %s\n' "$config_file" >&2
        return 1
    }

    while IFS= read -r line || [[ -n "$line" ]]; do
        ((line_number += 1))

        [[ -z "${line//[[:space:]]/}" ]] && continue
        [[ "$line" =~ ^[[:space:]]*# ]] && continue

        if [[ "$line" =~ $assignment_pattern ]]; then
            key=${BASH_REMATCH[1]}
            value=${BASH_REMATCH[2]}
        else
            printf 'invalid installer configuration at line %d\n' "$line_number" >&2
            return 1
        fi

        case "$key" in
            86BOX_REPOSITORY|86BOX_VERSION|86BOX_ASSET)
                variable="RETROBOX_$key"
                ;;
            RETROBOX_CONFIG_ROOT|RETROBOX_DATA_ROOT)
                variable="$key"
                ;;
            INSTALLER_LABEL)
                variable="RETROBOX_$key"
                ;;
            *)
                printf 'unsupported installer configuration key at line %d: %s\n' "$line_number" "$key" >&2
                return 1
                ;;
        esac

        printf -v "$variable" '%s' "$value"
    done < "$config_file"
}
