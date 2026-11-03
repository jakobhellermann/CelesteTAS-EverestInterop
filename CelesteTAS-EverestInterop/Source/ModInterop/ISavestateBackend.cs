namespace TAS.ModInterop;

internal interface ISavestateBackend {
    bool Installed { get; }
    bool MultipleSaveSlotsSupported { get; }

    bool Save(string slot);
    bool Load(string slot);
    void Clear(string slot);
    bool IsSaved(string slot);
}
