using System;
using StudioCommunication;
using UnityEngine;

namespace TAS.Input.Commands;

public static class ExitGameCommand {
    [TasCommand("ExitGame")]
    private static void ExitGame(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        Application.Quit();
        Environment.Exit(0);
    }
}
