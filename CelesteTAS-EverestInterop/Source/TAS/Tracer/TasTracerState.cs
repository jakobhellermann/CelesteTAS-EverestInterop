using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using NineSolsAPI;
using NineSolsAPI.Utils;
using RCGMaker.Test;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using TAS.EverestInterop;
using System.Threading;
using TAS.Input.Commands;
using TAS.Tracer;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ObjectUtils = NineSolsAPI.Utils.ObjectUtils;
using Random = UnityEngine.Random;

// ReSharper disable CollectionNeverUpdated.Local
// ReSharper disable InconsistentNaming

namespace TAS;

[Flags]
public enum TasTracerFilter {
    Miscellaneous = 1 << 1,
    Monsters = 1 << 2,
    Random = 1 << 2,
    Movement = 1 << 4,
    Wip = 1 << 5,
    TraceVarsThroughFrame = 1 << 6,
    MBEnabled = 1 << 7,
    Trigger = 1 << 8,
}

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class TasTracerState {
    private static TasTracerFilter Filter => TasMod.Instance.ConfigTasTraceFilter.Value;
    private static bool FrameHistoryEnabled => TasMod.Instance.ConfigTasTraceFrameHistory.Value;

    public static bool ShouldTrace(TasTracerFilter? filter = null) {
        if (!FrameHistoryEnabled || !Manager.Running) return false;
        if (filter != null && !Filter.HasFlag(filter)) return false;
        // if (LoadCommand.IsLoading) return false;

        return true;
    }


    private static Dictionary<string, object?> _traceVars = [];

    #region traceVars

    private static Dictionary<string, Func<object?>> traceVarsChanged = new() {
        // { "pos", () => Player.i?.transform.position },
        /*{
            "collider", () => {
                var obj = ObjectUtils.LookupPath(
                    "GameLevel/Room/Prefab/EventBinder/General Boss Fight FSM Object Variant/FSM Animator/LogicRoot/---Boss---/Boss_Yi Gung/MonsterCore/Animator(Proxy)/Animator/LogicRoot/Sensors/1_AttackRunToSensor");
                if (!obj) return null;

                return new object?[] { obj?.transform.position, obj?.GetComponent<BoxCollider2D>() };
            }
        },*/
    };

    private static (string, Func<object?>)[] traceVarsThroughFrame = [
        ("monsterMoveScaler", () => {
            if (MonsterManager.Instance.ClosetMonster is not { } monster) return null;

            var moveScaler = monster.animator.transform.Find("LogicRoot/MoveScale").GetComponent<AnimationMoveScaler>();

            return new object?[] {
                moveScaler.GetFieldValue<float>("_moveXScale"),
                moveScaler.IsContinuousUpdate,
                moveScaler.EvaluateOffset,
                moveScaler.MoveXMinScale,
                moveScaler.MoveXMaxScale,
            };
        }),
        // ("foo", () => MonsterManager.Instance.ClosetMonster.animator.transform.localPosition),
        // ("foo", () => Player.i.fooAttackWasPressedCondition.FalseTimer),
        /*("closest", () => {
            if (MonsterManager.Instance.ClosetMonster is not { } monster) return null;

            return new object?[] { monster.transform.position, monster._rigidbody2D.position, monster.animator.deltaPosition };
        }),
        ("colls", () => Player.i.pushAwayCollider.colliders.Select(ObjectUtils.ObjectComponentPath).ToArray()),*/
        // ("playerPos", () => Player.i?.transform.position),
        /*("animation", () => {
            if (Player.i == null) return null;

            var state = Player.i.animator.GetCurrentAnimatorStateInfo(0);
            return (state.fullPathHash, state.length * state.normalizedTime);
        }),
        ("ts", () => Time.timeScale),
        ("dt", () => Time.deltaTime),
        ("dtt", () => TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale),*/
        /*("playerState", () => Player.i?.fsm.State),
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


    private static (string, Func<object?>)[] traceVarsThroughFramePaused = [
        // ("foo", () => Player.i.fooAttackWasPressedCondition.FalseTimer),
        /*("closest", () => {
            if (MonsterManager.Instance.ClosetMonster is not { } monster) return null;

            return new object?[] { monster.transform.position, monster._rigidbody2D.position, monster.animator.deltaPosition };
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
    ];

    #endregion

    private static List<object?[]> frameHistory = [];
    internal static List<object?[]> FrameHistoryPaused = [];

    #region Patches

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), [typeof(object)])]
    private static void DebugLog(object message) {
        if (!ShouldTrace()) return;

        Log.Info($"DebugLog: {message}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string)])]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string), typeof(object)])]
    // [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutineManaged))]
    // [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutineManaged2))]
    [HarmonyPatch(typeof(GameCore), nameof(GameCore.currentCoreState), MethodType.Setter)]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(int), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(string), typeof(bool), typeof(float)])]
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
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    // [HarmonyPatch(typeof(Time), nameof(Time.timeScale), MethodType.Setter)]
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
    // [HarmonyPatch(typeof(MonsterBase), "AfterAnimationUpdate")]
    // [HarmonyPatch(typeof(FSMStateMachineRunner), "UpdateProxy")]
    // [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    [HarmonyPatch(typeof(Physics), nameof(Physics2D.Simulate))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(Physics), nameof(Physics.SyncTransforms))]
    // [HarmonyPatch(typeof(Actor), nameof(Actor.OnRebindAnimatorMove))]
    // [HarmonyPatch(typeof(MonsterBase), nameof(MonsterBase.OnRebindAnimatorMove))]
    [HarmonyPatch(typeof(Health), nameof(Health.BecomeInvincible))]
    [HarmonyPatch(typeof(Health), nameof(Health.RemoveInvincible))]
    [HarmonyPatch(typeof(Player), "EnablePushSlowCheck")]
    // [HarmonyPatch(typeof(Player), "DisablePushSlowCheck")]
    [HarmonyPatch(typeof(FxPlayer), nameof(FxPlayer.OnEnableInvoke))]
    [HarmonyPatch(typeof(FxPlayer), nameof(FxPlayer.OnEnable))]
    [HarmonyPatch(typeof(FxPlayer), nameof(FxPlayer.PlayCustomAt), typeof(Vector3), typeof(Vector3), typeof(Transform))]
    [HarmonyPatch(typeof(PoolObject), "PoolObjectResetAndStart")]
    [HarmonyPatch(typeof(GeneralState), nameof(GeneralState.OnStateEnter))]
    [HarmonyPatch(typeof(AnimationMoveScaler), "EvaluateUpdate")]
    [HarmonyPatch(typeof(FooManager), nameof(FooManager.AutoExplode))]
    [HarmonyPatch(typeof(FooDeposit), nameof(FooDeposit.Explode))]
    [HarmonyPatch(typeof(PlayerBaseState), nameof(PlayerBaseState.OnStateEnter))]
    [HarmonyPatch(typeof(EffectReceiver), nameof(EffectReceiver.OnHitEnter))]
    //[HarmonyPatch(typeof(TriggerDetector), "UpdateImplement")]
    // [HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
    // [HarmonyPatch(typeof(EffectDealer), "DelayShootEffect")]
    // [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.TimePause))]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        AddFrameHistory([
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
    // [HarmonyPatch(typeof(Transform), nameof(Transform.localPosition), MethodType.Setter)]
    // [HarmonyPatch(typeof(Transform), nameof(Transform.position), MethodType.Setter)]
    private static void
        FrameHistoryPatchMovement(PhysicsMover __instance, MethodBase __originalMethod, object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Movement)) return;

        if (!Filter.HasFlag(TasTracerFilter.Monsters) && __instance is MonsterBase) {
            return;
        }

        AddFrameHistory([
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
        if (!ShouldTrace(TasTracerFilter.Monsters)) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(PushAwayWall), "SetWallRect")]
    private static void FrameHistoryPatchWip(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Wip)) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Timer.DelayTask), "CompleteInternal")]
    private static void FrameHistoryTimerComplete(Timer.DelayTask __instance) {
        if (!ShouldTrace()) return;

        AddFrameHistory("DelayTask.Complete", new StackTrace());
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FxPlayer), nameof(FxPlayer.OnEnableInvoke))]
    private static void Wipp(FxPlayer __instance) {
        // WIP HACk TODO
        // __instance.SetFieldValue("lastPlayTime", 0f);
    }
    

    /*[HarmonyPrefix]
    [HarmonyPatch(typeof(AnimationMoveScaler), "AnimatorMoved")]
    private static void AnimMotionRebind(AnimationMoveScaler __instance) {
        if (!ShouldTrace()) return;

        AddFrameHistory([
            "AnimatorMoved",
            __instance.GetFieldValue<bool>("_enabledEvaluateInUpdate"),
            __instance.transform.EnumerateParents().Select(x => x.transform.position).ToArray(),
            __instance.transform.EnumerateParents().Select(x => x.transform.localPosition).ToArray(),
            __instance.transform.EnumerateParents().Select(x => x.transform.name).ToArray(),
            __instance.GetFieldValue<float>("_moveXScale"),
            new StackTrace(),
        ]);
    }*/


    [HarmonyPrefix]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Update))]
    [HarmonyPatch(typeof(PhysicsMover), nameof(PhysicsMover.SetPosition))]
    private static void FrameHistoryPausedPatch(MonoBehaviour? __instance, MethodBase __originalMethod,
        object[] __args) {
        if (!ShouldTrace()) return;
        if (Manager.CurrState is not Manager.State.Paused and not Manager.State.FrameAdvance) return;

        AddFrameHistoryPaused([
            $"{__instance} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace(),
        ]);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Behaviour), nameof(Behaviour.enabled), MethodType.Setter)]
    private static void
        FrameHistorySetEnabledPatch(Behaviour __instance, MethodBase __originalMethod, object[] __args) {
        if (!ShouldTrace(TasTracerFilter.MBEnabled)) return;

        if (__instance is _2dxFX_Base or AkGameObj or RCGPostProcessManager or WOWROTATION or HighLightCamera) return;

        SortedFrameHistory.Add([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
            ObjectUtils.ObjectPath(__instance.gameObject),
            __args[0],
            new StackTrace(),
        ]);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(ReplayTest), nameof(ReplayTest.LogTestState))]
    private static void LogTestStatePatch(MonoBehaviour m, object targetName, object stateName) {
        if (!ShouldTrace(TasTracerFilter.Miscellaneous)) return;
        if (m is PauseUIPanel) return;

        AddFrameHistory(["LogTestState", m.ToString(), targetName.ToString(), stateName.ToString(), new StackTrace()]);
    }


    [HarmonyPostfix]
    // [HarmonyPatch(typeof(EffectDealer), nameof(EffectDealer.GetReceivers))]
    // [HarmonyPatch(typeof(EffectDealer), "HitEffectReceiverCheck")]
    // [HarmonyPatch(typeof(EffectDealer), nameof(EffectDealer.CanHitReceiver), [typeof(EffectReceiver)])]
    // [HarmonyPatch(typeof(PlayerInputCommandQueue), nameof(PlayerInputCommandQueue.RemoveFirstBufferAndGetLatestTime))] TODO
    // [HarmonyPatch(typeof(AnimationRescalePolicyBehavior), nameof(AnimationRescalePolicyBehavior.RescaleX))]
    [HarmonyPatch(typeof(AnimationMoveScaler), nameof(AnimationMoveScaler.AnimatorMoved))]
    [HarmonyFinalizer]
    private static void FrameHistoryPatchWithReturn(ref object __result, object? __instance,
        MethodBase __originalMethod, object[] __args) {
        if (!Manager.Running) return;

        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            __args,
            "->",
            __result,
            new StackTrace(),
        ]);
    }

    /*[HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterStat), nameof(CharacterStat.RemoveModifier))]
    private static void RemoveModifier(CharacterStat __instance, StatModifier mod) {
        frameHistory.Add([
            "RemoveModifier",
            __instance, mod.Value, mod.Type, mod.DurationType, mod.Source.name,
            new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "HorizontalMoveCheck")]
    private static void Horiz(Player __instance, float velChangingRate) => frameHistory.Add([
        "HorizontalMoveCheck",
        velChangingRate,
        __instance.MaxRunStat.Value,
        __instance.MaxRunStat.statModifiers.Count,
    ]);*/


    /*
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
    ]);*/


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), "Update")]
    private static void PlayerUpdate() {
        if (!ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        AddFrameHistory("Player.Update", Time.deltaTime);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAnimatorEvents), nameof(PlayerAnimatorEvents.AnimationDone))]
    private static void HistoryAnimationDone(PlayerAnimationEventTag tag) {
        if (!ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        AddFrameHistory(["PlayerAnimationDone", tag.ToString(), Player.i.AnimationVelocity]);
    }

    #endregion

    #region Sorted TriggerEnter2D

    private static List<object?[]> SortedFrameHistory = [];

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PathFindTarget), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(PathFindTarget), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(PlayerSensor), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(PlayerSensor), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(TriggerDetector), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(TriggerDetector), "OnTriggerExit2D")]
    [HarmonyPatch(typeof(MonsterPushAway), "OnTriggerEnter2D")]
    [HarmonyPatch(typeof(MonsterPushAway), "OnTriggerExit2D")]
    private static void FrameHistorySortedEvents(MonoBehaviour __instance, MethodBase __originalMethod,
        object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Trigger)) return;
        // if (Manager.Controller.CurrentFrameInTas == 0) return;

        var collider = (Collider2D)__args[0];
        if ((!Filter.HasFlag(TasTracerFilter.Monsters) && __instance.transform.parent.name == "MonsterCore") ||
            collider.transform.parent.name == "MonsterCore") {
            return;
        }

        DebugInfo.frameEvents.Add($"{__originalMethod.Name}({__instance.name}, {collider.name})");

        SortedFrameHistory.Add([
            // AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
            ObjectUtils.ObjectPath(__instance.gameObject),
            ObjectUtils.ObjectPath(collider.gameObject),
            new StackTrace(),
        ]);
    }

    #endregion

    #region LateUpdate

    public static void LateUpdate() {
        if (!ShouldTrace()) return;

        var monsters = GameVersions.Select<MonsterBase[]>(GameVersions.SpeedrunPatch,
            [],
            [MonsterManager.Instance.ClosetMonster]);
        foreach (var monster in monsters) {
            var state = (StealthPreAttackState)monster.fsm.FindMappingState(MonsterBase.States.PreAttack);
            AddFrameHistory(
                "ClosestMonster",
                // monster.fsm.State.ToString(),
                // AnimatorSnapshot.Snapshot(monster.animator).StateHash,
                // AnimatorSnapshot.Snapshot(monster.animator).NormalizedTime,
                monster.transform.position
                // monster.pathFindAgent.IsSameAreaWithTarget,
                // state.SchemesIndex,
                // state.ApproachingSchemes.Count
                // monster.pathFindAgent.target?.currentArea?.gameObject is {} obj ? ObjectUtils.ObjectPath(obj) : null,
                // monster.pathFindAgent.currentArea?.gameObject is {} obj2 ? ObjectUtils.ObjectPath(obj2) : null,
                // state.ApproachingSchemes.Count,
                // state.SchemesIndex,
                // state.IsFollowingSomeone,
                // monster.transform.position,
                // monster.AnimationVelocity,
                // monster.fsm.isPaused,
                // monster.finalOffset,
                // monster.fsm.IsInTransition,
                // ReflectionExtensions.GetFieldValue<bool>(monster, "__isEngaging"),
                // monster.GetDistanceToPlayer()
            );
        }
    }

    #endregion

    public static void AddFrameHistory(params object?[] args) {
        frameHistory.Add(args);
    }

    public static void AddFrameHistoryPaused(params object?[] args) {
        FrameHistoryPaused.Add(args);
    }

    internal static void TraceVarsThroughFrame(string phase) {
        if (!ShouldTrace(TasTracerFilter.TraceVarsThroughFrame)) return;

        try {
            if (traceVarsThroughFrame.Length > 0) {
                var vars = traceVarsThroughFrame.ToDictionary(x => x.Item1, x => x.Item2.Invoke());
                AddFrameHistory($"ThroughFrame-{phase}", vars);
            }

            if (traceVarsThroughFramePaused.Length > 0) {
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
        SortedFrameHistory.Clear();
        _traceVars.Clear();

        TasTracer.TraceEvent("EnableRun");
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
        SortedFrameHistory.Clear();
    }


    [UsedImplicitly]
    private record Change(string Name, object? From, object? To);

    [TasTraceAddState]
    private static void ExtendState(TraceData data) {
        var player = Player.i != null ? Player.i : null;
        var playerState = player?.fsm.FindMappingState(player.fsm.State);

        List<Change> changes = [];
        foreach (var (name, func) in traceVarsChanged) {
            var newVal = func();
            if (_traceVars.TryGetValue(name, out var oldVal) && Equals(oldVal, newVal)) {
                changes.Add(new Change(name, oldVal, newVal));
            }


            _traceVars[name] = newVal;
        }


        data.Add("Position", player?.transform.position);
        data.Add("Subpixel", player?.movementCounter);
        data.Add("Velocity", player?.Velocity);
        data.Add("FinalVelocity", player?.FinalVelocity);
        data.Add("IsLoading", GameInterop.IsLoading());
        var animState = player?.animator.GetCurrentAnimatorStateInfo(0);
        if (animState != null) {
            var time = animState.Value.normalizedTime * animState.Value.length;
            var animTime = (float)Math.Truncate(time * 1e5) / 1e5;
            data.Add("AnimationTime", animTime);
            // data.Add("AnimationTimeRaw", time);
        }

        data.Add("PlayerState", playerState);
        data.Add("Info", GameInfo.StudioInfo);
        if (Filter.HasFlag(TasTracerFilter.Random))
            data.Add("RandomState", DebugInfo.HashToAlphabet(Random.state));
        if (Filter.HasFlag(TasTracerFilter.Monsters)) {
            data.Add("MonsterInfo",
                DebugInfo.GetMonsterInfotext(DebugInfo.DebugFilter.All & ~DebugInfo.DebugFilter.TraceRandom &
                                             ~DebugInfo.DebugFilter.TraceStatechange));
        }


        if (FrameHistoryEnabled && frameHistory.Count > 0) {
            data.Add("FrameHistory", new List<object?[]>(frameHistory));
            if (SortedFrameHistory.Count > 0) {
                SortedFrameHistory.Sort((a, b) => a.Zip(b,
                        (item1, item2) => item1 is string i1 && item2 is string i2
                            ? string.Compare(i1, i2, StringComparison.Ordinal)
                            : 0)
                    .Skip(1)
                    .FirstOrDefault(cmp => cmp != 0));

                data.Add("FrameHistorySorted", new List<object?[]>(SortedFrameHistory));
            }
        }

        if (changes.Count > 0) {
            data.Add("TraceChanges", changes);
        }
    }
}

public static class TransformExtensions {
    public static IEnumerable<Transform> EnumerateParents(this Transform child) {
        var current = child;
        while (current != null) {
            yield return current;

            current = current.parent;
        }
    }
}
