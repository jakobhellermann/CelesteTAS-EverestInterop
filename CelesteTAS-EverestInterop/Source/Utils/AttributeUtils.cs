using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TAS.Utils;

public static class AttributeUtils {
    private static readonly Dictionary<Type, MethodInfo[]> attributeMethods = new();

    /// Gathers all static, parameterless methods with attribute T
    /// Only searches through CelesteTAS itself
    public static void CollectOwnMethods<T>() where T : Attribute {
        attributeMethods[typeof(T)] = typeof(TasMod).Assembly
            .GetTypes()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => info.GetCustomAttribute<T>() != null && info.GetParameters().Length == 0))
            .ToArray();
    }

    /// Gathers all static, parameterless methods with attribute T
    /// Only searches through all mods - Should only be called after Load()
    public static int CollectAllMethods<T>() where T : Attribute {
        var collected = typeof(TasMod).Assembly
            .GetTypes()
            .SelectMany(type => type
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            // .Where(info => info.GetCustomAttribute<T>() != null && info.GetParameters().Length == 0))
            .Where(info => info.GetCustomAttribute<T>() != null))
            .ToArray();
        attributeMethods[typeof(T)] = collected;
        return collected.Length;
    }

    /// Invokes all previously gathered methods for attribute T
    public static void Invoke<T>(object?[]? parameters = null) where T : Attribute {
        if (!attributeMethods.TryGetValue(typeof(T), out var methods)) {
            Log.Error($"Tried to call AttributeUtils.Invoke without collecting first: {typeof(T).Name}");
            return;
        }

        foreach (var method in methods) {
            try {
                method.Invoke(null, parameters);
            } catch (Exception e) {
                e.LogException($"Error invoking method {method.DeclaringType?.Name}::{method.Name}");
            }
        }
    }
}
