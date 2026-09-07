#!/usr/bin/env python3
"""Regenerate the embedded scene/transition-gate catalog used by the `load` and
`LoadTransition` command auto-completes.

Sources (offline, via rabex):
  - `rabex scenes` for the full list of loadable scene names.
  - every `TransitionPoint` reference in the monoscripts bundle for the entry
    gates per scene. A gate's name is the short GameObject name (last path
    segment) because that is what `GameManager.FindTransitionPoint` matches on.

Output: CelesteTAS-EverestInterop/Assets/scene-catalog.json
  { "scenes": [...], "gates": { "<scene>": ["<gate>", ...] } }

Run from the repo root (or anywhere; paths are resolved relative to this file):
  python3 tools/gen-scene-catalog.py
Requires `rabex` on PATH and the Silksong install locatable via --steam-game.
"""
import json
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

STEAM_GAME = "silksong"
MONOSCRIPTS_BUNDLE = "94696d22b6ed0a74097d1bd58feb4dce_monoscripts.bundle"
OUT = Path(__file__).resolve().parent.parent / "CelesteTAS-EverestInterop" / "Assets" / "scene-catalog.json"


def rabex_json(*args):
    cmd = ["rabex", "--format", "json", "--steam-game", STEAM_GAME, *args]
    out = subprocess.run(cmd, check=True, capture_output=True, text=True).stdout
    return json.loads(out)


def main():
    scenes = rabex_json("scenes")
    refs = rabex_json("bundle", MONOSCRIPTS_BUNDLE, "file", "object", "TransitionPoint", "references")

    scene_names = sorted({s["name"] for s in scenes})

    gates = defaultdict(set)
    for r in refs["referrers"]:
        # label is a component path like "Inspect Door/door1@TransitionPoint";
        # the entry-gate name is the GameObject name = last segment before '@'.
        name = r["label"].split("@")[0].split("/")[-1]
        gates[r["scene"]].add(name)

    unknown = sorted(set(gates) - set(scene_names))
    if unknown:
        print(f"warning: {len(unknown)} gate-scenes not in scene list: {unknown[:10]}", file=sys.stderr)

    catalog = {
        "scenes": scene_names,
        "gates": {scene: sorted(gates[scene]) for scene in sorted(gates)},
    }

    OUT.write_text(json.dumps(catalog, indent=0, separators=(",", ":")) + "\n")
    print(f"wrote {OUT} ({len(scene_names)} scenes, {sum(len(v) for v in gates.values())} gates)")


if __name__ == "__main__":
    main()
