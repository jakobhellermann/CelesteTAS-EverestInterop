using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using StudioCommunication;
using TAS.Utils;

namespace TAS.Input.Commands;

/// Asserts that two savestate files hold the same state, ignoring fields that legitimately differ by how many
/// frames elapsed before capture rather than by game state. Aborts the TAS with the first difference on mismatch.
///
/// Usage: <c>AssertEqualSavestates, &lt;a&gt;, &lt;b&gt;</c> (paths, absolute or relative to the TAS file).
public static class AssertEqualSavestatesCommand {
    private const string CommandName = "AssertEqualSavestates";

    // Nothing is ignored: a savestate is expected to be fully deterministic. The clock (GameTime/GameFrameCount),
    // RNG and free-running world timers are pinned on load, so a residual diff here is a real determinism bug to fix
    // at the source, not to mask.
    private static readonly string[] IgnoredKeys = [];

    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;A]{CommandInfo.Separator}[1;B]";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            yield break;
        }
    }

    [TasCommand(CommandName, ExecuteTiming = ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    private static void AssertEqualSavestates(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        if (commandLine.Arguments.Length < 2) {
            AbortTas($"{CommandName}: need two savestate paths");
            return;
        }

        var pathA = SavestateLoad.ResolvePath(commandLine.Arguments[0], filePath);
        var pathB = SavestateLoad.ResolvePath(commandLine.Arguments[1], filePath);
        if (!File.Exists(pathA)) {
            AbortTas($"{CommandName}: '{pathA}' not found");
            return;
        }
        if (!File.Exists(pathB)) {
            AbortTas($"{CommandName}: '{pathB}' not found");
            return;
        }

        var a = JToken.Parse(File.ReadAllText(pathA));
        var b = JToken.Parse(File.ReadAllText(pathB));
        foreach (var key in IgnoredKeys) {
            (a as JObject)?.Remove(key);
            (b as JObject)?.Remove(key);
        }

        if (FirstDiff(a, b, "") is { } diff) {
            // Full diff to the log; the toast (via AbortTas) stays a single line.
            $"""
             {CommandName} '{Path.GetFileName(filePath)}' line {fileLine} failed
             {Path.GetFileName(pathA)} != {Path.GetFileName(pathB)} at {diff.Path}
               A: {diff.A}
               B: {diff.B}
             """.Log(LogLevel.Error);
            AbortTas($"{CommandName} failed at {diff.Path}: '{diff.A}' != '{diff.B}' ({Path.GetFileName(filePath)}:{fileLine})");
        }
    }

    private static (string Path, string A, string B)? FirstDiff(JToken a, JToken b, string path) {
        if (JToken.DeepEquals(a, b)) {
            return null;
        }

        if (a is JObject oa && b is JObject ob) {
            var keys = new SortedSet<string>();
            foreach (var p in oa.Properties()) {
                keys.Add(p.Name);
            }
            foreach (var p in ob.Properties()) {
                keys.Add(p.Name);
            }

            foreach (var key in keys) {
                if (FirstDiff(oa[key] ?? JValue.CreateNull(), ob[key] ?? JValue.CreateNull(),
                        path.Length == 0 ? key : $"{path}.{key}") is { } d) {
                    return d;
                }
            }

            return null;
        }

        if (a is JArray aa && b is JArray ab) {
            if (aa.Count != ab.Count) {
                return (path, $"[{aa.Count} items]", $"[{ab.Count} items]");
            }

            for (var i = 0; i < aa.Count; i++) {
                var segment = $"{path}[{i}]";
                if (aa[i] is JObject eo && eo["Path"]?.ToString() is { Length: > 0 } p) {
                    var label = eo["FsmName"]?.ToString() is { Length: > 0 } fsm ? $"{p}#{fsm}" : p;
                    segment = $"{path}[{i} {label}]";
                }

                if (FirstDiff(aa[i], ab[i], segment) is { } d) {
                    return d;
                }
            }

            return null;
        }

        return (path, Truncate(a.ToString()), Truncate(b.ToString()));
    }

    private static string Truncate(string s) => s.Length > 80 ? s[..80] + "…" : s;
}
