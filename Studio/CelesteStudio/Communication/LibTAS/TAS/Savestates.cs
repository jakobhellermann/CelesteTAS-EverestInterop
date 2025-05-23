using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TAS.EverestInterop;
using TAS.Input;

namespace TAS;

/// Handles saving / loading game state with SpeedrunTool
public static class Savestates {
    public static int StudioHighlightLine = 0;
    
    private static bool savedByBreakpoint;
    private static int savedChecksum;
    private static InputController? savedController;
    
    private static int SavedCurrentFrame => IsSaved ? savedController.CurrentFrameInTas : -1;

    private static bool BreakpointHasBeenDeleted => IsSaved && 
                                                    savedByBreakpoint && 
                                                    Manager.Controller.FastForwards.TryGetValue(SavedCurrentFrame, out var ff)
                                                    && ff is { SaveState: false };
    

    [MemberNotNullWhen(true, nameof(savedController))]
    private static bool IsSaved => // StateManager.Instance.IsSaved &&
                                   // StateManager.Instance.SavedByTas &&
                                   savedController != null &&
                                   savedController.FilePath == Manager.Controller.FilePath;
    
    /// Update for each TAS frame
    public static void Update() {
        // Only save-state when the current breakpoint is the last save-state one
        if (Manager.Controller.Inputs.Count > Manager.Controller.CurrentFrameInTas
            && Manager.Controller.FastForwards.TryGetValue(Manager.Controller.CurrentFrameInTas, out var currentFastForward) && currentFastForward is { SaveState: true }
            && Manager.Controller.FastForwards.Last(pair => pair.Value.SaveState).Value == currentFastForward
            && SavedCurrentFrame != currentFastForward.Frame
           ) { 
             SaveState(byBreakpoint: true); 
             return;
        }
        // Autoload state after entering the level if the TAS was started outside the level
        if (Manager.Running && IsSaved
                            // && Engine.Scene is Level
                            && Manager.Controller.CurrentFrameInTas < savedController.CurrentFrameInTas) {
            LoadState();
        }
    }

    /// Update for checking hotkeys
    internal static void UpdateMeta() {
        /*if (!SpeedrunToolInterop.Installed) {
            return;
        }*/
        
        if (Manager.Running && Hotkeys.SaveState.Pressed) {
            SaveState(byBreakpoint: false);
            return;
        }
        if (Hotkeys.ClearState.Pressed) {
            ClearState();
            Manager.DisableRun();
            return;
        }

        if (Manager.Running && BreakpointHasBeenDeleted) {
            ClearState();
        }
    }

    // Called explicitly to ensure correct execution order
    internal static void EnableRun() {
        if (/*SpeedrunToolInterop.Installed && */ IsSaved) {
            LoadState();
        }
    }

    public static void SaveState(bool byBreakpoint) {
        if (IsSaved &&
            Manager.Controller.CurrentFrameInTas == savedController.CurrentFrameInTas &&
            savedChecksum == Manager.Controller.CalcChecksum(savedController.CurrentFrameInTas))
        {
            return; // Already saved
        }
        
        /*if (!StateManager.Instance.SaveState()) {
            return;
        }*/
        
        savedByBreakpoint = byBreakpoint;
        savedChecksum = Manager.Controller.CalcChecksum(Manager.Controller.CurrentFrameInTas);
        savedController = Manager.Controller.Clone();
        SaveGameInfo();
        SetTasState();
    }

    public static void LoadState() {
        if (IsSaved) {
            if (!BreakpointHasBeenDeleted && savedChecksum == Manager.Controller.CalcChecksum(savedController.CurrentFrameInTas)) {
                if (Manager.Controller.CurrentFrameInTas == savedController.CurrentFrameInTas) {
                    // Don't repeat loading the state, just play
                    Manager.NextState = Manager.State.Running;
                    return;
                }

                if (/*Engine.Scene is Level */ true) {
                    // StateManager.Instance.LoadState();
                    Manager.Controller.CopyProgressFrom(savedController);

                    LoadGameInfo();
                    UpdateStudio();
                    SetTasState();
                }
            } else {
                ClearState();
            }
        }
    }

    public static void ClearState() {
        // StateManager.Instance.ClearState();
        ClearGameInfo();
        savedByBreakpoint = false;
        savedChecksum = -1;
        savedController = null;

        UpdateStudio();
    }

    private static void SaveGameInfo() {

    }

    private static void LoadGameInfo() {

    }

    private static void ClearGameInfo() {

    }

    private static void SetTasState() {
        if (Manager.Controller.HasFastForward) {
            Manager.CurrState = Manager.NextState = Manager.State.Running;
        } else {
            Manager.CurrState = Manager.NextState = Manager.State.Paused;
        }
    }

    private static void UpdateStudio() {
        // TODO GameInfo.Update();
        Manager.SendStudioState();
    }
}
