using Celeste;
using Celeste.Mod;
using Celeste.Pico8;
using StudioCommunication;
using System.Linq;
using TAS.Utils;
using Engine = Monocle.Engine;

namespace TAS.EverestInterop;

public static class GameInterop {
    public static bool IsUnsafeInput() {
        return Engine.Scene is Level or LevelLoader or LevelExit or Emulator or LevelEnter;
    }

    public static bool IsInsideLevel() {
        return Engine.Scene is Level;
    }

    /// TAS-execution is paused during loading screens
    public static bool IsLoading() {
        return Engine.Scene switch {
            Level level => level.IsAutoSaving() && level.Session.Level == "end-cinematic",
            SummitVignette summit => !summit.ready,
            Overworld overworld => (overworld.Current is OuiFileSelect { SlotIndex: >= 0 } slot &&
                                    slot.Slots[slot.SlotIndex].StartingGame) ||
                                   (overworld.Next is OuiChapterSelect && UserIO.Saving) ||
                                   (overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings)),
            Emulator emulator => emulator.game == null,
            _ => Engine.Scene is LevelExit or LevelLoader or GameLoader ||
                 Engine.Scene.GetType().Name == "LevelExitToLobby",
        };
    }

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() {
        if (Manager.Controller.Inputs.GetValueOrDefault(Manager.Controller.CurrentFrameInTas) is { } current &&
            current.ParentCommand is { } command && command.Is("SaveAndQuitReenter")) {
            // SaveAndQuitReenter manually adds the optimal S&Q real-time
            return true;
        }

        return Engine.Scene switch {
            Level level => level.IsAutoSaving(),
            SummitVignette summit => !summit.ready,
            Overworld overworld => (overworld.Next is OuiChapterSelect && UserIO.Saving) ||
                                   (overworld.Next is OuiMainMenu && (UserIO.Saving || Everest._SavingSettings)),
            LevelExit exit => (exit.mode == LevelExit.Mode.Completed && !exit.completeLoaded) || UserIO.Saving,
            _ => Engine.Scene is LevelLoader or GameLoader ||
                 (Engine.Scene.GetType().Name == "LevelExitToLobby" && UserIO.Saving),
        };
    }

    public static void SetStudioState(ref StudioState state) {
        if (Engine.Scene is Level level && level.GetPlayer() is { } player) {
            state.PlayerPosition = (player.Position.X, player.Position.Y);
            state.PlayerPositionRemainder = (player.PositionRemainder.X, player.PositionRemainder.Y);
            state.PlayerSpeed = (player.Speed.X, player.Speed.Y);
        } else if (Engine.Scene is Emulator emulator &&
                   emulator.game?.objects.FirstOrDefault(o => o is Classic.player) is Classic.player classicPlayer) {
            state.PlayerPosition = (classicPlayer.x, classicPlayer.y);
            state.PlayerPositionRemainder = (classicPlayer.rem.X, classicPlayer.rem.Y);
            state.PlayerSpeed = (classicPlayer.spd.X, classicPlayer.spd.Y);
        }
    }
}
