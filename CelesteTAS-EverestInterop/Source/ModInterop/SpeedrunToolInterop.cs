global using SavestateData = System.Collections.Generic.Dictionary<string, object?>;

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TAS.Input.Commands;
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

internal static class SpeedrunToolInterop {
    public static ISavestateBackend? Backend;

    public static bool Installed => Backend?.Installed ?? false;
    public static bool MultipleSaveSlotsSupported => Backend?.MultipleSaveSlotsSupported ?? false;

    [Initialize]
    private static void Initialize() {
        AttributeUtils.CollectOwnMethods<SaveStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<LoadStateAttribute>(typeof(Dictionary<string, object?>));
        AttributeUtils.CollectOwnMethods<ClearStateAttribute>();
    }
    
    [Unload]
    private static void Unload() {
    }

    [DisableRun]
    private static void OnTasDisableRun() {
    }

    public const string DefaultSlot = "CelesteTAS";

    private static ISavestateBackend AssertBackend => Backend ?? throw new InvalidOperationException("No savestate backend is registered");

    /// Saves the current state into the specified slot. Returns whether it was successful
    public static bool SaveState(string? slot = null) => AssertBackend.Save(slot ?? DefaultSlot);

    /// Starts loading the specified slot, see <see cref="SavestateLoad"/>. Returns whether the slot holds a savestate
    public static bool LoadState(string? slot = null) {
        if (!IsSaved(slot)) {
            return false;
        }

        SavestateLoad.Load(slot ?? DefaultSlot, "Savestate");
        return true;
    }

    /// Clears the specified save slot
    public static void ClearState(string? slot = null) => AssertBackend.Clear(slot ?? DefaultSlot);

    /// Checks if something is saved in the specified save slot
    public static bool IsSaved(string? slot = null) => AssertBackend.IsSaved(slot ?? DefaultSlot);
}
