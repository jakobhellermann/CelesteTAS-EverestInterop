using Cysharp.Threading.Tasks;
using DG.Tweening;
using EverestInterop;
using HarmonyLib;
using MonsterLove.StateMachine;
using Newtonsoft.Json;
using NineSolsAPI;
using NineSolsAPI.Utils;
using System.Collections.Generic;
using StudioCommunication;
using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Health), nameof(Health.InvincibleForDuration))]
    private static bool SkipInvincible() => !IsLoading;

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

        ClearFoo();
        StopTimers();

        Random.InitState(0);
        
        TimePauseManager.Instance.gamePlayTimeScaleModifier.UpdateTimeScale();
        TimePauseManager.Instance.uiTimeScaleModifier.UpdateTimeScale();

        NormalizePre(new Vector2(x, y));
        gameCore.ResetLevel();
        NormalizePost();

        ResetColliders();

        foreach (var fxPlayer in Object.FindObjectsByType<FxPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            fxPlayer.SetFieldValue("lastPlayTime", 0);
        }
        foreach (var playerSensor in Object.FindObjectsByType<PlayerSensor>(FindObjectsSortMode.None)) {
            playerSensor.IsPlayerInside = false;
        }

        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.ClearCondition();
            condition.ResetTrueTime();
        }

        foreach (var vote in Player.i.health.isInvincibleVote.votes.Keys.ToArray()) {
            Player.i.health.isInvincibleVote.Revoke(vote);
        }

        foreach (var projectile in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) {
            projectile.ReturnToPool();
        }

        MetadataCommands.UpdateDamageSection();
        IsLoading = false;
    }

    public static void NormalizePre(Vector2 position) {
        if (Player.i is not { } player) {
            AbortTas("Could not find player");
            return;
        }

        player.transform.position = player.transform.position with { x = position.x, y = position.y };
        player.movementCounter = Vector2.zero;

        var snapshot = new AnimatorSnapshot {
            StateHash = 1432961145,
            NormalizedTime = 0,
            ParamsFloat = new Dictionary<int, float> {
                { 1602690925, 1 }, // OnGround
            },
            ParamsInt = new Dictionary<int, int>(),
            ParamsBool = new Dictionary<int, bool>(),
        };
        snapshot.Restore(player.animator);
        InputHelper.WithPrevent(() => { Player.i.animator.Update(0); });

        NormalizeActor(player);
        NormalizeFsm(player.fsm);
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
        player.GroundCheck();
        player.chiContainer.Clear();
        player.IsAirJumpIncreased = false;
        player.EnablePushByMonster();

        player.ChargingReleasedOrCanceled();
        player.chargeAttackParticleAnim.Play(
            player.chargeAttackParticleAnim.GetCurrentAnimatorStateInfo(0).fullPathHash,
            0,
            0);
        player.chargeAttackParticleAnim.Update(0);


        player.ChangeState(PlayerStateType.Normal, true);

        foreach (var condition in ConditionTimer.Instance.AllConditions) {
            condition.SetFieldValue("_isFalseTimer", float.PositiveInfinity);
        }

        foreach (var monster in MonsterManager.Instance.monsterDict.Values) {
            NormalizeMonsterPre(monster);
        }

        var normalState = (PlayerNormalState)player.fsm.FindMappingState(PlayerStateType.Normal);
        normalState.SetFieldValue("weakCoughTimer", 0);

        var lieDownState = (PlayerLieDownState)player.fsm.FindMappingState(PlayerStateType.LieDown);
        lieDownState.SetFieldValue("isGettingUp", false);
        lieDownState.SetFieldValue("canMove", true);

        var attack = (PlayerAttackState)player.fsm.FindMappingState(PlayerStateType.Attack);
        attack.SetFieldValue("count", 0);
        attack.SetFieldValue("isAir", false);
        attack.SetFieldValue("isAir", false);
        attack.SetFieldValue("canDoMove", false);
        attack.SetFieldValue("canDoParry", false);

        var weaponState = (PlayerWeaponState)player.fsm.FindMappingState(PlayerStateType.ShootArrow);
        player.ChargingReleasedOrCanceled(weaponState.ArrowChargeParticleAnim);
        weaponState.ArrowChargeParticleAnim.Play(weaponState.ArrowChargeParticleAnim.GetCurrentAnimatorStateInfo(0)
                .fullPathHash,
            0,
            0);
        weaponState.ArrowChargeParticleAnim.Update(0);

        TimePauseManager.Instance.gamePlayTimeScaleModifier.Resume();
        TimePauseManager.Instance.uiTimeScaleModifier.Resume();

        // CameraManager.Instance.ResetCamera2DDockerToPlayer();
        // CameraManager.Instance.camera2D.CenterOnTargets();
        // CameraManager.Instance.dummyOffset = Vector2.SmoothDamp(
        // SingletonBehaviour<CameraManager>.Instance.dummyOffset, direction, ref this.currentV, 0.25f);
    }

    public static void NormalizePost() {
        NormalizeActor(Player.i);
        Player.i.Facing = Facings.Left;
        NormalizePathFindAgent(Player.i.pathFindAgent);

        foreach (var monster in MonsterManager.Instance.monsterDict.Values) {
            NormalizeMonsterPost(monster);
            NormalizePathFindAgent(monster.pathFindAgent);
        }
    }

    private static void NormalizePathFindAgent(PathFindAgent pathFindAgent) {
        pathFindAgent.SetFieldValue("lastArea", null);
        pathFindAgent.SetFieldValue("_currentArea", null);
        pathFindAgent.GetFieldValue<List<PathArea>>("touchingAreas")!.Clear();
        pathFindAgent.target = null;
        pathFindAgent.FindCurrentPathArea();

        TasMod.Instance.StartCoroutine(X(pathFindAgent.GetComponent<BoxCollider2D>()));
    }


    private static void NormalizeActor(Actor actor) {
        actor.Velocity = Vector2.zero;
        actor.AnimationVelocity = Vector3.zero;
        actor.finalOffset = Vector2.zero;
        actor.movementCounter = Vector2.zero;
        actor.CanMove = true;

        actor.InvokeMethod("UpdateBounds");

        actor.SetFieldValue("_needToCheckGround", true);
        var onGround = actor.IsOnGroundCheck();
        actor.SetOnGround = onGround;
        actor.SetFieldValue("_wasOnGround", onGround);
        actor.lastHitGroundSpeed = 0;

        actor.SetFieldValue("lastFacing", actor.Facing);
    }

    private static void NormalizeMonsterPost(MonsterBase monsterBase) {
        NormalizeActor(monsterBase);
    }


    private static void NormalizeFsm<T>(StateMachine<T> fsm) where T : struct, IConvertible, IComparable {
        foreach (var entry in fsm._stateMapping.getAllStates) {
            entry.stateBehavior.SetFieldValue("statusTimer", 0);
        }
    }

    private static void NormalizeMonsterPre(MonsterBase monsterBase) {
        // NormalizeActor(monsterBase);
        monsterBase.canSeePlayerCondition?.ExtendFalseTime();
        monsterBase.lastAttackSensor = null;
        monsterBase.monsterContext = new MonsterBase.MonsterContextRuntimeData();
        monsterBase.EngagingTimer = 0;
        monsterBase.SetFieldValue("outOfCameraCounter", 0f);
        monsterBase.SetFieldValue("hurtSoundCooldownTimer", 0f);
        monsterBase.SetFieldValue("_cachedCanSeePlayer", false);
        monsterBase.SetFieldValue("FirstTakeDamageTime", 0f);
        monsterBase.LinkMoveReset();
        monsterBase.postureSystem.lastPostureValue = monsterBase.postureSystem.GetFieldValue<float>("_postureValue");
        monsterBase.postureSystem.GetFieldValue<InternalDamageUpdater>("_internalDamageUpdater")!.Reset();


        var moveScaler = monsterBase.animator.transform.Find("LogicRoot/MoveScale")
            ?.GetComponent<AnimationMoveScaler>();
        if (moveScaler != null) {
            moveScaler.IsContinuousUpdate = true;
            moveScaler.EvaluateOffset = 120;
            moveScaler.MoveXMinScale = 0.1f;
            moveScaler.MoveXMaxScale = 1.5f;
            moveScaler.RefDisRemain = 1f;
            moveScaler.ReferenceDistance = 0;
            moveScaler.ClearEvaluate();
            moveScaler.EvaluatePosition();
            moveScaler.SetFieldValue("_moveXScale", 0.1f);
        }
        
        TasTracerState.AddFrameHistory(JsonUtility.ToJson(moveScaler), moveScaler?.GetFieldValue<float>("_moveXScale"));
        

        NormalizeFsm(monsterBase.fsm);
        foreach (var entry in monsterBase.fsm._stateMapping.getAllStates) {
            var state = (MonsterState)entry.stateBehavior;

            state.ChangeStateTime = 0;
            state.SetFieldValue("ChangeStateTimer", 0);
            state.SetFieldValue("StateTimer", 0);

            if (state is BossGeneralState generalState) {
                generalState.QueuedAttacks.Clear();
                generalState.damageOnTimeList.Clear();
                generalState.SetFieldValue("linkMoveGroupIndex", 1);
                generalState.SetFieldValue("radToPlayer", 1f);

                var groupingNodes = generalState.GetFieldValue<LinkMoveGroupingNode[]>("groupingNodes")!;
                if (groupingNodes.Length > 0) {
                    foreach (var attack in groupingNodes) {
                        var q = attack.GetFieldValue<MonsterStateQueue>("queue")!;
                        q.QueuedAttacks.Clear();
                        q.SetFieldValue("LinkMoveOptionCount", 0);
                    }
                }
            }

            if (state is StealthEngaging engaging) {
                engaging.ForceRunToPlayer = false;
            }

            if (state is StealthPreAttackState preAttack) {
                preAttack.SetFieldValue("ChangeToEngageDelayTime", 42);
                preAttack.exitPreAttackCoolDown = 0.5f;
            }
        }

        foreach (var sensor in monsterBase.AttackSensorsCompat()) {
            sensor.SetFieldValue("_failReason", "");
            sensor.ResetAggressivenessResetCounter();
        }
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

    private static IEnumerator X(Collider2D pathFindAgent) {
        pathFindAgent.enabled = false;
        yield return new WaitForEndOfFrame();

        pathFindAgent.enabled = true;
    }

    public static void ClearFoo() {
        var fooManager = FooManager.Instance;
        var newList = fooManager.GetFieldValue<List<FooDeposit>>("newList")!;
        newList.AddRange(fooManager.deposits);

        foreach (var deposit in newList) {
            if(!deposit)continue;
            // deposit.FooExpired();
            deposit.bindingFooAttachable?.OnExpired();
            deposit.SetFieldValue("_currentValue", 0);
            deposit.transform.parent = null;
            deposit.GetFieldValue<Animator>("fooAnimator")!.gameObject.SetActive(false);
            deposit.GetFieldValue<PoolObject>("_poolObject")!.ReturnToPool();
        }

        fooManager.deposits.Clear();
        newList.Clear();
    }

    public static void StopTimers() {
        var n = UniTaskHelper.Clear(PlayerLoopTiming.Update);
        Log.Info($"Cleared {n} unitask tasks");


        List<DG.Tweening.Tween> list = [];
        DOTween.PlayingTweens(list);
        Log.Info($"Cleared {list.Count} DOTween tweens");

        DebugInfo.DoTweenManager?.InvokeMethod("PurgeAll");

        Log.Info($"Cleared {Timer.Instance.allDelayTasks.Count} Timer tasks");
        foreach (var task in Timer.Instance.allDelayTasks) {
            task.Cancel();
        }
    }
}
