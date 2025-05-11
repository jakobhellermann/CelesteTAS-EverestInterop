using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using PlayerLoopHelper;
using TAS.Communication;
using TAS.Module;
using TAS.Tracer;
using TAS.Utils;

namespace TAS;

[BepInPlugin("TasTools", "TasTools", "1.0.0")]
public class TasMod : BaseUnityPlugin {
    public static TasMod Instance = null!;
    public CelesteTasSettings TasSettings = null!;

    private Harmony harmony = null!;

    internal ConfigEntry<TasTracerFilter> ConfigTasTraceFilter = null!;
    internal ConfigEntry<bool> ConfigTasTraceFrameHistory = null!;

    // private ConfigEntry<bool> configOpenStudioOnLaunch = null!;
    // private ConfigEntry<KeyboardShortcut> configOpenStudioShortcut = null!;

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
            ConfigTasTraceFilter = Config.Bind("Tracer",
                "Frame History Filter",
                TasTracerFilter.Random | TasTracerFilter.Movement
            );

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

            AttributeUtils.CollectAllMethods<UnloadAttribute>();
            AttributeUtils.CollectAllMethods<InitializeAttribute>();
            AttributeUtils.CollectAllMethods<BeforeTasFrame>();
            AttributeUtils.CollectAllMethods<BeforeActiveTasFrame>();

            AttributeUtils.Invoke<InitializeAttribute>();

            harmony = Harmony.CreateAndPatchAll(typeof(TasMod).Assembly);

            if (TasSettings.AttemptConnectStudio) CommunicationWrapper.Start();
        } catch (Exception e) {
            Log.Error($"Failed to load TasTools: {e}");
        }

        // https://giannisakritidis.com/blog/Early-And-Super-Late-Update-In-Unity/

        Logger.LogInfo($"Plugin TasTools is loaded!");
    }

    private struct EarlyUpdateSystem;

    private struct PostLateUpdateSystem;

    private struct FirstUpdateSystem;

    private struct LastUpdateSystem;

    private static Type? alsoTraceAround = typeof(UnityEngine.PlayerLoop.PreUpdate.Physics2DUpdate);

    private void Start() {
        PlayerLoopSystemHelper.Register(typeof(EarlyUpdateSystem),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.EarlyUpdate),
            EarlyUpdate);
        PlayerLoopSystemHelper.Register(typeof(FirstUpdateSystem),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.Update),
            FirstUpdate);
        PlayerLoopSystemHelper.Register(typeof(LastUpdateSystem),
            InsertPosition.LastChildOf,
            typeof(UnityEngine.PlayerLoop.Update),
            LastUpdate);
        PlayerLoopSystemHelper.Register(typeof(PostLateUpdateSystem),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.PostLateUpdate),
            PostLateUpdate);

        if (alsoTraceAround is { } system) {
            PlayerLoopSystemHelper.Register(typeof(TasMod),
                InsertPosition.Before,
                system,
                TraceBefore);
            PlayerLoopSystemHelper.Register(typeof(TasMod),
                InsertPosition.After,
                system,
                TraceAfter);
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

    private static void TraceBefore() => TasTracerState.TraceVarsThroughFrame($"TraceBefore-{alsoTraceAround}");
    private static void TraceAfter() => TasTracerState.TraceVarsThroughFrame($"TraceAfter-{alsoTraceAround}");

    private static void FirstUpdate() => TasTracerState.TraceVarsThroughFrame("FirstUpdate");
    private static void LastUpdate() => TasTracerState.TraceVarsThroughFrame("LastUpdate");

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
    }

    private void OnDestroy() {
        PlayerLoopSystemHelper.Unregister(typeof(EarlyUpdateSystem));
        PlayerLoopSystemHelper.Unregister(typeof(FirstUpdateSystem));
        PlayerLoopSystemHelper.Unregister(typeof(LastUpdateSystem));
        PlayerLoopSystemHelper.Unregister(typeof(PostLateUpdateSystem));

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
