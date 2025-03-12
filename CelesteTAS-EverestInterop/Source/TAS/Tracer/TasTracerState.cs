using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NineSolsAPI.Utils;
using MonsterLove.StateMachine;
using RCGMaker.Test;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using TAS.Tracer;
using UnityEngine;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

namespace TAS;

[Flags]
public enum TasTracerFilter {
    None = 0,
    TraceVarsThroughFrame = 1 << 0,
    Random = 1 << 1,
    Enabled = 1 << 2,
}

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class TasTracerState {
    public const TasTracerFilter Filter =
        TasTracerFilter.TraceVarsThroughFrame
        | TasTracerFilter.Random;
    // | TasTracerFilter.Enabled;

    private static List<(string, Func<object?>)> traceVarsThroughFrame = [
        // ("playerPos", () => Player.i?.transform.position),
        /*("animation", () => {
            if (Player.i == null) return null;

            var state = Player.i.animator.GetCurrentAnimatorStateInfo(0);
            return (state.fullPathHash, state.normalizedTime);
        }),
        ("playerState", () => Player.i?.fsm.State),
        ("playerPos", () => Player.i?.transform.position),
        ("playerVel", () => Player.i?.VelX),
        ("playerBreaking", () => Player.i?.IsBreaking),
        ("playerMoveX", () => Player.i?.moveX),
        ("randomState", () => Random.state.s0 + Random.state.s1 + Random.state.s2 + Random.state.s3),
        // ("animVel", () => Player.i?.AnimationVelocity),
        // ("TimeScaleRCG", () => RCGTime.timeScale),
        // ("TimeScaleRCGGlob", () => RCGTime.GlobalSimulationSpeed),
        // ("TimeScale", () => Time.timeScale),
        /*("attacktriggerenabled", () => {
            var path =
                "GameCore(Clone)/RCG LifeCycle/PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront@TriggerDetector";
            var obj = (TriggerDetector?)ObjectUtils.LookupObjectComponentPath(path);
            if (obj == null) return null;

            var collider = obj.GetComponent<Collider2D>();

            return new object?[] { obj.enabled, obj.transform.position, collider.bounds.center };
        }),*/
        /*("effectreceivingboxcollider2d", () => {
            var path =
                "GameLevel/Room/Prefab/EventBinder/General Boss Fight FSM Object Variant/FSM Animator/LogicRoot/---Boss---/Boss_Yi Gung/MonsterCore/Animator(Proxy)/Animator/[@]EffectReceivingCollider@BoxCollider2D";
            var obj = (BoxCollider2D?)ObjectUtils.LookupObjectComponentPath(path);
            if (obj == null) return null;

            return new object?[] {
                obj.enabled,
                obj.transform.position,
                obj.bounds.center,
                obj.offset,
            };
        }),*/

        /*("closestMonsterAnim",
            () => MonsterManager.Instance?.ClosetMonster?.animator is { } animator
                ? AnimatorSnapshot.Snapshot(animator)
                : null),*/
        /*("closestMonsterArea",
            () => MonsterManager.Instance?.ClosetMonster?.pathFindAgent?.currentArea is { } area
                ? ObjectUtils.ObjectPath(area.gameObject)
                : null),*/
        /*("playerArea",
            () => Player.i?.pathFindAgent?.currentArea is { } area
                ? ObjectUtils.ObjectPath(area.gameObject)
                : null),
        // ("", () => UnityEngine.Object.FindObjectOfType<SceneConnectionPoint>()?.IsFromThisConnection().ToString()),
        ("timeScale", () => Time.timeScale),*/
    ];


    private static List<(string, Func<object?>)> traceVarsThroughFramePaused = [
        /*("effectreceivingboxcollider2d", () => {
            var path =
                "GameLevel/Room/Prefab/EventBinder/General Boss Fight FSM Object Variant/FSM Animator/LogicRoot/---Boss---/Boss_Yi Gung/MonsterCore/Animator(Proxy)/Animator/[@]EffectReceivingCollider@BoxCollider2D";
            var obj = (BoxCollider2D?)ObjectUtils.LookupObjectComponentPath(path);
            if (obj == null) return null;

            return new object?[] {
                obj.enabled,
                obj.transform.position,
                obj.bounds.center,
                obj.offset,
            };
        }),*/
    ];

    private static List<object?[]> frameHistory = [];
    internal static List<object?[]> FrameHistoryPaused = [];

    #region Patches

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string)])]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string), typeof(object)])]
    // [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutineManaged))]
    // [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutineManaged2))]
    [HarmonyPatch(typeof(GameCore), nameof(GameCore.currentCoreState), MethodType.Setter)]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(int), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(string), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.MoveHExact))]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.SetPosition))]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Update))]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int), typeof(float)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int), typeof(int), typeof(float)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int), typeof(int)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int)])]
    [HarmonyPatch(typeof(DOTween),
        nameof(DOTween.To),
        [typeof(DOGetter<float>), typeof(DOSetter<float>), typeof(float), typeof(float)])]
    // [HarmonyPatch(typeof(DOTween), nameof(DOTween.To), [typeof(DOGetter<Vector2>), typeof(DOSetter<Vector2>), typeof(Vector2), typeof(Vector2)])]
    // [HarmonyPatch(typeof(DOTween), nameof(DOTween.To), [typeof(DOGetter<Vector3>), typeof(DOSetter<Vector3>), typeof(Vector3), typeof(Vector3)])]
    // [HarmonyPatch(typeof(PathFindTarget), "OnTriggerEnter2D")]
    // [HarmonyPatch(typeof(PathFindTarget), "OnTriggerExit2D")]
    // [HarmonyPatch(typeof(TriggerDetector), "OnTriggerEnter2D")]
    // [HarmonyPatch(typeof(TriggerDetector), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.SetSimulationSpeed))]
    // [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.SetTimeScaleUnsafe))]
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
    [HarmonyPatch(typeof(StealthEngaging), "OnStateUpdate")]
    [HarmonyPatch(typeof(StealthEngaging), "PreAttackCheck")]
    [HarmonyPatch(typeof(StealthPreAttackState), "EnterSchemeCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "PrepareQueue")]
    [HarmonyPatch(typeof(MonsterBase), "EngageCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "FetchQueuedAttack")]
    // [HarmonyPatch(typeof(FSMStateMachineRunner), "UpdateProxy")]
    [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    [HarmonyPatch(typeof(AbstractEmitter), "Update")]
    [HarmonyPatch(typeof(Physics), nameof(Physics2D.Simulate))]
    [HarmonyPatch(typeof(Physics), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(Actor), nameof(Actor.OnRebindAnimatorMove))]
    //[HarmonyPatch(typeof(TriggerDetector), "UpdateImplement")]
    // [HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
    // [HarmonyPatch(typeof(EffectDealer), "DelayShootEffect")]
    // [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.TimePause))]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Update))]
    //[HarmonyPatch(typeof(Transform), nameof(Transform.position), MethodType.Setter)]
    //[HarmonyPatch(typeof(Transform), nameof(Transform.localPosition), MethodType.Setter)]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.SetPosition))]
    private static void FrameHistoryPausedPatch(MonoBehaviour? __instance, MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;

        AddFrameHistoryPaused([
            $"{__instance} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace(),
        ]);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Behaviour), nameof(Behaviour.enabled), MethodType.Setter)]
    private static void
        FrameHistorySetEnabledPatch(Behaviour __instance, MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;
        if (!Filter.HasFlag(TasTracerFilter.Enabled)) return;

        if (__instance is _2dxFX_Base or AkGameObj or RCGPostProcessManager or WOWROTATION or HighLightCamera) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            __args[0],
            new StackTrace(),
        ]);
        AddFrameHistoryPaused([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            __args[0],
            new StackTrace(),
        ]);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReplayTest), nameof(ReplayTest.LogTestState))]
    private static void LogTestStatePatch(MonoBehaviour m, object targetName, object stateName) {
        if (!Manager.Running) return;
        if (m is PauseUIPanel) return;

        AddFrameHistory(["LogTestState", m.ToString(), targetName.ToString(), stateName.ToString(), new StackTrace()]);
    }


    /*[HarmonyPostfix]
    // [HarmonyPatch(typeof(EffectDealer), nameof(EffectDealer.GetReceivers))]
    [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    [HarmonyPatch(typeof(EffectDealer), nameof(EffectDealer.CanHitReceiver), [typeof(EffectReceiver)])]
    [HarmonyFinalizer]
    private static void FrameHistoryPatchWithReturn(ref object __result, object? __instance,
        MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            "->",
            __result,
            new StackTrace(),
        ]);
    }*/


    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbstractEmitter), "Update")]
    private static void Test(AbstractEmitter __instance) {
        AddFrameHistory("emitter path", ObjectUtils.ObjectComponentPath(__instance));
    }


    [HarmonyPrefix]
    // [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    private static void UpdateImplement(EffectDealer __instance, Collider2D col) => frameHistory.Add([
        "HitEffectImGoingInsane",
        __instance.lastFrameReceivers.Count,
        __instance.lastFrameReceivers.Count > 0 ? __instance.lastFrameReceivers[0] : null,
        "this",
        __instance.thisFrameReceivers.Count,
        __instance.thisFrameReceivers.Count > 0 ? __instance.thisFrameReceivers[0] : null,
        ObjectUtils.ObjectComponentPath(__instance),
        EffectDealer.GetReceivers(col).Select(x => $"{__instance.lastFrameReceivers.Contains(x)}").Join(),
        EffectDealer.GetReceivers(col).Select(x => x == null ? "null" : ObjectUtils.ObjectComponentPath(x)).Join(),
    ]);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EffectDealer), "HitReceiverPost")]
    private static void UpdateImplement2(EffectDealer __instance) => frameHistory.Add([
        "HitEffectImGoingInsane2",
        __instance.lastFrameReceivers.Count,
        __instance.lastFrameReceivers.Count > 0 ? __instance.lastFrameReceivers[0] : null,
        "this",
        __instance.thisFrameReceivers.Count,
        __instance.thisFrameReceivers.Count > 0 ? __instance.thisFrameReceivers[0] : null,
    ]);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TriggerDetector), "UpdateImplement")]
    private static void UpdateImplement3(TriggerDetector __instance) => frameHistory.Add([
        "UpdateImplement",
        __instance.GetFieldValue<List<Collider2D>>("enteredColliders")!
            .Select(x => $"{x?.name} {x?.gameObject.activeInHierarchy}").Join(),
        __instance.GetFieldValue<EffectDealer[]>("effectDealers")!
            .Select(x => $"{x?.name} {x?.gameObject.activeInHierarchy}").Join(),
        new StackTrace(),
    ]);


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "Update")]
    private static void PlayerUpdate() =>
        frameHistory.Add(["Player.Update", Time.deltaTime]);

    [HarmonyPatch(typeof(MonsterState), nameof(MonsterState.AnimationEvent))]
    private static void BossAnimationEvent(MonsterState __instance, AnimationEvents.AnimationEvent e) {
        frameHistory.Add([$"BossAnimationEvent {__instance}", e.ToString()]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAnimatorEvents), nameof(PlayerAnimatorEvents.AnimationDone))]
    private static void HistoryAnimationDone(PlayerAnimationEventTag tag) {
        frameHistory.Add(["PlayerAnimationDone", tag.ToString(), Player.i.AnimationVelocity]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerNormalState), nameof(PlayerNormalState.OnStateEnter))]
    private static void PlayerNormalStateE(PlayerNormalState __instance) {
        frameHistory.Add(["PlayerNormalState", Player.i.IsOnGround, AnimatorSnapshot.Snapshot(Player.i.animator)]);
    }

    #endregion

    public static void Clear() {
        frameHistory.Clear();
    }

    public static void AddFrameHistory(params object?[] args) {
        frameHistory.Add(args);
    }

    public static void AddFrameHistoryPaused(params object?[] args) {
        FrameHistoryPaused.Add(args);
    }

    internal static void TraceVarsThroughFrame(string phase) {
        // if (!Filter.HasFlag(TasTracerFilter.TraceVarsThroughFrame)) return;

        try {
            if (traceVarsThroughFrame.Count > 0) {
                var vars = traceVarsThroughFrame.ToDictionary(x => x.Item1, x => x.Item2.Invoke());
                AddFrameHistory($"ThroughFrame-{phase}", vars);
            }

            if (traceVarsThroughFramePaused.Count > 0) {
                var vars = traceVarsThroughFramePaused.ToDictionary(x => x.Item1, x => x.Item2.Invoke());
                AddFrameHistoryPaused($"ThroughFrame-{phase}", vars);
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    [EnableRun]
    private static void EnableRun() {
        frameHistory.Clear();
        FrameHistoryPaused.Clear();

        TasTracer.TraceEvent($"EnableRun");
        Time.timeScale = 1;
    }

    [DisableRun]
    private static void DisableRun() {
        TasTracer.TraceEvent("DisableRun");
    }

    [BeforeTasFrame]
    private static void BeforeTasFrame() {
        frameHistory.Clear();
        FrameHistoryPaused.Clear();
    }

    [TasTraceAddState]
    private static void ExtendState(TraceData data) {
        var player = Player.i != null ? Player.i : null;
        var playerState = player?.fsm.FindMappingState(player.fsm.State);

        data.Add("Position", player?.transform.position);
        data.Add("Subpixel", player?.movementCounter);
        data.Add("Velocity", player?.Velocity);
        data.Add("FinalVelocity", player?.FinalVelocity);
        data.Add("IsLoading", Manager.IsLoading());
        data.Add("AnimationTime", player?.animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
        data.Add("PlayerState", playerState);
        data.Add("Info", GameInfo.StudioInfo);
        data.Add("MonsterInfo", DebugInfo.GetMonsterInfotext(DebugInfo.DebugFilter.All));

        data.Add("FrameHistory", new List<object?[]>(frameHistory));
    }
}
