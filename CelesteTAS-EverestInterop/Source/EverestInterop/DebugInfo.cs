using System;

namespace TAS;

public static class DebugInfo {
    [Flags]
    public enum DebugFilter {
        Base = 0,
        All = Base,
    }

    public static string GetInfoText(DebugFilter filter = DebugFilter.Base) {
        return "TODO";
    }
}
