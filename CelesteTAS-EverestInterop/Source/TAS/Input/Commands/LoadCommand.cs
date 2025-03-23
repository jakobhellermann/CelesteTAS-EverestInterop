using Cysharp.Threading.Tasks;
using DG.Tweening;
using EverestInterop;
using HarmonyLib;
using NineSolsAPI;
using NineSolsAPI.Utils;
using System.Collections.Generic;
using StudioCommunication;
using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using TAS.ModInterop;
using UnityEngine;
using Random = UnityEngine.Random;
using Tween = PrimeTween.Tween;

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
        // ToastManager.Toast("gamelevelstart");

        if (GameCore.Instance.currentCutScene is SimpleCutsceneManager cutScene) {
            cutScene.TrySkip();
            TasTracerState.AddFrameHistory("skipped cutscene");
        }


        // Manager.EnableRun();
    }

    public static void EarlyUpdate() {
        if (justFinishedLoad is not { } key) return;

        justFinishedLoad = null;

        TasTracerState.AddFrameHistory("create `load` cache savestate");
        var savestate =
            interop!.CreateSavestateDisk("lastload", "TAS", SavestateFilter.Player | SavestateFilter.Monsters);
        loadCommandSavestates.Add(key, savestate);
    }

    [TasCommand("load", MetaDataProvider = typeof(LoadMeta))]
    private static void Load(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Log.TasTrace("Executing Load Command");
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

        /*if (loadCommandSavestates.TryGetValue(key, out var loadSavestate)) {
            var didLoadImmediately = interop.LoadSavestate(loadSavestate);
            if (!didLoadImmediately) {
                AbortTas("Did not load savestate in a single frame, aborting.");
                return;
            }
            TasTracerState.AddFrameHistory("Loaded `load` cache savestate");
            ToastManager.Toast("loaded savestate");

            return;
        }*/

        // UIManager.Instance.PausePanelUI.HideUIEasy();
        InputHelper.WithPrevent(() => UIManager.Instance.PausePanelUI.HideImmediately());

        var gameCore = GameCore.Instance;
        if (gameCore.gameLevel?.SceneName != scene) {
            ChangeSceneImmediately.ChangeScene(new SceneConnectionPoint.ChangeSceneData {
                    sceneName = scene,
                    playerSpawnPosition = () => new Vector3(x, y, 0),
                    changeSceneMode = SceneConnectionPoint.ChangeSceneMode.Teleport,
                    findMode = SceneConnectionPoint.FindConnectionMode.ID,
                    ChangedDoneEvent = () => {
                        /*TasTracer.Clear();
                        TasTracerState.Clear();

                        TasTracerState.AddFrameHistory("load command finished");
                        // Player.i.ChangeState(PlayerStateType.Normal, true);
                        Player.i.PlayAnimation("Idle", true, 0);
                        InputHelper.WithPrevent(() => { Player.i.animator.Update(0); });
                        Player.i.movementCounter = Vector2.zero;
                        Player.i.IsScriptedMove = false;


                        justFinishedLoad = key;*/
                    },
                },
                true);
            AbortTas("Loading scene, please restart TAS when finished");
            return;
        }
        
        Random.InitState(1344);
        // Random.InitState(0);

        Tween.StopAll();

        int n = UniTaskHelper.Clear(PlayerLoopTiming.Update);
        Log.Info($"Cleared {n} active tasks");
        DoTweenManager.InvokeMethod("PurgeAll");
        
        Normalize(new Vector2(x, y));
        gameCore.ResetLevel();
        
        Player.i.AllFull();
        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        }
        
        CameraManager.Instance.camera2D.MoveCameraInstantlyToPosition(Player.i.transform.position);
    }

    private static Type? doTweenManager;
    private static Type DoTweenManager => doTweenManager ??= typeof(DOTween).Assembly.GetType("DG.Tweening.Core.TweenManager");
    

    public static void Normalize(Vector2 position) {
        if (Player.i is not { } player) {
            AbortTas("Could not find player");
            return;
        }

        player.transform.position = player.transform.position with { x = position.x, y = position.y };
        player.movementCounter = Vector2.zero;

        var snapshot = new AnimatorSnapshot {
            StateHash = 1432961145,
            NormalizedTime = 0,
            Time = 0,
            ParamsFloat = new Dictionary<int, float>() {
                { 1602690925, 1 }, // OnGround
            },
            ParamsInt = new Dictionary<int, int>(),
            ParamsBool = new Dictionary<int, bool>(),
            UpdateMode = Player.i.animator.updateMode,
        };
        snapshot.Restore(Player.i.animator);
        InputHelper.WithPrevent(() => { Player.i.animator.Update(0); });

        NormalizeActor(Player.i);
        player.jumpState = Player.PlayerJumpState.None;
        player.varJumpSpeed = 0;
        player.dashCooldownTimer = 0;
        player.rollCooldownTimer = 0;
        player.parryCoolDownTimer = 0;
        player.meleeAttackCooldownTimer = 0;
        player.fooAttackInputLockTimer = 0;
        player.CanMove = true;
        player.pathFindAgent.Clear();
        player.InvokeMethod("UpdateBounds");
        Player.i.lastMoveX = 0;
        Player.i.moveX = 0;
        Player.i.ForceOnGround();
        Player.i.SetOnGround = true;
        Physics2D.SyncTransforms();
        player.GroundCheck();
        NormalizePathFindAgent(player.pathFindAgent);
        
        player.ChangeState(PlayerStateType.Normal, true);

        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        }

        foreach (var monster in MonsterManager.Instance.monsterDict.Values) {
            NormalizeMonster(monster);
        }

        var attack = player.fsm.FindMappingState(PlayerStateType.Attack);
        if (attack is PlayerAttackState) {
            attack.SetFieldValue("count", 0);
            attack.SetFieldValue("isAir", false);
        }
        
        TimePauseManager.Instance.gamePlayTimeScaleModifier.Resume();
        TimePauseManager.Instance.uiTimeScaleModifier.Resume();

        // CameraManager.Instance.ResetCamera2DDockerToPlayer();
        // CameraManager.Instance.camera2D.CenterOnTargets();
        // CameraManager.Instance.dummyOffset = Vector2.SmoothDamp(
        // SingletonBehaviour<CameraManager>.Instance.dummyOffset, direction, ref this.currentV, 0.25f);
    }

    private static void NormalizePathFindAgent(PathFindAgent pathFindAgent) {
        pathFindAgent.Clear();
        pathFindAgent.GetFieldValue<List<PathArea>>("touchingAreas")!.Clear();
        pathFindAgent.target = null;
        pathFindAgent.FindCurrentPathArea();

        TasMod.Instance.StartCoroutine(X(pathFindAgent.GetComponent<BoxCollider2D>()));
    }

    private static IEnumerator X(Collider2D pathFindAgent) {
        pathFindAgent.enabled = false;
        // yield return new WaitForEndOfFrame();
        pathFindAgent.enabled = true;
        yield break;
    }

    private static void NormalizeActor(Actor actor) {
        actor.Velocity = Vector2.zero;
        actor.AnimationVelocity = Vector3.zero;
        actor.Facing = Facings.Right;
        actor.finalOffset = Vector2.zero;
        actor.movementCounter = Vector2.zero;
    }

    private static void NormalizeMonster(MonsterBase monsterBase) {
        NormalizeActor(monsterBase);
        monsterBase.canSeePlayerCondition.ExtendFalseTime();
        monsterBase.lastAttackSensor = null;
        monsterBase.monsterContext = new();
        monsterBase.EngagingTimer = 0;
        NormalizePathFindAgent(monsterBase.pathFindAgent);
        foreach (var state in monsterBase.fsm._stateMapping.getAllStates) {
            if (state.stateBehavior is BossGeneralState generalState) {
                generalState.QueuedAttacks.Clear();
                generalState.damageOnTimeList.Clear();
            }
            if (state.stateBehavior is StealthEngaging engaging) {
                engaging.ForceRunToPlayer = false;
            }
        }

        foreach (var sensor in monsterBase.AttackSensorsCompat()) {
            sensor.SetFieldValue("_failReason", "");
            sensor.ResetAggressivenessResetCounter();
        }
    }
}
