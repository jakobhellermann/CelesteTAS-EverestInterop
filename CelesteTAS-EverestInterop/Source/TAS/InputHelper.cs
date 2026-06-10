using HarmonyLib;
using System;
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

    public static void FeedInputs(InputFrame inputFrame) {
        // TODO(input)
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
}

