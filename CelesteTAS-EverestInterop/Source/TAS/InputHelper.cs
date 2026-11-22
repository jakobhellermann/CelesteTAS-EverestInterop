using HarmonyLib;
using Newtonsoft.Json.Linq;
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

    private const int DefaultTasFramerate = 100;
    public static int CurrentTasFramerate = DefaultTasFramerate;
    
    /// The game's fixed timestep at load, before the game rescales it (PlayMaker ScaleTime writes 0.02 * timeScale)
    private static float gameFixedDeltaTime;

    [Initialize]
    private static void CaptureGameFixedDeltaTime() => gameFixedDeltaTime = Time.fixedDeltaTime;

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
    private static FramerateTimeConfig? savedFramerateTimeConfig;

    [EnableRun]
    private static void EnableRun() {
        // EnableRun is re-invoked mid-run by frame step back, when the TAS config is already applied
        savedFramerateTimeConfig ??= FramerateTimeConfig.Save();

        SetTasFramerate(DefaultTasFramerate);
        Time.fixedDeltaTime = gameFixedDeltaTime;
        QualitySettings.vSyncCount = 0;

        Time.timeScale = 1;

        RequestFixedStepAlignment();
    }

    // Vsync caps playback at the refresh rate; mods turn it back on (FastGameLoad after every scene load)
    [BeforeActiveTasFrame]
    private static void KeepVsyncOff() {
        if (QualitySettings.vSyncCount == 0) return;

        $"Turning vsync off again, something set vSyncCount to {QualitySettings.vSyncCount} during the TAS".Log();
        QualitySettings.vSyncCount = 0;
    }

    [DisableRun]
    private static void DisableRun() {
        if (savedFramerateTimeConfig is null) {
            throw new InvalidOperationException("DisableRun without a saved framerate config");
        }
        savedFramerateTimeConfig.Restore();
        savedFramerateTimeConfig = null;

        alignFixedStep = false;
        restoreCaptureFramerate = false;
        alignmentTargetTicks = null;
    }

    private static bool alignFixedStep;
    private static bool restoreCaptureFramerate;
    private static float? timeScaleBeforeAlignment;

    /// Spend the next frame on <see cref="AlignFixedStep"/>
    public static void RequestFixedStepAlignment() => alignFixedStep = true;

    public static bool FixedStepAlignmentPending => alignFixedStep || restoreCaptureFramerate;

    /// Engine tft for the next alignment, instead of the run start's
    private static long? alignmentTargetTicks;
    private static long alignedTftTicks;

    public static JToken CaptureFixedStep() => new JObject {
        ["TftTicks"] = DeterministicTimePatch.TftTicks,
        ["FixedDeltaTime"] = Time.fixedDeltaTime,
    };

    public static void RestoreFixedStep(JToken data) {
        Time.fixedDeltaTime = (float) (data["FixedDeltaTime"] ?? throw new InvalidOperationException("savestate CelesteTAS data has no FixedDeltaTime"));
        alignmentTargetTicks = (long) (data["TftTicks"] ?? throw new InvalidOperationException("savestate CelesteTAS data has no TftTicks"));
    }

    /// The engine's tft is left over from what ran before, so one frame gets the length that leaves the next step
    /// boundary an irrational fraction of a TAS frame ahead (or at a savestate's tft): the frame after steps physics.
    /// Unity counts time in ticks, so the tft is reached exactly.
    public static void AlignFixedStep() {
        if (restoreCaptureFramerate) {
            restoreCaptureFramerate = false;
            if (DeterministicTimePatch.TftTicks is var reached && reached != alignedTftTicks) {
                throw new InvalidOperationException($"FixedUpdate alignment reached tft {reached} ticks instead of {alignedTftTicks}");
            }
            Time.captureFramerate = CurrentTasFramerate;
            // A load that started meanwhile has already replaced the override
            if (DeterministicTimePatch.OverwriteTimeScale == 1) {
                DeterministicTimePatch.OverwriteTimeScale = timeScaleBeforeAlignment;
            }
        }
        if (!alignFixedStep) return;
        alignFixedStep = false;

        double step = Time.fixedDeltaTime;
        var stepTicks = (long) Math.Floor(step * DeterministicTimePatch.TicksPerSecond);
        alignedTftTicks = alignmentTargetTicks ?? DeterministicTimePatch.ToTicks(step - (Math.Sqrt(2) - 1) / CurrentTasFramerate % step);
        alignmentTargetTicks = null;
        var frameTicks = stepTicks - DeterministicTimePatch.TftTicks + alignedTftTicks;
        // Unity floors captureDeltaTime to whole ticks, so aim between two
        Time.captureDeltaTime = (float) ((frameTicks + 0.5) / DeterministicTimePatch.TicksPerSecond);
        // e.g. a load during the death freeze
        timeScaleBeforeAlignment = DeterministicTimePatch.OverwriteTimeScale;
        DeterministicTimePatch.OverwriteTimeScale = 1;
        restoreCaptureFramerate = true;
    }

    private static InputFrame? currentFeed;

    public static void FeedInputs(InputFrame inputFrame) {
        currentFeed = inputFrame;
#if UNITY_NEW_INPUT_SYSTEM
        NewInputSystemInjector.FeedFrame(inputFrame);
#endif
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

