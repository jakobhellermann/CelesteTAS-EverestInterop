#!/usr/bin/env python3
"""Write a test TAS that resumes one frame of a TAS and asserts Hornet ends where the continuous run puts her.

The test plays the TAS to frame k and saves, optionally plays on to frame `--prior` (the state the resume starts
from), then loads frame k, plays n frames and asserts the hero's position. The expected position comes from running
the continuous TAS to frame k+n in the game (needs the game running).

--query asserts another Assert template instead of the position, e.g. {TinkEffect._nextTinkTime}.

Usage: python3 resume_case.py <file.tas> <k> <n> [--prior P] [--query '{...}'] [-o out.tas]
"""
import argparse
import os
import re
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from resume_check import compact, frame_lines, last_framerate, parse  # noqa: E402
from tas_run import run  # noqa: E402

POSITION = "{HeroController.rb2d.position}"


def continuous_value(setup: list[str], frames, end: int, query: str) -> str:
    """The query's value after frame `end`, formatted like Assert formats it: read from a deliberately failing Assert."""
    probe = setup + frame_lines(frames, 0, end) + [f"Assert, Equal, probe, {query}", "***"]
    with tempfile.NamedTemporaryFile("w", suffix=".tas", delete=False) as f:
        f.write("\n".join(probe) + "\n")
    try:
        outcome = run(f.name, quiet=True)
    finally:
        os.remove(f.name)

    if not (m := re.search(r"But was: (.*)", outcome["error"])):
        raise RuntimeError(f"probe didn't fail its Assert: {outcome}")
    return m.group(1).strip()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("tas")
    ap.add_argument("k", type=int, help="resume after this frame")
    ap.add_argument("n", type=int, help="frames to play after the resume")
    ap.add_argument("--prior", type=int, help="play the continuous run on to this frame before the resume")
    ap.add_argument("--query", default=POSITION, help="Assert template to compare (default: the hero position)")
    ap.add_argument("-o", dest="out", help="output .tas (default: stdout)")
    args = ap.parse_args()

    setup, frames = parse(args.tas)
    end = args.k + args.n
    if end > len(frames) or (args.prior or 0) > len(frames):
        sys.exit(f"{args.tas} has {len(frames)} frames")
    expected = continuous_value(setup, frames, end, args.query)

    lines = [f"# resume after frame {args.k} of {os.path.basename(args.tas)}, {args.n} frames"
             + (f", from the state at frame {args.prior}" if args.prior else "")]
    lines += setup + frame_lines(frames, 0, args.k)
    lines.append("SaveSavestate, {scratch}.resume.json")
    if args.prior and args.prior > args.k:
        lines += frame_lines(frames, args.k, args.prior)

    # Framerate is TAS state, which a load doesn't restore; a TAS starts at InputHelper.DefaultTasFramerate
    framerate = last_framerate(setup, frames, args.k) or "Framerate, 100"
    if framerate != (last_framerate(setup, frames, max(args.prior or 0, args.k)) or "Framerate, 100"):
        lines.append(framerate)
    lines.append("LoadSavestate, {scratch}.resume.json")
    lines += frame_lines(frames, args.k, end)
    lines.append(f"Assert, Equal, {expected.replace(',', chr(92) + ',')}, {args.query}")
    lines.append("***")

    text = "\n".join(compact(lines)) + "\n"
    if args.out:
        open(args.out, "w").write(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
