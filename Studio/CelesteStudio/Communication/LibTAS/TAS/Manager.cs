using CelesteStudio.Communication;
using System;
using System.Collections.Concurrent;
using System.Linq;
using JetBrains.Annotations;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.Input;
using TAS.Module;
using TAS.Playback;
using TAS.Utils;

namespace TAS;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class EnableRunAttribute(int priority = 0) : EventAttribute(priority);

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class DisableRunAttribute(int priority = 0) : EventAttribute(priority);

/// Causes the method to be called every real-time frame, even if a TAS is currently running / paused
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class UpdateMetaAttribute : Attribute;

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
    public static float PlaybackSpeed { get; private set; } = 1.0f;

    public static State CurrState, NextState;
    public static readonly InputController Controller = new();

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

#if DEBUG
    // Hot-reloading support
    [Load]
    private static void RestoreStudioTasFilePath() {
        /*if (Engine.Instance.GetDynamicDataInstance().Get<string>("CelesteTAS_FilePath") is { } filePath) {
            Controller.FilePath = filePath;
        }*/

    }

    [Unload]
    private static void SaveStudioTasFilePath() {
        Controller.Stop();
        Controller.Clear();
    }
#endif

    public static void EnableRun() {
        if (Running) {
            return;
        }

        CurrState = NextState = State.Running;
        PlaybackSpeed = 1.0f;

        Controller.Stop();
        Controller.RefreshInputs();

        if (Controller.Inputs.Count == 0) {
            // Empty / Invalid file
            CurrState = NextState = State.Disabled;
            // SyncChecker.ReportRunFinished();
            return;
        }

        AttributeUtils.Invoke<EnableRunAttribute>();

        $"Starting TAS: {Controller.FilePath}".Log();
    }

    public static void DisableRun() {
        Console.WriteLine($"Disablerun called");
        if (!Running) {
            return;
        }

        "Stopping TAS".Log();

        AttributeUtils.Invoke<DisableRunAttribute>();
        // SyncChecker.ReportRunFinished();
        CurrState = NextState = State.Disabled;
        Controller.Stop();
    }

    /// Will start the TAS on the next update cycle
    public static void EnableRunLater() => NextState = State.Running;
    /// Will stop the TAS on the next update cycle
    public static void DisableRunLater() => NextState = State.Disabled;

    /// Updates the TAS itself
    public static void Update() {
        if (!Running && NextState == State.Running) {
            EnableRun();
        }
        if (Running && NextState == State.Disabled) {
            DisableRun();
        }

        CurrState = NextState;

        while (mainThreadActions.TryDequeue(out var action)) {
            action.Invoke();
        }

        SavestateManager.Update();

        if (!Running || CurrState == State.Paused || IsLoading()) {
            return;
        }

        /*if (CriticalErrorHandler.CurrentHandler != null) {
            // Always prevent execution inside crash handler, even with Unsafe
            // TODO: Move this after executing the first frame, once Everest fixes scene changes from the crash handler
            DisableRun();
            return;
        }*/

        if (Controller.HasFastForward) {
            NextState = State.Running;
        }

        Controller.AdvanceFrame(out bool couldPlayback);

        if (!couldPlayback) {
            DisableRun();
            return;
        }

        // Auto-pause at end of drafts
        if (!Controller.CanPlayback && TasSettings.AutoPauseDraft && IsDraft()) {
            NextState = State.Paused;
        }
        // Pause the TAS if breakpoint is hit
        // Special-case for end of regular files, to update *Time-commands
        else if (Controller.Break && (Controller.CanPlayback || IsDraft())) {
            Controller.NextLabelFastForward = null;
            NextState = State.Paused;
        }

        // Prevent executing unsafe actions unless explicitly allowed
        /*TODO if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
            // Only allow specific scenes
            if (Engine.Scene is not (Level or LevelLoader or LevelExit or Emulator or LevelEnter)) {
                SyncChecker.ReportUnsafeAction();
                DisableRun();
            }
            // Disallow modifying options
            else if (Engine.Scene is Level level && level.Tracker.GetEntity<TextMenu>() is { } menu) {
                var item = menu.Items.FirstOrDefault();

                if (item is TextMenu.Header { Title: { } title }
                    && (title == Dialog.Clean("OPTIONS_TITLE") || title == Dialog.Clean("MENU_VARIANT_TITLE")
                        || Dialog.Has("MODOPTIONS_EXTENDEDVARIANTS_PAUSEMENU_BUTTON") && title == Dialog.Clean("MODOPTIONS_EXTENDEDVARIANTS_PAUSEMENU_BUTTON").ToUpperInvariant())
                    || item is TextMenuExt.HeaderImage { Image: "menu/everest" }
                ) {
                    SyncChecker.ReportUnsafeAction();
                    DisableRun();
                }
            }
        }*/
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

        if (Running && Hotkeys.FastForwardComment.Pressed) {
            Controller.FastForwardToNextLabel();
            return;
        }

        /*if (TASRecorderInterop.IsRecording) {
            // Force recording at 1x playback
            NextState = State.Running;
            PlaybackSpeed = 1.0f;
            return;
        }*/

        switch (CurrState) {
            case State.Running:
                if (Hotkeys.PauseResume.Pressed || Hotkeys.FrameAdvance.Pressed) {
                    NextState = State.Paused;
                }
                break;

            case State.FrameAdvance:
                NextState = State.Paused;
                break;

            case State.Paused:
                if (Hotkeys.PauseResume.Pressed) {
                    NextState = State.Running;
                } else if (Hotkeys.FrameAdvance.Repeated || Hotkeys.FastForward.Check) {
                    // Prevent frame-advancing into the end of the TAS
                    if (!Controller.CanPlayback) {
                        Controller.RefreshInputs(); // Ensure there aren't any new inputs
                    }
                    if (Controller.CanPlayback) {
                        NextState = State.FrameAdvance;
                    } else {
                        // TODO: Display toast "Reached end-of-file". Currently not possible due to them not being updated
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
            case State.Running when Hotkeys.FastForward.Check:
                PlaybackSpeed = TasSettings.FastForwardSpeed;
                break;
            case State.Running when Hotkeys.SlowForward.Check:
                PlaybackSpeed = TasSettings.SlowForwardSpeed;
                break;

            case State.Paused or State.SlowForward when Hotkeys.SlowForward.Check:
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

    /// TAS-execution is paused during loading screens
    public static bool IsLoading() {
        return false; // TODO
        /*return Engine.Scene switch {
            Level level => level.IsAutoSaving() && level.Session.Level == "end-cinematic",
            SummitVignette summit => !summit.ready,
            Overworld overworld => overworld.Current is OuiFileSelect { SlotIndex: >= 0 } slot && slot.Slots[slot.SlotIndex].StartingGame ||
                                   overworld.Next is OuiChapterSelect && UserIO.Saving ||
                                   overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings),
            Emulator emulator => emulator.game == null,
            _ => Engine.Scene is LevelExit or LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby",
        };*/
    }

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() {
        if (Controller.Inputs!.GetValueOrDefault(Controller.CurrentFrameInTas) is { } current && current.ParentCommand is { } command && command.Is("SaveAndQuitReenter")) {
            // SaveAndQuitReenter manually adds the optimal S&Q real-time
            return true;
        }

        return false;
        /*return Engine.Scene switch {
            Level level => level.IsAutoSaving(),
            SummitVignette summit => !summit.ready,
            Overworld overworld => overworld.Next is OuiChapterSelect && UserIO.Saving ||
                                   overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings),
            LevelExit exit => exit.mode == LevelExit.Mode.Completed && !exit.completeLoaded || UserIO.Saving,
            _ => Engine.Scene is LevelLoader or GameLoader || Engine.Scene.GetType().Name == "LevelExitToLobby" && UserIO.Saving,
        };*/
    }

    /// Determine if current TAS file is a draft
    private static bool IsDraft() {
        /*if (TASRecorderInterop.IsRecording) {
            return false;
        }*/

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

            // GameInfo = GameInfo.StudioInfo,
            GameInfo = "yo ho",
            // LevelName = GameInfo.LevelName,
            // ChapterTime = GameInfo.ChapterTime,

            // ShowSubpixelIndicator = TasSettings.InfoSubpixelIndicator && Engine.Scene is Level or Emulator,
        };

        /*if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
            state.PlayerPosition = (player.Position.X, player.Position.Y);
            state.PlayerPositionRemainder = (player.PositionRemainder.X, player.PositionRemainder.Y);
            state.PlayerSpeed = (player.Speed.X, player.Speed.Y);
        } else if (Engine.Scene is Emulator emulator && emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            state.PlayerPosition = (classicPlayer.x, classicPlayer.y);
            state.PlayerPositionRemainder = (classicPlayer.rem.X, classicPlayer.rem.Y);
            state.PlayerSpeed = (classicPlayer.spd.X, classicPlayer.spd.Y);
        }

        CommunicationWrapper.SendState(state);*/

        CommunicationWrapper.OnStateChanged(state);
    }
}
