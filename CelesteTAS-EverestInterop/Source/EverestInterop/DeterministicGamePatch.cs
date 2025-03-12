using HarmonyLib;
using UnityEngine;

namespace TAS;

[HarmonyPatch]
public static class DeterministicGamePatch {
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.timeScale), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool TimeScaleGet(ref float __result) {
        __result = DeterministicTimePatch.OverwriteTimeScale ?? DeterministicTimePatch.gameTimeScale;
        return false;
    }

    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.timeScale), MethodType.Setter)]
    [HarmonyPrefix]
    private static bool TimeScaleSet(ref float value) {
        DeterministicTimePatch.gameTimeScale = value;

        if (DeterministicTimePatch.OverwriteTimeScale == null)
            Time.timeScale = value;

        return false;
    }
}
