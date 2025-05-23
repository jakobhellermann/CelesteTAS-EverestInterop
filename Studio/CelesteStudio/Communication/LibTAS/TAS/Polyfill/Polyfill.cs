using System;
using System.Reflection;

namespace TAS.Utils;

public enum LogLevel {
    Debug,
    Info,
    Verbose,
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
