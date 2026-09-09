// ReSharper disable InconsistentNaming
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;
using TAS.EverestInterop;
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

    private static IEnumerable<object?> Context() => [
        ..CaptureStack(),
    ];

    internal static readonly (string Name, Func<object?> Get)[] TraceVarsChanged = [
        // ("RandomState", () => HashToAlphabet(Random.state))
    ];
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFrameVars = [
        // ("RandomState", () => HashToAlphabet(Random.state))
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

        if (TasTracer.ShouldTrace(TasTracerFilter.Random)) data.Add("RandomState", HashToAlphabet(Random.state));
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
        AddFrameHistoryMethod(__originalMethod, __instance, __args, filter: TasTracerFilter.Miscellaneous);
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

