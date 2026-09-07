using System;
using System.IO;
using System.Threading.Tasks;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Tracer;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Savestate-load machinery for <see cref="StartOnSavestateCommand"/> (at TAS start) and
/// <see cref="LoadSavestateCommand"/> (mid-TAS, at the command's line). The playback-pausing gate is shared with
/// scene loads — see <see cref="TasLoad"/>.
internal static class SavestateLoad {
    /// Layer CelesteTAS stores its savestates under (kept separate from the in-game UI savestates).
    public const string Layer = "CelesteTAS";

    /// Whether a command argument names a savestate file (absolute path, or a relative path / *.json) rather than a
    /// slot in the CelesteTAS layer.
    public static bool IsPathArg(string arg) =>
        Path.IsPathRooted(arg) || arg.Contains('/') || arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// Resolve a file-path argument: absolute as-is, relative against the TAS file's directory.
    public static string ResolvePath(string arg, string tasFilePath) =>
        Path.IsPathRooted(arg) ? arg : Path.Combine(Path.GetDirectoryName(tasFilePath) ?? ".", arg);

    /// Load from a slot in the CelesteTAS layer.
    public static void Load(string slot, string commandName) =>
        LoadInternal(interop => interop.LoadSavestate(slot, Layer), $"slot '{slot}'", commandName);

    /// Load from a file path (absolute, or relative to the TAS file), bypassing the slot store.
    public static void LoadFile(string path, string commandName) =>
        LoadInternal(interop => interop.LoadSavestateFromFile(path), $"file '{path}'", commandName);

    /// Kick off the async savestate load. Opens the <see cref="TasLoad"/> gate immediately so the next
    /// Manager.Update pauses playback; the restore itself runs as an async continuation. <paramref name="commandName"/>
    /// is only used for user-facing messages.
    private static async void LoadInternal(Func<PreciseSavestatesInterop, Task<bool>> load, string source, string commandName) {
        if (PreciseSavestatesInterop.Instance is not { } interop) {
            PopupToast.ShowAndLog($"{commandName}: PreciseSavestates is not installed");
            return;
        }

        TasTracer.TraceEvent($"Savestate load: {source}");
        // Hold the component/FSM/RNG/clock restore instead of applying it in the async scene-load continuation; it is
        // applied at a controlled player-loop phase in ApplyPendingRestore (symmetric with the capture), so a resumed
        // run lands byte-identical to a continuous one. Only the scene + pre-Start save data restore inline here.
        interop.DeferSnapshotRestore = true;
        TasLoad.Begin();
        try {
            if (!await load(interop)) {
                // The load failed (missing savestate, or an inner exception PreciseSavestates caught and toasted —
                // e.g. "Can't load savestate in state EXITING_LEVEL"). A TAS whose savestate didn't load can't
                // continue meaningfully, and silently completing would let a broken run pass a test. Abort so it
                // surfaces (as the run's abort, which the headless runner reports as a failure).
                TasLoad.End();
                AbortTas($"{commandName}: failed to load savestate ({source})", log: true);
            }
            // On success the snapshot is now pending — ApplyPendingRestore applies it and drops the gate.
        } catch (Exception e) {
            TasLoad.End();
            AbortTas($"{commandName}: failed to load savestate ({source}): {e.Message}", log: true);
        }
    }

    /// Applies the held snapshot after Manager.Update (i.e. after AdvanceFrame): the load-completion frame then
    /// doesn't advance (AdvanceFrame saw IsLoading=true), so no input frame is consumed on a frame that isn't
    /// traced/simulated. Rebases the clock and drops the gate so the *next* frame is the first to play live.
    internal static void ApplyPendingRestore() {
        if (!Manager.Running || PreciseSavestatesInterop.Instance is not { SnapshotPending: true } interop) {
            return;
        }

        interop.ApplyPendingSnapshot();
        if (interop.LastLoadedGameTime is { } time) {
            DeterministicTimePatch.RebaseClock(time, interop.LastLoadedFrameCount ?? 0);
        }

        // Restore is applied at this frame boundary, so drop the gate now (no deferred loading frame) — the next
        // frame plays live on the restored state, symmetric with a continuous run.
        TasLoad.EndNow();
        // Lift the load-freeze here (frame end) rather than a frame later via DeterministicTimePatch's edge, so the
        // next frame's FirstUpdate physics runs live instead of frozen. Clearing the override restores rcgTimeScale
        // (or a timeScale the snapshot restored), not a hardcoded value.
        DeterministicTimePatch.OverwriteTimeScale = null;
    }

    /// Manual (non-TAS) loads must restore synchronously — nothing drives the deferred apply outside a run.
    [DisableRun]
    private static void ResetDeferSnapshotRestore() {
        if (PreciseSavestatesInterop.Instance is not { } interop) {
            return;
        }

        // A run can end with a snapshot still deferred (a LoadSavestate right before it stopped). Apply it now so the
        // load completes instead of orphaning PendingSnapshot — an orphaned pending snapshot would permanently block
        // the next load, which is now gated on it being clear.
        if (interop.SnapshotPending) {
            interop.ApplyPendingSnapshot();
        }

        interop.DeferSnapshotRestore = false;
    }
}
