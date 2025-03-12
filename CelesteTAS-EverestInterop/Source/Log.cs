using BepInEx.Logging;
using NineSolsAPI;

namespace TAS;

internal static class Log {
    private static ManualLogSource logSource = null!;

    internal static void Init(ManualLogSource source) {
        Log.logSource = source;
    }

    internal static void Debug(object? data) => logSource.LogDebug(data);

    internal static void Error(string section, object? data) => logSource.LogError($"[{section}] {data}");
    
    internal static void Error(object? data) => logSource.LogError(data);

    internal static void Fatal(object? data) => logSource.LogFatal(data);

    internal static void Info(object? data) => logSource.LogInfo(data);
    
    internal static void Info(string section, object? data) => logSource.LogInfo($"[{section}] {data}");

    internal static void Message(object? data) => logSource.LogMessage(data);

    internal static void Warn(object? data) => logSource.LogWarning(data);
    
    internal static void Warn(string section, object? data) => logSource.LogWarning($"[{section}] {data}");

    internal static void LogMessage(object? data, LogLevel level) => logSource.Log(level, data);

    internal static void Toast(object message) {
        ToastManager.Toast(message);
    }
}
