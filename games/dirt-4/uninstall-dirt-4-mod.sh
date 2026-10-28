#!/usr/bin/env bash
# Restores the original DiRT 4 executables and removes the EgoNet Revival hosts block.
# Usage: ./uninstall-dirt-4-mod.sh "/path/to/steamapps/common/DiRT 4"
set -euo pipefail

game_path="${1:-}"
if [ -z "$game_path" ]; then
    echo "Usage: $0 \"/path/to/steamapps/common/DiRT 4\"" >&2
    exit 1
fi

for executable in "$game_path/bin/Dirt4" "$game_path/dirt4.exe"; do
    backup="$executable.racenet-original.bak"
    [ -f "$backup" ] || continue
    mv -f "$backup" "$executable"
    echo "$(basename "$executable"): restored."
done

sudo sed -i '/^# EgoNet Revival DiRT 4$/,/^# End EgoNet Revival DiRT 4$/d' /etc/hosts
echo "/etc/hosts cleaned."
echo "Steam 'Verify integrity of game files' also restores the original executables."
