// ReSharper disable InconsistentNaming
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using TAS.EverestInterop;
using TAS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;
#pragma warning disable CS0162 // Unreachable code detected

namespace TAS.Tracer;

/// Game-specific tracing: which engine calls to record into the frame-history, what per-frame state to
/// capture, and how to serialize game/engine types.
[HarmonyPatch]
internal static class GameTrace {
    /// Additional converters for game types
    public static JsonConverter[] JsonConverters => [
    ];
    
    // Game-provided variables; assign these from the game-specific tracer (GameTrace) to capture them.
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsChanged = [];
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFrameVars = [
        ("vel", () => HeroController.instance.GetFieldValue<Rigidbody2D>("rb2d")!.velocity),
        ("pos", () => HeroController.instance?.transform.position),
    ];
    internal static readonly (string Name, Func<object?> Get)[] TraceVarsThroughFramePausedVars = [];

    /// Per-frame game state added to each trace entry.
    [TasTraceAddState]
    private static void AddState(TraceData data) {
        if (TasTracer.TraceLoadingFrames) {
            data.Add("IsLoading", GameInterop.IsLoading());
        }

        data.Add("Info", GameInfo.StudioInfo);
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

        // Log.Info($"DebugLog: {message}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), typeof(string))]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), typeof(string), typeof(object))]
    [HarmonyPatch(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), typeof(IEnumerator))]
    [HarmonyPatch(typeof(HeroController), "DoWallJump")]
    [HarmonyPatch(typeof(HeroController), "HeroJump")]
    [HarmonyPatch(typeof(HeroController), "DoDoubleJump")]
    [HarmonyPatch(typeof(HeroController), "CancelJump")]
    [HarmonyPatch(typeof(HeroController), "HeroDash")]
    [HarmonyPatch(typeof(HeroController), "DoAttack")]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int), typeof(float)])]
    // [HarmonyPatch(typeof(Time), nameof(Time.timeScale), MethodType.Setter)]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.Simulate))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(HeroController), nameof(HeroController.EnterScene))]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.position), MethodType.Setter)]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.MovePosition))]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddForce), typeof(Vector2))]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.velocity), MethodType.Setter)]
    [HarmonyPatch(typeof(Rigidbody2D), nameof(Rigidbody2D.AddTorque), typeof(float))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(Transform), nameof(Transform.localPosition), MethodType.Setter)]
    [HarmonyPatch(typeof(Transform), nameof(Transform.position), MethodType.Setter)]
    private static void FrameHistoryPatchMovement(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!TasTracer.ShouldTrace(TasTracerFilter.Movement)) return;

        // Only the hero's own transform / rigidbody, to keep the movement history focused.
        bool show = (__instance is Transform t && t == HeroController.UnsafeInstance.transform)
                    || (__instance is Rigidbody2D rb && rb == HeroController.UnsafeInstance.GetFieldValue<Rigidbody2D>("rb2d"));
        if (!show) return;

        TasTracer.AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    #endregion
}
