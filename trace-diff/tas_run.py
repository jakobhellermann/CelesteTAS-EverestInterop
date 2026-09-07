#!/usr/bin/env python3
"""Drive a TAS run headless through breakpoint pauses to completion, print the trace path.

Usage: tas_run.py <abs .tas path>
Relies on the DevServer routes /tastools/{run,continue,state,stop} (DevServer must be pumped
from its PlayerLoop system so /continue is deliverable while a TAS is paused at a breakpoint).
"""
import json
import sys
import urllib.parse
import urllib.request

BASE = "http://localhost:8200/tastools"


def call(path, method="POST", timeout=120):
    req = urllib.request.Request(f"{BASE}{path}", data=b"" if method == "POST" else None, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        try:
            return json.loads(e.read().decode())
        except Exception:
            return {"error": str(e), "status": e.code}


def state_of(resp):
    s = resp.get("state")
    return s if isinstance(s, str) else (s or {}).get("state")


def frame_of(resp):
    s = resp.get("state")
    return resp.get("currentFrame") if isinstance(s, str) else (s or {}).get("currentFrame")


def run(path, quiet=False):
    """Drive a TAS to completion and return the outcome as a dict: {ok, trace, error, completed, state}.

    `ok` is True only if the run reached completion without an abort — an assert failure (or a failed savestate
    command) surfaces as an `error`/`abort` field on the DevServer response, which the previous version silently
    dropped by returning only the trace path. Mirrors test_runner.run_assert_case's checks so ad-hoc runs report
    pass/fail the same way the suite does, instead of leaving the caller to guess from the trace alone."""
    log = (lambda *a: None) if quiet else print
    call("/stop", timeout=10)
    # /run and /continue block until the run next stops advancing (pause/breakpoint/completion) and return `completed`
    # plus the final state, so drive off their responses directly — no /state polling or settle sleeps needed.
    resp = call(f"/run?path={urllib.parse.quote(path)}", timeout=120)
    log(f"  run: {state_of(resp)} frame={frame_of(resp)} complete={resp.get('completed')} trace={resp.get('tracePath')}")
    last_trace = resp.get("tracePath")

    # Continue past each breakpoint pause, until the run completes or aborts (error/abort). Bounded so a stuck
    # resume can't loop forever.
    guard = 0
    while (not resp.get("error") and not resp.get("abort") and not resp.get("completed")
           and state_of(resp) == "Paused" and guard < 60):
        guard += 1
        resp = call("/continue", timeout=120)
        last_trace = resp.get("tracePath") or last_trace
        log(f"  continue {guard}: {state_of(resp)} frame={frame_of(resp)} complete={resp.get('completed')} err={resp.get('error','')}")

    error = resp.get("error") or resp.get("abort") or ""
    if not error and not resp.get("completed"):
        error = f"did not complete (state={state_of(resp)}, guard={guard})"
    outcome = {
        "ok": not error,
        "trace": last_trace,
        "error": error,
        "completed": bool(resp.get("completed")),
        "state": state_of(resp),
    }
    log(f"  final: {outcome['state']} frame={frame_of(resp)} complete={outcome['completed']} "
        f"{'PASS' if outcome['ok'] else 'FAIL: ' + error} trace={last_trace}")
    return outcome


if __name__ == "__main__":
    result = run(sys.argv[1])
    # Trace path on its own line (stable for callers that grep it); explicit verdict + exit code so a failed
    # run (aborted assert) is never mistaken for a pass.
    print(result["trace"] or "")
    print(f"RESULT: {'PASS' if result['ok'] else 'FAIL — ' + result['error']}")
    sys.exit(0 if result["ok"] else 1)
