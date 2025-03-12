using System;
using System.Reflection;
using TAS.Utils;
using UnityEngine;

namespace TAS.InfoHUD;

internal class MonobehaviourQueryHandler : TargetQuery.Handler {
    public override bool CanResolveInstances(Type type) => type.IsSameOrSubclassOf(typeof(MonoBehaviour));

    public override object[] ResolveInstances(Type type) {
        var entityInstances =
            UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);
        // ReSharper disable once CoVariantArrayConversion
        return entityInstances;
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
