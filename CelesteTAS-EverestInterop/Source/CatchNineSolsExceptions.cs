using HarmonyLib;
using System;
using UnityEngine;

namespace TAS;

    [HarmonyPatch]
public class CatchNineSolsExceptions {
    [HarmonyPatch(typeof(BossGeneralState), "LinkMove")]
    [HarmonyFinalizer]
    private static Exception? LoadCommandsFromType(Exception? __exception) {
        if (__exception != null) {
            Log.Error($"Uncaught exception in nine sols: {__exception}");
        }
        return null;

    }
    
}
