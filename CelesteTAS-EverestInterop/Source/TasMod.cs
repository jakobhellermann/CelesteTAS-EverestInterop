using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using PlayerLoopHelper;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;
using UnityEngine;

namespace TAS;

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

            // KeybindManager.Add(this, LaunchStudio, () => configOpenStudioShortcut.Value);

            TasSettings = new CelesteTasSettings();

            AttributeUtils.CollectAllMethods<LoadAttribute>();
            AttributeUtils.CollectAllMethods<UnloadAttribute>();
            AttributeUtils.CollectAllMethods<InitializeAttribute>();
            AttributeUtils.CollectAllMethods<BeforeTasFrame>();
            AttributeUtils.CollectAllMethods<AfterTasFrame>();

            AttributeUtils.Invoke<InitializeAttribute>();
            AttributeUtils.Invoke<LoadAttribute>();

            harmony = Harmony.CreateAndPatchAll(typeof(TasMod).Assembly);

            if (TasSettings.AttemptConnectStudio) CommunicationWrapper.Start();
        } catch (Exception e) {
            Log.Error($"Failed to load TasTools: {e}");
        }

        // https://giannisakritidis.com/blog/Early-And-Super-Late-Update-In-Unity/

        Logger.LogInfo($"Plugin TasTools is loaded!");
    }

    private void Start() {
        PlayerLoopSystemHelper.Register(typeof(TasMod),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.EarlyUpdate),
            EarlyUpdate);
        PlayerLoopSystemHelper.Register(typeof(TasMod),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.PostLateUpdate),
            PostLateUpdate);
        PlayerLoopSystemHelper.Register(typeof(TasMod),
            InsertPosition.FirstChildOf,
            typeof(UnityEngine.PlayerLoop.Update),
            PreUpdate);
        PlayerLoopSystemHelper.Register(typeof(TasMod),
            InsertPosition.LastChildOf,
            typeof(UnityEngine.PlayerLoop.Update),
            AfterUpdate);
    }

    private void EarlyUpdate() {
        Log.TasTrace("-- FRAME BEGIN --");
    }

    private void FixedUpdate() {
        Log.TasTrace($"-- FixedUpdate dt={Time.fixedDeltaTime}--");
    }

    private static void PreUpdate() {
        if (Physics2D.simulationMode != SimulationMode2D.Script) return;

        if (Manager.CurrState != Manager.State.Paused) {
            Physics2D.Simulate(Time.deltaTime);
        }
    }

    private static void AfterUpdate() {
        Log.TasTrace($"-- Update dt={Time.deltaTime}-- ");

        // CameraManager.Instance.cameraCore.dockObj.localPosition = Vector3.zero;

        if (Manager.CurrState == Manager.State.Paused) {
            Manager.UpdateMeta();
            if (Manager.CurrState == Manager.State.Paused && Manager.NextState != Manager.State.Paused) {
                Manager.DisablePause();
            }
        }
    }

    private void PostLateUpdate() {
        Log.TasTrace("-- FRAME END --");

        try {
            GameInfo.Update();

            AttributeUtils.Invoke<AfterTasFrame>();

            AttributeUtils.Invoke<BeforeTasFrame>();

            Manager.UpdateMeta();
            Manager.Update();

            Log.TasTrace($"State: {Manager.CurrState} -> {Manager.NextState}");

            // TODO: ensure consistent fixedupdate
        } catch (Exception e) {
            e.LogException("");
            Manager.DisableRun();
        }
    }

    private void OnDestroy() {
        PlayerLoopSystemHelper.Unregister(typeof(TasMod));
        
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
