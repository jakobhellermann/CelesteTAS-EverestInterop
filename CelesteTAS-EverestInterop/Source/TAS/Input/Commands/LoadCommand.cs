using GlobalEnums;
using HarmonyLib;
using System.Collections.Generic;
using StudioCommunication;
using System.Diagnostics.CodeAnalysis;
using TAS.Tracer;
using UnityEngine;
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

    /// Position to drop the hero at once the scene-transition kicked off by Load finishes (see OnFinishedEnteringScene).
    private static Vector2? pendingPosition;

    [TasCommand("load", MetaDataProvider = typeof(LoadMeta))]
    private static void Load(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        TasTracer.AddFrameHistory("Executing load command");

        if (commandLine.Arguments.Length != 3) {
            AbortTas($"Invalid number of arguments in load command: '{commandLine.OriginalText}'.");
            return;
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

        // Go through the game's own scene transition (not raw SceneManager.LoadScene) so the level loads properly —
        // GameManager state, hero, camera. Same async path PreciseSavestates uses for savestate loads; the shared
        // TasLoad gate pauses playback until the scene has finished entering, and we drop the hero at (x, y) then.
        TasLoad.Begin();
        pendingPosition = new Vector2(x, y);
        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = scene,
            HeroLeaveDirection = GatePosition.unknown,
            EntryGateName = "dreamGate",
            EntryDelay = 0f,
            PreventCameraFadeOut = true,
            WaitForSceneTransitionCameraFade = false,
            Visualization = GameManager.SceneLoadVisualizations.Default,
        });
    }

    // Once the scene the load kicked off has finished entering, place the hero at the requested position and close
    // the TasLoad gate (deferred to the next frame boundary). Runs after the original method, so the position sticks.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HeroController), "FinishedEnteringScene")]
    private static void OnFinishedEnteringScene() {
        if (pendingPosition is not { } pos) {
            return;
        }

        pendingPosition = null;

        var hero = HeroController.instance;

        // Place the hero at the target before normalizing: the scene's dreamGate entry can spawn the hero inside a
        // water region (the entry point, not the target); moving to the target leaves it, which would fire a
        // splash-out recoil on the next physics step — with the body already moved + SyncTransforms, the normalize
        // below overwrites velocity/cState instead. SyncTransforms so physics sees the body at the target.
        hero.transform.position = new Vector3(pos.x, pos.y, hero.transform.position.z);
        Physics2D.SyncTransforms();

        GameManager.instance.cameraCtrl.PositionToHeroInstant(true);

        Normalize();
        SetSafeHazardRespawn(hero);

        TasLoad.End();
    }

    // Deterministic hazard-respawn failsafe for `load`. hazardRespawnLocation is [NonSerialized] (never persisted;
    // the game re-derives it on scene entry from the entry gate). Our dreamGate load has no resolvable entry gate,
    // so FinishedEnteringScene anchors it to the hero's landing position — which, if the load target is inside a
    // hazard, makes a hazard death respawn back into the hazard and loop, each respawn forcing a full blocking GC
    // (~1 fps). Pin it to the nearest marker/transition instead: deterministic, always safe, and superseded by the
    // game the moment the hero crosses a real HazardRespawnMarker, so normal play still behaves as in-game.
    private static void SetSafeHazardRespawn(HeroController hero) {
        var pos = (Vector2)hero.transform.position;
        var bestDist = float.MaxValue;
        var best = Vector3.zero;
        var found = false;

        foreach (var m in UnityEngine.Object.FindObjectsByType<HazardRespawnMarker>(FindObjectsSortMode.None)) {
            var d = ((Vector2)m.transform.position - pos).sqrMagnitude;
            if (d < bestDist) (bestDist, best, found) = (d, m.transform.position, true);
        }

        foreach (var tp in UnityEngine.Object.FindObjectsByType<TransitionPoint>(FindObjectsSortMode.None)) {
            var tpPos = tp.respawnMarker != null ? tp.respawnMarker.transform.position : tp.transform.position;
            var d = ((Vector2)tpPos - pos).sqrMagnitude;
            if (d < bestDist) (bestDist, best, found) = (d, tpPos, true);
        }

        if (found) hero.SetHazardRespawn(best, hero.cState.facingRight);
    }

    private static void Normalize() {
        Random.InitState(0);
    }
}
