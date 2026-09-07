#!/usr/bin/env python3
"""Determinism test runner.

Discovers `tests/*.tas`, runs each against the running game (DevServer + tracer enabled), and reports pass/fail
with per-test timing. Add a test by dropping a `.tas` file — no runner change.

A test is configured by header lines — `#! key=value` / `#! each var=v1,v2` — each a `#` TAS comment the engine
ignores while the runner reads it. In the body: `{scratch}` is replaced with a per-case /tmp path prefix (for
savestate files), `{tests}` with the tests/ directory (for checked-in fixtures like savestate JSONs), and `{var}`
with the current value of an `each` variable.

Modes (`#! Mode=...`; the default `assert` is omitted):

  assert         Run the TAS; passes iff it completes without aborting — an assert command (e.g.
                 AssertEqualSavestates) surfaces its failure as an abort. Single run, fast: the default for most
                 tests and regressions.

  compareTraces  Run every combination of the `each` variables and assert their traces are identical — i.e. the
                 result is invariant under those variables. The compared portion must be wrapped in
                 `BeginTrace, scenario` / `EndTrace` (so setup frames outside it don't count); the segments are
                 aligned by position (trace-diff --relative). Used for prior-independence, e.g.:
                     #! Mode=compareTraces
                     #! each prior=idle,airborne
                     Read, priors/{prior}.tas
                     BeginTrace, scenario
                     load Tut_01 47.73 4.57
                        5,R
                     EndTrace

Usage: python3 test_runner.py [<name> ...]   (game running with the DevServer; tracer enabled)
"""
import glob
import itertools
import os
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from tas_run import run, call, state_of  # noqa: E402  reuse the DevServer driver

MANIFEST = os.path.join(HERE, "Cargo.toml")
TESTS_DIR = os.path.join(HERE, "tests")
SCRATCH = "/tmp/corpus"
TRACE_ROOT = "/tmp/TAS-Traces"
SCENARIO_SEGMENT = "scenario"  # compareTraces tests wrap the compared portion in `BeginTrace, scenario` / `EndTrace`


def parse_header(body: str) -> tuple[str, dict[str, list[str]], str | None]:
    """Read the leading `#! ...` directive lines → (mode, {each-var: [values]}, xfail-reason). Plain comments skipped.

    `#! xfail <reason>` marks a known-failing test: a failure is reported as XFAIL and does not fail the suite, but an
    unexpected pass is reported as XPASS and does (so a fix is noticed and the marker removed)."""
    mode = "assert"
    eachvars: dict[str, list[str]] = {}
    xfail: str | None = None
    for line in body.splitlines():
        s = line.strip()
        if not s or (s.startswith("#") and not s.startswith("#!")):
            continue  # blank line or plain comment
        if not s.startswith("#!"):
            break  # first TAS line — header is over
        directive = s[2:].strip()
        if directive.lower().startswith("each "):
            var, _, values = directive[5:].partition("=")
            eachvars[var.strip()] = [v.strip() for v in values.split(",") if v.strip()]
        elif directive.lower().startswith("mode="):
            mode = directive.partition("=")[2].strip()
        elif directive.lower().startswith("xfail"):
            xfail = directive[5:].strip() or "known failure"
    return mode, eachvars, xfail


def expand(eachvars: dict[str, list[str]]) -> list[dict[str, str]]:
    """Cross-product of the each-variables → one dict per case (a single empty case if there are none)."""
    if not eachvars:
        return [{}]
    keys = list(eachvars)
    return [dict(zip(keys, combo)) for combo in itertools.product(*(eachvars[k] for k in keys))]


def write(path: str, body: str) -> str:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(body)
    return path


def sync_priors() -> None:
    """Copy tests/priors/*.tas next to the generated scenarios so `Read, priors/<x>.tas` resolves."""
    dst = os.path.join(SCRATCH, "priors")
    os.makedirs(dst, exist_ok=True)
    for f in glob.glob(os.path.join(TESTS_DIR, "priors", "*.tas")):
        shutil.copy(f, dst)


def newest_trace(name: str, since: float | None = None) -> str:
    files = glob.glob(os.path.join(TRACE_ROOT, name, "20*.json"))
    if not files:
        raise RuntimeError(f"no trace produced for '{name}' in {TRACE_ROOT}/{name}")
    newest = max(files, key=os.path.getmtime)
    # Freshness guard: if the newest trace predates this run, the run produced none (test missing, wedge, crash) —
    # fail loudly instead of silently reading a stale trace from an earlier run.
    if since is not None and os.path.getmtime(newest) < since:
        raise RuntimeError(f"no fresh trace for '{name}': newest ({newest}) is stale (from before this run)")
    return newest


def trace_diff(a: str, b: str, *flags: str) -> tuple[bool, str]:
    proc = subprocess.run(
        ["cargo", "run", "--quiet", "--manifest-path", MANIFEST, "--", *flags, a, b],
        capture_output=True, text=True,
    )
    return proc.returncode == 0, (proc.stdout + proc.stderr).strip()


def run_assert_case(path: str) -> tuple[bool, str, str | None]:
    """Run a TAS; passes iff it reaches completion without an abort. A trailing `***` breakpoint (used to
    fast-forward the whole run for speed) pauses playback just short of completion — the asserts still execute
    during the fast-forward and a failure surfaces as an abort, so drive /continue past each pause to the end.
    Returns (ok, detail, trace_path) — the trace the run actually wrote, so callers report the exact file instead
    of guessing at latest/."""
    call("/stop", timeout=10)
    run_start = time.time()
    resp = call(f"/run?path={urllib.parse.quote(path)}", timeout=180)

    # Continue past each breakpoint pause until the run completes or aborts. Bounded so a stuck resume can't loop.
    guard = 0
    while (not resp.get("error") and not resp.get("abort") and not resp.get("completed")
           and state_of(resp) == "Paused" and guard < 60):
        guard += 1
        resp = call("/continue", timeout=180)

    call("/stop", timeout=10)  # leave the game unpaused so hot-reload keeps working
    trace = resp.get("tracePath")
    # An abort (failed assert) leaves tracePath unset, but the tracer still wrote a partial trace up to the aborting
    # frame — grab it from disk so a failure is immediately diffable (against a known-good run) instead of re-running
    # to try to catch it again. Freshness-guarded so a stale trace from an earlier run is never mistaken for this one.
    if not trace:
        try:
            trace = newest_trace(os.path.splitext(os.path.basename(path))[0], since=run_start)
        except RuntimeError:
            trace = None
    if resp.get("error"):
        return False, f"run error: {resp['error']}", trace
    if resp.get("abort"):
        return False, resp["abort"], trace
    if not resp.get("completed"):
        return False, f"did not complete: state={resp.get('state')}", trace
    return True, "", trace


def run_test(name: str, body: str, mode: str, eachvars: dict) -> tuple[bool, str, list[tuple[str, str]]]:
    sync_priors()
    cases = expand(eachvars)
    segments = {}
    traces: list[tuple[str, str]] = []  # (label, trace path) — reported so nobody has to guess at latest/
    for combo in cases:
        label = ".".join(combo.values()) or "default"
        resolved = body
        for var, val in combo.items():
            resolved = resolved.replace("{" + var + "}", val)
        resolved = resolved.replace("{scratch}", os.path.join(SCRATCH, f"{name}.{label}"))
        resolved = resolved.replace("{tests}", TESTS_DIR)  # checked-in fixtures (e.g. savestate JSONs)
        path = write(os.path.join(SCRATCH, f"{name}.{label}.tas"), resolved)

        t = time.perf_counter()
        if mode == "compareTraces":
            run_start = time.time()  # wall-clock, to match trace file mtimes for the freshness guard
            run(path, quiet=True)
            segments[label] = shutil.copy(newest_trace(SCENARIO_SEGMENT, since=run_start),
                                          os.path.join(SCRATCH, f"{name}.trace.{label}.json"))
            traces.append((label, segments[label]))
        else:  # assert
            ok, detail, trace = run_assert_case(path)
            if trace:
                traces.append((label, trace))
            if not ok:
                return False, (f"[{label}] {detail}" if len(cases) > 1 else detail), traces
        if len(cases) > 1:
            print(f"      {label:<16} {time.perf_counter() - t:5.1f}s", flush=True)

    if mode == "compareTraces":
        # --relative: each case's segment sits at different absolute frame numbers (differing setup lengths), so
        # align by position within the segment, not by absolute frame.
        ref_label, ref = next(iter(segments.items()))
        for label, other in list(segments.items())[1:]:
            ok, detail = trace_diff(ref, other, "--relative")
            if not ok:
                return False, f"'{ref_label}' vs '{label}':\n{detail}", traces
    return True, "", traces


def settle_gc() -> None:
    """Force a GC + finalizer pass between tests (GC.Collect + WaitForPendingFinalizers). Observed, not fully
    explained: the full suite run back-to-back segfaults in the engine's GC (GC_invoke_finalizers), while each test
    is stable in isolation and the suite survives if this runs between tests. Likely the many heterogeneous scene
    reloads pile up faster than the GC drains, but the exact cause isn't confirmed — this just reliably avoids the
    crash. Best-effort (needs SilksongPlayground)."""
    try:
        req = urllib.request.Request("http://localhost:8200/silksongplayground/gc", data=b"", method="POST")
        urllib.request.urlopen(req, timeout=20).read()
    except Exception:
        pass


def main() -> None:
    only = set(sys.argv[1:])  # optional filter: test_runner.py <name> [<name> ...]
    tests = sorted(glob.glob(os.path.join(TESTS_DIR, "*.tas")))

    results = {}
    started = time.perf_counter()
    for path in tests:
        name = os.path.splitext(os.path.basename(path))[0]
        if only and name not in only:
            continue
        body = open(path).read()
        mode, eachvars, xfail = parse_header(body)
        label = mode + ("  xfail" if xfail else "") + "".join(f"  {v}={','.join(vals)}" for v, vals in eachvars.items())
        print(f"▶ {name}  ({label})", flush=True)

        t0 = time.perf_counter()
        ok, detail, traces = run_test(name, body, mode, eachvars)
        if xfail:
            # XFAIL (failed as expected) doesn't fail the suite; XPASS (unexpectedly passed) does, so a fix is noticed.
            status, results[name] = ("XPASS", False) if ok else ("XFAIL", True)
        else:
            status, results[name] = ("PASS" if ok else "FAIL"), ok
        print(f"  {status}  {name}  ({time.perf_counter() - t0:.1f}s)"
              + (f"  [{xfail}]" if xfail else ""), flush=True)
        for tlabel, tpath in traces:
            tag = f" [{tlabel}]" if len(traces) > 1 else ""
            print(f"        trace{tag}: {tpath}")
        if not ok and not xfail:
            for line in detail.splitlines():
                print(f"        {line}")

        settle_gc()  # the unpaced back-to-back suite otherwise segfaults in the engine GC (see settle_gc)

    if not results:
        print(f"no tests matched {sorted(only)}" if only else f"no tests found in {TESTS_DIR}")
        sys.exit(2)

    passed = sum(results.values())
    print(f"\n{passed}/{len(results)} passed  ({time.perf_counter() - started:.1f}s total)")
    sys.exit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
