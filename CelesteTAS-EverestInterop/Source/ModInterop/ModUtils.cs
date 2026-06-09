using BepInEx;
using BepInEx.Bootstrap;
using System;
using System.Linq;
using System.Reflection;
using Random = UnityEngine.Random;

namespace TAS.ModInterop;

internal static class ModUtils {
    private static readonly Assembly VanillaAssembly = typeof(HeroController).Assembly;

    public static Type[] GetTypes() {
        return new[] {
            VanillaAssembly,
            typeof(Random).Assembly
        }.Concat(
            Chainloader.PluginInfos.Values
                .Select(pluginInfo => pluginInfo.Instance)
                .Where(instance => instance != null)
                .Select(instance => instance.GetType().Assembly)
        ).Distinct().SelectMany(x => x.GetTypes()).ToArray();
    }
    
    internal static BaseUnityPlugin? GetPlugin(string id, Version? minVersion = null) {
        if (Chainloader.PluginInfos.TryGetValue(id, out var info)) {
            return minVersion == null || info.Metadata.Version >= minVersion ? info.Instance : null;
        }

        return null;
    }
}
