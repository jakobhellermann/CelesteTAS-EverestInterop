using HarmonyLib;
using NineSolsAPI;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class TimeHelper {
    [HarmonyPatch(typeof(Time), nameof(Time.frameCount), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool TimeScaleGet(ref int __result) {
        if (Manager.Running) {
            // __result = Manager.Controller.CurrentFrameInTas;
            // return false;
            return true;
        }
        return true;
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
        
        if(overwrittenTimeScale == null)
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
