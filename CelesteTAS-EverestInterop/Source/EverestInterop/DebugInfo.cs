using System;
using System.Collections.Generic;
using System.Linq;

namespace TAS;

public static class DebugInfo {
    private const TimeDisplayMode TimeDisplay = TimeDisplayMode.Frames;

    [Flags]
    public enum DebugFilter {
        Base = 0,
        All = Base,
    }

    public static string GetInfoText(DebugFilter filter = DebugFilter.Base) {
        var text = "";
        text += $"Pos:   {"todo"}\n";
        text += $"Vel:   {"todo"}\n";
        text += $"State: {"todo"}\n";
        text += $"HP:    {"todo"}\n";

        List<(bool, string)> flags = [];
        List<(float, string)> timers = [];
        List<(object?, string)> values = [];
        text += "Flags: " + Flags(flags) + "\n";
        text += "Timers: " + Timers(timers) + "\n";
        text += "Values: " + Values(values) + "\n";


        return text;
    }

    private static string Flags(IEnumerable<(bool, string)> flags) {
        var text = "";
        foreach (var (val, name) in flags) {
            if (!val) continue;
            text += $"{name} ";
        }

        return text;
    }

    private static string Values(IEnumerable<(object?, string)> values) {
        var text = "";
        foreach (var (val, name) in values) {
            if (val is null or "" or 0) continue;
            text += $"{name}={val} ";
        }

        return text;
    }

    private static string Timers(IEnumerable<(float, string)> timers) {
        var text = "";
        foreach (var (timer, name) in timers) {
            if (timer <= 0) continue;
            text += $"{name}({FormatTime(timer)})";
            text += " ";
        }

        return text;
    }

    private static string RoundUpTimeToFrames(float time) {
        if (float.IsInfinity(time)) {
            return "Inf";
        }

        var frames = time * InputHelper.CurrentTasFramerate;
        var rounded = Math.Round(frames, 4);
        return ((int)Math.Ceiling(rounded)).ToString();
    }

    private enum TimeDisplayMode {
        Frames,
        Time,
    }

    private static string FormatTime(float time) =>
        TimeDisplay == TimeDisplayMode.Time ? $"{time:0.000}" : RoundUpTimeToFrames(time);

    private static string FormatTimeMaybe(float time) => time > 0 ? $"{FormatTime(time)} " : "";
}
