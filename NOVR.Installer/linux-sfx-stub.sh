#!/usr/bin/env sh
set -eu

marker="__NOVR_LINUX_SFX_PAYLOAD_v1__"
line="$(awk "/^${marker}$/ { print NR + 1; exit }" "$0")"

if [ -z "$line" ]; then
    echo "This executable does not contain an embedded NOVR installer payload." >&2
    exit 1
fi

tmp="${TMPDIR:-/tmp}/NOVR.Installer.$$"
rm -rf "$tmp"
mkdir -p "$tmp"

cleanup() {
    rm -rf "$tmp"
}
trap cleanup EXIT INT TERM

tail -n +"$line" "$0" | tar -xzf - -C "$tmp"

installer="$tmp/NOVR.Installer"
if [ ! -f "$installer" ]; then
    echo "The embedded installer payload did not contain NOVR.Installer." >&2
    exit 1
fi

chmod +x "$installer"
"$installer" "$@"
