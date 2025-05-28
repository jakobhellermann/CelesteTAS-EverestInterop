
using MessagePack;

namespace StudioCommunication;

[MessagePackObject(keyAsPropertyName: true)]
public record struct LevelInfo {
    public int? WakeupTime;
    public string ModUrl;
}
