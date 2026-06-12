#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/create-reference-archive.sh <Nuclear Option game dir> [output.zip]

Creates stripped metadata-only reference assemblies for CI. Keep the output zip
private; do not commit it to git.
EOF
}

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
  usage
  exit 0
fi

game_dir="${1:-${NUCLEAR_OPTION_GAME_DIR:-}}"
output_zip="${2:-nuclear-option-references.zip}"
case "$output_zip" in
  /*) output_path="$output_zip" ;;
  *) output_path="$PWD/$output_zip" ;;
esac

if [ -z "$game_dir" ]; then
  usage >&2
  exit 2
fi

managed_dir="$game_dir/NuclearOption_Data/Managed"
if [ ! -d "$managed_dir" ]; then
  echo "Could not find NuclearOption_Data/Managed under: $game_dir" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required to install/run JetBrains Refasmer." >&2
  exit 1
fi

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required to create the archive." >&2
  exit 1
fi

tmp_root="$(mktemp -d)"
tool_dir="$tmp_root/tools"
refs_root="$tmp_root/references"
refs_managed="$refs_root/NuclearOption_Data/Managed"
mkdir -p "$tool_dir" "$refs_managed"
trap 'rm -rf "$tmp_root"' EXIT

dotnet tool install jetbrains.refasmer.clitool \
  --tool-path "$tool_dir" \
  --version 2.0.3 \
  >/dev/null

find "$managed_dir" -maxdepth 1 -type f -name '*.dll' -print0 \
  | xargs -0 env PATH="$tool_dir:$PATH" refasmer \
      -q \
      -c \
      --all \
      --omit-non-api-members=false \
      -O "$refs_managed"

mkdir -p "$(dirname "$output_path")"
(
  cd "$refs_root"
  zip -qr "$output_path" NuclearOption_Data
)

echo "Wrote $output_path"
