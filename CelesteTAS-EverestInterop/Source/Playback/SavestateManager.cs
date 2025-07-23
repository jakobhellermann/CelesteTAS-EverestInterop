using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using System;
using System.IO;
using TAS.EverestInterop;
using TAS.Input;
using TAS.ModInterop;
using TAS.UnityInterop;
using TAS.Utils;

namespace TAS.Playback;

/// Handles saving / loading game state with SpeedrunTool
internal static class SavestateManager {
    public readonly record struct Savestate(InputController Controller, int Checksum, bool SavedByBreakpoint) {
        /// SpeedrunTool slot, which is used for this save-state
        public readonly string Slot = $"{SpeedrunToolInterop.DefaultSlot}_{Checksum}";

        public int Frame => Controller.CurrentFrameInTas;
        public int StudioLine =>
            (SavedByBreakpoint
                ? (Manager.Controller.FastForwards.GetValueOrDefault(Frame)?.StudioLine ??
                   Manager.Controller.Comments.GetValueOrDefault(Frame)?.Where(comment => {
                       var span = comment.Text.AsSpan().TrimStart();
                       return span.StartsWith("***") && span["***".Length..].Contains("s", StringComparison.OrdinalIgnoreCase);
                   }).FirstOrNull()?.StudioLine)
                : Manager.Controller.Inputs.GetValueOrDefault(Frame)?.StudioLine) ?? -1;

        /// Checks if the breakpoint is currently just commented out
        public bool BreakpointCommented =>
            SavedByBreakpoint
                && (Manager.Controller.Comments.GetValueOrDefault(Frame)?.Any(comment => {
                    var span = comment.Text.AsSpan().TrimStart();
                    return span.StartsWith("***") && span["***".Length..].Contains("s", StringComparison.OrdinalIgnoreCase);
                }) ?? false);

        /// Check if the breakpoint has been deleted and not just commented out
        public bool BreakpointDeleted =>
            SavedByBreakpoint
                && Manager.Controller.FastForwards.GetValueOrDefault(Frame)?.SaveState != true
                && !BreakpointCommented;

        public bool Load() => SavestateManager.Load(this);
        public void Clear() => SavestateManager.Clear(this);
    }

    public static IEnumerable<Savestate> AllSavestates => BreakpointSavestates
        .Concat(ManualSavestate.HasValue ? [ManualSavestate.Value] : [])
        .OrderBy(state => state.Frame);

    private static Savestate? ManualSavestate;
    private static readonly List<Savestate> BreakpointSavestates = [];

    [UsedImplicitly] // Only included in hot-reloading support
    public static void ClearAllSavestates() {
        ManualSavestate?.Clear();
        ManualSavestate = null;

        foreach (var state in BreakpointSavestates) {
            state.Clear();
        }
        BreakpointSavestates.Clear();
    }

    /// Update for each TAS frame
    public static void Update() {
        var controller = Manager.Controller;

        if (Manager.CurrState != Manager.State.Running) {
            // Only savestate while TAS is actively running and not while paused
            return;
        }
        if (!SpeedrunToolInterop.Installed) {
            // Validate that no savestate breakpoints are used
            if (controller.FastForwards.GetValueOrDefault(controller.CurrentFrameInTas) is { SaveState: true} fastForward) {
                PopupToast.ShowAndLog($"""
                                       {Path.GetFileName(fastForward.FilePath)} line {fastForward.FileLine}:
                                       Found savestate breakpoint '{fastForward.Format()}'. 
                                       The 'Speedrun Tool' mod is required to use savestates with CelesteTAS.
                                       """, timeout: 5.0f);
            }

            return;
        }

        // Only save-state when the current breakpoint is new
        if (controller.CurrentFrameInTas < controller.Inputs.Count
            && controller.FastForwards.GetValueOrDefault(controller.CurrentFrameInTas) is { SaveState: true } currentFastForward
            && controller.CurrentFrameInTas == currentFastForward.Frame
            && Save(byBreakpoint: true, out var savestate)
        ) {
            if (SpeedrunToolInterop.MultipleSaveSlotsSupported) {
                BreakpointSavestates.Add(savestate);
            } else {
                ManualSavestate = savestate;
            }

            return;
        }

        // Autoload state after entering the level, if the TAS was started outside the level
        if (GameInterop.IsInsideLevel()) {
            // Load ideal savestate to start playing from for frame step back
            if (Manager.FrameStepBackTargetFrame > 0) {
                foreach (var state in AllSavestates.Reverse()) {
                    if (state.Frame > Manager.FrameStepBackTargetFrame || state.Frame <= controller.CurrentFrameInTas) {
                        continue;
                    }

                    state.Load();
                    return;
                }

                // No viable state found
                return;
            }

            foreach (var state in AllSavestates.Reverse()) {
                if (state.Frame <= controller.CurrentFrameInTas
                    || Manager.Controller.FastForwards.Values.Any(val => val.Frame >= controller.CurrentFrameInTas && val.Frame < state.Frame && val.ForceStop)
                    || controller.FilePath != state.Controller.FilePath || state.BreakpointCommented) {
                    continue;
                }

                state.Load();
                if (state.Frame == controller.CurrentFrameInTas) {
                    Manager.CurrState = Manager.NextState = Manager.State.Paused;
                }
                return;
            }
        }
    }

    /// Update for checking hotkeys
    internal static void UpdateMeta() {
        if (!SpeedrunToolInterop.Installed) {
            return;
        }

        if (Manager.Running && Hotkeys.SaveState.Pressed) {
            ManualSavestate?.Clear();
            ManualSavestate = Save(byBreakpoint: false, out var savestate) ? savestate : null;
            return;
        }
        if (Hotkeys.ClearState.Pressed) {
            if (ManualSavestate != null) {
                ManualSavestate.Value.Clear();
                ManualSavestate = null;
            } else {
                foreach (var state in BreakpointSavestates) {
                    state.Clear();
                }
                BreakpointSavestates.Clear();
            }
            return;
        }

        if (Manager.Running) {
            ClearDeletedSavestates();
        }
    }

    /// Clean-up deleted breakpoint savestates, to free unused memory
    private static void ClearDeletedSavestates() {
        if (SpeedrunToolInterop.MultipleSaveSlotsSupported) {
            foreach (var state in BreakpointSavestates) {
                if (state.BreakpointDeleted) {
                    state.Clear();
                }
            }
            BreakpointSavestates.RemoveAll(state => state.BreakpointDeleted);
        } else if (ManualSavestate is { SavedByBreakpoint: true, BreakpointDeleted: true } manual) {
            manual.Clear();
            ManualSavestate = null;
        }
    }

    [EnableRun]
    internal static void EnableRun() {
        if (!SpeedrunToolInterop.Installed || !GameInterop.IsInsideLevel()) {
            return;
        }

        // If the file was changed, there might be leftover breakpoints
        ClearDeletedSavestates();

        // Load ideal savestate to start playing from for frame step back
        if (Manager.FrameStepBackTargetFrame > 0) {
            foreach (var state in AllSavestates.Reverse()) {
                if (state.Frame > Manager.FrameStepBackTargetFrame) {
                    continue;
                }

                if (!state.Load()) {
                    continue;
                }
                if (state.Frame == Manager.FrameStepBackTargetFrame) {
                    Manager.CurrState = Manager.NextState = Manager.State.Paused;
                }
                return;
            }

            // No viable state found
            return;
        }

        foreach (var state in AllSavestates.Reverse()) {
            if (state.BreakpointCommented
                || Manager.Controller.FastForwards.Any(entry => entry.Value.Frame < state.Frame && entry.Value.ForceStop)
            ) {
                continue;
            }

            if (!state.Load()) {
                continue;
            }

            // Pause TAS if latest breakpoint
            if (Manager.Controller.FastForwards.LastOrDefault().Value?.Frame <= state.Frame) {
                Manager.CurrState = Manager.NextState = Manager.State.Paused;
            } else {
                Manager.CurrState = Manager.NextState = Manager.State.Running;
            }
            return;
        }
    }

    private static bool Save(bool byBreakpoint, out Savestate savestate) {
        int checksum = Manager.Controller.CalcChecksum(Manager.Controller.CurrentFrameInTas);

        // Check for already existing savestate
        if (AllSavestates.Any(state => state.Frame == Manager.Controller.CurrentFrameInTas && state.Checksum == checksum)) {
            savestate = default;
            return false;
        }

        savestate = new Savestate(Manager.Controller.Clone(), Manager.Controller.CalcChecksum(Manager.Controller.CurrentFrameInTas), byBreakpoint);
        if (!SpeedrunToolInterop.SaveState(savestate.Slot)) {
            return false;
        }

        UpdateStudio();
        return true;
    }
    private static bool Load(Savestate savestate) {
        // Don't load save-states while recording
        if (TASRecorderInterop.IsRecording) {
            return false;
        }

        if (savestate.BreakpointDeleted || savestate.Checksum != Manager.Controller.CalcChecksum(savestate.Controller.CurrentFrameInTas)) {
            return false; // Invalid
        }

        if (Manager.Controller.CurrentFrameInTas == savestate.Controller.CurrentFrameInTas) {
            // Don't repeat loading the state, just play
            Manager.NextState = Manager.State.Running;
            return true;
        }

        if (!SpeedrunToolInterop.LoadState(savestate.Slot)) {
            return false;
        }

        Manager.Controller.CopyProgressFrom(savestate.Controller);

        UpdateStudio();
        return true;
    }
    private static void Clear(Savestate savestate) {
        SpeedrunToolInterop.ClearState(savestate.Slot);

        UpdateStudio();
    }

    private static void UpdateStudio() {
        GameInfo.Update();
        Manager.SendStudioState();
    }
}
