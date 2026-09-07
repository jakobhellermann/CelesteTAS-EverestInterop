using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Snapshots;
using System.Collections;
using TAS.Input;
using TAS.Utils;
using UnityEngine;

namespace TAS.Tracer;

internal record TasTrace {
    public string? FilePath;
    public List<TraceData> Trace = [];
    public int Checksum;

    public override string ToString() => Trace.Select((x, n) => $"{x} {n}").Join(delimiter: "\n");
}

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
public class TasTraceAddState : Attribute;

public class TraceData {
    public readonly Dictionary<string, object?> Data = [];

    public override string ToString() => $"TraceData {{ {Data.Select(kv => $"{kv.Key}: {kv.Value}").Join()} }}";

    public void Add(string name, object? value) {
        Data[name] = value;
    }

    public override bool Equals(object? obj) {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not TraceData b) return false;

        if (!EqualsHelper.CompareDeep(Data, b.Data, out var failurePath, out var left, out var right)) {
            Log.Warn($"{left} != {right} at TraceData{failurePath}");
            return false;
        }

        return true;
    }

    public override int GetHashCode() => Data.GetHashCode();

    public static bool operator ==(TraceData a, TraceData b) => Equals(a, b);

    public static bool operator !=(TraceData a, TraceData b) => !(a == b);
}

public class TracerIrrelevantState(object? data) : IComparable {
    public object? Data = data;

    public override bool Equals(object? obj) => true;

    protected bool Equals(TracerIrrelevantState other) => true;

    public override int GetHashCode() => 0;

    public int CompareTo(object obj) => 0;
}

internal enum TracePauseMode {
    None,
    Reduced,
    Full,
}

/// Categories the frame-history can be filtered by. Generic mechanism; a game-specific tracer may add
/// further flags here for its own patches.
[Flags]
public enum TasTracerFilter {
    Miscellaneous = 1 << 1,
    Random = 1 << 2,
    Enemies = 1 << 3,
    Movement = 1 << 4,
    TraceVarsThroughFrame = 1 << 6,
    Collision = 1 << 7,
}

internal static class TasTracer {
    private static bool IsWine() => Environment.GetEnvironmentVariable("WINEPREFIX") != null;

    private static string TempPath() => IsWine() ? "/tmp" : Path.GetTempPath();

    private static readonly string traceDirRoot = Path.Combine(TempPath(), "TAS-Traces");
    public static readonly TracePauseMode TracePauseMode = TracePauseMode.Reduced;
    private const bool CheckMismatches = true;

    internal const bool TraceLoadingFrames = false;

    // Capture the stack trace of each top level call. Expensive.
    internal static bool CaptureCallRootStacks = false;

    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectAllMethods<TasTraceAddState>(typeof(TraceData));

        ClearOldTraces();
    }


    private static void ClearOldTraces() {
        DeleteDirectoryChildren(traceDirRoot);
    }


    private static TasTrace trace = new();

    private static Dictionary<int, List<TasTrace>> traceCache = new();

    /// Absolute path of the most recently saved trace JSON. Set in SaveTrace, cleared when a run starts, so callers
    /// can pick up exactly this run's trace once it completes (null while a run is in progress or if none was saved).
    public static string? LastSavedTracePath;


    // Named trace segment: BeginSegment records the current position in the trace, EndSegment writes everything
    // since to TAS-Traces/<name>/ (reusing SaveTrace) so the differ can compare two segments frame-by-frame.
    private static int segmentStart = -1;
    private static string? segmentName;

    internal static void BeginSegment(string name) {
        segmentName = name;
        segmentStart = trace.Trace.Count;
    }

    internal static void EndSegment() {
        if (segmentStart < 0 || segmentName is not { } name) {
            Log.Warn("EndTrace without a matching BeginTrace");
            return;
        }

        var slice = trace.Trace.GetRange(segmentStart, trace.Trace.Count - segmentStart);
        SaveTrace(trace with { Trace = [..slice], FilePath = name });
        Log.Info($"Saved trace segment '{name}' ({slice.Count} frames)");
        segmentStart = -1;
        segmentName = null;
    }

    #region Dynamic trace probes (TraceVar command)

    // Reusable per-frame reflection probes registered live from a TAS via `TraceVar, <path>` — no rebuild needed to
    // add a trace variable. The path's first segment is a named root (game code registers these in TraceRoots, e.g.
    // "HeroController" -> its singleton), the rest are navigated as instance fields/properties by reflection.
    internal static readonly Dictionary<string, Func<object?>> TraceRoots = new();
    private static readonly List<(string Label, Func<object?> Get)> dynamicProbes = [];

    internal static void AddTraceVar(string path) {
        var tokens = path.Split('.');
        dynamicProbes.Add((path, () => {
            try {
                if (!TraceRoots.TryGetValue(tokens[0], out var root)) {
                    return $"<no root '{tokens[0]}'>";
                }

                object? current = root();
                for (var i = 1; i < tokens.Length && current != null; i++) {
                    current = GetMember(current, tokens[i]);
                }

                return current is null or bool or int or long or float or double or string or Enum ? current : current.ToString();
            } catch (Exception e) {
                return $"<{e.GetType().Name}>";
            }
        }));
        Log.Info($"TraceVar: probing '{path}'");
    }

    private static object? GetMember(object obj, string name) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = obj.GetType();
        if (type.GetField(name, flags) is { } field) {
            return field.GetValue(obj);
        }

        return type.GetProperty(name, flags) is { } prop ? prop.GetValue(obj) : $"<no member '{name}'>";
    }

    [TasTraceAddState]
    private static void AddDynamicProbes(TraceData data) {
        foreach (var (label, get) in dynamicProbes) {
            data.Add(label, get());
        }
    }

    #endregion

    [EnableRun]
    private static void BeginTrace() {
        LastSavedTracePath = null;
        traceSaved = false;
        segmentStart = -1;
        dynamicProbes.Clear();
        trace.Trace.Clear();
        trace.Checksum = Manager.Controller.Checksum;
        trace.FilePath = Manager.Controller.FilePath.Replace(@"\", "/");

        ClearFrameHistory();
        traceVarsState.Clear();
        TraceEvent("EnableRun");
    }

    // Hot-reload hygiene: ScriptEngine never unloads the old plugin assembly, so its static traceCache would pin
    // every accumulated trace. Drop them on unload so they can be collected.
    [Unload]
    private static void ClearTraceCache() {
        traceCache.Clear();
    }

    private static bool traceSaved;

    [DisableRun]
    private static void EndTrace() {
        TraceEvent("DisableRun");

        // A run that ended without completing — an aborted assert, a wedge, or a manual stop. If it traced any frames,
        // still save the partial trace (up to the aborting frame) so the failure is immediately debuggable: trace-diff
        // it against a good run instead of re-running to try to catch an intermittent failure again. Nothing traced
        // (e.g. a stop before the first frame) → clear it.
        if (!Manager.DidComplete) {
            if (!traceSaved) {
                if (trace.Trace.Count > 0) {
                    SaveTrace(trace);
                    traceSaved = true;
                } else {
                    Log.Warn("TAS Trace not saved");
                    trace.Trace.Clear();
                    trace.Checksum = 0;
                    trace.FilePath = null;
                }
            }

            return;
        }

        SaveCompletedTrace();
    }

    /// Saves the finished run's trace exactly once, at the moment it completes — whether it ends by disabling or by
    /// auto-pausing a draft on the last frame. Keying only off [DisableRun] misses the draft-pause case (a completed
    /// draft never disables), which is why it is also called from the completion point in Manager.Update.
    internal static void SaveCompletedTrace() {
        if (traceSaved || !Manager.DidComplete) {
            return;
        }

        traceSaved = true;

        if (!traceCache.ContainsKey(trace.Checksum)) traceCache[trace.Checksum] = [];
        var checksumTraces = traceCache[trace.Checksum];

        if (checksumTraces.Count > 0 && CheckMismatches && TracePauseMode == TracePauseMode.None) {
            var previousTrace = checksumTraces[^1];
            var len = Math.Min(previousTrace.Trace.Count, trace.Trace.Count);

            var hasMismatch = false;

            if (previousTrace.Trace.Count != trace.Trace.Count) {
                Log.Warn($"Length mismatch: {previousTrace.Trace.Count} != {trace.Trace.Count}");
                hasMismatch = true;
            } else {
                for (var i = 0; i < len; i++) {
                    if (!EqualsHelper.CompareDeep(previousTrace.Trace[i],
                            trace.Trace[i],
                            out var failurePath,
                            out var left,
                            out var right)) {
                        Log.Warn($"{left} != {right} at frame {i}: TraceData{failurePath}");
                        hasMismatch = true;
                    }
                }
            }

            if (hasMismatch) {
                Log.Warn("TAS nondeterminism detected!");
                Log.Warn($"Check traces at {traceDirRoot}");
            }
        }

        checksumTraces.Add(trace with { Trace = [..trace.Trace] });
        SaveTrace(trace);
    }

    public static void TraceEvent(string evt, params object?[] extra) {
        AddFrameHistory("event", evt);
        var data = new TraceData();
        data.Add("event", evt);
        if (extra.Length == 1) {
            data.Add("data", extra[0]);
        } else if (extra.Length > 0) {
            data.Add("data", extra);
        }

        trace.Trace.Add(data);
    }

    public static void TraceFrame() {
        if (!TraceLoadingFrames && EverestInterop.GameInterop.IsLoading()) {
            return;
        }

        var advancing = Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance;
        if (!advancing) {
            // Skip the terminal hold: a draft auto-pauses on its last frame (see Manager.Update), producing a run
            // of held frames with no new input whose count varies run-to-run — pure trace clutter. Mid-TAS
            // breakpoint pauses still have CanPlayback and are kept (they carry the freeze-leak signal).
            if (!Manager.Controller.CanPlayback || !frameStages.Any(s => s.HasContent)) {
                return;
            }
        }

        using var _ = SuppressTrace();

        var data = new TraceData();
        data.Add("Frame", Manager.Controller.CurrentFrameInTas);
        data.Add("State", Manager.CurrState);
        var inputFrame = Manager.Controller.Previous;
        if (inputFrame != null) data.Add("InputLine", inputFrame.ToString());
        // data.Add("Time", TimeHelper.timeInTas);
        AttributeUtils.Invoke<TasTraceAddState>([data]);

        trace.Trace.Add(data);
    }

    #region Frame history (framework)

    private static TasTracerFilter Filter => TasMod.Instance.ConfigTasTraceFilter.Value;
    private static bool FrameHistoryEnabled => TasMod.Instance.ConfigTasTraceFrameHistory.Value;

    internal static bool ShouldTrace(TasTracerFilter? filter = null) {
        if (!FrameHistoryEnabled || !Manager.Running) return false;
        // Skip all frame-history recording on loading frames we won't emit anyway (same gate as TraceFrame).
        // Otherwise we'd build stage trees and capture stacktraces (CaptureCallRootStacks) all through the scene
        // load — the dominant cost there (~10x slower loads) — only for TraceFrame to discard the frame. IsLoading
        // only flips at frame boundaries (see StartOnSavestateCommand), so this never toggles mid-frame and
        // PushCall/PopCall stay balanced.
        if (!TraceLoadingFrames && EverestInterop.GameInterop.IsLoading()) return false;
        if (filter != null && !Filter.HasFlag(filter)) return false;

        return true;
    }

    // Frame history as a tree of stages. Each player loop phase opens a stage, and events are recorded as children in a call tree.
    private sealed class FrameStage {
        public required string Stage;
        public Dictionary<string, object?>? Vars;
        public readonly List<object?> Events = [];

        public bool HasContent => Events.Count > 0 || Vars is { Count: > 0 };

        public Dictionary<string, object?> ToDict() {
            var d = new Dictionary<string, object?> { ["Stage"] = Stage };
            if (Vars is { Count: > 0 }) d["Vars"] = Vars;
            if (Events.Count > 0) d["Events"] = Events.Select(Collapse).ToList();
            return d;
        }

        // A call node with no children and no stack renders as just its name, keeping the common (leaf) case
        // compact. Top-level nodes carry a "Stack" (their external caller) and are always kept as objects.
        private static object? Collapse(object? node) {
            if (node is Dictionary<string, object?> call && call.TryGetValue("Children", out var raw)
                                                          && raw is List<object?> children) {
                var hasStack = call.ContainsKey("Stack");
                if (children.Count == 0 && !hasStack) {
                    return call["Call"];
                }

                var d = new Dictionary<string, object?> { ["Call"] = call["Call"] };
                if (hasStack) d["Stack"] = call["Stack"];
                if (children.Count > 0) d["Children"] = children.Select(Collapse).ToList();
                return d;
            }

            return node;
        }
    }

    private static readonly List<FrameStage> frameStages = [];
    private static FrameStage currentStage = new() { Stage = "start" };

    // Live call tree: the current append target is the top of callStack (a Children list); the bottom is the
    // current stage's Events. HeroController prefixes push, finalizers pop; a new stage resets to its root.
    private static readonly List<List<object?>> CallStack = [];

    private static void StartStage(string name) {
        currentStage = new FrameStage { Stage = name };
        frameStages.Add(currentStage);
        CallStack.Clear();
        CallStack.Add(currentStage.Events);
    }

    private static readonly Dictionary<string, object?> traceVarsState = [];

    // Skip trace while our instrumentation (e.g. debuginfo) reads the game state.
    internal static bool DoSuppressTrace;

    internal static TraceSuppressScope SuppressTrace() => new();

    internal readonly struct TraceSuppressScope : IDisposable {
        private readonly bool prev;
        public TraceSuppressScope() {
            prev = DoSuppressTrace;
            DoSuppressTrace = true;
        }
        public void Dispose() => DoSuppressTrace = prev;
    }

    internal static void AddFrameHistory(params object?[] args) {
        if (CallStack.Count == 0) return;

        if (CallStack.Count == 1 && CaptureCallRootStacks) {
            args = [..args, new StackTrace()];
        }

        CallStack[^1].Add(args.Length == 1 ? args[0] : args);
    }

    internal static void PushCall(string name) {
        if (CallStack.Count == 0) return;
        var children = new List<object?>();
        var node = new Dictionary<string, object?> { ["Call"] = name, ["Children"] = children };
        if (CallStack.Count == 1 && CaptureCallRootStacks) {
            node["Stack"] = new StackTrace();
        }
        CallStack[^1].Add(node);
        CallStack.Add(children);
    }

    internal static void PopCall() {
        if (CallStack.Count > 1) CallStack.RemoveAt(CallStack.Count - 1);
    }

    /// Opens a new stage for the given player-loop phase; subsequent recorded events become its children.
    /// Optionally captures per-phase state vars (gated by the TraceVarsThroughFrame filter).
    internal static void BeginStage(string phase) {
        if (!ShouldTrace()) return;

        using var _ = SuppressTrace();

        StartStage(phase);

        if (!ShouldTrace(TasTracerFilter.TraceVarsThroughFrame)) return;

        try {
            if (GameTrace.TraceVarsThroughFrameVars.Length > 0) {
                currentStage.Vars = GameTrace.TraceVarsThroughFrameVars.ToDictionary(x => x.Name, x => x.Get());
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    public static void LateUpdate() {
        if (!ShouldTrace()) return;
    }

    [UsedImplicitly]
    private record Change(string Name, object? From, object? To);

    [TasTraceAddState]
    private static void AddFrameHistoryState(TraceData data) {
        List<Change> changes = [];
        foreach (var (name, func) in GameTrace.TraceVarsChanged) {
            var newVal = func();
            if (traceVarsState.TryGetValue(name, out var oldVal) && !Equals(oldVal, newVal)) {
                changes.Add(new Change(name, oldVal, newVal));
            }

            traceVarsState[name] = newVal;
        }

        if (FrameHistoryEnabled && frameStages.Any(s => s.HasContent)) {
            data.Add("FrameHistory", frameStages.Where(s => s.HasContent).Select(s => s.ToDict()).ToList());
        }

        if (changes.Count > 0) {
            data.Add("TraceChanges", changes);
        }
    }

    private static void ClearFrameHistory() {
        frameStages.Clear();
        StartStage("start");
    }

    [BeforeTasFrame]
    private static void BeforeTasFrame() {
        ClearFrameHistory();
    }

    #endregion

    private static void SaveTrace(TasTrace newTrace) {
        var json = JsonConvert.SerializeObject(newTrace,
            Formatting.Indented,
            new JsonSerializerSettings {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = (_, args) => {
                    // args.ErrorContext.Handled = true;
                    Log.Error(
                        $"Serialization error while creating snapshot: {args.CurrentObject?.GetType()}: {args.ErrorContext.Path}: {args.ErrorContext.Error.Message}");
                },
                ContractResolver = new WritableOnlyResolver(),
                Converters = [
                    new FuncConverter<TraceData>(data => data.Data),
                    new FuncConverter<TracerIrrelevantState>(data => data.Data),
                    new FuncConverter<Func<bool>>(func => {
                        var method = func.Method;
                        return $"{method.DeclaringType}.{method.Name}";
                    }),
                    new FuncConverter<StackTrace>(st => {
                        var frames = new List<string>(st.FrameCount - 1);
                        for (var i = 1; i < st.FrameCount; i++) {
                            var frame = st.GetFrame(i);
                            var method = frame.GetMethod();
                            // Drop runtime/MonoMod frames, our own instrumentation (TAS.Tracer.*), and the
                            // Harmony DMD<…> trampoline of the patched method — so the stack starts at the
                            // real external caller.
                            if (method.DeclaringType?.Namespace is { } ns &&
                                (ns.StartsWith("System") || ns.StartsWith("MonoMod") || ns.StartsWith("TAS.Tracer"))) {
                                continue;
                            }

                            var name = method.Name;
                            if (name.StartsWith("DMD<")) {
                                continue;
                            }

                            frames.Add($"{method.DeclaringType}.{name}");
                        }

                        return frames;
                    }),
                    new FuncConverter<IEnumerator>(x => $"IEnumerator({x})"),
                    new ToStringConverter<InputFrame>(),
                    new StringEnumConverter(),
                    // Unity specific converters
                    new FuncConverter<Vector2>(vec => $"({vec.x:0.0000}, {vec.y:0.0000})"),
                    new FuncConverter<Vector3>(vec =>
                        $"({vec.x:0.0000}, {vec.y:0.0000}" + (vec.z != 0 ? $", {vec.z:0.0000}" : "") + ")"),
                    new ToStringConverter<MonoBehaviour>(),
                    ..ExtraUnityConverters.UnityConverters,
                    // Game-specific converters 
                    ..GameTrace.JsonConverters,
                ],
            });


        var name = Path.GetFileNameWithoutExtension(newTrace.FilePath)!;
        var traceDir = Path.Combine(traceDirRoot, name);
        Directory.CreateDirectory(traceDir);
        var datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var tracePath = Path.Combine(traceDir, $"{datetime}.json");
        File.WriteAllText(tracePath, json);
        LastSavedTracePath = tracePath;
        Log.Info($"Saved TAS trace to {tracePath}");


        var latest = Path.Combine(traceDir, "latest");
        Directory.CreateDirectory(latest);
        var latestChecksum = Path.Combine(latest, "checksum.txt");

        using var file = File.Open(latestChecksum, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        var reader = new StreamReader(file);
        if (reader.ReadToEnd() != newTrace.Checksum.ToString()) {
            DeleteFileChildren(latest, "*.json");
            file.SetLength(0);
            file.Write(Encoding.UTF8.GetBytes(newTrace.Checksum.ToString()));
        }

        File.Copy(tracePath, Path.Combine(latest, $"{datetime}.json"), true);
        if (File.Exists(Path.Combine(latest, "1.json"))) {
            File.Copy(Path.Combine(latest, $"1.json"), Path.Combine(latest, "2.json"), true);
        }

        File.Copy(tracePath, Path.Combine(latest, $"1.json"), true);
    }

    private static void DeleteDirectoryChildren(string path) {
        if (!Directory.Exists(path)) return;

        try {
            foreach (var dir in Directory.GetDirectories(path)) {
                Directory.Delete(dir, true);
            }
        } catch (Exception e) {
            Log.Debug($"Failed to delete old traces: {e}");
        }
    }

    private static void DeleteFileChildren(string path, string searchPattern) {
        foreach (var dir in Directory.GetFiles(path, searchPattern)) {
            File.Delete(dir);
        }
    }
}
