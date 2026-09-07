#!/usr/bin/env bash
# Clean startup + staged log snapshots for CelesteTAS/Silksong debugging.
#
# kill game → fresh logsnap session (silksong state, same as the `slogsnap` alias) → launch + load a save slot →
# checkpoint the logs at two stages: "boot" (after BepInEx + mod init) and "loadlevel" (after the save's gameplay
# scene is up). The logsnap session cursors at EOF and is rotation-aware, so it follows BepInEx truncating
# LogOutput.log on relaunch. Inspect with:  slogsnap list  /  slogsnap diff  /  slogsnap diff <id>
#
# Usage:  tools/clean-reload.sh [slot]      # slot defaults to 4
set -eu

SLOT="${1:-4}"
DEV="localhost:8200"
PROFILE="$HOME/.config/r2modmanPlus-local/HollowKnightSilksong/profiles/Default"
BEPINEX_LOG="$PROFILE/BepInEx/LogOutput.log"
PLAYER_LOG="$HOME/.config/unity3d/Team Cherry/Hollow Knight Silksong/Player.log"
WRAPPER="$HOME/.config/r2modmanPlus-local/HollowKnightSilksong/linux_wrapper.sh"
GAME="$HOME/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong"
export LOGSNAP_STATE="$HOME/.local/state/logsnap/silksong"   # same state dir as the `slogsnap` fish alias

# 0. Fresh logsnap session on the mod + engine logs (cursors at EOF), then kill any running instance.
logsnap open "$BEPINEX_LOG" "$PLAYER_LOG"
# Match the game by its executable path (in the argv of the game + wrapper + mangohud), NOT the bare string
# "Hollow Knight Silksong" — the latter with -f also matches any *caller* whose command line references the game's
# data dir (e.g. a shell loading …/Hollow Knight Silksong/ModData/…/Savestates/…), killing it too. The exe path
# "…/Hollow Knight Silksong/Hollow Knight Silksong" doesn't appear in those data-dir paths, so this only hits the game.
# Kill any running instance and block until it has fully exited before relaunching. Its DevServer keeps answering
# for a moment after SIGTERM, so relaunching immediately would let the readiness poll below race the dying instance
# (see it "up" against the old server, commit a bogus boot checkpoint, and briefly run two overlapping instances).
# `tail --pid` waits on the (non-child) process with no polling/sleep. Exe-path match is caller-safe (see above).
pids=$(pgrep -f "$GAME" || true)
if [ -n "$pids" ]; then
    echo "killing running Silksong (pids: $pids)"
    kill $pids 2>/dev/null || true
    for pid in $pids; do tail --pid="$pid" -f /dev/null; done
    echo "exited"
fi

# 1. Launch detached so the game survives this script exiting. Matches the `start-silksong` fish function
#    (DISPLAY= + mangohud + the r2modman wrapper on the Default profile).
nohup env DISPLAY= mangohud "$WRAPPER" --r2profile Default "$GAME" >/dev/null 2>&1 < /dev/null &
echo "launched Silksong (pid $!)"

# 2. Wait for the DevServer — only listening once BepInEx + the mods finish Initialize, so "routes up" is the
#    boot-done signal. Checkpoint the boot log burst.
echo -n "waiting for DevServer"
for _ in $(seq 1 120); do
    if curl -sf --max-time 1 "$DEV/routes" >/dev/null 2>&1; then echo " up"; break; fi
    echo -n "."
    sleep 1
done
logsnap commit -m "boot" --settle 500ms

# 3. Load the save slot. The route blocks until the save's scene is up (state PLAYING) — so the POST returning is
#    itself the "in gameplay" signal; no separate poll needed. Generous --max-time to cover a slow scene load.
echo "loading save slot $SLOT (blocks until PLAYING) ..."
curl -s --max-time 10 -X POST "$DEV/silksongplayground/load-game?slot=$SLOT" -d ''; echo
logsnap commit -m "loadlevel" --settle 500ms

echo "done — checkpoints: boot, loadlevel   (slogsnap list / slogsnap diff)"
