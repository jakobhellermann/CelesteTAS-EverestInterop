global using static TAS.GlobalVariables;
using NineSolsAPI;
using TAS.Module;

namespace TAS;

public static class GlobalVariables {
    public static CelesteTasSettings TasSettings => TasMod.Instance.TasSettings;

    public static void AbortTas(string message, bool log = false, float duration = 2f) {
        ToastManager.Toast($"Aborting TAS: {message}");
        Manager.DisableRunLater();
    }
}