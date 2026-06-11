// ReSharper disable InconsistentNaming
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using HarmonyLib;
using HutongGames.PlayMaker;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;
using TAS.EverestInterop;
using TAS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;
#pragma warning disable CS0618 // Type or member is obsolete

#pragma warning disable CS0162 // Unreachable code detected

namespace TAS.Tracer;

/// Game-specific tracing: which engine calls to record into the frame-history, what per-frame state to
/// capture, and how to serialize game/engine types.
[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
internal static class GameTrace {
    /// Additional converters for game types
    public static JsonConverter[] JsonConverters => [
    ];

    private static bool IncludeStackTraces => TasMod.Instance.ConfigTasTraceStackTraces.Value;

    private static IEnumerable<object?> CaptureStack() => IncludeStackTraces ? [new StackTrace()] : [];

    private static IEnumerable<object?> FsmContext()
        => PlayMakerFsmContext.Current is var fsm && fsm && fsm.gameObject ? [$"fsm:{fsm.FsmName}@{fsm.gameObject.name}"] : [];

    private static IEnumerable<object?> Context() => [
        ..FsmContext(),
        ..CaptureStack(),
    ];

    internal static readonly (string Name, Func<object?> Get)[] TraceVarsChanged = [
        // ("RandomState", () => HashToAlphabet(Random.state))
    ];
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFrameVars = [
        // ("RandomState", () => HashToAlphabet(Random.state))
        // Clock probes (unpatched getters → the real engine values HeroController.Update reads). With
        // TraceLoadingFrames on, these reveal per-phase whether timeScale is actually 0 during the savestate
        // load window, and what deltaTime the deltaTime-driven timers (downSpikeRecoveryTimer) see.
        // ("timeScale", () => Time.timeScale),
        // ("deltaTime", () => Time.deltaTime),
        // Downspike landing OoO: sampled at every player-loop phase so we can see, within one frame, the
        // order in which onGround flips true (physics) vs. downSpikeRecovery clears (HeroController.Update).
        /*("onGround", () => HeroController.SilentInstance?.cState.onGround),
        ("downSpiking", () => HeroController.SilentInstance?.cState.downSpiking),
        ("downSpikeRecovery", () => HeroController.SilentInstance?.cState.downSpikeRecovery),
        ("downSpikeRecoveryTimer", () => HeroController.SilentInstance?.downSpikeRecoveryTimer),
        ("fallTimer", () => HeroController.SilentInstance?.fallTimer),
        ("heroState", () => HeroController.SilentInstance?.hero_state),
        ("vel", () => HeroController.SilentInstance?.GetFieldValue<Rigidbody2D>("rb2d")?.linearVelocity),
        ("pos", () => HeroController.SilentInstance?.transform.position),*/
    ];
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFramePausedVars = TraceVarsThroughFrameVars;

    private static int loadingFrame;

    [BeforeTasFrame]
    private static void CountLoadingFrame() {
        loadingFrame = GameInterop.IsLoading() ? loadingFrame + 1 : 0;
    }

    /// Per-frame game state added to each trace entry.
    [TasTraceAddState]
    private static void AddState(TraceData data) {
        if (TasTracer.TraceLoadingFrames) {
            data.Add("IsLoading", GameInterop.IsLoading());
            data.Add("LoadingFrame", loadingFrame);
        } else {
            data.Add("Info", GameInfo.StudioInfo);
        }

        data.Add("FrameCount", Time.frameCount);

        // The game's own scene-transition state (distinct from the TAS load gate). Reveals how many frames the game
        // spends in a transition and on which frame it reaches PLAYING — the variable async-load window shows here.
        if (GameManager.instance is { } gm) {
            data.Add("GmState", gm.GameState.ToString());
            data.Add("InTransition", gm.IsInSceneTransition);
        }

        if (TasTracer.ShouldTrace(TasTracerFilter.Random)) data.Add("RandomState", HashToAlphabet(Random.state));
        // data.Add("CameraPosition", GameInterop.MainCamera is { } cam ? cam.transform.position : null);

        // Full-precision hero physics probe for determinism debugging — uncomment to enable. Floats are formatted
        // "R" to bypass the 4-decimal Vector converter, so the differ sees sub-ULP divergence; worldCom exposes
        // Box2D's internal center of mass (its sweep.c is what integrates, not rb.position — see the Rigidbody2D
        // round-trip in PreciseSavestates). For the pre-Simulate value, capture the same in TasMod.FirstUpdate
        // right before Physics2D.Simulate.
        // if (HeroController.SilentInstance?.GetFieldValue<Rigidbody2D>("rb2d") is { } rb) {
        //     string R(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        //     data.Add("HeroPhys", new Dictionary<string, object?> {
        //         ["pos"] = $"{R(rb.position.x)},{R(rb.position.y)}",
        //         ["vel"] = $"{R(rb.linearVelocity.x)},{R(rb.linearVelocity.y)}",
        //         ["worldCom"] = $"{R(rb.worldCenterOfMass.x)},{R(rb.worldCenterOfMass.y)}",
        //     });
        // }

        // Per-frame snapshot of every PlayMaker FSM's current state. Heavy, so gated behind the Misc
        // filter — lets us see whether a divergent FSM is already in the wrong state at frame 0 (state
        // survived the load) or only diverges later.
        if (TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) {
            var fsmStates = new SortedDictionary<string, string>();
            foreach (var fsm in PlayMakerFSM.FsmList) {
                if (fsm != null) {
                    fsmStates[$"{ObjectPath(fsm.gameObject)}/{fsm.FsmName}"] = fsm.ActiveStateName;
                }
            }

            // data.Add("FsmStates", fsmStates);
        }

        if (TasTracer.ShouldTrace(TasTracerFilter.Enemies)) {
            // Per-frame boss/enemy state, queryable via `jq '.Trace[].BossState'`. Present only while HealthManager
            // actors are alive (empty outside a fight → no key), so it's free elsewhere. The FSM states are the point:
            // each actor's own behaviour FSM plus the parent controller/phase FSM — for Cog_Dancers that's Dancer A/B
            // "Control" plus "Dancer Control" (Control/Beat Control/…) and the "Boss Scene" Sequence, so the whole
            // Dormant→Gate Close→Windup→… progression shows up. hp/pos are captured too (the coarse actor state).
            if (typeof(HealthManager).GetFieldValue<List<HealthManager>>("_activeHealthManagers") is { Count: > 0 } healthManagers) {
                var actors = new SortedDictionary<string, object?>();
                var fsmStates = new SortedDictionary<string, string>();
                var seenFsmObjects = new HashSet<GameObject>();
                foreach (var hm in healthManagers) {
                    if (!hm) {
                        continue;
                    }

                    actors[ObjectPath(hm.gameObject)] = new Dictionary<string, object?> {
                        ["hp"] = hm.hp,
                        ["pos"] = hm.transform.position,
                    };

                    // The actor's own FSMs and every ancestor's FSMs (the parent controller drives phase/attack state).
                    for (var t = hm.transform; t != null; t = t.parent) {
                        if (!seenFsmObjects.Add(t.gameObject)) {
                            continue;
                        }

                        foreach (var fsm in t.GetComponents<PlayMakerFSM>()) {
                            fsmStates[$"{ObjectPath(fsm.gameObject)}/{fsm.FsmName}"] = fsm.ActiveStateName;
                        }
                    }
                }

                data.Add("BossState", new Dictionary<string, object?> {
                    ["actors"] = actors,
                    ["fsms"] = fsmStates,
                });
            }
        }
    }
    
    public static string HashToAlphabet(Random.State input) =>
        HashToAlphabet(input.s0 + input.s1 + input.s2 + input.s3);

    public static string HashToAlphabet(int hash) {
        var positiveHash = (uint)hash;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var result = new char[4];
        for (var i = 0; i < 4; i++) {
            result[i] = alphabet[(int)(positiveHash % alphabet.Length)];
            positiveHash /= (uint)alphabet.Length;
        }

        return new string(result);
    }

    private static void AddFrameHistoryMethod(MethodBase method, object? instance, object[] args, TasTracerFilter? filter = null)
    {
        if (filter != null && !TasTracer.ShouldTrace(filter)) return;
        
        TasTracer.AddFrameHistory([
            $"{method.DeclaringType?.Name}.{method.Name}{(instance != null ? " on " : "")}{instance}",
            ..args, ..Context(),
        ]);
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
    [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int), typeof(float)])]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int)])]
    [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string)])]
    [HarmonyPatch(typeof(Time), nameof(Time.timeScale), MethodType.Setter)]
    private static void FrameHistoryPatchAnimation(object? __instance, MethodBase __originalMethod, object[] __args) {
        AddFrameHistoryMethod(__originalMethod, __instance, __args, filter: TasTracerFilter.Animation);
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string)])]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), [typeof(string), typeof(object)])]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, ..CaptureStack(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.position), MethodType.Setter)]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.MovePosition))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(Transform), nameof(Transform.localPosition), MethodType.Setter)]
    [HarmonyPatch(typeof(Transform), nameof(Transform.position), MethodType.Setter)]
    private static void FrameHistoryPatchMovement(object? __instance, MethodBase __originalMethod, object[] __args) {
        AddFrameHistoryMethod(__originalMethod, __instance, __args, filter: TasTracerFilter.Positions);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Random), nameof(Random.Range), [typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(Random), nameof(Random.Range), [typeof(int), typeof(int)])]
    [HarmonyPatch(typeof(Random), nameof(Random.value), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.insideUnitCircle), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.insideUnitSphere), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.onUnitSphere), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.rotation), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.rotationUniform), MethodType.Getter)]
    [HarmonyPatch(typeof(Random), nameof(Random.ColorHSV), [])]
    [HarmonyPatch(typeof(Random), nameof(Random.ColorHSV), [typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(Random), nameof(Random.ColorHSV), [typeof(float), typeof(float), typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(Random), nameof(Random.ColorHSV),
        [typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float)])]
    [HarmonyPatch(typeof(Random), nameof(Random.ColorHSV),
        [typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float)])]
    private static void RandomTrace(MethodBase __originalMethod, object[] __args, object __result) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Random)) return;
        if (TasTracer.RandomIsolationDepth > 0) return;

        TasTracer.AddFrameHistory([
            $"Random.{__originalMethod.Name} -> {__result}", ..__args, ..Context(),
        ]);
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Random), nameof(Random.InitState))]
    [HarmonyPatch(typeof(Random), nameof(Random.state), MethodType.Setter)]
    [HarmonyPatch(typeof(Random), nameof(Random.seed), MethodType.Setter)]
    private static void RandomStateWriteTrace(MethodBase __originalMethod) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Random)) return;
        if (TasTracer.RandomIsolationDepth > 0) return;

        TasTracer.AddFrameHistory([
            $"Random.{__originalMethod.Name} => {JsonUtility.ToJson(Random.state)}", ..Context(),
        ]);
    }

    /// Records every PlayMaker FSM state entry (source GameObject/FSM + state name + trigger stacktrace).
    /// High desync-potential: an FSM switching state a frame off, or only in one run, cascades widely.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FsmState), nameof(FsmState.OnEnter))]
    private static void FsmStateOnEnter(FsmState __instance) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        var fsm = __instance.Fsm;
        TasTracer.AddFrameHistory([
            $"FsmState.OnEnter '{fsm?.GameObjectName}'/'{fsm?.Name}': {__instance.Name}",
            ..CaptureStack(),
        ]);
    }

    /// Records what *triggers* an FSM transition (from-state --event--> to-state + the action that fired
    /// it, via stacktrace). The transition is decided in state.OnUpdate() by an action reading game
    /// state/events — that's the control-flow fork that diverges; Fsm.Update/SwitchState are deterministic.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fsm), "DoTransition")]
    private static void FsmDoTransition(Fsm __instance, FsmTransition transition, bool isGlobal) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"Fsm.DoTransition {__instance.GameObjectName}/{__instance.Name}: {__instance.ActiveStateName} --{transition.EventName}--> {transition.ToState} (global={isGlobal})",
            ..CaptureStack(),
        ]);
    }

    /// Records every FSM event as it's processed (which FSM, current state, event name + sender via
    /// stacktrace). Good granularity: game-relevant but not per-frame spam. Events drive most transitions,
    /// so a divergent event here is usually the upstream cause of a divergent DoTransition/OnEnter.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fsm), nameof(Fsm.ProcessEvent))]
    private static void FsmProcessEvent(Fsm __instance, FsmEvent fsmEvent) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"Fsm.Event {__instance.GameObjectName}/{__instance.Name}: {__instance.ActiveStateName} <- {fsmEvent?.Name}",
            ..CaptureStack(),
        ]);
    }

    /// Which GameObject (re)starts a TimedEventCaller coroutine and when. These restart on OnEnable (so
    /// fresh on every scene load) and draw Random immediately for their delay — a prime load-window churn source.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TimedEventCaller), nameof(TimedEventCaller.StartCallRoutine))]
    private static void TimedEventCallerStart(TimedEventCaller __instance) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"TimedEventCaller.StartCallRoutine on {ObjectPath(__instance.gameObject)}",
            ..CaptureStack(),
        ]);
    }

    private static string ObjectPath(GameObject go) {
        if (go == null) {
            return "null";
        }

        var path = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent) {
            path = $"{t.name}/{path}";
        }

        return path;
    }
    
    // Hero collision edges (enter/exit). Physics-contact continuity is a recurring desync source — e.g. a savestate
    // load teleports the hero onto ground it wasn't touching, so Box2D fires a phantom OnCollisionEnter2D (and its
    // landing FSM cascade) that a continuously-grounded run never sees. Gated behind the Collision filter.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeroController), "OnCollisionEnter2D")]
    private static void OnCollisionEnter2DTrace(Collision2D collision) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Collision)) return;
        var go = collision.gameObject;
        TasTracer.AddFrameHistory($"HeroController.OnCollisionEnter2D: {go.name} layer={go.layer} tag={go.tag}", CaptureStack());
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeroController), "OnCollisionExit2D")]
    private static void OnCollisionExit2DTrace(Collision2D collision) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Collision)) return;
        var go = collision.gameObject;
        TasTracer.AddFrameHistory($"HeroController.OnCollisionExit2D: {go.name} layer={go.layer} tag={go.tag}", CaptureStack());
    }

    #endregion
    
    #region Generic call stack

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeroController), "SetState")]
    private static void SetStateTrace(ActorStates newState) {
        if (!TasTracer.ShouldTrace()) return;
        TasTracer.AddFrameHistory($"HeroController.SetState({newState})", CaptureStack());
    }

    private static Harmony? traceAllHarmony;
    private static readonly Type traceAllType = typeof(HeroController);
    private static readonly HashSet<string> Unpatchable = ["DownAttack", "CheckForBump"]; // crashes UnpatchSelf
    private static readonly HashSet<string> TracedSeparately = ["SetState"];

    [Initialize]
    private static void PatchHeroControllerTraceAll() {
        traceAllHarmony = new Harmony("TasTools.GameTrace.TraceAll");
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(GameTrace), nameof(TraceAllPrefix)));
        var finalizer = new HarmonyMethod(AccessTools.Method(typeof(GameTrace), nameof(TraceAllFinalizer)));

        foreach (var method in AccessTools.GetDeclaredMethods(traceAllType)
                     .Where(method => !method.IsAbstract
                                      && !method.ContainsGenericParameters
                                      && method.GetMethodBody() != null
                                      && !Unpatchable.Contains(method.Name)
                                      && !TracedSeparately.Contains(method.Name))) {
            try {
                traceAllHarmony.Patch(method, prefix: prefix, finalizer: finalizer);
            } catch (Exception e) {
                Log.Warn($"TraceAll: skipping HeroController.{method.Name}: {e.Message}");
            }
        }
    }

    [Unload]
    private static void UnpatchHeroControllerTraceAll() {
        try {
            traceAllHarmony?.UnpatchSelf();
        } catch (Exception e) {
            Log.Warn($"TraceAll: unpatch failed: {e.Message}");
        }
        traceAllHarmony = null;
    }

    private static void TraceAllPrefix(MethodBase __originalMethod) {
        if (TasTracer.DoSuppressTrace) return;
        if (!TasTracer.ShouldTrace(TasTracerFilter.CallTree)) return;

        TasTracer.PushCall($"{traceAllType.Name}.{__originalMethod.Name}");
    }

    private static void TraceAllFinalizer() {
        if (TasTracer.DoSuppressTrace) return;
        if (!TasTracer.ShouldTrace(TasTracerFilter.CallTree)) return;

        TasTracer.PopCall();
    }
    
    #endregion
}

/// Tracks which PlayMakerFSM is currently being updated
[HarmonyPatch]
internal static class PlayMakerFsmContext {
    internal static PlayMakerFSM? Current { get; private set; }

    // No Harmony __state save/restore, which may conflict with other calls
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayMakerFSM), "Update")]
    private static void Prefix(PlayMakerFSM __instance) {
        Current = __instance;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayMakerFSM), "Update")]
    private static void Postfix() {
        Current = null;
    }
}
