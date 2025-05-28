using StudioCommunication;
using TAS.Utils;
using UnityEngine;

namespace TAS.EverestInterop;

public static class GameInterop {
    public static bool IsUnsafeInput() => false;

    public static bool IsInsideLevel() => true;

    /// TAS-execution is paused during loading screens
    // HeroController.UnsafeInstance?.transitionState is HeroTransitionState.WAITING_TO_ENTER_LEVEL;
    public static bool IsLoading() => false; 

    /// Whether the game is currently truly loading, i.e. waiting an undefined amount of time
    public static bool IsActuallyLoading() => false;

    public static void SetStudioState(ref StudioState state) {
        if (HeroController.SilentInstance is { } player) {
            state.PlayerPosition = Vec2(player.transform.position);
            state.PlayerPositionRemainder = Vec2(Vector2.zero);
            state.PlayerSpeed = Vec2(player.current_velocity);
        }
    }

    private static (float, float) Vec2(Vector2 vec) => (vec.x, vec.y);

    public static Camera? MainCamera {
        get {
            var cameras = typeof(GameCameras).GetFieldValue<GameCameras>("_instance");
            if (!cameras) return null;

            return cameras!.tk2dCam.ScreenCamera;
        }
    }
}
