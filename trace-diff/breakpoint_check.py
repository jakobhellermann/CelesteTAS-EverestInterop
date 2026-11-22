#!/usr/bin/env python3
"""Resume a TAS from a ***S savestate breakpoint at every frame and compare against the run that created it.

For each frame k: clear the breakpoint savestates, write the TAS with ***S after frame k and a trailing ***, run it
once (it passes ***S, saves there and plays on: the continuous run) and again (it resumes from the savestate at k),
and diff the second run's frames after k against the first's.

Usage: python3 breakpoint_check.py <file.tas> [--window N] [--every M] [--from K] [--to K]
"""
import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from resume_check import compact, first_divergence, frame_lines, must_run, parse, running, timed, TIMINGS, write_trace  # noqa: E402
from tas_run import call  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("tas")
    ap.add_argument("--window", type=int, default=30, help="frames to play after each resume")
    ap.add_argument("--every", type=int, default=1, help="resume every M-th frame")
    ap.add_argument("--from", dest="start", type=int, default=1)
    ap.add_argument("--to", dest="end", type=int, default=None)
    args = ap.parse_args()

    name = os.path.splitext(os.path.basename(args.tas))[0]
    work = f"/tmp/breakpoint-check/{name}/{time.strftime('%Y-%m-%d_%H-%M-%S')}"
    os.makedirs(work)
    latest = f"/tmp/breakpoint-check/{name}/latest"
    if os.path.islink(latest):
        os.remove(latest)
    os.symlink(work, latest)

    setup, frames = parse(args.tas)
    n = sum(1 for _, inputs in frames if inputs is not None)
    end = min(args.end or n, n - 1)
    failures = 0
    for k in range(args.start, end + 1, args.every):
        path = f"{work}/k{k}.tas"
        body = setup + frame_lines(frames, 0, k) + ["***S"] + frame_lines(frames, k, min(k + args.window, len(frames))) + ["***"]
        open(path, "w").write("\n".join(compact(body)) + "\n")

        call("/clearstates")
        created = [e for e in running(timed("creating run", must_run, path)) if e["Frame"] > k]
        resumed = [e for e in running(timed("resumed run", must_run, path)) if e["Frame"] > k]
        write_trace(f"{work}/created_{k}.json", created)
        write_trace(f"{work}/resumed_{k}.json", resumed)

        diff = timed("diff", first_divergence, f"{work}/created_{k}.json", f"{work}/resumed_{k}.json")
        if not resumed:
            diff = "the second run didn't resume past the breakpoint"
        if diff:
            failures += 1
            print(f"k={k}: DIVERGES\n  " + diff.replace("\n", "\n  "), flush=True)
        else:
            print(f"k={k}: ok ({len(resumed)} frames)", flush=True)

    print("timings: " + ", ".join(
        f"{label} {sum(t):.1f}s" + (f" ({len(t)}x, avg {sum(t) / len(t):.2f}s)" if len(t) > 1 else "")
        for label, t in TIMINGS.items()))
    count = len(range(args.start, end + 1, args.every))
    print(f"{failures} of {count} breakpoint resumes diverge; files in {work}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
