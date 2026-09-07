using HarmonyLib;

namespace TAS;

/// While a TAS run is active, skip GCManager.ForceCollect. The game fires a full *blocking* GC (~1s on the large
/// heap) at every scene load to avoid mid-gameplay hitches; under TAS playback that's pure wall-clock cost (runs are
/// short and bounded) and a source of load-time variance. Unity's incremental GC still runs — only the forced
/// blocking collect is skipped.
[HarmonyPatch]
internal static class GCSkipPatch {
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GCManager), nameof(GCManager.ForceCollect))]
    private static bool ShouldCollectGC() => !Manager.Running;
}
