using BepInEx.Logging;
using HarmonyLib;
using TAS.EverestInterop;
using TAS.ModInterop;
using Random = UnityEngine.Random;

namespace TAS;

[HarmonyPatch]
public static class DeterministicRandomPatch {
    private const int Seed = 0;

    private static bool wasLoading;

    [EnableRun]
    public static void SeedRandomness() {
        wasLoading = false;
        Random.InitState(Seed);
    }

    /// When loading finishes, wipe the load-window RNG churn so playback frame 0 always starts from a deterministic
    /// state. Re-apply the last-loaded savestate's captured RNG: for a `load` command that's the idle fixture's RNG
    /// (== the seed), for a savestate resume it's the captured gameplay RNG. Every load path records this (the `load`
    /// command via its idle fixture, a savestate via its snapshot), so a missing value means a load path isn't
    /// restoring RNG — surface that rather than masking it with a blanket re-seed (which would also clobber a
    /// resume's restored RNG).
    [BeforeTasFrame]
    private static void ReseedAfterLoading() {
        var loading = GameInterop.IsLoading();
        if (wasLoading && !loading) {
            if (PreciseSavestatesInterop.Instance?.LastLoadedRandomState is { } state) {
                Random.state = state;
            } else {
                "ReseedAfterLoading: a load finished without a recorded RandomState — RNG left un-restored (a load path isn't recording LastLoadedRandomState)".Log(LogLevel.Warning);
            }
        }

        wasLoading = loading;
    }
}
