#!/usr/bin/env python3
"""Run a TAS several times back to back and diff each run's trace against the first.

Each run starts from the previous run's end state, so state that leaks across runs shows up as a divergence. With
--prior, the given TAS files run in turn before each run, so each starts from a different state.

Usage: python3 repeat_check.py <file.tas> [-n N] [--prior a.tas b.tas ...]
"""
import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from resume_check import first_divergence, must_run, running, write_trace  # noqa: E402


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("tas")
    ap.add_argument("-n", type=int, default=4, help="number of runs")
    ap.add_argument("--prior", nargs="*", default=[], help="TAS files to run in turn before each run")
    args = ap.parse_args()

    name = os.path.splitext(os.path.basename(args.tas))[0]
    work = f"/tmp/repeat-check/{name}/{time.strftime('%Y-%m-%d_%H-%M-%S')}"
    os.makedirs(work)
    latest = f"/tmp/repeat-check/{name}/latest"
    if os.path.islink(latest):
        os.remove(latest)
    os.symlink(work, latest)

    traces = []
    for i in range(args.n):
        if args.prior:
            must_run(os.path.abspath(args.prior[i % len(args.prior)]))
        path = f"{work}/run_{i}.json"
        write_trace(path, running(must_run(os.path.abspath(args.tas))))
        traces.append(path)

    failures = 0
    for path in traces[1:]:
        if diff := first_divergence(traces[0], path):
            failures += 1
            print(f"{os.path.basename(path)}: DIVERGES\n  " + diff.replace("\n", "\n  "))
        else:
            print(f"{os.path.basename(path)}: identical")

    print(f"{failures} of {args.n - 1} runs diverge from run_0; traces in {work}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
