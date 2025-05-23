namespace TAS;

public static class Dialog {
    public static string Clean(string name) => name switch {
        "TAS_AutoPauseToast" => "",
        _ => $"Unknown dialogue key: {name}",
    };
}
