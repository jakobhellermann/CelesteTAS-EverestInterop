using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using JetBrains.Annotations;
using System.Diagnostics.CodeAnalysis;
using StudioCommunication.Util;
using UnityEngine;

namespace TAS.Utils;

internal delegate TReturn GetDelegate<in TInstance, out TReturn>(TInstance instance);

internal static class FastReflection {
    // ReSharper disable UnusedMember.Local
    private record struct DelegateKey(Type Type, string Name, Type InstanceType, Type ReturnType) {
        public readonly Type Type = Type;
        public readonly string Name = Name;
        public readonly Type InstanceType = InstanceType;
        public readonly Type ReturnType = ReturnType;
    }
    // ReSharper restore UnusedMember.Local

    private static readonly ConcurrentDictionary<DelegateKey, Delegate> CachedFieldGetDelegates = new();

    private static GetDelegate<TInstance, TReturn>? CreateGetDelegateImpl<TInstance, TReturn>(Type type, string name) {
        FieldInfo? field = type.GetFieldInfo(name);
        if (field == null) {
            return null;
        }

        Type returnType = typeof(TReturn);
        Type fieldType = field.FieldType;
        if (!returnType.IsAssignableFrom(fieldType)) {
            throw new InvalidCastException($"{field.Name} is of type {fieldType}, it cannot be assigned to the type {returnType}.");
        }

        var key = new DelegateKey(type, name, typeof(TInstance), typeof(TReturn));
        if (CachedFieldGetDelegates.TryGetValue(key, out var result)) {
            return (GetDelegate<TInstance, TReturn>) result;
        }

        if (field.IsConst()) {
            TReturn value = (TReturn) field.GetValue(null)!;
            Func<TInstance, TReturn> func = _ => value;

            GetDelegate<TInstance, TReturn> getDelegate =
                (GetDelegate<TInstance, TReturn>) func.Method.CreateDelegate(typeof(GetDelegate<TInstance, TReturn>), func.Target);
            CachedFieldGetDelegates[key] = getDelegate;
            return getDelegate;
        }

        var method = new DynamicMethod($"{field} Getter", returnType, [typeof(TInstance)], field.DeclaringType!, true);
        var il = method.GetILGenerator();

        if (field.IsStatic) {
            il.Emit(OpCodes.Ldsfld, field);
        } else {
            il.Emit(OpCodes.Ldarg_0);
            if (field.DeclaringType!.IsValueType && !typeof(TInstance).IsValueType) {
                il.Emit(OpCodes.Unbox_Any, field.DeclaringType);
            }

            il.Emit(OpCodes.Ldfld, field);
        }

        if (fieldType.IsValueType && !returnType.IsValueType) {
            il.Emit(OpCodes.Box, fieldType);
        }

        il.Emit(OpCodes.Ret);

        result = CachedFieldGetDelegates[key] = method.CreateDelegate(typeof(GetDelegate<TInstance, TReturn>));
        return (GetDelegate<TInstance, TReturn>) result;
    }

    public static GetDelegate<TInstance, TResult>? CreateGetDelegate<TInstance, TResult>(this Type type, string fieldName) {
        return CreateGetDelegateImpl<TInstance, TResult>(type, fieldName);
    }

    public static GetDelegate<TInstance, TResult>? CreateGetDelegate<TInstance, TResult>(string fieldName) {
        return CreateGetDelegate<TInstance, TResult>(typeof(TInstance), fieldName);
    }
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

// TODO: ports of the Celeste-specific Info HUD formatting helpers, stubbed so InfoCustom compiles
internal static class InfoFormattingExtensions {
    public static string ToSimpleString(this Vector2 value, int decimals) =>
        $"{value.x.ToFormattedString(decimals)}, {value.y.ToFormattedString(decimals)}";
    public static int ToCeilingFrames(this float seconds) => (int) Math.Ceiling(seconds * 60f);
    public static string FormatFrames(this int frames) => frames.ToString();
}
