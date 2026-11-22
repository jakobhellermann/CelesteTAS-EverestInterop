using System;
using HarmonyLib;
using TAS.Utils;
using UnityEngine;

namespace TAS;

/// Patch Time.* to give deterministic values during TAS playback
[HarmonyPatch]
public static class DeterministicTimePatch {
    private static int? overrideFrameCount;
    private static float? timeInTas;
    private static bool wasLoading;

    [EnableRun]
    private static void EnableRun() {
        // Symmetric to DisableRun: live timestamps move onto the TAS clock. Frame step back re-enters mid-run, from the TAS clock.
        const float tasStartTime = 0;
        double currentTime = timeInTas ?? EngineTime;
        if (ModInterop.SpeedrunToolInterop.Backend is { Installed: true } backend) {
            backend.RebaseAbsoluteTimes(tasStartTime - currentTime);
        }

        overrideFrameCount = 1000;
        timeInTas = tasStartTime;
        wasLoading = false;
    }

    [DisableRun]
    private static void DisableRun() {
        // Timestamps taken on the TAS clock would otherwise be off by the jump back to the engine clock
        if (timeInTas is { } tasTime && ModInterop.SpeedrunToolInterop.Backend is { Installed: true } backend) {
            backend.RebaseAbsoluteTimes(EngineTime - tasTime);
        }

        overrideFrameCount = null;
        timeInTas = null;
    }

    private static bool readEngine;

    private static T Engine<T>(Func<T> get) {
        readEngine = true;
        try {
            return get();
        } finally {
            readEngine = false;
        }
    }

    /// The engine's own scaled time, which the TAS clock replaces
    public static double EngineTime => Engine(() => Time.timeAsDouble);

    /// Engine time of the current frame, as read outside FixedUpdate (inside, Unity's time returns the step's fixedTime)
    private static double frameEngineTime;

    /// Unity keeps time, fixedTime and captureDeltaTime in whole ticks of this many per second
    public const double TicksPerSecond = 141120000;

    public static long ToTicks(double seconds) => (long) Math.Round(seconds * TicksPerSecond);

    /// Engine time since the last FixedUpdate step (T - FT), which decides the frames that step physics and Unity's
    /// Rigidbody2D interpolation
    public static long TftTicks => ToTicks((Time.inFixedTimeStep ? frameEngineTime : EngineTime) - Engine(() => Time.fixedTimeAsDouble));

    public static void RecordFrameEngineTime() => frameEngineTime = EngineTime;

    // Each FixedUpdate step sees its own fixedTime and, as in the engine, time == fixedTime
    private static double TasFixedTime(float tasTime) => tasTime - TftTicks / TicksPerSecond;

    // Re-base the clock onto a loaded savestate's capture time, else its absolute-time/-frame state is stale.
    public static void RebaseClock(float time, int frameCount) {
        if (timeInTas != null) {
            timeInTas = time;
        }

        if (overrideFrameCount != null) {
            overrideFrameCount = frameCount;
        }
    }

    /// Ends the load's timeScale freeze now instead of on the next frame, so a pause set right after keeps its own
    public static void EndLoadFreeze() {
        OverwriteTimeScale = null;
        wasLoading = false;
    }

    [BeforeTasFrame]
    private static void Update() {
        var loading = Input.Commands.TasLoad.IsLoading;

        // Freeze world simulation during the load via the timeScale override; clearing it restores the game's timeScale
        // (the real value) rather than hardcoding 1. A resumed savestate lifts the freeze itself at its restore
        // boundary (see SavestateLoad.ApplyPendingRestore) so the first live frame's physics isn't frozen; this
        // edge only covers loads that finish without a deferred restore (e.g. the `load` command).
        if (loading) {
            OverwriteTimeScale = 0;
        } else if (wasLoading) {
            OverwriteTimeScale = null;
        }
        wasLoading = loading;

        if (Manager.CurrState == Manager.State.Paused || loading) return;

        if (overrideFrameCount != null) {
            overrideFrameCount++;
        }

        if (timeInTas != null) {
            timeInTas += 1f / InputHelper.CurrentTasFramerate;
        }
    }


    [HarmonyPatch(typeof(Time), nameof(Time.unscaledDeltaTime), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetUnscaledDeltaTime(ref float __result) {
        if (Manager.Running) {
            __result = Manager.CurrState == Manager.State.Paused ? 0 : 1f / InputHelper.CurrentTasFramerate;
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.smoothDeltaTime), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetSmoothDeltaTime(ref float __result) {
        if (Manager.Running) {
            __result = InputHelper.CurrentTasFramerate;
            return false;
        }

        return true;
    }


    [HarmonyPatch(typeof(Time), nameof(Time.time), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetTime(ref float __result) {
        if (readEngine || timeInTas is not { } time) return true;

        __result = Time.inFixedTimeStep ? (float) TasFixedTime(time) : time;
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.fixedTime), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetFixedTime(ref float __result) {
        if (readEngine || timeInTas is not { } time) return true;

        __result = (float) TasFixedTime(time);
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.fixedTimeAsDouble), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetFixedTimeAsDouble(ref double __result) {
        if (readEngine || timeInTas is not { } time) return true;

        __result = TasFixedTime(time);
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.realtimeSinceStartup), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetRealtimeSinceStartup(ref float __result) {
        if (timeInTas is not { } time) return true;

        __result = time;
        return false;
    }

    // TODO: returns time-since-TAS-start, not truly since-level-load (no per-level reset) — fine while consumers use
    // only deltas of it; revisit if an absolute value is needed.
    [HarmonyPatch(typeof(Time), nameof(Time.timeSinceLevelLoad), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetTimeSinceLevelLoad(ref float __result) {
        if (timeInTas is not { } time) return true;

        __result = time;
        return false;
    }

    // unscaledTime is a separate session-cumulative clock (unaffected by timeScale), so it bypasses the `time`
    // patch above. Pin it to the TAS clock too, else absolute-unscaled-time state baked into savestates (e.g.
    // RandomAudioClipTable.nextPlayTime = Time.unscaledTimeAsDouble + cooldown) drifts with session age between
    // runs and breaks byte-reproducibility. The double accessors (timeAsDouble / unscaledTimeAsDouble) likewise
    // bypass the float getters, so patch them explicitly.
    [HarmonyPatch(typeof(Time), nameof(Time.unscaledTime), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetUnscaledTime(ref float __result) {
        if (timeInTas is not { } time) return true;

        __result = time;
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.timeAsDouble), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetTimeAsDouble(ref double __result) {
        if (readEngine || timeInTas is not { } time) return true;

        __result = Time.inFixedTimeStep ? TasFixedTime(time) : time;
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.unscaledTimeAsDouble), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetUnscaledTimeAsDouble(ref double __result) {
        if (timeInTas is not { } time) return true;

        __result = time;
        return false;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.frameCount), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool FrameCountGet(ref int __result) {
        if (overrideFrameCount is not { } frameCount) return true;

        __result = frameCount;
        return false;
    }


    #region Timescale Override

    private static float? overwrittenTimeScale = null;

    public static float? OverwriteTimeScale {
        get => overwrittenTimeScale;
        set {
            overwrittenTimeScale = value;
            if (value is { } timeScale) {
                Time.timeScale = timeScale;
            } else {
                // The game's own value (time controls, pause, debug scale), which it only reapplies on change
                typeof(TimeManager).InvokeMethod("UpdateTimeScale");
            }
        }
    }

    #endregion
}
