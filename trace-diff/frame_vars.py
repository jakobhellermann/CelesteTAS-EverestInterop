#!/usr/bin/env python3
"""Print Pos/Vel and the per-phase trace vars of chosen frames of a trace.

Usage: python3 frame_vars.py <trace.json> <frame> [<frame> ...] [--stage FirstUpdate]
"""
import argparse
import json
import re


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("frames", type=int, nargs="+")
    ap.add_argument("--stage", default=None, help="only this player-loop stage")
    args = ap.parse_args()

    for entry in json.load(open(args.trace))["Trace"]:
        if entry.get("State") != "Running" or entry["Frame"] not in args.frames:
            continue

        info = entry.get("Info", "")
        pos = re.search(r"Pos:\s+\(([^)]*)\)", info)
        vel = re.search(r"Vel:\s+\(([^)]*)\)", info)
        print(f"frame {entry['Frame']}  pos=({pos.group(1) if pos else '?'})  vel=({vel.group(1) if vel else '?'})  "
              f"tft={entry.get('TftTicks')}")
        for stage in entry.get("FrameHistory", []):
            if stage.get("Vars") and (args.stage is None or stage["Stage"] == args.stage):
                print(f"    {stage['Stage']:<16} {stage['Vars']}")


if __name__ == "__main__":
    main()
