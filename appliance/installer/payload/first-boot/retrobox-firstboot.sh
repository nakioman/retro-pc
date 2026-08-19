#!/usr/bin/env bash
# RetroBox first-boot provisioning — runs once on the overlay root, generates
# per-machine identity the installer used to bake into the image, then marks
# itself done so it never runs again. See appliance/read-only-root.md.

set -euo pipefail

ssh-keygen -A
systemd-machine-id-setup

mkdir -p /data/system
: > /data/system/first-boot-done
