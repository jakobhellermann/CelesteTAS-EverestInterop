using System;
using System.Collections.Generic;
using BepInEx;
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

/// Mod-Interop with Speedrun Tool
internal static class SpeedrunToolInterop {
    public const bool MultipleSaveSlotsSupported = false; // TODO

    // Can't be resolved during [Initialize] if loaded through ScriptEngine.
    private static BaseUnityPlugin? Plugin => plugin ??=  ModUtils.GetPlugin(ModUtils.PreciseSavestatesId);
    private static BaseUnityPlugin? plugin;


    public static bool Installed => PreciseSavestatesInterop.Installed;
    private static PreciseSavestatesInterop AssertPlugin => PreciseSavestatesInterop.Instance ?? throw new InvalidOperationException("PreciseSavestates not loaded") ;

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

    // PreciseSavestates' SavestateStore parses the slot out of the filename by splitting on the first '-', so a
    // slot containing '-' wouldn't round-trip — and our breakpoint slots are "CelesteTAS_<checksum>" with a
    // possibly-negative checksum (e.g. "CelesteTAS_-1569144177"). Map '-' to '_' consistently everywhere.
    private static string SlotFile(string? slot) => (slot ?? DefaultSlot).Replace('-', '_');

    /// Saves the current state into the specified slot. Returns whether it was successful.
    /// PreciseSavestates captures the snapshot synchronously.
    public static bool SaveState(string? slot = null) {
        var s = SlotFile(slot);
        Tracer.TasTracer.TraceEvent($"Savestate save: {s}");
        return AssertPlugin.CreateSavestate(s, s, SavestateLoad.Layer);
    }

    /// Starts loading the specified slot. The PreciseSavestates load is asynchronous (scene transition), so this
    /// only kicks it off via <see cref="SavestateLoad"/> (which sets IsLoading to pause playback); the caller
    /// (<see cref="Playback.SavestateManager"/>) finishes the resume once the load completes. Returns whether a
    /// savestate exists in the slot.
    public static bool LoadState(string? slot = null) {
        if (!IsSaved(slot)) {
            return false;
        }

        SavestateLoad.Load(SlotFile(slot), "Savestate");
        return true;
    }

    /// Clears the specified save slot
    public static void ClearState(string? slot = null) {
        AssertPlugin.DeleteSavestate(SlotFile(slot), SavestateLoad.Layer);
    }

    /// Checks if something is saved in the specified save slot
    public static bool IsSaved(string? slot = null) {
        return AssertPlugin.HasSavestate(SlotFile(slot), SavestateLoad.Layer);
    }
}
