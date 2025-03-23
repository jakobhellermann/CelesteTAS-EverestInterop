using System;
using static TAS.DebugInfo;

namespace TAS;

public class GameInfo {
    public static string StudioInfo = "";
    public static string LevelName = "";
    public static string ChapterTime = "";

    public static void Update() {
        try {
            // var filter = DebugFilter.Tweens | DebugFilter.RapidlyChanging|DebugFilter.Monsters|DebugFilter.Camera;
            var filter = DebugFilter.RapidlyChanging|DebugFilter.Monsters|DebugFilter.Tweens;
            StudioInfo = GetInfoText(filter);
            ChapterTime = $"{Manager.Controller.CurrentFrameInTas}";
        } catch (Exception e) {
            StudioInfo = "<error>";
            Log.Error($"Failed to get game info text: {e}");
        }

        LevelName =
            (GameCore.IsAvailable() && GameCore.Instance.gameLevel ? GameCore.Instance.gameLevel?.name : null) ?? "";
    }
}
