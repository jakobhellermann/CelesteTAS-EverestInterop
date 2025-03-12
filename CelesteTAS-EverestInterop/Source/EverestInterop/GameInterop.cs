using StudioCommunication;
using UnityEngine;

namespace TAS.EverestInterop;

public static class GameInterop {
    public static bool IsUnsafeInput() => false;

    public static bool IsInsideLevel() => true;

    /// TAS-execution is paused during loading screens
    public static bool IsLoading() {
        if (!GameCore.IsAvailable()) return true;
        return GameCore.Instance.currentCoreState is GameCore.GameCoreState.ChangingScene ||
               GameCore.Instance.currentCutScene;
    }

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() => false;

    public static void SetStudioState(ref StudioState state) {
        if (Player.i is { } player) {
            state.PlayerPosition = Vec2(player.transform.position);
            state.PlayerPositionRemainder = (0, 0);
            state.PlayerSpeed = Vec2(player.Velocity);
        }
    }

    private static (float X, float Y) Vec2(Vector2 vec) {
        return (vec.x, vec.y);
    }
    
    public static Camera? MainCamera => Camera.main; // TODO: this can usually be done cheaper
}
