using System;

namespace StudioCommunication;

public static class WineInterop {
    public static bool IsWine() => Environment.GetEnvironmentVariable("WINEPREFIX") != null;

    public static string UnixToWindows(string path) => $"Z:{path.Replace('/', '\\')}";

    public static string WindowsToUnix(string path) => path[2..].Replace('\\', '/');
}
