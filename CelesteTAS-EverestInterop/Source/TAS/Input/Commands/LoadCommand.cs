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
using Object = UnityEngine.Object;
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

        InputHelper.WithPrevent(() => UIManager.Instance.PausePanelUI.HideImmediately());

        var gameCore = GameCore.Instance;
        if (gameCore.gameLevel?.SceneName != scene) {
            AbortTas("Loading scene, please restart TAS when finished");
            // ChangeSceneImmediately.ChangeScene(new SceneConnectionPoint.ChangeSceneData {});
            GameCore.Instance.ChangeScene(new SceneConnectionPoint.ChangeSceneData {
                sceneName = scene,
                playerSpawnPosition = () => new Vector3(x, y, 0),
                changeSceneMode = SceneConnectionPoint.ChangeSceneMode.Teleport,
                findMode = SceneConnectionPoint.FindConnectionMode.ID,
            });
            return;
        }

        Tween.StopAll();
        foreach (var task in Timer.Instance.allDelayTasks) {
            task.Cancel();
        }

        var n = UniTaskHelper.Clear(PlayerLoopTiming.Update);
        Log.Info($"Cleared {n} active tasks");
        DoTweenManager.InvokeMethod("PurgeAll");

        Random.InitState(0);

        Normalize(new Vector2(x, y));
        gameCore.ResetLevel();
        ResetColliders();

        Player.i.AllFull();
        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        }

        // CameraManager.Instance.camera2D.MoveCameraInstantlyToPosition(Player.i.transform.position);
    }

    private static void ResetColliders() {
        List<(Collider2D, bool)> allColliders = [];
        foreach (var trigger in Object.FindObjectsByType<TriggerDetector>(FindObjectsInactive.Include,
                     FindObjectsSortMode.InstanceID)) {
            var collider = trigger.GetComponent<Collider2D>();
            allColliders.Add((collider, collider.enabled));
            collider.enabled = false;
        }

        Physics2D.Simulate(0);
        foreach (var (collider, enabled) in allColliders) {
            collider.enabled = enabled;
        }
    }

    private static Type? doTweenManager;

    private static Type DoTweenManager =>
        doTweenManager ??= typeof(DOTween).Assembly.GetType("DG.Tweening.Core.TweenManager");


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
        };
        snapshot.Restore(player.animator);
        InputHelper.WithPrevent(() => { Player.i.animator.Update(0); });

        NormalizeActor(player);
        player.jumpState = Player.PlayerJumpState.None;
        player.varJumpSpeed = 0;
        player.dashCooldownTimer = 0;
        player.rollCooldownTimer = 0;
        player.parryCoolDownTimer = 0;
        player.meleeAttackCooldownTimer = 0;
        player.fooAttackInputLockTimer = 0;
        player.offLedgeTimer = 0;
        player.CanMove = true;
        player.pathFindAgent.SetFieldValue("lastArea", null);
        player.InvokeMethod("UpdateBounds");
        player.lastMoveX = 0;
        player.moveX = 0;
        player.ForceOnGround();
        player.SetOnGround = true;
        player.health.GetFieldValue<InternalDamageUpdater>("_internalDamageUpdater")!.Reset();
        Physics2D.SyncTransforms();
        player.GroundCheck();
        NormalizePathFindAgent(player.pathFindAgent);

        player.chargeAttackParticleAnim.playbackTime = 0;
        player.chargeAttackParticleAnim.playbackTime = 0;

        player.ChargingReleasedOrCanceled();


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
        pathFindAgent.SetFieldValue("lastArea", null);
        pathFindAgent.SetFieldValue("_currentArea", null);
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
        actor.lastHitGroundSpeed = 0;
    }

    private static void NormalizeMonster(MonsterBase monsterBase) {
        NormalizeActor(monsterBase);
        if (monsterBase.canSeePlayerCondition != null) monsterBase.canSeePlayerCondition.ExtendFalseTime();
        monsterBase.lastAttackSensor = null;
        monsterBase.monsterContext = new MonsterBase.MonsterContextRuntimeData();
        monsterBase.EngagingTimer = 0;
        monsterBase.SetFieldValue("hurtSoundCooldownTimer", 0);
        monsterBase.LinkMoveReset();
        monsterBase.postureSystem.lastPostureValue = monsterBase.postureSystem.PostureValue;
        monsterBase.postureSystem.GetFieldValue<InternalDamageUpdater>("_internalDamageUpdater")!.Reset();
        var moveScaler = monsterBase.animator.transform.Find("LogicRoot/MoveScale")
            ?.GetComponent<AnimationMoveScaler>();
        if (moveScaler != null) moveScaler.EvaluatePosition();
        NormalizePathFindAgent(monsterBase.pathFindAgent);
        foreach (var state in monsterBase.fsm._stateMapping.getAllStates) {
            if (state.stateBehavior is BossGeneralState generalState) {
                generalState.QueuedAttacks.Clear();
                generalState.damageOnTimeList.Clear();
            }

            if (state.stateBehavior is StealthEngaging engaging) {
                engaging.ForceRunToPlayer = false;
            }

            if (state.stateBehavior is StealthPreAttackState preAttack) {
                preAttack.SetFieldValue("ChangeToEngageDelayTime", 42);
            }
        }

        foreach (var sensor in monsterBase.AttackSensorsCompat()) {
            sensor.SetFieldValue("_failReason", "");
            sensor.ResetAggressivenessResetCounter();
        }
    }
}
