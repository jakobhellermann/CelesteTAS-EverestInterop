using System;
using static TAS.DebugInfo;

namespace TAS;

public class GameInfo {
    public static string StudioInfo = "";
    public static string LevelName = "";
    public static string ChapterTime = "";

    public const DebugFilter Filter = DebugFilter.Base;

    public static void Update() {
        try {
            StudioInfo = GetInfoText(Filter);
            ChapterTime = $"{Manager.Controller.CurrentFrameInTas}";
        } catch (Exception e) {
            StudioInfo = "<error>";
            Log.Error($"Failed to get game info text: {e}");
        }

        LevelName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
}
