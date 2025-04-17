using System;
using UnityEngine;
using static TAS.DebugInfo;

namespace TAS;

public enum SpeedUnit {
    PixelPerSecond,
    PixelPerFrame,
}

public class GameInfo {
    // TODO: port speed-unit conversion; stubbed to return the value unchanged
    public static float ConvertSpeedUnit(float value, SpeedUnit unit) => value;
    public static Vector2 ConvertSpeedUnit(Vector2 value, SpeedUnit unit) => value;

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
