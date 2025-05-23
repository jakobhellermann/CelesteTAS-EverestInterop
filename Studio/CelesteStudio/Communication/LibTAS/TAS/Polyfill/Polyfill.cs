using System;
using System.Reflection;

namespace TAS.Utils;

public enum LogLevel {
    Debug,
    Info,
    Verbose,
    Warn,
    Error,
}

public static class Polyfill {
    public static void Log(this object msg, LogLevel level = LogLevel.Info) {
    }

    public static void LogException(this Exception exception, string msg, LogLevel level = LogLevel.Info) {
    }

    public static Type[] GetTypesSafe(this Assembly asm) {
        return asm.GetTypes();
    }
}

public static class FakeAssembly {
    public static Assembly GetFakeEntryAssembly() {
        return typeof(FakeAssembly).Assembly;
    }
}
public static class Everest {
    public static string PathEverest = "/tmp/";
}
public static class Calc {
    public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
public static class Core {
    // TODO
    public static float PlaybackDeltaTime => 0;
}
