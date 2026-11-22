#!/usr/bin/env python3
"""Resume every frame of a TAS and compare against the continuous run.

1. Runs the TAS once plain and once with a `SaveSavestate` after every frame; the two traces must match (saving is
   transparent).
2. For each frame k: a TAS that does `LoadSavestate <k>` and plays the following `--window` frames. The resumes run
   back to back, so each starts from the previous resume's end state rather than a clean one.
3. Diffs each resume's trace against the continuous trace's frames after k (trace-diff) and prints the first
   divergence per k.

The TAS is expanded to single-frame input lines. Commands before k are not replayed on resume, except the last
`Framerate`, which is TAS state rather than game state.

--reload loads every resume with ReloadScene, so the scene reloads even where the savestate could apply in place.

Usage: python3 resume_check.py <file.tas> [--window N] [--every M] [--from K] [--to K] [--reload]
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from tas_run import run  # noqa: E402

MANIFEST = os.path.join(HERE, "Cargo.toml")
BEPINEX_LOG = os.path.expanduser("~/.config/gust/hollow-knight-silksong/default/BepInEx/LogOutput.log")
INPUT_LINE = re.compile(r"^\s*(\d+)\s*(,.*)?$")


def parse(path: str) -> tuple[list[str], list[tuple[list[str], str]]]:
    """→ (setup commands before the first frame, [(commands before this frame, inputs) per single frame])."""
    setup: list[str] = []
    frames: list[tuple[list[str], str]] = []
    pending: list[str] = []
    for raw in open(path).read().splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or line.startswith("***"):
            continue

        if m := INPUT_LINE.match(line):
            for _ in range(int(m.group(1))):
                frames.append((pending, m.group(2) or ""))
                pending = []
            continue

        (pending if frames else setup).append(line)

    if pending:
        frames.append((pending, None))  # trailing commands after the last frame
    return setup, frames


def frame_lines(frames, start: int, end: int) -> list[str]:
    out = []
    for commands, inputs in frames[start:end]:
        out += commands
        if inputs is not None:
            out.append(f"   1{inputs}")
    return out


def compact(lines: list[str]) -> list[str]:
    """Merges consecutive input lines with the same inputs back into one, as a TAS is written."""
    out: list[str] = []
    for line in lines:
        if (m := INPUT_LINE.match(line.strip())) and out and (prev := INPUT_LINE.match(out[-1].strip())) \
                and prev.group(2) == m.group(2):
            out[-1] = f"{int(prev.group(1)) + int(m.group(1)):4}{m.group(2) or ''}"
        else:
            out.append(line)
    return out


def last_framerate(setup, frames, k: int) -> str | None:
    found = None
    for line in setup + [c for commands, _ in frames[:k] for c in commands]:
        if line.lower().startswith("framerate"):
            found = line
    return found


def comparable(entry: dict) -> dict:
    """Drop what isn't game state: frame-history events (savestate saves/loads record their own) and the input line's
    text (compacting a TAS merges lines)."""
    entry = {k: v for k, v in entry.items() if k != "InputLine"}
    if "FrameHistory" not in entry:
        return entry
    stages = [{"Stage": s["Stage"], "Vars": s["Vars"]} for s in entry["FrameHistory"] if s.get("Vars")]
    return {**entry, "FrameHistory": stages}


def running(trace_path: str) -> list[dict]:
    return [comparable(e) for e in json.load(open(trace_path))["Trace"] if e.get("State") == "Running"]


def write_trace(path: str, entries: list[dict]) -> None:
    json.dump({"FilePath": path, "Trace": entries, "Checksum": 0}, open(path, "w"))


def first_divergence(a: str, b: str) -> str | None:
    out = subprocess.run(["cargo", "run", "-q", "--manifest-path", MANIFEST, "--", "--color=never", a, b],
                         capture_output=True, text=True).stdout
    if "identical" in out:
        return None
    lines = out.splitlines()
    start = next((i for i, l in enumerate(lines) if "first divergence" in l), None)
    if start is None:
        return out.strip()
    return "\n".join(lines[start:start + 7])


def load_modes(log_start: int) -> dict[str, str]:
    """TAS file → how its savestate load went ("in place" / "scene reload"), from the BepInEx log after log_start."""
    modes, current = {}, None
    with open(BEPINEX_LOG, errors="replace") as log:
        log.seek(log_start)
        for line in log:
            if m := re.search(r"Starting TAS: (\S+)", line):
                current = m.group(1)
            elif current and "applying in place" in line:
                modes.setdefault(current, "in place")
            elif current and ("loading the scene" in line or "Loaded scene in" in line):
                modes.setdefault(current, "scene reload")
    return modes


TIMINGS: dict[str, list[float]] = {}


def timed(label: str, fn, *args):
    start = time.monotonic()
    try:
        return fn(*args)
    finally:
        TIMINGS.setdefault(label, []).append(time.monotonic() - start)


def must_run(path: str) -> str:
    outcome = run(path, quiet=True)
    if not outcome["ok"]:
        # /tmp/TAS-Traces is cleared on the next TasTools reload
        kept = shutil.copy(outcome["trace"], os.path.splitext(path)[0] + ".failed.json") if outcome["trace"] else "no trace"
        raise RuntimeError(f"{path}: {outcome['error']} (trace: {kept})")
    return outcome["trace"]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("tas")
    ap.add_argument("--window", type=int, default=30, help="frames to play after each resume")
    ap.add_argument("--every", type=int, default=1, help="resume every M-th frame")
    ap.add_argument("--from", dest="start", type=int, default=1)
    ap.add_argument("--to", dest="end", type=int, default=None)
    ap.add_argument("--reload", action="store_true", help="load every resume with ReloadScene")
    args = ap.parse_args()

    name = os.path.splitext(os.path.basename(args.tas))[0]
    work = f"/tmp/resume-check/{name}/{time.strftime('%Y-%m-%d_%H-%M-%S')}"
    os.makedirs(work)
    latest = f"/tmp/resume-check/{name}/latest"
    if os.path.islink(latest):
        os.remove(latest)
    os.symlink(work, latest)

    setup, frames = parse(args.tas)
    n = sum(1 for _, inputs in frames if inputs is not None)
    print(f"{args.tas}: {n} frames, setup: {setup}")

    plain = f"{work}/plain.tas"
    # A trailing breakpoint fast-forwards the run instead of playing it in real time
    open(plain, "w").write("\n".join(compact(setup + frame_lines(frames, 0, len(frames)) + ["***"])) + "\n")
    saving = f"{work}/saving.tas"
    lines = list(setup)
    for k in range(len(frames)):
        lines += frame_lines(frames, k, k + 1)
        if frames[k][1] is not None:
            lines.append(f"SaveSavestate, {work}/{k + 1}.json")
    open(saving, "w").write("\n".join(compact(lines + ["***"])) + "\n")

    log_start = os.path.getsize(BEPINEX_LOG)
    write_trace(f"{work}/plain.json", running(timed("plain run", must_run, plain)))
    saving_frames = running(timed("saving run", must_run, saving))
    write_trace(f"{work}/saving.json", saving_frames)
    saving_diff = first_divergence(f"{work}/plain.json", f"{work}/saving.json")
    continuous = {e["Frame"]: e for e in saving_frames}

    end = min(args.end or n, n - 1)
    failures = 0
    results = []
    for k in range(args.start, end + 1, args.every):
        resume = f"{work}/resume_{k}.tas"
        body = [f"LoadSavestate, {work}/{k}.json" + (", ReloadScene" if args.reload else "")]
        if framerate := last_framerate(setup, frames, k):
            body.insert(0, framerate)
        body += frame_lines(frames, k, min(k + args.window, len(frames)))
        open(resume, "w").write("\n".join(compact(body + ["***"])) + "\n")

        outcome = timed("resume run", run, resume, True)
        if not outcome["ok"] and not outcome["trace"]:
            raise RuntimeError(f"{resume}: {outcome['error']}")
        resumed = running(outcome["trace"])
        expected = []
        for e in resumed:
            if (c := continuous.get(e["Frame"] + k)) is None:
                raise RuntimeError(f"continuous trace has no frame {e['Frame'] + k}")
            expected.append({**c, "Frame": e["Frame"]})
        write_trace(f"{work}/expected_{k}.json", expected)
        write_trace(f"{work}/resumed_{k}.json", resumed)

        diff = timed("diff", first_divergence, f"{work}/expected_{k}.json", f"{work}/resumed_{k}.json")
        if not outcome["ok"]:
            diff = f"{outcome['error']}\n{diff or 'trace identical up to the failure'}"
        results.append((k, resume, diff, len(resumed)))

    # BepInEx writes its log buffered, so the load modes are only complete after all runs
    must_run(plain)
    modes = load_modes(log_start)
    if saving_diff:
        print(f"SAVING IS NOT TRANSPARENT (plain run: {modes.get(plain, 'load mode not logged')}, "
              f"saving run: {modes.get(saving, 'load mode not logged')}):\n{saving_diff}")
    for k, resume, diff, frames in results:
        mode = modes.get(resume, "load mode not logged")
        if diff:
            failures += 1
            print(f"k={k} ({mode}): DIVERGES\n  " + diff.replace("\n", "\n  "))
        else:
            print(f"k={k} ({mode}): ok ({frames} frames)")

    by_mode = {}
    for k, resume, diff, _ in results:
        by_mode.setdefault(modes.get(resume, "load mode not logged"), [0, 0])[bool(diff)] += 1
    print("timings: " + ", ".join(
        f"{label} {sum(t):.1f}s" + (f" ({len(t)}x, avg {sum(t) / len(t):.2f}s, max {max(t):.2f}s)" if len(t) > 1 else "")
        for label, t in TIMINGS.items()))
    print(f"{failures} of {len(results)} resumes diverge; " +
          ", ".join(f"{mode}: {ok} ok, {bad} diverge" for mode, (ok, bad) in by_mode.items()) + f"; files in {work}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
