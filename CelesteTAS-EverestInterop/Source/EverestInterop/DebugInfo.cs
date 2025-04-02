using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using EverestInterop;
using HarmonyLib;
using MonsterLove.StateMachine;
using NineSolsAPI;
using NineSolsAPI.Utils;
using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Random = UnityEngine.Random;
using Tween = Febucci.UI.Core.Tween;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace TAS;

[HarmonyPatch]
[SuppressMessage("Method Declaration", "Harmony003:Harmony non-ref patch parameters modified")]
public static class DebugInfo {
    private static float lastDeltaTime;

    [Flags]
    public enum DebugFilter {
        Base = 0,
        RapidlyChanging = 1 << 0,
        Tweens = 1 << 1,
        Monsters = 1 << 2,
        Camera = 1 << 3,
        AttackSensors = 1 << 4,
        AnimationClips = 1 << 5,
        AllAnimatorLayers = 1 << 6,

        All = RapidlyChanging | Tweens | Monsters | Camera | AttackSensors,
    }

    private static readonly Regex RegexCleanMethod = new(@"^<([^>]+)>b__\d+$");

    private static string ExtractMethodName(string input) {
        var match = RegexCleanMethod.Match(input);
        return match.Success ? match.Groups[1].Value : input; // Return original if no match
    }


    public static string GetInfoText(DebugFilter filter = DebugFilter.Base) {
        var text = "";

        if (filter.HasFlag(DebugFilter.Camera)) {
            var cameraCore = CameraManager.Instance.cameraCore;
            var proCam = CameraManager.Instance.camera2D;
            text += "Camera\n";
            text += $"  Pos: {cameraCore.theRealSceneCamera.transform.position}\n";
            text += $"  Dock: {cameraCore.dockObj.transform.localPosition}\n";
            text += "Camera2d\n";
            text += $"  Pos: {proCam.transform.position}\n";
            text += $"  LocalPos: {proCam.transform.localPosition}\n";
            text += $"  Target: {proCam.CameraTargetPosition}\n";
            text += "\n";
        }

        if (filter.HasFlag(DebugFilter.Tweens)) {
            var hasRcg = false;
            var rcgTimer = Timer.Instance;
            foreach (var delayTask in rcgTimer.allDelayTasks) {
                if (!hasRcg) {
                    hasRcg = true;
                    text += "RCGTimer:\n";
                }

                // var _type = delayTask.GetFieldValue<UpdateType>("_type");
                var counter = delayTask.GetFieldValue<float>("_counter");
                var delay = delayTask.GetFieldValue<float>("_delay");

                text +=
                    $"-{delayTask.ID} Timer({counter:0.00}/{delay:0.00} by '{CleanName(delayTask.BelongTo?.name)}')\n";
            }

            var hasDoTween = false;
            List<DG.Tweening.Tween> list = [];
            foreach (var tween in DOTween.PlayingTweens(list) ?? []) {
                try {
                    if (tween.GetType().GetGenericTypeDefinition() == typeof(TweenerCore<,,>)) {
                        var start = tween.GetFieldValue<object>("startValue");
                        var end = tween.GetFieldValue<object>("endValue");
                        var tweenType = start?.GetType();

                        var getter = tween.GetFieldValue<Delegate>("getter")!.Method;
                        if (tweenType == typeof(Color) || getter.Name == "DoShakePosition" ||
                            tween.target is Transform { name: "AudioListener" }) {
                            continue;
                        }

                        if (!hasDoTween) {
                            hasDoTween = true;
                            text += "DOTween:\n";
                        }

                        string? tweenName = null;
                        if (getter.Name.StartsWith("<DOLocalMove>")) tweenName = "LocalPos";

                        var tweenDesc = tweenName ?? $"<{tweenType}>";
                        text +=
                            $"- Tween{tweenDesc}({tween.Elapsed():0.00}/{tween.Duration():0.00}";
                        if (tween.target != null) text += $" on '{tween.target}'";
                        // if (tweenName == null) text += " " + ExtractMethodName(getter.Name);
                        text += " " + ExtractMethodName(getter.Name);
                        text += $" {start} to {end}\n";
                    } else {
                        text += $"TODO: other tween {tween}\n";
                    }
                } catch (Exception e) {
                    text += $"{e}\n";
                }
            }


            var hasUniTask = false;
            foreach (var item in UniTaskHelper.GetLoopItems(PlayerLoopTiming.Update)) {
                if (item == null) continue;

                var typeName = item.GetType().Name;
                if (typeName == "AwakeMonitor") continue;

                if (!hasUniTask) {
                    hasUniTask = true;
                    text += "UniTask:\n";
                }

                text += "- ";
                if (typeName == "DelayPromise") {
                    var initialFrame = item.GetFieldValue<int>("initialFrame");
                    var delayTimeSpan = item.GetFieldValue<float>("delayTimeSpan");
                    var elapsed = item.GetFieldValue<float>("elapsed");
                    text +=
                        $"Delay(elapsed: {elapsed:0.00}, delayTimeSpan: {delayTimeSpan:0.00}, initialFrame: {initialFrame})\n";
                } else if (typeName == "DelayFramePromise") {
                    // todo patch this
                    var initialFrame = item.GetFieldValue<int>("initialFrame");
                    var delayFrameCount = item.GetFieldValue<int>("delayFrameCount");
                    var currentFrameCount = item.GetFieldValue<int>("currentFrameCount");
                    text += $"DelayFrame({currentFrameCount}/{delayFrameCount}, initialFrame: {initialFrame})\n";
                } else if (typeName == "DelayIgnoreTimeScale") {
                    var initialFrame = item.GetFieldValue<int>("initialFrame");
                    var delayFrameCount = item.GetFieldValue<int>("delayFrameCount");
                    var currentFrameCount = item.GetFieldValue<int>("currentFrameCount");
                    text += $"DelayFrame({currentFrameCount}/{delayFrameCount}, initialFrame: {initialFrame})\n";
                } else if (typeName == "DelayIgnoreTimeScalePromise") {
                    var initialFrame = item.GetFieldValue<int>("initialFrame");
                    var timeSpan = item.GetFieldValue<float>("delayFrameTimeSpan");
                    var elapsed = item.GetFieldValue<float>("elapsed");
                    text +=
                        $"DelayFrameIgnoreTimescale({elapsed:0.00}/{timeSpan:0.00}, initialFrame: {initialFrame})\n";
                } else if (typeName == "WaitUntilPromise") {
                    var predicate = item.GetFieldValue<Func<bool>>("predicate")!.Method;
                    text += $"WaitUntilPromise({predicate.DeclaringType}.{predicate.Name})\n";
                } else {
                    text += $"{item}\n";
                }
            }

            text += "\n";
        }

        if (filter.HasFlag(DebugFilter.Tweens) && typeof(Tween).Assembly.GetType("PrimeTween.PrimeTweenManager") is
                { } ty) {
            text += "Tweens:\n";
            var instance = ty.GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var tweens = instance.GetFieldValue<IList>("tweens")!;
            foreach (var tween in tweens) {
                text += $"- {tween}\n";
            }

            text += "\n";
        }


        if (!ApplicationCore.IsAvailable()) return "Loading";

        if (!GameCore.IsAvailable()) {
            text += "MainMenu\n";
            text += PlayerInputBinder.Instance.currentStateType.ToString();
            return text;
        }

        var core = GameCore.Instance;

        if (core.currentCoreState != GameCore.GameCoreState.Playing) {
            var coreState = typeof(GameCore.GameCoreState).GetEnumName(core.currentCoreState);
            text += $"{coreState}\n";
        }

        var player = core.player;
        if (player) {
            var stateName = typeof(PlayerStateType).GetEnumName(player.fsm.State);
            var inputState = player.playerInput.fsm.State;
            var playerState = (PlayerBaseState)player.fsm.FindMappingState(player.fsm.State);
            var animInfo = player.animator.GetCurrentAnimatorStateInfo(0);
            var clipInfo = player.animator.GetCurrentAnimatorClipInfo(0)[0];

            float? canMoveAt = null;
            float? canParryAt = null;
            float? fooExplodeAt = null;

            foreach (var evt in clipInfo.clip.events) {
                var animationTag = (PlayerAnimationEventTag)evt.intParameter;
                var canMoveTag = playerState is PlayerAttackState
                    ? PlayerAnimationEventTag.Break
                    : PlayerAnimationEventTag.CanMove;
                if (animationTag == canMoveTag) canMoveAt = evt.time;
                else if (animationTag == PlayerAnimationEventTag.AllowParry) canParryAt = evt.time;

                if (playerState is PlayerFooAttackState && animationTag == PlayerAnimationEventTag.Break) {
                    fooExplodeAt = evt.time;
                }
            }

            if (canParryAt == canMoveAt || playerState is PlayerNormalState) canParryAt = null;

            var animTime = animInfo.normalizedTime % 1 * animInfo.length;
            var canMoveIn = canMoveAt - animTime;
            var canParryIn = canParryAt - animTime;
            var fooExplodeIn = fooExplodeAt - animTime;
            var canMove = canMoveAt == null // idle state doesn't care
                          || playerState.GetFieldValue<bool>("canMove")
                          || (playerState is PlayerAttackState && canMoveIn <= 0);
            var canAttack = canMove && !player.IsSlowWalk && player.actions.Attack.Enabled
                            && player is { meleeAttackCooldownTimer: <= 0, IsInQTETutorial: false }
                                and not { IsAirBorne: true, canAirAttack: false }
                            && !(playerState is PlayerAttackState && !player.mainAbilities.QuickAttack.IsActivated);

            text += $"Pos: {(Vector2)player.transform.position}\n";
            text += $"Vel: {player.FinalVelocity}\n";
            // text += $"HP:  {player.health.CurrentHealthValue:0.00} (+{player.health.CurrentInternalInjury:0.00})\n";
            text += $"State:  {stateName} {(inputState == PlayerInputStateType.Action ? "" : inputState.ToString())}\n";
            // text += $"Area: {player.pathFindAgent.currentArea}\n";
            // text += // $"TouchingAreas: {player.pathFindAgent.GetFieldValue<List<PathArea>>("touchingAreas")!.Select(x => x.name).Join()}\n";

            List<(bool, string)> flags = [
                (!canMove, "!CanMove"),
                (!player.CanMove, "!CanMoveActor"),
                (player.freeze, "Freeze"),
                (player.isOnWall, "Wall"),
                (player.isOnLedge, "Ledge"),
                (player.isOnRope, "Rope"),
                (player.kicked, "Kicked"),
                (player.IsBreaking, "Breaking"),
                (player.lockMoving, "Locked"),
                (player.interactableFinder.CurrentInteractableArea, "CanInteract"),
                (player.IsScriptedMove, "ScriptedMove"),
                (player.rollCooldownTimer <= 0 && canMove, "CanDash"),
                (player.airJumpCount > 0, "AirJumping"),
                // (player.IsDodgeAttack, "DodgeAttack"),
                (canAttack, "CanAttack"),
                (!player.canAirAttack, "!CanAirAttack"),
                (player.onGround, "OnGround"),
            ];

            if (player.freeze) {
                var vote = player.GetFieldValue<RuntimeConditionVote>("FreezeVote")!;
                foreach (var (who, v) in vote.votes) {
                    text += $"{who} {v} ";
                }
            }


            var attackChargeTime = 0.5f + player.mainAbilities.ChargedAttackChargeTime.Value;
            List<(float, string)> timers = [
                (canMoveIn ?? 0, "CanMoveIn"),
                (canParryIn ?? 0, "CanParryIn"),
                (fooExplodeIn ?? 0, "FooExplodeIn"),
                (player.rollCooldownTimer, "RollCD"),
                (player.dashCooldownTimer, "DashCD"),
                (player.meleeAttackCooldownTimer, "AttackCD"),
                (player.fooAttackCoolDownTimer.currentValue, "TalismanCD"),
                (player.parryCoolDownTimer, "ParryCD"),
                (player.gadgetCooldownTimer, "GadgetCD"),
                (player.grabHookCooldownTimer, "GrabHookCD"),
                (player.spinMoveCooldownTimer, "SpinMoveCD"),
                (player.jumpGraceTimer != player.JumpGraceTime ? player.jumpGraceTimer : 0, "Coyote"),
                (player.IsCharging ? attackChargeTime - player.AttackChargedTimer : 0, "AttackCharge"),
            ];
            // I don't see when this is relevant, given canMove
            // if (player.fsm.State == PlayerStateType.Roll) timers.Add((0.1f - playerState.statusTimer, "DashNoMove"));
            text += "Flags:  " + Flags(flags) + "\n";
            text += "Timers: " + Timers(timers) + "\n";

            text += AnimationText(player.animator, filter);

            if (playerState is PlayerAttackState attackState) {
                var isAir = attackState.GetFieldValue<bool>("isAir");
                text += "Attack: " + Flags([(isAir, "IsAir")]) +
                        $"attackIdx={attackState.GetFieldValue<int>("count")}\n";
            } else if (playerState is PlayerParryState or PlayerParriedState) {
                text += "Parried:";
                if (lastParryParam is { } lastParry) {
                    text +=
                        $" knockback={lastParry.knockBackType.ToString()} {lastParry.knockBackValue} {lastParry.hurtLiftType}\n";
                }
                // } else if (playerState is PlayerFooExplodeState fooExplodeState) {
                // text += $"FooExplode: {fooExplodeState.GetFieldValue("subState")}";
            } else {
                text += "\n";
            }

            if (player.jumpState != Player.PlayerJumpState.None) {
                var varJumpTimer = player.currentVarJumpTimer;
                var groundReference = player.GetFieldValue<float>("GroundJumpRefrenceY");
                var height = player.transform.position.y - groundReference;
                text +=
                    $"JumpState {player.jumpState} {(varJumpTimer > 0 ? varJumpTimer.ToString("0.00") + " " : "")}h={height:0.00}\n";
            } else text += "\n";

            if (filter.HasFlag(DebugFilter.AnimationClips)) {
                for (var layer = 0; layer < player.animator.layerCount; layer++) {
                    text += ClipText<PlayerAnimationEventTag>(player.animator, layer);
                    if (!filter.HasFlag(DebugFilter.AllAnimatorLayers)) break;
                }

                text += "\n";
            }
        }

        var playerNymphState =
            (PlayerHackDroneControlState)player.fsm.FindMappingState(PlayerStateType.HackDroneControl);
        var nymph = playerNymphState.hackDrone;

        if (nymph.fsm != null && nymph.fsm.State != HackDrone.DroneStateType.Init) {
            text += $"\nNymph {nymph.fsm.State}\n";
            text += $"  Position: {(Vector2)nymph.transform.position}\n";
            text += $"  Speed: {(Vector2)nymph.droneVel}\n";
            text += "  " + Flags([
                (nymph.GetFieldValue<bool>("isDashCD"), "DashCD"),
                (nymph.GetFieldValue<bool>("isOutOfRange"), "OutOfRange"),
            ]) + " ";
            text += Timers([(nymph.GetFieldValue<float>("OutOfRangeTimer"), "OutOfRange")]) + "\n";
            text += AnimationText(nymph.animator, filter) + "\n";
        }

        var currentLevel = core.gameLevel;
        if (currentLevel) {
            text += $"[{currentLevel.SceneName}] ({currentLevel.BlockCountX}x{currentLevel.BlockCountY})";
            if (filter.HasFlag(DebugFilter.RapidlyChanging)) {
                if (Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance)
                    lastDeltaTime = Time.deltaTime;
                text += $" dt={lastDeltaTime:0.00000000}";
            }

            text += "\n";
        }

        if (core.currentCutScene) {
            text += $"{core.currentCutScene}";
            if (core.currentCutScene is SimpleCutsceneManager cutscene) {
                var currentTime = cutscene.GetFieldValue<float>("currentTime");
                var duration = cutscene.playableDirector.duration;
                text += $" {currentTime:0.00}/{duration:0.00}";
                text += $"{cutscene.BindingTeleportPoint}";
                /*var graph = cutscene.playableDirector.;
                text += $"{graph}\n";
                for (int i = 0; i < graph.GetOutputCount(); i++) {
                    var output = graph.GetOutput(i);
                    text += $"{output} {output.GetPlayableOutputType()} {output.GetWeight()}\n";
                }

                /*var asset = cutscene.playableDirector.playableAsset;
                text += $"{asset.duration}\n";
                foreach (var output in asset.outputs) {
                    text += $"- {output.streamName} {output.sourceObject} {output.outputTargetType} {output.m_CreateOutputMethod} {output.m_SourceBindingType}\n";
                }*/
            }

            // text += "\n";
        }

        // var randomState = Random.state.GetHashCode();
        // text += $"RandomState: {HashToAlphabet(randomState)}";


        if (filter.HasFlag(DebugFilter.Monsters)) text += "\n" + GetMonsterInfotext(filter);

        return text;
    }

    private static string HashToAlphabet<T>(T input) where T : notnull {
        var hash = input.GetHashCode();
        var positiveHash = (uint)hash;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var result = new char[4];
        for (var i = 0; i < 4; i++) {
            result[i] = alphabet[(int)(positiveHash % alphabet.Length)];
            positiveHash /= (uint)alphabet.Length;
        }

        return new string(result);
    }

    private static string Flags(IEnumerable<(bool, string)> flags) {
        var text = "";
        foreach (var (val, name) in flags) {
            if (!val) continue;

            text += $"{name} ";
        }

        return text;
    }

    private static string Values(IEnumerable<(object?, string)> values) {
        var text = "";
        foreach (var (val, name) in values) {
            if (val is null or "") continue;

            text += $"{name}={val} ";
        }

        return text;
    }

    private static int RoundUpTimeToFrames(float time) {
        var frames = time * InputHelper.CurrentTasFramerate;
        var rounded = Math.Round(frames, 4);
        return (int)Math.Ceiling(rounded);
    }

    private static string FormatTime(float time) =>
        timeMode == TimeMode.Time ? "{time:0.000}" : RoundUpTimeToFrames(time).ToString();

    private enum TimeMode {
        Frames,
        Time,
    }

    private static TimeMode timeMode = TimeMode.Frames;

    private static string Timers(IEnumerable<(float, string)> timers) {
        var text = "";
        foreach (var (timer, name) in timers) {
            if (timer <= 0) continue;

            text += $"{name}({FormatTime(timer)})";
            text += " ";
        }

        return text;
    }


    private static string AnimationText(Animator animator, DebugFilter filter) {
        if (!filter.HasFlag(DebugFilter.AllAnimatorLayers)) {
            return AnimationTextSingle(animator, filter.HasFlag(DebugFilter.RapidlyChanging), 0);
        }

        var text = "";
        for (var layer = 0; layer < animator.layerCount; layer++) {
            var name = animator.GetLayerName(layer);
            var weight = animator.GetLayerWeight(layer);
            text += $"{name}@{weight}\n";
            text += AnimationTextSingle(animator, filter.HasFlag(DebugFilter.RapidlyChanging), layer);
        }

        return text;
    }

    private static string AnimationTextSingle(Animator animator, bool includeRapidlyChanging, int layer = 0) {
        var animInfo = animator.GetCurrentAnimatorStateInfo(layer);
        var animName = animator.ResolveHash(animInfo.m_Name);
        var text = $"Animation {animName}";
        if (includeRapidlyChanging) {
            var time = animInfo.normalizedTime * animInfo.length;

            // text += $" {animInfo.normalizedTime % 1 * 100:00.0}%";
            text += $" {FormatTime(time)}/{FormatTime(animInfo.length)}";
            if (timeMode == TimeMode.Time) text += "s";
            if (animInfo.speed != 1) text += $" @ {animInfo.speed}";
        }

        return $"{text}\n";
    }

    public static string GetMonsterInfotext(DebugFilter filter) {
        var text = "";
        foreach (var monster in MonsterManager.Instance.monsterDict.Values) {
            if (!monster.isActiveAndEnabled) {
                // text += "(disabled) ";
                continue;
            }

            text += GetMonsterInfotext(monster, filter) + "\n";
        }

        return text;
    }

    public static string GetMonsterInfotext(MonsterBase monster, DebugFilter filter) {
        var text = "";
        text += MonsterName(monster) + "\n";

        var state = monster.fsm.FindMappingState(monster.fsm.State);
        var animInfo = monster.animator.GetCurrentAnimatorStateInfo(0);
        var preAttackState = (StealthPreAttackState?)monster.fsm.FindMappingState(MonsterBase.States.PreAttack);

        text += $"Pos: {(Vector2)monster.transform.position}\n";
        text += $"Vel: {monster.Velocity} + {(Vector2)monster.AnimationVelocity}\n";
        text += $"HP:  {monster.health.currentValue:0.00}\n";
        // text += $"Area: {monster.pathFindAgent.currentArea}\n";
        // text += $"TouchingAreas: {monster.pathFindAgent.GetFieldValue<List<PathArea>>("touchingAreas")!.Select(x => x.name).Join()}\n";
        if (state == null) {
            text += "fsm is null\n";
            return text;
        }

        text += $"State:  {monster.fsm.State} '{FsmStateName(state)}'";
        text += $" {animInfo.normalizedTime % 1 * 100:00}%";
        text += "\n";
        var flags = new List<(bool, string)>();
        var timers = new List<(float, string)>();
        if (!monster.IsAlwaysEngaging) flags.Add((monster.IsEngaging, "Engaging"));
        flags.AddRange([
            (monster.isDefending, "Defending"),
            (preAttackState?.IsFollowingSomeone ?? false, "Following"),
            (monster.IsEngagingFollowing, "EngagingFollowing"),
            (monster.IsWanderingFollowing, "WanderingFollowing"),
            (monster.IsParriedWillAttack, "ParriedWillAttack"),
            (monster.monsterContext.IsInForceDisEngageArea, "ForceDisengage"),
            (monster.GetPropertyValue<bool>("isForceDisEngage"), "ForceDisengage"),
            (monster.GetPropertyValue<bool>("isForceEngage"), "ForceEngage"),
            (monster.pathFindAgent.IsSameAreaWithTarget && !(Manager.Running && Manager.Controller.CurrentFrameInTas <= 1),
                "SameArea"), // TODO: flaky first frame
            (monster.CutSceneCheck(), "Cutscene"),
        ]);

        if (!monster.IsAlwaysEngaging) timers.Add((monster.EngagingTimer, "Engaging"));
        timers.AddRange([
            (monster.CanSeePlayerFalseTimer, "!CanSeePlayer"),
            (monster.CanSeePlayerTrueTimer, "CanSeePlayer"),
            // (preAttackState.exitPreAttackCoolDown, "PreAttackExit"), TODO: flaky
            (preAttackState?.GetFieldValue<float>("ChangeToEngageDelayTime") ?? 0, "ChangeToEngageDelayTime"),
        ]);
        text += "Flags:  " + Flags(flags) + "\n";
        text += "Timers: " + Timers(timers) + "\n";

        /*if (preAttackState != null) {
            if (preAttackState.ApproachingSchemes.Count > 1) text += "TODO: multiple schemes\n";

            var currentScheme = preAttackState.SchemesIndex != -1
                ? preAttackState.ApproachingSchemes[preAttackState.SchemesIndex]
                : null;
            text += $"Vars:   Scheme={currentScheme?.name} Dist={monster.GetDistanceToPlayer():0.00}\n";

            text += "Schemes:\n";
            foreach (var scheme in preAttackState.ApproachingSchemes) {
                text +=
                    $"- {scheme.name} {scheme.EnterApproachingRange} ± {scheme.EnterApproachingRangeRandomOffset}\n";
            }

            text += "\n";
        }*/

        // text +=
        // $"Stat: RaycastNeeded={monster.monsterStat.IsRayCastToPlayerNeededForEngage} IsGuarding={preAttackState?.IsGuardingPath}\n\n";


        var core = monster.monsterCore;

        if (core.attackSequenceMoodule.getCurrentSequence() is not null) {
            text += "TODO: attack sequence\n";
        }

        var hurtInterrupt = monster.HurtInterrupt;
        if (hurtInterrupt.isActiveAndEnabled) {
            var th = hurtInterrupt.GetFieldValue<float>("AccumulateDamageTh");
            if (th > 0) {
                text +=
                    $"Hurt Interrupt: {hurtInterrupt.currentAccumulateDamage / monster.postureSystem.FullPostureValue:0.00} > {th}\n";
            }
        }

        var canCrit = (bool)typeof(MonsterBase)
            .GetMethod("MonsterStatCanCriticalHit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(monster, []);
        if (canCrit) {
            // if (monster.IsEngaging) text += "IsEngaging\n";
        }

        if (filter.HasFlag(DebugFilter.AttackSensors)) {
            var attackSensors = monster.AttackSensorsCompat().ToArray();

            text += "Attack:\n";

            text += "Inside: ";
            foreach (var attackSensor in attackSensors) {
                if (attackSensor.IsPlayerInside) text += attackSensor.name + " ";
            }

            text += "\n";

            foreach (var attackSensor in attackSensors) {
                var name = ReNumPrefix.Replace(attackSensor.name, "");
                var active = attackSensor.gameObject.activeInHierarchy;
                var conditions = attackSensor.GetFieldValue<AbstractConditionComp[]>("_conditions");
                var currentState = monster.CurrentState;
                var stateCheck = attackSensor.forStateType == AttackSensorForStateType.AllValid ||
                                 (!(attackSensor.forStateType ==
                                    AttackSensorForStateType.EngagingAndPreAttackOrOutOfReachAndPanic &&
                                    currentState is not (MonsterBase.States.RunAway or MonsterBase.States.Panic
                                        or MonsterBase.States.OutOfReach or MonsterBase.States.LookingAround
                                        or MonsterBase.States.Engaging or MonsterBase.States.PreAttack)) &&
                                  !(attackSensor.forStateType == AttackSensorForStateType.EngagingOnly &&
                                    currentState is not MonsterBase.States.Engaging) &&
                                  !(attackSensor.forStateType == AttackSensorForStateType.PreAttackOnly &&
                                    currentState is not MonsterBase.States.PreAttack) &&
                                  !(attackSensor.forStateType == AttackSensorForStateType.WanderingAndIdleOnly &&
                                    currentState is not (MonsterBase.States.Wandering
                                        or MonsterBase.States.WanderingIdle)));


                if (!active) {
                    /*text += "\n\n";
                    foreach (var _ in conditions ?? []) {
                        text += "\n";
                    }*/

                    continue;
                }

                if (!active) text += "(inactive) ";
                text += $"{name}";
                if (attackSensor.BindindAttack != MonsterBase.States.Attack1)
                    text += $"{name}({attackSensor.BindindAttack})";
                text += "\n";

                foreach (var condition in conditions ?? []) {
                    var conditionStr = FormatCondition(condition);
                    text += $"    if: {conditionStr}\n";
                }

                text += "  " + Flags([
                    (!stateCheck, "WrongState"),
                    (attackSensor.playerInsideTimer >= attackSensor.currentAttackDelay, "InRangeLongEnough"),
                ]);

                text += " " + Values([
                    (attackSensor.aggressivenessResetCounter, "AggroCounter"),
                    (attackSensor.GetFieldValue<string>("_failReason"), "failReason"),
                ]);
                text += Timers([
                    (attackSensor.GetFieldValue<float>("_cooldownCounter"), "Cooldown"),
                    (attackSensor.currentAttackDelay, "AttackDelay"),
                    // (attackSensor.playerInsideTimer, "PlayerInside"), TODO
                    (attackSensor.triggerDelay, "TriggerDelay"),
                ]);
                text += "\n";
                // aggroCounter=0 -> cooldown = 0.4 on hit

                // text += "  fail: " + attackSensor.GetFieldValue<string>("_failReason") + "\n";

                /*var typeName = attackSensor.forStateType switch {
                    AttackSensorForStateType.EngagingAndPreAttackOrOutOfReachAndPanic => "EARP",
                    _ => attackSensor.forStateType.ToString(),
                };/
                text += $" {typeName}:";*/
                //     var stateCheck = attackSensor.forStateType == AttackSensorForStateType.AllValid ||
                //                      (!(attackSensor.forStateType ==
                //                         AttackSensorForStateType.EngagingAndPreAttackOrOutOfReachAndPanic &&
                //                         currentState is not (MonsterBase.States.RunAway or MonsterBase.States.Panic
                //                             or MonsterBase.States.OutOfReach or MonsterBase.States.LookingAround
                //                             or MonsterBase.States.Engaging or MonsterBase.States.PreAttack)) &&
                //                       !(attackSensor.forStateType == AttackSensorForStateType.EngagingOnly &&
                //                         currentState is not MonsterBase.States.Engaging) &&
                //                       !(attackSensor.forStateType == AttackSensorForStateType.PreAttackOnly &&
                //                         currentState is not MonsterBase.States.PreAttack) &&
                //                       !(attackSensor.forStateType == AttackSensorForStateType.WanderingAndIdleOnly &&
                //                         currentState is not (MonsterBase.States.Wandering
                //                             or MonsterBase.States.WanderingIdle)));
                //     if (!stateCheck) {
                //         // text += " WrongState";
                //     } else if (!attackSensor.CanAttack()) {
                //         if (!attackSensor.IsPlayerInside) {
                //             text += " PlayerOutside";
                //         } else if (attackSensor.CurrentCoolDown > 0) {
                //             text += " OnCooldown";
                //         } else if (attackSensor.playerInsideTimer < attackSensor.currentAttackDelay) {
                //             text += " AttackDelay";
                //         } else {
                //             text += " !CanAttack";
                //         }
                //     }

                if (attackSensor.QueuedAttacks.Count > 0) {
                    text += "  Queue:\n";
                    foreach (var attack in attackSensor.QueuedAttacks) {
                        text += $"  - {FsmStateName(monster.fsm, attack)}\n";
                    }
                }

                if (attackSensor.BindingAttacks.Count > 0) {
                    text += "  BindingAttacks:\n";
                    foreach (var attack in attackSensor.BindingAttacks) {
                        text += $"  - {FsmStateName(monster.fsm, attack)}\n";
                    }
                }

                // foreach(var bindingAttack in attackSensor.BindingAttacks)
                // text += attackSensor.AccessField<string>("_failReason");
            }

            if (state is BossGeneralState bgs) {
                text += "\n";

                if (bgs.attackQueue) {
                    text += "AttackQueue:\n";
                    foreach (var attack in bgs.attackQueue.QueuedAttacks) {
                        text += $"- {FsmStateName(monster.fsm, attack)}\n";
                    }
                }

                text += "Queue:\n";
                foreach (var attack in bgs.QueuedAttacks) {
                    text += $"- {FsmStateName(monster.fsm, attack)}\n";
                }

                if (bgs.clip != null && filter.HasFlag(DebugFilter.AnimationClips)) {
                    text += ClipText<AnimationEvents.AnimationEvent>(bgs.clip, animInfo);
                }
            }
        }


        return text;
    }

    private static string ClipText<T>(Animator animator, int layerIndex) {
        var animInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        var clipInfos = animator.GetCurrentAnimatorClipInfo(layerIndex);
        if (clipInfos.Length != 1) return "";

        var clipInfo = clipInfos[0];

        return ClipText<T>(clipInfo.clip, animInfo);
    }

    private static string ClipText<T>(AnimationClip clip, AnimatorStateInfo info) {
        var text = "";
        text += "Clip:\n";
        text += $"+ {info.normalizedTime:0.00}\n";
        foreach (var evt in clip.events) {
            var e = Enum.ToObject(typeof(T), evt.intParameter);
            text += $"- {evt.time:0.00}: {e}\n";
        }

        return text;
    }

    private static string FsmStateName(MappingState mappingState) =>
        ReCjk.Replace(mappingState.name, "").Replace("  ", " ").Trim();

    private static string FsmStateName(StateMachine<MonsterBase.States> monster, MonsterBase.States state) {
        if (monster.FindMappingState(state) is { } mappingState) {
            return FsmStateName(mappingState);
        }

        return state.ToString();
    }

    private static readonly Regex ReNumPrefix = new(@"\d+_");

    private static readonly Regex ReCjk = new(@"_?\p{IsCJKUnifiedIdeographs}+|^\d+_");
    // private static readonly Regex ReCjk = new(@"_?\p{IsCJKUnifiedIdeographs}+|\[\d+\] ?|^\d+_");

    private static string MonsterName(MonsterBase monster) {
        var field = typeof(MonsterBase).GetField("_monsterStat") ?? typeof(MonsterBase).GetField("monsterStat");
        var stat = (MonsterStat)field.GetValue(monster);
        var monsterName = stat.monsterName.ToString();

        if (monsterName != "") return monsterName;

        return monster.name.TrimStartMatches("StealthGameMonster_").TrimStartMatches("TrapMonster_")
            .TrimEndMatches("(Clone)")
            .ToString();
    }

    private static string FormatCondition(AbstractConditionComp condition) {
        var name = condition.name.TrimStartMatches("[Condition] ").ToString();
        var conditionStr = "";

        switch (condition) {
            case FlagBoolCondition boolCondition: {
                conditionStr =
                    $"{name} bool flag {FormatVariable(boolCondition.flagBool)} current {boolCondition.flagBool?.FlagValue}";
                conditionStr += $" {boolCondition.flagBool?.boolFlag?.FinalSaveID}";
                conditionStr += $" {boolCondition.flagBool}";
                break;
            }
            case GeneralCondition generalCondition: {
                conditionStr += $" {generalCondition}";
                break;
            }
            case PlayerMovePredictCondition movePredictCondition: {
                conditionStr += "Player";
                if (movePredictCondition.ParryDetect) conditionStr += " Parry";
                if (movePredictCondition.DodgeDetect) conditionStr += " Dash";
                if (movePredictCondition.JumpDetect) conditionStr += " Jump";
                if (movePredictCondition.InAirDetect) conditionStr += " InAir";
                if (movePredictCondition.AttackDetect) conditionStr += " Attack";
                if (movePredictCondition.ThirdAttackDetect) conditionStr += " Third";
                if (movePredictCondition.ChargeAttackDetect) conditionStr += " Charged";
                if (movePredictCondition.FooDetect) conditionStr += " Foo";
                if (movePredictCondition.ArrowDetect) conditionStr += " Shoots";
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (movePredictCondition.randomChance != 1.0)
                    conditionStr += $" at {movePredictCondition.randomChance * 100:0}%";
                break;
            }
            default: {
                conditionStr += $" {condition.GetType()}";
                break;
            }
        }

        if (condition.FinalResultInverted) conditionStr = $"(inverted) {conditionStr}";
        // return $"({(condition.FinalResult ? "true" : "false")}) {conditionStr}";
        // return $"(todo) {conditionStr}";
        return $"{conditionStr}";
    }

    private static string FormatVariable(AbstractVariable? variable) {
        if (variable == null) return "null";

        var name = variable.ToString().TrimStartMatches("[Variable] ")
            .TrimEndMatches(" (VariableBool)").ToString();

        return $"{name} {variable.FinalData?.GetSaveID}";
    }

    private static readonly Regex ReCleanName = new(@"\[\d+\] ?|^\d+_");

    private static string? CleanName(string? name) => name is null ? null : ReCleanName.Replace(name, "");

    #region Patches

    private static ParryParam? lastParryParam;

    [EnableRun]
    private static void Reset() {
        lastParryParam = null;
    }

    [HarmonyPatch(typeof(PlayerParryState), nameof(PlayerParryState.Parried))]
    [HarmonyPostfix]
    private static void OnParry(bool __result, ParryParam param) {
        if (__result) {
            lastParryParam = param;
        }
    }

    #endregion
}
