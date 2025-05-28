using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using TAS.EverestInterop;
using TAS.Input.Commands;
using TAS.Tracer;
using TAS.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ReSharper disable CollectionNeverUpdated.Local
// ReSharper disable InconsistentNaming

namespace TAS;

[Flags]
public enum TasTracerFilter {
    Miscellaneous = 1 << 1,
    Random = 1 << 2,
    Movement = 1 << 4,
    TraceVarsThroughFrame = 1 << 6,
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
        ("vel", () => HeroController.instance.GetFieldValue<Rigidbody2D>("rb2d")!.velocity),
        ("pos", () => HeroController.instance?.transform.position),
    ];


    private static (string, Func<object?>)[] traceVarsThroughFramePaused = [];

    #endregion

    private static List<object?[]> frameHistory = [];
    internal static List<object?[]> FrameHistoryPaused = [];
    private static List<object?[]> SortedFrameHistory = [];

    #region Patches

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Debug), nameof(Debug.Log), [typeof(object)])]
    private static void DebugLog(object message) {
        if (!ShouldTrace()) return;

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
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string), typeof(int)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(string)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int), typeof(int), typeof(float)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int), typeof(int)])]
    // [HarmonyPatch(typeof(Animator), nameof(Animator.Play), [typeof(int)])]
    // [HarmonyPatch(typeof(Time), nameof(Time.timeScale), MethodType.Setter)]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.Simulate))]
    [HarmonyPatch(typeof(Physics2D), nameof(Physics2D.SyncTransforms))]
    [HarmonyPatch(typeof(HeroController), nameof(HeroController.EnterScene))]
    private static void FrameHistoryPatch(object? __instance, MethodBase __originalMethod, object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Miscellaneous)) return;

        AddFrameHistory([
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
        if (!ShouldTrace(TasTracerFilter.Movement)) return;

        bool show = (__instance is Transform t && t == HeroController.UnsafeInstance.transform)
                   || (__instance is Rigidbody2D rb && rb == HeroController.UnsafeInstance.GetFieldValue<Rigidbody2D>("rb2d"));

        if (!show) return;
        
        AddFrameHistory([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}{(__instance != null ? " on " : "")}{__instance}",
            ..__args, new StackTrace(),
        ]);
    }

    #endregion

    #region Sorted TriggerEnter2D

    /*[HarmonyPrefix]
    // [HarmonyPatch(typeof(), "OnTriggerEnter2D")]
    // [HarmonyPatch(typeof(), "OnTriggerExit2D")]
    private static void FrameHistorySortedEvents(MonoBehaviour __instance, MethodBase __originalMethod,
        object[] __args) {
        if (!ShouldTrace(TasTracerFilter.Trigger)) return;

        var collider = (Collider2D)__args[0];

        // DebugInfo.frameEvents.Add($"{__originalMethod.Name}({__instance.name}, {collider.name})");

        SortedFrameHistory.Add([
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
            // ObjectUtils.ObjectPath(__instance.gameObject),
            // ObjectUtils.ObjectPath(collider.gameObject),
            new StackTrace(),
        ]);
    }*/

    #endregion

    #region LateUpdate

    public static void LateUpdate() {
        if (!ShouldTrace()) return;
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
        List<Change> changes = [];
        foreach (var (name, func) in traceVarsChanged) {
            var newVal = func();
            if (_traceVars.TryGetValue(name, out var oldVal) && Equals(oldVal, newVal)) {
                changes.Add(new Change(name, oldVal, newVal));
            }


            _traceVars[name] = newVal;
        }


        data.Add("IsLoading", GameInterop.IsLoading());
        data.Add("Info", GameInfo.StudioInfo);
        //  if (Filter.HasFlag(TasTracerFilter.Random))
        //  data.Add("RandomState", DebugInfo.HashToAlphabet(Random.state));

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
