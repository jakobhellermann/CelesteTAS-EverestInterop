using System;
using System.Linq;
using HarmonyLib;

namespace TAS.Tracer;

/// Harmony patch class (including its nested classes) that only feeds the frame history
[AttributeUsage(AttributeTargets.Class)]
internal class TracerPatchAttribute : Attribute;

/// Applies GameTrace and the [TracerPatch] classes once frame history is first enabled instead of at startup
internal static class TracerPatches {
    private static Harmony? harmony;

    internal static bool IsTracerPatch(Type type) {
        for (var t = type; t != null; t = t.DeclaringType) {
            if (t == typeof(GameTrace) || t.IsDefined(typeof(TracerPatchAttribute), false)) return true;
        }

        return false;
    }

    internal static void Initialize() {
        TasMod.Instance.ConfigTasTraceFrameHistory.SettingChanged += OnSettingChanged;
        Sync();
    }

    [Unload]
    private static void Unload() {
        TasMod.Instance.ConfigTasTraceFrameHistory.SettingChanged -= OnSettingChanged;
        harmony?.UnpatchSelf();
        harmony = null;
    }

    private static void OnSettingChanged(object? sender, EventArgs e) => Sync();

    private static void Sync() {
        if (harmony != null || !TasMod.Instance.ConfigTasTraceFrameHistory.Value) return;

        harmony = new Harmony("TasTools.Tracer");
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(TasMod).Assembly).Where(IsTracerPatch)) {
            harmony.CreateClassProcessor(type).Patch();
        }
    }
}
