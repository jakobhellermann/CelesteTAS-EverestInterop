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
using TAS.Tracer;
using TAS.ModInterop;
using TAS.Playback;
using TAS.Tools;
using TAS.UnityInterop;
using TAS.Utils;
using UnityEngine;
using UnityEngine.LowLevel;
using PlayerLoopHelper;

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

    /// A breakpoint hit on the same frame as a load defers its pause until loading finishes — see Manager.Update.
    private static bool pendingBreakpointPause;

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    private static PopupToast.Entry? frameStepEofToast = null;
    private static PopupToast.Entry? autoPauseDraft = null;
    private static bool seenAutoPauseToast = false;

    // Allow accumulation of frames to step back, since the operation is time intensive
    internal static int FrameStepBackTargetFrame = -1;
    private const float FrameStepBackTime = 1.0f;
    private static float frameStepBackAmount = 0.0f;
    private static float frameStepBackTimeout = 0.0f;
    private static PopupToast.Entry? frameStepBackToast = null;

    public static bool DidComplete = false;

    /// The reason the most recent run stopped early (set by AbortTas); null while running or after a clean finish.
    public static string? LastAbortMessage;

    public static void EnableRun() {
        if (Running) {
            return;
        }

        DidComplete = false;
        LastAbortMessage = null;
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
        pendingBreakpointPause = false;
        Controller.Stop();
    }

    /// Will start the TAS on the next update cycle
    public static void EnableRunLater() => NextState = State.Running;
    /// Will stop the TAS on the next update cycle
    public static void DisableRunLater() => NextState = State.Disabled;

    /// TODO: slop
    /// Force the TAS into the paused state from *outside* the normal `NextState`→`Paused` transition (e.g. a
    /// savestate resume completing inside `SavestateManager.Update`, which runs after `Update`'s transition check
    /// and before `CurrState = NextState`). Assigning `CurrState = NextState = Paused` directly there would leave
    /// `CurrState` already Paused next frame, so the transition check never fires `EnablePause()` — MonoBehaviour
    /// updates and `timeScale` stay live through the "paused" frames and decay restored state (the savestate-resume
    /// freeze-leak). Calling this runs `EnablePause()` exactly once (guarded against the double-freeze that would
    /// corrupt the `loopBeforePause` snapshot) and then pins the state.
    public static void Pause() {
        if (CurrState != State.Paused) {
            EnablePause();
        }
        CurrState = NextState = State.Paused;
    }

    public static void EnablePause() {
        DeterministicTimePatch.OverwriteTimeScale = 0;

        FreezeScriptUpdates();

        try {
            // TODO(unity): pause animators
        } catch (Exception e) {
            Log.Error($"Error trying to snapshot animator: {e}");
        }
    }

    private static List<(Animator, AnimatorSnapshot)> prePauseAnimatorStates = [];

    // Pre-pause PlayerLoop snapshot
    private static PlayerLoopSystem? loopBeforePause;

    /// Stop all gameplay MonoBehaviour Update/LateUpdate while paused.
    private static void FreezeScriptUpdates() {
        loopBeforePause = PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystemHelper.Unregister(typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
        PlayerLoopSystemHelper.Unregister(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate));
    }

    private static void UnfreezeScriptUpdates() {
        if (loopBeforePause is { } loop) {
            PlayerLoop.SetPlayerLoop(loop);
            loopBeforePause = null;
        }
    }

    public static void DisablePause() {
        foreach (var (anim, _) in prePauseAnimatorStates) {
            // snapshot.Restore(anim);
            anim.enabled = true;
        }
        prePauseAnimatorStates.Clear();

        UnfreezeScriptUpdates();

        DeterministicTimePatch.OverwriteTimeScale = null;
    }

    /// Updates the TAS itself
    public static void Update() {
        if (CurrState != State.Paused && NextState == State.Paused) {
            EnablePause();

            // An end-of-file breakpoint completes the run at its pause (DidComplete set in the breakpoint check
            // below). TraceFrame for the final frame runs earlier in this same PostLateUpdate, so the trace is
            // complete exactly now — save it as the pause engages. Mid-run pauses have DidComplete=false → no-op.
            TasTracer.SaveCompletedTrace();
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

        // A breakpoint that coincided with a load deferred its pause (see below) so we wouldn't strand on a
        // still-loading frame. Loading is done now — pause on this clean frame, before AdvanceFrame moves us past
        // the breakpoint.
        if (pendingBreakpointPause) {
            pendingBreakpointPause = false;
            CompleteAtEndOfFile();
            NextState = State.Paused;
            return;
        }

        if (FrameStepBackTargetFrame > 0) {
            NextState = State.Running;
            PlaybackSpeed = FastForward.DefaultSpeed;
        } else if (Controller.CurrentFastForward is { } forward && forward.Frame > Controller.CurrentFrameInTas) {
            NextState = State.Running;
            PlaybackSpeed = forward.Speed;
        }

        Controller.AdvanceFrame(out bool couldPlayback);

        // couldPlayback is false once AdvanceFrame is called at the end frame — after it has run that frame's
        // commands (e.g. trailing asserts / FileTime). Reaching here completes the run: auto-pause a draft to keep
        // it editable on the last frame, otherwise stop. (A run stopped early — e.g. AbortTas — never gets here, so
        // DidComplete stays false.)
        if (!couldPlayback) {
            // A trailing command may have aborted the run this frame (AbortTas → NextState=Disabled). An abort is a
            // failure, not a completion: leave it disabling and keep DidComplete false.
            if (NextState == State.Disabled) {
                DisableRun();
                return;
            }

            DidComplete = true;
            TasTracer.SaveCompletedTrace();

            if (TasSettings.AutoPauseDraft && IsDraft()) {
                NextState = State.Paused;

                if (CurrState == State.Running && !FastForwarding) {
                    float duration = seenAutoPauseToast ? 2.0f : 8.0f;
                    if (autoPauseDraft is not { Active: true }) {
                        autoPauseDraft = PopupToast.Show(Dialog.Clean("TAS_AutoPauseToast"), duration);
                    } else {
                        autoPauseDraft.Text = Dialog.Clean("TAS_AutoPauseToast");
                        autoPauseDraft.Timeout = duration;
                    }

                    seenAutoPauseToast = true;
                }
            } else {
                DisableRun();
            }

            return;
        }

        // Catch frame step-back
        if (FrameStepBackTargetFrame > 0 && Controller.CurrentFrameInTas >= FrameStepBackTargetFrame) {
            FrameStepBackTargetFrame = -1;
            NextState = State.Paused;
        }

        // Pause the TAS if breakpoint is hit
        if (FrameStepBackTargetFrame == -1 && Controller.Break && (Controller.CanPlayback || IsDraft())) {
            Controller.NextLabelFastForward = null;
            // If a load was kicked off on this same frame (e.g. a `load`/savestate command sharing the breakpoint
            // frame), don't pause yet: pausing now strands us on a still-loading frame, and the first frame-advance
            // is wasted just clearing the load gate (IsLoading true→false) instead of advancing. Defer the pause to
            // the first non-loading frame — we're still on the breakpoint frame (it's frozen while loading).
            if (GameInterop.IsLoading()) {
                pendingBreakpointPause = true;
            } else {
                CompleteAtEndOfFile();
                NextState = State.Paused;
            }
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

    /// A pause landing on end-of-file (e.g. a trailing *** breakpoint) has consumed all its inputs — the run is
    /// over, so complete it AT the pause instead of a frame after the resume: a plain mid-run pause would write no
    /// trace (the completion path never fired), and resuming it runs one extra no-input game frame before the
    /// completion branch re-pauses. The trace itself is saved by the pause transition (see Update).
    private static void CompleteAtEndOfFile() {
        // Keep an abort honest: AbortTas on the breakpoint frame set NextState=Disabled — that run did not complete.
        if (Controller.CanPlayback || NextState == State.Disabled) return;
        DidComplete = true;
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
                // Never strand on a loading frame: if this step kicked off a load, stay in FrameAdvance so it runs to
                // completion, and settle to Paused only once we're back on a real, non-loading frame.
                if (!GameInterop.IsLoading()) {
                    NextState = State.Paused;
                }
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
                    // A completed draft is paused at its final frame with nothing left to play — resuming would run
                    // the end frame again and immediately re-pause. End the run instead.
                    NextState = DidComplete ? State.Disabled : State.Running;
                } else if (Hotkeys.FrameAdvance.Repeated || Hotkeys.FastForward.Check) {
                    if (DidComplete) {
                        // Same as resuming a completed draft: there's nothing left to advance into, so end the run.
                        NextState = State.Disabled;
                        break;
                    }

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
