using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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


    [EnableRun]
    private static void BeginTrace() {
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

    [DisableRun]
    private static void EndTrace() {
        TraceEvent("DisableRun");

        if (!Manager.DidComplete) {
            Log.Warn("TAS Trace not saved");
            trace.Trace.Clear();
            trace.Checksum = 0;
            trace.FilePath = null;
            return;
        }

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

        var data = new TraceData();
        var inputFrame = Manager.Controller.Previous;
        data.Add("Frame", Manager.Controller.CurrentFrameInTas);
        if (inputFrame != null) data.Add("InputLine", inputFrame.ToString());
        // data.Add("Time", TimeHelper.timeInTas);
        AttributeUtils.Invoke<TasTraceAddState>([data]);

        trace.Trace.Add(data);
    }

    public static void TraceFramePause() {
        if (!TraceLoadingFrames && EverestInterop.GameInterop.IsLoading()) {
            return;
        }

        var data = new TraceData();
        if (FrameHistoryPaused.Count > 0) {
            data.Add("FrameHistoryPaused", new List<object?[]>(new List<object?[]>(FrameHistoryPaused)));
            trace.Trace.Add(data);
        }
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

    private static readonly List<object?[]> frameHistory = [];
    internal static readonly List<object?[]> FrameHistoryPaused = [];
    private static readonly List<object?[]> SortedFrameHistory = [];

    private static readonly Dictionary<string, object?> traceVarsState = [];

    internal static void AddFrameHistory(params object?[] args) {
        frameHistory.Add(args);
    }

    internal static void AddFrameHistoryPaused(params object?[] args) {
        FrameHistoryPaused.Add(args);
    }

    internal static void TraceVarsThroughFrame(string phase) {
        if (!ShouldTrace(TasTracerFilter.TraceVarsThroughFrame)) return;

        try {
            if (GameTrace.TraceVarsThroughFrameVars.Length > 0) {
                var vars = GameTrace.TraceVarsThroughFrameVars.ToDictionary(x => x.Name, x => x.Get());
                AddFrameHistory($"ThroughFrame-{phase}", vars);
            }

            if (GameTrace.TraceVarsThroughFramePausedVars.Length > 0) {
                var vars = GameTrace.TraceVarsThroughFramePausedVars.ToDictionary(x => x.Name, x => x.Get());
                AddFrameHistoryPaused($"ThroughFrame-{phase}", vars);
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

        if (FrameHistoryEnabled && frameHistory.Count > 0)
        {
            var history = frameHistory.Select(x => x.Length == 1 ? x[0] : x).ToArray();
            data.Add("FrameHistory", history);
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

    private static void ClearFrameHistory() {
        frameHistory.Clear();
        FrameHistoryPaused.Clear();
        SortedFrameHistory.Clear();
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
                            if (method.DeclaringType?.Namespace is { } ns &&
                                (ns.StartsWith("System") || ns.StartsWith("MonoMod"))) {
                                continue;
                            }

                            var name = method.Name;
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
