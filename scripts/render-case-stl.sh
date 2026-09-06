#!/usr/bin/env bash
# Render Mac Classic case parts to STL with OpenSCAD.
#
# Usage: render-case-stl.sh [part ...]
# With no arguments every printable part is rendered — expect a long wait,
# the shell pieces take many minutes each on OpenSCAD's CGAL backend.
set -euo pipefail

cd "$(dirname "$0")/.."
scad=hardware/mac-classic-case/mac-classic-case.scad
out=hardware/mac-classic-case/stl

all_parts=(
  front-lower front-upper
  back-lower-left back-lower-right back-upper-left back-upper-right
  drive-cage cage-pedestals fdd-funnel dvd-funnel
  hdd-cradle lcd-frame dvd-tray-cover eject-cap tpu-feet
)

if [ $# -gt 0 ]; then
  parts=("$@")
else
  parts=("${all_parts[@]}")
fi

for p in "${parts[@]}"; do
  echo "== rendering $p"
  openscad -o "$out/$p.stl" -D "part=\"$p\"" "$scad"
done
