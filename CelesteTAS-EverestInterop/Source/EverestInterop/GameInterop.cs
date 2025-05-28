using StudioCommunication;
using UnityEngine;

namespace TAS.EverestInterop;

public static class GameInterop {
    public static bool IsUnsafeInput() => false;

    public static bool IsInsideLevel() => true;

    /// TAS-execution is paused during loading screens
    public static bool IsLoading() => false;

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() => false;

    public static void SetStudioState(ref StudioState state) {
        // state.PlayerPosition = TODO;
        // state.PlayerPositionRemainder = TODO;
        // state.PlayerSpeed = TODO;
    }
    
    public static Camera? MainCamera => Camera.main; // TODO: this can usually be done cheaper
}
