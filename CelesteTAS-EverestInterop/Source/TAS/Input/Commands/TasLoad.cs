namespace TAS.Input.Commands;

/// Shared loading gate for any async TAS load that spans frames — savestate loads (<see cref="SavestateLoad"/>)
/// and scene loads (<see cref="LoadCommand"/>). While a load is in progress <see cref="IsLoading"/> is true, which
/// pauses playback (see Manager.Update / GameInterop.IsLoading). Completion is deferred to the next frame boundary
/// (<see cref="finishLoadPending"/>) so the mid-frame restore/scene-entry isn't traced with pre-load leftover state.
internal static class TasLoad {
    /// True while a load is in progress. Gates TAS playback through <see cref="EverestInterop.GameInterop.IsLoading"/>.
    public static bool IsLoading { get; private set; }

    /// Set when the load's work finished; cleared (and <see cref="IsLoading"/> dropped) on the next
    /// <see cref="BeforeTasFrame"/>. The restore/scene-entry runs as an async continuation at ScriptRunDelayedTasks —
    /// mid-frame, after that frame's MonoBehaviour Updates and the tracer's early per-phase probes. Clearing
    /// IsLoading right there would let that frame be traced (IsLoading reads false at end-of-frame) with pre-load
    /// leftover state in its early stages — a run-dependent, non-deterministic value. Holding IsLoading true until
    /// the next frame boundary makes that frame a skipped loading frame, so the first traced frame starts clean.
    private static bool finishLoadPending;

    /// Begin a load: pauses playback until <see cref="End"/> plus the next frame boundary.
    public static void Begin() {
        IsLoading = true;
    }

    /// Mark the load's work done; <see cref="IsLoading"/> drops on the next <see cref="BeforeTasFrame"/>.
    public static void End() {
        finishLoadPending = true;
    }

    /// Drop the gate immediately, without the one-frame defer. For a restore already applied at a frame boundary
    /// (not mid-frame), there is no leftover-state window to skip, so the next frame can play live right away.
    public static void EndNow() {
        finishLoadPending = false;
        IsLoading = false;
    }

    [BeforeTasFrame]
    private static void FinishLoad() {
        if (finishLoadPending) {
            finishLoadPending = false;
            IsLoading = false;
        }
    }

    /// Reset loading state when a run ends, so an aborted mid-load can't leave playback gated for the next run.
    [DisableRun]
    private static void OnDisableRun() {
        IsLoading = false;
        finishLoadPending = false;
    }
}
