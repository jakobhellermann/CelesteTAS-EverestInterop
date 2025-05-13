using System;
using BepInEx;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using NineSolsAPI;
using TAS.Communication;
using TAS.ModInterop;
using TAS.Module;
using TAS.Tracer;
using TAS.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TAS;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin("TasTools", "TasTools", "1.0.0")]
public class TasMod : BaseUnityPlugin {
    public static TasMod Instance = null!;
    public CelesteTasSettings TasSettings = null!;

    private Harmony harmony = null!;

    internal ConfigEntry<TasTracerFilter> ConfigTasTraceFilter = null!;
    internal ConfigEntry<bool> ConfigTasTraceFrameHistory = null!;
    internal ConfigEntry<bool> ConfigTasTraceClearOnStart = null!;

    private ConfigEntry<bool> configAutoHeal = null!;
    private ConfigEntry<bool> configAutoSkipCutscenes = null!;

    internal ConfigEntry<DebugInfo.DebugFilter> ConfigDebugInfo = null!;

    // private ConfigEntry<bool> configOpenStudioOnLaunch = null!;
    // private ConfigEntry<KeyboardShortcut> configOpenStudioShortcut = null!;

    public DebugModPlusInteropGlue? DebugModPlusInterop;

    /*private static void LaunchStudio() {
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
    }*/

    private void Awake() {
        Log.Init(Logger);
        Instance = this;

        try {
            ConfigTasTraceFrameHistory = Config.Bind("Tracer", "Frame History", false);
            ConfigTasTraceClearOnStart = Config.Bind("Tracer", "Clear on start", false);
            ConfigTasTraceFilter = Config.Bind("Tracer",
                "Frame History Filter",
                TasTracerFilter.Random | TasTracerFilter.Movement
            );

            configAutoHeal = Config.Bind("Debug", "Invincible", false);
            configAutoSkipCutscenes = Config.Bind("Debug", "Autoskip cutscenes", true);

            ConfigDebugInfo = Config.Bind("Debug",
                "Debug Info",
                DebugInfo.DebugFilter.RapidlyChanging | DebugInfo.DebugFilter.Monsters | DebugInfo.DebugFilter.Random);

            /*
            configOpenStudioOnLaunch = Config.Bind("Studio", "Launch on start", true);
            configOpenStudioShortcut = Config.Bind("Studio", "Launch", new KeyboardShortcut());

            if (configOpenStudioOnLaunch.Value) {
                LaunchStudio();
            }

            // KeybindManager.Add(this, LaunchStudio, () => configOpenStudioShortcut.Value);
            KeybindManager.Add(this, LaunchStudio, () => configOpenStudioShortcut.Value);
            */

            TasSettings = new CelesteTasSettings(Config);
            RCGLifeCycle.DontDestroyForever(gameObject);

            AttributeUtils.CollectAllMethods<UnloadAttribute>();
            AttributeUtils.CollectAllMethods<InitializeAttribute>();
            AttributeUtils.CollectAllMethods<BeforeTasFrame>();
            AttributeUtils.CollectAllMethods<BeforeActiveTasFrame>();

            AttributeUtils.Invoke<InitializeAttribute>();

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
        DebugModPlusInterop = DebugModPlusInteropGlue.Load();

        PlayerLoopHelper.AddAction(PlayerLoopTiming.EarlyUpdate, new PlayerLoopItem(this, EarlyUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, new PlayerLoopItem(this, FirstUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.LastUpdate, new PlayerLoopItem(this, LastUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.PreUpdate, new PlayerLoopItem(this, PreUpdate));
        PlayerLoopHelper.AddAction(PlayerLoopTiming.PostLateUpdate, new PlayerLoopItem(this, PostLateUpdate));
    }

    private class PlayerLoopItem(TasMod mb, Action action) : IPlayerLoopItem {
        public bool MoveNext() {
            if (!mb) return false;

            action();
            return true;
        }
    }

    private void EarlyUpdate() {
        if (Manager.CurrState is Manager.State.Running or Manager.State.FrameAdvance) {
            AttributeUtils.Invoke<BeforeActiveTasFrame>();
        }

        TasTracerState.TraceVarsThroughFrame("EarlyUpdate");
    }

    private void FixedUpdate() {
        TasTracerState.TraceVarsThroughFrame("FixedUpdate");
    }

    private static void PreUpdate() {
        if (Physics2D.simulationMode != SimulationMode2D.Script) return;

        if (Manager.CurrState != Manager.State.Paused) {
            Physics2D.Simulate(Time.deltaTime);
        }

        TasTracerState.TraceVarsThroughFrame("PreUpdate-aftersim");
    }

    private static void FirstUpdate() => TasTracerState.TraceVarsThroughFrame("FirstUpdate");

    private static void LastUpdate() {
        TasTracerState.TraceVarsThroughFrame("Update");

        if (Instance.configAutoHeal.Value && !Manager.Running) {
            Player.i?.health?.GainFull();
        }
    }

    private void LateUpdate() {
        TasTracerState.TraceVarsThroughFrame("LateUpdate");
        TasTracerState.LateUpdate();
    }

    private void PostLateUpdate() {
        TasTracerState.TraceVarsThroughFrame("PostLateUpdate");

        try {
            GameInfo.Update();

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

            // TODO: ensure consistent fixedupdate
        } catch (Exception e) {
            e.LogException("");
            Manager.DisableRun();
        }

        if (Instance.configAutoSkipCutscenes.Value) {
            if (GameCore.IsAvailable()) {
                if (GameCore.Instance.currentCutScene is SimpleCutsceneManager cutscene) {
                    cutscene.TrySkip();
                }
            }
        }
        
        TasTracerState.TraceVarsThroughFrame("PreUpdate-end");
        
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
internal class UnloadAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class InitializeAttribute(int priority = 0) : EventAttribute(priority);

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class BeforeTasFrame : Attribute;

[AttributeUsage(AttributeTargets.Method)]
[MeansImplicitUse]
internal class BeforeActiveTasFrame : Attribute;
