using BepInEx.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TAS.Playback;

/// Popup toast message to be displayed in the bottom-left of the screen
internal static class PopupToast {
    private const int Padding = 25;
    public const float DefaultDuration = 3.0f;

    public class Entry(string text, float timeout, Color color) {
        public string Text = text;
        public Color Color = color;

        public float Timeout = timeout;

        public float Fade = 0.0f;
        public bool Active => entries.Contains(this);
    }

    private static readonly List<Entry> entries = [];

    public static Entry Show(string message, float timeout = DefaultDuration) {
        var entry = new Entry(message, timeout, Color.white);
        Show(entry);
        return entry;
    }
    public static Entry ShowWithColor(string message, Color color, float timeout = DefaultDuration) {
        var entry = new Entry(message, timeout, color);
        Show(entry);
        return entry;
    }
    public static Entry ShowAndLog(string message, float timeout = DefaultDuration, LogLevel level = LogLevel.Warning) {
        foreach (var line in message.AsSpan().EnumerateLines()) {
            line.ToString().Log(level);
        }

        return ShowWithColor(message, level switch {
            LogLevel.Message => Color.magenta,
            LogLevel.Debug => Color.blue,
            LogLevel.Info => Color.white,
            LogLevel.Warning => Color.yellow,
            LogLevel.Error => Color.red,
            LogLevel.Fatal => Color.red,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        }, timeout);
    }

    public static void Show(Entry entry) {
        Log.Error(entry.Text);
        Log.Toast(entry.Text);
        if (Manager.Running) {
            shownDuringRun.Add((entry.Text, Time.time));
        }
        entries.Add(entry);
    }

    /// NineSolsAPI's toast lifetime, which it measures in `Time.time`
    private const float ToastLifetime = 5.0f;

    /// Toasts age by `Time.time`, which follows the TAS clock during a run and jumps back to the
    /// game clock afterwards, so toasts from the run would vanish as soon as it ends.
    private static readonly List<(string Text, float Time)> shownDuringRun = [];

    [UpdateMeta]
    private static void ReshowRunToasts() {
        if (Manager.Running) {
            shownDuringRun.RemoveAll(toast => Time.time - toast.Time > ToastLifetime);
            return;
        }

        foreach (var (text, _) in shownDuringRun) {
            Log.Toast(text);
        }
        shownDuringRun.Clear();
    }
}
