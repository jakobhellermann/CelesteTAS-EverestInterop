using HarmonyLib;
using NineSolsAPI;
using NineSolsAPI.Utils;
using RCGMaker.Core;
using System.Collections.Generic;
using StudioCommunication;
using TAS.ModInterop;
using UnityEngine;

namespace TAS.Input.Commands;

[HarmonyPatch]
public static class LoadCommand {
    private class LoadMeta : ITasCommandMeta {
        public string Insert =>
            $"load{CommandInfo.Separator}[0;Scene]{CommandInfo.Separator}[1;X]{CommandInfo.Separator}[2;Y]";

        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath,
            int fileLine) {
            if (!GameCore.IsAvailable()) yield break;

            if (args.Length == 1) {
                GameCore.Instance.FetchScenes();
                foreach (var scene in GameCore.Instance.allScenes)
                    yield return new CommandAutoCompleteEntry { Name = scene, IsDone = true };
            }
        }
    }

    private static DebugModPlusInterop? interop;
    private static Dictionary<string, Savestate> loadCommandSavestates = new();

    private static string? justFinishedLoad = null;

    [HarmonyPatch(typeof(ResetManager), nameof(ResetManager.GameLevelStart))]
    [HarmonyPostfix]
    public static void GameLevelStart() {
        ToastManager.Toast("gamelevelstart");
        if (justFinishedLoad is not {} key) return;
        justFinishedLoad = null;

        if (GameCore.Instance.currentCutScene is SimpleCutsceneManager cutScene) {
            // cutScene.TrySkip();
            ToastManager.Toast($"cutscene {cutScene.CanSkip}");
        }

        var savestate = interop!.CreateSavestateDisk("lastload", "TAS", SavestateFilter.All & ~SavestateFilter.Flags);
        loadCommandSavestates.Add(key, savestate);
        
        Manager.EnableRun();
    }

    [TasCommand("load", MetaDataProvider = typeof(LoadMeta))]
    private static void Load(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Log.TasTrace("Executing Load Command");
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

        if (!GameCore.IsAvailable() || GameCore.Instance.gameLevel == null) {
            AbortTas("Attempted to start TAS outside of a level");
            return;
        }

        interop ??= DebugModPlusInterop.Load();
        if (interop == null) {
            AbortTas("DebugModPlus is not installed");
            return;
        }

        var key = $"{scene}-{x}-{y}";

        if (loadCommandSavestates.TryGetValue(key, out var loadSavestate)) {
            // var didLoadImmediately = interop.LoadSavestate(loadSavestate);
            // if (!didLoadImmediately) {
                // AbortTas("Did not load savestate in a single frame, aborting.");
                // return;
            // }

            return;
        }

        // AbortTas("Loading scene, please restart TAS when finished");
        UIManager.Instance.PausePanelUI.HideUIEasy();
        GameCore.Instance.ChangeSceneCompat(new SceneConnectionPoint.ChangeSceneData {
                sceneName = scene,
                playerSpawnPosition = () => new Vector3(x, y, 0),
                changeSceneMode = SceneConnectionPoint.ChangeSceneMode.Teleport,
                findMode = SceneConnectionPoint.FindConnectionMode.ID,
                ChangedDoneEvent = () => justFinishedLoad = key,
            },
            true);


        /*var gameCore = GameCore.Instance;

        if (gameCore.gameLevel?.SceneName != scene) {
            gameCore.ChangeSceneCompat(new SceneConnectionPoint.ChangeSceneData {
                    sceneName = scene,
                    playerSpawnPosition = () => new Vector3(x, y, 0),
                    changeSceneMode = SceneConnectionPoint.ChangeSceneMode.Teleport,
                    findMode = SceneConnectionPoint.FindConnectionMode.ID,
                },
                false);
            AbortTas("Restart TAS when in scene");
            return;
        }

        gameCore.ResetLevel();

        Normalize(new Vector2(x, y));*/
    }

    public static void Normalize(Vector2 position) {
        if (Player.i is not { } player) {
            AbortTas("Could not find player");
            return;
        }

        player.transform.position = player.transform.position with { x = position.x, y = position.y };
        player.movementCounter = Vector2.zero;

        player.ChangeState(PlayerStateType.Normal, true);
        var snapshot = new AnimatorSnapshot {
            StateHash = 1432961145,
            NormalizedTime = 0,
            ParamsFloat = new Dictionary<int, float>(),
            ParamsInt = new Dictionary<int, int>(),
            ParamsBool = new Dictionary<int, bool>(),
        };
        snapshot.Restore(Player.i.animator);
        InputHelper.WithPrevent(() => { Player.i.animator.Update(0); });

        player.Velocity = Vector2.zero;
        player.AnimationVelocity = Vector3.zero;
        player.Facing = Facings.Right;
        player.jumpState = Player.PlayerJumpState.None;
        player.varJumpSpeed = 0;
        player.dashCooldownTimer = 0;
        player.rollCooldownTimer = 0;
        player.parryCoolDownTimer = 0;
        player.meleeAttackCooldownTimer = 0;
        player.fooAttackInputLockTimer = 0;
        player.SetFieldValue("_onGround", true);
        player.pathFindAgent.Clear();
        TasTracerState.AddFrameHistory(player.pathFindAgent.target);

        player.GroundCheck();
        Physics2D.SyncTransforms();

        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        }

        foreach (var monster in MonsterManager.Instance.monsterDict.Values) {
            NormalizeMonster(monster);
        }

        var attack = player.fsm.FindMappingState(PlayerStateType.Attack);
        if (attack is PlayerAttackState) {
            attack.SetFieldValue("count", 0);
            attack.SetFieldValue("inAir", false);
        }
        Random.InitState(1337);
        
        TimePauseManager.Instance.gamePlayTimeScaleModifier.Resume();
        TimePauseManager.Instance.uiTimeScaleModifier.Resume();

        // CameraManager.Instance.ResetCamera2DDockerToPlayer();
        // CameraManager.Instance.camera2D.CenterOnTargets();
        // CameraManager.Instance.dummyOffset = Vector2.SmoothDamp(
        // SingletonBehaviour<CameraManager>.Instance.dummyOffset, direction, ref this.currentV, 0.25f);
    }

    private static void NormalizeMonster(MonsterBase monsterBase) {
        monsterBase.canSeePlayerCondition.ExtendFalseTime();
        monsterBase.pathFindAgent.Clear();
        foreach (var state in monsterBase.fsm._stateMapping.getAllStates) {
            if (state.stateBehavior is BossGeneralState generalState) {
                generalState.damageOnTimeList.Clear();
            }
        }
    }
}
