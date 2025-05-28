using System;
using System.Collections.Generic;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class ConsoleEnhancements {
    private static readonly Dictionary<string, string> AllModNames = new();

    internal const int InitializePriority = 0;

    public static string GetModName(Type type) {
        // tells you where that weird entity/trigger comes from
        return AllModNames.GetValueOrDefault(type.Assembly.FullName!, "Unknown");
    }
}
