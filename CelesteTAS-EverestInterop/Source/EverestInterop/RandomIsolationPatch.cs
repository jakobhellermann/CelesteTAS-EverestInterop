using System.Reflection;
using HarmonyLib;
using TAS.Tracer;
using Random = UnityEngine.Random;

namespace TAS;

/// Isolates the gameplay RNG from non-gameplay (cosmetic/audio) consumers that draw from the global
/// UnityEngine.Random. Those would otherwise advance the shared RNG and desync the TAS — e.g.:
///   - RandomAudioClipTable.Select* — random pitch/clip/volume for SFX (jump sound etc.)
///   - DeactivateAfter2dtkAnimation.OnEnable — random sprite flip when a tk2d animation object enables
///
/// We save Random.state before each such call and restore it after (only while a TAS runs), so the
/// effect still happens with its drawn value but the gameplay RNG is left untouched.
///
/// Found via the Random trace (GameTrace.RandomTrace): the call shows up in the frame-history with a
/// stacktrace; if it's not gameplay-relevant, add it here. Decomp: ~/dev/hkss/game/decomp/assembly-csharp.
[HarmonyPatch]
public static class RandomIsolationPatch {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectClip))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectClipIgnoreProbability))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectPitch))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectVolume))]
    [HarmonyPatch(typeof(DeactivateAfter2dtkAnimation), "OnEnable")]
    [HarmonyPatch(typeof(AudioManager), "BeginApplyAtmosCue")]
    // HeroAnimationController.ResetIdleLook draws Random.Range(10,15) for nextIdleLookTime — the timer to the hero's
    // next cosmetic idle look-around. It's re-drawn from scene-entry coroutines (EnterHeroSubHorizontal) whose start
    // ordering varies, so its draw lands on a variable frame and shifts the shared gameplay RNG. Purely visual — isolate.
    [HarmonyPatch(typeof(HeroAnimationController), nameof(HeroAnimationController.ResetIdleLook))]
    // CameraShakeProfile.GetOffset draws Random.insideUnitCircle for the per-frame camera-shake offset (fires on combat
    // hits etc.). Camera framing never affects gameplay/physics — isolate (cf. the CameraShake FSM + CoroutineTracker).
    [HarmonyPatch(typeof(CameraShakeProfile), nameof(CameraShakeProfile.GetOffset))]
    // GeoControl.PlayGleam re-rolls the geo pickup's cosmetic gleam delay (gleamDelayRange, a MinMaxFloat draw) on a
    // deltaTime schedule in OnUpdate. Dropped geo appears mid-combat, so the gleam lands on a load-timing-variable
    // frame — purely a sprite sparkle, isolate.
    [HarmonyPatch(typeof(GeoControl), "PlayGleam")]
    // Rigidbody2DDisturberImpulse.Impulse applies a random-direction/magnitude physics impulse to its configured decor
    // rigidbodies, fired from the ambient SetTriggerRandom.TriggerRoutine loop (itself cosmetic churn). Draws land on a
    // load-timing-variable frame; isolating keeps the gameplay RNG stream consistent (the decor jiggle may still vary
    // visually, which is fine — it touches no hero/enemy state).
    [HarmonyPatch(typeof(Rigidbody2DDisturberImpulse), nameof(Rigidbody2DDisturberImpulse.Impulse))]
    // ShakePositionV2.UpdateShaking (a PlayMaker action: "randomly shakes a GameObject's position by a diminishing
    // amount") draws Random.Range(-1,1)×3 for the shake offset then restores the position. Fires event-triggered on a
    // load-timing-variable frame, and its draws were shifting the RNG right before a gameplay Breakable.Break shard
    // draw. Purely visual — isolate (covers both its OnEnter and OnUpdate call sites).
    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.ShakePositionV2), "UpdateShaking")]
    // Pooled cosmetic effects spawned during a fight (via ObjectPool → OnEnable) that draw RNG for visuals/audio and
    // shift the shared RNG on a load-timing-variable spawn frame — the boss_resume RandomState divergence:
    //   SilkChunk.SetAnims — random anim frame for silk-debris chunks.
    //   AudioPlayerOneShotSingle.DoPlayRandomClip — random SFX pitch/clip (cf. RandomAudioClipTable above).
    // (VectorCurveAnimator.StartAnimation is isolated separately below — its type can't be named here since its base
    // class lives in an assembly this project doesn't reference.)
    [HarmonyPatch(typeof(SilkChunk), "SetAnims")]
    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle), "DoPlayRandomClip")]
    private static void SaveRandom(ref Random.State __state) {
        TasTracer.RandomIsolationDepth++;
        __state = Random.state;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectClip))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectClipIgnoreProbability))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectPitch))]
    [HarmonyPatch(typeof(RandomAudioClipTable), nameof(RandomAudioClipTable.SelectVolume))]
    [HarmonyPatch(typeof(DeactivateAfter2dtkAnimation), "OnEnable")]
    [HarmonyPatch(typeof(AudioManager), "BeginApplyAtmosCue")]
    [HarmonyPatch(typeof(HeroAnimationController), nameof(HeroAnimationController.ResetIdleLook))]
    [HarmonyPatch(typeof(CameraShakeProfile), nameof(CameraShakeProfile.GetOffset))]
    [HarmonyPatch(typeof(GeoControl), "PlayGleam")]
    [HarmonyPatch(typeof(Rigidbody2DDisturberImpulse), nameof(Rigidbody2DDisturberImpulse.Impulse))]
    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.ShakePositionV2), "UpdateShaking")]
    [HarmonyPatch(typeof(SilkChunk), "SetAnims")]
    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle), "DoPlayRandomClip")]
    private static void RestoreRandom(Random.State __state) {
        // Only isolate during playback — outside a TAS, these should randomize normally.
        if (Manager.Running) {
            Random.state = __state;
        }

        TasTracer.RandomIsolationDepth--;
    }

    /// VectorCurveAnimator.StartAnimation(bool) draws a random start offset (startElapsed.GetRandomValue) for a visual
    /// curve animation, spawned from the ObjectPool during a fight — a boss_resume RandomState leak. Its type can't be
    /// named in a HarmonyPatch attribute (its base class lives in an assembly this project doesn't reference), so
    /// resolve the method by string. Same save/restore-around-the-draw pattern as the fixed-method isolations.
    [HarmonyPatch]
    private static class VectorCurveAnimatorIsolation {
        private static MethodBase TargetMethod() =>
            AccessTools.Method("VectorCurveAnimator:StartAnimation", [typeof(bool)]);

        private static void Prefix(ref Random.State __state) {
            TasTracer.RandomIsolationDepth++;
            __state = Random.state;
        }

        private static void Postfix(Random.State __state) {
            if (Manager.Running) {
                Random.state = __state;
            }

            TasTracer.RandomIsolationDepth--;
        }
    }

    /// SetTriggerRandom.TriggerRoutine is a cosmetic ambient loop: it waits a random interval (Random.Range(1,3)),
    /// fires an animator trigger + a Rigidbody2DDisturberImpulse (decor jiggle), repeat. It's denylisted from the
    /// coroutine display, but its own interval draw was never isolated — and it lands on a load-timing-variable frame,
    /// which is the cluster-determining greymoor RNG divergence (DGNG vs QQDT). Wrap its MoveNext so the interval draw
    /// (and the synchronous impulse it triggers) don't advance the gameplay RNG.
    [HarmonyPatch]
    private static class SetTriggerRandomIsolation {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SetTriggerRandom), "TriggerRoutine"));

        private static void Prefix(ref Random.State __state) {
            TasTracer.RandomIsolationDepth++;
            __state = Random.state;
        }

        private static void Postfix(Random.State __state) {
            if (Manager.Running) {
                Random.state = __state;
            }

            TasTracer.RandomIsolationDepth--;
        }
    }

    /// RandomLooptk2dAnimator.Animate is a coroutine — its Random.Range(1,2) (random loop delay) is
    /// drawn inside the generated MoveNext, between yields, so it can't be wrapped at the method level.
    /// Patch the state-machine's MoveNext instead and save/restore around each step.
    [HarmonyPatch]
    private static class RandomLoopIsolation {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(RandomLooptk2dAnimator), "Animate"));

        private static void Prefix(ref Random.State __state) {
            TasTracer.RandomIsolationDepth++;
            __state = Random.state;
        }

        private static void Postfix(Random.State __state) {
            if (Manager.Running) {
                Random.state = __state;
            }

            TasTracer.RandomIsolationDepth--;
        }
    }

    /// ShellShard.EnableShineEffect is a cosmetic coroutine that draws RNG (start delay, effect rotation, next-spawn
    /// frequency) on a Time.deltaTime schedule in a while-loop, spawning shine effects on global pool objects. Those
    /// spawns do nothing gameplay-relevant, so isolating its RNG is enough to keep it from desyncing the run. Same
    /// MoveNext approach as RandomLoopIsolation.
    [HarmonyPatch]
    private static class ShineEffectIsolation {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ShellShard), "EnableShineEffect"));

        private static void Prefix(ref Random.State __state) {
            TasTracer.RandomIsolationDepth++;
            __state = Random.state;
        }

        private static void Postfix(Random.State __state) {
            if (Manager.Running) {
                Random.state = __state;
            }

            TasTracer.RandomIsolationDepth--;
        }
    }

    /// ShineAnimSequence.ShineSequence is a cosmetic coroutine that draws Random.Range for its inter-shine delay in a
    /// while-loop. Isolate the RNG at the MoveNext level so it doesn't advance the gameplay RNG.
    [HarmonyPatch]
    private static class ShineSequenceIsolation {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ShineAnimSequence), "ShineSequence"));

        private static void Prefix(ref Random.State __state) {
            TasTracer.RandomIsolationDepth++;
            __state = Random.state;
        }

        private static void Postfix(Random.State __state) {
            if (Manager.Running) {
                Random.state = __state;
            }

            TasTracer.RandomIsolationDepth--;
        }
    }

    /// Cosmetic PlayMaker FSMs that draw from the global RNG on a render-cadence / load-timing-variable schedule,
    /// desyncing the shared gameplay RNG stream. Scoped by instance (FSM name + owner) since the drawing actions
    /// (RandomFloat/SendRandomEvent) are generic ones gameplay FSMs also use. Members (confirmed cosmetic — verified
    /// via the FSM-attributed Random trace, GameTrace.FsmContext):
    ///   - "Motion" on a creature's "Sprite" child — the left-right sprite buzz (e.g. Mitefly). Moves only the sprite,
    ///     not the hitbox; each L<->R turn draws RandomFloat for tween time + target.
    ///   - "CameraShake" on CameraParent — camera-shake offsets (Random.Range(-1,1)). Camera framing never affects
    ///     gameplay/physics (cf. the CameraShake* entries in CoroutineTracker's denylist).
    private static bool IsCosmeticFsm(PlayMakerFSM fsm) =>
        (fsm.FsmName == "Motion" && fsm.gameObject.name == "Sprite")
        || fsm.FsmName == "CameraShake";

    [HarmonyPatch(typeof(PlayMakerFSM), "Update")]
    private static class SpriteMotionIsolation {
        // A static save (not Harmony __state): PlayMakerFSM.Update carries a second __state-using patch
        // (PlayMakerFsmContext) and the paired nullable-struct __state was not surviving to the postfix — the restore
        // and depth decrement were silently skipped, so the Motion draw leaked and the isolation depth ran away.
        // FSM updates are single-threaded and non-nested for our purposes, so one static holds the save; a finalizer
        // (runs even if Update throws) guarantees the restore + decrement.
        private static Random.State? saved;

        private static void Prefix(PlayMakerFSM __instance) {
            if (Manager.Running && IsCosmeticFsm(__instance)) {
                saved = Random.state;
                TasTracer.RandomIsolationDepth++;
            }
        }

        [HarmonyFinalizer]
        private static void Finalizer() {
            if (saved is { } state) {
                Random.state = state;
                saved = null;
                TasTracer.RandomIsolationDepth--;
            }
        }
    }
}
