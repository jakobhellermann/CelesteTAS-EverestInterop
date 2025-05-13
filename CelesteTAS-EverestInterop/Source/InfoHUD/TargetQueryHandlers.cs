using NineSolsAPI.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using TAS.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TAS.InfoHUD;

internal class MonobehaviourQueryHandler : FilterableQueryHandler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(MonoBehaviour));

    public override object[] ResolveInstances(Type type) {
        const FindObjectsInactive findDisabled = FindObjectsInactive.Include;
        var entityInstances = Object.FindObjectsByType(type, findDisabled, FindObjectsSortMode.InstanceID);
        // ReSharper disable once CoVariantArrayConversion
        return entityInstances;
    }
}

internal class CollectionQueryHandler : TargetQuery.Handler {
    /// Matches an index on a member
    /// e.g. `BaseType[Room:ID]`
    private static readonly Regex IndexRegex = new(@"^(.+)(?:\[(.+)\])$", RegexOptions.Compiled);

    private const string SpreadKey = "___SpreadCollection___";
    private const string IndexKey = "___Index___";

    public override IEnumerable<string> ProcessQueryArguments(IEnumerable<string> queryArgs, bool isAutoComplete) {
        foreach (string arg in queryArgs) {
            if (arg.EndsWith('*')) {
                string newArg = arg[..^1];

                if (IndexRegex.Match(newArg) is { Success: true} indexMatch) {
                    yield return indexMatch.Groups[1].Value;
                    yield return $"{IndexKey}{indexMatch.Groups[2].Value}";
                } else {
                    yield return newArg;
                }

                yield return SpreadKey;
            } else if (IndexRegex.Match(arg) is { Success: true} indexMatch) {
                if (indexMatch.Groups[1].Value.EndsWith('*')) {
                    yield return indexMatch.Groups[1].Value[..^1];
                    yield return SpreadKey;
                } else {
                    yield return indexMatch.Groups[1].Value;
                }

                yield return $"{IndexKey}{indexMatch.Groups[2].Value}";
            } else {
                yield return arg;
            }
        }
    }

    public override Result<bool, TargetQuery.MemberAccessError> ProcessValue(ref object?[] values, int valueIdx, object? value, Type currentType, ref int memberIdx, string[] memberArgs, bool needsFlush) {
        if (memberArgs.Length <= memberIdx + 1 || memberArgs[memberIdx + 1] != SpreadKey) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        bool success = needsFlush
            ? ProcessFlushableValue(ref values, valueIdx, value, currentType, memberIdx, memberArgs)
            : ProcessGetValue(ref values, valueIdx, value);

        memberIdx += 1; // Skip over SpreadKey member
        return Result<bool, TargetQuery.MemberAccessError>.Ok(success);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveTargetTypes(out Type[] targetTypes, Type type, ref int memberIdx, string[] memberArgs) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            targetTypes = null!;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        if (type.IsArray) {
            targetTypes = [type.GetElementType()!];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }
        if (typeof(IList).IsAssignableFrom(type)) {
            targetTypes = [type.GetElementType() ?? type.GenericTypeArguments[0]];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }
        if (typeof(IDictionary).IsAssignableFrom(type)) {
            targetTypes = [type.GenericTypeArguments[1]];
            return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
        }

        targetTypes = null!;
        return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
    }

    public override Result<bool, TargetQuery.MemberAccessError> ResolveMember(object? instance, out object? value, Type type, int memberIdx, string[] memberArgs) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        var keyType = typeof(int); // Default to an integer index
        if (typeof(IDictionary).IsAssignableFrom(type)) {
            keyType = type.GenericTypeArguments[0];
        }

        // TODO: This supports resolving target-queries, but the query-arg parsing logic does not..
        var keyResult = TargetQuery.ResolveValue([memberArgs[memberIdx][IndexKey.Length..]], [keyType]);
        if (keyResult.CheckFailure(out var error)) {
            value = null;
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, error.ToString()));
        }

        object key = keyResult.Value[0]!;
        switch (instance) {
            case IList list: {
                int index = (int) key;
                if (index < 0 || index >= list.Count) {
                    value = null;
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {list.Count - 1})"));
                }

                value = list[(int) key];
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            case IDictionary dict: {
                if (!dict.Contains(key)) {
                    value = null;
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot find key '{key}' in dictionary"));
                }

                value = dict[key];
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            default: {
                if (type.IsArray) {
                    int index = (int) key;
                    var array = (Array) instance!;
                    if (index < 0 || index >= array.Length) {
                        value = null;
                        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {array.Length - 1})"));
                    }

                    value = array.GetValue(index);
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;
            }
        }

        value = null;
        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot index type '{type}'"));
    }

    public override Result<bool, TargetQuery.MemberAccessError> SetMember(object? instance, object? value, Type type, int memberIdx, string[] memberArgs, bool forceAllowCodeExecution) {
        if (!memberArgs[memberIdx].StartsWith(IndexKey)) {
            return Result<bool, TargetQuery.MemberAccessError>.Ok(false);
        }

        var keyType = typeof(int); // Default to an integer index
        if (typeof(IDictionary).IsAssignableFrom(type)) {
            keyType = type.GenericTypeArguments[0];
        }

        var keyResult = TargetQuery.ResolveValue([memberArgs[memberIdx][IndexKey.Length..]], [keyType]);
        if (keyResult.CheckFailure(out var error)) {
            return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, error.ToString()));
        }

        object key = keyResult.Value[0]!;
        switch (instance) {
            case IList list: {
                int index = (int) key;
                if (index < 0 || index >= list.Count) {
                    return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {list.Count - 1})"));
                }

                list[(int) key] = value;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            case IDictionary dict: {
                dict[key] = value;
                return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
            }

            default: {
                if (type.IsArray) {
                    int index = (int) key;
                    var array = (Array) instance!;
                    if (index < 0 || index >= array.Length) {
                        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Index '{index}' is out-of-range (Expected >= 0 and <= {array.Length - 1})"));
                    }
                    array.SetValue(value, index);
                    return Result<bool, TargetQuery.MemberAccessError>.Ok(true);
                }
                break;
            }
        }

        return Result<bool, TargetQuery.MemberAccessError>.Fail(new TargetQuery.MemberAccessError.Custom(type, memberIdx, $"Cannot index type '{type}'"));
    }

    private static bool ProcessGetValue(ref object?[] values, int valueIdx, object? value) {
        if (value is System.Collections.ICollection collection) {
            switch (collection.Count) {
                case 0:
                    values[valueIdx] = TargetQuery.InvalidValue;
                    return true;

                case 1:
                    collection.CopyTo(values, valueIdx);
                    return true;

                default:
                    // Can only copy entire collection, so need to invalidate previous instance
                    values[valueIdx] = TargetQuery.InvalidValue;
                    int startIdx = values.Length;
                    Array.Resize(ref values, values.Length + collection.Count);
                    collection.CopyTo(values, startIdx);
                    return true;
            }
        }

        return false;
    }
    private static bool ProcessFlushableValue(ref object?[] values, int valueIdx, object? value, Type currentType, int memberIdx, string[] memberArgs) {
        switch (value) {
            case IList list:
                switch (list.Count) {
                    case 0:
                        values[valueIdx] = TargetQuery.InvalidValue;
                        return true;

                    case 1:
                        // Value types need a writable collection
                        if (list[0] != null && list[0]!.GetType().IsValueType) {
                            if (list.IsReadOnly) {
                                values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            } else {
                                if (values[valueIdx] is TargetQuery.BoxedValueHolder holder) {
                                    holder.ValueStack.Push(list[0]!);
                                } else {
                                    holder = new TargetQuery.BoxedValueHolder(list, 0, new(capacity: 1));
                                    holder.ValueStack.Push(list[0]!);
                                    values[valueIdx] = holder;
                                }
                            }
                        } else {
                            values[valueIdx] = list[0];
                        }
                        return true;

                    default:
                        // Can only copy entire collection, so need to invalidate previous instance
                        int startIdx = values.Length;
                        Array.Resize(ref values, values.Length + list.Count - 1);

                        // Value types need a writable collection
                        if (list[0] != null && list[0]!.GetType().IsValueType) {
                            if (list.IsReadOnly) {
                                values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            } else {
                                if (values[valueIdx] is TargetQuery.BoxedValueHolder holder) {
                                    holder.ValueStack.Push(list[0]!);
                                } else {
                                    holder = new TargetQuery.BoxedValueHolder(list, 0, new(capacity: 1));
                                    holder.ValueStack.Push(list[0]!);
                                    values[valueIdx] = holder;
                                }
                            }
                        } else {
                            values[valueIdx] = list[0];
                        }

                        for (int i = 1; i < list.Count; i++) {
                            if (list[i] != null && list[i]!.GetType().IsValueType) {
                                if (list.IsReadOnly) {
                                    values[startIdx + i - 1] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                                } else {
                                    if (values[startIdx + i - 1] is TargetQuery.BoxedValueHolder holder) {
                                        holder.ValueStack.Push(list[i]!);
                                    } else {
                                        holder = new TargetQuery.BoxedValueHolder(list, i, new(capacity: 1));
                                        holder.ValueStack.Push(list[i]!);
                                        values[startIdx + i - 1] = holder;
                                    }
                                }
                            } else {
                                values[startIdx + i - 1] = list[i];
                            }
                        }
                        return true;
                }

            case System.Collections.ICollection collection:
                switch (collection.Count) {
                    case 0:
                        values[valueIdx] = TargetQuery.InvalidValue;
                        return true;

                    case 1:
                        collection.CopyTo(values, valueIdx);

                        // Value types need a writable collection
                        if (values[valueIdx]?.GetType().IsValueType ?? false) {
                            values[valueIdx] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                        }
                        return true;

                    default:
                        // Can only copy entire collection, so need to invalidate previous instance
                        values[valueIdx] = TargetQuery.InvalidValue;
                        int startIdx = values.Length;
                        Array.Resize(ref values, values.Length + collection.Count);
                        collection.CopyTo(values, startIdx);

                        // Value types need a writable collection
                        for (int i = startIdx; i < values.Length; i++) {
                            if (values[i]?.GetType().IsValueType ?? false) {
                                values[i] = new TargetQuery.MemberAccessError.ReadOnlyCollection(currentType, memberIdx, memberArgs);
                            }
                        }
                        return true;
                }
        }

        return false;
    }
}

internal class FilterableQueryHandler : TargetQuery.Handler {
    private static readonly Regex FilterRegex = new(@"^(.+)(?:\[(.+)])(.*)$", RegexOptions.Compiled);

    private const string SpecialSeparator = "___";
    private const string EntityNameKey = "EntityFilter";

    public override (HashSet<Type> Types, string[] MemberArgs)? ResolveBaseTypes(string[] queryArgs) {
        if (queryArgs.Length == 0) return null;
        if (!queryArgs[0].Contains('[')) return null;

        var newQueryArgs = new List<string>(queryArgs.Length);
        if (FilterRegex.Match(queryArgs[0]) is { Success: true } match) {
            newQueryArgs.Add(match.Groups[1].Value);
            newQueryArgs.Add($"{EntityNameKey}{SpecialSeparator}{match.Groups[2]}");
        }

        newQueryArgs.AddRange(queryArgs[1..]);

        var baseTypes = TargetQuery.ParseGenericBaseTypes(newQueryArgs.ToArray(), out var memberArgs);
        return (baseTypes, memberArgs);
    }

    public override Result<bool, TargetQuery.QueryError> ResolveMemberValues(ref object?[] values, ref int memberIdx,
        string[] memberArgs) {
        string[] parts = memberArgs[memberIdx].Split(SpecialSeparator);
        if (parts.Length == 1) {
            return Result<bool, TargetQuery.QueryError>.Ok(false);
        }

        switch (parts[0]) {
            case EntityNameKey:
                var key = parts[1];

                for (var valueIdx = 0; valueIdx < values.Length; valueIdx++) {
                    if (values[valueIdx] is not Object value) {
                        values[valueIdx] = TargetQuery.InvalidValue;
                        continue;
                    }

                    if (!value.name.Contains(key)) {
                        values[valueIdx] = TargetQuery.InvalidValue;

                        Log.Info($"MonoBehaviour doesn't match '{key}'. Found {value.name}");
                    }
                }


                return Result<bool, TargetQuery.QueryError>.Ok(true);
        }

        return Result<bool, TargetQuery.QueryError>.Ok(false);
    }
}

internal class SingletonBehaviourResolver : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) {
        for (var ty = type; ty != null && ty != typeof(object); ty = ty.BaseType) {
            if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(SingletonBehaviour<>)) {
                return true;
            }
        }

        return false;
    }

    public override object[] ResolveInstances(Type type) {
        for (var ty = type.BaseType; ty != null; ty = ty.BaseType) {
            var field = ty.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) continue;

            return [field.GetValue(null)];
        }

        throw new Exception("Could not find `_instance` field on SingletonBehaviour");
    }
}
