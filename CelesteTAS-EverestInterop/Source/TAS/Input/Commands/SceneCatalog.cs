using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace TAS.Input.Commands;

/// Offline catalog of every loadable scene and its transition-entry gates, embedded from
/// `Assets/scene-catalog.json` (regenerate with `tools/gen-scene-catalog.py`). Drives the
/// `load` / `LoadTransition` auto-completes so candidates aren't limited to currently-loaded scenes.
///
/// A gate name is a TransitionPoint's GameObject name (e.g. `left1`, `door1`) — the value
/// `GameManager.FindTransitionPoint` matches on when resolving an EntryGateName.
internal static class SceneCatalog {
    private static IReadOnlyList<string>? scenes;
    private static Dictionary<string, IReadOnlyList<string>>? gates;

    private static void EnsureLoaded() {
        if (scenes != null) {
            return;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("scene-catalog.json")
                           ?? throw new InvalidOperationException("embedded resource scene-catalog.json not found");
        using var reader = new StreamReader(stream);
        var root = JObject.Parse(reader.ReadToEnd());

        var sceneList = new List<string>();
        foreach (var name in root["scenes"]!) {
            sceneList.Add(name.ToString());
        }

        var gateMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (scene, gateNames) in (JObject)root["gates"]!) {
            var list = new List<string>();
            foreach (var gate in gateNames!) {
                list.Add(gate.ToString());
            }

            gateMap[scene] = list;
        }

        scenes = sceneList;
        gates = gateMap;
    }

    /// All loadable scene names, sorted.
    public static IReadOnlyList<string> Scenes {
        get {
            EnsureLoaded();
            return scenes!;
        }
    }

    /// Entry-gate names present in the given scene, sorted. Empty if the scene has none / is unknown.
    public static IReadOnlyList<string> GatesFor(string scene) {
        EnsureLoaded();
        return gates!.TryGetValue(scene, out var list) ? list : Array.Empty<string>();
    }
}
