using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace TAS;

public static class NetStandardPolyfill {
#if !NETSTANDARD2_1_OR_GREATER
    public static bool StartsWith(this ReadOnlySpan<char> span, string prefix) {
        return span.StartsWith(prefix.AsSpan());
    }
    public static bool Contains(this ReadOnlySpan<char> span, string value, StringComparison comparison) {
        return span.Contains(value.AsSpan(), comparison);
    }
    
    public static void Write(this Stream stream, byte[] buffer) {
        stream.Write(buffer, 0, buffer.Length);
    }

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value) {
        key = kvp.Key;
        value = kvp.Value;
    }

    public static bool TryDequeue<T>(this Queue<T> queue, [NotNullWhen(true)] out T? value) where T : notnull {
        if (queue.Count == 0) {
            value = default;
            return false;
        } else {
            value = queue.Dequeue();
            return true;
        }
    }

    public static bool TryPop<T>(this Stack<T> queue, [NotNullWhen(true)] out T? value) where T : notnull {
        if (queue.Count == 0) {
            value = default;
            return false;
        } else {
            value = queue.Pop();
            return true;
        }
    }
    
    public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue) {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) {
        return dict.TryGetValue(key, out var value) ? value : default;
    }
    
    public static IEnumerable<T> SkipLast<T>(this IEnumerable<T> source, int n = 1) {
        using var it = source.GetEnumerator();
        bool hasRemainingItems;
        var cache = new Queue<T>(n + 1);
    
        do {
            hasRemainingItems = it.MoveNext();
            if (hasRemainingItems) {
                cache.Enqueue(it.Current);
                if (cache.Count > n)
                    yield return cache.Dequeue();
            }
        } while (hasRemainingItems);
    }
#endif
    
}
