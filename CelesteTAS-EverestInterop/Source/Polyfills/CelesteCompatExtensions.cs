using System;
using BepInEx.Logging;
using TAS.Input;

namespace TAS;

public static class CelesteCompatExtensions {
    public static bool Has(this ExecuteTiming states, ExecuteTiming flag) => (states & flag) == flag;
    
    public static void LogException(this Exception e, string message) {
        TAS.Log.Error($"{message}: {e}");
    }

    public static void Log(this string message, LogLevel level = LogLevel.Info) {
        TAS.Log.LogMessage(message, level);
    }
    public static void Log(this Exception message, LogLevel level = LogLevel.Info) {
        TAS.Log.LogMessage(message, level);
    }

    public static string ReplaceLineEndings(this string text, string replacementText) {
        return text.Replace("\n", replacementText);
    }
}

public class UnreachableException : Exception;
