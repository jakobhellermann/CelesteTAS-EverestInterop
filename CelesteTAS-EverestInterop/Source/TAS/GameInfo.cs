using System;
using static TAS.DebugInfo;

namespace TAS;

public class GameInfo {
    public static string StudioInfo = "";
    public static string LevelName = "";
    public static string ChapterTime = "";

    public static void Update() {
        try {
            StudioInfo = GetInfoText(TasMod.Instance.ConfigDebugInfo.Value);
            ChapterTime = $"{Manager.Controller.CurrentFrameInTas}";
        } catch (Exception e) {
            StudioInfo = "<error>";
            Log.Error($"Failed to get game info text: {e}");
        }

        LevelName =
            (GameCore.IsAvailable() && GameCore.Instance.gameLevel ? GameCore.Instance.gameLevel?.name : null) ?? "";
    }
}
