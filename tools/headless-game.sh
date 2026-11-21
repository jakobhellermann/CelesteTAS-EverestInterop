#!/usr/bin/env bash
# Run Silksong without a desktop session (e.g. over SSH): a headless weston (GPU renderer) provides the display.
# The game needs X11 (natively on Wayland it exits right after "falling back on no decorations"), so weston runs
# with Xwayland and the game gets its DISPLAY.
#
# Usage: tools/headless-game.sh start [slot]   # start weston + game, wait for the DevServer, load the save slot
#        tools/headless-game.sh stop           # stop game + weston
set -euo pipefail

DEV="localhost:8200"
SOCKET="wayland-tas"
GAME_COMM="Hollow Knight S"  # /proc/<pid>/comm of the game, truncated to 15 chars
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
LOGDIR="${TMPDIR:-/tmp}/headless-game"
mkdir -p "$LOGDIR"

weston_pid() { pgrep -u "$(id -u)" -f "weston .*--socket=$SOCKET" || true; }

stop() {
    for pid in $(pgrep -x "$GAME_COMM" || true) $(weston_pid); do
        kill "$pid"
        tail --pid="$pid" -f /dev/null
    done
}

start() {
    local slot="${1:-3}"
    if pgrep -x "$GAME_COMM" >/dev/null; then
        echo "game already running (pid $(pgrep -x "$GAME_COMM"))" >&2
        exit 1
    fi

    if [ -z "$(weston_pid)" ]; then
        nohup weston --backend=headless --renderer=gl --xwayland --socket="$SOCKET" --width=1920 --height=1080 \
            --idle-time=0 >"$LOGDIR/weston.log" 2>&1 </dev/null &
        timeout 10 bash -c "until grep -q 'xserver listening on display' '$LOGDIR/weston.log'; do sleep 0.2; done"
    fi
    local display
    display=$(grep -o 'xserver listening on display :[0-9]*' "$LOGDIR/weston.log" | tail -1 | grep -o ':[0-9]*')

    (cd ~ && WAYLAND_DISPLAY= DISPLAY="$display" gust --community hollow-knight-silksong run -d \
        >"$LOGDIR/gust.log" 2>&1 </dev/null)
    curl -sf --retry 90 --retry-delay 1 --retry-connrefused --max-time 2 "$DEV/routes" -o /dev/null
    echo "game up (pid $(pgrep -x "$GAME_COMM"), DISPLAY=$display)"

    curl -sf --max-time 60 -X POST -d '' "$DEV/silksongplayground/load-game?slot=$slot" >/dev/null
    echo "loaded save slot $slot"
}

case "${1:-}" in
    start) shift; start "$@" ;;
    stop) stop ;;
    *) echo "usage: $0 start [slot] | stop" >&2; exit 2 ;;
esac
