using System.Collections.Generic;
using HarmonyLib;
using InControl;
using NineSolsAPI;
using StudioCommunication;
using System;
using System.Diagnostics;
using System.Reflection;
using TAS.Input;
using UnityEngine;

namespace TAS;

[HarmonyPatch]
public static class RandomHelper {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), [typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), [typeof(int), typeof(int)])]
    [HarmonyPatch(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value), MethodType.Getter)]
    private static void FrameHistory(MethodBase __originalMethod, object[] __args, object __result)
        => TasTracerState.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} -> {__result}", ..__args, new StackTrace(),
        ]);
}
