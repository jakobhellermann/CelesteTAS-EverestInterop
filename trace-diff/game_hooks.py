"""Silksong hooks for test_runner.py."""
import urllib.request


def before_test() -> None:
    pass


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
