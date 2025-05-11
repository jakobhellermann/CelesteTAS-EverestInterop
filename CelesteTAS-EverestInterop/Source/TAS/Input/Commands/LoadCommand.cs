using HarmonyLib;
using System.Collections.Generic;
using StudioCommunication;
using System.Diagnostics.CodeAnalysis;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace TAS.Input.Commands;

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class LoadCommand {
    private class LoadMeta : ITasCommandMeta {
        public string Insert =>
            $"load{CommandInfo.Separator}[0;Scene]{CommandInfo.Separator}[1;X]{CommandInfo.Separator}[2;Y]";

        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath,
            int fileLine) {
            if (!InGame()) yield break;

            if (args.Length == 1) {
                for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    yield return new CommandAutoCompleteEntry { Name = scene.name, IsDone = true };
                }
            }
        }
    }

    private static bool InGame() => true;

    public static bool IsLoading = false;

    [TasCommand("load", MetaDataProvider = typeof(LoadMeta))]
    private static void Load(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        IsLoading = true;
        TasTracerState.AddFrameHistory("Executing load command");

        if (commandLine.Arguments.Length != 3) {
            AbortTas($"Invalid number of arguments in load command: '{commandLine.OriginalText}'.");
        }

        var scene = commandLine.Arguments[0];
        var xString = commandLine.Arguments[1];
        var yString = commandLine.Arguments[2];

        if (!float.TryParse(xString, out var x)) {
            AbortTas($"Not a valid float: '{xString}'.");
            return;
        }

        if (!float.TryParse(yString, out var y)) {
            AbortTas($"Not a valid float: '{yString}'.");
            return;
        }

        if (!InGame()) {
            AbortTas("Attempted to start TAS outside of a level");
            return;
        }

        Random.InitState(0);
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }
}
