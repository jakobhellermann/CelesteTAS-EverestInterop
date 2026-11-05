using System.Threading.Tasks;

namespace TAS.ModInterop;

internal class PreciseSavestatesBackend : ISavestateBackend {
    [Initialize]
    private static void Register() => SpeedrunToolInterop.Backend = new PreciseSavestatesBackend();

    private const string Layer = "CelesteTAS";

    private static PreciseSavestatesInterop Plugin => PreciseSavestatesInterop.Instance ?? throw new System.InvalidOperationException("PreciseSavestates not loaded");

    public bool Installed => PreciseSavestatesInterop.Installed;
    public bool MultipleSaveSlotsSupported => false;

    // PreciseSavestates' SavestateStore parses the slot out of the filename by splitting on the first '-', so a
    // slot containing '-' wouldn't round-trip — and our breakpoint slots are "CelesteTAS_<checksum>" with a
    // possibly-negative checksum (e.g. "CelesteTAS_-1569144177"). Map '-' to '_' consistently everywhere.
    private static string SlotFile(string slot) => slot.Replace('-', '_');

    public bool Save(string slot) {
        var s = SlotFile(slot);
        Tracer.TasTracer.TraceEvent($"Savestate save: {s}");
        return Plugin.CreateSavestate(s, s, Layer);
    }

    public bool SaveToFile(string path) => Plugin.CreateSavestateToFile(path);

    public Task<bool> Load(string slot) => Plugin.LoadSavestate(SlotFile(slot), Layer);

    public Task<bool> LoadFromFile(string path) => Plugin.LoadSavestateFromFile(path);

    public void Clear(string slot) => Plugin.DeleteSavestate(SlotFile(slot), Layer);

    public bool IsSaved(string slot) => Plugin.HasSavestate(SlotFile(slot), Layer);

    public string[] ListSlots() => Plugin.ListSlots(Layer);

    public bool DeferSnapshotRestore {
        set => Plugin.DeferSnapshotRestore = value;
    }

    public bool SnapshotPending => Plugin.SnapshotPending;

    public void ApplyPendingSnapshot() => Plugin.ApplyPendingSnapshot();

    public void RebaseAbsoluteTimes(double delta) => Plugin.RebaseAbsoluteTimes(delta);

    public (float Time, int FrameCount)? LastLoadedClock =>
        Plugin.LastLoadedGameTime is { } time ? (time, Plugin.LastLoadedFrameCount ?? 0) : null;
}
