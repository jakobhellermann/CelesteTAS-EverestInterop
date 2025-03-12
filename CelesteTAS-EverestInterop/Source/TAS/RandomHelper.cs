using HarmonyLib;
using System.Diagnostics;
using System.Reflection;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class RandomHelper {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), [typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), [typeof(int), typeof(int)])]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value), MethodType.Getter)]
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
}
