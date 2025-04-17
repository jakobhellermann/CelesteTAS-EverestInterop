global using static TAS.GlobalVariables;
using BepInEx.Logging;
using TAS.Module;
using TAS.Playback;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => TasMod.Instance.TasSettings;

    public static void AbortTas(string message, bool log = false, float duration = PopupToast.DefaultDuration) {
#if DEBUG
        // Always log in debug builds
        log = true;
#endif

        if (log) {
            PopupToast.ShowAndLog(message, duration, LogLevel.Error);
        } else {
            PopupToast.Show(message, duration);
        }

        Manager.DisableRunLater();
    }
}
