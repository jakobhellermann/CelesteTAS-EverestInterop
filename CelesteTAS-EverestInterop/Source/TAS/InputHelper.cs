using System.Collections.Generic;
using HarmonyLib;
using InControl;
using NineSolsAPI;
using NineSolsAPI.Utils;
using RCGMaker.Core;
using StudioCommunication;
using System;
using System.Reflection;
using TAS.Input;
using UnityEngine;

// ReSharper disable InconsistentNaming

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

    // HACK
    [HarmonyPatch(typeof(PushAwayWall), "Update")]
    [HarmonyPrefix]
    public static bool DontRunAtStart(PushAwayWall __instance) {
        if (!Manager.Running) return true;

        var wouldHave = __instance.playerSensor.IsPlayerInside;
        if (wouldHave) {
            ToastManager.Toast($"would have pushawaywall'd {Manager.Controller.CurrentFrameInTas}");
        }

        return Manager.Controller.CurrentFrameInTas > 12;
    }

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

    private record FramerateState(int TargetFramerate, int VsyncCount) {
        public static FramerateState Save() => new(Application.targetFrameRate, QualitySettings.vSyncCount);

        public void Restore() {
            Application.targetFrameRate = TargetFramerate;
            QualitySettings.vSyncCount = VsyncCount;
        }
    }

    public static void WriteFramerate() {
        Application.targetFrameRate = (int)(CurrentTasFramerate * Manager.PlaybackSpeed);
    }


    public static void SetTasFramerate(int framerate) {
        // If we have 1:1 tas playback, keep that
        // if (Application.targetFrameRate == Time.captureFramerate) {
        // }
        CurrentTasFramerate = framerate;
        WriteFramerate();
        Time.captureFramerate = framerate;
    }


    private static FramerateState framerateState = FramerateState.Save();

    [EnableRun]
    private static void EnableRun() {
        InputManager.SuspendInBackground = false;
        InputManager.Enabled = true;
        ClearInputState();
        // typeof(InputManager).SetFieldValue("initialTime", Time.realtimeSinceStartup);
        // typeof(InputManager).SetFieldValue("currentTick", 0U);
        // typeof(InputManager).SetFieldValue("currentTime", 0f);

        framerateState = FramerateState.Save();

        Time.timeScale = 1;
        RCGTime.GlobalSimulationSpeed = 1;

        SetTasFramerate(DefaultTasFramerate);
        QualitySettings.vSyncCount = 0;

        Time.fixedDeltaTime = 1f / 60f;
        // Physics.simulationMode = SimulationMode.FixedUpdate;

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
        InputManager.SuspendInBackground = true;
        ClearInputState();

        framerateState.Restore();
        Time.captureFramerate = 0;

        Time.fixedDeltaTime = 0.02f;
        Physics2D.simulationMode = SimulationMode2D.Update;

        DialoguePlayer.Instance.TrySkip();
        if (GameCore.Instance.currentCutScene is SimpleCutsceneManager cutScene) {
            cutScene.TrySkip();
        }

        Player.i.playerInput.fsm.ChangeState(PlayerInputStateType.Action);
    }

    private static InputFrame? currentFeed;

    public static void FeedInputs(InputFrame inputFrame) {
        currentFeed = inputFrame;
    }

    private static Dictionary<Actions, Key> actionKeyMap = new() {
        { Actions.Up, Key.W },
        { Actions.Down, Key.S },
        { Actions.Left, Key.A },
        { Actions.Right, Key.D },

        { Actions.Jump, Key.Space },
        { Actions.Dash, Key.LeftShift },
        
        { Actions.Interact, Key.E },
        { Actions.Attack, Key.J },
        { Actions.Shoot, Key.C },
        { Actions.Parry, Key.K },

        { Actions.Talisman, Key.F },
        { Actions.Nymph, Key.Q },
        { Actions.Heal, Key.R },
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
}
