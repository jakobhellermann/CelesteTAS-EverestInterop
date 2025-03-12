using HarmonyLib;
using JetBrains.Annotations;
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
    [HarmonyPatch(typeof(Random), nameof(Random.insideUnitCircle), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.onUnitSphere), MethodType.Getter)]
    private static void FrameHistory(MethodBase __originalMethod, object[] __args, object __result) {
        if (!TasTracerState.ShouldTrace(TasTracerFilter.Random)) return;

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


    [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList))]
    private class RandomP {
        [HarmonyPrefix]
        [UsedImplicitly]
        private static void Prefix(out Random.State? __state) {
            if (!Manager.Running) {
                __state = null;
                return;
            }

            __state = Random.state;
        }


        [HarmonyPostfix]
        [UsedImplicitly]
        private static void Postfix(Random.State? __state) {
            if (__state is not { } state) return;

            Random.state = state;
        }
    }
}
