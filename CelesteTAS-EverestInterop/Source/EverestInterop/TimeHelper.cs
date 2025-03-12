using HarmonyLib;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class TimeHelper {
    private static int? overrideFrameCount;
    internal static float? timeInTas;

    [EnableRun]
    private static void EnableRun() {
        overrideFrameCount = 1000;
        timeInTas = 0;
    }

    [DisableRun]
    private static void DisableRun() {
        overrideFrameCount = null;
        timeInTas = null;
    }

    [BeforeTasFrame]
    private static void Update() {
        if (Manager.CurrState == Manager.State.Paused) return;

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

    #endregion
}
