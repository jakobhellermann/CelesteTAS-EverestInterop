using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using NineSolsAPI;
using TAS.Communication;
using TAS.Module;
using TAS.Tracer;
using TAS.Utils;
using UnityEngine;

namespace TAS;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin("TasTools", "TasTools", "1.0.0")]
public class TasMod : BaseUnityPlugin {
    public static TasMod Instance = null!;
    public CelesteTasSettings TasSettings = null!;

    private Harmony harmony = null!;

    private ConfigEntry<bool> configOpenStudioOnLaunch = null!;
    private ConfigEntry<KeyboardShortcut> configOpenStudioShortcut = null!;

    private static void LaunchStudio() {
        var path = Assembly.GetAssembly(typeof(TasMod)).Location;
        if (path == "") return;

        var studioPath = Path.Join(Path.GetDirectoryName(path) ?? "", "CelesteStudio.exe");
        Log.Info($"Studio path at {studioPath}");

        if (File.Exists(studioPath)) {
            var p = new Process();
            p.StartInfo = new ProcessStartInfo(studioPath) { UseShellExecute = true };
            Log.Info("Trying to start");
            var success = p.Start();
            Log.Info($"Trying to start: {success}");
        }
    }

    private void Awake() {
        Log.Init(Logger);
        Instance = this;

        try {
            configOpenStudioOnLaunch = Config.Bind("Studio", "Launch on start", true);
            configOpenStudioShortcut = Config.Bind("Studio", "Launch", new KeyboardShortcut());

            if (configOpenStudioOnLaunch.Value) {
                LaunchStudio();
            }

            KeybindManager.Add(this, LaunchStudio, () => configOpenStudioShortcut.Value);

            TasSettings = new CelesteTasSettings();
            RCGLifeCycle.DontDestroyForever(gameObject);

            AttributeUtils.CollectAllMethods<LoadAttribute>();
            AttributeUtils.CollectAllMethods<UnloadAttribute>();
            AttributeUtils.CollectAllMethods<InitializeAttribute>();
            AttributeUtils.CollectAllMethods<BeforeTasFrame>();
            AttributeUtils.CollectAllMethods<AfterTasFrame>();

            AttributeUtils.Invoke<InitializeAttribute>();
            AttributeUtils.Invoke<LoadAttribute>();

            harmony = Harmony.CreateAndPatchAll(typeof(TasMod).Assembly);
            if (!GameVersions.IsVersion(GameVersions.SpeedrunPatch)) {
                harmony.PatchAll(typeof(InputHelper.PatchesNonSpeedrunpatch));
            }

            if (TasSettings.AttemptConnectStudio) CommunicationWrapper.Start();
        } catch (Exception e) {
            Log.Error($"Failed to load {MyPluginInfo.PLUGIN_GUID}: {e}");
        }

        // https://giannisakritidis.com/blog/Early-And-Super-Late-Update-In-Unity/

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Start() {
        PlayerLoopHelper.AddAction(PlayerLoopTiming.EarlyUpdate, new PlayerLoopItem(this, EarlyUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.PostLateUpdate, new PlayerLoopItem(this, PostLateUpdate));

        PlayerLoopHelper.AddAction(PlayerLoopTiming.LastUpdate, new PlayerLoopItem(this, AfterUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.PreUpdate, new PlayerLoopItem(this, PreUpdate));
    }

    private class PlayerLoopItem(TasMod mb, Action action) : IPlayerLoopItem {
        public bool MoveNext() {
            if (!mb) return false;

            action();
            return true;
        }
    }


    private void EarlyUpdate() {
        Log.TasTrace("-- FRAME BEGIN --");

        TasTracerState.TraceVarsThroughFrame("EarlyUpdate");
    }

    private void FixedUpdate() {
        Log.TasTrace($"-- FixedUpdate dt={Time.fixedDeltaTime}--");
    }

    private static void PreUpdate() {
        if (Physics2D.simulationMode != SimulationMode2D.Script) return;

        TasTracerState.TraceVarsThroughFrame("PreUpdate");
        if (Manager.CurrState != Manager.State.Paused) {
            Physics2D.Simulate(Time.deltaTime);
        }

        TasTracerState.TraceVarsThroughFrame("PreUpdate-aftersim");
    }

    private static void AfterUpdate() {
        Log.TasTrace($"-- Update dt={Time.deltaTime}-- ");

        // CameraManager.Instance.cameraCore.dockObj.localPosition = Vector3.zero;
        TasTracerState.TraceVarsThroughFrame("Update");

        if (Manager.CurrState == Manager.State.Paused) {
            Manager.UpdateMeta();
            if (Manager.CurrState == Manager.State.Paused && Manager.NextState != Manager.State.Paused) {
                Manager.DisablePause();
            }
        }
    }

    private void LateUpdate() {
        // TasTracerState.AddFrameHistory("count", Time.frameCount);
        TasTracerState.TraceVarsThroughFrame("LateUpdate");

        // TasTracerState.AddFrameHistory("count", Time.frameCount);
        if (Manager.Running) {
            var monsters = GameVersions.Select<MonsterBase[]>(GameVersions.SpeedrunPatch,
                [],
                [MonsterManager.Instance.ClosetMonster]);
            foreach (var monster in monsters) {
                var state = (StealthPreAttackState)monster.fsm.FindMappingState(MonsterBase.States.PreAttack);
                TasTracerState.AddFrameHistory(
                    "ClosestMonster",
                    // monster.fsm.State.ToString(),
                    // AnimatorSnapshot.Snapshot(monster.animator).StateHash,
                    // AnimatorSnapshot.Snapshot(monster.animator).NormalizedTime,
                    monster.transform.position,
                    monster.pathFindAgent.IsSameAreaWithTarget,
                    state.SchemesIndex,
                    state.ApproachingSchemes.Count
                    // monster.pathFindAgent.target?.currentArea?.gameObject is {} obj ? ObjectUtils.ObjectPath(obj) : null,
                    // monster.pathFindAgent.currentArea?.gameObject is {} obj2 ? ObjectUtils.ObjectPath(obj2) : null,
                    // state.ApproachingSchemes.Count,
                    // state.SchemesIndex,
                    // state.IsFollowingSomeone,
                    // monster.transform.position,
                    // monster.AnimationVelocity,
                    // monster.fsm.isPaused,
                    // monster.finalOffset,
                    // monster.fsm.IsInTransition,
                    // ReflectionExtensions.GetFieldValue<bool>(monster, "__isEngaging"),
                    // monster.GetDistanceToPlayer()
                );
            }
        }
    }

    private void PostLateUpdate() {
        TasTracerState.TraceVarsThroughFrame("PostLateUpdate");

        Log.TasTrace("-- FRAME END --");

        try {
            GameInfo.Update();

            AttributeUtils.Invoke<AfterTasFrame>();

            if (Manager.Running) {
                try {
                    if (Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance) {
                        TasTracer.TraceFrame();
                    } else {
                        if (TasTracer.TracePauseMode == TracePauseMode.Reduced) {
                            TasTracer.TraceFramePause();
                        } else if (TasTracer.TracePauseMode == TracePauseMode.Full) {
                            TasTracer.TraceFrame();
                        }
                    }
                } catch (Exception e) {
                    e.LogException("Error trying to collect trace data");
                }
            }

            AttributeUtils.Invoke<BeforeTasFrame>();

            Manager.UpdateMeta();
            Manager.Update();

            Log.TasTrace($"State: {Manager.CurrState} -> {Manager.NextState}");
            /*TasTracerState.AddFrameHistory("StateAfter",
                new TracerIrrelevantState($"{Manager.CurrState} -> {Manager.NextState}"));*/
        } catch (Exception e) {
            e.LogException("");
            Manager.DisableRun();
        }
    }

    private void OnDestroy() {
        AttributeUtils.Invoke<UnloadAttribute>();
        if (Manager.Running) Manager.DisableRun();
        harmony?.UnpatchSelf();

        CommunicationWrapper.SendReset();
        CommunicationWrapper.Stop();
    }
}

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class LoadAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class UnloadAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class InitializeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class BeforeTasFrame : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class AfterTasFrame : Attribute;
