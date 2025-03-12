using HarmonyLib;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class TimeHelper {
    private static int? overrideFrameCount;
    private static int? overrideRealTimeSinceStartup;

    [EnableRun]
    private static void EnableRun() {
        overrideFrameCount = 1000;
        overrideRealTimeSinceStartup = 0;
    }

    [DisableRun]
    private static void DisableRun() {
        overrideFrameCount = null;
        overrideRealTimeSinceStartup = null;
    }

    [BeforeTasFrame]
    private static void Update() {
        if (Manager.CurrState == Manager.State.Paused) return;

        if (overrideFrameCount != null) {
            overrideFrameCount++;
        }

        if (overrideRealTimeSinceStartup != null) {
            overrideRealTimeSinceStartup += InputHelper.CurrentTasFramerate;
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

    [HarmonyPatch(typeof(Time), nameof(Time.realtimeSinceStartup), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool GetRealtimeSinceStartup(ref float __result) {
        if (overrideRealTimeSinceStartup is { } realTimeSinceStartup) {
            __result = realTimeSinceStartup;
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(Time), nameof(Time.frameCount), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool FrameCountGet(ref int __result) {
        if (overrideFrameCount is not { } frameCount) return true;

        __result = frameCount;
        return false;
    }


    #region RCG Override

    private static float rcgTimeScale = Time.timeScale;
    private static float? overwrittenTimeScale = null;

    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.timeScale), MethodType.Getter)]
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
    }

    public static float? OverwriteTimeScale {
        get => overwrittenTimeScale;
        set {
            overwrittenTimeScale = value;
            Time.timeScale = value ?? rcgTimeScale;
        }
    }

    /*[HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    [HarmonyPrefix]
    public static bool GlobalSimSpeedSet(float value) {
        var field = typeof(RCGTime).GetField("_globalSimulationSpeed", BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null) {
            Log.Error("Could not set _globalSimulationSpeed: field does not exist");
            return true;
        }

        field.SetValue(null, value);
        // Time.timeScale = RCGTime._globalSimulationSpeed * actualTimeScale;

        return false;
    }*/

    #endregion
}
