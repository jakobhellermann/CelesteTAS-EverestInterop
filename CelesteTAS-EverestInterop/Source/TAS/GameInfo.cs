using System;
using static TAS.DebugInfo;

namespace TAS;

public class GameInfo {
    public static string StudioInfo = "";
    public static string LevelName = "";
    public static string ChapterTime = "";

    public const DebugFilter Filter =
            DebugFilter.RapidlyChanging
            // | DebugFilter.Monsters
            // | DebugFilter.AnimationClips
            // | DebugFilter.Tweens
        ;

    public static void Update() {
        try {
            StudioInfo = GetInfoText(Filter);
            ChapterTime = $"{Manager.Controller.CurrentFrameInTas}";
        } catch (Exception e) {
            StudioInfo = "<error>";
            Log.Error($"Failed to get game info text: {e}");
        }

        LevelName =
            (GameCore.IsAvailable() && GameCore.Instance.gameLevel ? GameCore.Instance.gameLevel?.name : null) ?? "";
    }
}
