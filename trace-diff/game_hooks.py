"""Silksong hooks for test_runner.py."""
import json
import time
import urllib.request


def before_test() -> None:
    wait_idle()


def after_test() -> None:
    settle_gc()  # the unpaced back-to-back suite otherwise segfaults in the engine GC (see settle_gc)


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

def wait_idle(timeout: float = 30) -> None:
    """Block until the game is PLAYING outside a scene transition: a test aborting mid-load leaves its transition
    running, and the next test's `load` refuses to start inside it."""
    deadline = time.time() + timeout
    while True:
        with urllib.request.urlopen("http://localhost:8200/silksongplayground/gamestate", timeout=5) as r:
            state = json.load(r)
        if state.get("state") == "PLAYING" and not state.get("transitioning"):
            return
        if time.time() > deadline:
            raise RuntimeError(f"game not idle after {timeout}s: {state}")
        time.sleep(0.2)
