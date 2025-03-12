using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NineSolsAPI;
using StudioCommunication;
using TAS.Utils;

namespace TAS.Input.Commands;


public static class LoadTeleportCommand {
    private static ImmutableSortedDictionary<string, List<TeleportPointData>>? teleports;

    private static ImmutableSortedDictionary<string, List<TeleportPointData>>? LoadTeleports() {
        if (!GameCore.IsAvailable()) return null;

        var tps = GameCore.Instance.allTeleportPoints.rawCollection.Cast<TeleportPointData>();
        return tps
            .Where(tp => !tp.sceneName.IsEmpty())
            .GroupBy(tp => tp.sceneName)
            .ToImmutableSortedDictionary(g => g.Key, g => g.ToList(), new NaturalComparer());
    }

    private class LoadTeleportMeta : ITasCommandMeta {
        public string Insert =>
            $"load_teleport{CommandInfo.Separator}[0;Scene]{CommandInfo.Separator}[1;X]";

        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath,
            int fileLine) {
            teleports ??= LoadTeleports();
            if (teleports == null) yield break;

            if (args.Length == 1) {
                foreach (var (sceneName, tps) in teleports) {
                    for (var i = 0; i < tps.Count; i++) {
                        var teleport = tps[i];
                        yield return new CommandAutoCompleteEntry
                            { Name = sceneName, Extra = $"{i} {teleport.Title}", IsDone = true };
                    }
                }
            }

            var scene = args[0];
            if (args.Length == 2) {
                if (!teleports.TryGetValue(scene, out var tps)) yield break;

                for (var i = 0; i < tps.Count; i++) {
                    var teleport = tps[i];
                    yield return new CommandAutoCompleteEntry
                        { Name = i.ToString(), Extra = $"{teleport.Title}", IsDone = true };
                }
            }
        }
    }

    [TasCommand("load_teleport", MetaDataProvider = typeof(LoadTeleportMeta))]
    private static void Load(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Log.TasTrace("Executing Load Command");
        if (commandLine.Arguments.Length != 2) {
            AbortTas($"Invalid number of arguments in load command: '{commandLine.OriginalText}'.");
            return;
        }

        var scene = commandLine.Arguments[0];
        var indexString = commandLine.Arguments[1];

        if (!int.TryParse(indexString, out var index)) {
            AbortTas($"Not a valid integer: '{indexString}'.");
            return;
        }

        if (!GameCore.IsAvailable() || GameCore.Instance.gameLevel == null) {
            AbortTas("Attempted to start TAS outside of a level");
            return;
        }

        var gameCore = GameCore.Instance;

        teleports ??= LoadTeleports();
        if (!teleports!.TryGetValue(scene, out var tps)) {
            AbortTas($"Scene {scene} does not exist");
            return;
        }
        if (index >= tps.Count) {
            AbortTas($"Scene {scene} has {tps.Count} teleports, attempted to load {index}");
            return;
        }

        var tp = tps[index];

        if (gameCore.gameLevel?.SceneName != scene) {
            gameCore.ChangeSceneCompat(new SceneConnectionPoint.ChangeSceneData {
                    sceneName = scene,
                    playerSpawnPosition = () => tp.TeleportPosition,
                    changeSceneMode = SceneConnectionPoint.ChangeSceneMode.Teleport,
                    findMode = SceneConnectionPoint.FindConnectionMode.ID,
                },
                false);
        }
        // gameCore.ResetLevel();

        LoadCommand.Normalize(tp.TeleportPosition);
    }
}
