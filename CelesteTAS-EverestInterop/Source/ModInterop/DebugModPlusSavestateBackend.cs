using System;
using System.Linq;
using System.Threading.Tasks;

namespace TAS.ModInterop;

internal class DebugModPlusSavestateBackend(DebugModPlusInteropGlue debugModPlus) : ISavestateBackend {
    private const string Layer = "tas";
    private const string SlotPrefix = "tas";
    private const SavestateFilter TasSavestateFilter = SavestateFilter.Player | SavestateFilter.Monsters;

    public bool Installed => true;
    public bool MultipleSaveSlotsSupported => false;

    public bool Save(string slot) {
        debugModPlus.CreateSavestateDisk(SlotPrefix + slot, Layer, TasSavestateFilter);
        return true;
    }

    public bool SaveToFile(string path) => throw new NotSupportedException("DebugModPlus savestates can't be saved to a file path");

    public Task<bool> Load(string slot) {
        debugModPlus.LoadSavestateDisk(SlotPrefix + slot, Layer);
        return Task.FromResult(true);
    }

    public Task<bool> LoadFromFile(string path) => throw new NotSupportedException("DebugModPlus savestates can't be loaded from a file path");

    public void Clear(string slot) { }

    public bool IsSaved(string slot) => debugModPlus.ListSavestates(Layer).Contains(SlotPrefix + slot);

    public string[] ListSlots() => debugModPlus.ListSavestates(Layer)
        .Where(name => name.StartsWith(SlotPrefix))
        .Select(name => name[SlotPrefix.Length..])
        .ToArray();

    public bool DeferSnapshotRestore {
        set { }
    }

    public bool SnapshotPending => false;

    public void ApplyPendingSnapshot() { }

    public (float Time, int FrameCount)? LastLoadedClock => null;
}
