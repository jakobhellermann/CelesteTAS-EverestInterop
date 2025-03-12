using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class RandomHelper {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Random), nameof(Random.Range), [typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(Random), nameof(Random.Range), [typeof(int), typeof(int)])]
    [HarmonyPatch(typeof(Random), nameof(Random.value), MethodType.Getter)]
    private static void FrameHistory(MethodBase __originalMethod, object[] __args, object __result) {
        if (!Manager.Running) return;
        if (!TasTracerState.Filter.HasFlag(TasTracerFilter.Random)) return;

        TasTracerState.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} -> {__result}", ..__args, new StackTrace(),
        ]);
        TasTracerState.AddFrameHistoryPaused([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} -> {__result}", ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(_2dxFX_Distortion), "XUpdate")]
    private static bool Fix() {
        if (Manager.Running) {
            // TODO: why isn't this deterministic
            return false;
        }

        return true;
    }
}
