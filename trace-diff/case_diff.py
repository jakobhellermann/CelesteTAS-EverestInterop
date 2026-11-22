#!/usr/bin/env python3
"""Run a resume test written by resume_case.py and diff its resumed frames against the continuous frames of the same run.

The test plays frames 1..P continuously (saving after frame k), loads frame k and plays n frames. Resumed frame P+i is
compared with continuous frame k+i.

With --probe, it instead stops both paths after continuous frame k+i and resumed frame i and runs the C# file there
through /devutils/eval-cs.

Usage: python3 case_diff.py <test.tas> [trace-diff flags, e.g. --all]
       python3 case_diff.py <test.tas> --probe <file.cs> --at <i>
"""
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from resume_check import INPUT_LINE, MANIFEST, comparable, write_trace  # noqa: E402
from tas_run import eval_cs, run  # noqa: E402


def frames_before(lines: list[str], prefix: str) -> int:
    frames = 0
    for line in lines:
        if line.strip().startswith(prefix):
            return frames
        if m := INPUT_LINE.match(line.strip()):
            frames += int(m.group(1))
    raise ValueError(f"no '{prefix}' line")


def cut_after(lines: list[str], frames: int, start: int = 0) -> list[str]:
    """lines[:start] plus the lines from start until `frames` more frames are played, then a breakpoint"""
    out, played = lines[:start], 0
    for line in lines[start:]:
        if m := INPUT_LINE.match(line.strip()):
            if played + int(m.group(1)) > frames:
                if frames > played:
                    out.append(f"{frames - played:4}{m.group(2) or ''}")
                break
            played += int(m.group(1))
        out.append(line)
    # one more frame after the breakpoint, so the run pauses there instead of completing
    return out + ["***", "   1"]


def probe(work: str, lines: list[str], k: int, at: int, source: str) -> None:
    load = next(i for i, l in enumerate(lines) if l.startswith(f"LoadSavestate, {work}"))
    continuous = cut_after(lines[:load], k + at)
    resumed = cut_after(lines, at, start=load + 1)
    for label, variant in (("continuous", continuous), ("resumed", resumed)):
        path = f"{work}/probe_{label}.tas"
        open(path, "w").write("\n".join(l for l in variant if not l.startswith("Assert")) + "\n")
        results = []
        run(path, quiet=True, at_pause=lambda frame: results.append(eval_cs(source)))
        if len(results) != 1:
            raise RuntimeError(f"{label}: expected one pause, got {len(results)}")
        result = results[0]
        print(f"{label}: {result.get('result') if 'result' in result else result}")


def main() -> None:
    path = os.path.abspath(sys.argv[1])
    name = os.path.splitext(os.path.basename(path))[0]
    work = f"/tmp/case-diff/{name}/{time.strftime('%Y-%m-%d_%H-%M-%S')}"
    os.makedirs(work)

    body = open(path).read().replace("{tests}", os.path.dirname(path)).replace("{scratch}", f"{work}/{name}")
    resolved = f"{work}/{name}.tas"
    open(resolved, "w").write(body)

    lines = body.splitlines()
    k = frames_before(lines, "SaveSavestate")
    prior = frames_before(lines, f"LoadSavestate, {work}")
    if "--probe" in sys.argv:
        source = open(sys.argv[sys.argv.index("--probe") + 1]).read()
        at = int(sys.argv[sys.argv.index("--at") + 1])
        print(f"k={k} prior={prior}, after continuous frame {k + at} / resumed frame {at}")
        probe(work, lines, k, at, source)
        return
    outcome = run(resolved, quiet=True)
    print(f"k={k} prior={prior}: {'PASS' if outcome['ok'] else outcome['error'].splitlines()[-1]}")
    if not outcome["trace"]:
        sys.exit(f"no trace: {outcome}")

    by_frame = {e["Frame"]: comparable(e) for e in json.load(open(outcome["trace"]))["Trace"] if e.get("State") == "Running"}
    n = max(by_frame) - prior
    write_trace(f"{work}/continuous.json", [{**by_frame[k + i], "Frame": i} for i in range(1, n + 1)])
    write_trace(f"{work}/resumed.json", [{**by_frame[prior + i], "Frame": i} for i in range(1, n + 1)])
    subprocess.run(["cargo", "run", "-q", "--manifest-path", MANIFEST, "--", "--color=never", *sys.argv[2:],
                    f"{work}/continuous.json", f"{work}/resumed.json"])
    print(f"files in {work}")


if __name__ == "__main__":
    main()
