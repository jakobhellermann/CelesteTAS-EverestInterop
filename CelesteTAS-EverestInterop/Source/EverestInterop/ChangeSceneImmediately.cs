#if FALSE

using HarmonyLib;
using NineSolsAPI;
using PrimeTween;
using System;
using TAS;

namespace EverestInterop;

[HarmonyPatch]
public class ChangeSceneImmediately {
    private static bool changeSceneImmediately = false;

    public static void ChangeScene(SceneConnectionPoint.ChangeSceneData changeSceneData, bool showTip) {
        changeSceneImmediately = true;
        GameCore.Instance.ChangeSceneCompat(changeSceneData, showTip);
    }


    [HarmonyPatch(typeof(GameCore), nameof(GameCore.FadeToBlack), typeof(float), typeof(float))]
    [HarmonyPrefix]
    public static bool FadeToBlack() => !changeSceneImmediately;

    [HarmonyPatch(typeof(GameCore), nameof(GameCore.FadeOutBlack))]
    [HarmonyPrefix]
    public static void FadeOutBlack(ref float fadeTime, ref float delayTime) {
        if (!changeSceneImmediately) return;

        TasTracerState.AddFrameHistory("fadeoutblack");

        changeSceneImmediately = false;
        fadeTime = 0;
        delayTime = 0;
    }

    [HarmonyPatch(typeof(Tween), nameof(Tween.Delay), typeof(float), typeof(Action), typeof(bool), typeof(bool))]
    [HarmonyPrefix]
    public static void TweenDelay(ref float duration) {
        if (!changeSceneImmediately) return;

        duration = 0;
    }
}

#endif
