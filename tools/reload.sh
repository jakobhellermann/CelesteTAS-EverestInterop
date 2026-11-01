#!/usr/bin/env bash
# Hot-reload a mod into the running game and block until AutoReload has loaded it; `logsnap diff --in <label>` is
# then exactly the reload's log burst.
# Usage: tools/reload.sh [project.csproj] [PluginName] [checkpoint-label]
#        defaults: CelesteTAS-EverestInterop/CelesteTAS-EverestInterop.csproj TasTools reload
set -euo pipefail

project=${1:-CelesteTAS-EverestInterop/CelesteTAS-EverestInterop.csproj}
plugin=${2:-TasTools}
label=${3:-reload}
export LOGSNAP_STATE=${LOGSNAP_STATE:-$HOME/.local/state/logsnap/silksong}

# AutoReload processes its queue in a MonoBehaviour Update, which a paused TAS removes from the PlayerLoop
curl -sf -m 5 -X POST -d '' "${DEVSERVER:-http://localhost:8200}/tastools/stop" >/dev/null \
    || echo "warning: couldn't stop the TAS (DevServer down or TasTools not loaded)" >&2

logsnap commit -m "pre-reload" >/dev/null

log=$(mktemp)
marker=$(mktemp)
trap 'rm -f "$log" "$marker"' EXIT

echo "building $project..."
if ! dotnet build "$project" -c Debug >"$log" 2>&1; then
    echo "build FAILED:" >&2
    grep -E " error " "$log" | sort -u >&2 || tail -20 "$log" >&2
    exit 1
fi

output=$(grep -oE -- "-> .*\.dll$" "$log" | tail -1 | cut -c4-)
if [ -z "$output" ]; then
    echo "build output names no dll" >&2
    exit 1
fi
# An up-to-date build doesn't rewrite the dll, so nothing gets deployed and no reload will come
if [ ! "$output" -nt "$marker" ]; then
    echo "$(basename "$output") is up to date, nothing to reload"
    exit 0
fi

echo "build ok, waiting for AutoReload to load $plugin"
logsnap commit -m "$label" --wait-for "AutoReload] Loading [$plugin " --settle 200ms --at-most 15s
