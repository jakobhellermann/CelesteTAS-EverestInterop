using System.Collections.Generic;
using HarmonyLib;
using StudioCommunication;
using System;
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

    /*[HarmonyPrefix]
    public static bool DontRunWhenPaused(MethodBase __originalMethod) =>
        Manager.CurrState != Manager.State.Paused && !Prevent;

    [HarmonyPrefix]
    public static bool DontRunInTAS(MethodBase __originalMethod) => !Manager.Running;*/

    private const int DefaultTasFramerate = 100;
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
        framerateState = FramerateState.Save();

        Time.timeScale = 1;

        SetTasFramerate(DefaultTasFramerate);
        QualitySettings.vSyncCount = 0;

        Time.fixedDeltaTime = 1f / 60f;
        // Physics.simulationMode = SimulationMode.FixedUpdate;

        // Physics2D.simulationMode = SimulationMode2D.Script;
    }

    [DisableRun]
    private static void DisableRun() {
        framerateState.Restore();
        Time.captureFramerate = 0;

        Time.fixedDeltaTime = 0.02f;
        Physics2D.simulationMode = SimulationMode2D.Update;
    }

    private static InputFrame? currentFeed;

    public static void FeedInputs(InputFrame inputFrame) {
        currentFeed = inputFrame;
    }


    private static Dictionary<Actions, KeyCode> actionKeyMap = new() {
        { Actions.Up, KeyCode.UpArrow },
        { Actions.Down, KeyCode.DownArrow },
        { Actions.Left, KeyCode.LeftArrow },
        { Actions.Right, KeyCode.RightArrow },

        { Actions.Jump, KeyCode.Y },
        { Actions.Dash, KeyCode.Z },
        
        { Actions.DashOnly, KeyCode.X },
    };

    [HarmonyPatch(typeof(UnityEngine.Input), nameof(UnityEngine.Input.GetKey), [typeof(KeyCode)])]
    [HarmonyPrefix]
    public static bool GetKey(KeyCode key, ref bool __result) {
        if (!Manager.Running || currentFeed is null) return true;

        foreach (var (action, actionKey) in actionKeyMap) {
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

        foreach (var (action, actionKey) in actionKeyMap) {
            if ((currentFeed.Actions & action) != 0 && actionKey == key) {
                // TODO: only true for a frame
                __result = true;
            }
        }

        return false;
    }
    
    // TODO: GetKeyUp
}
