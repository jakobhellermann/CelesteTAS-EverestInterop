using HarmonyLib;
using System;
using TAS.Input;

// ReSharper disable InconsistentNaming

namespace TAS;

[HarmonyPatch]
public static class InputHelper {
    public static bool Prevent = false;

    public static void WithPrevent(Action a) {
        Prevent = true;
        a();
        Prevent = false;
    }

    private const int DefaultTasFramerate = 60;
    public static int CurrentTasFramerate = DefaultTasFramerate;

    [EnableRun]
    private static void EnableRun() {
        // TODO(time)
    }

    [DisableRun]
    private static void DisableRun() {
        // TODO(time)
    }

    public static void FeedInputs(InputFrame inputFrame) {
        // TODO(input)
    }
}
