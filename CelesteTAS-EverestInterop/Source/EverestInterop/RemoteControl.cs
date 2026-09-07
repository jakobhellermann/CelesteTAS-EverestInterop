using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using DevUtils;
using HutongGames.PlayMaker;
using TAS.Communication;
using TAS.InfoHUD;
using TAS.Tracer;

namespace TAS;

/// HTTP routes registered on DevUtils' DevServer to drive TAS playback remotely (e.g. from tooling or an agent)
/// without going through Studio. Deliberately shares the same playback engine and InputController.FilePath as the
/// editor, so the editor stays a live observer: /run just points the controller at a file and starts it, exactly
/// like Studio's Start button. Nothing here locks out or suppresses the editor — stepping/pausing via hotkeys keeps
/// working while a run is active. Routes live under the plugin slug, i.e. /tastools/run, /tastools/state, /tastools/stop.
public static class RemoteControl {
    /// Upper bound on frames a blocking route waits for a queued state change (run start / resume / stop) to take
    /// hold before giving up with a 504 rather than hanging the HTTP request forever. These loops `yield return null`
    /// once per frame, so this is ~10s at 60fps — generously above any real start/stop latency.
    private const int MaxWaitFrames = 600;

    /// Watchdog bound for a run that stops advancing while still "Running" — a stuck load or a playback deadlock. If
    /// the TAS frame hasn't moved for this many settle-loop iterations (~5s at 60fps, well above any real load or
    /// fast-forward pause), the run is aborted and the request surfaces the stall instead of hanging forever. Normal
    /// playback advances the frame every iteration (or reaches a breakpoint/completion), so this only trips on a hang.
    private const int StallFrames = 300;

    [Initialize]
    private static void Register() {
        var owner = TasMod.Instance;

        DevServer.MapPostAsync(owner, "/run", (string? path, Action<object?> respond) => Run(path, respond),
            "Run a TAS. Optional path=<.tas> (default: current file, follows in the editor). Blocks until the run " +
            "ends or is stopped; background the request for fire-and-forget.");
        DevServer.MapGet(owner, "/state", _ => Snapshot(),
            "Current TAS playback state: state, didComplete, currentFrame, totalFrames, filePath.");
        DevServer.MapPostAsync(owner, "/stop", (string? _, Action<object?> respond) => Stop(respond),
            "Stop the current TAS run (DisableRun). Blocks until the run is fully stopped, so a follow-up /run " +
            "doesn't race the teardown.");
        DevServer.MapPostAsync(owner, "/continue", (string? _, Action<object?> respond) => Continue(respond),
            "Resume a run paused at a breakpoint (***) / savestate, like the PauseResume hotkey. Blocks until the " +
            "run ends or hits the next pause; use to play through a ***S breakpoint headless.");
        DevServer.MapPost(owner, "/clearstates", _ => ClearStates(),
            "Clear all stored savestates (breakpoint ***S and manual). Use to get a clean first run that creates " +
            "the savestate rather than resuming from an existing one.");
        DevServer.MapGet(owner, "/eval", (string query) => Eval(query),
            "Evaluate an InfoHUD/TargetQuery template line, e.g. ?query={SceneManager.sceneCount} — the same " +
            "expression syntax the Assert command uses. For checking what a test's Assert can query.");
        DevServer.MapGet(owner, "/set", (string target, string value) => Set(target, value),
            "Set a member via TargetQuery (the same engine as the Set command), e.g. " +
            "?target=UnityEngine.Application.targetFrameRate&value=100000. Runs on the main thread; the write side of /eval.");
        DevServer.MapGet(owner, "/fsms", _ => Fsms(),
            "List every PlayMaker FSM in the loaded scenes (scene, owner path, FsmName, active state) — for seeing " +
            "which scene/boss FSMs exist and are dynamically relevant.");
        DevServer.MapGet(owner, "/config", (string? section) => ConfigList(section),
            "List TAS config entries (the BepInEx .cfg): section, key, value (cfg-string form, round-trips into " +
            "POST /config), type, acceptable values / enum names, description. Optional ?section=<name> filter.");
        DevServer.MapPost(owner, "/config", (string? section, string? key, string? value) => ConfigSet(section, key, value),
            "Set a TAS config entry live, e.g. ?section=Tracer&key=Frame History Filter&value=Random, Movement — the " +
            "value parses like the .cfg line (bools, ints, floats, enums incl. comma-separated flags). Invalid values " +
            "are rejected with ok=false. Saves the config file so the value survives restarts.");
    }

    // Snapshot of every live PlayMaker FSM (scene + owner path + name + current state), so scene/boss FSM landscape
    // can be inspected on demand without a JIT console.
    private static object Fsms() {
        var fsms = PlayMakerFSM.FsmList
            .Where(f => f)
            .Select(f => {
                var t = f.transform;
                var path = f.gameObject.name;
                while (t.parent != null) {
                    t = t.parent;
                    path = t.name + "/" + path;
                }

                return new { scene = f.gameObject.scene.name, path, fsm = f.FsmName, state = f.ActiveStateName };
            })
            .OrderBy(x => x.scene).ThenBy(x => x.path)
            .ToArray();
        return new { ok = true, count = fsms.Length, fsms };
    }

    // Set a member live via TargetQuery (the `Set` command's engine), so values can be poked for experiments without
    // a JIT console. Sync handler → runs on the main thread, so mutating game state here is safe.
    private static object Set(string target, string value) {
        target ??= "";
        value ??= "";
        try {
            var result = TargetQuery.SetMemberValues(target, new[] { value }, forceAllowCodeExecution: true);
            return result.Failure
                ? new { ok = false, target, value, error = result.Error.ToString() }
                : new { ok = true, target, value };
        } catch (Exception e) {
            return new { ok = false, target, value, error = e.Message };
        }
    }

    // Evaluate an InfoHUD template line (the `{expr}` syntax Assert uses) and return the result, so an Assert
    // expression can be checked live before baking it into a test.
    private static object Eval(string query) {
        query ??= "";
        try {
            return new { ok = true, query, result = string.Join("\n", InfoCustom.ParseTemplateLine(query, 0, forceAllowCodeExecution: true)) };
        } catch (Exception e) {
            return new { ok = false, query, error = e.Message };
        }
    }

    private static object ClearStates() {
        Manager.AddMainThreadAction(Playback.SavestateManager.ClearAllSavestates);
        return new { ok = true };
    }

    // BepInEx config (the .cfg) over HTTP: listing + live set. The tracer options (frame history, its filter,
    // stack traces) are cfg entries, so this is how tooling flips them per-run — no hand-editing the file (which
    // the game rewrites on exit anyway; runtime sets here persist through that).
    private static object ConfigList(string? section) {
        // 'Values' is only implemented explicitly on this BepInEx build (public surface: GetConfigEntries).
        var values = ((IDictionary<ConfigDefinition, ConfigEntryBase>)TasMod.Instance.Config).Values;
        var entries = values
            .Where(e => section == null || string.Equals(e.Definition.Section, section, StringComparison.OrdinalIgnoreCase))
            .Select(e => new {
                section = e.Definition.Section,
                key = e.Definition.Key,
                value = e.GetSerializedValue(),
                type = e.SettingType.Name,
                acceptable = AcceptableValues(e),
                description = e.Description?.Description,
            })
            .OrderBy(x => x.section).ThenBy(x => x.key)
            .ToArray();
        return new { ok = true, count = entries.Length, entries };
    }

    private static string?[]? AcceptableValues(ConfigEntryBase entry) {
        if (entry.SettingType.IsEnum) {
            return Enum.GetNames(entry.SettingType);
        }

        if (entry.Description?.AcceptableValues is { } acceptable
            && acceptable.GetType().GetProperty("AcceptableValues")?.GetValue(acceptable) is System.Collections.IEnumerable values) {
            return values.Cast<object?>().Select(v => v?.ToString()).ToArray();
        }

        return null;
    }

    private static object ConfigSet(string? section, string? key, string? value) {
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key) || value == null) {
            return new { ok = false, error = "section, key and value are required — GET /tastools/config lists all" };
        }

        var entry = ((IDictionary<ConfigDefinition, ConfigEntryBase>)TasMod.Instance.Config).Values.FirstOrDefault(e =>
            string.Equals(e.Definition.Section, section, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Definition.Key, key, StringComparison.OrdinalIgnoreCase));
        if (entry == null) {
            return new { ok = false, section, key, error = $"no config entry '{section}/{key}' — GET /tastools/config lists all" };
        }

        // SetSerializedValue swallows parse errors (logs a warning, keeps the old value) — validate first so a bad
        // value surfaces to the caller instead of silently doing nothing.
        try {
            TomlTypeConverter.ConvertToValue(value, entry.SettingType);
        } catch (Exception e) {
            return new { ok = false, section, key, value, error = $"invalid value for {entry.SettingType.Name}: {e.Message}" };
        }

        entry.SetSerializedValue(value);
        TasMod.Instance.Config.Save();
        return new { ok = true, section, key, value = entry.GetSerializedValue() };
    }

    private static IEnumerator Stop(Action<object?> respond) {
        Manager.AddMainThreadAction(() => {
            if (Manager.Running) {
                Manager.DisableRun();
            }
        });

        // Block until the queued DisableRun has taken hold (it runs on the TAS pump a tick or two later). Returning
        // before the run is actually down lets a follow-up /run start against a still-tearing-down run and load stale
        // state. Bounded so a stuck teardown can't hang the request forever.
        int guard = 0;
        while (Manager.Running && ++guard <= MaxWaitFrames) {
            yield return null;
        }

        respond(new { ok = true, state = Snapshot() });
    }

    private static object Snapshot() {
        var controller = Manager.Controller;
        return new {
            // state (Running/Paused/Disabled/...) already implies running/playbackRunning/canPlayback, so
            // those are dropped. didComplete is kept because it distinguishes a finished run from a stopped
            // one (both end up Disabled).
            state = Manager.CurrState.ToString(),
            didComplete = Manager.DidComplete,
            currentFrame = controller.CurrentFrameInTas,
            totalFrames = controller.Inputs.Count,
            filePath = controller.FilePath,
            // The live info-panel text (GameInfo.Update runs every frame), so state/coroutines can be inspected
            // headlessly without running a TAS.
            info = GameInfo.StudioInfo,
        };
    }

    private static IEnumerator Run(string? path, Action<object?> respond) {
        // Validate the file up-front: the FilePath setter silently falls back to the default file when the path
        // doesn't exist, which would otherwise run the wrong TAS without any error surfacing to the caller.
        if (!string.IsNullOrEmpty(path)) {
            string full = Path.GetFullPath(path!);
            if (!File.Exists(full)) {
                respond(DevResponse.Json(new { error = $"file not found: {full}" }, 404));
                yield break;
            }

            path = full;
        }

        // Mirror Studio's Start button: stop any current run, point the controller at the file, start fresh.
        // Queued onto the TAS main-thread action pump so the mutation lands at the canonical Manager.Update point
        // rather than mid-way through the DevServer host's own Update.
        bool[] started = [false];
        Manager.AddMainThreadAction(() => {
            if (Manager.Running) {
                Manager.DisableRun();
            }

            if (!string.IsNullOrEmpty(path)) {
                Manager.Controller.FilePath = path!;
                // Pull the editor onto the same file so it stays a live observer. Studio ignores this if it's
                // already showing the file, so it's safe to send unconditionally (no reload loop).
                CommunicationWrapper.SendCurrentFile(path!);
            }

            Manager.EnableRun();
            started[0] = true;
        });

        // The pump runs every frame in Manager.Update; this only trips if the TAS player loop is dead.
        int waited = 0;
        while (!started[0]) {
            if (++waited > MaxWaitFrames) {
                respond(DevResponse.Json(new { error = "timed out waiting for run to start" }, 504));
                yield break;
            }

            yield return null;
        }

        // EnableRun disables itself immediately on an empty / invalid file (Inputs.Count == 0).
        if (!Manager.Running && !Manager.DidComplete) {
            respond(DevResponse.Json(new { error = "TAS did not start (empty or invalid file)", state = Snapshot() }, 422));
            yield break;
        }

        yield return BlockUntilSettledAndRespond(respond);
    }

    private static IEnumerator Continue(Action<object?> respond) {
        if (Manager.CurrState != Manager.State.Paused) {
            respond(DevResponse.Json(new { error = "not paused", state = Snapshot() }, 409));
            yield break;
        }

        // Mirror the PauseResume hotkey exactly (Paused -> Running), including its completed-run case: a run
        // completed at its final frame (auto-paused draft or an end-of-file breakpoint) has nothing left to play —
        // resuming it would run one no-input game frame and immediately re-pause. End the run instead.
        Manager.AddMainThreadAction(() => {
            if (Manager.CurrState == Manager.State.Paused) {
                Manager.NextState = Manager.DidComplete ? Manager.State.Disabled : Manager.State.Running;
            }
        });

        // If this request queues the teardown (completed run), settle on Disabled — settling on Paused would return
        // the pre-teardown snapshot. DidComplete can't change while paused, so reading it here is stable.
        yield return BlockUntilSettledAndRespond(respond, waitForTeardown: Manager.DidComplete);
    }

    /// Block until the run is no longer actively advancing — it finished (Disabled / DidComplete) or was stopped /
    /// hit a pause / breakpoint — then respond with the final state. Stopping the TAS (via /stop, a hotkey, or an
    /// error) ends the loop too.
    private static IEnumerator BlockUntilSettledAndRespond(Action<object?> respond, bool waitForTeardown = false) {
        // Wait for the queued resume/start to actually take hold before sampling. The state change is queued onto the
        // TAS pump, so it takes a couple of Manager.Update ticks to apply (the action sets NextState, CurrState flips
        // on a later tick); a single yield would sample the still-Paused pre-resume state and return it as if the run
        // never moved. Exit as soon as the run shows it moved — left the starting Paused frame, or completed/stopped.
        // Bounded so a resume that gets superseded (NextState overwritten) can't hang the request forever.
        int startFrame = Manager.Controller.CurrentFrameInTas;
        int guard = 0;
        while (Manager.CurrState == Manager.State.Paused
               && Manager.Controller.CurrentFrameInTas == startFrame
               && !Manager.DidComplete) {
            if (++guard > MaxWaitFrames) {
                // The resume never took effect (the run is still paused on the exact frame we started from). Surface
                // it rather than falling through to respond with the stale pre-resume state as if it had settled.
                respond(DevResponse.Json(new { error = "resume did not take effect", state = Snapshot() }, 504));
                yield break;
            }

            yield return null;
        }

        int stall = 0;
        int lastFrame = Manager.Controller.CurrentFrameInTas;
        while (Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance or Manager.State.SlowForward
               && !Manager.DidComplete) {
            int frame = Manager.Controller.CurrentFrameInTas;
            if (frame != lastFrame) {
                lastFrame = frame;
                stall = 0;
            } else if (++stall > StallFrames) {
                // The run is Running but hasn't advanced a frame for StallFrames iterations — a stuck load or a
                // playback deadlock. Abort it (so it releases input and resolves this request) rather than hang.
                Manager.AddMainThreadAction(() => {
                    if (Manager.Running) {
                        Manager.DisableRun();
                    }
                });
                respond(DevResponse.Json(new { error = "run stalled: no frame progress — aborted", state = Snapshot() }, 504));
                yield break;
            }

            yield return null;
        }

        // A completed run settles either paused at its final frame (draft auto-pause / end-of-file breakpoint) or
        // — when this request itself queued the teardown by resuming a completed run — disabled. CurrState stays
        // Paused for a frame after the queued DisableRun, so settling on Paused there would return the
        // pre-teardown snapshot as if the resume did nothing.
        if (Manager.DidComplete) {
            var settledState = waitForTeardown ? Manager.State.Disabled : Manager.State.Paused;
            while (Manager.Running && Manager.CurrState != settledState) {
                yield return null;
            }
        }

        respond(new {
            ok = true,
            completed = Manager.DidComplete,
            abort = Manager.LastAbortMessage,
            tracePath = TasTracer.LastSavedTracePath,
            state = Snapshot(),
        });
    }

    [Unload]
    private static void Unregister() {
        DevServer.Deregister(TasMod.Instance);
    }
}
