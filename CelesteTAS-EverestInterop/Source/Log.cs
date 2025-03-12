using BepInEx.Logging;
using UnityEngine;

namespace TAS;

internal static class Log {
    private static ManualLogSource logSource = null!;

    internal static void Init(ManualLogSource logSource) {
        Log.logSource = logSource;
    }

    internal static void Debug(object data) => logSource.LogDebug(data);

    internal static void Error(object data) => logSource.LogError(data);

    internal static void Fatal(object data) => logSource.LogFatal(data);

    internal static void Info(object data) => logSource.LogInfo(data);

    internal static void Message(object data) => logSource.LogMessage(data);

    internal static void Warn(object data) => logSource.LogWarning(data);

    internal static void LogMessage(object data, LogLevel level) => logSource.Log(level, data);

    private const bool TasTraceEnabled = false;
    
    internal static void TasTrace(object data) {
        // if (TasTraceEnabled && Manager.CurrState is not Manager.State.Disabled and not Manager.State.Paused) {
        if (TasTraceEnabled && Manager.CurrState is not Manager.State.Disabled && Time.timeScale > 0) {
            Info(data);
        }
    }
}
