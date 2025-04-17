namespace TAS.Utils;

internal static class HashHelper {
    public static string ComputeHash(string text) {
        return text.GetHashCode().ToString();
    }
}
