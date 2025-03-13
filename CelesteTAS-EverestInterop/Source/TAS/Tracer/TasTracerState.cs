using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using TAS.Tracer;
using UnityEngine;

namespace TAS;

[HarmonyPatch]
public static class TasTracerState {
    public static void AddFrameHistory(params object?[] args) {
        frameHistory.Add(args);
    }

    public static void AddFrameHistoryPaused(params object?[] args) {
        FrameHistoryPaused.Add(args);
    }

    private static readonly List<(string, Func<object?>)> traceVarsThroughFrame = [
        ("animation", () => {
            if (Player.i == null) return null;
            var state = Player.i.animator.GetCurrentAnimatorStateInfo(0);
            return (state.fullPathHash, state.normalizedTime);
        }),
        ("playerPos", () => Player.i?.transform.position),
        ("animVel", () => Player.i?.AnimationVelocity),
        ("TimeScaleRCG", () => RCGTime.timeScale),
        ("TimeScaleRCGGlob", () => RCGTime.GlobalSimulationSpeed),
        ("TimeScale", () => Time.timeScale),
    ];

    public static void TraceVarsThroughFrame(string phase) {
        var vars = traceVarsThroughFrame
            .ToDictionary(x => x.Item1, x => x.Item2.Invoke());
        if (vars.Count > 0) {
            AddFrameHistory($"ThroughFrame-{phase}", vars);
            AddFrameHistoryPaused($"ThroughFrame-{phase}", vars);
        }
    }


    private static List<object?[]> frameHistory = [];
    internal static List<object?[]> FrameHistoryPaused = [];

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(int), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(string), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.GlobalSimulationSpeed), MethodType.Setter)]
    [HarmonyPatch(typeof(RCGTime), nameof(RCGTime.SetTimeScaleUnsafe))]
    [HarmonyPatch(typeof(StealthEngaging), "PreAttackCheck")]
    [HarmonyPatch(typeof(StealthPreAttackState), "EnterSchemeCheck")]
    // [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.TimePause))]
    [HarmonyPatch(typeof(TimePauseManager), nameof(TimePauseManager.SetSimulationSpeed))]
    private static void FrameHistory(Actor __instance, MethodBase __originalMethod, object[] __args)
    => AddFrameHistory([$"{__instance} {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace()]);
    
    /*[HarmonyPrefix]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(int), typeof(bool), typeof(float)])]
    [HarmonyPatch(typeof(Actor), nameof(Actor.PlayAnimation), [typeof(string), typeof(bool), typeof(float)])]
    private static void FrameHistory(MethodBase __originalMethod, object[] __args)
    => AddFrameHistory([$"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}", ..__args, new StackTrace()]);*/
    /*[HarmonyPatch(typeof(Actor), "LogTestState")]
    [HarmonyPrefix]
    private static void PlayerUpdate() {
        frameHistory.Add(["Player.Update", Time.deltaTime]);
    }*/
    

    [HarmonyPatch(typeof(Player), "Update")]
    [HarmonyPrefix]
    private static void PlayerUpdate() {
        frameHistory.Add(["Player.Update", Time.deltaTime]);
    }
    
    [HarmonyPatch(typeof(MonsterState), nameof(MonsterState.AnimationEvent))]
    [HarmonyPrefix]
    private static void BossAnimationEvent(MonsterState __instance, AnimationEvents.AnimationEvent e) {
        frameHistory.Add([$"BossAnimationEvent {__instance}", e.ToString()]);
    }
    

    [HarmonyPatch(typeof(PlayerAnimatorEvents), nameof(PlayerAnimatorEvents.AnimationDone))]
    [HarmonyPrefix]
    private static void HistoryAnimationDone(PlayerAnimationEventTag tag) =>
        frameHistory.Add(["PlayerAnimationDone", tag.ToString(), Player.i.AnimationVelocity]);

    [EnableRun]
    private static void EnableRun() {
        TasTracer.TraceEvent("EnableRun");
        TasTracerState.AddFrameHistory($"timescale {TimeHelper.OverwriteTimeScale}", Time.timeScale);
        Time.timeScale = 1;
        TasTracerState.AddFrameHistory("timescale", Time.timeScale);
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
        data.Add("AnimationTime", player?.animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
        // data.Add("PlayerState", playerState);
        data.Add("Info", GameInfo.StudioInfo);
        data.Add("MonsterInfo", DebugInfo.GetMonsterInfotext());

        data.Add("FrameHistory", new List<object?[]>(frameHistory));
    }
}
