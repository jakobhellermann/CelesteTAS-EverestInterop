using System;
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
