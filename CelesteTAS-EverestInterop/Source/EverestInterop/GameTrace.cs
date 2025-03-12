// ReSharper disable InconsistentNaming
// ReSharper disable CollectionNeverUpdated.Local
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NineSolsAPI;
using Newtonsoft.Json;
using RCGMaker.Test;
using TAS.EverestInterop;
using TAS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ObjectUtils = NineSolsAPI.Utils.ObjectUtils;
using Random = UnityEngine.Random;

namespace TAS.Tracer;

/// Game-specific tracing for Nine Sols: which engine calls to record into the frame-history, what
/// per-frame state to capture, and how to serialize game/engine types.
///
/// Everything generic lives in <see cref="TasTracer"/>. Hooks here are wired through attributes
/// (<see cref="TasTraceAddState"/>, <c>[EnableRun]</c>, <c>[HarmonyPatch]</c>); the only framework→game
/// pull is <see cref="JsonConverters"/>.
[HarmonyPatch]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
internal static class GameTrace {
    /// Converters for game/engine types, appended to the framework converters in TasTracer.SaveTrace.
    public static JsonConverter[] JsonConverters => [
        new FuncConverter<Collider2D>(ObjectUtils.ObjectComponentPath),
    ];

    #region traceVars

    internal static readonly (string Name, Func<object?> Get)[] TraceVarsChanged = [];

    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFrameVars = [
        // ("foo", () => MonsterManager.Instance.ClosetMonster.animator.transform.localPosition),
        // ("playerPos", () => Player.i?.transform.position),
        // ("playerState", () => Player.i?.fsm.State),
        // ("playerVel", () => Player.i?.VelX),
        // ("randomState", () => Random.state.s0 + Random.state.s1 + Random.state.s2 + Random.state.s3),
        // ("timeScale", () => Time.timeScale),
    ];

    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFramePausedVars = [];

    #endregion

    /// Per-frame game state added to each trace entry.
    [TasTraceAddState]
    private static void AddState(TraceData data) {
        var player = Player.i != null ? Player.i : null;
        var playerState = player?.fsm.FindMappingState(player.fsm.State);

        if (TasTracer.TraceLoadingFrames) {
            data.Add("IsLoading", GameInterop.IsLoading());
        }

        data.Add("Position", player?.transform.position);
        data.Add("Subpixel", player?.movementCounter);
        data.Add("Velocity", player?.Velocity);
        data.Add("FinalVelocity", player?.FinalVelocity);

        var animState = player?.animator.GetCurrentAnimatorStateInfo(0);
        if (animState != null) {
            var time = animState.Value.normalizedTime * animState.Value.length;
            var animTime = (float)Math.Truncate(time * 1e5) / 1e5;
            data.Add("AnimationTime", animTime);
        }

        data.Add("PlayerState", playerState);
        data.Add("Info", GameInfo.StudioInfo);

        if (TasTracer.HasFilter(TasTracerFilter.Random)) {
            data.Add("RandomState", DebugInfo.HashToAlphabet(Random.state));
        }
        if (TasTracer.HasFilter(TasTracerFilter.Monsters)) {
            data.Add("MonsterInfo",
                DebugInfo.GetMonsterInfotext(DebugInfo.DebugFilter.All & ~DebugInfo.DebugFilter.TraceRandom &
                                             ~DebugInfo.DebugFilter.TraceStatechange));
        }
    }

    /// Tracks the closest monster each late-update.
    [TasTraceLateUpdate]
    private static void LateUpdate() {
        var monsters = GameVersions.Select<MonsterBase[]>(GameVersions.SpeedrunPatch,
            [],
            [MonsterManager.Instance.ClosetMonster]);
        foreach (var monster in monsters) {
            TasTracer.AddFrameHistory(
                "ClosestMonster",
                monster.transform.position
            );
        }
    }

    // Game-specific roots for the reusable TraceVar probe (TasTracer): a path like "HeroController.cState.onGround"
    // resolves "HeroController" here, then TasTracer navigates the rest by reflection. Register game singletons.
    [EnableRun]
    private static void RegisterTraceRoots() {
        TasTracer.TraceRoots["HeroController"] = () => HeroController.SilentInstance;
        TasTracer.TraceRoots["GameManager"] = () => GameManager.instance;
        TasTracer.TraceRoots["PlayerData"] = () => PlayerData.instance;
    }

    #region Frame-history patches

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), [typeof(object)])]
    private static void DebugLog(object message) {
        if (!TasTracer.ShouldTrace()) return;

        Log.Info($"DebugLog: {message}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string)])]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string), typeof(object)])]
    [HarmonyPatch(typeof(GameCore), nameof(GameCore.currentCoreState), MethodType.Setter)]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(int), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(string), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(DOTween),
        nameof(DOTween.To),
        [typeof(DOGetter<float>), typeof(DOSetter<float>), typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.SetSimulationSpeed))]
    [HarmonyPatch(typeof(TimeScaleModifier), "SetSlowMotionFactor")]
    [HarmonyPatch(typeof(UniTask),
        nameof(UniTask.Delay),
        [typeof(TimeSpan), typeof(DelayType), typeof(PlayerLoopTiming), typeof(CancellationToken)])]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.DelayFrame))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitUntil))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitWhile))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitUntilCanceled))]
    [HarmonyPatch(typeof(Timer), nameof(Timer.AddTask), [typeof(Action), typeof(float), typeof(GameObject)])]
    [HarmonyPatch(typeof(Timer),
        nameof(Timer.AddTask),
        [typeof(Action), typeof(float), typeof(MonoBehaviour), typeof(string)])]
    [HarmonyPatch(typeof(Timer),
        nameof(Timer.AddTask),
        [typeof(Timer), typeof(Action), typeof(float), typeof(MonoBehaviour), typeof(string)])]
    [HarmonyPatch(typeof(MonsterBase), "AfterAnimationUpdate")]
    [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    [HarmonyPatch(typeof(AbstractEmitter), "Update")]
    [HarmonyPatch(typeof(Physics), nameof(Physics2D.Simulate))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(Physics), nameof(Physics.SyncTransforms))]
    [HarmonyPatch(typeof(Actor), nameof(Actor.OnRebindAnimatorMove))]
    [HarmonyPatch(typeof(MonsterBase), nameof(MonsterBase.OnRebindAnimatorMove))]
    [HarmonyPatch(typeof(Health), nameof(Health.BecomeInvincible))]
    [HarmonyPatch(typeof(Health), nameof(Health.RemoveInvincible))]
    [HarmonyPatch(typeof(Player), "EnablePushSlowCheck")]
    [HarmonyPatch(typeof(Player), "DisablePushSlowCheck")]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.MoveHExact))]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.MoveVExact))]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.SetPosition))]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.VelX), MethodType.Setter)]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.VelY), MethodType.Setter)]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.position), MethodType.Setter)]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.MovePosition))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    private static void FrameHistoryPatchMovement(PhysicsMover __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Movement)) return;

        if (!TasTracer.HasFilter(TasTracerFilter.Monsters) && __instance is MonsterBase) {
            return;
        }

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(StealthEngaging), "OnStateUpdate")]
    [HarmonyPatch(typeof(StealthEngaging), "PreAttackCheck")]
    [HarmonyPatch(typeof(StealthPreAttackState), "EnterSchemeCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "PrepareQueue")]
    [HarmonyPatch(typeof(MonsterBase), "EngageCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "FetchQueuedAttack")]
    private static void FrameHistoryPatchMonsters(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Monsters)) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PushAwayWall), "SetWallRect")]
    private static void FrameHistoryPatchWip(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Wip)) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Timer.DelayTask), "CompleteInternal")]
    private static void FrameHistoryTimerComplete(Timer.DelayTask __instance) {
        if (!TasTracer.ShouldTrace()) return;

        TasTracer.AddFrameHistory(["DelayTask.Complete", new StackTrace()]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.SetPosition))]
    private static void FrameHistoryPausedPatch(MonoBehaviour? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace()) return;
        if (Manager.CurrState is not Manager.State.Paused and not Manager.State.FrameAdvance) return;

        TasTracer.AddFrameHistoryPaused([
            $"{__instance} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Behaviour), nameof(Behaviour.enabled), MethodType.Setter)]
    private static void FrameHistorySetEnabledPatch(Behaviour __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.MBEnabled)) return;

        if (__instance is _2dxFX_Base or AkGameObj or RCGPostProcessManager or WOWROTATION or HighLightCamera) return;

        TasTracer.AddSortedFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
            ObjectUtils.ObjectPath(__instance.gameObject),
            __args[0],
            new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReplayTest), nameof(ReplayTest.LogTestState))]
    private static void LogTestStatePatch(MonoBehaviour m, object targetName, object stateName) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;
        if (m is PauseUIPanel) return;

        TasTracer.AddFrameHistory(["LogTestState", m.ToString(), targetName.ToString(), stateName.ToString(), new StackTrace()]);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AnimationMoveScaler), nameof(AnimationMoveScaler.AnimatorMoved))]
    [HarmonyFinalizer]
    private static void FrameHistoryPatchWithReturn(ref object __result, object? __instance,
        MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            __args,
            "->",
            __result,
            new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "Update")]
    private static void PlayerUpdate() {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            "Player.Update",
            Time.deltaTime,
            TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale,
            TimePauseManager.UITimeScaleModifier.finalTimeScale,
            TimePauseManager.GlobalSimulationSpeed,
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAnimatorEvents), nameof(PlayerAnimatorEvents.AnimationDone))]
    private static void HistoryAnimationDone(PlayerAnimationEventTag tag) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory(["PlayerAnimationDone", tag.ToString(), Player.i.AnimationVelocity]);
    }

    #endregion

    #region Sorted TriggerEnter2D

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PathFindTarget), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(PathFindTarget), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(PlayerSensor), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(PlayerSensor), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(TriggerDetector), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(TriggerDetector), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(MonsterPushAway), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(MonsterPushAway), "OnTriggerExit2D")]
    private static void FrameHistorySortedEvents(MonoBehaviour __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Trigger)) return;

        var collider = (Collider2D)__args[0];
        if ((!TasTracer.HasFilter(TasTracerFilter.Monsters) && __instance.transform.parent.name == "MonsterCore") ||
            collider.transform.parent.name == "MonsterCore") {
            return;
        }

        DebugInfo.frameEvents.Add($"{__originalMethod.Name}({__instance.name}, {collider.name})");

        TasTracer.AddSortedFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
            ObjectUtils.ObjectPath(__instance.gameObject),
            ObjectUtils.ObjectPath(collider.gameObject),
            new StackTrace(),
        ]);
    }

    #endregion
}
