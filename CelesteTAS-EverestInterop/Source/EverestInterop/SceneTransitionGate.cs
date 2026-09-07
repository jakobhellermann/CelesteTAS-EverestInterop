using TAS.Input.Commands;
using TAS.Utils;

namespace TAS;

/// Makes the variable async-load wait of the game's own scene transitions invisible to the TAS timeline, so a
/// transition consumes a deterministic number of frames regardless of how long the load takes (fast-forward safe).
///
/// Every transition runs GameManager.BeginSceneTransitionRoutine, whose loop waits until the camera fade has elapsed
/// AND the async scene load is ready to activate. The fade counts down on the fixed TAS deltaTime (deterministic —
/// stays visible); the load-ready wait is wall-clock-variable (under fast-forward the input-frame rate outruns it, so
/// it eats a variable number of input frames and a SaveSavestate right after fires mid-transition). So gate playback
/// — via the shared TasLoad gate, which freezes the clock, stops input-frame advancement and sets timeScale = 0 —
/// only during that second phase: transition in progress, fade done, load not yet ready.
///
/// timeScale = 0 is safe here precisely because the fade is already finished: nothing left reads deltaTime to
/// deadlock, and the load still completes (the driving coroutine's `yield return null` and the Addressables load are
/// independent of timeScale). The fade and the post-activation entry run outside this window, so they stay visible
/// on the fixed clock.
///
/// The `load` command and savestate loads set TasLoad themselves before their own BeginSceneTransition, so this only
/// engages for organic transitions the gate isn't already held for (the `!TasLoad.IsLoading` check).
internal static class SceneTransitionGate {
    private static bool gating;

    [BeforeTasFrame]
    private static void Update() {
        if (!Manager.Running) {
            gating = false;
            return;
        }

        bool inLoadWait = InLoadWait();

        if (inLoadWait && !TasLoad.IsLoading) {
            TasLoad.Begin();
            gating = true;
        } else if (!inLoadWait && gating) {
            TasLoad.End();
            gating = false;
        }
    }

    /// True while an organic transition is doing its variable async fetch: in EXITING_LEVEL with the camera fade
    /// already elapsed. The fade (also EXITING_LEVEL, but WaitForFade still set) stays visible so its deltaTime
    /// countdown isn't frozen; the hero entry (ENTERING_LEVEL) stays visible too. Only the fetch between them — the
    /// part that stalls a variable number of input frames under fast-forward — is hidden.
    private static bool InLoadWait() {
        if (GameManager.instance is not { IsInSceneTransition: true } gm) {
            return false;
        }

        // String-compare the state to avoid pinning the GameState enum's namespace; sceneLoad is null outside a
        // transition and WaitForFade is cleared once the fade elapses.
        return gm.GameState.ToString() == "EXITING_LEVEL"
               && gm.GetFieldValue<SceneLoad>("sceneLoad") is { WaitForFade: false };
    }
}
