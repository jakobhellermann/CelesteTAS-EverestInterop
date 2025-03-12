using System.Collections.Generic;
using HarmonyLib;
using InControl;
using RCGMaker.Core;
using StudioCommunication;
using System;
using System.Reflection;
using TAS.Input;
using TAS.Utils;
using UnityEngine;

namespace TAS;

[HarmonyPatch]
public static class InputHelper {
    public static bool Prevent = false;

    public static void WithPrevent(Action a) {
        Prevent = true;
        a();
        Prevent = false;
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.OnRebindAnimatorMove))]
    [HarmonyPatch(typeof(Player), nameof(Player.OnRebindAnimatorMove))]
    [HarmonyPatch(typeof(Actor), nameof(Actor.Move))]
    [HarmonyPatch(typeof(Player), "Update")]
    [HarmonyPatch(typeof(Health), nameof(Health.InvincibleForDuration))]
    [HarmonyPatch(typeof(SelectableNavigationRemapping), "RemapAfterAFrame")]
    [HarmonyPatch(typeof(PushAwayWall), "Update")]
    [HarmonyPatch(typeof(BackgroundTaskExecutor), "Update")]
    [HarmonyPatch(typeof(BackgroundTaskExecutor), "Update")]
    [HarmonyPatch(typeof(AbstractEmitter), "Update")]
    [HarmonyPatch(typeof(TimePauseManager), "Update")] // TODO: patch Time.timeScale
    [HarmonyPatch(typeof(ConditionTimer), "Update")]
    [HarmonyPatch(typeof(InputManager), "UpdateInternal")]
    [HarmonyPatch(typeof(PlayerInputCommandQueue), "Update")]
    // [HarmonyPatch(typeof(UpdateLoopManager), "Update")]
    [HarmonyPrefix]
    public static bool DontRunWhenPaused(MethodBase __originalMethod) =>
        Manager.CurrState != Manager.State.Paused && !Prevent;

    [HarmonyPatch(typeof(LoadingLoopIcon), nameof(LoadingLoopIcon.ShowLoadingLoopIcon))]
    [HarmonyPatch(typeof(LoadingLoopIcon), nameof(LoadingLoopIcon.HideLoadingLoopIcon))]
    [HarmonyPrefix]
    public static bool DontRunInTAS(MethodBase __originalMethod) => !Manager.Running;

    public static class PatchesNonSpeedrunpatch {
        [HarmonyPatch(typeof(UpdateLoopManager), "Update")]
        [HarmonyPatch(typeof(UpdateLoopManager), "LateUpdate")]
        [HarmonyPrefix]
        public static bool DontRunWhenPaused(MethodBase __originalMethod) =>
            Manager.CurrState != Manager.State.Paused && !Prevent;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CameraManager), nameof(CameraManager.CameraBackToFollowPlayer))]
    public static bool CameraInstant(ref float duration, CameraManager __instance) {
        var run = Manager.CurrState != Manager.State.Paused && !Prevent;
        if (run) return true;

        __instance.cameraCore.dockObj.transform.localPosition = Vector3.zero;
        duration = 0;
        return false;
    }

    private const int DefaultTasFramerate = 60;
    public static int CurrentTasFramerate = DefaultTasFramerate;
    
    private const int DefaultFixedFramerate = 60;

    /// Apply the `CurrentTasFramerate` (and Playback speed to unity)
    public static void WriteFramerate() {
        Application.targetFrameRate = (int)(CurrentTasFramerate * Manager.PlaybackSpeed);
    }

    /// Set the current tas framerate
    public static void SetTasFramerate(int framerate) {
        // If we have 1:1 tas playback, keep that
        // if (Application.targetFrameRate == Time.captureFramerate) { }
        CurrentTasFramerate = framerate;
        WriteFramerate();
        Time.captureFramerate = framerate;
    }


    /// Framerate/Time config before the TAS was started
    private static FramerateTimeConfig savedFramerateTimeConfig = FramerateTimeConfig.Save();

    [EnableRun]
    private static void EnableRun() {
        savedFramerateTimeConfig = FramerateTimeConfig.Save();

        Time.timeScale = 1;
        InputManager.SuspendInBackground = false;
        InputManager.Enabled = true;
        RCGTime.GlobalSimulationSpeed = 1;
        ClearInputState();
        // typeof(InputManager).SetFieldValue("initialTime", Time.realtimeSinceStartup);
        // typeof(InputManager).SetFieldValue("currentTick", 0U);
        // typeof(InputManager).SetFieldValue("currentTime", 0f);

        SetTasFramerate(DefaultTasFramerate);
        Time.fixedDeltaTime = 1.0f / DefaultFixedFramerate; // TODO: think about how to expose fixed timestamp
        QualitySettings.vSyncCount = 0;
        
        Time.timeScale = 1; 

        // Physics simulation in FixedUpdate can't easily be synced to TAS playback,
        // so it is simulated in TasMod.FirstUpdate
        Physics2D.simulationMode = SimulationMode2D.Script;
    }

    public static void ClearInputState() {
        typeof(InputManager).InvokeMethod("SetZeroTickOnAllControls");
        InputManager.ClearInputState();
        foreach (var actionSet in typeof(InputManager).GetFieldValue<List<PlayerActionSet>>("playerActionSets")!) {
            foreach (var action in actionSet.Actions) {
                action.SetFieldValue("clearInputState", false);
            }
        }
    }

    [DisableRun]
    private static void DisableRun() {
        savedFramerateTimeConfig.Restore();
        InputManager.SuspendInBackground = true;
        ClearInputState();

        Time.fixedDeltaTime = 0.02f;
        Physics2D.simulationMode = SimulationMode2D.Update;
    }

    private static InputFrame? currentFeed;

    public static void FeedInputs(InputFrame inputFrame) {
        currentFeed = inputFrame;
#if UNITY_NEW_INPUT_SYSTEM
        NewInputSystemInjector.FeedFrame(inputFrame);
#endif
    }

    private static Dictionary<Actions, Key> actionKeyMap = new() {
        { Actions.Up, Key.W },
        { Actions.Down, Key.S },
        { Actions.Left, Key.A },
        { Actions.Right, Key.D },

        { Actions.Jump, Key.Space },
        { Actions.Dash, Key.LeftShift },
    };

    [HarmonyPatch(typeof(UnityKeyboardProvider), nameof(UnityKeyboardProvider.GetKeyIsPressed))]
    [HarmonyPrefix]
    public static bool GetKeyIsPressed(Key control, ref bool __result) {
        if (!Manager.Running || currentFeed is null) return true;

        foreach (var (action, actionKey) in actionKeyMap) {
            if ((currentFeed.Actions & action) != 0 && actionKey == control) {
                __result = true;
            }
        }

        return false;
    }
    
    private record FramerateTimeConfig(
        int TargetFramerate,
        int VsyncCount,
        float TimeScale,
        float FixedDeltaTime,
        float CaptureDeltaTime) {
        public static FramerateTimeConfig Save() => new(
            Application.targetFrameRate, QualitySettings.vSyncCount, Time.timeScale, Time.fixedDeltaTime, Time.captureDeltaTime
        );

        public void Restore() {
            Application.targetFrameRate = TargetFramerate;
            QualitySettings.vSyncCount = VsyncCount;
            Time.timeScale = TimeScale;
            Time.fixedDeltaTime = FixedDeltaTime;
            Time.captureDeltaTime = CaptureDeltaTime;
        }
    }

    public static Dictionary<Actions, KeyCode> OldInputSystemActionKeyMap = [];

    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKey), [typeof(KeyCode)])]
    [HarmonyPrefix]
    public static bool GetKey(KeyCode key, ref bool __result) {
        if (!Manager.Running || currentFeed is null) return true;

        foreach (var (action, actionKey) in OldInputSystemActionKeyMap) {
            if ((currentFeed.Actions & action) != 0 && actionKey == key) {
                __result = true;
            }
        }

        return false;
    }
    
    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKeyDown), [typeof(KeyCode)])]
    [HarmonyPrefix]
    public static bool GetKeyDown(KeyCode key, ref bool __result) {
        if (!Manager.Running || currentFeed is null) return true;

        foreach (var (action, actionKey) in OldInputSystemActionKeyMap) {
            if ((currentFeed.Actions & action) != 0 && actionKey == key) {
                // TODO: only true for a frame
                __result = true;
            }
        }

        return false;
    }
    
    // TODO: GetKeyUp
}

