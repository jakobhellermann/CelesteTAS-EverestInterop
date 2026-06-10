using HarmonyLib;
using StudioCommunication;
using System;
using System.Collections.Generic;
using TAS.Input;
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

    /*[HarmonyPrefix]
    public static bool DontRunWhenPaused(MethodBase __originalMethod) =>
        Manager.CurrState != Manager.State.Paused && !Prevent;

    [HarmonyPrefix]
    public static bool DontRunInTAS(MethodBase __originalMethod) => !Manager.Running;*/

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

        SetTasFramerate(DefaultTasFramerate);
        Time.fixedDeltaTime = 1.0f / DefaultFixedFramerate; // TODO: think about how to expose fixed timestamp
        QualitySettings.vSyncCount = 0;
        
        Time.timeScale = 1; 

        // Physics simulation in FixedUpdate can't easily be synced to TAS playback,
        // so it is simulated in TasMod.FirstUpdate
        Physics2D.simulationMode = SimulationMode2D.Script;
    }

    [DisableRun]
    private static void DisableRun() {
        savedFramerateTimeConfig.Restore();

        Time.fixedDeltaTime = 0.02f;
        Physics2D.simulationMode = SimulationMode2D.Update;
    }

    private static InputFrame? currentFeed;

    public static void FeedInputs(InputFrame inputFrame) {
        currentFeed = inputFrame;
    }

    private record FramerateTimeConfig(
        int TargetFramerate,
        int VsyncCount,
        float TimeScale,
        float FixedDeltaTime,
        float CaptureDeltaTime)
    {
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

    private static Dictionary<Actions, KeyCode> actionKeyMap = new() {
        { Actions.Up, KeyCode.W },
        { Actions.Down, KeyCode.S },
        { Actions.Left, KeyCode.A },
        { Actions.Right, KeyCode.D },

        { Actions.Jump, KeyCode.Space },
        { Actions.Dash, KeyCode.LeftShift },
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

