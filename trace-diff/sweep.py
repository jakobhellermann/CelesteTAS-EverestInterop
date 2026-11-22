#!/usr/bin/env python3
"""Run a TAS once per value of a `$NAME` placeholder in it and report pass/fail (asserts) per value.

Usage: python3 sweep.py <file.tas> NAME=v1,v2,... | NAME=start:stop:step
"""
import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from tas_run import run  # noqa: E402


def values(spec: str) -> list[str]:
    if ":" in spec:
        start, stop, step = (int(x) for x in spec.split(":"))
        return [str(v) for v in range(start, stop + 1, step)]
    return spec.split(",")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("tas")
    ap.add_argument("var", help="NAME=v1,v2,... or NAME=start:stop:step (inclusive)")
    args = ap.parse_args()

    name, spec = args.var.split("=", 1)
    template = open(args.tas).read()
    if f"${name}" not in template:
        sys.exit(f"{args.tas} has no ${name}")

    work = f"/tmp/sweep/{os.path.splitext(os.path.basename(args.tas))[0]}/{time.strftime('%Y-%m-%d_%H-%M-%S')}"
    os.makedirs(work)

    failures = 0
    for value in values(spec):
        path = f"{work}/{name}_{value}.tas"
        # A trailing breakpoint fast-forwards the run instead of playing it in real time
        open(path, "w").write(template.replace(f"${name}", value).rstrip("\n") + "\n***\n")
        start = time.monotonic()
        outcome = run(path, quiet=True)
        took = time.monotonic() - start
        if outcome["ok"]:
            print(f"{name}={value}: PASS ({took:.1f}s)")
        else:
            failures += 1
            print(f"{name}={value}: FAIL ({took:.1f}s): {outcome['error']} trace={outcome['trace']}")

    print(f"{failures} of {len(values(spec))} fail; files in {work}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
