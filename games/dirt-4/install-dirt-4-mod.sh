#!/usr/bin/env bash
# EgoNet Revival - DiRT 4 installer for Linux.
# Works with the native Feral port (bin/Dirt4) and with dirt4.exe under Proton.
# Usage: ./install-dirt-4-mod.sh [server] [game path]
set -euo pipefail

server="${1:-142.93.206.37}"
game_path="${2:-}"
expected_cert_length=1003
host_names=(prod.egonet.codemasters.com egonet.codemasters.com racenet.codemasters.com)
hosts_begin="# EgoNet Revival DiRT 4"
hosts_end="# End EgoNet Revival DiRT 4"

fail() {
    echo "Error: $*" >&2
    exit 1
}

find_game_path() {
    local root vdf library
    for root in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
        vdf="$root/steamapps/libraryfolders.vdf"
        [ -f "$vdf" ] || continue
        while IFS= read -r library; do
            if [ -d "$library/steamapps/common/DiRT 4" ]; then
                echo "$library/steamapps/common/DiRT 4"
                return 0
            fi
        done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf")
    done
    return 1
}

for tool in curl openssl python3 sudo; do
    command -v "$tool" >/dev/null || fail "$tool is required."
done

if [ -z "$game_path" ]; then
    game_path="$(find_game_path)" || fail "DiRT 4 not found in any Steam library. Pass the game folder as the second argument."
fi

echo "EgoNet Revival - DiRT 4 installer (Linux)"
echo "Server: $server"
echo "Game path: $game_path"

[ -d "$game_path" ] || fail "Game folder not found: $game_path"

executables=()
[ -f "$game_path/bin/Dirt4" ] && executables+=("$game_path/bin/Dirt4")
[ -f "$game_path/dirt4.exe" ] && executables+=("$game_path/dirt4.exe")
[ "${#executables[@]}" -gt 0 ] || fail "Neither bin/Dirt4 nor dirt4.exe found in $game_path"

if pgrep -x "Dirt4|dirt4.exe" >/dev/null; then
    fail "DiRT 4 is running. Close it and run the installer again."
fi

download_dir="$(mktemp -d)"
trap 'rm -rf "$download_dir"' EXIT
cert_path="$download_dir/codemasters-local-root-ca.cer"
cert_url="http://$server/racenet-root-ca.cer"

echo "Downloading root certificate from $cert_url"
curl -fsS -o "$cert_path" "$cert_url"

cert_length="$(stat -c %s "$cert_path")"
[ "$cert_length" -eq "$expected_cert_length" ] ||
    fail "Server root certificate must be $expected_cert_length bytes for in-place patching, but it is $cert_length bytes."

echo "Patching game executables..."
for executable in "${executables[@]}"; do
    # Same search as the Windows installer: a 256-8192 byte DER certificate whose subject is the Codemasters root CA.
    python3 -I - "$executable" "$cert_path" <<'EOF'
import os
import subprocess
import shutil
import sys

executable, cert_path = sys.argv[1], sys.argv[2]
name = os.path.basename(executable)
root = open(cert_path, "rb").read()
data = bytearray(open(executable, "rb").read())


def subject(block):
    result = subprocess.run(["openssl", "x509", "-inform", "der", "-noout", "-subject"], input=block, capture_output=True)
    return result.stdout.decode(errors="replace") if 0 == result.returncode else ""


# Certificates of this size always use a two-byte DER length (30 82 xx xx). The subject check skips certificates
# that only name the Codemasters root as their issuer.
candidates = []
offset = data.find(b"\x30\x82")
while -1 != offset:
    length = 4 + int.from_bytes(data[offset + 2:offset + 4], "big")
    if 256 <= length <= 8192 and offset + length <= len(data):
        block = bytes(data[offset:offset + length])
        if b"Codemasters" in block:
            name_line = subject(block)
            if "CN = Codemasters Online Root CA" in name_line or "OU = Codemasters Online" in name_line:
                candidates.append((offset, block))
    offset = data.find(b"\x30\x82", offset + 1)

if [] == candidates:
    print(f"{name}: Codemasters root CA not found.")
    sys.exit(0)

changed = False
for offset, block in candidates:
    if block == root:
        print(f"{name}: already patched at 0x{offset:x}.")
        continue
    if len(block) != len(root):
        sys.exit(f"{name}: embedded certificate length is {len(block)}, but server root certificate is {len(root)}.")
    backup = executable + ".racenet-original.bak"
    if not os.path.exists(backup):
        shutil.copy2(executable, backup)
        print(f"{name}: backup written.")
    data[offset:offset + len(root)] = root
    changed = True
    print(f"{name}: patched at 0x{offset:x}.")

if changed:
    with open(executable, "r+b") as file:
        file.write(data)
EOF
done

echo "Updating /etc/hosts (sudo)..."
hosts_block="$hosts_begin"$'\n'
for host_name in "${host_names[@]}"; do
    hosts_block+="$server $host_name"$'\n'
done
hosts_block+="$hosts_end"

# Drop any previous block and stray entries for the same names, then append a fresh block.
sudo python3 -I - "$hosts_begin" "$hosts_end" "$hosts_block" "${host_names[@]}" <<'EOF'
import sys

begin, end, block, names = sys.argv[1], sys.argv[2], sys.argv[3], set(sys.argv[4:])
lines = open("/etc/hosts").read().splitlines()
kept = []
inside = False
for line in lines:
    stripped = line.strip()
    if stripped == begin:
        inside = True
        continue
    if stripped == end:
        inside = False
        continue
    if inside:
        continue
    parts = stripped.split()
    if not stripped.startswith("#") and len(parts) >= 2 and parts[1].lower() in names:
        continue
    kept.append(line)

while kept and "" == kept[-1].strip():
    kept.pop()
with open("/etc/hosts", "w") as file:
    file.write("\n".join(kept + ["", block]) + "\n")
EOF
echo "/etc/hosts updated."

echo "Testing HTTPS health endpoint..."
openssl x509 -inform der -in "$cert_path" -out "$cert_path.pem"
if curl -fsS --cacert "$cert_path.pem" "https://prod.egonet.codemasters.com/health"; then
    echo
else
    echo "Health check failed. The game is patched, but the server could not be reached." >&2
fi

echo
echo "Done. Open DiRT 4 and enter RaceNet."
