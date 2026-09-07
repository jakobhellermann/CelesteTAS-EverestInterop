using System;
using GlobalEnums;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StudioCommunication;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;
using TAS.ModInterop;
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
            if (args.Length == 1) {
                return SceneEntries(hasNext: true).GetEnumerator();
            }

            return EmptyEntries();
        }
    }

    private class LoadTransitionMeta : ITasCommandMeta {
        public string Insert =>
            $"LoadTransition{CommandInfo.Separator}[0;Scene]{CommandInfo.Separator}[1;Gate]";

        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath,
            int fileLine) {
            if (args.Length == 1) {
                return SceneEntries(hasNext: true).GetEnumerator();
            }

            if (args.Length == 2) {
                return GateEntries(args[0]).GetEnumerator();
            }

            return EmptyEntries();
        }
    }

    private static IEnumerator<CommandAutoCompleteEntry> EmptyEntries() {
        yield break;
    }

    /// Scene-name candidates: currently-loaded scenes first (ranked as suggestions), then the full offline
    /// catalog so any loadable scene can be completed, not just the active ones.
    private static IEnumerable<CommandAutoCompleteEntry> SceneEntries(bool hasNext) {
        var seen = new HashSet<string>();

        for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
            var name = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name;
            if (seen.Add(name)) {
                yield return new CommandAutoCompleteEntry { Name = name, Extra = "loaded", Suggestion = true, IsDone = true, HasNext = hasNext };
            }
        }

        foreach (var name in SceneCatalog.Scenes) {
            if (seen.Add(name)) {
                yield return new CommandAutoCompleteEntry { Name = name, IsDone = true, HasNext = hasNext };
            }
        }
    }

    /// Entry-gate candidates for the chosen target scene (from the offline catalog).
    private static IEnumerable<CommandAutoCompleteEntry> GateEntries(string scene) {
        foreach (var gate in SceneCatalog.GatesFor(scene)) {
            yield return new CommandAutoCompleteEntry { Name = gate, Extra = "gate", IsDone = true, HasNext = false };
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

        Normalize();

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

    [TasCommand("LoadTransition", MetaDataProvider = typeof(LoadTransitionMeta))]
    private static void LoadTransition(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        TasTracer.AddFrameHistory("Executing LoadTransition command");

        if (commandLine.Arguments.Length != 2) {
            AbortTas($"Invalid number of arguments in LoadTransition command: '{commandLine.OriginalText}'.");
            return;
        }

        var scene = commandLine.Arguments[0];
        var gate = commandLine.Arguments[1];

        if (!InGame()) {
            AbortTas("Attempted to start TAS outside of a level");
            return;
        }

        Normalize();

        // Unlike `load` (dreamGate + guessed x/y), enter at a named TransitionPoint gate so the game's entry flow
        // positions the hero. That flow (EnterHero → PositionHeroAtSceneEntrance → walk-in animation + entryDelay)
        // needs *frames* to run, so we must NOT hold the TasLoad gate: TasLoad freezes the clock (timeScale = 0) and
        // stops input-frame advancement, which would stall the entry sequence forever → FinishedEnteringScene never
        // fires → deadlock. Instead we let the transition play out as an *organic* transition: normalize the hero to a
        // clean idle up front (prior-independent RNG/clock/timers + idle pose), then kick off the transition and let it
        // advance over visible TAS frames. SceneTransitionGate already hides only the variable async fetch, so the
        // fade + gate entry stay deterministic-and-visible and playback continues normally once the hero has entered.
        NormalizeToIdle();
        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = scene,
            HeroLeaveDirection = GatePosition.unknown,
            EntryGateName = gate,
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

        // Place the hero at the target *before* normalizing, so the fixture apply runs at the final position. The
        // scene's dreamGate entry can spawn the hero inside a water region (the entry point, not the target); moving
        // to the target leaves it, which fires SurfaceWaterRegion.OnTriggerExit2D → a splash-out RecoilRightLong on
        // the next physics step. The scene-less fixture apply does position-first → Physics2D.Simulate(0) → full
        // restore, so with the hero already at the target that exit fires inside the (untraced) settle and its recoil
        // velocity/cState is then overwritten by the fixture's Rigidbody2D/HeroController restore. Symmetric to the
        // savestate restore's contact settle — no manual recoil cancel needed. SyncTransforms so the body is at the
        // target for that Simulate (the position-less fixture no longer carries a body position).
        hero.transform.position = new Vector3(pos.x, pos.y, hero.transform.position.z);
        Physics2D.SyncTransforms();

        GameManager.instance.cameraCtrl.PositionToHeroInstant(true);

        NormalizeToIdle();
        SetSafeHazardRespawn(hero);

        TasLoad.End();
    }

    // Normalize the hero's transient state (velocity/anim/cState/FSMs) to a known idle via a scene-less fixture,
    // applied in-place (no reload; a manual reset is unreliable because refs are cached). Also pins the free-running
    // state the fixture carries to its canonical values, so a load is prior-independent (otherwise scene-load RNG
    // churn / elapsed-time timers leak the prior in). The scene-less apply restores RandomState itself; the
    // deterministic clock and the PlayerData world-timer subset are not on that path, so apply them here. Only the
    // FisherWalker* timers are overwritten — abilities/progress are preserved.
    private static void NormalizeToIdle() {
        if (PreciseSavestatesInterop.Instance is not { } interop) {
            return;
        }

        var fixturePath = IdleFixturePath();
        _ = interop.LoadSavestateFromFile(fixturePath);

        DeterministicTimePatch.RebaseClock(interop.LastLoadedGameTime ?? 0f, interop.LastLoadedFrameCount ?? 0);
        if (JObject.Parse(File.ReadAllText(fixturePath))["PlayerData"] is { } fixturePlayerData) {
            JsonUtility.FromJsonOverwrite(fixturePlayerData.ToString(), PlayerData.instance);
        }
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

    private static string? idleFixturePath;

    /// Extracts the embedded idle savestate to a temp file (cached) so it can be loaded via the file-path API.
    private static string IdleFixturePath() {
        if (idleFixturePath is { } cached) {
            return cached;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("idle-normalization.json")
                           ?? throw new InvalidOperationException("embedded resource idle-normalization.json not found");
        using var reader = new StreamReader(stream);
        var path = Path.Combine(Path.GetTempPath(), "celestetas-idle-normalization.json");
        File.WriteAllText(path, reader.ReadToEnd());
        return idleFixturePath = path;
    }
}
