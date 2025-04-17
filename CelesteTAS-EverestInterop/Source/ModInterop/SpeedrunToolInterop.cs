using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TAS.Utils;

#pragma warning disable CS8321 // Local function is declared but never used
namespace TAS.ModInterop;

/// Invoked with a <c>Dictionary&lt;string, object&gt;</c> to which relevant data should be saved.
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class SaveStateAttribute(int priority = 0) : EventAttribute(priority);

/// Invoked with a <c>Dictionary&lt;string, object&gt;</c> from which previously saved data should be retrieved.
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class LoadStateAttribute(int priority = 0) : EventAttribute(priority);

/// Invoked when savestate data is cleared
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class ClearStateAttribute(int priority = 0) : EventAttribute(priority);

/// Mod-Interop with Speedrun Tool
internal static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }
    public static bool MultipleSaveSlotsSupported { get; private set; }

    private static object saveLoadHandle = null!;

    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectOwnMethods<SaveStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<LoadStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<ClearStateAttribute>();

        Installed = false;
    }
    
    [Unload]
    private static void Unload() {
    }

    [DisableRun]
    private static void OnTasDisableRun() {
    }

    public const string DefaultSlot = "CelesteTAS";

    /// Saves the current state into the specified slot. Returns whether it was successful
    public static bool SaveState(string? slot = null) => throw new NotImplementedException();

    /// Loads the specified slot into the current state. Returns whether it was successful
    public static bool LoadState(string? slot = null) => throw new NotImplementedException();

    /// Clears the specified save slot
    public static void ClearState(string? slot = null) => throw new NotImplementedException();

    /// Checks if something is saved in the specified save slot
    public static bool IsSaved(string? slot = null) => throw new NotImplementedException();
}
