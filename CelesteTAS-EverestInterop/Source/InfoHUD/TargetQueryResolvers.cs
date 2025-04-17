using System;
using System.Collections.Generic;
using System.Linq;
using TAS.Utils;
using UnityEngine;

namespace TAS.InfoHUD;

internal class GlobalInstanceResolver<T>(Func<T> instanceProvider) : IInstanceResolver where T : notnull {
    public bool CanResolve(Type type) => type == typeof(T);

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) => [instanceProvider()];
}

internal class MonobehaviourInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(MonoBehaviour));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        var entityInstances =
            UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

        if (!componentTypes.IsEmpty()) {
            Log.Warn("componentTypes filter not supported");
        }

        return entityInstances.Select(e => (object) e).ToList();
    }
}
