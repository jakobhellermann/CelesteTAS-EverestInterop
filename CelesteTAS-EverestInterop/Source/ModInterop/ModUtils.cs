﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TAS.ModInterop;

internal static class ModUtils {
    // public static readonly Assembly VanillaAssembly = typeof(Player).Assembly;

    /// Returns all specified type from the given mod, if the mod is present
    /*public static Type? GetType(string modName, string fullTypeName) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            return null;
        }

        var type = asm.GetType(fullTypeName);
        if (type == null) {
            $"Failed to find type '{fullTypeName}' in assembly '{asm}' of mod '{modName}'".Log(LogLevel.Error);
            return null;
        }

        return type;
    }*/

    /// Returns all specified types from the given mod, if the mod is present
    /*public static IEnumerable<Type> GetTypes(string modName, params string[] fullTypeNames) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            yield break;
        }

        foreach (string fullTypeName in fullTypeNames) {
            var type = asm.GetType(fullTypeName);
            if (type == null) {
                $"Failed to find type '{fullTypeName}' in assembly '{asm}' of mod '{modName}'".Log(LogLevel.Error);
                continue;
            }

            yield return type;
        }
    }

    /// Returns the specified field from the given mod, if the mod is present
    public static FieldInfo? GetField(string modName, string fullTypeName, string fieldName) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            return null;
        }

        var type = asm.GetType(fullTypeName);
        if (type == null) {
            $"Failed to find type '{fullTypeName}' in assembly '{asm}' of mod '{modName}'".Log(LogLevel.Error);
            return null;
        }

        var field = type.GetFieldInfo(fieldName);
        if (field == null) {
            $"Failed to find field '{fieldName}' in type '{type}' of mod '{modName}'".Log(LogLevel.Error);
            return null;
        }

        return field;
    }

    /// Returns the specified method from the given mod, if the mod is present
    public static MethodInfo? GetMethod(string modName, string fullTypeName, string methodName, Type?[]? parameterTypes = null) {
        var asm = GetAssembly(modName);
        if (asm == null) {
            return null;
        }

        var type = asm.GetType(fullTypeName);
        if (type == null) {
            $"Failed to find type '{fullTypeName}' in assembly '{asm}' of mod '{modName}'".Log(LogLevel.Error);
            return null;
        }

        var method = type.GetMethodInfo(methodName, parameterTypes);
        if (method == null) {
            $"Failed to find method '{methodName}' in type '{type}' of mod '{modName}'".Log(LogLevel.Error);
            return null;
        }

        return method;
    }
    */
    
    public static Type[] GetTypes() {
        return new[] {
            typeof(HeroController).Assembly,
            typeof(UnityEngine.Random).Assembly,
        }.SelectMany(x => x.GetTypes()).ToArray();
    }

    /*public static EverestModule? GetModule(string modName) {
        return Everest.Modules.FirstOrDefault(module => module.Metadata?.Name == modName);
    }

    public static bool IsInstalled(string modName) {
        return GetModule(modName) != null;
    }

    public static Assembly? GetAssembly(string modName) {
        return GetModule(modName)?.GetType().Assembly;
    }*/
}
