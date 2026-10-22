using System;
using System.Collections.Generic;
using BepInEx.Logging;
using StudioCommunication;
using TAS.EverestInterop;
using TAS.ModInterop;

namespace TAS.InfoHUD;

internal class TargetQueryHost : ITargetQueryHost {
    public IEnumerable<Type> GetTypes() => ModUtils.GetTypes();
    public string GetModName(Type type) => ConsoleEnhancements.GetModName(type);
    public void LogError(string message) => message.Log(LogLevel.Error);
    public void LogException(Exception exception, string message) => exception.LogException(message);

    [Initialize(ConsoleEnhancements.InitializePriority + 1)]
    private static void CollectAllTypes() => TargetQuery.CollectAllTypes();
}

internal static class TargetQueryAutoCompleteExtensions {
    public static CommandAutoCompleteEntry ToStudio(this TargetQuery.AutoCompleteEntry entry) => new() {
        Name = entry.Name,
        Prefix = entry.Prefix,
        Suffix = entry.Suffix,
        Extra = entry.Extra,
        Suggestion = entry.Suggestion,
        IsDone = entry.IsDone,
        HasNext = entry.HasNext,
        StorageKey = entry.StorageKey,
        StorageName = entry.StorageName,
    };
}
