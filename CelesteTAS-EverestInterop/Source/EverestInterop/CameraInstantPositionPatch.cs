using HarmonyLib;
using TAS.EverestInterop;

namespace TAS;

/// While a TAS load is in flight, position the camera instantly instead of via PositionToHero's coroutine. That
/// coroutine holds a fixed WaitForFixedUpdate + 0.1s (independent of how far the camera moves) before switching the
/// camera back to FOLLOWING and firing PositionedAtHero. A scene load restarts it, so that fixed-duration tail spans
/// real frames after the load completes — landing on a different playback frame depending on when the load was kicked
/// off. The instant path does the same positioning and fires the same PositionedAtHero without the hold, so the
/// camera settles inside the (untraced) load window and playback frame 0 starts from a settled camera. Covers both
/// the `load` command and savestate resumes, which share the IsLoading gate.
[HarmonyPatch]
public static class CameraInstantPositionPatch {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CameraController), nameof(CameraController.PositionToHero))]
    private static bool PositionInstantWhileLoading(CameraController __instance, bool forceDirect) {
        if (!GameInterop.IsLoading()) {
            return true;
        }

        __instance.PositionToHeroInstant(forceDirect);
        return false;
    }
}
