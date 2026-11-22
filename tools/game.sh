#!/usr/bin/env bash
# Start Silksong in a memory-limited systemd scope, on the running desktop session's X display, or (without one, or
# with --headless) on a headless weston. The game needs X11 (natively on Wayland it exits right after "falling back
# on no decorations"), so headless weston runs with Xwayland.
#
# Usage: tools/game.sh start [--headless] [slot]   # start the game, wait for the DevServer, load the save slot
#        tools/game.sh stop                        # stop the game (and a headless weston)
# MEMORY_MAX (default 20G) limits the game's scope.
set -euo pipefail

DEV="localhost:8200"
SOCKET="wayland-tas"
GAME_COMM="Hollow Knight S"  # /proc/<pid>/comm of the game, truncated to 15 chars
MEMORY_MAX="${MEMORY_MAX:-20G}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
LOGDIR="${TMPDIR:-/tmp}/silksong-game"
mkdir -p "$LOGDIR"

weston_pid() { pgrep -u "$(id -u)" -f "weston .*--socket=$SOCKET" || true; }

stop() {
    for pid in $(pgrep -x "$GAME_COMM" || true) $(weston_pid); do
        kill "$pid"
        tail --pid="$pid" -f /dev/null
    done
}

headless_display() {
    if [ -z "$(weston_pid)" ]; then
        nohup weston --backend=headless --renderer=gl --xwayland --socket="$SOCKET" --width=1920 --height=1080 \
            --idle-time=0 >"$LOGDIR/weston.log" 2>&1 </dev/null &
        timeout 10 bash -c "until grep -q 'xserver listening on display' '$LOGDIR/weston.log'; do sleep 0.2; done"
    fi
    grep -o 'xserver listening on display :[0-9]*' "$LOGDIR/weston.log" | tail -1 | grep -o ':[0-9]*'
}

start() {
    local headless=false slot=3
    for arg in "$@"; do
        case "$arg" in
            --headless) headless=true ;;
            *) slot="$arg" ;;
        esac
    done

    if pgrep -x "$GAME_COMM" >/dev/null; then
        echo "game already running (pid $(pgrep -x "$GAME_COMM"))" >&2
        exit 1
    fi

    # Mesa picks the integrated GPU by default; render on the one with the most VRAM
    local gpu
    gpu=$(for card in /sys/class/drm/card[0-9]/device; do
        echo "$(cat "$card/mem_info_vram_total" 2>/dev/null || echo 0) $(basename "$(readlink -f "$card")")"
    done | sort -n | tail -1 | awk '{print "pci-" $2}' | tr ':.' '__')
    export DRI_PRIME="$gpu"

    local display
    display=$(systemctl --user show-environment | sed -n 's/^DISPLAY=//p')
    if $headless || [ -z "$display" ]; then
        display=$(headless_display)
    fi

    (cd ~ && systemd-run --user --scope --collect -p MemoryMax="$MEMORY_MAX" --unit="silksong-$(date +%s)" \
        env DRI_PRIME="$gpu" WAYLAND_DISPLAY= DISPLAY="$display" gust --community hollow-knight-silksong run -d \
        >"$LOGDIR/gust.log" 2>&1 </dev/null)
    curl -sf --retry 90 --retry-delay 1 --retry-connrefused --max-time 2 "$DEV/routes" -o /dev/null
    local pid
    pid=$(pgrep -x "$GAME_COMM")
    echo "game up (pid $pid, DISPLAY=$display, DRI_PRIME=$gpu, $(sed -n 's/^0:://p' "/proc/$pid/cgroup"))"

    curl -sf --max-time 60 -X POST -d '' "$DEV/silksongplayground/load-game?slot=$slot" >/dev/null
    echo "loaded save slot $slot"
}

case "${1:-}" in
    start) shift; start "$@" ;;
    stop) stop ;;
    *) echo "usage: $0 start [--headless] [slot] | stop" >&2; exit 2 ;;
esac
