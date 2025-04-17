using System;
using BepInEx.Logging;
using TAS.Input;

namespace TAS;

public static class CelesteCompatExtensions {
    public static bool Has(this ExecuteTiming states, ExecuteTiming flag) => (states & flag) == flag;

    public static void LogException(this Exception e, string message) {
        TAS.Log.Error($"{message}: {e}");
    }

    public static void Log(this string message, LogLevel level = LogLevel.Info) {
        TAS.Log.LogMessage(message, level);
    }

    public static void Log(this Exception message, LogLevel level = LogLevel.Info) {
        TAS.Log.LogMessage(message, level);
    }

    public static string ReplaceLineEndings(this string text, string replacementText) =>
        text.Replace("\n", replacementText);


    public static LineEnumerator EnumerateLines(this ReadOnlySpan<char> span) => new(span);

    public ref struct LineEnumerator(ReadOnlySpan<char> span) {
        private ReadOnlySpan<char> remaining = span;

        public ReadOnlySpan<char> Current { get; private set; } = default;

        public bool MoveNext() {
            if (remaining.IsEmpty)
                return false;

            var index = 0;
            while (index < remaining.Length && remaining[index] != '\r' && remaining[index] != '\n')
                index++;

            Current = remaining.Slice(0, index);

            if (index < remaining.Length) {
                if (remaining[index] == '\r' && index + 1 < remaining.Length && remaining[index + 1] == '\n')
                    index++;

                index++;
            }

            remaining = remaining[index..];
            return true;
        }

        public LineEnumerator GetEnumerator() => this;
    }
}

public class UnreachableException : Exception;
