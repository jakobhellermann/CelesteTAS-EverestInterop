using NineSolsAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TAS.Utils;
using UnityEngine;

namespace TAS.InfoHUD;

internal class GlobalInstanceResolver<T>(Func<T> instanceProvider) : IInstanceResolver where T : notnull {
    public bool CanResolve(Type type) => type == typeof(T);

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) => [instanceProvider()];
}

internal class SingletonBehaviourResolver : IInstanceResolver {
    public bool CanResolve(Type type) {
        for(var ty = type; ty != null && ty != typeof(object); ty = ty.BaseType) {
            if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(SingletonBehaviour<>)) {
                return true;
            }
        }

        return false;
    }

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        for (var ty = type.BaseType; ty != null; ty = ty.BaseType) {
            var field = ty.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) continue;
            return [field.GetValue(null)];
        }

        throw new Exception($"Could not find `_instance` field on SingletonBehaviour");
    }
}

internal class MonobehaviourInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(MonoBehaviour));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        var entityInstances =
            UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

        if (!componentTypes.IsEmpty()) {
            ToastManager.Toast("componentTypes filter not supported");
        }

        return entityInstances.Select(e => (object) e).ToList();
    }
}
