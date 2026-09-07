using System.Threading.Tasks;

namespace TAS.ModInterop;

internal interface ISavestateBackend {
    bool Installed { get; }
    bool MultipleSaveSlotsSupported { get; }

    bool Save(string slot);
    bool SaveToFile(string path);
    Task<bool> Load(string slot);
    Task<bool> LoadFromFile(string path);
    void Clear(string slot);
    bool IsSaved(string slot);
    string[] ListSlots();

    // With deferral, Load only restores the scene and holds the rest until ApplyPendingSnapshot; backends without
    // it restore everything inside Load and never report a pending snapshot
    bool DeferSnapshotRestore { set; }
    bool SnapshotPending { get; }
    void ApplyPendingSnapshot();

    (float Time, int FrameCount)? LastLoadedClock { get; }
}
