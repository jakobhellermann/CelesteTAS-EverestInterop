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

/// Provides improved runtime-reflection utilities
internal static class ReflectionExtensions {
    internal const BindingFlags InstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal const BindingFlags StaticAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    internal const BindingFlags StaticInstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal const BindingFlags InstanceAnyVisibilityDeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [UsedImplicitly]
    private readonly record struct MemberKey(Type Type, string Name);
    [UsedImplicitly]
    private readonly record struct MethodKey(Type Type, string Name, long ParameterHash);
    [UsedImplicitly]
    private readonly record struct ConstructorKey(Type Type, long ParameterHash);

    [UsedImplicitly]
    private readonly record struct AllMemberKey(Type Type, BindingFlags BindingFlags);

    private static readonly ConcurrentDictionary<MemberKey, MemberInfo?> CachedMemberInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, FieldInfo?> CachedFieldInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, PropertyInfo?> CachedPropertyInfos = new();
    private static readonly ConcurrentDictionary<MethodKey, MethodInfo?> CachedMethodInfos = new();
    private static readonly ConcurrentDictionary<ConstructorKey, ConstructorInfo?> CachedConstructorInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, EventInfo?> CachedEventInfos = new();

    private static readonly ConcurrentDictionary<MemberKey, MethodInfo?> CachedGetMethodInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, MethodInfo?> CachedSetMethodInfos = new();

    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<FieldInfo>> CachedAllFieldInfos = new();
    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<PropertyInfo>> CachedAllPropertyInfos = new();
    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<MethodInfo>> CachedAllMethodInfos = new();
    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<ConstructorInfo>> CachedAllConstructorInfos = new();

    /// Resolves the target member on the type, caching the result
    public static MemberInfo? GetMemberInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedMemberInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            result = currentType.GetMember(name, bindingFlags).FirstOrDefault();
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            $"Failed to find member '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedMemberInfos[key] = result;
    }

    /// Resolves the target field on the type, caching the result
    public static FieldInfo? GetFieldInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedFieldInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            result = currentType.GetField(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            $"Failed to find field '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedFieldInfos[key] = result;
    }

    /// Resolves the target property on the type, caching the result
    public static PropertyInfo? GetPropertyInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedPropertyInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            result = currentType.GetProperty(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            $"Failed to find property '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedPropertyInfos[key] = result;
    }

    /// Resolves the target method on the type, with the specific parameter types, caching the result
    public static MethodInfo? GetMethodInfo(this Type type, string name, Type?[]? parameterTypes = null, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MethodKey(type, name, parameterTypes.GetCustomHashCode());
        if (CachedMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            if (parameterTypes != null) {
                foreach (var method in currentType.GetAllMethodInfos(bindingFlags)) {
                    if (method.Name != name) {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length) {
                        continue;
                    }

                    for (int i = 0; i < parameters.Length; i++) {
                        // Treat a null type as a wild card
                        if (parameterTypes[i] != null && parameterTypes[i] != parameters[i].ParameterType) {
                            goto NextMethod;
                        }
                    }

                    if (result != null) {
                        // "Amphibious" matches on different types indicate overrides. Choose the "latest" method
                        if (result.DeclaringType != null && result.DeclaringType != method.DeclaringType) {
                            if (method.DeclaringType!.IsSubclassOf(result.DeclaringType)) {
                                result = method;
                            }
                        } else {
                            if (logFailure) {
                                $"Method '{name}' with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}' is ambiguous between '{result}' and '{method}'".Log(LogLevel.Error);
                            }
                            result = null;
                            break;
                        }
                    } else {
                        result = method;
                    }

                    NextMethod:;
                }
            } else {
                try {
                    result = currentType.GetMethod(name, bindingFlags);
                } catch (Exception ex) {
                    ex.LogException($"Failed to get method '{name}' on type '{type}'");
                    return CachedMethodInfos[key] = null;
                }
            }
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            if (parameterTypes == null) {
                $"Failed to find method '{name}' on type '{type}'".Log(LogLevel.Error);
            } else {
                $"Failed to find method '{name}' with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}'".Log(LogLevel.Error);
            }
        }

        return CachedMethodInfos[key] = result;
    }

    /// Resolves the target method on the type, with the specific parameter types, caching the result
    public static ConstructorInfo? GetConstructorInfo(this Type type, Type?[] parameterTypes, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new ConstructorKey(type, parameterTypes.GetCustomHashCode());
        if (CachedConstructorInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            foreach (var constructor in currentType.GetAllConstructorInfos(bindingFlags)) {

                var parameters = constructor.GetParameters();
                if (parameters.Length != parameterTypes.Length) {
                    continue;
                }

                for (int i = 0; i < parameters.Length; i++) {
                    // Treat a null type as a wild card
                    if (parameterTypes[i] != null && parameterTypes[i] != parameters[i].ParameterType) {
                        goto NextMethod;
                    }
                }

                if (result != null) {
                    // "Amphibious" matches on different types indicate overrides. Choose the "latest" method
                    if (result.DeclaringType != null && result.DeclaringType != constructor.DeclaringType) {
                        if (constructor.DeclaringType!.IsSubclassOf(result.DeclaringType)) {
                            result = constructor;
                        }
                    } else {
                        if (logFailure) {
                            $"Constructor with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}' is ambiguous between '{result}' and '{constructor}'".Log(LogLevel.Error);
                        }
                        result = null;
                        break;
                    }
                } else {
                    result = constructor;
                }

                NextMethod:;
            }
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            $"Failed to find constructor with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}'".Log(LogLevel.Error);
        }

        return CachedConstructorInfos[key] = result;
    }

    /// Resolves the target event on the type, with the specific parameter types, caching the result
    public static EventInfo? GetEventInfo(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedEventInfos.TryGetValue(key, out var result)) {
            return result;
        }

        var currentType = type;
        do {
            result = currentType.GetEvent(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null) {
            $"Failed to find event '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedEventInfos[key] = result;
    }

    /// Resolves the target get-method of the property on the type, caching the result
    public static MethodInfo? GetGetMethod(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedGetMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        result = type.GetPropertyInfo(name, bindingFlags)?.GetGetMethod(nonPublic: true);
        if (result == null) {
            $"Failed to find get-method of property '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedGetMethodInfos[key] = result;
    }

    /// Resolves the target set-method of the property on the type, caching the result
    public static MethodInfo? GetSetMethod(this Type type, string name, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedSetMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        result = type.GetPropertyInfo(name, bindingFlags)?.GetSetMethod(nonPublic: true);
        if (result == null) {
            $"Failed to find set-method of property '{name}' on type '{type}'".Log(LogLevel.Error);
        }

        return CachedSetMethodInfos[key] = result;
    }

    /// Resolves all fields of the type, caching the result
    public static IEnumerable<FieldInfo> GetAllFieldInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllFieldInfos.TryGetValue(key, out var result)) {
            return result;
        }

        HashSet<FieldInfo> allFields = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allFields.AddRange(currentType.GetFields(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllFieldInfos[key] = allFields;
    }

    /// Resolves all properties of the type, caching the result
    public static IEnumerable<PropertyInfo> GetAllPropertyInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllPropertyInfos.TryGetValue(key, out var result)) {
            return result;
        }

        HashSet<PropertyInfo> allProperties = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allProperties.AddRange(currentType.GetProperties(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllPropertyInfos[key] = allProperties;
    }

    /// Resolves all methods of the type, caching the result
    public static IEnumerable<MethodInfo> GetAllMethodInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllMethodInfos.TryGetValue(key, out var result)) {
            return result;
        }

        HashSet<MethodInfo> allMethods = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allMethods.AddRange(currentType.GetMethods(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllMethodInfos[key] = allMethods;
    }

    /// Resolves all constructors of the type, caching the result
    public static IEnumerable<ConstructorInfo> GetAllConstructorInfos(this Type type, BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllConstructorInfos.TryGetValue(key, out var result)) {
            return result;
        }

        return CachedAllConstructorInfos[key] = type.GetConstructors(bindingFlags);
    }

    /// Gets the value of the instance field on the object
    public static T? GetFieldValue<T>(this object obj, string name) {
        if (obj.GetType().GetFieldInfo(name, InstanceAnyVisibility) is not { } field) {
            return default;
        }
        var value = field.GetValue(obj);
        if (value is not T val) {
            throw new Exception($"GetFieldValue for {name}: Expected {typeof(T)} but got {value.GetType()}");
        }

        return val;
    }

    /// Gets the value of the static field on the type
    public static T? GetFieldValue<T>(this Type type, string name) {
        if (type.GetFieldInfo(name, StaticAnyVisibility) is not { } field) {
            return default;
        }

        return (T?) field.GetValue(null);
    }

    /// Sets the value of the instance field on the object
    public static void SetFieldValue(this object obj, string name, object? value) {
        if (obj.GetType().GetFieldInfo(name, InstanceAnyVisibility) is not { } field) {
            return;
        }

        field.SetValue(obj, value);
    }

    /// Sets the value of the static field on the type
    public static void SetFieldValue(this Type type, string name, object? value) {
        if (type.GetFieldInfo(name, StaticAnyVisibility) is not { } field) {
            return;
        }

        field.SetValue(null, value);
    }

    /// Gets the value of the instance property on the object
    public static T? GetPropertyValue<T>(this object obj, string name) {
        if (obj.GetType().GetPropertyInfo(name, InstanceAnyVisibility) is not { } property) {
            return default;
        }
        if (!property.CanRead) {
            $"Property '{name}' on type '{obj.GetType()}' is not readable".Log(LogLevel.Error);
            return default;
        }

        return (T?) property.GetValue(obj);
    }

    /// Gets the value of the static property on the type
    public static T? GetPropertyValue<T>(this Type type, string name) {
        if (type.GetPropertyInfo(name, StaticAnyVisibility) is not { } property) {
            return default;
        }
        if (!property.CanRead) {
            $"Property '{name}' on type '{type}' is not readable".Log(LogLevel.Error);
            return default;
        }

        return (T?) property.GetValue(null);
    }

    /// Sets the value of the instance property on the object
    public static void SetPropertyValue(this object obj, string name, object? value) {
        if (obj.GetType().GetPropertyInfo(name, InstanceAnyVisibility) is not { } property) {
            return;
        }
        if (!property.CanWrite) {
            $"Property '{name}' on type '{obj.GetType()}' is not writable".Log(LogLevel.Error);
            return;
        }

        property.SetValue(obj, value);
    }

    /// Sets the value of the static property on the type
    public static void SetPropertyValue(this Type type, string name, object? value) {
        if (type.GetPropertyInfo(name, StaticAnyVisibility) is not { } property) {
            return;
        }
        if (!property.CanWrite) {
            $"Property '{name}' on type '{type}' is not writable".Log(LogLevel.Error);
            return;
        }

        property.SetValue(null, value);
    }

    /// Invokes the instance method on the type
    public static void InvokeMethod(this object obj, string name, params object?[]? parameters) {
        if (obj.GetType().GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), InstanceAnyVisibility) is not { } method) {
            return;
        }

        method.Invoke(obj, parameters);
    }

    /// Invokes the static method on the type
    public static void InvokeMethod(this Type type, string name, params object?[]? parameters) {
        if (type.GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), StaticAnyVisibility) is not { } method) {
            return;
        }

        method.Invoke(null, parameters);
    }

    /// Invokes the instance method on the type, returning the result
    public static T? InvokeMethod<T>(this object obj, string name, params object?[]? parameters) {
        if (obj.GetType().GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), InstanceAnyVisibility) is not { } method) {
            return default;
        }

        return (T?) method.Invoke(obj, parameters);
    }

    /// Invokes the static method on the type, returning the result
    public static T? InvokeMethod<T>(this Type type, string name, params object?[]? parameters) {
        if (type.GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), StaticAnyVisibility) is not { } method) {
            return default;
        }

        return (T?) method.Invoke(null, parameters);
    }
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
