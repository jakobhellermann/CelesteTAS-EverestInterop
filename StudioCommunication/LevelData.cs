using MessagePack;

namespace StudioCommunication;

[MessagePackObject(keyAsPropertyName: true)]
public record struct LevelInfo {
    public string ModUrl;

    /// Amount of frames which the intro animation takes, if it could be figured out
    public int? IntroTime;
    /// Name of the starting room without the lvl_ prefix
    public string StartingRoom;
}
