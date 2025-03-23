using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MonsterLove.StateMachine;
using NineSolsAPI.Utils;
using RCGMaker.Test;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using TAS.Tracer;
using UnityEngine;
// ReSharper disable InconsistentNaming

namespace TAS;

[Flags]
public enum TasTracerFilter {
    None = 0,
    TraceVarsThroughFrame = 1 << 0,
    Random = 1 << 1,
}

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class TasTracerState {
    public const TasTracerFilter Filter =
        TasTracerFilter.TraceVarsThroughFrame
        | TasTracerFilter.Random
        ;

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
        ("queue", GetMonsterQueue),*/
        // ("animVel", () => Player.i?.AnimationVelocity),
        // ("TimeScaleRCG", () => RCGTime.timeScale),
        // ("TimeScaleRCGGlob", () => RCGTime.GlobalSimulationSpeed),
        // ("TimeScale", () => Time.timeScale),
        
        /*("closestMonsterAnim",
            () => MonsterManager.Instance?.ClosetMonster.animator is { } animator
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
    ];

    /*public static object? GetMonsterQueue() {
        var monster = MonsterManager.Instance.ClosetMonster;
        if (monster == null) return null;

        if (monster.fsm.FindMappingState(monster.fsm.State) is BossGeneralState s) {
            return s.QueuedAttacks.Select(x => x.ToString()).ToList();
        }
        if (monster.fsm.FindMappingState(MonsterBase.States.JumpBack) is BossGeneralState jb) {
            return new object[] { "jb", jb.QueuedAttacks.Select(x => x.ToString()).ToList() };
        }

        return "otherstate";
    }*/


    private static List<object?[]> frameHistory = [];
    internal static List<object?[]> FrameHistoryPaused = [];

    #region Patches

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReplayTest), nameof(ReplayTest.LogTestState))]
    private static void LogTestStatePatch(object[] __args) {
        if (!Manager.Running) return;
        
        if(__args[0] is PauseUIPanel) return;
        AddFrameHistory(["LogTestState", ..__args.Select(x => x.ToString()), new StackTrace()]);
    }

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
    [HarmonyPatch(typeof(PushAwayWall), "Update")]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Update))]
    [HarmonyPatch(typeof(DOTween), nameof(DOTween.To), [typeof(DOGetter<float>), typeof(DOSetter<float>), typeof(float), typeof(float)])]
    // [HarmonyPatch(typeof(DOTween), nameof(DOTween.To), [typeof(DOGetter<Vector2>), typeof(DOSetter<Vector2>), typeof(Vector2), typeof(Vector2)])]
    // [HarmonyPatch(typeof(DOTween), nameof(DOTween.To), [typeof(DOGetter<Vector3>), typeof(DOSetter<Vector3>), typeof(Vector3), typeof(Vector3)])]
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    // [HarmonyPatch(typeof(PathFindTarget), "OnTriggerEnter2D")]
    // [HarmonyPatch(typeof(PathFindTarget), "OnTriggerExit2D")]
    // [HarmonyPatch(typeof(TriggerDetector), "OnTriggerEnter2D")]
    // [HarmonyPatch(typeof(TriggerDetector), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.Delay), [typeof(TimeSpan), typeof(DelayType), typeof(PlayerLoopTiming), typeof(CancellationToken)])]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.DelayFrame))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitUntil))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitWhile))]
    [HarmonyPatch(typeof(UniTask), nameof(UniTask.WaitUntilCanceled))]
    [HarmonyPatch(typeof(MonsterBase), "AfterAnimationUpdate")]
    [HarmonyPatch(typeof(StealthEngaging), "OnStateUpdate")]
    [HarmonyPatch(typeof(StealthEngaging), "PreAttackCheck")]
    [HarmonyPatch(typeof(StealthPreAttackState), "EnterSchemeCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "PrepareQueue")]
    [HarmonyPatch(typeof(MonsterBase), "EngageCheck")]
    [HarmonyPatch(typeof(BossGeneralState), "FetchQueuedAttack")]
    [HarmonyPatch(typeof(FSMStateMachineRunner), "UpdateProxy")]
    // [HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
    // [HarmonyPatch(typeof(EffectDealer), "DelayShootEffect")]
    [HarmonyPatch(typeof(TriggerDetector), "UpdateImplement")]
    // [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.SetTimeScaleUnsafe))]
    // [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.TimePause))]
    [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.SetSimulationSpeed))]
    private static void FrameHistoryPatch(Actor __instance, MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;
        
        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}", ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Update))]
    private static void FrameHistoryPausedPatch(Actor __instance, MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;
        
        AddFrameHistoryPaused([
            $"{__instance} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace(),
        ]);
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TriggerDetector), "UpdateImplement")]
    private static void UpdateImplement(TriggerDetector __instance) => frameHistory.Add(["UpdateImplement", __instance.GetFieldValue<List<Collider2D>>("enteredColliders").Count, __instance.GetFieldValue<bool>("_hasNewColEntered")]);
    

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "Update")]
    private static void PlayerUpdate() => frameHistory.Add(["Player.Update", Time.deltaTime]);

    [HarmonyPrefix]
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
        if (!Filter.HasFlag(TasTracerFilter.TraceVarsThroughFrame)) return;

        if (traceVarsThroughFrame.Count > 0) {
            var vars = traceVarsThroughFrame.ToDictionary(x => x.Item1, x => x.Item2.Invoke());
            AddFrameHistory($"ThroughFrame-{phase}", vars);
        }
        if (traceVarsThroughFramePaused.Count > 0) {
            var vars = traceVarsThroughFramePaused.ToDictionary(x => x.Item1, x => x.Item2.Invoke());
            AddFrameHistory($"ThroughFrame-{phase}", vars);
            AddFrameHistoryPaused($"ThroughFrame-{phase}", vars);
        }
    }

    [EnableRun]
    private static void EnableRun() {
        frameHistory.Clear();
        FrameHistoryPaused.Clear();

        TasTracer.TraceEvent($"EnableRun frame={Time.frameCount})");
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
        data.Add("VelX", player?.VelX);
        data.Add("VelY", player?.VelY);
        data.Add("AnimationVelocity", player?.AnimationVelocity);
        data.Add("FinalVelocity", player?.FinalVelocity);
        data.Add("JumpFalseTimer", player?.jumpWasPressedCondition.FalseTimer);
        data.Add("CanJump", player?.CanJump);
        data.Add("IsLoading", Manager.IsLoading());
        data.Add("AnimationTime", player?.animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
        // data.Add("PlayerState", playerState);
        data.Add("Info", GameInfo.StudioInfo);
        data.Add("MonsterInfo", DebugInfo.GetMonsterInfotext(DebugInfo.DebugFilter.All));

        data.Add("FrameHistory", new List<object?[]>(frameHistory));
    }
}
