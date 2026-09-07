using HarmonyLib;
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
        overrideFrameCount = 1000;
        timeInTas = 0;
        wasLoading = false;
    }

    [DisableRun]
    private static void DisableRun() {
        overrideFrameCount = null;
        timeInTas = null;
    }

    // Re-base the clock onto a loaded savestate's capture time, else its absolute-time/-frame state is stale.
    public static void RebaseClock(float time, int frameCount) {
        if (timeInTas != null) {
            timeInTas = time;
        }

        if (overrideFrameCount != null) {
            overrideFrameCount = frameCount;
        }
    }

    [BeforeTasFrame]
    private static void Update() {
        var loading = EverestInterop.GameInterop.IsLoading();

        // Freeze world simulation during the load via the timeScale override; clearing it restores rcgTimeScale
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
        if (timeInTas is not { } time) return true;

        __result = time;
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
        if (timeInTas is not { } time) return true;

        __result = time;
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

    private static float rcgTimeScale = Time.timeScale;
    private static float? overwrittenTimeScale = null;

    // TODO: patch using preloader?
    /*[HarmonyPatch(typeof(RCGTime), nameof(RCGTime.timeScale), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool TimeScaleGet(ref float __result) {
        __result = overwrittenTimeScale ?? rcgTimeScale;
        return false;
    }

    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.timeScale), MethodType.Setter)]
    [HarmonyPrefix]
    private static bool TimeScaleSet(ref float value) {
        rcgTimeScale = value;

        if (overwrittenTimeScale == null)
            Time.timeScale = value;

        return false;
    }*/

    public static float? OverwriteTimeScale {
        get => overwrittenTimeScale;
        set {
            overwrittenTimeScale = value;
            Time.timeScale = value ?? rcgTimeScale;
        }
    }

    #endregion
}
