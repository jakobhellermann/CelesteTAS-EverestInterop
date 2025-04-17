// ReSharper disable all

using Microsoft.Xna.Framework.Input;
using MonoMod.ModInterop;
using StudioCommunication.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using TAS.Input.Commands;

#pragma warning disable CS8321 // Local function is declared but never used
namespace TAS.ModInterop;

/// Mod-Interop with Speedrun Tool
internal static class SpeedrunToolInterop {
    public static bool Installed { get; private set; }
    public static bool MultipleSaveSlotsSupported { get; private set; }

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
