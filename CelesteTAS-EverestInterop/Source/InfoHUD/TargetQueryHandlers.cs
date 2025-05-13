using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TAS.InfoHUD;

internal class ScriptableObjectHandler : FilterableQueryHandler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(ScriptableObject));

    public override object[] ResolveInstances(Type type) {
        var resources = Resources.FindObjectsOfTypeAll(type);
        // ReSharper disable once CoVariantArrayConversion
        return resources;
    }
}

internal class MonobehaviourQueryHandler : FilterableQueryHandler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(MonoBehaviour));

    public override object[] ResolveInstances(Type type) {
        const FindObjectsInactive findDisabled = FindObjectsInactive.Include;
        var entityInstances = Object.FindObjectsByType(type, findDisabled, FindObjectsSortMode.InstanceID);
        // ReSharper disable once CoVariantArrayConversion
        return entityInstances;
    }
}

abstract internal class FilterableQueryHandler : TargetQuery.Handler {
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
