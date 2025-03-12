using System;
using System.Collections.Concurrent;
using System.Linq;
using JetBrains.Annotations;
using NineSolsAPI;
using StudioCommunication;
using System.Collections.Generic;
using TAS.Communication;
using TAS.Input;
using TAS.UnityInterop;
using TAS.Utils;
using UnityEngine;

namespace TAS;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class EnableRunAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class DisableRunAttribute : Attribute;

/// Causes the method to be called every real-time frame, even if a TAS is currently running / paused
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class UpdateMetaAttribute : Attribute;

/// Main controller, which manages how the TAS is played back
public static class Manager {
    private const bool PauseOnEndDraft = true;
    
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
            Application.targetFrameRate = (int)(InputHelper.CurrentTasFramerate * playbackSpeed);
            playbackSpeed = value;
        }
    }

    public static State CurrState, NextState;
    public static readonly InputController Controller = new();

    private static readonly ConcurrentQueue<Action> mainThreadActions = new();

    public static bool DidComplete;

/*#if DEBUG
    // Hot-reloading support
    [Load]
    private static void RestoreStudioTasFilePath() {
        if (Engine.Instance.GetDynamicDataInstance().Get<string>("CelesteTAS_FilePath") is { } filePath) {
            Controller.FilePath = filePath;
        }

        // Stop TAS to avoid blocking reload
        typeof(AssetReloadHelper)
            .GetMethodInfo(nameof(AssetReloadHelper.Do), [typeof(string), typeof(Func<bool, Task>), typeof(bool), typeof(bool)])!
            .HookBefore(DisableRun);
    }

    [Unload]
    private static void SaveStudioTasFilePath() {
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_FilePath", Controller.FilePath);

        Controller.Stop();
        Controller.Clear();
    }
#endif*/

    public static void EnableRun() {
        if (Running) {
            return;
        }

        DidComplete = false;
        CurrState = NextState = State.Running;
        PlaybackSpeed = 1.0f;

        Controller.Stop();
        Controller.RefreshInputs(forceRefresh: true);

        if (Controller.Inputs.Count == 0) {
            // Empty file
            CurrState = NextState = State.Disabled;
            return;
        }

        AttributeUtils.Invoke<EnableRunAttribute>();

        // This needs to happen after EnableRun, otherwise the input state will be reset in BindingHelper.SetTasBindings
        Savestates.EnableRun();

        $"Starting TAS: {Controller.FilePath}".Log();
    }

    public static void DisableRun() {
        if (!Running) {
            return;
        }

        "Stopping TAS".Log();

        AttributeUtils.Invoke<DisableRunAttribute>();

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
            if (Player.i?.animator is {} animator) {
                prePauseAnimatorStates.Add((animator, AnimatorSnapshot.Snapshot(animator)));
                var snap = AnimatorSnapshot.Snapshot(animator);
                prePauseAnimatorStates.Add((animator, snap));
                animator.enabled = false;
            }

            if (MonsterManager.Instance.ClosetMonster) {
                var monsterAnim = MonsterManager.Instance.ClosetMonster.animator;
                monsterAnim.enabled = false;
                prePauseAnimatorStates.Add((monsterAnim, AnimatorSnapshot.Snapshot(monsterAnim)));
            }
        } catch (Exception e) {
            ToastManager.Toast($"Error tyring to snapshot animator: {e}");
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

        CurrState = NextState;

        Savestates.Update();

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

        if (!Controller.CanPlayback) {
            DidComplete = true;
        }

        // Auto-pause at end of drafts
        if (!Controller.CanPlayback && IsDraft()) {
            NextState = PauseOnEndDraft ? State.Paused : State.Disabled;
        }
        // Pause the TAS if breakpoint is hit
        // Special-case for end of regular files, to update *Time-commands
        else if (Controller.Break && (Controller.CanPlayback || IsDraft())) {
            Controller.NextLabelFastForward = null;
            NextState = State.Paused;
        }

        // Prevent executing unsafe actions unless explicitly allowed
        /*if (SafeCommand.DisallowUnsafeInput && Controller.CurrentFrameInTas > 1) {
            // Only allow specific scenes
            if (Engine.Scene is not (Level or LevelLoader or LevelExit or Emulator or LevelEnter)) {
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
        
        while (mainThreadActions.TryDequeue(out var action)) {
            action.Invoke();
        }

        Hotkeys.UpdateMeta();
        Savestates.UpdateMeta();
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
        if (!GameCore.IsAvailable()) return true;

        return GameCore.Instance.currentCoreState == GameCore.GameCoreState.ChangingScene ||
               GameCore.Instance.currentCutScene != null;
        
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
    public static bool IsActuallyLoading() => IsLoading();

    /// Determine if current TAS file is a draft
    private static bool IsDraft() {
        /*if (TASRecorderInterop.IsRecording) {
            return false;
        }*/

        // Require any *Time, alternatively Midway*Time at the end for the TAS to be counted as finished
        return Controller.Commands.Values
            .SelectMany(commands => commands)
            .All(command => !command.Is("FileTime") && !command.Is("ChapterTime") && !command.Is("RealTime"))
        && DictionaryExtensions.GetValueOrDefault(Controller.Commands, Controller.Inputs.Count, [])
            .All(command => !command.Is("MidwayFileTime") && !command.Is("MidwayChapterTime") && !command.Is("MidwayRealTime"));
    }

    public static bool PreventSendStudioState = false; // a cursed demand of tas helper's predictor

    internal static void SendStudioState() {
        if (PreventSendStudioState) {
            return;
        }
        var previous = Controller.Previous;
        var state = new StudioState {
            CurrentLine = previous?.Line ?? -1,
            CurrentLineSuffix = $"{Controller.CurrentFrameInInput + (previous?.FrameOffset ?? 0)}{previous?.RepeatString ?? ""}",
            CurrentFrameInTas = Controller.CurrentFrameInTas,
            SaveStateLine = Savestates.StudioHighlightLine,
            PlaybackRunning = CurrState == State.Running,

            FileNeedsReload = Controller.NeedsReload,
            TotalFrames = Controller.Inputs.Count,
            GameInfo = GameInfo.StudioInfo,
            LevelName = GameInfo.LevelName,
            ChapterTime = GameInfo.ChapterTime,

            // ShowSubpixelIndicator = TasSettings.InfoSubpixelIndicator && Engine.Scene is Level or Emulator,
            PlayerPosition = Vec(Player.i?.transform.position),
            PlayerSpeed = Vec(Player.i?.Velocity),
        };

        /*if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
            state.PlayerPosition = (player.Position.X, player.Position.Y);
            state.PlayerPositionRemainder = (player.PositionRemainder.X, player.PositionRemainder.Y);
            state.PlayerSpeed = (player.Speed.X, player.Speed.Y);
        } else if (Engine.Scene is Emulator emulator && emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            state.PlayerPosition = (classicPlayer.x, classicPlayer.y);
            state.PlayerPositionRemainder = (classicPlayer.rem.X, classicPlayer.rem.Y);
            state.PlayerSpeed = (classicPlayer.spd.X, classicPlayer.spd.Y);
        }*/

        CommunicationWrapper.SendState(state);
    }

    private static (float X, float Y) Vec(Vector2? vec) => (vec?.x ?? 0, vec?.y ?? 0);

    /*
    [Monocle.Command("dump_tas", "Dumps the parsed TAS file into the console (CelesteTAS)"), UsedImplicitly]
    private static void CmdDumpTas() {
        if (Controller.NeedsReload) {
            if (Running) {
                "Cannot dump TAS file while running it with unparsed changes".Log(LogLevel.Error);
                return;
            }

            // Pretend to start a TAS. so that AbortTas detection works
            NextState = State.Running;
            Controller.RefreshInputs(forceRefresh: true);
            if (NextState == State.Disabled) {
                "TAS contains errors. Cannot dump to console".ConsoleLog(LogLevel.Error);
                return;
            }
            NextState = State.Disabled;
        }

        $"TAS file dump for '{Controller.FilePath}':".Log();

        var writer = Console.Out;
        for (int i = 0; i < Controller.Inputs.Count;) {
            foreach (var comment in Controller.Comments.GetValueOrDefault(i, [])) {
                writer.WriteLine($"#{comment.Text}");
            }
            foreach (var command in Controller.Commands.GetValueOrDefault(i, [])) {
                if (command.Attribute.ExecuteTiming == ExecuteTiming.Parse) {
                    // Comment-out parse-only commands
                    writer.WriteLine($"# {command.CommandLine.ToString()}");
                } else {
                    writer.WriteLine(command.CommandLine.ToString());
                }
            }
            if (Controller.FastForwards.TryGetValue(i, out var fastForward)) {
                writer.WriteLine(fastForward.ToString());
            }

            writer.WriteLine(Controller.Inputs[i]);
            i += Controller.Inputs[i].Frames;
        }
        writer.Flush();

        "Successfully dumped TAS file to console".ConsoleLog();
    }*/
}
