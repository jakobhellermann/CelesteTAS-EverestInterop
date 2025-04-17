using System;
using System.Collections.Concurrent;
using System.Linq;
using JetBrains.Annotations;
using StudioCommunication;
using System.Collections.Generic;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Input.Commands;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Tools;
using TAS.UnityInterop;
using TAS.Utils;
using UnityEngine;

namespace TAS;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class EnableRunAttribute(int priority = 0) : EventAttribute(priority);

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class DisableRunAttribute(int priority = 0) : EventAttribute(priority);

/// Causes the method to be called every real-time frame, even if a TAS is currently running / paused
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class UpdateMetaAttribute(int priority = 0) : EventAttribute(priority);

/// Main controller, which manages how the TAS is played back
public static class Manager {
    public enum State {
        /// No TAS is currently active
        Disabled,
        /// Plays the current TAS back at the specified PlaybackSpeed
        Running,
        /// Pauses the current TAS
        Paused,
        /// Advances the current TAS by 1 frame and resets back to Paused
        FrameAdvance,
        /// Forwards the TAS while paused
        SlowForward,
    }

    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectAllMethods<EnableRunAttribute>();
        AttributeUtils.CollectAllMethods<DisableRunAttribute>();
        AttributeUtils.CollectAllMethods<UpdateMetaAttribute>();
    }

    public static bool Running => CurrState != State.Disabled;
    public static bool FastForwarding => Running && PlaybackSpeed >= 5.0f;

    private static float playbackSpeed = 1.0f;
    public static float PlaybackSpeed {
        get => playbackSpeed;
        private set {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (value == playbackSpeed) return;

            playbackSpeed = value;
            InputHelper.WriteFramerate();
        }
    }

    public static State CurrState, NextState;
    public static readonly InputController Controller = new();

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    private static PopupToast.Entry? frameStepEofToast = null;
    private static PopupToast.Entry? autoPauseDraft = null;

    // Allow accumulation of frames to step back, since the operation is time intensive
    internal static int FrameStepBackTargetFrame = -1;
    private const float FrameStepBackTime = 1.0f;
    private static float frameStepBackAmount = 0.0f;
    private static float frameStepBackTimeout = 0.0f;
    private static PopupToast.Entry? frameStepBackToast = null;

    public static void EnableRun() {
        if (Running) {
            return;
        }

        CurrState = NextState = State.Running;
        PlaybackSpeed = 1.0f;

        FrameStepBackTargetFrame = -1;

        Controller.Stop();
        Controller.RefreshInputs(forceRefresh: true);

        if (Controller.Inputs.Count == 0) {
            // Empty / Invalid file
            CurrState = NextState = State.Disabled;
            SyncChecker.ReportRunFinished();
            return;
        }

        AttributeUtils.Invoke<EnableRunAttribute>();

        $"Starting TAS: {Controller.FilePath}".Log();
    }

    public static void DisableRun() {
        if (!Running) {
            return;
        }

        "Stopping TAS".Log();

        AttributeUtils.Invoke<DisableRunAttribute>();

        SyncChecker.ReportRunFinished();

        if (CurrState == State.Paused) {
            DisablePause();
        }
        
        CurrState = NextState = State.Disabled;
        Controller.Stop();
    }

    /// Will start the TAS on the next update cycle
    public static void EnableRunLater() => NextState = State.Running;
    /// Will stop the TAS on the next update cycle
    public static void DisableRunLater() => NextState = State.Disabled;

    public static void EnablePause() {
        TimeHelper.OverwriteTimeScale = 0;

        try {
            // TODO(unity): pause animators
        } catch (Exception e) {
            Log.Error($"Error trying to snapshot animator: {e}");
        }
    }

    private static List<(Animator, AnimatorSnapshot)> prePauseAnimatorStates = [];
    
    public static void DisablePause() {
        foreach (var (anim, _) in prePauseAnimatorStates) {
            // snapshot.Restore(anim);
            anim.enabled = true;
        }
        prePauseAnimatorStates.Clear();

        TimeHelper.OverwriteTimeScale = null;
    }

    /// Updates the TAS itself
    public static void Update() {
        if (CurrState != State.Paused && NextState == State.Paused) {
            EnablePause();
        }
        if (CurrState == State.Paused && NextState != State.Paused) {
            DisablePause();
        }
        
        
        if (!Running && NextState == State.Running) {
            EnableRun();
        }
        if (Running && NextState == State.Disabled) {
            DisableRun();
        }

        SavestateManager.Update();

        CurrState = NextState;

        while (mainThreadActions.TryDequeue(out var action)) {
            action.Invoke();
        }

        if (!Running || CurrState == State.Paused || GameInterop.IsLoading()) {
            return;
        }


        if (Controller.HasFastForward || FrameStepBackTargetFrame > 0) {
            NextState = State.Running;
        }

        Controller.AdvanceFrame(out bool couldPlayback);

        if (!couldPlayback) {
            DisableRun();
            return;
        }

        // Catch frame step-back
        if (FrameStepBackTargetFrame > 0 && Controller.CurrentFrameInTas >= FrameStepBackTargetFrame) {
            FrameStepBackTargetFrame = -1;
            NextState = State.Paused;
        }
        // Auto-pause at end of drafts
        else if (!Controller.CanPlayback && TasSettings.AutoPauseDraft && IsDraft()) {
            NextState = State.Paused;

            if (CurrState == State.Running && !FastForwarding) {
                const string text = "Auto-pause draft on end:\nInsert any Time command or disable the setting to prevent the pausing";
                const float duration = 2.0f;
                if (autoPauseDraft is not { Active: true }) {
                    autoPauseDraft = PopupToast.Show(text, duration);
                } else {
                    autoPauseDraft.Text = text;
                    autoPauseDraft.Timeout = duration;
                }
            }
        }
        // Pause the TAS if breakpoint is hit
        // Special-case for end of regular files, to update *Time-commands
        else if (FrameStepBackTargetFrame == -1 && Controller.Break && (Controller.CanPlayback || IsDraft())) {
            Controller.NextLabelFastForward = null;
            NextState = State.Paused;
        }

        // Prevent executing unsafe actions unless explicitly allowed
        if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
            // Only allow specific scenes
            if (GameInterop.IsUnsafeInput()) {
                SyncChecker.ReportUnsafeAction();
                DisableRun();
            }
        }
    }

    /// Updates everything around the TAS itself, like hotkeys, studio-communication, etc.
    public static void UpdateMeta() {
        if (!Hotkeys.Initialized) {
            return; // Still loading
        }

        Hotkeys.UpdateMeta();
        SavestateManager.UpdateMeta();
        AttributeUtils.Invoke<UpdateMetaAttribute>();

        SendStudioState();

        // Pending EnableRun/DisableRun. Prevent overwriting
        if (Running && NextState == State.Disabled || !Running && NextState != State.Disabled) {
            return;
        }

        // Check if the TAS should be enabled / disabled
        if (Hotkeys.StartStop.Pressed) {
            if (Running) {
                DisableRun();
            } else {
                EnableRun();
            }
            return;
        }

        if (Hotkeys.Restart.Pressed) {
            DisableRun();
            EnableRun();
            return;
        }

        if (TASRecorderInterop.IsRecording) {
            // Force recording at 1x playback
            NextState = State.Running;
            PlaybackSpeed = 1.0f;
            return;
        }

        if (frameStepBackAmount > 0.0f) {
            int frames = (int) Math.Round(frameStepBackAmount / Core.PlaybackDeltaTime);
            frameStepBackTimeout -= Core.PlaybackDeltaTime;

            // Advance a frame extra, since otherwise 0s would only be rendered AFTER the lag from the TAS restart
            string text = $"Frame Step Back: -{frames}f   (in {Math.Max(0.0f, frameStepBackTimeout - Core.PlaybackDeltaTime):F2}s)";
            if (frameStepBackToast is not { Active: true }) {
                frameStepBackToast = PopupToast.Show(text);
            } else {
                frameStepBackToast.Text = text;
            }
            frameStepBackToast.Timeout = frameStepBackTimeout;

            if (frameStepBackTimeout <= 0.0f) {
                FrameStepBackTargetFrame = Math.Max(1, Controller.CurrentFrameInTas - frames);

                Controller.Stop();
                CurrState = NextState = State.Running;
                AttributeUtils.Invoke<EnableRunAttribute>();

                frameStepBackTimeout = 0.0f;
                frameStepBackAmount = 0.0f;
            }
        }

        if (Running && Hotkeys.FastForwardComment.Pressed) {
            Controller.FastForwardToNextLabel();
            return;
        }

        switch (CurrState) {
            case State.Running:
                if (Hotkeys.PauseResume.Pressed || Hotkeys.FrameAdvance.Pressed) {
                    NextState = State.Paused;
                } else if (Hotkeys.FrameStepBack.Pressed) {
                    NextState = State.Paused;
                    frameStepBackTimeout = FrameStepBackTime;
                    frameStepBackAmount = Core.PlaybackDeltaTime;
                }
                break;

            case State.FrameAdvance:
                NextState = State.Paused;
                break;

            case State.Paused:
                if (frameStepBackAmount > 0.0f) {
                    if (Hotkeys.FrameStepBack.Repeated) {
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += Core.PlaybackDeltaTime;
                    } else if (Hotkeys.FastForward.Check) {
                        // Fast-forward during pause plays at 0.5x speed (due to alternating advancing / pausing)
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += Core.PlaybackDeltaTime * 2.0f;
                    } else if (Hotkeys.SlowForward.Check) {
                        frameStepBackTimeout = FrameStepBackTime;
                        frameStepBackAmount += TasSettings.SlowForwardSpeed;
                    }
                } else if (Hotkeys.FrameStepBack.Repeated) {
                    frameStepBackTimeout = FrameStepBackTime;
                    frameStepBackAmount = Core.PlaybackDeltaTime;
                } else if (Hotkeys.PauseResume.Pressed) {
                    NextState = State.Running;
                } else if (Hotkeys.FrameAdvance.Repeated || Hotkeys.FastForward.Check) {
                    // Prevent frame-advancing into the end of the TAS
                    if (!Controller.CanPlayback) {
                        Controller.RefreshInputs(); // Ensure there aren't any new inputs
                    }
                    if (Controller.CanPlayback) {
                        NextState = State.FrameAdvance;
                    } else {
                        const string text = "Cannot advance further: Reached end-of-file";
                        const float duration = 1.0f;
                        if (frameStepEofToast is not { Active: true }) {
                            frameStepEofToast = PopupToast.Show(text, duration);
                        } else {
                            frameStepEofToast.Text = text;
                            frameStepEofToast.Timeout = duration;
                        }
                    }
                }
                break;

            case State.Disabled:
            default:
                break;
        }

        // Allow altering the playback speed with the right thumb-stick
        /*float normalSpeed = Hotkeys.RightThumbSticksX switch {
            >=  0.001f => Hotkeys.RightThumbSticksX * TasSettings.FastForwardSpeed,
            <= -0.001f => (1 + Hotkeys.RightThumbSticksX) * TasSettings.SlowForwardSpeed,
            _          => 1.0f,
        };*/
        float normalSpeed = 1.0f;

        // Apply fast / slow forwarding
        switch (NextState) {
            case State.Running when FrameStepBackTargetFrame != -1:
                PlaybackSpeed = FastForward.DefaultSpeed;
                break;
            case State.Running when Hotkeys.FastForward.Check:
                PlaybackSpeed = TasSettings.FastForwardSpeed;
                break;
            case State.Running when Hotkeys.SlowForward.Check:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                break;

            case State.Paused or State.SlowForward when Hotkeys.SlowForward.Check && frameStepBackAmount <= 0.0f:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                NextState = State.SlowForward;
                break;
            case State.Paused or State.SlowForward:
                PlaybackSpeed = normalSpeed;
                NextState = State.Paused;
                break;

            case State.FrameAdvance:
                PlaybackSpeed = normalSpeed;
                break;

            default:
                PlaybackSpeed = Controller.HasFastForward ? Controller.CurrentFastForward!.Speed : normalSpeed;
                break;
        }
    }

    /// Queues an action to be performed on the main thread
    public static void AddMainThreadAction(Action action) {
        mainThreadActions.Enqueue(action);
    }


    /// Determine if current TAS file is a draft
    private static bool IsDraft() {
        if (TASRecorderInterop.IsRecording) {
            return false;
        }

        // Require any *Time, alternatively Midway*Time at the end for the TAS to be counted as finished
        return Controller.Commands.Values
            .SelectMany(commands => commands)
            .All(command => !command.Is("FileTime") && !command.Is("ChapterTime") && !command.Is("RealTime"))
        && Controller.Commands.GetValueOrDefault(Controller.Inputs.Count, [])
            .All(command => !command.Is("MidwayFileTime") && !command.Is("MidwayChapterTime") && !command.Is("MidwayRealTime"));
    }

    public static bool PreventSendStudioState = false; // a cursed demand of tas helper's predictor

    internal static void SendStudioState() {
        if (PreventSendStudioState) {
            return;
        }
        var previous = Controller.Previous;
        var state = new StudioState {
            CurrentLine = previous?.StudioLine ?? -1,
            CurrentLineSuffix = $"{Controller.CurrentFrameInInput + (previous?.FrameOffset ?? 0)}{previous?.RepeatString ?? ""}",
            CurrentFrameInTas = Controller.CurrentFrameInTas,
            SaveStateLines = SavestateManager.AllSavestates.Select(state => state.StudioLine).ToArray(),
            PlaybackRunning = CurrState == State.Running,

            FileNeedsReload = Controller.NeedsReload,
            TotalFrames = Controller.Inputs.Count,

            GameInfo = GameInfo.StudioInfo,
            LevelName = GameInfo.LevelName,
            ChapterTime = GameInfo.ChapterTime,

            // ShowSubpixelIndicator = TasSettings.InfoSubpixelIndicator && Engine.Scene is Level or Emulator,
        };
        GameInterop.SetStudioState(ref state);

        CommunicationWrapper.SendState(state);
    }
}
