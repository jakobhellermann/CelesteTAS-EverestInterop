using System;

namespace StudioCommunication;

/// A parsed line representing a fast-forward point in the TAS
public record struct FastForwardLine(bool ForceStop, bool SaveState, string SpeedText, float? PlaybackSpeed) {

    public static FastForwardLine? Parse(ReadOnlySpan<char> line) => TryParse(line, out var fastForwardLine) ? fastForwardLine : null;
    public static bool TryParse(ReadOnlySpan<char> line, out FastForwardLine fastForwardLine) {
        fastForwardLine = default;

        var lineTrimmed = line.TrimStart();
        if (!lineTrimmed.StartsWith("***".AsSpan())) {
            return false;
        }

        var modifiers = lineTrimmed.Slice("***".Length);
        if (modifiers.StartsWith("!".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
            fastForwardLine.ForceStop = true;
            modifiers = modifiers.Slice("!".Length);
        } else {
            fastForwardLine.ForceStop = false;
        }

        if (modifiers.StartsWith("S".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
            fastForwardLine.SaveState = true;
            modifiers = modifiers.Slice("S".Length);
        } else {
            fastForwardLine.SaveState = false;
        }

        fastForwardLine.SpeedText = modifiers.ToString();
        fastForwardLine.PlaybackSpeed = float.TryParse(fastForwardLine.SpeedText, out float x) ? x : null;
        return true;
    }

    public override string ToString() {
        return $"***{(ForceStop ? "!" : "")}{(SaveState ? "S" : "")}{SpeedText}";
    }
    public string Format() {
        return $"***{(ForceStop ? "!" : "")}{(SaveState ? "S" : "")}{(PlaybackSpeed != null ? PlaybackSpeed.Value : "")}";
    }
}
