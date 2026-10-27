#!/bin/bash
# PredatorSense key: opens PredatorCore, or brings its window forward when it is already running.
# Runs as root from nitro-key-detection.service (it reads the keyboard device directly).
# install.sh puts it at /usr/local/bin/nitro-key-detection.sh, in place of the DAMX version.
set -uo pipefail

APP="/opt/predatorcore/PredatorCore"
KEY=425
if [ -f /etc/damx/nitro_key.conf ]; then
    # shellcheck disable=SC1091
    source /etc/damx/nitro_key.conf
    KEY="${NITRO_KEY:-425}"
fi

# The user to open it for, looked up at each press, so logging out and back in needs no restart.
session_user() {
    if [ -n "${DAMX_TARGET_USER:-}" ] && id -u "$DAMX_TARGET_USER" >/dev/null 2>&1; then
        echo "$DAMX_TARGET_USER"
        return
    fi
    loginctl list-sessions --no-legend 2>/dev/null | awk '$4 == "seat0" && $3 != "gdm" { print $3; exit }'
}

launch() {
    local user uid
    user="$(session_user)"
    [ -n "$user" ] || { echo "No desktop session to open PredatorCore in."; return; }
    uid="$(id -u "$user")"
    [ -S "/run/user/$uid/bus" ] || { echo "$user isn't logged in to a desktop yet."; return; }

    # Start it inside the user's own systemd session, so it gets that session's current DISPLAY,
    # WAYLAND_DISPLAY and XAUTHORITY: Xwayland's cookie file changes at every login, and a copy taken
    # when this service started goes stale. A second copy just raises the running window and exits.
    runuser -u "$user" -- env XDG_RUNTIME_DIR="/run/user/$uid" \
        DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" \
        systemd-run --user --collect --quiet "$APP" ||
        echo "Couldn't start $APP for $user."
}

DEVICE=$(awk '/^N: Name="AT Translated Set 2 keyboard"/ { f = 1 }
    f && /^H: Handlers=/ { if (match($0, /event[0-9]+/)) { print "/dev/input/" substr($0, RSTART, RLENGTH); exit } }
    /^$/ { f = 0 }' /proc/bus/input/devices)
[ -n "$DEVICE" ] || { echo "Error: could not find the built-in keyboard."; exit 1; }
command -v evtest >/dev/null 2>&1 || { echo "Error: evtest is required (sudo dnf install evtest)."; exit 1; }

echo "Watching $DEVICE for the PredatorSense key (code $KEY)"
last=0
evtest "$DEVICE" | grep --line-buffered "code $KEY (.*value 1$" | while read -r _; do
    now=$(date +%s%3N)
    (( now - last < 800 )) && continue # one press, not a burst
    last=$now
    launch
done
