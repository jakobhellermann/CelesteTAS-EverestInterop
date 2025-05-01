using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NineSolsAPI.Utils;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace TAS.Utils;

/// Provides improved runtime-reflection utilities
internal static class ReflectionExtensions {
    internal const BindingFlags InstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal const BindingFlags StaticAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    internal const BindingFlags StaticInstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal const BindingFlags InstanceAnyVisibilityDeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

}

internal static class HashCodeExtensions {
    public static long GetCustomHashCode<T>(this IEnumerable<T>? enumerable) {
        if (enumerable == null) {
            return 0;
        }

        unchecked {
            long hash = 17;
            foreach (var item in enumerable) {
                hash = hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(item!);
            }

            return hash;
        }
    }

    public static HashCode Append<T>(this HashCode hash, T value) {
        hash.Add(value);
        return hash;
    }
}

internal static class TypeExtensions {
    public static bool IsSameOrSubclassOf(this Type potentialDescendant, Type potentialBase) {
        return potentialDescendant == potentialBase || potentialDescendant.IsSubclassOf(potentialBase);
    }

    public static bool IsSameOrSubclassOf(this Type potentialDescendant, params Type[] potentialBases) {
        return potentialBases.Any(potentialDescendant.IsSameOrSubclassOf);
    }

    public static bool IsSimpleType(this Type type) {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Vector2);
    }

    public static bool IsStructType(this Type type) {
        return type.IsValueType && !type.IsEnum && !type.IsPrimitive && !type.IsEquivalentTo(typeof(decimal));
    }

    public static bool IsConst(this FieldInfo fieldInfo) {
        return fieldInfo.IsLiteral && !fieldInfo.IsInitOnly;
    }

    /// Checks if the current type could be implicitly converted to the target type
    public static bool CanCoerceTo(this Type type, Type target) {
        // Trivial case
        if (target.IsAssignableFrom(type)) {
            return true;
        }

        // Implicit conversion operators
        foreach (var method in type.GetAllMethodInfos(ReflectionExtensions.StaticAnyVisibility).Concat(target.GetAllMethodInfos(ReflectionExtensions.StaticAnyVisibility))) {
            if (method.Name == "op_Implicit" &&
                target.IsAssignableFrom(method.ReturnType) &&
                method.GetParameters() is { Length: 1 } parameters &&
                parameters[0].ParameterType.IsAssignableFrom(type)
            ) {
                return true;
            }
        }

        return false;
    }

    /// Implicitly converts the current object to the target
    public static Result<object?, string> CoerceTo(this object? obj, Type target) {
        if (obj == null) {
            return target.IsValueType
                ? Result<object?, string>.Ok(null)
                : Result<object?, string>.Fail($"Cannot coerce null into a value type '{target}'");
        }

        var type = obj.GetType();

        // Trivial case
        if (target.IsAssignableFrom(type)) {
            return Result<object?, string>.Ok(obj);
        }

        // Implicit conversion operators
        foreach (var method in type.GetAllMethodInfos(ReflectionExtensions.StaticAnyVisibility).Concat(target.GetAllMethodInfos(ReflectionExtensions.StaticAnyVisibility))) {
            if (method.Name == "op_Implicit" &&
                target.IsAssignableFrom(method.ReturnType) &&
                method.GetParameters() is { Length: 1 } parameters &&
                parameters[0].ParameterType.IsAssignableFrom(type)
            ) {
                return Result<object?, string>.Ok(method.Invoke(null, [obj]));
            }
        }

        return Result<object?, string>.Fail($"Cannot coerce value of type '{type}' into '{target}'");
    }
}

internal static class PropertyInfoExtensions {
    public static bool IsStatic(this PropertyInfo source, bool nonPublic = true)
        => source.GetAccessors(nonPublic).Any(x => x.IsStatic);
}

internal static class CommonExtensions {
    public static T Apply<T>(this T obj, Action<T> action) {
        action(obj);
        return obj;
    }
}

// https://github.com/NoelFB/Foster/blob/main/Framework/Extensions/EnumExt.cs
internal static class EnumExtensions {
    /// Enum.HasFlag boxes the value, whereas this method does not
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool Has<TEnum>(this TEnum lhs, TEnum rhs) where TEnum : unmanaged, Enum {
        return sizeof(TEnum) switch {
            1 => (*(byte*) &lhs & *(byte*) &rhs) > 0,
            2 => (*(ushort*) &lhs & *(ushort*) &rhs) > 0,
            4 => (*(uint*) &lhs & *(uint*) &rhs) > 0,
            8 => (*(ulong*) &lhs & *(ulong*) &rhs) > 0,
            _ => throw new Exception("Size does not match a known Enum backing type."),
        };
    }
}

internal static class StringExtensions {
    private static readonly Regex LineBreakRegex = new(@"\r\n?|\n", RegexOptions.Compiled);

    public static string ReplaceLineBreak(this string text, string replacement) {
        return LineBreakRegex.Replace(text, replacement);
    }

    public static bool IsNullOrEmpty(this string? text) {
        return string.IsNullOrEmpty(text);
    }

    public static bool IsNotNullOrEmpty([NotNullWhen(true)] this string? text) {
        return !string.IsNullOrEmpty(text);
    }

    public static bool IsNullOrWhiteSpace(this string text) {
        return string.IsNullOrWhiteSpace(text);
    }

    public static bool IsNotNullOrWhiteSpace(this string text) {
        return !string.IsNullOrWhiteSpace(text);
    }
}

internal static class EnumerableExtensions {
    public static bool IsEmpty<T>(this IEnumerable<T> enumerable) {
        return !enumerable.Any();
    }

    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? enumerable) {
        return enumerable == null || !enumerable.Any();
    }

    public static bool IsNotEmpty<T>(this IEnumerable<T> enumerable) {
        return !enumerable.IsEmpty();
    }

    public static bool IsNotNullOrEmpty<T>(this IEnumerable<T> enumerable) {
        return !enumerable.IsNullOrEmpty();
    }

    /// Checks if the first sequence starts with the second sequence
    public static bool SequenceStartsWith<T>(this IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer = null) {
        // Optimize for certain cases
        if (first is ICollection<T> firstCollection && second is ICollection<T> secondCollection) {
            if (firstCollection.Count < secondCollection.Count) {
                return false;
            }

#if NET5_0_OR_GREATER
            if (first is T[] firstArray && second is T[] secondArray) {
                int count = secondArray.Length;
                return ((ReadOnlySpan<T>)firstArray)[..count].SequenceEqual((ReadOnlySpan<T>) secondArray, comparer);
            }
#endif

            if (first is IList<T> firstList && second is IList<T> secondList) {
                comparer ??= EqualityComparer<T>.Default;

                int count = secondList.Count;
                for (int i = 0; i < count; ++i) {
                    if (!comparer.Equals(firstList[i], secondList[i])) {
                        return false;
                    }
                }
                return true;
            }
        }

        // Generic case
        comparer ??= EqualityComparer<T>.Default;

        using var firstEnumerator = first.GetEnumerator();
        using var secondEnumerator = second.GetEnumerator();

        while (secondEnumerator.MoveNext()) {
            if (!firstEnumerator.MoveNext() || !comparer.Equals(firstEnumerator.Current, secondEnumerator.Current)) {
                return false;
            }
        }

        return true;
    }
}

internal static class CollectionExtension {
    /// Adds all items from the collection to the HashSet
    public static void AddRange<T>(this HashSet<T> hashSet, params T[] items) {
        foreach (var item in items) {
            hashSet.Add(item);
        }
    }
}

internal static class ListExtensions {
    public static T? GetValueOrDefault<T>(this IList<T> list, int index, T? defaultValue = default) {
        return index >= 0 && index < list.Count ? list[index] : defaultValue;
    }
}

internal static class EnumerableExtension {
    /// Iterates each entry of the IEnumerable and invokes the callback Action
    public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action) {
        foreach (var item in enumerable) {
            action(item);
        }
    }

    /// Returns the first matching element; otherwise null
    public static T? FirstOrNull<T>(this IEnumerable<T> enumerable) where T : struct {
        using var enumerator = enumerable.GetEnumerator();
        if (enumerator.MoveNext()) {
            return enumerator.Current;
        }

        return null;
    }
    /// Returns the first matching element; otherwise null
    public static T? FirstOrNull<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate) where T : struct {
        foreach (var item in enumerable) {
            if (predicate(item)) {
                return item;
            }
        }

        return null;
    }

    private readonly struct DynamicComparer<T>(Func<T, T, int> compare) : IComparer<T> {
        public int Compare(T? x, T? y) => compare(x!, y!);
    }

    /*
    /// Sorts the elements according to the comparision function
    public static IEnumerable<T> Sort<T>(this IEnumerable<T> enumerable, Func<T, T, int> compare) {
        return enumerable.Order(new DynamicComparer<T>(compare));
    }*/
}
