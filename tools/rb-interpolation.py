#!/usr/bin/env python3
"""Which Rigidbody2Ds in Silksong's scenes interpolate, and which of them belong to enemies (a HealthManager on the
same GameObject). Reads the serialized scenes with rabex, so components added at runtime and prefabs spawned at
runtime are not included.

Usage: tools/rb-interpolation.py [scene ...]   (default: every scene)
"""
import json
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

RABEX = ["rabex", "--steam-game", "silksong"]
INTERPOLATION = {0: "None", 1: "Interpolate", 2: "Extrapolate"}
BODY_TYPE = {0: "Dynamic", 1: "Kinematic", 2: "Static"}


def rabex_json(*args: str):
    out = subprocess.run([*RABEX, *args, "--format", "json"], capture_output=True, text=True, check=True).stdout
    return json.loads(out)


def scene_bodies(scene: str) -> list[dict]:
    bodies = rabex_json("scene", scene, "find", "Rigidbody2D")
    enemies = {hm["gameobject_path_id"] for hm in rabex_json("scene", scene, "find", "HealthManager")}
    result = []
    for body in bodies:
        data = json.loads(subprocess.run([*RABEX, "scene", scene, "object", str(body["component_path_id"]), "cat"],
                                         capture_output=True, text=True, check=True).stdout)
        result.append({
            "scene": scene,
            "path": body["path"],
            "interpolation": INTERPOLATION[data["m_Interpolate"]],
            "bodyType": BODY_TYPE[data["m_BodyType"]],
            "enemy": body["gameobject_path_id"] in enemies,
        })
    return result


def main() -> None:
    scenes = sys.argv[1:] or [s["name"] for s in rabex_json("scenes")]
    with ThreadPoolExecutor(max_workers=16) as pool:
        bodies = [b for scene in pool.map(scene_bodies, scenes) for b in scene]

    for b in bodies:
        if b["interpolation"] != "None":
            print(f"{b['scene']}: {b['path']} {b['interpolation']} {b['bodyType']}{' enemy' if b['enemy'] else ''}")

    enemies = [b for b in bodies if b["enemy"]]
    print(f"\n{len(scenes)} scenes, {len(bodies)} Rigidbody2D, "
          f"{sum(b['interpolation'] != 'None' for b in bodies)} interpolated; "
          f"{len(enemies)} on enemies, {sum(b['interpolation'] != 'None' for b in enemies)} of them interpolated")


if __name__ == "__main__":
    main()
